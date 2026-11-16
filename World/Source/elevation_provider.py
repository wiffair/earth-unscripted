"""Public measured elevation acquisition for England and Swiss test regions.

Returns immutable native rasters with receipts. The numeric builder samples
physical pixel sizes rather than assuming one metre. England uses bounded WCS
1 km crops; Switzerland uses official STAC COG assets, selecting the newest
available tile and preferring the 2 m terrain product over larger variants.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import math
import time
import urllib.error
from email.utils import parsedate_to_datetime
import urllib.parse
import zipfile
from pyproj import Transformer
from region_frame import british_grid_origin,british_grid_tile

EA_DATASETS={
    'dtm':('lidar-composite-digital-terrain-model-dtm-1m','13787b9a-26a4-4775-8523-806d13af58fc__Lidar_Composite_Elevation_DTM_1m'),
    'dsm':('lidar-composite-digital-surface-model-last-return-dsm-1m','9ba4d5ac-d596-445a-9056-dae3ddec0178__Lidar_Composite_Elevation_LZ_DSM_1m')}
SWISS_COLLECTIONS={'dtm':'ch.swisstopo.swissalti3d','dsm':'ch.swisstopo.swisssurface3d-raster'}
SOURCE_TILE_METRES=1000
EA_REQUEST_INTERVAL_SECONDS=5.
EA_RATE_LIMIT_BACKOFF_SECONDS=30.
EA_BULK_LIMIT_BYTES=160*1024*1024
EA_TILE_SEARCH_URL='https://environment.data.gov.uk/backend/catalog/api/tiles/collections/survey/search'
EA_PRODUCTS={'dtm':'lidar_composite_dtm','dsm':'lidar_composite_last_return_dsm'}


def read_native_archive(archive):
    """Validate the bounded TIFF payload, including ZIP CRC, before caching it."""
    with zipfile.ZipFile(archive) as package:
        candidates=[item for item in package.infolist() if item.filename.lower().endswith('.tif')]
        if len(candidates)!=1 or candidates[0].file_size>EA_BULK_LIMIT_BYTES:
            raise ValueError('Unexpected elevation archive contents')
        data=package.read(candidates[0])
    if data[:4] not in (b'II*\x00',b'MM\x00*',b'II+\x00',b'MM\x00+'):
        raise ValueError('Invalid archived TIFF')
    return data


def acquire_england_bulk(bounds,frame,raw,fetch,event):
    """Acquire official 5 km ZIP tiles; retain verified native raster receipts.

    The official survey client publishes this catalogue and download API. A
    bounded archive is never blindly extracted: exactly one native TIFF is
    copied to an owned cache path, with both archive and extracted hashes.
    Valid extracted rasters bypass archive downloads on subsequent requests.
    """
    if frame.epsg!=27700:raise ValueError('England elevation requires EPSG:27700')
    west,south,east,north=frame.geographic_bounds(bounds)
    geometry=dict(type='Polygon',coordinates=[[[west,south],[east,south],[east,north],[west,north],[west,south]]])
    body=json.dumps(geometry,separators=(',',':')).encode()
    catalogue=raw/('ea-survey-'+hashlib.sha256(body).hexdigest()[:20]+'.catalogue')
    receipts=[fetch(catalogue,EA_TILE_SEARCH_URL,event,body,content_type='application/geo+json')]
    rows=json.loads(catalogue.read_text()).get('results',[])
    selected={kind:[row for row in rows if row['product']['id']==product and row['year']['id']=='2022' and row['resolution']['id']=='1'] for kind,product in EA_PRODUCTS.items()}
    if any(not rows or len(rows)>8 for rows in selected.values()):raise ValueError('No bounded complete native England bulk coverage')
    jobs=[]
    for kind,rows in selected.items():
        for row in rows:
            tile=row['tile']['id'];url=row['uri']
            if not tile.isalnum() or not url.startswith('https://environment.data.gov.uk/tiles/collections/survey/'):raise ValueError('Unexpected official tile identity')
            raster=raw/f'ea-bulk-2022-{tile}-{kind}.tif';receipt_path=raster.with_suffix('.tif.receipt.json')
            if raster.exists() and receipt_path.exists():
                previous=json.loads(receipt_path.read_text())
                if previous['url']==url and previous['sha256']==hashlib.sha256(raster.read_bytes()).hexdigest():
                    raster.touch();receipt_path.touch();receipt=dict(previous,cached=True,bytes=raster.stat().st_size,file=raster.name,seconds=0)
                    receipts.append(receipt);event('source',**receipt);jobs.append((raster,url));continue
            archive=raw/f'ea-bulk-2022-{tile}-{kind}.zip'
            archive_receipt=None
            for attempt in range(3):
                try:
                    archive_receipt=fetch(archive,url,event,max_bytes=EA_BULK_LIMIT_BYTES)
                    data=read_native_archive(archive)
                    break
                except (urllib.error.URLError,TimeoutError,ConnectionError,ValueError,zipfile.BadZipFile,EOFError) as error:
                    archive_receipt=None
                    if isinstance(error,(ValueError,zipfile.BadZipFile,EOFError)):
                        # A 200 response can still be a partially assembled ZIP.
                        # Discard its owned cache pair so retry cannot reuse it.
                        archive.unlink(missing_ok=True)
                        archive.with_suffix('.zip.receipt.json').unlink(missing_ok=True)
                    # A cold official archive can briefly return 404 while it
                    # is assembled. Retry that tile, then crop only its missing
                    # product/overlap; keep all successful bulk rasters.
                    if attempt<2:
                        delay=2**(attempt+1)
                        event('source-tile-retry',provider='Environment Agency',tile=tile,productKind=kind,url=url,seconds=delay,error=str(error))
                        time.sleep(delay)
                        continue
                    tile_x,tile_z=british_grid_origin(tile)
                    absolute=frame.source_bounds(bounds)
                    overlap=[max(absolute[0],tile_x),max(absolute[1],tile_z),min(absolute[2],tile_x+5000),min(absolute[3],tile_z+5000)]
                    if overlap[0]>=overlap[2] or overlap[1]>=overlap[3]:break
                    local=frame.world_bounds(overlap)
                    event('source-tile-fallback',provider='Environment Agency',tile=tile,productKind=kind,url=url,error=str(error),bounds=local)
                    missing_jobs,missing_receipts=acquire_england(local,frame,raw,fetch,event,kinds={kind},native_bounds=overlap)
                    jobs.extend(missing_jobs);receipts.extend(missing_receipts)
            if archive_receipt is None:continue
            receipts.append(archive_receipt)
            temporary=raster.with_suffix('.tif.partial');temporary.write_bytes(data);temporary.replace(raster)
            receipt=dict(url=url,sha256=hashlib.sha256(data).hexdigest(),archiveSha256=archive_receipt['sha256'],fetchedUTC=time.time(),requestBodySha256=None)
            temporary=receipt_path.with_suffix('.partial');temporary.write_text(json.dumps(receipt));temporary.replace(receipt_path)
            receipts.append(dict(receipt,cached=False,file=raster.name,bytes=len(data),seconds=0));jobs.append((raster,url))
    return jobs,receipts


def acquire_england(bounds,frame,raw,fetch,event,kinds=None,native_bounds=None):
    """Fetch native 1 m floating point DTM/DSM crops in EPSG:27700."""
    if frame.epsg!=27700:raise ValueError('England elevation requires the national grid frame')
    jobs=[]
    west,south,east,north=native_bounds if native_bounds is not None else frame.source_bounds(bounds)
    for x in range(math.floor(west/1000)*1000,math.ceil(east/1000)*1000,1000):
        for z in range(math.floor(south/1000)*1000,math.ceil(north/1000)*1000,1000):
            for kind,(service,coverage) in EA_DATASETS.items():
                if kinds is not None and kind not in kinds:continue
                arguments=[('service','WCS'),('version','2.0.1'),('request','GetCoverage'),('coverageId',coverage),('format','image/tiff'),('subset',f'E({x},{x+1000})'),('subset',f'N({z},{z+1000})')]
                url='https://environment.data.gov.uk/spatialdata/'+service+'/wcs?'+urllib.parse.urlencode(arguments)
                jobs.append((raw/f'ea-2022-{x}-{z}-{kind}.tif',url))
    # The public coverage service rate-limits bursts. Reuse every successful
    # crop, serialize misses and respect Retry-After rather than restarting a
    # complete region or repeatedly hammering a rejected URL.
    receipts=[];next_request=0.
    for path,url in jobs:
        cached=False;receipt=path.with_suffix(path.suffix+'.receipt.json')
        if path.exists() and receipt.exists():
            previous=json.loads(receipt.read_text())
            cached=previous['url']==url and previous['sha256']==hashlib.sha256(path.read_bytes()).hexdigest()
        if not cached:
            delay=max(0,next_request-time.monotonic())
            if delay:time.sleep(delay)
        for attempt in range(5):
            try:
                result=fetch(path,url,event);receipts.append(result)
                if not result['cached']:next_request=time.monotonic()+EA_REQUEST_INTERVAL_SECONDS
                break
            except urllib.error.HTTPError as error:
                if error.code not in (429,503) or attempt==4:raise
                value=error.headers.get('Retry-After')
                try:delay=float(value)
                except (TypeError,ValueError):
                    try:delay=max(0,parsedate_to_datetime(value).timestamp()-time.time())
                    except (TypeError,ValueError):delay=min(120,EA_RATE_LIMIT_BACKOFF_SECONDS*2**attempt)
                delay=max(EA_RATE_LIMIT_BACKOFF_SECONDS,delay);event('source-throttle',provider='Environment Agency',seconds=delay,attempt=attempt+1,url=url)
                deadline=time.monotonic()+delay
                while time.monotonic()<deadline:time.sleep(max(0,min(1,deadline-time.monotonic())))

            except (TimeoutError,urllib.error.URLError,ConnectionError) as error:
                if attempt==4:raise
                delay=min(30,2**(attempt+1));event('source-network-retry',provider='Environment Agency',seconds=delay,attempt=attempt+1,error=str(error))
                time.sleep(delay)
    return jobs,receipts


def acquire_switzerland(bounds,frame,raw,fetch,event):
    """Read bounded official STAC catalogues; original COG bytes remain cached."""
    if frame.epsg!=2056:raise ValueError('Swiss elevation requires LV95 / EPSG:2056')
    geographic=frame.geographic_bounds(bounds);bbox=','.join(f'{p:.9f}' for p in geographic)
    jobs=[];receipts=[]
    for kind,collection in SWISS_COLLECTIONS.items():
        url='https://data.geo.admin.ch/api/stac/v1/collections/'+collection+'/items?'+urllib.parse.urlencode(dict(bbox=bbox,limit=100))
        items=[]
        for page in range(8):
            key=hashlib.sha256(url.encode()).hexdigest()[:20];path=raw/f'ch-{kind}-{key}.geojson'
            receipts.append(fetch(path,url,event));data=json.loads(path.read_text());items.extend(data['features'])
            following=[link['href'] for link in data.get('links',[]) if link.get('rel')=='next']
            if not following:break
            url=following[0]
        else:raise ValueError('Swiss catalogue exceeds bounded pagination')
        selected={}
        for item in items:
            assets=[(name,asset) for name,asset in item['assets'].items() if name.endswith('.tif') and asset.get('proj:epsg')==frame.epsg]
            if not assets:continue
            desired=2. if kind=='dtm' else .5
            name,asset=min(assets,key=lambda pair:abs(float(pair[1].get('gsd',desired))-desired))
            # Item ids encode acquisition year and projected kilometre cell.
            parts=item['id'].split('_');cell=parts[-1];year=int(parts[-2]);old=selected.get(cell)
            if old is None or year>old[0]:selected[cell]=(year,name,asset)
        if not selected:raise ValueError('No measured Swiss elevation assets cover this request')
        for year,name,asset in selected.values():
            path=raw/(name[:-4]+'-'+kind+'.tif');jobs.append((path,asset['href']))
    with ThreadPoolExecutor(max_workers=4) as pool:
        receipts.extend(pool.map(lambda job:fetch(*job,event),jobs))
    return jobs,receipts


def acquire_britain(bounds,frame,raw,fetch,event,catalogue):
    """Combine Welsh and English surveys without stopping at their border.

    One bounded Welsh catalogue request classifies the unknown native 1 km
    cells. English gaps are grouped within 5 km archive cells. The approximate
    background remains available if a survey product/catalogue is unavailable;
    receipts distinguish estimates from measured DTM/DSM source products.
    """
    import coarse_provider
    jobs,receipts=coarse_provider.acquire(bounds,frame,raw,event)
    west,south,east,north=frame.source_bounds(bounds)
    cells={(x,z) for x in range(math.floor(west/1000),math.floor(east/1000)+1)
                   for z in range(math.floor(south/1000),math.floor(north/1000)+1)}
    if len(cells)>1024:raise ValueError('British survey cell request exceeds bound')
    identities={}
    for cell in cells:
        try:identities[cell]=british_grid_tile(*cell)
        except ValueError:pass  # estimated background supports travel beyond the national grid
    unknown=sorted(identity for identity in identities.values() if identity not in catalogue)
    if unknown:
        selection='british_gr IN ('+','.join("'"+identity+"'" for identity in unknown)+')'
        parameters=dict(service='WFS',version='2.0.0',request='GetFeature',
            typeNames='geonode:welsh_government_lidar_tile_catalogue_2020_2023',
            outputFormat='application/json',count=len(unknown)+1,
            propertyName='british_gr,dtm_link,dsm_link',CQL_FILTER=selection)
        url='https://datamap.gov.wales/geoserver/wfs?'+urllib.parse.urlencode(parameters)
        key=hashlib.sha256(url.encode()).hexdigest()[:20]
        path=raw/('welsh-cells-'+key+'.geojson')
        try:
            receipts.append(fetch(path,url,event))
            matches={}
            for row in json.loads(path.read_text())['features']:
                record=row['properties'];identity=record['british_gr']
                if identity in matches:raise ValueError('Ambiguous Welsh survey cell '+identity)
                if identity in unknown:matches[identity]=record
            for identity in unknown:catalogue[identity]=matches.get(identity)
        except Exception as error:
            event('survey-unavailable',provider='Welsh catalogue',error=str(error))

    welsh=[]
    english={}
    for cell,identity in identities.items():
        entry=catalogue.get(identity)
        if entry:
            for kind in ('dtm','dsm'):
                link=entry.get(kind+'_link')
                if not isinstance(link,str) or not link:
                    event('survey-unavailable',provider='Welsh catalogue',tile=identity,product=kind,error='No raster link')
                    continue
                if link.startswith('http://'):link='https://'+link[7:]
                welsh.append((raw/(identity+'-'+kind+'.tif'),link if link.startswith('https://') else 'https://'+link))
        else:
            english.setdefault((cell[0]//5,cell[1]//5),[]).append(cell)
    with ThreadPoolExecutor(max_workers=4) as pool:
        futures=[(job,pool.submit(fetch,*job,event)) for job in welsh]
        for job,future in futures:
            try:
                receipts.append(future.result());jobs.append(job)
            except Exception as error:
                event('survey-unavailable',provider='Welsh raster',file=job[0].name,error=str(error))
    for group in english.values():
        native=[min(c[0] for c in group)*1000,min(c[1] for c in group)*1000,
                (max(c[0] for c in group)+1)*1000,(max(c[1] for c in group)+1)*1000]
        try:
            local=frame.world_bounds(native)
            new_jobs,new_receipts=acquire_england_bulk(local,frame,raw,fetch,event)
            jobs.extend(new_jobs);receipts.extend(new_receipts)
        except Exception as error:
            event('survey-unavailable',provider='England survey',bounds=native,error=str(error))
    return list({str(path):(path,url) for path,url in jobs}.values()),list({row['file']:row for row in receipts}.values())


def acquire(bounds,frame,raw,fetch,event):
    if frame.provider=='coarse':
        import coarse_provider
        return coarse_provider.acquire(bounds,frame,raw,event)
    if frame.provider=='swiss-or-coarse':
        import coarse_provider
        jobs,receipts=coarse_provider.acquire(bounds,frame,raw,event)
        try:
            measured,proofs=acquire_switzerland(bounds,frame,raw,fetch,event)
            jobs.extend(measured);receipts.extend(proofs)
        except Exception as error:
            event('survey-unavailable',provider='Swiss survey',error=str(error))
        return jobs,receipts
    if frame.provider=='england':
        try:return acquire_england_bulk(bounds,frame,raw,fetch,event)
        except Exception as error:
            event('source-provider-fallback',provider='Environment Agency',strategy='1 km WCS crops',error=str(error))
            return acquire_england(bounds,frame,raw,fetch,event)
    if frame.provider=='switzerland':return acquire_switzerland(bounds,frame,raw,fetch,event)
    raise ValueError('Unsupported elevation provider '+frame.provider)
