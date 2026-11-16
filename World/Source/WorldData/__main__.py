"""Offline collector/converter CLI. Credentials never enter the shipped game.

Provider-specific authenticated streaming transports remain outside this CLI;
authorised collectors can pass bounded input files or call the adapters directly.
"""
import argparse,json,hashlib
from pathlib import Path
import time
from .catalogue import Catalogue
from .model import validate
from . import adapters,gtfs
from .bridge import game_snapshot

def main():
    parser=argparse.ArgumentParser();sub=parser.add_subparsers(dest='command',required=True)
    index=sub.add_parser('resolve');index.add_argument('longitude',type=float);index.add_argument('latitude',type=float);index.add_argument('country');index.add_argument('capability')
    convert=sub.add_parser('convert');convert.add_argument('adapter',choices=['gtfs-schedule','gtfs-realtime','gtfs-realtime-json','digitraffic-geojson','networkrail-trust','networkrail-schedule','osm-json']);convert.add_argument('input',type=Path);convert.add_argument('output',type=Path);convert.add_argument('--provider',default='configured-provider');convert.add_argument('--country',default='');convert.add_argument('--date');convert.add_argument('--received',type=float);convert.add_argument('--attribution',default='Configured provider; retain its source licence')
    bridge=sub.add_parser('export-game');bridge.add_argument('schedule',type=Path);bridge.add_argument('bindings',type=Path);bridge.add_argument('output',type=Path);bridge.add_argument('--observations',type=Path);bridge.add_argument('--now',type=float);bridge.add_argument('--stock-ids',nargs='+',required=True)
    args=parser.parse_args()
    if args.command=='resolve':
        print(json.dumps(Catalogue().resolve(args.longitude,args.latitude,args.country,args.capability),indent=2));return
    if args.command=='export-game':
        result,unbound=game_snapshot(json.loads(args.schedule.read_text()),json.loads(args.observations.read_text()) if args.observations else None,json.loads(args.bindings.read_text()),args.now if args.now is not None else time.time(),args.stock_ids)
        print(json.dumps(dict(services=len(result['services']),unboundCount=len(unbound))))
    else:
        if args.input.stat().st_size>128*1024*1024:raise ValueError('Input limit')
        payload=args.input.read_bytes();now=args.received if args.received is not None else time.time();identity=dict(provider=args.provider,country=args.country,received=now,attribution=args.attribution)
        if args.adapter=='gtfs-schedule':
            if not args.date:raise ValueError('An explicit GTFS service date is required')
            result=gtfs.schedule(payload,service_date=args.date,**identity)
        elif args.adapter.startswith('gtfs-realtime'):result=gtfs.RealtimeState().convert(payload,json_input=args.adapter.endswith('-json'),**identity)
        elif args.adapter=='digitraffic-geojson':result=adapters.digitraffic(json.loads(payload),received=now)
        elif args.adapter=='networkrail-trust':result=adapters.NetworkRailTrust().convert(json.loads(payload),received=now)
        elif args.adapter=='networkrail-schedule':
            if not args.date:raise ValueError('An explicit rail service date is required')
            result=adapters.networkrail_schedule(json.loads(payload),received=now,service_date=args.date)
        else:result=adapters.osm(json.loads(payload),received=now)
        result['sourceHash']=hashlib.sha256(payload).hexdigest();validate(result,now)
    args.output.parent.mkdir(parents=True,exist_ok=True);temporary=args.output.with_name(args.output.name+'.pending');temporary.write_text(json.dumps(result,separators=(',',':'),allow_nan=False)+'\n');temporary.replace(args.output)
if __name__=='__main__':main()
