// Opt-in private QA only. Uses normal import/event/race/service code and an AI
// guide through main -> selected pit -> main. No pose writes after race Begin.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    public sealed partial class ShowcaseCircuitCapture
    {
        IEnumerator RunContinuousPitLap(GeoRegion region)
        {
            Check(AudioListener.volume==0&&AudioListener.pause,"Continuous race remains silent");
            session.SetMenu(false);session.Racing.Begin(2);
            Check(session.Racing.Running&&session.Racing.Mode==2&&session.Racing.Entrants==1,"Normal imported one-car race began");
            var main=session.World.Route;
            var lane=region.roads.Single(r=>r.id==region.CircuitScenery.PitRoadId).points;
            float entry=main.Closest(lane[0],out _,out _),exit=main.Closest(lane[lane.Length-1],out _,out _);
            var points=new List<Vector3>();
            void Add(Vector3 p){if(points.Count==0||(points[points.Count-1]-p).sqrMagnitude>.000001f)points.Add(p);}
            for(int i=0;i<main.Points.Length;i++)if(main.DistanceAtPoint(i)<entry)Add(main.Points[i]);
            Add(lane[0]);foreach(var point in lane)Add(point);
            if(exit>.001f)for(int i=0;i<main.Points.Length;i++)if(main.DistanceAtPoint(i)>exit)Add(main.Points[i]);
            Add(main.Points[0]);
            var guide=region.SnapshotCopy();guide.CircuitRoadSurfaces=region.CircuitRoadSurfaces;guide.route=points.ToArray();
            var path=new RoutePath(guide);var pitGuide=region.SnapshotCopy();pitGuide.CircuitRoadSurfaces=region.CircuitRoadSurfaces;pitGuide.route=lane;var pitPath=new RoutePath(pitGuide);
            session.Car.NetworkDriven=true;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=path;driver.Difficulty=0;driver.PaceLimitMetresPerSecond=12;driver.Recovered+=_=>recoveries++;
            observations=new StreamWriter(prefix+"-continuous-pit-lap.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");
            // Normal grid countdown settles the initial Recover suspension drop.
            // It is not part of the measured drive and must not count as a jump.
            while(session.Racing.Countdown>0)yield return null;
            previous=session.Car.Body.position;distance=0;stage="continuous-race";driving=true;
            float began=Time.unscaledTime,bayStation=pitPath.Closest(session.Pits.Bay,out _,out _),serviceSeconds=0;
            bool serviced=false,entered=false,lapStarted=false;int captures=0;
            while(session.Racing.Lap.CompletedLaps<1)
            {
                if(Time.unscaledTime-began>1000)throw new TimeoutException("Continuous race pit lap exceeded bounded window");
                CheckDriving();
                if(session.Racing.Lap.Armed)lapStarted=true;
                if(lapStarted&&!session.Racing.Lap.Armed)throw new InvalidOperationException("Continuous pit lap lost its checkpoint sequence");
                if(session.Racing.Lap.Reason.Contains("discontinuity"))throw new InvalidOperationException("Continuous pit lap falsely aborted");
                if(!entered&&session.Pits.OnLane(session.Car.Body.position)&&pitPath.Closest(session.Car.Body.position,out _,out float entryGap)>15&&pitPath.Closest(session.Car.Body.position,out _,out _)<100&&entryGap<4){entered=true;AuthoredPhase("continuous-pit-entered");}
                driver.PaceLimitMetresPerSecond=session.Pits.OnLane(session.Car.Body.position)?8:12;
                float pitStation=pitPath.Closest(session.Car.Body.position,out _,out float gap);
                if(!serviced&&entered&&pitStation>=bayStation-3&&pitStation<bayStation+5&&gap<4)
                {
                    driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
                    float stopping=Time.unscaledTime;
                    while(session.Car.AbsoluteSpeed>.2f){if(Time.unscaledTime-stopping>5)throw new TimeoutException("Continuous pit stop failed");yield return null;}
                    Check(session.Pits.AtBay,"Continuous physical approach reaches selected bay");
                    int credits=session.Economy.Credits;session.Car.ReplicaDamage(25);session.SyncActiveCondition();Check(session.TrySave(),"Isolated continuous service fixture saved");
                    float start=Time.unscaledTime;
                    while(!session.Pits.Servicing){if(Time.unscaledTime-start>3)throw new TimeoutException("Continuous normal service did not start");yield return null;}
                    bool captured=false;
                    while(session.Pits.Servicing)
                    {
                        if(Time.unscaledTime-start>10)throw new TimeoutException("Continuous normal service did not finish");
                        if(!captured&&Time.unscaledTime-start>3){ScreenCapture.CaptureScreenshot(prefix+"-continuous-service.png");captured=true;}
                        yield return null;
                    }
                    serviceSeconds=Time.unscaledTime-start;
                    Check(serviceSeconds>=5.8f&&serviceSeconds<10&&session.Car.Damage==0,"Continuous normal six-second repair completed");
                    var saved=PlayerProfile.Load();Check(saved.garage.Active.damage==0&&session.Economy.Credits==credits&&saved.credits==credits,"Continuous free repair saved without charge");
                    serviced=true;previous=session.Car.Body.position;driving=true;driver.enabled=true;AuthoredPhase("continuous-service-complete");
                }
                if(captures<3&&Time.unscaledTime-began>30+captures*180){ScreenCapture.CaptureScreenshot(prefix+"-continuous-drive-"+captures+".png");captures++;}
                yield return null;
            }
            Check(distance>path.Length*.95f&&distance<path.Length*1.1f,"Race completion uses one physical guide lap");
            // Wing exit shares the timing line. Resume normally after results
            // and drive beyond that shared seam before accepting the rejoin.
            float finishDistance=distance;session.ResumeDrive();Check(!session.Menu,"Normal owned-car Resume permits pit-exit cooldown");
            stage="pit-rejoin-cooldown";float cooldownBegan=Time.unscaledTime;
            while(distance-finishDistance<60)
            {if(Time.unscaledTime-cooldownBegan>30)throw new TimeoutException("Post-finish pit rejoin stalled");CheckDriving();yield return null;}
            float postFinishDistance=distance-finishDistance;
            driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;observations.Dispose();observations=null;
            Check(entered&&serviced&&session.Racing.Lap.CompletedLaps==1,"Normal race completes a full lap through selected pit and service");
            Check(recoveries==0&&maxDamage<.1f&&maxAirborne<.3f,"Continuous drive has no recovery, collision or sustained airborne interval");
            Check(!session.Racing.Lap.LastValid,"Existing service invalidates clean-lap reward; policy unchanged");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-continuous-finish.png");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{{"status","PASS_CONTINUOUS_RACE_PIT_SERVICE_PLAYER"},{"checks",new JArray(checks)},{"pitRoadId",region.CircuitScenery.PitRoadId},{"distanceMetres",distance},{"serviceSeconds",serviceSeconds},{"completedLaps",session.Racing.Lap.CompletedLaps},{"postFinishDistanceMetres",postFinishDistance},{"lastLapValid",session.Racing.Lap.LastValid},{"recoveries",recoveries},{"damage",maxDamage},{"maxAirborneSeconds",maxAirborne},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"scope","Normal imported race, stock vehicle and AI guide: continuous main-selected pit-main with normal repair/save. Only normal race initial grid placement; no later pose writes. Existing pit-service clean-lap invalidation retained. No accuracy, Windows hardware or general race scoring acceptance."}}.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }
    }
}
