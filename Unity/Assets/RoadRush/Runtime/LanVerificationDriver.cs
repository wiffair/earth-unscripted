using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RoadRush
{
    // An explicit, isolated standalone test harness. Normal play never activates it.
    [DefaultExecutionOrder(400)]
    public sealed class LanVerificationDriver : MonoBehaviour
    {
        // QA readiness only, in unscaled seconds after this driver's start.
        // A shared map is transferred before the guest's genuine asynchronous
        // world reload can reconnect. Retain all post-join observation gates;
        // the external process/overall proof limits remain independently bounded.
        const float InitialJoinReadinessSeconds = 180;
        // The async world boot yields before this component is created. An
        // unattended host must be able to reach Ready without window focus.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void PrepareUnattendedProof()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushLanProof");
            if(at>=0&&at+2<args.Length&&(args[at+1]=="host"||args[at+1]=="client"))Application.runInBackground=true;
        }
        public GameSession Session;string role,prefix;float start;Vector3 initial,lastGuest;int impacts,lastRttSample;float maxRtt,otherMass,otherDent,expectedMass;bool began,watchingGuest,epochMismatch,shareProof,everJoined,cachedProof,captured;float joinedAt;int reloads;int environmentSamples,environmentFailures; readonly List<float> rtts=new List<float>();
        LanParkingProofScenario parkingProof;
        int visualEnvironmentSamples,visualEnvironmentFailures;RailwayLanProofEvidence railProof;LanVehicleMetricEvidence vehicleMetricProof;
        int expectedCanopyCount=-1,canopySamples,canopyFailures,canopyNearSamples,peakActiveCanopyTrees,peakCanopyTrunks,peakCanopyDrawSubmissions;string initialCanopyDigest="",lastCanopyDigest="";float nextCanopySample;
        int expectedBuildingCount=-1,expectedImportantCount=-1,buildingSamples,buildingFailures,buildingNearSamples,peakBuildingWalls,peakBuildingWindows,peakBuildingDoors,peakBuildingFlights,peakBuildingDraws;
        float nextBuildingSample;string initialBuildingDigest="",lastBuildingDigest="";
        [Serializable] public sealed class BuildingPartEvidence
        {public string id,sourceId,kind,wallDigest,roofDigest,featureDigest;public float floorY,groundMedianY,boundaryMinY,boundaryMaxY;public bool conservativeFallback,roofBurialRisk,wallRayHit;public int windows,doors,roofMeshes;}
        [Serializable] public sealed class BuildingAccessEvidence
        {public string id,meshDigest;public int flights;public bool sealedMesh,colliderRayHit;}
        [Serializable] public sealed class BuildingEvidence
        {public string canonicalDigest;public int canonicalCount,importantCount;public BuildingPartEvidence[] parts;public BuildingAccessEvidence[] access;}
        readonly Dictionary<string,BuildingPartEvidence> buildingParts=new Dictionary<string,BuildingPartEvidence>(StringComparer.Ordinal);
        readonly Dictionary<string,BuildingAccessEvidence> buildingAccess=new Dictionary<string,BuildingAccessEvidence>(StringComparer.Ordinal);
        string updateRoot,updateCountryHash,updateWorldEpoch;RoadRush.Core.DataFileManifest expectedUpdate,expectedLastUpdate;long expectedUpdateBytes;
        void FixedUpdate(){if(began&&railProof!=null&&Session)railProof.FixedSample(Session);}
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushLanProof");
            if(at<0||at+2>=args.Length){Destroy(this);yield break;}role=args[at+1];prefix=args[at+2];DontDestroyOnLoad(gameObject);shareProof=Array.IndexOf(args,"-roadRushShareProof")>=0;cachedProof=Array.IndexOf(args,"-roadRushCachedShareProof")>=0;
            while(!Session||!Session.BootReady)yield return null;
            int parkingAt=Array.IndexOf(args,"-roadRushParkingProof");
            if(parkingAt>=0)
            {
                if(parkingAt+2>=args.Length||Array.IndexOf(args,"-roadRushExpectedRailwayCount")<0)throw new InvalidOperationException("Parking scenario requires shared prefix, token and existing rail proof");
                parkingProof=new LanParkingProofScenario(prefix,args[parkingAt+1],role,args[parkingAt+2]);
            }
            if(Array.IndexOf(args,"-roadRushVehicleMetricsProof")>=0)vehicleMetricProof=new LanVehicleMetricEvidence();
            int railAt=Array.IndexOf(args,"-roadRushExpectedRailwayCount");
            if(railAt>=0){if(railAt+1>=args.Length||!int.TryParse(args[railAt+1],out int railCount)||railCount<1||railCount>RailwayMetrics.MaximumWays){File.WriteAllText(prefix+"-result.txt","FAIL invalid expected railway count\n");Application.Quit(1);yield break;}railProof=new RailwayLanProofEvidence(railCount);}
            int canopyAt=Array.IndexOf(args,"-roadRushExpectedCanopyCount");
            if(canopyAt>=0&&(canopyAt+1>=args.Length||!int.TryParse(args[canopyAt+1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out expectedCanopyCount)||expectedCanopyCount<1||expectedCanopyCount>16384))
            {File.WriteAllText(prefix+"-result.txt","FAIL invalid expected canopy count\n");Application.Quit(1);yield break;}
            foreach(var option in new[]{"-roadRushExpectedCanonicalBuildingCount","-roadRushExpectedImportantBuildingCount"})
            {int index=Array.IndexOf(args,option);if(index<0)continue;if(index+1>=args.Length||!int.TryParse(args[index+1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int count)||count<0||count>16384){File.WriteAllText(prefix+"-result.txt","FAIL invalid expected building count\n");Application.Quit(1);yield break;}if(option=="-roadRushExpectedCanonicalBuildingCount")expectedBuildingCount=count;else expectedImportantCount=count;}
            if(expectedImportantCount>=0&&expectedBuildingCount<1){File.WriteAllText(prefix+"-result.txt","FAIL important building proof requires canonical count\n");Application.Quit(1);yield break;}
            int expected=Array.IndexOf(args,"-roadRushExpectedPeerMass");if(expected>=0&&expected+1<args.Length)float.TryParse(args[expected+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out expectedMass);
            epochMismatch=Array.IndexOf(args,"-roadRushEpochMismatch")>=0;if(epochMismatch&&role=="client")Session.World.Route.Region.heights[0]+=1;
            int updateArg=Array.IndexOf(args,"-roadRushPeerUpdates");
            if(updateArg>=0&&updateArg+1<args.Length)
            {
                updateRoot=Path.GetFullPath(args[updateArg+1]);expectedUpdate=JsonUtility.FromJson<RoadRush.Core.DataFileManifest>(File.ReadAllText(Path.Combine(updateRoot,"expected.json")));
                string lastExpected=Path.Combine(updateRoot,"expected-last.json");expectedLastUpdate=File.Exists(lastExpected)?JsonUtility.FromJson<RoadRush.Core.DataFileManifest>(File.ReadAllText(lastExpected)):expectedUpdate;
                string expectedBytesPath=Path.Combine(updateRoot,"expected-payload-bytes.txt");expectedUpdateBytes=File.Exists(expectedBytesPath)?long.Parse(File.ReadAllText(expectedBytesPath)):expectedUpdate.bytes;
                CountryPacks.ExtraRoot=Path.Combine(updateRoot,role=="host"?"HostPacks":"GuestPacks");CountryUpdateService.OverrideFolder=Path.Combine(updateRoot,role=="host"?"HostUpdates":"GuestUpdates");
                CountryUpdateService.AllowVerification=true;Session.Updates.Enabled=true;Session.Updates.Automatic=false;
                updateCountryHash=RoadRush.Core.DataPackageRules.HashFile(Path.Combine(updateRoot,"GuestPacks/Country/map.sqlite"));
                updateWorldEpoch=RoadRush.Core.WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,"proof",Session.World.Route.Region==null?LanMapShare.Seed:WorldSnapshotCodec.CanonicalJson(Session.World.Route.Region));
            }
            Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
            Session.Input.enabled=false;foreach(var t in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))t.gameObject.SetActive(false);
            yield return new WaitForSecondsRealtime(role=="host"?.5f:1.5f);
            if(role=="host")Session.Network.Host();else Session.Network.Join("127.0.0.1");
            Session.Car.Crashed+=(_,__)=>impacts++;initial=Session.Car.Body.position;start=Time.unscaledTime;began=true;
        }
        public static string CanopyDigest(GeoTree[] trees)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,new UTF8Encoding(false,true)))
            {
                writer.Write(trees?.Length??0);foreach(var tree in (trees??Array.Empty<GeoTree>()).OrderBy(t=>t.id,StringComparer.Ordinal))
                {writer.Write(tree.id);writer.Write(tree.x);writer.Write(tree.z);writer.Write(tree.height);writer.Write(tree.crownRadius);writer.Write(tree.seed);}writer.Flush();return RoadRush.Core.DataPackageRules.Hash(stream.ToArray());
            }
        }
        // Unity serializes null strings/arrays as empty values. Use the same canonical
        // representation on either side of the first signed snapshot reconstruction.
        public static string BuildingDigest(GeoBuilding[] buildings)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,new UTF8Encoding(false,true)))
            {
                writer.Write(buildings?.Length??0);foreach(var b in (buildings??Array.Empty<GeoBuilding>()).OrderBy(b=>b.id??"",StringComparer.Ordinal))
                {writer.Write(b.id??"");writer.Write(b.kind??"");writer.Write(b.roof??"");writer.Write(b.height);writer.Write(b.measuredHeight);writer.Write(b.points?.Length??0);foreach(var p in b.points??Array.Empty<Vector3>()){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}}
                writer.Flush();return RoadRush.Core.DataPackageRules.Hash(stream.ToArray());
            }
        }
        static void WritePoint(BinaryWriter writer,Vector3 p){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
        static string MeshDigest(IEnumerable<MeshFilter> filters)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,new UTF8Encoding(false,true)))
            {
                var all=filters.OrderBy(f=>f.name,StringComparer.Ordinal).ToArray();writer.Write(all.Length);
                foreach(var filter in all){var mesh=filter.sharedMesh;writer.Write(filter.name);writer.Write(mesh?mesh.vertexCount:0);if(!mesh)continue;foreach(var v in mesh.vertices)WritePoint(writer,filter.transform.TransformPoint(v));var indices=mesh.triangles;writer.Write(indices.Length);foreach(int i in indices)writer.Write(i);var uv=mesh.uv;writer.Write(uv.Length);foreach(var v in uv){writer.Write(v.x);writer.Write(v.y);}}
                writer.Flush();return RoadRush.Core.DataPackageRules.Hash(stream.ToArray());
            }
        }
        public static string FeatureDigest(IEnumerable<BuildingFacadeChunk.Feature> features)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,new UTF8Encoding(false,true)))
            {var all=features.OrderBy(f=>f.Id,StringComparer.Ordinal).ToArray();writer.Write(all.Length);foreach(var f in all){writer.Write(f.Id??"");WritePoint(writer,f.Position);writer.Write(f.Rotation.x);writer.Write(f.Rotation.y);writer.Write(f.Rotation.z);writer.Write(f.Rotation.w);writer.Write(f.Width);writer.Write(f.Height);writer.Write(f.IsDoor);writer.Write(f.Palette);}writer.Flush();return RoadRush.Core.DataPackageRules.Hash(stream.ToArray());}
        }
        static bool ColliderWitness(MeshCollider collider,bool upwardOnly)
        {
            if(!collider||!collider.enabled||!collider.gameObject.activeInHierarchy||!collider.sharedMesh)return false;var mesh=collider.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;
            for(int i=0;i<t.Length;i+=3){var a=collider.transform.TransformPoint(v[t[i]]);var b=collider.transform.TransformPoint(v[t[i+1]]);var c=collider.transform.TransformPoint(v[t[i+2]]);var normal=Vector3.Cross(b-a,c-a).normalized;if(normal.sqrMagnitude<.5f||(upwardOnly&&normal.y<.99f))continue;var centre=(a+b+c)/3;return collider.Raycast(new Ray(centre+normal*.3f,-normal),out var hit,.6f)&&Vector3.Distance(hit.point,centre)<.01f;}
            return false;
        }
        void ObserveBuildings()
        {
            if(expectedBuildingCount<0||!Session.Network.Joined||Session.Network.RttSamples<10||Time.unscaledTime<nextBuildingSample)return;nextBuildingSample=Time.unscaledTime+.5f;buildingSamples++;
            var region=Session.World.Route.Region;var world=Session.World.GeographicWorld;var canonical=region?.buildings??Array.Empty<GeoBuilding>();lastBuildingDigest=BuildingDigest(canonical);if(initialBuildingDigest.Length==0)initialBuildingDigest=lastBuildingDigest;
            bool valid=world&&canonical.Length==expectedBuildingCount&&lastBuildingDigest==initialBuildingDigest&&(expectedImportantCount<0||canonical.Count(b=>b.kind=="important_building")==expectedImportantCount);
            int walls=0,windows=0,doors=0,flights=0,nearWalls=0,nearWindows=0,nearDoors=0,nearAccess=0;
            if(world)
            {
                var byId=canonical.ToDictionary(b=>b.id,StringComparer.Ordinal);var features=new Dictionary<string,List<BuildingFacadeChunk.Feature>>(StringComparer.Ordinal);var ids=new HashSet<string>(StringComparer.Ordinal);
                foreach(var chunk in world.GetComponentsInChildren<BuildingFacadeChunk>(true))if(chunk.gameObject.activeInHierarchy)
                {valid&=chunk.OverflowCount==0;foreach(var f in chunk.Features){valid&=ids.Add(f.Id);int edge=f.Id.LastIndexOf("/e",StringComparison.Ordinal);if(edge<0){valid=false;continue;}string part=f.Id.Substring(0,edge);if(!features.TryGetValue(part,out var list))features[part]=list=new List<BuildingFacadeChunk.Feature>();list.Add(f);bool near=(f.Position-Session.Car.Body.position).sqrMagnitude<150*150;if(f.IsDoor){doors++;if(near)nearDoors++;}else{windows++;if(near)nearWindows++;}}}
                foreach(string part in features.Keys){string source=part;int split=source.IndexOf("/site-",StringComparison.Ordinal);if(split>=0)source=source.Substring(0,split);valid&=byId.ContainsKey(source)&&world.BuildingSites.ContainsKey(part);}
                foreach(var collider in world.GetComponentsInChildren<MeshCollider>(true))if(collider.gameObject.activeInHierarchy&&collider.enabled&&collider.name.StartsWith("Building ",StringComparison.Ordinal))
                {
                    walls++;if(collider.bounds.SqrDistance(Session.Car.Body.position)<150*150)nearWalls++;string part=collider.name.Substring(9);int suffix=part.LastIndexOf(" (",StringComparison.Ordinal);if(suffix>=0)part=part.Substring(0,suffix);
                    if(buildingParts.ContainsKey(part))continue;if(buildingParts.Count>=4096){valid=false;continue;}
                    string source=part;int split=source.IndexOf("/site-",StringComparison.Ordinal);if(split>=0)source=source.Substring(0,split);
                    if(!byId.TryGetValue(source,out var original)||!world.BuildingSites.TryGetValue(part,out var site)){valid=false;continue;}
                    var partFeatures=features.TryGetValue(part,out var found)?found:new List<BuildingFacadeChunk.Feature>();var roofs=collider.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.transform!=collider.transform).ToArray();
                    var record=new BuildingPartEvidence{id=part,sourceId=source,kind=original.kind??"",floorY=site.LevelFloorY,groundMedianY=site.GroundMedianY,boundaryMinY=site.BoundaryMinY,boundaryMaxY=site.BoundaryMaxY,conservativeFallback=site.ConservativeFallback,roofBurialRisk=site.RoofBurialRisk,wallDigest=MeshDigest(new[]{collider.GetComponent<MeshFilter>()}),roofDigest=MeshDigest(roofs),featureDigest=FeatureDigest(partFeatures),windows=partFeatures.Count(f=>!f.IsDoor),doors=partFeatures.Count(f=>f.IsDoor),roofMeshes=roofs.Length,wallRayHit=ColliderWitness(collider,false)};
                    buildingParts.Add(part,record);valid&=record.wallRayHit&&!record.roofBurialRisk;
                }
                foreach(var access in world.GetComponentsInChildren<BuildingAccessChunk>(true))if(access.gameObject.activeInHierarchy)
                {
                    flights+=access.FlightCount;valid&=access.Sealed&&access.OverflowCount==0;var collider=access.GetComponentsInChildren<MeshCollider>().FirstOrDefault(c=>c.name=="Supported garden steps");if(access.FlightCount>0&&collider&&collider.bounds.SqrDistance(Session.Car.Body.position)<150*150)nearAccess++;
                    string key=access.transform.name;if(buildingAccess.ContainsKey(key))continue;if(buildingAccess.Count>=256){valid=false;continue;}var record=new BuildingAccessEvidence{id=key,flights=access.FlightCount,sealedMesh=access.Sealed,meshDigest=MeshDigest(access.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Supported garden steps")),colliderRayHit=ColliderWitness(collider,true)};buildingAccess.Add(key,record);valid&=access.FlightCount==0||record.colliderRayHit;
                }
            }
            var stats=BuildingFacadeChunk.Statistics;peakBuildingWalls=Math.Max(peakBuildingWalls,walls);peakBuildingWindows=Math.Max(peakBuildingWindows,windows);peakBuildingDoors=Math.Max(peakBuildingDoors,doors);peakBuildingFlights=Math.Max(peakBuildingFlights,flights);peakBuildingDraws=Math.Max(peakBuildingDraws,stats.drawSubmissions);
            valid&=walls>0&&windows>0&&doors>0&&flights>0;if(!valid)buildingFailures++;
            if(nearWalls>0&&nearWindows>0&&nearDoors>0&&nearAccess>0&&stats.frame==Time.frameCount&&stats.submittedWindows>0&&stats.submittedDoors>0&&stats.drawSubmissions>0)buildingNearSamples++;
        }
        void ObserveCanopy()
        {
            if(expectedCanopyCount<0||!Session.Network.Joined||Session.Network.RttSamples<10||Time.unscaledTime<nextCanopySample)return;nextCanopySample=Time.unscaledTime+.5f;canopySamples++;
            var region=Session.World.Route.Region;var geography=Session.World.GeographicWorld;lastCanopyDigest=CanopyDigest(region?.trees);if(initialCanopyDigest.Length==0)initialCanopyDigest=lastCanopyDigest;
            bool valid=region?.trees?.Length==expectedCanopyCount&&lastCanopyDigest==initialCanopyDigest&&geography;
            int active=0,trunks=0;bool near=false;
            if(geography)
            {
                var known=(region?.trees??Array.Empty<GeoTree>()).ToDictionary(t=>t.id);var seen=new HashSet<string>();
                foreach(var chunk in geography.GetComponentsInChildren<DetailedWoodlandChunk>(true))if(chunk.gameObject.activeInHierarchy)
                {
                    active+=chunk.TreeCount;foreach(var tree in chunk.Instances){valid&=seen.Add(tree.Id)&&known.TryGetValue(tree.Id,out var canonical)&&tree.Position.x==canonical.x&&tree.Position.z==canonical.z&&tree.Height==canonical.height&&tree.Radius==canonical.crownRadius;}
                    foreach(var collider in chunk.GetComponentsInChildren<CapsuleCollider>(true))if(collider.name.StartsWith("Mapped tree trunk ",StringComparison.Ordinal)&&collider.enabled&&collider.gameObject.activeInHierarchy)
                    {trunks++;var delta=collider.transform.position-Session.Car.Body.position;delta.y=0;if(delta.sqrMagnitude<=120*120)near=true;}
                }
            }
            var stats=DetailedWoodlandChunk.Statistics;peakActiveCanopyTrees=Math.Max(peakActiveCanopyTrees,active);peakCanopyTrunks=Math.Max(peakCanopyTrunks,trunks);peakCanopyDrawSubmissions=Math.Max(peakCanopyDrawSubmissions,stats.drawSubmissions);
            valid&=active>0&&trunks==active;if(!valid)canopyFailures++;if(near&&stats.submittedTrees>0&&stats.drawSubmissions>0&&stats.frame>=Time.frameCount-2)canopyNearSamples++;
        }
        void LateUpdate()
        {
            if(!began)return;
            var current=FindAnyObjectByType<GameSession>();if(!current||!current.BootReady||LocationLoading.Busy)return;
            if(Session!=current){Session=current;reloads++;Session.Input.enabled=false;foreach(var t in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))t.gameObject.SetActive(false);lastRttSample=0;watchingGuest=false;}
            ObserveCanopy();ObserveBuildings();railProof?.Observe(Session);vehicleMetricProof?.Observe(Session);var car=Session.Car;float elapsed=Time.unscaledTime-start;
            if((shareProof||expectedUpdate!=null||expectedCanopyCount>=0||expectedBuildingCount>=0||railProof!=null||vehicleMetricProof!=null)&&Session.Network.Joined&&!everJoined){everJoined=true;joinedAt=Time.unscaledTime;initial=car.Body.position;if(expectedUpdate!=null&&shareProof)updateWorldEpoch=RoadRush.Core.WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,"proof",Session.World.Route.Region==null?LanMapShare.Seed:WorldSnapshotCodec.CanonicalJson(Session.World.Route.Region));}
            if((shareProof||expectedUpdate!=null||expectedCanopyCount>=0||expectedBuildingCount>=0||railProof!=null||vehicleMetricProof!=null)&&everJoined)elapsed=Time.unscaledTime-joinedAt;
            if(shareProof&&role=="client"&&Session.Network.ReceivedStates>100&&!captured){ScreenCapture.CaptureScreenshot(prefix+"-joined.png");captured=true;}
            float d=Session.World.Route.Closest(car.Body.position,out _,out _);var target=Session.World.Route.At(d+12,Session.World.Route.LaneOffset(d+12));
            var local=car.transform.InverseTransformPoint(target);float steer=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg/20,-1,1);
            float desired=railProof!=null?7:role=="host"?6:12;float throttle=car.AbsoluteSpeed<desired?.45f:0;float brake=car.AbsoluteSpeed>desired+1?.2f:0;
            // The bounded rail corridor must be traversed after both observers
            // join; otherwise the host passes the bridge while the guest loads.
            bool waitingForRailGuest=railProof!=null&&!everJoined;
            if(waitingForRailGuest){throttle=0;brake=0;}
            if(railProof!=null){Session.Input.Handbrake=waitingForRailGuest?1:0;car.NetworkHandbrake=waitingForRailGuest?1:0;}
            if(parkingProof!=null)
            {
                float handbrake=Session.Input.Handbrake;
                bool crossed=railProof!=null&&railProof.TraversalObserved;
                parkingProof.Tick(Session,crossed,ref steer,ref throttle,ref brake,ref handbrake);
                Session.Input.Handbrake=car.NetworkHandbrake=handbrake;
                // Host's local physical controller is NetworkDriven in this harness.
                // Preserve production command semantics for that path too.
                if(role=="host"){car.NetworkParkingBrakeRequest=Session.Input.ParkingBrakeRequest;car.NetworkParkingBrakeCommand=Session.Input.ParkingBrakeCommand;}
            }
            Session.Input.Steer=steer;Session.Input.Throttle=throttle;Session.Input.Brake=brake;
            if(role=="host")
            {car.NetworkDriven=true;car.NetworkSteer=steer;car.NetworkThrottle=throttle;car.NetworkBrake=brake;if(Session.Network.Other)lastGuest=Session.Network.Other.Body.position;}
            if(role=="client"&&Session.Network.Joined&&Session.Network.ReceivedStates>5)
            {
                Session.Weather=5;environmentSamples++;if(!Session.Network.EnvironmentSynchronized)environmentFailures++;
            }
            if(Session.Network.Joined&&Session.Network.Other&&Session.Network.RttSamples>10)
            {var visual=Session.Network.Other.Visual;bool lit=Session.Conditions.Daylight<.55f||Session.Conditions.Rain>.2f;visualEnvironmentSamples++;if(visual.Headlights==null||visual.Headlights.Length!=2||System.Array.Exists(visual.Headlights,l=>!l||l.enabled!=lit)||Mathf.Abs(visual.WiperRain-Session.Conditions.Rain)>.03f)visualEnvironmentFailures++;}
            if(Session.Network.RttSamples>lastRttSample){lastRttSample=Session.Network.RttSamples;rtts.Add(Session.Network.RoundTripMs);maxRtt=Mathf.Max(maxRtt,Session.Network.RoundTripMs);}
            if(Session.Network.Other){otherMass=Session.Network.Other.Body.mass;otherDent=Session.Network.Other.Visual.DeformationExtent;}
            if(Session.Network.Other&&!watchingGuest){watchingGuest=true;Session.Network.Other.Crashed+=(_,__)=>impacts++;}
            if((shareProof||expectedUpdate!=null||expectedCanopyCount>=0||expectedBuildingCount>=0||railProof!=null||vehicleMetricProof!=null)&&!everJoined&&elapsed<InitialJoinReadinessSeconds)return;
            if((!shareProof||everJoined)&&elapsed<(epochMismatch&&!shareProof?(role=="host"?8:6):(role=="host"?24:20)))return;
            if(parkingProof!=null&&!parkingProof.Completed)return;
            bool pass=role=="host"?Session.Network.ReceivedInputs>100&&Session.Network.RttSamples>100:Session.Network.ReceivedStates>100&&Vector3.Distance(initial,car.Body.position)>25;
            if(epochMismatch&&!shareProof)pass=Session.Menu&&!Session.Network.Joined&&Session.Network.ReceivedInputs==0&&Session.Network.ReceivedStates==0&&Session.Network.Other==null&&Session.Network.Status.IndexOf("different map data",StringComparison.OrdinalIgnoreCase)>=0;
            if(shareProof)pass=pass&&everJoined&&(role=="host"?(cachedProof?Session.Network.SharedBytesSent==0:Session.Network.SharedBytesSent>0):reloads==1&&(cachedProof?LanSession.LastMapBytesReceived==0:LanSession.LastMapBytesReceived>0)&&Session.World.Route.Region.id.StartsWith("download-peer-"));
            if((!epochMismatch||shareProof)&&role=="client")pass=pass&&environmentSamples>100&&environmentFailures==0&&Session.Conditions.WeatherRequests==0;
            if(expectedMass>0)pass=pass&&Mathf.Abs(otherMass-expectedMass)<.1f&&otherDent>.01f;
            if(!epochMismatch||shareProof)pass=pass&&visualEnvironmentSamples>100&&visualEnvironmentFailures==0;
            bool updateStaged=false,updatePinned=false;
            if(expectedUpdate!=null)
            {
                string country=Path.Combine(updateRoot,"GuestPacks/Country"),directory=Path.Combine(updateRoot,"GuestUpdates",expectedUpdate.pack);
                updateStaged=true;
                for(long version=long.Parse(expectedUpdate.toVersion);version<=long.Parse(expectedLastUpdate.toVersion);version++)
                {
                    string header=Path.Combine(directory,expectedUpdate.pack+"-"+version+".json");
                    if(!File.Exists(header)){updateStaged=false;break;}
                    var staged=JsonUtility.FromJson<RoadRush.Core.DataFileManifest>(File.ReadAllText(header));
                    updateStaged=updateStaged&&staged.format==2&&RoadRush.Core.DataPackageRules.FileMatches(RoadRush.Core.DataPackageRules.PathFor(directory,staged),staged)&&RoadRush.Core.DataPackageRules.VerifySignature(staged,Session.Network.HostFingerprint);
                    if(version==long.Parse(expectedLastUpdate.toVersion))updateStaged=updateStaged&&staged.timestamp==(string.IsNullOrEmpty(expectedLastUpdate.timestamp)?"2026-11-05T00:00:00Z":expectedLastUpdate.timestamp);
                }
                updatePinned=new CountryPack(country).Version=="13"&&RoadRush.Core.DataPackageRules.HashFile(Path.Combine(country,"map.sqlite"))==updateCountryHash&&RoadRush.Core.WorldEpoch.Matches(updateWorldEpoch,RoadRush.Core.WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,"proof",Session.World.Route.Region==null?LanMapShare.Seed:WorldSnapshotCodec.CanonicalJson(Session.World.Route.Region)));
                pass=pass&&updateStaged&&updatePinned&&Session.Updates.SourceRequests==0&&(role!="host"||Session.Network.SharedBytesSent>=expectedUpdateBytes);
            }
            if(expectedCanopyCount>=0)pass=pass&&canopySamples>10&&canopyFailures==0&&canopyNearSamples>0&&Session.World.Route.Region?.trees?.Length==expectedCanopyCount&&Session.Updates.SourceRequests==0;
            if(expectedBuildingCount>=0)
            {
                pass=pass&&buildingSamples>10&&buildingFailures==0&&buildingNearSamples>0&&Session.Updates.SourceRequests==0;
                var evidence=new BuildingEvidence{canonicalDigest=lastBuildingDigest,canonicalCount=Session.World.Route.Region.buildings.Length,importantCount=Session.World.Route.Region.buildings.Count(b=>b.kind=="important_building"),parts=buildingParts.Values.OrderBy(p=>p.id,StringComparer.Ordinal).ToArray(),access=buildingAccess.Values.OrderBy(p=>p.id,StringComparer.Ordinal).ToArray()};
                File.WriteAllText(prefix+"-buildings.json",JsonUtility.ToJson(evidence,true));
            }
            if(parkingProof!=null)pass=pass&&parkingProof.Passed;
            if(vehicleMetricProof!=null)pass=vehicleMetricProof.Finish(Session,prefix+"-vehicle-metrics.json")&&pass;
            if(railProof!=null)pass=railProof.Finish(Session,prefix+"-railways.json")&&pass&&Session.Updates.SourceRequests==0&&Session.Conditions.WeatherRequests==0;
            rtts.Sort();float median=rtts.Count>0?rtts[rtts.Count/2]:0,p95=rtts.Count>0?rtts[Mathf.Min(rtts.Count-1,(int)(rtts.Count*.95f))]:0;
            string result=$"{(pass?"PASS":"FAIL")} {role}\nexpectedCanonicalBuildingCount={expectedBuildingCount}\nexpectedImportantBuildingCount={expectedImportantCount}\nbuildingDigest={lastBuildingDigest}\ninitialBuildingDigest={initialBuildingDigest}\nbuildingSamples={buildingSamples}\nbuildingFailures={buildingFailures}\nbuildingNearSamples={buildingNearSamples}\npeakBuildingWalls={peakBuildingWalls}\npeakBuildingWindows={peakBuildingWindows}\npeakBuildingDoors={peakBuildingDoors}\npeakBuildingFlights={peakBuildingFlights}\npeakBuildingDrawSubmissions={peakBuildingDraws}\nbuildingPartRecords={buildingParts.Count}\nexpectedCanopyCount={expectedCanopyCount}\ncanonicalCanopyCount={Session.World.Route.Region?.trees?.Length??0}\ncanopyDigest={lastCanopyDigest}\ninitialCanopyDigest={initialCanopyDigest}\ncanopySamples={canopySamples}\ncanopyFailures={canopyFailures}\ncanopyNearSamples={canopyNearSamples}\npeakActiveCanopyTrees={peakActiveCanopyTrees}\npeakCanopyTrunks={peakCanopyTrunks}\npeakCanopyDrawSubmissions={peakCanopyDrawSubmissions}\ncanopyNearRadiusMetres=120\npeerUpdates={expectedUpdate!=null}\nvisualEnvironmentSamples={visualEnvironmentSamples}\nvisualEnvironmentFailures={visualEnvironmentFailures}\nexpectedLastUpdate={expectedLastUpdate?.toVersion}\nexpectedUpdateBytes={expectedUpdateBytes}\nupdateStaged={updateStaged}\nupdatePinned={updatePinned}\nmapSourceRequests={Session.Updates.SourceRequests}\nshareProof={shareProof}\nsceneReloads={reloads}\nmapBytesSent={Session.Network.SharedBytesSent}\nmapBytesReceived={LanSession.LastMapBytesReceived}\nmap={Session.World.Route.Region?.id}\nepochMismatchFixture={epochMismatch}\nrttSamples={Session.Network.RttSamples}\ninputs={Session.Network.ReceivedInputs}\nstates={Session.Network.ReceivedStates}\ntravel={Vector3.Distance(initial,car.Body.position):F2}\nmedianRttMs={median:F1}\np95RttMs={p95:F1}\nmaxRttMs={maxRtt:F1}\nimpacts={impacts}\nenvironmentSamples={environmentSamples}\nenvironmentFailures={environmentFailures}\nweatherSourceRequests={Session.Conditions.WeatherRequests}\nsharedHour={Session.Conditions.Hour:F3}\nsharedRain={Session.Conditions.Rain:F3}\nsharedFog={Session.Conditions.Fog:F6}\notherMass={otherMass}\notherDent={otherDent:F3}\nposition={car.Body.position}\nguest={lastGuest}\nstatus={Session.Network.Status}\n";
            if(railProof!=null)result+=$"railDigest={railProof.Data.railDigest}\nrailRoadDigest={railProof.Data.roadDigest}\nrailSamples={railProof.Data.samples}\nrailFailures={railProof.Data.failures}\nrailEpoch={railProof.Data.epoch}\n";
            if(vehicleMetricProof!=null)result+=$"vehicleMetricSamples={vehicleMetricProof.Data.samples}\nvehicleMetricFailures={vehicleMetricProof.Data.failures}\nvehicleMetricInterpolationChecks={vehicleMetricProof.Data.interpolationChecks}\nvehicleMetricMaxResidual={vehicleMetricProof.Data.maxInterpolationResidual:R}\nvehicleMetricWheelRange={vehicleMetricProof.Data.wheelRange:R}\n";
            File.WriteAllText(prefix+"-result.txt",result);Debug.Log(result);Application.Quit(pass?0:1);began=false;
        }
    }
}
