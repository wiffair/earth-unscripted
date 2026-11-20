#if DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace EarthUnscripted
{
    // Explicit isolated diagnostics only. No component, polling or profiler work
    // exists in the normal player or an ordinary Development career.
    sealed class BetaProfilerWindow : MonoBehaviour
    {
        GameSession session;double delay,duration,drivingSeconds,startedAt;
        bool recording,finished;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-earthProfilerDriveWindow");
            if(at<0||!BenchmarkDrives.Active||SinglePlayerBeta.VerificationOutput==null)return;
            int benchmark=Array.IndexOf(args,"-earthUnscriptedBenchmark");
            if(at+2>=args.Length||benchmark<0||benchmark+1>=args.Length||!Guid.TryParseExact(args[benchmark+1],"N",out _))return;
            if(!double.TryParse(args[at+1],NumberStyles.Float,CultureInfo.InvariantCulture,out double delay)||
               !double.TryParse(args[at+2],NumberStyles.Float,CultureInfo.InvariantCulture,out double duration)||
               double.IsNaN(delay)||double.IsInfinity(delay)||double.IsNaN(duration)||double.IsInfinity(duration)||delay<0||delay>7200||duration<5||duration>120)return;
            var host=new GameObject("Isolated diagnostic profiler window");var capture=host.AddComponent<BetaProfilerWindow>();
            capture.delay=delay;capture.duration=duration;capture.output=Path.Combine(SinglePlayerBeta.ProductRoot,"Diagnostics","Benchmarks",args[benchmark+1],"cpu-window.raw");
            // Keep pre-existing evidence intact, including an accidental retry.
            if(File.Exists(capture.output)){capture.finished=true;Destroy(host);return;}
        }
        void LateUpdate()
        {
            if(finished)return;if(!session)session=FindFirstObjectByType<GameSession>();
            if(!session||!session.BootReady||session.Menu||session.PreparingStart)return;
            drivingSeconds+=Time.unscaledDeltaTime;
            if(!recording)
            {
                if(drivingSeconds<delay)return;
                Profiler.enableAllocationCallstacks=false;
                Profiler.maxUsedMemory=128*1024*1024;
                Profiler.SetAreaEnabled(ProfilerArea.Memory,true);
                Profiler.logFile=output;Profiler.enableBinaryLog=true;Profiler.enabled=true;
                recording=true;startedAt=Time.realtimeSinceStartupAsDouble;return;
            }
            double captured=Time.realtimeSinceStartupAsDouble-startedAt;
            // Close just after a real driving hitch so the offline reader's
            // bounded last-frame history retains its cause, rather than shutdown.
            if(captured>=duration||(captured>=5&&Time.unscaledDeltaTime>.1f))Finish("window-or-hitch");
        }
        void Finish(string reason)
        {
            if(!recording||finished)return;finished=true;recording=false;
            double frameMilliseconds=Time.unscaledDeltaTime*1000d,seconds=Time.realtimeSinceStartupAsDouble-startedAt;
            Profiler.enabled=false;Profiler.logFile="";
            File.WriteAllText(Path.ChangeExtension(output,"json"),"{\"reason\":\""+reason+"\",\"delayDrivingSeconds\":"+delay.ToString(CultureInfo.InvariantCulture)+",\"capturedSeconds\":"+seconds.ToString(CultureInfo.InvariantCulture)+",\"lastDrivingFrameMilliseconds\":"+frameMilliseconds.ToString(CultureInfo.InvariantCulture)+"}");
        }
        void OnApplicationQuit(){Finish("quit-before-window-ended");}
    }
}
#endif
