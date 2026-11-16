import copy,io,json,math,tempfile,unittest,zipfile
from datetime import datetime,timezone
from pathlib import Path
from WorldData import adapters,gtfs
from WorldData.catalogue import Catalogue,contains
from WorldData.model import envelope,usable,validate,utc
from WorldData.cache import Store
from WorldData.bridge import game_snapshot

NOW=utc('2026-11-15T14:00:00Z')
def zipped(files):
    output=io.BytesIO()
    with zipfile.ZipFile(output,'w') as z:
        for name,body in files.items():z.writestr(name,body)
    return output.getvalue()
def timetable():
    return {
        'agency.txt':'agency_id,agency_name,agency_url,agency_timezone\na,Test rail,https://example.org,Europe/London\n',
        'routes.txt':'route_id,agency_id,route_type\nr,a,2\nb,a,3\n',
        'trips.txt':'route_id,service_id,trip_id,trip_headsign\nr,daily,t,Destination\nb,daily,bus,Bus\n',
        'stops.txt':'stop_id,stop_name,stop_lon,stop_lat\nx,A,-3.18,51.48\ny,B,-3.17,51.48\n',
        'calendar.txt':'service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\ndaily,1,1,1,1,1,1,1,20260101,20261231\n',
        'stop_times.txt':'trip_id,arrival_time,departure_time,stop_id,stop_sequence\nt,13:59:00,14:00:00,x,1\nt,14:03:00,14:03:00,y,2\n'}
def convert(files=None,day='2026-11-15'):
    return gtfs.schedule(zipped(files or timetable()),provider='test',country='GB',received=NOW,service_date=day,attribution='Test fixture')

class CatalogueChecks(unittest.TestCase):
    def setUp(self):self.c=Catalogue()
    def test_wales_uses_existing_british_binding(self):self.assertEqual(self.c.terrain_binding(-3.18,51.48,'GB-WLS'),('britain',27700,'terrain.gb'))
    def test_swiss_survey(self):self.assertEqual(self.c.terrain_binding(8.5,46.5,'CH')[:2],('swiss-or-coarse',2056))
    def test_japan_has_global_fallback_not_british_data(self):self.assertEqual(self.c.terrain_binding(139.7,35.7,'JP')[:2],('coarse',3857))
    def test_country_and_coverage_both_required(self):self.assertEqual(self.c.terrain_binding(-3.18,51.48,'US')[0],'coarse')
    def test_live_auth_is_distinct_from_timetable(self):self.assertEqual(self.c.resolve(-3.18,51.48,'GB','rail.live')[0]['reason'],'credentials required')
    def test_credential_and_failure_resolution(self):
        self.assertTrue(self.c.resolve(-3.18,51.48,'GB','rail.live',credentials=['networkrail-feed'])[0]['available'])
        self.assertFalse(self.c.resolve(-3.18,51.48,'GB','rail.live',credentials=['networkrail-feed'],unavailable=['rail.gb.networkrail'])[0]['available'])
    def test_expiry(self):self.assertEqual(self.c.resolve(-3.18,51.48,'GB','rail.timetable',date='2026-12-14')[-1]['reason'],'outside validity dates')
    def test_dateline(self):self.assertTrue(contains([170,-10,-170,10],179,0));self.assertTrue(contains([170,-10,-170,10],-179,0));self.assertFalse(contains([170,-10,-170,10],0,0))
    def test_invalid_point(self):
        with self.assertRaises(ValueError):self.c.resolve(400,0,'','terrain')

class GtfsChecks(unittest.TestCase):
    def test_valid_import_rail_only(self):
        data=convert();self.assertEqual(len(data['records']),1);self.assertEqual(data['records'][0]['stops'][0]['departureUtc'],NOW);self.assertEqual(data['quality'],'published')
    def test_calendar_removal(self):
        files=timetable();files['calendar_dates.txt']='service_id,date,exception_type\ndaily,20261115,2\n';self.assertEqual(convert(files)['records'],[])
    def test_exception_only_service(self):
        files=timetable();files.pop('calendar.txt');files['calendar_dates.txt']='service_id,date,exception_type\ndaily,20261115,1\n';self.assertEqual(len(convert(files)['records']),1)
    def test_overnight_and_dst_anchor(self):
        self.assertEqual(gtfs.clock('25:30:00'),91800)
        noon=datetime(2026,10,25,12,tzinfo=__import__('zoneinfo').ZoneInfo('Europe/London')).timestamp()
        self.assertEqual(gtfs.service_utc('2026-10-25',91800,'Europe/London'),noon-43200+91800)
    def test_reversed_times_refused(self):
        files=timetable();files['stop_times.txt']=files['stop_times.txt'].replace('14:03:00','13:50:00')
        with self.assertRaises(ValueError):convert(files)
    def test_missing_timezone_refused(self):
        files=timetable();files['agency.txt']=files['agency.txt'].replace('Europe/London','Unknown/Nowhere')
        with self.assertRaises(Exception):convert(files)
    def feed(self):
        from google.transit import gtfs_realtime_pb2 as pb
        f=pb.FeedMessage();f.header.gtfs_realtime_version='2.0';f.header.timestamp=int(NOW)
        e=f.entity.add();e.id='vehicle';e.vehicle.trip.trip_id='t';e.vehicle.trip.start_date='20261115';e.vehicle.position.latitude=51.48;e.vehicle.position.longitude=-3.175;e.vehicle.timestamp=int(NOW)-5;e.vehicle.position.speed=5
        return f
    def rt(self,state,feed):return state.convert(feed.SerializeToString(),provider='test',country='GB',received=NOW,attribution='Test fixture')
    def test_real_protobuf_and_units(self):
        d=self.rt(gtfs.RealtimeState(),self.feed());self.assertEqual(d['records'][0]['speedMps'],5);self.assertEqual(d['records'][0]['observedUtc'],NOW-5)
    def test_missing_vehicle_time_not_fabricated_from_feed_time(self):
        f=self.feed();f.entity[0].vehicle.ClearField('timestamp');d=self.rt(gtfs.RealtimeState(),f);self.assertNotIn('observedUtc',d['records'][0]);self.assertEqual(d['quality'],'unknown')
    def test_differential_deletion(self):
        state=gtfs.RealtimeState();f=self.feed();self.rt(state,f);f.ClearField('entity');f.header.incrementality=1;e=f.entity.add();e.id='vehicle';e.is_deleted=True;self.assertEqual(self.rt(state,f)['records'],[])
    def test_future_observation_does_not_poison_state(self):
        state=gtfs.RealtimeState();f=self.feed();self.rt(state,f);f.entity[0].vehicle.timestamp=int(NOW)+100
        with self.assertRaises(ValueError):self.rt(state,f)
        self.assertEqual(state.entities['vehicle'].vehicle.timestamp,int(NOW)-5)
    def test_cancelled_trip_updates(self):
        f=self.feed();f.ClearField('entity');e=f.entity.add();e.id='cancel';e.trip_update.trip.trip_id='t';e.trip_update.trip.start_date='20261115';e.trip_update.trip.schedule_relationship=3
        self.assertTrue(self.rt(gtfs.RealtimeState(),f)['records'][0]['cancelled'])

class AdapterChecks(unittest.TestCase):
    def test_digitraffic_kmh_to_mps(self):
        d=adapters.digitraffic({'type':'FeatureCollection','features':[{'properties':{'trainNumber':1,'departureDate':'2026-11-15','timestamp':'2026-11-15T13:59:55Z','speed':72},'geometry':{'type':'Point','coordinates':[25,60]}}]},received=NOW)
        self.assertEqual(d['records'][0]['speedMps'],20);self.assertEqual(d['country'],'FI')
    def test_unactivated_trust_is_unbound_report_point(self):
        r=adapters.NetworkRailTrust().convert([{'header':{'msg_type':'0003'},'body':{'train_id':'opaque','loc_stanox':'123','actual_timestamp':str(int(NOW*1000))}}],received=NOW)['records'][0]
        self.assertNotIn('position',r);self.assertEqual(r['serviceDate'],'');self.assertEqual(r['reportPoint']['system'],'STANOX')
    def test_activation_then_cancel(self):
        s=adapters.NetworkRailTrust();s.convert([{'header':{'msg_type':'0001'},'body':{'train_id':'a','train_uid':'uid','origin_dep_timestamp':str(int(NOW*1000))}}],received=NOW)
        r=s.convert([{'header':{'msg_type':'0002'},'body':{'train_id':'a'}}],received=NOW)['records'][0];self.assertEqual(r['tripId'],'uid');self.assertEqual(r['serviceDate'],'2026-11-15');self.assertTrue(r['cancelled'])
        self.assertTrue(usable(envelope('test','test','train.observations',[r],NOW,attribution='Fixture'),NOW+600))
        self.assertFalse(s.convert([{'header':{'msg_type':'0005'},'body':{'train_id':'a'}}],received=NOW)['records'][0]['cancelled'])
    def test_schedule_overlay_cancellation(self):
        row={'CIF_train_uid':'uid','schedule_start_date':'2026-11-01','schedule_end_date':'2026-12-01','schedule_days_runs':'1111111','CIF_stp_indicator':'P','schedule_segment':{'schedule_location':[{'tiploc_code':'A','departure':'1400'},{'tiploc_code':'B','arrival':'1403H'}]}}
        normal=adapters.networkrail_schedule([{'JsonScheduleV1':row}],received=NOW,service_date='2026-11-15');self.assertEqual(normal['records'][0]['stops'][1]['arrivalUtc'],NOW+210)
        cancelled=copy.deepcopy(row);cancelled['CIF_stp_indicator']='C';self.assertEqual(adapters.networkrail_schedule([{'JsonScheduleV1':row},{'JsonScheduleV1':cancelled}],received=NOW,service_date='2026-11-15')['records'],[])
    def test_osm_coordinates_and_identity(self):
        d=adapters.osm({'elements':[{'type':'way','id':7,'tags':{'highway':'primary'},'geometry':[{'lon':-3,'lat':51},{'lon':-3,'lat':52}]}]},received=NOW)
        self.assertEqual(d['records'][0]['geometry'][0],[-3,51]);self.assertEqual(d['records'][0]['id'],'osm:way:7')
    def test_raster_role_units_datum_are_separate(self):
        import numpy as np,rasterio
        from rasterio.transform import from_origin
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'surface.tif'
            with rasterio.open(path,'w',driver='GTiff',width=2,height=2,count=1,dtype='float32',crs='EPSG:4326',transform=from_origin(-3,52,.001,.001)) as d:d.write(np.zeros((1,2,2),dtype='float32'));d.set_band_unit(1,'foot')
            r=adapters.raster(path,role='surface',provider='test',received=NOW,vertical_datum='native-unconverted',attribution='Fixture')['records'][0]
            self.assertEqual(r['unitToMetres'],.3048);self.assertEqual(r['role'],'surface');self.assertEqual(r['verticalDatum'],'native-unconverted')
    def test_actual_worker_metadata_hook_preserves_numeric_world(self):
        import types,numpy as np,rasterio
        from rasterio.transform import from_origin
        from WorldData.worker import install
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);raw=root/'Raw';raw.mkdir();path=raw/'survey-dsm.tif'
            with rasterio.open(path,'w',driver='GTiff',width=2,height=2,count=1,dtype='float32',crs='EPSG:27700',transform=from_origin(320000,200000,1,1)) as d:d.write(np.zeros((1,2,2),dtype='float32'))
            model={'heights':[1,2,3,4]};manifest={'sourceReceipts':[{'file':path.name,'sha256':'fixture-hash'}]};events=[]
            class Pipeline:
                def __init__(self):self.cache=root;self.raw=raw;self.frame=types.SimpleNamespace(provider='britain',vertical_datum='native-unconverted');self.event=lambda *a,**kw:events.append((a,kw))
                def _prepare_locked(self,tile):return model,manifest,True
            module=types.SimpleNamespace(Pipeline=Pipeline);install(module);pipeline=Pipeline();result=pipeline._prepare_locked((0,0))
            self.assertIs(result[0],model);self.assertEqual(model['heights'],[1,2,3,4]);self.assertTrue(events)
            output=json.loads(next((root/'WorldDataReceipts').glob('*.json')).read_text());self.assertEqual(output['records'][0]['role'],'surface');self.assertEqual(output['quality'],'measured')
            pipeline._prepare_locked((0,0));self.assertEqual(len(events),1)

class BridgeAndCacheChecks(unittest.TestCase):
    def binding(self):return {'r':dict(pathId='mapped-real-path',stockId='gb.197.2',sourceWays=['123'],stops={'x':100,'y':900},direction=1,geographicPath=[[-3.18,51.48,100],[-3.17,51.48,900]])}
    def test_schedule_export_reaches_existing_game_contract(self):
        r,u=game_snapshot(convert(),None,self.binding(),NOW,['gb.197.2']);self.assertFalse(u);self.assertEqual(r['schema'],1);self.assertEqual(r['services'][0]['calls'][0]['metres'],100)
    def test_unmapped_route_never_invents_rails(self):
        r,u=game_snapshot(convert(),None,{},NOW,['gb.197.2']);self.assertFalse(r['services']);self.assertEqual(len(u),1)
    def test_gps_report_projects_only_to_explicit_path(self):
        obs=envelope('test','test','train.observations',[dict(id='v',tripId='t',serviceDate='2026-11-15',observedUtc=NOW-5,position=[-3.175,51.48],speedMps=5)],NOW,quality='observed',attribution='Fixture')
        r,_=game_snapshot(convert(),obs,self.binding(),NOW,['gb.197.2']);self.assertAlmostEqual(r['services'][0]['reports'][0]['metres'],500)
        obs['records'][0]['position']=[-3.175,51.49];r,_=game_snapshot(convert(),obs,self.binding(),NOW,['gb.197.2']);self.assertFalse(r['services'][0]['reports'])
    def test_date_mismatch_does_not_cancel_other_run(self):
        obs=envelope('test','test','train.observations',[dict(id='v',tripId='t',serviceDate='2026-11-14',cancelled=True)],NOW,attribution='Fixture');r,_=game_snapshot(convert(),obs,self.binding(),NOW,['gb.197.2']);self.assertFalse(r['services'][0]['cancelled'])
    def test_real_cancel_and_delay(self):
        obs=envelope('test','test','train.observations',[dict(id='v',tripId='t',serviceDate='2026-11-15',cancelled=True,stopUpdates=[dict(stopId='y',arrivalDelaySeconds=30,departureDelaySeconds=30)])],NOW,attribution='Fixture');r,_=game_snapshot(convert(),obs,self.binding(),NOW,['gb.197.2']);self.assertTrue(r['services'][0]['cancelled']);self.assertEqual(utc(r['services'][0]['calls'][1]['arrivalUtc']),NOW+210)
    def test_cache_expiry_quality_and_policy_identity(self):
        with tempfile.TemporaryDirectory() as temp:
            store=Store(temp,1024*1024);key=store.key('test','a','v1','wgs84','abc');value=envelope('test','a','train.observations',[dict(id='a',observedUtc=NOW-10)],NOW,quality='estimated',attribution='Fixture');store.put(key,value)
            self.assertEqual(store.get(key,NOW)['quality'],'estimated');self.assertIsNone(store.get(key,NOW+200));self.assertNotEqual(key,store.key('test','a','v2','wgs84','abc'))
    def test_cache_rejects_path_traversal(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):Store(temp).get('../private',NOW)

if __name__=='__main__':unittest.main()
