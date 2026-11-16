"""Run the packaged worker, chooser and both Unity runtimes on Windows."""
from pathlib import Path
import argparse, hashlib, json, os, subprocess, sys, tempfile, time, uuid

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
        plugin = player/(name+'_Data')/'Plugins/x86_64/EarthUnscriptedHaptics.dll'
        native_check = ('import ctypes; p=ctypes.CDLL('+repr(str(plugin))+'); '
                        'assert all(hasattr(p,n) for n in ["eu_open","eu_force","eu_stop","eu_close","eu_error"]); '
                        'print("Earth Unscripted native haptics ABI passed")')
        subprocess.run([str(python), '-c', native_check], check=True, cwd=worker, timeout=30)
        resolved = subprocess.check_output([str(python), '-X', 'utf8', str(worker/'_internal/worker_entry.py'),
            '--world-data', 'resolve', '-3.2', '51.5', 'GB', 'terrain'], cwd=worker, timeout=90, text=True)
        if not json.loads(resolved):
            raise AssertionError('Empty provider resolution')
        # Headless startup verifies the Windows engine/native plugins, not rendered driving.
        token = uuid.uuid4().hex
        log = Path(tempfile.gettempdir())/('EarthUnscripted-Windows-'+mode+'-'+token+'.log')
        # -quit is an Editor argument, not a standalone-player exit contract.
        # Observe native/managed scene startup, then stop only our owned process.
        process = subprocess.Popen([str(player/(name+'.exe')), '-batchmode', '-nographics',
            '-earthUnscriptedBetaSmoke', token, '-earthUnscriptedSharedProfile', token, '-logFile', str(log)], cwd=player)
        try:
            deadline = time.monotonic()+90
            while time.monotonic() < deadline:
                text = log.read_text(errors='replace') if log.exists() else ''
                if all(marker in text for marker in ['Initialize engine version',
                        'Begin MonoManager ReloadAssembly', 'UnloadTime:']):
                    time.sleep(2)
                    break
                if process.poll() is not None:
                    raise AssertionError('Unity exited before scene startup: '+str(process.returncode))
                time.sleep(.2)
            else:
                raise TimeoutError('No native/managed scene startup within 90 seconds')
        finally:
            text = log.read_text(errors='replace') if log.exists() else '(no player log created)'
            print(mode+' player startup log:\n'+text[-8000:], flush=True)
            if process.poll() is None:
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
                process.wait(timeout=15)
        if 'Initialize engine version' not in text or any(error in text for error in
            ['DllNotFoundException', 'BadImageFormatException', 'Fallback handler could not load library']):
            raise AssertionError('Unity startup failed; inspect '+str(log))
        # The worker must leave signed/hashed resources untouched.
        for relative, expected in json.loads((worker/'package-files.json').read_text()).items():
            if hashlib.sha256((worker/relative).read_bytes()).hexdigest() != expected:
                raise AssertionError('Worker changed after execution: '+relative)
        checks.append(mode+': worker, provider resolution, immutable files, native/managed headless scene startup')
    helper = game/'EarthUnscriptedUpdateInstaller.exe'
    if not helper.is_file(): raise AssertionError('Native updater missing')
    probe = subprocess.run([str(helper)], capture_output=True, text=True, timeout=20)
    if probe.returncode != 1 or 'Use --apply' not in probe.stderr:
        raise AssertionError('Native updater did not start correctly')
    checks.append('Native updater starts and rejects an unprepared job')
    chooser = game/'Earth Unscripted.exe'
    subprocess.run([str(chooser), '--check'], check=True, timeout=15)
    subprocess.run([str(chooser), '--ui-smoke'], check=True, timeout=20)
    result = dict(passed=True, checks=checks+['Native chooser paths/identities and window startup'],
                  limitation='Headless Unity startup does not validate rendered Windows driving or a physical controller.')
    Path('windows-release-result.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result, indent=2))

if __name__ == '__main__':
    main()
