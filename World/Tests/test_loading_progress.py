import unittest,threading,time,tempfile,json,http.client
from pathlib import Path
from unittest.mock import patch
import numpy as np
import area_server,build_map
from worker_security import SessionServer

class LoadingProgressTests(unittest.TestCase):
 def test_zero_junction_correction_is_exact_noop(self):
  from scipy.interpolate import PchipInterpolator
  for category in ('road','path','rail'):
   f=dict(category=category,stations=np.arange(0.,301.,2.),heights=np.sin(np.arange(151)/10.))
   before=f['heights'].copy()
   self.assertFalse(build_map.apply_junction_targets(f,{0:before[0],70:before[70]}));np.testing.assert_array_equal(before,f['heights'])
   # A nonzero target still fits the requested junction exactly.
   self.assertTrue(build_map.apply_junction_targets(f,{70:before[70]+2.}));self.assertAlmostEqual(f['heights'][70],before[70]+2.)
 def test_pending_progress_dedup_and_cancellation_over_authenticated_http(self):
  bound=threading.Event();release=threading.Event();progress=threading.Event();holder={}
  class Server(SessionServer):
   def __init__(self,*args):super().__init__(*args);holder['server']=self;bound.set()
  class Pipeline:
   def __init__(self,cache,event,frame):self.cache=cache;cache.mkdir();self.event=event;self.version='fixture'
   def prepare(self,tile):
    self.event('preparation-progress',stage='Fitting road gradients',completed=3,total=8);progress.set();release.wait(5)
    self.event('preparation-progress',stage='Fitting road gradients',completed=4,total=8)
    raise ValueError('Fixture finished')
  with tempfile.TemporaryDirectory() as temp,patch.object(area_server,'StoragePipeline',Pipeline),patch.object(area_server,'SessionServer',Server),patch('WorldData.public_rail.Collector'):
   root=Path(temp)
   with patch('sys.argv',['worker','--cache',str(root/'cache'),'--evidence',str(root/'evidence')]):
    thread=threading.Thread(target=area_server.main);thread.start();self.assertTrue(bound.wait(3));server=holder['server']
    def request(path,body=None):
     conn=http.client.HTTPConnection('127.0.0.1',server.server_port,timeout=3)
     try:
      headers={'X-Earth-Session':server.session_token,'Content-Type':'application/json'}
      conn.request('POST' if body is not None else 'GET',path,json.dumps(body) if body is not None else None,headers);response=conn.getresponse();data=json.loads(response.read());self.assertEqual(response.status,200,data);return data
     finally:conn.close()
    try:
     body=dict(x=0,z=0,headingX=1,headingZ=0,coverageOnly=True)
     first=request('/request',body)['id'];self.assertTrue(progress.wait(3))
     self.assertEqual(request('/request',body)['id'],first)
     pending=request('/job/'+first);self.assertEqual(pending['stage'],'Fitting road gradients');self.assertEqual((pending['completed'],pending['total']),(3,8));self.assertLess(pending['idleSeconds'],3)
     request('/cancel',dict(body,id=first));self.assertNotEqual(request('/request',body)['id'],first)
    finally:release.set();server.shutdown();thread.join(5);self.assertFalse(thread.is_alive())
if __name__=='__main__':unittest.main()
