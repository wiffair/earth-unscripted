using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    public sealed partial class ShowcaseCircuitCapture
    {
        IEnumerator RunPitRecoveryFixture(GeoRegion region)
        {
            Check(AudioListener.volume==0&&AudioListener.pause,"Pit recovery remains silent");
            session.SetMenu(false);session.Racing.Begin(2);Check(session.Racing.Mode==2&&session.Racing.Entrants==1,"Normal isolated recovery race selected");
            var main=session.World.Route;session.Car.NetworkDriven=true;
            driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=main;driver.Difficulty=0;driver.PaceLimitMetresPerSecond=8;driver.Recovered+=_=>recoveries++;
            float began=Time.unscaledTime;
            while(!session.Racing.Lap.Armed){if(Time.unscaledTime-began>15)throw new TimeoutException("Recovery race did not cross start");yield return null;}
            driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            var guide=region.SnapshotCopy();guide.CircuitRoadSurfaces=region.CircuitRoadSurfaces;guide.route=region.roads.Single(r=>r.id==region.CircuitScenery.PitRoadId).points;
            var pit=new RoutePath(guide);driver.Route=pit;
            // Explicit isolated fixture placement at pit entry only. The normal
            // recovery under test happens later, after physical pit driving.
            session.Car.Recover(pit.At(0),pit.Rotation(0));session.Racing.Lap.Recover(main.Closest(pit.At(0),out _,out _));
            yield return new WaitForSecondsRealtime(1);
            previous=session.Car.Body.position;distance=0;stage="pit-recovery-approach";driving=true;
            observations=new StreamWriter(prefix+"-pit-recovery.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");
            driver.enabled=true;began=Time.unscaledTime;
            while(pit.Closest(session.Car.Body.position,out _,out _)<pit.Length*.5f)
            {if(Time.unscaledTime-began>150)throw new TimeoutException("Pit recovery approach stalled");CheckDriving();CheckArmed();yield return null;}
            driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            float stopped=Time.unscaledTime;
            while(session.Car.AbsoluteSpeed>.2f){if(Time.unscaledTime-stopped>5)throw new TimeoutException("Pit recovery stop stalled");yield return null;}
            CheckArmed();float penaltyBefore=session.Racing.Lap.Penalties;int serialBefore=session.Racing.Lap.Serial;
            session.Recover(); // Actual keyboard/controller rescue handler.
            yield return new WaitForSecondsRealtime(1);
            CheckArmed();Check(!session.Racing.Lap.Valid&&session.Racing.Lap.Penalties==penaltyBefore+8&&session.Racing.Lap.Serial==serialBefore,"Normal rescue keeps gate sequence with existing invalidation and8spenalty");
            var selected=region.Nearest(session.Car.Body.position,out _,out _);
            Check(selected!=null&&selected.id==region.CircuitScenery.PitRoadId&&session.Pits.OnLane(session.Car.Body.position),"Normal rescue remains on selected pit pavement");
            float after=distance;previous=session.Car.Body.position;driver.enabled=true;stage="pit-recovery-resume";began=Time.unscaledTime;
            while(distance-after<25){if(Time.unscaledTime-began>15)throw new TimeoutException("Recovered car did not resume");CheckDriving();CheckArmed();yield return null;}
            driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;observations.Dispose();observations=null;
            ScreenCapture.CaptureScreenshot(prefix+"-pit-recovery-resumed.png");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{{"status","PASS_NORMAL_PIT_RECOVERY_PLAYER"},{"checks",new JArray(checks)},{"pitRoadId",selected.id},{"distanceMetres",distance},{"resumedDistanceMetres",distance-after},{"penaltyBefore",penaltyBefore},{"penaltyAfter",session.Racing.Lap.Penalties},{"serial",serialBefore},{"automaticRecoveries",recoveries},{"damage",maxDamage},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"scope","Normal imported race/start-line crossing; explicit fixture placement at selected pit entry, physical half-lane drive, actual GameSession.Recover and25mresumed drive. Preserves gate sequence and existing recovery penalty/invalidation. Not a full race lap or no-pose-write claim."}}.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }
        void CheckArmed(){if(!session.Racing.Lap.Armed||session.Racing.Lap.Reason.Contains("discontinuity"))throw new InvalidOperationException("Pit recovery lost lap checkpoint state");}
    }
}
