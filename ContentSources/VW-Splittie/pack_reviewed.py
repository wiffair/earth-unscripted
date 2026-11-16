# SPDX-License-Identifier: MIT
"""Repack the frozen, reviewed camper payload; no Blender dependency."""
from pathlib import Path
import hashlib,json,zipfile
ROOT=Path(__file__).resolve().parent
manifest=json.loads((ROOT/'manifest.json').read_text())
assert hashlib.sha256((ROOT/'mesh.json').read_bytes()).hexdigest()==manifest['meshSha256']
output=ROOT/'Generated';output.mkdir(exist_ok=True)
path=output/'VW-Splittie-2.0.0.eucar'
with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
 for name in ['LICENSE.txt','README.txt','manifest.json','mesh.json']:
  info=zipfile.ZipInfo(name,(2026,11,16,13,36,26));info.compress_type=zipfile.ZIP_DEFLATED
  archive.writestr(info,(ROOT/name).read_bytes())
with zipfile.ZipFile(path) as archive:assert archive.testzip() is None
assert hashlib.sha256(path.read_bytes()).hexdigest()==json.loads((ROOT/'review.json').read_text())['pack']['sha256']
result=dict(file=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),meshSha256=manifest['meshSha256'])
print(json.dumps(result,indent=2))
