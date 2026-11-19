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

if __name__=='__main__':unittest.main()
