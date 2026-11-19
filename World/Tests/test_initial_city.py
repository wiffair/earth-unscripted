import base64,json,runpy,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import public_overpass as module
import map_transport

def encoded(s):return base64.b64encode(s.encode()).decode()
class InitialCity(unittest.TestCase):
 def job(self,root):
  request=root/'request.json';bounds=[-33.88,151.20,-33.86,151.22];box=','.join(str(v) for v in bounds)
  request.write_text(json.dumps(dict(query=f'[out:json];way[highway][access!~"^(no|private)$"][motor_vehicle!=no]({box});out body geom;',attempted=[],bounds=bounds,cache=str(root/'PublicOverpassDirectory'),worldCache=str(root/'AutomaticWorldCache'))));return request
 def regional(self,path,bounds,query,event,city_hint=''):
  if city_hint=='Unknown city label':raise ValueError('No catalogue city matches source place name')
  path.write_text(json.dumps(dict(elements=[dict(type='way',id=1,tags=dict(highway='residential')),dict(type='way',id=2,tags=dict(highway='residential',access='private')),dict(type='way',id=3,tags=dict(highway='residential',motor_vehicle='no'))])))
  path.with_suffix('.json.receipt.json').write_text(json.dumps(dict(url='https://download.bbbike.org/osm/bbbike/Sydney/Sydney.osm.pbf')))
 def test_city_encoding_handles_unicode_and_shell_metacharacters(self):
  for s in ['Sydney','München · centre','Name " --request ../bad & $HOME']:
   self.assertEqual(module.decode_city_hint(encoded(s)),s)
  for s in ['../../','x'*1401,encoded('\x00bad'),encoded('x'*257)]:
   with self.assertRaises((ValueError,UnicodeError)):module.decode_city_hint(s)
 def test_actual_worker_early_cli_enters_city_fallback_and_preserves_access(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);request=self.job(root);entry=Path(map_transport.__file__).with_name('worker_entry.py')
   with patch('sys.argv',[str(entry),'--public-map-fallback','--request',str(request),'--city',encoded('Sydney')]),patch.object(module,'recover',return_value=None),patch.object(map_transport,'regional',side_effect=self.regional) as regional:
    with self.assertRaises(SystemExit) as e:runpy.run_path(str(entry),run_name='__main__')
    self.assertEqual(e.exception.code,0);self.assertEqual(regional.call_args.kwargs['city_hint'],'Sydney');self.assertEqual(regional.call_args.args[0].parent,root/'AutomaticWorldCache/Raw');self.assertTrue(regional.call_args.args[0].name.startswith('ui-map-'))
   self.assertEqual([r['id'] for r in json.loads((root/'map.json').read_bytes())['elements']],[1]);self.assertTrue((root/'AutomaticWorldCache/prepare.lock').exists());self.assertTrue((root/'AutomaticWorldCache/Raw').exists())
 def test_directory_exception_allows_independent_city_source(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);request=self.job(root)
   with patch('sys.argv',['worker','--request',str(request),'--city',encoded('Sydney')]),patch.object(module,'recover',side_effect=RuntimeError('directory unavailable')),patch.object(map_transport,'regional',side_effect=self.regional):module.main()
   self.assertTrue((root/'map.json').exists())
 def test_directory_exception_preserves_old_caller_failure(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);request=self.job(root)
   with patch('sys.argv',['worker','--request',str(request)]),patch.object(module,'recover',side_effect=RuntimeError('directory unavailable')):
    with self.assertRaisesRegex(RuntimeError,'directory unavailable'):module.main()
 def test_old_cli_without_city_does_not_import_or_call_regional_fallback(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);request=self.job(root)
   with patch('sys.argv',['worker','--request',str(request)]),patch.object(module,'recover',return_value=(b'{"elements":[{}]}','https://overpass-api.de/api/interpreter')),patch.object(module,'regional_city_recovery',side_effect=AssertionError('old caller entered city fallback')):
    module.main()
   self.assertTrue((root/'map.json').exists())
 def test_unknown_hint_fails_honestly_without_output(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);request=self.job(root)
   with patch('sys.argv',['worker','--request',str(request),'--city',encoded('Unknown city label')]),patch.object(module,'recover',return_value=None),patch.object(map_transport,'regional',side_effect=self.regional):
    with self.assertRaisesRegex(ValueError,'No catalogue'):module.main()
   self.assertFalse((root/'map.json').exists())
 def test_output_cap_and_fixed_cache_boundary_retained(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);request=self.job(root)
   def too_large(path,*a,**kw):path.write_bytes(b'x'*16000001)
   with patch.object(map_transport,'regional',side_effect=too_large),self.assertRaisesRegex(ValueError,'byte cap'):module.regional_city_recovery(json.loads(request.read_bytes()),request,'Sydney')
   job=json.loads(request.read_bytes());job['worldCache']=str(root/'Unrelated')
   with self.assertRaisesRegex(ValueError,'Owned world cache'):module.regional_city_recovery(job,request,'Sydney')
if __name__=='__main__':unittest.main()
