"""Failure-only city PBF fallback from the official BBBike catalogue.

A place name is a catalogue hint, never a coverage assertion: the complete
requested rectangle must fit the source .poly, including islands/holes. No
caller URL, redirects, proxy or non-public DNS address is admitted.
"""
import hashlib,http.client,json,math,re,shutil,socket,ssl,threading,time,unicodedata
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlsplit
import certifi
from shapely.geometry import Polygon,box,mapping
from shapely.ops import unary_union
from public_overpass import _resolve,_BoundedResponse
from city_coverage import COVERAGE as CITY_COVERAGE

HOST='download.bbbike.org';BASE='https://'+HOST+'/osm/bbbike/'
CAP=256*1024*1024;AGE=7*86400
_verified={}
SLUG=re.compile(r'[A-Za-z0-9_-]{1,80}')

def admitted_url(url):
    p=urlsplit(url)
    if p.scheme!='https' or p.hostname!=HOST or p.port not in (None,443) or p.username or p.password or p.query or p.fragment:raise ValueError('Untrusted city extract URL')
    if p.path=='/osm/bbbike/':return url
    m=re.fullmatch(r'/osm/bbbike/([A-Za-z0-9_-]{1,80})/([A-Za-z0-9_-]{1,80})\.(poly|osm\.pbf)',p.path)
    if not m or m[1]!=m[2]:raise ValueError('Untrusted city extract path')
    return url

def digest(path):
    result=hashlib.sha256()
    with path.open('rb') as source:
        while chunk:=source.read(1024*1024):result.update(chunk)
    return result.hexdigest()

def _receipt(path,url,limit):
    try:
        metadata=path.with_suffix(path.suffix+'.receipt.json')
        with metadata.open('rb') as source:raw=source.read(32769)
        if len(raw)>32768:return None
        row=json.loads(raw);now=time.time()
        if not isinstance(row,dict) or row.get('url')!=url:return None
        fetched=row.get('fetchedUTC');size=row.get('bytes');sha=row.get('sha256')
        if isinstance(fetched,bool) or not isinstance(fetched,(int,float)) or not math.isfinite(fetched) or not 0<=now-fetched<AGE:return None
        if isinstance(size,bool) or not isinstance(size,int) or not 0<size<=limit or path.stat().st_size!=size:return None
        if not isinstance(sha,str) or not re.fullmatch('[a-f0-9]{64}',sha):return None
        stat=path.stat();key=(str(path),stat.st_size,stat.st_mtime_ns,sha)
        if key not in _verified:
            if digest(path)!=sha:return None
            if len(_verified)>=64:_verified.clear()
            _verified[key]=True
        path.touch();updated=path.stat();_verified[(str(path),updated.st_size,updated.st_mtime_ns,sha)]=True
        return row
    except (OSError,ValueError,TypeError,RecursionError):return None

def download(path,url,limit,seconds,event=lambda *a,**k:None,cancel=lambda:None,socket_timeout=20):
    from osm_cache import file_lease
    with file_lease(path,cancel=cancel):return _download_locked(path,url,limit,seconds,event,cancel,socket_timeout)


def _download_locked(path,url,limit,seconds,event,cancel,socket_timeout):
    """Pinned, verified streaming GET. One total deadline covers DNS/TLS/body."""
    admitted_url(url);cached=_receipt(path,url,limit)
    if cached:return cached
    from osm_cache import admit_peer
    peer=admit_peer(path,url,limit,event)
    if peer is not None:return peer
    path.parent.mkdir(parents=True,exist_ok=True)
    if shutil.disk_usage(path.parent).free<limit*2:raise ValueError('Insufficient free space for bounded city extract')
    from source_resume import DownloadResume
    state=DownloadResume(path,url,limit)
    if state.complete:return state.finish()
    deadline=time.monotonic()+seconds;rows=_resolve(HOST,deadline);last_progress=0.
    if state.count:event('regional-download-resumed',file=path.name,bytesAlready=state.count,total=state.expected)
    context=ssl.create_default_context(cafile=certifi.where());last=None
    for family,address in rows:
        raw=None;tls=None;connection=None
        def expire():
            active=tls or raw
            if active:
                try:active.shutdown(socket.SHUT_RDWR)
                except OSError:pass
                active.close()
        watchdog=threading.Timer(max(.001,deadline-time.monotonic()),expire);watchdog.daemon=True;watchdog.start()
        try:
            raw=socket.socket(family,socket.SOCK_STREAM);raw.settimeout(max(.001,min(socket_timeout,deadline-time.monotonic())));raw.connect(address)
            tls=context.wrap_socket(raw,server_hostname=HOST);raw=None
            connection=http.client.HTTPSConnection(HOST,443,context=context);connection.sock=tls;connection.response_class=_BoundedResponse
            cancel()
            connection.request('GET',urlsplit(url).path,headers={'Host':HOST,'User-Agent':'EarthUnscripted/32 (https://earthunscripted.com)','Accept-Encoding':'identity','Connection':'close',**state.headers})
            response=connection.getresponse()
            if response.status==416 and state.count:
                state.discard();raise TimeoutError('City resume rejected; retry starts a fresh representation')
            mode=state.accept(response.status,{key:value for key in ('Content-Length','Content-Encoding','Content-Range','ETag') if (value:=response.getheader(key)) is not None})
            with state.partial.open(mode) as output:
                checkpoint=state.count
                while True:
                    cancel();remaining=deadline-time.monotonic()
                    if remaining<=0:raise TimeoutError('City extract total deadline; verified progress retained')
                    tls.settimeout(min(socket_timeout,remaining));chunk=response.read1(min(65536,limit+1-state.count))
                    if not chunk:break
                    state.received(chunk);output.write(chunk)
                    now=time.monotonic()
                    if now-last_progress>=1 or state.complete:
                        event('preparation-progress',stage='Downloading regional map' if path.name.endswith('.pbf') else 'Downloading city catalogue',completed=state.count,total=state.expected or 0);last_progress=now
                    if state.count-checkpoint>=4*1024*1024:
                        output.flush();state.checkpoint();checkpoint=state.count
            return state.finish()
        except (OSError,ValueError,http.client.HTTPException) as error:
            state.interrupted(error);last=error
        except BaseException as error:
            state.interrupted(error);raise
        finally:
            watchdog.cancel()
            if connection:connection.close()
            elif tls:tls.close()
            if raw:raw.close()
        if time.monotonic()>=deadline:break
    raise last or TimeoutError('City source unavailable')

class Catalogue(HTMLParser):
    def __init__(self):super().__init__();self.names=set()
    def handle_starttag(self,tag,attrs):
        if tag!='a':return
        href=dict(attrs).get('href','')
        if href.endswith('/') and SLUG.fullmatch(href[:-1]):self.names.add(href[:-1])

def names(raw):
    if not isinstance(raw,bytes) or len(raw)>256*1024:raise ValueError('City catalogue byte cap')
    parser=Catalogue();parser.feed(raw.decode('utf-8'))
    if not 1<=len(parser.names)<=512:raise ValueError('City catalogue count invalid')
    return sorted(parser.names)

def candidates(catalogue,hint):
    def normalize(s):return re.sub('[^a-z0-9]','',unicodedata.normalize('NFKD',str(s)).encode('ascii','ignore').decode().lower())
    hint=normalize(str(hint).split('·')[0].split(',')[0].strip())
    if len(hint)<3:return []
    return sorted((n for n in catalogue if SLUG.fullmatch(n) and (hint.startswith(normalize(n)) or len(hint)>=4 and normalize(n).startswith(hint))),key=lambda n:(normalize(n)!=hint,-len(n),n))[:3]

def parse_poly(raw):
    if not isinstance(raw,bytes) or len(raw)>1024*1024:raise ValueError('City boundary byte cap')
    lines=[l.strip() for l in raw.decode('ascii').splitlines() if l.strip()]
    if len(lines)<6:raise ValueError('City boundary missing rings')
    outers=[];holes=[];index=1;count=0
    while index<len(lines):
        label=lines[index];index+=1
        if label=='END':
            if index!=len(lines):raise ValueError('Trailing city boundary data')
            break
        if not re.fullmatch('!?[A-Za-z0-9_-]+',label):raise ValueError('City boundary ring label')
        points=[]
        while index<len(lines) and lines[index]!='END':
            values=lines[index].split();index+=1
            if len(values)!=2:raise ValueError('City boundary coordinate pair')
            x,y=map(float,values);count+=1
            if not math.isfinite(x) or not math.isfinite(y) or not -180<=x<=180 or not -90<=y<=90 or count>20000:raise ValueError('City boundary coordinate bounds')
            points.append((x,y))
        if index>=len(lines) or len(set(points))<3:raise ValueError('Unclosed city boundary ring')
        index+=1;polygon=Polygon(points)
        if not polygon.is_valid or polygon.area<=0:raise ValueError('Invalid city boundary ring')
        (holes if label.startswith('!') else outers).append(polygon)
    else:raise ValueError('City boundary final END absent')
    if not outers:raise ValueError('City boundary outer absent')
    coverage=unary_union(outers).difference(unary_union(holes))
    if not coverage.is_valid or coverage.is_empty:raise ValueError('City coverage invalid')
    return coverage

def geographic_candidates(catalogue,bounds):
    # Bundled boxes only shortlist candidates. A fresh verified .poly below is
    # still the coverage authority, including concavities, islands and holes.
    south,west,north,east=bounds;offered=[]
    for name in catalogue:
        row=CITY_COVERAGE.get(name)
        if not isinstance(row,(list,tuple)) or len(row)!=4:continue
        try:
            w,s,e,n=map(float,row)
            if not all(math.isfinite(v) for v in (w,s,e,n)) or not -180<=w<e<=180 or not -90<=s<n<=90:continue
            if w<=west<east<=e and s<=south<north<=n:offered.append(((e-w)*(n-s),name))
        except (ValueError,TypeError):continue
    return [name for _,name in sorted(offered)[:3]]

def fetch_city(root,bounds,hint,event=lambda *a,**k:None,fetch=download,cached_only=False):
    south,west,north,east=bounds
    if not all(math.isfinite(v) for v in bounds) or not -90<=south<north<=90 or not -180<=west<east<=180:raise ValueError('City request bounds invalid')
    if fetch is download:
        stream_download=fetch
        fetch=lambda path,url,limit,seconds:stream_download(path,url,limit,seconds,event=event)
    if cached_only:
        def fetch(path,url,limit,seconds):
            receipt=_receipt(path,url,limit)
            if receipt is None:raise ValueError('No fresh verified city extract cache')
            return receipt
    index=root/'bbbike-index.html';fetch(index,BASE,256*1024,8)
    catalogue=names(index.read_bytes());by_name=candidates(catalogue,hint);by_area=geographic_candidates(catalogue,bounds)
    offered=list(dict.fromkeys(by_name+by_area))[:4]
    if not offered:raise ValueError('No catalogue city matches source place name or requested geographic coverage')
    for name in offered:
        boundary=root/('bbbike-'+name+'.poly');fetch(boundary,BASE+name+'/'+name+'.poly',1024*1024,8)
        shape=parse_poly(boundary.read_bytes())
        if not shape.covers(box(west,south,east,north)):continue
        path=root/('regional-bbbike-'+name+'.osm.pbf');receipt=fetch(path,BASE+name+'/'+name+'.osm.pbf',CAP,300)
        event('city-extract-source',city=name,url=receipt['url'],bytes=receipt['bytes'],coverageSHA256=digest(boundary))
        return path,dict(receipt,city=name,coverageSHA256=digest(boundary))
    raise ValueError('No exact city extract covers the complete requested area')


def partial_city_regions(root,bounds,event=lambda *a,**k:None,cached_only=False,cancel=lambda:None):
    """Exact small extract polygons for a cross-border coverage union, not data."""
    south,west,north,east=bounds;envelope=box(west,south,east,north)
    def fetch(path,url,limit,seconds):
        if not cached_only:return download(path,url,limit,seconds,event=event,cancel=cancel,socket_timeout=5)
        row=_receipt(path,url,limit)
        if row is None:raise ValueError('No fresh verified city metadata cache')
        return row
    index=root/'bbbike-index.html';fetch(index,BASE,256*1024,8);catalogue=names(index.read_bytes());offered=[]
    for name in catalogue:
        row=CITY_COVERAGE.get(name)
        if not isinstance(row,(list,tuple)) or len(row)!=4:continue
        try:
            w,s,e,n=map(float,row)
            if not all(math.isfinite(v) for v in (w,s,e,n)) or not -180<=w<e<=180 or not -90<=s<n<=90:continue
            candidate=box(w,s,e,n)
            if candidate.intersects(envelope):offered.append((candidate.area,name))
        except (ValueError,TypeError):continue
    result=[]
    for _,name in sorted(offered)[:3]:
        path=root/('bbbike-'+name+'.poly');fetch(path,BASE+name+'/'+name+'.poly',1024*1024,8)
        polygon=parse_poly(path.read_bytes())
        if polygon.intersects(envelope):result.append(dict(type='Feature',geometry=mapping(polygon),properties=dict(id='bbbike-'+name,city=name,urls=dict(pbf=BASE+name+'/'+name+'.osm.pbf'))))
    return result
