"""Failure-only public global Overpass recovery. No GIS/Unity imports.

New endpoint trust derives from the official global-instance table, never a
caller URL. DNS results are validated then pinned to a TLS connection retaining
original-host SNI/certificate verification. Redirects and proxies are not used.
"""
import concurrent.futures, hashlib, http.client, ipaddress, json, os, queue, re, socket, ssl, threading, time
from pathlib import Path
from urllib.parse import urlsplit, urlencode

DIRECTORY='https://wiki.openstreetmap.org/w/api.php?action=parse&page=Overpass_API&prop=wikitext&format=json&formatversion=2'
CAP=256*1024
TTL=86400
STALE=7*86400
BACKOFF=3600
_dns_slots=threading.BoundedSemaphore(2)
_dns_jobs=queue.Queue(maxsize=2)
def _dns_worker():
    while True:
        future,host=_dns_jobs.get()
        try:
            if future.set_running_or_notify_cancel():
                try:future.set_result(socket.getaddrinfo(host,443,type=socket.SOCK_STREAM))
                except BaseException as error:future.set_exception(error)
        finally:_dns_slots.release();_dns_jobs.task_done()
for _number in range(2):threading.Thread(target=_dns_worker,name='public-map-dns-'+str(_number),daemon=True).start()

class _HeaderReader:
    def __init__(self,source):self.source=source;self.remaining=16384;self.headers=True
    def readline(self,size=-1):
        limit=min(4097,self.remaining+1) if self.headers else 4097
        if size>=0:limit=min(limit,size)
        line=self.source.readline(limit)
        if len(line)>4096:raise ValueError('Public response header/chunk line cap')
        if self.headers:
            self.remaining-=len(line)
            if self.remaining<0:raise ValueError('Public response header byte cap')
            if line in (b'\r\n',b'\n'):self.headers=False
        return line
    def __getattr__(self,name):return getattr(self.source,name)
class _BoundedResponse(http.client.HTTPResponse):
    def __init__(self,*args,**kwargs):super().__init__(*args,**kwargs);self.fp=_HeaderReader(self.fp)
    def _read_status(self):
        version,status,reason=super()._read_status()
        # No request sends Expect:100-continue. Reject unsolicited interim
        # statuses before HTTPResponse can loop into a fresh header block.
        if 100<=status<200:raise ValueError('Unsolicited interim HTTP response')
        return version,status,reason


def public_ip(value):
    address=ipaddress.ip_address(value)
    if not address.is_global or any((address.is_private,address.is_loopback,address.is_link_local,address.is_multicast,address.is_reserved,address.is_unspecified)):return False
    if isinstance(address,ipaddress.IPv6Address):
        if address.sixtofour or address.teredo:return False
        if address.ipv4_mapped:return public_ip(str(address.ipv4_mapped))
    return True


def endpoint(value):
    if not isinstance(value,str) or len(value)>512 or any(ord(c)<33 or ord(c)>126 for c in value):raise ValueError('Invalid public endpoint')
    parsed=urlsplit(value)
    if parsed.scheme!='https' or parsed.username or parsed.password or parsed.port not in (None,443) or parsed.query or parsed.fragment:raise ValueError('Public HTTPS443 endpoint required')
    host=parsed.hostname
    if not host or len(host)>253 or not re.fullmatch(r'(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}',host):raise ValueError('Public DNS hostname required')
    path=parsed.path
    if path.endswith('/api/'):path+='interpreter'
    if not path.endswith('/interpreter') or not re.fullmatch(r'/[A-Za-z0-9_./-]+',path) or '..' in path or '//' in path:raise ValueError('Unsupported interpreter path')
    if re.search(r'key|token|secret|password|your_|\{',path,re.I):raise ValueError('Credential path rejected')
    return 'https://'+host+path


def _unique(pairs):
    result={}
    for key,value in pairs:
        if key in result:raise ValueError('Duplicate directory JSON property')
        result[key]=value
    return result


def parse_directory(raw):
    if not isinstance(raw,bytes) or len(raw)>CAP:raise ValueError('Directory byte budget')
    root=json.loads(raw,object_pairs_hook=_unique)
    if not isinstance(root,dict) or not isinstance(root.get('parse'),dict):raise ValueError('Directory object required')
    parsed=root['parse']
    if parsed.get('title')!='Overpass API' or parsed.get('pageid')!=35322:raise ValueError('Unexpected official page')
    text=parsed.get('wikitext')
    if not isinstance(text,str):raise ValueError('Directory text required')
    heading=re.search(r'^===\s*Instances with global data coverage\s*===\s*$',text,re.M)
    if not heading:raise ValueError('Global table absent')
    section=re.split(r'^={2,3}[^=]',text[heading.end():],maxsplit=1,flags=re.M)[0]
    tables=re.findall(r'\{\|[^\n]*wikitable.*?\|\}',section,re.S)
    if len(tables)!=1:raise ValueError('Ambiguous global table')
    rows=re.split(r'^\|-\s*$',tables[0],flags=re.M)
    header=next((row for row in rows if 'API Endpoint' in row and 'Usage policy' in row),None)
    if not header or 'Data coverage' in header:raise ValueError('Global table schema changed')
    columns=[]
    for line in header.splitlines():
        if line.startswith('!'):
            cell=line[1:];cell=cell.split('|',1)[1] if 'scope=' in cell and '|' in cell else cell
            columns.append(cell.strip())
    try:url_column=columns.index('API Endpoint');policy_column=columns.index('Usage policy')
    except ValueError:raise ValueError('Global table columns changed')
    result=[]
    for row in rows:
        if row==header:continue
        cells=[]
        for line in row.splitlines():
            if line.startswith('|') and not line.startswith(('|}','|-')):cells.append(line[1:])
            elif cells:cells[-1]+='\n'+line
        if len(cells)!=len(columns):continue
        policy=cells[policy_column]
        if not re.search(r'feel free|free public|free to use|no (?:requests?|rate).*limit|don.t disturb|one-off use|public access|open access|no account|anonymous|anyone may use|available to everyone|no authentication',policy,re.I):continue
        if re.search(r'private deployment|private access|restricted access|prior approval|contact.{0,100}for access',policy,re.I):continue
        # "No API key" is public; signup/key/payment-required services are not.
        policy=re.sub(r'no\s+(?:api[ -]?key|account|registration|authentication)|without\s+(?:an?\s+)?(?:api[ -]?key|account|registration|authentication)','',policy,flags=re.I)
        if re.search(r'api[ _-]?keys?|accounts?|authentication|bearer|authorization|sign\s*up|signup|register|registration|payment\s+required|monthly\s+billing|evaluation\s+period|free\s+trial',policy,re.I):continue
        urls=re.findall(r'https://[^\s<>\[\]{}"\']+',cells[url_column])
        if len(urls)!=1:continue
        try:value=endpoint(urls[0])
        except ValueError:continue
        if value not in result:result.append(value)
        if len(result)>16:raise ValueError('Directory endpoint count budget')
    if not result:raise ValueError('No anonymous global instances')
    return result


def _resolve(host,deadline):
    if not _dns_slots.acquire(blocking=False):raise TimeoutError('DNS resolver busy')
    future=concurrent.futures.Future();_dns_jobs.put_nowait((future,host))
    try:rows=future.result(timeout=max(.001,min(2,deadline-time.monotonic())))
    except TimeoutError:future.cancel();raise
    if not rows or len(rows)>32 or any(not public_ip(row[4][0]) for row in rows):raise ValueError('Nonpublic/oversized DNS answer rejected')
    result=[]
    for family,kind,protocol,_,address in rows:
        if family not in (socket.AF_INET,socket.AF_INET6):raise ValueError('Unsupported DNS family')
        if (family,address) not in result:result.append((family,address))
    return result[:2]


def secure_request(url,maximum,seconds=8,body=None):
    # Only the fixed official directory GET may contain query parameters.
    if url!=DIRECTORY:url=endpoint(url)
    parsed=urlsplit(url);deadline=time.monotonic()+seconds;rows=_resolve(parsed.hostname,deadline)
    try:
        import certifi
        context=ssl.create_default_context(cafile=certifi.where())
    except ImportError:context=ssl.create_default_context()
    last=None
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
            raw=socket.socket(family,socket.SOCK_STREAM);raw.settimeout(max(.001,deadline-time.monotonic()));raw.connect(address)
            tls=context.wrap_socket(raw,server_hostname=parsed.hostname);raw=None
            connection=http.client.HTTPSConnection(parsed.hostname,443,context=context);connection.sock=tls;connection.response_class=_BoundedResponse
            target=parsed.path+('?' + parsed.query if parsed.query else '')
            headers={'Host':parsed.hostname,'User-Agent':'EarthUnscripted/18 (https://earthunscripted.com)','Accept':'application/json','Accept-Encoding':'identity','Connection':'close'}
            if body is not None:headers['Content-Type']='application/x-www-form-urlencoded'
            connection.request('POST' if body is not None else 'GET',target,body=body,headers=headers)
            response=connection.getresponse()
            if response.status!=200:raise ValueError('Public source HTTP '+str(response.status)) # redirects fail closed
            if response.getheader('Content-Encoding','identity').lower()!='identity':raise ValueError('Compressed response not admitted')
            declared=response.getheader('Content-Length');declared=None if declared is None else int(declared)
            if declared is not None and (declared<0 or declared>maximum):raise ValueError('Public response byte budget')
            output=bytearray()
            while True:
                remaining=deadline-time.monotonic()
                if remaining<=0:raise TimeoutError('Public source total deadline')
                tls.settimeout(remaining);chunk=response.read1(min(65536,maximum+1-len(output)))
                if not chunk:break
                output.extend(chunk)
                if len(output)>maximum:raise ValueError('Public response byte budget')
            if declared is not None and len(output)!=declared:raise ValueError('Truncated public response')
            return bytes(output)
        except (OSError,ValueError,http.client.HTTPException) as error:last=error
        finally:
            watchdog.cancel()
            if connection:connection.close()
            elif tls:tls.close()
            if raw:raw.close()
        if time.monotonic()>=deadline:break
    raise last or TimeoutError('Public source unavailable')


def _atomic(path,data):
    temporary=path.with_name(path.name+'.partial');temporary.write_bytes(data);temporary.replace(path)


class Directory:
    def __init__(self,folder,fetch=None,clock=time.time):
        self.folder=Path(folder);self.folder.mkdir(parents=True,exist_ok=True);self.fetch=fetch or secure_request;self.clock=clock
    def candidates_after_failure(self):
        now=self.clock();raw_path=self.folder/'official-global.json';state_path=self.folder/'state.json';state={};cached=[]
        try:
            if state_path.stat().st_size<4096:
                state=json.loads(state_path.read_bytes())
                import math
                if not isinstance(state,dict) or any(not isinstance(state.get(key,0),(int,float)) or not math.isfinite(state.get(key,0)) or not 0<=state.get(key,0)<=now+5 for key in ('fetchedUTC','attemptUTC')):state={}
                if 'sha256' in state and (not isinstance(state['sha256'],str) or not re.fullmatch(r'[a-f0-9]{64}',state['sha256'])):state={}
            if raw_path.stat().st_size<=CAP and 0<=now-state['fetchedUTC']<STALE:
                raw=raw_path.read_bytes()
                if hashlib.sha256(raw).hexdigest()==state['sha256']:cached=parse_directory(raw)
        except (OSError,ValueError,KeyError,TypeError,RecursionError):state={}
        if cached and now-state['fetchedUTC']<TTL:return cached
        if now-state.get('attemptUTC',0)<BACKOFF:return cached
        lock=self.folder/'refresh.lock'
        try:
            if lock.exists() and now-lock.stat().st_mtime>60:lock.unlink()
            descriptor=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600);os.close(descriptor)
        except OSError:return cached
        try:
            state['attemptUTC']=now;_atomic(state_path,json.dumps(state).encode())
            raw=self.fetch(DIRECTORY,CAP,8);result=parse_directory(raw);_atomic(raw_path,raw)
            state.update(fetchedUTC=now,sha256=hashlib.sha256(raw).hexdigest());_atomic(state_path,json.dumps(state).encode());return result
        except (OSError,ValueError,KeyError,TypeError,TimeoutError,RecursionError,http.client.HTTPException):return cached
        finally:lock.unlink(missing_ok=True)


def recover(folder,query,attempted,fetch=None,limit=16000000,allow_empty=False,failed_hosts=()):
    if not isinstance(query,str) or len(query.encode())>65536:raise ValueError('Map query byte budget')
    fetch=fetch or secure_request
    candidates=Directory(folder,fetch).candidates_after_failure();attempted=set(attempted);failed_hosts=set(failed_hosts)
    folder=Path(folder);lock=folder/'query.lock';failures_path=folder/'query-failures.json';now=time.time();failures={}
    try:
        if lock.exists() and now-lock.stat().st_mtime>60:lock.unlink()
        descriptor=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600);os.close(descriptor)
    except OSError:return None
    try:
        try:
            if failures_path.stat().st_size<=16384:
                failures=json.loads(failures_path.read_bytes())
                if not isinstance(failures,dict) or len(failures)>16 or any(not isinstance(value,(int,float)) or not 0<=value<=now+5 for value in failures.values()):failures={}
        except (OSError,ValueError,TypeError,RecursionError):failures={}
        count=0
        eligible=[value for value in candidates if value not in attempted and urlsplit(value).hostname not in failed_hosts]
        # Untried/least-recently failed endpoints precede repeatedly failing
        # table leaders. Stable ties retain official directory order.
        eligible.sort(key=lambda value:failures.get(value,0))
        for url in eligible:
            if now-failures.get(url,0)<30:continue
            if count>=2:break
            count+=1
            try:
                data=fetch(url,limit,10,body=urlencode({'data':query}).encode('ascii'))
                parsed=json.loads(data)
                if not isinstance(parsed,dict) or not isinstance(parsed.get('elements'),list) or (not parsed['elements'] and not allow_empty) or parsed.get('remark'):raise ValueError('Incomplete discovered map response')
                if url in failures:del failures[url]
                _atomic(failures_path,json.dumps(failures).encode());return data,url
            except (OSError,ValueError,TimeoutError,RecursionError,http.client.HTTPException):
                failures[url]=time.time();_atomic(failures_path,json.dumps(failures).encode())
        return None
    finally:lock.unlink(missing_ok=True)


def main():
    """Early worker CLI: only called after all shipped map providers failed."""
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--request',type=Path,required=True);args=parser.parse_args()
    if args.request.stat().st_size>128*1024:raise ValueError('Recovery job byte cap')
    job=json.loads(args.request.read_bytes(),object_pairs_hook=_unique)
    query=job['query'];attempted=job['attempted'];bounds=job['bounds']
    if not isinstance(attempted,list) or len(attempted)>16 or not all(isinstance(value,str) and len(value)<1024 for value in attempted):raise ValueError('Recovery attempt cap')
    if not isinstance(bounds,list) or len(bounds)!=4 or not all(isinstance(v,(int,float)) for v in bounds):raise ValueError('Recovery bounds required')
    south,west,north,east=bounds
    import math
    if not all(math.isfinite(v) for v in bounds) or not(-90<=south<north<=90 and -180<=west<east<=180):raise ValueError('Invalid recovery bounds')
    if (north-south)*111320>10000 or (east-west)*111320*math.cos(math.radians((south+north)/2))>10000:raise ValueError('Recovery area exceeds10km')
    boxes=re.findall(r'\((-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)\)',query)
    if not boxes or any(any(abs(float(a)-b)>.00001 for a,b in zip(row,bounds)) for row in boxes):raise ValueError('Query must use only the selected recovery bounds')
    result=recover(job['cache'],query,attempted)
    if result is None:raise RuntimeError('No new public map instance available')
    data,url=result;_atomic(args.request.parent/'map.json',data)
    _atomic(args.request.parent/'receipt.json',json.dumps({'endpoint':url,'sha256':hashlib.sha256(data).hexdigest()}).encode())
