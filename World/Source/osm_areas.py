"""Complete mapped area geometry, including relation courtyards and islands."""
from shapely.geometry import LineString, Polygon
from shapely.ops import unary_union, polygonize


def expanded(elements):
    """Expose valid multipolygon parts to the common way-based source reader.

    Outer member ways of an accepted relation are not emitted a second time.
    Roads, rails and all unrelated source elements are retained unchanged.
    """
    additions=[];members=set()
    for element in elements:
        tags=element.get('tags',{})
        if element.get('type')!='relation' or not (tags.get('building') or tags.get('railway')=='platform' or tags.get('public_transport')=='platform' or tags.get('natural') in ('water','wood') or tags.get('landuse')=='forest' or tags.get('waterway')=='riverbank'):continue
        if tags.get('type') not in ('multipolygon','boundary',None):continue
        platform=tags.get('railway')=='platform' or tags.get('public_transport')=='platform'
        outer=[];inner=[];outer_ids=[]
        for member in element.get('members',[]):
            if member.get('type')!='way':continue
            geometry=member.get('geometry',[])
            if any(p is None for p in geometry):
                if platform:outer=[];break
                raise ValueError('Incomplete mapped-area relation '+str(element['id']))
            if len(geometry)<2:continue
            line=LineString([(p['lon'],p['lat']) for p in geometry])
            if member.get('role')=='inner':inner.append(line)
            elif member.get('role','') in ('','outer','outline'):outer.append(line);outer_ids.append(member['ref'])
        shapes=list(polygonize(unary_union(outer))) if outer else []
        if not shapes:
            if platform:continue
            raise ValueError('Unclosed mapped-area relation '+str(element['id']))
        shape=unary_union(shapes)
        if inner:
            holes=list(polygonize(unary_union(inner)))
            if not holes:
                if platform:continue
                raise ValueError('Unclosed inner area ring '+str(element['id']))
            shape=shape.difference(unary_union(holes))
        shapes=[shape] if shape.geom_type=='Polygon' else list(getattr(shape,'geoms',[]))
        for index,polygon in enumerate(shapes):
            if polygon.geom_type!='Polygon' or polygon.is_empty:continue
            geometry=[dict(lon=x,lat=y) for x,y in polygon.exterior.coords]
            holes=[[dict(lon=x,lat=y) for x,y in ring.coords] for ring in polygon.interiors]
            additions.append(dict(type='way',id='relation-'+str(element['id'])+'-'+str(index),tags=tags,geometry=geometry,_holes=holes))
        members.update(outer_ids)
    return [e for e in elements if not(e.get('type')=='way' and e.get('id') in members)]+additions


def triangulated_water(records,elevation,features,terrain,gx,gz):
    """Render mapped inland water separately from the tidal coastline.

    Surface levels prefer mapped elevation, otherwise a robust low percentile
    of local shore samples. Bed depth is visual inference, never bathymetry.
    """
    import numpy as np
    from shapely import constrained_delaunay_triangles, contains_xy, distance, points
    output=[];flat_x=gx.ravel();flat_z=gz.ravel()
    for identity,name,tags,polygon in records:
        if tags.get('water')=='sea':continue
        parts=[polygon] if polygon.geom_type=='Polygon' else list(getattr(polygon,'geoms',[]))
        for part_index,part in enumerate(parts):
            if part.geom_type!='Polygon' or part.area<1:continue
            boundary=np.asarray(part.exterior.coords)
            samples=np.asarray(elevation.sample(boundary),dtype=float);samples=samples[np.isfinite(samples)]
            if samples.size==0:continue
            try:level=float(str(tags.get('ele','')).split()[0])-float(elevation.height_origin)
            except (ValueError,IndexError,AttributeError):level=float(np.percentile(samples,10))
            # Existing deck samples constrain a visual plane, without imposing
            # a river/boat clearance or raising it into the carriageway.
            for feature in features:
                if feature['category']!='road':continue
                xy=feature['xy'];inside=contains_xy(part,xy[:,0],xy[:,1])
                if np.any(inside):level=min(level,float(np.min(feature['heights'][inside]))-.3)
            inside=contains_xy(part,flat_x,flat_z)
            if np.any(inside):
                shore=distance(points(flat_x[inside],flat_z[inside]),part.boundary)
                depth=np.clip(.15+shore*.12,.15,8)
                terrain[inside]=np.minimum(terrain[inside],level-depth)
            vertices=[]
            for triangle in constrained_delaunay_triangles(part).geoms:
                for x,z in list(triangle.exterior.coords)[:3]:vertices.append(dict(x=round(x,3),y=round(level,3),z=round(z,3)))
            output.append(dict(id=str(identity)+'-'+str(part_index),sourceId=str(identity),name=name,level=round(level,3),outline=[dict(x=x,y=level,z=z) for x,z in part.exterior.coords],holes=[dict(points=[dict(x=x,y=level,z=z) for x,z in ring.coords]) for ring in part.interiors],vertices=vertices,levelSource='mapped-ele' if tags.get('ele') else 'local-shore-estimate'))
    return output
