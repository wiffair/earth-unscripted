"""Stage a portable Standard/HD game download, including the world-data worker."""
from pathlib import Path
import argparse, hashlib, json, plistlib, shutil, subprocess, zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.23.0-beta.52'
NAMES = ['Earth Unscripted Beta', 'Earth Unscripted Beta HD']
IDENTIFIERS = ['games.blackwood.roadrush.beta.singleplayer', 'games.blackwood.roadrush.beta.singleplayer.hd']

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('platform', choices=['macOS-arm64', 'Windows-x64'])
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--launcher', type=Path, required=True)
    parser.add_argument('--icon', type=Path)
    parser.add_argument('--output', type=Path, required=True, help='Fresh staging directory')
    args = parser.parse_args()
    windows = args.platform == 'Windows-x64'
    identity = json.loads((ROOT/'SOURCE_IDENTITY.json').read_text())
    # Check the actual game source, excluding the editor-only build helpers.
    pins = {str(p.relative_to(ROOT/'Unity')):sha(p) for p in sorted((ROOT/'Unity/Assets').rglob('*.cs')) if '/Editor/' not in str(p)}
    source = hashlib.sha256(json.dumps(pins, sort_keys=True).encode()).hexdigest()
    if source != identity['sharedSource']:
        raise SystemExit('Gameplay source differs from the release identity')
    args.output.mkdir(parents=True, exist_ok=False)
    folder = args.output/'Earth Unscripted'
    folder.mkdir()
    if windows:
        shutil.copy2(args.launcher, folder/'Earth Unscripted.exe')
        players = folder/'Players'
    else:
        app = folder/'Earth Unscripted Beta.app'
        resources = app/'Contents/Resources'
        resources.mkdir(parents=True)
        (app/'Contents/MacOS').mkdir()
        shutil.copy2(args.launcher, app/'Contents/MacOS/ChooseGraphics')
        (app/'Contents/MacOS/ChooseGraphics').chmod(0o755)
        shutil.copy2(args.icon, resources/'AppIcon.icns')
        info = dict(CFBundleDisplayName='Earth Unscripted Beta', CFBundleName='Earth Unscripted Beta',
                    CFBundleExecutable='ChooseGraphics', CFBundleIconFile='AppIcon.icns',
                    CFBundleIdentifier='games.blackwood.roadrush.beta.launcher.current',
                    CFBundlePackageType='APPL', CFBundleShortVersionString=VERSION, CFBundleVersion='52',
                    LSMinimumSystemVersion='15.0',
                    LSApplicationCategoryType='public.app-category.games', NSHighResolutionCapable=True)
        (app/'Contents/Info.plist').write_bytes(plistlib.dumps(info))
        players = resources/'Players'
    assemblies = {}
    choices = []
    for index, (mode, name) in enumerate(zip(['Standard', 'HD'], NAMES)):
        src = args.build/(name if windows else name+'.app')
        target = players/(mode if windows else mode+'.app')
        shutil.copytree(src, target, symlinks=True, ignore=shutil.ignore_patterns(
            '*_BackUpThisFolder_ButDontShipItWithYourGame', '*BurstDebugInformation_DoNotShip*',
            '*.pdb', '*.dSYM', '.DS_Store'))
        data = target/(name+'_Data' if windows else 'Contents/Resources/Data')
        worker = target/('Helpers/AutomaticWorld' if windows else 'Contents/Resources/AutomaticWorld')
        required = worker/('Python/python.exe' if windows else 'RoadRushWorldWorker')
        if not required.is_file():
            raise SystemExit('World-data worker missing: '+str(required))
        manifest = json.loads((worker/'package-files.json').read_text())
        for relative, expected in manifest.items():
            if sha(worker/relative) != expected:
                raise SystemExit('Worker checksum mismatch: '+relative)
        assemblies[mode] = sha(data/'Managed/Assembly-CSharp.dll')
        (data/'StreamingAssets').mkdir(exist_ok=True)
        (data/'StreamingAssets/SharedGameSource.json').write_text(json.dumps(dict(version=VERSION,
            source=source, assembly=assemblies[mode]), indent=2)+'\n')
        # macOS signatures must follow all worker/manifest changes.
        if not windows:
            player_info = target/'Contents/Info.plist'
            info = plistlib.loads(player_info.read_bytes())
            info['LSMinimumSystemVersion'] = '15.0'
            player_info.write_bytes(plistlib.dumps(info))
            subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(target)], check=True)
            # Signing can change nested Mach-O bytes. Record the signed worker,
            # then seal the app without re-signing those nested executables.
            signed_pins = {relative:sha(worker/relative) for relative in manifest}
            (worker/'package-files.json').write_text(json.dumps(signed_pins, indent=2)+'\n')
            subprocess.run(['codesign', '--force', '--sign', '-', str(target)], check=True)
            subprocess.run(['codesign', '--verify', '--deep', '--strict', str(target)], check=True)
        choices.append(dict(title=mode, detail='Balanced graphics and performance' if index==0 else 'Richer lighting and reflections',
            path='Players/'+mode+'.app', identifier=IDENTIFIERS[index]))
    if len(set(assemblies.values())) != 1:
        raise SystemExit('Standard/HD gameplay assemblies differ')
    if not windows:
        (resources/'LaunchManifest.json').write_text(json.dumps(dict(version=VERSION, source=source, choices=choices), indent=2)+'\n')
        subprocess.run(['codesign', '--force', '--sign', '-', str(app)], check=True)
        subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)], check=True)
    shutil.copytree(ROOT/'Provenance', folder/'Licences/Provenance')
    shutil.copytree(ROOT/'LICENSES', folder/'Licences/LICENSES')
    for name in ['LICENSE', 'LICENSE_SCOPE.md']:
        shutil.copy2(ROOT/name, folder/'Licences'/name)
    shutil.copy2(ROOT/'Docs/PLAYING.md', folder/'PLAYING.md')
    first = ('Open Earth Unscripted Beta.app. To keep it, move the whole app to Applications.\n'
             'Requires an Apple silicon Mac with macOS 15 or newer. The beta is ad-hoc signed, not notarized.\n'
             'macOS may require you to approve this downloaded app in Privacy & Security.\n') if not windows else (
             'Extract the complete ZIP first, then open Earth Unscripted.exe.\n'
             'Keep the launcher and Players folders together. This download is for 64-bit Windows 10/11.\n'
             'This beta does not yet have a publisher signing certificate; Windows may show a reputation prompt.\n')
    (folder/'Read me first.txt').write_text('EARTH UNSCRIPTED — BETA 52\n\n'+first+
        '\nChoose Standard for performance or HD for richer lighting. Both share your beta progress.\n'
        'No Unity, Python or developer tools are required. Internet access is used to fetch new areas.\n'
        'The scenery and radio needed at runtime are included. The separate content ZIPs are for source builders.\n'
        '\nInstructions and controls: PLAYING.md\nSource and updates: https://github.com/wiffair/earth-unscripted\n')
    receipt = dict(version=VERSION, platform=args.platform, sharedSource=source, assemblies=assemblies,
                   files=sum(p.is_file() for p in folder.rglob('*')))
    (args.output/'package-result.json').write_text(json.dumps(receipt, indent=2)+'\n')
    print(json.dumps(receipt, indent=2))

if __name__ == '__main__':
    main()
