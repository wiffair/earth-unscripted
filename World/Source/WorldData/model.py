"""Shared geographic/transport contracts. UTC seconds, WGS84 lon/lat, SI units.

Native raster height datums remain explicit: no unsurveyed vertical conversion.
Unknown locations/stock are retained as unknown rather than fabricated.
"""
import hashlib
import json
import math
from datetime import datetime, timezone

QUALITIES = {'observed', 'measured', 'published', 'estimated', 'unknown'}
FAMILIES = {'train.services', 'train.observations', 'terrain.raster', 'map.features'}

def finite(value, name='number'):
    value=float(value)
    if not math.isfinite(value):raise ValueError('Non-finite '+name)
    return value

def utc(value):
    if isinstance(value,(int,float)):return finite(value,'timestamp')
    date=datetime.fromisoformat(str(value).replace('Z','+00:00'))
    if date.tzinfo is None:raise ValueError('Timestamp requires a UTC offset')
    return date.timestamp()

def point(lon,lat):
    lon,lat=finite(lon),finite(lat)
    if not -180<=lon<=180 or not -90<=lat<=90:raise ValueError('Invalid geographic point')
    return [lon,lat]

def digest(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False).encode()).hexdigest()

def envelope(provider,adapter,family,records,received,*,quality='unknown',dataset='unknown',valid_until=None,attribution='',country='',source_hash=''):
    if family not in FAMILIES or quality not in QUALITIES:raise ValueError('Unknown family/quality')
    if not provider or not adapter or not attribution:raise ValueError('Source identity and attribution required')
    if len(records)>100000:raise ValueError('Record limit exceeded')
    result=dict(schema=1,provider=provider,adapter=adapter,adapterVersion='1.0.0',family=family,
        datasetVersion=str(dataset),country=country.upper(),receivedUtc=utc(received),quality=quality,
        attribution=attribution,sourceHash=source_hash,records=records)
    if valid_until is not None:result['validUntilUtc']=utc(valid_until)
    json.dumps(result,allow_nan=False)
    return result

def validate(value,now=None):
    if value.get('schema')!=1 or value.get('family') not in FAMILIES or value.get('quality') not in QUALITIES:raise ValueError('Unsupported world data contract')
    if not all(value.get(k) for k in ['provider','adapter','adapterVersion','attribution']):raise ValueError('Missing source provenance')
    received=utc(value['receivedUtc'])
    if now is not None and received>utc(now)+30:raise ValueError('Future receipt')
    if not isinstance(value.get('records'),list) or len(value['records'])>100000:raise ValueError('Record limit')
    for record in value['records']:
        if not isinstance(record,dict) or not record.get('id'):raise ValueError('Record identity required')
        if 'position' in record and record['position'] is not None:point(*record['position'])
        if 'observedUtc' in record:
            if utc(record['observedUtc'])>received+30:raise ValueError('Observation is in the future')
        if 'speedMps' in record and not 0<=finite(record['speedMps'])<=150:raise ValueError('Invalid reported speed')
    json.dumps(value,allow_nan=False)
    return value

def usable(value,now):
    try:
        validate(value,now)
        if value.get('validUntilUtc',float('inf'))<now:return False
        if value['family']=='train.observations':
            return any(0<=now-utc(r.get('observedUtc',0))<=120 for r in value['records']) or (0<=now-value['receivedUtc']<=48*3600 and any(r.get('cancelled') for r in value['records']))
        return True
    except (ValueError,TypeError,KeyError):return False
