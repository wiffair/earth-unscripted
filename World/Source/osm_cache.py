"""Independent bounded OSM source storage, with process-held dataset leases."""
from contextlib import contextmanager
from pathlib import Path
import errno,hashlib,json,math,os,shutil,threading,time
if os.name=='nt':import msvcrt
else:import fcntl

_guard=threading.RLock();_preparing={};_io={}

@contextmanager
def activity(root,kind='prepare'):
    key=str(Path(root).resolve());table=_io if kind=='io' else _preparing
    with _guard:table[key]=table.get(key,0)+1
    try:yield
    finally:
        with _guard:
            table[key]-=1
            if not table[key]:del table[key]

def busy(root,kind='prepare'):
    with _guard:return bool((_io if kind=='io' else _preparing).get(str(Path(root).resolve())))

@contextmanager
def file_lease(path,wait=True,cancel=lambda:None):
    path=Path(path);folder=path.parent/'.leases';folder.mkdir(parents=True,exist_ok=True)
    lock=folder/(hashlib.sha256(path.name.encode()).hexdigest()+'.lock');held=False
    with lock.open('a+b') as stream:
        if os.name=='nt':
            stream.seek(0,2)
            if not stream.tell():stream.write(b'\0');stream.flush()
        while True:
            cancel()
            try:
                if os.name=='nt':stream.seek(0);msvcrt.locking(stream.fileno(),msvcrt.LK_NBLCK,1)
                else:fcntl.flock(stream.fileno(),fcntl.LOCK_EX|fcntl.LOCK_NB)
                held=True;break
            except OSError as error:
                if error.errno not in (errno.EACCES,errno.EAGAIN,errno.EDEADLK):raise
                if not wait:break
                time.sleep(.1)
        try:yield held
        finally:
            if held:
                if os.name=='nt':stream.seek(0);msvcrt.locking(stream.fileno(),msvcrt.LK_UNLCK,1)
                else:fcntl.flock(stream.fileno(),fcntl.LOCK_UN)


def migrate(raw,root,event):
    """Move known legacy OSM entries under the existing preparation lease."""
    raw=Path(raw);root=Path(root);root.mkdir(parents=True,exist_ok=True)
    if not raw.exists():return
    count=0
    for path in raw.iterdir():
        if not path.is_file() or path.is_symlink():continue
        if not (path.name.startswith(('regional-','bbbike-')) or '-osm-areas-v' in path.name or '-controls-v' in path.name):continue
        target=root/path.name
        if target.exists():continue
        path.rename(target);count+=1
    if count:event('osm-cache-migrated',files=count,scope='Source downloads and local OSM indexes; terrain and prepared models retained')


def limit_bytes(root):
    value=5.
    try:
        settings=json.loads((Path(root).parent/'storage-settings.json').read_text());candidate=settings.get('osmGigabytes',5.)
        if isinstance(candidate,(int,float)) and not isinstance(candidate,bool) and math.isfinite(candidate):value=min(50.,max(.5,candidate))
    except (OSError,ValueError,TypeError,AttributeError):pass
    return int(value*1_000_000_000)


def usage_bytes(root):
    try:return sum(p.stat().st_size for folder in [Path(root),Path(root)/'Incoming'] if folder.is_dir() and not folder.is_symlink() for p in folder.iterdir() if p.is_file() and not p.is_symlink())
    except FileNotFoundError:return 0


def dataset_path(path):
    name=path.name
    if name.startswith('bbbike-') and '.poly' in name:
        return path.parent/('regional-'+name.split('.poly',1)[0]+'.osm.pbf')
    if name.startswith('regional-') and '.osm' in name:return path.parent/(name.split('.osm',1)[0]+'.osm.pbf')
    for suffix in ('.receipt.json','.resume.json','.resume.partial','.partial'):
        if name.endswith(suffix):name=name.removesuffix(suffix);break
    return path.parent/name


def prune(root,event=lambda *a,**k:None):
    root=Path(root)
    # A foreground prepare can still be reading an area JSON. Background
    # download completion defers eviction until that immutable copy is made.
    if busy(root):return
    with file_lease(root/'cache-inventory',wait=False) as held:
        if not held:return
        groups={}
        for folder in [root,root/'Incoming']:
            if not folder.is_dir() or folder.is_symlink():continue
            for p in folder.iterdir():
                if p.is_file() and not p.is_symlink() and not p.name.startswith('.'):groups.setdefault(dataset_path(p),[]).append(p)
        used=sum(p.stat().st_size for files in groups.values() for p in files)
        free=shutil.disk_usage(root).free;limit=min(limit_bytes(root),max(0,free+used-2_000_000_000))
        # Retain the most recently used complete dataset even if a user's
        # unusually small quota is less than that one indivisible dataset.
        complete=[(key,files) for key,files in groups.items() if key.exists()]
        newest=max(complete,key=lambda row:max(p.stat().st_mtime for p in row[1]),default=None)
        if newest:limit=max(limit,sum(p.stat().st_size for p in newest[1]))
        for key,files in sorted(groups.items(),key=lambda row:max(p.stat().st_mtime for p in row[1])):
            if used<=limit:break
            if key.name in ('regional-index.geojson','bbbike-index.html'):continue
            if newest is not None and key==newest[0]:continue
            with file_lease(key,wait=False) as available:
                if not available:continue
                for path in files:
                    try:size=path.stat().st_size;path.unlink();used-=size;event('osm-cache-evicted',file=path.name,bytes=size)
                    except FileNotFoundError:pass


def admit_peer(path,url,limit,event=lambda *a,**k:None):
    """Consume only complete, verified source files staged by the LAN receiver.

    The network receiver authenticates its host and signed manifest before
    writing this commit receipt. Recheck size, freshness, URL and content hash
    here; never accept a peer SQLite index, code, or prepared physics model.
    """
    root=Path(path).parent;incoming=root/'Incoming'
    if not incoming.is_dir() or incoming.is_symlink():return None
    candidates=[]
    for metadata in list(incoming.glob('*.receipt.json'))[:256]:
        try:
            if metadata.is_symlink() or metadata.stat().st_size>32768:continue
            row=json.loads(metadata.read_text());sha=row.get('sha256','');size=row.get('bytes');stamp=row.get('fetchedUTC');fingerprint=row.get('peerFingerprint','')
            if row.get('url')!=url or row.get('origin')!='authenticated-gameplay-peer' or row.get('licence')!='ODbL-1.0' or not isinstance(sha,str) or len(sha)!=64 or any(c not in '0123456789abcdef' for c in sha):continue
            if not isinstance(fingerprint,str) or len(fingerprint)!=64 or any(c not in '0123456789abcdef' for c in fingerprint):continue
            if isinstance(size,bool) or not isinstance(size,int) or not 0<size<=limit or isinstance(stamp,bool) or not isinstance(stamp,(int,float)) or not 0<=time.time()-stamp<7*86400:continue
            source=incoming/(sha+'.osm.pbf')
            if source.is_symlink() or source.stat().st_size!=size:continue
            candidates.append((stamp,source,metadata,row))
        except (OSError,ValueError,TypeError):continue
    for _,source,metadata,row in sorted(candidates,key=lambda value:-value[0]):
        with file_lease(source,wait=False) as held:
            if not held:continue
            digest=hashlib.sha256()
            try:
                with source.open('rb') as stream:
                    while chunk:=stream.read(1024*1024):digest.update(chunk)
                if digest.hexdigest()!=row['sha256']:continue
                source.replace(path)
                receipt=Path(path).with_suffix(Path(path).suffix+'.receipt.json');temp=receipt.with_suffix('.partial');temp.write_text(json.dumps(row));temp.replace(receipt);metadata.unlink(missing_ok=True)
                event('osm-peer-source-admitted',file=Path(path).name,bytes=row['bytes'],sha256=row['sha256']);return row
            except OSError:continue
    return None
