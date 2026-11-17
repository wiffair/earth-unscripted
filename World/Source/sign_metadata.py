"""Small source-bound sign context sidecars; never regenerate or alter geometry."""
from pathlib import Path
import hashlib,json,math
from functools import lru_cache
from shapely.geometry import shape,Point,LineString
from shapely.prepared import prep
from pyproj import Transformer
SCHEMA=3
@lru_cache(maxsize=1)
def countries():
    data=json.loads((Path(__file__).parent/'Sources/uk-countries-2024.geojson').read_text())
    codes={'Wales':'GB-WLS','England':'GB-ENG','Scotland':'GB-SCT','Northern Ireland':'GB-NIR'}
    return [(codes[f['properties']['CTRY24NM']],shape(f['geometry']),prep(shape(f['geometry']))) for f in data['features']]
def region_at(lon,lat):
    p=Point(lon,lat)
    return next((code for code,geom,prepared in countries() if prepared.covers(p)),'')
@lru_cache(maxsize=32)
def geographic(crs):return Transformer.from_crs(crs,4326,always_xy=True)
def contexts(feature,frame):
    points=feature['points'];to_geo=geographic(frame.world_projection)
    origin=frame.world_origin;coords=[to_geo.transform(origin[0]+p['x'],origin[1]+p['z']) for p in points]
    whole=LineString(coords);total=sum(math.hypot(b["x"]-a["x"],b["z"]-a["z"]) for a,b in zip(points,points[1:]))
    for code,geom,prepared in countries():
        if prepared.covers(whole):return [dict(start=0.,end=total,region=code)]
    spans=[];station=0
    for i in range(1,len(points)):
        a,b=points[i-1],points[i];length=math.hypot(b['x']-a['x'],b['z']-a['z'])
        if length<.001:continue
        line=LineString([coords[i-1],coords[i]]);cuts={0.,1.}
        for code,geom,prepared in countries():
            if not prepared.intersects(line):continue
            hit=line.intersection(geom.boundary)
            for part in getattr(hit,'geoms',[hit]):
                if part.is_empty:continue
                if part.geom_type=='Point':cuts.add(max(0.,min(1.,line.project(part,normalized=True))))
                elif part.geom_type=='LineString':
                    for q in (Point(part.coords[0]),Point(part.coords[-1])):cuts.add(line.project(q,normalized=True))
        values=sorted(cuts)
        for lo,hi in zip(values,values[1:]):
            mid=line.interpolate((lo+hi)*.5,normalized=True);code=region_at(mid.x,mid.y)
            start=station+lo*length;end=station+hi*length
            if spans and spans[-1]['region']==code and abs(spans[-1]['end']-start)<.01:spans[-1]['end']=end
            else:spans.append(dict(start=start,end=end,region=code))
        station+=length
    return spans

def prepare(pipeline,tile,model,manifest):
    import area_pipeline
    from transport_metadata import national_speed
    name=f'{tile[0]}_{tile[1]}'
    raw=pipeline.raw/(pipeline.frame.identifier+'-'+name+f'-osm-areas-v2-h{area_pipeline.HALO_METRES}.json')
    if not raw.exists():raw=pipeline.raw/(pipeline.frame.identifier+'-'+name+f'-osm-h{area_pipeline.HALO_METRES}.json')
    raw_bytes=raw.read_bytes() if raw.exists() else b'{}'
    key=hashlib.sha256((str(SCHEMA)+manifest['sha256']).encode()+raw_bytes).hexdigest()[:20]
    output=pipeline.prepared/(name+'-signs-'+key+'.json')
    if not output.exists():
        source=json.loads(raw_bytes);ways={str(e['id']):e.get('tags',{}) for e in source.get('elements',[]) if e.get('type')=='way'}
        rows=[]
        for f in model['features']:
            if f['category']!='road':continue
            tags=ways.get(str(f.get('sourceId','')),{});lit=tags.get('lit','unknown')
            if lit not in ('yes','no'):lit='unknown'
            zone=tags.get('zone:maxspeed','') or tags.get('maxspeed:type','') or tags.get('source:maxspeed','')
            zone=zone if 'zone' in zone.lower() or 'zone:maxspeed' in tags else ''
            rows.append(dict(id=f['id'],lighting=lit,zone=zone,limitForward=tags.get('maxspeed:backward' if tags.get('oneway')=='-1' else 'maxspeed:forward',tags.get('maxspeed','')),limitBackward=tags.get('maxspeed:forward' if tags.get('oneway')=='-1' else 'maxspeed:backward',tags.get('maxspeed','')),national=national_speed(tags,1) or national_speed(tags,-1),nationalForward=national_speed(tags,1),nationalBackward=national_speed(tags,-1),dual=tags.get('dual_carriageway')=='yes' or any('nsl_dual' in tags.get(k,'').lower() for k in ('maxspeed','maxspeed:type','source:maxspeed')),contexts=contexts(f,pipeline.frame)))
        result=dict(schema=SCHEMA,geometrySha256=manifest['sha256'],sourceOSMSha256=hashlib.sha256(raw_bytes).hexdigest(),boundarySource='ONS Countries December2024 UK BGC / OGL',features=rows)
        tmp=output.with_suffix('.partial');tmp.write_text(json.dumps(result,separators=(',',':')));tmp.replace(output)
    data=output.read_bytes()
    return dict(signUrl='/prepared/'+output.name,signSha256=hashlib.sha256(data).hexdigest(),signBytes=len(data))
