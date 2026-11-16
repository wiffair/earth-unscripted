"""Session boundary for the private loopback world service; no GIS imports."""
import hmac
import json
import os
import re
import secrets
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HEADER = 'X-Earth-Session'

class SessionHandler(BaseHTTPRequestHandler):
    def setup(self):
        self.request.settimeout(15)
        super().setup()

    def parse_request(self):
        if not super().parse_request():
            return False
        # Check before reading a body or invoking any provider/cache operation.
        valid = (self.headers.get_all('Host') == [f'127.0.0.1:{self.server.server_port}']
                 and self.headers.get('Origin') is None
                 and self.headers.get_all(HEADER) is not None
                 and len(self.headers.get_all(HEADER)) == 1
                 and hmac.compare_digest(self.headers.get(HEADER, '').encode('utf-8'), self.server.session_token.encode('ascii'))
                 and self.headers.get('Transfer-Encoding') is None)
        if not valid:
            self.send_error(403, 'Private world session required')
            self.close_connection = True
            return False
        if self.command == 'POST' and (self.headers.get_content_type() != 'application/json'
                                      or len(self.headers.get_all('Content-Length', [])) != 1):
            self.send_error(415, 'JSON with a single content length required')
            self.close_connection = True
            return False
        return True

class SessionServer(ThreadingHTTPServer):
    daemon_threads = True
    def __init__(self, address, handler):
        if address[0] != '127.0.0.1':
            raise ValueError('Loopback binding required')
        self.session_token = secrets.token_hex(32)
        self._connections = threading.BoundedSemaphore(24)
        super().__init__(address, handler)

    def process_request(self, request, address):
        if not self._connections.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, address)
        except BaseException:
            self._connections.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self._connections.release()

    def publish_endpoint(self, folder):
        folder = Path(folder)
        folder.mkdir(parents=True, exist_ok=True, mode=0o700)
        if os.name != 'nt':
            folder.chmod(0o700)
        target = folder/'endpoint.json'
        temporary = folder/('endpoint-' + secrets.token_hex(8) + '.tmp')
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        try:
            with os.fdopen(descriptor, 'w') as output:
                json.dump(dict(url=f'http://127.0.0.1:{self.server_port}', pid=os.getpid(),
                               token=self.session_token), output)
                output.flush()
                os.fsync(output.fileno())
            os.replace(temporary, target)
        finally:
            temporary.unlink(missing_ok=True)

def prepared_file(root, name):
    # A URL path is a filename, never a path on either supported OS.
    if not re.fullmatch(r'[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\.json', name) or 'manifest' in name:
        raise ValueError('Invalid prepared filename')
    root = Path(root).resolve()
    candidate = root/name
    if candidate.resolve().parent != root or candidate.is_symlink():
        raise ValueError('Prepared file must remain inside its cache')
    return candidate

class JobStore:
    """Bound active work and retained results; callers can retry expired jobs."""
    def __init__(self, executor, event_lock, activity, cancelled):
        self.executor, self.event_lock = executor, event_lock
        self.activity, self.cancelled = activity, cancelled
        self.jobs = {}
        self.lock = threading.Lock()

    def _prune(self):
        done = [key for key, future in self.jobs.items() if future.done()]
        for key in done[:-32]:
            del self.jobs[key]
            with self.event_lock:
                self.activity.pop(key, None)
                self.cancelled.discard(key)

    def submit(self, identity, action, *args):
        with self.lock:
            self._prune()
            if sum(not future.done() for future in self.jobs.values()) >= 8:
                raise ValueError('World preparation queue is full; retry shortly')
            self.jobs[identity] = self.executor.submit(action, *args)

    def get(self, identity):
        with self.lock:
            return self.jobs.get(identity)
