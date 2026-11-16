using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class Beta37PolicyVerification
    {
        static int count;static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException("Beta37: "+text);count++;}
        public static void Run()
        {
            count=0;var c=WorldProviderRegistry.Catalogue;Check((int)c["schema"]==1,"versioned shared catalogue");
            Check(JToken.DeepEquals(c,JObject.Parse(File.ReadAllText("AutomaticWorldWorker/_internal/WorldData/catalogue.json"))),"worker and player use identical provider catalogue");
            Check(WorldProviderRegistry.Resolve(-3.18,51.48,"GB-WLS","terrain")[0].Id=="terrain.gb","Welsh regional terrain selection");
            Check(WorldProviderRegistry.Resolve(8.5,46.5,"CH","terrain")[0].Id=="terrain.ch","Swiss survey selection");
            Check(WorldProviderRegistry.Resolve(139.7,35.7,"JP","terrain")[0].Id=="terrain.global","global approximate fallback");
            Check(WorldProviderRegistry.Resolve(-3.18,51.48,"GB","rail.live")[0].Reason=="credentials required","unregistered live feed is labelled");
            Check(WorldProviderRegistry.Resolve(-3.18,51.48,"GB","rail.timetable",new DateTime(2026,11,15)).Any(s=>s.Id=="rail.gb.published"&&s.Available),"published fallback requires valid coverage and dates");
            Check(!WorldProviderRegistry.Resolve(-3.18,51.48,"GB","rail.timetable",new DateTime(2027,1,1)).Any(s=>s.Available),"expired timetable is unavailable");
            Check(!WorldProviderRegistry.Resolve(139.7,35.7,"JP","rail.timetable").Any(),"no invented Japanese timetable coverage");
            Check(WorldProviderRegistry.Rules("JP")["railGaugesMm"].Count()>1,"country can contain several rail gauges");
            Check((string)WorldProviderRegistry.Rules("XX")["drivingSide"]=="source-defined","unknown country retains source rules");
            foreach(var p in c["providers"].OfType<JObject>())
            {Check(!string.IsNullOrEmpty((string)p["attribution"])&&!string.IsNullOrEmpty((string)p["cachePolicy"]),"provider retains attribution and cache policy");foreach(var v in p["endpoints"]??new JArray())Check(TrainServiceFeed.AllowedEndpoint((string)v)&&!((string)v).Contains("token="),"no credentials in provider endpoints");}
            var converted=TrainServiceSnapshot.Parse(File.ReadAllText("Assets/RoadRush/Editor/Fixtures/WorldDataGame.json"),TrainServiceSnapshot.Utc("2026-11-15T14:00:00Z"));
            Check(converted.Services.Length==1&&converted.Services[0].Calls.Length==2,"packaged GTFS converter output parses through the actual game contract");
            Check(converted.Services[0].Reports.Length==1&&Math.Abs(converted.Services[0].Reports[0].Speed-5)<.001,"protobuf position and SI speed reach gameplay without format-specific code");
            Debug.Log("Beta37 provider policy checks passed: "+count);
        }
    }
}
