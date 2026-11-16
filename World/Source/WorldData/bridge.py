"""Explicit admitted rail bindings turn geographic service data into game v1.

A binding is a surveyed/mapped path with stop/report-point stationing and actual
OSM source ways. No straight-line rail paths are invented from two stations.
Unmapped/ambiguous observations remain canonical data and do not spawn actors.
"""
from datetime import datetime,timezone
from .model import validate,utc,point,finite

def iso(seconds):return datetime.fromtimestamp(seconds,timezone.utc).isoformat()

def game_snapshot(schedule,observations,bindings,now,stock_ids):
    validate(schedule,now)
    if schedule['family']!='train.services':raise ValueError('Schedule envelope required')
    if observations is not None:
        validate(observations,now)
        if observations['family']!='train.observations' or observations['provider']!=schedule['provider']:raise ValueError('Observations must match their schedule provider')
    services=[];unbound=[]
    for trip in schedule['records']:
        binding=bindings.get(trip['id']) or bindings.get(trip.get('routeId',''))
        if not binding:unbound.append(trip['id']);continue
        ways=binding.get('sourceWays',[]);stock=binding.get('stockId');direction=binding.get('direction',1)
        if not ways or len(ways)>128 or stock not in stock_ids or direction not in (1,-1):raise ValueError('Unadmitted track/stock binding')
        calls=[]
        updates=[r for r in observations['records'] if r.get('tripId')==trip['tripId'] and r.get('serviceDate')==trip['serviceDate']] if observations else []
        for stop in trip['stops']:
            if 'arrivalUtc' not in stop:continue
            if stop['stopId'] not in binding['stops']:calls=[];break
            arrival,departure=stop['arrivalUtc'],stop['departureUtc'];stopping=stop.get('stop',True)
            for update in updates:
                for changed in update.get('stopUpdates',[]):
                    if changed.get('stopId')!=stop['stopId'] and changed.get('sequence')!=stop.get('sequence'):continue
                    arrival=changed.get('arrivalUtc',stop['arrivalUtc']+changed.get('arrivalDelaySeconds',0))
                    departure=changed.get('departureUtc',stop['departureUtc']+changed.get('departureDelaySeconds',0))
                    stopping=stopping and not changed.get('skipped',False)
            if departure<arrival:raise ValueError('Live stop update reverses dwell')
            calls.append(dict(location=stop.get('name',stop['stopId']),metres=finite(binding['stops'][stop['stopId']]),arrivalUtc=iso(arrival),departureUtc=iso(departure),stop=stopping))
        if len(calls)<2:unbound.append(trip['id']);continue
        if any((b['metres']-a['metres'])*direction<0 for a,b in zip(calls,calls[1:])):raise ValueError('Mapped timing points reverse direction')
        if utc(calls[-1]['departureUtc'])<now or utc(calls[0]['arrivalUtc'])>now+120:continue
        reports=[];cancelled=bool(trip.get('cancelled',False))
        for report in observations['records'] if observations else []:
            if report.get('tripId')!=trip['tripId'] or report.get('serviceDate')!=trip['serviceDate']:continue
            cancelled=cancelled or report.get('cancelled',False)
            stamp=report.get('observedUtc')
            if stamp is None or not 0<=now-stamp<=120:continue
            at=None
            if 'reportPoint' in report:
                p=report['reportPoint'];at=binding.get('reportPoints',{}).get(p['system']+':'+p['id'])
            # GPS projection requires an explicit mapped, increasing path. The
            # chosen track identity, elevation and clearance stay with the binding.
            elif report.get('position') is not None and binding.get('geographicPath'):
                lon,lat=point(*report['position']);best=(float('inf'),None)
                import math
                cosine=math.cos(math.radians(lat));scale=111319.4908
                path=binding['geographicPath']
                for a,b in zip(path,path[1:]):
                    ax,az=(a[0]-lon)*scale*cosine,(a[1]-lat)*scale;dx,dz=(b[0]-a[0])*scale*cosine,(b[1]-a[1])*scale
                    length=dx*dx+dz*dz
                    if length<=0:continue
                    f=max(0,min(1,-(ax*dx+az*dz)/length));gap=math.hypot(ax+f*dx,az+f*dz)
                    if gap<best[0]:best=(gap,a[2]+(b[2]-a[2])*f)
                if best[0]<=binding.get('maximumSnapMetres',15):at=best[1]
            if at is None:continue
            reports.append(dict(metres=finite(at),observedUtc=iso(stamp),receivedUtc=iso(observations['receivedUtc']),speedMps=report.get('speedMps',0)))
        reports.sort(key=lambda r:utc(r['observedUtc']))
        services.append(dict(id=trip['id'],pathId=binding['pathId'],stockId=stock,stockConfirmed=False,sourceWays=ways,direction=direction,calls=calls,reports=reports,cancelled=cancelled))
    return dict(schema=1,provider=schedule['provider'],attribution=schedule['attribution'],generatedUtc=iso(now),services=services[:64]),unbound
