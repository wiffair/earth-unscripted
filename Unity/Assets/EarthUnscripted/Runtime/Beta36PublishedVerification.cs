using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public static class Beta36PublishedVerification
    {
        public static IEnumerator Exercise(GameSession session,Action<bool,string> check)
        {
            check(SinglePlayerBeta.DataRoot.Contains("/private/tmp/"),"Published timetable acceptance uses disposable progress");
            var p=session.Profile;int oldMode=p.trainServiceMode,oldLimit=p.trainFleetLimit;bool oldFallback=p.trainTimetableFallback,oldEnabled=p.simulatedTrains;string oldUrl=p.trainFeedUrl;
            double clock=TrainServiceSnapshot.Utc("2026-11-15T14:01:00Z"),started=0;bool advancing=false;
            try
            {
                TrainServiceFeed.VerificationClock=()=>clock+(advancing?Time.realtimeSinceStartupAsDouble-started:0);
                p.trainServiceMode=0;p.trainFleetLimit=1;p.simulatedTrains=true;p.trainFeedUrl="";p.trainTimetableFallback=true;
                session.Car.StartLocked=true;session.SetMenu(false);yield return null;yield return null;p.trainServiceMode=1;
                var feed=session.GetComponent<TrainServiceFeed>();var train=session.GetComponent<RailTrainSystem>();
                double deadline=Time.realtimeSinceStartupAsDouble+45;
                while((feed.Snapshot==null||!feed.Snapshot.Published||!train.Running||!train.ServiceId.StartsWith("published-"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                var services=feed.Snapshot?.Services??Array.Empty<TrainServicePlan>();
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta36-published-service.json"),new JObject{
                    ["feed"]=feed.Status,["train"]=train.Status,["way"]=train.SourceWay,["service"]=train.ServiceId,
                    ["services"]=new JArray(services.Select(s=>(JToken)new JObject{["id"]=s.Id,["metres"]=s.Sample(clock,false,55).Metres,["available"]=s.Sample(clock,false,55).Available,["coverage"]=train.PublishedCoverage(s)}))}.ToString());
                check(feed.Snapshot!=null&&feed.Snapshot.Published&&feed.Status.Contains("Published timetable estimates"),"No-account fallback loads actual published services without a live connection");
                check(train.Running&&train.ServiceId.StartsWith("published-"),"Published Sunday Cardiff service occupies real prepared track at its estimated timetable position");
                check(train.PositionQuality==TrainPositionQuality.Timetable,"Published service is never labelled a live observation");
                double before=train.TravelledMetres;advancing=true;started=Time.realtimeSinceStartupAsDouble;yield return new WaitForSecondsRealtime(6);
                check(train.TravelledMetres>before+10,"Published clock times advance the physical train along connected loaded source ways");
                p.trainServiceMode=2;yield return null;yield return null;
                deadline=Time.realtimeSinceStartupAsDouble+20;while((feed.Snapshot==null||!feed.Snapshot.Published)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                check(feed.Snapshot!=null&&feed.Snapshot.Published,"Reported-position mode automatically retains published fallback while no feed account is configured");
                p.trainTimetableFallback=false;deadline=Time.realtimeSinceStartupAsDouble+20;while(feed.Snapshot!=null&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                check(feed.Snapshot==null,"Disabling published fallback does not silently continue scheduled services");
            }
            finally{TrainServiceFeed.VerificationClock=null;p.trainServiceMode=oldMode;p.trainFleetLimit=oldLimit;p.trainTimetableFallback=oldFallback;p.simulatedTrains=oldEnabled;p.trainFeedUrl=oldUrl;session.SetMenu(true);}
        }
    }
}
