using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RoadRush.Candidates.Articulated;
using RoadRush.Candidates.Compatibility;
using RoadRush.NewbridgePrototype;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class Beta36PolicyVerification
    {
        static int count;static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException("Beta36: "+text);count++;}
        static void Refuses(Action operation,string label){bool refused=false;try{operation();}catch{refused=true;}Check(refused,label);}
        static JObject Document(double now)
        {
            string U(double seconds)=>DateTimeOffset.FromUnixTimeSeconds((long)seconds).ToString("O");
            return new JObject{["schema"]=1,["provider"]="Synthetic acceptance fixture",["attribution"]="Test data, not a real timetable",["generatedUtc"]=U(now),["services"]=new JArray(new JObject{
                ["id"]="fixture-1",["stockId"]="gb.197.2",["pathId"]="fixture-path",["sourceWays"]=new JArray("a"),["direction"]=1,
                ["calls"]=new JArray(new JObject{["location"]="A",["metres"]=100,["arrivalUtc"]=U(now-120),["departureUtc"]=U(now-60)},new JObject{["location"]="B",["metres"]=900,["arrivalUtc"]=U(now+120),["departureUtc"]=U(now+180)}),
                ["reports"]=new JArray(new JObject{["metres"]=300,["observedUtc"]=U(now-30),["receivedUtc"]=U(now-29),["speedMps"]=5},new JObject{["metres"]=400,["observedUtc"]=U(now-10),["receivedUtc"]=U(now-9),["speedMps"]=5})})};
        }
        public static void Run()
        {
            count=0;var route=new RailRoute(new[]{new Vec3(0,0,0),new Vec3(0,0,1200)});
            Check(TrainStock.All.Length>=5&&TrainStock.All.Select(t=>t.Id).Distinct().Count()==TrainStock.All.Length,"distinct real-inspired stock formations");
            foreach(var stock in TrainStock.All)
            {
                var pose=stock.Pose(route,700);Check(pose.status=="active"&&pose.cars.Count==stock.Vehicles.Length,"complete "+stock.Id+" consist");
                for(int i=0;i<pose.cars.Count;i++)
                {
                    var car=pose.cars[i];Check(car.bogies.Count==2&&car.bogies.All(b=>b.axles.Count==stock.Vehicles[i].Axles),"correct stock axle configuration");
                    Check(Math.Abs(car.body.origin.X)<.001&&Math.Abs(car.body.origin.Y)<.001,"bodies follow rail datum");
                    Check(car.bogies.All(b=>b.axles.All(a=>Math.Abs(a.wheels[1].contact.X-a.wheels[0].contact.X-1.435)<.0001)),"standard-gauge wheel contact centres");
                    Check(car.bogies.All(b=>b.axles.All(a=>a.wheels.All(w=>Math.Abs(w.centre.Y-stock.Vehicles[i].WheelRadius)<.0001))),"stock wheel radius matches contact height");
                }
                Check(stock.Pose(route,stock.Length-1).status!="active","whole-consist entry extent");Check(stock.Pose(route,1201).status!="active","no route-end extrapolation");
                Check(pose.couplerGaps.All(g=>g>=0&&g<2),"car gaps remain bounded");
            }
            double now=TrainServiceSnapshot.Utc("2026-11-15T00:00:30Z");var data=Document(now);var snapshot=TrainServiceSnapshot.Parse(data.ToString(),now);var plan=snapshot.Services[0];
            Check(snapshot.Provider.Contains("Synthetic"),"fixture never masquerades as real data");
            Check(plan.Sample(now-90,false,44).Dwelling,"station dwell follows timetable");
            Check(plan.Sample(now,false,44).Quality==TrainPositionQuality.Timetable,"scheduled interpolation is labelled estimated");
            Check(plan.Sample(now,true,44).Quality==TrainPositionQuality.Interpolated,"between-report position is labelled interpolated");
            Check(!plan.Sample(now-121,true,44).Available&&!plan.Sample(now+181,true,44).Available,"service operating extent");
            plan.Cancelled=true;Check(!plan.Sample(now,true,44).Available,"cancelled service is not rendered");plan.Cancelled=false;
            Check(plan.Sample(now+119,true,44).Quality==TrainPositionQuality.Timetable,"stale reports fall back to a labelled timetable estimate");
            Refuses(()=>TrainServiceSnapshot.Utc("2026-11-15T12:00:00"),"offset-free time is refused");
            var bad=Document(now);bad["services"][0]["reports"][1]["metres"]=10000;Refuses(()=>TrainServiceSnapshot.Parse(bad.ToString(),now),"impossible report speed");
            bad=Document(now);bad["services"][0]["calls"][1]["arrivalUtc"]="2026-11-14T00:00:00Z";Refuses(()=>TrainServiceSnapshot.Parse(bad.ToString(),now),"reversed schedule order");
            bad=Document(now);bad["services"][0]["reports"][0]["receivedUtc"]="2026-11-14T00:00:00Z";Refuses(()=>TrainServiceSnapshot.Parse(bad.ToString(),now),"receipt cannot precede observation");
            var feature=new NewbridgeWorld.Feature{sourceId="a",category="rail",kind="rail",gauge="1435",points=new[]{Vector3.zero,new Vector3(0,0,1000)}};
            Check(RailTrainSystem.BuildServiceRoute(plan,new[]{feature},out var bound)&&Math.Abs(bound.Length-1000)<.01,"service binds only to actual admitted source geometry");
            feature.gauge="1000";Check(!RailTrainSystem.BuildServiceRoute(plan,new[]{feature},out _),"wrong gauge refuses service");feature.gauge="1435";
            plan.SourceWays=new[]{"a","b"};var second=new NewbridgeWorld.Feature{sourceId="b",category="rail",kind="rail",points=new[]{new Vector3(0,0,1000),new Vector3(0,0,1500)}};
            Check(RailTrainSystem.BuildServiceRoute(plan,new[]{feature,second},out _),"matching rail seam joins");
            second.points=new[]{new Vector3(0,6,1000),new Vector3(0,6,1500)};Check(!RailTrainSystem.BuildServiceRoute(plan,new[]{feature,second},out _),"rail overbridge does not join lower track");
            second.points=new[]{new Vector3(4,0,1000),new Vector3(4,0,1500)};Check(!RailTrainSystem.BuildServiceRoute(plan,new[]{feature,second},out _),"parallel track gap is not invented");
            Check(TrainServiceFeed.AllowedEndpoint("https://example.org/rail.json")&&TrainServiceFeed.AllowedEndpoint("http://127.0.0.1:8000/rail.json"),"secure or loopback data endpoint");
            Check(!TrainServiceFeed.AllowedEndpoint("http://example.org/rail.json")&&!TrainServiceFeed.AllowedEndpoint("https://secret:password@example.org/rail.json")&&!TrainServiceFeed.AllowedEndpoint("file:///etc/passwd"),"no plaintext remote/userinfo/local-file URL transport");
            Check(PublishedTrainTimetable.TripCount>=390&&PublishedTrainTimetable.Paths.Count==3,"published timetable facts and three mapped South Wales corridors load offline");
            Check(PublishedTrainTimetable.TryUtc(new DateTime(2026,11,15),14*60+30,out double winter)&&Math.Abs(winter-TrainServiceSnapshot.Utc("2026-11-15T14:30:00Z"))<.1,"published winter times use GMT");
            Check(PublishedTrainTimetable.TryUtc(new DateTime(2026,9,15),14*60+30,out double summer)&&Math.Abs(summer-TrainServiceSnapshot.Utc("2026-09-15T13:30:00Z"))<.1,"published summer times use British Summer Time");
            Check(!PublishedTrainTimetable.TryUtc(new DateTime(2026,10,25),90,out _)&&!PublishedTrainTimetable.TryUtc(new DateTime(2026,3,29),90,out _),"ambiguous or missing DST clock times are not guessed");
            var sunday=PublishedTrainTimetable.Create(winter,out var publishedStatus);
            Check(sunday!=null&&sunday.Published&&sunday.Services.Length>0&&publishedStatus.Contains("no live"),"real Sunday services are explicitly published timetable estimates");
            Check(sunday.Services.All(p=>p.PublishedPath!=null&&!p.StockConfirmed&&p.Reports.Length==0),"published data does not invent observed reports or confirmed unit allocations");
            Check(sunday.Services.All(p=>p.Sample(winter,true,55).Quality==TrainPositionQuality.Timetable||!p.Sample(winter,true,55).Available),"reported-position mode remains estimated without observations");
            var monday=PublishedTrainTimetable.Create(TrainServiceSnapshot.Utc("2026-11-16T14:30:00Z"),out _);
            Check(monday.Services.Any(p=>!sunday.Services.Any(s=>s.Id.Split(new[]{"-2026"},StringSplitOptions.None)[0]==p.Id.Split(new[]{"-2026"},StringSplitOptions.None)[0])),"weekday and Sunday service calendars are distinct");
            Check(PublishedTrainTimetable.Create(TrainServiceSnapshot.Utc("2026-12-14T12:00:00Z"),out var expired)==null&&expired.Contains("expired"),"expired published schedules are not repeated indefinitely");
            foreach(var corridor in PublishedTrainTimetable.Paths.Values)
            {
                Check(corridor.Points.Length>100&&corridor.Ways.Count>20&&corridor.Metres.Zip(corridor.Metres.Skip(1),(a,b)=>b>a).All(v=>v),"mapped corridor has increasing physical stationing: "+corridor.Id);
                int i=corridor.Points.Length/2;double projected=corridor.Project(corridor.Points[i],out float gap);
                Check(gap<.01&&Math.Abs(projected-corridor.Metres[i])<.1,"indexed projection preserves actual railway stationing: "+corridor.Id);
            }
            var straight=new PublishedTrainPath{Id="published-policy",Points=new[]{new Vector2(0,0),new Vector2(0,1000)},Metres=new[]{0d,1000d},Ways=new HashSet<string>{"mapped"}};
            var origin=new GeoRegion{latitude=51.5,longitude=-3};
            Check(RailTrainSystem.PublishedSection(straight,origin,new[]{new Vector3(0,2,800),new Vector3(0,2,600),new Vector3(0,2,400)},out var oriented,out var globalAt,out var localAt)&&oriented[0].z==400&&Math.Abs(globalAt[0]-400)<.01&&Math.Abs(localAt[2]-400)<.01,"published section reverses source order without leaving actual track geometry");
            Check(!RailTrainSystem.PublishedSection(straight,origin,new[]{new Vector3(30,2,100),new Vector3(30,2,300)},out _,out _,out _),"unmapped parallel railway is not snapped to a published corridor");
            Check(!RailTrainSystem.PublishedSection(straight,origin,new[]{new Vector3(0,2,100),new Vector3(0,52,300)},out _,out _,out _),"sudden rail height jump is not admitted for timetable playback");
            Debug.Log("Beta36 train/timetable policy checks passed: "+count);
        }
    }
}
