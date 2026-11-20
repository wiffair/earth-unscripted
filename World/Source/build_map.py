"""Derive bounded Newbridge profiles from OSM topology and Welsh 1 m LiDAR.

Output contains numeric terrain and profiles, never baked Unity meshes. Unity
constructs and retires actual geometry in spatial chunks at runtime. Coordinates
are BNG metres relative to (321000,197000); local Y is source height minus 100 m.
The LiDAR delivery vertical reference remains unverified; no absolute conversion
to the installed game's Copernicus terrain is attempted.
"""
from pathlib import Path
import hashlib
import json
import math
import time
import argparse
import re
import gradient_planning
import tunnel_profiles
import road_layout
import source_topology
import canopy
import profile_safety
import transport_metadata
import railway_furniture
import osm_areas
import numpy as np
import rasterio
from pyproj import Transformer
from scipy.ndimage import map_coordinates, median_filter, gaussian_filter1d
from scipy.interpolate import PchipInterpolator
from shapely.geometry import LineString, box, Point, Polygon
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
ORIGIN = np.array([321000., 197000.])
HEIGHT_ORIGIN = 100.
FRAME_CRS = 27700
from region_frame import geographic_handoff

WORLD_CRS = None  # optional game projection; raster sampling remains FRAME_CRS
TRAFFIC_SIDE = 'left'
COUNTRY_CODE = 'GB'
ROUTE_PLAN=json.loads((ROOT/'route-plan.json').read_text())
BOUNDS=tuple(float(value-ORIGIN[index%2]) for index,value in enumerate(ROUTE_PLAN['boundsBNG']))
PROFILE_STEP = 2.
GRID_STEP = 2.
DECK_DEPTH = .75
ROAD_CLEARANCE = 5.1
TRAIN_CLEARANCE = 5.6  # game envelope allowance, not a surveyed UK railway standard
FOOT_CLEARANCE = 2.7


def osm_layer(tags):
    """Resolve imperfect layer tags without confusing relative order with metres.

    Keep a single explicit integer (including zero) exactly. For conflicting
    semicolon lists, bridge/tunnel context selects the upper/lower declaration;
    otherwise retain the first valid declaration. Missing layer keeps OSM's
    implicit zero. Only wholly malformed declarations use structural context.
    """
    raw=tags.get('layer')
    if raw is None or str(raw).strip()=='':return 0
    values=[]
    for token in str(raw)[:1024].split(';'):
        token=token.strip()
        if not re.fullmatch(r'[+-]?\d{1,10}(?:\.0+)?',token):continue
        value=int(token.split('.')[0])
        if -(2**31)<=value<2**31:values.append(value)
    bridge=str(tags.get('bridge','no')).lower() not in ('no','false','0','')
    tunnel=str(tags.get('tunnel','no')).lower() not in ('no','false','0','') or tags.get('location')=='underground'
    if values:
        if tunnel and not bridge:return min(values)
        if bridge and not tunnel:return max(values)
        return values[0]
    return -1 if tunnel and not bridge else 1 if bridge and not tunnel else 0


class FeatureSet(list):
    """Derived features plus numeric constraints needed after seam anchoring."""
    profile_constraints = None


PROGRESS = None
_last_progress = 0.
_progress_stage = "Preparing road profiles"
_progress_completed = _progress_total = 0

def report_progress(stage=None, completed=None, total=None):
    global _last_progress, _progress_stage, _progress_completed, _progress_total
    changed = stage is not None and stage != _progress_stage
    if stage is not None:_progress_stage = stage
    if completed is not None:_progress_completed = completed
    if total is not None:_progress_total = total
    if PROGRESS is None:return
    now=time.monotonic()
    if changed or now-_last_progress>=.5:
        _last_progress=now;PROGRESS(_progress_stage,_progress_completed,_progress_total)


class RasterCoverageIndex:
    """Exact candidate filter; keep original raster precedence and sampling."""
    CELL = 1024.
    MAX_CELLS = 4096

    def __init__(self, rows):
        self.count = len(rows)
        cells = {}
        large = []
        for index, row in enumerate(rows):
            bounds = row[0]
            left, right = math.floor(bounds.left/self.CELL), math.floor(bounds.right/self.CELL)
            bottom, top = math.floor(bounds.bottom/self.CELL), math.floor(bounds.top/self.CELL)
            if (right-left+1)*(top-bottom+1) > self.MAX_CELLS:
                large.append(index)
                continue
            for x in range(left, right+1):
                for z in range(bottom, top+1):
                    cells.setdefault((x,z), []).append(index)
        self.cells = {key:tuple(value) for key,value in cells.items()}
        self.large = tuple(large)

    def candidates(self, xy):
        if not self.count or not len(xy):
            return ()
        try:
            if len(xy) == 1:
                key = (math.floor(float(xy[0,0])/self.CELL), math.floor(float(xy[0,1])/self.CELL))
                found = self.cells.get(key, ())
                return tuple(sorted(set(found).union(self.large))) if self.large else found
            low, high = np.min(xy,axis=0), np.max(xy,axis=0)
            left, right = math.floor(float(low[0])/self.CELL), math.floor(float(high[0])/self.CELL)
            bottom, top = math.floor(float(low[1])/self.CELL), math.floor(float(high[1])/self.CELL)
        except (ValueError, OverflowError):
            # Preserve the sampler's existing missing/nonfinite-data behaviour.
            return range(self.count)
        if (right-left+1)*(top-bottom+1) > self.MAX_CELLS:
            return range(self.count)
        found = set(self.large)
        for x in range(left, right+1):
            for z in range(bottom, top+1):
                found.update(self.cells.get((x,z), ()))
        return sorted(found)


class Elevation:
    """Keep original DTM/DSM separate; interpolate only within available tiles."""
    def __init__(self):
        self.rasters = {}
        self.survey_rasters = {}
        self.raster_indexes = {}
        self.survey_indexes = {}
        self.progress_samples=0
        self.to_source=geographic_handoff(WORLD_CRS,FRAME_CRS) if WORLD_CRS else None
        for kind in ('dtm', 'dsm'):
            rows = []
            surveyed = []
            for path in sorted((ROOT/'Sources').glob('*-'+kind+'.tif')):
                with rasterio.open(path) as source:
                    assert source.crs.to_epsg() == FRAME_CRS and source.res[0]>0 and source.res[1]>0
                    measured=source.read(1,masked=True)
                    data=np.asarray(measured.filled(np.nan),dtype=float)
                    missing=~np.isfinite(data)
                    if missing.all():continue
                    valid=(~missing).astype('uint8') if missing.any() else None
                    if missing.any():
                        # Preserve valid measurements; nearest valid land fills
                        # water/void cells only. Covered roads use independent
                        # portal profiles rather than treating this as measured.
                        from scipy.ndimage import distance_transform_edt
                        indices=distance_transform_edt(missing,return_distances=False,return_indices=True)
                        data[missing]=data[tuple(indices[:,missing])]
                    assert np.min(data)>-1000 and np.max(data)<10000
                    rows.append((source.bounds,data,source.res,valid))
                    if not path.name.startswith('00-coarse-'):
                        surveyed.append((source.bounds,source.res,valid))
            assert len(rows) >= 1
            self.rasters[kind] = rows
            self.survey_rasters[kind] = surveyed
            self.raster_indexes[kind] = RasterCoverageIndex(rows)
            self.survey_indexes[kind] = RasterCoverageIndex(surveyed)

    def measured_at(self,xy,kind='dtm'):
        xy=np.asarray(xy)+ORIGIN
        if self.to_source is not None:
            east,north=self.to_source.transform(xy[:,0],xy[:,1]);xy=np.c_[east,north]
        observed=np.zeros(len(xy),dtype=bool)
        for row in self.survey_indexes[kind].candidates(xy):
            bounds,resolution,valid = self.survey_rasters[kind][row]
            mask=(xy[:,0]>=bounds.left)&(xy[:,0]<bounds.right)&(xy[:,1]>=bounds.bottom)&(xy[:,1]<bounds.top)
            if not mask.any():continue
            indices=np.flatnonzero(mask)
            if valid is None:observed[indices]=True
            else:
                coordinates=[(bounds.top-xy[mask,1])/resolution[1]-.5,(xy[mask,0]-bounds.left)/resolution[0]-.5]
                observed[indices[map_coordinates(valid,coordinates,order=0,mode='nearest')>0]]=True
        return observed

    def sample(self, xy, kind='dtm'):
        self.progress_samples+=1
        if self.progress_samples%256==0:report_progress()
        xy = np.asarray(xy) + ORIGIN
        if self.to_source is not None:
            east,north=self.to_source.transform(xy[:,0],xy[:,1])
            xy=np.c_[east,north]
        result = np.full(len(xy), np.nan)
        trusted=np.zeros(len(xy),dtype=bool)
        for row in self.raster_indexes[kind].candidates(xy):
            bounds, data, resolution,valid = self.rasters[kind][row]
            mask = (xy[:,0]>=bounds.left)&(xy[:,0]<bounds.right)&(xy[:,1]>=bounds.bottom)&(xy[:,1]<bounds.top)
            if not mask.any():continue
            coordinates=[(bounds.top-xy[mask,1])/resolution[1]-.5,(xy[mask,0]-bounds.left)/resolution[0]-.5]
            sampled=map_coordinates(data,coordinates,order=1,mode='nearest')
            observed=map_coordinates(valid,coordinates,order=0,mode='nearest')>0 if valid is not None else np.ones(len(sampled),dtype=bool)
            indices=np.flatnonzero(mask)
            accepted=observed | ~trusted[indices]
            result[indices[accepted]]=sampled[accepted]
            trusted[indices[observed]]=True
        if not np.all(np.isfinite(result)):
            raise ValueError('Missing elevation coverage')
        return result - HEIGHT_ORIGIN


def widths(tags, category):
    """Prefer mapped dimensions/lanes; conservative class estimates are explicit."""
    try:
        if 'width' in tags:
            return min(25., max(1., float(tags['width'].split()[0]))), 'mapped width'
    except ValueError:
        pass
    if category == 'rail': return 4.2, 'single-track ballast envelope'
    if category == 'water': return (17. if tags.get('waterway') == 'river' else 2.5), 'waterway-class estimate'
    if category == 'path': return (2.8 if tags.get('highway')=='cycleway' else 1.8), 'path-class estimate'
    try:
        lanes = int(tags['lanes'])
        return min(18., max(3.8, lanes*3.2+.4)), 'mapped lanes / estimated lane width'
    except (KeyError, ValueError):
        return dict(primary=6.8, secondary=6.4, tertiary=6., residential=5.6, service=4., unclassified=5.2, track=3.4).get(tags.get('highway'),5.2), 'road-class estimate'


def speed_limit(tags, category, width):
    """Return metres/second plus provenance; OSM bare numbers are km/h.

    Mapped limits and UK national-limit codes take precedence. Unmapped values
    are explicit conservative game estimates, never claimed as legal surveys.
    """
    if category!='road':return 0.,'not a vehicle road'
    value=tags.get('maxspeed','').split(';')[0].strip().lower()
    national={'gb:nsl_dual':70,'gb:nsl_single':60,'gb:motorway':70}
    if value in national:return national[value]*.44704,'mapped '+value
    match=re.fullmatch(r'(\d+(?:\.\d+)?)\s*(mph|km/h)?',value)
    if match:
        return float(match[1])*(.44704 if match[2]=='mph' else 1/3.6),'mapped '+value
    kind=tags.get('highway','')
    if tags.get('junction')=='roundabout':mph=20
    elif kind=='motorway':mph=70
    elif kind in ('trunk','primary') and tags.get('oneway') in ('yes','1','true','-1') and width>=6.4:mph=70
    elif kind in ('trunk','primary'):mph=60
    elif kind in ('secondary','tertiary'):mph=30
    else:mph=20
    return mph*.44704,'estimated road-class fallback'


def line_samples(line, source_line=None):
    # Keep the regular grid tied to the complete source way. Restarting it at
    # each clipped halo gives neighbouring areas different vertical-curve
    # knots, so even an unchanged owned profile can become infeasible.
    # Retain original vertices and clip endpoints to preserve XZ turns.
    coords = np.asarray(line.coords)
    original = np.r_[0., np.cumsum(np.linalg.norm(np.diff(coords,axis=0),axis=1))]
    offset = 0. if source_line is None else source_line.project(Point(line.coords[0]))
    first = math.ceil((offset - 1.e-8) / PROFILE_STEP) * PROFILE_STEP - offset
    regular = np.arange(max(0., first), line.length, PROFILE_STEP)
    stations = np.unique(np.r_[original, regular, line.length])
    xy = np.array([line.interpolate(s).coords[0] for s in stations])
    return stations, xy


def physical_smooth(stations, heights, sigma_metres):
    """Filter in metres, then return to the geometry's nonuniform vertex stations."""
    # Extend the uniform filter grid past the endpoint with clamped source values;
    # appending a tiny last interval would falsely amplify its smoothed grade.
    regular=np.arange(0.,math.ceil(stations[-1]/PROFILE_STEP)*PROFILE_STEP+.1,PROFILE_STEP)
    sampled=np.interp(regular,stations,heights)
    filtered=gaussian_filter1d(median_filter(sampled,size=min(9,max(1,len(sampled)//2*2-1)),mode='nearest'),sigma=sigma_metres/PROFILE_STEP,mode='nearest')
    return np.interp(stations,regular,filtered)


def smooth_road_seams(features):
    """Smooth connected road profiles across source-way boundaries in metres.

    DTM and DSM often disagree at a bridge endpoint. Filtering each OSM way
    separately preserves a false dip there. Extend the profile 40 m into the
    straightest actual connected road (within 35 degrees), then apply an 8 m
    Gaussian. Work from one immutable height snapshot so traversal order cannot
    change the result. Clearance solving and exact node stitching follow this.
    """
    roads=[feature for feature in features if feature['category']=='road']
    endpoints={}
    snapshot={feature['id']:feature['heights'].copy() for feature in roads}
    for feature in roads:
        for endpoint in (0,-1):
            key=source_topology.sample_identity(feature,endpoint)
            endpoints.setdefault(key,[]).append((feature,endpoint))
    for feature in roads:
        source_stations=feature['stations'].copy()
        source_heights=snapshot[feature['id']].copy()
        for endpoint in (0,-1):
            point=feature['xy'][endpoint]
            inward=feature['xy'][1 if endpoint==0 else -2]-point
            inward/=max(1e-8,np.linalg.norm(inward))
            candidates=[]
            for neighbour,neighbour_end in endpoints[source_topology.sample_identity(feature,endpoint)]:
                if neighbour is feature:continue
                direction=neighbour['xy'][1 if neighbour_end==0 else -2]-point
                direction/=max(1e-8,np.linalg.norm(direction))
                alignment=float(np.dot(-inward,direction))
                if alignment>math.cos(math.radians(35)):
                    candidates.append((alignment,neighbour,neighbour_end))
            if not candidates:continue
            _,neighbour,neighbour_end=max(candidates,key=lambda row:row[0])
            distance=np.arange(PROFILE_STEP,min(40.,neighbour['stations'][-1])+.001,PROFILE_STEP)
            if len(distance)==0:continue
            neighbour_station=distance if neighbour_end==0 else neighbour['stations'][-1]-distance
            height=np.interp(neighbour_station,neighbour['stations'],snapshot[neighbour['id']])
            extension=-distance if endpoint==0 else feature['stations'][-1]+distance
            source_stations=np.r_[source_stations,extension]
            source_heights=np.r_[source_heights,height]
        order=np.argsort(source_stations)
        source_stations=source_stations[order];source_heights=source_heights[order]
        offset=source_stations[0]
        filtered=physical_smooth(source_stations-offset,source_heights,8.)
        feature['heights']=np.interp(feature['stations'],source_stations,filtered)


def apply_junction_targets(feature, anchors):
    # Most junctions already match after the first pass. A zero-valued PCHIP
    # correction is exactly zero everywhere; avoid constructing it repeatedly.
    if not anchors or all(target == feature['heights'][index] for index,target in anchors.items()):
        return False
    real_stations=np.array([feature['stations'][index] for index in anchors])
    knots={float(feature['stations'][index]):target-feature['heights'][index] for index,target in anchors.items()}
    radius=160. if feature['category']=='road' else 25.
    threshold=159.9 if feature['category']=='road' else 24.9
    for station in real_stations:
        for candidate in [max(0.,station-radius),min(feature['stations'][-1],station+radius)]:
            if np.min(np.abs(real_stations-candidate))>threshold:knots.setdefault(float(candidate),0.)
    knots.setdefault(0.,0.);knots.setdefault(float(feature['stations'][-1]),0.)
    ordered=sorted(knots)
    if len(ordered)>1:feature['heights']+=PchipInterpolator(ordered,[knots[station] for station in ordered])(feature['stations'])
    return True


def derive(bridge_recovery=True):
    started = time.perf_counter()
    report_progress("Opening elevation tiles",0,0)
    elevation = Elevation()
    load_seconds = time.perf_counter()-started
    source = json.loads((ROOT/'Sources/osm-area-complete.json').read_text())
    transform = Transformer.from_crs(4326, WORLD_CRS or FRAME_CRS, always_xy=True)
    envelope = box(*BOUNDS)
    features = FeatureSet()
    buildings = []
    woodland = []
    water_areas = []
    mapped_water = []
    places = []
    ways=list(osm_areas.expanded(source['elements']))
    report_progress("Extracting roads and buildings",0,len(ways))
    for item_index,way in enumerate(ways):
        if item_index%32==0:report_progress(completed=item_index,total=len(ways))
        tags = way.get('tags', {})
        if way.get('type')=='node' and tags.get('place') and tags.get('name'):
            x,z=np.asarray(transform.transform(way['lon'],way['lat']))-ORIGIN
            places.append(dict(id=str(way['id']),name=tags['name'],x=float(x),z=float(z)))
            continue
        visual_area=not (tags.get('highway') or tags.get('railway')) and (tags.get('landuse')=='forest' or tags.get('natural') in ('wood','water') or tags.get('waterway')=='riverbank')
        if not way.get('geometry') or len(way['geometry'])<2: continue
        if any(point is None for point in way['geometry']):
            if visual_area:continue
            bounds=way.get('bounds')
            if bounds:
                corners=[transform.transform(lon,lat) for lon in (bounds['minlon'],bounds['maxlon']) for lat in (bounds['minlat'],bounds['maxlat'])]
                projected=np.asarray(corners)-ORIGIN
                if not box(*projected.min(axis=0),*projected.max(axis=0)).intersects(envelope):continue
            raise ValueError('Incomplete in-region OSM geometry: '+str(way.get('id')))
        coordinates = np.array([transform.transform(p['lon'],p['lat']) for p in way['geometry']])-ORIGIN
        source_nodes=way.get('nodes',[])
        if visual_area and len(coordinates)>=4:
            holes=[np.asarray([transform.transform(p["lon"],p["lat"]) for p in ring])-ORIGIN for ring in way.get("_holes",[])]
            polygon=Polygon(coordinates,holes)
            if not polygon.is_valid:polygon=polygon.buffer(0)
            polygon=polygon.intersection(envelope)
            if not polygon.is_empty:
                if tags.get('natural')=='water' or tags.get('waterway')=='riverbank':
                    water_areas.append(polygon);mapped_water.append((way['id'],tags.get('name','Mapped water'),tags,polygon))
                else:woodland.append(polygon)
            continue
        if tags.get('oneway')=='-1':coordinates=coordinates[::-1];source_nodes=source_nodes[::-1]
        if tags.get('building') and len(coordinates)>=4:
            centre = coordinates.mean(axis=0)
            if envelope.contains(Point(centre)):
                try:levels=max(1,min(40,float(tags.get('building:levels','2').split(';')[0])))
                except ValueError:levels=2.
                explicit_height=road_layout.metres(tags.get('height'))
                height=explicit_height or levels*2.9
                kind=tags.get('building','yes')
                small=Polygon(coordinates).area<280
                shape=tags.get('roof:shape','gabled' if kind in ('house','residential','detached','semidetached_house','terrace') or kind=='yes' and small else 'flat')
                roof_height=road_layout.metres(tags.get('roof:height')) or min(2.4,height*.25)
                # Every footprint/hole vertex uses this same immutable terrain sample.
                base_height=round(float(elevation.sample([centre])[0]),3)
                holes=[[dict(x=round(float(x),3),y=base_height,z=round(float(z),3)) for x,z in np.asarray([transform.transform(p["lon"],p["lat"]) for p in ring])-ORIGIN] for ring in way.get("_holes",[])]
                buildings.append(dict(holes=[dict(points=ring) for ring in holes],id=str(way['id']),kind=kind,roofShape=shape,roofHeight=roof_height,
                    heightIncludesRoof=bool(explicit_height),levels=int(round(levels)),
                    layer=osm_layer(tags),underground=tags.get('location')=='underground',minHeight=road_layout.metres(tags.get('min_height')) or 0.,
                    points=[dict(x=round(x,3),y=base_height,z=round(z,3)) for x,z in coordinates],height=min(120.,height)))
            continue
        if tags.get('railway') in ('rail','light_rail','subway','tram','narrow_gauge'): category='rail'
        elif tags.get('waterway') in ('river','stream','canal'): category='water'
        elif tags.get('highway') in ('footway','path','cycleway','steps','pedestrian','bridleway'): category='path'
        elif tags.get('highway') in ('primary','secondary','tertiary','residential','service','unclassified','track','primary_link','secondary_link','trunk','trunk_link','motorway','motorway_link','living_street'): category='road'
        else: continue
        if tags.get('area')=='yes': continue
        source_line = LineString(coordinates)
        clipped = source_line.intersection(envelope)
        if clipped.is_empty: continue
        pieces = [clipped] if clipped.geom_type=='LineString' else list(getattr(clipped,'geoms',[]))
        for piece_index,line in enumerate(pieces):
            if line.geom_type!='LineString' or line.length<1.: continue
            station,xy = line_samples(line, source_line)
            tangent=np.gradient(xy,axis=0);tangent/=np.maximum(1.e-8,np.linalg.norm(tangent,axis=1))[:,None]
            side=np.c_[-tangent[:,1],tangent[:,0]]
            width,width_source=widths(tags,category)
            offsets=np.array([-.24,0.,.24])*min(width,6.)
            dtm=np.median(np.array([elevation.sample(xy+side*offset) for offset in offsets]),axis=0)
            dsm=np.median(np.array([elevation.sample(xy+side*offset,'dsm') for offset in offsets]),axis=0)
            bridge=tags.get('bridge','no') not in ('no','false','0')
            heights=dsm if bridge else dtm
            # Reject short object spikes without flattening long hills.
            heights=physical_smooth(station,heights,3.)
            if category=='rail': heights=physical_smooth(station,heights,25.)+.45
            if category=='water': heights=physical_smooth(station,heights,14.)-.35
            if category=='path': heights+=.06
            if category=='road': heights+=.03
            features.append(dict(id=str(way['id'])+(f'-clip{piece_index}' if len(pieces)>1 else ''), sourceId=str(way['id']), name=tags.get('name',tags.get('ref',category)), ref=tags.get('ref',''), kind=tags.get('highway',tags.get('railway',tags.get('waterway',''))),category=category,width=width,widthSource=width_source,bridge=bridge,tunnel=tags.get('tunnel','no') not in ('no','false','0'),tunnelClearance=0. if category=='water' else TRAIN_CLEARANCE if category=='rail' else FOOT_CLEARANCE if category=='path' else ROAD_CLEARANCE,layer=osm_layer(tags),oneway=road_layout.one_way(tags)[0],roundabout=tags.get('junction') in ('roundabout','circular'),stations=station,xy=xy,heights=heights,raw=dtm.copy(),line=line,tags=tags,originalXY=coordinates))
            feature=features[-1]
            if category=='rail':feature.update(transport_metadata.railway(tags,COUNTRY_CODE))
            feature['onewaySource']=road_layout.one_way(tags)[1]
            feature['level']=tags.get('level','0')
            feature['location']=tags.get('location','')
            if category=='rail' and (tags.get('location')=='underground' or tags.get('railway')=='subway' and feature['layer']<0 and tags.get('tunnel')!='no'):feature['tunnel']=True
            feature['tunnelKind']=tags.get('tunnel','no')
            feature['galleryOpenLeft']=tags.get('avalanche_protector:left')=='open'
            feature['galleryOpenRight']=tags.get('avalanche_protector:right')=='open'
            if tags.get('oneway')=='-1':feature['galleryOpenLeft'],feature['galleryOpenRight']=feature['galleryOpenRight'],feature['galleryOpenLeft']
            feature['sourceNodeIds']=source_topology.point_identities(str(way['id']),coordinates,source_nodes,xy)
            if len(source_nodes)==len(coordinates):feature['originalNodeIds']=source_nodes
            if feature['tunnel'] and category!='water':
                portal_heights=[]
                for end,adjacent in ((0,1),(-1,-2)):
                    outward=coordinates[end]-coordinates[adjacent]
                    outward/=max(1.e-8,np.linalg.norm(outward))
                    distances=np.arange(6.,33.,2.)
                    observations=elevation.sample(coordinates[end]+distances[:,None]*outward)
                    fit=np.polyfit(distances,observations,1)
                    residual=abs(observations-np.polyval(fit,distances))
                    keep=residual<=max(.3,float(np.median(residual))*2.5)
                    if keep.sum()>=3:fit=np.polyfit(distances[keep],observations[keep],1)
                    portal_heights.append(float(fit[1]))
                feature['portalHeights']=portal_heights
    tunnel_profiles.plan(features)
    for feature in features:
        if feature['category']=='road':
            feature.update(road_layout.layout(feature['tags'],feature['width'],TRAFFIC_SIDE,feature['tags'].get('oneway')=='-1'))
        feature['surfaceLine']=road_layout.surface_line(feature)
        value,provenance=road_layout.clearance(feature['tags'],feature['category'],feature['tunnelClearance'])
        feature['tunnelClearance']=0. if feature['category']=='water' else feature['railClearanceHeight'] if feature['category']=='rail' else value
        feature['tunnelClearanceSource']=provenance
    report_progress("Planning road and railway gradients",0,0)
    stacked_recoveries=gradient_planning.recover_stacked_decks(features)
    smooth_road_seams(features)
    gradient_planning.smooth(features)
    gradient_planning.roundabout_planes(features)
    profiles_seconds=time.perf_counter()-started-load_seconds

    # Mask lower samples hidden by bridge decks, interpolate from visible approaches.
    bridges=[f for f in features if f['bridge'] and f['category']!='water']
    crossings=[]
    adjustments=[]
    report_progress("Solving bridge and tunnel crossings",0,len(bridges))
    for bridge_index,upper in enumerate(bridges):
        report_progress(completed=bridge_index,total=len(bridges))
        for lower in features:
            if lower is upper or lower['category']=='water': continue
            # Multiple bridges can cross at different declared layers. Only
            # the higher layer is the upper structure in this ordered pair.
            if lower['bridge'] and upper['layer']<=lower['layer']:continue
            intersection=upper['line'].intersection(lower['line'])
            points=[intersection] if intersection.geom_type=='Point' else [p for p in getattr(intersection,'geoms',[]) if p.geom_type=='Point']
            for point in points:
                us=upper['line'].project(point);ls=lower['line'].project(point)
                # Same-level source endpoint joins are not underpasses.
                # The two ribbons can overlap beside their real source-node
                # join. That pocket is an approach, not a second underpass.
                shared=source_topology.shared_coordinates(upper,lower)
                join_radius=max(upper['width'],lower['width'])*.5+1.5
                if any(Point(q).distance(point)<join_radius for q in shared):continue
                radius=upper['width']*.5+5.
                hidden=np.abs(lower['stations']-ls)<radius
                visible=~hidden
                if np.sum(visible)>=2:
                    lower['heights'][hidden]=np.interp(lower['stations'][hidden],lower['stations'][visible],lower['heights'][visible])
                required=lower['railClearanceHeight'] if lower['category']=='rail' else FOOT_CLEARANCE if lower['category']=='path' else ROAD_CLEARANCE
                # A mapped low road restriction is stronger evidence than fallback.
                if lower['category']=='road':required,_=road_layout.clearance(lower['tags'],'road',required)
                uy=float(np.interp(us,upper['stations'],upper['heights']))
                ly=float(np.interp(ls,lower['stations'],lower['heights']))
                deficit=required-(uy-DECK_DEPTH-ly)
                if deficit>0:
                    transition=120. if lower['category']=='rail' else 45. if lower['category']=='road' else 25.
                    distance=np.abs(lower['stations']-ls)
                    weight=np.where(distance<transition,(.5+.5*np.cos(np.pi*np.minimum(distance/transition,1.))),0.)
                    # Predominantly lower the obscured section; avoid big rail dips.
                    # Keep measured approaches close to their evidence. Prefer
                    # raising the unknown structure over carving a deep road dip.
                    drop=min(deficit,.5 if lower['category'] in ('rail','path') else .6)
                    lower['heights']-=drop*weight
                    remaining=deficit-drop
                    if remaining>0:
                        distance=np.abs(upper['stations']-us)
                        transition=100. if upper['category']=='rail' else 70.
                        weight=np.where(distance<transition,(.5+.5*np.cos(np.pi*np.minimum(distance/transition,1.))),0.)
                        upper['heights']+=remaining*weight
                    adjustments.append(dict(upper=upper['id'],lower=lower['id'],loweringMetres=round(drop,4),raisingMetres=round(remaining,4)))
                crossings.append(dict(upper=upper['id'],lower=lower['id'],x=point.x,z=point.y,upperStation=us,lowerStation=ls,required=required))

    # Connect actual shared source coordinates, not nearby independent roads.
    # Every original OSM vertex was retained above, including interior T joins.
    groups={}
    for f in features:
        if f['category']=='water': continue
        for index,(x,z) in enumerate(f['xy']):
            key=(source_topology.sample_identity(f,index),f['category'])
            groups.setdefault(key,[]).append((f,index))
    def joined_height(entries):
        # Preserve the fitted ring while its connected approaches blend in.
        observed=[f['heights'][i] for f,i in entries if f['roundabout']]
        if observed:return float(np.median(observed))
        if entries[0][0]['category']=='road':
            # A small driveway/tunnel stub cannot dictate a major road's
            # height merely because both share a junction node. Fit the minor
            # approach to the authoritative main profile, including on decks.
            def authority(feature):
                return 100 if feature['kind'].endswith('_link') else gradient_planning.ROAD_PROFILE_PRIORITY.get(feature['kind'],8)
            highest=min(authority(f) for f,i in entries)
            entries=[(f,i) for f,i in entries if authority(f)==highest]
        if not observed:
            covered=[f['heights'][i] for f,i in entries if f['tunnel'] and f['category']!='water']
            if covered:return float(min(covered))
            observed=[f['heights'][i] for f,i in entries if f['bridge']] if bridge_recovery else []
        return float(np.median(observed or [f['heights'][i] for f,i in entries]))

    for entries in groups.values():
        if len(entries)<2 or len({f['id'] for f,i in entries})<2: continue
        target=joined_height(entries)
        for f,index in entries:
            delta=target-f['heights'][index]
            distance=np.abs(f['stations']-f['stations'][index]);radius=160. if f['category']=='road' else 16.
            weight=np.maximum(0.,1.-distance/radius)
            f['heights']+=delta*weight

    # Recheck after connected-node blending. Small remaining deficits lift the
    # upper structure and smoothly carry that change into connected approaches.
    by_id={f['id']:f for f in features}
    for _ in range(3):
        for c in crossings:
            upper,lower=by_id[c['upper']],by_id[c['lower']]
            gap=float(np.interp(c['upperStation'],upper['stations'],upper['heights'])-DECK_DEPTH-np.interp(c['lowerStation'],lower['stations'],lower['heights']))
            deficit=c['required']+.03-gap
            if deficit<=0: continue
            upper['heights']+=deficit
            for endpoint in [0,-1]:
                q=upper['xy'][endpoint]
                for neighbour in features:
                    if neighbour is upper or neighbour['category']!=upper['category']: continue
                    distances=np.linalg.norm(neighbour['xy']-q,axis=1)
                    index=int(np.argmin(distances))
                    if distances[index]>.01:continue
                    if source_topology.sample_identity(neighbour,index)!=source_topology.sample_identity(upper,endpoint):continue
                    delta=upper['heights'][endpoint]-neighbour['heights'][index]
                    distance=np.abs(neighbour['stations']-neighbour['stations'][index])
                    radius=150. if neighbour['category']=='rail' else 180.
                    weight=np.where(distance<radius,.5+.5*np.cos(np.pi*np.minimum(distance/radius,1.)),0.)
                    neighbour['heights']+=delta*weight
    footprint_samples={}
    def footprint_minimum(upper,lower):
        """Sample the complete overlapping widths, including overlap boundary vertices."""
        key=(upper['id'],lower['id'])
        if key in footprint_samples:
            upper_stations,lower_stations=footprint_samples[key]
            return float(np.min(np.interp(upper_stations,upper['stations'],upper['heights'])-DECK_DEPTH-np.interp(lower_stations,lower['stations'],lower['heights'])))
        overlap=upper['surfaceLine'].buffer(upper['width']*.5,cap_style='flat').intersection(lower['surfaceLine'].buffer(lower['width']*.5,cap_style='flat'))
        if overlap.is_empty:return None
        if bridge_recovery:
            # A bridge and its ramp/steps can overlap as a connected polygon.
            # Their genuine shared source-node join is intentionally at deck
            # height, not an underpass requiring clearance from itself.
            shared=source_topology.shared_coordinates(upper,lower)
            join_radius=max(upper['width'],lower['width'])*.5+1.5
            for coordinate in shared:
                overlap=overlap.difference(Point(coordinate).buffer(join_radius))
            if overlap.is_empty:return None
        candidates=[]
        polygons=[overlap] if overlap.geom_type=='Polygon' else [g for g in getattr(overlap,'geoms',[]) if g.geom_type=='Polygon']
        # A looping ramp can both cross underneath and join the same bridge.
        # Its shared endpoint is intentionally at deck height. Only disconnected
        # footprint components containing an actual underpass are constraints;
        # including the connected endpoint creates an impossible clearance and
        # repeatedly raises the whole bridge on each solver iteration.
        crossing_points=[Point(c['x'],c['z']) for c in crossings
                         if c['upper']==upper['id'] and c['lower']==lower['id']]
        polygons=[polygon for polygon in polygons
                  if any(polygon.distance(point)<.001 for point in crossing_points)]
        if not polygons:return None
        from shapely.ops import unary_union
        overlap=unary_union(polygons)
        west,south,east,north=overlap.bounds
        for polygon in polygons:candidates.extend(Point(q) for q in polygon.exterior.coords)
        for x in np.arange(west,east+.5,.5):
            for z in np.arange(south,north+.5,.5):
                point=Point(x,z)
                if overlap.covers(point):candidates.append(point)
        if not candidates:return None
        upper_stations=np.array([upper['line'].project(p) for p in candidates]);lower_stations=np.array([lower['line'].project(p) for p in candidates])
        footprint_samples[key]=(upper_stations,lower_stations)
        return float(np.min(np.interp(upper_stations,upper['stations'],upper['heights'])-DECK_DEPTH-np.interp(lower_stations,lower['stations'],lower['heights'])))

    # Width-aware final pass catches a sloping bridge's low corner, rather than
    # accepting only its centreline intersection. Raise and blend the upper route.
    for _ in range(2):
        for c in crossings:
            upper,lower=by_id[c['upper']],by_id[c['lower']]
            minimum=footprint_minimum(upper,lower)
            if minimum is None:continue
            lift=c['required']+.04-minimum
            if lift<=0:continue
            upper['heights']+=lift
            for endpoint in [0,-1]:
                q=upper['xy'][endpoint]
                for neighbour in features:
                    if neighbour is upper or neighbour['category']!=upper['category']:continue
                    distances=np.linalg.norm(neighbour['xy']-q,axis=1);index=int(np.argmin(distances))
                    if distances[index]>.01:continue
                    if source_topology.sample_identity(neighbour,index)!=source_topology.sample_identity(upper,endpoint):continue
                    delta=upper['heights'][endpoint]-neighbour['heights'][index]
                    distance=np.abs(neighbour['stations']-neighbour['stations'][index]);radius=150. if neighbour['category']=='rail' else 180.
                    weight=np.where(distance<radius,.5+.5*np.cos(np.pi*np.minimum(distance/radius,1.)),0.)
                    neighbour['heights']+=delta*weight
            adjustments.append(dict(upper=upper['id'],lower=lower['id'],fullWidthLiftMetres=lift))
    # Simultaneous node corrections prevent a later bridge approach adjustment
    # leaving a step at a nearby side-road junction. Interpolate one correction
    # field per feature so one constraint cannot undo another shared-node value.
    path_junction_fits={}
    road_junction_fits={}
    def stitch():
        report_progress()
        targets={}
        for entries in groups.values():
            if len({f['id'] for f,i in entries})<2:continue
            first=entries[0][0]['heights'][entries[0][1]]
            if all(f['heights'][index]==first for f,index in entries):continue
            target=joined_height(entries)
            for f,index in entries:targets.setdefault(f['id'],{})[index]=target
        for f in features:
            apply_junction_targets(f,targets.get(f['id']))
        for fit in gradient_planning.fit_path_road_junctions(features):
            previous=path_junction_fits.get(fit['path'])
            if previous is None or fit['maximumCorrectionMetres']>previous['maximumCorrectionMetres']:
                path_junction_fits[fit['path']]=fit
        # Clearance/tunnel iterations call stitch again after raising a shared
        # structure. Preserve the complete pavement overlap each time, rather
        # than letting endpoint tapering undo the earlier merge fit.
        for fit in gradient_planning.conform_road_junctions(features):
            previous=road_junction_fits.get(fit['road'])
            if previous is None or fit['maximumCorrectionMetres']>previous['maximumCorrectionMetres']:
                road_junction_fits[fit['road']]=fit
    stitch()
    for iteration in range(2):
        report_progress("Smoothing junction transitions",iteration,2)
        changed=False
        for c in crossings:
            upper,lower=by_id[c['upper']],by_id[c['lower']]
            minimum=footprint_minimum(upper,lower)
            if minimum is not None and minimum<c['required']+.01:
                upper['heights']+=c['required']+.08-minimum;changed=True
        if not changed:break
        stitch()
    gradient_planning.finish(features,crossings,footprint_minimum,stitch,max_iterations=2)
    tunnel_crossings=tunnel_profiles.constraints(features)
    for cover_iteration in range(2):
        report_progress("Checking tunnel and bridge clearance",cover_iteration,2)
        if not tunnel_profiles.fit_cover(features,tunnel_crossings):break
        stitch()
        gradient_planning.finish(features,crossings,footprint_minimum,stitch,filter_profiles=False,max_iterations=2)
    junction_fits=gradient_planning.conform_road_junctions(features)
    path_fits=gradient_planning.conform_at_grade_paths(features)
    if path_fits or junction_fits:
        # Recheck any bridge/tunnel envelope touched by the short walking-path
        # approach correction. Carriageway profiles remain authoritative.
        gradient_planning.finish(features,crossings,footprint_minimum,stitch,filter_profiles=False,max_iterations=2)
        for cover_iteration in range(2):
            if not tunnel_profiles.fit_cover(features,tunnel_crossings):break
            stitch()
            gradient_planning.finish(features,crossings,footprint_minimum,stitch,filter_profiles=False,max_iterations=2)
    # Short heuristic warm-up supplies targets; the final constrained solver
    # and independent census, rather than repeated mutually undoing lifts,
    # remain the authority for every clearance/grade/curve requirement.
    report_progress("Fitting road gradients and clearances",0,0)
    grade_fit=profile_safety.road_grades(features,crossings,footprint_samples,DECK_DEPTH)
    features.profile_constraints = (crossings, footprint_samples)
    water_fit=profile_safety.waterways(features,elevation)
    # Exact final clearance census; intersections are independently recomputed above.
    clearance=[]
    for c in crossings:
        upper,lower=by_id[c['upper']],by_id[c['lower']]
        gap=float(np.interp(c['upperStation'],upper['stations'],upper['heights'])-DECK_DEPTH-np.interp(c['lowerStation'],lower['stations'],lower['heights']))
        minimum=footprint_minimum(upper,lower)
        clearance.append(dict(c,clearance=gap,minimumAcrossWidths=minimum,passes=gap>=c['required']-.03 and (minimum is None or minimum>=c['required']-.03)))

    report_progress("Sampling terrain",0,0)
    grid_x=np.arange(BOUNDS[0],BOUNDS[2]+GRID_STEP*.5,GRID_STEP)
    grid_z=np.arange(BOUNDS[1],BOUNDS[3]+GRID_STEP*.5,GRID_STEP)
    gx,gz=np.meshgrid(grid_x,grid_z)
    terrain=elevation.sample(np.c_[gx.ravel(),gz.ravel()])
    transport_metadata.controls(source,features,transform,ORIGIN)
    serial=[]
    for f in features:
        f['speedLimitMps'],f['speedLimitSource']=speed_limit(f['tags'],f['category'],f.get('travelWidth',f['width']))
        f.update(surface_audio_metadata(f['tags']))
        for direction in ('Forward','Backward'):
            key='maxspeed:'+direction.lower()
            if f['tags'].get('oneway')=='-1':key='maxspeed:'+('backward' if direction=='Forward' else 'forward')
            tags=dict(f['tags'])
            if key in tags:tags['maxspeed']=tags[key]
            f['speedLimit'+direction+'Mps']=speed_limit(tags,f['category'],f.get('travelWidth',f['width']))[0]
        serial.append({k:f[k] for k in ('id','sourceId','name','ref','kind','category','width','widthSource','bridge','tunnel','tunnelClearance','layer','oneway','onewaySource','roundabout','speedLimitMps','speedLimitSource')}|dict(points=[dict(x=round(float(q[0]),4),y=round(float(h),6),z=round(float(q[1]),4)) for q,h in zip(f['xy'],f['heights'])]))
    for feature,serialized in zip(features,serial):
        for key in ['speedLimitForwardMps','speedLimitBackwardMps','location','depthSource','mappedElevation','controls','turnLanes','turnLanesBackward','electrified','voltage','frequency','contactLineType','railClearanceHeight','railClearanceWidth','railClearanceSource','loadingGauge','gauge','travelWidth','shoulderLeft','shoulderRight','roadCentreOffset','laneCount','shoulderSource','laneAccess','laneAccessConditional','hasVariableLane','laneControl','trafficSide','tunnelClearanceSource','sourceNodeIds','level','tunnelKind','galleryOpenLeft','galleryOpenRight','surfacePriority','junctions','surface','smoothness']:
            if key in feature:serialized[key]=feature[key]
    route_points=[]
    for edge in ROUTE_PLAN['edges']:
        start=np.asarray(edge['start'])-ORIGIN;end=np.asarray(edge['end'])-ORIGIN
        candidates=[f for f in features if f['sourceId']==edge['way'] and f['line'].distance(Point(start))<.01 and f['line'].distance(Point(end))<.01]
        assert len(candidates)==1,('Unbound route edge',edge)
        f=candidates[0];a=f['line'].project(Point(start));b=f['line'].project(Point(end))
        if abs(a-b)>np.linalg.norm(end-start)+.1:
            if a<.001 and b>f['line'].length*.5:a=f['line'].length
            elif b<.001 and a>f['line'].length*.5:b=f['line'].length
            else:raise ValueError('Ambiguous source route projection')
        stations=np.r_[min(a,b),f['stations'][(f['stations']>min(a,b)+1e-5)&(f['stations']<max(a,b)-1e-5)],max(a,b)]
        if a>b:stations=stations[::-1]
        points=[]
        for s in stations:
            x,z=f['line'].interpolate(float(s)).coords[0];y=np.interp(s,f['stations'],f['heights'])
            points.append(dict(x=round(x,4),y=round(float(y),4),z=round(z,4)))
        if route_points:
            assert math.dist([route_points[-1][k] for k in ('x','y','z')],[points[0][k] for k in ('x','y','z')])<.01,('Route seam',edge)
        route_points.extend(points if not route_points else points[1:])
    routes=[dict(name=ROUTE_PLAN['name'],points=route_points)]
    elevation.height_origin=HEIGHT_ORIGIN
    water_surfaces=osm_areas.triangulated_water(mapped_water,elevation,features,terrain,gx,gz)
    model=dict(waterAreas=water_surfaces,renderedAreaSchema=2,places=places,format='NewbridgeStreamingPrototype1',originEasting=ORIGIN[0],originNorthing=ORIGIN[1],heightOrigin=HEIGHT_ORIGIN,minX=BOUNDS[0],minZ=BOUNDS[1],maxX=BOUNDS[2],maxZ=BOUNDS[3],gridStep=GRID_STEP,nx=len(grid_x),nz=len(grid_z),heights=np.round(terrain,3).tolist(),features=serial,buildings=buildings,routes=routes,attribution='© OpenStreetMap contributors (ODbL); Welsh Government / NRW LiDAR (OGL). Approximate private test map; LiDAR vertical reference unverified.')
    scenery_started=time.perf_counter()
    report_progress("Preparing tree placements",0,0)
    trees,tree_report=canopy.derive(elevation,BOUNDS,features,buildings,woodland,water_areas)
    model['railwayBuffers'],model['railwayPlatforms'],canopy_roofs=railway_furniture.derive(source,features,transform,ORIGIN,BOUNDS,COUNTRY_CODE,osm_layer)
    model['buildings']=[b for b in model['buildings'] if b['id'] not in canopy_roofs]
    model['trees']=trees
    tree_report['seconds']=time.perf_counter()-scenery_started
    report=dict(loadRasterSeconds=load_seconds,profileExtractionSeconds=profiles_seconds,profileConstraintsAndTerrainSeconds=time.perf_counter()-started-load_seconds-profiles_seconds,features={k:sum(f['category']==k for f in features) for k in ['road','path','rail','water']},buildings=len(buildings),clearances=clearance,adjustments=adjustments,sourcePins=[dict(file=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted((ROOT/'Sources').iterdir()) if p.suffix in ('.json','.tif')],automaticBridgeRecovery=bridge_recovery,scope='Numeric profile/terrain derivation only; meshes constructed and streamed in Unity at runtime. Widths are mapped-lane/class approximations. Private bounded prototype; no survey or seamless-world claim.')
    report['tunnelCoverChecks']=tunnel_profiles.cover_report(features,tunnel_crossings)
    report['stackedDeckRecoveries']=stacked_recoveries
    report['stackedBridgeCrossings']=sum(by_id[c['lower']]['bridge'] for c in crossings)
    report['atGradePathFits']=path_fits
    report['roadJunctionFits']=list(road_junction_fits.values())
    report['pathRoadJunctionFits']=list(path_junction_fits.values())
    report['canopy']=tree_report
    report['finalRoadFit']=grade_fit
    return model,report,features


def plan_image(features,path):
    scale=min(.9,1800/(BOUNDS[2]-BOUNDS[0]))
    image=Image.new('RGB',(int((BOUNDS[2]-BOUNDS[0])*scale),int((BOUNDS[3]-BOUNDS[1])*scale)),(225,231,218))
    draw=ImageDraw.Draw(image)
    colours=dict(road='#62676b',path='#d0a67e',rail='#48372e',water='#5a9dbb')
    def pixel(q): return ((q[0]-BOUNDS[0])*scale,(BOUNDS[3]-q[1])*scale)
    for f in sorted(features,key=lambda f:(f['bridge'],f['layer'])):
        pts=[pixel(q) for q in f['xy']]
        draw.line(pts,fill=colours[f['category']],width=max(2,round(f['width']*scale)))
        if f['bridge']:draw.line(pts,fill='#d5b654',width=2)
        if f['ref'] or f['name']=='Bridge Street':
            mid=pts[len(pts)//2]; draw.text(mid,f['ref'] or f['name'],fill='#1b222b')
    image.save(path)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=ROOT/'Generated');args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    start=time.perf_counter();model,report,features=derive()
    output=args.output/'newbridge.json';output.write_text(json.dumps(model,separators=(',',':')))
    report['totalGenerationSeconds']=time.perf_counter()-start
    report['outputBytes']=output.stat().st_size;report['outputSha256']=hashlib.sha256(output.read_bytes()).hexdigest()
    (args.output/'generation.json').write_text(json.dumps(report,indent=2)+'\n')
    plan_image(features,args.output/'plan.png')
    print(json.dumps({k:v for k,v in report.items() if k not in ['clearances','sourcePins','adjustments']},indent=2))
    print('Clearances:',len(report['clearances']),'failures:',sum(not c['passes'] for c in report['clearances']))



def surface_audio_metadata(tags):
    """Acoustic material/condition only; absent tags never invent road age."""
    return {key: (tags[key].strip().lower()[:64] if isinstance(tags.get(key), str) else "")
            for key in ("surface", "smoothness")}


if __name__=='__main__':main()

