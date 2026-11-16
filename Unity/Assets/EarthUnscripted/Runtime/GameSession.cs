using UnityEngine;
using UnityEngine.Rendering.Universal;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    public sealed partial class GameSession : MonoBehaviour
    {
        public PlayerProfile Profile { get; private set; }
        public DriveEconomy Economy { get; private set; }
        public DriftSkills Drifts { get; private set; }
        public CheckpointRun Run { get; private set; }
        public WorldBuilder World { get; private set; }
        public DriverInput Input { get; private set; }
        public VehicleController Car { get; private set; }
        public FeedbackDriver Feedback { get; private set; }
        public DriveCamera View { get; private set; }
        public bool Menu { get; private set; }=true;
        public bool Started { get; private set; }
        public float Progress { get; private set; }
        public float Lateral { get; private set; }
        public int Weather;
        public EnvironmentController Conditions { get; private set; }
        public LanSession Network { get; private set; }
        public CircuitSession Racing {get;private set;}
        public PitService Pits {get;private set;}
        public GlobalRegions Downloads {get;private set;}
        public CountryUpdateService Updates {get;private set;}
        public static bool AutoStartCircuit;public static bool SuppressAutomaticLaunch;
        static string resumeUpdatedArea;
        public bool PreparingStart {get;private set;}
        public bool PreparingRoadFrontier {get;private set;}
        public string PreparingStartStatus {get;private set;}="";
        int startPreparationGeneration;
        public GeoRoad CurrentRoad { get; private set; }
        public string Notice { get; private set; }="Welcome to Earth Unscripted";
        bool markingFallbackNotified;
        public float Wanted { get; private set; }
        public string LastResult { get; private set; }="";
        public bool OnRoad { get; private set; }
        public int Limit=>CurrentRoad!=null?CurrentRoad.limit:World?.Route?.Limit(Progress)??0;
        TrafficCar police;
        readonly System.Collections.Generic.List<PoliceDriver> patrols=new System.Collections.Generic.List<PoliceDriver>();
        DrivingAudio audioDriver;
        Transform gate;
        ParticleSystem rain,snow;Material rainMaterial,snowMaterial;
        float saveAt,noticeUntil,offenceTimer,captureTimer,escapeTimer,previousGateDistance;
        Vector3 lastPosition;
        int nearestCheckpoint;
        public bool AtGarage=>BootReady&&Car&&World&&World.Garage&&Vector3.Distance(Car.Body.position,World.Garage.position)<20;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterScenes()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded-=SceneReady;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded+=SceneReady;
        }
        static void SceneReady(UnityEngine.SceneManagement.Scene scene,UnityEngine.SceneManagement.LoadSceneMode mode)=>Launch();
        
        static void Launch()
        {
            if(SuppressAutomaticLaunch||System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-earthUnscriptedSingleSeatPhysicalProof")>=0)return;
            // Explicit geometry-only native diagnostics own their scene and never
            // load a career or begin the normal world-preparation pipeline.
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-earthUnscriptedRoadGeometryProof")>=0)return;
            if(FindAnyObjectByType<GameSession>())return;
            new GameObject(ProductIdentity.Name).AddComponent<GameSession>();
        }
        public EarthUnscripted.Updates.ApplicationUpdateService AppUpdates { get; private set; }
        void Awake()
        {
            if(SinglePlayerBeta.Enabled && SinglePlayerBeta.StartupError!=null){enabled=false;return;}
            // Explicit standalone capture must keep preparing before BuildCapture
            // is attached after actors. Its later background setting is too late
            // when an unfocused Player pauses during asynchronous boot. Ordinary
            // launches retain the production foreground/background policy.
            var captureArguments = System.Environment.GetCommandLineArgs();
            int captureArgument = System.Array.IndexOf(captureArguments, "-earthUnscriptedCapture");
            if (captureArgument >= 0 && captureArgument + 1 < captureArguments.Length &&
                !string.IsNullOrWhiteSpace(captureArguments[captureArgument + 1]))
            {
                Application.runInBackground = true;
            }
            // Apply the SAME no-VSync policy used by ApplyQuality before the
            // first loading-camera frame, rather than only after Town/actors.
            // Keep the existing target120 and100Hz physics setup below unchanged.
            QualitySettings.vSyncCount=0;
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-earthUnscriptedSilent")>=0){AudioListener.volume=0;AudioListener.pause=true;}
            EnsureBootClearingCamera();
            try { ValidateScopedIsolationBeforeProfileLoad800(); } catch (System.Exception error) { FailBoot(error); return; }
            bootTiming=BootTelemetry.Begin("profile-pending");using var bootAwake=BootTelemetry.Enter(bootTiming,"main.awake");
            using(BootTelemetry.Measure("main.profile.load"))Profile=PlayerProfile.Load();if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-earthUnscriptedSwitchProof")>=0&&!FindAnyObjectByType<StandaloneFeedbackProof>())new GameObject("Standalone switch verification").AddComponent<StandaloneFeedbackProof>();Economy=new DriveEconomy(Profile.credits);Drifts=new DriftSkills();Run=new CheckpointRun(8);
            if(Profile.LoadBlocked){FailBoot(new System.InvalidOperationException(Profile.LoadIssue));return;}
            if(SinglePlayerBeta.Enabled){AppUpdates=gameObject.AddComponent<EarthUnscripted.Updates.ApplicationUpdateService>();AppUpdates.Initialize(this);}
            Application.targetFrameRate=120;Time.fixedDeltaTime=1f/100;
            var args=System.Environment.GetCommandLineArgs();string regionId=System.Array.IndexOf(args,"-earthUnscriptedDee")>=0?"dee-valley":Profile.region;
            int trackArg=System.Array.IndexOf(args,"-earthUnscriptedCircuit");if(trackArg>=0&&trackArg+1<args.Length&&(args[trackArg+1]=="silverstone"||args[trackArg+1]=="spa"))regionId=args[trackArg+1];BeginBoot(regionId,args);
        }
        // Synchronous compatibility/oracle wrapper drains the SAME ordered policy.
        // Normal asynchronous boot consumes PrepareActorStages directly.
        void CompleteSceneSetup(string regionId,string[] args)
        {var work=PrepareActorStages(regionId,args,bootGeneration);try{while(work.MoveNext()){}}finally{(work as System.IDisposable)?.Dispose();}}

        void SpawnTraffic(){foreach(var step in SpawnTrafficStages(Car,null)){} }

        TrafficCar MakeTraffic(string name,float distance,int dir,Color colour)=>MakeBootTraffic(name,distance,dir,colour,Car,null);
        void BuildGate()
        {
            gate=OwnBootRoot(new GameObject("Next checkpoint")).transform;var cyan=Geometry.Material("Checkpoint cyan",new Color(.12f,.75f,.75f));cyan.EnableKeyword("_EMISSION");cyan.SetColor("_EmissionColor",new Color(.1f,1,1)*2);
            foreach(int s in new[]{-1,1})Geometry.Box("Checkpoint post",gate,new Vector3(s*5.7f,2.8f,0),new Vector3(.2f,5.6f,.2f),cyan);
            Geometry.Box("Checkpoint header",gate,new Vector3(0,5.5f,0),new Vector3(11.6f,.22f,.22f),cyan);gate.gameObject.SetActive(false);
        }
        void BuildWeather()
        {
            rain=OwnBootRoot(new GameObject("Local rain")).AddComponent<ParticleSystem>();var main=rain.main;main.startLifetime=1.5f;main.startSpeed=25;main.startSize=.035f;main.maxParticles=1800;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startColor=new Color(.7f,.8f,.9f,.5f);
            var shape=rain.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(35,1,35);
            var emission=rain.emission;emission.rateOverTime=700;rain.transform.rotation=Quaternion.Euler(90,0,0);
            var renderer=rain.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=3;
            rainMaterial=ParticleAppearance.Create("Soft rain",Color.white);renderer.sharedMaterial=rainMaterial;
            rain.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            snow=OwnBootRoot(new GameObject("Local falling snow")).AddComponent<ParticleSystem>();
            var flakes=snow.main;flakes.startLifetime=10;flakes.startSpeed=2.2f;flakes.startSize=new ParticleSystem.MinMaxCurve(.035f,.09f);flakes.maxParticles=2200;flakes.simulationSpace=ParticleSystemSimulationSpace.World;flakes.startColor=new Color(.9f,.94f,1,.8f);
            var snowShape=snow.shape;snowShape.shapeType=ParticleSystemShapeType.Box;snowShape.scale=new Vector3(45,1,45);snow.transform.rotation=Quaternion.Euler(90,0,0);
            var drift=snow.noise;drift.enabled=true;drift.strength=.45f;drift.frequency=.12f;
            var snowRenderer=snow.GetComponent<ParticleSystemRenderer>();snowRenderer.renderMode=ParticleSystemRenderMode.Billboard;snowMaterial=ParticleAppearance.Create("Soft snowflakes",Color.white);snowRenderer.sharedMaterial=snowMaterial;
            snow.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        // Actual tyre contacts identify the road's physical level independently
        // of the guided route. Only grounded contacts are current observations;
        // airborne fallback uses this real vehicle's loaded suspension geometry.
        float VehicleRoadSupportHeight()
        {
            // Replica wheel visuals are networked; suspension contacts are not refreshed.
            // Skip those stale arrays and use the existing loaded-rest fallback below.
            float total=0;int count=0;
            for(int wheel=0;!Car.Replica&&wheel<Car.WheelGrounded.Length;wheel++)
            {
                if(!Car.WheelGrounded[wheel])continue;
                float height=Car.ContactPoints[wheel].y;
                if(float.IsNaN(height)||float.IsInfinity(height))continue;
                total+=height;count++;
            }
            return count>0?total/count:Car.Body.position.y-Car.Metrics.RestRootHeight(Car.Body.mass,Car.SpringFactor);
        }

        void Update()
        {
            using var frameWork=WorkTelemetry.Measure("frame.game-session",null,8);
            if (!BootReady)
            {
                bootMenus.Update(!LocationLoading.Busy, () => { if (!BootFailed) CancelBoot(); });
                return;
            }
            Input.ParkingInputAllowed=!Menu&&!PreparingStart&&!Input.Calibrating;
            if(Input.PausePressed&&!Input.Calibrating&&!PreparingStart){if(Menu)ResumeDrive();else SetMenu(true);}
            if(Menu){Feedback.Driving=false;return;}
            Feedback.Driving=true;
            Input.ParkingInputAllowed=!PreparingStart&&!Input.Calibrating;
            if(Input.ParkingInputAllowed&&Input.ParkingBrakePressed)Input.QueueParkingToggle(Car.ParkingBrakeApplied,Car.ParkingBrakeCommandAck);
            if(!markingFallbackNotified&&World.GeographicWorld&&World.GeographicWorld.SimplifiedRoadMarkings){markingFallbackNotified=true;Notify("Road markings are simplified in this area");}
            if(Input.ResetPressed)Recover();if(Input.RepairPressed)Repair();if(Input.ChallengePressed)StartRun();
            Progress=World.Route.Closest(Car.Body.position,out float lateral,out float separation);Lateral=lateral;
            if(World.Route.Region!=null)
            {
                if(World.Route.Region.circuit)
                {
                    // Keep circuit/pit classification behavior unchanged here.
                    var projection=World.Route.Region.ProjectRoad(Car.Body.position);
                    CurrentRoad=projection.Road;OnRoad=World.Route.Region.CircuitRoadSurfaces!=null
                        ?World.Route.Region.CircuitRoadSurfaces.TryHeight(Car.Body.position.x,Car.Body.position.z,out _)
                        :CurrentRoad!=null&&projection.EdgeDistance<0;
                }
                else
                {
                    var projection=World.Route.Region.ProjectRoadForVehicle(Car.Body.position,VehicleRoadSupportHeight());
                    CurrentRoad=projection.HasPlausibleHeight?projection.Road:null;OnRoad=projection.OnRoad;
                }
            }
            else{CurrentRoad=null;OnRoad=separation<World.Route.Width(Progress)*.5f;}
            var surfaceWeather=Conditions.ApplySurface(Car);Car.Grip=(OnRoad?1:Car.OffroadGripFactor)*WeatherSurfaceRules.Grip(surfaceWeather,Car.Metrics.Model==3)*Car.TyreFactor;Car.RoadSurface=OnRoad;
            var localVelocity=Car.transform.InverseTransformDirection(Car.Body.linearVelocity);
            float driftAngle=Mathf.Atan2(localVelocity.x,Mathf.Max(.01f,localVelocity.z))*Mathf.Rad2Deg;
            int driftCredits=Drifts.Tick(Time.deltaTime,Car.Speed,driftAngle,Car.Body.position.x,Car.Body.position.z,Car.Grounded,Wanted<=0&&!Network.Active);
            if(driftCredits>0){Economy.AddCredits(driftCredits);Notify(Drifts.Feedback);}
            float dt=Time.deltaTime;
            if(!Network.Active&&!World.IsCircuit)Economy.Tick(dt,Car.Speed,OnRoad,Car.AbsoluteSpeed*DrivingMath.MphPerMetreSecond<=Limit+3,Wanted>0,Car.Grounded);
            Run.Tick(dt);UpdateRun();UpdatePolice(dt);UpdateWeather();
            var region=World.Route.Region;
            RememberAutomaticSafeRoad();
            if(region!=null&&!region.AutomaticWorld&&(Car.Body.position.x<region.minX+12||Car.Body.position.x>region.minX+region.width-12||Car.Body.position.z<region.minZ+12||Car.Body.position.z>region.minZ+region.depth-12))
            {Run.Cancel();gate.gameObject.SetActive(false);Drifts.Cancel();Car.Recover(World.Route.At(35,World.Route.LaneOffset(35)),World.Route.Rotation(35));World.GeographicWorld.Refresh(Car.Body.position,true);Racing?.Recovery();Notify("End of bundled map coverage · returned to a safe road");}
            if(region?.AutomaticWorld==true)
            {if(!Network.Active && !PreparingStart && hasAutomaticSafeRoad && Car.Body.position.y<automaticSafeRoad.y-30 &&
                new Vector2(Car.Body.position.x-automaticSafeRoad.x,Car.Body.position.z-automaticSafeRoad.z).sqrMagnitude<300*300)Recover();}
            else if(Car.transform.position.y<-20){Recover();}
            if(Time.unscaledTime>saveAt){Save();saveAt=Time.unscaledTime+15;}
            if(Time.time>noticeUntil)Notice="";
            lastPosition=Car.transform.position;
        }
        public void SetMenu(bool value)
        {if(!BootReady)return;
            if (!value && RefuseScopedDriving800()) return;
            Menu=value;GetComponent<Radio.CarRadio>()?.SynchronizeMenuPause();Time.timeScale=value?0:1;Feedback.Driving=!value;if(value){Feedback.StopAll();Save();}else Started=true;
        }
        public void Notify(string message){Notice=message;noticeUntil=Time.time+5;}
        void OnCrash(float severity,Vector3 point)
        {
            if(severity<2)return;Racing?.Incident();
            if(World.IsCircuit){Drifts.Cancel();Economy.Incident(0);Feedback.Impact(severity);audioDriver.Crash(severity);Notify(Racing&&Racing.Running&&Racing.Lap!=null&&Racing.Lap.Armed&&!Racing.Lap.Valid?"Track contact · lap invalid · damage retained":"Track contact · damage retained");return;}
            if(Car.LastImpactWasPolice){Drifts.Cancel();Economy.Incident(0);Run.Collision();Feedback.Impact(severity);audioDriver.Crash(severity);Notify("Patrol contact · recover the slide");return;}
            Drifts.Cancel();Economy.Incident(0);Run.Collision();Feedback.Impact(severity);audioDriver.Crash(severity);
            Notify("Collision · streak lost · repair is free outside a challenge");
            if(severity>6)AlertPolice();
        }
        public const float PursuitDurationSeconds=60;
        public void ReportSpeeding(PoliceDriver observer)
        {
            if(Network.Active||Wanted>0||!observer||!observer.CanObserveSpeeding())return;
            police=observer.Marker;observer.BeginPursuit();Wanted=PursuitDurationSeconds;escapeTimer=captureTimer=0;Notify("Roadside patrol observed speeding · pursuit");
        }
        void AlertPolice()
        {
            if(Network.Active||Wanted>0)return;
            PoliceDriver nearest=null;float distance=140;
            foreach(var p in patrols){if(!p.gameObject.activeInHierarchy)continue;float d=Vector3.Distance(p.Car.Body.position,Car.Body.position);if(d<distance){distance=d;nearest=p;}}
            if(!nearest)return;police=nearest.Marker;nearest.BeginPursuit();Wanted=PursuitDurationSeconds;escapeTimer=captureTimer=0;
        }
        void UpdatePolice(float dt)
        {
            audioDriver.Siren=false;if(Network.Active)return;
            if(Wanted<=0)return;
            Wanted=Mathf.Max(0,Wanted-dt);bool expired=Wanted<=0;float separation=float.MaxValue;
            foreach(var patrol in patrols)if(patrol.Pursuing)separation=Mathf.Min(separation,Vector3.Distance(patrol.Car.Body.position,Car.Body.position));
            if(separation<9&&Car.AbsoluteSpeed<5)captureTimer+=dt;else captureTimer=0;
            if(separation>140)escapeTimer+=dt;else escapeTimer=0;
            if(captureTimer>2){Economy.Incident(200);Wanted=0;Notify("Stopped by patrol · 200 credit fine");}
            if(expired)Notify(separation>140?"Patrol lost contact":"Pursuit ended");
            if(Wanted<=0){foreach(var patrol in patrols)patrol.Disengage();Wanted=0;}
        }
        void UpdateWeather()
        {
            float snowAmount=Conditions.SurfaceAt(Car.Body.position).Snowfall;snow.transform.position=Car.transform.position+Vector3.up*18;
            var snowEmission=snow.emission;snowEmission.rateOverTime=snowAmount*180;
            if(snowAmount>.03f&&!snow.isPlaying)snow.Play();else if(snowAmount<=.03f&&snow.isPlaying)snow.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            float rainAmount=Conditions.RainAt(Car.Body.position);rain.transform.position=Car.transform.position+Vector3.up*18;
            var emission=rain.emission;emission.rateOverTime=rainAmount*1100;
            if(rainAmount>.03f&&!rain.isPlaying)rain.Play();else if(rainAmount<=.03f&&rain.isPlaying)rain.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        System.Collections.IEnumerator Start()
        {
            while(!BootReady&&!BootFailed)yield return null;if(!BootReady)yield break;
            if(string.IsNullOrEmpty(resumeUpdatedArea)||resumeUpdatedArea!=World.Route.Region?.id)yield break;
            resumeUpdatedArea=null;while(LocationLoading.Busy)yield return null;FinishResumeDrive();
        }
        public void ResumeDrive()
        {
            if(Racing&&!Racing.RetryPendingLap())return;if(!BootReady)return;
            if(IsRenting&&World.IsCircuit&&(!Racing.Running||Racing.Finished))
            {Racing.Begin(Profile.trackMode);if(!Racing.Running)return;}
            if (RefuseScopedDriving800()) return;
            if(PreparingStart)return;
            if(Started||World.IsCircuit||Network.Active||!Updates.Enabled){FinishResumeDrive();return;}
            PreparingStart=true;StartCoroutine(PrepareFirstDrive(++startPreparationGeneration));
        }
        System.Collections.IEnumerator PrepareFirstDrive(int request)
        {
            PreparingStart=true;PreparingRoadFrontier=false;PreparingStartStatus="Checking your starting area…";GeoRegion updated=null;
            yield return StartAreaPreflight.Prepare(this,r=>updated=r);
            if(request!=startPreparationGeneration)yield break;
            if(updated!=null)
            {
                // Both snapshots use the same geographic origin. Preserve the
                // selected start while moving to a new immutable map version.
                if(ChangeLocation(updated.id,Car.Body.position,true)){PreparingStart=false;yield break;}
            }
            yield return PrepareFrontierAndResume(request);
        }
        void FinishResumeDrive()
        {
            if(World.IsCircuit||Network.Active||!World.GeographicWorld){EnterDrive();return;}
            PreparingStart=true;StartCoroutine(PrepareFrontierAndResume(++startPreparationGeneration));
        }
        System.Collections.IEnumerator PrepareFrontierAndResume(int request)
        {
            if(World.IsCircuit||Network.Active||!World.GeographicWorld){PreparingStart=false;EnterDrive();yield break;}
            PreparingStart=true;PreparingRoadFrontier=true;PreparingStartStatus="Preparing nearby roads…";
            var geography=World.GeographicWorld;
            yield return geography.PrepareDriveFrontier(Car.Body.position,(ready,total)=>{if(request==startPreparationGeneration)PreparingStartStatus=$"Preparing nearby roads {ready}/{total}";},()=>request!=startPreparationGeneration||!Menu||Network.Active);
            if(request!=startPreparationGeneration)yield break;PreparingStart=false;PreparingRoadFrontier=false;
            if(!geography||!geography.DriveFrontierReady){PreparingStartStatus=geography?geography.DriveFrontierStatus:"Road preparation interrupted";Notify(PreparingStartStatus);yield break;}
            PreparingStartStatus="";EnterDrive();
        }
        public void CancelPreparingStart()
        {
            if(!PreparingStart)return;startPreparationGeneration++;World.GeographicWorld?.CancelDriveFrontier();PreparingStart=false;PreparingRoadFrontier=false;PreparingStartStatus="Preparation cancelled · still paused";Notify(PreparingStartStatus);
        }
        void EnterDrive(){SetMenu(false);if(World.IsCircuit&&!Racing.Running&&!Racing.Finished)Racing.Begin(Profile.trackMode);}
        public void SelectCircuit(string id,int mode,int laps,int difficulty)
        {if(!BootReady)return;
            if(Profile.garage.Active.model>=ImportedVehicleCatalogue.BuiltInCount){Notify("Select an owned built-in car before entering a circuit; imported-car race records are not supported yet.");return;}
            if (RefuseScopedDriving800()) return;
            if(Network.Active){Notify("Leave the LAN session before selecting a race mode");return;}
            if(Racing&&!Racing.RetryPendingLap())return;
            string oldRegion=Profile.region;int oldMode=Profile.trackMode,oldLaps=Profile.raceLaps,oldDifficulty=Profile.raceDifficulty;
            Profile.region=id;Profile.trackMode=mode;Profile.raceLaps=laps;Profile.raceDifficulty=difficulty;
            if(!TrySave()){Profile.region=oldRegion;Profile.trackMode=oldMode;Profile.raceLaps=oldLaps;Profile.raceDifficulty=oldDifficulty;Notify("Circuit not changed · career save failed");return;}
            AutoStartCircuit=true;Time.timeScale=1;
            LocationLoading.Open(id);
        }
        public bool CommitAchievement(string id,TrialRecord record,int reward)
        {if(!BootReady)return false;
            if(Network.Active||Profile.rewardedEvents.Contains(id))return false;SyncActiveCondition();
            var oldRecords=Profile.records;var oldEvents=Profile.rewardedEvents;int oldCredits=Profile.credits;
            var records=new System.Collections.Generic.List<TrialRecord>(oldRecords);if(record!=null){records.RemoveAll(r=>r.key==record.key);records.Add(record);if(records.Count>1000)records.RemoveAt(0);}
            var events=new System.Collections.Generic.List<string>(oldEvents){id};if(events.Count>256)events.RemoveAt(0);
            Profile.records=records;Profile.rewardedEvents=events;Profile.credits=Mathf.Clamp(Economy.Credits+Mathf.Max(0,reward),0,10000000);
            if(!Profile.Save()){Profile.records=oldRecords;Profile.rewardedEvents=oldEvents;Profile.credits=oldCredits;Notify("Could not save the result · no reward applied");return false;}
            Economy.AddCredits(Mathf.Max(0,reward));return true;
        }
        public bool SaveCareer(int slot,string name)
        {if(!BootReady)return false;if(Racing&&!Racing.RetryPendingLap())return false;
            if(Network.Active){Notify("Leave LAN before saving a career");return false;}
            SyncActiveCondition();Profile.credits=Economy.Credits;Profile.weatherMode=Weather;
            bool saved=CareerStorage.Store(slot,name,Profile);Notify(saved?"Career saved to slot "+(slot+1):"Career save failed · current career retained");return saved;
        }
        public bool LoadCareer(int slot)
        {if(!BootReady)return false;if(Racing&&!Racing.RetryPendingLap())return false;
            if (RefuseScopedDriving800()) return false;
            if(Network.Active){Notify("Leave LAN before loading a career");return false;}
            var loaded=CareerStorage.Read(slot);if(loaded==null){Notify("No valid saved career in this slot");return false;}
            if(loaded.LoadBlocked){Notify(loaded.LoadIssue);return false;}if(!TrySave()){Notify("Current career could not be saved · career not switched");return false;}if(!loaded.Save()){Notify("Could not load career · current career retained");return false;}
            AutoStartCircuit=false;LocationLoading.Open(loaded.region);return true;
        }
        public void StartAt(string id,Vector3 position)=>TryStartAt(id,position);
        public bool TryStartAt(string id,Vector3 position)=>ChangeLocation(id,position,false);
        public bool SelectRegion(string id)=>ChangeLocation(id,null,false);
        bool ChangeLocation(string id,Vector3? position,bool resume)
        {if(!BootReady)return false;if(Racing&&!Racing.RetryPendingLap())return false;
            if (RefuseScopedDriving800()) return false;
            if(Network.Active){Notify("Leave LAN before selecting a start location");return false;}
            if(LocationLoading.Busy)return false;
            string previousRegion=Profile.region,previousStart=Profile.startRegion;bool previousCustom=Profile.customStart;float x=Profile.startX,z=Profile.startZ;
            Profile.region=id;
            if(position.HasValue){Profile.customStart=true;Profile.startRegion=id;Profile.startX=position.Value.x;Profile.startZ=position.Value.z;}
            SyncActiveCondition();Profile.credits=Economy.Credits;Profile.weatherMode=Weather;
            if(!Profile.Save())
            {
                Profile.region=previousRegion;Profile.startRegion=previousStart;Profile.customStart=previousCustom;Profile.startX=x;Profile.startZ=z;
                Notify("Could not save the new location · current drive retained");return false;
            }
            if(resume)resumeUpdatedArea=id;Time.timeScale=1;LocationLoading.Open(id);return true;
        }

        public void StartRun()
        {if(!BootReady)return;
            if (RefuseScopedDriving800()) return;
            if(World.IsCircuit){Racing.Begin(Profile.trackMode);return;}
            if(RegionCache.Safe(World.Route.Region?.id)){Notify("Downloaded regions offer free driving; County Run is available in the bundled worlds");return;}
            if(Network.Active){Notify("LAN prototype supports free drive; races are a later stage");return;}
            if(Wanted>0){Notify("Resolve the pursuit before starting a challenge");return;}
            Car.Recover(World.Route.At(15,World.Route.LaneOffset(15)),World.Route.Rotation(15));Run.Start();nearestCheckpoint=0;LastResult="";previousGateDistance=-1;lastPosition=Car.transform.position;PlaceGate();Notify("COUNTY RUN • all 8 checkpoints • collisions add 3 seconds");
        }
        void PlaceGate()
        {
            gate.gameObject.SetActive(Run.Active);if(!Run.Active)return;
            float d=(Run.Next+1)*World.Route.Length/8;gate.SetPositionAndRotation(World.Route.At(d),World.Route.Rotation(d));
        }
        void UpdateRun()
        {
            if(!Run.Active)return;
            float signed=Vector3.Dot(Car.transform.position-gate.position,gate.forward);
            // Crossing a plane prevents high-speed tunnelling through checkpoint volumes.
            if(previousGateDistance<0&&signed>=0&&Mathf.Abs(Vector3.Dot(Car.transform.position-gate.position,gate.right))<World.Route.Width(Progress)*.5f+1&&Vector3.Distance(Car.transform.position,gate.position)<20&&Car.Speed>0)
            {
                Run.Pass(nearestCheckpoint,true);nearestCheckpoint++;
                if(Run.Finished)
                {
                    float gold=World.Route.Length/24,silver=World.Route.Length/18;string medal=Run.Medal(gold,silver);int reward=medal=="GOLD"?900:medal=="SILVER"?650:400;Economy.AddCredits(reward);
                    LastResult=$"{medal}  {Run.Result:F2}s  +{reward} credits";Notify(LastResult);
                    if(Profile.bestLap<=0||Run.Result<Profile.bestLap)Profile.bestLap=Run.Result;Save();gate.gameObject.SetActive(false);
                }
                else {PlaceGate();signed=Vector3.Dot(Car.transform.position-gate.position,gate.forward);}
            }
            previousGateDistance=signed;
        }
        public void SyncActiveCondition()
        {if(!BootReady)return;
            if(Car==null||Profile?.garage?.Active==null)return;var owned=DrivenCar;owned.damage=Car.Damage;Car.CaptureRaceCondition(owned);owned.dents=new System.Collections.Generic.List<SavedDent>(Car.Visual.RecordedDents);
        }
        public bool GarageChange(System.Func<GarageState,int,GarageTransaction> create)
        {if(!BootReady)return false;if(IsRenting){Notify("Return the hired car before changing your owned garage");return false;}if(Racing&&!Racing.RetryPendingLap())return false;
            if(Network.Active||Wanted>0||Run.Active||(Racing&&Racing.Competitive)){Notify("Finish the challenge or pursuit and leave LAN before changing cars");return false;}
            if(!AtGarage||Car.AbsoluteSpeed>1){Notify("Stop at HARBOR AUTO to fit parts or change cars");return false;}
            SyncActiveCondition();var transaction=create(Profile.garage,Economy.Credits);
            if(transaction==null){Notify("Cannot apply: check credits, garage capacity or fitted parts");return false;}
            var previous=Profile.garage;int credits=Profile.credits;Profile.garage=transaction.Garage;Profile.credits=transaction.Credits;Profile.weatherMode=Weather;
            if(!Profile.Save()){Profile.garage=previous;Profile.credits=credits;Notify("Could not save · purchase was not applied");return false;}
            int change=transaction.Credits-Economy.Credits;if(change>=0)Economy.AddCredits(change);else Economy.Spend(-change);
            var owned=Profile.garage.Active;Car.Visual.Rebuild(owned);using(BootTelemetry.Measure("main.actor.player.tuning"))Car.ApplyTuning(owned);Car.Body.linearVelocity=Vector3.zero;Car.Body.angularVelocity=Vector3.zero;if(!Car.TryInitializeGarageRest(out var garageRestReason))Debug.LogWarning("Garage rest fit refused: "+garageRestReason);Drifts.Cancel();Notify(transaction.Message);return true;
        }
        public void Repair()
        {if(!BootReady)return;
            if(Car.Damage<1){Notify("Your car is already in good condition");return;}
            if(World.IsCircuit){Notify("Drive into the marked pit service bay for free repair");return;}
            if(Network.Active||Wanted>0||Run.Active){Notify("End the challenge/pursuit or leave LAN before free repair");return;}
            FreeRepair("Free roadside repair completed");
        }
        public bool FreeRepair(string message)
        {if(!BootReady)return false;if(Racing&&!Racing.RetryPendingLap())return false;
            if(IsRenting)
            {if(!TrySave()){Notify("Repair could not be saved · try again");return false;}Car.Repair();SyncActiveCondition();Notify(message);return true;}
            SyncActiveCondition();var before=Profile.garage;var repaired=before.Clone();repaired.Active.damage=0;repaired.Active.dents.Clear();Profile.garage=repaired;
            Profile.credits=Economy.Credits;if(!Profile.Save()){Profile.garage=before;Notify("Repair could not be saved · try again");return false;}
            Car.Repair();Notify(message);return true;
        }
        public void Recover()
        {
            if (RefuseScopedDriving800()) return;if(!BootReady)return;
            Drifts.Cancel();
            if(World.AutomaticWorld){Run.Cancel();gate.gameObject.SetActive(false);BeginAutomaticRecovery(false);return;}
            if(Run.Active){Run.Cancel();gate.gameObject.SetActive(false);Notify("Recovery used • challenge cancelled");}else Notify("Recovered to road • damage and police status retained");
            if(World.Route.Region!=null)
            {
                var region=World.Route.Region;var road=region.Nearest(Car.Body.position,out var point,out _);
                if(road!=null){point=region.DrivingLanePoint(road,point,Car.transform.forward,out var forward);Car.Recover(point,Quaternion.LookRotation(forward,Vector3.up));World.GeographicWorld.Refresh(point,true);Racing?.Recovery();Feedback.StopAll();return;}
            }
            float d=World.Route.Closest(Car.transform.position,out _,out _);Car.Recover(World.Route.At(d,World.Route.LaneOffset(d)),World.Route.Rotation(d));Racing?.Recovery();Feedback.StopAll();
        }
        public void TowToGarage(){if(!BootReady)return;if(RefuseScopedDriving800())return;if(Wanted>0||Network.Active){Notify("Resolve the pursuit or leave LAN before towing");return;}Run.Cancel();Drifts.Cancel();gate.gameObject.SetActive(false);if(World.AutomaticWorld){BeginAutomaticRecovery(true);return;}Car.Recover(World.Garage.position,World.Garage.rotation);Notify("Towed to HARBOR AUTO");}
        public void ApplyQuality(int level)
        {
            Profile.quality=Mathf.Clamp(level,0,2);QualitySettings.vSyncCount=0;
            QualitySettings.shadows=level==0?UnityEngine.ShadowQuality.Disable:UnityEngine.ShadowQuality.All;QualitySettings.shadowDistance=level==0?0:level==1?100:220;
            var pipeline=QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if(!pipeline)pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline){pipeline.renderScale=level==0?.85f:level==1?1:Profile.clarity==2?1.5f:Profile.clarity==1?1.25f:1;pipeline.msaaSampleCount=level==2?4:2;pipeline.shadowDistance=level==0?0:level==1?100:220;}
        }
        void RememberBetaStart()
        {
            if(!SinglePlayerBeta.Enabled||!Started||!World||World.IsCircuit||!Car||!Car.Grounded||!OnRoad||Network.Active)return;
            var region=World.Route.Region;if(region==null||!region.AutomaticWorld)return;
            var point=Car.Body.position;
            if(float.IsNaN(point.x)||float.IsNaN(point.z)||float.IsInfinity(point.x)||float.IsInfinity(point.z))return;
            Profile.customStart=true;Profile.startRegion=region.id;Profile.startX=point.x;Profile.startZ=point.z;
        }
        public void Save()=>TrySave();
        public bool TrySave(){if(Racing&&Racing.HasPendingLap)return false;if(!BootReady||Profile==null||LocationLoading.Busy)return false;RememberBetaStart();SyncActiveCondition();Profile.credits=Economy.Credits;Profile.weatherMode=Weather;return Profile.Save();}
        void OnDestroy(){RevokeActualVehicleRoster();DisposeBootClearingCamera();CancelBootWorker();if(!BootReady)CleanupIncompleteBootScene();else CountryUpdateService.Unbind(this);if(rainMaterial)Destroy(rainMaterial);if(rain)Destroy(rain.gameObject);if(snowMaterial)Destroy(snowMaterial);if(snow)Destroy(snow.gameObject);}
        void OnApplicationQuit(){Save();Time.timeScale=1;}
        void OnApplicationFocus(bool focus){if(!focus&&Started&&!Menu&&(Network==null||!Network.Active)&&!Application.runInBackground)SetMenu(true);}
    }
}
