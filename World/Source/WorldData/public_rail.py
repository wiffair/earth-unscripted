"""Location-selected account-free collectors; no network on the request thread.

Raw feeds and daily schedules are cached atomically. Providers have independent
cooldowns. A bad/empty feed cannot stall road generation or fabricate trains.
"""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime,timedelta,timezone
from io import BytesIO,StringIO
from pathlib import Path
from threading import Lock
from urllib.request import Request,urlopen
from zoneinfo import ZoneInfo
from zipfile import ZipFile,ZIP_DEFLATED
import csv,hashlib,json,ssl,time,xml.etree.ElementTree as ET
import certifi
from .catalogue import Catalogue
from .model import envelope,validate,point,utc
from . import gtfs,adapters

HEADERS={'User-Agent':'RoadRushBeta/38 (+account-free world-data collector)','ET-Client-Name':'blackwood-roadrush-beta','Digitraffic-User':'RoadRushBeta/38'}
ENTUR_QUERY='{vehicles(mode:RAIL,maxDataAge:"PT2M"){vehicleId mode lastUpdatedEpochSecond location{latitude longitude} bearing serviceJourney{id} destinationName}}'

def download(url,limit=8*1024*1024,body=None):
    headers=dict(HEADERS)
    if body is not None:headers['Content-Type']='application/json'
    request=Request(url,data=body,headers=headers)
    with urlopen(request,timeout=15,context=ssl.create_default_context(cafile=certifi.where())) as response:
        if int(response.headers.get('Content-Length','0'))>limit:raise ValueError('Provider response limit')
        data=response.read(limit+1)
    if len(data)>limit:raise ValueError('Provider response limit')
    return data

def entur(payload,now):
    raw=json.loads(payload)
    if raw.get('errors') or not isinstance(raw.get('data',{}).get('vehicles'),list):raise ValueError('Entur rail response unavailable')
    rows=[]
    for item in raw['data']['vehicles']:
        if item.get('mode')!='RAIL' or not item.get('location') or not item.get('lastUpdatedEpochSecond'):continue
        location=item['location'];stamp=utc(item['lastUpdatedEpochSecond'])
        if not 0<=now-stamp<=120:continue
        identity=item.get('vehicleId') or (item.get('serviceJourney') or {}).get('id')
        if not identity:continue
        row=dict(id=identity,tripId=(item.get('serviceJourney') or {}).get('id',''),position=point(location['longitude'],location['latitude']),observedUtc=stamp,positionQuality='observed',gaugeMm=1435)
        if item.get('bearing') is not None:row['bearingDegrees']=float(item['bearing'])%360
        rows.append(row)
    return envelope('rail.no.entur','entur-rail-json','train.observations',rows,now,quality='observed',country='NO',attribution='Entur / Norwegian transport data owners (NLOD)')

def irishrail(payload,now):
    if b'<!DOCTYPE' in payload.upper() or b'<!ENTITY' in payload.upper():raise ValueError('XML entity declarations forbidden')
    root=ET.fromstring(payload);rows=[]
    for item in root:
        fields={child.tag.rsplit('}',1)[-1]:child.text or '' for child in item}
        if fields.get('TrainStatus')!='R':continue # Running only, not departed/not-yet-running services.
        lon,lat=point(fields['TrainLongitude'],fields['TrainLatitude'])
        if lon==0 or lat==0:continue
        day=datetime.strptime(fields['TrainDate'],'%d %b %Y').date()
        if day!=datetime.fromtimestamp(now,ZoneInfo('Europe/Dublin')).date():continue
        rows.append(dict(id=fields['TrainCode']+':'+day.isoformat(),tripId=fields['TrainCode'],serviceDate=day.isoformat(),position=[lon,lat],observedUtc=now,positionQuality='estimated',timestampQuality='receipt-only',gaugeMm=1600))
    return envelope('rail.ie.irishrail','irishrail-xml','train.observations',rows,now,quality='estimated',country='IE',attribution='Iarnród Éireann Irish Rail; approximate reported locations, not continuous GPS')

def rail_only_zip(payload):
    """Stream all-mode GTFS into a bounded mainline-rail subset, never buses."""
    if len(payload)>128*1024*1024:raise ValueError('Schedule archive limit')
    with ZipFile(BytesIO(payload)) as source:
        if sum(i.file_size for i in source.infolist())>512*1024*1024:raise ValueError('Expanded schedule limit')
        if len(source.namelist())!=len(set(source.namelist())):raise ValueError('Duplicate schedule tables')
        def table(name):
            if name not in source.namelist():return
            import io
            with source.open(name) as f:
                for index,row in enumerate(csv.DictReader(io.TextIOWrapper(f,encoding='utf-8-sig'))):
                    if index>=5000000:raise ValueError('Schedule table row limit')
                    yield row
        routes=[r for r in table('routes.txt') if int(r['route_type'])==2 or 100<=int(r['route_type'])<200]
        route_ids={r['route_id'] for r in routes};trips=[r for r in table('trips.txt') if r['route_id'] in route_ids];trip_ids={r['trip_id'] for r in trips}
        stop_times=[r for r in table('stop_times.txt') if r['trip_id'] in trip_ids]
        if len(stop_times)>500000:raise ValueError('Rail timing row limit')
        stop_ids={r['stop_id'] for r in stop_times};out=BytesIO()
        with ZipFile(out,'w',ZIP_DEFLATED) as target:
            tables={'routes.txt':routes,'trips.txt':trips,'stop_times.txt':stop_times,'stops.txt':[r for r in table('stops.txt') if r['stop_id'] in stop_ids]}
            for name in ['agency.txt','calendar.txt','calendar_dates.txt']:tables[name]=list(table(name))
            for name,rows in tables.items():
                if not rows:continue
                stream=StringIO();writer=csv.DictWriter(stream,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows);target.writestr(name,stream.getvalue())
        return out.getvalue(),trip_ids

class Collector:
    def __init__(self,root,fetcher=download,clock=time.time):
        self.root=Path(root);self.root.mkdir(parents=True,exist_ok=True);self.fetch=fetcher;self.clock=clock
        self.catalogue=Catalogue();self.pool=ThreadPoolExecutor(max_workers=2,thread_name_prefix='public-rail');self.lock=Lock();self.states={}
    def _read(self,path,max_age):
        if path.exists() and self.clock()-path.stat().st_mtime<max_age:return path.read_bytes()
    def _write(self,path,data):
        temp=path.with_suffix(path.suffix+'.pending');temp.write_bytes(data);temp.replace(path)
    def _schedule(self,p):
        path=self.root/(p['id']+'.gtfs.zip');raw=self._read(path,86400)
        if raw is None:
            raw=self.fetch(p['scheduleEndpoint'],limit=128*1024*1024);subset,trip_ids=rail_only_zip(raw);self._write(path,subset)
        else:
            subset=raw
            with ZipFile(BytesIO(subset)) as z:trip_ids={r['trip_id'] for r in csv.DictReader(StringIO(z.read('trips.txt').decode('utf-8-sig')))}
        now=self.clock();day=datetime.fromtimestamp(now,ZoneInfo('America/New_York')).date();services=[]
        for date in (day-timedelta(days=1),day):
            converted=gtfs.schedule(subset,provider=p['id'],country='US',received=now,service_date=date.isoformat(),attribution=p['attribution'])
            services.extend(t for t in converted['records'] if t['stops'][0]['arrivalUtc']<=now+3600 and t['stops'][-1]['departureUtc']>=now-120)
        return envelope(p['id'],'gtfs-schedule','train.services',services,now,quality='published',country='US',attribution=p['attribution']),trip_ids
    def collect(self,p):
        now=self.clock();schedule=None;warnings=[]
        if p.get('scheduleEndpoint'):
            try:schedule,trip_ids=self._schedule(p)
            except Exception as error:trip_ids=set();warnings.append('Timetable unavailable: '+type(error).__name__)
        try:
            body=json.dumps({'query':ENTUR_QUERY}).encode() if p['id']=='rail.no.entur' else None
            payload=self.fetch(p['endpoints'][0],body=body)
            if p['id']=='rail.no.entur':observations=entur(payload,self.clock())
            elif p['id']=='rail.ie.irishrail':observations=irishrail(payload,self.clock())
            elif p['id']=='rail.fi.digitraffic':observations=adapters.digitraffic(json.loads(payload),received=self.clock())
            else:
                observations=gtfs.RealtimeState().convert(payload,provider=p['id'],country='US',received=self.clock(),attribution=p['attribution'])
                observations['records']=[dict(r,gaugeMm=1435,positionQuality='observed') for r in observations['records'] if r.get('tripId') in trip_ids and r.get('position') and 0<=self.clock()-r.get('observedUtc',0)<=120]
            validate(observations,self.clock());observations['sourceHash']=hashlib.sha256(payload).hexdigest()
            self._write(self.root/(p['id']+'.observations.json'),json.dumps(observations).encode())
        except Exception as error:
            observations=None;warnings.append('Live source unavailable: '+type(error).__name__)
        result=dict(schema=1,provider=p['id'],attribution=p['attribution'],receivedUtc=self.clock(),observations=observations,schedule=schedule,status='; '.join(warnings) or ('Reported locations · approximate' if p['id']=='rail.ie.irishrail' else 'Public rail data connected'))
        with self.lock:self.states[p['id']]=dict(result=result,next=self.clock()+(30 if observations is not None else 300),future=None)
        return result
    def snapshot(self,lon,lat,country):
        candidates=[r['provider'] for r in self.catalogue.resolve(lon,lat,country,'rail.live') if r['available'] and not r['provider'].get('credentialRef')]
        for p in candidates:
            with self.lock:
                state=self.states.setdefault(p['id'],dict(result=None,next=0,future=None))
                if self.clock()>=state['next'] and state['future'] is None:
                    # Seed a recent persisted observation while a new bounded poll runs.
                    if state['result'] is None:
                        saved=self._read(self.root/(p['id']+'.observations.json'),120)
                        if saved:
                            try:state['result']=dict(schema=1,provider=p['id'],attribution=p['attribution'],observations=json.loads(saved),schedule=None,status='Cached reported positions',receivedUtc=self.clock())
                            except (ValueError,TypeError):pass
                    state['next']=self.clock()+300;state['future']=self.pool.submit(self.collect,p)
                result=state['result']
            if result:
                # Bound geographic output without truncating the nationwide cache.
                result=dict(result);observations=result.get('observations')
                if observations:
                    result['observations']=dict(observations,records=[r for r in observations['records'] if r.get('position') and abs(r['position'][1]-lat)<.05 and abs(r['position'][0]-lon)*max(.1,__import__('math').cos(__import__('math').radians(lat)))<.08 and 0<=self.clock()-r.get('observedUtc',0)<=120][:64])
                if result.get('schedule'):
                    result['schedule']=dict(result['schedule'],records=[t for t in result['schedule']['records'] if any(abs(s['position'][1]-lat)<.08 and abs(s['position'][0]-lon)<.15 for s in t['stops'])][:64])
                return result
        return dict(schema=1,provider='',attribution='',receivedUtc=self.clock(),observations=None,schedule=None,status='Connecting public source' if candidates else 'No account-free rail feed for this location')
    def close(self):self.pool.shutdown(wait=False,cancel_futures=True)
