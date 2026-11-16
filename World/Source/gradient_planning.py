"""Long-context major-road height planning and bounded bridge approaches.

Profiles follow actual connected carriageways across source-way boundaries.
A 512 m continuation on each end supplies context; only the owned core is
published. Robust filtering rejects short DTM holes and DSM object spikes.
Bridge clearance lifts are tapered along those same continuations rather than
creating a short steep ramp at the first OSM-way endpoint.
"""
import math
import numpy as np
import source_topology
from scipy.ndimage import gaussian_filter1d,median_filter

MAJOR={'motorway','motorway_link','trunk','trunk_link','primary','primary_link'}
MAXIMUM_ROUNDABOUT_GRADE=.02  # gameplay fit; source height is retained at the group median
SPAN=512.
STEP=2.
LINK_MERGE_TAPER_METRES=160.  # blend approach corrections along the joining road
ROAD_VERGE_METRES=1.4  # matches the renderer's gravel support outside asphalt
JUNCTION_FIT_RADIUS_METRES=80.  # limit a fit to the actual shared source junction
# Height authority, not traffic right of way. Preserve the major-road profile
# and fit its connected minor approach; links are always approach candidates.
ROAD_PROFILE_PRIORITY={'motorway':0,'trunk':1,'primary':2,'secondary':3,
                       'tertiary':4,'unclassified':5,'residential':5,
                       'service':6,'track':7}


def endpoint_index(features,category='road'):
    result={}
    for feature in features:
        if feature['category']!=category:continue
        for end in (0,-1):result.setdefault(source_topology.sample_identity(feature,end),[]).append((feature,end))
    return result


def continuation(feature,end,index,span=SPAN):
    """Yield connected features, entry endpoint and preceding arc metres."""
    visited={feature['id']};current=feature;endpoint=end;distance=0.
    while distance<span:
        location=current['xy'][endpoint]
        inward=current['xy'][1 if endpoint==0 else -2]-location
        inward/=max(1e-8,np.linalg.norm(inward));matches=[]
        for neighbour,entry in index.get(source_topology.sample_identity(current,endpoint),[]):
            if neighbour['id'] in visited or neighbour['roundabout']:continue
            if feature['ref'] and neighbour['ref'] and feature['ref']!=neighbour['ref']:continue
            direction=neighbour['xy'][1 if entry==0 else -2]-location
            direction/=max(1e-8,np.linalg.norm(direction))
            alignment=float(np.dot(-inward,direction))
            if alignment>math.cos(math.radians(40)):
                matches.append((alignment+(0.2 if neighbour['ref']==feature['ref'] else 0),neighbour,entry))
        if not matches:return
        _,current,entry=max(matches,key=lambda row:row[0]);visited.add(current['id'])
        yield current,entry,distance
        distance+=current['stations'][-1];endpoint=-1 if entry==0 else 0


def smooth(features):
    index=endpoint_index(features);snapshot={f['id']:f['heights'].copy() for f in features}
    for feature in features:
        # Tunnel floors have already been inferred from portals. Filter that
        # inferred profile with its connected approaches too: excluding it
        # preserved short, steep portal corrections after shared-node fitting.
        if feature['category']!='road' or feature['kind'] not in MAJOR or feature['roundabout']:continue
        stations=feature['stations'].copy();heights=snapshot[feature['id']].copy()
        for end in (0,-1):
            for neighbour,entry,previous in continuation(feature,end,index):
                distance=np.arange(STEP,min(neighbour['stations'][-1],SPAN-previous)+.001,STEP)
                if not len(distance):continue
                source=distance if entry==0 else neighbour['stations'][-1]-distance
                extended=-(previous+distance) if end==0 else feature['stations'][-1]+previous+distance
                stations=np.r_[stations,extended];heights=np.r_[heights,np.interp(source,neighbour['stations'],snapshot[neighbour['id']])]
        order=np.argsort(stations);stations=stations[order];heights=heights[order]
        regular=np.arange(stations[0],stations[-1]+STEP*.5,STEP)
        sampled=np.interp(regular,stations,heights)
        filtered=gaussian_filter1d(median_filter(sampled,size=31,mode='nearest'),sigma=22/STEP,mode='nearest')
        feature['heights']=np.interp(feature['stations'],regular,filtered)


def recover_stacked_decks(features):
    """Replace LiDAR samples obscured by a higher mapped bridge layer.

    DSM sees the top deck in a stack. Lower bridge profiles therefore use
    visible samples on their own connected carriageway, including up to 512 m
    of approaches, before ordinary grade filtering. The 10 m mask allowance
    includes cross-road sample offsets and the initial short spike filter.
    This is an inferred game profile, not a surveyed lower deck measurement.
    """
    from shapely import points as geometry_points,distance as geometry_distance
    bridges=[feature for feature in features if feature['bridge'] and feature['category']!='water']
    snapshot={feature['id']:feature['heights'].copy() for feature in features}
    indexes={category:endpoint_index(features,category) for category in ('road','rail','path')}
    recovered=0
    for feature in bridges:
        higher=[upper for upper in bridges if upper['layer']>feature['layer'] and upper['line'].distance(feature['line'])<upper['width']*.5+10]
        if not higher:continue
        stations=feature['stations'].copy();xy=feature['xy'].copy();heights=snapshot[feature['id']].copy()
        for end in (0,-1):
            for neighbour,entry,previous in continuation(feature,end,indexes[feature['category']]):
                if neighbour['bridge'] and neighbour['layer']!=feature['layer']:break
                distance=np.arange(STEP,min(neighbour['stations'][-1],SPAN-previous)+.001,STEP)
                if not len(distance):continue
                source=distance if entry==0 else neighbour['stations'][-1]-distance
                extended=-(previous+distance) if end==0 else feature['stations'][-1]+previous+distance
                stations=np.r_[stations,extended]
                xy=np.vstack([xy,np.c_[np.interp(source,neighbour['stations'],neighbour['xy'][:,0]),np.interp(source,neighbour['stations'],neighbour['xy'][:,1])]])
                heights=np.r_[heights,np.interp(source,neighbour['stations'],snapshot[neighbour['id']])]
        hidden=np.zeros(len(stations),dtype=bool)
        locations=geometry_points(xy)
        for upper in bridges:
            if upper['layer']>feature['layer']:
                hidden|=geometry_distance(locations,upper['line'])<upper['width']*.5+10
        visible=~hidden
        if hidden.any() and visible.sum()>=2:
            order=np.argsort(stations[visible])
            feature['heights']=np.interp(feature['stations'],stations[visible][order],heights[visible][order])
            recovered+=1
    return recovered


def conform_road_junctions(features):
    """Fit minor-road surfaces across a real, same-level junction overlap.

    Shared nodes align centre lines but leave intersecting wide ribbons at
    different heights. This applies equally to ordinary roads, joined bridge
    decks and tunnel ramps. A matching source node, layer and level is required;
    a remote crossing of the same two ways cannot inherit the junction fit.
    Corrections blend along the minor approach over 160 m.
    """
    from shapely import points as geometry_points, distance as geometry_distance, line_locate_point
    from scipy.interpolate import PchipInterpolator

    def priority(road):
        base=100 if road['kind'].endswith('_link') else ROAD_PROFILE_PRIORITY.get(road['kind'],8)
        # Two diverging bridge links can share one deck but have the same road
        # class. Give their overlap one stable authority instead of leaving
        # both ribbons at conflicting heights (or fitting them to each other).
        return (base,-road['width'],str(road['sourceId'])) if road['bridge'] else (base,0.,'')

    roads=[feature for feature in features if feature['category']=='road']
    by_node={}
    for road in roads:
        for node in road.get('originalNodeIds',[]):
            by_node.setdefault(str(node),{})[road['id']]=road
    reports=[]
    for approach in sorted(roads,key=priority):
        connected={}
        for node in approach.get('originalNodeIds',[]):
            connected.update(by_node.get(str(node),{}))
        targets=approach['heights'].copy()
        best=np.full(len(targets),np.inf)
        owners=set()
        locations=geometry_points(approach['xy'])
        junction_anchors=[]
        carried_nodes=set()
        for index in range(len(approach['xy'])):
            token=source_topology.sample_identity(approach,index)
            if isinstance(token,str) and token.startswith('osm:') and len(by_node.get(token[4:],{}))>1:
                junction_anchors.append((token[4:],float(approach['stations'][index])))
        for road in connected.values():
            if priority(road)>=priority(approach):
                continue
            if road['layer']!=approach['layer'] or road['tags'].get('level','0')!=approach['tags'].get('level','0'):
                continue
            junctions=source_topology.shared_coordinates(approach,road)
            actual_overlaps=[]
            for point in junctions:
                ends=[]
                for feature in (approach,road):
                    index=int(np.argmin(np.linalg.norm(feature['xy']-point,axis=1)))
                    if index not in (0,len(feature['xy'])-1):break
                    inward=feature['xy'][1 if index==0 else -2]-feature['xy'][index]
                    ends.append(inward/max(1.e-8,np.linalg.norm(inward)))
                # Consecutive nearly straight source ways meet end-to-end;
                # their asphalt is not a diverging merge. A clamped projection
                # onto the preceding way falsely flattened a short bridge and
                # left a steep ramp at the edge of its 8 m overlap allowance.
                if len(ends)==2 and float(np.dot(*ends)) < -math.cos(math.radians(35)):
                    continue
                actual_overlaps.append(point)
            junctions=actual_overlaps
            if not junctions:
                continue
            near_join=np.minimum.reduce([np.linalg.norm(approach['xy']-point,axis=1) for point in junctions])<=JUNCTION_FIT_RADIUS_METRES
            distance=geometry_distance(locations,road['surfaceLine'])
            covered=near_join & (distance<road['width']*.5+approach['width']*.5+ROAD_VERGE_METRES) & (distance<best)
            if not covered.any():
                continue
            road_station=line_locate_point(road['line'],locations[covered])
            correction=np.interp(road_station,road['stations'],road['heights'])-approach['heights'][covered]
            road_nodes={str(node) for node in road.get('originalNodeIds',[])}
            protection=np.ones(len(correction))
            for node,station in junction_anchors:
                if node in road_nodes:continue
                neighbours=[other for other in by_node.get(node,{}).values() if other is not approach]
                short_branch=(approach['bridge'] and road['bridge']) or (
                    approach['kind'].endswith('_link') and not approach['tunnel'] and not road['tunnel'])
                if (short_branch and approach['stations'][-1]<JUNCTION_FIT_RADIUS_METRES
                    and neighbours and all(not other['bridge'] and not other['tunnel']
                        and priority(other)>=priority(approach)
                        and other['tags'].get('level','0')==approach['tags'].get('level','0') for other in neighbours)):
                    # A short split branch must follow the through road/deck
                    # across its width. Carry that fit into its actual minor
                    # ground approach instead of pinning an artificial step at
                    # the bridge tag's end. Other bridge/major-road anchors stay
                    # protected, including the Spaghetti multi-level case.
                    carried_nodes.add(node)
                    continue
                # This approach also meets another actual road/deck. A nearby
                # pavement overlap must not overwrite that separate connection,
                # especially a short ground ramp leading to an elevated deck.
                distance_to_join=abs(approach['stations'][covered]-station)
                protection*=np.sin(np.minimum(distance_to_join/35.,1.)*np.pi*.5)**2
            targets[covered]=approach['heights'][covered]+correction*protection
            best[covered]=distance[covered]
            owners.add(road['sourceId'])
        indices=np.flatnonzero(np.isfinite(best))
        if not len(indices):
            continue
        observed=approach['stations'][indices]
        knots={float(approach['stations'][index]):float(targets[index]-approach['heights'][index]) for index in indices}
        for node,station in junction_anchors:
            if node not in carried_nodes:knots[station]=0.
        for station in observed:
            for support in (max(0.,station-LINK_MERGE_TAPER_METRES),min(approach['stations'][-1],station+LINK_MERGE_TAPER_METRES)):
                if np.min(abs(observed-support))>LINK_MERGE_TAPER_METRES-.01:
                    knots.setdefault(float(support),0.)
        knots.setdefault(0.,0.)
        knots.setdefault(float(approach['stations'][-1]),0.)
        stations=sorted(knots)
        if len(stations)<2:
            continue
        correction=PchipInterpolator(stations,[knots[station] for station in stations])(approach['stations'])
        approach['heights']+=correction
        for node,station in junction_anchors:
            if node not in carried_nodes:continue
            delta=float(np.interp(station,approach['stations'],correction))
            for neighbour in by_node[node].values():
                if neighbour is approach:continue
                anchors={0.:0.,float(neighbour['stations'][-1]):0.}
                join_station=None
                for index in range(len(neighbour['xy'])):
                    identity=source_topology.sample_identity(neighbour,index)
                    if identity=='osm:'+node:
                        join_station=float(neighbour['stations'][index])
                    elif isinstance(identity,str) and identity.startswith('osm:') and len(by_node.get(identity[4:],{}))>1:
                        anchors[float(neighbour['stations'][index])]=0.
                if join_station is None:continue
                anchors[max(0.,join_station-LINK_MERGE_TAPER_METRES)]=0.
                anchors[min(float(neighbour['stations'][-1]),join_station+LINK_MERGE_TAPER_METRES)]=0.
                anchors[join_station]=delta
                ordered=sorted(anchors)
                if len(ordered)>1:
                    neighbour['heights']+=PchipInterpolator(ordered,[anchors[s] for s in ordered])(neighbour['stations'])
        reports.append(dict(road=approach['sourceId'],roads=sorted(owners),maximumCorrectionMetres=float(np.max(abs(correction)))))
    return reports


def conform_at_grade_paths(features):
    """Fit ordinary footpath crossings to the final carriageway, in metres.

    Long road filtering can place asphalt below the adjacent raw terrain. A
    ground-level path must descend to that crossing instead of becoming an
    untagged low footbridge. Explicit bridges, tunnels and different layers or
    levels remain separate. Corrections taper over 35 m along the path.
    """
    from shapely import STRtree,points as geometry_points,distance as geometry_distance,line_locate_point
    from scipy.interpolate import PchipInterpolator
    roads=[f for f in features if f['category']=='road' and not f['bridge'] and not f['tunnel']]
    if not roads:return []
    footprints=[f['surfaceLine'].buffer(f['width']*.5+.3) for f in roads]
    tree=STRtree(footprints);reports=[];transition=35.
    for path in features:
        if path['category']!='path' or path['bridge'] or path['tunnel']:continue
        locations=geometry_points(path['xy'])
        targets=path['heights'].copy();best=np.full(len(targets),np.inf);owners=set()
        for road_index in tree.query(path['line'].buffer(path['width']*.5+.3)):
            road=roads[road_index]
            if road['layer']!=path['layer']:continue
            if road['tags'].get('level','0')!=path['tags'].get('level','0'):continue
            distance=geometry_distance(locations,road['surfaceLine'])
            covered=(distance<road['width']*.5+path['width']*.5+.2)&(distance<best)
            if not covered.any():continue
            road_station=line_locate_point(road['line'],locations[covered])
            targets[covered]=np.interp(road_station,road['stations'],road['heights'])+.03
            best[covered]=distance[covered];owners.add(road['sourceId'])
        indices=np.flatnonzero(np.isfinite(best))
        if not len(indices):continue
        observed=path['stations'][indices]
        knots={float(path['stations'][i]):float(targets[i]-path['heights'][i]) for i in indices}
        for station in observed:
            for support in (max(0.,station-transition),min(path['stations'][-1],station+transition)):
                if np.min(abs(observed-support))>transition-.01:knots.setdefault(float(support),0.)
        knots.setdefault(0.,0.);knots.setdefault(float(path['stations'][-1]),0.)
        stations=sorted(knots)
        if len(stations)<2:continue
        correction=PchipInterpolator(stations,[knots[station] for station in stations])(path['stations'])
        path['heights']+=correction
        reports.append(dict(path=path['sourceId'],roads=sorted(owners),maximumCorrectionMetres=float(np.max(abs(correction)))))
    return reports


def fit_path_road_junctions(features):
    """Join walking routes to road height at an actual shared OSM node.

    A footbridge endpoint is a connection, not an overhead crossing. Keeping
    categories separate in the ordinary profile stitch left short footbridges
    nearly a metre above their track approaches. Only real shared source-node
    tokens establish these anchors; coincident geometry cannot create one.
    Corrections taper along the path over 35 m, preserving remote approaches.
    """
    from scipy.interpolate import PchipInterpolator
    road_nodes={}
    for road in features:
        if road['category']!='road':continue
        for index in range(len(road['xy'])):
            token=source_topology.sample_identity(road,index)
            if isinstance(token,str) and token.startswith('osm:'):
                road_nodes.setdefault(token,[]).append((road,index))
    reports=[]
    transition=35.
    for path in features:
        if path['category']!='path':continue
        anchors={};owners=set()
        for index in range(len(path['xy'])):
            token=source_topology.sample_identity(path,index)
            matches=[(road,station) for road,station in road_nodes.get(token,[])
                     if road['tags'].get('level','0')==path['tags'].get('level','0')]
            if not matches:continue
            anchors[index]=float(np.median([road['heights'][station] for road,station in matches]))
            owners.update(road['sourceId'] for road,station in matches)
        if not anchors:continue
        observed=np.array([path['stations'][index] for index in anchors])
        knots={float(path['stations'][index]):target-path['heights'][index] for index,target in anchors.items()}
        for station in observed:
            for support in (max(0.,station-transition),min(path['stations'][-1],station+transition)):
                if np.min(abs(observed-support))>transition-.01:knots.setdefault(float(support),0.)
        knots.setdefault(0.,0.);knots.setdefault(float(path['stations'][-1]),0.)
        ordered=sorted(knots)
        if len(ordered)<2:continue
        correction=PchipInterpolator(ordered,[knots[station] for station in ordered])(path['stations'])
        path['heights']+=correction
        reports.append(dict(path=path['sourceId'],roads=sorted(owners),maximumCorrectionMetres=float(np.max(abs(correction)))))
    return reports


def roundabout_planes(features):
    """Fit a gentle robust plane to each source-connected roundabout ring."""
    rings=[f for f in features if f['category']=='road' and f['roundabout']]
    parents={f['id']:f['id'] for f in rings}
    def root(identity):
        while parents[identity]!=identity:identity=parents[identity]
        return identity
    nodes={}
    for feature in rings:
        for index,point in enumerate(feature['originalXY']):
            key=source_topology.original_identity(feature,index)
            if key in nodes:parents[root(feature['id'])]=root(nodes[key])
            else:nodes[key]=feature['id']
    groups={}
    for feature in rings:groups.setdefault(root(feature['id']),[]).append(feature)
    for group in groups.values():
        xy=np.concatenate([f['xy'] for f in group]);height=np.concatenate([f['heights'] for f in group])
        origin=xy.mean(axis=0);design=np.c_[xy-origin,np.ones(len(xy))]
        weights=np.ones(len(height));solution=None
        for iteration in range(4):
            solution=np.linalg.lstsq(design*weights[:,None],height*weights,rcond=None)[0]
            residual=height-design@solution;scale=max(.3,float(np.median(abs(residual)))*1.4826)
            weights=np.minimum(1,2*scale/np.maximum(.001,abs(residual)))
        slope=np.linalg.norm(solution[:2])
        if slope>MAXIMUM_ROUNDABOUT_GRADE:solution[:2]*=MAXIMUM_ROUNDABOUT_GRADE/slope
        solution[2]=np.median(height-(xy-origin)@solution[:2])
        for feature in group:feature['heights']=(feature['xy']-origin)@solution[:2]+solution[2]


def lift_with_approaches(upper,delta,features):
    """Lift a connected deck together so join stitching cannot undo clearance.

    OSM may split one bridge into several ways. Shared original source nodes
    connect pieces of the same category; nearby separate crossings do not join.
    Non-bridge approaches are blended by stitch after the complete deck moves.
    """
    deck={upper['id']:upper}
    if upper['bridge'] or upper['roundabout']:
        nodes={}
        for feature in features:
            same_structure=feature['roundabout'] if upper['roundabout'] else feature['bridge']
            if not same_structure or feature['category']!=upper['category'] or feature['layer']!=upper['layer']:continue
            for index,point in enumerate(feature['originalXY']):
                nodes.setdefault(source_topology.original_identity(feature,index),[]).append(feature)
        queue=[upper]
        while queue:
            current=queue.pop()
            for index,point in enumerate(current['originalXY']):
                for neighbour in nodes.get(source_topology.original_identity(current,index),[]):
                    if neighbour['id'] not in deck:
                        deck[neighbour['id']]=neighbour;queue.append(neighbour)
    corrections={identity:np.full(len(feature['heights']),delta) for identity,feature in deck.items()}
    if upper['category'] in ('road','rail','path'):
        index=endpoint_index(features,upper['category'])
        for feature in deck.values():
            for end in (0,-1):
                for neighbour,entry,previous in continuation(feature,end,index):
                    distance=previous+(neighbour['stations'] if entry==0 else neighbour['stations'][-1]-neighbour['stations'])
                    weight=np.where(distance<SPAN,.5+.5*np.cos(np.pi*np.minimum(distance/SPAN,1)),0)
                    # Negative tunnel-floor adjustments must lower their
                    # approaches too. A positive-only maximum would discard
                    # every negative taper and leave a sharp portal step.
                    envelope=np.maximum if delta>=0 else np.minimum
                    corrections[neighbour['id']]=envelope(corrections.get(neighbour['id'],np.zeros(len(weight))),delta*weight)
    if upper['category']=='path' and upper['bridge']:
        # A short footbridge can connect directly to service/track approaches,
        # with no intervening path way. Keep the real shared endpoint together
        # when clearance lifts the span; otherwise the road/path stitch drags
        # the entire short bridge back through the railway on every iteration.
        # Major roads retain their measured profile authority.
        road_nodes={}
        for road in features:
            if road['category']!='road' or road['kind'] not in ('service','track','unclassified','residential'):continue
            for index in range(len(road['xy'])):
                token=source_topology.sample_identity(road,index)
                if isinstance(token,str) and token.startswith('osm:'):
                    road_nodes.setdefault(token,[]).append((road,index))
        for feature in deck.values():
            for end in (0,len(feature['xy'])-1):
                token=source_topology.sample_identity(feature,end)
                for road,index in road_nodes.get(token,[]):
                    if road['tags'].get('level','0')!=feature['tags'].get('level','0'):continue
                    distance=abs(road['stations']-road['stations'][index])
                    weight=np.where(distance<SPAN,.5+.5*np.cos(np.pi*np.minimum(distance/SPAN,1)),0)
                    envelope=np.maximum if delta>=0 else np.minimum
                    corrections[road['id']]=envelope(corrections.get(road['id'],np.zeros(len(weight))),delta*weight)
    for feature in features:
        if feature['id'] in corrections:feature['heights']+=corrections[feature['id']]


def finish(features,crossings,minimum,stitch,filter_profiles=True):
    """Remove short solver ripples, then re-enforce all full-width clearances."""
    if filter_profiles:smooth(features);roundabout_planes(features);stitch()
    by_id={f['id']:f for f in features}
    for iteration in range(64):
        changed=False
        for crossing in crossings:
            upper,lower=by_id[crossing['upper']],by_id[crossing['lower']]
            gap=minimum(upper,lower)
            if gap is not None and gap<crossing['required']+.02:
                lift_with_approaches(upper,crossing['required']+.08-gap,features);changed=True
        if not changed:break
        stitch()
