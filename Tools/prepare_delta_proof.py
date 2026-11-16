"""A genuine metadata-only radio revision, retaining existing recordings."""
from pathlib import Path
import sys,zipfile,json,shutil,hashlib,struct
from prepare_update_release import radio_delta,artifact
root=Path(__file__).resolve().parents[1];out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
source=root/'Unity/Assets/StreamingAssets/RadioPacks/EarthUnscripted-TwoStations-3.0.0.euradio';shutil.copy2(source,out/'old.euradio')
def body(archive,name):
 with zipfile.ZipFile(archive) as z:e=z.getinfo(name)
 data=archive.read_bytes();a,b=struct.unpack_from('<HH',data,e.header_offset+26);start=e.header_offset+30+a+b;return data[start:start+e.compress_size]
with zipfile.ZipFile(source) as old:
 music=next(n for n in old.namelist() if n.startswith('library/audio/songs/'))
 level=next(i for i in range(1,10) if __import__('zlib').compress(old.read(music),i)[2:-4]==body(source,music))
 target=out/'new.euradio'
 with zipfile.ZipFile(target,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=level) as new:
  for entry in old.infolist():
   data=old.read(entry.filename)
   if entry.filename=='playlists/rewind.json':
    payload=json.loads(data);payload['entries']=list(reversed(payload['entries']));data=(json.dumps(payload,indent=2)+'\n').encode()
   new.writestr(entry,data,compresslevel=level)
chunks=out/'pieces';chunks.mkdir(exist_ok=True);recipe=radio_delta(target,chunks,'v0.24.0-beta.4')
recipe_file=out/'recipe.json';recipe_file.write_text(json.dumps(recipe,separators=(',',':'))+'\n')
cache=out/'cache';cache.mkdir(exist_ok=True);descriptor=artifact(recipe_file,'v0.24.0-beta.4');shutil.copy2(recipe_file,cache/(descriptor['sha256']+'.json'))
(out/'artifact.json').write_text(json.dumps(artifact(target,'v0.24.0-beta.4'))+'\n');(out/'recipe-artifact.json').write_text(json.dumps(descriptor)+'\n')
print('Prepared playlist-only delta proof; compression level',level)
