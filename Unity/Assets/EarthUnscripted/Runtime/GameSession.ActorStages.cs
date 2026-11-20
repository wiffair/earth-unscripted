using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        // Private real suspension owner. Receipt maps record ORIGINAL states;
        // they are never caller readiness flags or a replacement activation path.
        sealed class BootActorState
        {
            readonly Dictionary<Behaviour, bool> flags = new Dictionary<Behaviour, bool>();
            struct BodyState { public bool Kinematic; public CollisionDetectionMode Mode; }
            readonly Dictionary<Rigidbody, BodyState> bodies = new Dictionary<Rigidbody, BodyState>();
            readonly Dictionary<Behaviour, bool> activatedFlags = new Dictionary<Behaviour, bool>();
            readonly Dictionary<Rigidbody, BodyState> activatedBodies = new Dictionary<Rigidbody, BodyState>();
            bool activated;

            public void Suspend(Behaviour behaviour)
            {
                if (!flags.ContainsKey(behaviour)) flags.Add(behaviour, behaviour.enabled);
                behaviour.enabled = false;
            }
            public void SuspendBody(Rigidbody body)
            {
                bodies[body] = new BodyState { Kinematic = body.isKinematic, Mode = body.collisionDetectionMode };
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.isKinematic = true;
            }
            public void SuspendVehicle(VehicleController car) { Suspend(car); SuspendBody(car.Body); }
            bool Suspended(Behaviour behaviour) => !behaviour || flags.ContainsKey(behaviour) && !behaviour.enabled;
            bool Restored(Behaviour behaviour) => !behaviour || activatedFlags.TryGetValue(behaviour, out bool enabled) && behaviour.enabled == enabled;
            public bool OwnsSuspendedVehicle(GameObject root)
            {
                var body = root.GetComponent<Rigidbody>();
                return !activated && body && bodies.ContainsKey(body) && body.isKinematic &&
                    root.GetComponent<CarVisual>() && Suspended(root.GetComponent<CarVisual>()) &&
                    Suspended(root.GetComponent<VehicleController>()) && Suspended(root.GetComponent<TrafficCar>()) &&
                    Suspended(root.GetComponent<PoliceDriver>());
            }
            public bool OwnsActivatedVehicle(GameObject root)
            {
                var body = root.GetComponent<Rigidbody>();
                return activated && body && activatedBodies.TryGetValue(body, out var original) &&
                    body.isKinematic == original.Kinematic && body.collisionDetectionMode == original.Mode &&
                    Restored(root.GetComponent<CarVisual>()) && Restored(root.GetComponent<VehicleController>()) &&
                    Restored(root.GetComponent<TrafficCar>()) && Restored(root.GetComponent<PoliceDriver>());
            }
            public void Activate()
            {
                // Same real physics/behaviour writes, in the original order.
                foreach (var item in bodies) if (item.Key)
                { item.Key.isKinematic = item.Value.Kinematic; item.Key.collisionDetectionMode = item.Value.Mode; }
                foreach (var item in flags) if (item.Key) item.Key.enabled = item.Value;
                foreach (var item in bodies) activatedBodies.Add(item.Key, item.Value);
                foreach (var item in flags) activatedFlags.Add(item.Key, item.Value);
                activated = true;
                flags.Clear(); bodies.Clear();
            }
            public void Forget()
            {
                flags.Clear(); bodies.Clear(); activatedFlags.Clear(); activatedBodies.Clear(); activated = false;
            }
        }
        public int BootTrafficPrepared {get;private set;}
        public int BootPatrolPrepared {get;private set;}
#if UNITY_EDITOR
        public static int? VerificationPatrolSeed;
#endif
        IEnumerator PrepareActorStages(string regionId,string[] args,int request)
        {
            var pending=new BootActorState();double began=BootTelemetry.Now;bool committed=false;
            try
            {
                if(request!=bootGeneration)yield break;
                BeginStagedActualVehicleRoster(pending);
                BootTrafficPrepared=BootPatrolPrepared=0;BootStatus="Preparing your car…";
                // Freeze the EXACT collider universe present at the old synchronous
                // actor-call boundary; do not advance outer chunks between yields.
                if(World.GeographicWorld)World.GeographicWorld.BeginBootActorPreparation();
                VehicleController preparedPlayer;
                using(BootTelemetry.Enter(bootTiming,"main.actor.player.prepare"))preparedPlayer=PreparePlayerActor(regionId,pending);
                yield return null;
                foreach(var step in SpawnTrafficStages(preparedPlayer,pending))
                {if(request!=bootGeneration)yield break;yield return null;}
                if(request!=bootGeneration)yield break;
                BootStatus="Preparing driving controls…";yield return null;
                if(request!=bootGeneration)yield break;
                // NO YIELD from publishing fields until Ready, ownership and actor
                // activation are committed. Lifecycle Start/Update happens later.
                using(BootTelemetry.Enter(bootTiming,"main.actor.commit"))
                {
                    CompletePreparedActorFields(regionId,args,preparedPlayer);
                    BootReady=true;RestoreDrivingRendering();Application.targetFrameRate=Menu?30:GraphicsQualityPreset.DrivingFrameLimit(Profile.quality);
                    pending.Activate();
                    SealActualVehicleBoot();
                    CommitScopedSessionActors800();
                    ObserveReadyBootDriveCamera(request);
                    if(World.GeographicWorld)World.GeographicWorld.EndBootActorPreparation();
                    CommitBootSceneOwnership();BootStatus="";bootWork?.Dispose();bootWork=null;committed=true;
                }
                bootTiming?.Complete("ready");
            }
            finally
            {
                // Never re-enable cancelled actors. Central ownership rollback
                // destroys roots/components; this drops only managed references.
                pending.Forget();bootTiming?.Wall("actors.stage.wall",began,committed?"ready":"cancelled-or-failed");
            }
        }
        VehicleController PreparePlayerActor(string regionId,BootActorState suspended)
        {
            using var populationFactory = BeginActualVehicleFactory(this, "player", VehiclePopulation.ActualVehicleRosterDomain.Role.Player);
            var player=OwnBootRoot(new GameObject("Player sports coupe"));player.layer=8;player.transform.SetPositionAndRotation(World.Route.At(35,World.Route.LaneOffset(35))+Vector3.up*.7f,World.Route.Rotation(35));
            if (World.ScopedSession800 != null)
                player.transform.SetPositionAndRotation(World.Route.At(World.ScopedSession800.ActorDistance(0)) + Vector3.up * .7f, World.Route.Rotation(0));
            AddBootCollider(player);
            var owned=Profile.garage.Active;var visual=player.AddComponent<CarVisual>();suspended.Suspend(visual);
            using(BootTelemetry.Measure("main.actor.player.visual"))visual.Build(CarColours.Paint[owned.paint],true,owned.model,owned.wheels,owned.bodyKit,owned.finish);
            var result=player.AddComponent<VehicleController>();result.Visual=visual;result.ParkingAutomaticEnabled=true;
            using(BootTelemetry.Measure("main.actor.player.tuning"))result.ApplyTuning(owned);
            // Recover while dynamic, as before; writing kinematic velocity would
            // produce Unity warnings. Freeze BEFORE the first coroutine yield.
            if(Profile.customStart&&Profile.startRegion==regionId&&World.Route.Region!=null&&!World.IsCircuit)
            {var region=World.Route.Region;var road=region.Nearest(new Vector3(Profile.startX,0,Profile.startZ),out var point,out _);if(road!=null){point=region.DrivingLanePoint(road,point,Vector3.forward,out var forward);result.Recover(point,Quaternion.LookRotation(forward,Vector3.up));World.GeographicWorld.Refresh(point,true);}}
            if(World.AutomaticWorld)
            {
                if(!PlaceClearAutomaticStart(result))throw new InvalidOperationException("Could not find a clear supported road with room to drive away. Choose a nearby road and retry.");
            }
            else using(BootTelemetry.Measure("main.actor.player.supported-rest"))
                if(!result.TryInitializeSupportedRest(result.Body.position-Vector3.up*.7f,result.Body.rotation,out var restError,out bool supportMissing))
                {Debug.Log("Initial vehicle keeps safe loading drop"+(supportMissing?" (missing footprint support)":"")+": "+restError);}
            suspended.SuspendVehicle(result);
            populationFactory.Commit();
            return result;
        }
        void CompletePreparedActorFields(string regionId,string[] args,VehicleController preparedPlayer)
        {
            Weather=Profile.weatherMode;if(Array.IndexOf(args,"-earthUnscriptedLive")>=0){Weather=5;Profile.clockMode=2;}
            Car=preparedPlayer;Input=gameObject.AddComponent<DriverInput>();Input.Profile=Profile;Car.Input=Input;Car.Crashed+=OnCrash;
            var tyreEffects=Car.gameObject.AddComponent<TyreEffects>();tyreEffects.Car=Car;
            Feedback=gameObject.AddComponent<FeedbackDriver>();Feedback.Profile=Profile;Feedback.Car=Car;Feedback.Input=Input;
            // DrivingAudio.Awake no longer starts output for a non-ready owner.
            audioDriver=gameObject.AddComponent<DrivingAudio>();audioDriver.Profile=Profile;audioDriver.Car=Car;audioDriver.Input=Input;
            using(BootTelemetry.Measure("main.actor.camera"))
            {
                var cameraObject=OwnBootRoot(new GameObject("Driving camera"));cameraObject.tag="MainCamera";var camera=cameraObject.AddComponent<Camera>();camera.nearClipPlane=.06f;camera.farClipPlane=World.AutomaticWorld?20000:World.Route.Region!=null?1300:1900;
                cameraObject.AddComponent<AudioListener>();cameraObject.AddComponent<AudioMixSafety>();var extra=cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();extra.renderPostProcessing=true;extra.antialiasing=UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                View=cameraObject.AddComponent<DriveCamera>();View.Car=Car;View.Input=Input;cameraObject.transform.position=Car.transform.position-Car.transform.forward*7+Vector3.up*3;
                if(World.GeographicWorld)World.GeographicWorld.Observer=Car.transform;
            }
            using(BootTelemetry.Measure("main.actor.gate"))BuildGate();using(BootTelemetry.Measure("main.actor.weather"))BuildWeather();
            using(BootTelemetry.Measure("main.actor.services"))
            {
                Conditions=gameObject.AddComponent<EnvironmentController>();Conditions.Session=this;Conditions.Shelter=gameObject.AddComponent<TunnelWeatherController>();Conditions.Shelter.Session=this;
                Network=gameObject.AddComponent<LanSession>();Network.Session=this;Racing=gameObject.AddComponent<CircuitSession>();Racing.Session=this;Pits=gameObject.AddComponent<PitService>();Pits.Initialize(this);Downloads=gameObject.AddComponent<GlobalRegions>();if (!IsScopedReaderSession800) gameObject.AddComponent<LivingScenery>().Initialize(this);
                if(Array.IndexOf(args,"-earthUnscriptedLanProof")>=0&&!FindAnyObjectByType<LanVerificationDriver>())OwnBootRoot(new GameObject("Standalone LAN proof")).AddComponent<LanVerificationDriver>().Session=this;
                if(!IsScopedReaderSession800)gameObject.AddComponent<RegionalPackActivation>().Initialize(this);
                if (SinglePlayerBeta.Enabled && World.AutomaticWorld) gameObject.AddComponent<RailTrainSystem>().Initialize(this);
                if(SinglePlayerBeta.Enabled&&!GetComponent<Radio.CarRadio>())gameObject.AddComponent<Radio.CarRadio>().Initialize(this);
                gameObject.AddComponent<DrivingUI>().Session=this;using(BootTelemetry.Measure("main.actor.quality"))ApplyQuality(Profile.quality);Updates=CountryUpdateService.Bind(this);
            }
            lastPosition=Car.transform.position;Time.timeScale=0;gameObject.AddComponent<BuildCapture>();
        }
        // Preserve role-specific vertical collider setup; share reviewed XZ fit.
        // ApplyTuning remains authority for the player model-specific XZ box.
        static void AddBootCollider(GameObject root,bool traffic=false)
        {var box=root.AddComponent<BoxCollider>();box.center=traffic?new Vector3(0,.85f,0):new Vector3(0,.86f,0);box.size=traffic?new Vector3(2,.9f,4.25f):new Vector3(1.95f,.85f,4.15f);VehicleGeometry.ApplyCollider(box,0);}
        TrafficCar MakeBootTraffic(string name,float distance,int dir,Color colour,VehicleController player,BootActorState suspended,Vector3? automaticPoint=null,Vector3? automaticForward=null)
        {
            var g=OwnBootRoot(new GameObject(name));
            try
            {
            g.transform.SetPositionAndRotation(VehicleGeometry.RoadRestPosition(World.Route.At(distance,World.Route.LaneOffset(distance,dir)),0),World.Route.Rotation(distance)*Quaternion.Euler(0,dir<0?180:0,0));
            if(World.Route.Region?.AutomaticWorld==true)
            {
                var centre=World.Route.At(distance);var projection=World.Route.Region.ProjectRoadForVehicle(centre,centre.y);
                if(projection.Road!=null)
                {
                    var point=World.Route.Region.DrivingLanePoint(projection.Road,projection.Source.Point,World.Route.Forward(distance)*dir,out var forward);
                    g.transform.SetPositionAndRotation(VehicleGeometry.RoadRestPosition(point,0),Quaternion.LookRotation(forward,Vector3.up));
                }
            }
            if(automaticPoint.HasValue && automaticForward.HasValue)
                g.transform.SetPositionAndRotation(VehicleGeometry.RoadRestPosition(automaticPoint.Value,0),Quaternion.LookRotation(automaticForward.Value,Vector3.up));
            if (World.ScopedSession800 != null)
                g.transform.SetPositionAndRotation(VehicleGeometry.RoadRestPosition(World.Route.At(distance), 0), World.Route.Rotation(distance) * Quaternion.Euler(0, dir < 0 ? 180 : 0, 0));
            AddBootCollider(g,true);
            var v=g.AddComponent<CarVisual>();if(suspended!=null)suspended.Suspend(v);v.Build(colour);
            var t=g.AddComponent<TrafficCar>();t.Session=this;t.Route=World.Route;t.Player=player;t.Visual=v;t.Distance=distance;t.Direction=dir;t.Lane=World.Route.LaneOffset(distance,dir);
            if(suspended!=null){suspended.Suspend(t);suspended.SuspendBody(g.GetComponent<Rigidbody>());}return t;
            }
            catch{g.SetActive(false);if(Application.isPlaying)Destroy(g);else DestroyImmediate(g);throw;}
        }
        IEnumerable<int> SpawnTrafficStages(VehicleController player,BootActorState suspended)
        {
            if(BenchmarkDrives.Active&&!BenchmarkDrives.LiveTraffic){ResetDeferredAutomaticTraffic();yield break;}
            // A repeated compatibility call must not discard required cohort0 slots.
            if(deferredAutomaticActors.Count>0&&ReferenceEquals(World,automaticTrafficOwnerWorld)&&ReferenceEquals(player,automaticTrafficOwnerPlayer))yield break;
            ResetDeferredAutomaticTraffic();
            if(World.IsCircuit)yield break;
            int populationCohort = NextActualTrafficCohort();
            var fleet=new TrafficCar[VehiclePopulation.ActualVehicleRosterDomain.RoadTrafficSlots];var random=new System.Random(128);
            var automaticSites=AutomaticTrafficSites(player,fleet.Length+SourcePoliceSlots);
            automaticTrafficFleet=fleet;automaticTrafficOwnerWorld=World;automaticTrafficOwnerPlayer=player;
            // Reserve the declared one real base patrol; no hidden response identities.
            int trafficSites=automaticSites==null?fleet.Length:InitialSourceTrafficCount(automaticSites.Count,SourcePoliceSlots);
            for(int i=0;i<fleet.Length;i++)
            {
                if(automaticSites!=null&&i>=trafficSites)
                {
                    int slot=i;int dir=slot%2==0?1:-1;float d=World.Route.Length*(slot+.5f)/fleet.Length;var colour=Color.HSVToRGB((float)random.NextDouble(),.38f,.65f);
                    deferredAutomaticActors.Enqueue(site=>{using var factory=BeginActualVehicleFactory(this,ActualTrafficSlot(populationCohort,slot,false),VehiclePopulation.ActualVehicleRosterDomain.Role.Traffic);var car=MakeBootTraffic("Traffic "+slot,d,dir,colour,Car,null,site.Point,site.Forward);car.Speed=5;fleet[slot]=car;RefreshAutomaticTrafficObservations();factory.Commit();});
                    continue;
                }
                using(BootTelemetry.Enter(suspended==null?BootTelemetry.Current:bootTiming,"main.actor.traffic.car",i.ToString()))
                {
                    using var populationFactory = BeginActualVehicleFactory(this, ActualTrafficSlot(populationCohort, i, false), VehiclePopulation.ActualVehicleRosterDomain.Role.Traffic);
                    int dir=i%2==0?1:-1;
                    float d=World.ScopedSession800 != null ? World.ScopedSession800.ActorDistance(i + 1) : World.Route.Length*(i+.5f)/fleet.Length;
                    fleet[i]=MakeBootTraffic("Traffic "+i,d,dir,Color.HSVToRGB((float)random.NextDouble(),.38f,.65f),player,suspended,
                        automaticSites==null?(Vector3?)null:automaticSites[i].Point,automaticSites==null?(Vector3?)null:automaticSites[i].Forward);
                    if(automaticSites!=null)fleet[i].Speed=5;
                    populationFactory.Commit();
                }
                if(suspended!=null){BootTrafficPrepared=i+1;BootStatus="Preparing road traffic "+(i+1)+" / "+fleet.Length;}yield return 0;
            }
            foreach(var t in fleet)if(t)t.Fleet=fleet;
            var patrolRandom=new System.Random();
#if UNITY_EDITOR
            if(VerificationPatrolSeed.HasValue&&!string.IsNullOrEmpty(PlayerProfile.StorageOverride))patrolRandom=new System.Random(VerificationPatrolSeed.Value);
#endif
            for(int i=0;i<(populationCohort==0?SourcePoliceSlots:0);i++)
            {
                if(automaticSites!=null&&trafficSites+i>=automaticSites.Count)
                {
                    int slot=i;deferredAutomaticActors.Enqueue(site=>{using var factory=BeginActualVehicleFactory(this,ActualTrafficSlot(populationCohort,slot,true),VehiclePopulation.ActualVehicleRosterDomain.Role.Police);PreparePatrolActor(slot,100,site.Point,site.Forward,Car,null);factory.Commit();});continue;
                }
                float d=100;Vector3 spot=World.Route.At(d);Physics.SyncTransforms();
                if (World.ScopedSession800 != null)
                { d = World.ScopedSession800.ActorDistance(25 + i); spot = World.Route.At(d); }
                for(int attempt=0;World.ScopedSession800 == null && automaticSites==null && attempt<80;attempt++)
                {
                    bool accepted;
                    using(BootTelemetry.Enter(suspended==null?BootTelemetry.Current:bootTiming,"main.actor.patrol.search",i+"/"+attempt))
                    {
                        d=World.Route.Length*((i+(float)patrolRandom.NextDouble())/SourcePoliceSlots);float lateral=World.Route.DrivingSide*(World.Route.Width(d)*.5f+3.2f);spot=World.Route.At(d,lateral);
                        accepted=!(Vector3.Angle(World.Route.Forward(d-8),World.Route.Forward(d+8))>18);
                        if(accepted&&World.Route.Region!=null){var r=World.Route.Region.Nearest(World.Route.At(d),out var q,out _);if(r==null||r.bridge||r.tunnel)accepted=false;else{float at=World.Route.Region.RoadProgress(r,q);if(Mathf.Min(at,r.length-at)<18)accepted=false;}}
                        if(accepted&&Physics.CheckBox(spot+Vector3.up*1.25f,new Vector3(1.2f,.4f,2.6f),World.Route.Rotation(d),~(1<<8),QueryTriggerInteraction.Ignore))accepted=false;
                    }
                    if(accepted)break;yield return 0;
                }
                using(BootTelemetry.Enter(suspended==null?BootTelemetry.Current:bootTiming,"main.actor.patrol.prepare",i.ToString()))
                {
                    if(automaticSites!=null)spot=automaticSites[trafficSites+i].Point;
                    if (World.ScopedSession800 == null && automaticSites==null)
                    { var layby=Geometry.Box("Roadside patrol layby",World.transform,spot-Vector3.up*.08f,new Vector3(4.8f,.12f,8.5f),Geometry.Surface("Layby gravel",new Color(.34f,.33f,.28f),.3f,1),true);layby.transform.rotation=World.Route.Rotation(d); }
                    using var populationFactory = BeginActualVehicleFactory(this, ActualTrafficSlot(populationCohort, i, true), VehiclePopulation.ActualVehicleRosterDomain.Role.Police);
                    PreparePatrolActor(i,d,spot,automaticSites==null?(Vector3?)null:automaticSites[trafficSites+i].Forward,player,suspended);
                    populationFactory.Commit();
                }
                if(suspended!=null){BootPatrolPrepared=i+1;BootStatus="Preparing roadside patrols "+(i+1)+" / "+SourcePoliceSlots;}yield return 0;
            }
        }
        void PreparePatrolActor(int i,float d,Vector3 spot,Vector3? automaticForward,VehicleController player,BootActorState suspended)
        {
            var marker=MakeBootTraffic("County patrol "+i,d,1,Color.white,player,suspended,automaticForward.HasValue?(Vector3?)spot:null,automaticForward);
            try
            {
            marker.Police=true;if(!automaticForward.HasValue)marker.GetComponent<Rigidbody>().position=spot+Vector3.up*.7f;
            var blue=Geometry.Material("Police blue",Color.blue);blue.EnableKeyword("_EMISSION");blue.SetColor("_EmissionColor",Color.blue*4);Geometry.Box("Police lightbar",marker.transform,new Vector3(0,Core.VehicleDimensions.ForModel(0).RoofLocalY+.075f,-.3f),new Vector3(1,.12f,.28f),blue);
            var driver=marker.gameObject.AddComponent<PoliceDriver>();driver.Configure(this,marker);if(automaticForward.HasValue&&!driver.Car.TryInitializeSupportedRest(spot,marker.transform.rotation,out var patrolRestError,out _))throw new InvalidOperationException("Patrol needs supported road: "+patrolRestError);patrols.Add(driver);RefreshAutomaticTrafficObservations();if(i==0)police=marker;
            if(suspended!=null){suspended.Suspend(driver);suspended.SuspendVehicle(driver.Car);}
            }
            catch{patrols.RemoveAll(p=>p&&p.Marker==marker);if(police==marker)police=null;marker.gameObject.SetActive(false);if(Application.isPlaying)Destroy(marker.gameObject);else DestroyImmediate(marker.gameObject);throw;}
        }
    }
}
