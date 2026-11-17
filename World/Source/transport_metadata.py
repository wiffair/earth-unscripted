"""Source-bound transport controls and explicitly approximate rail envelopes.

Vehicle examples are not national loading gauges. Route values take precedence;
unknown routes retain a conservative labelled fallback, never certified clearance.
"""
import math
import numpy as np
from shapely.geometry import Point
import road_layout

VEHICLES={
 'GB':(2.72,3.915,'TfW Class231 vehicle-change notice, 2022'),
 'CH':(2.88,4.260,'Stadler BLS FLIRT FBLS0822e, 2022'),
}

def railway(tags,country):
    country=country.upper().replace('UK','GB').split('-')[0]
    width,height,source=VEHICLES.get(country,(3.4,4.8,'unknown-country game estimate'))
    electrified=tags.get('electrified','unknown')
    overhead='contact_line' in electrified.split(';')
    # Network Rail South Wales electrification FAQ specifies 4.7m contact wire.
    # Movement/structural margins below are game approximations, not standards.
    wire=4.7 if country=='GB' else max(5.3,height+.5)
    mapped_wire=road_layout.metres(tags.get('contact_line:height'))
    if mapped_wire:wire=mapped_wire
    clearance=max(height+.5,wire+.9 if overhead else 0.,5.6 if country=='GB' else 6.)
    mapped=road_layout.metres(tags.get('maxheight:physical'))
    if mapped:clearance=mapped
    return dict(electrified=electrified,voltage=tags.get('voltage',''),frequency=tags.get('frequency',''),
        contactLineType=tags.get('electrified:contact_line',''),gauge=tags.get('gauge',''),
        loadingGauge=tags.get('loading_gauge',''),railClearanceHeight=clearance,
        railClearanceWidth=max(width+.8,4.2),
        railClearanceSource=('mapped physical clearance' if mapped else source+'; route gauge unavailable, estimated margins/fallback'))



def national_speed(tags,direction=1):
    """UK NSL identity, distinct from an equal numeric local restriction.

    An explicit directional numeric limit overrides a way-wide NSL source.
    Direction follows normalized geometry, including reversed oneway=-1 ways.
    """
    suffix='forward' if direction==1 else 'backward'
    if tags.get('oneway')=='-1':suffix='backward' if suffix=='forward' else 'forward'
    directed='maxspeed:'+suffix
    raw=str(tags.get(directed,tags.get('maxspeed',''))).strip().lower()
    markers={'gb:nsl','gb:nsl_single','gb:nsl_dual'}
    if raw in markers:return True
    source=tags.get('source:maxspeed:'+suffix,tags.get('maxspeed:type:'+suffix,''))
    if directed in tags and not source:return False
    marker=str(source or tags.get('maxspeed:type','') or tags.get('source:maxspeed','')).strip().lower()
    if marker not in markers:return False
    try:mph=float(raw.replace('mph','').strip())
    except ValueError:return False
    return mph==(70 if marker=='gb:nsl_dual' else 60) if marker!='gb:nsl' else mph in (60,70)

def controls(source,features,transform,origin):
    roads=[f for f in features if f['category']=='road']
    junctions={}
    for f in roads:
        for i,n in enumerate(f.get('sourceNodeIds',[])):
            if str(n).startswith('osm:'):junctions.setdefault(n,[]).append((f,i))
        tags=f['tags'];f['controls']=[]
        f['turnLanes']=tags.get('turn:lanes:forward',tags.get('turn:lanes',''))
        f['turnLanesBackward']=tags.get('turn:lanes:backward','')
        if tags.get('oneway')=='-1':
            f['turnLanes']=tags.get('turn:lanes:backward',tags.get('turn:lanes',''))
            f['turnLanesBackward']=tags.get('turn:lanes:forward','')
    junctions={n:items for n,items in junctions.items() if len({f['sourceId'] for f,i in items})>=2}
    def add(f,identity,kind,station,direction,value='',crossing=''):
        station=float(np.clip(station,0,f['stations'][-1]));p=f['line'].interpolate(station)
        a=f['line'].interpolate(max(0,station-1));b=f['line'].interpolate(min(f['line'].length,station+1))
        dx,dz=b.x-a.x,b.y-a.y;length=max(.0001,math.hypot(dx,dz))
        group=identity;best=45.
        if kind=='traffic_signals':
            for node,items in junctions.items():
                q=items[0][0]['xy'][items[0][1]];dist=math.hypot(p.x-q[0],p.y-q[1])
                if dist<best:best=dist;group=node
        f['controls'].append(dict(id=f['sourceId']+':'+str(identity)+':'+str(direction)+':'+kind,kind=kind,group=str(group),
            station=station,direction=direction,value=str(value),crossing=crossing,
            x=p.x,y=float(np.interp(station,f['stations'],f['heights'])),z=p.y,dx=dx/length*direction,dz=dz/length*direction))
    for element in source.get('elements',[]):
        if element.get('type')!='node':continue
        tags=element.get('tags',{});kind=tags.get('highway','');sign=tags.get('traffic_sign','')
        if kind not in ('traffic_signals','stop','give_way','crossing') and not sign:continue
        if tags.get('traffic_signals') in ('blinker','stop'):kind='stop'
        if tags.get('crossing')=='no':continue
        xy=np.asarray(transform.transform(element['lon'],element['lat']))-origin;p=Point(xy)
        candidates=[f for f in roads if 'osm:'+str(element['id']) in f.get('sourceNodeIds',[])]
        if not candidates:
            candidates=sorted(roads,key=lambda f:f['line'].distance(p))[:1]
            if not candidates or candidates[0]['line'].distance(p)>12:continue
        # Shared intersection nodes apply to every connected approach, never to
        # a grade-separated road selected merely by nearest horizontal distance.
        for f in candidates:
            station=f['line'].project(p)
            direction=tags.get('traffic_signals:direction',tags.get('stop:direction',tags.get('direction','')))
            dirs=[1] if f['oneway'] else [1,-1]
            # Geometry was reversed for oneway=-1; explicit node direction must follow it.
            if f['tags'].get('oneway')=='-1':direction={'forward':'backward','backward':'forward'}.get(direction,direction)
            if direction in ('forward','backward'):dirs=[1 if direction=='forward' else -1]
            elif kind in ('give_way','stop') and len(candidates)==1:dirs=[-1 if station<f['line'].length*.5 else 1]
            if f['oneway']:dirs=[d for d in dirs if d==1]
            for d in dirs:
                if kind in ('traffic_signals','stop','give_way') and ((d==1 and station<.01) or (d==-1 and station>f['line'].length-.01)):continue
                if kind in ('traffic_signals','stop','give_way','crossing'):
                    crossing=tags.get('crossing:markings',tags.get('crossing_ref',tags.get('crossing','unknown')))
                    add(f,element['id'],kind,station,d,crossing=crossing)
                    if kind=='crossing' and (tags.get('crossing')=='traffic_signals' or tags.get('crossing:signals')=='yes'):add(f,element['id'],'traffic_signals',station,d)
                # Named international signs; numeric national codes remain source data
                # until an explicit national catalogue entry exists.
                elif sign in ('stop','give_way','no_entry'):add(f,element['id'],sign,station,d)
    for f in roads:
        t=f['tags']
        for d in ([1] if f['oneway'] else [1,-1]):
            key='maxspeed:forward' if d==1 else 'maxspeed:backward'
            if t.get('oneway')=='-1':key='maxspeed:backward' if d==1 else 'maxspeed:forward'
            value=t.get(key,t.get('maxspeed',''))
            if value and value not in ('signals','variable','none'):add(f,f['sourceId']+':speed:'+str(d),'speed',0 if d==1 else f['line'].length,d,'national' if national_speed(t,d) else value)
        if f['oneway'] and not f['roundabout']:
            add(f,f['sourceId']+':oneway','oneway',min(12,f['line'].length*.25),1)
            add(f,f['sourceId']+':entry','no_entry',f['line'].length,-1)
