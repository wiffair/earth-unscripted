import unittest,json,tempfile,time,threading
from pathlib import Path
from unittest.mock import patch
from WorldData import public_rail as rail,public_terrain as terrain
from WorldData.catalogue import Catalogue
from WorldData.model import validate

class PublicProviders(unittest.TestCase):
 def test_location_selection_and_fallback(self):
  c=Catalogue()
  for lon,lat,country,expected in [(4.9,52.37,'NL','terrain.nl.ahn'),(-75.7,45.42,'CA','terrain.ca.hrdem'),(-71,42.3,'US','terrain.us.3dep'),(139,35,'JP','terrain.global')]:
   self.assertEqual(c.terrain_binding(lon,lat,country)[2],expected)
  self.assertFalse(c.resolve(-122,38,'US','rail.live'))
  self.assertEqual(c.resolve(-71.06,42.36,'US','rail.live')[0]['provider']['id'],'rail.us.mbta')
  rows=c.resolve(4.9,52.37,'NL','terrain',unavailable=['terrain.nl.ahn']);self.assertEqual(rows[0]['provider']['id'],'terrain.global')
 def test_entur_rejects_modes_and_stale(self):
  now=1000
  def row(mode='RAIL',stamp=990):return dict(vehicleId=mode+str(stamp),mode=mode,lastUpdatedEpochSecond=stamp,location=dict(latitude=60,longitude=10),serviceJourney=dict(id='trip'),bearing=180)
  e=rail.entur(json.dumps(dict(data=dict(vehicles=[row(),row('BUS'),row(stamp=800)]))),now)
  validate(e,now);self.assertEqual(len(e['records']),1);self.assertEqual(e['records'][0]['gaugeMm'],1435)
  self.assertNotIn('speedMps',e['records'][0])
  with self.assertRaises(ValueError):rail.entur(b'{"errors":["bad"]}',now)
 def test_irish_reports_are_approximate(self):
  now=1794747600
  payload=b'<root><train><TrainStatus>R</TrainStatus><TrainLatitude>53.3</TrainLatitude><TrainLongitude>-6.2</TrainLongitude><TrainCode>ABC</TrainCode><TrainDate>15 Nov 2026</TrainDate></train></root>'
  e=rail.irishrail(payload,now);validate(e,now);self.assertEqual(len(e['records']),1);r=e['records'][0]
  self.assertEqual((r['gaugeMm'],r['positionQuality'],r['timestampQuality']),(1600,'estimated','receipt-only'))
  self.assertEqual(rail.irishrail(payload.replace(b'>R<',b'>N<'),now)['records'],[])
  with self.assertRaises(ValueError):rail.irishrail(b'<!DOCTYPE foo []><root/>',now)
 def test_gtfs_filter_excludes_buses_and_trams(self):
  from io import BytesIO
  from zipfile import ZipFile
  payload=BytesIO()
  tables={'routes.txt':'route_id,route_type\nr,2\nb,3\nt,0\n','trips.txt':'route_id,trip_id\nr,train\nb,bus\nt,tram\n','stop_times.txt':'trip_id,stop_id\ntrain,s1\nbus,s2\ntram,s3\n','stops.txt':'stop_id\ns1\ns2\ns3\n'}
  with ZipFile(payload,'w') as z:
   for name,text in tables.items():z.writestr(name,text)
  filtered,ids=rail.rail_only_zip(payload.getvalue());self.assertEqual(ids,{'train'})
  with ZipFile(BytesIO(filtered)) as z:
   self.assertNotIn(b'bus',z.read('stop_times.txt'));self.assertNotIn(b's2',z.read('stops.txt'))
 def test_nonblocking_failure_cooldown(self):
  release=threading.Event();calls=[]
  def fetch(*args,**kw):calls.append(1);release.wait(2);raise TimeoutError()
  with tempfile.TemporaryDirectory() as t:
   collector=rail.Collector(t,fetcher=fetch,clock=lambda:1000)
   before=time.monotonic();reply=collector.snapshot(10,60,'NO');elapsed=time.monotonic()-before
   self.assertLess(elapsed,.1);self.assertIsNone(reply['observations']);release.set()
   for future in list(collector.states.values()):future['future'].result(3)
   self.assertIn('unavailable',collector.snapshot(10,60,'NO')['status']);self.assertEqual(len(calls),1);collector.close()
 def test_native_numeric_samples(self):
  folder=Path(__file__).resolve().parents[2]/'Beta/Feedback44/Probes'
  if not folder.exists():self.skipTest('Online evidence available in workspace only')
  for name,epsg in [('nl',28992),('ca',3979),('us',3857)]:
   proof=terrain.inspect(folder/(name+'_crop.data'),epsg);self.assertGreater(proof['validPixels'],0)
  with self.assertRaises(ValueError):terrain.inspect(folder/'nl_crop.data',3979)
 def test_generator_identity_preserves_existing_areas(self):
  import types,sys
  import importlib
  fake=types.ModuleType('elevation_provider');fake.acquire=lambda *a:'old';pipeline=types.SimpleNamespace(generator_identity=lambda frame=None:'oldhash')
  fit=types.ModuleType('profile_safety');fit.road_grades=lambda *a:'original'
  with patch.dict(sys.modules,{'elevation_provider':fake,'profile_safety':fit}):
   terrain.install(pipeline)
   self.assertEqual(fit.road_grades([],[],{},.5)['nodes'],0)
   self.assertEqual(pipeline.generator_identity(types.SimpleNamespace(provider='britain')),'oldhash')
   self.assertNotEqual(pipeline.generator_identity(types.SimpleNamespace(provider='nl-ahn-v1')),'oldhash')
 def test_no_colour_maps_or_fabricated_surface(self):
  kinds=list(terrain.requests('us-3dep-v1',0,0));self.assertEqual([k for k,u in kinds],['dtm']);self.assertIn('pixelType=F32',kinds[0][1])
  self.assertEqual(len(list(terrain.requests('nl-ahn-v1',0,0))),2)

if __name__=='__main__':unittest.main()
