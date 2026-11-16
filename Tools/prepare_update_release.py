"""Prepare unsigned release metadata and exact-byte radio delta recipes for publisher review."""
from pathlib import Path
import argparse,json,hashlib,zipfile,struct,base64,datetime,re
ROOT=Path(__file__).resolve().parents[1]
def sha(data):return hashlib.sha256(data).hexdigest()
def file_sha(path):
 h=hashlib.sha256()
 with path.open('rb') as f:
  for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
 return h.hexdigest()
def artifact(path,tag):return dict(filename=path.name,bytes=path.stat().st_size,sha256=file_sha(path),url=f'https://github.com/wiffair/earth-unscripted/releases/download/{tag}/{path.name}')
def radio_delta(source,output,tag):
 raw=source.read_bytes();pieces=[]
 def append(data):
  if not data:return
  digest=sha(data);row=dict(bytes=len(data),sha256=digest)
  if len(data)<=32768:row['inline']=base64.b64encode(data).decode()
  else:
   name='radio-chunk-'+digest+'.bin';path=output/name
   if not path.exists():path.write_bytes(data)
   row['url']=f'https://github.com/wiffair/earth-unscripted/releases/download/{tag}/{name}'
  pieces.append(row)
 with zipfile.ZipFile(source) as z:
  entries=sorted(z.infolist(),key=lambda e:e.header_offset);cursor=0
  for e in entries:
   assert e.header_offset>=cursor
   append(raw[cursor:e.header_offset]);header=e.header_offset
   name_len,extra_len=struct.unpack_from('<HH',raw,header+26)
   body=header+30+name_len+extra_len;append(raw[header:body]);append(raw[body:body+e.compress_size]);cursor=body+e.compress_size
  append(raw[cursor:])
 recipe=dict(format='EarthUnscriptedRadioDelta',schemaVersion=1,bytes=len(raw),sha256=sha(raw),pieces=pieces)
 # Self-check reconstruction before advertising a delta.
 reconstructed=b''.join(base64.b64decode(p['inline']) if 'inline'in p else (output/('radio-chunk-'+p['sha256']+'.bin')).read_bytes() for p in pieces)
 assert reconstructed==raw
 return recipe
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--players',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--sequence',type=int,required=True);a=p.parse_args()
 version=re.search(r'Version = "([^"]+)"',(ROOT/'Unity/Assets/EarthUnscripted/Runtime/ProductIdentity.cs').read_text())[1];tag='v'+version
 a.output.mkdir(parents=True,exist_ok=True)
 downloads=[]
 for target in ['macOS-arm64','Windows-x64']:
  path=a.players/f'EarthUnscripted-{version}-{target}.zip';row=artifact(path,tag)
  with zipfile.ZipFile(path) as z:row['unpackedBytes']=sum(e.file_size for e in z.infolist())
  row['target']=target;row['minimumOS']='macOS 15; Apple silicon' if target=='macOS-arm64' else 'Windows 10/11; x64';downloads.append(row)
 packs=[]
 for path in sorted((ROOT/'Unity/Assets/StreamingAssets/VehiclePacks').glob('*.eucar')):
  with zipfile.ZipFile(path) as z:m=json.loads(z.read('manifest.json'))
  pack_tag='vw-splittie-v2.0.0' if m['id']=='earthunscripted.vw-splittie' and m['version']=='2.0.0' else 'v0.24.0-beta.1' if m['version']=='2.0.0' else tag
  packs.append(dict(id=m['id'],name=m['name'],kind='vehicle',version=m['version'],revision=m['revision'],minimumGameVersion=version,autoAdd=False,artifact=artifact(path,pack_tag)))
 approved=set(json.loads((ROOT/'Unity/Assets/StreamingAssets/RadioPacks/approved-audio.json').read_text()))
 # The combined pack is the current managed radio distribution. It retains source pack IDs.
 for path in sorted((ROOT/'Unity/Assets/StreamingAssets/RadioPacks').glob('*TwoStations*.euradio')):
  with zipfile.ZipFile(path) as z:m=json.loads(z.read('manifest.json'))
  recipe=radio_delta(path,a.output,tag);name='Radio-Delta-'+m['pack_id']+'-'+m['pack_version']+'.json';file=a.output/name;file.write_text(json.dumps(recipe,separators=(',',':'))+'\n')
  pack_tag='v0.24.0-beta.1' if m['pack_version']=='3.0.0' else tag
  packs.append(dict(id=m['pack_id'],name='Earth Unscripted radio stations',kind='radio',version=m['pack_version'],minimumGameVersion=version,autoAdd=True,artifact=artifact(path,pack_tag),deltaRecipe=artifact(file,tag),approvedAudioSha256=sorted(approved&{item['sha256'] for item in m['file_integrity']})))
 now=datetime.datetime.now(datetime.timezone.utc)
 result=dict(format='EarthUnscriptedUpdates',schemaVersion=1,sequence=a.sequence,issuedUtc=now.isoformat(),expiresUtc=(now+datetime.timedelta(days=90)).isoformat(),game=dict(version=version,channel='beta',notesUrl=f'https://github.com/wiffair/earth-unscripted/releases/tag/{tag}',downloads=downloads),packs=packs)
 (a.output/'catalogue.unsigned.json').write_text(json.dumps(result,indent=2)+'\n')
 print(json.dumps(dict(version=version,packs=len(packs),deltaFiles=len(list(a.output.glob('radio-chunk-*.bin'))),output=str(a.output))))

if __name__=='__main__':main()
