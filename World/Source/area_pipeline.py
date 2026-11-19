"""Prepare one reusable 1,536 m core with a 256 m source/context halo.

The worker owns all mutations. Public input files are immutable cache entries;
prepared JSON is committed atomically only after finite-data/clearance/seam
checks. BNG X/Z metres share the first prototype's origin (321000,197000).
This module never starts the game or reads/writes careers/preferences.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import hashlib
import io
import heapq
import importlib.util
import json
import math
import ssl
import time
import urllib.parse
import urllib.request
import zipfile
import certifi
import os
import shutil
import errno
if os.name == 'nt':
    import msvcrt
else:
    import fcntl
import numpy as np
from pyproj import Transformer
from shapely.geometry import LineString, Point, box
from region_frame import RegionFrame,british_grid_tile
import elevation_provider

ROOT=Path(__file__).resolve().parent
CORE_METRES=1536
HALO_METRES=768
ORIGIN=np.array([321000.,197000.])
DOWNLOAD_LIMIT=32*1024*1024
CACHE_FORMAT='LiveWorldArea1'
OSM_MAX_AGE_SECONDS=30*24*60*60
PREPARED_CACHE_LIMIT_BYTES=512*1024*1024
RAW_CACHE_LIMIT_BYTES=1024*1024*1024


def link_cached_source(target, source):
    """Reference public cache inputs without Windows symlink privileges.

    Work and Raw share a cache volume. NTFS hard links need no elevation;
    copying is a fallback for other filesystems. Work references are released
    in prepare's finally block, so they cannot evade the raw-cache disk budget.
    """
    if os.name == 'nt':
        try:
            os.link(source, target)
        except OSError:
            shutil.copy2(source, target)
    else:
        target.symlink_to(source.resolve())


def clear_work_sources(directory):
    if directory.exists():
        for reference in directory.iterdir():
            if reference.is_file() or reference.is_symlink():
                reference.unlink()


def acquire_cache_lease(lease):
    """Hold an OS-released cross-process lease on Mac or Windows.

    Windows locks the first byte; ensure it exists before retrying contention.
    Closing the owning file releases either platform's lock, including crashes.
    """
    if os.name != 'nt':
        fcntl.flock(lease.fileno(), fcntl.LOCK_EX)
        return
    lease.seek(0, 2)
    if lease.tell() == 0:
        lease.write(b'\0')
        lease.flush()
    while True:
        lease.seek(0)
        try:
            msvcrt.locking(lease.fileno(), msvcrt.LK_NBLCK, 1)
            return
        except OSError as error:
            if error.errno not in (errno.EACCES, errno.EAGAIN, errno.EDEADLK):
                raise
            time.sleep(.1)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def atomic_json(path,value):
    temporary=path.with_suffix(path.suffix+'.partial')
    temporary.write_text(json.dumps(value,separators=(',',':')))
    temporary.replace(path)


def generator_identity(frame=None):
    digest=hashlib.sha256()
    for name in ['area_pipeline.py','build_map.py','canopy.py','gradient_planning.py','profile_safety.py','tunnel_profiles.py','road_layout.py','source_topology.py','transport_metadata.py','elevation_provider.py','coarse_provider.py','region_frame.py','osm_areas.py','Sources/welsh-lidar-catalogue-bbox.geojson']:
        digest.update((ROOT/name).read_bytes())
    digest.update(json.dumps((frame or RegionFrame.load()).identity(),sort_keys=True).encode())
    return digest.hexdigest()


def incomplete_osm_geometry(parsed,geographic_bounds=None):
    """Reject missing in-region nodes; ignore demonstrably remote extract ways.

    Some regional extracts return incomplete ways wholly outside the requested
    box. Their complete published bounds prove they cannot affect this core.
    A missing/overlapping bound does not establish safety and triggers fallback.
    Bounds use Overpass order: south, west, north, east, in degrees.
    """
    failures=[]
    for element in parsed.get('elements',[]):
        if not any(point is None for point in element.get('geometry',[])):continue
        tags=element.get('tags',{})
        if not (tags.get('highway') or tags.get('railway')) and (tags.get('landuse')=='forest' or tags.get('natural') in ('wood','water')):continue
        bounds=element.get('bounds')
        if bounds and geographic_bounds:
            south,west,north,east=geographic_bounds
            if bounds['maxlat']<south or bounds['minlat']>north or bounds['maxlon']<west or bounds['minlon']>east:continue
        failures.append(element.get('id'))
    return failures


def fetch(path,url,event,body=None,geographic_bounds=None,max_bytes=DOWNLOAD_LIMIT,content_type=None,allow_empty_osm=False,download_timeout=35,city_hint=None):
    """Bounded TLS download; cache hits never open a network connection."""
    started=time.perf_counter()
    receipt_path=path.with_suffix(path.suffix+'.receipt.json')
    body_identity=hashlib.sha256(body).hexdigest() if body is not None else None
    cached=path.exists() and receipt_path.exists()
    if cached:
        previous=json.loads(receipt_path.read_text())
        cached=previous['url']==url and previous['sha256']==sha(path) and previous.get('requestBodySha256')==body_identity
        if path.suffix in ('.json','.geojson') and time.time()-previous['fetchedUTC']>OSM_MAX_AGE_SECONDS:
            cached=False
        if cached and path.suffix=='.json':
            # Regional extracts can retain a way whose geometry contains null
            # nodes outside the extract. Never feed those incomplete polylines
            # into the topology/profile builder, including from an old cache.
            parsed=json.loads(path.read_bytes())
            cached=not incomplete_osm_geometry(parsed,geographic_bounds)
        if cached and path.suffix=='.zip':
            cached=zipfile.is_zipfile(path)
    if not cached:
        headers={'User-Agent':'EarthUnscripted-private-live-area-proof/1.0'}
        if content_type:headers['Content-Type']=content_type
        request=urllib.request.Request(url,data=body,headers=headers)
        with urllib.request.urlopen(request,timeout=download_timeout,context=ssl.create_default_context(cafile=certifi.where())) as response:
            data=response.read(max_bytes+1)
        if len(data)>max_bytes:
            raise ValueError('Source exceeds configured bounded response size')
        if path.suffix=='.zip' and not zipfile.is_zipfile(io.BytesIO(data)):
            raise ValueError('Elevation service returned an incomplete ZIP archive')
        if path.suffix=='.geojson':
            parsed=json.loads(data)
            if parsed.get('type')!='FeatureCollection' or not isinstance(parsed.get('features'),list):raise ValueError('Invalid geographic source response')
        if path.suffix=='.tif' and data[:4] not in (b'II*\x00',b'MM\x00*',b'II+\x00',b'MM\x00+'):
            raise ValueError('Elevation service did not return a TIFF')
        if path.suffix=='.json':
            parsed=json.loads(data)
            if not isinstance(parsed.get('elements'),list) or (not parsed['elements'] and not allow_empty_osm) or parsed.get('remark'):
                raise ValueError('Incomplete OSM response: '+str(parsed.get('remark')))
            incomplete=incomplete_osm_geometry(parsed,geographic_bounds)
            if incomplete:raise ValueError('Incomplete OSM geometry; retry another endpoint: '+str(incomplete[:8]))
        temporary=path.with_suffix(path.suffix+'.partial')
        temporary.write_bytes(data)
        temporary.replace(path)
        atomic_json(receipt_path,dict(url=url,sha256=sha(path),fetchedUTC=time.time(),requestBodySha256=body_identity))
    path.touch();receipt_path.touch()
    result=dict(file=path.name,url=url,requestBodySha256=body_identity,bytes=path.stat().st_size,sha256=sha(path),cached=cached,seconds=time.perf_counter()-started)
    event('source',**result)
    return result


def core_bounds(tile):
    x,z=tile
    return [x*CORE_METRES,z*CORE_METRES,(x+1)*CORE_METRES,(z+1)*CORE_METRES]


def portal_contexts(source,context,frame):
    """Request small measured windows around real, out-of-core tunnel portals.

    A clipped tunnel edge is inside the hill. Sampling that edge as a portal
    gives a false steep ascent. Complete OSM way geometry supplies the real
    endpoints; only their 48 m approach windows need extra measured rasters.
    These windows are numeric planning context, not prebuilt route/world cores.
    """
    transform=Transformer.from_crs(4326,frame.world_projection,always_xy=True)
    envelope=box(*context);result=[];seen=set()
    for element in source.get('elements',[]):
        tags=element.get('tags',{});geometry=element.get('geometry',[])
        if tags.get('tunnel','no') in ('no','false','0') or not (tags.get('highway') or tags.get('railway')):continue
        if len(geometry)<2 or any(point is None for point in geometry):continue
        points=np.array([transform.transform(point['lon'],point['lat']) for point in geometry])-frame.world_origin
        if not LineString(points).intersects(envelope):continue
        for point in points[[0,-1]]:
            window=box(point[0]-48,point[1]-48,point[0]+48,point[1]+48)
            if envelope.covers(window):continue
            key=tuple(np.round(point,3))
            if key in seen:continue
            seen.add(key);result.append(list(window.bounds))
    return result


def anchor_neighbours(model,neighbours):
    """Preserve committed boundary profiles; fade corrections over 96 m.

    All categories use the same spatial weight. Interpolation uses a matching
    source-way identity, never a nearby separate carriageway or crossing route.
    Old context extends past its core, supplying an independent height anchor
    on both sides of the seam. Terrain raw heights use the same global grid.
    """
    corrections=0
    references={}
    for old in neighbours:
        old_core=box(old['coreMinX'],old['coreMinZ'],old['coreMaxX'],old['coreMaxZ'])
        for feature in old['features']:
            points=np.array([[p['x'],p['z']] for p in feature['points']])
            line=LineString(points)
            stations=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(points,axis=0),axis=1))]
            heights=np.array([p['y'] for p in feature['points']])
            references.setdefault(feature['sourceId'],[]).append((old_core,line,stations,heights))
    for feature in model['features']:
        candidates=references.get(feature['sourceId'],[])
        if not candidates:continue
        for point in feature['points']:
            location=Point(point['x'],point['z']);anchors=[]
            for old_core,line,stations,heights in candidates:
                distance=old_core.distance(location)
                if distance>=96 or line.distance(location)>.01:continue
                target=float(np.interp(line.project(location),stations,heights))
                anchors.append((distance,target))
            if not anchors:continue
            minimum=min(row[0] for row in anchors)
            exact=[target for distance,target in anchors if distance<.0001]
            if exact:target=float(np.median(exact));strength=1.
            else:
                weights=[1/max(.001,distance)**4 for distance,target in anchors]
                target=sum(weight*row[1] for weight,row in zip(weights,anchors))/sum(weights)
                strength=.5+.5*math.cos(math.pi*minimum/96)
            point['y']=round(point['y']+(target-point['y'])*strength,4)
            corrections+=1
    return corrections


def published_road_anchors(features, neighbours, model):
    """Pin interpolated shared edges during the final fit.

    The 96 m neighbour blend is only an initial guess. It must not be allowed
    to introduce a new steep ramp or tear apart a junction after grade fitting.
    Only the new core is published. Its old-core halo remains planning
    context, so pinning that entire halo would overconstrain a fresh fit.
    """
    anchors = []
    new_core = box(model['coreMinX'], model['coreMinZ'], model['coreMaxX'], model['coreMaxZ'])
    for old in neighbours:
        core = box(old['coreMinX'], old['coreMinZ'], old['coreMaxX'], old['coreMaxZ'])
        edge = new_core.boundary.intersection(core.boundary)
        references = {}
        for previous in old['features']:
            if previous['category'] not in ('road','rail','path','water'):
                continue
            xy = np.array([[p['x'], p['z']] for p in previous['points']])
            stations = np.r_[0., np.cumsum(np.linalg.norm(np.diff(xy, axis=0), axis=1))]
            references.setdefault(previous['sourceId'], []).append((LineString(xy), stations, [p['y'] for p in previous['points']]))
        for feature in features:
            if feature['category'] not in ('road','rail','path','water'):
                continue
            samples = []
            intersection = feature['line'].intersection(edge)
            locations = [intersection] if intersection.geom_type == 'Point' else [p for p in getattr(intersection, 'geoms', []) if p.geom_type == 'Point']
            samples += [(feature['line'].project(point), point) for point in locations]
            for station, point in samples:
                for line, old_stations, heights in references.get(feature['sourceId'], []):
                    if line.distance(point) < .015:
                        anchors.append((feature['id'], station, float(np.interp(line.project(point), old_stations, heights))))
    return anchors


def railway_context_anchors(features, neighbours, model):
    """Plan connected rails against owned neighbour profiles before publishing.

    Shared-edge pins alone miss a railway bending through a diagonal neighbour.
    Sparse samples in its actual core carry that grade into the planning halo;
    an old halo is never treated as surveyed or committed geometry.
    """
    anchors=[]
    new_core=box(model['coreMinX'],model['coreMinZ'],model['coreMaxX'],model['coreMaxZ'])
    for old in neighbours:
        core=box(old['coreMinX'],old['coreMinZ'],old['coreMaxX'],old['coreMaxZ'])
        references={}
        for previous in old['features']:
            if previous['category']!='rail':continue
            xy=np.array([[p['x'],p['z']] for p in previous['points']])
            references.setdefault(previous['sourceId'],[]).append((LineString(xy),np.r_[0.,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))],[p['y'] for p in previous['points']]))
        for feature in features:
            if feature['category']!='rail' or not feature['line'].intersects(new_core):continue
            for station in np.arange(feature['stations'][0],feature['stations'][-1]+.001,32.):
                point=feature['line'].interpolate(float(station))
                if not core.covers(point):continue
                for line,stations,heights in references.get(feature['sourceId'],[]):
                    if line.distance(point)<.015:
                        anchors.append((feature['id'],float(station),float(np.interp(line.project(point),stations,heights))))
    return anchors


def fit_neighbour_profiles(features, neighbours, model, fit, protected_tiles=None):
    """Reconcile reusable proposals without moving a delivered area's seam.

    Cached profiles that have never been delivered to this player are reusable
    proposals, not additional immutable boundaries. If their combined heights
    are impossible, refit against the delivered areas and retire incompatible
    proposals only after independent final validation. Unknown ownership is
    conservative: no cached boundary may be discarded.
    """
    from profile_safety import ProfileFitInfeasible
    def attempt(references):
        edges=published_road_anchors(features,references,model)
        planning=railway_context_anchors(features,references,model)
        if planning:
            try:return fit(edges+planning),edges
            except ProfileFitInfeasible:pass
        return fit(edges),edges
    try:
        result,anchors=attempt(neighbours)
        return result,anchors,neighbours,[]
    except ProfileFitInfeasible:
        if protected_tiles is None:raise
        committed=[old for old in neighbours if (old['tileX'],old['tileZ']) in protected_tiles]
        proposals=[old for old in neighbours if (old['tileX'],old['tileZ']) not in protected_tiles]
        if not proposals:raise
        result,anchors=attempt(committed)
        return result,anchors,committed,proposals


def anchor_unfitted_profiles(features, anchors):
    """Retain committed water/path edges after their final independent fit.

    Road/rail constraints are solved by road_grades. Waterways are fitted later
    from local low-channel samples, which otherwise overwrite neighbour anchors.
    Smooth compact corrections preserve the exact interpolated edge height;
    changing the seam tolerance would only hide the resulting visible step.
    """
    by_id={feature['id']:feature for feature in features}
    grouped={}
    for identity,station,height in anchors:
        feature=by_id.get(identity)
        if feature is not None and feature['category'] in ('water','path'):
            grouped.setdefault(identity,[]).append((float(station),float(height)))
    for identity,rows in grouped.items():
        feature=by_id[identity];stations=np.asarray(feature['stations']);heights=np.asarray(feature['heights'])
        # Adjacent old cores can report the same shared corner. Keep one
        # authority per station, rejecting conflicting previously admitted data.
        unique={}
        for station,height in rows:
            key=round(station,4)
            if key in unique and abs(unique[key][1]-height)>.025:
                raise ValueError('Conflicting committed water/path seam anchors')
            unique[key]=(station,height)
        rows=list(unique.values());centres=np.array([row[0] for row in rows])
        basis=.5+.5*np.cos(np.pi*np.minimum(1,np.abs(stations[:,None]-centres[None,:])/96.))
        matrix=np.array([[np.interp(station,stations,basis[:,column]) for column in range(len(rows))] for station,_ in rows])
        delta=np.array([height-np.interp(station,stations,heights) for station,height in rows])
        coefficients=np.linalg.lstsq(matrix,delta,rcond=None)[0]
        fitted=heights+basis@coefficients
        if max(abs(np.interp(station,stations,fitted)-height) for station,height in rows)>.002:
            raise ValueError('Water/path seam constraints could not be reconciled')
        feature['heights']=fitted
    return len(grouped)


def check_seams(model,neighbours):
    """Independently intersect each shared core edge and compare profile heights."""
    maximum=0.;samples=0
    new_core=box(model['coreMinX'],model['coreMinZ'],model['coreMaxX'],model['coreMaxZ'])
    for old in neighbours:
        old_core=box(old['coreMinX'],old['coreMinZ'],old['coreMaxX'],old['coreMaxZ'])
        edge=new_core.boundary.intersection(old_core.boundary)
        if edge.is_empty or edge.length<1:continue
        old_features={}
        for feature in old['features']:
            old_features.setdefault(feature['sourceId'],[]).append(feature)
        for feature in model['features']:
            points=np.array([[p['x'],p['z']] for p in feature['points']]);line=LineString(points)
            intersection=line.intersection(edge)
            locations=[intersection] if intersection.geom_type=='Point' else [p for p in getattr(intersection,'geoms',[]) if p.geom_type=='Point']
            stations=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(points,axis=0),axis=1))]
            for location in locations:
                for previous in old_features.get(feature['sourceId'],[]):
                    old_points=np.array([[p['x'],p['z']] for p in previous['points']]);old_line=LineString(old_points)
                    if old_line.distance(location)>.01:continue
                    old_stations=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(old_points,axis=0),axis=1))]
                    before=float(np.interp(old_line.project(location),old_stations,[p['y'] for p in previous['points']]))
                    after=float(np.interp(line.project(location),stations,[p['y'] for p in feature['points']]))
                    maximum=max(maximum,abs(before-after));samples+=1
    if maximum>.025:
        raise ValueError(f'Area seam mismatch {maximum:.4f} m')
    return dict(samples=samples,maximumHeightGapMetres=maximum)




class Pipeline:
    def __init__(self,cache,event,frame=None):
        self.cache=cache;self.event=event;self.frame=frame or RegionFrame.load()
        self.protected_tiles=None  # area_server supplies delivered/resident ownership
        self.version=generator_identity(self.frame)
        self.raw=cache/'Raw';self.prepared=cache/'Prepared'/self.frame.identifier/self.version[:16];self.work=cache/'Work'/self.frame.identifier/self.version[:16]
        for folder in [self.raw,self.prepared,self.work]:folder.mkdir(parents=True,exist_ok=True)
        compatibility=json.loads((ROOT/'compatible-generators.json').read_text())
        self.compatible={self.version}
        catalogue=json.loads((ROOT/'Sources/welsh-lidar-catalogue-bbox.geojson').read_text())
        self.catalogue={row['properties']['british_gr']:row['properties'] for row in catalogue['features']}
        specification=importlib.util.spec_from_file_location('live_numeric_builder',ROOT/'build_map.py')
        self.builder=importlib.util.module_from_spec(specification);specification.loader.exec_module(self.builder)
        # JSON may encode whole-metre origins as integers. Keep source-frame
        # arithmetic floating point so NumPy integer scalars cannot leak into
        # the committed JSON model (the regional integer-origin preflight
        # exposed this even though the Welsh default used float literals).
        self.builder.PROGRESS=lambda stage,completed,total:self.event('preparation-progress',stage=stage,completed=completed,total=total)
        self.builder.ORIGIN=np.asarray(self.frame.world_origin,dtype=float)
        self.builder.HEIGHT_ORIGIN=self.frame.height_origin
        self.builder.FRAME_CRS=self.frame.epsg
        self.builder.WORLD_CRS=self.frame.world_crs
        self.builder.TRAFFIC_SIDE=self.frame.traffic_side
        self.builder.COUNTRY_CODE=self.frame.country_code or ('GB' if self.frame.provider in ('britain','welsh') else 'CH' if self.frame.provider in ('switzerland','swiss-or-coarse') else '')

    def prepare(self,tile):
        # Separate test/worker processes may share this persistent cache. Hold
        # an OS-released lease through acquisition, derivation and pruning so
        # another worker cannot evict rasters still being sampled. A process
        # crash releases the lease automatically; prepared hits use it too.
        with (self.cache/'prepare.lock').open('a+b') as lease:
            acquire_cache_lease(lease)
            try:
                return self._prepare_locked(tile)
            finally:
                clear_work_sources(self.work / f"{tile[0]}_{tile[1]}" / "Sources")

    def _neighbour_models(self,bounds,allowed=None):
        neighbours=[]
        for previous_path in sorted(self.prepared.glob('*.manifest.json')):
            previous=json.loads(previous_path.read_text())
            tile=tuple(previous['tile'])
            if allowed is not None and tile not in allowed:continue
            if box(*bounds).distance(box(*core_bounds(tile)))>=.01:continue
            if previous['generator'] not in self.compatible:continue
            old_file=self.prepared/previous['file']
            if not old_file.exists() or sha(old_file)!=previous['sha256']:continue
            neighbours.append(json.loads(old_file.read_text()))
        return neighbours

    def _prepare_locked(self,tile):
        started=time.perf_counter();name=f'{tile[0]}_{tile[1]}'
        self.event('preparation-progress',stage='Checking prepared cache',completed=0,total=0)
        output=self.prepared/(name+'.json');manifest_path=self.prepared/(name+'.manifest.json')
        if output.exists() and manifest_path.exists():
            manifest=json.loads(manifest_path.read_text())
            if manifest['generator'] in self.compatible and manifest['sha256']==sha(output) and time.time()-manifest['createdUTC']<OSM_MAX_AGE_SECONDS:
                model=json.loads(output.read_text())
                try:
                    if self.protected_tiles is not None:
                        check_seams(model,self._neighbour_models(core_bounds(tile),self.protected_tiles))
                except ValueError:
                    self.event('prepared-cache-seam-rebuild',tile=name)
                else:
                    output.touch()
                    self.event('prepared-cache-hit',tile=name,sha256=manifest['sha256'])
                    return model,manifest,True
        self.event('preparation-progress',stage='Fetching map and elevation sources',completed=0,total=0)
        bounds=core_bounds(tile);context=[bounds[0]-HALO_METRES,bounds[1]-HALO_METRES,bounds[2]+HALO_METRES,bounds[3]+HALO_METRES]
        absolute=np.asarray(context)+np.tile(self.frame.world_origin,2)
        inverse=Transformer.from_crs(self.frame.world_projection,4326,always_xy=True)
        corners=[inverse.transform(x,z) for x in [absolute[0]-10,absolute[2]+10] for z in [absolute[1]-10,absolute[3]+10]]
        bbox=','.join(str(value) for value in [min(p[1] for p in corners),min(p[0] for p in corners),max(p[1] for p in corners),max(p[0] for p in corners)])
        query='[out:json][timeout:25];('+''.join(f'way[{tag}]({bbox});' for tag in ['highway','railway','public_transport=platform','waterway','building','landuse=forest','natural=wood','natural=water'])+''.join(f'relation[{tag}]({bbox});' for tag in ['building','railway=platform','public_transport=platform','natural=water','waterway=riverbank','natural=wood','landuse=forest'])+f'node[railway~"^(buffer_stop|station|halt)$"]({bbox});node[place~"^(city|town|village|hamlet|suburb|neighbourhood)$"]({bbox}););out body geom;'
        osm=self.raw/(self.frame.identifier+'-'+name+f'-osm-areas-v2-h{HALO_METRES}.json');errors=[]
        endpoints=['https://overpass.osm.ch/api/interpreter','https://overpass-api.de/api/interpreter','https://overpass.private.coffee/api/interpreter'] if self.frame.provider in ('switzerland','swiss-or-coarse') else ['https://overpass.atownsend.org.uk/api/interpreter','https://overpass.private.coffee/api/interpreter','https://overpass-api.de/api/interpreter']
        for endpoint in endpoints:
            try:
                osm_receipt=fetch(osm,endpoint,self.event,urllib.parse.urlencode({'data':query}).encode(),geographic_bounds=[float(value) for value in bbox.split(',')],city_hint=self.frame.name);break
            except Exception as exception:
                errors.append(str(exception));self.event('source-retry',tile=name,error=str(exception))
        else:raise RuntimeError(str(errors))
        contexts=[context]+portal_contexts(json.loads(osm.read_text()),context,self.frame)
        if self.frame.provider=='welsh':
            jobs=[]
            cells=set()
            for source_context in contexts:
                source_absolute=self.frame.source_bounds(source_context)
                cells.update((east,north) for east in range(math.floor(source_absolute[0]/1000),math.floor(source_absolute[2]/1000)+1) for north in range(math.floor(source_absolute[1]/1000),math.floor(source_absolute[3]/1000)+1))
            for east,north in sorted(cells):
                identity=british_grid_tile(east,north)
                if identity not in self.catalogue:
                    parameters=dict(service='WFS',version='2.0.0',request='GetFeature',typeNames='geonode:welsh_government_lidar_tile_catalogue_2020_2023',outputFormat='application/json',srsName='EPSG:27700',bbox=f'{east*1000+499},{north*1000+499},{east*1000+501},{north*1000+501},urn:ogc:def:crs:EPSG::27700')
                    catalogue_path=self.raw/(identity+'.catalogue.geojson')
                    fetch(catalogue_path,'https://datamap.gov.wales/geoserver/wfs?'+urllib.parse.urlencode(parameters),self.event)
                    rows=json.loads(catalogue_path.read_text())['features']
                    matches=[row['properties'] for row in rows if row['properties']['british_gr']==identity]
                    if len(matches)!=1:raise ValueError('No unique Welsh LiDAR tile for '+identity)
                    self.catalogue[identity]=matches[0]
                for kind in ['dtm','dsm']:
                    path=self.raw/(identity+'-'+kind+'.tif')
                    jobs.append((path,'https://'+self.catalogue[identity][kind+'_link']))
            with ThreadPoolExecutor(max_workers=4) as pool:
                receipts=list(pool.map(lambda job:fetch(*job,self.event),jobs))
        else:
            jobs=[];receipts=[]
            for source_context in contexts:
                if self.frame.provider=='britain':
                    new_jobs,new_receipts=elevation_provider.acquire_britain(source_context,self.frame,self.raw,fetch,self.event,self.catalogue)
                else:
                    new_jobs,new_receipts=elevation_provider.acquire(source_context,self.frame,self.raw,fetch,self.event)
                jobs.extend(new_jobs);receipts.extend(new_receipts)
            jobs=list({str(path):(path,url) for path,url in jobs}.values())
            receipts=list({row['file']:row for row in receipts}.values())
        # Supplement controls separately: existing large OSM way downloads and
        # terrain rasters remain reusable when adding traffic-control support.
        control_path=self.raw/(self.frame.identifier+'-'+name+f'-controls-v1-h{HALO_METRES}.json')
        control_query=f'[out:json][timeout:10];(node[highway~"^(traffic_signals|stop|give_way|crossing)$"]({bbox});node[traffic_sign]({bbox}););out body;'
        control_data={'elements':[]};control_receipts=[];control_available=False
        for endpoint in endpoints:
            try:
                receipt=fetch(control_path,endpoint,self.event,urllib.parse.urlencode({'data':control_query}).encode(),allow_empty_osm=True,download_timeout=12,max_bytes=4*1024*1024,city_hint=self.frame.name)
                control_data=json.loads(control_path.read_text());control_receipts=[receipt];control_available=True;break
            except Exception as exception:self.event('traffic-control-source-retry',tile=name,error=str(exception))
        if not control_available:self.event('traffic-controls-unavailable',tile=name,scope='Roads/terrain remain usable; mapped controls unavailable')
        acquired=time.perf_counter()
        job_root=self.work/name;source=job_root/'Sources';source.mkdir(parents=True,exist_ok=True)
        osm_link=source/'osm-area-complete.json'
        if osm_link.is_symlink():osm_link.unlink()
        if osm_link.exists():osm_link.unlink()
        combined=json.loads(osm.read_text());known={(row['type'],row['id']) for row in combined['elements']}
        combined['elements'].extend(row for row in control_data['elements'] if (row['type'],row['id']) not in known)
        osm_link.write_text(json.dumps(combined,separators=(',',':')))
        for path,url in jobs:
            link=source/path.name
            if link.is_symlink():link.unlink()
            if not link.exists():link_cached_source(link,path)
        self.builder.ROOT=job_root;self.builder.BOUNDS=tuple(context)
        self.builder.ROUTE_PLAN=dict(name='Runtime area',edges=[])
        model,report,features=self.builder.derive()
        recovery=False
        if not all(row['passes'] for row in report['clearances']):
            failures=sum(not row['passes'] for row in report['clearances'])
            self.event('automatic-recovery-started',tile=name,failedCrossings=failures,strategy='Bridge-priority joins and same-level approach exclusions')
            model,report,features=self.builder.derive(bridge_recovery=True)
            recovery=True
            if not all(row['passes'] for row in report['clearances']):
                raise ValueError('Bridge clearance remains unresolved after automatic topology recovery')
            self.event('automatic-recovery-passed',tile=name,crossings=len(report['clearances']))
        if not all(row['passes'] for row in report['tunnelCoverChecks']):
            raise ValueError('Tunnel roof cover remains unresolved after connected-approach fitting')
        # Only core terrain is retained. Profiles keep the context halo for
        # cuttings, exact source joins and neighbour anchoring, but duplicating
        # the full halo terrain would inflate every prepared cache entry.
        grid=np.asarray(model['heights']).reshape(model['nz'],model['nx'])
        offset=int(HALO_METRES/model['gridStep']);count=int(CORE_METRES/model['gridStep'])+1
        model['heights']=grid[offset:offset+count,offset:offset+count].ravel().tolist()
        model.update(minX=bounds[0],minZ=bounds[1],maxX=bounds[2],maxZ=bounds[3],nx=count,nz=count)
        # Visual canopy keeps the already sampled 768 m context halo. It can
        # appear on distant terrain before a core's drivable meshes are needed.
        # Consumers deduplicate the deterministic global 8 m cells. Terrain,
        # colliders and query authority still belong exclusively to the core.
        model.update(frame=self.frame.identity(),format='LiveWorldNumeric1',coreMinX=bounds[0],coreMinZ=bounds[1],coreMaxX=bounds[2],coreMaxZ=bounds[3],tileX=tile[0],tileZ=tile[1])
        estimated=any(path.name.startswith('00-coarse-') for path,url in jobs)
        model['elevationQuality']='survey where available; approximate background' if estimated and len(jobs)>2 else 'approximate terrain' if estimated else 'measured survey'
        notices={'welsh':'Welsh Government / NRW LiDAR (OGL)',
                 'england':'Environment Agency 2022 LiDAR (OGL)',
                 'switzerland':'swisstopo measured terrain/surface data',
                 'britain':'Welsh Government / NRW and Environment Agency surveys; Mapzen terrain background',
                 'swiss-or-coarse':'swisstopo surveys; Mapzen terrain background',
                 'coarse':'Mapzen Terrarium terrain estimate'}
        model['trafficControlsAvailable']=control_available
        model['attribution']='© OpenStreetMap contributors (ODbL); '+notices.get(self.frame.provider,self.frame.provider)
        if urllib.parse.urlsplit(osm_receipt.get('url','')).hostname=='download.bbbike.org':model['attribution']+='; city extract by BBBike.org'
        neighbours=self._neighbour_models(bounds)
        # A lake is one plane across streamed tile boundaries. Keep the first
        # admitted local-source level rather than refitting it per clipped core.
        water_levels={water.get('sourceId',water['id']):(water['level'],water.get('levelSource','neighbour')) for old in neighbours for water in old.get('waterAreas',[])}
        for water in model.get('waterAreas',[]):
            if water.get('sourceId',water['id']) not in water_levels:continue
            level,source=water_levels[water.get('sourceId',water['id'])];delta=level-water['level'];water['level']=level;water['levelSource']=source
            for point in water['vertices']+water['outline']:point['y']=level
            for ring in water.get('holes',[]):
                for point in ring['points']:point['y']=level
        self.event('preparation-progress',stage='Joining neighbouring areas',completed=0,total=0)
        correction_count=anchor_neighbours(model,neighbours)
        crossings, footprints = features.profile_constraints
        anchors=[];proposals=[]
        if neighbours:
            for feature,serialized in zip(features,model['features']):
                feature['heights']=np.array([point['y'] for point in serialized['points']])
            final_fit,anchors,neighbours,proposals=fit_neighbour_profiles(
                features,neighbours,model,
                lambda fixed:self.builder.profile_safety.road_grades(features,crossings,footprints,self.builder.DECK_DEPTH,fixed),
                self.protected_tiles)
            self.builder.profile_safety.waterways(features, self.builder.Elevation())
        else:
            # Nothing has changed the just-validated high-precision profiles.
            # Keep them for the independent census instead of rounding and
            # solving the identical city-wide problem a second time.
            final_fit = report['finalRoadFit']
        anchor_unfitted_profiles(features, anchors)
        # Validate the final published profiles, not the pre-anchor proposal.
        by_id = {feature['id']: feature for feature in features}
        for crossing in crossings:
            upper, lower = by_id[crossing['upper']], by_id[crossing['lower']]
            upper_stations, lower_stations = footprints.get((upper['id'], lower['id']), ([crossing['upperStation']], [crossing['lowerStation']]))
            gap = np.interp(upper_stations, upper['stations'], upper['heights']) - self.builder.DECK_DEPTH - np.interp(lower_stations, lower['stations'], lower['heights'])
            if np.min(gap) < crossing['required'] - .03:
                raise ValueError('Final anchored bridge clearance is unresolved')
        for feature, serialized in zip(features, model['features']):
            for point, height in zip(serialized['points'], feature['heights']):
                point['y'] = round(float(height), 6)
            for control in serialized.get('controls',[]):
                control['y']=float(np.interp(control['station'],feature['stations'],feature['heights']))
        seams=check_seams(model,neighbours)
        tunnel_checks=self.builder.tunnel_profiles.cover_report(features)
        if not all(row['passes'] for row in tunnel_checks):
            raise ValueError('Anchored tunnel roof cover is unresolved')
        # Commit only after the full grade/clearance/cover/seam census. A
        # retired proposal keeps its raw sources and numeric JSON; removing its
        # admission manifest forces automatic regeneration on the next request.
        # Delivered areas are never retired or changed by this recovery.
        retired=[]
        for old in proposals:
            try:check_seams(model,[old])
            except ValueError:
                old_tile=(old['tileX'],old['tileZ'])
                if self.protected_tiles is None or old_tile in self.protected_tiles:
                    raise ValueError('Refusing to retire a delivered area')
                old_manifest=self.prepared/f'{old_tile[0]}_{old_tile[1]}.manifest.json'
                old_manifest.unlink(missing_ok=True);retired.append(list(old_tile))
        if retired:self.event('prepared-cache-proposals-retired',tile=name,retired=retired)
        self.event('preparation-progress',stage='Saving prepared area',completed=0,total=0)
        atomic_json(output,model)
        manifest=dict(automaticRecovery=recovery,format=CACHE_FORMAT,generator=self.version,tile=list(tile),file=output.name,sha256=sha(output),bytes=output.stat().st_size,sourceReceipts=[osm_receipt]+control_receipts+receipts,trafficControlsAvailable=control_available,seconds=time.perf_counter()-started,acquisitionSeconds=acquired-started,preparationSeconds=time.perf_counter()-acquired,seams=seams,anchoredPoints=correction_count,bridgeChecks=len(report['clearances']),tunnelCoverChecks=tunnel_checks,createdUTC=time.time(),neighbours=[dict(tile=[old['tileX'],old['tileZ']]) for old in neighbours])
        manifest['dataIdentity']=hashlib.sha256(json.dumps([(row['url'],row['sha256']) for row in manifest['sourceReceipts']],sort_keys=True).encode()).hexdigest()
        manifest['finalRoadFit']=final_fit
        manifest['stackedDeckRecoveries']=report['stackedDeckRecoveries']
        manifest['stackedBridgeCrossings']=report['stackedBridgeCrossings']
        manifest['atGradePathFits']=report['atGradePathFits']
        manifest['roadJunctionFits']=report['roadJunctionFits']
        manifest['pathRoadJunctionFits']=report['pathRoadJunctionFits']
        atomic_json(manifest_path,manifest)
        self.event('prepared',tile=name,**{key:manifest[key] for key in ['sha256','bytes','seconds','acquisitionSeconds','preparationSeconds','seams','anchoredPoints']})
        self.prune()
        return model,manifest,False

    def prune(self):
        """Bound all regions/generators together; evict data with its receipt.

        Preparation holds the shared OS lease. Current outputs are the newest
        entries, and loaded Player/worker models remain owned in memory. ZIPs
        with a verified extracted raster are expendable before raster inputs;
        the raster receipt retains the original archive digest and source URL.
        """
        prepared_root=self.cache/'Prepared'
        prepared=[]
        for path in prepared_root.rglob('*.json'):
            if path.name.endswith('.manifest.json'):continue
            manifest=path.with_suffix('.manifest.json')
            prepared.append((path,[path]+([manifest] if manifest.exists() else []),1))
        for manifest in prepared_root.rglob('*.manifest.json'):
            model=manifest.with_name(manifest.name.removesuffix('.manifest.json')+'.json')
            if not model.exists():
                manifest.unlink();self.event('cache-evicted',file=str(manifest.relative_to(self.cache)))
        self._prune_groups(prepared,PREPARED_CACHE_LIMIT_BYTES)

        raw=[]
        for path in self.raw.iterdir():
            if not path.is_file() or path.name.endswith('.receipt.json'):continue
            receipt=path.with_suffix(path.suffix+'.receipt.json')
            priority=0 if path.suffix=='.zip' and path.with_suffix('.tif').exists() else 1
            raw.append((path,[path]+([receipt] if receipt.exists() else []),priority))
        for receipt in self.raw.glob('*.receipt.json'):
            original=receipt.with_name(receipt.name.removesuffix('.receipt.json'))
            if not original.exists():
                receipt.unlink();self.event('cache-evicted',file=str(receipt.relative_to(self.cache)))
        self._prune_groups(raw,RAW_CACHE_LIMIT_BYTES)

    def _prune_groups(self,groups,limit):
        """Evict complete cache entries in priority/LRU order, counting metadata."""
        total=sum(path.stat().st_size for _,paths,_ in groups for path in paths)
        for primary,paths,priority in sorted(groups,key=lambda group:(group[2],group[0].stat().st_mtime)):
            if total<=limit:break
            for path in paths:
                total-=path.stat().st_size;path.unlink()
                self.event('cache-evicted',file=str(path.relative_to(self.cache)))
