"""Mapped railway furniture; never invent stops at clipping/streaming boundaries.

Platform height=* is height above rail, per OpenRailwayMap/Tagging. Country
fallbacks below are typical visual GAME ESTIMATES, not surveyed/mandatory
heights or railway compliance. A mapped valid height takes priority.
https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging
"""
from collections import Counter
import math
import numpy as np
from shapely.geometry import Point,Polygon,LineString,box
from shapely.ops import triangulate,unary_union
import osm_areas,road_layout,transport_metadata

TRACKS={'rail','light_rail','subway','tram','narrow_gauge','disused','abandoned','construction','preserved'}

def derive(source,features,transform,origin,bounds,country,layer):
    rails=[f for f in features if f['category']=='rail'];roads=[f for f in features if f['category']=='road']
    if not rails:return [],[],set()
    envelope=box(*bounds);degree=Counter();ends={};explicit={};buffers=[];seen=set()
    def xy(p):return np.asarray(transform.transform(p['lon'],p['lat']))-origin
    def height(f,p):return float(np.interp(f['line'].project(p),f['stations'],f['heights']))
    def nearby(p,items=rails):return sorted((f for f in items if f['line'].distance(p)<32),key=lambda f:(f['line'].distance(p),f['id']))
    for row in source.get('elements',[]):
        tags=row.get('tags',{})
        if row.get('type')=='node' and tags.get('railway')=='buffer_stop':explicit[str(row['id'])]=(xy(row),tags)
        if row.get('type')!='way' or tags.get('railway') not in TRACKS:continue
        nodes=row.get('nodes',[]);geometry=row.get('geometry',[])
        for a,b in zip(nodes,nodes[1:]):degree[str(a)]+=1;degree[str(b)]+=1
        if len(nodes)==len(geometry) and len(nodes)>1 and nodes[0]!=nodes[-1]:
            for index in (0,-1):
                if geometry[index] is not None:ends[str(nodes[index])]=(xy(geometry[index]),tags)
    for identity,(position,tags) in sorted({**ends,**explicit}.items()):
        p=Point(position);mapped=identity in explicit
        if not envelope.contains(p) or not mapped and (degree[identity]!=1 or envelope.boundary.distance(p)<8):continue
        candidates=[f for f in nearby(p) if 'osm:'+identity in f.get('sourceNodeIds',[]) and f['line'].distance(p)<.15]
        if not candidates:continue # No nearest-track guess across different levels.
        f=min(candidates,key=lambda f:f['id']);station=f['line'].project(p)
        if not mapped and min(station,f['line'].length-station)>.05:continue
        if not mapped:
            status=f['tags']
            if status.get('railway') not in ('rail','light_rail','subway','narrow_gauge') or status.get('railway:status') in ('disused','abandoned','dismantled','demolished','proposed','construction'):continue
            if any(str(status.get(key,'no')).lower() not in ('no','false','0','') for key in ('disused','abandoned','dismantled','demolished','proposed','construction')):continue
        y=height(f,p)
        if not mapped and any(r['line'].distance(p)<r['width']*.5+2 and abs(height(r,p)-y)<3 for r in roads):continue
        if (round(position[0],3),round(position[1],3)) in seen:continue
        seen.add((round(position[0],3),round(position[1],3)))
        inward=f['line'].interpolate(min(f['line'].length,station+1) if station<f['line'].length*.5 else max(0,station-1));dx,dz=inward.x-p.x,inward.y-p.y;length=math.hypot(dx,dz)
        if length<.01:continue
        buffers.append(dict(id='buffer:'+identity,sourceWay=f['sourceId'],x=float(p.x),y=y,z=float(p.y),dx=dx/length,dz=dz/length,mapped=mapped))
    stations=[]
    for row in source.get('elements',[]):
        if row.get('type')=='node' and row.get('tags',{}).get('railway') in ('station','halt') and row.get('tags',{}).get('name'):stations.append((Point(xy(row)),row['tags']['name']))
    platforms=[];consumed_roofs=set();rows=osm_areas.expanded(source.get('elements',[]));roof_rows=[]
    def polygon(row):
        points=row.get('geometry',[])
        if len(points)<4 or any(p is None for p in points):return None
        coordinates=[xy(p) for p in points]
        if np.linalg.norm(coordinates[0]-coordinates[-1])>.01:return None
        shape=Polygon(coordinates,[[xy(p) for p in ring] for ring in row.get('_holes',[])])
        if not shape.is_valid:shape=shape.buffer(0)
        return shape.intersection(envelope)
    for row in rows:
        if row.get('tags',{}).get('building')=='roof':
            shape=polygon(row)
            if shape is not None and not shape.is_empty:roof_rows.append((row,shape))
    platform_height={'GB':.915,'IE':.915,'DE':.76,'AU':.76,'CH':.55,'NL':.76}.get(country.upper().split('-')[0],.55)
    vehicle_half=transport_metadata.VEHICLES.get(country.upper().split('-')[0],(3.4,4.8,''))[0]*.5+.05
    for row in rows:
        tags=row.get('tags',{})
        if tags.get('railway')!='platform' and tags.get('public_transport')!='platform':continue
        if tags.get('bus')=='yes' and tags.get('train')!='yes' and tags.get('railway')!='platform':continue
        shape=polygon(row)
        if shape is None:
            points=row.get('geometry',[])
            if len(points)<2 or any(p is None for p in points):continue
            width=road_layout.metres(tags.get('width')) or 3.
            shape=LineString([xy(p) for p in points]).buffer(max(.5,min(width,20))*.5,cap_style='flat').intersection(envelope)
        if shape.is_empty:continue
        near=nearby(shape.representative_point())
        if 'layer' in tags:near=[f for f in near if f['layer']==layer(tags)]
        if not near:continue
        reference=near[0];rise=road_layout.metres(tags.get('height')) or platform_height;rise=max(.1,min(rise,2))
        # Keep mapped pavement out of the actual train body and road footprint.
        for f in near:
            if abs(height(f,shape.representative_point())-height(reference,shape.representative_point()))<2.5:shape=shape.difference(f['line'].buffer(vehicle_half,cap_style='flat'))
        for road in roads:
            if shape.is_empty:break
            if road['line'].distance(shape)<road['width']*.5+.25 and abs(height(road,shape.representative_point())-(height(reference,shape.representative_point())+rise))<3:shape=shape.difference(road['line'].buffer(road['width']*.5+.25,cap_style='flat'))
        if shape.is_empty:continue
        pieces=[shape] if shape.geom_type=='Polygon' else [p for p in getattr(shape,'geoms',[]) if p.geom_type=='Polygon']
        for index,part in enumerate(pieces):
            if part.area<1:continue
            def serialize(points,offset=0):return [dict(x=float(x),y=height(reference,Point(x,z))+rise+offset,z=float(z)) for x,z in points]
            def triangles(poly,offset=0):
                output=[]
                for triangle in triangulate(poly):
                    if poly.covers(triangle):output.extend(serialize(list(triangle.exterior.coords)[:3],offset))
                return output
            roof_parts=[];mapped_roof_height=0
            if tags.get('covered')=='yes' or tags.get('shelter')=='yes':roof_parts.append(part)
            for roof,footprint in roof_rows:
                overlap=part.intersection(footprint)
                if overlap.area>.5:
                    roof_parts.extend([overlap] if overlap.geom_type=='Polygon' else [p for p in getattr(overlap,'geoms',[]) if p.geom_type=='Polygon']);consumed_roofs.add(str(roof['id']))
                    mapped_roof_height=max(mapped_roof_height,road_layout.metres(roof.get('tags',{}).get('min_height')) or road_layout.metres(roof.get('tags',{}).get('height')) or 0)
            roof_height=max(3.,mapped_roof_height-rise,max((f.get('railClearanceHeight',6)+height(f,part.representative_point()) for f in near),default=6)-height(reference,part.representative_point())-rise+.25)
            roof_triangles=[]
            merged=unary_union(roof_parts)
            for road in roads:
                if merged.is_empty:break
                probe=merged.representative_point();base=height(reference,probe)+rise;road_y=height(road,probe)
                if base+1<road_y<base+roof_height+.75 and road['line'].distance(merged)<road['width']*.5+.5:merged=merged.difference(road['line'].buffer(road['width']*.5+.5,cap_style='flat'))
            for roof_part in ([merged] if merged.geom_type=='Polygon' else getattr(merged,'geoms',[])):roof_triangles.extend(triangles(roof_part,roof_height))
            posts=[];post_support=merged.buffer(.01)
            for ring in [part.exterior,*part.interiors]:
                line=LineString(ring.coords)
                for station in np.arange(2,line.length,12):
                    p=line.interpolate(station)
                    if not merged.is_empty and post_support.covers(p) and not any(f['line'].distance(p)<f.get('railClearanceWidth',4.2)*.5+.2 for f in near):posts.extend(serialize([(p.x,p.y)]))
            nearby_names=sorted((p.distance(part),name) for p,name in stations if p.distance(part)<350)
            name=tags.get('name') or (nearby_names[0][1] if nearby_names else 'Platform')
            if tags.get('ref'):name+=' · '+tags['ref']
            platforms.append(dict(id=str(row['id'])+':'+str(index),name=name,triangles=triangles(part),outline=serialize(part.exterior.coords),roofTriangles=roof_triangles,posts=posts if roof_triangles else [],heightSource='mapped OSM height' if road_layout.metres(tags.get('height')) else 'typical game estimate; not surveyed or regulatory',roofHeight=roof_height,depth=rise+.55,minX=part.bounds[0],minZ=part.bounds[1],maxX=part.bounds[2],maxZ=part.bounds[3]))
    return buffers,platforms,consumed_roofs
