"""Construct an owned normal-game worker session from legacy map coordinates.

No profile is loaded or written here. The game supplies its selected map's
public geographic origin and a source-road centre/heading. Survey selection
has an approximate-terrain fallback beyond measured national coverage.
"""
import hashlib
import json
import math
from pathlib import Path

from region_frame import RegionFrame


def prepare(path,evidence):
    if path.stat().st_size>32768:raise ValueError('Session configuration exceeds limit')
    config=json.loads(path.read_text())
    latitude=float(config['latitude']);longitude=float(config['longitude'])
    datum=float(config.get('datum',0))
    country=str(config.get('countryCode','')).upper()
    from WorldData.catalogue import Catalogue
    catalogue=Catalogue()
    provider,epsg,source_id=catalogue.terrain_binding(longitude,latitude,country)
    with (evidence/'world-data-selection.json').open('x') as selected:
        json.dump(dict(schema=1,catalogueVersion=catalogue.data['version'],catalogueIdentity=catalogue.identity,
            selectedTerrain=source_id,providers=catalogue.resolve(longitude,latitude,country,'terrain')),selected,indent=2)
    side=config.get('trafficSide','left')
    if side not in ('left','right'):raise ValueError('Invalid driving side')
    name=str(config.get('name','Live world'))[:160]
    identity=hashlib.sha256(json.dumps(dict(latitude=latitude,longitude=longitude,
        datum=datum,provider=provider,side=side),sort_keys=True).encode()).hexdigest()[:20]
    frame=RegionFrame.legacy_map('main-'+identity,name,latitude,longitude,datum,epsg,provider,side)
    from dataclasses import replace
    frame=replace(frame,country_code=country)
    start={key:float(config['start'].get(key,0)) for key in ('x','y','z')}
    heading=[float(config.get('headingX',0)),float(config.get('headingZ',1))]
    if not all(math.isfinite(v) for v in list(start.values())+heading) or max(abs(start['x']),abs(start['z']))>10000000:
        raise ValueError('Invalid session start')
    length=math.hypot(*heading)
    if length<.001:raise ValueError('Session heading is empty')
    heading=[v/length for v in heading]
    bootstrap=dict(name=name,attribution='© OpenStreetMap contributors · Survey and approximate terrain sources',
        start=start,headingX=heading[0],headingZ=heading[1])
    source_way=str(config.get('sourceWay') or '')
    if source_way:
        if not source_way.isdigit() or len(source_way)>24:raise ValueError('Invalid source way identity')
        bootstrap['sourceWay']=source_way
    # A short legal-road prefix supports normal spawn/garage/UI consumers.
    # Subsequent human streaming forecasts every reachable branch, independently
    # of this initial guide. There is no scripted future driving destination.
    destination=dict(name=name+' starting roads',x=start['x']+heading[0]*1200,
        z=start['z']+heading[1]*1200,radiusMetres=40,localGoal=True,initialHumanGuide=True)
    values={'generated-region.json':frame.identity(),'generated-bootstrap.json':bootstrap,
            'generated-destination.json':destination}
    for filename,value in values.items():
        with (evidence/filename).open('x') as output:json.dump(value,output,indent=2)
    with (evidence/'session-input-binding.json').open('x') as output:
        json.dump(dict(inputSHA256=hashlib.sha256(path.read_bytes()).hexdigest(),
            coordinateConvention='legacy local map metres; native source raster sampling',
            provider=provider,epsg=epsg,frameIdentity=frame.identity()),output,indent=2)
    return tuple(evidence/name for name in ('generated-region.json','generated-bootstrap.json','generated-destination.json'))
