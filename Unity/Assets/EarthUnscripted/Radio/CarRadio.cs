using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

namespace EarthUnscripted.Radio
{
    public sealed class RadioSettings
    {
        public int schema=1,jingleEvery=3,completedSongs;
        public string station="",lastStation="",song="",importDirectory="";
        public float seconds,volume=.45f,music=.8f,speech=1,jingles=.8f,introChance=.8f;
        public Dictionary<string,List<string>> history=new Dictionary<string,List<string>>();
        public HashSet<string> approved=new HashSet<string>();
    }
    public sealed class CarRadio : MonoBehaviour
    {
        public RadioSettings Settings {get;private set;}=new RadioSettings();
        public RadioCatalog Catalog {get;private set;}=new RadioCatalog();
        public RadioLibrary Library {get;private set;}
        public bool Ready {get;private set;}
        public bool Importing=>import!=null;
        public string Status {get;private set;}="Radio loading…";
        public string NowPlaying {get;private set;}="Radio off";
        public string SegmentType {get;private set;}="";
        public AudioSource Output=>output;
        public bool HasCurrentSegment=>output&&output.clip&&playingGeneration==generation;
        public readonly List<RadioSegment> Played=new List<RadioSegment>();
        public readonly List<string> MediaErrors=new List<string>();
        readonly HashSet<string> failed=new HashSet<string>();
        readonly System.Random random=new System.Random();
        Task<RadioCatalog> import;
        Task saving;
        readonly HashSet<CancellationTokenSource> pcmPreparations=new HashSet<CancellationTokenSource>();
        readonly HashSet<RadioPcmPreparation.Upload> pcmUploads=new HashSet<RadioPcmPreparation.Upload>();
        GameSession session;
        AudioSource output;
        int generation,playingGeneration=-1;
        bool preview,quitting,tunePending,pausedByMenu;
        public bool MenuPaused=>session&&session.Menu&&!preview;
        public const float MixGain=1.4f;
        float nextSave,nextControllerVolume;
        int resumedFrame=-10,controllerVolumeDirection,lastAudioSample;
        bool audioConfigurationChanged;
        double lastAudioAdvance,expectedAudioEndDsp;
        bool playbackClockPaused;
        public int PlaybackRecoveries {get;private set;}
        public RadioAudioPolicy.Level CurrentLevel {get;private set;}
        void OnEnable(){AudioSettings.OnAudioConfigurationChanged+=AudioConfigurationChanged;}
        void OnDisable(){AudioSettings.OnAudioConfigurationChanged-=AudioConfigurationChanged;}
        void AudioConfigurationChanged(bool deviceChanged){audioConfigurationChanged=true;}

        public bool ControllerHeld=>Ready&&session&&!session.Menu&&Gamepad.current?.leftShoulder.isPressed==true;
        public void Initialize(GameSession owner){session=owner;}
        IEnumerator Start()
        {
            Library=new RadioLibrary(Path.Combine(SinglePlayerBeta.DataRoot,"RadioLibrary"));
            var audioObject=new GameObject("Radio output");audioObject.transform.SetParent(transform,false);
            output=audioObject.AddComponent<AudioSource>();output.playOnAwake=false;output.loop=false;output.spatialBlend=0;output.priority=16;output.ignoreListenerPause=false;output.bypassReverbZones=true;output.bypassEffects=true;
            try
            {
                string path=Path.Combine(Library.Root,"settings.json");if(File.Exists(path))Settings=JsonConvert.DeserializeObject<RadioSettings>(File.ReadAllText(path))??new RadioSettings();
                if(Settings.schema!=1)Settings=new RadioSettings();
            }
            catch(Exception e) when(e is IOException||e is JsonException){Status="Radio settings reset: "+e.Message;Settings=new RadioSettings();}
            Settings.volume=Finite(Settings.volume,.45f);Settings.music=Finite(Settings.music,.8f);Settings.speech=Finite(Settings.speech,1);Settings.jingles=Finite(Settings.jingles,.8f);Settings.introChance=Finite(Settings.introChance,.8f);Settings.jingleEvery=Mathf.Clamp(Settings.jingleEvery,0,20);if(float.IsNaN(Settings.seconds)||float.IsInfinity(Settings.seconds)||Settings.seconds<0)Settings.seconds=0;
            Settings.history=Settings.history??new Dictionary<string,List<string>>();Settings.approved=Settings.approved??new HashSet<string>();
            // A station is a broadcast, not a saved music player. App startup
            // joins afresh; ordinary menu pauses never enter this path.
            tunePending=!string.IsNullOrEmpty(Settings.station);Settings.song="";Settings.seconds=0;
            if(string.IsNullOrWhiteSpace(Settings.importDirectory))Settings.importDirectory=Path.Combine(Library.Root,"Imports");
            Refresh();
            while(import!=null)yield return null;
            StartCoroutine(Playback());
        }
        static float Finite(float value,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp01(value);
        public void Refresh()
        {
            if(Importing||Library==null)return;
            string builtin=Path.Combine(Application.streamingAssetsPath,"RadioPacks"),inbox=Settings.importDirectory;
            var approved=new HashSet<string>(Settings.approved);Status="Checking radio packs…";
            import=Task.Run(()=>
            {
                Directory.CreateDirectory(inbox);
                string approval=Path.Combine(builtin,"approved-audio.json");
                if(File.Exists(approval))foreach(var hash in JArray.Parse(File.ReadAllText(approval)))approved.Add((string)hash);
                RadioCatalog result=Library.Load();
                foreach(string folder in new[]{builtin,inbox}.Distinct(StringComparer.Ordinal))
                    if(Directory.Exists(folder))foreach(string pack in Directory.GetFiles(folder,"*.euradio").OrderBy(p=>p,StringComparer.Ordinal)){var manifest=RadioPackIdentity(pack);if(result.packs.TryGetValue((string)manifest["pack_id"],out var installed)&&Version.Parse((string)installed["version"]).CompareTo(Version.Parse((string)manifest["pack_version"]))>0)continue;result=Library.Import(pack,approved);}
                return result;
            });
        }
        static JObject RadioPackIdentity(string archive)
        {
            using(var zip=System.IO.Compression.ZipFile.OpenRead(archive))
            {
                var entry=zip.GetEntry("manifest.json");if(entry==null||entry.Length>2*1024*1024)throw new InvalidDataException("Radio manifest is missing/too large.");
                using(var input=entry.Open())using(var output=new MemoryStream()){var buffer=new byte[8192];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>2*1024*1024)throw new InvalidDataException("Radio manifest exceeded its limit.");output.Write(buffer,0,count);}return Updates.UpdateProtocol.Json(output.ToArray());}
            }
        }
        public void AcceptUpdatedCatalog(RadioCatalog next)
        {
            if(!Ready||Importing||next==null)throw new InvalidOperationException("The radio is not ready to adopt an update.");
            RadioLibrary.Validate(next);Catalog=next;failed.Clear();
            // The active AudioClip and sequence retain their immutable records.
            // Do not change generation, source/time, station choice or menu pause.
            // The next selection uses the new playlists; new stations are visible now.
            Status=$"Radio updated · {Catalog.ActiveStations.Count()} stations · {Catalog.songs.Count} songs";
        }
        public string TrackLabel=>SegmentType=="song"&&Catalog.songs.TryGetValue(Settings.song??"",out var song)?(string)song["artist"]+" — "+(string)song["title"]:SegmentType=="intro"?"Presenter introduction":SegmentType=="jingle"?"Station jingle":SegmentType=="drop"?"Station link":"Tuning…";
        void Update()
        {
            if(Ready&&session&&!session.Menu&&Keyboard.current!=null)
            {var k=Keyboard.current;if(k.f7Key.wasPressedThisFrame)TogglePower();if(k.f8Key.wasPressedThisFrame)TuneNext();if(k.f9Key.wasPressedThisFrame)AdjustVolume(-.05f);if(k.f10Key.wasPressedThisFrame)AdjustVolume(.05f);}
            UpdateController();

            if(import!=null&&import.IsCompleted)
            {
                if(import.IsFaulted){Status="Radio import failed: "+import.Exception.GetBaseException().Message;try{Catalog=Library.Load();}catch(Exception){Catalog=new RadioCatalog();}}
                else{Catalog=import.Result;failed.Clear();Status=$"Ready · {Catalog.ActiveStations.Count()} station(s) · {Catalog.songs.Count} songs";}
                foreach(var row in ReviewClips)if(Settings.approved.Contains(Path.GetFileNameWithoutExtension((string)row["_media"])))row["_approved"]=true;
                import=null;Ready=true;
            }
            if(output)
            {
                // Segment levels are normalized from decoded PCM. Legacy independent
                // speech/jingle boosts must not undo the common peak ceiling.
                output.volume=Mathf.Clamp01(MixGain*Mathf.Clamp01(Settings.volume)*(session?Mathf.Clamp01(session.Profile.volume):1));
                SynchronizeMenuPause();
            }
            if(output&&output.clip&&SegmentType=="song"&&!preview)Settings.seconds=output.time;
            if(Ready&&Time.unscaledTime>=nextSave){nextSave=Time.unscaledTime+30;Save();}
        }
        public void SynchronizeMenuPause()
        {
            if(!output)return;
            bool pause=MenuPaused;
            if(pause==pausedByMenu)return;
            pausedByMenu=pause;
            if(pause)output.Pause();else if(output.clip){output.UnPause();resumedFrame=Time.frameCount;}
        }
        bool SegmentStillActive()
        {
            // Coroutines can run before Update on the resume frame. Reconcile
            // the source first, so a paused source is never mistaken for EOF.
            SynchronizeMenuPause();
            if(!output.clip)return false;
            double now=Time.realtimeSinceStartupAsDouble,dsp=AudioSettings.dspTime;
            if(pausedByMenu||AudioListener.pause){lastAudioAdvance=now;playbackClockPaused=true;return true;}
            if(playbackClockPaused)
            {
                playbackClockPaused=false;
                expectedAudioEndDsp=dsp+(output.clip.samples-lastAudioSample)/(double)output.clip.frequency;
            }
            int cursor=output.timeSamples;
            // Stop/device loss resets Unity's cursor to zero. Keep the last
            // advancing sample until recovery, rather than restarting the song.
            if(output.isPlaying&&!audioConfigurationChanged&&cursor>0&&cursor!=lastAudioSample)
            {
                lastAudioSample=cursor;lastAudioAdvance=now;
                expectedAudioEndDsp=dsp+(output.clip.samples-cursor)/(double)output.clip.frequency;
            }
            // The DSP keeps playing during a render hitch. Its predicted end
            // distinguishes real EOF from an interrupted source even if the last
            // rendered frame was seconds before the final samples. Paused time
            // is excluded by rebasing the deadline on resume.
            bool atEnd=RadioAudioPolicy.NaturalEnd(lastAudioSample,output.clip.samples,output.clip.frequency,output.isPlaying,audioConfigurationChanged,dsp,expectedAudioEndDsp);
            // Device resets and genuine source stalls are recoverable without
            // retuning the station or skipping the rest of a song. Never treat
            // a stopped source near its beginning as normal EOF.
            if(audioConfigurationChanged||!atEnd&&(!output.isPlaying||now-lastAudioAdvance>2))
            {
                audioConfigurationChanged=false;output.Stop();output.timeSamples=Mathf.Clamp(lastAudioSample,0,output.clip.samples-1);output.Play();
                lastAudioAdvance=now;expectedAudioEndDsp=dsp+(output.clip.samples-lastAudioSample)/(double)output.clip.frequency;resumedFrame=Time.frameCount;PlaybackRecoveries++;
                Debug.Log("RADIO_PLAYBACK_RECOVERY cursor="+lastAudioSample+" sampleRate="+AudioSettings.outputSampleRate);
                return true;
            }
            audioConfigurationChanged=false;
            return output.isPlaying||Time.frameCount<=resumedFrame+1;
        }
        void UpdateController()
        {
            if(!ControllerHeld){controllerVolumeDirection=0;return;}
            var pad=Gamepad.current;
            if(pad.dpad.left.wasPressedThisFrame)TuneNext(-1);
            if(pad.dpad.right.wasPressedThisFrame)TuneNext(1);
            if(pad.buttonNorth.wasPressedThisFrame)TogglePower();
            int direction=pad.dpad.up.isPressed?1:pad.dpad.down.isPressed?-1:0;
            if(direction!=0&&(direction!=controllerVolumeDirection||Time.unscaledTime>=nextControllerVolume))
            {AdjustVolume(.05f*direction);nextControllerVolume=Time.unscaledTime+(direction!=controllerVolumeDirection?.35f:.13f);}
            controllerVolumeDirection=direction;
        }
        public void Save()
        {
            if(Library==null||saving!=null&&!saving.IsCompleted)return;
            string path=Path.Combine(Library.Root,"settings.json"),json=JsonConvert.SerializeObject(Settings);
            saving=Task.Run(()=>AtomicWrite(path,json));
        }
        static void AtomicWrite(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string pending=path+".partial";File.WriteAllText(pending,text);
            if(File.Exists(path))File.Replace(pending,path,null);else File.Move(pending,path);
        }
        public void ChangeStation(string id)
        {
            if(id!=""&&!Catalog.ActiveStations.Any(s=>(string)s["id"]==id))return;
            if(!string.IsNullOrEmpty(Settings.station))Settings.lastStation=Settings.station;
            generation++;preview=false;tunePending=id!="";output?.Stop();Settings.station=id;Settings.song="";Settings.seconds=0;Settings.completedSongs=0;NowPlaying=id==""?"Radio off":"Tuning…";Save();
        }
        public void TogglePower()
        {
            if(Settings.station!=""){ChangeStation("");return;}
            string id=Catalog.ActiveStations.Any(s=>(string)s["id"]==Settings.lastStation)?Settings.lastStation:Catalog.ActiveStations.Select(s=>(string)s["id"]).FirstOrDefault();if(id!=null)ChangeStation(id);
        }
        public void TuneNext(int direction=1)
        {
            var ids=Catalog.ActiveStations.Select(s=>(string)s["id"]).ToArray();if(ids.Length==0)return;int at=Array.IndexOf(ids,Settings.station);ChangeStation(ids[at<0?0:(at+direction+ids.Length)%ids.Length]);
        }
        public void AdjustVolume(float delta){Settings.volume=Mathf.Clamp01(Settings.volume+delta);Save();}
        public void StepStation(int direction)
        {
            var ids=new[]{""}.Concat(Catalog.ActiveStations.Select(s=>(string)s["id"])).ToArray();int at=Array.IndexOf(ids,Settings.station);ChangeStation(ids[(Math.Max(0,at)+direction+ids.Length)%ids.Length]);
        }
        IEnumerator Playback()
        {
            while(!quitting)
            {
                if(!Ready||preview||string.IsNullOrEmpty(Settings.station)){yield return null;continue;}
                string station=Settings.station;int token=generation;
                if(!Catalog.ActiveStations.Any(s=>(string)s["id"]==station)){ChangeStation("");Status="Selected station is no longer available";continue;}
                if(!Settings.history.TryGetValue(station,out var recent))Settings.history[station]=recent=new List<string>();
                if(recent==null)Settings.history[station]=recent=new List<string>();
                bool resume=Settings.seconds>0&&Catalog.songs.TryGetValue(Settings.song??"",out var resumed)&&
                    ((JArray)Catalog.playlists[station]["entries"]).Any(e=>(string)e["song_id"]==Settings.song&&(bool?)e["enabled"]!=false&&(double?)e["weight"]>0)&&!failed.Contains((string)resumed["_media"]);
                JObject song=resume?Catalog.songs[Settings.song]:RadioPlan.Song(Catalog,station,recent,random,failed);
                if(song==null){Status="No playable songs in selected station. Refresh packs to retry.";ChangeStation("");continue;}
                float offset=resume?Settings.seconds:0;Settings.song=(string)song["id"];Settings.seconds=offset;
                var sequence=resume?new List<RadioSegment>{new RadioSegment{type="song",station=station,song=Settings.song,id=Settings.song,record=song}}:
                    (tunePending?RadioPlan.JoinBroadcast(Catalog,station,song,random,failed):RadioPlan.Broadcast(Catalog,station,song,random,failed));
                tunePending=false;
                bool songPlayed=false;
                foreach(var segment in sequence)
                {
                    if(token!=generation||preview)break;
                    yield return Play(segment,segment.type=="song"?offset:0,token,ok=>{if(segment.type=="song")songPlayed=ok;});
                }
                if(token==generation&&!preview)
                {
                    if(songPlayed){recent.Add((string)song["id"]);while(recent.Count>50)recent.RemoveAt(0);Settings.completedSongs++;}
                    Settings.seconds=0;Settings.song="";Save();
                }
                yield return null;
            }
        }
        IEnumerator Play(RadioSegment segment,float offset,int token,Action<bool> completed)
        {
            string path=null,error=null;try{path=Library.MediaPath(segment.record);}catch(Exception e){error=e.Message;}
            if(error!=null){Failed(segment,error);completed(false);yield break;}
            var validation=Task.Run(()=>Library.MediaValid(segment.record));while(!validation.IsCompleted){if(token!=generation){completed(false);yield break;}yield return null;}
            if(token!=generation){completed(false);yield break;}
            if(validation.IsFaulted||!validation.Result){Failed(segment,"Missing/corrupt radio audio");completed(false);yield break;}
            AudioType kind=path.EndsWith(".wav",StringComparison.Ordinal)?AudioType.WAV:path.EndsWith(".ogg",StringComparison.Ordinal)?AudioType.OGGVORBIS:AudioType.MPEG;
            AudioClip clip=null;
            if(kind==AudioType.MPEG)
            {
                yield return PrepareMp3(segment,path,token,(prepared,level,failure)=>{clip=prepared;CurrentLevel=level;error=failure;});
            }
            else
            using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,kind))
            {
                // Local cached media is decoded before playback. Random seeking
                // must not race a streaming decoder's initial compressed buffer.
                var handler=(DownloadHandlerAudioClip)request.downloadHandler;handler.streamAudio=false;handler.compressed=false;request.timeout=20;
                var download=request.SendWebRequest();while(!download.isDone){if(token!=generation){request.Abort();completed(false);yield break;}yield return null;}
                if(request.result==UnityWebRequest.Result.Success){try{clip=DownloadHandlerAudioClip.GetContent(request);}catch(Exception e){error=e.Message;}}
                else error=request.error;
            }
            if(token!=generation){if(clip)Destroy(clip);completed(false);yield break;}
            if(!clip||clip.length<=0){Failed(segment,error??"Audio decoder returned an empty clip");if(clip)Destroy(clip);completed(false);yield break;}
            if(kind!=AudioType.MPEG)
            {
                // Keep the decoder's resident PCM clip. Three bounded passes avoid
                // a song-sized managed array and a second native AudioClip allocation.
                int channels=clip.channels,frequency=clip.frequency,frames=clip.samples;
                if(channels<1||channels>8||frequency<8000||frequency>192000||frames<=0||(long)frames*channels>RadioAudioPolicy.BoundedNormalization.MaximumSamples)
                {Failed(segment,"Decoded radio clip dimensions are unsupported");Destroy(clip);completed(false);yield break;}
                var normalization=new RadioAudioPolicy.BoundedNormalization(frames,channels,frequency);
                const int BlockFrames=8192;
                var full=new float[Math.Min(BlockFrames,frames)*channels];
                int remainder=frames%BlockFrames;var tail=remainder>0&&frames>BlockFrames?new float[remainder*channels]:full;
                bool readable=true;var slice=System.Diagnostics.Stopwatch.StartNew();
                for(int pass=0;pass<3&&readable;pass++)
                {
                    for(int frame=0;frame<frames;frame+=BlockFrames)
                    {
                        if(token!=generation){Destroy(clip);completed(false);yield break;}
                        int count=Math.Min(BlockFrames,frames-frame)*channels;var block=count==full.Length?full:tail;
                        using(WorkTelemetry.Measure("radio.normalize-slice",pass==0?"dc":pass==1?"energy":"apply",8))
                        {
                            if(!clip.GetData(block,frame)){readable=false;break;}
                            try
                            {
                                if(pass==0)normalization.AccumulateDc(block,count);
                                else if(pass==1)normalization.Measure(block,count);
                                else{normalization.Apply(block,count);if(!clip.SetData(block,frame))readable=false;}
                            }
                            catch(ArgumentException e){error=e.Message;readable=false;}
                        }
                        if(!readable)break;
                        if(slice.ElapsedMilliseconds>=2){yield return null;slice.Restart();}
                    }
                    if(readable)
                    {
                        if(pass==0)normalization.FinishDc();
                        else if(pass==1)CurrentLevel=normalization.FinishMeasurements(segment.type=="song");
                        else normalization.FinishApply();
                    }
                }
                if(!readable){Failed(segment,error??"Radio decoder did not expose writable PCM");Destroy(clip);completed(false);yield break;}
                full=null;tail=null;normalization=null;
            }
            bool finished=false;
            try
            {
                // A single radio source owns the entire sequence: speech never overlaps music.
                output.Stop();output.clip=clip;output.time=Mathf.Clamp(segment.startFraction>0?segment.startFraction*clip.length:offset,0,Mathf.Max(0,clip.length-.05f));SegmentType=segment.type;
                string station=Catalog.stations.TryGetValue(segment.station,out var s)?(string)s["name"]:segment.station;
                NowPlaying=segment.type=="song"?station+" · "+(string)segment.record["artist"]+" — "+(string)segment.record["title"]:station+" · "+(segment.type=="intro"?"Presenter introduction":segment.type=="jingle"?"Station jingle":"Station link");
                output.Play();lastAudioSample=output.timeSamples;lastAudioAdvance=Time.realtimeSinceStartupAsDouble;
                expectedAudioEndDsp=AudioSettings.dspTime+(clip.samples-lastAudioSample)/(double)clip.frequency;playbackClockPaused=MenuPaused||AudioListener.pause;
                audioConfigurationChanged=false;playingGeneration=token;if(MenuPaused)output.Pause();resumedFrame=Time.frameCount;Played.Add(segment);if(Played.Count>64)Played.RemoveAt(0);
                yield return null;
                while(token==generation&&SegmentStillActive())yield return null;
                finished=token==generation;
            }
            finally{if(output&&output.clip==clip){output.Stop();output.clip=null;}if(clip)Destroy(clip);}
            completed(finished);
        }
        IEnumerator PrepareMp3(RadioSegment segment,string path,int token,Action<AudioClip,RadioAudioPolicy.Level,string> complete)
        {
            var cancel=new CancellationTokenSource();pcmPreparations.Add(cancel);
            string root=Path.Combine(Library.Root,"PreparedPcm");
            var preparing=Task.Run(()=>RadioPcmPreparation.Prepare(path,Path.GetFileNameWithoutExtension(path),root,segment.type=="song",cancel.Token));
            // Observe abandoned faults too; disposal/cancellation never waits on the main thread.
            preparing.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);
            AudioClip clip=null;RadioPcmPreparation.Upload upload=null;
            try
            {
                while(!preparing.IsCompleted){if(token!=generation||quitting)yield break;yield return null;}
                if(token!=generation||quitting)yield break;
                if(preparing.IsFaulted||preparing.IsCanceled){complete(null,null,preparing.Exception?.GetBaseException().Message??"Radio preparation cancelled");yield break;}
                var prepared=preparing.Result;
                using(WorkTelemetry.Measure("radio.pcm-create",null,8))clip=AudioClip.Create("Radio PCM · "+segment.id,prepared.Frames,prepared.Channels,prepared.Frequency,false);
                if(!clip){complete(null,null,"Could not create resident radio PCM");yield break;}
                upload=new RadioPcmPreparation.Upload(prepared);pcmUploads.Add(upload);int frames=0;var slice=System.Diagnostics.Stopwatch.StartNew();
                while(frames<prepared.Frames)
                {
                    if(token!=generation||quitting)yield break;
                    while(upload.TryTake(out var chunk))
                    {
                        bool written;using(WorkTelemetry.Measure("radio.pcm-upload",null,8))written=clip.SetData(chunk.Samples,chunk.Frame);
                        frames+=chunk.Samples.Length/prepared.Channels;upload.Release(chunk);
                        if(!written){complete(null,null,"Radio PCM upload failed");yield break;}
                        if(slice.ElapsedMilliseconds>=2)break;
                    }
                    if(upload.Completion.IsFaulted||upload.Completion.IsCanceled){complete(null,null,upload.Completion.Exception?.GetBaseException().Message??"Radio PCM upload cancelled");yield break;}
                    if(frames>=prepared.Frames)break;
                    if(upload.Completion.IsCompleted&&upload.Empty){complete(null,null,"Radio PCM upload ended early");yield break;}
                    // No data is dequeued for probing; producer ownership remains explicit.
                    yield return null;slice.Restart();
                }
                complete(clip,prepared.Level,null);clip=null;
            }
            finally
            {
                if(upload!=null){pcmUploads.Remove(upload);upload.Dispose();}if(clip)Destroy(clip);pcmPreparations.Remove(cancel);cancel.Cancel();
                preparing.ContinueWith(t=>cancel.Dispose(),TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        void Failed(RadioSegment segment,string error)
        {
            failed.Add((string)segment.record["_media"]);string message=segment.id+": "+error;Status="Skipped radio media · "+message;MediaErrors.Add(message);if(MediaErrors.Count>32)MediaErrors.RemoveAt(0);
        }
        public IEnumerable<JObject> ReviewClips=>Catalog.intros.Values.Concat(Catalog.jingles.Values).Concat(Catalog.drops.Values).OrderBy(r=>(string)r["station_id"]).ThenBy(r=>(string)r["id"]);
        public void Approve(JObject row)
        {string hash=Path.GetFileNameWithoutExtension((string)row["_media"]);Settings.approved.Add(hash);row["_approved"]=true;Save();}
        public void Preview(JObject row)
        {
            generation++;output.Stop();preview=true;
            StartCoroutine(PreviewClip(row,generation));
        }
        IEnumerator PreviewClip(JObject row,int token)
        {
            yield return Play(new RadioSegment{type=Catalog.intros.ContainsKey((string)row["id"])?"intro":Catalog.jingles.ContainsKey((string)row["id"])?"jingle":"drop",station=(string)row["station_id"],id=(string)row["id"],record=row},0,token,ok=>{});
            if(token==generation){preview=false;SegmentType="";if(Settings.station=="")NowPlaying="Radio off";}
        }
        public void StopPreview(){if(!preview)return;generation++;output.Stop();preview=false;SegmentType="";NowPlaying=Settings.station==""?"Radio off":"Tuning…";}
        public string StationLabel(JObject station)=>$"{(string)station["name"]} · {(double)station["frequency_mhz"]:0.#} FM";
        void OnApplicationQuit(){quitting=true;if(Library==null)return;try{saving?.GetAwaiter().GetResult();AtomicWrite(Path.Combine(Library.Root,"settings.json"),JsonConvert.SerializeObject(Settings));}catch(IOException){}}
        void OnDestroy(){quitting=true;generation++;foreach(var pending in pcmPreparations)pending.Cancel();foreach(var pending in pcmUploads)pending.Dispose();if(output){output.Stop();if(output.clip)Destroy(output.clip);Destroy(output.gameObject);}}
    }
}
