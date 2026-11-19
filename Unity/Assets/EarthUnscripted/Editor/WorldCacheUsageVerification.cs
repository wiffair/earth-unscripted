using System;
using System.IO;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class WorldCacheUsageVerification
    {
        public static void Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"EarthUnscriptedCacheUsage-"+Guid.NewGuid().ToString("N"));int checks=0;
            void Need(bool ok,string why){checks++;if(!ok)throw new Exception("CACHE USAGE "+why);}
            try
            {
                Need(BenchmarkDrives.ValidScenario("dense-city")&&BenchmarkDrives.ValidScenario("open-road")&&BenchmarkDrives.ValidScenario("tunnel-bridge")&&BenchmarkDrives.ValidScenario("m4-m32")&&!BenchmarkDrives.ValidScenario("../escape"),"only fixed benchmark scenarios");
                Need(BenchmarkEndurance.RouteLength>32000&&BenchmarkEndurance.RouteLength<34000&&BenchmarkEndurance.TargetMph(0)==100&&BenchmarkEndurance.TargetMph(BenchmarkEndurance.RouteLength-50)==70,"long route and explicit motorway speed stages");
                var seed=Resources.Load<TextAsset>("Benchmarks/M4M32Start");var source=WorldSnapshotCodec.DecodeBootSource(seed.text,out _);Need(AutomaticWorldHost.Eligible(source)&&source.route.Length>=2,"bounded M4 boot seed is valid automatic-world input");
                Need(!BenchmarkDrives.IsIsolatedCache(root)&&BenchmarkDrives.IsIsolatedCache(Path.Combine(Path.GetTempPath(),"EarthUnscriptedBetaWorld-"+Guid.NewGuid().ToString("N"))),"benchmark cache cannot target owner folders");
                var worldRoot=new GameObject("Actual corridor source-preemption fixture");
                try
                {
                    var renderer=worldRoot.AddComponent<NewbridgePrototype.NewbridgeWorld>();renderer.enabled=false;
                    var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=renderer.GetType();
                    type.GetField("pendingArea",hidden).SetValue(renderer,new Vector2Int(5,-2));
                    var candidateType=type.GetNestedType("Candidate",System.Reflection.BindingFlags.NonPublic);var candidate=Activator.CreateInstance(candidateType,true);
                    candidateType.GetField("tileX").SetValue(candidate,5);candidateType.GetField("tileZ").SetValue(candidate,-1);
                    bool Yield()=> (bool)type.GetMethod("ShouldYieldDataRequest",hidden).Invoke(renderer,new[]{candidate});
                    type.GetField("immediateDrivingCorridor",hidden).SetValue(renderer,new[]{new Vector3(7900,0,-1600)});
                    Need(!Yield(),"nearer retry does not cancel useful work farther along actual driving corridor");
                    type.GetField("immediateDrivingCorridor",hidden).SetValue(renderer,new[]{new Vector3(6100,0,-100)});
                    Need(Yield(),"off-corridor source work can still yield to actual driving source");
                    candidateType.GetField("tileZ").SetValue(candidate,-2);Need(!Yield(),"same source tile never cancels itself");
                }
                finally{UnityEngine.Object.DestroyImmediate(worldRoot);}
                WorkTelemetry.Clear();WorkTelemetry.SetRuntimeEnabled(true);
                using(WorkTelemetry.Measure("verification.runtime-toggle")){}
                Need(WorkTelemetry.Capture().Length==1,"runtime capture can enable without isolated profile");
                WorkTelemetry.SetRuntimeEnabled(false);Need(WorkTelemetry.Capture().Length==0,"disabling clears buffered work");
                using(WorkTelemetry.Measure("verification.disabled")){}
                Need(WorkTelemetry.Capture().Length==0,"disabled normal play records no scope");
                var profile=JsonUtility.FromJson<PlayerProfile>("{\"detailedPerformanceCapture\":true}");
                Need(profile.detailedPerformanceCapture&&JsonUtility.ToJson(profile).Contains("\"detailedPerformanceCapture\":true"),"capture preference round trips");
                foreach(string name in new[]{"Prepared","FinishedGeometry","Raw","FarTerrain"})Directory.CreateDirectory(Path.Combine(root,name));
                File.WriteAllBytes(Path.Combine(root,"Prepared","a.json"),new byte[12]);File.WriteAllBytes(Path.Combine(root,"FinishedGeometry","a.eugeo"),new byte[20]);File.WriteAllBytes(Path.Combine(root,"Raw","a.tif"),new byte[30]);
                var snapshot=WorldCacheUsage.Measure(root);Need(snapshot.prepared==12&&snapshot.finished==20&&snapshot.sources==30,"distinct file inventories");Need(snapshot.sourceLimit==5&&snapshot.preparedLimit==5&&snapshot.finishedLimit==5,"all defaults 5GB");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"{\"preparedGigabytes\":2,\"finishedGigabytes\":0,\"sourceGigabytes\":8}");snapshot=WorldCacheUsage.Measure(root);Need(snapshot.preparedLimit==2&&snapshot.finishedLimit==0&&snapshot.sourceLimit==8,"independent configured quotas");Need(snapshot.Summary.Contains("sources")&&snapshot.Summary.Contains("/8"),"stats expose sources and quota");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"{\"preparedGigabytes\":3}");snapshot=WorldCacheUsage.Measure(root);Need(snapshot.sourceLimit==5,"old settings inherit source default");
                File.WriteAllText(Path.Combine(root,"storage-settings.json"),"invalid json");Need(WorldCacheUsage.Measure(root).sourceLimit==5,"bad settings safe default");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
            Debug.Log("WORLD_CACHE_USAGE_PASS "+checks);
        }
    }
}
