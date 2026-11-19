"""Loopback-only background data worker for the isolated live-world prototype.

The Player explicitly requests one core at a time. A single preparation worker
serializes source/cache/seam mutations; HTTP polling never blocks that worker.
Only immutable, hash-verified prepared files are served. No arbitrary URLs,
paths, code or commands are accepted from the client.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from worker_security import SessionHandler, SessionServer, JobStore, prepared_file
import argparse
import json
import math
import threading
import time
import uuid
import hashlib
import shutil
from area_pipeline import Pipeline, CORE_METRES
from live_routing import Forecast, guide
from collections import OrderedDict
from far_terrain import FarTerrain
from region_frame import RegionFrame



# Storage policy is deliberately outside the numeric generator identity: a
# retention preference must never invalidate otherwise reusable prepared areas.
def retained_forecast_model(model,cache_only=False,revision=''):
    """A distant cached area contributes roads without retaining terrain/buildings."""
    if cache_only:model=dict(tileX=model['tileX'],tileZ=model['tileZ'],features=[f for f in model['features'] if f['category']=='road'])
    return dict(model,_forecastRevision=revision)


class StoragePipeline(Pipeline):
    def _prune_groups(self, groups, limit):
        from area_pipeline import PREPARED_CACHE_LIMIT_BYTES,RAW_CACHE_LIMIT_BYTES
        raw_cache=limit==RAW_CACHE_LIMIT_BYTES
        if limit == PREPARED_CACHE_LIMIT_BYTES or raw_cache:
            requested = 5.0
            try:
                settings = json.loads((self.cache/'storage-settings.json').read_text())
                value = settings.get('sourceGigabytes' if raw_cache else 'preparedGigabytes', 5.0)
                if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value):
                    requested = min(50.0, max(0.5, value))
            except (OSError, ValueError, TypeError, AttributeError):
                pass
            used = sum(path.stat().st_size for _, paths, _ in groups for path in paths)
            # Already occupied cache space is reclaimable. Never reserve the
            # requested quota up front; keep two decimal GB free as it grows.
            free = shutil.disk_usage(self.cache).free
            newest = max(groups, key=lambda group: group[0].stat().st_mtime, default=None)
            current = sum(path.stat().st_size for path in newest[1]) if newest else 0
            # The just-published area must remain available to the Player.
            limit = max(current, min(int(requested*1_000_000_000), max(0, free+used-2_000_000_000)))
        if raw_cache:
            # Retain a PBF and its spatial indexes together. Independent LRU
            # eviction previously discarded the PBF/catalogue while leaving its
            # index, forcing another download and import on the next area.
            regional={};numeric=[]
            for representative,files,priority in groups:
                if representative.name.startswith('regional-'):
                    key=representative.name.split('.osm',1)[0]
                    regional.setdefault(key,[]).extend(files)
                else:numeric.append((representative,files,priority))
            source_groups=[]
            for files in regional.values():
                files=list(dict.fromkeys(files))
                indexes=sorted((p for p in files if p.suffix=='.sqlite'),key=lambda p:p.stat().st_mtime)
                for old_index in indexes[:-1]:
                    old_files=[p for p in files if p==old_index or p==old_index.with_suffix('.sqlite.receipt.json')]
                    source_groups.append((old_index,old_files,0));files=[p for p in files if p not in old_files]
                representative=max(files,key=lambda p:p.stat().st_mtime)
                source_groups.append((representative,files,1))
            newest=max(source_groups,key=lambda g:g[0].stat().st_mtime,default=None)
            required=sum(p.stat().st_size for p in newest[1]) if newest else 0
            source_budget=min(limit-64*1024*1024,max(limit//2,required))
            super()._prune_groups(source_groups,source_budget)
            retained=sum(p.stat().st_size for _,files,_ in source_groups for p in files if p.exists())
            return super()._prune_groups(numeric,max(0,limit-retained))
        return super()._prune_groups(groups, limit)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--cache',type=Path,required=True)
    parser.add_argument('--evidence',type=Path,required=True)
    parser.add_argument('--port',type=int,default=0)
    parser.add_argument('--start',type=Path)
    parser.add_argument('--region',type=Path)
    parser.add_argument('--destination',type=Path)
    parser.add_argument('--session',type=Path,help='Owned normal-game session configuration')
    args=parser.parse_args();args.evidence.mkdir(parents=True,exist_ok=True)
    if args.session:
        if args.region or args.start or args.destination:raise ValueError('Session and explicit case inputs are mutually exclusive')
        from session_config import prepare
        args.region,args.start,args.destination=prepare(args.session,args.evidence)
    event_lock=threading.Lock()
    worker_context=threading.local()
    job_activity={}
    job_progress={}
    submissions={};submission_lock=threading.Lock()
    cancelled_jobs=set()
    class PreparationSuperseded(BaseException):pass
    def event(kind,**fields):
        row=dict(event=kind,utcSeconds=time.time(),**fields)
        with event_lock:
            identity=getattr(worker_context,'identity',None)
            if identity in cancelled_jobs:raise PreparationSuperseded('Preparation superseded by planned route')
            if identity is not None:
                now=time.monotonic();job_activity[identity]=now
                progress=job_progress.setdefault(identity,dict(started=now,stage='Preparing area',completed=0,total=0))
                if kind=='preparation-progress':progress.update({k:fields[k] for k in ('stage','completed','total') if k in fields})
                if len(job_progress)>64:job_progress.pop(next(iter(job_progress)))
            with (args.evidence/'events.jsonl').open('a') as output:output.write(json.dumps(row)+'\n')
    frame=RegionFrame.load(args.region)
    source_root=Path(__file__).resolve().parent
    worker_pins={p.name:hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in sorted(source_root.glob('*.py'))}
    worker_identity=hashlib.sha256(json.dumps(worker_pins,sort_keys=True,separators=(',',':')).encode()).hexdigest()
    session_hash=hashlib.sha256(args.session.read_bytes()).hexdigest() if args.session else ''
    pipeline=StoragePipeline(args.cache,event,frame);executor=ThreadPoolExecutor(max_workers=1)
    far=FarTerrain(args.cache,event,frame)
    jobs=JobStore(executor,event_lock,job_activity,cancelled_jobs)
    models=OrderedDict();model_lock=threading.Lock();forecast=Forecast()
    delivered_tiles=set()  # conservative for the whole authenticated player session
    destination=json.loads((args.destination or Path(__file__).resolve().parent/'destination.json').read_text())
    waypoints=destination.get('waypoints',[])+[destination]
    waypoint_index=json.loads(args.start.read_text()).get('waypointIndex',0) if args.start else 0
    if not 0<=waypoint_index<len(waypoints):raise ValueError('Invalid checkpoint waypoint index')
    def current_goal(start):
        nonlocal waypoint_index
        while waypoint_index<len(waypoints)-1:
            goal=waypoints[waypoint_index]
            if math.hypot(start['x']-goal['x'],start['z']-goal['z'])>goal['radiusMetres']:break
            event('guide-waypoint-committed',index=waypoint_index,name=goal['name'],x=start['x'],z=start['z'])
            waypoint_index+=1
        return waypoints[waypoint_index]
    startup=json.loads(args.start.read_text()) if args.start else {}
    def retain(model,cache_only=False,revision=''):
        model=retained_forecast_model(model,cache_only,revision)
        tile=(model['tileX'],model['tileZ'])
        with model_lock:
            models[tile]=model;models.move_to_end(tile)
            while len(models)>24:models.popitem(last=False)
    def available(request):
        result=[]
        for tile in request['loaded']:
            key=(tile['x'],tile['z'])
            with model_lock:model=models.get(key)
            if model is None:
                path=pipeline.prepared/f'{key[0]}_{key[1]}.json'
                if not path.exists():raise ValueError('Resident area is unavailable to the worker')
                model=json.loads(path.read_text());retain(model)
            result.append(model)
        return result
    def work(request,identity):
        worker_context.identity=identity
        tile=(math.floor(request['x']/CORE_METRES),math.floor(request['z']/CORE_METRES))
        event('requested',tile=list(tile),cacheOnly=request.get('cacheOnly',False),playerSeconds=request.get('playerSeconds',0),playerProgress=request.get('playerProgress',0))
        # HiGHS has no progress callback. Report actual process CPU activity
        # while its bounded solve runs; a sleeping/stalled worker stays idle.
        activity_stop=threading.Event()
        def numeric_activity():
            worker_context.identity=identity;previous=time.process_time()
            while not activity_stop.wait(5):
                current=time.process_time();busy=current-previous;previous=current
                if busy>.05:
                    try:event('preparation-active',cpuSeconds=round(busy,3))
                    except PreparationSuperseded:return
        monitor=threading.Thread(target=numeric_activity,daemon=True);monitor.start()
        # A ready reply may still be downloading, so protect every delivered
        # tile as well as the client's resident list. The serial executor owns
        # this set; speculative cache entries from earlier sessions may rebuild.
        pipeline.protected_tiles=delivered_tiles|{(row['x'],row['z']) for row in request.get('loaded',[])}
        import profile_solver
        def cancel_numeric():
            with event_lock:
                if identity in cancelled_jobs:raise PreparationSuperseded('Numeric preparation superseded')
        profile_solver.configure(cancel_numeric,lambda seconds:event('preparation-active',cpuSeconds=round(seconds,3)))
        try:model,manifest,cached=pipeline.prepare(tile)
        finally:
            profile_solver.configure();activity_stop.set();monitor.join()

        cache_only=request.get('cacheOnly',False) is True
        import sign_metadata
        sign_reply={} if cache_only else sign_metadata.prepare(pipeline,tile,model,manifest)
        retain(model,cache_only,manifest['sha256'])
        continuation=None;hint={}
        if not request.get('coverageOnly',False) and not cache_only:
            source_way=startup.get('sourceWay')
            if source_way and math.hypot(request['start']['x']-startup['start']['x'],request['start']['z']-startup['start']['z'])<1:
                import numpy as np
                from shapely.geometry import LineString,Point
                matches=[f for f in model['features'] if f['category']=='road' and f['sourceId']==str(source_way)]
                if not matches:raise ValueError('Checkpoint source road is absent')
                location=Point(request['start']['x'],request['start']['z'])
                feature=min(matches,key=lambda f:LineString([(p['x'],p['z']) for p in f['points']]).distance(location))
                xy=np.array([[p['x'],p['z']] for p in feature['points']]);station=np.r_[0,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))];line=LineString(xy)
                if line.distance(location)>8:raise ValueError('Checkpoint is not on its declared road')
                request['start']['y']=float(np.interp(line.project(location),station,[p['y'] for p in feature['points']]))
            continuation,hint=guide(model,request['start'],[request['headingX'],request['headingZ']],current_goal(request['start']))
        if not cache_only:delivered_tiles.add(tile)
        event('ready',tile=list(tile),cached=cached,guidePoints=len(continuation['points']) if continuation else 0,coverageOnly=request.get('coverageOnly',False),cacheOnly=cache_only)
        return dict(state='ready',tileX=tile[0],tileZ=tile[1],cached=cached,url='/prepared/'+manifest['file'],sha256=manifest['sha256'],bytes=manifest['bytes'],generationSeconds=0 if cached else manifest['seconds'],seamSamples=manifest['seams']['samples'],maximumSeamGap=manifest['seams']['maximumHeightGapMetres'],route=continuation,**hint,**sign_reply)
    from WorldData.public_rail import Collector
    rail_collector=Collector(pipeline.cache/'PublicRail')
    class Handler(SessionHandler):
        def log_message(self,*values):pass
        def send_json(self,value,status=200):
            body=json.dumps(value,separators=(',',':')).encode()
            self.send_response(status);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
        def do_POST(self):
            if self.path not in ['/request','/frontier','/guide','/far','/coast','/cancel']:return self.send_json({'error':'Unknown endpoint'},404)
            try:
                length=int(self.headers.get('Content-Length','0'))
                if length<1 or length>32768:raise ValueError('Invalid request size')
                request=json.loads(self.rfile.read(length))
                for key in ['x','z','headingX','headingZ']:
                    if not math.isfinite(float(request[key])):raise ValueError('Non-finite position')
                # Bounded proof: only the published catalogue's local BNG area.
                if max(abs(request['x']),abs(request['z']))>10000000:raise ValueError('Outside supported projected coordinates')
                if self.path=='/cancel':
                    identity=request.get('id','')
                    if len(identity)!=32 or any(c not in '0123456789abcdef' for c in identity) or jobs.get(identity) is None:raise ValueError('Unknown preparation job')
                    with event_lock:cancelled_jobs.add(identity)
                    jobs.get(identity).cancel()
                    event('preparation-superseded',job=identity)
                    return self.send_json(dict(state='cancelled'))
                if self.path=='/far':return self.send_json(far.prepare(request))
                if self.path=='/coast':return self.send_json(far.prepare_coast(request))
                if self.path=='/frontier':
                    resident=available(request);known={(m['tileX'],m['tileZ']) for m in resident}
                    from live_routing import source_forecast_metres
                    horizon=source_forecast_metres(request)
                    with model_lock:
                        extras=[m for key,m in models.items() if key not in known and math.hypot((key[0]+.5)*CORE_METRES-request['x'],(key[1]+.5)*CORE_METRES-request['z'])<=horizon+CORE_METRES]
                    result=forecast.reachable(resident+extras,request)
                    event('forecast',playerSeconds=request.get('playerSeconds',0),playerProgress=request.get('playerProgress',0),candidateTiles=[dict(x=row['tileX'],z=row['tileZ'],distance=row['distanceMetres']) for row in result['candidates']],corridorChunks=len(result['corridors']),reachableRoadNodes=result['reachableRoadNodes'])
                    return self.send_json(result)
                if self.path=='/guide':
                    tile=(math.floor(request['x']/CORE_METRES),math.floor(request['z']/CORE_METRES))
                    with model_lock:model=models.get(tile)
                    if model is None:raise ValueError('Guide area must already be resident')
                    continuation,hint=guide(model,request['start'],[request['headingX'],request['headingZ']],current_goal(request['start']),available(request))
                    tail=continuation['points'][-1]
                    event('guide-extended',tile=list(tile),points=len(continuation['points']),playerProgress=request.get('playerProgress',0),onA470=hint['onA470'],tailX=tail['x'],tailZ=tail['z'],nextX=hint['nextX'],nextZ=hint['nextZ'])
                    return self.send_json(dict(state='ready',route=continuation,**hint))
                # Re-poll the same live job rather than queueing duplicate cold
                # area preparation when a transport request is retried.
                key=hashlib.sha256(json.dumps(request,sort_keys=True,separators=(',',':')).encode()).hexdigest()
                with submission_lock:
                    identity=submissions.get(key);future=jobs.get(identity) if identity else None
                    if future is None or future.done() or identity in cancelled_jobs:
                        identity=uuid.uuid4().hex;jobs.submit(identity,work,request,identity);submissions[key]=identity
                    if len(submissions)>64:submissions.pop(next(iter(submissions)))
                self.send_json(dict(id=identity,state='pending'))
            except Exception as exception:self.send_json(dict(error=str(exception)),400)
        def do_GET(self):
            if self.path.startswith('/trains?'):
                try:
                    from urllib.parse import urlsplit,parse_qs
                    q=parse_qs(urlsplit(self.path).query,strict_parsing=True)
                    lon=float(q['lon'][0]);lat=float(q['lat'][0]);country=q['country'][0]
                    from WorldData.model import point
                    point(lon,lat)
                    if len(country)>8:raise ValueError('Country code limit')
                    return self.send_json(rail_collector.snapshot(lon,lat,country))
                except Exception as error:return self.send_json(dict(error=type(error).__name__),400)
            if self.path=='/health':return self.send_json(dict(ready=True,cacheFormat='LiveWorldArea1',
                protocol=1,workerIdentity=worker_identity,sessionSHA256=session_hash,frame=frame.identity(),worldDataSchema=1,providerCatalogue=__import__('WorldData.catalogue',fromlist=['Catalogue']).Catalogue().identity))
            if self.path.startswith('/job/'):
                future=jobs.get(self.path[5:])
                if future is None:return self.send_json(dict(error='Unknown job'),404)
                if not future.done():
                    with event_lock:last_activity=job_activity.get(self.path[5:])
                    idle=0 if last_activity is None else max(0,time.monotonic()-last_activity)
                    with event_lock:progress=dict(job_progress.get(self.path[5:],{}))
                    return self.send_json(dict(state='pending',idleSeconds=idle,
                        stage=progress.get('stage','Waiting for preparation worker'),completed=progress.get('completed',0),total=progress.get('total',0),
                        elapsedSeconds=max(0,time.monotonic()-progress.get('started',time.monotonic()))))
                try:return self.send_json(future.result())
                except PreparationSuperseded:return self.send_json(dict(state='cancelled'))
                except Exception as exception:
                    event('failed',error=str(exception));return self.send_json(dict(state='failed',error=str(exception)))
            if self.path.startswith('/prepared/'):
                name=self.path[len('/prepared/'):]
                try:path=prepared_file(pipeline.prepared,name)
                except ValueError:return self.send_json(dict(error='Invalid file'),400)
                if not path.is_file():return self.send_json(dict(error='Unavailable area'),404)
                data=path.read_bytes();self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data);return
            self.send_json(dict(error='Unknown endpoint'),404)
    server=SessionServer(('127.0.0.1',args.port),Handler)
    server.publish_endpoint(args.evidence)
    event('listening',port=server.server_port,generator=pipeline.version)
    try:server.serve_forever(poll_interval=.2)
    finally:rail_collector.close();executor.shutdown(wait=False,cancel_futures=True);server.server_close()


if __name__=='__main__':main()
