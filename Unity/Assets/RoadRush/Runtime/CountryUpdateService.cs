using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RoadRush.Core;
using UnityEngine;
namespace RoadRush
{
    public sealed class ReplicationState
    {
        public long sequence;public string timestamp;
        public static ReplicationState Parse(string text)
        {
            if(text==null||text.Length>16384)throw new InvalidDataException("Oversized replication state");var values=new Dictionary<string,string>();
            foreach(var line in text.Split('\n')){int equals=line.IndexOf('=');if(equals>0&&!line.StartsWith("#"))values[line.Substring(0,equals).Trim()]=line.Substring(equals+1).Trim().Replace("\\:",":");}
            if(!values.TryGetValue("sequenceNumber",out var number)||!long.TryParse(number,NumberStyles.None,CultureInfo.InvariantCulture,out long sequence)||sequence<1||sequence>999999999||!values.TryGetValue("timestamp",out var timestamp)||!DateTimeOffset.TryParse(timestamp,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _))throw new InvalidDataException("Invalid replication state");
            return new ReplicationState{sequence=sequence,timestamp=timestamp};
        }
        public static string File(long sequence)=>$"{sequence/1000000:D3}/{sequence/1000%1000:D3}/{sequence%1000:D3}";
    }
    public sealed class CountryUpdateService : MonoBehaviour
    {
        public static CountryUpdateService Instance {get;private set;}
        public static string OverrideFolder;public static bool AllowVerification;
        public static Func<string,string,int,CancellationToken,Task> FetchOverride;
        public static Func<bool> BusyOverride;
        public GameSession Session;public bool Enabled=true,Automatic=true;public bool Busy {get;private set;}
        public string Status {get;private set;}="Country updates waiting for idle network";
        public string Folder=>OverrideFolder??Path.Combine(RoadRush.SinglePlayerBeta.ProductRoot,"MapUpdates");
        public int SourceRequests {get;private set;}
        readonly Dictionary<string,float> checkedAt=new Dictionary<string,float>();CancellationTokenSource request;Task current;bool applying,preflight;float deadline,nextSweep=20;string curl;
        public static CountryUpdateService Bind(GameSession session)
        {
            if(!Instance){var root=new GameObject("Country cache background maintenance");Instance=root.AddComponent<CountryUpdateService>();DontDestroyOnLoad(root);}
            Instance.Session=session;Instance.Enabled=string.IsNullOrEmpty(PlayerProfile.StorageOverride)||AllowVerification;return Instance;
        }
        public static void Unbind(GameSession session)
        {
            var service=Instance;if(!service||!ReferenceEquals(service.Session,session))return;
            service.Session=null;
            // An already applying immutable update finishes on its owned worker;
            // keeping the coroutine alive releases Busy and observes its result.
            if(!service.applying)service.request?.Cancel();
        }
        void Start(){curl=Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor?Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),"curl.exe"):"/usr/bin/curl";StartCoroutine(Sweep());}
        bool IsBlocked(bool foreground)=>!Enabled||!Session||!Session.BootReady||LocationLoading.Busy||Session.Network.MapNetworkingBusy||Session.Network.Active&&!Session.Network.Hosting||(!foreground&&Session.Downloads.Busy)||AllowVerification&&BusyOverride!=null&&BusyOverride()||!DataPackageRules.NetworkMayRun(false,Session.Conditions.NetworkingBusy,false,Session.Network.Active?Session.Network.RoundTripMs:0,Session.Network.GameplayQueuedBytes);
        bool Blocked=>IsBlocked(preflight);
        void Update(){if(request!=null&&!applying&&(Blocked||preflight&&Time.realtimeSinceStartup>deadline))request.Cancel();}
        IEnumerator Sweep()
        {
            while(true)
            {
                if(Automatic&&!Busy&&!Blocked&&Time.unscaledTime>=nextSweep)
                {
                    nextSweep=Time.unscaledTime+3600;
                    CountryPack[] packs=Array.Empty<CountryPack>();try{packs=CountryPacks.Installed().Where(p=>p.Ready&&(bool?)p.Metadata["completeNodes"]==true).GroupBy(p=>p.Id).Select(g=>g.First()).ToArray();}catch(Exception e){Status="Using cached maps · "+e.Message;}
                    foreach(var pack in packs){while(Busy)yield return null;if(Blocked)break;if(checkedAt.TryGetValue(pack.Id,out float last)&&Time.unscaledTime-last<3600)continue;yield return Check(pack,false);}
                }
                yield return null;
            }
        }
        public IEnumerator BeforePrepare(CountryPack pack)
        {
            if(!Enabled||Session.Network.Active&&!Session.Network.Hosting)yield break;
            float wait=Time.realtimeSinceStartup+8;while((Busy||IsBlocked(true))&&Time.realtimeSinceStartup<wait)yield return null;if(Busy||IsBlocked(true))yield break;
            if(checkedAt.TryGetValue(pack.Id,out float last)&&Time.unscaledTime-last<60)yield break;
            // Own the job on the persistent service. Destroying a menu/session
            // that awaited it must not leave Busy latched or orphan its request.
            StartCoroutine(Check(pack,true));
            while(Busy&&Time.realtimeSinceStartup<wait+.3f)yield return null;
        }
        string Source(CountryPack pack)
        {
            if(!DataPackageRules.SafeId(pack.Id))throw new InvalidDataException("Unsupported country identity");return pack.ReplicationSource;
        }
        IEnumerator Get(string url,string path,int maximum)
        {
            request=new CancellationTokenSource();SourceRequests++;current=AllowVerification&&FetchOverride!=null?FetchOverride(url,path,maximum,request.Token):BackgroundHttps.Fetch(curl,url,path,maximum,request.Token);
            while(!current.IsCompleted)yield return null;
            request.Dispose();request=null;
        }
        Exception Failure=>current.IsCanceled?new OperationCanceledException():current.Exception?.GetBaseException();
        static Task<T> LowWork<T>(Func<T> job)=>Task.Factory.StartNew(()=>
        {
            var old=Thread.CurrentThread.Priority;try{Thread.CurrentThread.Priority=System.Threading.ThreadPriority.BelowNormal;return job();}finally{Thread.CurrentThread.Priority=old;}
        },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        static DataFileManifest ReadStaged(string path,string manifestPath,string pack,string version,string source,string timestamp)
        {
            try
            {
                if(!System.IO.File.Exists(manifestPath)||new FileInfo(manifestPath).Length>DataPackageRules.MaximumHeaderBytes)return null;
                var candidate=JsonConvert.DeserializeObject<DataFileManifest>(System.IO.File.ReadAllText(manifestPath));
                return candidate!=null&&(candidate.format==1||candidate.timestamp==DateTimeOffset.Parse(timestamp,CultureInfo.InvariantCulture).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ",CultureInfo.InvariantCulture))&&candidate.pack==pack&&long.TryParse(candidate.toVersion,out long sequence)&&candidate.source==source+"/"+ReplicationState.File(sequence)+".osc.gz"&&DataPackageRules.NextSequence(version,candidate.fromVersion,candidate.toVersion)&&DataPackageRules.FileMatches(path,candidate)?candidate:null;
            }
            catch{return null;}
        }
        static void WriteManifest(string path,DataFileManifest manifest)
        {string temporary=path+".partial";System.IO.File.WriteAllText(temporary,JsonConvert.SerializeObject(manifest));if(System.IO.File.Exists(path))System.IO.File.Replace(temporary,path,path+".bak");else System.IO.File.Move(temporary,path);}
        IEnumerator Check(CountryPack initial,bool foreground)
        {
            if(Busy)yield break;Busy=true;preflight=foreground;if(IsBlocked(foreground)){Status="Country changes waiting for idle network";Busy=preflight=false;yield break;}deadline=Time.realtimeSinceStartup+8;CountryPack pack=initial;string source=null,root=null,error=null;
            try{source=Source(pack);root=Path.Combine(Folder,pack.Id);Directory.CreateDirectory(root);}catch(Exception e){error=e.Message;}
            if(error==null)
            {
                Status="Checking "+pack.Name+" map changes…";string statePath=Path.Combine(root,"latest.state.txt");yield return Get(source+"/state.txt",statePath,16384);
                if(Failure!=null)error=Failure is OperationCanceledException?"Update check deferred · cached map stays available":Failure.Message;
                ReplicationState latest=null;long installed=0;
                if(error==null)try{latest=ReplicationState.Parse(System.IO.File.ReadAllText(statePath));if(!long.TryParse(pack.Version,out installed))throw new InvalidDataException("Country needs a replication-capable base pack");if(latest.sequence<installed)throw new InvalidDataException("Source replication is older than the installed country");if(latest.sequence-installed>100)throw new InvalidDataException("Country base is over100updates old · a fresh base pack is needed");}catch(Exception e){error=e.Message;}
                int applied=0;
                while(error==null&&installed<latest.sequence&&applied<7)
                {
                    if(Blocked||foreground&&Time.realtimeSinceStartup>deadline){error="Updates pending · current driving area remains pinned";break;}
                    long next=installed+1;string id=pack.Id+"-"+next,path=Path.Combine(root,id+".osc.gz"),state=Path.Combine(root,id+".state.txt"),manifestPath=Path.Combine(root,id+".json");DataFileManifest manifest=null;
                    ReplicationState changed=null;
                    if(System.IO.File.Exists(state))try{changed=ReplicationState.Parse(System.IO.File.ReadAllText(state));if(changed.sequence!=next)changed=null;}catch{changed=null;}
                    if(changed==null)
                    {
                        yield return Get(source+"/"+ReplicationState.File(next)+".state.txt",state,16384);if(Failure!=null){error=Failure.Message;break;}
                        try{changed=ReplicationState.Parse(System.IO.File.ReadAllText(state));if(changed.sequence!=next)throw new InvalidDataException("Daily state does not match its update");}catch(Exception e){error=e.Message;break;}
                    }
                    var capturedId=pack.Id;var capturedVersion=pack.Version;var validation=LowWork(()=>ReadStaged(path,manifestPath,capturedId,capturedVersion,source,changed.timestamp));current=validation;
                    while(!current.IsCompleted)yield return null;if(Failure!=null){error=Failure.Message;break;}manifest=validation.Result;
                    if(manifest==null)
                    {
                        if(Blocked){error="Download waiting for idle network";break;}
                        yield return Get(source+"/"+ReplicationState.File(next)+".osc.gz",path,DataPackageRules.MaximumFileBytes);if(Failure!=null){error=Failure.Message;break;}
                        var fingerprint=LowWork(()=>
                        {
                            var item=new DataFileManifest{id=id,kind="map-diff",pack=capturedId,fromVersion=installed.ToString(CultureInfo.InvariantCulture),toVersion=next.ToString(CultureInfo.InvariantCulture),bytes=new FileInfo(path).Length,sha256=DataPackageRules.HashFile(path),source=source+"/"+ReplicationState.File(next)+".osc.gz",licence="ODbL-1.0"};
                            if(!DataPackageRules.Valid(item))throw new InvalidDataException("Invalid staged update");WriteManifest(manifestPath,item);return item;
                        });current=fingerprint;while(!current.IsCompleted)yield return null;if(Failure!=null){error=Failure.Message;break;}manifest=fingerprint.Result;
                    }
                    if(Blocked||foreground&&Time.realtimeSinceStartup>deadline){error="Changes downloaded · activation deferred";break;}
                    applying=true;var capturedPack=pack;var capturedManifest=manifest;string updateRoot=Folder;current=LowWork(()=>
                    {
                        var result=MapReplication.Apply(capturedPack,path,capturedManifest,changed.timestamp);
                        if(string.IsNullOrEmpty(capturedManifest.signature)&&result.west<=result.east&&result.south<=result.north){capturedManifest.hasBounds=true;capturedManifest.west=result.west;capturedManifest.south=result.south;capturedManifest.east=result.east;capturedManifest.north=result.north;WriteManifest(manifestPath,capturedManifest);}PeerCountryUpdates.TrimApplied(updateRoot,capturedPack.Folder);return result;
                    });
                    while(!current.IsCompleted)yield return null;applying=false;
                    if(Failure!=null){error=Failure.Message;break;}installed=next;applied++;try{pack=new CountryPack(pack.Folder);}catch(Exception e){error=e.Message;break;}
                }
                if(error==null){checkedAt[pack.Id]=Time.unscaledTime;Status=installed==latest.sequence?pack.Name+" cache current to "+latest.timestamp+" · active drive unchanged":pack.Name+" changes prepared · more catch-up pending";if(installed<latest.sequence)nextSweep=Time.unscaledTime+60;}
            }
            if(error!=null){Status="Cached maps available · "+error;nextSweep=Mathf.Min(nextSweep,Time.unscaledTime+900);}
            Busy=preflight=applying=false;current=null;
        }
        void OnApplicationQuit(){request?.Cancel();}
        void OnDestroy(){request?.Cancel();if(Instance==this)Instance=null;}
    }
}
