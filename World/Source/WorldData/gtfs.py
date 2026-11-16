"""GTFS Schedule ZIP and GTFS Realtime protobuf/JSON adapters.

Service clocks use GTFS's local-noon-minus-12-hours rule, including DST and
hours beyond24. Real observations require a vehicle observation timestamp;
a feed publication timestamp is not a GPS measurement timestamp.
"""
import csv
from datetime import datetime,date,time,timedelta
from io import StringIO,BytesIO
import json
from zoneinfo import ZoneInfo
from zipfile import ZipFile
from .model import envelope,point,utc,finite

def clock(value):
    parts=str(value).split(':')
    if len(parts)!=3:raise ValueError('Invalid GTFS clock')
    h,m,s=map(int,parts)
    if not 0<=h<72 or not 0<=m<60 or not 0<=s<60:raise ValueError('GTFS clock range')
    return h*3600+m*60+s

def service_utc(day,seconds,zone):
    day=date.fromisoformat(day)
    return datetime.combine(day,time(12),ZoneInfo(zone)).timestamp()-43200+seconds

def schedule(payload,*,provider,country,received,service_date,attribution):
    if len(payload)>128*1024*1024:raise ValueError('Schedule ZIP too large')
    with ZipFile(BytesIO(payload)) as archive:
        if sum(r.file_size for r in archive.infolist())>256*1024*1024:raise ValueError('Expanded GTFS limit')
        if len({r.filename for r in archive.infolist()})!=len(archive.infolist()):raise ValueError('Duplicate GTFS files')
        def rows(name):
            if name not in archive.namelist():return []
            raw=archive.read(name)
            if len(raw)>128*1024*1024:raise ValueError('GTFS table limit')
            result=list(csv.DictReader(StringIO(raw.decode('utf-8-sig'))))
            if len(result)>500000:raise ValueError('GTFS row limit')
            return result
        agencies=rows('agency.txt');routes={r['route_id']:r for r in rows('routes.txt')};stops={r['stop_id']:r for r in rows('stops.txt')}
        day=date.fromisoformat(service_date);compact=day.strftime('%Y%m%d');weekday=('monday','tuesday','wednesday','thursday','friday','saturday','sunday')[day.weekday()];active=set()
        for r in rows('calendar.txt'):
            if r['start_date']<=compact<=r['end_date'] and r[weekday]=='1':active.add(r['service_id'])
        for r in rows('calendar_dates.txt'):
            if r['date']==compact:
                if r['exception_type']=='1':active.add(r['service_id'])
                elif r['exception_type']=='2':active.discard(r['service_id'])
                else:raise ValueError('Unknown GTFS calendar exception')
        trips={}
        for r in rows('trips.txt'):
            route=routes[r['route_id']];kind=int(route['route_type'])
            if r['service_id'] not in active or not(kind==2 or 100<=kind<200):continue
            if r['trip_id'] in trips:raise ValueError('Duplicate GTFS trip')
            agency=next((a for a in agencies if a.get('agency_id')==route.get('agency_id')),agencies[0] if len(agencies)==1 else None)
            if agency is None:raise ValueError('Ambiguous GTFS timezone')
            trips[r['trip_id']]=dict(id=r['trip_id']+':'+service_date,tripId=r['trip_id'],serviceDate=service_date,
                routeId=r['route_id'],operator=agency.get('agency_name',''),timeZone=agency['agency_timezone'],
                destination=r.get('trip_headsign',''),stockConfirmed=False,stops=[],cancelled=False)
        for r in rows('stop_times.txt'):
            trip=trips.get(r['trip_id'])
            if trip is None:continue
            stop=stops[r['stop_id']];arrival=r.get('arrival_time') or r.get('departure_time');departure=r.get('departure_time') or r.get('arrival_time')
            item=dict(stopId=r['stop_id'],name=stop.get('stop_name',''),sequence=int(r['stop_sequence']),position=point(stop['stop_lon'],stop['stop_lat']),stop=True)
            if arrival:
                item.update(arrivalUtc=service_utc(service_date,clock(arrival),trip['timeZone']),departureUtc=service_utc(service_date,clock(departure),trip['timeZone']),timeQuality='published' if r.get('timepoint','1')=='1' else 'estimated')
                if item['departureUtc']<item['arrivalUtc']:raise ValueError('Reversed GTFS dwell')
            trip['stops'].append(item)
        output=[]
        for trip in trips.values():
            trip['stops'].sort(key=lambda s:s['sequence'])
            if len({s['sequence'] for s in trip['stops']})!=len(trip['stops']):raise ValueError('Duplicate GTFS stop sequence')
            timed=[s for s in trip['stops'] if 'arrivalUtc' in s]
            if len(timed)<2:continue
            if any(b['arrivalUtc']<a['departureUtc'] for a,b in zip(timed,timed[1:])):raise ValueError('GTFS times reverse')
            output.append(trip)
        valid=max((s['departureUtc'] for t in output for s in t['stops'] if 'departureUtc' in s),default=service_utc(service_date,86400,agencies[0]['agency_timezone']) if agencies else received)
        return envelope(provider,'gtfs-schedule','train.services',output,received,quality='published',country=country,dataset=service_date,valid_until=valid,attribution=attribution)

class RealtimeState:
    def __init__(self):self.entities={};self.timestamp=0
    def convert(self,payload,*,provider,country,received,attribution,json_input=False):
        from google.transit import gtfs_realtime_pb2 as pb
        from google.protobuf.json_format import Parse
        if len(payload)>8*1024*1024:raise ValueError('Realtime feed limit')
        feed=pb.FeedMessage()
        if json_input:Parse(payload.decode() if isinstance(payload,bytes) else payload,feed)
        else:feed.ParseFromString(payload)
        if not feed.IsInitialized():raise ValueError('Incomplete GTFS Realtime message')
        if len(feed.entity)>100000:raise ValueError('Realtime entity limit')
        stamp=feed.header.timestamp if feed.header.HasField('timestamp') else None
        if stamp is not None and (stamp>received+30 or stamp<self.timestamp):raise ValueError('Future or reversed feed clock')
        if len({e.id for e in feed.entity})!=len(feed.entity):raise ValueError('Duplicate feed entity')
        if feed.header.incrementality==pb.FeedHeader.FULL_DATASET:next_entities={}
        else:next_entities=dict(self.entities)
        for e in feed.entity:
            if e.is_deleted:next_entities.pop(e.id,None)
            else:next_entities[e.id]=e
        if len(next_entities)>100000:raise ValueError('Realtime accumulated state limit')
        records=[]
        for identity,e in next_entities.items():
            v=e.vehicle if e.HasField('vehicle') else None;u=e.trip_update if e.HasField('trip_update') else None
            if v is None and u is None:continue
            t=v.trip if v is not None else u.trip
            record=dict(id=identity,tripId=t.trip_id,serviceDate=(datetime.strptime(t.start_date,'%Y%m%d').date().isoformat() if t.start_date else ''),cancelled=t.schedule_relationship==pb.TripDescriptor.CANCELED)
            if v is not None:
                if v.HasField('position'):record['position']=point(v.position.longitude,v.position.latitude)
                if v.position.HasField('speed'):
                    speed=finite(v.position.speed)
                    if not 0<=speed<=150:raise ValueError('Invalid vehicle speed')
                    record['speedMps']=speed
                if v.HasField('timestamp'):
                    if v.timestamp>received+30:raise ValueError('Future vehicle observation')
                    record['observedUtc']=v.timestamp
                record['vehicleId']=v.vehicle.id
            if u is not None:
                record['stopUpdates']=[]
                for stop in u.stop_time_update:
                    update=dict(stopId=stop.stop_id,sequence=stop.stop_sequence,skipped=stop.schedule_relationship==pb.TripUpdate.StopTimeUpdate.SKIPPED)
                    for name in ['arrival','departure']:
                        event=getattr(stop,name)
                        if event.HasField('time'):update[name+'Utc']=event.time
                        elif event.HasField('delay'):update[name+'DelaySeconds']=event.delay
                    record['stopUpdates'].append(update)
            records.append(record)
        result=envelope(provider,'gtfs-realtime','train.observations',records,received,quality='observed' if any('observedUtc' in r for r in records) else 'unknown',country=country,dataset=str(stamp or 'unknown'),attribution=attribution)
        self.entities=next_entities;self.timestamp=stamp or self.timestamp
        return result
