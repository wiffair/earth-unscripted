import unittest,json,tempfile
from pathlib import Path
from unittest.mock import patch
import map_transport as transport
import city_extract
from regional_parts import select_parts,merge_elements
from shapely.geometry import mapping,box

def feature(name,shape,city=None):
 props=dict(id=name,urls=dict(pbf='https://download.geofabrik.de/'+name+'.osm.pbf'))
 if city:props['city']=city
 return dict(type='Feature',properties=props,geometry=mapping(shape))
class CompositeCoverage(unittest.TestCase):
 def test_exact_partial_country_and_city_cover_cross_border_request(self):
  index=dict(features=[feature('whole',box(-10,-10,10,10)),feature('country',box(-2,-2,0,2)),feature('city',box(-.1,-1,2,1),'City')])
  result=select_parts(index,(-.5,-1,.5,1));self.assertEqual({r['id'] for r in result},{'country','city'})
 def test_uncovered_strip_is_not_approximated_or_filled(self):
  index=dict(features=[feature('left',box(-2,-2,-.01,2)),feature('right',box(.01,-2,2,2))])
  with self.assertRaisesRegex(ValueError,'complete'):select_parts(index,(-.5,-1,.5,1))
 def test_complete_city_is_allowed_without_a_large_parent(self):
  index=dict(features=[feature('whole',box(-10,-10,10,10)),feature('small-city',box(-2,-2,2,2),'City')])
  self.assertEqual([r['id'] for r in select_parts(index,(-.5,-1,.5,1))],['small-city'])
 def test_shared_osm_ids_are_deduplicated_without_geometry_changes(self):
  road=dict(type='way',id=4,nodes=[10,20],geometry=[dict(lon=0,lat=0),dict(lon=1,lat=1)],tags=dict(highway='motorway'))
  result=merge_elements([dict(elements=[road]),dict(elements=[dict(road),dict(type='node',id=4,lat=0,lon=0)])]);self.assertEqual(len(result['elements']),2);self.assertEqual(next(r for r in result['elements'] if r['type']=='way'),road)
 def test_conflicting_cross_border_revisions_are_not_spliced(self):
  a=dict(type='way',id=4,nodes=[10,20],geometry=[dict(lon=0,lat=0),dict(lon=1,lat=1)])
  b=dict(a,nodes=[10,30])
  with self.assertRaisesRegex(ValueError,'Conflicting'):merge_elements([dict(elements=[a]),dict(elements=[b])])
 def test_unknown_object_type_is_rejected(self):
  with self.assertRaisesRegex(ValueError,'identity'):merge_elements([dict(elements=[dict(type='invented',id=3)])])

 def test_combined_download_cap_applies_before_indexing_any_parts(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);limits=[]
   def download(path,url,limit):
    if url==transport.INDEX:path.write_text('{"features":[]}');return {}
    limits.append(limit)
    with path.open('wb') as f:f.truncate(200*1024*1024)
    return dict(url=url,sha256='a'*64,fetchedUTC=1)
   regions=[dict(id=n,urls=dict(pbf='https://download.geofabrik.de/'+n+'.osm.pbf')) for n in ['a','b']]
   transport._regional_failure.clear()
   with patch.object(transport,'_download',side_effect=download),patch.object(city_extract,'fetch_city',side_effect=ValueError('No city')),patch.object(city_extract,'partial_city_regions',return_value=[]),patch('regional_parts.select_parts',return_value=regions),patch.object(transport,'indexed_extract',side_effect=AssertionError('Oversized combined source must not be indexed')):
    with self.assertRaisesRegex(ValueError,'Combined'):transport.regional(root/'out.json',(0,0,1,1),'out body geom',lambda *a,**k:None)
   self.assertEqual(limits,[transport.CAP,transport.CAP-200*1024*1024]);self.assertFalse((root/'out.json').exists())

class CachedCompositeRecovery(unittest.TestCase):
 def fixture(self,folder,stale=False):
  import hashlib,time
  root=Path(folder);bounds=(-.5,-1.,.5,1.)
  left=feature('left',box(-2,-2,0,2));city=feature('bbbike-City',box(-.1,-1,2,1),'City');city['properties']['urls']['pbf']='https://download.bbbike.org/osm/bbbike/City/City.osm.pbf'
  def store(path,data,url,age=0):
   path.write_bytes(data);path.with_suffix(path.suffix+'.receipt.json').write_text(json.dumps(dict(url=url,sha256=hashlib.sha256(data).hexdigest(),fetchedUTC=time.time()-age,bytes=len(data))))
  store(root/'regional-index.geojson',json.dumps(dict(features=[left])).encode(),transport.INDEX)
  store(root/'regional-left.osm.pbf',b'country fixture',left['properties']['urls']['pbf'])
  store(root/'regional-bbbike-City.osm.pbf',b'city fixture',city['properties']['urls']['pbf'],8*86400 if stale else 0)
  return root,bounds,city
 def test_verified_cached_union_bypasses_network_cooldown_without_network(self):
  import time
  with tempfile.TemporaryDirectory() as folder:
   root,bounds,city=self.fixture(folder);transport._regional_failure[(str(root),bounds)]=time.monotonic()+180
   road=dict(type='way',id=2,nodes=[1,2],geometry=[dict(lon=-1,lat=0),dict(lon=1,lat=0)],tags=dict(highway='motorway'))
   with patch.object(city_extract,'partial_city_regions',return_value=[city]) as metadata,patch.object(transport,'_download',side_effect=AssertionError('No network')),patch.object(city_extract,'fetch_city',side_effect=AssertionError('No city fetch')),patch.object(transport,'indexed_extract',return_value=dict(elements=[road])):
    result=transport.regional(root/'out.json',bounds,'out body geom',lambda *a,**k:None)
   self.assertEqual(json.loads((root/'out.json').read_text())['elements'],[road]);self.assertTrue(result['regional']);metadata.assert_called_once_with(root,bounds,unittest.mock.ANY,cached_only=True)
 def test_stale_cached_part_cannot_bypass_failure_cooldown(self):
  import time
  with tempfile.TemporaryDirectory() as folder:
   root,bounds,city=self.fixture(folder,stale=True);transport._regional_failure[(str(root),bounds)]=time.monotonic()+180
   with patch.object(city_extract,'partial_city_regions',return_value=[city]),patch.object(transport,'_download',side_effect=AssertionError('No retry during cooldown')):
    with self.assertRaisesRegex(RuntimeError,'cooling down'):transport.regional(root/'out.json',bounds,'out body geom',lambda *a,**k:None)
 def test_cache_only_city_metadata_never_downloads_when_missing(self):
  with tempfile.TemporaryDirectory() as folder,patch.object(city_extract,'download',side_effect=AssertionError('Offline lookup must not download')):
   with self.assertRaisesRegex(ValueError,'metadata cache'):city_extract.partial_city_regions(Path(folder),(-.5,-1.,.5,1.),cached_only=True)

class RegionalFailureBackoff(unittest.TestCase):
 def test_socket_timeout_retries_soon_but_invalid_data_keeps_long_backoff(self):
  import time
  for error,seconds in [(TimeoutError('socket read timed out'),15),(ValueError('invalid source'),180)]:
   with self.subTest(error=type(error).__name__),tempfile.TemporaryDirectory() as folder:
    root=Path(folder);bounds=(0.,0.,1.,1.);source=dict(url='https://download.bbbike.org/osm/bbbike/City/City.osm.pbf',sha256='a'*64,fetchedUTC=time.time())
    with patch.object(transport,'cached_composite',return_value=None),patch.object(city_extract,'fetch_city',return_value=(root/'source.pbf',source)),patch.object(transport,'indexed_extract',side_effect=error):
     with self.assertRaises(type(error)):transport.regional(root/'out.json',bounds,'out body geom',lambda *a,**k:None)
    delay=transport._regional_failure[(str(root),bounds)]-time.monotonic();self.assertGreater(delay,seconds-2);self.assertLessEqual(delay,seconds)
    if isinstance(error,ValueError):
     with patch.object(transport,'cached_composite',return_value=([root/'source.pbf'],source)),patch.object(transport,'indexed_extract',side_effect=AssertionError('Invalid unchanged cache must respect backoff')):
      with self.assertRaisesRegex(RuntimeError,'cooling down'):transport.regional(root/'out.json',bounds,'out body geom',lambda *a,**k:None)

if __name__=='__main__':unittest.main()
