"""Small provider-specific converters into the common world-data contracts."""
from datetime import datetime
from zoneinfo import ZoneInfo
from .model import envelope,utc,point,finite

def networkrail_schedule(messages,*,received,service_date):
    """Convert a complete JSON Schedule extract for one operating day.

    The caller supplies a full extract, not unmerged incremental updates. STP
    cancellation/overlay precedence is applied before creating active services.
    """
    from datetime import date,timedelta
    day=date.fromisoformat(service_date);chosen={};priority={'P':0,'N':1,'O':2,'C':3}
    for document in messages:
        row=document.get('JsonScheduleV1')
        if row is None:continue
        if row.get('transaction_type','Create')!='Create':raise ValueError('Merge Schedule increments before conversion')
        if not str(row['schedule_start_date'])[:10]<=service_date<=str(row['schedule_end_date'])[:10]:continue
        if row.get('schedule_days_runs','0000000')[day.weekday()]!='1':continue
        stp=row['CIF_stp_indicator'];uid=row['CIF_train_uid']
        if stp not in priority:raise ValueError('Unknown STP indicator')
        if uid not in chosen or priority[stp]>priority[chosen[uid]['CIF_stp_indicator']]:chosen[uid]=row
        elif priority[stp]==priority[chosen[uid]['CIF_stp_indicator']]:raise ValueError('Ambiguous same-priority rail schedule')
    output=[]
    for uid,row in chosen.items():
        cancelled=row['CIF_stp_indicator']=='C'
        if cancelled:continue
        segment=row['schedule_segment'];stops=[];previous=-1;roll=0
        def seconds(text):
            text=text.replace(':','');half=text.endswith('H');text=text.rstrip('H')
            if len(text) not in (4,6) or not text.isdigit():raise ValueError('Invalid working timetable clock')
            h,m=int(text[:2]),int(text[2:4]);s=int(text[4:]) if len(text)==6 else 30 if half else 0
            if h>23 or m>59 or s>59:raise ValueError('Working timetable clock range')
            return h*3600+m*60+s
        for sequence,s in enumerate(segment['schedule_location']):
            arrival=s.get('arrival') or s.get('pass') or s.get('departure');departure=s.get('departure') or s.get('pass') or s.get('arrival')
            if not arrival:continue
            a,d=seconds(arrival),seconds(departure)
            if a+roll<previous:roll+=86400
            a+=roll;d+=roll
            if d<a:d+=86400
            previous=d
            def instant(seconds):
                local=datetime.combine(day,datetime.min.time())+timedelta(seconds=seconds);zone=ZoneInfo('Europe/London')
                early=local.replace(tzinfo=zone,fold=0);late=local.replace(tzinfo=zone,fold=1)
                if early.utcoffset()!=late.utcoffset():raise ValueError('Ambiguous/missing UK DST working time')
                return early.timestamp()
            stops.append(dict(stopId=s['tiploc_code'],name=s['tiploc_code'],sequence=sequence,arrivalUtc=instant(a),departureUtc=instant(d),stop=not bool(s.get('pass')),timeQuality='published'))
        if len(stops)>=2:output.append(dict(id=uid+':'+service_date,tripId=uid,serviceDate=service_date,routeId='',stockConfirmed=False,stops=stops,cancelled=False))
    return envelope('rail.gb.networkrail','networkrail-json','train.services',output,received,quality='published',country='GB',dataset=service_date,
        valid_until=max((t['stops'][-1]['departureUtc'] for t in output),default=received),attribution='Network Rail open data; provider access terms apply')

def digitraffic(document,*,received):
    if document.get('type')!='FeatureCollection':raise ValueError('Expected Digitraffic GeoJSON')
    if len(document['features'])>100000:raise ValueError('Feature limit')
    records=[]
    for feature in document['features']:
        p=feature['properties'];geometry=feature['geometry']
        if geometry['type']!='Point':raise ValueError('Expected a train point')
        observed=utc(p['timestamp']);speed=finite(p.get('speed',0))/3.6
        if not 0<=speed<=150 or observed>received+30:raise ValueError('Invalid train observation')
        records.append(dict(id=str(p['trainNumber'])+':'+p['departureDate'],tripId=str(p['trainNumber']),serviceDate=p['departureDate'],
            position=point(*geometry['coordinates'][:2]),observedUtc=observed,speedMps=speed,cancelled=False))
    return envelope('rail.fi.digitraffic','digitraffic-geojson','train.observations',records,received,quality='observed',country='FI',attribution='Fintraffic Digitraffic · CC BY 4.0')

class NetworkRailTrust:
    """Stateful activation/movement/cancellation decoder, not a broker client.

    STANOX reports retain their reporting-point identity. Without an activation
    or reference mapping they remain unbound; no GPS coordinates are invented.
    """
    def __init__(self):self.activations={};self.records={}
    def convert(self,messages,*,received):
        if not isinstance(messages,list) or len(messages)>100000:raise ValueError('TRUST message limit')
        activations=dict(self.activations);records=dict(self.records)
        for message in messages:
            kind=message['header']['msg_type'];body=message['body'];train=str(body.get('train_id',''))
            if not train:continue
            if kind=='0001':
                stamp=float(body['origin_dep_timestamp'])/1000
                day=datetime.fromtimestamp(stamp,ZoneInfo('Europe/London')).date().isoformat()
                activations[train]=(body['train_uid'],day)
                records.pop(train,None)
            elif kind=='0005' and train in records:
                records[train]=dict(records[train],cancelled=False)
            elif kind in ('0002','0003'):
                uid,day=activations.get(train,(train,''));record=dict(id=train,tripId=uid,serviceDate=day,cancelled=kind=='0002' or records.get(train,{}).get('cancelled',False))
                if kind=='0003':
                    stamp=float(body['actual_timestamp'])/1000
                    if stamp>received+30 or stamp<0:raise ValueError('Invalid TRUST observation')
                    record.update(observedUtc=stamp,reportPoint=dict(system='STANOX',id=str(body['loc_stanox'])),event=body.get('event_type',''))
                    old=records.get(train)
                    if old and old.get('observedUtc',0)>stamp:continue
                records[train]=record
        # Retain bounded current runs; active trains can report after midnight.
        records={k:v for k,v in records.items() if v.get('observedUtc',received)>=received-48*3600}
        if len(records)>100000 or len(activations)>100000:raise ValueError('TRUST state limit')
        result=envelope('rail.gb.networkrail','networkrail-json','train.observations',list(records.values()),received,quality='observed',country='GB',attribution='Network Rail open data; provider access terms apply')
        self.activations={k:v for k,v in activations.items() if k in records or v[1]>=datetime.fromtimestamp(received-48*3600,ZoneInfo('Europe/London')).date().isoformat()};self.records=records
        return result

def osm(document,*,received,provider='map.osm'):
    elements=document.get('elements',[])
    if len(elements)>100000:raise ValueError('OSM element limit')
    nodes={r['id']:r for r in elements if r['type']=='node' and 'lon' in r};features=[]
    for row in elements:
        tags=row.get('tags',{})
        if not tags:continue
        if row['type']=='relation':
            members=[]
            for member in row.get('members',[]):
                geometry=member.get('geometry',[])
                if any(p is None for p in geometry):raise ValueError('Incomplete relation member')
                members.append(dict(id=str(member['ref']),type=member['type'],role=member.get('role',''),geometry=[point(p['lon'],p['lat']) for p in geometry]))
            features.append(dict(id='osm:relation:'+str(row['id']),geometryType='collection',geometry=[],members=members,properties=tags));continue
        geometry=row.get('geometry')
        if row['type']=='node' and 'lon' in row:geometry=[row]
        elif row['type']=='way' and geometry is None:
            if any(n not in nodes for n in row.get('nodes',[])):continue
            geometry=[nodes[n] for n in row.get('nodes',[])]
        if geometry is None:continue # Relations need their own ring-assembly adapter.
        if any(p is None or 'lon' not in p or 'lat' not in p for p in geometry):raise ValueError('Incomplete OSM geometry')
        features.append(dict(id='osm:'+row['type']+':'+str(row['id']),geometry=[point(p['lon'],p['lat']) for p in geometry],properties=tags,
            sourceVersion=row.get('version'),geometryType='point' if row['type']=='node' else 'line'))
    return envelope(provider,'osm-json','map.features',features,received,quality='published',attribution='© OpenStreetMap contributors · ODbL1.0')

def raster(path,*,role,provider,received,quality='measured',vertical_datum='unknown',declared_units=None,attribution):
    import rasterio
    from rasterio.warp import transform_bounds
    if role not in ('ground','surface'):raise ValueError('Separate ground/surface role required')
    with rasterio.open(path) as data:
        if data.count!=1 or data.crs is None:raise ValueError('A georeferenced single elevation band is required')
        units=data.units[0] if data.units else None
        scale={'metre':1,'meter':1,'m':1,'foot':.3048,'ft':.3048,'US survey foot':1200/3937}.get(units or declared_units)
        if scale is None:raise ValueError('Unknown elevation units')
        bounds=list(transform_bounds(data.crs,'EPSG:4326',*data.bounds,densify_pts=21))
        record=dict(id=path.name,role=role,horizontalCRS=data.crs.to_string(),verticalDatum=vertical_datum,
            unitToMetres=scale,unitsAssumed=units is None,resolutionNative=list(map(abs,data.res)),boundsWGS84=bounds,
            width=data.width,height=data.height,nodata=data.nodata,quality=quality)
        if record['nodata'] is not None and not __import__('math').isfinite(record['nodata']):record['nodata']='NaN'
        return envelope(provider,'native-raster','terrain.raster',[record],received,quality=quality,attribution=attribution)
