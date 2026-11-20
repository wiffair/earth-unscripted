import hashlib,json,tempfile,time,unittest
from pathlib import Path
from unittest.mock import patch
import osm_cache,map_transport
URL='https://download.geofabrik.de/test-latest.osm.pbf'
class PeerAdmission(unittest.TestCase):
    def fixture(self,root,**changes):
        incoming=root/'Incoming';incoming.mkdir();data=b'complete verified peer source';sha=hashlib.sha256(data).hexdigest();p=incoming/(sha+'.osm.pbf');p.write_bytes(data)
        receipt=dict(url=URL,bytes=len(data),sha256=sha,fetchedUTC=time.time(),origin='authenticated-gameplay-peer',peerFingerprint='a'*64,licence='ODbL-1.0');receipt.update(changes);p.with_suffix('.pbf.receipt.json').write_text(json.dumps(receipt));return p,data
    def test_actual_downloader_uses_committed_peer_source_without_network(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);p,data=self.fixture(root);target=root/'regional-test.osm.pbf';events=[]
            with patch.object(map_transport.urllib.request,'urlopen',side_effect=AssertionError('No source redownload')):row=map_transport._download(target,URL,1000,lambda kind,**kw:events.append(kind))
            self.assertEqual(target.read_bytes(),data);self.assertFalse(p.exists());self.assertEqual(row['origin'],'authenticated-gameplay-peer');self.assertIn('osm-peer-source-admitted',events)
    def test_wrong_url_hash_age_size_and_uncommitted_origin_are_not_admitted(self):
        cases=[{'url':URL+'?other'},{'sha256':'b'*64},{'fetchedUTC':time.time()-8*86400},{'bytes':True},{'origin':'unverified-download'},{'peerFingerprint':'bad'},{'licence':'unknown'}]
        for changes in cases:
            with self.subTest(changes=changes),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);self.fixture(root,**changes);target=root/'regional-test.osm.pbf';self.assertIsNone(osm_cache.admit_peer(target,URL,1000));self.assertFalse(target.exists())
    def test_corrupted_received_payload_does_not_enter_source_cache(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);p,data=self.fixture(root);p.write_bytes(b'x'*len(data));self.assertIsNone(osm_cache.admit_peer(root/'regional-test.osm.pbf',URL,1000))
    def test_incoming_sources_count_toward_same_osm_quota(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);p,data=self.fixture(root);self.assertGreater(osm_cache.usage_bytes(root),len(data))
if __name__=='__main__':unittest.main()
