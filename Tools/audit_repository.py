"""Bounded, read-only release hygiene check. Does not claim full legal clearance."""
from pathlib import Path
import hashlib,json,re,sys

ROOT=Path(__file__).resolve().parents[1]
TEXT={'.cs','.py','.json','.md','.txt','.shader','.asmdef','.xml','.m','.c','.h','.swift','.yml','.yaml','.plist','.js','.mjs','.ts','.tsx','.html','.css','.jsonc'}
PRIVATE={'Library','Temp','Logs','Obj','UserSettings','__pycache__','BetaData','Careers','AutomaticWorldCache','.git','.venv','.worker-build','AutomaticWorldWorker','AutomaticWorldWindowsWorker','node_modules','.wrangler'}
def audit():
    problems=[];scanned=0;largest=[]
    for path in sorted(ROOT.rglob('*')):
        rel=path.relative_to(ROOT)
        if any(x in PRIVATE for x in rel.parts) or rel.parts[:2] == ('Website','dist') or (rel.parts[0]=='Updater' and any(x in {'bin','obj','native'} for x in rel.parts[1:])):continue
        if path.is_symlink():problems.append(dict(path=str(rel),reason='Symlink is not a portable source payload'));continue
        if not path.is_file():continue
        size=path.stat().st_size
        if size>100*1024**2 and str(rel) not in json.loads((ROOT/'Content/catalogue.json').read_text())['hydratedPaths']:
            problems.append(dict(path=str(rel),reason='Oversized ordinary Git file'))
        largest.append((size,str(rel)))
        if (path.name.startswith('.env') and path.name!='.env.example'
            or path.suffix.lower() in {'.pem','.key','.p12','.pfx','.mobileprovision'}
            or path.name in {'.netrc','.git-credentials','credentials.local.json'}
            or path.name.endswith('.credentials.json')
            or any(n in {'.secrets','.aws','.ssh'} for n in rel.parts)):
            problems.append(dict(path=str(rel),reason='Private configuration/key filename'))
        if path.suffix.lower() not in TEXT:continue
        text=path.read_text(errors='replace');scanned+=1
        for pattern in [r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',r'\bgh[pousr]_[A-Za-z0-9]{30,}\b',r'\bAKIA[A-Z0-9]{16}\b',r'/Users/[A-Za-z][^/\s"\']+/']:
            if re.search(pattern,text):problems.append(dict(path=str(rel),reason='Credential/private-machine pattern'));break
    return dict(passed=not problems,textFilesScanned=scanned,problems=problems,largestFiles=sorted(largest,reverse=True)[:8],scope='Bounded filename/text pattern scan. Binary payload provenance, unknown/encoded secrets and legal ownership are not proven by this check.')
if __name__=='__main__':
    result=audit();print(json.dumps(result,indent=2));sys.exit(0 if result['passed'] else 1)
