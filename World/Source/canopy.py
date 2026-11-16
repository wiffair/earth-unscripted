"""Deterministic visual tree seeds from surface/ground height and mapped woods.

These are canopy estimates, not surveyed tree stems or navigation obstacles.
Mapped roads, railways, paths, waterways and buildings exclude candidates.
The same global metre cells give adjacent cores identical placement decisions.
"""
import math
import numpy as np
from shapely import STRtree,points as geometry_points
from shapely.geometry import Polygon

SPACING_METRES=8


def inside(polygons,points):
    if not polygons:return np.zeros(len(points),dtype=bool)
    hits=STRtree(polygons).query(geometry_points(points),predicate='intersects')
    result=np.zeros(len(points),dtype=bool)
    if len(hits[0]):result[hits[0]]=True
    return result


def derive(elevation,bounds,features,buildings,woodland=(),extra_exclusions=()):
    gx=np.arange(math.floor(bounds[0]/SPACING_METRES),math.ceil(bounds[2]/SPACING_METRES))
    gz=np.arange(math.floor(bounds[1]/SPACING_METRES),math.ceil(bounds[3]/SPACING_METRES))
    cx,cz=np.meshgrid(gx,gz);cx=cx.ravel().astype(np.int64);cz=cz.ravel().astype(np.int64)
    seeds=((cx*73856093)^(cz*19349663))&0x7fffffff
    # Cell-centred jitter cannot move a seed into the next ownership cell.
    jitter_x=((seeds*1664525+1013904223)&0xffff)/65535.-.5
    jitter_z=((seeds*22695477+1)&0xffff)/65535.-.5
    xy=np.c_[(cx+.5+jitter_x*.7)*SPACING_METRES,(cz+.5+jitter_z*.7)*SPACING_METRES]
    available=(xy[:,0]>=bounds[0])&(xy[:,0]<bounds[2])&(xy[:,1]>=bounds[1])&(xy[:,1]<bounds[3])
    exclusions=list(extra_exclusions)
    for feature in features:
        width=feature['width']*.5+(4 if feature['category']=='water' else 3)
        exclusions.append(feature.get('surfaceLine',feature['line']).buffer(width))
    for building in buildings:
        polygon=Polygon([(p['x'],p['z']) for p in building['points']])
        if not polygon.is_valid:polygon=polygon.buffer(0)
        if not polygon.is_empty:exclusions.append(polygon.buffer(3))
    available &= ~inside(exclusions,xy)
    indices=np.flatnonzero(available);locations=xy[indices]
    if not len(locations):return [],dict(candidates=len(xy),accepted=0,estimated=0)
    ground=elevation.sample(locations)
    heights=[]
    for offset in ((0,0),(2.5,0),(-2.5,0),(0,2.5),(0,-2.5)):
        point=locations+np.asarray(offset)
        point[:,0]=np.clip(point[:,0],bounds[0]+.01,bounds[2]-.01)
        point[:,1]=np.clip(point[:,1],bounds[1]+.01,bounds[3]-.01)
        heights.append(elevation.sample(point,'dsm')-elevation.sample(point))
    canopy=np.median(heights,axis=0)
    native=elevation.measured_at(locations,'dsm') if hasattr(elevation,'measured_at') else np.ones(len(locations),dtype=bool)
    measured=(canopy>3.5)&native
    estimated=(~native)&inside(list(woodland),locations)
    accepted=measured|estimated
    result=[]
    for row in np.flatnonzero(accepted):
        index=indices[row];seed=int(seeds[index])
        height=float(np.clip(canopy[row],5,30)) if measured[row] else 10.+(seed%1200)/100.
        result.append(dict(x=round(float(locations[row,0]),3),y=round(float(ground[row]),3),
            z=round(float(locations[row,1]),3),height=round(height,2),seed=seed,
            crownRadius=round(min(7.,max(1.5,height*.22)),2),
            heightSource='canopy surface estimate' if measured[row] else 'mapped woodland estimate'))
    return result,dict(candidates=len(xy),accepted=len(result),estimated=int(estimated[accepted].sum()),
        spacingMetres=SPACING_METRES,scope='Visual canopy estimates; mapped corridors/buildings excluded')
