import unittest,weakref,gc
from types import SimpleNamespace
from unittest.mock import patch
from area_server import StoragePipeline
from area_pipeline import Pipeline

class ElevationJobReuse(unittest.TestCase):
 def fixture(self):
  pipeline=object.__new__(StoragePipeline);builder=SimpleNamespace(ROOT=None);created=[];events=[]
  class Raster:
   def __init__(self):self.source=builder.ROOT;created.append(weakref.ref(self))
   def sample(self):return self.source
  builder.Elevation=Raster;pipeline.builder=builder;pipeline.event=lambda kind,**fields:events.append((kind,fields))
  return pipeline,builder,Raster,created,events
 def test_one_decoding_per_job_with_fresh_inputs_next_job(self):
  p,b,factory,created,events=self.fixture()
  def prepare(self,tile):
   b.ROOT=tile;first=b.Elevation();second=b.Elevation();self_test.assertIs(first,second);return second.sample()
  self_test=self
  with patch.object(Pipeline,'_prepare_locked',prepare):
   self.assertEqual(p._prepare_locked((1,2)),(1,2));self.assertIs(b.Elevation,factory)
   self.assertEqual(p._prepare_locked((3,4)),(3,4));self.assertIs(b.Elevation,factory)
  gc.collect();self.assertEqual(len(created),2);self.assertTrue(all(ref() is None for ref in created));self.assertEqual([e[1]['loadsAvoided'] for e in events],[1,1])
 def test_cache_hit_allocates_no_raster(self):
  p,b,factory,created,events=self.fixture()
  with patch.object(Pipeline,'_prepare_locked',return_value='cached'):self.assertEqual(p._prepare_locked((0,0)),'cached')
  self.assertEqual(created,[]);self.assertIs(b.Elevation,factory)
 def test_failure_restores_factory_and_releases_decoded_arrays(self):
  p,b,factory,created,events=self.fixture()
  def fail(self,tile):self.builder.Elevation();raise ValueError('Injected preparation failure')
  with patch.object(Pipeline,'_prepare_locked',fail):
   with self.assertRaises(ValueError):p._prepare_locked((0,0))
  gc.collect();self.assertIs(b.Elevation,factory);self.assertIsNone(created[0]())

if __name__=='__main__':unittest.main()
