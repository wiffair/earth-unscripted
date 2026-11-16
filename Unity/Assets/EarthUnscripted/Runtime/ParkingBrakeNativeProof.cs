using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering;
namespace EarthUnscripted
{
 // Explicit isolated native proof. No replacement world, parking latch or physics mode.
 [DefaultExecutionOrder(500)]public sealed class ParkingBrakeNativeProof:MonoBehaviour
 {
  const string Flag="-earthUnscriptedParkingNative";static string prefix,initialError,realPath,realHash,sourceHash,cacheHash;static Config config;static bool requested;static int runtimeErrors;static readonly List<string> errors=new List<string>();
  [Serializable]public sealed class Config{public string source,sourceSHA256,cache,cacheSHA256,profileSHA256,region,road;public float x,z;}
  [Serializable]public sealed class Contact{public string collider,material,road;public int triangle;public Vector3 point,normal;public float grade,distance;}
  [Serializable]public sealed class Stage{public string name;public float wallSeconds,simulationSeconds,distance,initialRollback,creep,speed,damage;public int samples;public bool applied,holding,reverse;public Vector3 position;public Contact[] contacts;}
  [Serializable]public sealed class Report{public string status,error,version,source,sourceSHA256,cacheSHA256,epochBefore,epochAfter,scope;public bool realCareerUnchanged,sourceUnchanged,cacheUnchanged,physicsAutomatic,profileIsolated,hudEnabled;public int runtimeErrors,width,height,criticalSamples,criticalFailures,contactSamples,contactFailures,impacts;public string[] screenshots,checks,firstErrors;public Stage[] stages;}
  readonly List<string> checks=new List<string>(),screenshots=new List<string>();readonly List<Stage> stages=new List<Stage>();GameSession session;bool done,observing;string phase="boot";double deadline;int criticalSamples,criticalFailures,contactSamples,contactFailures,impacts;string epochBefore;
  static string Hash(string path)=>File.Exists(path)?Core.DataPackageRules.HashFile(path):"missing";
  static void Log(string message,string stack,LogType type){if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;runtimeErrors++;if(errors.Count<16)errors.Add((message??"").Substring(0,Math.Min(message?.Length??0,1200)));}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]static void Isolate()
  {
   var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,Flag);if(at<0)return;requested=true;Application.runInBackground=true;Application.logMessageReceived+=Log;
   // Even malformed arguments must never reach the real career.
   string safe=Path.Combine(Application.temporaryCachePath,"invalid-parking-proof-"+Guid.NewGuid().ToString("N"));PlayerProfile.StorageOverride=safe+"-profile.json";RegionCache.OverrideFolder=safe+"-regions";
   try
   {
    if(at+2>=args.Length)throw new ArgumentException("Parking proof requires prefix and config path");prefix=Path.GetFullPath(args[at+1]);realPath=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=Hash(realPath);string profile=prefix+"-profile.json";
    if(string.Equals(Path.GetFullPath(profile),Path.GetFullPath(realPath),StringComparison.OrdinalIgnoreCase))throw new IOException("Proof career must be isolated");if(File.Exists(prefix+"-result.json"))throw new IOException("Fresh proof output prefix required");
    config=JsonUtility.FromJson<Config>(File.ReadAllText(Path.GetFullPath(args[at+2])));if(config==null||Hash(config.source)!=config.sourceSHA256||Hash(config.cache)!=config.cacheSHA256||Hash(profile)!=config.profileSHA256)throw new IOException("Pinned parking fixture hash mismatch");
    if(!config.region.StartsWith("download-",StringComparison.Ordinal)||!string.Equals(Path.GetFullPath(config.cache),Path.Combine(Path.GetFullPath(prefix+"-regions"),config.region+".json"),StringComparison.Ordinal))throw new IOException("Cache is not the owned proof region");
    sourceHash=Hash(config.source);cacheHash=Hash(config.cache);PlayerProfile.StorageOverride=profile;RegionCache.OverrideFolder=prefix+"-regions";GameSession.AutoStartCircuit=false;
   }
   catch(Exception e){initialError=e.ToString();PlayerProfile.StorageOverride=safe+"-profile.json";RegionCache.OverrideFolder=safe+"-regions";if(prefix==null)prefix=safe;}
  }
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Install(){if(!requested)return;var root=new GameObject("Isolated native parking proof");DontDestroyOnLoad(root);root.AddComponent<ParkingBrakeNativeProof>();}
  void Start(){deadline=Time.realtimeSinceStartupAsDouble+360;StartCoroutine(Guard(Run()));}
  void Update(){if(!done&&Time.realtimeSinceStartupAsDouble>deadline)Finish(false,"Native parking watchdog expired");}
  void OnDestroy(){Application.logMessageReceived-=Log;}
  IEnumerator Guard(IEnumerator work)
  {
   while(!done){bool more=false;object next=null;Exception error=null;try{more=work.MoveNext();if(more)next=work.Current;}catch(Exception e){error=e;}if(error!=null){Finish(false,error.ToString());yield break;}if(!more)yield break;if(next is IEnumerator nested)yield return Guard(nested);else yield return next;}
  }
  void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);checks.Add(why);Debug.Log("PARKING NATIVE PASS "+why);}
  Contact[] Contacts()
  {
   var result=new Contact[4];var car=session.Car;for(int i=0;i<4;i++)
   {
    if(!Physics.Raycast(car.transform.TransformPoint(VehicleGeometry.Anchor(car.Metrics.Model,i)),-car.transform.up,out var hit,Core.VehicleMetric.RayLength,~(1<<8),QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Missing actual wheel contact "+i);
    var renderer=hit.collider.GetComponent<MeshRenderer>();var road=session.World.Route.Region.Nearest(hit.point,out _,out _);float grade=Vector3.Angle(hit.normal,Vector3.up);
    if(!(hit.collider is MeshCollider)||!renderer||renderer.sharedMaterial!=session.World.GeographicWorld.Asphalt||road==null||road.id!=config.road||grade<8||grade>15)throw new InvalidOperationException("Unqualified actual asphalt hill contact "+i+" "+hit.collider.name+" grade="+grade+" road="+road?.id);
    result[i]=new Contact{collider=hit.collider.name,material=renderer.sharedMaterial.name,road=road.id,triangle=hit.triangleIndex,point=hit.point,normal=hit.normal,grade=grade,distance=hit.distance};
   }
   return result;
  }
  void FixedUpdate()
  {
   if(!observing||done||!session||!session.BootReady||Time.timeScale==0)return;var p=session.Car.Body.position;
   for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){criticalSamples++;if(!session.World.GeographicWorld.HasSurface(p+new Vector3(x*320,0,z*320)))criticalFailures++;}
   contactSamples++;try{Contacts();}catch(Exception e){contactFailures++;if(errors.Count<16)errors.Add(phase+": "+e.Message);}
  }
  void Inputs(float gas=0,float brake=0,float hand=0){session.Input.Throttle=gas;session.Input.Brake=brake;session.Input.Handbrake=hand;session.Input.Steer=0;}
  Stage Record(string name,double wall,float simulation,Vector3 initial)
  {var car=session.Car;var stage=new Stage{name=name,wallSeconds=(float)(Time.realtimeSinceStartupAsDouble-wall),simulationSeconds=Time.time-simulation,distance=Vector3.Distance(initial,car.Body.position),speed=car.AbsoluteSpeed,damage=car.Damage,applied=car.ParkingBrakeApplied,holding=car.ParkingBrakeHolding,reverse=car.Reverse,position=car.Body.position,contacts=Contacts()};stages.Add(stage);return stage;}
  IEnumerator Capture(string label)
  {
   for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();Require(!session.Menu&&session.GetComponent<DrivingUI>()&&session.GetComponent<DrivingUI>().enabled,"normal gameplay HUD enabled for "+label);
   var image=ScreenCapture.CaptureScreenshotAsTexture();try{string path=prefix+"-"+label+".png";File.WriteAllBytes(path,image.EncodeToPNG());screenshots.Add(path);Require(image.width>=1280&&image.height>=720&&new FileInfo(path).Length>20000,"actual rendered HUD screenshot saved "+label);}finally{Destroy(image);}
  }
  IEnumerator Hold(string label)
  {
   phase=label;Inputs();double start=Time.realtimeSinceStartupAsDouble;float simulation=Time.time;var initial=session.Car.Body.position;
   while(Time.time-simulation<2){RequireProgress(start,10);yield return new WaitForEndOfFrame();}
   float rollback=Vector3.Distance(initial,session.Car.Body.position);Require(session.Car.ParkingBrakeApplied&&session.Car.ParkingBrakeHolding&&rollback<.15f,"automatic zero-pedal stop settles within15cm "+label);
   var settled=session.Car.Body.position;double hold=Time.realtimeSinceStartupAsDouble;float holdSimulation=Time.time;int frames=0;
   while(Time.time-holdSimulation<10){RequireProgress(hold,25);if(!session.Car.ParkingBrakeApplied||!session.Car.ParkingBrakeHolding)throw new InvalidOperationException("Parking hold lost "+label);frames++;yield return new WaitForEndOfFrame();}
   var record=Record(label,hold,holdSimulation,settled);record.initialRollback=rollback;record.creep=record.distance;record.samples=frames;Require(record.creep<.05f&&record.speed<.04f&&frames>100,"actual ten-simulation-second real-road hold "+label);yield return Capture(label+"-holding");
  }
  void RequireProgress(double start,double limit){if(Time.realtimeSinceStartupAsDouble-start>limit)throw new TimeoutException("Native simulation did not progress in "+phase);}
  IEnumerator Drive(bool reverse)
  {
   phase=reverse?"reverse":"forward";var car=session.Car;var initial=car.Body.position;var forward=Vector3.ProjectOnPlane(car.transform.forward,Vector3.up).normalized;double began=Time.realtimeSinceStartupAsDouble;float simulation=Time.time;
   Inputs(reverse?0:.4f,reverse?1:0);if(reverse){while(Time.time-simulation<.2f){RequireProgress(began,5);yield return new WaitForEndOfFrame();}Require(!car.Reverse&&car.ParkingBrakeApplied,"intentional brake-to-reverse retains deliberate delay while parked");}
   while((reverse?-1:1)*Vector3.Dot(car.Body.position-initial,forward)<2){RequireProgress(began,10);if(Vector3.Distance(initial,car.Body.position)>8)throw new InvalidOperationException("Drive left verified bounded hill corridor");yield return new WaitForEndOfFrame();}
   Require(!car.ParkingBrakeApplied&&car.Reverse==reverse&&(reverse?-car.Speed:car.Speed)>.15f,"actual "+phase+" drive automatically releases parking");Record(phase,began,simulation,initial);yield return Capture(phase+"-released");
  }
  IEnumerator Stop()
  {
   phase="controlled-stop";double began=Time.realtimeSinceStartupAsDouble;var car=session.Car;
   while(car.AbsoluteSpeed>.1f){Inputs(car.Reverse?1:0,car.Reverse?0:1,1);RequireProgress(began,8);yield return new WaitForEndOfFrame();}Inputs();
  }
  IEnumerator Run()
  {
   Require(initialError==null,"early isolated fixture setup succeeded: "+initialError);Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"native graphics device active");
   while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady||LocationLoading.Busy){if(session&&session.BootFailed)throw new InvalidOperationException(session.BootStatus);yield return null;}
   Require(PlayerProfile.StoragePath==prefix+"-profile.json"&&session.World.Route.Region.id==config.region,"actual production GameSession uses isolated pinned region/career");Require(Physics.simulationMode==SimulationMode.FixedUpdate,"production automatic physics remains mode0");Require(session.Car.ParkingAutomaticEnabled&&session.Car.Visual.ImportedAppearance,"production imported player has automatic parking enabled");
   session.Updates.Enabled=session.Updates.Automatic=false;session.Input.enabled=false;session.Car.NetworkDriven=false;Inputs();foreach(var car in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))car.gameObject.SetActive(false);session.View.Mode=0;Application.targetFrameRate=60;
   epochBefore=WorldSnapshotCodec.Epoch(session.World.Route.Region);Require(Contacts().Length==4,"four actual asphalt wheel contacts qualify8–15degree real hill before claim");session.Car.Crashed+=(_,__)=>impacts++;
   session.ResumeDrive();while(session.PreparingStart)yield return new WaitForEndOfFrame();Require(!session.Menu&&session.World.GeographicWorld.DriveFrontierReady,"normal ResumeDrive completes physical frontier");observing=true;
   yield return Hold("boot-automatic");yield return Drive(false);yield return Stop();yield return Hold("real-stop-automatic");yield return Drive(true);yield return Stop();
   Require(criticalSamples>0&&criticalFailures==0&&contactSamples>0&&contactFailures==0,"all observed physical coverage and real asphalt contacts remain valid");Require(impacts==0&&session.Car.Damage==0,"bounded hill hold/drive incurs no collision or damage");Require(session.Conditions.WeatherRequests==0&&session.Updates.SourceRequests==0,"no weather or map provider requests");Require(WorldSnapshotCodec.Epoch(session.World.Route.Region)==epochBefore,"canonical world/width/rail epoch unchanged");Finish(true,null);
  }
  void Finish(bool pass,string error)
  {
   if(done)return;done=true;observing=false;bool real=realPath!=null&&Hash(realPath)==realHash,source=config!=null&&Hash(config.source)==sourceHash,cache=config!=null&&Hash(config.cache)==cacheHash;if(!real||!source||!cache||runtimeErrors!=0)pass=false;
   var report=new Report{status=pass?"PASS":"FAIL",error=error,version=Application.version,source=config?.source,sourceSHA256=sourceHash,cacheSHA256=cacheHash,epochBefore=epochBefore,epochAfter=session&&session.World?.Route?.Region!=null?WorldSnapshotCodec.Epoch(session.World.Route.Region):null,realCareerUnchanged=real,sourceUnchanged=source,cacheUnchanged=cache,physicsAutomatic=Physics.simulationMode==SimulationMode.FixedUpdate,profileIsolated=PlayerProfile.StoragePath==prefix+"-profile.json",hudEnabled=session&&!session.Menu&&session.GetComponent<DrivingUI>()&&session.GetComponent<DrivingUI>().enabled,runtimeErrors=runtimeErrors,width=Screen.width,height=Screen.height,criticalSamples=criticalSamples,criticalFailures=criticalFailures,contactSamples=contactSamples,contactFailures=contactFailures,impacts=impacts,screenshots=screenshots.ToArray(),checks=checks.ToArray(),firstErrors=errors.ToArray(),stages=stages.ToArray(),scope="Actual production GameSession/native automatic physics on four verified8–15degree asphalt contacts. Automatic parking at boot and after real forward drive/controlled stop, ten-second zero-pedal holds, forward/reverse release and normal HUD PNGs. No pose/latch/physics-mode substitution; no slippery/impact/LAN/Windows or performance claim."};
   Directory.CreateDirectory(Path.GetDirectoryName(prefix));File.WriteAllText(prefix+"-result.json",JsonUtility.ToJson(report,true));Debug.Log("PARKING NATIVE "+report.status+" "+error);Application.Quit(pass?0:1);
  }
 }
}
