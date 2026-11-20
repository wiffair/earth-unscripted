"""Bounded partial downloads tied to one strong HTTP representation validator.

Callers keep their existing trusted-host/TLS policy. A partial file is never a
source cache hit. Only a complete byte count and the accumulated digest can be
published. RFC9110 Range/If-Range prevents combining different daily extracts.
"""
from pathlib import Path
import hashlib,http.client,json,re,time

_ETAG=re.compile(r'"[^"\x00-\x20\x7f]{1,180}"')
_SHA=re.compile('[a-f0-9]{64}')

class DownloadResume:
    def __init__(self,path,url,limit):
        self.path=Path(path);self.url=url;self.limit=limit
        self.partial=self.path.with_suffix(self.path.suffix+'.partial')
        self.metadata=self.path.with_suffix(self.path.suffix+'.resume.json')
        self.count=0;self.expected=None;self.etag=None;self.sha=hashlib.sha256()
        try:
            if self.metadata.stat().st_size>4096:raise ValueError('Oversized resume metadata')
            row=json.loads(self.metadata.read_text());count=row['bytes'];expected=row['expectedBytes'];updated=row['updatedUTC'];etag=row['etag'];sha=row['sha256']
            if row['url']!=url or isinstance(count,bool) or not isinstance(count,int) or isinstance(expected,bool) or not isinstance(expected,int) or not 0<count<=expected<=limit:raise ValueError('Invalid resume bounds')
            if isinstance(updated,bool) or not isinstance(updated,(int,float)) or not 0<=time.time()-updated<7*86400 or not isinstance(etag,str) or not _ETAG.fullmatch(etag) or not isinstance(sha,str) or not _SHA.fullmatch(sha):raise ValueError('Invalid resume validator')
            if self.partial.stat().st_size<count:raise ValueError('Truncated resume checkpoint')
            with self.partial.open('rb') as source:
                remaining=count
                while remaining:
                    chunk=source.read(min(1024*1024,remaining));self.sha.update(chunk);remaining-=len(chunk)
                    if not chunk:raise ValueError('Missing resume bytes')
            if self.sha.hexdigest()!=sha:raise ValueError('Changed resume bytes')
            # An interrupted process may have written beyond its last atomic
            # checkpoint. Keep only the independently verified prefix.
            with self.partial.open('r+b') as output:output.truncate(count)
            self.count=count;self.expected=expected;self.etag=etag
        except (OSError,ValueError,TypeError,KeyError,RecursionError):self.discard()

    @property
    def complete(self):return self.expected is not None and self.count==self.expected

    @property
    def headers(self):
        return {'Range':f'bytes={self.count}-','If-Range':self.etag} if self.count and self.etag else {}

    def accept(self,status,headers):
        length=headers.get('Content-Length')
        if length is not None:
            if not re.fullmatch(r'\d{1,12}',length):raise ValueError('Invalid source content length')
            length=int(length)
        if headers.get('Content-Encoding','identity').lower()!='identity':raise ValueError('Encoded regional transfer not admitted')
        if status==206:
            value=headers.get('Content-Range','');match=re.fullmatch(r'bytes (\d+)-(\d+)/(\d+)',value)
            if not self.count or not match or tuple(map(int,match.groups()))!=(self.count,self.expected-1,self.expected) or headers.get('ETag')!=self.etag or length not in (None,self.expected-self.count):raise ValueError('Regional resume representation changed')
            return 'ab'
        if status!=200:raise ValueError('Regional source HTTP '+str(status))
        if length is not None and not 0<length<=self.limit:raise ValueError('Regional extract exceeds byte cap')
        # A 200 response to If-Range means restart, never append the new file.
        self.count=0;self.sha=hashlib.sha256();self.expected=length
        etag=headers.get('ETag');self.etag=etag if isinstance(etag,str) and _ETAG.fullmatch(etag) else None
        self.metadata.unlink(missing_ok=True)
        return 'wb'

    def received(self,chunk):
        if self.count+len(chunk)>self.limit or self.expected is not None and self.count+len(chunk)>self.expected:raise ValueError('Regional extract exceeds byte cap/declared length')
        self.count+=len(chunk);self.sha.update(chunk)

    def checkpoint(self):
        if not self.count or not self.etag or self.expected is None:return
        row=dict(url=self.url,bytes=self.count,expectedBytes=self.expected,etag=self.etag,sha256=self.sha.hexdigest(),updatedUTC=time.time())
        temp=self.metadata.with_suffix('.partial');temp.write_text(json.dumps(row));temp.replace(self.metadata)

    def finish(self):
        if not self.count:raise ValueError('Empty regional download')
        if self.expected is not None and self.count!=self.expected:raise http.client.IncompleteRead(b'',self.expected-self.count)
        self.partial.replace(self.path)
        row=dict(url=self.url,bytes=self.count,sha256=self.sha.hexdigest(),fetchedUTC=time.time())
        receipt=self.path.with_suffix(self.path.suffix+'.receipt.json');temp=receipt.with_suffix('.partial');temp.write_text(json.dumps(row));temp.replace(receipt)
        self.metadata.unlink(missing_ok=True)
        return row

    def interrupted(self,error):
        if isinstance(error,ValueError):self.discard();return
        try:self.checkpoint()
        except OSError:pass  # retain a prior atomic checkpoint if storage failed
        if not self.metadata.exists():self.partial.unlink(missing_ok=True)

    def discard(self):
        self.partial.unlink(missing_ok=True);self.metadata.unlink(missing_ok=True)
        self.count=0;self.expected=None;self.etag=None;self.sha=hashlib.sha256()
