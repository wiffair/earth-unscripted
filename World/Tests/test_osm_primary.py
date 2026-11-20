import tempfile,unittest,urllib.parse
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock,patch
import map_transport
class PrimaryRegionalSources(unittest.TestCase):
    def test_managed_world_uses_full_source_before_any_api_query(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'OpenStreetMap';root.mkdir();path=root/'area.json';original=Mock(side_effect=AssertionError('API must not run'))
            pipeline=SimpleNamespace(fetch=original);map_transport.install(pipeline)
            body=urllib.parse.urlencode(dict(data='[out:json];(way[highway](0,0,1,1);way[railway](0,0,1,1););out body geom;')).encode()
            with patch.object(map_transport,'regional',return_value={'regional':True}) as regional:result=pipeline.fetch(path,'https://overpass-api.de/api/interpreter',lambda *a,**k:None,body=body)
            self.assertTrue(result['regional']);regional.assert_called_once();original.assert_not_called()
    def test_resumable_primary_download_is_not_replaced_by_api_timeout_probes(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'OpenStreetMap';root.mkdir();(root/'regional-one.osm.pbf.resume.json').write_text('{}');original=Mock(side_effect=AssertionError('Keep progressing full source'))
            pipeline=SimpleNamespace(fetch=original);map_transport.install(pipeline);body=urllib.parse.urlencode(dict(data='[out:json];(way[highway](0,0,1,1);way[railway](0,0,1,1););out body geom;')).encode()
            with patch.object(map_transport,'regional',side_effect=TimeoutError('Verified progress retained')):
                with self.assertRaises(TimeoutError):pipeline.fetch(root/'area.json','https://overpass-api.de/api/interpreter',lambda *a,**k:None,body=body)
            original.assert_not_called()
if __name__=='__main__':unittest.main()
