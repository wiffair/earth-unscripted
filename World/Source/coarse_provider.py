"""Bounded approximate elevation when measured survey tiles are unavailable.

Mapzen Terrarium data is resampled into the requested native raster CRS on a
32 m grid. Both channels are terrain estimates, not measured road-surface LiDAR.
Files sort before native surveys so valid measured pixels replace them. Raw
source tile hashes and the derived raster hashes remain in their receipts.
"""
import hashlib
import json
import math
from pathlib import Path
import time

import numpy as np
from pyproj import Transformer
import rasterio
from rasterio.transform import from_origin

from far_terrain import FarTerrain,ZOOM
from region_frame import geographic_handoff

COARSE_STEP_METRES=32.


def acquire(bounds,frame,raw,event):
    source_bounds=frame.source_bounds(bounds)
    west=math.floor(source_bounds[0]/COARSE_STEP_METRES)*COARSE_STEP_METRES
    south=math.floor(source_bounds[1]/COARSE_STEP_METRES)*COARSE_STEP_METRES
    east=math.ceil(source_bounds[2]/COARSE_STEP_METRES)*COARSE_STEP_METRES
    north=math.ceil(source_bounds[3]/COARSE_STEP_METRES)*COARSE_STEP_METRES
    nx=int(round((east-west)/COARSE_STEP_METRES))
    nz=int(round((north-south)/COARSE_STEP_METRES))
    if min(nx,nz)<1 or nx*nz>1_000_000:
        raise ValueError('Approximate terrain request exceeds bounded raster size')
    identity=hashlib.sha256(json.dumps(dict(bounds=[west,south,east,north],
        frame=frame.identity(),step=COARSE_STEP_METRES,zoom=ZOOM,coordinatePolicy="geographic-wgs84-v1"),sort_keys=True).encode()).hexdigest()[:20]
    jobs=[]
    receipts=[]
    cached=[]
    for kind in ('dtm','dsm'):
        path=raw/('00-coarse-'+identity+'-'+kind+'.tif')
        proof=path.with_suffix('.tif.receipt.json')
        if path.exists() and proof.exists():
            record=json.loads(proof.read_text())
            if hashlib.sha256(path.read_bytes()).hexdigest()==record['sha256']:
                path.touch();proof.touch();record.update(cached=True,bytes=path.stat().st_size,file=path.name,seconds=0)
                cached.append((path,record))
    if len(cached)==2:
        for path,receipt in cached:
            jobs.append((path,receipt['url']));receipts.append(receipt);event('source-derived',**receipt)
        return jobs,receipts

    started=time.perf_counter()
    x,z=np.meshgrid(west+(np.arange(nx)+.5)*COARSE_STEP_METRES,
                    north-(np.arange(nz)+.5)*COARSE_STEP_METRES)
    if frame.world_crs:
        transformer=geographic_handoff(frame.epsg,frame.world_crs)
        world_x,world_z=transformer.transform(x.ravel(),z.ravel())
    else:
        world_x=x.ravel()-frame.origin_x
        world_z=z.ravel()-frame.origin_z
    terrain=FarTerrain(raw.parent,event,frame)
    heights=terrain.sample(world_x,world_z).reshape(nz,nx).astype('float32')
    source_tiles=[]
    for tile_x,tile_z in sorted(terrain.tiles):
        path=terrain.raw/f'{ZOOM}_{tile_x}_{tile_z}.png'
        source_tiles.append(dict(url=f'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{ZOOM}/{tile_x}/{tile_z}.png',
                                 sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    for kind in ('dtm','dsm'):
        path=raw/('00-coarse-'+identity+'-'+kind+'.tif')
        temporary=path.with_suffix('.tif.partial')
        with rasterio.open(temporary,'w',driver='GTiff',width=nx,height=nz,count=1,
            dtype='float32',crs='EPSG:'+str(frame.epsg),compress='deflate',
            transform=from_origin(west,north,COARSE_STEP_METRES,COARSE_STEP_METRES)) as dataset:
            dataset.write(heights,1)
        temporary.replace(path)
        receipt=dict(url='derived:mapzen-terrarium/'+identity,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                     fetchedUTC=time.time(),requestBodySha256=None,sourceTiles=source_tiles,
                     resolutionMetres=COARSE_STEP_METRES,quality='approximate terrain; no measured surface channel')
        proof=path.with_suffix('.tif.receipt.json')
        proof.write_text(json.dumps(receipt))
        row=dict(receipt,file=path.name,bytes=path.stat().st_size,cached=False,seconds=time.perf_counter()-started)
        jobs.append((path,row['url']));receipts.append(row);event('source-derived',**row)
    return jobs,receipts
