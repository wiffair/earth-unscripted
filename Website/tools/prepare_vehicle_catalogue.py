"""Record released vehicle download details and preserve their original notices."""
# SPDX-License-Identifier: MIT
from pathlib import Path
import zipfile,json,hashlib
ROOT=Path(__file__).resolve().parents[1]
cars=[]
for slug,file,title,tag in [('vw-splittie','VW-Splittie-2.0.0.eucar','VW Splittie','vw-splittie-v2.0.0'),('hekla','Hekla-Formula-Offroad-2.0.0.eucar','Hekla Formula Offroad','v0.24.0-beta.1')]:
 path=ROOT.parent/'Unity/Assets/StreamingAssets/VehiclePacks'/file
 with zipfile.ZipFile(path) as z:
  manifest=json.loads(z.read('manifest.json'))
  (ROOT/'public/assets/cars'/(slug+'-LICENSE.txt')).write_bytes(z.read('LICENSE.txt'))
 cars.append({'id':slug,'title':title,'name':manifest['name'],'version':manifest['version'],'filename':file,'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'credits':manifest['priceCredits'],'url':'https://github.com/wiffair/earth-unscripted/releases/download/'+tag+'/'+file,'physics':manifest['physics']})
(ROOT/'data/cars.json').write_text(json.dumps(cars,indent=2)+'\n')
