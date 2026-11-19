"""Offline admission/cache/pinned-transport tests; no public map queries."""
import json,socket,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import public_overpass as module
RAW=(Path(__file__).with_name('Fixtures')/'PublicOverpass'/'official-api-response.json').read_bytes()
FUTURE='https://future.example.org/api/interpreter'

def future_fixture():
    obj=json.loads(RAW);text=obj['parse']['wikitext'];start=text.index('=== Instances with global data coverage ===');end=text.index('|}',start)
    row='|-\n|Future public instance\n|'+FUTURE+'\n|0.7.62\n|yes\n|small\n|Free public global use, no API key.\n'
    obj['parse']['wikitext']=text[:end]+row+text[end:];return json.dumps(obj).encode()

class Tests(unittest.TestCase):
    def test_actual_official_schema_and_credential_rows(self):
        self.assertEqual(set(module.parse_directory(RAW)),{'https://overpass-api.de/api/interpreter','https://maps.mail.ru/osm/tools/overpass/api/interpreter','https://overpass.private.coffee/api/interpreter'})
    def test_new_public_endpoint_is_actually_queried_not_static_intersection(self):
        calls=[]
        def fetch(url,limit,seconds,body=None):
            calls.append((url,body));return future_fixture() if url==module.DIRECTORY else b'{"elements":[{"type":"way","id":1}]}'
        with tempfile.TemporaryDirectory() as folder:
            result=module.recover(folder,'[out:json];way(0,0,.01,.01);out;',module.parse_directory(RAW),fetch)
            self.assertEqual(result[1],FUTURE);self.assertEqual([c[0] for c in calls],[module.DIRECTORY,FUTURE]);self.assertIn(b'data=',calls[1][1])
    def test_url_bounds(self):
        for value in ['http://future.example.org/api/interpreter','https://user:pass@future.example.org/api/interpreter','https://127.0.0.1/api/interpreter','https://[::1]/api/interpreter','https://future.example.org:444/api/interpreter','https://future.example.org/api/interpreter?key=x','https://future.example.org/api/../interpreter','https://future.example.org/YOUR_API_KEY/interpreter','https://future.example.org/api/interpreter#x']:
            with self.assertRaises(ValueError):module.endpoint(value)
    def test_public_dns(self):
        for value in ['127.0.0.1','10.1.2.3','172.16.1.1','192.168.1.2','169.254.169.254','100.64.0.1','0.0.0.0','224.0.0.1','192.0.2.1','::1','fc00::1','fe80::1','ff02::1','::ffff:127.0.0.1']:
            self.assertFalse(module.public_ip(value),value)
        self.assertTrue(module.public_ip('8.8.8.8'))
        with patch.object(module.socket,'getaddrinfo',return_value=[(socket.AF_INET,socket.SOCK_STREAM,6,'',('127.0.0.1',443))]):
            with self.assertRaises(ValueError):module._resolve('future.example.org',module.time.monotonic()+2)
    def test_cache_ttl_backoff_and_no_startup_network(self):
        now=[1000000];calls=[]
        def fetch(*args,**kwargs):calls.append(args);return RAW
        with tempfile.TemporaryDirectory() as folder:
            directory=module.Directory(folder,fetch,lambda:now[0]);self.assertEqual(calls,[])
            expected=directory.candidates_after_failure();self.assertEqual(len(calls),1)
            self.assertEqual(directory.candidates_after_failure(),expected);self.assertEqual(len(calls),1)
            now[0]+=module.TTL+1
            def failed(*args,**kwargs):calls.append(args);raise TimeoutError()
            directory.fetch=failed;self.assertEqual(directory.candidates_after_failure(),expected);self.assertEqual(len(calls),2)
            self.assertEqual(directory.candidates_after_failure(),expected);self.assertEqual(len(calls),2)
    def test_pinned_address_original_tls_host_no_redirect(self):
        connected=[];sni=[];requests=[]
        class Sock:
            def settimeout(self,*args):pass
            def connect(self,value):connected.append(value)
            def close(self):pass
            def shutdown(self,*args):pass
        class Context:
            def wrap_socket(self,sock,server_hostname):sni.append(server_hostname);return sock
        class Response:
            status=200
            def getheader(self,key,default=None):return default
            def read1(self,count):
                if getattr(self,'done',False):return b''
                self.done=True;return b'{}'
        class Connection:
            def __init__(self,*args,**kwargs):self.sock=None
            def request(self,*args,**kwargs):requests.append((args,kwargs))
            def getresponse(self):return Response()
            def close(self):pass
        with patch.object(module,'_resolve',return_value=[(socket.AF_INET,('8.8.8.8',443))]),patch.object(module.socket,'socket',side_effect=lambda *args:Sock()),patch.object(module.ssl,'create_default_context',return_value=Context()),patch.object(module.http.client,'HTTPSConnection',Connection):
            self.assertEqual(module.secure_request(FUTURE,100),b'{}')
            self.assertEqual(connected,[('8.8.8.8',443)]);self.assertEqual(sni,['future.example.org']);self.assertEqual(requests[0][1]['headers']['Host'],'future.example.org')
            with patch.object(Response,'status',302):
                with self.assertRaises(ValueError):module.secure_request(FUTURE,100)
    def test_prose_regional_and_key_endpoints_never_discovered(self):
        obj=json.loads(RAW);obj['parse']['wikitext']+='\nhttps://evil.example.org/api/interpreter\n';self.assertNotIn('https://evil.example.org/api/interpreter',module.parse_directory(json.dumps(obj).encode()))
        self.assertNotIn('https://overpass.osm.ch/api/interpreter',module.parse_directory(RAW))
        self.assertNotIn('YOUR_API_KEY',str(module.parse_directory(RAW)))

class BoundsAndDeadlineTests(unittest.TestCase):
    def request(self,serve,maximum=100,seconds=.15,require_closed=False):
        import threading,time
        listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(1);listener.settimeout(1);address=listener.getsockname();closed=threading.Event();accepted=threading.Event()
        def server():
            try:
                client,_=listener.accept();accepted.set()
                with client:
                    client.recv(16384);serve(client,closed)
            except (OSError,TimeoutError):pass
            finally:listener.close()
        worker=threading.Thread(target=server,daemon=True);worker.start()
        class Context:
            def wrap_socket(self,sock,server_hostname):return sock
        began=time.monotonic()
        try:
            with patch.object(module,'_resolve',return_value=[(socket.AF_INET,address)]),patch.object(module.ssl,'create_default_context',return_value=Context()):
                return module.secure_request(FUTURE,maximum,seconds)
        finally:
            self.assertLess(time.monotonic()-began,1,"socket watchdog bounds actual blocking header/body reads")
            self.assertTrue(accepted.wait(.5),'deadline test reached actual socket transport')
            worker.join(timeout=1);listener.close()
            if require_closed:self.assertTrue(closed.is_set(),'deadline actually shuts down the blocked socket')
    def test_http10_and_connection_close_retains_body_socket(self):
        for version in [b'HTTP/1.0',b'HTTP/1.1']:
            def serve(client,closed):client.sendall(version+b' 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\n{}')
            self.assertEqual(self.request(serve),b'{}')
    def test_connection_close_deadline_still_closes_actual_body_socket(self):
        def serve(client,closed):
            client.sendall(b'HTTP/1.0 200 OK\r\nContent-Length: 50\r\n\r\n{}');client.settimeout(.8)
            if client.recv(1)==b'':closed.set()
        with self.assertRaises((OSError,ValueError,module.http.client.HTTPException)):self.request(serve,require_closed=True)
    def test_oversized_declared_body(self):
        def serve(client,closed):client.sendall(b'HTTP/1.1 200 OK\r\nContent-Length: 101\r\n\r\n')
        with self.assertRaises(ValueError):self.request(serve)
    def test_oversized_chunked_body(self):
        def serve(client,closed):client.sendall(b'HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n65\r\n'+b'x'*101+b'\r\n0\r\n\r\n')
        with self.assertRaises(ValueError):self.request(serve)
    def test_oversized_header_line_and_total(self):
        for headers in [b'X-Long: '+b'x'*5000+b'\r\n',b''.join(b'X-'+str(i).encode()+b': '+b'x'*900+b'\r\n' for i in range(20))]:
            def serve(client,closed):client.sendall(b'HTTP/1.1 200 OK\r\n'+headers+b'\r\n{}')
            with self.assertRaises(ValueError):self.request(serve)
    def test_unsolicited_interim_cannot_reset_header_budget(self):
        def serve(client,closed):client.sendall(b'HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 200 OK\r\n'+b''.join(b'X-'+str(i).encode()+b': '+b'x'*900+b'\r\n' for i in range(20))+b'\r\n{}')
        with self.assertRaisesRegex(ValueError,'interim'):self.request(serve)
    def test_slow_header_socket_closed(self):
        def serve(client,closed):
            client.sendall(b'HTTP/1.1 200 OK\r\nX-Slow: ');client.settimeout(.8)
            if client.recv(1)==b'':closed.set()
        with self.assertRaises((OSError,ValueError,module.http.client.HTTPException)):self.request(serve,require_closed=True)
    def test_slow_body_socket_closed(self):
        def serve(client,closed):
            client.sendall(b'HTTP/1.1 200 OK\r\nContent-Length: 50\r\n\r\n{}');client.settimeout(.8)
            if client.recv(1)==b'':closed.set()
        with self.assertRaises((OSError,ValueError,module.http.client.HTTPException)):self.request(serve,require_closed=True)
    def test_bad_schema_duplicate_json_and_oversize_directory(self):
        for raw in [b'{"parse":{},"parse":{}}',b'x'*(module.CAP+1),b'{"parse":{"title":"Wrong","pageid":35322}}']:
            with self.assertRaises((ValueError,KeyError)):module.parse_directory(raw)
    def test_future_account_entry_is_rejected(self):
        obj=json.loads(future_fixture());obj['parse']['wikitext']=obj['parse']['wikitext'].replace('Free public global use, no API key.','A contributor account is required.')
        self.assertNotIn(FUTURE,module.parse_directory(json.dumps(obj).encode()))
    def test_expired_corrupt_cache_and_failed_backoff(self):
        now=[1000000];calls=[]
        def fetch(*args,**kwargs):calls.append(args);return RAW
        with tempfile.TemporaryDirectory() as folder:
            d=module.Directory(folder,fetch,lambda:now[0]);d.candidates_after_failure();now[0]+=module.STALE+1
            def failed(*args,**kwargs):calls.append(args);raise TimeoutError()
            d.fetch=failed;self.assertEqual(d.candidates_after_failure(),[]);self.assertEqual(d.candidates_after_failure(),[]);self.assertEqual(len(calls),2)
    def test_concurrent_directory_refresh_is_single_and_atomic(self):
        import threading,time
        calls=[];entered=threading.Event();release=threading.Event()
        def fetch(*args,**kwargs):calls.append(args);entered.set();release.wait(1);return RAW
        with tempfile.TemporaryDirectory() as folder:
            first=module.Directory(folder,fetch);result=[];thread=threading.Thread(target=lambda:result.append(first.candidates_after_failure()));thread.start();self.assertTrue(entered.wait(1))
            self.assertEqual(module.Directory(folder,fetch).candidates_after_failure(),[]);release.set();thread.join(1)
            self.assertEqual(len(calls),1);self.assertEqual(result[0],module.parse_directory(RAW));self.assertFalse((Path(folder)/'refresh.lock').exists());self.assertFalse(list(Path(folder).glob('*.partial')))

    def test_corrupt_state_recovers_instead_of_permanent_failure(self):
        for state in [[],{'attemptUTC':'bad'},{'fetchedUTC':float('nan')},{'attemptUTC':999999999999}, {'sha256':3}]:
            with tempfile.TemporaryDirectory() as folder:
                (Path(folder)/'state.json').write_text(json.dumps(state))
                self.assertEqual(module.Directory(folder,lambda *args:RAW,lambda:1000000).candidates_after_failure(),module.parse_directory(RAW))
    def test_nonobject_response_tries_next_candidate(self):
        second='https://second.example.org/api/interpreter';obj=json.loads(future_fixture());obj['parse']['wikitext']=obj['parse']['wikitext'].replace('|Future public instance', '|Second public instance\n|'+second+'\n|0.7.62\n|yes\n|small\n|Free public global use.\n|-\n|Future public instance')
        def fetch(url,*args,**kwargs):return json.dumps(obj).encode() if url==module.DIRECTORY else b'[]' if url==second else b'{"elements":[{}]}'
        with tempfile.TemporaryDirectory() as folder:self.assertEqual(module.recover(folder,'query',module.parse_directory(RAW),fetch)[1],FUTURE)

class RecoveryConcurrencyTests(unittest.TestCase):
    def test_only_one_concurrent_discovered_query(self):
        import threading
        entered=threading.Event();release=threading.Event();queries=[]
        def fetch(url,*args,**kwargs):
            if url==module.DIRECTORY:return future_fixture()
            queries.append(url);entered.set();release.wait(1);return b'{"elements":[{}]}'
        with tempfile.TemporaryDirectory() as folder:
            results=[];attempted=module.parse_directory(RAW);thread=threading.Thread(target=lambda:results.append(module.recover(folder,'query',attempted,fetch)));thread.start();self.assertTrue(entered.wait(1))
            self.assertIsNone(module.recover(folder,'query',attempted,fetch));release.set();thread.join(1)
            self.assertEqual(queries,[FUTURE]);self.assertEqual(results[0][1],FUTURE);self.assertFalse((Path(folder)/'query.lock').exists())
    def test_failed_endpoint_has_persisted_short_backoff(self):
        queries=[]
        def fetch(url,*args,**kwargs):
            if url==module.DIRECTORY:return future_fixture()
            queries.append(url);raise TimeoutError()
        with tempfile.TemporaryDirectory() as folder:
            attempted=module.parse_directory(RAW);self.assertIsNone(module.recover(folder,'query',attempted,fetch));self.assertIsNone(module.recover(folder,'query',attempted,fetch));self.assertEqual(queries,[FUTURE])



class FairnessTests(unittest.TestCase):
    def test_expired_failures_do_not_starve_previously_untried_future_third(self):
        calls=[];data=future_fixture();attempted=['https://overpass.private.coffee/api/interpreter']
        def fetch(url,maximum,seconds,body=None):
            if url==module.DIRECTORY:return data
            calls.append(url)
            if url!=FUTURE:raise BrokenPipeError('simulated rapid gateway close')
            return b'{"elements":[{}]}'
        with tempfile.TemporaryDirectory() as folder:
            self.assertIsNone(module.recover(folder,'query',attempted,fetch));self.assertEqual(len(calls),2)
            failures=Path(folder)/'query-failures.json';state=json.loads(failures.read_bytes());failures.write_text(json.dumps({url:stamp-31 for url,stamp in state.items()}));calls.clear()
            result=module.recover(folder,'query',attempted,fetch)
            self.assertEqual(result[1],FUTURE);self.assertEqual(calls,[FUTURE])
    def test_candidate_query_budget_stays_at_two(self):
        calls=[]
        def fetch(url,*args,**kwargs):
            if url==module.DIRECTORY:return future_fixture()
            calls.append(url);raise BrokenPipeError()
        with tempfile.TemporaryDirectory() as folder:
            self.assertIsNone(module.recover(folder,'query',[],fetch));self.assertEqual(len(calls),2)


class CliBounds(unittest.TestCase):
    def test_valid_high_latitude_ui_area_is_not_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);request=root/'request.json';box=[85.0,0.0,85.01,0.01]
            request.write_text(json.dumps(dict(query='[out:json];way(85.0,0.0,85.01,0.01);out;',attempted=[],bounds=box,cache=str(root/'cache'))))
            with patch('sys.argv',['worker','--request',str(request)]),patch.object(module,'recover',return_value=(b'{"elements":[{"type":"way","id":1}]}',FUTURE)):
                module.main()
            self.assertEqual(json.loads((root/'map.json').read_bytes())['elements'][0]['id'],1)
