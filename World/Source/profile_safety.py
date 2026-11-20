"""Final physically plausible road grades and terrain-constrained waterways.

Coordinates/stations/heights are metres. The road fit minimizes weighted
absolute changes to inferred elevations while enforcing shared source nodes,
class-specific grades, and the existing full-width bridge constraints. Water
has no vehicle clearance requirement and never lifts a road.
"""
import numpy as np
from scipy.ndimage import minimum_filter1d, gaussian_filter1d
from scipy.optimize import linprog
from scipy.sparse import coo_matrix
from shapely.geometry import Point, LineString
import source_topology
import tunnel_profiles


class ProfileFitInfeasible(ValueError):
    """The exact physical constraints have no common solution."""


# These are surface ownership rules, not traffic right of way. A ring owns its
# mouths; other ties are stable across tile order and source-way enumeration.
ROAD_PRIORITY = dict(motorway=0, trunk=1, primary=2, secondary=3, tertiary=4,
                     unclassified=5, residential=6, living_street=7, service=8, track=9)
JUNCTION_HEIGHT_TOLERANCE_METRES = .015


def surface_priority(road):
    rank = -1 if road['roundabout'] else (100 if road['kind'].endswith('_link') else ROAD_PRIORITY.get(road['kind'], 20))
    return rank, -float(road['width']), str(road['sourceId']), str(road['id'])


def junction_connections(features):
    """Find local, same-level source junctions with one stable surface owner.

    Crossing X/Z geometry alone never creates a connection. Nearly straight
    end-to-end source fragments are continuations rather than overlapping
    junction mouths. The result is also serialized for the mesh generator, so
    numeric profiles, asphalt and paint use exactly the same authority.
    """
    roads = sorted((f for f in features if f['category'] == 'road'), key=surface_priority)
    by_node = {}
    for order, road in enumerate(roads):
        road['surfacePriority'] = order
        road['junctions'] = []
        for index in range(len(road['xy'])):
            identity = source_topology.sample_identity(road, index)
            if isinstance(identity, str) and identity.startswith('osm:'):
                by_node.setdefault(identity, []).append((road, index))
    connections = []
    seen = set()
    for entries in by_node.values():
        for approach, index in entries:
            for owner, owner_index in entries:
                key = approach['id'], owner['id'], index
                if key in seen or surface_priority(owner) >= surface_priority(approach):
                    continue
                seen.add(key)
                if owner['layer'] != approach['layer'] or owner['tags'].get('level', '0') != approach['tags'].get('level', '0'):
                    continue
                ends = []
                for feature, vertex in ((approach, index), (owner, owner_index)):
                    if vertex not in (0, len(feature['xy']) - 1):
                        break
                    direction = feature['xy'][1 if vertex == 0 else -2] - feature['xy'][vertex]
                    ends.append(direction / max(1.e-8, np.linalg.norm(direction)))
                if len(ends) == 2 and np.dot(*ends) < -np.cos(np.radians(35)):
                    continue
                point = approach['xy'][index]
                radius = min(80., max(24., owner['width'] + approach['width'] + 4.))
                approach['junctions'].append(dict(ownerId=owner['id'], x=float(point[0]), z=float(point[1]), radius=radius))
                connections.append((approach, owner, point, radius))
    return connections


def curvature_limit(road):
    """Maximum change of grade per metre (gameplay vertical-curve envelope)."""
    if road['category']=='rail':return .0005
    if road['category']=='path':return .02
    kind=road['kind'].replace('_link','')
    if road['roundabout']:return .0025
    if kind in ('motorway','trunk'):return .001
    if kind=='primary':return .0015
    if kind in ('secondary','tertiary'):return .0025
    if kind=='track':return .012
    return .004


def road_grades(features, crossings, footprints, deck_depth, fixed_samples=()):
    connections = junction_connections(features)
    # Ordinary paths are fitted to pavement afterwards. Paths participating
    # in a physical crossing must be adjustable rather than fixed obstacles.
    by_id = {feature['id']:feature for feature in features}
    covers=tunnel_profiles.constraints(features)
    constrained_paths={identity for crossing in crossings for identity in (crossing['upper'],crossing['lower']) if by_id[identity]['category']=='path'}
    constrained_paths.update(feature['id'] for cover in covers for feature in (cover['upper'],cover['lower']) if feature['category']=='path')
    roads = [feature for feature in features if feature['category'] in ('road','rail') or feature['id'] in constrained_paths]
    nodes, feature_nodes, targets, weights = {}, {}, [], []
    for road in roads:
        indices = []
        priority = 8. if road['kind'] in ('motorway', 'trunk', 'primary') else 2.
        for index, height in enumerate(road['heights']):
            key = (road['category'], source_topology.sample_identity(road, index))
            if key not in nodes:
                nodes[key] = len(targets)
                targets.append(float(height))
                weights.append(priority)
            else:
                node = nodes[key]
                targets[node] = (targets[node] * weights[node] + float(height) * priority) / (weights[node] + priority)
                weights[node] += priority
            indices.append(nodes[key])
        feature_nodes[road['id']] = np.asarray(indices)
    count = len(targets)
    rows, columns, values, limits = [], [], [], []

    def constraint(coefficients, maximum):
        row = len(limits)
        for node, coefficient in coefficients:
            rows.append(row); columns.append(int(node)); values.append(float(coefficient))
        limits.append(float(maximum))

    for road in roads:
        indices = feature_nodes[road['id']]
        grade = .10 if road['kind'].replace('_link', '') in ('motorway', 'trunk', 'primary') else .18
        if road['kind'] in ('residential', 'service', 'unclassified', 'living_street'):
            grade = .25
        if road['kind'] == 'track':
            grade = .35
        if road['roundabout']:
            grade = .02
        if road['category']=='path':grade=.8 if road['kind']=='steps' else .5
        if road['category']=='rail':grade=.04  # plausibility limit; not a national railway design standard
        for index, length in enumerate(np.diff(road['stations'])):
            a, b = indices[index:index + 2]
            constraint(((a, 1), (b, -1)), grade * length)
            constraint(((a, -1), (b, 1)), grade * length)
    # Limit vertical curvature after every clearance/junction/anchor fit.
    # Scale the nonuniform finite difference rather than dividing by tiny
    # retained source-node intervals. This is |g_out-g_in| / mean arc length.
    curve_rows=[];seen_curves=set()
    def curve(a,b,c,first,last,maximum):
        if min(first,last)<.001 or len({int(a),int(b),int(c)})<3:return
        identity=(min(int(a),int(c)),int(b),max(int(a),int(c)))
        if identity in seen_curves:return
        seen_curves.add(identity)
        scale=first+last
        coefficients=((a,last/scale),(b,-1.),(c,first/scale))
        bound=maximum*first*last*.5
        constraint(coefficients,bound)
        constraint([(node,-value) for node,value in coefficients],bound)
        curve_rows.append((int(a),int(b),int(c),first,last,maximum))
    endpoints={}
    for road in roads:
        indices=feature_nodes[road['id']];lengths=np.diff(road['stations'])
        maximum=curvature_limit(road)
        for i in range(1,len(indices)-1):
            curve(*indices[i-1:i+2],float(lengths[i-1]),float(lengths[i]),maximum)
        for end in (0,-1):
            endpoints.setdefault((road['category'],source_topology.sample_identity(road,end)),[]).append((road,end))
    for entries in endpoints.values():
        for i,(first,end) in enumerate(entries):
            for second,other_end in entries[i+1:]:
                if first is second and end==other_end:continue
                if first['layer']!=second['layer'] or first['tags'].get('level','0')!=second['tags'].get('level','0'):continue
                a=1 if end==0 else len(first['xy'])-2
                c=1 if other_end==0 else len(second['xy'])-2
                da=first['xy'][a]-first['xy'][end];dc=second['xy'][c]-second['xy'][other_end]
                if np.dot(da,dc)>-np.cos(np.radians(35))*np.linalg.norm(da)*np.linalg.norm(dc):continue
                la=abs(first['stations'][a]-first['stations'][end]);lc=abs(second['stations'][c]-second['stations'][other_end])
                curve(feature_nodes[first['id']][a],feature_nodes[first['id']][end],feature_nodes[second['id']][c],float(la),float(lc),min(curvature_limit(first),curvature_limit(second)))
    by_id = {feature['id']: feature for feature in features}

    def interpolation(feature, station, sign):
        index = min(len(feature['stations']) - 2, max(0, np.searchsorted(feature['stations'], station) - 1))
        fraction = (station - feature['stations'][index]) / max(.00001, feature['stations'][index + 1] - feature['stations'][index])
        indices = feature_nodes.get(feature['id'])
        if indices is None:
            return [], sign * float(np.interp(station, feature['stations'], feature['heights']))
        return [(indices[index], sign * (1 - fraction)), (indices[index + 1], sign * fraction)], 0.

    # The final grade/junction fit must retain roof cover too. A later
    # surface adjustment otherwise undoes the earlier hidden-floor fit.
    tunnel_samples = 0
    for cover in covers:
        upper, lower = cover['upper'], cover['lower']
        required = lower['tunnelClearance'] + tunnel_profiles.ROOF_COVER_METRES + .02
        for upper_station, lower_station in zip(*tunnel_profiles.constraint_stations(cover)):
            upper_coefficients, upper_fixed = interpolation(upper, upper_station, -1)
            lower_coefficients, lower_fixed = interpolation(lower, lower_station, 1)
            constraint(upper_coefficients + lower_coefficients, -required - upper_fixed - lower_fixed)
            tunnel_samples += 1

    # A shared centre node is insufficient: a wide, sloping through road
    # crosses an entire approach ribbon. Keep the approach centre profile on
    # that surface through the complete local mouth. Rendering fits its edges
    # to the same owner, preserving crossfall without a vertical overlap step.
    mouth_samples = 0
    for approach, owner, junction, radius in connections:
        for station, point in zip(approach['stations'], approach['xy']):
            if np.linalg.norm(point - junction) > radius:
                continue
            location = Point(point)
            if owner['surfaceLine'].distance(location) > (owner['width'] + approach['width']) * .5 + 1.4:
                continue
            owner_station = owner['line'].project(location)
            left, left_fixed = interpolation(approach, station, 1)
            right, right_fixed = interpolation(owner, owner_station, -1)
            coefficients = left + right
            fixed = left_fixed + right_fixed
            constraint(coefficients, JUNCTION_HEIGHT_TOLERANCE_METRES - fixed)
            constraint([(node, -value) for node, value in coefficients], JUNCTION_HEIGHT_TOLERANCE_METRES + fixed)
            mouth_samples += 1
    # Adjustable crossing paths still meet their actual same-floor road
    # nodes. A vertical offset is pavement thickness, never another deck.
    road_nodes={}
    for road in roads:
        if road['category']!='road':continue
        for index in range(len(road['xy'])):
            token=source_topology.sample_identity(road,index)
            if isinstance(token,str) and token.startswith('osm:'):road_nodes.setdefault(token,[]).append((road,index))
    for path in roads:
        if path['category']!='path':continue
        for index in range(len(path['xy'])):
            for road,vertex in road_nodes.get(source_topology.sample_identity(path,index),[]):
                if road['tags'].get('level','0')!=path['tags'].get('level','0'):continue
                a,b=feature_nodes[path['id']][index],feature_nodes[road['id']][vertex]
                constraint(((a,1),(b,-1)),.06);constraint(((a,-1),(b,1)),0.)
    # Neighbour publication is an authority boundary. Refit newly generated
    # approaches after anchoring, but never move an already published seam.
    for identity, station, height in fixed_samples:
        feature = by_id.get(identity)
        if feature is None or feature['id'] not in feature_nodes:
            continue
        coefficients, fixed = interpolation(feature, station, 1)
        constraint(coefficients, height + .001 - fixed)
        constraint([(node, -value) for node, value in coefficients], -height + .001 + fixed)

    for crossing in crossings:
        upper, lower = by_id[crossing['upper']], by_id[crossing['lower']]
        if upper['id'] not in feature_nodes and lower['id'] not in feature_nodes:
            continue
        samples = footprints.get((upper['id'], lower['id']))
        if samples is None:
            samples = ([crossing['upperStation']], [crossing['lowerStation']])
        for upper_station, lower_station in zip(*samples):
            upper_coefficients, upper_fixed = interpolation(upper, upper_station, -1)
            lower_coefficients, lower_fixed = interpolation(lower, lower_station, 1)
            constraint(upper_coefficients + lower_coefficients,
                       -crossing['required'] - deck_depth - .04 - upper_fixed - lower_fixed)
    # Hidden railway floors must leave a solid roof below the terrain even
    # when no road happens to cross the bore. Only real surface portals open.
    from shapely.geometry import LineString
    for feature in roads:
        if feature['category']!='rail' or not feature['tunnel']:continue
        complete=LineString(feature['originalXY'])
        portal_flags=feature.get('surfacePortals',[False,False])
        for index,point in enumerate(feature['xy']):
            at=complete.project(Point(point))
            if portal_flags[0] and at<35 or portal_flags[1] and complete.length-at<35:continue
            coefficients,fixed=interpolation(feature,feature['stations'][index],1)
            constraint(coefficients,float(feature['raw'][index])-feature['tunnelClearance']-tunnel_profiles.ROOF_COVER_METRES-fixed)
    for index, target in enumerate(targets):
        constraint(((index, 1), (count + index, -1)), target)
        constraint(((index, -1), (count + index, -1)), -target)
    matrix = coo_matrix((values, (rows, columns)), shape=(len(limits), count * 2)).tocsr()
    result = linprog(np.r_[np.zeros(count), weights], A_ub=matrix, b_ub=limits,
                     bounds=[(-1000, 10000)] * count + [(0, None)] * count,
                     method='highs', options={'time_limit': 180,'primal_feasibility_tolerance':1.e-9,'dual_feasibility_tolerance':1.e-9})
    if not result.success:
        error=ProfileFitInfeasible if result.status==2 else ValueError
        raise error('Connected road grade/clearance fit refused: ' + result.message)
    maximum_curve=0.;maximum_curve_ratio=0.
    for a,b,c,first,last,maximum in curve_rows:
        actual=abs((result.x[c]-result.x[b])/last-(result.x[b]-result.x[a])/first)/((first+last)*.5)
        maximum_curve=max(maximum_curve,actual);maximum_curve_ratio=max(maximum_curve_ratio,actual/maximum)
    if maximum_curve_ratio>1.002:raise ValueError('Road curvature fit exceeded its bound')
    maximum_change = 0.
    for road in roads:
        fitted = result.x[feature_nodes[road['id']]]
        maximum_change = max(maximum_change, float(np.max(abs(fitted - road['heights']))))
        road['heights'] = fitted
    return dict(curvatureSamples=len(curve_rows),maximumCurvature=maximum_curve,maximumCurvatureRatio=maximum_curve_ratio,nodes=count, constrainedPaths=len(constrained_paths), constraints=len(limits), junctionMouthSamples=mouth_samples, tunnelCoverSamples=tunnel_samples, maximumCorrectionMetres=maximum_change)


def waterways(features, elevation):
    """Use low channel samples; mask bridge/road embankments along waterways.

    The declared OSM flow direction supplies only topology, not a strict
    monotonic assumption. Streams may be culverted; only explicitly mapped
    fords allow water at carriageway level. No road/rail profile is modified.
    """
    roads = [feature for feature in features if feature['category'] in ('road','rail')]
    adjusted = 0
    for water in (feature for feature in features if feature['category'] == 'water'):
        points, stations = water['xy'], water['stations']
        tangent = np.gradient(points, axis=0)
        tangent /= np.maximum(1.e-8, np.linalg.norm(tangent, axis=1))[:, None]
        side = np.c_[-tangent[:, 1], tangent[:, 0]]
        offsets = np.linspace(-.45, .45, 5) * water['width']
        channel = np.min([elevation.sample(points + side * offset) for offset in offsets], axis=0) - .12
        terrain_cap = channel.copy()
        visible = np.ones(len(points), dtype=bool)
        crossing_caps = []
        for road in roads:
            if road['tags'].get('ford') in ('yes', 'stepping_stones'):
                continue
            if road['line'].distance(water['line']) > road['width'] * .5 + water['width'] * .5 + 2:
                continue
            for index, point in enumerate(points):
                location = Point(point)
                if road['line'].distance(location) < road['width'] * .5 + 4:
                    visible[index] = False
                    height = float(np.interp(road['line'].project(location), road['stations'], road['heights']))
                    crossing_caps.append((index, height - .65))
        if visible.sum() >= 2:
            channel[~visible] = np.interp(stations[~visible], stations[visible], channel[visible])
        regular = np.arange(0, stations[-1] + 2, 2.)
        low_channel = minimum_filter1d(np.interp(regular, stations, channel), size=11, mode='nearest')
        filtered = gaussian_filter1d(low_channel, sigma=2, mode='nearest')
        water['heights'] = np.minimum(terrain_cap, np.minimum(channel, np.interp(stations, regular, filtered)))
        for index, maximum in crossing_caps:
            water['heights'][index] = min(water['heights'][index], maximum)
        adjusted += 1
    return dict(waterways=adjusted)
