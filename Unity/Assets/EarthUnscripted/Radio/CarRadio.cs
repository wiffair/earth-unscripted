using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        GameSession session;
        AudioSource output;
        int generation,playingGeneration=-1;
        bool preview,quitting,tunePending,pausedByMenu;
        public bool MenuPaused=>session&&session.Menu&&!preview;
        public const float MixGain=1.4f;
        float nextSave,nextControllerVolume;
        int resumedFrame=-10,controllerVolumeDirection;
        public bool ControllerHeld=>Ready&&session&&!session.Menu&&Gamepad.current?.leftShoulder.isPressed==true;
        public void Initialize(GameSession owner){session=owner;}
        IEnumerator Start()
        {
            Library=new RadioLibrary(Path.Combine(SinglePlayerBeta.DataRoot,"RadioLibrary"));
            var audioObject=new GameObject("Radio output");audioObject.transform.SetParent(transform,false);
            output=audioObject.AddComponent<AudioSource>();output.playOnAwake=false;output.loop=false;output.spatialBlend=0;output.priority=80;output.ignoreListenerPause=false;
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
                    if(Directory.Exists(folder))foreach(string pack in Directory.GetFiles(folder,"*.euradio").OrderBy(p=>p,StringComparer.Ordinal))result=Library.Import(pack,approved);
                return result;
            });
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
                output.volume=Mathf.Clamp01(MixGain*Mathf.Clamp01(Settings.volume)*(session?Mathf.Clamp01(session.Profile.volume):1)*Mathf.Clamp01(SegmentType=="song"?Settings.music:SegmentType=="jingle"?Settings.jingles:Settings.speech));
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
            return pausedByMenu||AudioListener.pause||output.isPlaying||Time.frameCount<=resumedFrame+1;
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
                    (tunePending?RadioPlan.Tune(Catalog,station,song,random,Mathf.Clamp01(Settings.introChance),Settings.jingleEvery,failed):RadioPlan.Sequence(Catalog,station,song,random,Mathf.Clamp01(Settings.introChance),Settings.completedSongs,Settings.jingleEvery,failed));
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
            var validation=Task.Run(()=>Library.MediaValid(segment.record));while(!validation.IsCompleted)yield return null;
            if(token!=generation){completed(false);yield break;}
            if(validation.IsFaulted||!validation.Result){Failed(segment,"Missing/corrupt radio audio");completed(false);yield break;}
            AudioType kind=path.EndsWith(".wav",StringComparison.Ordinal)?AudioType.WAV:path.EndsWith(".ogg",StringComparison.Ordinal)?AudioType.OGGVORBIS:AudioType.MPEG;
            AudioClip clip=null;
            using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,kind))
            {
                // Local cached media is decoded before playback. Random seeking
                // must not race a streaming decoder's initial compressed buffer.
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio=false;request.timeout=20;yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success){try{clip=DownloadHandlerAudioClip.GetContent(request);}catch(Exception e){error=e.Message;}}
                else error=request.error;
            }
            if(token!=generation){if(clip)Destroy(clip);completed(false);yield break;}
            if(!clip||clip.length<=0){Failed(segment,error??"Audio decoder returned an empty clip");if(clip)Destroy(clip);completed(false);yield break;}
            bool finished=false;
            try
            {
                // A single radio source owns the entire sequence: speech never overlaps music.
                output.Stop();output.clip=clip;output.time=Mathf.Clamp(segment.startFraction>0?segment.startFraction*clip.length:offset,0,Mathf.Max(0,clip.length-.05f));SegmentType=segment.type;
                string station=Catalog.stations.TryGetValue(segment.station,out var s)?(string)s["name"]:segment.station;
                NowPlaying=segment.type=="song"?station+" · "+(string)segment.record["artist"]+" — "+(string)segment.record["title"]:station+" · "+(segment.type=="intro"?"Presenter introduction":segment.type=="jingle"?"Station jingle":"Station link");
                output.Play();playingGeneration=token;if(MenuPaused)output.Pause();resumedFrame=Time.frameCount;Played.Add(segment);if(Played.Count>64)Played.RemoveAt(0);
                yield return null;
                while(token==generation&&SegmentStillActive())yield return null;
                finished=token==generation;
            }
            finally{if(output&&output.clip==clip){output.Stop();output.clip=null;}if(clip)Destroy(clip);}
            completed(finished);
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
        void OnDestroy(){quitting=true;generation++;if(output){output.Stop();if(output.clip)Destroy(output.clip);Destroy(output.gameObject);}}
    }
}
