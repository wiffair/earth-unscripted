"""Local navigation and route-independent reachable-road forecasting.

Only already prepared model graphs are read. Forecasting uses the actual car
position and legal source direction, never the test driver's destination/path.
The separate guide query follows A472 then A470 toward Coryton for acceptance.
Shared primary/trunk junction links remain traversable even without an A472 ref.
"""
import heapq
import math
import numpy as np
from shapely.geometry import LineString,Point
from source_topology import RoadNode

AREA_METRES=1536
ROAD_FORECAST_METRES=1800
GEOMETRY_FORECAST_METRES=768


def tile_at(point):
    return (math.floor(point[0]/AREA_METRES),math.floor(point[1]/AREA_METRES))


def road_graph(models,major_only=False,route_policy=None,forecast_only=False):
    nodes={};graph={};preferred=set();a470=set();motorway=set();limits={};limit_sources={};edge_features={};edge_priority={}
    for model in models:
        for feature in model['features']:
            if feature['category']!='road':continue
            if major_only and feature['kind'] not in ['primary','primary_link','trunk','trunk_link','motorway','motorway_link']:continue
            reference=set(feature['ref'].split(';'))
            if route_policy and route_policy.get('allowedRefs'):
                allowed=bool(reference & set(route_policy['allowedRefs']))
                allowed|=bool(route_policy.get('allowRoundabouts') and feature['roundabout'])
                radius=route_policy.get('goalAccessRadiusMetres',0)
                if radius:
                    goal=(route_policy['x'],route_policy['z'])
                    allowed|=any(math.dist((p['x'],p['z']),goal)<radius for p in feature['points'])
                if not allowed:continue
            main=bool(reference & ({'A472','A470','M4','A4042'}|set((route_policy or {}).get('allowedRefs',[]))))
            keys=[]
            identities=feature.get('sourceNodeIds')
            if identities is not None and len(identities)!=len(feature['points']):raise ValueError('Road node/profile identity mismatch')
            for point_index,point in enumerate(feature['points']):
                coordinate=(round(point['x'],3),round(point['z'],3))
                key=RoadNode(*coordinate,identities[point_index]) if identities is not None else coordinate
                nodes[key]=point;keys.append(key)
                # Forecasting needs connectivity and heights only. Route-cost,
                # speed and ownership metadata belong to guide construction.
                if not forecast_only:
                    limit=feature.get('speedLimitMps',13.4112)
                    if limit<limits.get(key,float('inf')):
                        limits[key]=limit;limit_sources[key]=feature.get('speedLimitSource','unavailable')
                    if main:preferred.add(key)
                    if 'A470' in reference:a470.add(key)
                    if 'M4' in reference:motorway.add(key)
            for a,b in zip(keys,keys[1:]):
                distance=math.dist(a,b)
                if distance<.0001:continue
                cost=distance*(1 if main or feature['roundabout'] or not major_only else 4)
                graph.setdefault(a,[]).append((b,cost,distance))
                if not forecast_only:
                    priority=0 if tile_at(((a[0]+b[0])*.5,(a[1]+b[1])*.5))==(model['tileX'],model['tileZ']) else 1
                    if priority<edge_priority.get((a,b),2):edge_features[(a,b)]=feature;edge_priority[(a,b)]=priority
                if not feature['oneway']:
                    graph.setdefault(b,[]).append((a,cost,distance))
                    if not forecast_only and priority<edge_priority.get((b,a),2):edge_features[(b,a)]=feature;edge_priority[(b,a)]=priority
    return nodes,graph,preferred,a470,limits,limit_sources,motorway,edge_features


def search(nodes,graph,start,heading=None,limit=float('inf'),binding_distance=8.):
    position=(start['x'],start['z'])
    # Adjacent cores can clip/resample the same source way at different
    # stations. Its exact old endpoint may have no forward edge even though a
    # neighbouring core has a continuing sample within two metres. Bind to a
    # nearby *traversable* vertex, retaining height and legal-direction checks.
    entries=[]
    for node in nodes:
        if math.dist(node,position)>binding_distance:continue
        edges=graph.get(node,[])
        if heading is not None:
            edges=[edge for edge in edges if np.dot((np.asarray(edge[0])-node)/edge[2],heading)>=-.2]
        if edges:entries.append(node)
    if not entries:raise ValueError('Current position does not bind a forward road')
    # Remote X/Z starts have no measured Y yet (the client uses zero). Do not
    # select a lower deck merely because that placeholder is closer to it.
    # A real tyre/body-height hint remains authoritative when it matches a
    # candidate surface within eight metres.
    hint=start.get('y')
    trusted=hint is not None and min(abs(nodes[node]['y']-hint) for node in entries)<8
    entry=min(entries,key=lambda node:math.dist(node,position)+(abs(nodes[node]['y']-hint)*.5 if trusted else 0))
    best={entry:0.};physical={entry:0.};previous={};queue=[(0.,entry)]
    while queue:
        cost,node=heapq.heappop(queue)
        if cost!=best[node] or physical[node]>limit:continue
        for target,weight,length in graph.get(node,[]):
            if node==entry and heading is not None:
                direction=(np.asarray(target)-node)/length
                if np.dot(direction,heading)<-.2:continue
            candidate=cost+weight
            if candidate<best.get(target,float('inf')):
                best[target]=candidate;physical[target]=physical[node]+length
                previous[target]=node;heapq.heappush(queue,(candidate,target))
    return entry,best,physical,previous


def guide(model,start,heading,destination,context_models=None):
    """Extend only the automated acceptance driver's local guide, plus next hint.

    The hint is an actual source-road point beyond this core, chosen from the
    context halo. It avoids guessing a future tile by projecting a straight ray.
    """
    human_initial=bool(destination.get('initialHumanGuide'))
    nodes,graph,preferred,a470,limits,limit_sources,motorway,edge_features=road_graph(context_models or [model],major_only=not human_initial,route_policy=destination if destination.get('localGoal') else None)
    if human_initial:preferred=set(nodes)
    heading=np.asarray(heading,dtype=float);heading/=max(1e-8,np.linalg.norm(heading))
    snapped_initial=False
    try:entry,best,physical,previous=search(nodes,graph,start,heading)
    except ValueError:
        if not human_initial:raise
        # A human can save on a railway, beach, verge or car park. Acquire the
        # surrounding world using a nearby legal-road guide; never invent a
        # traversable road from the saved off-road position to that guide.
        entry,best,physical,previous=search(nodes,graph,start,None,binding_distance=256.)
        snapped_initial=True
    inside=lambda node:model['coreMinX']+8<=node[0]<=model['coreMaxX']-8 and model['coreMinZ']+8<=node[1]<=model['coreMaxZ']-8
    # A prepared core is an ownership boundary, not a routing boundary. At a
    # junction beside that boundary the correct exit may immediately leave it,
    # while the wrong carriageway stays inside. Compare the entire available
    # source graph so clipping cannot force a premature turn or wrong exit.
    candidates=[node for node in best if node in preferred and physical[node]>30]
    if not candidates and human_initial:
        candidates=[node for node in best if physical[node]>2]
    if not candidates:raise ValueError('No forward main-road continuation in available context')
    goal=np.array([destination['x'],destination['z']]);position=np.array([start['x'],start['z']])
    via=destination.get('via',destination)
    via_point=np.array([via['x'],via['z']])
    on_a470=False;on_m4=False
    if destination.get('localGoal'):
        # Keep the acceptance driver on its requested corridor until the exit
        # waypoint itself is locally reachable. Forecasting remains independent.
        # A corridor may legitimately change number (A354 -> A35, or M4 ->
        # A4042). allowedRefs already restricts the graph; forcing its first
        # reference forever strands the guide at the transition junction.
        wanted_ref=destination.get('routeRef') if len(destination.get('allowedRefs',[]))<=1 else None
        if wanted_ref:
            corridor=set()
            for edge,feature in edge_features.items():
                if wanted_ref in feature['ref'].split(';') and feature['kind'] in ('motorway','trunk','primary'):
                    corridor.update(edge)
            on_corridor=[node for node in candidates if node in corridor]
            if on_corridor:candidates=on_corridor
        nearby=[node for node in best if math.dist(node,goal)<max(18,destination['radiusMetres']*.55) and physical[node]>2]
        if nearby:target=min(nearby,key=lambda node:math.dist(node,goal))
        else:target=min(candidates,key=lambda node:math.dist(node,goal)+.015*physical[node])
    else:
        motorway_candidates=[node for node in candidates if node in motorway and np.linalg.norm(np.asarray(node)-goal)<np.linalg.norm(position-goal)]
        a470_candidates=[node for node in candidates if node in a470 and np.linalg.norm(np.asarray(node)-via_point)<np.linalg.norm(position-via_point)]
        if motorway_candidates:
            candidates=motorway_candidates;on_m4=True
            score=lambda node:-float(np.linalg.norm(np.asarray(node)-goal))
        elif a470_candidates:
            candidates=a470_candidates;on_a470=True
            score=lambda node:-float(np.linalg.norm(np.asarray(node)-via_point))
        else:score=lambda node:-node[0]-.001*physical[node]
        target=max(candidates,key=score)
        near_goal=[node for node in candidates if math.dist(node,goal)<destination['radiusMetres']*.6]
        if near_goal:target=min(near_goal,key=lambda node:physical[node])
    chain=[target]
    while chain[-1]!=entry:chain.append(previous[chain[-1]])
    chain.reverse();points=[dict(x=float(entry[0]),z=float(entry[1]),y=nodes[entry]['y']) if snapped_initial else dict(start,y=nodes[entry]['y'])];speed_limits=[];sources=[];source_ways=[]
    # Limits belong to the traversed edge, not a shared node. A 20 mph side
    # road must not impose a one-point speed-limit island on a 70 mph mainline.
    model_lookup={(item['tileX'],item['tileZ']):item for item in (context_models or [model])}
    geometries={}
    def owned_sample(node,feature):
        owner=model_lookup.get(tile_at(node))
        if owner is None:return nodes[node]['y'],feature
        key=(owner['tileX'],owner['tileZ'],feature['sourceId'])
        if key not in geometries:
            rows=[]
            for candidate in owner['features']:
                if candidate['category']!='road' or candidate['sourceId']!=feature['sourceId']:continue
                xy=np.array([[p['x'],p['z']] for p in candidate['points']]);line=LineString(xy)
                station=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))]
                rows.append((line,station,np.array([p['y'] for p in candidate['points']]),candidate))
            geometries[key]=rows
        location=Point(node);rows=geometries[key]
        if not rows:return nodes[node]['y'],feature
        line,station,heights,source=min(rows,key=lambda row:row[0].distance(location))
        if line.distance(location)>.02:return nodes[node]['y'],feature
        return float(np.interp(line.project(location),station,heights)),source
    for index,node in enumerate(chain):
        edge=(node,chain[index+1]) if index+1<len(chain) else (chain[index-1],node)
        feature=edge_features[edge];height,source=owned_sample(node,feature)
        limit=source.get('speedLimitMps',13.4112);provenance=source.get('speedLimitSource','unavailable')
        if index==0:
            speed_limits.append(limit);sources.append(provenance);source_ways.append(source['sourceId'])
        if math.dist((points[-1]['x'],points[-1]['z']),node)>.01:
            points.append(dict(x=nodes[node]['x'],y=height,z=nodes[node]['z']))
            speed_limits.append(limit);sources.append(provenance);source_ways.append(source['sourceId'])
    # Follow the directed major-road graph beyond the selected tail, excluding
    # the just-traversed prefix, until the next core is encountered.
    blocked=set(chain[:-1]);future={target:0.};pending=[(0.,target)];next_node=None
    while pending:
        distance,node=heapq.heappop(pending)
        if distance!=future[node]:continue
        if not inside(node) and tile_at(node)!=tile_at(target) and node in preferred:
            next_node=node;break
        for candidate,cost,length in graph.get(node,[]):
            if candidate in blocked:continue
            candidate_distance=distance+cost
            if candidate_distance<future.get(candidate,float('inf')):
                future[candidate]=candidate_distance;heapq.heappush(pending,(candidate_distance,candidate))
    if destination.get('localGoal') and math.dist(target,goal)<destination['radiusMetres']:next_node=target
    if next_node is None:
        tail_heading=np.asarray(target)-np.asarray(chain[max(0,len(chain)-20)])
        tail_heading/=max(1e-8,np.linalg.norm(tail_heading));next_node=np.asarray(target)+tail_heading*128
    return dict(name='Live route → '+destination['name'],points=points,speedLimits=speed_limits,speedLimitSources=sources,sourceWays=source_ways),dict(nextX=float(next_node[0]),nextZ=float(next_node[1]),onA470=on_a470,onM4=on_m4)


class Forecast:
    """Cache only the graph of client-resident numeric areas, rebuilding on change."""
    def __init__(self):
        self.identity=None;self.graph=None

    def terrain_frontier(self,loaded,request):
        """Continue loading while a human is off the mapped road network.

        Road graph reachability cannot bind a car crossing a field or entering
        an unmapped car park. Request nearby numeric cores radially instead of
        reporting a permanent road-binding failure at the next terrain edge.
        """
        position=np.array([request['start']['x'],request['start']['z']])
        direction=np.array([request['headingX'],request['headingZ']],dtype=float)
        direction/=max(1e-8,np.linalg.norm(direction))
        centre=tile_at(position)
        radius=math.ceil(ROAD_FORECAST_METRES/AREA_METRES)
        candidates=[]
        for dx in range(-radius,radius+1):
            for dz in range(-radius,radius+1):
                tile=(centre[0]+dx,centre[1]+dz)
                if tile in loaded:continue
                point=np.array([np.clip(position[0],tile[0]*AREA_METRES+.5,(tile[0]+1)*AREA_METRES-.5),
                                np.clip(position[1],tile[1]*AREA_METRES+.5,(tile[1]+1)*AREA_METRES-.5)])
                offset=point-position
                distance=float(np.linalg.norm(offset))
                if distance>ROAD_FORECAST_METRES:continue
                alignment=float(np.dot(offset/max(1e-8,distance),direction))
                candidates.append(dict(tileX=tile[0],tileZ=tile[1],x=float(point[0]),z=float(point[1]),
                    distanceMetres=distance,priority=distance+180*(1-alignment)))
        return dict(candidates=sorted(candidates,key=lambda row:row['priority']),corridors=[],
                    reachableRoadNodes=0,offRoad=True)

    def reachable(self,models,request):
        identity=tuple(sorted((model['tileX'],model['tileZ']) for model in models))
        if identity!=self.identity:
            self.graph=road_graph(models,forecast_only=True);self.identity=identity
        nodes,graph,*unused=self.graph
        try:
            if not nodes:raise ValueError('No mapped roads in current terrain')
            entry,best,physical,previous=search(nodes,graph,request['start'],limit=ROAD_FORECAST_METRES)
        except ValueError:
            return self.terrain_frontier(set(identity),request)
        loaded=set(identity);candidates={};corridors={}
        direction=np.array([request['headingX'],request['headingZ']]);direction/=max(1e-8,np.linalg.norm(direction))
        position=np.array([request['start']['x'],request['start']['z']])
        for node,distance in physical.items():
            if distance>ROAD_FORECAST_METRES:continue
            tile=tile_at(node)
            if tile not in loaded:
                offset=np.asarray(node)-position
                alignment=float(np.dot(offset/max(1e-8,np.linalg.norm(offset)),direction))
                priority=distance+180*(1-alignment)
                row=dict(tileX=tile[0],tileZ=tile[1],x=node[0],z=node[1],distanceMetres=distance,priority=priority)
                if tile not in candidates or priority<candidates[tile]['priority']:candidates[tile]=row
            if distance<=GEOMETRY_FORECAST_METRES:
                chunk=(math.floor(node[0]/64),math.floor(node[1]/64))
                if chunk not in corridors or distance<corridors[chunk]['distanceMetres']:
                    corridors[chunk]=dict(point=nodes[node],distanceMetres=distance)
        return dict(candidates=sorted(candidates.values(),key=lambda row:row['priority']),corridors=list(corridors.values()),reachableRoadNodes=sum(distance<=ROAD_FORECAST_METRES for distance in physical.values()))
