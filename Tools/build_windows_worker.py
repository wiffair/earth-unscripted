"""Build the Windows x64 worker on any host; Windows users need no Python install."""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, sys, urllib.request, zipfile

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = json.loads((ROOT/'World/runtime-pins.json').read_text())
PYTHON_URL = RUNTIME['windows']['url']
PYTHON_SHA256 = RUNTIME['windows']['sha256']

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--python-archive', type=Path)
    parser.add_argument('--wheels', type=Path, required=True,
                        help='Directory containing the files in World/windows-wheels.json')
    parser.add_argument('--output', type=Path, default=ROOT/'Unity/AutomaticWorldWindowsWorker')
    args = parser.parse_args()
    source = ROOT/'World/Source'
    for name, expected in json.loads((ROOT/'World/source-pins.json').read_text()).items():
        if digest(source/name) != expected:
            raise SystemExit('Worker source differs from release pins: '+name)
    wheel_pins = json.loads((ROOT/'World/windows-wheels.json').read_text())
    for name, expected in wheel_pins.items():
        if digest(args.wheels/name) != expected:
            raise SystemExit('Windows wheel digest mismatch: '+name)
    archive = args.python_archive or ROOT/'.worker-build'/PYTHON_URL.rsplit('/',1)[-1]
    if not archive.exists():
        archive.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(PYTHON_URL, timeout=60) as response:
            archive.write_bytes(response.read(32*1024*1024))
    if digest(archive) != PYTHON_SHA256:
        raise SystemExit('Official CPython digest mismatch')
    args.output.mkdir(parents=True, exist_ok=False)
    runtime = args.output/'Python'
    with zipfile.ZipFile(archive) as package:
        package.extractall(runtime)
    subprocess.run([sys.executable, '-m', 'pip', 'install', '--no-index', '--no-deps',
                    '--no-compile', '--target', str(runtime/'Lib/site-packages'),
                    '--platform', 'win_amd64', '--python-version', '3.14',
                    '--implementation', 'cp', '--abi', 'cp314', '--only-binary=:all:',
                    *[str(args.wheels/name) for name in sorted(wheel_pins)]], check=True)
    (runtime/'python314._pth').write_text('python314.zip\n.\nLib/site-packages\n../_internal\nimport site\n')
    shutil.copytree(source, args.output/'_internal', ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    notices = args.output/'ThirdPartyNotices'
    notices.mkdir()
    shutil.copy2(runtime/'LICENSE.txt', notices/'CPython-LICENSE.txt')
    # Wheel dist-info licences remain installed alongside their dependencies.
    (notices/'DEPENDENCIES.json').write_text(json.dumps(dict(pythonVersion=RUNTIME['version'], pythonURL=PYTHON_URL,
        pythonSHA256=PYTHON_SHA256, wheels=wheel_pins), indent=2)+'\n')
    files = {str(p.relative_to(args.output)):digest(p) for p in sorted(args.output.rglob('*')) if p.is_file()}
    (args.output/'package-files.json').write_text(json.dumps(files, indent=2)+'\n')
    print(json.dumps(dict(worker=str(args.output), files=len(files), bytes=sum(p.stat().st_size
        for p in args.output.rglob('*') if p.is_file())), indent=2))

if __name__ == '__main__':
    main()
