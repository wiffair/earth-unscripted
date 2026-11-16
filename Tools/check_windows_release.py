"""Run the packaged worker, chooser and both Unity runtimes on Windows."""
from pathlib import Path
import argparse, hashlib, json, os, subprocess, sys, tempfile, uuid

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game', type=Path, help='Extracted Earth Unscripted folder')
    args = parser.parse_args()
    if sys.platform != 'win32':
        raise SystemExit('Native Windows execution is required')
    game = args.game.resolve()
    checks = []
    for mode, name in [('Standard','Earth Unscripted Beta'), ('HD','Earth Unscripted Beta HD')]:
        player = game/'Players'/mode
        worker = player/'Helpers/AutomaticWorld'
        for relative, expected in json.loads((worker/'package-files.json').read_text()).items():
            if hashlib.sha256((worker/relative).read_bytes()).hexdigest() != expected:
                raise AssertionError('Worker checksum: '+relative)
        python = worker/'Python/python.exe'
        code = '''import sys;sys.dont_write_bytecode=True
import numpy, rasterio, shapely, pyproj, scipy, osmium, certifi
from PIL import Image
from google.transit import gtfs_realtime_pb2
from zoneinfo import ZoneInfo
assert ZoneInfo('Europe/London')
assert pyproj.Transformer.from_crs(4326,3857,always_xy=True).transform(-3,51)[0]<0
import area_pipeline, area_server
print('Native Windows GIS imports, CRS and time zones passed')
'''
        subprocess.run([str(python), '-X', 'utf8', '-c', code], check=True, cwd=worker, timeout=90)
        resolved = subprocess.check_output([str(python), '-X', 'utf8', str(worker/'_internal/worker_entry.py'),
            '--world-data', 'resolve', '-3.2', '51.5', 'GB', 'terrain'], cwd=worker, timeout=90, text=True)
        if not json.loads(resolved):
            raise AssertionError('Empty provider resolution')
        # Headless startup verifies the Windows engine/native plugins, not rendered driving.
        token = uuid.uuid4().hex
        log = Path(tempfile.gettempdir())/('EarthUnscripted-Windows-'+mode+'-'+token+'.log')
        subprocess.run([str(player/(name+'.exe')), '-batchmode', '-nographics', '-quit',
            '-roadRushBetaSmoke', token, '-roadRushSharedProfile', token, '-logFile', str(log)],
            cwd=player, check=True, timeout=120)
        text = log.read_text(errors='replace')
        if 'Initialize engine version' not in text or any(error in text for error in
            ['DllNotFoundException', 'BadImageFormatException', 'Fallback handler could not load library']):
            raise AssertionError('Unity startup failed; inspect '+str(log))
        # The worker must leave signed/hashed resources untouched.
        for relative, expected in json.loads((worker/'package-files.json').read_text()).items():
            if hashlib.sha256((worker/relative).read_bytes()).hexdigest() != expected:
                raise AssertionError('Worker changed after execution: '+relative)
        checks.append(mode+': worker, provider resolution, immutable files, Unity headless startup')
    chooser = game/'Earth Unscripted.exe'
    subprocess.run([str(chooser), '--check'], check=True, timeout=15)
    subprocess.run([str(chooser), '--ui-smoke'], check=True, timeout=20)
    result = dict(passed=True, checks=checks+['Native chooser paths/identities and window startup'],
                  limitation='Headless Unity startup does not validate rendered Windows driving or a physical controller.')
    Path('windows-release-result.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result, indent=2))

if __name__ == '__main__':
    main()
