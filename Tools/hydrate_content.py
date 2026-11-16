"""Restore a locally downloaded, exact-version Earth Unscripted content release."""
from pathlib import Path, PurePosixPath
import hashlib,json,sys,zipfile

ROOT=Path(__file__).resolve().parents[1]
def digest(data):return hashlib.sha256(data).hexdigest()

def hydrate(path):
    catalogue=json.loads((ROOT/'Content/catalogue.json').read_text())
    entry=next((x for x in catalogue['packs'] if x['filename']==path.name),None)
    if entry is None or digest(path.read_bytes())!=entry['sha256']:
        raise ValueError('Archive is not the exact release declared in Content/catalogue.json')
    with zipfile.ZipFile(path) as archive:
        names=archive.namelist()
        if len(names)!=len(set(names)):raise ValueError('Duplicate archive paths')
        manifest=json.loads(archive.read('content-manifest.json'))
        rows=manifest['files'];declared={x['path'] for x in rows}
        if set(names)!=declared|{'content-manifest.json'}:raise ValueError('Undeclared archive entries')
        if sum(x['bytes'] for x in rows)>4*1024**3:raise ValueError('Content exceeds restore limit')
        pending=[]
        # Verify the entire transaction before writing any payload.
        for row in rows:
            logical=PurePosixPath(row['path'])
            if logical.is_absolute() or '..' in logical.parts or '\\' in row['path'] or not row['path'].startswith('Unity/Assets/'):
                raise ValueError('Unsafe restore path')
            data=archive.read(row['path'])
            if len(data)!=row['bytes'] or digest(data)!=row['sha256']:raise ValueError('Payload integrity failed: '+row['path'])
            target=ROOT.joinpath(*logical.parts)
            if not target.resolve().is_relative_to(ROOT.resolve()):raise ValueError('Restore escaped repository')
            if target.exists() and digest(target.read_bytes())!=row['sha256']:raise ValueError('Existing file differs: '+row['path'])
            pending.append((target,data))
        for target,data in pending:
            if target.exists():continue
            target.parent.mkdir(parents=True,exist_ok=True)
            temporary=target.with_name(target.name+'.hydrate-partial')
            with temporary.open('xb') as stream:stream.write(data)
            temporary.replace(target)
    return dict(pack=manifest['id'],version=manifest['version'],files=len(rows),passed=True)

if __name__=='__main__':
    if len(sys.argv)!=2:raise SystemExit('Usage: python3 Tools/hydrate_content.py CONTENT.zip')
    print(json.dumps(hydrate(Path(sys.argv[1]).resolve()),indent=2))
