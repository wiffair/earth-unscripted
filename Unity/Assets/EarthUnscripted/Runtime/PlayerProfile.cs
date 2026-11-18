using System;
using System.IO;
using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion=9;
        [NonSerialized] public string LoadIssue="";
        public bool LoadBlocked=>!string.IsNullOrEmpty(LoadIssue);
        [Serializable] sealed class SchemaHeader {public int version;}
        static PlayerProfile Refuse(PlayerProfile parsed,string reason){var result=parsed??new PlayerProfile();result.LoadIssue=reason;Debug.LogWarning(reason);return result;}
        public string worldDate="";
        public string region="", regionTimeZone="Europe/London";
        public int weatherMode,clockMode;
        public bool surfaceTracks=true;
        public int trainServiceMode=1;public bool trainTimetableFallback=true;
        public int trainFleetLimit=4;
        public string trainFeedUrl="";
        public float fixedHour=14,clockOffset,dayMinutes=24;
        public GarageState garage=GarageRules.CreateDefault();
        public System.Collections.Generic.List<TrialRecord> records=new System.Collections.Generic.List<TrialRecord>();
        public System.Collections.Generic.List<string> rewardedEvents=new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<ModelLayoutLapRecord> modelLayoutBests=new System.Collections.Generic.List<ModelLayoutLapRecord>();
        public int trackMode=1,raceLaps=1,raceDifficulty=1;
        public bool detailedHud;
        public bool performanceStats;
        public bool customStart;public string startRegion="";public float startX,startZ;public int clarity=0;public bool livingScenery=true;public bool simulatedTrains=true;public bool detailedScenery=false;
        public string careerName="",careerSavedUtc="";
        public int version=9, credits=1200, quality=2;
        public float bestLap, volume=.65f, engineVolume=.35f, rumble=.65f, engineRumble=0, wheelStrength=.25f;
        public int displayWidth,displayHeight,displayMode=1;
        public bool wheelEnabled, wheelForceEnabled, invertForce;
        public string steerPath="", gasPath="", brakePath="", wheelName="G29";
        public float steerLeft=-1,steerCentre=0,steerRight=1,gasRest=1,gasFull=-1,brakeRest=1,brakeFull=-1;
        public static string StorageOverride;
        public static string StoragePath=>PathName;
        static string PathName => string.IsNullOrEmpty(StorageOverride)?Path.Combine(EarthUnscripted.SinglePlayerBeta.ProductRoot,"earth-unscripted-save.json"):StorageOverride;
        public static PlayerProfile Load()
        {
            if(string.IsNullOrEmpty(StorageOverride))
            {
                var args=Environment.GetCommandLineArgs();int proof=Array.IndexOf(args,"-earthUnscriptedLanProof");
                if(proof>=0&&proof+2<args.Length)StorageOverride=Path.GetFullPath(args[proof+2]+"-profile.json");
                int switches=Array.IndexOf(args,"-earthUnscriptedSwitchProof");if(switches>=0&&switches+1<args.Length)StorageOverride=Path.GetFullPath(args[switches+1]+"-profile.json");
                int appearance=Array.IndexOf(args,"-earthUnscriptedAppearanceProof");if(appearance>=0&&appearance+1<args.Length)StorageOverride=Path.GetFullPath(args[appearance+1]+"-profile.json");
                int capture=Array.IndexOf(args,"-earthUnscriptedCapture");
                if(capture>=0&&capture+1<args.Length)StorageOverride=Path.GetFullPath(args[capture+1]+"-profile.json");
            }
            PlayerProfile parsed=null;
            try
            {
                string contents;
                try{contents=File.ReadAllText(PathName);}catch(FileNotFoundException){return new PlayerProfile();}catch(DirectoryNotFoundException){return new PlayerProfile();}
                parsed=JsonUtility.FromJson<PlayerProfile>(contents);
                if(parsed==null)throw new InvalidDataException("Career is empty or invalid");
                if (!contents.Contains("\"simulatedTrains\"")) parsed.simulatedTrains=true;
                if (!contents.Contains("\"surfaceTracks\"")) parsed.surfaceTracks=true;
                if (!contents.Contains("\"trainFleetLimit\"")) parsed.trainFleetLimit=4;
                if (!contents.Contains("\"trainServiceMode\"")) parsed.trainServiceMode=1;
                if (!contents.Contains("\"trainTimetableFallback\"")) parsed.trainTimetableFallback=true;
                parsed.trainServiceMode=Mathf.Clamp(parsed.trainServiceMode,0,2);parsed.trainFleetLimit=Mathf.Clamp(parsed.trainFleetLimit,1,8);
                if (!contents.Contains("\"engineRumble\"")) parsed.engineRumble=0;
                if (!contents.Contains("\"engineVolume\"")) parsed.engineVolume=.35f;
                var p=parsed;p.version=JsonUtility.FromJson<SchemaHeader>(contents).version;
                if(p.version>CurrentVersion)return Refuse(p,"This career uses a newer schema ("+p.version+"). Open it with a compatible build; this build will not save it.");
                if(p.version<CurrentVersion&&!File.Exists(PathName+(p.version<7?".pre-lap-economy-v7.bak":p.version<8?".pre-vehicle-packs-v8.bak":".pre-paint-sky-v9.bak")))
                {try{File.Copy(PathName,PathName+(p.version<7?".pre-lap-economy-v7.bak":p.version<8?".pre-vehicle-packs-v8.bak":".pre-paint-sky-v9.bak"));}catch(Exception backup){return Refuse(p,"Career retained, but migration backup failed. Startup and saving are blocked: "+backup.Message);}}
                if(p.version<2){p.clockMode=0;p.fixedHour=14;p.clockOffset=0;p.dayMinutes=24;p.weatherMode=0;p.region="";p.version=2;}
                if(p.garage==null)p.garage=GarageRules.CreateDefault();GarageRules.Validate(p.garage);if(p.version<5)p.livingScenery=true;p.version=9;p.clarity=Mathf.Clamp(p.clarity,0,2);
                if(p.records==null)p.records=new System.Collections.Generic.List<TrialRecord>();if(p.rewardedEvents==null)p.rewardedEvents=new System.Collections.Generic.List<string>();
                p.records.RemoveAll(r=>r==null||string.IsNullOrEmpty(r.key)||float.IsNaN(r.lap)||float.IsInfinity(r.lap)||r.lap<0||r.lap>86400||float.IsNaN(r.peak)||float.IsInfinity(r.peak)||r.peak<0||r.peak>200||r.sectors==null||r.sectors.Length!=3);if(p.records.Count>1000)p.records.RemoveRange(0,p.records.Count-1000);if(p.rewardedEvents.Count>256)p.rewardedEvents.RemoveRange(0,p.rewardedEvents.Count-256);
                if(p.modelLayoutBests==null)p.modelLayoutBests=new System.Collections.Generic.List<ModelLayoutLapRecord>();
                // Never infer a model PB from setup/conditions records. Preserve valid
                // schema7 values; refuse corrupted identity rather than overwrite it.
                if(p.modelLayoutBests.Count>1000)return Refuse(p,"Too many model/course records; career retained for review");
                var modelKeys=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach(var best in p.modelLayoutBests){if(best==null)throw new InvalidDataException("Missing model/course record");best.Read();if(!modelKeys.Add(best.key))throw new InvalidDataException("Duplicate model/course record");}
                p.trackMode=Mathf.Clamp(p.trackMode,0,3);p.raceLaps=Mathf.Clamp(p.raceLaps,1,5);p.raceDifficulty=Mathf.Clamp(p.raceDifficulty,0,2);
                p.credits=Mathf.Clamp(p.credits,0,10000000);p.quality=Mathf.Clamp(p.quality,0,2);
                p.engineVolume=Finite(p.engineVolume,.35f,0,1);
                if(p.displayWidth<640||p.displayHeight<360||p.displayWidth>16384||p.displayHeight>16384||(long)p.displayWidth*p.displayHeight>67108864){p.displayWidth=p.displayHeight=0;}p.displayMode=Mathf.Clamp(p.displayMode,0,1);
                p.volume=Finite(p.volume,.65f,0,1);p.rumble=Finite(p.rumble,.65f,0,1);p.engineRumble=Finite(p.engineRumble,0,0,1);p.wheelStrength=Finite(p.wheelStrength,.25f,0,.65f);
                p.bestLap=Finite(p.bestLap,0,0,86400);
                p.clockMode=Mathf.Clamp(p.clockMode,0,3);p.weatherMode=Mathf.Clamp(p.weatherMode,0,8);p.fixedHour=Finite(p.fixedHour,14,0,23.99f);p.clockOffset=Finite(p.clockOffset,0,-24,24);p.dayMinutes=Finite(p.dayMinutes,24,2,120);
                if(p.region!="dee-valley"&&p.region!="silverstone"&&p.region!="spa"&&!Packs.WorldPackCircuitReference.IsToken(p.region)&&!RegionCache.Safe(p.region))p.region="";
                if(string.IsNullOrEmpty(p.regionTimeZone))p.regionTimeZone="Europe/London";
                p.steerLeft=Finite(p.steerLeft,-1,-1,1);p.steerCentre=Finite(p.steerCentre,0,-1,1);p.steerRight=Finite(p.steerRight,1,-1,1);
                p.gasRest=Finite(p.gasRest,1,-1,1);p.gasFull=Finite(p.gasFull,-1,-1,1);p.brakeRest=Finite(p.brakeRest,1,-1,1);p.brakeFull=Finite(p.brakeFull,-1,-1,1);
                // Force must always be armed deliberately each session.
                p.wheelForceEnabled=false;return p;
            }
            catch(Exception e){return Refuse(parsed,"Career could not be loaded safely. Startup and saving are blocked: "+e.Message);}
        }
        public static bool SupportsSchema(string json){try{var header=JsonUtility.FromJson<SchemaHeader>(json);return header!=null&&header.version<=CurrentVersion;}catch{return false;}}
        static float Finite(float v,float fallback,float min,float max)=>float.IsNaN(v)||float.IsInfinity(v)?fallback:Mathf.Clamp(v,min,max);
        static readonly object saveGate=new object();
        long saveRevision;
        static void WriteSnapshot(string path,string json){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}
        public void QueueAutosave()
        {
            if(LoadBlocked||version>CurrentVersion)return;
            long revision=System.Threading.Interlocked.Increment(ref saveRevision);string path=PathName,json=JsonUtility.ToJson(this,true);
            _=System.Threading.Tasks.Task.Run(()=>{try{lock(saveGate){if(revision!=System.Threading.Volatile.Read(ref saveRevision))return;WriteSnapshot(path,json);}}catch(Exception error){Debug.LogWarning("Automatic progress save failed: "+error.Message);}});
        }
        public bool Save()
        {
            using var telemetry=WorkTelemetry.Measure("career-save");
            if(LoadBlocked||version>CurrentVersion){Debug.LogWarning("Career was not saved: "+LoadIssue);return false;}
            try{System.Threading.Interlocked.Increment(ref saveRevision);string path=PathName,json=JsonUtility.ToJson(this,true);lock(saveGate)WriteSnapshot(path,json);return true;}
            catch(Exception e){Debug.LogWarning("Progress could not be saved: "+e.Message);return false;}
        }
    }
}
