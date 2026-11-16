"""Build the existing worker source on the host target; preserve its ABI name."""
from pathlib import Path
import argparse,hashlib,json,platform,shutil,subprocess,sys

ROOT=Path(__file__).resolve().parents[1]
source=ROOT/'World/Source';work=ROOT/'.worker-build'
runtime=json.loads((ROOT/'World/runtime-pins.json').read_text())
if platform.python_version()!=runtime['version']:
    raise SystemExit('Build with the pinned CPython '+runtime['version']+' runtime; found '+platform.python_version())

windows=sys.platform=='win32'
if windows:
    raise SystemExit('Use Tools/build_windows_worker.py --wheels <downloaded pinned wheels>; the Windows player uses embedded CPython, not a PyInstaller EXE.')
target=ROOT/'Unity'/('AutomaticWorldWindowsWorker' if windows else 'AutomaticWorldWorker')
if target.exists():raise SystemExit('Worker output exists; move it aside before rebuilding')
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--wheels',type=Path,required=True,help='Directory containing World/macos-wheels.json archives')
args=parser.parse_args()
wheel_pins=json.loads((ROOT/'World/macos-wheels.json').read_text())
for name,digest in wheel_pins.items():
    if hashlib.sha256((args.wheels/name).read_bytes()).hexdigest()!=digest:
        raise SystemExit('Mac wheel digest mismatch: '+name)
subprocess.run([sys.executable,'-m','pip','install','--no-index','--no-deps','--force-reinstall',
                *[str(args.wheels/name) for name in sorted(wheel_pins)]],check=True)

pins=json.loads((ROOT/'World/source-pins.json').read_text())
for name,digest in pins.items():
    if hashlib.sha256((source/name).read_bytes()).hexdigest()!=digest:raise SystemExit('Worker source changed; update its identity and release pins explicitly: '+name)
command=[sys.executable,'-m','PyInstaller','--noconfirm','--clean','--onedir','--name','EarthUnscriptedWorldWorker','--distpath',str(work/'dist'),'--workpath',str(work/'build'),'--specpath',str(work/'spec'),'--paths',str(source)]
for package in ['rasterio','pyproj','osmium']:command+=['--collect-all',package]
command+=['--hidden-import','build_map']
for p in sorted(source.rglob('*')):
    if not p.is_file() or '__pycache__' in p.parts:continue
    relative=p.relative_to(source)
    command+=['--add-data',str(p)+':'+str(relative.parent)]
command.append(str(source/'worker_entry.py'))
subprocess.run(command,check=True,cwd=ROOT)
built=work/'dist/EarthUnscriptedWorldWorker'
if sys.platform=='darwin':
    # GIS wheels can carry incompatible PROJ builds with identical basenames.
    # Keep pyproj's original extension/library pair together instead of using
    # PyInstaller's flattened, basename-deduplicated copy.
    import pyproj,os
    package=Path(pyproj.__file__).parent
    isolated=built/'_internal/pyproj'
    shutil.copytree(package/'.dylibs',isolated/'.dylibs',dirs_exist_ok=True)
    for extension in package.glob('*.so'):
        extension_target=isolated/extension.name;shutil.copy2(extension,extension_target)
        lines=subprocess.check_output(['otool','-L',str(extension_target)],text=True).splitlines()[1:]
        for line in lines:
            library=line.strip().split(' (')[0]
            if '.dylibs/' in library:
                subprocess.run(['install_name_tool','-change',library,'@loader_path/.dylibs/'+Path(library).name,str(extension_target)],check=True)
        subprocess.run(['codesign','--force','--sign','-',str(extension_target)],check=True)
    for library in (isolated/'.dylibs').glob('*.dylib'):
        subprocess.run(['codesign','--force','--sign','-',str(library)],check=True)
subprocess.run([str(built/'EarthUnscriptedWorldWorker'),'--help'],check=True,capture_output=True)
notices=built/'ThirdPartyNotices';notices.mkdir(exist_ok=True)
(notices/'DEPENDENCIES.json').write_text(json.dumps(dict(pythonVersion=platform.python_version(),runtime=runtime['macOS'],wheels=json.loads((ROOT/'World/macos-wheels.json').read_text())),indent=2)+'\n')
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
