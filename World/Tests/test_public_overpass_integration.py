import hashlib,importlib.util,json,tempfile,types,unittest
from pathlib import Path
from unittest.mock import patch
from urllib.parse import urlencode
import public_overpass
from test_public_overpass import future_fixture,FUTURE,RAW
spec=importlib.util.spec_from_file_location('proposal_map_transport',Path(__file__).resolve().parents[1]/'Source/map_transport.py');transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)
QUERY='[out:json];way[highway](0,0,0.01,0.01);way[railway](0,0,0.01,0.01);out body geom;'
BODY=urlencode({'data':QUERY}).encode('ascii')
SOURCE='https://overpass.private.coffee/api/interpreter'

class Integration(unittest.TestCase):
    def pipeline(self,fetch):
        return types.SimpleNamespace(fetch=fetch,OSM_MAX_AGE_SECONDS=86400,sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest(),incomplete_osm_geometry=lambda parsed,bounds:False)
    def test_drive_failure_new_instance_secure_transport_and_receipt_cache(self):
        calls=[]
        def fetch(url,*args,**kwargs):calls.append(url);return future_fixture() if url==public_overpass.DIRECTORY else b'{"elements":[{"type":"way","id":1}]}'
        def failed(*args,**kwargs):raise TimeoutError('simulated static service failure')
        with tempfile.TemporaryDirectory() as folder,patch.object(public_overpass,'secure_request',fetch),patch.object(transport,'regional',side_effect=AssertionError('regional must not precede successful discovered source')):
            transport._failed.clear()
            for host in ('overpass-api.de','maps.mail.ru'):transport._failed[host]=transport.time.monotonic()+180
            pipeline=self.pipeline(failed);transport.install(pipeline);path=Path(folder)/'area.json';events=[]
            result=pipeline.fetch(path,SOURCE,lambda *args,**kwargs:events.append((args,kwargs)),body=BODY)
            self.assertEqual(result['url'],FUTURE);self.assertTrue(result['publicDirectory']);self.assertEqual(calls,[public_overpass.DIRECTORY,FUTURE]);self.assertEqual(json.loads(path.read_bytes())['elements'][0]['id'],1)
            with patch.object(public_overpass,'secure_request',side_effect=AssertionError('cached area must not redownload')):
                cached=pipeline.fetch(path,SOURCE,lambda *args,**kwargs:None,body=BODY)
            self.assertTrue(cached['cached']);self.assertTrue(cached['publicDirectory']);self.assertEqual(cached['sha256'],hashlib.sha256(path.read_bytes()).hexdigest())
    def test_one_failed_host_tries_healthy_untried_current_mirror(self):
        calls=[]
        def fetch(url,*args,**kwargs):calls.append(url);return RAW if url==public_overpass.DIRECTORY else b'{"elements":[{"type":"way","id":2}]}'
        def failed(*args,**kwargs):raise TimeoutError('only chosen primary failed')
        with tempfile.TemporaryDirectory() as folder,patch.object(public_overpass,'secure_request',fetch),patch.object(transport,'regional',side_effect=AssertionError('healthy untried mirror must recover')):
            transport._failed.clear();pipeline=self.pipeline(failed);transport.install(pipeline)
            result=pipeline.fetch(Path(folder)/'area.json',SOURCE,lambda *args,**kwargs:None,body=BODY)
            self.assertEqual(result['url'],'https://overpass-api.de/api/interpreter');self.assertEqual(calls,[public_overpass.DIRECTORY,'https://overpass-api.de/api/interpreter'])
    def test_successful_existing_source_does_not_refresh_directory(self):
        with tempfile.TemporaryDirectory() as folder,patch.object(public_overpass,'secure_request',side_effect=AssertionError('no discovery on successful static path')):
            transport._failed.clear()
            def okay(path,url,event,**kwargs):
                data=b'{"elements":[{"type":"way","id":1}]}';path.write_bytes(data);path.with_suffix('.json.receipt.json').write_text(json.dumps({'sha256':hashlib.sha256(data).hexdigest(),'url':url,'fetchedUTC':0}));return {'url':url,'sha256':hashlib.sha256(data).hexdigest()}
            pipeline=self.pipeline(okay);transport.install(pipeline);result=pipeline.fetch(Path(folder)/'area.json',SOURCE,lambda *args,**kwargs:None,body=BODY);self.assertNotIn('publicDirectory',result)
    def test_no_new_directory_source_preserves_existing_regional_fallback(self):
        # Directory has only the primary here: recovery must not send it through
        # original urllib a second time or prevent the existing bounded fallback.
        obj=json.loads(RAW);text=obj['parse']['wikitext'];start=text.index('=== Instances with global data coverage ===');end=text.index('=== Instances with data only for a specific region ===');table='=== Instances with global data coverage ===\n{| class="wikitable"\n|-\n!Name\n!API Endpoint\n!Usage policy\n|-\n|Primary\n|'+SOURCE+'\n|Free public use.\n|}\n';obj['parse']['wikitext']=text[:start]+table+text[end:]
        with tempfile.TemporaryDirectory() as folder,patch.object(public_overpass,'secure_request',return_value=json.dumps(obj).encode()),patch.object(transport,'regional',return_value={'regional':True}) as regional:
            transport._failed.clear();pipeline=self.pipeline(lambda *args,**kwargs:(_ for _ in ()).throw(TimeoutError()));transport.install(pipeline)
            self.assertTrue(pipeline.fetch(Path(folder)/'area.json',SOURCE,lambda *args,**kwargs:None,body=BODY)['regional']);self.assertEqual(regional.call_count,1)
if __name__=='__main__':unittest.main()

class ReceiptRecovery(unittest.TestCase):
    pipeline=Integration.pipeline
    def test_malformed_public_receipts_allow_fresh_primary(self):
        valid_data=b'{"elements":[{"type":"way","id":9}]}'
        good={'url':FUTURE,'sha256':hashlib.sha256(valid_data).hexdigest(),'fetchedUTC':transport.time.time(),'publicDirectory':True,'querySha256':hashlib.sha256(QUERY.encode()).hexdigest()}
        variants=[b'[]',b'{',json.dumps(dict(good,fetchedUTC='bad')).encode(),json.dumps(dict(good,fetchedUTC=float('nan'))).encode(),json.dumps(dict(good,sha256=2)).encode(),json.dumps(dict(good,url=[])).encode(),json.dumps(dict(good,querySha256=None)).encode(),json.dumps(dict(good,publicDirectory='yes')).encode(),json.dumps(dict(good,regional=True)).encode(),b'x'*32769,json.dumps({'url':SOURCE,'sha256':good['sha256'],'fetchedUTC':'bad','regional':True}).encode()]
        for raw in variants:
            with self.subTest(receipt=raw[:60]),tempfile.TemporaryDirectory() as folder,patch.object(public_overpass,'secure_request',side_effect=AssertionError('fresh primary must not need directory')):
                path=Path(folder)/'area.json';receipt=path.with_suffix('.json.receipt.json');path.write_bytes(valid_data);receipt.write_bytes(raw);calls=[]
                def primary(path,url,event,**kwargs):
                    self.assertFalse(receipt.exists(),'invalid metadata is removed before original fetch sees it');self.assertEqual(path.read_bytes(),valid_data,'valid area payload was not removed');calls.append(url)
                    row={'url':url,'sha256':hashlib.sha256(valid_data).hexdigest(),'fetchedUTC':transport.time.time()};receipt.write_text(json.dumps(row));return {'url':url,'sha256':row['sha256']}
                transport._failed.clear();pipeline=self.pipeline(primary);transport.install(pipeline);result=pipeline.fetch(path,SOURCE,lambda *args,**kwargs:None,body=BODY)
                self.assertEqual(len(calls),1);self.assertEqual(result['sha256'],good['sha256']);self.assertIsInstance(json.loads(receipt.read_bytes()),dict)
    def test_malformed_public_receipt_allows_secure_recovery(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'area.json';receipt=path.with_suffix('.json.receipt.json');path.write_bytes(b'{"elements":[{"type":"way","id":0}]}');receipt.write_bytes(b'[]')
            def failed(*args,**kwargs):self.assertFalse(receipt.exists());raise TimeoutError('primary unavailable')
            def secure(url,*args,**kwargs):return RAW if url==public_overpass.DIRECTORY else b'{"elements":[{"type":"way","id":10}]}'
            with patch.object(public_overpass,'secure_request',secure),patch.object(transport,'regional',side_effect=AssertionError('secure recovery must work')):
                transport._failed.clear();pipeline=self.pipeline(failed);transport.install(pipeline);result=pipeline.fetch(path,SOURCE,lambda *args,**kwargs:None,body=BODY)
            self.assertTrue(result['publicDirectory']);self.assertEqual(json.loads(path.read_bytes())['elements'][0]['id'],10);self.assertEqual(json.loads(receipt.read_bytes())['sha256'],hashlib.sha256(path.read_bytes()).hexdigest())

class PrimaryCooldown(unittest.TestCase):
    pipeline=Integration.pipeline
    def test_cooling_primary_still_checks_remaining_public_sources(self):
        query='[out:json];way[highway](51.48,-3.19,51.49,-3.18);way[railway](51.48,-3.19,51.49,-3.18);out body geom;';body=urlencode({'data':query}).encode();calls=[]
        def secure(url,*args,**kwargs):calls.append(url);return RAW if url==public_overpass.DIRECTORY else b'{"elements":[{"type":"way","id":4}]}'
        def primary(*args,**kwargs):raise AssertionError('cooling primary must not be queried again')
        with tempfile.TemporaryDirectory() as folder,patch.object(public_overpass,'secure_request',secure),patch.object(transport,'regional',side_effect=AssertionError('healthy remaining public instance should recover')):
            transport._failed.clear();transport._failed['overpass.atownsend.org.uk']=transport.time.monotonic()+180
            pipeline=self.pipeline(primary);transport.install(pipeline)
            result=pipeline.fetch(Path(folder)/'area.json','https://overpass.atownsend.org.uk/api/interpreter',lambda *args,**kwargs:None,body=body)
            self.assertTrue(result['publicDirectory']);self.assertEqual(calls,[public_overpass.DIRECTORY,'https://overpass-api.de/api/interpreter'])
