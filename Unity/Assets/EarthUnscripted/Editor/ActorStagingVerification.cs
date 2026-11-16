using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad]public static class ActorStagingVerification
    {
        const string Key="EUActorStaging";const int PatrolSeed=24601;
        static string root,realPath,realHash,profileHash,epoch;static int phase,checks,errors,frame=-1,actorFrames,cancelFrame,motorCommands,earlyMotorCommands;
        static double deadline,physicsStarted;static float physicsSimulationStarted,physicsFixedStarted;static GameSession session,previous;static Gamepad pad;static Delegate commandHook;
        static readonly Dictionary<Rigidbody,Vector3> frozen=new Dictionary<Rigidbody,Vector3>();static int positiveFrame=-1,heldChunks=-1;static bool positiveChecked;
        static readonly Dictionary<int,CarSnapshot> regularGolden=new Dictionary<int,CarSnapshot>();static readonly Dictionary<int,float> patrolDraws=new Dictionary<int,float>();
        static readonly HashSet<int> setupFrames=new HashSet<int>();static bool sawFive,sawPatrol;static PoliceDriver drivingPatrol;static Vector3 drivingInitial;static bool previousDriverEnabled;
        [Serializable]sealed class CarSnapshot{public Vector3 position;public Quaternion rotation;public float distance;public int direction;public Color colour;}
        static ActorStagingVerification()
        {
            if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");realPath=SessionState.GetString(Key+"Real","");realHash=SessionState.GetString(Key+"Hash","");epoch=SessionState.GetString(Key+"Epoch","");
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");GameSession.VerificationPatrolSeed=PatrolSeed;BootTelemetry.EnableIsolated();
            deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");root=Path.GetFullPath("Builds/Verification/actor-staging-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
            realPath=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=Hash(realPath);PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
            var data=WorldDataFinalizer.PrepareOwned(GeoRegion.Load("dee-valley"),null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);epoch=WorldSnapshotCodec.Epoch(data.Region);
            var p=new PlayerProfile{region="dee-valley",credits=4321,quality=0,volume=0,rumble=.4f,livingScenery=false,detailedScenery=false,weatherMode=0,clockMode=0,fixedHour=14,wheelEnabled=false,wheelForceEnabled=false};p.garage.Active.engine=2;p.garage.Active.damage=11;p.garage.cars.Add(new OwnedCar{id="actor-spare",model=1,engine=1});File.WriteAllText(PlayerProfile.StoragePath,JsonUtility.ToJson(p));
            SessionState.SetString(Key+"Root",root);SessionState.SetString(Key+"Real",realPath);SessionState.SetString(Key+"Hash",realHash);SessionState.SetString(Key+"Epoch",epoch);SessionState.SetBool(Key,true);GameSession.AutoStartCircuit=false;EditorApplication.EnterPlaymode();
        }
        static string Hash(string path)=>File.Exists(path)?DataPackageRules.HashFile(path):"missing";
        static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors++;}
        static void Check(bool value,string why){if(!value)throw new Exception("ACTOR STAGING FAIL: "+why);checks++;Debug.Log("ACTOR STAGING PASS: "+why);}
        static T[] All<T>()where T:UnityEngine.Object=>UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        // InputSystem's public delegate has a pointer parameter. A tiny emitted IL
        // adapter forwards it as IntPtr without changing project allowUnsafeCode.
        // It is Editor-only, uses the installed delegate's exact signature and only
        // inspects the trusted command header while the synchronous callback lives.
        static void InstallVirtualPad()
        {
            var evt=typeof(InputSystem).GetEvent("onDeviceCommand",BindingFlags.Public|BindingFlags.Static);var invoke=evt.EventHandlerType.GetMethod("Invoke");var parameters=invoke.GetParameters().Select(p=>p.ParameterType).ToArray();
            var bridge=new DynamicMethod("ActorCommandBridge",invoke.ReturnType,parameters,typeof(ActorStagingVerification),true);var il=bridge.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Call,typeof(ActorStagingVerification).GetMethod(nameof(Command),BindingFlags.NonPublic|BindingFlags.Static));il.Emit(OpCodes.Ret);commandHook=bridge.CreateDelegate(evt.EventHandlerType);evt.AddEventHandler(null,commandHook);
            pad=InputSystem.AddDevice<Gamepad>("Actor staging virtual pad");pad.MakeCurrent();
        }
        static long? Command(InputDevice device,IntPtr pointer)
        {
            var header=Marshal.PtrToStructure<InputDeviceCommand>(pointer);if(header.type!=new FourCC('R','M','B','L'))return null;
            if(device==pad){motorCommands++;if(session&&!session.BootReady&&session.BootTrafficPrepared>0)earlyMotorCommands++;return InputDeviceCommand.GenericSuccess;}return null;
        }
        static CarSnapshot Snap(TrafficCar car)=>new CarSnapshot{position=car.transform.position,rotation=car.transform.rotation,distance=car.Distance,direction=car.Direction,colour=car.Visual.Body.sharedMaterial.color};
        static bool Same(CarSnapshot a,CarSnapshot b)=>Vector3.Distance(a.position,b.position)<.0001f&&Quaternion.Angle(a.rotation,b.rotation)<.001f&&Mathf.Abs(a.distance-b.distance)<.0001f&&a.direction==b.direction&&Mathf.Abs(a.colour.r-b.colour.r)<.00001f&&Mathf.Abs(a.colour.g-b.colour.g)<.00001f&&Mathf.Abs(a.colour.b-b.colour.b)<.00001f;
        static void InspectPending()
        {
            if(session.BootTrafficPrepared==0)return;
            if(frame!=Time.frameCount){frame=Time.frameCount;actorFrames++;setupFrames.Add(frame);}
            if(session.Car||session.Input||session.Feedback||session.Network||session.Conditions||session.View||session.Racing||session.Pits||session.Downloads)throw new Exception("Partial actor stage published gameplay services");
            if(All<AudioSource>().Any(a=>a.isPlaying))throw new Exception("Audio played before Ready");
            if(All<VehicleController>().Any(c=>c.enabled||!c.Body.isKinematic)||All<TrafficCar>().Any(c=>c.enabled)||All<PoliceDriver>().Any(c=>c.enabled)||All<CarVisual>().Any(c=>c.enabled))throw new Exception("Partial actor callback or dynamic body is active");
            var world=session.World.GeographicWorld;if(!world||!world.BootPreparationHeld)throw new Exception("Actor placement query universe was not held");if(heldChunks<0)heldChunks=world.LoadedChunks;else if(heldChunks!=world.LoadedChunks)throw new Exception("Chunks changed during staged placement");
            foreach(var body in All<Rigidbody>()){if(frozen.TryGetValue(body,out var point)&&Vector3.Distance(point,body.position)>.0001f)throw new Exception("Frozen actor moved before Ready");frozen[body]=body.position;}
            foreach(var t in All<TrafficCar>().Where(t=>!t.Police))
            {int index=int.Parse(t.name.Substring("Traffic ".Length));var snap=Snap(t);if(regularGolden.TryGetValue(index,out var old)){if(!Same(old,snap))throw new Exception("Fixed-seed traffic changed across cancel/retry "+index);}else regularGolden[index]=snap;}
            foreach(var t in All<TrafficCar>().Where(t=>t.Police)){int i=int.Parse(t.name.Substring("County patrol ".Length));patrolDraws[i]=t.Distance;}
            if(!positiveChecked&&phase==0)
            {if(positiveFrame<0){positiveFrame=Time.frameCount;Time.timeScale=1;}else if(Time.frameCount>=positiveFrame+2){Time.timeScale=0;positiveChecked=true;Check(true,"disabled/kinematic pending actors survive actual positive-time physics frames");}}
        }
        static void ResetStage(){heldChunks=-1;frozen.Clear();setupFrames.Clear();actorFrames=0;frame=-1;patrolDraws.Clear();}
        static void CancelAtCheckpoint(string message)
        {Time.timeScale=0;profileHash=Hash(PlayerProfile.StoragePath);cancelFrame=Time.frameCount;session.CancelBoot();Check(session.BootFailed&&!session.BootReady,message);}
        static void CleanupThenRetry(int next)
        {
            if(Time.frameCount<=cancelFrame+2)return;
            Check(!session.Car&&!session.World&&All<VehicleController>().Length==0&&All<TrafficCar>().Length==0&&All<PoliceDriver>().Length==0&&All<GeoWorld>().Length==0,"cancel removes private player/fleet/patrols/world roots");
            Check(session.BootCleanupStatus==""&&Hash(PlayerProfile.StoragePath)==profileHash&&Hash(realPath)==realHash,"cancel preserves owned/real careers without cleanup warnings");Check(earlyMotorCommands==0,"never-activated cancelled setup sends no virtual haptics");
            previous=session;LocationLoading.Retry();ResetStage();phase=next;
        }
        static void VerifyReady()
        {
            var readyTrace=BootTelemetry.Capture().boots.Last(b=>b.status=="ready");var carSteps=readyTrace.events.Where(e=>e.operation=="main.actor.traffic.car").ToArray();Check(carSteps.Length==24&&carSteps.Select(e=>e.frame).Distinct().Count()==24,"actual actor creation yields a distinct game frame per regular car");
            var all=All<TrafficCar>();var regular=all.Where(t=>!t.Police).OrderBy(t=>int.Parse(t.name.Substring(8))).ToArray();var patrol=all.Where(t=>t.Police).ToArray();Check(all.Length==27&&regular.Length==24&&patrol.Length==3,"all24regulars and3patrols retained");
            var fleet=regular[0].Fleet;Check(fleet!=null&&fleet.Length==24&&fleet.Distinct().Count()==24&&regular.All(t=>ReferenceEquals(t.Fleet,fleet)&&t.enabled&&t.Visual.enabled&&t.GetComponent<Rigidbody>().isKinematic),"all regulars activate only with one complete shared fleet");
            var random=new System.Random(128);for(int i=0;i<24;i++){var expected=Color.HSVToRGB((float)random.NextDouble(),.38f,.65f);var got=regular[i].Visual.Body.sharedMaterial.color;Check(regular[i].Direction==(i%2==0?1:-1)&&Mathf.Abs(expected.r-got.r)<.00001f&&Mathf.Abs(expected.g-got.g)<.00001f&&Mathf.Abs(expected.b-got.b)<.00001f,"traffic ordinal/direction/fixed RNG color "+i);}
            var patrolRandom=new System.Random(PatrolSeed);for(int i=0;i<3;i++){bool found=false;for(int attempt=0;attempt<80;attempt++){float d=session.World.Route.Length*((i+(float)patrolRandom.NextDouble())/3);if(Mathf.Abs(d-patrolDraws[i])<.0001f){found=true;break;}}Check(found,"patrol consumes one persistent seeded draw stream within original80attempt budget "+i);}
            Check(patrol.All(t=>t.enabled&&t.Police&&t.ManagedPolice&&t.ManagedPolice.Marker==t&&t.ManagedPolice.enabled&&t.ManagedPolice.Car.enabled&&t.GetComponent<Rigidbody>().mass==1500&&!t.GetComponent<Rigidbody>().isKinematic),"original enabled marker+managed physics driver policy is preserved");
            // Exercise the ACTUAL private FixedUpdate against deliberately different
            // NPC state. ManagedPolice must bypass both NPC road/pursuit writers.
            var fixedUpdate=typeof(TrafficCar).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var t in patrol){float d=t.Distance,s=t.Speed;t.Distance=123.456f;t.Speed=17;var p=t.PhysicsPosition;var q=t.transform.rotation;fixedUpdate.Invoke(t,null);Check(t.Distance==123.456f&&t.Speed==17&&Vector3.Distance(t.PhysicsPosition,p)<.0001f&&Quaternion.Angle(t.transform.rotation,q)<.001f,"actual managed marker FixedUpdate bypasses NPC body/state writes");t.Distance=d;t.Speed=s;}
            Check(session.Car&&session.Input&&session.Feedback&&session.Network&&session.Conditions&&!session.World.GeographicWorld.BootPreparationHeld,"Ready publishes complete controls/services and releases world hold");Check(session.Economy.Credits==4321&&session.Profile.garage.cars.Count==2&&session.Profile.garage.Active.engine==2&&session.Car.Damage==11,"staged setup preserves career/tuning/damage");Check(WorldEpoch.Matches(WorldSnapshotCodec.Epoch(session.World.Route.Region),epoch),"actor staging leaves canonical world epoch unchanged");
            Check(earlyMotorCommands==0,"no virtual motor commands before Ready");int before=motorCommands;pad.SetMotorSpeeds(.1f,.1f);pad.ResetHaptics();Check(motorCommands>=before+2,"virtual haptic interception is live; zero count was not a missing probe");
            var trace=BootTelemetry.Capture().boots.Last(b=>b.status=="ready");var ops=trace.aggregates.ToDictionary(a=>a.operation);Check(trace.activeScopes==0&&ops["actors.stage.wall"].worst.kind=="coroutine-wall"&&!ops["actors.stage.wall"].worst.cpuAvailable,"actor total is wall-only and no calling-thread scope crosses yields");Check(ops["main.actor.traffic.car"].count==24&&ops["main.actor.patrol.prepare"].count==3&&ops["main.actor.commit"].count==1,"per-synchronous-slice telemetry retains all actor work");
            drivingPatrol=patrol[0].ManagedPolice;previousDriverEnabled=drivingPatrol.enabled;drivingPatrol.enabled=false;foreach(var t in all)if(t.ManagedPolice!=drivingPatrol)t.gameObject.SetActive(false);
            var point=session.World.Garage.position;var forward=Vector3.ProjectOnPlane(session.World.Garage.forward,Vector3.up).normalized;drivingPatrol.Car.Recover(point,Quaternion.LookRotation(forward,Vector3.up));drivingPatrol.Car.NetworkThrottle=.5f;drivingPatrol.Car.NetworkBrake=drivingPatrol.Car.NetworkHandbrake=drivingPatrol.Car.NetworkSteer=0;session.Car.StartLocked=true;session.Input.enabled=false;drivingInitial=drivingPatrol.Car.Body.position;physicsStarted=Time.realtimeSinceStartupAsDouble;physicsSimulationStarted=Time.time;physicsFixedStarted=Time.fixedTime;Time.timeScale=1;phase=5;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Actor staging timeout phase "+phase);if(!EditorApplication.isPlaying)return;if(pad==null)InstallVirtualPad();
                if(phase==1){CleanupThenRetry(2);return;}if(phase==3){CleanupThenRetry(4);return;}
                if(phase==5)
                {if(Time.realtimeSinceStartupAsDouble-physicsStarted<2)return;Time.timeScale=0;File.WriteAllText(Path.Combine(root,"patrol-drive-diagnostic.json"),JsonUtility.ToJson(new PatrolDriveDiagnostic{elapsedWall=Time.realtimeSinceStartupAsDouble-physicsStarted,simulationTime=Time.time-physicsSimulationStarted,fixedTime=Time.fixedTime-physicsFixedStarted,forwardDistance=Vector3.Dot(drivingPatrol.Car.Body.position-drivingInitial,Vector3.ProjectOnPlane(session.World.Garage.forward,Vector3.up).normalized),initial=drivingInitial,position=drivingPatrol.Car.Body.position,velocity=drivingPatrol.Car.Body.linearVelocity,grounded=drivingPatrol.Car.Grounded,enabled=drivingPatrol.Car.enabled,kinematic=drivingPatrol.Car.Body.isKinematic,playerPosition=session.Car.Body.position,throttle=drivingPatrol.Car.NetworkThrottle,brake=drivingPatrol.Car.NetworkBrake,handbrake=drivingPatrol.Car.NetworkHandbrake},true));Check(Vector3.Dot(drivingPatrol.Car.Body.position-drivingInitial,Vector3.ProjectOnPlane(session.World.Garage.forward,Vector3.up).normalized)>.2f&&drivingPatrol.Car.Grounded&&drivingPatrol.Car.enabled,"real managed VehicleController drives patrol physics while NPC marker path is bypassed");drivingPatrol.enabled=previousDriverEnabled;Check(session.Conditions.WeatherRequests==0&&session.Updates.SourceRequests==0&&Hash(realPath)==realHash&&errors==0,"no providers/runtime errors/real-career changes");Check(sawFive&&sawPatrol,"both allocated actor cancellation boundaries exercised");File.WriteAllText(Path.Combine(root,"result.txt"),"PASS "+checks+" actual actor freeze/cancel/retry/fleet/RNG/managed-patrol/haptics checks\n");Finish(0);return;}
                var found=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!found||ReferenceEquals(found,previous))return;session=found;if(session.BootFailed)throw new Exception(session.BootStatus);
                if(!session.BootReady)
                {InspectPending();if(phase==0&&session.BootTrafficPrepared>=5&&positiveChecked){sawFive=true;CancelAtCheckpoint("cancel after five allocated traffic cars");phase=1;}else if(phase==2&&session.BootPatrolPrepared>=1){sawPatrol=true;CancelAtCheckpoint("cancel after configured silent patrol");phase=3;}return;}
                if(LocationLoading.Busy)return;if(phase!=4)throw new Exception("Actor setup reached Ready before requested cancellation checkpoint");VerifyReady();
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        [Serializable]sealed class PatrolDriveDiagnostic{public double elapsedWall;public float simulationTime,fixedTime,forwardDistance,throttle,brake,handbrake;public Vector3 initial,position,velocity,playerPosition;public bool grounded,enabled,kinematic;}
        static void Finish(int code)
        {
            try{File.WriteAllText(Path.Combine(root,"boot-timing.json"),JsonUtility.ToJson(BootTelemetry.Capture(),true));if(Hash(realPath)!=realHash){Debug.LogError("Protected career changed");code=1;}}
            finally{SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;if(commandHook!=null)typeof(InputSystem).GetEvent("onDeviceCommand").RemoveEventHandler(null,commandHook);if(pad!=null)InputSystem.RemoveDevice(pad);GameSession.VerificationPatrolSeed=null;Time.timeScale=1;EditorApplication.Exit(code);}
        }
    }
}
