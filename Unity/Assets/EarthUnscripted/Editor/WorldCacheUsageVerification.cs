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
