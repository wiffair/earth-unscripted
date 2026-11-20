"""Malformed scenery must not block independent road/rail source admission."""
import hashlib,json,tempfile,time,unittest,urllib.parse
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock
import map_transport
from osm_areas import expanded

def member(identifier,role,points):return dict(type='way',ref=identifier,role=role,geometry=[dict(lon=x,lat=y) for x,y in points])
def relation(members):return dict(type='relation',id=5582341,tags=dict(building='university',type='multipolygon'),members=members)
RING=[(0,0),(1,0),(1,1),(0,1),(0,0)]
ROAD=dict(type='way',id=5,tags=dict(highway='motorway'),geometry=[dict(lon=0,lat=0),dict(lon=1,lat=1)])
RAIL=dict(type='way',id=6,tags=dict(railway='rail'),geometry=[dict(lon=0,lat=1),dict(lon=1,lat=0)])
class OptionalAreas(unittest.TestCase):
 def test_courtyard_without_outer_boundary_omitted_transport_unchanged(self):
  broken=relation([member(375143589,'inner',RING)]);data=dict(elements=[ROAD,broken,RAIL]);kept,bad=map_transport._safe_area_relations(data)
  self.assertEqual(bad,[5582341]);self.assertEqual(kept['elements'],[ROAD,RAIL]);self.assertIs(kept['elements'][0],ROAD)
 def test_unclosed_outer_and_incomplete_optional_members_do_not_block_roads(self):
  for members in [[member(1,'outer',[(0,0),(1,0)])],[dict(type='way',ref=2,role='outer',geometry=[None,None])]]:
   with self.subTest(members=members):
    kept,bad=map_transport._safe_area_relations(dict(elements=[ROAD,relation(members),RAIL]));self.assertEqual(bad,[5582341]);self.assertEqual(kept['elements'],[ROAD,RAIL])
 def test_complete_building_with_courtyard_is_retained(self):
  valid=relation([member(1,'outer',[(0,0),(4,0),(4,4),(0,4),(0,0)]),member(2,'inner',[(1,1),(2,1),(2,2),(1,2),(1,1)])])
  data=dict(elements=[ROAD,valid,RAIL]);kept,bad=map_transport._safe_area_relations(data);self.assertEqual(kept,data);self.assertFalse(bad)
  polygon=next(e for e in expanded(kept['elements']) if str(e.get('id','')).startswith('relation-'));self.assertEqual(len(polygon['_holes']),1)
 def test_old_regional_and_discovered_cached_responses_are_sanitized_without_network(self):
  query='[out:json];(way[highway](0,0,1,1);way[railway](0,0,1,1););out body geom;'
  for public in [False,True]:
   with self.subTest(public=public),tempfile.TemporaryDirectory() as folder:
    path=Path(folder)/'area.json';path.write_text(json.dumps(dict(elements=[ROAD,RAIL,relation([member(1,'inner',RING)])])));before=hashlib.sha256(path.read_bytes()).hexdigest()
    receipt=dict(url='https://download.geofabrik.de/test.osm.pbf',sha256=before,fetchedUTC=time.time(),railwayFurnitureSchema=1)
    if public:receipt.update(publicDirectory=True,querySha256=hashlib.sha256(query.encode()).hexdigest())
    else:receipt['regional']=True
    metadata=path.with_suffix('.json.receipt.json');metadata.write_text(json.dumps(receipt));original=Mock(side_effect=AssertionError('No redownload'))
    pipeline=SimpleNamespace(fetch=original,OSM_MAX_AGE_SECONDS=30*86400,sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest(),incomplete_osm_geometry=lambda *a:[])
    map_transport.install(pipeline);events=[];result=pipeline.fetch(path,'https://overpass.atownsend.org.uk/api/interpreter',lambda kind,**fields:events.append(kind),body=urllib.parse.urlencode(dict(data=query)).encode())
    original.assert_not_called();self.assertEqual(json.loads(path.read_text())['elements'],[ROAD,RAIL]);after=json.loads(metadata.read_text());self.assertEqual(after['downloadSha256'],before);self.assertEqual(after['sha256'],result['sha256']);self.assertEqual(after['excludedMalformedAreaRelations'],[5582341]);self.assertIn('malformed-optional-area-omitted',events)

if __name__=='__main__':unittest.main()
