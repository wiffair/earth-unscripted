"""Interrupted trusted downloads never combine different regional revisions."""
import hashlib,http.client,io,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from source_resume import DownloadResume
import map_transport
URL='https://download.geofabrik.de/test.osm.pbf'
TAG='"version-one"'
class Resume(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory();self.addCleanup(self.folder.cleanup);self.path=Path(self.folder.name)/'regional-test.osm.pbf'
    def partial(self):
        state=DownloadResume(self.path,URL,100);mode=state.accept(200,{'Content-Length':'6','ETag':TAG})
        with state.partial.open(mode) as out:out.write(b'abc')
        state.received(b'abc');state.checkpoint();return state
    def test_verified_resume_finishes_exact_original_bytes(self):
        self.partial();state=DownloadResume(self.path,URL,100);self.assertEqual(state.headers,{'Range':'bytes=3-','If-Range':TAG})
        mode=state.accept(206,{'Content-Range':'bytes 3-5/6','Content-Length':'3','ETag':TAG})
        with state.partial.open(mode) as out:out.write(b'def')
        state.received(b'def');receipt=state.finish();self.assertEqual(self.path.read_bytes(),b'abcdef');self.assertEqual(receipt['sha256'],hashlib.sha256(b'abcdef').hexdigest());self.assertFalse(state.metadata.exists())
    def test_server_changed_representation_restarts_instead_of_appending(self):
        self.partial();state=DownloadResume(self.path,URL,100);mode=state.accept(200,{'Content-Length':'4','ETag':'"new"'});self.assertEqual(mode,'wb')
        with state.partial.open(mode) as out:out.write(b'NEW!')
        state.received(b'NEW!');state.finish();self.assertEqual(self.path.read_bytes(),b'NEW!')
    def test_wrong_range_or_validator_is_never_appended(self):
        for fields in [{'Content-Range':'bytes 2-5/6','ETag':TAG},{'Content-Range':'bytes 3-5/6','ETag':'"new"'},{'Content-Range':'bytes 3-6/7','ETag':TAG}]:
            with self.subTest(fields=fields):
                state=self.partial()
                with self.assertRaises(ValueError):state.accept(206,fields)
                self.assertFalse(self.path.exists())
    def test_changed_local_prefix_is_discarded(self):
        s=self.partial();s.partial.write_bytes(b'BAD');state=DownloadResume(self.path,URL,100);self.assertEqual(state.headers,{});self.assertFalse(state.partial.exists())
    def test_checkpoint_survives_uncommitted_trailing_bytes(self):
        s=self.partial()
        with s.partial.open('ab') as out:out.write(b'uncommitted')
        state=DownloadResume(self.path,URL,100);self.assertEqual(state.count,3);self.assertEqual(state.partial.read_bytes(),b'abc')
    def test_partial_for_another_url_or_oversize_limit_is_not_reused(self):
        self.partial();self.assertEqual(DownloadResume(self.path,URL+'?different',100).count,0)
        self.partial();self.assertEqual(DownloadResume(self.path,URL,2).count,0)
    def test_no_strong_validator_means_no_resumption(self):
        state=DownloadResume(self.path,URL,100);state.accept(200,{'Content-Length':'6','ETag':'W/"weak"'});state.partial.write_bytes(b'abc');state.received(b'abc');state.interrupted(TimeoutError());self.assertFalse(state.partial.exists());self.assertFalse(state.metadata.exists())
    def test_incomplete_body_is_not_published(self):
        state=self.partial()
        with self.assertRaises(http.client.IncompleteRead):state.finish()
        self.assertFalse(self.path.exists())
    def test_oversize_and_encoded_bodies_are_rejected(self):
        for fields in [{'Content-Length':'101'},{'Content-Length':'6','Content-Encoding':'gzip'}]:
            with self.subTest(fields=fields),self.assertRaises(ValueError):DownloadResume(self.path,URL,100).accept(200,fields)

class NativeDownload(unittest.TestCase):
    def test_time_budget_keeps_verified_prefix_then_actual_download_resumes(self):
        with tempfile.TemporaryDirectory() as name:
            path=Path(name)/'regional-test.osm.pbf';clock=[0.];body=b'a'*65536+b'b'*65536;events=[]
            class Response(io.BytesIO):
                status=200;headers={'Content-Length':str(len(body)),'ETag':TAG}
                def read(self,size=-1):
                    data=super().read(size);clock[0]=301;return data
            with patch.object(map_transport.urllib.request,'urlopen',return_value=Response(body)),patch.object(map_transport.time,'monotonic',side_effect=lambda:clock[0]):
                with self.assertRaises(TimeoutError):map_transport._download(path,URL,200000,lambda kind,**kw:events.append((kind,kw)))
            self.assertFalse(path.exists());self.assertEqual(path.with_suffix('.pbf.partial').stat().st_size,65536)
            class Remainder(io.BytesIO):
                status=206;headers={'Content-Length':'65536','Content-Range':'bytes 65536-131071/131072','ETag':TAG}
            def resume(request,**kwargs):
                self.assertEqual(request.get_header('Range'),'bytes=65536-');self.assertEqual(request.get_header('If-range'),TAG);return Remainder(body[65536:])
            with patch.object(map_transport.urllib.request,'urlopen',side_effect=resume):receipt=map_transport._download(path,URL,200000,lambda kind,**kw:events.append((kind,kw)))
            self.assertEqual(path.read_bytes(),body);self.assertEqual(receipt['sha256'],hashlib.sha256(body).hexdigest());self.assertTrue(any(kind=='regional-download-resumed' for kind,_ in events))

if __name__=='__main__':unittest.main()
