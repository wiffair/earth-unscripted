using System;
using System.IO;
using UnityEngine;
namespace RoadRush
{
    // Explicit native diagnostics only. Fixtures are prepared externally, not as
    // unmeasured pre-boot world preparation inside this component.
    [DefaultExecutionOrder(-32000)]public sealed class BootTimingNativeProof:MonoBehaviour
    {
        static string prefix,error,realPath,realHash;bool done,switched;int errors,readyFrame=-1,lastFrame=-1;double last,deadline,installed;bool screenshots;string next;
        [Serializable]sealed class Report{public string status,error,realCareerBefore,realCareerAfter;public int runtimeErrors,profileQuality;public BootTelemetry.Bundle timing;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]static void Isolate()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushBootTiming");if(at<0)return;
            // Isolate BEFORE parsing/validating the fixture. Bad arguments cannot
            // fall through to a real career. Only explicit owned prefix files used.
            string candidate=at+1<args.Length?args[at+1]:Path.Combine(Path.GetTempPath(),"road-rush-invalid-boot-timing");
            PlayerProfile.StorageOverride=Path.Combine(Path.GetTempPath(),"road-rush-invalid-boot-timing-profile.json");RegionCache.OverrideFolder=PlayerProfile.StorageOverride+".regions";
            try{prefix=Path.GetFullPath(candidate);PlayerProfile.StorageOverride=prefix+"-profile.json";RegionCache.OverrideFolder=prefix+"-regions";}catch(Exception e){prefix=Path.Combine(Path.GetTempPath(),"road-rush-invalid-boot-timing");error="Invalid owned prefix: "+e.Message;}
            realPath=Path.Combine(Application.persistentDataPath,"road-rush-save.json");realHash=File.Exists(realPath)?RoadRush.Core.DataPackageRules.HashFile(realPath):"missing";
            if(at+1>=args.Length||!File.Exists(PlayerProfile.StoragePath))error="Boot timing requires its externally prepared isolated profile";
            BootTelemetry.EnableIsolated();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Install()
        {if(prefix==null)return;var go=new GameObject("Isolated boot timing");DontDestroyOnLoad(go);go.AddComponent<BootTimingNativeProof>();}
        void Awake(){Application.runInBackground=true;installed=BootTelemetry.Now;deadline=Time.realtimeSinceStartupAsDouble+660;var args=Environment.GetCommandLineArgs();screenshots=Array.IndexOf(args,"-roadRushBootTimingScreenshot")>=0;int at=Array.IndexOf(args,"-roadRushBootTimingNext");if(at>=0&&at+1<args.Length)next=args[at+1];Application.logMessageReceived+=Log;}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors++;}
        void Update()
        {
            if(done)return;
            if(lastFrame!=Time.frameCount){if(lastFrame>=0)BootTelemetry.Heartbeat(last,lastFrame);else BootTelemetry.FirstFrame(installed);last=BootTelemetry.Now;lastFrame=Time.frameCount;}
            if(error!=null){Finish(error);return;}
            if(Time.realtimeSinceStartupAsDouble>deadline){Finish("Native boot timing timed out");return;}
            var session=FindAnyObjectByType<GameSession>();if(!session)return;
            if(session.BootFailed){Finish(session.BootStatus);return;}if(!session.BootReady){readyFrame=-1;return;}
            if(session.Updates.SourceRequests!=0||session.Conditions.WeatherRequests!=0){Finish("Unexpected source/weather request");return;}
            // Include several updates AFTER ready: the final actor-creation stall
            // must appear in the frame-start interval series, not vanish at ready.
            if(readyFrame<0){readyFrame=Time.frameCount;if(screenshots){BootTelemetry.Scene("diagnostic.screenshot.request",prefix);ScreenCapture.CaptureScreenshot(prefix+(switched?"-next-ready.png":"-ready.png"));}return;}
            if(Time.frameCount-readyFrame<5)return;
            if(!switched&&!string.IsNullOrEmpty(next)){if(!session.SelectRegion(next)){Finish("Second location request refused");return;}switched=true;readyFrame=-1;return;}
            Finish(null);
        }
        void Finish(string failure)
        {
            if(done)return;done=true;string after=File.Exists(realPath)?RoadRush.Core.DataPackageRules.HashFile(realPath):"missing";
            if(after!=realHash)failure=(failure??"")+" protected real career changed";
            if(errors!=0)failure=(failure??"")+" runtime errors="+errors;
            var active=FindAnyObjectByType<GameSession>();var report=new Report{profileQuality=active&&active.Profile!=null?active.Profile.quality:-1,status=failure==null?"FUNCTIONAL_TIMING_CAPTURED":"FAIL",error=failure,realCareerBefore=realHash,realCareerAfter=after,runtimeErrors=errors,timing=BootTelemetry.Capture()};
            try{Directory.CreateDirectory(Path.GetDirectoryName(prefix));File.WriteAllText(prefix+"-boot-timing.json",JsonUtility.ToJson(report,true));}catch(Exception e){Debug.LogError("Boot timing report could not be saved: "+e.Message);failure="Timing output failed";}
            Application.Quit(failure==null?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
