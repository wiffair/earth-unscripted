import json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from types import SimpleNamespace
from area_server import StoragePipeline
from area_pipeline import Pipeline,RAW_CACHE_LIMIT_BYTES,PREPARED_CACHE_LIMIT_BYTES

class StoragePolicy(unittest.TestCase):
    def limits(self,settings,kind=RAW_CACHE_LIMIT_BYTES,free=100_000_000_000):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'storage-settings.json').write_text(json.dumps(settings));file=root/'source.tif';file.write_bytes(b'data')
            store=StoragePipeline.__new__(StoragePipeline);store.cache=root;calls=[]
            with patch.object(Pipeline,'_prune_groups',lambda self,groups,limit:calls.append(limit)),patch('area_server.shutil.disk_usage',return_value=SimpleNamespace(free=free)):
                store._prune_groups([(file,[file],1)],kind)
            return calls[-1]
    def test_source_default_is_five_decimal_gigabytes(self):self.assertEqual(self.limits({}),5_000_000_000)
    def test_old_settings_keep_new_source_default(self):self.assertEqual(self.limits(dict(preparedGigabytes=2,finishedGigabytes=1)),5_000_000_000)
    def test_source_limit_is_independent(self):self.assertEqual(self.limits(dict(sourceGigabytes=8,preparedGigabytes=2)),8_000_000_000)
    def test_prepared_limit_stays_independent(self):self.assertEqual(self.limits(dict(sourceGigabytes=8,preparedGigabytes=2),PREPARED_CACHE_LIMIT_BYTES),2_000_000_000)
    def test_malformed_or_nonfinite_setting_uses_safe_default(self):
        for value in (True,'unbounded',None,float('nan'),float('inf')):
            with self.subTest(value=value):self.assertEqual(self.limits(dict(sourceGigabytes=value)),5_000_000_000)
    def test_source_clamps_to_menu_range(self):
        self.assertEqual(self.limits(dict(sourceGigabytes=-1)),500_000_000);self.assertEqual(self.limits(dict(sourceGigabytes=100)),50_000_000_000)
    def test_reserves_free_space_without_reserving_whole_requested_quota(self):self.assertEqual(self.limits({},free=3_000_000_000),1_000_000_004)
if __name__=='__main__':unittest.main()
