using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public static class Beta36ServiceVerification
    {
        static double Station(Vector3[] points,Vector3 point)
        {
            double cursor=0,bestAt=0;float best=float.PositiveInfinity;
            for(int i=1;i<points.Length;i++)
            {var d=points[i]-points[i-1];float length=d.magnitude,f=Mathf.Clamp01(Vector3.Dot(point-points[i-1],d)/d.sqrMagnitude);float error=(points[i-1]+d*f-point).sqrMagnitude;if(error<best){best=error;bestAt=cursor+f*length;}cursor+=length;}
            return bestAt;
        }
        static string U(double value)=>DateTimeOffset.FromUnixTimeMilliseconds((long)(value*1000)).ToString("O");
        public static IEnumerator Exercise(GameSession session,Action<bool,string> check)
        {
            check(SinglePlayerBeta.DataRoot.Contains("/private/tmp/"),"Rail data acceptance is isolated from real accounts");
            var p=session.Profile;int oldLimit=p.trainFleetLimit,oldMode=p.trainServiceMode;bool oldEnabled=p.simulatedTrains,oldFallback=p.trainTimetableFallback;p.trainTimetableFallback=false;string oldUrl=p.trainFeedUrl;
            p.trainFleetLimit=4;p.trainServiceMode=0;p.trainFeedUrl="";p.simulatedTrains=true;session.Car.StartLocked=true;session.SetMenu(false);
            var master=session.GetComponent<RailTrainSystem>();var feed=session.GetComponent<TrainServiceFeed>();double deadline=Time.realtimeSinceStartupAsDouble+100;
            while(master.ActiveFleetCount<2&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta36-local-fleet.json"),new JObject{["active"]=master.ActiveFleetCount,["primary"]=master.Status,["members"]=new JArray(master.Members.Where(m=>m).Select(m=>(JToken)new JObject{["stock"]=m.Formation.Id,["running"]=m.Running,["status"]=m.Status,["way"]=m.SourceWay}))}.ToString());
            check(master.ActiveFleetCount>=2,"Multiple stock-specific trains operate concurrently on separate real loaded railway sections");
            while(!master.Running&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            check(master.Running&&master.TryPointAhead(0,out _,out _),"Connected-service fixture starts from a supported actual railway");
            master.TryPointAhead(0,out var lead,out _);
            string way=master.SourceWay;var feature=session.World.AutomaticWorld.Renderer.ResidentAreas.SelectMany(a=>a.features).Where(f=>f.sourceId==way).OrderByDescending(f=>f.points.Length).First();
            double at=Station(feature.points,lead),now=TrainServiceFeed.Now;
            var document=new JObject{["schema"]=1,["provider"]="Beta36 synthetic acceptance only",["attribution"]="Synthetic test fixture, not a real railway timetable",["generatedUtc"]=U(now),["services"]=new JArray(new JObject{
                ["id"]="beta36-test-service",["stockId"]="gb.197.2",["stockConfirmed"]=false,["pathId"]="test-actual-"+way,["sourceWays"]=new JArray(way),["direction"]=1,
                ["calls"]=new JArray(new JObject{["location"]="synthetic A",["metres"]=at-30,["arrivalUtc"]=U(now-80),["departureUtc"]=U(now-60)},new JObject{["location"]="synthetic B",["metres"]=at+80,["arrivalUtc"]=U(now+180),["departureUtc"]=U(now+200)}),["reports"]=new JArray()})};
            string file=feed.LocalPath;Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,document.ToString());p.trainFleetLimit=1;p.trainServiceMode=1;
            deadline=Time.realtimeSinceStartupAsDouble+45;
            while(master.ServiceId!="beta36-test-service"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            check(master.Running&&master.ServiceId=="beta36-test-service"&&master.Formation.Id=="gb.197.2","Normalised timetable data selects its specific train formation on actual source geometry");
            check(master.PositionQuality==TrainPositionQuality.Timetable,"Timetable-driven train is labelled as an estimated position");
            double moved=master.TravelledMetres;yield return new WaitForSecondsRealtime(6);
            check(master.TravelledMetres>moved+.5,"Connected timetable advances the physical train without a pose jump");
            now=TrainServiceFeed.Now;double current=master.HeadMetres;
            document["generatedUtc"]=U(now);document["services"][0]["reports"]=new JArray(new JObject{["metres"]=current-3,["observedUtc"]=U(now-15),["receivedUtc"]=U(now-14),["speedMps"]=.3},new JObject{["metres"]=current,["observedUtc"]=U(now-5),["receivedUtc"]=U(now-4),["speedMps"]=.3});
            File.WriteAllText(file,document.ToString());p.trainServiceMode=2;deadline=Time.realtimeSinceStartupAsDouble+45;
            while((master.ServiceId!="beta36-test-service"||master.PositionQuality!=TrainPositionQuality.Interpolated)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            check(master.Running&&master.PositionQuality==TrainPositionQuality.Interpolated,"Received reporting-point updates drive explicitly labelled between-report movement");
            document["generatedUtc"]=U(TrainServiceFeed.Now);document["services"][0]["cancelled"]=true;File.WriteAllText(file,document.ToString());
            deadline=Time.realtimeSinceStartupAsDouble+8;while(master.Running&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            check(!master.Running,"A newly cancelled connected service is removed rather than continuing its old timetable");
            File.WriteAllText(file,"{");deadline=Time.realtimeSinceStartupAsDouble+8;
            while(feed.Snapshot!=null&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            check(feed.Snapshot==null&&!master.Running,"Malformed rail data is contained without fake live traffic or gameplay exceptions");
            File.Delete(file);p.trainFleetLimit=oldLimit;p.trainServiceMode=oldMode;p.simulatedTrains=oldEnabled;p.trainFeedUrl=oldUrl;p.trainTimetableFallback=oldFallback;session.SetMenu(true);
        }
    }
}
