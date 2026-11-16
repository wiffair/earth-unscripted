"""Interpret OSM lane/shoulder dimensions without inventing live lane states.

Lane count includes variable-access running lanes. An explicit shoulder=no is
not overridden by motorway defaults. Missing presence remains unknown; widths
for a mapped but unmeasured shoulder are explicit 3 m gameplay estimates.
"""
import math
import re

ESTIMATED_SHOULDER_METRES=3.


def one_way(tags):
    """Fixed direction for ordinary cars, with explicit tags taking priority.

    Motorways and roundabouts imply one-way operation. Untagged motorway
    links use a stated game inference; OSM recommends explicit link tags.
    See Research/oneway-model.md for the primary tag documentation.
    """
    value=str(tags.get('oneway','')).strip().lower()
    if value in ('yes','1','true','-1'):return True,'mapped oneway='+value
    if value in ('no','0','false'):return False,'mapped two-way'
    if value:return False,'mapped '+value+'; no fixed direction assumed'
    if tags.get('junction')=='roundabout':return True,'implied roundabout'
    if tags.get('highway')=='motorway':return True,'implied motorway'
    if tags.get('highway')=='motorway_link':return True,'unmapped motorway-link direction inferred'
    return False,'no one-way restriction mapped'


def metres(value):
    if value is None:return None
    text=str(value).strip().lower()
    match=re.fullmatch(r'(\d+(?:\.\d+)?)\s*(?:m|metres?|meters?)?',text)
    if match:result=float(match[1])
    else:
        match=re.fullmatch(r'''(\d+(?:\.\d+)?)\s*(?:ft|')\s*(\d+(?:\.\d+)?)?\s*(?:in|")?''',text)
        if not match:return None
        result=float(match[1])*.3048+float(match[2] or 0)*.0254
    return result if math.isfinite(result) and 0<result<10000 else None


def clearance(tags,category,default):
    physical=metres(tags.get('maxheight:physical'))
    if physical:return physical,'mapped physical height'
    signed=metres(tags.get('maxheight'))
    if signed and category=='road':return signed+.10,'mapped vehicle limit + 0.10 m game allowance'
    return default,'estimated '+category+' clearance'


def layout(tags,base_width,traffic_side='left',reversed_way=False):
    raw=tags.get('shoulder','unknown').lower();sides={'left':None,'right':None}
    if raw=='no':sides={'left':False,'right':False}
    elif raw=='both':sides={'left':True,'right':True}
    elif raw in ('left','right'):sides={side:side==raw for side in sides}
    elif raw=='yes':
        oneway,_=one_way(tags)
        default_side=('right' if traffic_side=='left' else 'left') if reversed_way else traffic_side
        sides={side:side==default_side if oneway else True for side in sides}
    for side in sides:
        specific=tags.get('shoulder:'+side,tags.get('shoulder:both'))
        if specific in ('yes','no'):sides[side]=specific=='yes'
    widths={};provenance=[]
    for side,present in sides.items():
        measured=metres(tags.get('shoulder:'+side+':width',tags.get('shoulder:width')))
        widths[side]=(measured or ESTIMATED_SHOULDER_METRES) if present else 0.
        if present:provenance.append(side+(':mapped width' if measured else ':estimated 3 m width'))
    if reversed_way:widths={'left':widths['right'],'right':widths['left']};sides={'left':sides['right'],'right':sides['left']}
    try:lanes=max(1,min(12,int(tags.get('lanes','0')))) if int(tags.get('lanes','0'))>0 else 0
    except ValueError:lanes=0
    travel=base_width
    explicit=metres(tags.get('width'))
    if explicit and explicit-sum(widths.values())>=max(2.6,lanes*2.4):travel=explicit-sum(widths.values())
    access='|'.join(v.strip() for v in tags.get('motor_vehicle:lanes',tags.get('access:lanes','')).split('|'))
    conditional=tags.get('motor_vehicle:lanes:conditional',tags.get('access:lanes:conditional',''))
    return dict(travelWidth=travel,width=travel+sum(widths.values()),shoulderLeft=widths['left'],shoulderRight=widths['right'],roadCentreOffset=(widths['right']-widths['left'])*.5,laneCount=lanes or max(1,round(travel/3.3)),shoulderSource=';'.join(provenance) if provenance else 'mapped absence' if all(p is False for p in sides.values()) else 'unmapped',laneAccess=access,laneAccessConditional=conditional,hasVariableLane='variable' in access.split('|'),laneControl=tags.get('active_traffic_management')=='yes' or 'variable' in access.split('|'),trafficSide=traffic_side)



def surface_line(feature):
    """Match Unity's bounded right-facing miter for the full paved centre."""
    import numpy as np
    from shapely.geometry import LineString
    offset=feature.get('roadCentreOffset',0)
    if not offset:return feature['line']
    points=feature['xy'];delta=np.diff(points,axis=0);length=np.linalg.norm(delta,axis=1)
    sides=np.c_[delta[:,1],-delta[:,0]]/np.maximum(length,1.e-8)[:,None]
    joins=np.empty_like(points);joins[0]=sides[0];joins[-1]=sides[-1]
    for index in range(1,len(points)-1):
        previous,next_side=sides[index-1],sides[index];combined=previous+next_side
        if np.dot(combined,combined)<.01:joins[index]=next_side
        else:
            normal=combined/np.linalg.norm(combined);joins[index]=normal/max(.7,float(np.dot(normal,next_side)))
    return LineString(points+joins*offset)
