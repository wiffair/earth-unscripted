import concurrent.futures
import http.client
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from worker_security import SessionHandler, SessionServer, prepared_file, JobStore

class Handler(SessionHandler):
    calls = 0
    def log_message(self, *args): pass
    def do_GET(self):
        type(self).calls += 1
        self.send_response(200); self.end_headers(); self.wfile.write(b'ok')
    def do_POST(self):
        self.rfile.read(int(self.headers['Content-Length']))
        self.do_GET()

class WorkerBoundary(unittest.TestCase):
    def setUp(self):
        self.server = SessionServer(('127.0.0.1', 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
    def tearDown(self):
        self.server.shutdown(); self.server.server_close(); self.thread.join()
    def request(self, headers=None, method='GET', body=None):
        client = http.client.HTTPConnection('127.0.0.1', self.server.server_port, timeout=3)
        try:
            client.request(method, '/health', body=body, headers=headers or {})
            reply = client.getresponse(); reply.read(); return reply.status
        finally: client.close()
    def auth(self): return {'X-Earth-Session': self.server.session_token}
    def test_every_route_requires_current_secret(self):
        self.assertEqual(self.request(), 403)
        self.assertEqual(self.request({'X-Earth-Session': 'bad'}), 403)
        self.assertEqual(self.request({'X-Earth-Session': 'é'}), 403)
        self.assertEqual(self.request(self.auth()), 200)
        other = SessionServer(('127.0.0.1', 0), Handler)
        try: self.assertNotEqual(self.server.session_token, other.session_token)
        finally: other.server_close()
    def test_browser_origin_and_rebinding_host_rejected(self):
        for key, value in [('Origin', 'https://hostile.example'), ('Origin', 'null'), ('Host', 'attacker.example')]:
            self.assertEqual(self.request(dict(self.auth(), **{key:value})), 403)
    def test_simple_posts_and_chunked_requests_rejected_before_handler(self):
        calls = Handler.calls
        self.assertEqual(self.request(dict(self.auth(), **{'Content-Type':'text/plain'}), 'POST', '{}'), 415)
        self.assertEqual(self.request(dict(self.auth(), **{'Transfer-Encoding':'chunked'})), 403)
        self.assertEqual(Handler.calls, calls)
        self.assertEqual(self.request(dict(self.auth(), **{'Content-Type':'application/json'}), 'POST', '{}'), 200)
    def test_endpoint_is_private_atomic_and_rotates(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)/'session'
            self.server.publish_endpoint(folder)
            target=folder/'endpoint.json'; value=json.loads(target.read_text())
            self.assertEqual(value['token'], self.server.session_token)
            self.assertEqual(value['url'], f'http://127.0.0.1:{self.server.server_port}')
            self.assertEqual(list(folder.glob('*.tmp')), [])
            if os.name != 'nt':
                self.assertEqual(target.stat().st_mode & 0o777, 0o600)
                self.assertEqual(folder.stat().st_mode & 0o777, 0o700)
    def test_prepared_filename_and_symlink_escape(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'prepared'; root.mkdir()
            self.assertEqual(prepared_file(root, '-1_2.signs.json'), root.resolve()/'-1_2.signs.json')
            for name in ['../private.json', '..\\private.json', 'C:private.json', '/private.json', '%2e%2e%2fprivate.json', 'manifest.json']:
                with self.assertRaises(ValueError): prepared_file(root, name)
            if os.name != 'nt':
                (root/'escape.json').symlink_to(Path(temp)/'private.json')
                with self.assertRaises(ValueError): prepared_file(root, 'escape.json')
    def test_queue_and_retention_are_bounded(self):
        release = threading.Event()
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
            activity={}; cancelled=set(); jobs=JobStore(executor,threading.Lock(),activity,cancelled)
            try:
                for i in range(8): jobs.submit(str(i), release.wait)
                with self.assertRaises(ValueError): jobs.submit('overflow', release.wait)
            finally: release.set()
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
            jobs=JobStore(executor,threading.Lock(),activity,cancelled)
            for i in range(100):
                key=str(i); jobs.submit(key, lambda: None); jobs.get(key).result(timeout=3)
                activity[key]=1; cancelled.add(key)
            self.assertLessEqual(len(jobs.jobs), 33)
            self.assertLessEqual(len(activity), 33)
            self.assertLessEqual(len(cancelled), 33)

if __name__ == '__main__': unittest.main()
