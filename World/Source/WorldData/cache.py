"""Atomic bounded normalised cache; live observations retain a short lifetime.

Keys include adapter, source edition and conversion policy, independently of
game executable versions. This cache never silently upgrades estimated quality.
"""
import json
import os
from pathlib import Path
import tempfile
from .model import digest,validate,usable

class Store:
    def __init__(self,root,max_bytes=64*1024*1024):self.root=Path(root);self.root.mkdir(parents=True,exist_ok=True);self.limit=max_bytes
    @staticmethod
    def key(provider,dataset,adapter,policy,source_hash):return digest([provider,dataset,adapter,policy,source_hash])
    def put(self,key,value):
        if len(key)!=64 or any(c not in '0123456789abcdef' for c in key):raise ValueError('Invalid cache key')
        validate(value);data=json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
        if len(data)>self.limit:raise ValueError('Entry exceeds cache budget')
        fd,name=tempfile.mkstemp(prefix='pending-',suffix='.tmp',dir=self.root)
        try:
            with os.fdopen(fd,'wb') as output:output.write(data);output.flush();os.fsync(output.fileno())
            Path(name).replace(self.root/(key+'.json'))
        finally:
            if Path(name).exists():Path(name).unlink()
        files=sorted(self.root.glob('*.json'),key=lambda p:p.stat().st_mtime,reverse=True);total=0
        for file in files:
            total+=file.stat().st_size
            if total>self.limit:file.unlink()
    def get(self,key,now):
        if len(key)!=64 or any(c not in '0123456789abcdef' for c in key):raise ValueError('Invalid cache key')
        path=self.root/(key+'.json')
        try:
            if path.stat().st_size>self.limit:return None
            value=json.loads(path.read_text())
            if not usable(value,now):return None
            path.touch();return value
        except (OSError,ValueError,TypeError,KeyError):return None
