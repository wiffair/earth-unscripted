import hashlib,http.client,importlib.util,json,socket,ssl,tempfile,threading,time,types,unittest
from pathlib import Path
from unittest.mock import patch
from urllib.parse import urlencode
import city_extract as m
from shapely.geometry import box

POLY=b'Sydney\n1\n150.83 -34.06\n151.33 -34.06\n151.33 -33.68\n150.83 -33.68\nEND\nEND\n'
INDEX=b'<a href="Sydney/">Sydney</a><a href="Amsterdam/">Amsterdam</a><a href="https://evil.test/X/">X</a><a href="../">up</a>'
class Admission(unittest.TestCase):
 def test_only_official_fixed_catalogue_and_paths(self):
  self.assertEqual(m.names(INDEX),['Amsterdam','Sydney']);self.assertEqual(m.candidates(m.names(INDEX),'Sydney · real roads'),['Sydney'])
  self.assertEqual(m.candidates(m.names(INDEX),'Your saved location'),[])
  for url in ['http://download.bbbike.org/osm/bbbike/','https://download.bbbike.org:444/osm/bbbike/','https://user@download.bbbike.org/osm/bbbike/','https://download.bbbike.org.evil.test/osm/bbbike/','https://127.0.0.1/osm/bbbike/','https://download.bbbike.org/osm/bbbike/../Sydney.poly','https://download.bbbike.org/osm/bbbike/Sydney/Other.poly','https://download.bbbike.org/osm/bbbike/Sydney/Sydney.poly?token=x','https://download.bbbike.org/osm/bbbike/Sydney/Sydney.poly#x']:
   with self.subTest(url=url),self.assertRaises(ValueError):m.admitted_url(url)
 def test_exact_polygon_bounds_holes_and_invalid_numbers(self):
  self.assertTrue(m.parse_poly(POLY).covers(box(151.2,-33.88,151.22,-33.86)))
  hole=POLY[:-4]+b'!2\n151.20 -33.88\n151.22 -33.88\n151.22 -33.86\n151.20 -33.86\nEND\nEND\n'
  self.assertFalse(m.parse_poly(hole).covers(box(151.205,-33.875,151.215,-33.865)))
  for raw in [b'',POLY.replace(b'150.83',b'nan'),POLY.replace(b'-34.06',b'-94.06'),POLY[:-4],POLY+b'garbage',b'x'*1048577]:
   with self.subTest(raw=raw[:30]),self.assertRaises(ValueError):m.parse_poly(raw)
 def fake(self,calls):
  def fetch(path,url,limit,seconds):
   calls.append((url,limit,seconds));data=INDEX if url==m.BASE else POLY if url.endswith('.poly') else b'valid-pbf-fixture'
   path.write_bytes(data);return dict(url=url,bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),fetchedUTC=time.time())
  return fetch
 def test_whole_request_contained_before_pbf_no_coordinate_special_case(self):
  with tempfile.TemporaryDirectory() as folder:
   calls=[];path,receipt=m.fetch_city(Path(folder),(-33.88,151.2,-33.86,151.22),'Sydney',fetch=self.fake(calls))
   self.assertEqual(path.read_bytes(),b'valid-pbf-fixture');self.assertEqual(calls[-1],(m.BASE+'Sydney/Sydney.osm.pbf',m.CAP,120));self.assertIn('coverageSHA256',receipt)
   calls.clear()
   with self.assertRaisesRegex(ValueError,'complete'):m.fetch_city(Path(folder),(-34.1,151.2,-33.86,151.22),'Sydney',fetch=self.fake(calls))
   self.assertFalse(any(u.endswith('.pbf') for u,_,_ in calls))
 def test_verified_city_cache_reused_without_regional_or_city_download(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder)
   for path,url,data in [(root/'bbbike-index.html',m.BASE,INDEX),(root/'bbbike-Sydney.poly',m.BASE+'Sydney/Sydney.poly',POLY),(root/'regional-bbbike-Sydney.osm.pbf',m.BASE+'Sydney/Sydney.osm.pbf',b'pbf')]:
    path.write_bytes(data);path.with_suffix(path.suffix+'.receipt.json').write_text(json.dumps(dict(url=url,bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),fetchedUTC=time.time())))
   calls=[]
   def forbidden(*args):calls.append(args);raise AssertionError('cached city opened a network source')
   path,receipt=m.fetch_city(root,(-33.88,151.2,-33.86,151.22),'Sydney',fetch=forbidden,cached_only=True)
   self.assertEqual(path.read_bytes(),b'pbf');self.assertEqual(calls,[])
 def test_future_directory_city_admitted_generically(self):
  with tempfile.TemporaryDirectory() as folder:
   def future(path,url,limit,seconds):
    data=b'<a href="FutureCity/">future</a>' if url==m.BASE else POLY if url.endswith('.poly') else b'pbf';path.write_bytes(data);return dict(url=url,bytes=len(data),sha256=hashlib.sha256(data).hexdigest(),fetchedUTC=time.time())
   path,_=m.fetch_city(Path(folder),(-33.88,151.2,-33.86,151.22),'Future City · real roads',fetch=future);self.assertIn('FutureCity',path.name)

class Transport(unittest.TestCase):
 def fake_network(self,data=b'citydata',status=200,headers=None,ssl_error=False):
  calls=dict(connected=[],sni=[],headers=[])
  class Sock:
   def settimeout(self,*args):pass
   def connect(self,address):calls['connected'].append(address)
   def close(self):calls['closed']=True
   def shutdown(self,*args):pass
  class Context:
   def wrap_socket(self,sock,server_hostname):
    calls['sni'].append(server_hostname)
    if ssl_error:raise ssl.SSLCertVerificationError('bad certificate')
    return sock
  class Response:
   def getheader(self,name,default=None):return (headers or {}).get(name,default)
   def read1(self,size):
    if getattr(self,'done',False):return b''
    self.done=True;return data
  Response.status=status
  class Connection:
   def __init__(self,*args,**kwargs):self.sock=None
   def request(self,*args,**kwargs):calls['headers'].append(kwargs['headers'])
   def getresponse(self):return Response()
   def close(self):calls['closed']=True
  return calls,Context,Connection,Sock
 def run_fake(self,data=b'citydata',status=200,headers=None,limit=100,ssl_error=False):
  calls,Context,Connection,Sock=self.fake_network(data,status,headers,ssl_error)
  with tempfile.TemporaryDirectory() as folder,patch.object(m,'_resolve',return_value=[(socket.AF_INET,('8.8.8.8',443))]),patch.object(m.socket,'socket',side_effect=lambda *a:Sock()),patch.object(m.ssl,'create_default_context',return_value=Context()),patch.object(m.http.client,'HTTPSConnection',Connection):
   path=Path(folder)/'city.poly';row=m.download(path,m.BASE+'Sydney/Sydney.poly',limit,1)
   self.assertEqual(path.read_bytes(),data);self.assertFalse(list(Path(folder).glob('*.partial')))
   # Exact verified receipt reuses the local file, without a DNS request.
   with patch.object(m,'_resolve',side_effect=AssertionError('cache opened network')):self.assertEqual(m.download(path,m.BASE+'Sydney/Sydney.poly',limit,1),row)
  return calls
 def test_pinned_dns_original_tls_sni_and_receipt_cache(self):
  calls=self.run_fake();self.assertEqual(calls['connected'],[('8.8.8.8',443)]);self.assertEqual(calls['sni'],[m.HOST]);self.assertEqual(calls['headers'][0]['Host'],m.HOST)
 def test_redirect_oversize_truncation_and_bad_certificate_fail_closed(self):
  for args in [dict(status=302),dict(headers={'Content-Length':'101'}),dict(data=b'x'*101),dict(headers={'Content-Length':'30'}),dict(ssl_error=True)]:
   with self.subTest(args=args),self.assertRaises((ValueError,OSError)):self.run_fake(**args)
 def test_private_dns_rejected_by_existing_validated_resolver(self):
  import public_overpass
  with patch.object(public_overpass.socket,'getaddrinfo',return_value=[(socket.AF_INET,socket.SOCK_STREAM,6,'',('127.0.0.1',443))]):
   with self.assertRaises(ValueError):m._resolve(m.HOST,time.monotonic()+1)
 def real_socket(self,serve,limit=100):
  client,server=socket.socketpair();closed=threading.Event()
  def handle():
   try:
    server.recv(8192);serve(server,closed)
   finally:server.close()
  thread=threading.Thread(target=handle);thread.start()
  class Adapter:
   def connect(self,address):pass
   def __getattr__(self,name):return getattr(client,name)
  class Context:
   def wrap_socket(self,sock,server_hostname):return sock
  try:
   with tempfile.TemporaryDirectory() as folder,patch.object(m,'_resolve',return_value=[(socket.AF_INET,('8.8.8.8',443))]),patch.object(m.socket,'socket',side_effect=lambda *a:Adapter()),patch.object(m.ssl,'create_default_context',return_value=Context()):
    start=time.monotonic()
    try:m.download(Path(folder)/'city.poly',m.BASE+'Sydney/Sydney.poly',limit,.15)
    finally:self.assertLess(time.monotonic()-start,.8);self.assertFalse(list(Path(folder).glob('*.partial')))
  finally:client.close();thread.join(1)
  return closed.is_set()
 def test_http10_and_connection_close_retain_watchdog_socket(self):
  for version in [b'HTTP/1.0',b'HTTP/1.1']:
   reply=version+b' 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\n{}'
   self.real_socket(lambda s,c:s.sendall(reply))
 def test_slow_header_and_body_deadline_closes_real_socket(self):
  for reply in [b'HTTP/1.1 200 OK\r\nX-Slow: ',b'HTTP/1.1 200 OK\r\nContent-Length: 50\r\n\r\n{}']:
   observed=[]
   def serve(s,closed):
    s.sendall(reply);s.settimeout(.8)
    if s.recv(1)==b'':closed.set();observed.append(True)
   with self.assertRaises((OSError,ValueError,http.client.HTTPException)):self.real_socket(serve)
   self.assertEqual(observed,[True])
 def test_large_headers_and_chunked_body_rejected(self):
  for reply in [b'HTTP/1.1 200 OK\r\nX-Large: '+b'x'*5000+b'\r\n\r\n',b'HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n65\r\n'+b'x'*101+b'\r\n0\r\n\r\n']:
   with self.assertRaises((OSError,ValueError,http.client.HTTPException)):self.real_socket(lambda s,c:s.sendall(reply))

class Integration(unittest.TestCase):
 def test_actual_failed_provider_path_uses_city_after_regional_cap(self):
  spec=importlib.util.spec_from_file_location('candidate_map_transport',(Path(__file__).with_name('map_transport.py') if Path(__file__).with_name('map_transport.py').exists() else Path(__file__).parents[1]/'Source/map_transport.py'));transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)
  def failed(*a,**kw):raise TimeoutError('primary outage')
  pipeline=types.SimpleNamespace(fetch=failed,OSM_MAX_AGE_SECONDS=86400,sha=m.digest,incomplete_osm_geometry=lambda *a:False);transport.install(pipeline)
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);pbf=root/'regional-bbbike-Sydney.osm.pbf';pbf.write_bytes(b'pbf')
   receipt=dict(url=m.BASE+'Sydney/Sydney.osm.pbf',sha256=m.digest(pbf),bytes=3,fetchedUTC=time.time())
   with patch.object(transport,'_download',side_effect=ValueError('Regional extract exceeds cap')),patch.object(transport,'public_recovery',side_effect=TimeoutError()),patch.object(m,'fetch_city',side_effect=[ValueError('No fresh cached city'),(pbf,dict(receipt,city='Sydney'))]) as city,patch.object(transport,'indexed_extract',return_value=dict(elements=[dict(type='way',id=1,tags=dict(highway='residential'))])):
    query='[out:json];way[highway](-33.88,151.2,-33.86,151.22);way[railway](-33.88,151.2,-33.86,151.22);out body geom;'
    result=pipeline.fetch(root/'map.json','https://overpass-api.de/api/interpreter',lambda *a,**k:None,body=urlencode({'data':query}).encode(),city_hint='Sydney · real roads')
    self.assertIn('BBBike',json.loads((root/'map.json').read_bytes())['generator']);self.assertTrue(result['regional']);self.assertEqual(city.call_args.args[2],'Sydney · real roads');self.assertEqual(json.loads((root/'map.json').read_bytes())['elements'][0]['id'],1)
class GeographicFallback(unittest.TestCase):
 def test_custom_route_and_empty_hint_use_geographic_coverage(self):
  for hint in ['Magor to Bristol benchmark','Your saved location','']:
   with self.subTest(hint=hint),tempfile.TemporaryDirectory() as folder,patch.object(m,'CITY_COVERAGE',{'Sydney':[150.83,-34.06,151.33,-33.68]}):
    calls=[];path,_=m.fetch_city(Path(folder),(-33.88,151.2,-33.86,151.22),hint,fetch=Admission().fake(calls))
    self.assertIn('Sydney',path.name);self.assertEqual(calls[-1][0],m.BASE+'Sydney/Sydney.osm.pbf')
 def test_snapshot_does_not_override_live_catalogue_or_exact_polygon(self):
  with patch.object(m,'CITY_COVERAGE',{'Absent':[150,-35,153,-33],'Sydney':[150,-35,153,-33]}):
   self.assertEqual(m.geographic_candidates(['Sydney'],(-33.88,151.2,-33.86,151.22)),['Sydney'])
   with tempfile.TemporaryDirectory() as folder:
    calls=[]
    with self.assertRaisesRegex(ValueError,'complete'):m.fetch_city(Path(folder),(-34.1,151.2,-33.86,151.22),'unmatched',fetch=Admission().fake(calls))
    self.assertFalse(any(url.endswith('.pbf') for url,_,_ in calls))
 def test_malformed_or_remote_boxes_do_not_create_candidates(self):
  with patch.object(m,'CITY_COVERAGE',{'A':[0,0,float('nan'),2],'B':'bad','C':[-2,1,-3,4],'D':[10,10,11,11]}):
   self.assertEqual(m.geographic_candidates(['A','B','C','D'],(-33.88,151.2,-33.86,151.22)),[])
 def test_no_hint_regional_fallback_and_per_area_cooldown(self):
  spec=importlib.util.spec_from_file_location('geographic_map_transport',Path(__file__).parents[1]/'Source/map_transport.py');transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);(root/'regional-index.geojson').write_text('{"features":[]}');pbf=root/'fixture.pbf';pbf.write_bytes(b'pbf');bounds=(-33.88,151.2,-33.86,151.22);other=(-33.8,151.2,-33.78,151.22)
   with patch.object(transport,'_download',side_effect=ValueError('regional cap')),patch.object(m,'fetch_city',side_effect=ValueError('no city')),patch.object(m,'partial_city_regions',return_value=[]):
    with self.assertRaisesRegex(ValueError,'complete'):transport.regional(root/'map.json',bounds,'out body geom',lambda *a,**k:None)
   receipt=dict(url=m.BASE+'Sydney/Sydney.osm.pbf',sha256=m.digest(pbf),bytes=3,fetchedUTC=time.time(),city='Sydney')
   with patch.object(m,'fetch_city',return_value=(pbf,receipt)) as city,patch.object(transport,'indexed_extract',return_value=dict(elements=[dict(type='way',id=1,tags=dict(highway='residential'))])):
    row=transport.regional(root/'other.json',other,'out body geom',lambda *a,**k:None)
    self.assertTrue(row['regional']);self.assertEqual(city.call_args.args[2],'')
    with self.assertRaisesRegex(RuntimeError,'cooling down'):transport.regional(root/'map.json',bounds,'out body geom',lambda *a,**k:None)

if __name__=='__main__':unittest.main()
