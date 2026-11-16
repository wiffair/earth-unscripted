using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Explicit private capture only: normal library import and scene
    /// switch, labelled stationary art views, then an actual input-driven lap.
    /// No real career paths, pose writes during the measured lap or provider work.</summary>
    public sealed partial class ShowcaseCircuitCapture : MonoBehaviour
    {
        GameSession session; string prefix,archive; double deadline;
        readonly List<string> checks=new List<string>();
        RaceDriver driver; StreamWriter observations; int samples,recoveries;
        float distance,maxDamage,maxAirborne,airborne; Vector3 previous;
        bool driving,noDriveScreenshots;string stage="main";
        readonly List<JObject> driveCaptureEvents=new List<JObject>();
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);checks.Add(why);}
        public void Initialize(GameSession owner,string output,string pack)
        {
            if(string.IsNullOrEmpty(PlayerProfile.StorageOverride)||Path.GetFullPath(PlayerProfile.StoragePath)!=Path.GetFullPath(output+"-profile.json"))throw new InvalidOperationException("Isolated profile required");
            session=owner;prefix=output;archive=pack;deadline=Time.realtimeSinceStartupAsDouble+(Environment.GetCommandLineArgs().Contains("-authoredRealSourceFixture")?1200:900);
            DontDestroyOnLoad(gameObject);Application.runInBackground=true;
            AudioListener.volume=0;AudioListener.pause=true;owner.Profile.volume=0;
            noDriveScreenshots=Environment.GetCommandLineArgs().Contains("-showcaseNoDriveScreenshots");
            if(Environment.GetCommandLineArgs().Contains("-showcaseBootTiming"))BootTelemetry.EnableIsolated();
            InitializeFrameTiming();StartCoroutine(Guard(Run()));
        }
        IEnumerator Guard(IEnumerator job)
        {
            while(true)
            {
                object next=null;bool more=false;Exception error=null;
                try{if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Showcase native capture");more=job.MoveNext();if(more)next=job.Current;}catch(Exception e){error=e;}
                if(error!=null)
                {
                    observations?.Dispose();File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="FAIL",["error"]=error.ToString(),["checks"]=new JArray(checks),["samples"]=samples,["distance"]=distance,["damage"]=maxDamage,["recoveries"]=recoveries}.ToString());
                    Debug.LogError(error);Application.Quit(1);yield break;
                }
                if(!more)yield break;if(next is IEnumerator nested)yield return Guard(nested);else yield return next;
            }
        }
        void Prepare(GameSession s)
        {
            s.Profile.volume=0;s.Profile.weatherMode=0;s.Profile.clockMode=0;s.Profile.fixedHour=14;
            if(Environment.GetCommandLineArgs().Contains("-showcaseRainNight"))
            {s.Weather=1;s.Profile.weatherMode=1;s.Profile.fixedHour=22;}
            if(s.Updates){s.Updates.Enabled=false;s.Updates.Automatic=false;}s.Conditions.CancelLocalFetch();
            s.SetMenu(true);s.Car.StartLocked=true;s.Input.enabled=false;s.Input.Throttle=s.Input.Steer=0;s.Input.Brake=s.Input.Handbrake=1;
        }
        IEnumerator Run()
        {
            if(Environment.GetCommandLineArgs().Contains("-attestationMenuFixture")){yield return RunAttestationMenuFixture();yield break;}
            if(Environment.GetCommandLineArgs().Contains("-libraryCheckFixture")){yield return RunLibraryCheckFixture();yield break;}
            if(Environment.GetCommandLineArgs().Contains("-authoredFixture")){yield return RunAuthoredFixture();yield break;}
            Check(AudioListener.volume==0&&AudioListener.pause,"Sound disabled before native test");
            Prepare(session);session.GetComponent<DrivingUI>().ShowImportedPacks();
            string originalRegion=session.Profile.region;
            int originalCredits=session.Profile.credits;
            var library=session.GetComponent<DrivingUI>().ImportedPacks;
            while(library.Busy)yield return null;library.ImportArchive(archive);
            while(library.Busy)yield return null;
            library.CheckContent(0);
            while(library.Busy)yield return null;
            Check(library.Status.Contains("1/1 driving layouts readable"),"Normal library CHECK recognises supported circuit environment");
            Check(library.OpenCircuitRevision(0),"Normal library opens showcase import");
            var chooser=session.GetComponent<DrivingUI>().ImportedCircuits;
            if(Environment.GetCommandLineArgs().Contains("-showcaseExpectAllocationRefusal"))
            {
                while(chooser.Busy)yield return null;
                Check(chooser.SelectedCircuit==null&&chooser.Status.Contains("scenery exceeds the supported detail limit"),"Normal chooser explains expanded-allocation refusal before activation");
                Check(!chooser.StartSession(0,1,0),"Refused layout cannot start or overwrite a career destination");
                Check(session.BootReady&&!session.BootFailed&&session.Profile.region==originalRegion&&session.Profile.credits==originalCredits,"Bad pack retains working scene and career credits/destination");
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-allocation-refusal.png");
                session.ResumeDrive();yield return new WaitForSecondsRealtime(1);
                Check(!session.Menu&&session.BootReady&&session.Profile.region==originalRegion,"Normal resume returns to the original playable circuit");
                ScreenCapture.CaptureScreenshot(prefix+"-allocation-refusal-resume.png");
                File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_ALLOCATION_REFUSAL_AND_RESUME",["checks"]=new JArray(checks),["region"]=session.Profile.region,["credits"]=session.Profile.credits}.ToString());
                yield return new WaitForSecondsRealtime(.5f);Application.Quit();yield break;
            }
            while(chooser.Busy||chooser.SelectedCircuit==null)yield return null;
            Check(chooser.SelectedLayout=="gp-2026","Normal chooser selects GP layout");
            yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(prefix+"-chooser.png");
            yield return new WaitForSecondsRealtime(.3f);
            string token=chooser.SelectedCircuit.Token;var old=session;
            Check(chooser.StartSession(0,1,0),"Normal start handler enters imported circuit");
            while(true)
            {
                session=FindAnyObjectByType<GameSession>();
                if(Environment.GetCommandLineArgs().Contains("-showcaseCancelCircuitBoot")&&session&&session!=old&&session.World&&session.World.CircuitArt&&!session.World.CircuitArt.InitialDetailReady&&session.World.CircuitArt.ResidentCollisionCount>0)
                {yield return VerifyCancelledCircuitBoot();yield break;}
                if(session&&session!=old&&session.BootReady&&session.World.Route.Region.id==token)break;
                if(session&&session.BootFailed)throw new InvalidOperationException("Circuit boot failed");yield return null;
            }
            Prepare(session);Check(session.World.Route.Region.CircuitScenery!=null,"Normal boot attaches verified environment");
            Check(session.World.CircuitArt.InitialDetailReady&&session.World.CircuitArt.InitialResidentDetailCells<session.World.CircuitArt.DetailCellCount,"Initial admission retains only nearby detailed cells");
            session.SetMenu(false);session.Racing.Cancel();session.Car.StartLocked=true;
            Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            session.GetComponent<DrivingUI>().enabled=false;session.View.enabled=false;
            var camera=session.View.GetComponent<Camera>();camera.farClipPlane=4000;camera.fieldOfView=55;
            bool rainNight=Environment.GetCommandLineArgs().Contains("-showcaseRainNight");
            if(rainNight)
            {
                // Let the ordinary rain/wetness transition settle, rather than
                // injecting conditions or enabling lights from the test itself.
                yield return new WaitForSecondsRealtime(60);
                Check(Mathf.Abs(session.Conditions.Hour-22)<.05f&&session.Conditions.Rain>.65f&&session.Conditions.Wetness>.7f,"Ordinary fixed22h rain/wetness transition settled");
                Check(session.Car.Visual.Headlights.Length==2&&session.Car.Visual.Headlights.All(l=>l&&l.enabled&&l.intensity>0),"Normal automatic headlights enabled in rain/night");
            }
            if(Environment.GetCommandLineArgs().Contains("-showcaseNightCompare"))
            {yield return CompareNightLighting();yield break;}
            var eyes=new[]{new Vector3(-665,100,-700),new Vector3(-550,40,-465),new Vector3(-370,65,-1000),new Vector3(-230,55,-170),new Vector3(230,53,620),new Vector3(-125,55,690),new Vector3(-80,48,320)};
            var targets=new[]{new Vector3(-460,45,-430),new Vector3(-460,44,-440),new Vector3(-470,34,-760),new Vector3(-365,35,-240),new Vector3(235,48,705),new Vector3(-50,42,613),new Vector3(-25,44,250)};
            string[] names={"hamilton-wing","wing-garages","pit-entry","pit-exit","copse-grandstand","national-pit-complex","wellington-bridge"};
            for(int i=0;i<eyes.Length;i++)
            {
                float d=session.World.Route.Closest(targets[i],out _,out _);
                session.Car.Recover(session.World.Route.At(d),session.World.Route.Rotation(d));session.Car.StartLocked=true;
                session.World.GeographicWorld.Refresh(targets[i],true);
                camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i],Vector3.up));
                yield return new WaitForSecondsRealtime(2);yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(prefix+"-"+names[i]+".png");yield return new WaitForSecondsRealtime(.25f);
            }
            if(rainNight)
            {
                session.View.enabled=true;session.View.Mode=0;session.GetComponent<DrivingUI>().enabled=true;
                session.Car.Recover(session.World.Route.At(-8),session.World.Route.Rotation(-8));
                // Fixture placement is not a road jump. Keep the car stopped
                // until all suspension contacts settle before measuring driving.
                session.Car.StartLocked=true;session.Car.NetworkDriven=true;
                session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
                float settleDeadline=Time.unscaledTime+5;int supportedTicks=0;
                while(supportedTicks<20)
                {
                    Check(Time.unscaledTime<settleDeadline,"Wet-night fixture settles within5s");
                    supportedTicks=session.Car.WheelGrounded.All(g=>g)?supportedTicks+1:0;
                    yield return new WaitForFixedUpdate();
                }
                Check(true,"Initial placement settled before measured driving");
                session.Car.StartLocked=false;session.Car.NetworkDriven=true;
                session.Car.NetworkHandbrake=0;session.Car.NetworkBrake=0;
                driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=session.World.Route;driver.Difficulty=0;driver.PaceLimitMetresPerSecond=12;
                driver.Recovered+=_=>recoveries++;
                var wiper=session.Car.Visual.Wipers.First();var beforeWiper=wiper.localRotation;
                yield return new WaitForSecondsRealtime(.3f);
                Check(Quaternion.Angle(beforeWiper,wiper.localRotation)>.1f,"Normal automatic windscreen wipers move in rain");
                observations=new StreamWriter(prefix+"-drive.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");stage="rain-night";previous=session.Car.Body.position;distance=0;driving=true;
                float began=Time.unscaledTime;bool chase=false,cockpit=false;
                while(Time.unscaledTime-began<30)
                {
                    float elapsed=Time.unscaledTime-began;
                    if(!chase&&elapsed>5){ScreenCapture.CaptureScreenshot(prefix+"-rain-night-chase.png");chase=true;}
                    if(!cockpit&&elapsed>15){session.View.Mode=2;yield return new WaitForSecondsRealtime(1);ScreenCapture.CaptureScreenshot(prefix+"-rain-night-cockpit.png");cockpit=true;}
                    CheckDriving();yield return null;
                }
                driving=false;driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;observations.Dispose();observations=null;
                Check(distance>200&&maxDamage<.1f&&recoveries==0&&maxAirborne<.3f,"Actual30s wet-night drive without damage, recovery or sustained airborne interval");
                File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_RAIN_NIGHT_DRIVE",["checks"]=new JArray(checks),["distance"]=distance,["damage"]=maxDamage,["recoveries"]=recoveries,["maxAirborneSeconds"]=maxAirborne,["hour"]=session.Conditions.Hour,["rain"]=session.Conditions.Rain,["wetness"]=session.Conditions.Wetness,["asphaltSmoothness"]=session.World.Asphalt.GetFloat("_Smoothness"),["daylight"]=session.Conditions.Daylight,["displayExposureEV"]=NightExposure(),["listenerVolume"]=AudioListener.volume,["listenerPaused"]=AudioListener.pause,["testRumble"]=session.Profile.rumble,["testWheelForceEnabled"]=session.Profile.wheelForceEnabled}.ToString());
                yield return new WaitForSecondsRealtime(.5f);Application.Quit();yield break;
            }
            if(Environment.GetCommandLineArgs().Contains("-showcaseVisualOnly"))
            {File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_IMPORT_AND_STATIONARY_VIEWS_ONLY",["checks"]=new JArray(checks),["drivenLap"]=false}.ToString());Application.Quit();yield break;}
            session.View.enabled=true;session.View.Mode=0;session.GetComponent<DrivingUI>().enabled=true;
            bool raceRequested=Environment.GetCommandLineArgs().Contains("-showcaseRace");
            session.Racing.Begin(raceRequested?2:1);session.SetMenu(false);session.Car.NetworkDriven=true;
            Check(session.Racing.Running&&session.Racing.Lap!=null,raceRequested?"Normal standing-start race session started":"Normal time-trial session started");
            driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=session.World.Route;driver.Difficulty=0;
            int opponentRecoveries=0;
            if(raceRequested)
            {
                // Use the existing AI avoidance and live race roster. No opponent
                // poses, tuning, timing or classification are overridden by this observer.
                driver.Index=0;driver.Lane=-2.7f;
                driver.Fleet=FindObjectsByType<RaceDriver>(FindObjectsSortMode.None).Select(d=>d.Car).ToList();
                Check(session.Racing.Entrants==4&&driver.Fleet.Count==4,"Normal race creates three physical opponents");
                foreach(var ai in FindObjectsByType<RaceDriver>(FindObjectsSortMode.None))
                    if(ai!=driver)ai.Recovered+=_=>opponentRecoveries++;
            }
            driver.Recovered+=_=>recoveries++;
            observations=new StreamWriter(prefix+"-drive.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");
            while(session.Racing.Countdown>0)yield return null;
            distance=0;previous=session.Car.Body.position;driving=true;
            float nextCapture=1000;bool crossedStart=false;
            bool frameProbe=Environment.GetCommandLineArgs().Contains("-showcaseFrameProbe");
            while(session.Racing.Lap.CompletedLaps<1)
            {
                float progress=session.World.Route.Closest(session.Car.Body.position,out _,out _);
                if(progress<100)crossedStart=true;
                if(crossedStart&&progress>nextCapture&&progress<session.World.Route.Length-100){CaptureDriveView("lap-"+(int)nextCapture);nextCapture+=1000;}
                if(frameProbe&&distance>1700)
                {
                    Check(distance>1700&&maxDamage<.1f&&recoveries==0&&maxAirborne<.3f,"Partial frame diagnostic corridor is driven without incident; not a full lap");
                    driving=false;driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;
                    observations.Dispose();observations=null;WriteStreamActions();
                    File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_PARTIAL_FRAME_DIAGNOSTIC_ONLY",["distance"]=distance,["damage"]=maxDamage,["recoveries"]=recoveries,["maxAirborneSeconds"]=maxAirborne,["checks"]=new JArray(checks)}.ToString());
                    yield return new WaitForSecondsRealtime(.5f);Application.Quit();yield break;
                }
                CheckDriving();yield return null;
            }
            driving=false;driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;
            observations.Dispose();observations=null;
            Check(recoveries==0,"Full normal-physics lap completed without recovery");
            Check(maxDamage<.1f,"Full lap completed without collision damage");
            Check(maxAirborne<.3f,"No sustained airborne jump during driven lap");
            Check(session.Racing.Lap.LastValid,"Lap tracker accepted the actual driven lap");
            float mainDistance=distance,mainDamage=maxDamage,mainAirborne=maxAirborne,lapSeconds=session.Racing.Lap.LastTime;
            yield return new WaitForSecondsRealtime(1);CaptureDriveView("lap-complete");
            if(raceRequested)
            {
                while(!session.Racing.Finished)yield return null;
                Check(session.Racing.Classification.Length==4,"Normal race publishes four-place classification");
                Check(session.Racing.Classification.All(line=>!line.Contains("DNF")),"All three physical opponents complete the race");
                Check(opponentRecoveries==0,"Physical race opponents complete without automatic recovery");
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-race-classification.png");
                File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_PHYSICAL_RACE_AND_CLASSIFICATION",["checks"]=new JArray(checks),["samples"]=samples,["distance"]=mainDistance,["damage"]=mainDamage,["recoveries"]=recoveries,["opponentRecoveries"]=opponentRecoveries,["maxAirborneSeconds"]=mainAirborne,["lapSeconds"]=lapSeconds,["position"]=session.Racing.Position,["classification"]=new JArray(session.Racing.Classification)}.ToString());
                yield return new WaitForSecondsRealtime(.5f);Application.Quit();yield break;
            }
            // Separate, explicitly placed pit-entry fixture. From35m before the
            // real branch through service and50m beyond exit there are NO pose writes.
            session.Racing.Cancel();var main=session.World.Route;var region=main.Region;
            var pit=region.roads.Single(r=>r.id==region.CircuitScenery.PitRoadId);
            float entry=main.Closest(pit.points[0],out _,out _),exit=main.Closest(pit.points[pit.points.Length-1],out _,out _);
            var guide=new List<Vector3>();
            void Add(Vector3 p){if(guide.Count==0||Vector3.Distance(guide[guide.Count-1],p)>.05f)guide.Add(p);}
            for(float d=entry-35;d<entry;d+=3)Add(main.At(d));foreach(var p in pit.points)Add(p);
            for(float d=exit+3;d<=exit+50;d+=3)Add(main.At(d));
            var guideRegion=region.SnapshotCopy();guideRegion.route=guide.ToArray();var pitRoute=new RoutePath(guideRegion);
            session.Car.Recover(pitRoute.At(0),pitRoute.Rotation(0));session.Car.ReplicaDamage(15);
            session.Car.NetworkDriven=true;session.Car.StartLocked=false;
            driver.Route=pitRoute;driver.PaceLimitMetresPerSecond=12;driver.enabled=true;
            float serviceProgress=pitRoute.Closest(region.CircuitScenery.Bay,out _,out _);
            stage="pit";previous=session.Car.Body.position;observations=new StreamWriter(prefix+"-pit-drive.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");driving=true;
            bool serviceStarted=false;float nextPitCapture=100;
            while(!serviceStarted)
            {
                float d=pitRoute.Closest(session.Car.Body.position,out _,out _);
                driver.PaceLimitMetresPerSecond=Mathf.Clamp(Mathf.Sqrt(6*Mathf.Max(0,serviceProgress-d-2)),.05f,12);
                if(session.Car.Damage>15.1f)throw new InvalidOperationException("Collision damage on pit entry before service repair: "+session.Car.LastImpactObject);
                if(d>=nextPitCapture){CaptureDriveView("pit-driven-"+((int)nextPitCapture));nextPitCapture+=100;}
                if(session.Pits.Servicing)serviceStarted=true;
                if(recoveries>0)throw new InvalidOperationException("Recovery during pit entry");yield return null;
            }
            driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;
            Check(session.Car.Damage<=15.1f,"Driven pit entry incurred no damage before the repair could mask it");
            Check(session.Pits.AtBay,"Actual driven approach reached the mapped service bay");
            CaptureDriveView("pit-service");
            while(session.Pits.Servicing)yield return null;
            Check(session.Car.Damage<.1f,"Normal free pit service repaired the injected fixture damage");
            driver.PaceLimitMetresPerSecond=12;driver.enabled=true;session.Car.StartLocked=false;
            while(pitRoute.Closest(session.Car.Body.position,out _,out _)<pitRoute.Length-10)
            {if(recoveries>0||session.Car.Damage>1)throw new InvalidOperationException("Incident during pit exit");
             float d=pitRoute.Closest(session.Car.Body.position,out _,out _);
             if(d>=nextPitCapture){CaptureDriveView("pit-driven-"+((int)nextPitCapture));nextPitCapture+=100;}
             yield return null;}
            driving=false;driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;
            observations.Dispose();observations=null;Check(recoveries==0,"Driven mapped pit entry/service/exit completed without recovery");
            CaptureDriveView("pit-exit-driven");
            distance=mainDistance;maxDamage=mainDamage;maxAirborne=mainAirborne;
            WriteStreamActions();
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_IMPORT_VIEWS_AND_DRIVEN_LAP",["checks"]=new JArray(checks),["samples"]=samples,["distance"]=distance,["damage"]=maxDamage,["recoveries"]=recoveries,["maxAirborneSeconds"]=maxAirborne,["lapSeconds"]=lapSeconds,["pitDriveVerified"]=true,["detailCells"]=session.World.CircuitArt.DetailCellCount,["initialDetailCells"]=session.World.CircuitArt.InitialResidentDetailCells,["residentDetailCells"]=session.World.CircuitArt.ResidentDetailCells,["detailCellsBuilt"]=session.World.CircuitArt.DetailCellsBuilt,["detailCellsRetired"]=session.World.CircuitArt.DetailCellsRetired,["residentDetailVertices"]=session.World.CircuitArt.ResidentDetailVertices,["collisionCount"]=session.World.CircuitArt.ResidentCollisionCount,["maximumSliceMilliseconds"]=session.World.CircuitArt.MaximumSliceMilliseconds}.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }
        IEnumerator VerifyCancelledCircuitBoot()
        {
            var pending=session;var world=pending.World;var art=world.CircuitArt;
            string garage=JsonUtility.ToJson(pending.Profile.garage);int credits=pending.Profile.credits;
            var assets=world.GetComponentsInChildren<CircuitShowcaseScenery>().SelectMany(owner=>(List<UnityEngine.Object>)typeof(CircuitShowcaseScenery).GetField("owned",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(owner)).ToArray();
            Check(!pending.BootReady&&pending.Car==null&&art.ResidentCollisionCount>0,"Cancellation fixture reaches real streamed collision allocation before actors");
            pending.CancelBoot();yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.25f);
            Check(pending.BootFailed&&!pending.BootReady&&pending.World==null&&pending.Car==null,"Normal Cancel preparation rolls back the incomplete world");
            Check(!world&&!art&&assets.All(item=>!item)&&string.IsNullOrEmpty(pending.BootCleanupStatus),"Actual cancelled world releases owned meshes/materials without cleanup warnings");
            Check(pending.Profile.credits==credits&&JsonUtility.ToJson(pending.Profile.garage)==garage,"Cancelled circuit preparation keeps career car and credits");
            ScreenCapture.CaptureScreenshot(prefix+"-cancelled-boot.png");yield return new WaitForSecondsRealtime(.3f);
            Check(pending.CanReturnFromFailedLocation&&pending.ReturnToCountyKeepingCareer(),"Normal error-screen county recovery is available after cancellation");
            while(true)
            {
                session=FindAnyObjectByType<GameSession>();
                if(session&&session!=pending&&session.BootReady)break;
                if(session&&session!=pending&&session.BootFailed)throw new InvalidOperationException("County recovery failed");yield return null;
            }
            Check(session.Profile.region==""&&session.Menu&&session.Profile.credits==credits&&JsonUtility.ToJson(session.Profile.garage)==garage,"County menu opens with existing car and credits after cancellation");
            ScreenCapture.CaptureScreenshot(prefix+"-cancelled-boot-recovered.png");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_NATIVE_STREAM_CANCEL_CLEANUP_AND_COUNTY_RECOVERY",["checks"]=new JArray(checks),["credits"]=credits,["ownedAssetsChecked"]=assets.Length}.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }
        // Private observer comparison: capture itself may stall rendering.
        // Record each request before optionally suppressing only driving PNGs.
        void CaptureDriveView(string name)
        {
            driveCaptureEvents.Add(new JObject{["name"]=name,["frame"]=Time.frameCount,["stage"]=driving?stage:"views-or-menu",["realtimeSeconds"]=Time.realtimeSinceStartupAsDouble,["utcMilliseconds"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),["suppressed"]=noDriveScreenshots});
            if(!noDriveScreenshots)ScreenCapture.CaptureScreenshot(prefix+"-"+name+".png");
        }
        void WriteStreamActions(){File.WriteAllText(prefix+"-drive-capture-events.json",new JArray(driveCaptureEvents).ToString());File.WriteAllText(prefix+"-stream-actions.json",Newtonsoft.Json.JsonConvert.SerializeObject(session.World.CircuitArt.CaptureActions(),Newtonsoft.Json.Formatting.Indented));}
        void CheckDriving(){if(recoveries>0||session.Car.Damage>1)throw new InvalidOperationException("Driven lap incident; inspect retained observations");}
        void FixedUpdate()
        {
            if(!driving||!session||!session.BootReady)return;
            var car=session.Car;var p=car.Body.position;distance+=Vector3.Distance(previous,p);previous=p;maxDamage=Mathf.Max(maxDamage,car.Damage);
            bool grounded=car.WheelGrounded.Any(x=>x);airborne=grounded?0:airborne+Time.fixedDeltaTime;maxAirborne=Mathf.Max(maxAirborne,airborne);
            float progress=session.World.Route.Closest(p,out _,out float separation);samples++;
            observations.WriteLine(FormattableString.Invariant($"{stage},{Time.fixedTime:F3},{progress:F3},{separation:F3},{car.AbsoluteSpeed:F3},{p.x:F3},{p.y:F3},{p.z:F3},{car.Damage:F3},{grounded}"));
        }
        void OnDestroy(){observations?.Dispose();CloseFrameTiming();}
    }
}
