"""Bounded cross-border extract planning; never invent or splice OSM geometry."""
import itertools,json
from shapely.geometry import box,shape
from shapely.ops import unary_union

def select_parts(index,bounds,maximum=3):
    south,west,north,east=bounds;envelope=box(west,south,east,north);offered=[]
    for feature in index.get('features',[]):
        props=feature.get('properties',{});url=props.get('urls',{}).get('pbf')
        if not url:continue
        polygon=shape(feature['geometry'])
        if polygon.is_empty or not polygon.is_valid or (polygon.covers(envelope) and not props.get("city")) or not polygon.intersects(envelope):continue
        clip=polygon.intersection(envelope)
        if clip.area>0:offered.append((polygon.area,props,clip))
    offered.sort(key=lambda row:(row[0],row[1]['id']))
    # At most sixteen smallest relevant regions, three source files and a small
    # fixed set-cover search. Larger parent continents cannot win this path.
    offered=offered[:16];best=None
    for count in range(1,maximum+1):
        for rows in itertools.combinations(offered,count):
            score=sum(row[0] for row in rows)
            if best is not None and score>=best[0]:continue
            if unary_union([row[2] for row in rows]).covers(envelope):best=(score,[row[1] for row in rows])
    if best is None:raise ValueError('No bounded composite region covers complete requested area')
    return best[1]

def merge_elements(parts):
    merged={}
    for data in parts:
        for row in data.get('elements',[]):
            identity=(row.get('type'),row.get('id'))
            if identity[0] not in ('node','way','relation') or not isinstance(identity[1],int):raise ValueError('Composite OSM identity invalid')
            previous=merged.get(identity)
            # Different extracts can be from different dates. Reject conflicting
            # copies instead of guessing a road position or mixing node revisions.
            if previous is not None and previous!=row:raise ValueError('Conflicting cross-border OSM object '+str(identity))
            merged[identity]=row
            if len(merged)>200000:raise ValueError('Composite map exceeds feature budget')
    return dict(version=.6,generator='EarthUnscripted indexed cross-border fallback',elements=[merged[key] for key in sorted(merged)])
