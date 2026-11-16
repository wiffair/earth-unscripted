"""Refresh runtime source identity after a reviewed source change; geometry keeps its own identity."""
from pathlib import Path
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[1];unity=ROOT/'Unity'
version=re.search(r'Version = "([^"]+)"',(unity/'Assets/EarthUnscripted/Runtime/ProductIdentity.cs').read_text())[1]
identity=json.loads((ROOT/'SOURCE_IDENTITY.json').read_text())
pins={str(p.relative_to(unity)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((unity/'Assets').rglob('*.cs')) if '/Editor/' not in str(p)}
shared=hashlib.sha256(json.dumps(pins,sort_keys=True).encode()).hexdigest();identity.update(version=version,sharedSource=shared,sourcePins=pins)
(ROOT/'SOURCE_IDENTITY.json').write_text(json.dumps(identity,indent=2)+'\n')
p=ROOT/'Launcher/Windows/GraphicsLauncher.c';p.write_text(re.sub(r'static const char \*source="[a-f0-9]{64}";',f'static const char *source="{shared}";',p.read_text()))
print(version,shared)
