"""Ad-hoc sign a locally built Mac player after all worker/manifest additions.

This is local execution signing, not publisher identity signing or notarization.
"""
from pathlib import Path
import argparse,plistlib,subprocess,sys
def main():
    p=argparse.ArgumentParser();p.add_argument('app',type=Path);args=p.parse_args()
    app=args.app.resolve()
    if sys.platform!='darwin' or app.suffix!='.app':p.error('A built macOS .app is required on macOS')
    info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') not in {'games.blackwood.roadrush.beta.singleplayer','games.blackwood.roadrush.beta.singleplayer.hd'}:p.error('Unexpected app identifier')
    subprocess.run(['/usr/bin/codesign','--force','--deep','--sign','-',str(app)],check=True)
    subprocess.run(['/usr/bin/codesign','--verify','--deep','--strict',str(app)],check=True)
    print('Local ad-hoc signature verified:',app)
if __name__=='__main__':main()
