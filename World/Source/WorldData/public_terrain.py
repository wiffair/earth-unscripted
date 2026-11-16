"""Account-free numeric height crops. Imagery/relief tiles are never elevation.

Coarse background is followed by valid native survey pixels. Negative coverage
is cached separately; outages trigger a bounded provider cooldown, not a loop.
Only these versioned bindings affect the numeric generator identity.
"""
import hashlib,json,math,time
from urllib.parse import urlencode
from pathlib import Path
import numpy as np
import rasterio

BINDINGS={'nl-ahn-v1':('terrain.nl.ahn',28992,.5,('dtm','dsm'),'NAP'),
          'ca-hrdem-v1':('terrain.ca.hrdem',3979,1.,('dtm','dsm'),'Native HRDEM source datum; no vertical conversion'),
          'us-3dep-v1':('terrain.us.3dep',3857,1.,('dtm',),'NAVD88 / source-specific; no vertical conversion')}
_COOLDOWN={}

def requests(binding,x,y,size=1000):
    provider,epsg,res,kinds,datum=BINDINGS[binding]
    for kind in kinds:
        if binding.startswith('nl-'):
            args=[('service','WCS'),('version','2.0.1'),('request','GetCoverage'),('coverageId',kind+'_05m'),('format','image/tiff'),('subset',f'x({x},{x+size})'),('subset',f'y({y},{y+size})')]
            url='https://service.pdok.nl/rws/actueel-hoogtebestand-nederland/wcs/v1_0?'+urlencode(args)
        elif binding.startswith('ca-'):
            args=dict(service='WCS',version='1.1.1',request='GetCoverage',identifier=kind,format='image/geotiff',boundingbox=f'{x},{y},{x+size},{y+size},urn:ogc:def:crs:EPSG::3979',GridBaseCRS='urn:ogc:def:crs:EPSG::3979',GridOffsets='1,-1')
            url='https://datacube.services.geo.ca/ows/elevation?'+urlencode(args)
        else:
            args=dict(f='image',bbox=f'{x},{y},{x+size},{y+size}',bboxSR=epsg,imageSR=epsg,size=f'{size},{size}',format='tiff',pixelType='F32',noData=-999999,interpolation='RSP_NearestNeighbor')
            url='https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/exportImage?'+urlencode(args)
        yield kind,url

def inspect(path,epsg):
    with rasterio.open(path) as raster:
        if raster.count!=1 or raster.crs is None or raster.crs.to_epsg()!=epsg or raster.width*raster.height>5000000 or raster.dtypes[0] not in ('float32','float64','int16','int32'):
            raise ValueError('Not a bounded single-band numeric elevation crop')
        values=raster.read(1,masked=True).astype("float32");valid=np.asarray(~np.ma.getmaskarray(values))&np.isfinite(values.filled(np.nan))
        valid &= (values.filled(-999999)>-500)&(values.filled(999999)<9000)
        if not valid.any():return None
        return dict(gridSpacingMetres=[abs(raster.res[0]),abs(raster.res[1])],validPixels=int(valid.sum()),pixels=int(values.size))

def acquire(bounds,frame,raw,fetch,event):
    import coarse_provider
    jobs,receipts=coarse_provider.acquire(bounds,frame,raw,event)
    binding=frame.provider;provider,epsg,res,kinds,datum=BINDINGS[binding]
    if frame.epsg!=epsg:raise ValueError('Provider native frame mismatch')
    if _COOLDOWN.get(provider,0)>time.time():
        event('survey-fallback',provider=provider,reason='provider cooldown',quality='estimated');return jobs,receipts
    west,south,east,north=frame.source_bounds(bounds)
    cells=[(x,y) for x in range(math.floor(west/1000)*1000,math.ceil(east/1000)*1000,1000) for y in range(math.floor(south/1000)*1000,math.ceil(north/1000)*1000,1000)]
    if len(cells)>64:raise ValueError('Survey crop count exceeds bound')
    for x,y in cells:
        for kind,url in requests(binding,x,y):
            path=raw/f'{binding}-{x}-{y}-{kind}.tif';negative=path.with_suffix('.no-coverage.json')
            if negative.exists() and time.time()-negative.stat().st_mtime<86400:continue
            try:
                receipt=fetch(path,url,event,max_bytes=32*1024*1024)
                metadata=inspect(path,epsg)
                if metadata is None:
                    negative.write_text(json.dumps(dict(url=url,checkedUtc=time.time())))
                    path.unlink(missing_ok=True);path.with_suffix('.tif.receipt.json').unlink(missing_ok=True)
                    event('survey-no-coverage',provider=provider,cell=[x,y],role=kind);continue
                receipt.update(provider=provider,role='ground' if kind=='dtm' else 'surface',verticalDatum=datum,quality='measured',**metadata)
                if provider=='terrain.us.3dep':receipt['quality']='source-dependent';receipt['resolutionNote']='1m output grid; source survey resolution varies. Not a guaranteed 1m LiDAR survey.'
                receipts.append(receipt);jobs.append((path,url))
                event('survey-numeric-validated',provider=provider,role=kind,verticalDatum=datum,**metadata)
            except Exception as error:
                # Invalid XML / colour tiles must never become a trusted raster.
                path.unlink(missing_ok=True);path.with_suffix('.tif.receipt.json').unlink(missing_ok=True)
                _COOLDOWN[provider]=time.time()+300
                event('survey-fallback',provider=provider,reason=type(error).__name__,quality='estimated')
                return jobs,receipts
    return jobs,receipts

def install(pipeline):
    import elevation_provider
    import profile_safety
    previous_fit=profile_safety.road_grades
    if not getattr(previous_fit,'empty_safe',False):
        def road_grades(features,crossings,footprints,deck_depth,fixed_samples=()):
            if not any(f['category'] in ('road','rail') for f in features):
                return dict(curvatureSamples=0,maximumCurvature=0.,maximumCurvatureRatio=0.,nodes=0,constraints=0,junctionMouthSamples=0,tunnelCoverSamples=0,maximumCorrectionMetres=0.)
            return previous_fit(features,crossings,footprints,deck_depth,fixed_samples)
        road_grades.empty_safe=True;profile_safety.road_grades=road_grades
    original=elevation_provider.acquire
    if getattr(original,'public_surveys',False):return
    def dispatch(bounds,frame,raw,fetch,event):
        if frame.provider in BINDINGS:return acquire(bounds,frame,raw,fetch,event)
        return original(bounds,frame,raw,fetch,event)
    dispatch.public_surveys=True;elevation_provider.acquire=dispatch
    previous=pipeline.generator_identity
    def identity(frame=None):
        base=previous(frame)
        if frame is not None and frame.provider in BINDINGS:
            return hashlib.sha256((base+hashlib.sha256(Path(__file__).read_bytes()).hexdigest()).encode()).hexdigest()
        return base
    pipeline.generator_identity=identity
