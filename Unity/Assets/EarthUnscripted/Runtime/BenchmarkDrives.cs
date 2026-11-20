using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted
{
    // Self-contained paired benchmarks. No owner cache or career is deleted or restored.
    public static class BenchmarkDrives
    {
        static readonly string[] commandArgs=Environment.GetCommandLineArgs();static bool? configuredTraffic;
        static bool completed;static FileStream cacheLease;static bool idleSleepAssertionAttempted;static string configuredCache;public static double HandoffWaitSeconds {get;private set;}
        public static string Status="Choose a drive. Each pair runs unattended, then saves a ZIP comparison. Close the test window to abort a run; incomplete pairs are not performance passes.";
        static string Arg(string key,int offset=1){var a=commandArgs;int i=Array.IndexOf(a,key);return i>=0&&i+offset<a.Length?a[i+offset]:null;}
        public static bool Endurance=>Active&&((string)JObject.Parse(File.ReadAllText(Path.Combine(Folder(Arg("-earthUnscriptedBenchmark")),"config.json")))["scenario"])=="m4-m32";
        public static bool Active=>Arg("-earthUnscriptedBenchmark")!=null;
        public static bool LiveTraffic
        {get{if(!Active)return true;if(!configuredTraffic.HasValue)configuredTraffic=(bool?)JObject.Parse(File.ReadAllText(Path.Combine(Folder(Arg("-earthUnscriptedBenchmark")),"config.json")))["liveTraffic"]??false;return configuredTraffic.Value;}}
        public static string CacheDirectory
        {get{if(configuredCache!=null)return configuredCache;string value=(string)JObject.Parse(File.ReadAllText(Path.Combine(Folder(Arg("-earthUnscriptedBenchmark")),"config.json")))["cache"];if(!IsIsolatedCache(value))throw new IOException("Invalid benchmark cache");return configuredCache=value;}}
        static string Reports=>Path.Combine(SinglePlayerBeta.ProductRoot,"Diagnostics","Benchmarks");
        static string Folder(string id){if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid benchmark identifier");return Path.Combine(Reports,id);}
        public static bool IsIsolatedCache(string path)=>Path.GetDirectoryName(Path.GetFullPath(path))==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(path).StartsWith("EarthUnscriptedBetaWorld-",StringComparison.Ordinal)&&Guid.TryParseExact(Path.GetFileName(path).Substring("EarthUnscriptedBetaWorld-".Length),"N",out _);
        public static bool ValidScenario(string id)=>id=="dense-city"||id=="open-road"||id=="tunnel-bridge"||id=="m4-m32";
        public static void OpenReports(){Directory.CreateDirectory(Reports);Application.OpenURL(new Uri(Reports).AbsoluteUri);}
        public static void Launch(GameSession session,string scenario)
        {
            try
            {
                if(!ValidScenario(scenario))throw new ArgumentException("Unknown benchmark");
                if(!session.TrySave())throw new IOException("Could not save career before benchmark");
                string id=Guid.NewGuid().ToString("N"),folder=Folder(id),cache=Path.Combine(Path.GetTempPath(),"EarthUnscriptedBetaWorld-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);Directory.CreateDirectory(cache);
                var seed=Resources.Load<TextAsset>(scenario=="m4-m32"?"Benchmarks/M4M32Start":scenario=="open-road"?"AutomaticWorld/BetaStart":"Benchmarks/NewYork");if(!seed)throw new IOException("Benchmark seed unavailable");
                File.WriteAllText(Path.Combine(folder,"seed.json"),seed.text);
                var config=new JObject{["schema"]=1,["scenario"]=scenario,["cache"]=cache,["liveTraffic"]=session.Profile.benchmarkLiveTraffic,["detailedCapture"]=session.Profile.detailedPerformanceCapture,["speedPlan"]=scenario=="m4-m32"?"M4 100 mph; M32 70 mph; posted limits ignored in isolated benchmark":"Mapped limits apply",["worldDate"]="2026-06-21",["quality"]=2,["width"]=1280,["height"]=800,["targetMph"]=scenario=="m4-m32"?100:scenario=="dense-city"?45:70,["targetMetres"]=scenario=="m4-m32"?BenchmarkEndurance.RouteLength:1000,["seedSha256"]=DataPackageRules.HashFile(Path.Combine(folder,"seed.json")),["createdUtc"]=DateTime.UtcNow.ToString("O")};
                File.WriteAllText(Path.Combine(folder,"config.json"),config.ToString());
                Spawn(id,"cold",config);Application.Quit();
            }
            catch(Exception e){Status="Benchmark could not start: "+e.Message;}
        }
        public static void WaitForPreviousPlayer()
        {
            if(!Active)return;var clock=Stopwatch.StartNew();string root=CacheDirectory;Directory.CreateDirectory(root);
            // Keep this non-inheritable handle until process teardown, after all
            // world cleanup. A zombie PID has closed its handles, so cannot hold
            // the next phase hostage. Never kill or inspect another process.
            while(cacheLease==null)
            {
                try
                {
                    var candidate=new FileStream(Path.Combine(root,".benchmark-player.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                    try{candidate.Lock(0,1);cacheLease=candidate;}catch{candidate.Dispose();throw;}
                }
                catch(IOException){if(clock.Elapsed.TotalSeconds>30)throw new IOException("Previous benchmark still owns the test cache");System.Threading.Thread.Sleep(100);}
            }
            HandoffWaitSeconds=clock.Elapsed.TotalSeconds;
            // Keep an unattended Mac benchmark from becoming an idle-sleep test.
            // This does not unlock the Mac or alter the user's security settings.
            if(!idleSleepAssertionAttempted&&Application.platform==RuntimePlatform.OSXPlayer)
            {
                idleSleepAssertionAttempted=true;
                try{var awake=Process.Start(new ProcessStartInfo("/usr/bin/caffeinate","-di -w "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true});awake?.Dispose();}
                catch(Exception e){UnityEngine.Debug.LogWarning("Benchmark idle-sleep assertion unavailable: "+e.Message);}
            }
        }
        public static void StartupFailure(Exception error)
        {
            if(!Active)return;
            try
            {
                string id=Arg("-earthUnscriptedBenchmark"),phase=Arg("-earthUnscriptedBenchmark",2),folder=Folder(id);if(phase!="cold"&&phase!="warm")return;
                Directory.CreateDirectory(folder);var failure=new JObject{["complete"]=false,["phase"]=phase,["state"]="startup-failed",["error"]=BetaDiagnostics.Redact(error.Message),["version"]=ProductIdentity.Version};
                File.WriteAllText(Path.Combine(folder,phase+"-state.json"),failure.ToString());File.WriteAllText(Path.Combine(folder,"comparison.json"),failure.ToString());
                string zipPath=Path.Combine(Reports,id+".zip");if(!File.Exists(zipPath))using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(folder,"comparison.json"),"comparison.json");completed=true;
            }catch(IOException){}catch(ArgumentException){}
        }
        public static void InstallAbortHandler()
        {
            if(!Active)return;
            Application.quitting+=()=>
            {
                if(completed)return;
                try{string id=Arg("-earthUnscriptedBenchmark"),folder=Folder(id),phase=Arg("-earthUnscriptedBenchmark",2);if(phase!="cold"&&phase!="warm")return;
                    File.WriteAllText(Path.Combine(folder,phase+"-state.json"),new JObject{["state"]="aborted",["reason"]="Player closed before the drive completed"}.ToString());
                    File.WriteAllText(Path.Combine(folder,"comparison.json"),new JObject{["complete"]=false,["reason"]="Benchmark interrupted"}.ToString());
                    string zipPath=Path.Combine(Reports,id+".zip");if(!File.Exists(zipPath))using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(folder,"comparison.json"),"comparison.json");
                }catch(IOException){}catch(ArgumentException){}
            };
        }
        public static bool FocusQualified(JObject timing)
        {
            double focused=(double?)timing["focusedSeconds"]??0,unfocused=(double?)timing["unfocusedSeconds"]??double.PositiveInfinity;
            return !double.IsNaN(focused)&&!double.IsInfinity(focused)&&!double.IsNaN(unfocused)&&!double.IsInfinity(unfocused)&&focused>0&&unfocused>=0&&unfocused<=Math.Max(1,(focused+unfocused)*.01);
        }
        static JObject Summary(JObject timing)
        {
            var bins=timing["frameHistogramMilliseconds"] as JArray;long n=(long?)timing["measuredFrames"]??0;double seconds=(double?)timing["drivingWallSeconds"]??0;
            int Percentile(double fraction){long count=0;if(n==0||bins==null)return -1;for(int i=0;i<bins.Count;i++){count+=(long)bins[i];if(count>=n*fraction)return i;}return -1;}
            return new JObject{["startupSeconds"]=((double?)timing["firstDrivingRealtimeSeconds"]??0)>0?Math.Max(0,(double)timing["firstDrivingRealtimeSeconds"]-HandoffWaitSeconds):-1,["drivingSeconds"]=seconds,["averageFrameRate"]=seconds>0?n/seconds:-1,["p50FrameMsBin"]=Percentile(.5),["p95FrameMsBin"]=Percentile(.95),["p99FrameMsBin"]=Percentile(.99),["worstFrameMs"]=timing["worstFrameMilliseconds"],["roadReadinessCapSeconds"]=timing["roadReadinessCapSeconds"],["roadReadinessStopSeconds"]=timing["roadReadinessStopSeconds"],["streamingBrakeSeconds"]=timing["streamingBrakeSeconds"],["finishedGeometryHits"]=timing["finishedCacheHits"],["focusedSeconds"]=timing["focusedSeconds"],["unfocusedSeconds"]=timing["unfocusedSeconds"],["focusQualified"]=FocusQualified(timing),["definition"]="Percentiles use whole-drive 1ms histogram bins; final bin >=1000ms. Startup begins after predecessor handoff. Instrumentation overhead and traffic variation remain."};
        }
        static string Quote(string s)=>"\""+s.Replace("\"","\\\"")+"\"";
        static void Spawn(string id,string phase,JObject config)
        {
            string folder=Folder(id),token=Guid.NewGuid().ToString("N"),shared=Guid.NewGuid().ToString("N");
            string profile=Path.Combine(Path.GetTempPath(),"EarthUnscriptedSharedProfile-"+shared,"BetaData");Directory.CreateDirectory(profile);
            var seed=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(Path.Combine(folder,"seed.json")));
            var p=new PlayerProfile{region=seed.id,regionTimeZone=seed.timeZone,quality=2,clarity=0,weatherMode=0,clockMode=0,fixedHour=14,livingScenery=true,detailedPerformanceCapture=(bool?)config["detailedCapture"]??true,performanceStats=true,worldDate="2026-06-21",volume=0,rumble=0};
            if((string)config["scenario"]=="tunnel-bridge"){p.customStart=true;p.startRegion=seed.id;p.startX=-826;p.startZ=-1180;}
            File.WriteAllText(Path.Combine(profile,"beta-save.json"),JsonUtility.ToJson(p));
            string executable=Process.GetCurrentProcess().MainModule.FileName;
            File.WriteAllText(Path.Combine(folder,phase+"-smoke-token.txt"),token);
            var args=new[]{"-earthUnscriptedBetaSmoke",token,"-earthUnscriptedSharedProfile",shared,"-earthUnscriptedBetaCoastSeed",Path.Combine(folder,"seed.json"),"-earthUnscriptedInternationalCache",(string)config["cache"],"-earthUnscriptedBenchmark",id,phase,"-earthUnscriptedBetaDriveMetres",config["targetMetres"].ToString(),"-earthUnscriptedBetaDriveSpeedMph",config["targetMph"].ToString(),((bool?)config["detailedCapture"]??true)?"-earthUnscriptedDetailedTiming":"-earthUnscriptedLightweightTiming","-screen-fullscreen","0","-screen-width","1280","-screen-height","800","-logFile",Path.Combine(folder,phase+"-player.log")};
            if((string)config["scenario"]=="tunnel-bridge")args=args.Concat(new[]{"-earthUnscriptedBetaSmokeStart","-826","-1180"}).ToArray();
            File.WriteAllText(Path.Combine(folder,phase+"-state.json"),new JObject{["state"]="started",["cacheBytesBefore"]=Bytes((string)config["cache"]),["utc"]=DateTime.UtcNow.ToString("O")}.ToString());
            var process=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,Arguments=string.Join(" ",args.Select(Quote))});if(process==null)throw new IOException("Could not start benchmark player");for(int activation=0;activation<20&&!MacDrivingActivity.YieldBenchmarkActivation(process.Id);activation++)System.Threading.Thread.Sleep(50);process.Dispose();
        }
        static long Bytes(string root)=>Directory.Exists(root)?new DirectoryInfo(root).EnumerateFiles("*",SearchOption.AllDirectories).Sum(f=>f.Length):0;
        static string DriverInfo()
        {
            try
            {
                if(Application.platform==RuntimePlatform.OSXPlayer)return "Apple GPU drivers are supplied with macOS; OS build: "+Run("/usr/bin/sw_vers","-buildVersion");
                if(Application.platform==RuntimePlatform.WindowsPlayer)return Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate | ConvertTo-Json -Compress\"");
                return "Unavailable through this platform's runtime API";
            }
            catch(Exception){return "Unavailable";}
        }
        static string Run(string file,string args)
        {
            using(var p=Process.Start(new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}))
            {if(!p.WaitForExit(3000)){p.Kill();return "Unavailable (query timeout)";}return p.ExitCode==0?p.StandardOutput.ReadToEnd().Trim():"Unavailable";}
        }
        public static JObject MachineInfo(string cacheRoot)
        {
            string diskFormat="Unavailable",diskType="Unavailable";long free=-1,total=-1;
            try{var d=new DriveInfo(Path.GetPathRoot(cacheRoot));free=d.AvailableFreeSpace;total=d.TotalSize;diskFormat=d.DriveFormat;diskType=d.DriveType.ToString();}catch(IOException){}catch(UnauthorizedAccessException){}
            return new JObject{["processor"]=SystemInfo.processorType,["logicalProcessors"]=SystemInfo.processorCount,["processorFrequencyMHz"]=SystemInfo.processorFrequency,["ramMiB"]=SystemInfo.systemMemorySize,["graphics"]=SystemInfo.graphicsDeviceName,["graphicsVendor"]=SystemInfo.graphicsDeviceVendor,["graphicsMemoryMiB"]=SystemInfo.graphicsMemorySize,["graphicsApi"]=SystemInfo.graphicsDeviceVersion,["graphicsDrivers"]=DriverInfo(),["os"]=SystemInfo.operatingSystem,["unity"]=Application.unityVersion,["width"]=Screen.width,["height"]=Screen.height,["unityQualityLevel"]=QualitySettings.GetQualityLevel(),["vSync"]=QualitySettings.vSyncCount,["frameCap"]=Application.targetFrameRate,["renderInterval"]=UnityEngine.Rendering.OnDemandRendering.renderFrameInterval,["cacheVolumeFreeBytes"]=free,["cacheVolumeTotalBytes"]=total,["cacheVolumeFormat"]=diskFormat,["cacheVolumeType"]=diskType,["diskModelAndSpeed"]="Not measured; no storage serial numbers collected"};
        }
        public static void Complete(GameSession session,bool passed,string error)
        {
            if(!Active)return;
            try
            {
                double measurementEnd=Time.realtimeSinceStartupAsDouble;
                string id=Arg("-earthUnscriptedBenchmark"),phase=Arg("-earthUnscriptedBenchmark",2),folder=Folder(id);if(phase!="cold"&&phase!="warm")throw new ArgumentException("Invalid benchmark phase");
                var config=JObject.Parse(File.ReadAllText(Path.Combine(folder,"config.json")));string cache=(string)config["cache"];if(!IsIsolatedCache(cache))throw new IOException("Invalid benchmark cache");
                var telemetry=UnityEngine.Object.FindFirstObjectByType<BetaDrivingTelemetry>();string timing=telemetry?telemetry.ExportSnapshot():"{}";
                var result=JObject.Parse(File.ReadAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json")));
                var route=BenchmarkEndurance.Active?BenchmarkEndurance.Guide:session?.World?.Route;string routeHash=route!=null?Hash(JsonUtility.ToJson(new RoutePoints{points=route.Points})):"unavailable";
                var report=new JObject{["schema"]=1,["scenario"]=config["scenario"],["phase"]=phase,["passed"]=passed,["error"]=error,["version"]=ProductIdentity.Version,["graphicsMode"]=GameGraphics.Name,["machine"]=MachineInfo(cache),["settings"]=new JObject{["quality"]=session?.Profile?.quality,["weather"]="dry fixed afternoon",["requestedMph"]=config["targetMph"],["requestedMetres"]=config["targetMetres"],["width"]=Screen.width,["height"]=Screen.height,["vSync"]=QualitySettings.vSyncCount,["frameCap"]=Application.targetFrameRate,["renderInterval"]=UnityEngine.Rendering.OnDemandRendering.renderFrameInterval},["seedSha256"]=config["seedSha256"],["routeSha256"]=routeHash,["datasetVersion"]=route?.Region?.datasetVersion,["elapsedSinceLaunchSeconds"]=measurementEnd,["predecessorWaitSeconds"]=HandoffWaitSeconds,["sharedGameSource"]=JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"SharedGameSource.json"))),["cacheBytesAfter"]=Bytes(cache),["cacheBefore"]=JObject.Parse(File.ReadAllText(Path.Combine(folder,phase+"-state.json"))),["bootTiming"]=BootTelemetry.Enabled?JObject.Parse(JsonUtility.ToJson(BootTelemetry.Capture())):new JObject{["enabled"]=false,["reason"]="Lightweight baseline; enable detailed diagnostics for startup spans"},["summary"]=Summary(JObject.Parse(timing)),["timing"]=JObject.Parse(timing),["drive"]=result,["limitations"]="World-cache cold/warm only. OS, network proxy and graphics-driver caches are not cleared. Live traffic is not a deterministic replay. Do not compare runs with different route hashes/settings/builds as hardware-only differences."};
                report["settings"]["startStationMetres"]=result["requestedStartStationMetres"]??new JValue(0);
                report["settings"]["speedPlan"]=config["speedPlan"];report["settings"]["liveTraffic"]=LiveTraffic;report["settings"]["worldDate"]="2026-06-21";report["settings"]["detailedCapture"]=JObject.Parse(timing)["detailedCapture"];
                result.Remove("profilePath");result.Remove("careerFolder");result.Remove("dataRoot");result.Remove("persistentDataPath");
                report["requestedDistanceCompleted"]=(float?)result["physicalDistanceMetres"]>=((float?)config["targetMetres"]??1000)-((string)config["scenario"]=="m4-m32"?100:50);
                File.WriteAllText(Path.Combine(folder,phase+"-report.json"),BetaDiagnostics.Redact(report.ToString()));
                File.WriteAllText(Path.Combine(folder,phase+"-state.json"),new JObject{["state"]=passed?"completed":"failed",["error"]=error}.ToString());
                if(phase=="cold"){Spawn(id,"warm",config);completed=true;return;}
                bool fullDistance=(float?)result["physicalDistanceMetres"]>=((float?)config["targetMetres"]??1000)-((string)config["scenario"]=="m4-m32"?100:50);report["requestedDistanceCompleted"]=fullDistance;
                var comparison=new JObject{["scenario"]=config["scenario"],["complete"]=phase=="warm"&&File.Exists(Path.Combine(folder,"cold-report.json")),["routeComparable"]=false};
                if(File.Exists(Path.Combine(folder,"cold-report.json"))&&phase=="warm")
                {var cold=JObject.Parse(File.ReadAllText(Path.Combine(folder,"cold-report.json")));comparison["bothDrivesPassed"]=(bool?)cold["passed"]==true&&passed&&(bool?)cold["requestedDistanceCompleted"]==true&&fullDistance;comparison["routeComparable"]=routeHash!="unavailable"&&(string)cold["routeSha256"]==routeHash;comparison["settingsComparable"]=JToken.DeepEquals(cold["settings"],report["settings"])&&(string)cold["version"]==ProductIdentity.Version&&(string)cold["graphicsMode"]==GameGraphics.Name&&(string)cold["sharedGameSource"]?["source"]==(string)report["sharedGameSource"]?["source"];comparison["focusComparable"]=FocusQualified((JObject)cold["timing"])&&FocusQualified((JObject)report["timing"]);comparison["comparable"]=(bool)comparison["bothDrivesPassed"]&&(bool)comparison["routeComparable"]&&(bool)comparison["settingsComparable"]&&(bool)comparison["focusComparable"];comparison["coldSummary"]=cold["summary"];comparison["warmSummary"]=report["summary"];comparison["coldStartupAndDriveSeconds"]=cold["elapsedSinceLaunchSeconds"];comparison["warmStartupAndDriveSeconds"]=report["elapsedSinceLaunchSeconds"];}
                completed=true;
                File.WriteAllText(Path.Combine(folder,"comparison.json"),comparison.ToString());
                using(var zip=ZipFile.Open(Path.Combine(Reports,id+".zip"),ZipArchiveMode.Create))foreach(string file in new[]{"cold-report.json","warm-report.json","comparison.json"})if(File.Exists(Path.Combine(folder,file)))zip.CreateEntryFromFile(Path.Combine(folder,file),file);
            }
            catch(Exception e){UnityEngine.Debug.LogError("Benchmark report failed: "+e.Message);}
        }
        [Serializable] sealed class RoutePoints{public Vector3[] points;}
        static string Hash(string value){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
    }
}
