using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class Beta22MenuVerification
    {
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception("Beta22 menu: "+label);}
            var redact=typeof(BetaDiagnostics).GetMethod("Redact",BindingFlags.Static|BindingFlags.NonPublic);
            string clean=(string)redact.Invoke(null,new object[]{"Authorization: Bearer sensitive-value\napi_key=other-secret URL https://user:pass@example.com/map?token=private /Users/"+"alice/Private/file.txt"});
            Check(!clean.Contains("sensitive-value")&&!clean.Contains("other-secret")&&!clean.Contains("user:pass")&&!clean.Contains("token=private")&&!clean.Contains("alice"),"credentials, URL query and private paths redacted");
            Check(clean.Contains("example.com/map"),"useful provider hostname/path retained");
            var dataRoot=typeof(SinglePlayerBeta).GetProperty("DataRoot",BindingFlags.Static|BindingFlags.Public);
            var data=typeof(BetaPlaceHistory).GetField("data",BindingFlags.Static|BindingFlags.NonPublic);
            string old=(string)dataRoot.GetValue(null),directory=Path.Combine(Path.GetTempPath(),"EarthUnscriptedMenuFixture-"+Guid.NewGuid().ToString("N"));
            try
            {
                dataRoot.SetValue(null,directory);data.SetValue(null,null);
                var place=new WorldPlace{name="Hamburg",country="Germany",country_code="DE",timezone="Europe/Berlin",latitude=53.5511,longitude=9.9937};
                BetaPlaceHistory.Remember(place);BetaPlaceHistory.Remember(place);
                Check(BetaPlaceHistory.Recent.Length==1,"recent selection deduplicated");
                Check(BetaPlaceHistory.ToggleFavourite(place)&&BetaPlaceHistory.IsFavourite(place),"favourite saved");
                BetaPlaceHistory.RememberSearch("Hamburg",new[]{place});
                place.name="MUTATED";data.SetValue(null,null);
                Check(BetaPlaceHistory.Recent[0].name=="Hamburg"&&BetaPlaceHistory.Favourites[0].name=="Hamburg","recent/favourite survive process-style reload and caller mutation");
                Check(BetaPlaceHistory.CachedSearch(" hamburg ",out var result)&&result[0].name=="Hamburg","offline cached geocoder result survives reload");
                var saved=BetaPlaceHistory.Favourites[0];Check(BetaPlaceHistory.ToggleFavourite(saved)&&!BetaPlaceHistory.IsFavourite(saved),"remove favourite persists");
                BetaPlaceHistory.Remember(new WorldPlace{name="bad",latitude=double.PositiveInfinity});Check(BetaPlaceHistory.Recent.Length==1,"invalid coordinates rejected");
                string profileOverride=PlayerProfile.StorageOverride,regionOverride=RegionCache.OverrideFolder;
                try
                {
                    PlayerProfile.StorageOverride=Path.Combine(directory,"isolated-career.json");RegionCache.OverrideFolder=Path.Combine(directory,"Regions");
                    var career=new PlayerProfile{region="download-missing-area-fixture"};Check(career.Save(),"isolated recovery career saved");
                    Check(PlayerProfile.Load().region==career.region,"missing downloaded start is retained for explicit error recovery rather than silently opening county");
                }
                finally{PlayerProfile.StorageOverride=profileOverride;RegionCache.OverrideFolder=regionOverride;}
                var telemetryRoot=new GameObject("Isolated telemetry evidence fixture");
                try
                {
                    var telemetry=telemetryRoot.AddComponent<BetaDrivingTelemetry>();string reportPath=Path.Combine(directory,"timing.json");File.WriteAllText(reportPath,"prior real driving evidence");
                    typeof(BetaDrivingTelemetry).GetField("path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(telemetry,reportPath);
                    typeof(BetaDrivingTelemetry).GetMethod("OnApplicationQuit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(telemetry,null);
                    Check(File.ReadAllText(reportPath)=="prior real driving evidence","never-driving or lease-refused launch cannot overwrite prior timing evidence with an empty report");
                }
                finally{UnityEngine.Object.DestroyImmediate(telemetryRoot);}
                // Exercise the actual local ZIP exporter with a disposable data root.
                // No session means no owner career/location is consulted.
                BetaDiagnostics.Begin(null);
                var export=(System.Threading.Tasks.Task<string>)typeof(BetaDiagnostics).GetField("export",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                Check(export!=null&&export.Wait(5000),"diagnostic ZIP completes within the bounded fixture deadline");
                string archive=export.GetAwaiter().GetResult();
                Check(Path.GetDirectoryName(archive)==Path.Combine(directory,"Diagnostics"),"diagnostic ZIP stays inside the disposable report folder");
                using(var zip=System.IO.Compression.ZipFile.OpenRead(archive))
                {
                    Check(zip.Entries.Count==1&&zip.GetEntry("diagnostics.json")!=null,"export contains only the expected diagnostic snapshot when no driving telemetry exists");
                    using(var reader=new StreamReader(zip.GetEntry("diagnostics.json").Open()))
                    {
                        var report=Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
                        Check((string)report["version"]==ProductIdentity.Version&&!(bool)report["bootReady"]&&!(bool)report["bootFailed"],"diagnostic snapshot retains version and unloaded-world state");
                        Check((float)report["x"]==0&&(float)report["y"]==0&&(float)report["z"]==0&&report["credits"]==null&&report["garage"]==null,"no-session export does not invent a location or include a career");
                    }
                }
                Check(BetaDiagnostics.Status.StartsWith("Report saved:")&&!BetaDiagnostics.Busy,"export completion is shown in the menu and releases the busy state");
                File.WriteAllText(Path.Combine(directory,"places-v1.json"),"broken json");data.SetValue(null,null);Check(BetaPlaceHistory.Recent.Length==0,"corrupt bookmark file does not break place menu");
            }
            finally{dataRoot.SetValue(null,old);data.SetValue(null,null);if(Directory.Exists(directory))Directory.Delete(directory,true);}
            Debug.Log("BETA22 MENU POLICY PASS "+checks);
        }
    }
}
