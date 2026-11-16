// Private opt-in acceptance through the existing capture entry point. Storage,
// mute and lifecycle guards are shared with ShowcaseCircuitCapture.Initialize.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class ShowcaseCircuitCapture
    {
        IEnumerator RunAuthoredRecoveryFixture()
        {
            Check(AudioListener.volume==0&&AudioListener.pause,"Muted before recovery acceptance");
            Prepare(session);Check(session.TrySave(),"Isolated starting career saved");
            byte[] startingSave=File.ReadAllBytes(PlayerProfile.StoragePath);
            string startingRegion=session.Profile.region;int startingCredits=session.Economy.Credits;
            var startingSession=session;var ui=session.GetComponent<DrivingUI>();ui.ShowImportedPacks();
            var library=ui.ImportedPacks;
            while(library.Busy)yield return null;AuthoredPhase("library-ready");library.ImportArchive(archive);
            while(library.Busy)yield return null;AuthoredPhase("archive-imported");library.CheckContent(0);
            while(library.Busy)yield return null;
            Check(library.Status.Contains("1/1 driving layouts readable"),"Catalogue readability is separate from physical admission");
            Check(library.OpenCircuitRevision(0),"Normal chooser opens invalid physical fixture");
            var chooser=ui.ImportedCircuits;
            while(chooser.Busy)yield return null;
            Check(chooser.SelectedCircuit==null&&chooser.Status.Contains("unsupported pavement gap"),"Normal chooser explains physical geometry refusal");
            Check(!chooser.StartSession(0,1,0),"Rejected geometry cannot launch a scene");
            Check(session==startingSession&&session.BootReady&&!session.BootFailed&&session.Profile.region==startingRegion&&session.Economy.Credits==startingCredits,"Refusal retains working scene, destination and credits");
            Check(File.ReadAllBytes(PlayerProfile.StoragePath).SequenceEqual(startingSave),"Geometry refusal leaves isolated career bytes unchanged");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-authored-refusal.png");
            yield return new WaitForSecondsRealtime(.2f);
            ui.ShowImportedPacks();while(library.Busy)yield return null;
            Check(ui.ImportedCircuits==null&&library.RevisionCount==1,"Normal Back handler returns to pack library");
            Check(library.OpenCircuitRevision(0),"A subsequent check can start");
            var cancelled=ui.ImportedCircuits;
            var field=typeof(CircuitPackMenu).GetField("work",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var pending=(System.Threading.Tasks.Task)field.GetValue(cancelled);
            Check(cancelled.Busy&&pending!=null,"Captured actual outstanding chooser task before Back");
            ui.ShowImportedPacks();float cancelBegan=Time.unscaledTime;
            while(!pending.IsCompleted)
            {if(Time.unscaledTime-cancelBegan>10)throw new TimeoutException("Disposed chooser worker did not terminate");yield return null;}
            cancelled.Poll();
            Check(cancelled.SelectedCircuit==null&&ui.ImportedCircuits==null,"Disposed worker cannot publish a late selection");
            Check(File.ReadAllBytes(PlayerProfile.StoragePath).SequenceEqual(startingSave),"Back/cancel leaves isolated career bytes unchanged");
            session.ResumeDrive();yield return new WaitForSecondsRealtime(.5f);
            Check(!session.Menu&&session.BootReady&&session==startingSession,"Normal Resume returns to original playable scene");
            var position=session.Car.Body.position;session.Car.NetworkDriven=true;session.Car.StartLocked=false;
            session.Car.NetworkThrottle=.3f;session.Car.NetworkBrake=0;session.Car.NetworkHandbrake=0;session.Car.NetworkSteer=0;
            yield return new WaitForSecondsRealtime(2);
            float resumedDistance=Vector3.Distance(position,session.Car.Body.position);
            session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            Check(resumedDistance>1&&session.Car.Damage<.1f,"Original drive responds to throttle after refusal/cancel");
            ScreenCapture.CaptureScreenshot(prefix+"-authored-refusal-resume.png");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{{"status","PASS_AUTHORED_REFUSAL_BACK_CANCEL_RESUME"},{"checks",new JArray(checks)},{"workerStatusAfterBack",pending.Status.ToString()},{"resumedDistance_m",resumedDistance},{"startingCredits",startingCredits},{"startingRegion",startingRegion},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"scope","Normal Player library/chooser physical-refusal, Back disposal with actual task termination, unchanged isolated career and physical resume. Not cancellation during scene construction or Windows hardware acceptance."}}.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }

        IEnumerator VerifyAuthoredCancelledBoot()
        {
            var pending=session;var world=pending.World;
            var colliders=world.GetComponentsInChildren<MeshCollider>(true);
            var meshes=world.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(o=>o.Mesh).Where(m=>m).Distinct().ToArray();
            int credits=pending.Profile.credits;string garage=JsonUtility.ToJson(pending.Profile.garage);
            Check(!pending.BootReady&&pending.Car==null&&colliders.Any(c=>c.name=="Authored circuit pavement")&&meshes.Length>0,"Real authored road allocation reached before actors");
            pending.CancelBoot();yield return new WaitForEndOfFrame();yield return new WaitForSecondsRealtime(.25f);
            Check(pending.BootFailed&&!pending.BootReady&&pending.World==null&&pending.Car==null,"Normal Cancel preparation rolls back authored scene");
            Check(!world&&meshes.All(m=>!m)&&colliders.All(c=>!c)&&string.IsNullOrEmpty(pending.BootCleanupStatus),"Cancelled authored chunks release actual owned meshes and colliders");
            Check(pending.Profile.credits==credits&&JsonUtility.ToJson(pending.Profile.garage)==garage,"Authored cancellation retains car condition and credits");
            ScreenCapture.CaptureScreenshot(prefix+"-authored-cancelled-boot.png");yield return new WaitForSecondsRealtime(.3f);
            Check(pending.CanReturnFromFailedLocation&&pending.ReturnToCountyKeepingCareer(),"Normal county recovery accepts cancelled authored destination");
            float began=Time.unscaledTime;
            while(true)
            {
                session=FindAnyObjectByType<GameSession>();
                if(session&&session!=pending&&session.BootReady)break;
                if(session&&session!=pending&&session.BootFailed)throw new InvalidOperationException("County recovery after authored cancellation failed");
                if(Time.unscaledTime-began>60)throw new TimeoutException("County recovery did not become ready");yield return null;
            }
            Prepare(session);
            Check(session.Profile.region==""&&session.Menu&&session.Profile.credits==credits&&JsonUtility.ToJson(session.Profile.garage)==garage,"County menu is usable with original garage and credits");
            Check(AudioListener.volume==0&&AudioListener.pause,"Recovery remains muted");
            ScreenCapture.CaptureScreenshot(prefix+"-authored-cancelled-recovered.png");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{{"status","PASS_AUTHORED_SCENE_CANCEL_CLEANUP_COUNTY_RECOVERY"},{"checks",new JArray(checks)},{"ownedMeshesChecked",meshes.Length},{"collidersChecked",colliders.Length},{"credits",credits},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"scope","Normal Player cancellation after real authored collision allocation, owned mesh/collider destruction and normal county recovery. Not Windows hardware acceptance or arbitrary cancellation timing exhaustiveness."}}.ToString());
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }

        readonly List<JObject> authoredPhases=new List<JObject>();
        void AuthoredPhase(string name)
        {
            if(!Environment.GetCommandLineArgs().Contains("-showcaseBootTiming")||authoredPhases.Count>=64)return;
            // Explicit QA-only phase journal. No career reads/writes or runtime
            // policy changes; monotonic wall time is not process/GPU CPU time.
            authoredPhases.Add(new JObject{{"phase",name},{"realtimeSeconds",Time.realtimeSinceStartupAsDouble},{"frame",Time.frameCount}});
            File.WriteAllText(prefix+"-authored-phases.json",new JArray(authoredPhases).ToString());
            File.WriteAllText(prefix+"-boot-timing.json",JsonUtility.ToJson(BootTelemetry.Capture(),true));
        }
        IEnumerator RunAuthoredFixture()
        {
            if(Environment.GetCommandLineArgs().Contains("-authoredRecoveryFixture")){yield return RunAuthoredRecoveryFixture();yield break;}
            AuthoredPhase("fixture-begin");
            bool singleSeatSetup=Environment.GetCommandLineArgs().Contains("-authoredSingleSeatFixture");
            bool realSource=Environment.GetCommandLineArgs().Contains("-authoredRealSourceFixture");
            bool nationalService=Environment.GetCommandLineArgs().Contains("-authoredNationalServiceFixture");
            Check(!nationalService||realSource,"National service uses explicit real-source fixture");
            string fittedSetupKey=null;
            Check(AudioListener.volume==0&&AudioListener.pause,"Muted before authored Player acceptance");
            if(singleSeatSetup)Check(session.DrivenCar.model==3,"Isolated owned single-seat fixture loaded normally");
            Prepare(session);session.GetComponent<DrivingUI>().ShowImportedPacks();
            var library=session.GetComponent<DrivingUI>().ImportedPacks;
            while(library.Busy)yield return null;AuthoredPhase("library-ready");library.ImportArchive(archive);
            while(library.Busy)yield return null;AuthoredPhase("archive-imported");library.CheckContent(0);
            while(library.Busy)yield return null;
            AuthoredPhase("content-checked");
            Check(library.Status.Contains("1/1 driving layouts readable"),"Normal library recognises authored layout");
            Check(library.OpenCircuitRevision(0),"Normal library opens authored circuit chooser");
            var chooser=session.GetComponent<DrivingUI>().ImportedCircuits;
            while(chooser.Busy||chooser.SelectedCircuit==null)yield return null;
            AuthoredPhase("default-layout-preview-ready");
            Check(chooser.SelectedLayout==(realSource?"gp":"loop"),"Normal chooser selects declared authored layout");
            if(nationalService||Environment.GetCommandLineArgs().Contains("-authoredContinuousPitLapFixture")||Environment.GetCommandLineArgs().Contains("-authoredPitRecoveryFixture"))
            {
                string eventId=nationalService?"gp-national-service":"gp-wing-service";
                AuthoredPhase("event-preview-start");chooser.SelectEvent(eventId);float eventBegan=Time.unscaledTime;
                while(chooser.Busy){if(Time.unscaledTime-eventBegan>180)throw new TimeoutException("National event selection exceeded bounded window");yield return null;}
                AuthoredPhase("event-preview-ready");Check(chooser.SelectedEvent==eventId&&chooser.SelectedCircuit.Event.PitComplexId==(nationalService?"national":"wing"),"Normal chooser selects explicit service event independently of GP layout");
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-authored-chooser.png");
            yield return null; // Finish the chooser capture before switching scenes.
            var old=session;string token=chooser.SelectedCircuit.Token;
            AuthoredPhase("scene-request");Check(chooser.StartSession(0,1,0),"Normal start handler launches authored course");
            while(true)
            {
                session=FindAnyObjectByType<GameSession>();
                if(session&&session.BootFailed)throw new Exception("Authored circuit boot failed");
                if(Environment.GetCommandLineArgs().Contains("-authoredCancelBootFixture")&&session&&session!=old&&session.World&&!session.BootReady&&session.Car==null&&session.World.GetComponentsInChildren<MeshCollider>(true).Any(c=>c.name=="Authored circuit pavement"))
                {yield return VerifyAuthoredCancelledBoot();yield break;}
                if(session&&session!=old&&session.BootReady&&session.World.Route.Region.id==token)
                {
                    if(Environment.GetCommandLineArgs().Contains("-authoredCancelBootFixture"))throw new InvalidOperationException("No cancellable authored allocation stage observed");
                    break;
                }
                yield return null;
            }
            AuthoredPhase("owned-scene-boot-ready");Prepare(session);var region=session.World.Route.Region;
            Check(region.CircuitRoadSurfaces!=null,"Player boot preserves authored physical surfaces");
            if(nationalService)
            {
                Check(region.CircuitScenery.PitComplexId=="national"&&region.CircuitScenery.PitRoadId=="silverstone-national-pit-mapped","Boot binds selected National pit complex");
                Check(Vector3.Distance(session.Pits.Bay,region.CircuitScenery.Bay)<.001f&&session.Pits.OnLane(session.Pits.Bay),"Normal pit box and lane detector use National anchor");
                var inactive=region.roads.Single(r=>r.id=="silverstone-international-pit-mapped").points;
                Check(!session.Pits.OnLane(inactive[inactive.Length/2]),"Inactive Wing lane is not the selected service lane");
                var marks=session.Pits.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="Stop box edge"||m.name=="Stop line").ToArray();
                Check(marks.Length==3,"Selected authored pit uses three surface-following paint ribbons");
                float minimumPaintClearance=float.MaxValue;
                foreach(var mark in marks)
                {
                    var mesh=mark.sharedMesh;var vertices=mesh.vertices.Select(mark.transform.TransformPoint).ToArray();
                    foreach(var p in vertices){Check(region.CircuitRoadSurfaces.TryHeight(p.x,p.z,out float y)&&Mathf.Abs(p.y-y-.012f)<.001f,"Paint vertex follows selected pit asphalt");}
                    var indices=mesh.triangles;
                    for(int i=0;i<indices.Length;i+=3)
                    {var p=(vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3;Check(region.CircuitRoadSurfaces.TryHeight(p.x,p.z,out float y),"Paint triangle remains on physical pavement");minimumPaintClearance=Mathf.Min(minimumPaintClearance,p.y-y);}
                }
                Check(minimumPaintClearance>.001f,"Stop-box paint is not buried between sampled vertices");
            }
            if(Environment.GetCommandLineArgs().Contains("-authoredPitRecoveryFixture")){yield return RunPitRecoveryFixture(region);yield break;}
            if(Environment.GetCommandLineArgs().Contains("-authoredContinuousPitLapFixture")){yield return RunContinuousPitLap(region);yield break;}
            session.SetMenu(false);session.Racing.Cancel();session.Car.NetworkDriven=true;
            session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            session.Car.StartLocked=false;yield return new WaitForSecondsRealtime(2);
            driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.enabled=false;
            driver.Car=session.Car;driver.Session=session;driver.Route=session.World.Route;
            driver.Difficulty=0;driver.PaceLimitMetresPerSecond=realSource?18:4;
            driver.Recovered+=_=>recoveries++;
            observations=new StreamWriter(prefix+"-authored-drive.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");
            stage="authored-main";previous=session.Car.Body.position;distance=0;driving=true;driver.enabled=true;
            float began=Time.unscaledTime,lastProgress=session.World.Route.Closest(session.Car.Body.position,out _,out _),advanced=0;
            bool captured=false;
            while(!nationalService&&advanced<session.World.Route.Length+5)
            {
                if(Time.unscaledTime-began>=(realSource?600:180))throw new TimeoutException("Main loop exceeded bounded driving window");CheckDriving();
                float progress=session.World.Route.Closest(session.Car.Body.position,out _,out _);
                advanced+=Core.DrivingMath.SignedRouteDelta(lastProgress,progress,session.World.Route.Length);lastProgress=progress;
                if(!captured&&Time.unscaledTime-began>10){ScreenCapture.CaptureScreenshot(prefix+"-authored-main.png");captured=true;}
                yield return null;
            }
            driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;
            if(!nationalService)Check(maxDamage<.1f&&recoveries==0&&maxAirborne<.3f,"Full main loop without damage/recovery/sustained airborne interval");
            float mainDistance=distance;
            // Separate explicit initial placement for pit acceptance. No pose
            // changes or recoveries are permitted during either measured drive.
            var guideRegion=region.SnapshotCopy();guideRegion.CircuitRoadSurfaces=region.CircuitRoadSurfaces;
            guideRegion.route=region.roads.Single(r=>r.id==region.CircuitScenery.PitRoadId).points;
            var pitRoute=new RoutePath(guideRegion);driver.Route=pitRoute;driver.PaceLimitMetresPerSecond=realSource?8:4;
            session.Car.Recover(pitRoute.At(0),pitRoute.Rotation(0));
            session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            yield return new WaitForSecondsRealtime(2);
            AuthoredPhase("pit-drive-start");previous=session.Car.Body.position;distance=0;stage="authored-pit";began=Time.unscaledTime;driving=true;driver.enabled=true;
            bool serviceVerified=false;int repairCreditsBefore=-1;float serviceSeconds=0,crewMovement=0;
            float bayStation=pitRoute.Closest(session.Pits.Bay,out _,out _);
            while(pitRoute.Closest(session.Car.Body.position,out _,out _)<pitRoute.Length-2)
            {
                if(Time.unscaledTime-began>=(realSource?240:90))throw new TimeoutException("Pit entry and exit exceeded bounded driving window");CheckDriving();
                if(!serviceVerified&&pitRoute.Closest(session.Car.Body.position,out _,out _)>=bayStation-3)
                {
                    // Explicit isolated damage fixture, after a clean physical
                    // drive to the bay. Service itself uses the normal Update,
                    // six-second crew sequence and save transaction, not a direct repair.
                    driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;
                    session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
                    float stopped=Time.unscaledTime;
                    while(session.Car.AbsoluteSpeed>.2f)
                    {if(Time.unscaledTime-stopped>5)throw new TimeoutException("Cannot stop at authored pit bay");yield return null;}
                    Check(session.Pits.AtBay,"Physical approach stops inside declared authored bay");
                    repairCreditsBefore=session.Economy.Credits;
                    session.Car.ReplicaDamage(25);session.SyncActiveCondition();
                    Check(session.TrySave(),"Isolated pre-service damage fixture saved");
                    Check(PlayerProfile.Load().garage.Active.damage>=25,"Pre-service saved damage exists");
                    string otherCarsBefore=JsonUtility.ToJson(new Core.GarageState{activeId="",cars=session.Profile.garage.cars.Where(c=>c.id!=session.DrivenCar.id).Select(c=>c.Clone()).ToList()});
                    if(singleSeatSetup)
                    {
                        var requested=session.Car.RaceSetup.Clone();requested.compound=Core.RaceCompound.Wet;
                        requested.finalDrive=1.1f;requested.brakeBias=.59f;requested.spring=1.15f;
                        requested.differential=.65f;requested.frontWing=.75f;requested.rearWing=.8f;
                        fittedSetupKey=requested.Identity();
                        Check(session.Pits.RequestSetup(requested),"Normal pit handler accepts owned single-seat setup and wet tyres");
                    }
                    var mechanics=session.Pits.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith(singleSeatSetup?"Original mechanic ":"Pit mechanic ")).ToArray();
                    Check(mechanics.Length==4,"Four mechanics belong to the active car service");
                    var homes=mechanics.Select(t=>t.position).ToArray();float serviceBegan=Time.unscaledTime;
                    while(!session.Pits.Servicing)
                    {if(Time.unscaledTime-serviceBegan>3)throw new TimeoutException("Normal pit service did not begin");yield return null;}
                    Check(session.Car.StartLocked,"Pit service locks propulsion");bool crewCaptured=false,bringCaptured=false,returnCaptured=false;
                    while(session.Pits.Servicing)
                    {
                        if(Time.unscaledTime-serviceBegan>10)throw new TimeoutException("Normal pit service did not finish");
                        for(int i=0;i<mechanics.Length;i++)crewMovement=Mathf.Max(crewMovement,Vector3.Distance(homes[i],mechanics[i].position));
                        int VisibleTyres(string name)=>session.Pits.GetComponentsInChildren<Transform>(true).Count(t=>t.name==name&&t.gameObject.activeInHierarchy);
                        float serviceElapsed=Time.unscaledTime-serviceBegan;
                        if(singleSeatSetup&&!bringCaptured&&serviceElapsed>1)
                        {
                            Check(VisibleTyres("Replacement tyre")==4&&VisibleTyres("Removed tyre")==0,"Crew bring four replacement tyres");
                            ScreenCapture.CaptureScreenshot(prefix+"-authored-bring-tyres.png");bringCaptured=true;
                        }
                        if(!crewCaptured&&serviceElapsed>3.15f)
                        {
                            if(singleSeatSetup)Check(VisibleTyres("Replacement tyre")==0&&VisibleTyres("Removed tyre")==4,"Crew exchange replacement and removed tyre props");
                            if(nationalService&&!singleSeatSetup)
                            {
                                foreach(var mechanic in mechanics){var p=mechanic.position;Check(region.CircuitRoadSurfaces.TryHeight(p.x,p.z,out float y)&&Mathf.Abs(y-p.y)<.005f,"Selected service mechanic stands on physical pit pavement");}
                            }
                            ScreenCapture.CaptureScreenshot(prefix+"-authored-pit-crew.png");crewCaptured=true;
                        }
                        if(singleSeatSetup&&!returnCaptured&&serviceElapsed>5)
                        {
                            Check(VisibleTyres("Removed tyre")==4,"Crew carry four old tyres back");
                            ScreenCapture.CaptureScreenshot(prefix+"-authored-return-tyres.png");returnCaptured=true;
                        }
                        yield return null;
                    }
                    serviceSeconds=Time.unscaledTime-serviceBegan;
                    Check(serviceSeconds>=5.8f&&serviceSeconds<10,"Normal six-second pit service elapsed");
                    Check(crewMovement>.5f,"Mechanics approach the stopped car");
                    Check(session.Car.Damage==0&&!session.Car.StartLocked,"Normal service repairs and unlocks car");
                    var saved=PlayerProfile.Load();
                    Check(saved.garage.Active.damage==0,"Completed free repair persisted in isolated career");
                    Check(session.Economy.Credits==repairCreditsBefore&&saved.credits==repairCreditsBefore,"Free service preserves credit balance");
                    if(singleSeatSetup)
                    {
                        Check(session.Car.RaceSetup.Identity()==fittedSetupKey&&saved.garage.Active.raceSetup.Identity()==fittedSetupKey,"Fitted setup matches live car and reloaded owned career");
                        Check(session.Car.RaceTyres.compound==Core.RaceCompound.Wet&&saved.garage.Active.raceTyres.compound==Core.RaceCompound.Wet&&session.Car.RaceTyres.wear<.00001f&&saved.garage.Active.raceTyres.wear==0,"Fresh wet tyres fitted live and persisted");
                        string otherCarsAfter=JsonUtility.ToJson(new Core.GarageState{activeId="",cars=saved.garage.cars.Where(c=>c.id!=saved.garage.activeId).Select(c=>c.Clone()).ToList()});
                        Check(otherCarsBefore==otherCarsAfter,"Other owned cars remain unchanged by pit fitting");
                    }
                    serviceVerified=true;previous=session.Car.Body.position;driving=true;driver.enabled=true;
                }
                yield return null;
            }
            AuthoredPhase("pit-drive-and-service-complete");Check(serviceVerified,"Authored pit transaction completed during physical lane traversal");
            driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;observations.Dispose();observations=null;
            Check(maxDamage<.1f&&recoveries==0&&maxAirborne<.3f,"Pit entry/exit without damage/recovery/sustained airborne interval");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-authored-pit-exit.png");
            yield return null; // Do not teleport to the National fixture in this capture frame.
            float wingDistance=distance;
            if(nationalService)realNationalDistance=wingDistance;
            else if(realSource)yield return DriveRealNationalPit(region);
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{{"status",nationalService?"PASS_NATIONAL_EVENT_SERVICE_PLAYER":realSource?"PASS_REAL_SOURCE_PLAYER_GP_AND_BOTH_PITS":"PASS_AUTHORED_PLAYER_LOOP_AND_PIT"},{"checks",new JArray(checks)},{"mainDistance",mainDistance},{"pitDistance",wingDistance},{"nationalPitDistance",realNationalDistance},{"damage",maxDamage},{"recoveries",recoveries},{"maxAirborneSeconds",maxAirborne},{"freeRepairCredits",repairCreditsBefore},{"serviceSeconds",serviceSeconds},{"crewMovement",crewMovement},{"singleSeatSetupVerified",singleSeatSetup},{"fittedSetupIdentity",fittedSetupKey},{"listenerVolume",AudioListener.volume},{"listenerPaused",AudioListener.pause},{"scope",nationalService?"Normal Player library/event selection/boot binds National box and crew; full selected pit drive and free service/save, explicit initial pit placement only. GP lap deliberately omitted; no scenery accuracy or Windows hardware acceptance.":realSource?"Normal Player library/import/start, stock controller and existing RaceDriver: complete real-source GP lap and both mapped pit routes, normal free Wing service/save. Only explicit initial pit placements; no pose writes during measured drives. Estimated geometry, not track accuracy or Windows hardware acceptance.":"Normal Player library/import/start, stock controller driven by existing RaceDriver. Synthetic fixture with isolated25damage seed and normal six-second free repair/save/crew sequence; single-seat fitting verified only when explicitly flagged. Not Silverstone accuracy or Windows hardware acceptance."}}.ToString());
            AuthoredPhase("fixture-complete");yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }
        float realNationalDistance;
        IEnumerator DriveRealNationalPit(GeoRegion region)
        {
            var guide=region.SnapshotCopy();guide.CircuitRoadSurfaces=region.CircuitRoadSurfaces;
            guide.route=region.roads.Single(r=>r.id=="silverstone-national-pit-mapped").points;
            var route=new RoutePath(guide);driver.Route=route;driver.PaceLimitMetresPerSecond=8;
            session.Car.Recover(route.At(0),route.Rotation(0));
            session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            yield return new WaitForSecondsRealtime(2);
            previous=session.Car.Body.position;distance=0;stage="real-national-pit";
            observations=new StreamWriter(prefix+"-national-pit-drive.csv");observations.WriteLine("stage,time,progress,separation,speed,x,y,z,damage,grounded");
            float began=Time.unscaledTime;driving=true;driver.enabled=true;int capture=0;
            while(route.Closest(session.Car.Body.position,out _,out _)<route.Length-2)
            {
                if(Time.unscaledTime-began>=210)throw new TimeoutException("National pit route exceeded bounded driving window");
                CheckDriving();float progress=route.Closest(session.Car.Body.position,out _,out _);
                if(capture<3&&progress>route.Length*(.1f+.4f*capture))
                {ScreenCapture.CaptureScreenshot(prefix+"-national-pit-"+capture+".png");capture++;}
                yield return null;
            }
            driver.enabled=false;driving=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            observations.Dispose();observations=null;realNationalDistance=distance;
            Check(distance>route.Length*.95f&&maxDamage<.1f&&recoveries==0&&maxAirborne<.3f,"Complete National pit traversal without damage/recovery/sustained airborne interval");
            Check(AudioListener.volume==0&&AudioListener.pause,"Real-source Player remains muted");
        }

    }
}
