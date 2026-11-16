"""Preserve OSM node identity alongside resampled local-metre profiles.

Two roads at different levels may have equal horizontal coordinates. Only a
shared OSM node establishes a source junction. Interpolated samples belong to
their own source way; they cannot create junctions with a different way.
"""


def point_identities(source_way,original_xy,original_nodes,sampled_xy):
    """Return one stable string per sample, without merging coincident nodes."""
    original={}
    if len(original_xy)==len(original_nodes):
        for point,node in zip(original_xy,original_nodes):
            key=tuple(round(float(value),3) for value in point)
            original.setdefault(key,set()).add(str(node))
    result=[]
    for point in sampled_xy:
        key=tuple(round(float(value),3) for value in point)
        candidates=original.get(key,set())
        if len(candidates)==1:
            result.append('osm:'+next(iter(candidates)))
        else:
            result.append(f'sample:{source_way}:{key[0]:.3f}:{key[1]:.3f}')
    return result


def sample_identity(feature,index):
    """Identify a resampled vertex; retain compatibility with older fixtures."""
    identities=feature.get('sourceNodeIds')
    if identities is not None:return identities[index]
    return tuple(round(float(value),3) for value in feature['xy'][index])


def original_identity(feature,index):
    """Identify a complete source-way vertex, including an out-of-core portal."""
    identities=feature.get('originalNodeIds')
    if identities is not None:return 'osm:'+str(identities[index])
    return tuple(round(float(value),3) for value in feature['originalXY'][index])


def shared_coordinates(first,second):
    """Return only genuinely shared original source-node coordinates."""
    coordinates={original_identity(first,index):point for index,point in enumerate(first['originalXY'])}
    return [coordinates[identity] for index in range(len(second['originalXY'])) if (identity:=original_identity(second,index)) in coordinates]


class RoadNode(tuple):
    """A two-coordinate graph key whose equality also requires source identity.

    Keeping two numeric coordinates preserves distance/vector operations in
    routing and forecasting. Hash/equality/order include the separate token so
    a coincident upper deck cannot overwrite a lower road's graph vertex.
    """
    def __new__(cls,x,z,identity):
        value=super().__new__(cls,(x,z))
        value.identity=identity
        return value

    def __hash__(self):
        return hash((tuple(self),self.identity))

    def __eq__(self,other):
        return isinstance(other,RoadNode) and tuple.__eq__(self,other) and self.identity==other.identity

    def __ne__(self,other):
        return not self.__eq__(other)

    def __lt__(self,other):
        return (tuple(self),self.identity)<(tuple(other),getattr(other,'identity','legacy'))

class UndergroundRailJunctions:
    """Recognise local, explicitly same-floor track connections despite layer changes.

    Layer is relative drawing order, not a physical floor/depth. Shared OSM
    rail nodes/crossovers on one explicit level can therefore cross a layer
    boundary without becoming an overpass. Only short actual network paths
    qualify; a distant loop connection cannot suppress a real tunnel crossing.
    """
    MAXIMUM_PATH_METRES = 120.
    MAXIMUM_PATCH_METRES = 30.

    @staticmethod
    def floor_key(feature):
        import math
        if feature.get('category')!='rail' or not feature.get('tunnel'):return None
        try:level=float(feature.get('tags',{}).get('level',''))
        except (ValueError,TypeError):return None
        if not math.isfinite(level):return None
        tags=feature.get('tags',{})
        return feature.get('kind'),tags.get('gauge',''),level

    def __init__(self,features):
        import numpy as np
        self.graphs={};self.ways={}
        for feature in features:
            key=self.floor_key(feature);nodes=feature.get('originalNodeIds');xy=feature.get('originalXY')
            if key is None or nodes is None or len(nodes)!=len(xy):continue
            stations=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))]
            nodes=[str(node) for node in nodes];self.ways[feature['id']]=(key,nodes,xy,stations)
            graph=self.graphs.setdefault(key,{})
            for i in range(1,len(nodes)):
                length=float(stations[i]-stations[i-1])
                if length<=0:continue
                graph.setdefault(nodes[i-1],{})[nodes[i]]=min(length,graph.get(nodes[i-1],{}).get(nodes[i],float('inf')))
                graph.setdefault(nodes[i],{})[nodes[i-1]]=min(length,graph.get(nodes[i],{}).get(nodes[i-1],float('inf')))

    def connected_at(self,first,second,point):
        import heapq
        from shapely.geometry import LineString
        a=self.ways.get(first['id']);b=self.ways.get(second['id'])
        if a is None or b is None or a[0]!=b[0]:return False
        limit=self.MAXIMUM_PATH_METRES;graph=self.graphs[a[0]]
        at=LineString(a[2]).project(point);bt=LineString(b[2]).project(point)
        targets={node:abs(float(station)-bt) for node,station in zip(b[1],b[3]) if abs(station-bt)<=limit}
        if not targets:return False
        best={};queue=[]
        for node,station in zip(a[1],a[3]):
            distance=abs(float(station)-at)
            if distance<=limit and distance<best.get(node,float('inf')):best[node]=distance;heapq.heappush(queue,(distance,node))
        while queue:
            distance,node=heapq.heappop(queue)
            if distance!=best[node]:continue
            if distance+targets.get(node,float('inf'))<=limit:return True
            for next_node,length in graph.get(node,{}).items():
                candidate=distance+length
                if candidate<=limit and candidate<best.get(next_node,float('inf')):best[next_node]=candidate;heapq.heappush(queue,(candidate,next_node))
        return False

    def same_floor_patch(self,first,second,polygon):
        import math
        from shapely.geometry import Point
        if self.floor_key(first) is None or self.floor_key(first)!=self.floor_key(second):return False
        # On one explicit floor, neighbouring trackbeds can overlap through
        # width/safety padding. That is not a roof: retain vertical clearance
        # only for an actual centreline crossing/coincident track component.
        crossing=first['line'].intersection(second['line'])
        if crossing.is_empty or not polygon.intersects(crossing):return True
        west,south,east,north=polygon.bounds
        if math.hypot(east-west,north-south)>self.MAXIMUM_PATCH_METRES:return False
        # A whole small overlap must be within a real local rail connection.
        # Large/remote overlap components retain the full clearance envelope.
        return all(self.connected_at(first,second,Point(p)) for p in polygon.exterior.coords)
