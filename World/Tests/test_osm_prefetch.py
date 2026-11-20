import json,tempfile,unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from shapely.geometry import box,mapping
import osm_cache,osm_prefetch,map_transport

def feature(identity,bounds,parent='world'):
    return dict(properties=dict(id=identity,parent=parent,urls=dict(pbf='https://download.geofabrik.de/'+identity+'.osm.pbf')),geometry=mapping(box(*bounds)))
class NearbySources(unittest.TestCase):
    def directory(self):return dict(features=[feature('world',(-180,-80,180,80),None),feature('here',(-.01,-.01,.01,.01)),feature('near',(.02,-.01,.1,.1)),feature('other',(-.1,-.01,-.02,.1)),feature('far',(10,10,20,20))])
    def test_only_nearby_leaf_regions_around_start_are_offered(self):
        rows=osm_prefetch.neighbouring_regions(self.directory(),(-.001,-.001,.001,.001));self.assertEqual({r['id'] for r in rows},{'near','other'})
    def test_overlapping_city_is_useful_even_when_region_contains_start(self):
        directory=self.directory();city=feature('bbbike-Crossing',(-.2,-.2,.2,.2));city['properties']['city']='Crossing';directory['features'].append(city)
        rows=osm_prefetch.neighbouring_regions(directory,(-.001,-.001,.001,.001));self.assertIn('bbbike-Crossing',{r['id'] for r in rows})
    def test_foreground_source_io_preempts_optional_downloads(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);manager=osm_prefetch.NearbySources(root,lambda *a,**kw:None,start=False);manager.schedule((-.001,-.001,.001,.001))
            with osm_cache.activity(root,'io'),patch.object(map_transport,'_download',side_effect=AssertionError('Foreground must win')):
                with self.assertRaises(osm_prefetch.PrefetchPaused):manager.step()
    def test_neighbour_downloads_are_bounded_cached_and_not_repeated(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'regional-index.geojson').write_text(json.dumps(self.directory()));events=[];manager=osm_prefetch.NearbySources(root,lambda kind,**kw:events.append(kind),start=False);manager.schedule((-.001,-.001,.001,.001));manager.city_probe=(0,0,__import__('time').monotonic());calls=[]
            def fetch(path,url,limit,event,cancel,**kwargs):
                calls.append(url);cancel();self.assertLessEqual(limit,map_transport.CAP);self.assertFalse(kwargs['lease_wait']);path.write_bytes(b'complete');return {}
            with patch.object(map_transport,'_cached_download',return_value=None),patch.object(map_transport,'_download',side_effect=fetch),patch.object(osm_prefetch.shutil,'disk_usage',return_value=SimpleNamespace(total=1_000_000_000_000,free=100_000_000_000)):
                manager.step();self.assertEqual(len(calls),2);manager.step();self.assertEqual(len(calls),2)
            self.assertIn('osm-prefetch-ready',events)
    def test_low_free_space_prevents_optional_download(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'regional-index.geojson').write_text(json.dumps(self.directory()));manager=osm_prefetch.NearbySources(root,lambda *a,**kw:None,start=False);manager.schedule((-.001,-.001,.001,.001));manager.city_probe=(0,0,__import__('time').monotonic())
            with patch.object(map_transport,'_cached_download',return_value=None),patch.object(map_transport,'_download',side_effect=AssertionError('No spare disk')),patch.object(osm_prefetch.shutil,'disk_usage',return_value=SimpleNamespace(total=1_000_000_000_000,free=5_000_000_000)):manager.step()

if __name__=='__main__':unittest.main()
