"""Infer hidden tunnel floors from visible portal approaches, in local metres.

Surface LiDAR above a tunnel measures the hill, not the carriageway. Connected
non-tunnel source ways provide a short visible approach fit at each portal.
The interior interpolates those endpoint heights; clearance is a game envelope.
"""
import numpy as np
import source_topology


def plan(features):
    endpoints={}
    for feature in features:
        if feature['tunnel']:continue
        for end in (0,-1):
            endpoints.setdefault((feature['category'],source_topology.sample_identity(feature,end)),[]).append((feature,end))
    for feature in features:
        if not feature['tunnel']:continue
        portals=[]
        surface_portals=[]
        for end in (0,-1):
            observations=[]
            key=(feature['category'],source_topology.original_identity(feature,end))
            for neighbour,entry in endpoints.get(key,[]):
                distance=neighbour['stations'] if entry==0 else neighbour['stations'][-1]-neighbour['stations']
                visible=(distance>=6)&(distance<=32)
                if visible.sum()<3:continue
                x=distance[visible];y=neighbour['raw'][visible]
                fit=np.polyfit(x,y,1);residual=abs(y-np.polyval(fit,x))
                keep=residual<=max(.3,float(np.median(residual))*2.5)
                if keep.sum()>=3:fit=np.polyfit(x[keep],y[keep],1)
                observations.append(float(fit[1]))
            surface_portals.append(bool(endpoints.get(key)))
            portals.append(float(np.median(observations)) if observations else feature.get('portalHeights',[float(feature['raw'][0]),float(feature['raw'][-1])])[0 if end==0 else 1])
        feature['surfacePortals']=surface_portals
        offset=.45 if feature['category']=='rail' else .06 if feature['category']=='path' else -.35 if feature['category']=='water' else .03
        # Interpolate at the position along the complete source tunnel. Core
        # clipping must not invent a portal on the hillside at the core edge.
        from shapely.geometry import LineString,Point
        complete=LineString(feature['originalXY'])
        fraction=feature['stations']/max(.001,feature['stations'][-1]) if feature['category']=='water' else np.array([complete.project(Point(point)) for point in feature['xy']])/max(.001,complete.length)
        feature['heights']=portals[0]*(1-fraction)+portals[1]*fraction+offset
        # layer indicates ordering, never metres. Explicit metric depth is usable;
        # ele needs datum conversion and is retained as evidence until verified.
        import road_layout
        depth=road_layout.metres(feature['tags'].get('depth'))
        feature['depthSource']='inferred portal profile'
        if depth and depth>0:
            from scipy.ndimage import gaussian_filter1d
            buried=gaussian_filter1d(feature['raw']-depth, max(1,25/max(.1,np.mean(np.diff(feature['stations'])))))
            feature['heights']=np.minimum(feature['heights'],buried)
            feature['depthSource']='mapped depth in metres; smoothed terrain-relative profile'
        feature['mappedElevation']=feature['tags'].get('ele','')


# The target roof allowance is generous; final acceptance retains at least a
# 0.10 m cover margin after profile anchoring/rounding between numeric cores.
ROOF_COVER_METRES=.35
MINIMUM_COVER_METRES=.10


def constraints(features):
    """Find covered widths from topology, never from a guessed floor height.

    A tunnel can initially inherit the surface road's LiDAR elevation. Using
    positive measured separation to select crossings would miss that exact
    failure. Explicit layers and shared source joins determine ordering instead.
    """
    from shapely.geometry import Point
    result=[]
    rail_junctions=source_topology.UndergroundRailJunctions(features)
    # A surface footway beside a portal is often drawn across the entrance
    # without a shared node. Its padded ribbon must not become a second roof.
    # Rail bores retain terrain cover; actual footbridges have bridge constraints.
    roads=[feature for feature in features if feature['category'] in ('road','rail')]
    footprints={feature['id']:feature.get('surfaceLine',feature['line']).buffer(feature['width']*.5+.8,cap_style='flat') for feature in roads}
    # Adjacent equal-level tunnel bores have open portal mouths. The surface
    # continuation of one bore cannot act as a roof over the neighbouring bore
    # solely through widened, overlapping endpoint ribbons.
    portal_nodes={}
    for tunnel in roads:
        if not tunnel['tunnel']:continue
        for end in (0,-1):
            key=source_topology.original_identity(tunnel,end)
            direction=tunnel['originalXY'][1 if end==0 else -2]-tunnel['originalXY'][end]
            length=np.linalg.norm(direction)
            if length>.001:portal_nodes.setdefault(key,[]).append((tunnel,tunnel['originalXY'][end],direction/length))
    mouths={}
    for road in roads:
        if road['tunnel']:continue
        entries=[]
        for index in range(len(road['originalXY'])):entries.extend(portal_nodes.get(source_topology.original_identity(road,index),[]))
        mouths[road['id']]=entries
    for lower in features:
        if not lower['tunnel'] or lower['category']=='water':continue
        footprint=lower.get('surfaceLine',lower['line']).buffer(lower['width']*.5+.8,cap_style='flat')
        for upper in roads:
            if upper is lower:continue
            # Separate underground roads can cross too. Their explicit layer
            # ordering is as binding as the ordering of stacked bridge decks.
            if upper['tunnel'] and upper['layer']<=lower['layer']:continue
            if lower['tags'].get('tunnel')=='building_passage' and upper['layer']<=lower['layer']:
                # A courtyard passage lies under a building, not necessarily
                # under the adjacent same-floor parking aisle. Width overlap
                # alone does not establish an overhead road in this case.
                intersection=lower['line'].intersection(upper['line'])
                shared=source_topology.shared_coordinates(lower,upper)
                for coordinate in shared:
                    intersection=intersection.difference(Point(coordinate).buffer(max(lower['width'],upper['width'])*.5+2.3))
                if intersection.is_empty:continue
            # Indoor/interchange level tags distinguish a pedestrian floor
            # above lower roads but below the upper carriageway.
            lower_level=lower['tags'].get('level');upper_level=upper['tags'].get('level')
            if lower_level is not None and upper_level is not None:
                try:
                    if float(upper_level)<float(lower_level):continue
                except ValueError:
                    if upper['layer']<lower['layer']:continue
            elif 'layer' in lower['tags'] and 'layer' in upper['tags'] and upper['layer']<=lower['layer']:
                # Explicit equal layers can describe a driveway entering an
                # open-sided gallery. It is a same-floor connection, not an
                # overhead road demanding another complete tunnel envelope.
                continue
            elif upper['layer']<lower['layer']:continue
            overlap=footprint.intersection(footprints[upper['id']])
            if overlap.is_empty:continue
            shared=source_topology.shared_coordinates(lower,upper)
            join_radius=max(lower['width'],upper['width'])*.5+2.3
            for coordinate in shared:overlap=overlap.difference(Point(coordinate).buffer(join_radius))
            if overlap.is_empty:continue
            if not upper['tunnel'] and not upper['bridge']:
                for neighbour,portal,direction in mouths.get(upper['id'],[]):
                    if neighbour is lower or neighbour['layer']!=lower['layer']:continue
                    if neighbour['tags'].get('level','0')!=lower['tags'].get('level','0'):continue
                    end=min((0,-1),key=lambda index:np.linalg.norm(lower['originalXY'][index]-portal))
                    if np.linalg.norm(lower['originalXY'][end]-portal)>max(upper['width'],lower['width'],neighbour['width'])+2.3:continue
                    tangent=lower['originalXY'][1 if end==0 else -2]-lower['originalXY'][end]
                    length=np.linalg.norm(tangent)
                    if length>.001 and abs(np.dot(tangent/length,direction))>np.cos(np.radians(35)):
                        overlap=overlap.difference(Point(portal).buffer(join_radius))
            if overlap.is_empty:continue
            # A same-level entrance beside a road is not an underpass merely
            # because its inferred width/padding touches that road's edge.
            # Retain all explicitly ordered decks/levels. For otherwise unknown
            # ordering require the tunnel centreline to lie under real pavement,
            # outside genuine shared-node portal mouths.
            # A layer orders actual crossings; it does not turn the gap
            # between nearby padded road ribbons into an overhead road.
            explicit_order=upper['bridge']
            if lower_level is not None and upper_level is not None:
                try:explicit_order=explicit_order or float(upper_level)>float(lower_level)
                except ValueError:pass
            if not explicit_order:
                covered=lower['line'].intersection(upper.get('surfaceLine',upper['line']).buffer(upper['width']*.5,cap_style='flat'))
                for coordinate in shared:covered=covered.difference(Point(coordinate).buffer(join_radius))
                if covered.is_empty:continue
            polygons=[overlap] if overlap.geom_type=='Polygon' else [p for p in getattr(overlap,'geoms',[]) if p.geom_type=='Polygon']
            polygons=[polygon for polygon in polygons if not rail_junctions.same_floor_patch(upper,lower,polygon)]
            if not polygons:continue
            from shapely.ops import unary_union
            overlap=unary_union(polygons)
            samples=[]
            for polygon in polygons:samples.extend(Point(p) for p in polygon.exterior.coords)
            west,south,east,north=overlap.bounds
            for x in np.arange(west,east+1,1):
                for z in np.arange(south,north+1,1):
                    point=Point(x,z)
                    if overlap.covers(point):samples.append(point)
            if samples:
                result.append(dict(lower=lower,upper=upper,lowerStations=np.array([lower['line'].project(p) for p in samples]),upperStations=np.array([upper['line'].project(p) for p in samples])))
    return result


def minimum_gap(constraint):
    lower,upper=constraint['lower'],constraint['upper']
    return float(np.min(np.interp(constraint['upperStations'],upper['stations'],upper['heights'])-np.interp(constraint['lowerStations'],lower['stations'],lower['heights'])))


def fit_cover(features,crossings):
    """Lower hidden floors and connected approaches beneath crossing roads."""
    import gradient_planning
    deficits={}
    for crossing in crossings:
        lower=crossing['lower'];deficit=lower['tunnelClearance']+ROOF_COVER_METRES-minimum_gap(crossing)
        deficits[lower['id']]=max(deficits.get(lower['id'],0),deficit)
    changed=False
    for feature in features:
        deficit=deficits.get(feature['id'],0)
        if deficit>.005:
            gradient_planning.lift_with_approaches(feature,-deficit-.03,features);changed=True
    return changed


def cover_report(features,crossings=None):
    if crossings is None:crossings=constraints(features)
    result=[]
    for crossing in crossings:
        lower,upper=crossing['lower'],crossing['upper'];gap=minimum_gap(crossing)
        result.append(dict(lower=lower['id'],upper=upper['id'],minimumAcrossWidths=gap,clearance=lower['tunnelClearance'],roofCover=gap-lower['tunnelClearance'],passes=gap>=lower['tunnelClearance']+MINIMUM_COVER_METRES))
    return result
