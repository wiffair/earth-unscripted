"""Build the existing worker source on the host target; preserve its ABI name."""
from pathlib import Path
import hashlib,json,platform,shutil,subprocess,sys

ROOT=Path(__file__).resolve().parents[1]
source=ROOT/'World/Source';work=ROOT/'.worker-build'
windows=sys.platform=='win32'
target=ROOT/'Unity'/('AutomaticWorldWindowsWorker' if windows else 'AutomaticWorldWorker')
if target.exists():raise SystemExit('Worker output exists; move it aside before rebuilding')
pins=json.loads((ROOT/'World/source-pins.json').read_text())
for name,digest in pins.items():
    if hashlib.sha256((source/name).read_bytes()).hexdigest()!=digest:raise SystemExit('Worker source changed; update its identity and release pins explicitly: '+name)
command=[sys.executable,'-m','PyInstaller','--noconfirm','--clean','--onedir','--name','RoadRushWorldWorker','--distpath',str(work/'dist'),'--workpath',str(work/'build'),'--specpath',str(work/'spec'),'--paths',str(source)]
for package in ['rasterio','pyproj','osmium']:command+=['--collect-all',package]
command+=['--hidden-import','build_map']
for p in sorted(source.rglob('*')):
    if not p.is_file() or '__pycache__' in p.parts:continue
    relative=p.relative_to(source)
    command+=['--add-data',str(p)+':'+str(relative.parent)]
command.append(str(source/'worker_entry.py'))
subprocess.run(command,check=True,cwd=ROOT)
built=work/'dist/RoadRushWorldWorker'
notices=built/'ThirdPartyNotices';notices.mkdir(exist_ok=True)
import importlib.metadata,sysconfig
for distribution in importlib.metadata.distributions():
    for item in distribution.files or []:
        if any(x in item.name.lower() for x in ['license','copying','notice']):
            file=Path(distribution.locate_file(item))
            if file.is_file():
                if '..' in Path(str(item)).parts:continue
                destination=notices/distribution.metadata['Name']/str(item);destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(file,destination)
python_license=Path(sysconfig.get_path('stdlib'))/'LICENSE.txt'
if python_license.exists():shutil.copy2(python_license,notices/'CPython-LICENSE.txt')
manifest={str(p.relative_to(built)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(built.rglob('*')) if p.is_file()}
(built/'package-files.json').write_text(json.dumps(manifest,indent=2)+'\n')
shutil.copytree(built,target)
print(json.dumps(dict(worker=str(target.relative_to(ROOT)),platform=platform.platform(),files=len(manifest)),indent=2))
