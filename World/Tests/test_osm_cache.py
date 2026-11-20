import json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import osm_cache

class MapSourceCache(unittest.TestCase):
    def test_legacy_map_sources_move_without_touching_terrain_or_prepared_models(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);raw=root/'Raw';raw.mkdir();maps=root/'OpenStreetMap'
            files={'regional-wales.osm.pbf':b'OSM','regional-wales.osm.pbf.receipt.json':b'receipt','bbbike-index.html':b'catalogue','main-0_0-osm-areas-v2-h768.json':b'area','main-0_0-controls-v1-h768.json':b'control','survey.tif':b'TERRAIN','survey.tif.receipt.json':b'surveyreceipt'}
            for name,data in files.items():(raw/name).write_bytes(data)
            osm_cache.migrate(raw,maps,lambda *a,**kw:None)
            self.assertEqual((raw/'survey.tif').read_bytes(),b'TERRAIN');self.assertEqual((raw/'survey.tif.receipt.json').read_bytes(),b'surveyreceipt')
            for name,data in files.items():
                if name.startswith('survey'):continue
                self.assertEqual((maps/name).read_bytes(),data);self.assertFalse((raw/name).exists())
            osm_cache.migrate(raw,maps,lambda *a,**kw:None);self.assertEqual((maps/'regional-wales.osm.pbf').read_bytes(),b'OSM')
    def test_new_quota_is_independent_and_defaults_for_old_settings(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);maps=root/'OpenStreetMap';maps.mkdir()
            for settings,expected in [({'sourceGigabytes':1},5_000_000_000),({'osmGigabytes':2,'sourceGigabytes':8},2_000_000_000),({'osmGigabytes':True},5_000_000_000),({'osmGigabytes':float('nan')},5_000_000_000),({'osmGigabytes':-4},500_000_000)]:
                (root/'storage-settings.json').write_text(json.dumps(settings));self.assertEqual(osm_cache.limit_bytes(maps),expected)
    def test_process_lease_excludes_a_second_writer_and_releases(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Path(temp)/'source.pbf'
            with osm_cache.file_lease(p) as first:
                self.assertTrue(first)
                with osm_cache.file_lease(p,wait=False) as second:self.assertFalse(second)
            with osm_cache.file_lease(p,wait=False) as after:self.assertTrue(after)
    def test_pruning_keeps_in_use_dataset_and_pairs_its_indexes_and_receipts(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);pbf=root/'regional-old.osm.pbf';pbf.write_bytes(b'old-data');index=root/'regional-old.osm-1.0-2.0.sqlite';index.write_bytes(b'index');receipt=pbf.with_suffix('.pbf.receipt.json');receipt.write_text('{}');new=root/'new.json';new.write_bytes(b'new')
            with patch.object(osm_cache,'limit_bytes',return_value=3),osm_cache.file_lease(pbf):
                osm_cache.prune(root);self.assertTrue(pbf.exists());self.assertTrue(index.exists())
            with patch.object(osm_cache,'limit_bytes',return_value=3):osm_cache.prune(root)
            self.assertFalse(pbf.exists());self.assertFalse(index.exists());self.assertFalse(receipt.exists());self.assertEqual(new.read_bytes(),b'new')
    def test_city_boundary_is_retained_with_its_dataset_and_catalogues_survive_pruning(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);pbf=root/'regional-bbbike-Bristol.osm.pbf';pbf.write_bytes(b'old-source');boundary=root/'bbbike-Bristol.poly';boundary.write_bytes(b'coverage')
            catalogue=root/'bbbike-index.html';catalogue.write_bytes(b'index');new=root/'new.json';new.write_bytes(b'new')
            self.assertEqual(osm_cache.dataset_path(boundary),pbf)
            with patch.object(osm_cache,'limit_bytes',return_value=3):osm_cache.prune(root)
            self.assertFalse(pbf.exists());self.assertFalse(boundary.exists());self.assertTrue(catalogue.exists())
    def test_background_pruning_waits_for_foreground_source_copy(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);p=root/'area.json';p.write_bytes(b'active')
            with patch.object(osm_cache,'limit_bytes',return_value=0),osm_cache.activity(root):
                osm_cache.prune(root);self.assertTrue(p.exists())

if __name__=='__main__':unittest.main()
