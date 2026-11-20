"""Low-priority nearby full-source downloads; never pre-generate whole regions."""
from pathlib import Path
import json,math,shutil,threading,time
from osm_cache import busy,limit_bytes,usage_bytes,prune,file_lease

_managers={};_guard=threading.Lock()
class PrefetchPaused(BaseException):pass


def neighbouring_regions(directory,bounds,maximum=12):
    from shapely.geometry import Point,box,shape
    from shapely.ops import nearest_points
    south,west,north,east=bounds;latitude=(south+north)*.5;longitude=(west+east)*.5
    point=Point(longitude,latitude);required=box(west,south,east,north)
    features=directory.get('features',[]);parents={row.get('properties',{}).get('parent') for row in features};offered=[]
    for feature in features:
        try:
            props=feature['properties'];identity=props['id'];url=props['urls']['pbf']
            if identity in parents:continue  # smallest published region leaves
            polygon=shape(feature['geometry'])
            if polygon.is_empty or (polygon.covers(required) and not props.get('city')):continue
            nearest=nearest_points(point,polygon)[1]
            distance=math.hypot((nearest.x-longitude)*111319.49*max(.1,math.cos(math.radians(latitude))),(nearest.y-latitude)*111319.49)
            if distance<=50000:offered.append((distance,polygon.area,identity,dict(id=identity,url=url,city=props.get('city'))))
        except (KeyError,ValueError,TypeError):continue
    return [row[-1] for row in sorted(offered,key=lambda row:row[:3])[:maximum]]


class NearbySources:
    def __init__(self,root,event,start=True):
        self.root=Path(root);self.event=event;self.bounds=None;self.seen={};self.wake=threading.Event();self.stop=threading.Event();self.lock=threading.Lock();self.thread=None;self.city_regions=[];self.city_probe=None
        if start:
            self.thread=threading.Thread(target=self._run,name='Nearby OSM downloads',daemon=True);self.thread.start()
    def schedule(self,bounds):
        with self.lock:self.bounds=tuple(bounds)
        self.wake.set()
    def _event(self,kind,**fields):
        self.event('osm-prefetch-progress' if kind=='preparation-progress' else 'osm-prefetch-'+kind,**fields)
    def _cancel(self):
        if self.stop.is_set() or busy(self.root,'io'):raise PrefetchPaused()
    def step(self):
        import map_transport as transport
        self._cancel();prune(self.root,self._event)
        with self.lock:bounds=self.bounds
        if bounds is None:return
        directory=self.root/'regional-index.geojson'
        if not directory.exists():return
        # Exact city extracts can bridge gaps between published regional leaves
        # (for example Bristol and the Welsh side of the Severn crossing).
        south,west,north,east=bounds;lat=(south+north)*.5;lon=(west+east)*.5
        now=time.monotonic();probe=self.city_probe
        moved=probe is None or math.hypot((lon-probe[1])*111319.49*max(.1,math.cos(math.radians(lat))),(lat-probe[0])*111319.49)>25000
        if moved or now-probe[2]>900:
            from city_extract import partial_city_regions
            dy=50000/111319.49;dx=dy/max(.1,math.cos(math.radians(lat)))
            window=(max(-89.9,lat-dy),max(-180.,lon-dx),min(89.9,lat+dy),min(180.,lon+dx))
            try:
                self.city_regions=partial_city_regions(self.root,window,self._event,cancel=self._cancel);self.city_probe=(lat,lon,now)
            except PrefetchPaused:raise
            except Exception as error:
                self.city_probe=(lat,lon,now-885);self._event('deferred',reason='Nearby city coverage unavailable',error=str(error))
        combined=json.loads(directory.read_text());combined['features']=combined.get('features',[])+self.city_regions
        offered=neighbouring_regions(combined,bounds);attempts=0
        for region in offered:
            self._cancel();identity=region['id'];now=time.monotonic()
            if self.seen.get(identity,0)>now:continue
            path=self.root/('regional-'+identity.replace('/','-')+'.osm.pbf')
            if region.get('city'):
                from city_extract import _receipt as cached,download as download_city
            else:cached=transport._cached_download
            if cached(path,region['url'],transport.CAP) is not None:
                self.seen[identity]=now+3600;continue
            free=shutil.disk_usage(self.root)
            reserve=max(2_000_000_000,min(10_000_000_000,int(free.total*.02)))
            available=min(transport.CAP,limit_bytes(self.root)-usage_bytes(self.root),free.free-reserve)
            if available<16*1024*1024:
                self._event('deferred',reason='Cache quota or device free-space reserve');return
            attempts+=1
            try:
                self._event('started',region=identity)
                if region.get('city'):download_city(path,region['url'],int(available),300,self._event,self._cancel,socket_timeout=5)
                else:transport._download(path,region['url'],int(available),self._event,self._cancel,socket_timeout=5,lease_wait=False)
                self.seen[identity]=time.monotonic()+3600;self._event('ready',region=identity,bytes=path.stat().st_size)
                prune(self.root,self._event)
            except PrefetchPaused:raise
            except Exception as error:
                delay=15 if transport.transient_source_error(error) else 3600;self.seen[identity]=time.monotonic()+delay;self._event('deferred',region=identity,error=str(error),retrySeconds=delay)
            if attempts>=3:break
        if len(self.seen)>256:self.seen={k:v for k,v in self.seen.items() if v>time.monotonic()}
    def _run(self):
        # Only one process may prefetch into a shared cache. Foreground reads
        # use independent dataset leases, and this worker yields on their IO.
        with file_lease(self.root/'nearby-prefetch',wait=False) as held:
            if not held:return
            while not self.stop.is_set():
                self.wake.wait(15);self.wake.clear()
                try:self.step()
                except PrefetchPaused:continue
                except Exception as error:self._event('deferred',error=str(error))
    def close(self):
        self.stop.set();self.wake.set()
        if self.thread:self.thread.join(timeout=6)


def configure(root,event,start=True):
    key=str(Path(root).resolve())
    with _guard:
        if key not in _managers:_managers[key]=NearbySources(root,event,start)
        return _managers[key]


def schedule(root,bounds):
    with _guard:manager=_managers.get(str(Path(root).resolve()))
    if manager is not None:manager.schedule(bounds)
