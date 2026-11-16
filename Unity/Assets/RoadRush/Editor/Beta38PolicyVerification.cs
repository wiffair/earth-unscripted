using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using NativeWorld=RoadRush.NewbridgePrototype.NewbridgeWorld;
namespace RoadRush.Editor
{
    public static class Beta38PolicyVerification
    {
        static int count;static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException("Beta38: "+text);count++;}
        static JObject Document(double now,int gauge=1435)=>new JObject{["schema"]=1,["provider"]="test.public",["attribution"]="Public provider fixture",["receivedUtc"]=now,["observations"]=new JObject{["records"]=new JArray(new JObject{["id"]="vehicle",["tripId"]="trip",["position"]=new JArray(0,500/111319.4908),["observedUtc"]=now-1,["gaugeMm"]=gauge,["bearingDegrees"]=0})}};
        public static void Run()
        {
            count=0;
            Check(WorldProviderRegistry.Resolve(4.9,52.37,"NL","terrain")[0].Id=="terrain.nl.ahn","Dutch survey priority");
            Check(WorldProviderRegistry.Resolve(-75.7,45.42,"CA","terrain")[0].Id=="terrain.ca.hrdem","Canadian survey priority");
            Check(WorldProviderRegistry.Resolve(-71.06,42.36,"US","terrain")[0].Id=="terrain.us.3dep","US source selection");
            Check(WorldProviderRegistry.Resolve(-122,38,"US","rail.live").Length==0,"Boston feed does not claim nationwide coverage");
            Check(WorldProviderRegistry.Resolve(10,60,"NO","rail.live")[0].Available,"Norwegian rail feed requires no account");
            var feature=new NativeWorld.Feature{sourceId="rail1",category="rail",kind="rail",gauge="1435",points=Enumerable.Range(0,101).Select(i=>new Vector3(0,0,i*10)).ToArray()};
            var data=new PublicTrainData();var doc=Document(1000);var result=data.Convert(doc.ToString(),1000,0,0,"NO",new[]{feature});
            Check(result.Services.Length==1,"Geographic report binds to real loaded track");var plan=result.Services[0];
            Check(plan.AutomaticReported&&!plan.StockConfirmed&&plan.StockId=="generic.1435","Generic allocation and live position quality are explicit");
            Check(RailTrainSystem.BuildServiceRoute(plan,new[]{feature},out var route),"Actual gameplay route admits reported section");
            Check(plan.Sample(1000,false,40).Quality!=TrainPositionQuality.Timetable,"Automatic mode uses real reports without manual URL");
            var parallel=new NativeWorld.Feature{sourceId="rail2",category="rail",kind="rail",gauge="1435",points=feature.points.Select(p=>p+Vector3.right).ToArray()};
            Check(data.Convert(doc.ToString(),1000,0,0,"NO",new[]{feature,parallel}).Services.Length==0,"Ambiguous parallel track not guessed");
            feature.gauge="1000";Check(data.Convert(doc.ToString(),1000,0,0,"NO",new[]{feature}).Services.Length==0,"Wrong gauge is refused");
            feature.gauge="1600";doc=Document(1000,1600);result=data.Convert(doc.ToString(),1000,0,0,"IE",new[]{feature});
            Check(result.Services.Length==1&&TrainStock.Find(result.Services[0].StockId).Gauge==1.6f,"Irish broad gauge uses matching formation");
            var stock=TrainStock.Generic(1600);Check(RailTrainSystem.BuildServiceRoute(result.Services[0],new[]{feature},out route),"Broad gauge actual route admission");
            var pose=stock.Pose(route,600);var wheels=pose.cars[0].bogies[0].axles[0].wheels;
            Check(Math.Abs((wheels[1].contact.X-wheels[0].contact.X)-1.6)<.001,"Wheel contacts use actual formation gauge");
            doc["observations"]["records"][0]["positionQuality"]="estimated";
            Check(!data.Convert(doc.ToString(),1000,0,0,"IE",new[]{feature}).Services[0].AutomaticReported,"Receipt-only Irish reports remain estimated");
            doc["observations"]["records"][0]["observedUtc"]=1;
            Check(data.Convert(doc.ToString(),1000,0,0,"IE",new[]{feature}).Services.Length==0,"Stale reports cannot spawn trains");
            feature.tunnel=true;doc=Document(1000,1600);Check(data.Convert(doc.ToString(),1000,0,0,"IE",new[]{feature}).Services.Length==0,"Unprepared tunnel rail is not rendered through terrain");
            Debug.Log("Beta38 international provider policy checks passed: "+count);
        }
    }
}
