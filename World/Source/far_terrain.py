"""Cached coarse scenery terrain, independent of road preparation and physics.

Mapzen Terrarium PNGs on the AWS public terrain registry supply distant relief.
They decode to metres (R*256+G+B/256-32768). EPSG:27700 local coordinates are
transformed to Web Mercator for sampling. A local median offset to raw Welsh
DTM anchors is visual calibration, not a surveyed vertical-datum conversion.
"""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import hashlib
import json
import math
import ssl
import threading
import time
import urllib.request
import certifi
import numpy as np
from PIL import Image
from pyproj import Transformer
from scipy.ndimage import map_coordinates
from region_frame import RegionFrame,geographic_handoff

RADIUS=8192
STEP=64
ZOOM=12
HALF_WORLD=math.pi*6378137
TILE_SPAN=2*HALF_WORLD/(1<<ZOOM)


class FarTerrain:
    def __init__(self,cache,event,frame=None):
        self.frame=frame or RegionFrame.load()
        self.root=cache/'FarTerrain'/self.frame.identifier;self.root.mkdir(parents=True,exist_ok=True)
        self.raw=self.root/'Raw';self.raw.mkdir(exist_ok=True)
        self.event=event;self.lock=threading.Lock();self.transform=geographic_handoff(self.frame.world_projection,3857)
        self.tiles={}

    def tile(self,key):
        path=self.raw/f'{ZOOM}_{key[0]}_{key[1]}.png'
        reused=path.exists()
        if not reused:
            url=f'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{ZOOM}/{key[0]}/{key[1]}.png'
            request=urllib.request.Request(url,headers={'User-Agent':'EarthUnscripted-private-distant-terrain/1.0'})
            with urllib.request.urlopen(request,timeout=25,context=ssl.create_default_context(cafile=certifi.where())) as response:data=response.read(2*1024*1024+1)
            if len(data)>2*1024*1024:raise ValueError('Unexpectedly large coarse elevation tile')
            temporary=path.with_suffix('.partial');temporary.write_bytes(data)
            with Image.open(temporary) as check:assert check.size==(256,256)
            temporary.replace(path)
            self.event('far-source',file=path.name,url=url,bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
        rgb=np.asarray(Image.open(path).convert('RGB'),dtype=float)
        decoded=rgb[:,:,0]*256+rgb[:,:,1]+rgb[:,:,2]/256-32768
        if np.min(decoded)<-500 or np.max(decoded)>9000:raise ValueError('Invalid coarse terrain heights')
        return key,decoded

    def sample(self,x,z):
        east,north=self.transform.transform(np.asarray(x)+self.frame.world_origin[0],np.asarray(z)+self.frame.world_origin[1])
        tx=(east+HALF_WORLD)/TILE_SPAN;ty=(HALF_WORLD-north)/TILE_SPAN
        ix=np.floor(tx).astype(int);iy=np.floor(ty).astype(int)
        keys=set(zip(ix.tolist(),iy.tolist()))
        missing=[key for key in keys if key not in self.tiles]
        if missing:
            with ThreadPoolExecutor(max_workers=2) as pool:
                for key,values in pool.map(self.tile,missing):self.tiles[key]=values
        result=np.empty(len(tx))
        for key in keys:
            mask=(ix==key[0])&(iy==key[1]);pixels=[(ty[mask]-key[1])*256-.5,(tx[mask]-key[0])*256-.5]
            result[mask]=map_coordinates(self.tiles[key],pixels,order=1,mode='nearest')
        return result

    def prepare(self,request):
        with self.lock:
            started=time.perf_counter();cx=round(request['x']/1024)*1024;cz=round(request['z']/1024)*1024
            path=self.root/f'patch-{cx}-{cz}-v2.json'
            cached=path.exists()
            if cached:patch=json.loads(path.read_text())
            else:
                x=np.arange(cx-RADIUS,cx+RADIUS+STEP/2,STEP);z=np.arange(cz-RADIUS,cz+RADIUS+STEP/2,STEP);gx,gz=np.meshgrid(x,z)
                heights=self.sample(gx.ravel(),gz.ravel())
                patch=dict(minX=float(x[0]),minZ=float(z[0]),step=STEP,nx=len(x),nz=len(z),heights=np.round(heights,2).tolist())
                temporary=path.with_suffix('.partial');temporary.write_text(json.dumps(patch,separators=(',',':')));temporary.replace(path)
            anchors=request.get('anchors',[])
            offset=0.
            if anchors:
                coarse=self.sample(np.array([point['x'] for point in anchors]),np.array([point['z'] for point in anchors]))
                offset=float(np.clip(np.median(np.array([point['y'] for point in anchors])-coarse),-30,30))
            result=dict(patch,verticalOffset=offset,cached=cached,seconds=time.perf_counter()-started)
            self.event('far-ready',centreX=cx,centreZ=cz,cached=cached,seconds=result['seconds'],verticalOffset=offset,radiusMetres=RADIUS,stepMetres=STEP)
            # Coarse-cache budgets are separate: 128 MiB patches, 64 MiB PNGs.
            for folder,limit,pattern in [(self.root,128*1024*1024,'patch-*.json'),(self.raw,64*1024*1024,'*.png')]:
                files=sorted(folder.glob(pattern),key=lambda item:item.stat().st_mtime);total=sum(item.stat().st_size for item in files)
                for item in files:
                    if total<=limit:break
                    total-=item.stat().st_size;item.unlink()
            return result


def coastal_geometry(elements, frame, bounds):
    """OSM directed coastline partitions; land left, sea right. No height flooding."""
    from shapely.geometry import LineString,Polygon,box,Point
    from shapely.ops import unary_union,polygonize,polygonize_full
    from shapely import constrained_delaunay_triangles
    transform=Transformer.from_crs(4326,frame.world_projection,always_xy=True)
    ox,oz=frame.world_origin;window=box(*bounds);coasts=[];beaches=[]
    def line(geometry):
        points=[transform.transform(p['lon'],p['lat']) for p in geometry if p]
        return LineString([(x-ox,z-oz) for x,z in points]) if len(points)>1 else None
    def triangles(polygon,height=0):
        output=[]
        for triangle in constrained_delaunay_triangles(polygon).geoms:
            for x,z in list(triangle.exterior.coords)[:3]:output.append(dict(x=round(x,3),y=height,z=round(z,3)))
        return output
    for element in elements:
        tags=element.get('tags',{})
        if tags.get('natural')=='coastline':
            item=line(element.get('geometry',[]))
            if item is not None:coasts.append(item)
        if tags.get('natural')!='beach':continue
        shape=None
        if element.get('type')=='way':
            item=line(element.get('geometry',[]))
            if item is not None and item.is_ring:shape=Polygon(item)
        elif element.get('type')=='relation':
            outer=[];inner=[]
            for member in element.get('members',[]):
                item=line(member.get('geometry',[]))
                if item is not None:(inner if member.get('role')=='inner' else outer).append(item)
            shapes=list(polygonize(unary_union(outer))) if outer else []
            if shapes:shape=unary_union(shapes)
            if shape is not None and inner:shape=shape.difference(unary_union(list(polygonize(unary_union(inner)))))
        if shape is None:continue
        shape=shape.buffer(0).intersection(window).simplify(1,preserve_topology=True)
        surface=tags.get('surface',tags.get('beach','unknown')).lower()
        kind='sand' if surface in ('sand','fine_sand') else 'pebbles' if surface in ('pebbles','pebblestone','shingle','gravel','fine_gravel','rock') else 'unknown'
        if not shape.is_empty:beaches.append(dict(name=tags.get('name','Beach'),surface=surface,kind=kind,vertices=triangles(shape)))
    if not coasts:return dict(sea=[],beaches=beaches,shore=[],coastlineFound=False)
    cut=unary_union([window.boundary]+[item.intersection(window) for item in coasts])
    faces,cuts,dangles,invalid=polygonize_full(list(cut.geoms) if hasattr(cut,'geoms') else [cut])
    if cuts.length+dangles.length+invalid.length>.5:raise ValueError('Incomplete coastline; refusing an ambiguous sea mask')
    sea=[]
    for face in faces.geoms:
        if face.area<1:continue
        point=face.representative_point();nearest=min(coasts,key=lambda item:item.distance(point));station=nearest.project(point)
        a=nearest.interpolate(max(0,station-.1));b=nearest.interpolate(min(nearest.length,station+.1))
        cross=(b.x-a.x)*(point.y-a.y)-(b.y-a.y)*(point.x-a.x)
        if cross>=0:continue
        face=face.simplify(2,preserve_topology=True)
        sea.append(dict(outline=[dict(x=x,y=0,z=z) for x,z in face.exterior.coords],holes=[dict(points=[dict(x=x,y=0,z=z) for x,z in ring.coords]) for ring in face.interiors],vertices=triangles(face)))
    shore=[]
    for item in coasts:
        clipped=item.intersection(window)
        for part in ([clipped] if clipped.geom_type=='LineString' else getattr(clipped,'geoms',[])):
            if part.geom_type=='LineString':shore.append(dict(points=[dict(x=x,y=0,z=z) for x,z in part.simplify(2).coords]))
    return dict(sea=sea,beaches=beaches,shore=shore,coastlineFound=True)


def prepare_coast(self,request):
    import urllib.parse
    from area_pipeline import fetch,atomic_json
    cx=round(request['x']/4096)*4096;cz=round(request['z']/4096)*4096
    target=self.root/f'coast-{cx}-{cz}-v1.json'
    if target.exists() and time.time()-target.stat().st_mtime<7*86400:return json.loads(target.read_text())
    bounds=[cx-16000,cz-16000,cx+16000,cz+16000]
    west,south,east,north=self.frame.geographic_bounds(bounds);bbox=f'{south},{west},{north},{east}'
    query=f'[out:json][timeout:35];(way[natural=coastline]({bbox});way[natural=beach]({bbox});relation[natural=beach]({bbox}););out body geom;'
    raw=self.raw/f'coast-{cx}-{cz}-v1-osm.json';errors=[]
    for endpoint in ['https://overpass.atownsend.org.uk/api/interpreter','https://overpass-api.de/api/interpreter','https://overpass.private.coffee/api/interpreter']:
        try:
            fetch(raw,endpoint,self.event,urllib.parse.urlencode({'data':query}).encode());break
        except Exception as error:errors.append(str(error))
    else:raise RuntimeError('Coastline source unavailable:'+str(errors))
    result=coastal_geometry(json.loads(raw.read_text())['elements'],self.frame,bounds)
    result.update(centreX=cx,centreZ=cz,radius=16000,seaLevel=.18-self.frame.height_origin,attribution='©OpenStreetMap contributors;coastline and mapped beach surfaces')
    atomic_json(target,result)
    older=sorted((p for p in self.root.glob('coast-*-v1.json') if p!=target),key=lambda p:p.stat().st_mtime)
    for old in older[:-11]:
        stem=old.name.removesuffix('.json');old.unlink()
        for source in self.raw.glob(stem+'-osm.json*'):source.unlink()
    self.event('coast-ready',seaPolygons=len(result['sea']),beaches=len(result['beaches']),file=target.name)
    return result

FarTerrain.prepare_coast=prepare_coast
