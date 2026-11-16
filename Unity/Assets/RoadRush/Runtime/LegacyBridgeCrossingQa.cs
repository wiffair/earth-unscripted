using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json.Linq;
namespace RoadRush
{
    // Opt-in observer/stock input driver. Never constructs surfaces or alters road heights.
    public sealed class LegacyBridgeCrossingQa : MonoBehaviour
    {
        const string Flag="-roadRushLegacyBridgeQa", Prefix="/private/tmp/rr019-legacy-bridge-";
        static string output,regionFile,inputHash,privateHash,routeFile,routeHash,baseline,phase,setupError;
        static bool requested,ownsOutput; static RouteSpec spec; static Vector3 seed;
        [Serializable] public sealed class Step {public string road;public bool forward=true;}
        [Serializable] public sealed class RouteSpec {public string bridge="38705787-75";public Step[] steps;}
        [Serializable] public sealed class Pose {public string name;public Vector3 eye,target;public float fieldOfView=55;}
        [Serializable] public sealed class Views {public string bridge,inputSHA256;public Pose[] poses;}
        [Serializable] public sealed class Wheel {public bool grounded,rayHit,underGeoWorld;public string collider,entity;public int triangle;public Vector3 actualContact,rayContact,normal;public float routeProgress,routeSeparation,expectedSurfaceDelta;}
        [Serializable] public sealed class Tick {public float time,progress,lateral,separation,speed,damage;public Vector3 position;public Wheel[] wheels;public int groundRoadContacts,deckContacts,poseRevision=-1;public string registryUnavailable;}
        [Serializable] public sealed class Shot {public string name,file,sha256;public int frame,width,height,renders;public Vector3 cameraPosition;public Quaternion cameraRotation;public float fov;}
        [Serializable] public sealed class Join {public string name,node;public Vector3 source;public float groundEvaluator;public Surface[] hits;}
        [Serializable] public sealed class Surface {public string collider,entity;public int triangle;public Vector3 point,normal;}
        [Serializable] public sealed class MeshRecord
        {public string kind,objectName,objectEntity,gameObjectEntity,meshEntity,geometrySHA256,error;public bool active,enabled,trigger;public int layer,vertices,triangles,verticalOrDegenerateTriangles;public float[] localToWorld;}
        [Serializable] public sealed class TriangleHit
        {public int meshRecord,triangle;public Vector3 point,normal,a,b,c;}
        [Serializable] public sealed class SeamPair
        {public string kind,asphaltCategory;public int deckMesh,deckTriangle,asphaltMesh,asphaltTriangle;public float signedHeightDelta;public bool withinFiveMillimetres;}
        [Serializable] public sealed class ApronPair {public string kind;public int apronMesh,apronTriangle,roadMesh,roadTriangle;public float apronMinusRoad,intentionalOffset=.003f;}
        [Serializable] public sealed class RenderColliderPair {public string owner;public int visualMesh,visualTriangle,colliderMesh,colliderTriangle;public float signedHeightDelta;}
        [Serializable] public sealed class EdgeWitness
        {public int meshRecord,triangle;public Vector3 a,b,c,actualEdgePoint,planeAtQuery,normal;public float xzDistance,planeDistance3D;public bool withinFiveMillimetres;}
        [Serializable] public sealed class PortalPair
        {public string kind,asphaltCategory;public int deckMesh,deckTriangle,asphaltMesh,asphaltTriangle;public bool deckUsesEdgeWitness,asphaltUsesEdgeWitness;public Vector3 deckActualPoint,asphaltActualPoint;public float deckEdgeDistance,asphaltEdgeDistance,deckPlaneY,asphaltPlaneY,intentionalAsphaltOffset,adjustedDelta,actualPairXZDistance,actualPairDistance3D;public bool withinFiveMillimetres;}
        [Serializable] public sealed class SectionPoint
        {public string join,station;public float along,lateral;public Vector3 coordinate;public TriangleHit[] hits;public EdgeWitness[] edgeWitnesses;public PortalPair[] drivablePortalPairs;public SeamPair[] mappedRoadPairs;public RenderColliderPair[] visualColliderPairs;public ApronPair[] apronRoadPairs;public float[] apronMinusDeckOffsets;public int renderDeckHits,colliderDeckHits,renderRoadHits,colliderRoadHits;}
        sealed class Geometry {public Vector3[] vertices;public int[] triangles;public string hash;}
        sealed class Candidate {public Mesh mesh;public Transform transform;public Bounds bounds;public int record;}
        readonly List<MeshRecord> meshRecords=new List<MeshRecord>();readonly Dictionary<Mesh,Geometry> geometries=new Dictionary<Mesh,Geometry>();
        [Serializable] public sealed class Report
        {
            public string status,error,phase,buildGUID,regionId,originalRegionId,regionName,inputFile,inputSHA256,privateRegionSHA256,routeFile,routeSHA256,bridge,profile,regionCache,updateCache;
            public string scope="Continuous stock-car input/physics traversal plus camera-moved join inspection; no full-world/hardware/display-FPS acceptance.";
            public bool bootReady,driveReady,freshDriveRender,baselineViewsUsed,approachPassed,deckPassed,farRoadPassed,poseDiscontinuity;
            public float bridgeStart,bridgeEnd,startProgress,finishProgress,travelMetres,elapsed,maxDamage;public int recoveries,initialPlacements,runtimeErrors,fixedSamples,airborneSamples;
            public float seamTargetMetres=.005f,intentionalApronOffsetMetres=.003f;public bool seamCoverageComplete,drivablePortalCoverageComplete;public string seamStatus,drivablePortalStatus;public MeshRecord[] actualMeshes;public SectionPoint[] endpointSections;
            public RailInspection[] railInspections;public int railCrossingCandidates,railCrossingRailCount,railViewsOmitted;public string railInspectionStatus;
            public float[] geoMatrix;public Step[] steps;public Tick[] ticks;public Shot[] shots;public Join[] joins;public string[] notes;
        }
        readonly Report report=new Report();readonly List<Tick> ticks=new List<Tick>();readonly List<Shot> shots=new List<Shot>();readonly List<string> notes=new List<string>();
        GameSession session;GeoWorld world;Camera camera;RoutePath route;RaceDriver driver;GeoRoad bridge;float bridgeStart,bridgeEnd,startD,endD;int renders,recoveries;bool finished,observing;Vector3 priorPosition;double driveBegan;int nextPhase,initialPoseRevision=-1;
        static string Arg(string[] a,string n,string fallback=null){int i=Array.IndexOf(a,n);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
        static string Hash(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static RoutePath Resolve(GeoRegion region,out GeoRoad target,out float begin,out float end)
        {
            if(spec?.steps==null||spec.steps.Length<3||spec.steps.Length>5)throw new InvalidOperationException("Explicit3–5 source road steps required");
            var points=new List<Vector3>();string previous=null;target=null;begin=end=0;float distance=0;
            foreach(var step in spec.steps)
            {
                var matches=region.roads.Where(r=>r.id==step.road).ToArray();if(matches.Length!=1)throw new InvalidOperationException("Missing/ambiguous source road "+step.road);
                var road=matches[0];if(road.tunnel||road.points==null||road.points.Length<2)throw new InvalidOperationException("Unsuitable road "+step.road);
                if(road.oneWay!=0&&(road.oneWay>0)!=step.forward)throw new InvalidOperationException("Wrong-way route "+road.id);
                string from=step.forward?road.start:road.end,to=step.forward?road.end:road.start;
                if(previous!=null&&from!=previous)throw new InvalidOperationException("Source nodes not connected "+previous+"→"+from);
                var ps=step.forward?road.points:road.points.Reverse().ToArray();
                if(points.Count>0&&Vector3.Distance(points[points.Count-1],ps[0])>.1f)throw new InvalidOperationException("Source endpoints disagree "+road.id);
                if(road.id==spec.bridge){if(target!=null)throw new InvalidOperationException("Repeated bridge");target=road;begin=distance;}
                else if(road.bridge)throw new InvalidOperationException("Only target bridge allowed in bounded route");
                for(int i=0;i<ps.Length;i++){if(points.Count==0)points.Add(ps[i]);else if(i>0){distance+=Vector3.Distance(points[points.Count-1],ps[i]);points.Add(ps[i]);}}
                if(road.id==spec.bridge)end=distance;previous=to;
            }
            if(target==null||begin<20||distance-end<20)throw new InvalidOperationException("Genuine ground approaches both sides required");
            var copy=region.SnapshotCopy();copy.route=points.ToArray();return new RoutePath(copy);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)] static void Isolate()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,Flag)<0)return;requested=true;
            PlayerProfile.StorageOverride=Path.Combine(Application.temporaryCachePath,"refused-bridge-"+Guid.NewGuid().ToString("N")+".json");RegionCache.OverrideFolder=PlayerProfile.StorageOverride+".regions";CountryUpdateService.OverrideFolder=PlayerProfile.StorageOverride+".updates";CountryUpdateService.AllowVerification=false;
            try
            {
                if(Application.isEditor||Application.companyName!="RoadRush Verification"||Application.productName!="LoadingCameraQA"||Application.identifier!="games.blackwood.roadrush.qa.loadingcamera")throw new InvalidOperationException("Separate LoadingCameraQA product required");
                if(args.Any(a=>a.StartsWith("-roadRush",StringComparison.Ordinal)&&a!=Flag&&a!="-roadRushBridgeRegion"&&a!="-roadRushBridgeRoute"&&a!="-roadRushBridgeViews"&&a!="-roadRushBridgePhase"))throw new InvalidOperationException("Competing RoadRush mode forbidden");
                output=Path.GetFullPath(Arg(args,Flag)??"");if(!output.StartsWith(Prefix,StringComparison.Ordinal)||Directory.Exists(output))throw new IOException("Fresh private bridge output required");Directory.CreateDirectory(output);ownsOutput=true;
                PlayerProfile.StorageOverride=Path.Combine(output,"profile.json");RegionCache.OverrideFolder=Path.Combine(output,"regions");CountryUpdateService.OverrideFolder=Path.Combine(output,"updates");
                regionFile=Path.GetFullPath(Arg(args,"-roadRushBridgeRegion")??"");routeFile=Path.GetFullPath(Arg(args,"-roadRushBridgeRoute")??"");
                if(!File.Exists(regionFile)||new FileInfo(regionFile).Length>32*1024*1024||!File.Exists(routeFile)||new FileInfo(routeFile).Length>16384)throw new IOException("Explicit region and small source route JSON required");
                inputHash=Hash(regionFile);routeHash=Hash(routeFile);var manifest=JObject.Parse(File.ReadAllText(routeFile));if((string)manifest["sourceWorldSha256"]!=inputHash)throw new InvalidOperationException("Route manifest source world hash mismatch");spec=new RouteSpec{bridge=(string)manifest["targetBridge"],steps=((JArray)manifest["orderedRoads"]).Select(v=>new Step{road=(string)v["roadId"],forward=(bool)v["forward"]}).ToArray()};var json=JObject.Parse(File.ReadAllText(regionFile));var supplied=JsonUtility.FromJson<GeoRegion>(json.ToString());
                if(supplied==null||supplied.circuit)throw new InvalidOperationException("Normal GeoRegion required");
                string originalId=supplied.id;if(!RegionCache.Safe(originalId)){supplied.id="download-bridge-qa-"+inputHash.Substring(0,16);json["id"]=supplied.id;}
                Directory.CreateDirectory(RegionCache.Folder);string cache=RegionCache.PathFor(supplied.id);if(originalId==supplied.id)File.Copy(regionFile,cache);else File.WriteAllText(cache,json.ToString());privateHash=Hash(cache);
                // Only top-level id changes for bundled IDs; source geometry fields remain JSON values.
                var loaded=GeoRegion.Load(supplied.id);var path=Resolve(loaded,out _,out float bs,out _);float d=Mathf.Max(3,bs-30);seed=path.At(d,path.LaneOffset(d));
                var profile=new PlayerProfile{region=supplied.id,startRegion=supplied.id,customStart=true,startX=seed.x,startZ=seed.z,regionTimeZone=supplied.timeZone,detailedScenery=true,quality=2,volume=0,rumble=0,wheelEnabled=false,wheelForceEnabled=false,weatherMode=0,clockMode=0,fixedHour=14};File.WriteAllText(PlayerProfile.StorageOverride,JsonUtility.ToJson(profile,true));
                baseline=Arg(args,"-roadRushBridgeViews");if(baseline!=null&&(!Path.GetFullPath(baseline).StartsWith(Prefix,StringComparison.Ordinal)||new FileInfo(baseline).Length>65536))throw new IOException("Private saved views required");phase=Arg(args,"-roadRushBridgePhase","inspection");Application.runInBackground=true;
            }
            catch(Exception e){setupError=e.ToString();GameSession.SuppressAutomaticLaunch=true;}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Attach(){if(requested)new GameObject("Private continuous bridge QA").AddComponent<LegacyBridgeCrossingQa>();}
        void Rendered(ScriptableRenderContext c,Camera actual){if(actual==camera)renders++;}
        void Log(string condition,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        IEnumerator Start(){RenderPipelineManager.endCameraRendering+=Rendered;Application.logMessageReceived+=Log;yield return Guard(Run());}
        IEnumerator Guard(IEnumerator work)
        {
            try{while(true){bool more=false;object next=null;Exception failure=null;try{more=work.MoveNext();if(more)next=work.Current;}catch(Exception e){failure=e;}if(failure!=null){Finish("QA_ERROR",failure.ToString(),1);yield break;}if(!more)yield break;if(next is IEnumerator inner)yield return Guard(inner);else yield return next;if(finished)yield break;}}
            finally{(work as IDisposable)?.Dispose();}
        }
        IEnumerator Run()
        {
            if(setupError!=null){Finish("ISOLATION_OR_INPUT_REFUSED",setupError,1);yield break;}
            report.inputFile=regionFile;report.inputSHA256=inputHash;report.privateRegionSHA256=privateHash;report.originalRegionId=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(regionFile)).id;report.routeFile=routeFile;report.routeSHA256=routeHash;report.steps=spec.steps;report.bridge=spec.bridge;report.phase=phase;report.buildGUID=Application.buildGUID;report.profile=PlayerProfile.StoragePath;report.regionCache=RegionCache.Folder;report.updateCache=CountryUpdateService.OverrideFolder;
            double deadline=Time.realtimeSinceStartupAsDouble+300;
            while(Time.realtimeSinceStartupAsDouble<deadline){session=FindAnyObjectByType<GameSession>();if(session&&session.BootFailed)throw new InvalidOperationException(session.BootStatus);if(session&&session.BootReady&&session.World&&session.World.GeographicWorld&&session.Car&&session.View)break;yield return null;}
            if(!session||!session.BootReady)throw new TimeoutException("Normal boot");world=session.World.GeographicWorld;camera=session.View.GetComponent<Camera>();report.bootReady=true;report.regionId=world.Region.id;report.regionName=world.Region.name;report.geoMatrix=Enumerable.Range(0,16).Select(i=>world.transform.localToWorldMatrix[i]).ToArray();
            if(world.transform.localToWorldMatrix!=Matrix4x4.identity)throw new InvalidOperationException("Normal identity GeoWorld frame required for stock source route driving");
            route=Resolve(world.Region,out bridge,out bridgeStart,out bridgeEnd);startD=Mathf.Max(3,bridgeStart-30);endD=Mathf.Min(route.Length-3,bridgeEnd+44);report.bridgeStart=bridgeStart;report.bridgeEnd=bridgeEnd;report.startProgress=startD;report.finishProgress=endD;
            // One initial placement on real ground, before traversal. No later body position writes.
            session.Car.Recover(route.At(startD,route.LaneOffset(startD)),route.Rotation(startD));report.initialPlacements=1;
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            session.Input.enabled=false;session.Car.NetworkDriven=true;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;
            session.ResumeDrive();deadline=Time.realtimeSinceStartupAsDouble+120;while(session.PreparingStart&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(session.PreparingStart||session.Menu||!session.Started||!world.DriveFrontierReady)throw new InvalidOperationException("Normal ResumeDrive not ready");report.driveReady=true;
            Screen.SetResolution(1440,900,FullScreenMode.Windowed);session.View.Mode=0;int previousRenders=renders;for(int i=0;i<12;i++)yield return new WaitForEndOfFrame();report.freshDriveRender=renders>previousRenders;if(!report.freshDriveRender)throw new InvalidOperationException("No actual drive camera render");
            yield return Capture("ground-approach-chase");yield return InspectJoins();
            driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=route;driver.Index=0;driver.Difficulty=0;driver.RoadLane=true;driver.Lane=route.LaneOffset(startD);driver.Fleet=new List<VehicleController>{session.Car};driver.Recovered+=Recovered;
            driveBegan=Time.realtimeSinceStartupAsDouble;priorPosition=session.Car.Body.position;observing=true;deadline=driveBegan+120;bool deckShot=false,farShot=false;
            while(Time.realtimeSinceStartupAsDouble<deadline&&recoveries==0&&!report.poseDiscontinuity)
            {
                yield return new WaitForEndOfFrame();if(report.deckPassed&&!deckShot){deckShot=true;yield return Capture("deck-chase");}if(report.farRoadPassed){farShot=true;yield return Capture("far-road-chase");break;}
            }
            observing=false;driver.enabled=false;session.Car.NetworkThrottle=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;report.elapsed=(float)(Time.realtimeSinceStartupAsDouble-driveBegan);report.recoveries=recoveries;
            if(!farShot)yield return Capture("crossing-stopped-chase");
            bool pass=report.approachPassed&&report.deckPassed&&report.farRoadPassed&&recoveries==0&&!report.poseDiscontinuity&&report.maxDamage<.01f&&report.runtimeErrors==0&&report.freshDriveRender;
            Finish(pass?"CROSSING_OBSERVED":"CROSSING_NOT_COMPLETED",pass?null:"Ordered actual ground/deck/far-road support, no recovery/damage and fresh rendering required; inspect ticks/captures.",pass?0:1);
        }
        void Recovered(float progress){recoveries++;if(driver)driver.enabled=false;}
        void FixedUpdate()
        {
            if(!observing||finished)return;var car=session.Car;float progress=route.Closest(car.Body.position,out float lateral,out float separation);var row=new Tick{time=(float)(Time.realtimeSinceStartupAsDouble-driveBegan),progress=progress,lateral=lateral,separation=separation,speed=car.AbsoluteSpeed,damage=car.Damage,position=car.Body.position,wheels=new Wheel[4]};
            float stepDistance=Vector3.Distance(priorPosition,car.Body.position);if(stepDistance>Mathf.Max(3,car.AbsoluteSpeed*Time.fixedDeltaTime*3+1)){report.poseDiscontinuity=true;if(driver)driver.enabled=false;}report.travelMetres+=stepDistance;priorPosition=car.Body.position;report.maxDamage=Mathf.Max(report.maxDamage,car.Damage);report.fixedSamples++;
            if(session.ActualVehicles!=null&&session.ActualVehicles.TryCapture(out var snapshot,out _)){var player=snapshot.Vehicles.FirstOrDefault(v=>v.Role=="Player");if(player!=null){row.poseRevision=player.PoseDiscontinuityRevision;if(initialPoseRevision<0)initialPoseRevision=row.poseRevision;else if(row.poseRevision!=initialPoseRevision){report.poseDiscontinuity=true;if(driver)driver.enabled=false;}}else row.registryUnavailable="Player absent";}else row.registryUnavailable="Fresh actual population snapshot unavailable";
            for(int i=0;i<4;i++)
            {
                var wheel=new Wheel{grounded=car.WheelGrounded[i],actualContact=car.ContactPoints[i]};var origin=car.transform.TransformPoint(VehicleGeometry.Anchor(car.Metrics.Model,i));
                if(Physics.Raycast(origin,-car.transform.up,out var hit,car.Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore))
                {wheel.rayHit=true;wheel.collider=hit.collider.name;wheel.entity=hit.collider.GetEntityId().ToString();wheel.triangle=hit.triangleIndex;wheel.rayContact=hit.point;wheel.normal=hit.normal;wheel.underGeoWorld=hit.collider.transform.IsChildOf(world.transform);wheel.routeProgress=route.Closest(hit.point,out _,out float contactSeparation);wheel.routeSeparation=contactSeparation;wheel.expectedSurfaceDelta=world.transform.InverseTransformPoint(hit.point).y-route.At(wheel.routeProgress).y;
                    if(wheel.grounded&&wheel.underGeoWorld&&wheel.routeSeparation<route.Width(wheel.routeProgress)*.5f&&Mathf.Abs(wheel.expectedSurfaceDelta)<1&&Vector3.Distance(wheel.actualContact,hit.point)<.5f){if(wheel.collider=="Bridge deck"&&wheel.routeProgress>bridgeStart+2&&wheel.routeProgress<bridgeEnd-2&&Mathf.Abs(world.transform.InverseTransformPoint(hit.point).y-route.At(wheel.routeProgress).y)<1)row.deckContacts++;if((wheel.collider=="Mapped road"||wheel.collider=="Junction apron")&&(wheel.routeProgress<bridgeStart-3||wheel.routeProgress>bridgeEnd+3))row.groundRoadContacts++;}}
                row.wheels[i]=wheel;
            }
            if(row.wheels.All(w=>!w.grounded))report.airborneSamples++;
            // Ordered geometry-backed observations. Proximity/chunk readiness alone cannot qualify passage.
            if(separation<route.Width(progress)*.5f&&row.groundRoadContacts>=2&&progress<bridgeStart-3&&nextPhase==0){report.approachPassed=true;nextPhase=1;}
            if(nextPhase==1&&separation<route.Width(progress)*.5f&&row.deckContacts>=2&&progress>bridgeStart+2&&progress<bridgeEnd-2){report.deckPassed=true;nextPhase=2;}
            if(nextPhase==2&&separation<route.Width(progress)*.5f&&row.groundRoadContacts==4&&progress>=endD&&row.wheels.All(w=>w.grounded&&w.rayHit&&w.routeProgress>=bridgeEnd+40)){report.farRoadPassed=true;nextPhase=3;}
            ticks.Add(row);
        }
        IEnumerator InspectJoins()
        {
            bool forward=spec.steps.First(s=>s.road==spec.bridge).forward;Vector3 a=bridge.points[forward?0:bridge.points.Length-1],b=bridge.points[forward?bridge.points.Length-1:0];var views=new Views{bridge=spec.bridge,inputSHA256=inputHash,poses=new[]{MakePose("near-join-overview",a,bridgeStart),MakePose("far-join-overview",b,bridgeEnd)}};
            if(baseline!=null){var old=JsonUtility.FromJson<Views>(File.ReadAllText(baseline));if(old?.bridge!=spec.bridge||old.inputSHA256!=inputHash||old.poses==null||old.poses.Length!=2||old.poses.Where(p=>p!=null).Select(p=>p.name).Distinct().Count()!=2||old.poses.Any(p=>p==null||!Finite(p.eye.x)||!Finite(p.eye.y)||!Finite(p.eye.z)||!Finite(p.target.x)||!Finite(p.target.y)||!Finite(p.target.z)||(p.eye-p.target).sqrMagnitude<1||p.fieldOfView<30||p.fieldOfView>100||!Finite(p.fieldOfView)||p.name!="near-join-overview"&&p.name!="far-join-overview"))throw new InvalidOperationException("Invalid baseline join views");views=old;report.baselineViewsUsed=true;}
            File.WriteAllText(Path.Combine(output,"views.json"),JsonUtility.ToJson(views,true));bool ve=session.View.enabled;var ui=session.GetComponent<DrivingUI>();bool ue=ui&&ui.enabled;float scale=Time.timeScale,fov=camera.fieldOfView;var pos=camera.transform.position;var rot=camera.transform.rotation;
            try{Time.timeScale=0;session.View.enabled=false;if(ui)ui.enabled=false;foreach(var pose in views.poses){camera.transform.position=world.transform.TransformPoint(pose.eye);camera.transform.rotation=Quaternion.LookRotation(world.transform.TransformPoint(pose.target)-camera.transform.position,world.transform.up);camera.fieldOfView=pose.fieldOfView;for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();yield return Capture(pose.name);}yield return InspectRailLevelCrossings();report.endpointSections=EndpointSections(a,b);CompactQueriedMeshes(report.endpointSections);report.actualMeshes=meshRecords.ToArray();report.joins=new[]{Survey("near",a,spec.steps.First(s=>s.road==spec.bridge).forward?bridge.start:bridge.end),Survey("far",b,spec.steps.First(s=>s.road==spec.bridge).forward?bridge.end:bridge.start)};}
            finally{camera.transform.SetPositionAndRotation(pos,rot);camera.fieldOfView=fov;session.View.enabled=ve;if(ui)ui.enabled=ue;Time.timeScale=scale;}
        }
        const int RailViewLayerMask = ~(1 << 8); // Actual GeoWorld only; actor layer excluded.
        const int MaximumRailViews = 2, MaximumRailHits = 16;
        const float RailEyeAboveHeadMetres = 1.5f, RailUpRangeMetres = 100;
        [Serializable] public sealed class RailHit
        {
            public string name, colliderEntity, meshEntity; public int layer, triangle;
            public Vector3 localPoint, worldPoint, normal; public float distance;
        }
        [Serializable] public sealed class FormationObservation
        {
            public float acrossMetres, effectiveRailhead, rawDem, derivedTerrainField, terrainMeshEvaluator;
            public Vector3 localQuery; public int actualDownHitCount, omittedDownHits;
            public RailHit[] actualDownHits, actualTerrainHits;
        }
        [Serializable] public sealed class RailInspection
        {
            public string railwayId, railwayName, roadId, imageName, error;
            public int railLayer, roadSegment, railSegment, actualBridgeUpHitCount, omittedBridgeUpHits;
            public bool estimatedProfile, nativeTerrainMissing, selectedForView, effectiveProfileObserved, surfacesObserved, viewCaptured;
            public int collisionLayerMask = RailViewLayerMask;
            public float upRayOriginAboveRailheadMetres = .05f, upRayRangeMetres = RailUpRangeMetres;
            public double roadT, railT;
            public Vector3 sourceCrossing, originalRailA, originalRailB, effectiveRailA, effectiveRailB;
            public float originalRailhead, effectiveRailhead;
            public Pose railLevelView;
            public RailHit[] actualBridgeUpHits;
            public FormationObservation[] formation;
            public string qualification = "Genuine source-XZ intersection; estimated effective rail profile. Finite actual collider observations/camera only, not train physics or legal clearance certificate.";
            [NonSerialized] public GeoRailway Railway;
        }

        List<RailInspection> SourceRailCrossings()
        {
            var rows = new List<RailInspection>();
            foreach (var railway in (world.Region.railways ?? Array.Empty<GeoRailway>())
                .Where(rail => rail.estimatedProfile && rail.layer < 0 && !rail.tunnel && rail.points != null)
                .OrderBy(rail => rail.id, StringComparer.Ordinal))
            for (int roadSegment = 1; roadSegment < bridge.points.Length; roadSegment++)
            for (int railSegment = 1; railSegment < railway.points.Length; railSegment++)
            {
                var a = bridge.points[roadSegment - 1]; var b = bridge.points[roadSegment];
                var c = railway.points[railSegment - 1]; var d = railway.points[railSegment];
                double ux = (double)b.x - a.x, uz = (double)b.z - a.z;
                double vx = (double)d.x - c.x, vz = (double)d.z - c.z;
                double denominator = ux * vz - uz * vx;
                if (denominator == 0) continue;
                double dx = (double)c.x - a.x, dz = (double)c.z - a.z;
                double roadT = (dx * vz - dz * vx) / denominator;
                double railT = (dx * uz - dz * ux) / denominator;
                if (roadT < 0 || roadT > 1 || railT < 0 || railT > 1) continue;
                var crossing = new Vector3((float)(a.x + roadT * ux),
                    (float)(c.y + railT * ((double)d.y - c.y)), (float)(a.z + roadT * uz));
                rows.Add(new RailInspection { Railway = railway, railwayId = railway.id, railwayName = railway.name,
                    roadId = bridge.id, railLayer = railway.layer, estimatedProfile = railway.estimatedProfile,
                    roadSegment = roadSegment - 1, railSegment = railSegment - 1, roadT = roadT, railT = railT,
                    sourceCrossing = crossing, originalRailA = c, originalRailB = d, originalRailhead = crossing.y });
            }
            return rows;
        }

        IEnumerator InspectRailLevelCrossings()
        {
            var rows = SourceRailCrossings(); report.railCrossingCandidates = rows.Count;
            report.railCrossingRailCount = rows.Select(row => row.Railway).Distinct().Count();
            // Retain every source intersection row. Capture at most one per actual
            // railway, in stable source order; omitted/extra intersections explicit.
            var selected = rows.GroupBy(row => row.Railway).Select(group => group.First()).Take(MaximumRailViews).ToArray();
            report.railViewsOmitted = rows.Count - selected.Length;
            report.railInspections = rows.ToArray();
            if (selected.Length < MaximumRailViews) notes.Add("Rail-level observer found " + selected.Length + " eligible actual negative-layer estimated railways; missing views not substituted.");
            if (report.railViewsOmitted > 0) notes.Add("Rail-level source intersections omitted from the two-view budget: " + report.railViewsOmitted + "; all source rows retained.");
            int image = 0;
            foreach (var row in selected)
            {
                row.selectedForView = true;
                try
                {
                    var railway = row.Railway;
                    var effective = world.Region.CopyEffectiveRailwayPoints(railway);
                    row.effectiveRailhead = world.Region.EffectiveRailwayHeight(railway, row.sourceCrossing.x, row.sourceCrossing.z);
                    row.effectiveProfileObserved = true;
                    if (effective.Length != railway.points.Length) throw new InvalidOperationException("Effective railway/source segment mapping differs; no guessed camera path");
                    row.effectiveRailA = effective[row.railSegment]; row.effectiveRailB = effective[row.railSegment + 1];
                    var direction = row.originalRailB - row.originalRailA; direction.y = 0; direction.Normalize();
                    if (direction.sqrMagnitude < .5f) throw new InvalidOperationException("Rail source tangent unavailable");
                    float length = Vector2.Distance(new Vector2(row.originalRailA.x, row.originalRailA.z), new Vector2(row.originalRailB.x, row.originalRailB.z));
                    bool fromStart = row.railT >= .5;
                    float available = (float)(fromStart ? row.railT : 1 - row.railT) * length;
                    float setback = Mathf.Min(6, available * .8f);
                    var eye = row.sourceCrossing + direction * (fromStart ? -setback : setback);
                    eye.y = world.Region.EffectiveRailwayHeight(railway, eye.x, eye.z) + RailEyeAboveHeadMetres;
                    var target = row.sourceCrossing; target.y = row.effectiveRailhead + RailEyeAboveHeadMetres;
                    if ((eye - target).sqrMagnitude < .01f) throw new InvalidOperationException("Source rail segment too short for distinct view; no substitute pose");
                    row.imageName = "rail-level-" + (++image);
                    row.railLevelView = new Pose { name = row.imageName, eye = eye, target = target, fieldOfView = 65 };
                    Physics.SyncTransforms(); ObserveRailSurfaces(row, direction); row.surfacesObserved = true;
                }
                catch (Exception error) { row.error = error.ToString(); notes.Add("Rail-level observer diagnostic unavailable for " + row.railwayId); }
                if (row.error != null) continue;
                camera.transform.position = world.transform.TransformPoint(row.railLevelView.eye);
                camera.transform.rotation = Quaternion.LookRotation(world.transform.TransformPoint(row.railLevelView.target) - camera.transform.position, world.transform.up);
                camera.fieldOfView = row.railLevelView.fieldOfView;
                for (int frame = 0; frame < 5; frame++) yield return new WaitForEndOfFrame();
                yield return Capture(row.imageName); row.viewCaptured = true;
            }
            report.railInspectionStatus = "FINITE_SOURCE_BOUND_RAIL_OBSERVATIONS_NO_CLEARANCE_ACCEPTANCE";
        }

        void ObserveRailSurfaces(RailInspection row, Vector3 direction)
        {
            var position = row.sourceCrossing; position.y = row.effectiveRailhead;
            var up = world.transform.up;
            // UP measures actual underside MeshColliders; no analytic deck-height
            // subtraction, no top-sheet substitute and no train-envelope verdict.
            var overhead = Physics.RaycastAll(world.transform.TransformPoint(position + Vector3.up * .05f), up,
                RailUpRangeMetres, RailViewLayerMask, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.collider is MeshCollider && hit.collider.transform.IsChildOf(world.transform) &&
                    (hit.collider.name == "Bridge deck" || hit.collider.name == "Bridge concrete structure"))
                .OrderBy(hit => hit.distance).ToArray();
            row.actualBridgeUpHitCount = overhead.Length; row.omittedBridgeUpHits = Math.Max(0, overhead.Length - MaximumRailHits);
            row.actualBridgeUpHits = overhead.Take(MaximumRailHits).Select(RailHitRow).ToArray();
            var right = Vector3.Cross(Vector3.up, direction);
            float shoulder = row.Railway.width * .5f + 2;
            var formation = new List<FormationObservation>();
            foreach (float across in new[] { -shoulder, 0, shoulder })
            {
                var query = position + right * across;
                var sample = new FormationObservation { acrossMetres = across, localQuery = query,
                    effectiveRailhead = world.Region.EffectiveRailwayHeight(row.Railway, query.x, query.z),
                    rawDem = world.Region.RawHeight(query.x, query.z),
                    derivedTerrainField = world.Region.TerrainHeight(query.x, query.z),
                    terrainMeshEvaluator = world.MeshSurfaceHeight(query.x, query.z) };
                var hits = Physics.RaycastAll(world.transform.TransformPoint(query + Vector3.up * 8), -up, 80,
                    RailViewLayerMask, QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.collider is MeshCollider && hit.collider.transform.IsChildOf(world.transform))
                    .OrderBy(hit => hit.distance).ToArray();
                sample.actualDownHitCount = hits.Length; sample.omittedDownHits = Math.Max(0, hits.Length - MaximumRailHits);
                sample.actualDownHits = hits.Take(MaximumRailHits).Select(RailHitRow).ToArray();
                sample.actualTerrainHits = hits.Where(hit => hit.collider.name == "Terrain").Take(MaximumRailHits).Select(RailHitRow).ToArray();
                if (sample.actualTerrainHits.Length == 0) row.nativeTerrainMissing = true;
                formation.Add(sample);
            }
            row.formation = formation.ToArray();
        }

        RailHit RailHitRow(RaycastHit hit)
        {
            var collider = (MeshCollider)hit.collider;
            return new RailHit { name = collider.name, colliderEntity = collider.GetEntityId().ToString(),
                meshEntity = collider.sharedMesh ? collider.sharedMesh.GetEntityId().ToString() : null,
                layer = collider.gameObject.layer, triangle = hit.triangleIndex,
                localPoint = world.transform.InverseTransformPoint(hit.point), worldPoint = hit.point,
                normal = hit.normal, distance = hit.distance };
        }

        Pose MakePose(string name,Vector3 p,float d)=>new Pose{name=name,eye=p-route.PlanarForward(d)*14+Vector3.up*12,target=p+Vector3.up*.3f};
        Join Survey(string name,Vector3 p,string node)
        {var hits=new List<Surface>();var ray=new Ray(world.transform.TransformPoint(p+Vector3.up*20),-world.transform.up);foreach(var c in world.GetComponentsInChildren<MeshCollider>(false))if(c.enabled&&!c.isTrigger&&c.sharedMesh&&c.bounds.IntersectRay(ray)&&c.Raycast(ray,out var h,80))hits.Add(new Surface{collider=c.name,entity=c.GetEntityId().ToString(),triangle=h.triangleIndex,point=world.transform.InverseTransformPoint(h.point),normal=h.normal});return new Join{name=name,node=node,source=p,groundEvaluator=world.Region.RoadSurfaceHeight(p.x,p.z),hits=hits.OrderByDescending(h=>h.point.y).ToArray()};}
        Geometry ActualGeometry(Mesh mesh)
        {
            if(geometries.TryGetValue(mesh,out var g))return g;
            if(!mesh.isReadable)throw new InvalidOperationException("Actual sharedMesh not CPU readable");
            g=new Geometry{vertices=mesh.vertices,triangles=mesh.triangles};
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){foreach(var v in g.vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}foreach(int t in g.triangles)writer.Write(t);}using(var h=SHA256.Create())g.hash=BitConverter.ToString(h.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();}
            geometries.Add(mesh,g);return g;
        }
        List<Candidate> ActualCandidates()
        {
            var result=new List<Candidate>();
            void Add(string kind,Component component,Mesh mesh,bool enabled,bool trigger,Bounds bounds)
            {
                if(!mesh)return;var record=new MeshRecord{kind=kind,objectName=component.name,objectEntity=component.GetEntityId().ToString(),gameObjectEntity=component.gameObject.GetEntityId().ToString(),meshEntity=mesh.GetEntityId().ToString(),active=component.gameObject.activeInHierarchy,enabled=enabled,trigger=trigger,layer=component.gameObject.layer,localToWorld=Enumerable.Range(0,16).Select(i=>component.transform.localToWorldMatrix[i]).ToArray()};
                // Metadata is retained independently for visual and collider owners.
                int index=meshRecords.Count;meshRecords.Add(record);result.Add(new Candidate{mesh=mesh,transform=component.transform,bounds=bounds,record=index});
            }
            foreach(var f in world.GetComponentsInChildren<MeshFilter>(false)){var renderer=f.GetComponent<Renderer>();if(renderer)Add("MeshFilter",f,f.sharedMesh,renderer.enabled,false,renderer.bounds);}
            foreach(var c in world.GetComponentsInChildren<MeshCollider>(false))Add("MeshCollider",c,c.sharedMesh,c.enabled,c.isTrigger,c.bounds);
            return result;
        }
        SectionPoint[] EndpointSections(Vector3 near,Vector3 far)
        {
            var candidates=ActualCandidates();var points=new List<SectionPoint>();Vector3 direction=far-near;direction.y=0;direction.Normalize();Vector3 right=Vector3.Cross(Vector3.up,direction).normalized;
            float half=bridge.width*.5f,lane=Mathf.Min(2.7f,bridge.width*.24f);float[] lateral={-half,-half+.01f,-lane,0,lane,half-.01f,half};string[] stations={"left-edge","left-edge-inset","left-lane","centre","right-lane","right-edge-inset","right-edge"};
            foreach(var join in new[]{Tuple.Create("near",near),Tuple.Create("far",far)})foreach(float along in new[]{-.05f,0f,.05f})for(int j=0;j<lateral.Length;j++)
            {
                Vector3 coordinate=join.Item2+direction*along+right*lateral[j];var sample=new SectionPoint{join=join.Item1,station=stations[j],along=along,lateral=lateral[j],coordinate=coordinate};var hits=new List<TriangleHit>();var edges=new List<EdgeWitness>();
                var ray=new Ray(world.transform.TransformPoint(coordinate+Vector3.up*20),-world.transform.up);
                foreach(var candidate in candidates)
                {
                    var record=meshRecords[candidate.record];if(!record.active||!record.enabled||record.trigger||!Expanded(candidate.bounds,.012f).IntersectRay(ray))continue;
                    try
                    {
                        var geometry=ActualGeometry(candidate.mesh);record.geometrySHA256=geometry.hash;record.vertices=geometry.vertices.Length;record.triangles=geometry.triangles.Length/3;int degenerate=0,hitsBefore=hits.Count,edgesBefore=edges.Count;EdgeWitness closestEdge=null;
                        for(int i=0;i<geometry.triangles.Length;i+=3)
                        {
                            Vector3 a=world.transform.InverseTransformPoint(candidate.transform.TransformPoint(geometry.vertices[geometry.triangles[i]])),b=world.transform.InverseTransformPoint(candidate.transform.TransformPoint(geometry.vertices[geometry.triangles[i+1]])),c=world.transform.InverseTransformPoint(candidate.transform.TransformPoint(geometry.vertices[geometry.triangles[i+2]]));
                            double denominator=(double)(b.z-c.z)*(a.x-c.x)+(double)(c.x-b.x)*(a.z-c.z);if(Math.Abs(denominator)<1e-10){degenerate++;continue;}
                            double u=((double)(b.z-c.z)*(coordinate.x-c.x)+(double)(c.x-b.x)*(coordinate.z-c.z))/denominator,v=((double)(c.z-a.z)*(coordinate.x-c.x)+(double)(a.x-c.x)*(coordinate.z-c.z))/denominator,w=1-u-v;
                            // No closest-point/clamping fallback: misses remain misses. Tiny barycentric roundoff only.
                            Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                            float y=(float)(u*a.y+v*b.y+w*c.y);if(y>coordinate.y+20||y<coordinate.y-60)continue;
                            if(u< -1e-6||v< -1e-6||w< -1e-6||u>1.000001||v>1.000001||w>1.000001)
                            {
                                if(normal.y>.5f)
                                {
                                    Vector3 nearest=ClosestEdgeXZ(coordinate,a,b,c);float dx=nearest.x-coordinate.x,dz=nearest.z-coordinate.z;float xz=Mathf.Sqrt(dx*dx+dz*dz);float distance=Vector3.Distance(nearest,new Vector3(coordinate.x,y,coordinate.z));
                                    var witness=new EdgeWitness{meshRecord=candidate.record,triangle=i/3,a=a,b=b,c=c,actualEdgePoint=nearest,planeAtQuery=new Vector3(coordinate.x,y,coordinate.z),normal=normal,xzDistance=xz,planeDistance3D=distance,withinFiveMillimetres=xz<=.005f&&distance<=.005f};if(closestEdge==null||xz<closestEdge.xzDistance)closestEdge=witness;if(xz<=.005f)edges.Add(witness);
                                }
                                continue;
                            }
                            hits.Add(new TriangleHit{meshRecord=candidate.record,triangle=i/3,point=new Vector3(coordinate.x,y,coordinate.z),normal=normal,a=a,b=b,c=c});
                        }
                        if(hits.Count==hitsBefore&&edges.Count==edgesBefore&&closestEdge!=null)edges.Add(closestEdge);record.verticalOrDegenerateTriangles=degenerate;
                    }
                    catch(Exception e){record.error=e.Message;}
                }
                sample.hits=hits.OrderByDescending(h=>h.point.y).ToArray();sample.edgeWitnesses=edges.OrderBy(e=>e.xzDistance).ToArray();var pairs=new List<SeamPair>();var apronPairs=new List<ApronPair>();var apronOffsets=new List<float>();
                foreach(string kind in new[]{"MeshFilter","MeshCollider"})
                {
                    var deck=hits.Where(h=>meshRecords[h.meshRecord].kind==kind&&meshRecords[h.meshRecord].objectName=="Bridge deck"&&h.normal.y>.5f).ToArray();var road=hits.Where(h=>meshRecords[h.meshRecord].kind==kind&&meshRecords[h.meshRecord].objectName=="Mapped road"&&h.normal.y>.5f).ToArray();
                    if(kind=="MeshFilter"){sample.renderDeckHits=deck.Length;sample.renderRoadHits=road.Length;}else{sample.colliderDeckHits=deck.Length;sample.colliderRoadHits=road.Length;}
                    if(along==0)foreach(var apron in hits.Where(h=>meshRecords[h.meshRecord].kind==kind&&meshRecords[h.meshRecord].objectName=="Junction apron"&&h.normal.y>.5f))foreach(var g in road)apronPairs.Add(new ApronPair{kind=kind,apronMesh=apron.meshRecord,apronTriangle=apron.triangle,roadMesh=g.meshRecord,roadTriangle=g.triangle,apronMinusRoad=apron.point.y-g.point.y});
                    if(along==0)foreach(var d in deck)
                    {
                        foreach(var g in road){float delta=g.point.y-d.point.y;pairs.Add(new SeamPair{kind=kind,asphaltCategory="Mapped road",deckMesh=d.meshRecord,deckTriangle=d.triangle,asphaltMesh=g.meshRecord,asphaltTriangle=g.triangle,signedHeightDelta=delta,withinFiveMillimetres=Mathf.Abs(delta)<=.005f});}
                        foreach(var apron in hits.Where(h=>meshRecords[h.meshRecord].kind==kind&&meshRecords[h.meshRecord].objectName=="Junction apron"&&h.normal.y>.5f))apronOffsets.Add(apron.point.y-d.point.y);
                    }
                }
                var consistency=new List<RenderColliderPair>();foreach(var visual in hits.Where(h=>meshRecords[h.meshRecord].kind=="MeshFilter"))foreach(var physical in hits.Where(h=>meshRecords[h.meshRecord].kind=="MeshCollider"&&meshRecords[h.meshRecord].gameObjectEntity==meshRecords[visual.meshRecord].gameObjectEntity))consistency.Add(new RenderColliderPair{owner=meshRecords[visual.meshRecord].gameObjectEntity,visualMesh=visual.meshRecord,visualTriangle=visual.triangle,colliderMesh=physical.meshRecord,colliderTriangle=physical.triangle,signedHeightDelta=physical.point.y-visual.point.y});sample.visualColliderPairs=consistency.ToArray();sample.apronRoadPairs=apronPairs.ToArray();sample.mappedRoadPairs=pairs.ToArray();sample.apronMinusDeckOffsets=apronOffsets.ToArray();sample.drivablePortalPairs=along==0?PortalPairs(sample):Array.Empty<PortalPair>();points.Add(sample);
            }
            var endpoint=points.Where(p=>p.along==0).ToArray();report.drivablePortalCoverageComplete=endpoint.All(p=>new[]{"MeshFilter","MeshCollider"}.All(k=>p.drivablePortalPairs.Any(pair=>pair.kind==k)))&&meshRecords.All(m=>string.IsNullOrEmpty(m.error));bool portalMismatch=endpoint.SelectMany(p=>p.drivablePortalPairs).Any(pair=>!pair.withinFiveMillimetres);report.drivablePortalStatus=portalMismatch?"OBSERVED_DRIVABLE_JOIN_MISMATCH":report.drivablePortalCoverageComplete?"OBSERVED_DRIVABLE_JOIN_WITHIN_5MM":"INCOMPLETE_MEASURED_DRIVABLE_JOIN_SUPPORT";bool complete=endpoint.All(p=>p.renderDeckHits>0&&p.colliderDeckHits>0&&p.renderRoadHits>0&&p.colliderRoadHits>0)&&meshRecords.All(m=>string.IsNullOrEmpty(m.error));
            report.seamCoverageComplete=complete;bool mismatch=endpoint.SelectMany(p=>p.mappedRoadPairs).Any(p=>!p.withinFiveMillimetres);report.seamStatus=mismatch?"OBSERVED_MAPPED_ROAD_SEAM_MISMATCH":!complete?"INCOMPLETE_ACTUAL_SECTION_SUPPORT":"OBSERVED_MAPPED_ROAD_SEAMS_WITHIN_5MM";
            notes.Add("Post-render direct sharedMesh vertex/triangle observations are independent for MeshFilter and MeshCollider; all intersected strata retained, no highest-hit selection. Target5mm applies deck versus mapped asphalt at SAME endpoint XZ only; ±.05m raw differences are slope samples, not seam failures. Apron minus DECK offsets remain descriptive; intentional3mm refers to apron minus MAPPED ROAD, separately recorded with both support IDs and never subtracted from the5mm deck-road budget. Measured mismatch takes precedence in status even when coverage incomplete. Exact-edge misses stay incomplete. Batch categories do not independently prove source-face ownership.");
            notes.Add("V3 preserves exact hits/MISS counts. Edge witnesses retain actual triangle vertices and closest edge point, XZ distance and3D distance to same plane at query; only measured distance<=5mm can bind bounded edge support. Apron is accepted as actual drivable join with explicit+3mm offset, not mandatory direct mapped-road coverage. Each portal pair also records actual support-point XZ/3D separation and requires actual paired3D gap<=5mm, preventing two opposite5mm edge allowances hiding a10mm gap. Queried read errors qualify completeness. Nearest edge beyond5mm may be retained descriptively but never counts as support. drivablePortalStatus is separate from original directmapped seamStatus and crossing outcome. Exposed lower railway geometry is a concern; no sole stall-cause inference.");return points.ToArray();
        }
        static Bounds Expanded(Bounds bounds,float amount){bounds.Expand(amount);return bounds;}
        static Vector3 ClosestEdgeXZ(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 Segment(Vector3 u,Vector3 v){double dx=v.x-u.x,dz=v.z-u.z;double length=dx*dx+dz*dz;double t=length>1e-20?Math.Max(0,Math.Min(1,((p.x-u.x)*dx+(p.z-u.z)*dz)/length)):0;return u+(v-u)*(float)t;}
            var candidates=new[]{Segment(a,b),Segment(b,c),Segment(c,a)};return candidates.OrderBy(v=>(double)(v.x-p.x)*(v.x-p.x)+(double)(v.z-p.z)*(v.z-p.z)).First();
        }
        PortalPair[] PortalPairs(SectionPoint sample)
        {
            var result=new List<PortalPair>();
            foreach(string kind in new[]{"MeshFilter","MeshCollider"})
            {
                var support=new List<Tuple<int,int,Vector3,float,float,bool>>();
                foreach(var h in sample.hits.Where(h=>meshRecords[h.meshRecord].kind==kind&&h.normal.y>.5f))support.Add(Tuple.Create(h.meshRecord,h.triangle,h.point,h.point.y,0f,false));
                foreach(var e in sample.edgeWitnesses.Where(e=>meshRecords[e.meshRecord].kind==kind&&e.withinFiveMillimetres))support.Add(Tuple.Create(e.meshRecord,e.triangle,e.actualEdgePoint,e.planeAtQuery.y,e.planeDistance3D,true));
                foreach(var deck in support.Where(h=>meshRecords[h.Item1].objectName=="Bridge deck"))foreach(var asphalt in support.Where(h=>meshRecords[h.Item1].objectName=="Mapped road"||meshRecords[h.Item1].objectName=="Junction apron"))
                {
                    float pairDistance=Vector3.Distance(deck.Item3,asphalt.Item3);float pairXZ=Vector2.Distance(new Vector2(deck.Item3.x,deck.Item3.z),new Vector2(asphalt.Item3.x,asphalt.Item3.z));string category=meshRecords[asphalt.Item1].objectName;float offset=category=="Junction apron"?.003f:0,delta=asphalt.Item4-deck.Item4-offset;
                    result.Add(new PortalPair{kind=kind,asphaltCategory=category,deckMesh=deck.Item1,deckTriangle=deck.Item2,asphaltMesh=asphalt.Item1,asphaltTriangle=asphalt.Item2,deckUsesEdgeWitness=deck.Item6,asphaltUsesEdgeWitness=asphalt.Item6,deckActualPoint=deck.Item3,asphaltActualPoint=asphalt.Item3,deckEdgeDistance=deck.Item5,asphaltEdgeDistance=asphalt.Item5,deckPlaneY=deck.Item4,asphaltPlaneY=asphalt.Item4,intentionalAsphaltOffset=offset,adjustedDelta=delta,actualPairXZDistance=pairXZ,actualPairDistance3D=pairDistance,withinFiveMillimetres=Mathf.Abs(delta)<=.005f&&pairDistance<=.005f});
                }
            }
            return result.ToArray();
        }
        void CompactQueriedMeshes(SectionPoint[] points)
        {
            // Keep actual queried meshes/errors, not16k unrelated world metadata rows.
            var used=new SortedSet<int>(points.SelectMany(p=>p.hits.Select(h=>h.meshRecord).Concat(p.edgeWitnesses.Select(h=>h.meshRecord))));
            for(int i=0;i<meshRecords.Count;i++)if(meshRecords[i].geometrySHA256!=null||meshRecords[i].error!=null)used.Add(i);
            var remap=used.Select((old,index)=>new{old,index}).ToDictionary(x=>x.old,x=>x.index);var compact=used.Select(i=>meshRecords[i]).ToArray();
            foreach(var p in points)
            {
                foreach(var h in p.hits)h.meshRecord=remap[h.meshRecord];foreach(var e in p.edgeWitnesses)e.meshRecord=remap[e.meshRecord];
                foreach(var pair in p.mappedRoadPairs){pair.deckMesh=remap[pair.deckMesh];pair.asphaltMesh=remap[pair.asphaltMesh];}
                foreach(var pair in p.visualColliderPairs){pair.visualMesh=remap[pair.visualMesh];pair.colliderMesh=remap[pair.colliderMesh];}
                foreach(var pair in p.apronRoadPairs){pair.apronMesh=remap[pair.apronMesh];pair.roadMesh=remap[pair.roadMesh];}
                foreach(var pair in p.drivablePortalPairs){pair.deckMesh=remap[pair.deckMesh];pair.asphaltMesh=remap[pair.asphaltMesh];}
            }
            meshRecords.Clear();meshRecords.AddRange(compact);
        }
        IEnumerator Capture(string name)
        {int before=renders;yield return null;yield return new WaitForEndOfFrame();if(renders<=before)throw new InvalidOperationException("Capture lacks fresh actual camera render");var texture=ScreenCapture.CaptureScreenshotAsTexture();try{string p=Path.Combine(output,name+".png");File.WriteAllBytes(p,texture.EncodeToPNG());shots.Add(new Shot{name=name,file=p,sha256=Hash(p),frame=Time.frameCount,width=texture.width,height=texture.height,renders=renders,cameraPosition=camera.transform.position,cameraRotation=camera.transform.rotation,fov=camera.fieldOfView});}finally{Destroy(texture);}}
        void StopDrive(){observing=false;if(driver)driver.enabled=false;if(session&&session.Car){session.Car.NetworkThrottle=0;session.Car.NetworkSteer=0;session.Car.NetworkBrake=1;session.Car.NetworkHandbrake=1;}}
        void Finish(string status,string error,int code)
        {if(finished)return;finished=true;StopDrive();report.status=status;report.error=error;report.ticks=ticks.ToArray();report.shots=shots.ToArray();notes.Add("Discontinuity guard rejects per-fixed-step body jumps exceeding max(3m,3*currentSpeed*fixedDelta+1m); this is a continuity diagnostic, not a terrain seam threshold. All four far-road rays must clear exit by40m. Wheel/body XZ corridor and a loose1m expected-height association reject unrelated road support; these are passage binding diagnostics, not road accuracy tolerances. Fresh registry player pose revision also observed when available; unavailability is explicit per tick and full trajectory retained.");notes.Add("Wheel flags/contact points read during observer FixedUpdate may refer to the preceding physics step; matching ray recorded separately. Batch name is category, not source ownership; ordered route source binding and position bound qualify passage.");notes.Add("Initial approach placement only; any RaceDriver recovery invalidates crossing. No collider/height/physics replacement. Static views explicitly pause/move only camera and restore state.");report.notes=notes.ToArray();try{if(ownsOutput)File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,false));Debug.Log("Legacy bridge QA "+status);}catch(Exception e){code=1;Debug.LogError("Bridge QA report write failed: "+e.Message);}finally{Application.Quit(code);}}
        void OnDestroy(){StopDrive();RenderPipelineManager.endCameraRendering-=Rendered;Application.logMessageReceived-=Log;if(driver){driver.Recovered-=Recovered;driver.enabled=false;}}
    }
}
