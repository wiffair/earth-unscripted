"""Create attributed 45-second web previews from the reviewed runtime radio pack."""
# SPDX-License-Identifier: MIT
from pathlib import Path
import zipfile,json,hashlib,subprocess,tempfile
ROOT=Path(__file__).resolve().parents[1]
source=ROOT.parent/'Unity/Assets/StreamingAssets/RadioPacks/EarthUnscripted-TwoStations-3.0.0.euradio'
with zipfile.ZipFile(source) as archive, tempfile.TemporaryDirectory(prefix='earth-radio-preview-') as temp:
 manifest=json.loads(archive.read('manifest.json'));integrity={item['path']:item['sha256'] for item in manifest['file_integrity']}
 songs=json.loads(archive.read('library/songs.json'))['songs']; stations=[]; sources=[]
 for name in ['iron-and-ash','rewind']:
  station=json.loads(archive.read('stations/'+name+'.json')); playlist=json.loads(archive.read(station['playlist']))
  ids={item['song_id'] for item in playlist['entries'] if item['enabled']}; tracks=[]
  for song in songs:
   if song['id'] not in ids:continue
   raw=archive.read(song['audio']);digest=hashlib.sha256(raw).hexdigest()
   if digest!=integrity[song['audio']]:raise RuntimeError('Source recording integrity mismatch')
   input_path=Path(temp)/(song['id']+'.mp3');input_path.write_bytes(raw)
   output=ROOT/'public/assets/audio'/(song['id']+'-preview.mp3')
   subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-ss','30','-i',str(input_path),'-t','45','-vn','-map_metadata','-1','-af','afade=t=in:st=0:d=0.15,afade=t=out:st=43:d=2,alimiter=limit=0.891:level=0','-codec:a','libmp3lame','-b:a','160k','-ar','44100','-metadata','title='+song['title']+' (45-second preview)','-metadata','artist='+song['artist'],'-metadata','composer=Richard Cole','-metadata','copyright=Richard Cole / CC BY-SA 4.0','-metadata','comment=Earth Unscripted; excerpt from 00:30 to 01:15; https://creativecommons.org/licenses/by-sa/4.0/','-metadata','purl=https://earthunscripted.com/radio/'+name,str(output)],check=True)
   tracks.append({key:song[key] for key in ['id','title','artist','genre','duration_seconds'] }|{'preview':'/assets/audio/'+output.name,'previewSeconds':45})
   sources.append({'id':song['id'],'source':song['audio'],'sourceSha256':digest,'previewSha256':hashlib.sha256(output.read_bytes()).hexdigest(),'startSeconds':30,'durationSeconds':45})
  stations.append({key:station[key] for key in ['id','name','frequency_mhz','tagline','genres','description']}|{'presenter':'Alex Mercer' if name=='iron-and-ash' else 'Jamie Bell','tracks':tracks})
 (ROOT/'data/radio.json').write_text(json.dumps({'packVersion':'3.0.0','stations':stations},indent=2,ensure_ascii=False)+'\n')
 (ROOT/'data/radio-preview-provenance.json').write_text(json.dumps({'sourcePack':source.name,'sourcePackSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'author':'Richard Cole','licence':'CC BY-SA 4.0','licenceUrl':'https://creativecommons.org/licenses/by-sa/4.0/','adaptation':'45-second excerpts, fades and peak limiting; original fictional artist and song identities retained','recordings':sources},indent=2)+'\n')
print('Prepared 12 attributed music previews',flush=True)
