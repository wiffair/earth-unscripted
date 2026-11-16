# SPDX-License-Identifier: MIT
"""Create seamless mono engine loops from credited public Freesound previews.
Requires ffmpeg and numpy (Blender's bundled Python has numpy). No synthesis.
Crossfades the source tail into its head, removes DC/rumble, and normalizes peaks.
"""
from pathlib import Path
import subprocess, wave, json, hashlib
import numpy as np
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'Generated/RecordedAudio';OUT.mkdir(parents=True,exist_ok=True)
def loop(source,start,duration,name):
 raw=subprocess.check_output(['ffmpeg','-v','error','-ss',str(start),'-i',str(ROOT/'AudioSource'/source),'-t',str(duration),'-af','highpass=f=45,lowpass=f=7500','-ar','44100','-ac','1','-f','f32le','-'])
 x=np.frombuffer(raw,dtype='<f4').copy();x-=np.mean(x)
 overlap=11025
 fade=np.linspace(0,1,overlap,endpoint=False)
 # Tail and head meet through a quarter-second equal-gain blend.
 blend=x[-overlap:]*(1-fade)+x[:overlap]*fade
 x=np.concatenate([blend,x[overlap:-overlap]])
 x[:64]*=np.linspace(0,1,64);x[-64:]*=np.linspace(1,0,64)
 x*=.78/max(np.max(np.abs(x)),1e-6)
 pcm=np.round(np.clip(x,-1,1)*32767).astype('<i2')
 path=OUT/(name+'.wav')
 with wave.open(str(path),'wb') as f:f.setparams((1,2,44100,len(pcm),'NONE','not compressed'));f.writeframes(pcm.tobytes())
 return dict(file=path.name,source=source,startSeconds=start,inputSeconds=duration,loopSeconds=len(x)/44100,peak=float(np.max(np.abs(x))),rms=float(np.sqrt(np.mean(x*x))),boundaryStep=float(abs(x[0]-x[-1])),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
report=[loop('vw-1961-idle.mp3',1.5,6,'vw-idle'),loop('vw-1974-contact-mic.mp3',91,6,'vw-load')]
(OUT/'processing.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
