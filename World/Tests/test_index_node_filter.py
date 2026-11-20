"""Native node filtering preserves referenced coordinates and indexed output."""
import hashlib,json,shutil,tempfile,time,unittest
from pathlib import Path
from unittest.mock import patch
import osmium
import map_transport

class IndexNodeFilter(unittest.TestCase):
 def source(self,root):
  path=root/'fixture.osm.pbf'
  rows=[(1,(0.,0.),{}),(2,(.01,0.),{}),(3,(.005,0.),{'highway':'traffic_signals'}),(4,(.002,.005),{'place':'hamlet','name':'Test'}),(5,(.008,.006),{'railway':'buffer_stop'}),(6,(.005,.003),{'ele':'123'})]
  with osmium.SimpleWriter(str(path)) as writer:
   for identity,location,tags in rows:writer.add_node(osmium.osm.mutable.Node(id=identity,location=location,tags=tags))
   writer.add_way(osmium.osm.mutable.Way(id=10,nodes=[1,2],tags={'highway':'motorway'}))
   writer.add_way(osmium.osm.mutable.Way(id=12,nodes=[2,6],tags={'railway':'rail'}))
   writer.add_way(osmium.osm.mutable.Way(id=11,nodes=[1,3,4,1],tags={}))
   writer.add_relation(osmium.osm.mutable.Relation(id=20,members=[('w',11,'outer')],tags={'building':'yes','type':'multipolygon'}))
  path.with_suffix('.pbf.receipt.json').write_text(json.dumps(dict(url='https://download.geofabrik.de/fixture.osm.pbf',sha256=hashlib.sha256(path.read_bytes()).hexdigest(),fetchedUTC=time.time(),bytes=path.stat().st_size)))
  return path
 def test_untagged_referenced_nodes_remain_available_to_all_ways(self):
  class Capture(osmium.SimpleHandler):
   def __init__(self):super().__init__();self.nodes=[];self.ways=[];self.relations=[]
   def node(self,n):self.nodes.append(n.id)
   def way(self,w):self.ways.append((w.id,[(n.ref,n.lon,n.lat,n.location.valid()) for n in w.nodes]))
   def relation(self,r):self.relations.append(r.id)
  with tempfile.TemporaryDirectory() as folder:
   source=self.source(Path(folder));old=Capture();old.apply_file(str(source),locations=True,idx='sparse_mem_array');new=Capture();new.apply_file(str(source),locations=True,idx='sparse_mem_array',filters=[map_transport.index_node_filter(osmium)])
   self.assertEqual(old.nodes,[1,2,3,4,5,6]);self.assertEqual(new.nodes,[3,4,5]);self.assertEqual(new.ways,old.ways);self.assertEqual(new.relations,[20]);self.assertTrue(all(node[-1] for _,nodes in new.ways for node in nodes))
 def test_full_index_queries_match_legacy_callbacks_including_untagged_area_members(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);a=root/'old';b=root/'new';a.mkdir();b.mkdir();source=self.source(a);shutil.copyfile(source,b/source.name);shutil.copyfile(source.with_suffix('.pbf.receipt.json'),(b/source.name).with_suffix('.pbf.receipt.json'));bounds=(-.001,-.001,.02,.02)
   with patch.object(map_transport,'index_node_filter',side_effect=lambda osm:osm.filter.EntityFilter(osm.osm.NODE).enable_for(osm.osm.NODE)):
    before=map_transport.indexed_extract(source,bounds)
   after=map_transport.indexed_extract(b/source.name,bounds);self.assertEqual(after,before)
   self.assertIn(20,[e['id'] for e in after['elements'] if e['type']=='relation']);self.assertIn(11,[e['id'] for e in after['elements'] if e['type']=='way'])
   controls=map_transport.indexed_extract(b/source.name,bounds,True);self.assertEqual([e['id'] for e in controls['elements']],[3])

if __name__=='__main__':unittest.main()
