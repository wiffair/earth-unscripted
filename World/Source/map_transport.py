"""Read-only map transport with host cooldowns and bounded regional PBF fallback.

Only public Geofabrik extracts are used; never the OSM editing API. Extracts
share Raw's existing LRU budget. Complete way geometry and original node IDs
are retained, including untagged members of selected multipolygons.
"""
import hashlib,json,math,re,shutil,ssl,time,urllib.parse,urllib.request
from contextlib import closing
from pathlib import Path
import certifi
from shapely.geometry import box,shape,LineString

INDEX='https://download.geofabrik.de/index-v1.json'
CAP=256*1024*1024
AGE=7*86400
COOLDOWN=180
_failed={}
_regional_failure={}
_verified={}


def _download(path,url,limit):
    if urllib.parse.urlsplit(url).scheme!='https' or urllib.parse.urlsplit(url).hostname!='download.geofabrik.de':
        raise ValueError('Untrusted regional source')
    receipt=path.with_suffix(path.suffix+'.receipt.json')
    if path.exists() and receipt.exists():
        row=json.loads(receipt.read_text());st=path.stat();key=(str(path),st.st_size,st.st_mtime_ns)
        if row.get('url')==url and time.time()-row['fetchedUTC']<AGE:
            if key not in _verified:_verified[key]=hashlib.sha256(path.read_bytes()).hexdigest()==row['sha256']
            if _verified[key]:
                path.touch();updated=path.stat();_verified[(str(path),updated.st_size,updated.st_mtime_ns)]=True;return row
    if shutil.disk_usage(path.parent).free<limit*2:raise ValueError('Insufficient free space for bounded regional extract')
    partial=path.with_suffix(path.suffix+'.partial');started=time.monotonic();digest=hashlib.sha256();count=0
    try:
        request=urllib.request.Request(url,headers={'User-Agent':'EarthUnscriptedBeta/40 (read-only regional map cache)'})
        with urllib.request.urlopen(request,timeout=20,context=ssl.create_default_context(cafile=certifi.where())) as response,partial.open('wb') as output:
            if int(response.headers.get('Content-Length','0'))>limit:raise ValueError('Regional extract exceeds 256 MiB automatic-download cap')
            while data:=response.read(1024*1024):
                count+=len(data)
                if count>limit or time.monotonic()-started>120:raise ValueError('Regional download exceeded size/time budget')
                output.write(data);digest.update(data)
        if not count:raise ValueError('Empty regional download')
        partial.replace(path)
        row=dict(url=url,bytes=count,sha256=digest.hexdigest(),fetchedUTC=time.time())
        temp=receipt.with_suffix('.partial');temp.write_text(json.dumps(row));temp.replace(receipt)
        return row
    finally:
        partial.unlink(missing_ok=True)


def select_region(index,bounds):
    south,west,north,east=bounds;envelope=box(west,south,east,north)
    candidates=[]
    for row in index['features']:
        geometry=shape(row['geometry']);url=row['properties'].get('urls',{}).get('pbf')
        if url and geometry.covers(envelope):candidates.append((geometry.area,row['properties']))
    if not candidates:raise ValueError('No complete regional extract covers requested bounds')
    return min(candidates,key=lambda row:row[0])[1]


def _safe_area_relations(data):
    # Malformed optional multipolygons with point members for their entire
    # outer ring cannot describe an area. Retain all actual ways/roads; do not
    # let an invalid building relation cancel a whole drivable core.
    bad=[];kept=[]
    for row in data.get('elements',[]):
        tags=row.get('tags',{})
        mapped=tags.get('building') or tags.get('natural') in ('water','wood') or tags.get('landuse')=='forest' or tags.get('waterway')=='riverbank'
        if row.get('type')=='relation' and mapped and tags.get('type') in ('multipolygon','boundary',None):
            outer=[m for m in row.get('members',[]) if m.get('role','') in ('','outer','outline')]
            if outer and all(m.get('type')=='node' for m in outer):bad.append(row['id']);continue
        kept.append(row)
    return dict(data,elements=kept),bad


def _sanitize(path,event,result):
    data,bad=_safe_area_relations(json.loads(path.read_bytes()))
    if not bad:return result
    previous=result['sha256'];raw=json.dumps(data,separators=(',',':')).encode();temp=path.with_suffix('.partial');temp.write_bytes(raw);temp.replace(path)
    receipt_path=path.with_suffix(path.suffix+'.receipt.json');row=json.loads(receipt_path.read_text());row.update(downloadSha256=previous,sha256=hashlib.sha256(raw).hexdigest(),excludedMalformedAreaRelations=bad)
    temp=receipt_path.with_suffix('.partial');temp.write_text(json.dumps(row));temp.replace(receipt_path)
    event('malformed-optional-area-omitted',relationIds=bad,downloadSha256=previous,sha256=row['sha256'])
    return dict(result,sha256=row['sha256'],bytes=len(raw))


def indexed_extract(path,bounds,controls=False,event=lambda *a,**k:None):
    """One region import, then indexed queries; never rescan a PBF per core."""
    import osmium,sqlite3,zlib
    south,west,north,east=bounds;requested=box(west,south,east,north)
    source=path.stat();stamp=[source.st_size,source.st_mtime_ns]
    source_sha=json.loads(path.with_suffix(path.suffix+".receipt.json").read_text()).get("sha256")
    cx=math.floor((west+east)*5)/10;cy=math.floor((south+north)*5)/10
    coverage=box(min(cx-.05,west),min(cy-.05,south),max(cx+.15,east),max(cy+.15,north))
    cx0,cy0,cx1,cy1=coverage.bounds
    db=path.with_name(path.stem+f'-{cy:.1f}-{cx:.1f}.sqlite');ready=False
    for candidate in sorted(path.parent.glob(path.stem+'-*.sqlite'),key=lambda p:-p.stat().st_mtime):
        receipt=candidate.with_suffix('.sqlite.receipt.json')
        if not receipt.exists():continue
        row=json.loads(receipt.read_text())
        if row.get('sourceSha256')==source_sha and source_sha is not None and box(*row['coverage']).covers(requested):
            # Validate a persistent index once per file version before reuse.
            st=candidate.stat();key=(str(candidate),st.st_size,st.st_mtime_ns)
            if key not in _verified:_verified[key]=hashlib.sha256(candidate.read_bytes()).hexdigest()==row['sha256']
            if _verified[key]:db=candidate;ready=True;break
    receipt=db.with_suffix('.sqlite.receipt.json')
    if not ready:
        temp=db.with_suffix('.sqlite.partial');temp.unlink(missing_ok=True)
        connection=sqlite3.connect(temp)
        try:
            connection.executescript('PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; CREATE TABLE objects(id INTEGER PRIMARY KEY,data BLOB); CREATE VIRTUAL TABLE spatial USING rtree(id,minx,maxx,miny,maxy); CREATE TABLE nodes(id INTEGER PRIMARY KEY,x REAL,y REAL,data BLOB); CREATE VIRTUAL TABLE node_spatial USING rtree(id,minx,maxx,miny,maxy); CREATE TABLE relations(id INTEGER PRIMARY KEY,data BLOB); CREATE TABLE members(way INTEGER,relation INTEGER);')
            class Import(osmium.SimpleHandler):
                count=0;node_count=0;needed=set();included=set()
                def node(self,n):
                    self.node_count+=1
                    if self.node_count%500000==0:event("regional-index-progress",phase="nodes",objects=self.node_count)
                    if not n.location.valid() or not (cx0<=n.location.lon<=cx1 and cy0<=n.location.lat<=cy1):return
                    tags=dict(n.tags)
                    if not tags or not n.location.valid():return
                    if tags.get('place') not in ('city','town','village','hamlet','suburb','neighbourhood') and tags.get('highway') not in ('traffic_signals','stop','give_way','crossing') and 'traffic_sign' not in tags:return
                    x,y=n.location.lon,n.location.lat;data=dict(type='node',id=n.id,lon=x,lat=y,tags=tags)
                    connection.execute('INSERT INTO nodes VALUES(?,?,?,?)',(n.id,x,y,zlib.compress(json.dumps(data,separators=(',',':')).encode(),1)))
                    connection.execute('INSERT INTO node_spatial VALUES(?,?,?,?,?)',(n.id,x,x,y,y))
                def way(self,w):
                    if len(w.nodes)<2 or any(not n.location.valid() for n in w.nodes):return
                    coords=[(n.lon,n.lat) for n in w.nodes];xs,ys=zip(*coords)
                    if (max(xs)<cx0 or min(xs)>cx1 or max(ys)<cy0 or min(ys)>cy1) and w.id not in self.needed:return
                    self.included.add(w.id)
                    data=dict(type='way',id=w.id,nodes=[n.ref for n in w.nodes],tags=dict(w.tags),geometry=[dict(lon=x,lat=y) for x,y in coords])
                    connection.execute('INSERT INTO objects VALUES(?,?)',(w.id,zlib.compress(json.dumps(data,separators=(',',':')).encode(),1)))
                    connection.execute('INSERT INTO spatial VALUES(?,?,?,?,?)',(w.id,min(xs),max(xs),min(ys),max(ys)))
                    self.count+=1
                    if self.count%10000==0:
                        event("regional-index-progress",phase="ways",objects=self.count)
                        connection.commit()
                        if temp.stat().st_size>512*1024*1024:raise ValueError('Regional map index exceeds 512 MiB cap')
                def relation(self,r):
                    tags=dict(r.tags)
                    if not(any(k in tags for k in ('building','waterway')) or tags.get('natural') in ('wood','water') or tags.get('landuse')=='forest'):return
                    members=[dict(type={'w':'way','n':'node','r':'relation'}[m.type],ref=m.ref,role=m.role) for m in r.members]
                    refs={m['ref'] for m in members if m['type']=='way'}
                    if not refs & self.included:return
                    self.needed.update(refs)
                    data=dict(type='relation',id=r.id,tags=tags,members=members)
                    connection.execute('INSERT INTO relations VALUES(?,?)',(r.id,zlib.compress(json.dumps(data,separators=(',',':')).encode(),1)))
                    connection.executemany('INSERT INTO members VALUES(?,?)',[(m['ref'],r.id) for m in members if m['type']=='way'])
            importer=Import();importer.apply_file(str(path),locations=True,idx='sparse_mem_array')
            missing=importer.needed-importer.included
            if missing:
                class Missing(osmium.SimpleHandler):
                    def way(self,w):
                        if w.id in missing:importer.way(w)
                Missing().apply_file(str(path),locations=True,idx='sparse_mem_array')
            connection.execute('CREATE INDEX member_way ON members(way)');connection.commit();connection.close()
            if temp.stat().st_size>512*1024*1024:raise ValueError('Regional index exceeds size cap')
            temp.replace(db);receipt.write_text(json.dumps(dict(sourceStamp=stamp,sourceSha256=source_sha,coverage=list(coverage.bounds),url=json.loads(path.with_suffix(path.suffix+'.receipt.json').read_text())['url'],sha256=hashlib.sha256(db.read_bytes()).hexdigest(),fetchedUTC=time.time())))
        finally:
            try:connection.close()
            except Exception:pass
            temp.unlink(missing_ok=True)
    south,west,north,east=bounds;envelope=box(west,south,east,north)
    with closing(sqlite3.connect('file:'+str(db)+'?mode=ro',uri=True)) as connection:
        objects={};elements=[]
        for blob, in connection.execute('SELECT n.data FROM nodes n JOIN node_spatial s ON s.id=n.id WHERE s.maxx>=? AND s.minx<=? AND s.maxy>=? AND s.miny<=?',(west,east,south,north)):
            row=json.loads(zlib.decompress(blob));tags=row['tags']
            if (tags.get('highway') in ('traffic_signals','stop','give_way','crossing') or 'traffic_sign' in tags) if controls else tags.get('place') in ('city','town','village','hamlet','suburb','neighbourhood'):elements.append(row)
        if not controls:
            related=set();selected=set()
            for object_id,blob in connection.execute('SELECT o.id,o.data FROM objects o JOIN spatial s ON s.id=o.id WHERE s.maxx>=? AND s.minx<=? AND s.maxy>=? AND s.miny<=?',(west,east,south,north)):
                row=json.loads(zlib.decompress(blob))
                if not LineString([(p['lon'],p['lat']) for p in row['geometry']]).intersects(envelope):continue
                objects[object_id]=row;tags=row['tags']
                if any(k in tags for k in ('highway','railway','waterway','building')) or tags.get('natural') in ('wood','water') or tags.get('landuse')=='forest':selected.add(object_id)
                related.update(r[0] for r in connection.execute('SELECT relation FROM members WHERE way=?',(object_id,)))
            for relation_id in sorted(related):
                record=connection.execute('SELECT data FROM relations WHERE id=?',(relation_id,)).fetchone()
                if not record:continue
                row=json.loads(zlib.decompress(record[0]));complete=True;refs=[]
                for member in row['members']:
                    if member['type']!='way':continue
                    key=member['ref'];refs.append(key)
                    if key not in objects:
                        record=connection.execute('SELECT data FROM objects WHERE id=?',(key,)).fetchone()
                        if not record:complete=False;break
                        objects[key]=json.loads(zlib.decompress(record[0]))
                    member['geometry']=objects[key]['geometry']
                if complete:elements.append(row);selected.update(refs)
            elements.extend(objects[key] for key in sorted(selected))
    db.touch()
    if len(elements)>200000:raise ValueError('Regional area exceeds feature budget')
    return dict(version=0.6,generator='EarthUnscripted indexed Geofabrik fallback',elements=elements)


def regional(path,bounds,query,event,original_error=None):
    root=path.parent;key=str(root)
    if time.monotonic()<_regional_failure.get(key,0):raise RuntimeError('Regional fallback cooling down after failure')
    started=time.monotonic()
    try:
        index_path=root/'regional-index.geojson';_download(index_path,INDEX,6*1024*1024)
        region=select_region(json.loads(index_path.read_bytes()),bounds)
        pbf=root/('regional-'+region['id'].replace('/','-')+'.osm.pbf')
        source=_download(pbf,region['urls']['pbf'],CAP)
        controls='out body geom' not in query
        source_data,bad=_safe_area_relations(indexed_extract(pbf,bounds,controls,event))
        if bad:event('malformed-optional-area-omitted',relationIds=bad)
        data=json.dumps(source_data,separators=(',',':')).encode()
        if len(data)>32*1024*1024:raise ValueError('Extracted map exceeds response budget')
        if not controls and not json.loads(data)['elements']:raise ValueError('Empty regional road area')
        temp=path.with_suffix('.partial');temp.write_bytes(data);temp.replace(path)
        receipt=dict(url=source['url'],sha256=hashlib.sha256(data).hexdigest(),fetchedUTC=source['fetchedUTC'],regional=True,sourceSha256=source['sha256'])
        receipt_path=path.with_suffix(path.suffix+'.receipt.json');temp=receipt_path.with_suffix('.partial');temp.write_text(json.dumps(receipt));temp.replace(receipt_path)
        result=dict(file=path.name,url=source['url'],bytes=len(data),sha256=receipt['sha256'],cached=False,seconds=time.monotonic()-started,regional=True)
        event('source',**result);return result
    except Exception:
        _regional_failure[key]=time.monotonic()+COOLDOWN
        raise


def install(pipeline):
    original=pipeline.fetch
    def fetch(path,url,event,body=None,**kwargs):
        if body is None or urllib.parse.urlsplit(url).path!='/api/interpreter':return original(path,url,event,body=body,**kwargs)
        fields=urllib.parse.parse_qs(body.decode('ascii'),strict_parsing=True)
        if set(fields)!={'data'}:return original(path,url,event,body=body,**kwargs)
        query=fields['data'][0]
        regional_allowed=('way[highway]' in query and 'way[railway]' in query) or ('node[highway~' in query and 'node[traffic_sign]' in query)
        boxes=re.findall(r'\((-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)\)',query)
        if not boxes:raise ValueError('Map query must have bounded geography')
        bounds=tuple(map(float,boxes[0]));south,west,north,east=bounds
        if urllib.parse.urlsplit(url).hostname=='overpass.atownsend.org.uk' and not(-9<=west<=east<=2 and 49<=south<=north<=61):
            # Use regional public coverage before the overloaded global mirrors.
            if -25<=west<=east<=45 and 34<=south<=north<=72:url='https://ovp.dresel.systems/api/interpreter'
            elif -170<=west<=east<=-50 and 5<=south<=north<=84:url='https://ovp.dresel.systems/api-na/interpreter'
            else:url='https://maps.mail.ru/osm/tools/overpass/api/interpreter'
        host=urllib.parse.urlsplit(url).hostname
        url+='?'+urllib.parse.urlencode({'data':query})
        receipt_path=path.with_suffix(path.suffix+'.receipt.json')
        if path.exists() and receipt_path.exists():
            receipt=json.loads(receipt_path.read_text())
            if regional_allowed and receipt.get('regional') and time.time()-receipt['fetchedUTC']<min(AGE,pipeline.OSM_MAX_AGE_SECONDS) and pipeline.sha(path)==receipt['sha256']:
                parsed=json.loads(path.read_bytes())
                if not pipeline.incomplete_osm_geometry(parsed,bounds):
                    path.touch();event('source',file=path.name,url=receipt['url'],cached=True,regional=True,seconds=0)
                    return dict(file=path.name,url=receipt['url'],sha256=receipt['sha256'],bytes=path.stat().st_size,cached=True,regional=True,seconds=0)
            elif receipt.get('url')==url and time.time()-receipt.get('fetchedUTC',0)<pipeline.OSM_MAX_AGE_SECONDS and pipeline.sha(path)==receipt['sha256']:
                return _sanitize(path,event,original(path,url,event,body=None,**kwargs))
        # Coast/beach and other query types must never receive road-area data.
        if not regional_allowed:
            if time.monotonic()<_failed.get(host,0):raise TimeoutError('Map host cooling down; optional query deferred')
            try:return original(path,url,event,body=None,**kwargs)
            except Exception as error:
                _failed[host]=time.monotonic()+COOLDOWN
                event('map-host-cooldown',host=host,seconds=COOLDOWN,error=str(error))
                raise
        # A completed local spatial index avoids even a health probe during
        # subsequent area requests. Keep the original road/terrain cache IDs.
        for cached_index in path.parent.glob('regional-*.sqlite.receipt.json'):
            try:
                row=json.loads(cached_index.read_text())
                if time.time()-row['fetchedUTC']<AGE and box(*row['coverage']).covers(box(west,south,east,north)):
                    try:return regional(path,bounds,query,event)
                    except Exception as error:event('regional-source-retry',error=str(error))
                    break
            except (OSError,ValueError,KeyError,TypeError):continue
        # After one outage use the local extract for later areas and controls;
        # duplicated mirrors cannot each consume a full 35-second timeout.
        if time.monotonic()<_failed.get(host,0):return regional(path,bounds,query,event)
        kwargs['download_timeout']=min(kwargs.get('download_timeout',35),10)
        try:return _sanitize(path,event,original(path,url,event,body=None,**kwargs))
        except Exception as error:
            _failed[host]=time.monotonic()+COOLDOWN
            event('map-host-cooldown',host=host,seconds=COOLDOWN,error=str(error))
            return regional(path,bounds,query,event,error)
    pipeline.fetch=fetch
