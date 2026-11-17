using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace EarthUnscripted.NewbridgePrototype
{
    /// <summary>
    /// Streams source-derived terrain, roads, railways and paths in owned chunks.
    /// The separate proof player supplies its own camera/car; normal game hosts
    /// explicitly prepare this renderer and retain their real player ownership.
    /// This component never loads careers or writes PlayerPrefs. Units are metres.
    /// </summary>
    public sealed partial class NewbridgeWorld : MonoBehaviour
    {
        [Serializable] public sealed class Feature
        {
            public string id, sourceId, name, @ref, kind, category, widthSource;
            public string signLighting,signZone,signLimitForward,signLimitBackward;public bool signDual,signNational,signNationalDirectional,signNationalForward,signNationalBackward;public SignContextSpan[] signContexts;
            public float width, speedLimitMps, tunnelClearance,travelWidth,shoulderLeft,shoulderRight,roadCentreOffset;
            public int laneCount;
            public float speedLimitForwardMps,speedLimitBackwardMps;
            public Control[] controls;
            public string turnLanes,turnLanesBackward,electrified,voltage,frequency,contactLineType,loadingGauge,gauge,railClearanceSource;
            public float railClearanceHeight,railClearanceWidth;
            public string shoulderSource,laneAccess,laneAccessConditional,trafficSide;
            public bool hasVariableLane,laneControl;
            public string level,tunnelKind;
            public bool galleryOpenLeft,galleryOpenRight;
            public string speedLimitSource;
            public bool bridge, tunnel, oneway, roundabout;
            public int layer;
            public Vector3[] points;
            public string[] sourceNodeIds;
            public int surfacePriority;
            public SurfaceJoin[] junctions;
            [NonSerialized] public List<Vector3> sharpBends;
            [NonSerialized] public HashSet<string> originalNodes;
        }
        [Serializable] public sealed class Control {public string id,kind,group,value,crossing;public float station,x,y,z,dx,dz;public int direction,mountSide;public bool repeater;public float diameter;public Vector3 Position=>new Vector3(x,y,z);public Vector3 Forward=>new Vector3(dx,0,dz);}
        [Serializable] public sealed class Building { public int layer;public bool underground;public float minHeight; public string id,kind,roofShape; public Vector3[] points;public CoastRing[] holes; public float height,roofHeight;public int levels;public bool heightIncludesRoof; [NonSerialized] public Vector3 centre; }
        [Serializable] public sealed class TreeSeed { public float x,y,z,height,crownRadius;public int seed;public string heightSource; }
        [Serializable] public sealed class DriveRoute { public string name; public Vector3[] points; public float[] speedLimits; public string[] speedLimitSources; public string[] sourceWays; [NonSerialized] public float[] stations; }
        [Serializable] public sealed class FrameInfo {public float height_origin=100;public string traffic_side,world_crs,provider,country_code;public int epsg;}
        [Serializable] public sealed class Place {public string id,name;public float x,z;}
        [Serializable] public sealed class Map
        {
            public string format, attribution, elevationQuality;
            public FrameInfo frame;
            public float minX, minZ, maxX, maxZ, gridStep;
            public int nx, nz;
            public float coreMinX, coreMinZ, coreMaxX, coreMaxZ;
            public int tileX, tileZ;
            public float[] heights;
            public Feature[] features;
            public Place[] places;
            public Building[] buildings;
            public TreeSeed[] trees;
            public WaterArea[] waterAreas;
            public DriveRoute[] routes;
            [NonSerialized] public Dictionary<Vector2Int,List<PavingPatch>> paving;
            [NonSerialized] public List<PavingPatch> preparedPaving;
        }
        [Serializable] public sealed class ChunkTiming
        {
            public int x, z, vertices, triangles;
            public double cpuMilliseconds, elapsedMilliseconds, colliderMilliseconds, diagnosticMilliseconds;
            public float requestedDistanceMetres, requestedAheadMetres, readyAheadMetres;
            public double queueToReadyMilliseconds;
        }
        [Serializable] public sealed class DriveResult
        {
            public string name, error, lastImpactObject;
            public float distance, maxDamage, airborneSeconds, maxLateralError;
            public bool completed;
            public int missingRoadSamples;
            public float elapsedSeconds, maximumSpeed;
        }
        [Serializable] public sealed class FrontierSample
        {
            public float seconds, progressMetres, remainingMetres, readyAheadMetres, speed;
            public int activeChunks, pendingChunks;
            public bool road128, road256, road512, road768;
        }
        [Serializable] public sealed class GeometryIssue
        {
            public string sourceWay,category,issue,collider;
            public Vector3 position;
            public float measured,required;
        }
        [Serializable] public sealed class AirborneSample
        {
            public Vector3 position;
            public float seconds,station,speed,targetSpeed;
        }
        [Serializable] public sealed class Report
        {
            public string scope = "Live source acquisition and numeric preparation with runtime forward geometry streaming";
            public double parseMilliseconds, initialReadySeconds, allCaptureSeconds;
            public int activeChunks, generatedChunks, retiredChunks, terrainSamples, buriedRailSamples, railSamples, railColliderSamples, buriedRailColliderSamples, pathColliderSamples, buriedPathColliderSamples, roadColliderSamples, buriedRoadColliderSamples;
            public bool flightRestorePassed, routeRestorePassed;
            public int bridgeCorridorSamples, blockedBridgeCorridorSamples, tunnelColliderSamples, obstructedTunnelSamples,laneControlGantries;
            public int roadCorridorSamples,blockedRoadCorridorSamples;
            public float minimumTunnelClearanceMetres=999;
            public int tunnelTerrainSamples,obstructedTunnelTerrainSamples;
            public int maximumActiveChunks, pendingChunks;
            public int reusedGeometryChunks, warmGeometryChunks, evictedWarmChunks;
            public int diskGeometryHits, diskGeometryMisses;
            public int controlLayoutBuilds,controlLayoutCacheHits,controlLayoutCachedMaps,controlLayoutCachedPlacements;
            public int trimmedBuildings,failedBuildingRoofTriangulations;
            public int buildingJobs;public double maximumBuildingWorkerMilliseconds;
            public double maximumBuildingRoadOverlap;
            public int forecastRequests;
            public double forecastMilliseconds;
            public int emptyFrontierFallbackRequests;
            public long warmGeometryBytes;
            public float routeLengthMetres, requestedLookAheadMetres=768;
            public List<FrontierSample> frontier=new List<FrontierSample>();
            public List<ChunkTiming> chunks = new List<ChunkTiming>();
            public List<DriveResult> drives = new List<DriveResult>();
            public List<float> frameMilliseconds = new List<float>();
            public List<GeometryIssue> geometryIssues=new List<GeometryIssue>();
            public List<AirborneSample> airborneSamples=new List<AirborneSample>();
        }

        // Chunk and scheduling budgets are gameplay approximations. Atomic mesh
        // upload/collider cooking may exceed SliceMilliseconds and are measured.
        const float ChunkSize = 64f;
        const float StreamRadius = 224f;
        const float RetireRadius = 340f;
        // Route distance in metres; retain a 160 m scenery corridor around the
        // next 768 m, in addition to the ordinary 224 m local view. Reconcile
        // after 24 m of physical movement; generation runs on Unity main thread.
        const float ForwardGenerationDistance=768f;
        const float ForwardCorridorRadius=160f;
        const float ReconcileMovement=24f;
        const double SliceMilliseconds = 2.0;
        const float TerrainStep = 2f;
        const float RailTopToBallast = .30f;
        const float DeckDepth = .75f;
        const int SegmentCellSize = 16;
        readonly Report report = new Report();
        readonly Dictionary<Vector2Int, GameObject> chunks = new Dictionary<Vector2Int, GameObject>();
        readonly Dictionary<Vector2Int,double> requestedAt=new Dictionary<Vector2Int,double>();
        readonly List<GameObject> ownedRoots = new List<GameObject>();
        Map map;
        Camera view;
        VehicleController car;
        DriverInput input;
        Vector3 observer;
        Vector3 lastReconcilePosition=new Vector3(float.MaxValue,0,0);
        int guideSegment;
        string output;
        bool capturing, building, ready, fly, overview, measuringDrive, changingRoute, changingFlight;
        int routeIndex;
        readonly Stopwatch life = Stopwatch.StartNew();
        Material grass, asphalt, gravel, steel, sleeper, concrete, water, path, paint, wall, roof,redSignal,greenSignal;

        internal struct Segment
        {
            public Feature feature;
            public Vector3 a, b;
            public Vector3 centreA,centreB;
            public float station;
            public int pointIndex;
            public bool surfaceStart,surfaceEnd;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate() { if(IsPrototypePlayer)GameSession.SuppressAutomaticLaunch = true; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch() { if(IsPrototypePlayer)new GameObject("Newbridge streaming test map").AddComponent<NewbridgeWorld>(); }

        void Awake()
        {
            if(!IsPrototypePlayer)return;
            Application.runInBackground = true;
            AudioListener.volume = 0;
            AudioListener.pause = true;
            Time.fixedDeltaTime = .01f;
            Time.timeScale = 1;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 90;
            var args = Environment.GetCommandLineArgs();
            int capture = Array.IndexOf(args, "-newbridgeCapture");
            capturing = capture >= 0 && capture + 1 < args.Length;
            if (capturing)
            {
                output = Path.GetFullPath(args[capture + 1]);
                Directory.CreateDirectory(output);
                Application.logMessageReceived += CaptureFailure;
            }
        }

        void CaptureFailure(string message,string stack,LogType type)
        {
            if(type!=LogType.Exception&&type!=LogType.Error)return;
            File.WriteAllText(Path.Combine(output,"failure.txt"),message+"\n"+stack);
            Application.Quit(3);
        }

        void IndexMap(Map source)
        {
            spatialByMap.Add(source,BuildSpatialIndex(source));
        }

        static SpatialData BuildSpatialIndex(Map source)
        {
            ResolveSurfaceJoins(source);
            var spatial = new SpatialData();
            var index = new Dictionary<Vector2Int,List<int>>();
            var uniqueSegments = new List<Segment>();
            var meshFeatures = spatial.features;
            var meshBuildings = spatial.buildings;
            QueryIndexBounds(source,out int queryMinX,out int queryMinZ,out int queryMaxX,out int queryMaxZ);
            foreach (var feature in source.features)
            {
                float station = 0;var featureBounds=new Vector4(float.PositiveInfinity,float.PositiveInfinity,float.NegativeInfinity,float.NegativeInfinity);
                for (int i = 1; i < feature.points.Length; i++)
                {
                    var segment = new Segment { feature = feature, a = feature.points[i-1], b = feature.points[i], centreA=feature.points[i-1],centreB=feature.points[i],pointIndex=i,station = station };
                    station += Vector2.Distance(new Vector2(segment.a.x,segment.a.z), new Vector2(segment.b.x,segment.b.z));
                    for(int cz=Mathf.FloorToInt(Mathf.Min(segment.a.z,segment.b.z)/ChunkSize);cz<=Mathf.FloorToInt(Mathf.Max(segment.a.z,segment.b.z)/ChunkSize);cz++)
                        for(int cx=Mathf.FloorToInt(Mathf.Min(segment.a.x,segment.b.x)/ChunkSize);cx<=Mathf.FloorToInt(Mathf.Max(segment.a.x,segment.b.x)/ChunkSize);cx++)
                        {
                            var meshKey=new Vector2Int(cx,cz);
                            if(!meshFeatures.TryGetValue(meshKey,out var set))meshFeatures[meshKey]=set=new HashSet<Feature>();
                            set.Add(feature);
                        }
                    if(feature.category=="road" && feature.roadCentreOffset!=0)
                    {
                        segment.a+=Join(feature.points,i-1)*feature.roadCentreOffset;
                        segment.b+=Join(feature.points,i)*feature.roadCentreOffset;
                    }
                    float margin = feature.width * .5f + 12;
                    int west=Mathf.FloorToInt((Mathf.Min(segment.a.x,segment.b.x)-margin)/SegmentCellSize),east=Mathf.FloorToInt((Mathf.Max(segment.a.x,segment.b.x)+margin)/SegmentCellSize);
                    int south=Mathf.FloorToInt((Mathf.Min(segment.a.z,segment.b.z)-margin)/SegmentCellSize),north=Mathf.FloorToInt((Mathf.Max(segment.a.z,segment.b.z)+margin)/SegmentCellSize);
                    featureBounds=new Vector4(Mathf.Min(featureBounds.x,west),Mathf.Min(featureBounds.y,south),Mathf.Max(featureBounds.z,east),Mathf.Max(featureBounds.w,north));
                    spatial.unboundedSegmentCopies+=(long)(east-west+1)*(north-south+1);
                    int firstZ=Mathf.Max(queryMinZ,south),lastZ=Mathf.Min(queryMaxZ,north);
                    int firstX=Mathf.Max(queryMinX,west),lastX=Mathf.Min(queryMaxX,east);
                    if(firstZ>lastZ||firstX>lastX)continue;
                    int sharedIndex=uniqueSegments.Count;uniqueSegments.Add(segment);
                    for (int z = firstZ; z <= lastZ; z++)
                        for (int x = firstX; x <= lastX; x++)
                        {
                            var key = new Vector2Int(x,z);
                            if (!index.TryGetValue(key,out var list)) index[key] = list = new List<int>();
                            list.Add(sharedIndex);spatial.indexedSegmentCopies++;
                        }
                }
                spatial.queryFeatureBounds[feature]=featureBounds;
            }
            // Build source-local views once off-thread. Surface sampling must not
            // scan every other resampled road in a dense spatial cell twice.
            // Filtering preserves the original candidate order and exact maths.
            // Publish one immutable value array. Cell order is unchanged; each
            // cell stores only ordered indices. Halo-only records stay out of
            // this array and belong to independently owned fallback cells.
            spatial.segmentStorage=uniqueSegments.ToArray();
            foreach(var cell in index)
            {
                var candidates=new SegmentCandidates(spatial.segmentStorage,cell.Value);
                spatial.segments.Add(cell.Key,candidates);
                spatial.indexedMembershipSlots+=cell.Value.Capacity;
                var sources=new Dictionary<string,List<int>>(StringComparer.Ordinal);
                for(int segmentIndex=0;segmentIndex<candidates.Count;segmentIndex++)
                {
                    var segment=candidates[segmentIndex];
                    string identity=segment.feature.sourceId??segment.feature.id;
                    if(!sources.TryGetValue(identity,out var own))sources[identity]=own=new List<int>();
                    own.Add(segmentIndex);
                }
                spatial.sourceSegments.Add(cell.Key,sources);
            }
            foreach(var house in source.buildings)
            {
                house.centre=house.points.Aggregate(Vector3.zero,(sum,p)=>sum+p)/house.points.Length;
                var key=ChunkKey(house.centre);
                if(!meshBuildings.TryGetValue(key,out var list))meshBuildings[key]=list=new List<Building>();
                list.Add(house);
            }
            return spatial;
        }

        static Vector2Int ChunkKey(Vector3 point)=>new Vector2Int(Mathf.FloorToInt(point.x/ChunkSize),Mathf.FloorToInt(point.z/ChunkSize));

        Vector3 RouteAt(float station)
        {
            var route=map.routes[routeIndex];
            station=Mathf.Clamp(station,0,route.stations.Last());
            int index=Array.BinarySearch(route.stations,station);
            if(index>=0)return route.points[index];
            index=Mathf.Clamp(~index-1,0,route.points.Length-2);
            float t=(station-route.stations[index])/Mathf.Max(1e-5f,route.stations[index+1]-route.stations[index]);
            return Vector3.Lerp(route.points[index],route.points[index+1],t);
        }

        // Project onto XZ route segments and return cumulative 3D metres. The
        // local search follows the current guide segment during physical driving;
        // streaming requests use the complete route for distant chunk locations.
        float ClosestStation(Vector3 position,out int nearest,bool local=false)
        {
            var route=map.routes[routeIndex];
            int first=local?Mathf.Max(0,guideSegment-35):0;
            int last=local?Mathf.Min(route.points.Length-1,guideSegment+100):route.points.Length-1;
            float best=float.MaxValue,station=0;nearest=first;
            for(int i=first;i<last;i++)
            {
                var a=route.points[i];var b=route.points[i+1];
                var delta=new Vector2(b.x-a.x,b.z-a.z);
                float t=Mathf.Clamp01(Vector2.Dot(new Vector2(position.x-a.x,position.z-a.z),delta)/Mathf.Max(1e-10f,delta.sqrMagnitude));
                var offset=new Vector2(a.x+delta.x*t-position.x,a.z+delta.y*t-position.z);
                if(offset.sqrMagnitude>=best)continue;
                best=offset.sqrMagnitude;nearest=i;station=Mathf.Lerp(route.stations[i],route.stations[i+1],t);
            }
            return station;
        }

        int geometryResumeFrame;
        bool NeedsReconcile(Vector3 position)=>!building&&Time.frameCount>=geometryResumeFrame&&(PendingSceneryChunks>0||Vector3.ProjectOnPlane(position-lastReconcilePosition,Vector3.up).sqrMagnitude>=ReconcileMovement*ReconcileMovement);

        SegmentCandidates Nearby(float x,float z)
        {
            var source = AreaAt(x,z) ?? workingMap;
            if (source == null) return null;
            var spatial=spatialByMap[source];var cell=new Vector2Int(Mathf.FloorToInt(x/SegmentCellSize),Mathf.FloorToInt(z/SegmentCellSize));
            if(spatial.segments.TryGetValue(cell,out var list))return list;
            return InsideQueryIndex(source,cell)?null:FallbackQueryCell(source,spatial,cell).candidates;
        }

        readonly struct SegmentView
        {
            readonly SegmentCandidates all;readonly List<int> indices;
            public SegmentView(SegmentCandidates all,List<int> indices){this.all=all;this.indices=indices;}
            public int Count=>indices?.Count??0;
            public Enumerator GetEnumerator()=>new Enumerator(all,indices);
            public struct Enumerator
            {
                readonly SegmentCandidates all;readonly List<int> indices;int index;
                public Enumerator(SegmentCandidates all,List<int> indices){this.all=all;this.indices=indices;index=-1;}
                public bool MoveNext()=>indices!=null&&++index<indices.Count;
                public Segment Current=>all[indices[index]];
            }
        }
        SegmentView OwnSegments(Feature feature,float x,float z)
        {
            var source=AreaAt(x,z)??workingMap;
            if(source==null)return default;
            var cell=new Vector2Int(Mathf.FloorToInt(x/SegmentCellSize),Mathf.FloorToInt(z/SegmentCellSize));
            var spatial=spatialByMap[source];
            SegmentCandidates all;
            if(!spatial.sourceSegments.TryGetValue(cell,out var sources))
            {
                if(InsideQueryIndex(source,cell))return default;
                var fallback=FallbackQueryCell(source,spatial,cell);all=fallback.candidates;sources=fallback.sources;
            }
            else all=spatial.segments[cell];
            sources.TryGetValue(feature.sourceId??feature.id,out var indices);
            return new SegmentView(all,indices);
        }

        public double MaximumGenerationSliceMilliseconds {get;private set;}
        public string MaximumGenerationSliceStage {get;private set;}
        public int GenerationSlicesOver8ms {get;private set;}
        void RecordGenerationSlice(string stage,double milliseconds)
        {
            if(!ready)return; // driving diagnostics exclude initial world preparation
            if(milliseconds>8)GenerationSlicesOver8ms++;
            if(milliseconds>MaximumGenerationSliceMilliseconds)
            {MaximumGenerationSliceMilliseconds=milliseconds;MaximumGenerationSliceStage=stage;}
        }

        static float Project(Segment segment,float x,float z,out float height)
        {
            float dx=segment.b.x-segment.a.x,dz=segment.b.z-segment.a.z;
            float t=Mathf.Clamp01(((x-segment.a.x)*dx+(z-segment.a.z)*dz)/Mathf.Max(.0001f,dx*dx+dz*dz));
            height=Mathf.Lerp(segment.a.y,segment.b.y,t);
            float qx=segment.a.x+dx*t-x,qz=segment.a.z+dz*t-z;
            return Mathf.Sqrt(qx*qx+qz*qz);
        }

        void RecordGeometryIssue(Feature feature,string issue,Vector3 position,float measured,float required,string collider)
        {
            // A bounded diagnostic sample identifies the exact source and
            // geometry behind aggregate failures without growing with a drive.
            if(report.geometryIssues.Count<200)
                report.geometryIssues.Add(new GeometryIssue{sourceWay=feature.sourceId,category=feature.category,issue=issue,position=position,measured=measured,required=required,collider=collider});
        }

        [Serializable] sealed class LiveDiagnostics
        {
            public List<GeometryIssue> geometryIssues;
            public List<AirborneSample> airborneSamples;
            public int blockedRoadCorridorSamples,buriedPathColliderSamples,blockedBridgeCorridorSamples,obstructedTunnelSamples;
        }

        void WriteLiveDiagnostics()
        {
            if(!capturing)return;
            var diagnostics=new LiveDiagnostics{geometryIssues=report.geometryIssues,airborneSamples=report.airborneSamples,blockedRoadCorridorSamples=report.blockedRoadCorridorSamples,buriedPathColliderSamples=report.buriedPathColliderSamples,blockedBridgeCorridorSamples=report.blockedBridgeCorridorSamples,obstructedTunnelSamples=report.obstructedTunnelSamples};
            File.WriteAllText(Path.Combine(output,"geometry-diagnostics.json"),JsonUtility.ToJson(diagnostics,true));
        }

        float RawHeight(float x,float z)
        {
            var source = AreaAt(x,z) ?? workingMap;
            float gx=Mathf.Clamp((x-source.minX)/source.gridStep,0,source.nx-1),gz=Mathf.Clamp((z-source.minZ)/source.gridStep,0,source.nz-1);
            int ix=Mathf.Min((int)gx,source.nx-2),iz=Mathf.Min((int)gz,source.nz-2);
            return Mathf.Lerp(Mathf.Lerp(source.heights[iz*source.nx+ix],source.heights[iz*source.nx+ix+1],gx-ix),Mathf.Lerp(source.heights[(iz+1)*source.nx+ix],source.heights[(iz+1)*source.nx+ix+1],gx-ix),gz-iz);
        }

        /// <summary>
        /// Ground follows the nearest actual ground corridor. Decks do not push
        /// lower terrain up. A conservative extra ring keeps coarse triangles out
        /// of rail/sleeper space; ballast and shoulders cover that shallow recess.
        /// </summary>
        float Ground(float x,float z)
        {
            if(PavedGround(x,z,out float paved))return paved;
            float original=RawHeight(x,z), result=original, best=float.MaxValue;
            var nearby=Nearby(x,z);
            if (nearby==null) return LowerSeaBed(x,z,result);
            Feature roadbedOwner=null;
            float roadbedEdge=float.MaxValue,roadbedHeight=0;
            foreach(var segment in nearby)
            {
                var road=segment.feature;
                if(road.category!="road" || road.bridge || road.tunnel)continue;
                float distance=Project(segment,x,z,out float height),edge=distance-road.width*.5f;
                if(edge>2.9f)continue;
                bool overlaps=roadbedOwner!=null && roadbedOwner!=road && edge<=0 && roadbedEdge<=0;
                if(roadbedOwner!=null && (overlaps?road.surfacePriority>=roadbedOwner.surfacePriority:edge>=roadbedEdge))continue;
                roadbedOwner=road;roadbedEdge=edge;
                roadbedHeight=RoadSurfacePoint(road,new Vector3(x,height,z)).y;
            }
            // A ground road owns its embankment across asphalt and the coarse
            // terrain triangle apron. Lower paths, incoming roads and culverted
            // streams must not dig an open trench through that support. Bridges
            // and tunnel floors are excluded so genuine underpasses remain open.
            if(roadbedOwner!=null)return roadbedHeight-.20f;
            foreach (var segment in nearby)
            {
                var f=segment.feature;
                if (f.bridge || f.tunnel || f.category=="water") continue;
                float distance=Project(segment,x,z,out float height),edge=distance-f.width*.5f;
                if (edge>10 || edge>=best) continue;
                best=edge;
                float recess=f.category=="rail"?.52f:f.category=="path"?.13f:.10f;
                float blend=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((edge-2)/8));
                result=Mathf.Lerp(original,height-recess,blend);
            }
            foreach (var segment in nearby)
            {
                var f=segment.feature;
                if(f.tunnel)continue;
                float distance=Project(segment,x,z,out float height);
                // Even a bridge endpoint may meet a retained DTM bank. Keep its
                // complete deck footprint clear without raising lower corridors.
                if(f.bridge)
                {
                    // Excavation stops at the actual span ends. Extending the
                    // endpoint projection into the approach made decks float.
                    var first=f.points[0];var last=f.points.Last();
                    var startDirection=Vector3.ProjectOnPlane(f.points[1]-first,Vector3.up);
                    var endDirection=Vector3.ProjectOnPlane(last-f.points[f.points.Length-2],Vector3.up);
                    var location=new Vector3(x,0,z);
                    bool withinEnds=Vector2.Distance(new Vector2(first.x,first.z),new Vector2(last.x,last.z))<.01f || Vector3.Dot(location-new Vector3(first.x,0,first.z),startDirection)>=0 && Vector3.Dot(location-new Vector3(last.x,0,last.z),endDirection)<=0;
                    if(withinEnds&&distance<f.width*.5f+2.9f)result=Mathf.Min(result,height-DeckDepth-.1f);
                    continue;
                }
                if(f.category=="road"&&distance<f.width*.5f+2.9f)
                    result=Mathf.Min(result,height-.20f);
                if (f.category=="rail" && distance<f.width*.5f+2.9f)
                    result=Mathf.Min(result,height-.52f);
                if (f.category=="path" && distance<f.width*.5f+2.9f)
                    result=Mathf.Min(result,height-.16f);
                if (f.category=="water" && distance<f.width*.5f+2.5f)
                    {
                    float radius=Mathf.Max(.5f,f.width*.55f);
                    float depth=Mathf.Clamp(f.width*.13f,.8f,5)*Mathf.Sqrt(Mathf.Clamp01(1-distance/radius));
                    result=Mathf.Min(result,height-Mathf.Max(.15f,depth));
                    }
            }
            return LowerSeaBed(x,z,result);
        }

        // Merging carriageways may be separate OSM bridge ways on one deck.
        // Their internal barriers must not cross adjoining same-level asphalt.
        bool BridgeBarrierClear(Vector3 a,Vector3 b,Feature owner)
        {
            foreach(var point in Enumerable.Range(0,Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(a,b))+1)).Select(i=>Vector3.Lerp(a,b,(float)i/Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b))))))
            {
                var nearby=Nearby(point.x,point.z);if(nearby==null)continue;
                foreach(var segment in nearby)
                {
                    var other=segment.feature;
                    if(other.sourceId==owner.sourceId)continue;
                    if(other.category=="rail"&&!other.tunnel)
                    {
                        float d=Project(segment,point.x,point.z,out float railHeight);
                        float width=Mathf.Max(4.2f,other.railClearanceWidth);
                        if(d<width*.5f+.15f&&point.y+.75f>=railHeight-.45f&&point.y-.45f<railHeight+Mathf.Max(4.5f,other.railClearanceHeight))return false;
                        continue;
                    }
                    if(other.category!="road")continue;
                    float distance=Project(segment,point.x,point.z,out float height);
                    if((PathSharesRoadLevel(owner,other)||Mathf.Abs(height-point.y)<.75f) && distance<other.width*.5f+.6f)return false;
                }
            }
            return true;
        }

        void BridgeAbutments(Batch batch,Feature feature,float x,float z)
        {
            if(Vector3.Distance(feature.points[0],feature.points.Last())<.01f)return;
            foreach(int end in new[]{0,feature.points.Length-1})
            {
                var canonical=feature.points[end];
                var point=canonical+Join(feature.points,end)*feature.roadCentreOffset;
                if(point.x<x||point.x>=x+ChunkSize||point.z<z||point.z>=z+ChunkSize)continue;
                var nearby=Nearby(point.x,point.z);if(nearby==null)continue;
                bool approach=nearby.Any(segment=>segment.feature.category==feature.category&&!segment.feature.bridge&&!segment.feature.tunnel&&segment.feature.points.Any(other=>Vector3.Distance(canonical,other)<.1f));
                if(!approach)continue;
                var side=Join(feature.points,end).normalized;
                bool clear=true;
                foreach(float offset in new[]{-feature.width*.5f,0,feature.width*.5f})
                {
                    var sample=point+side*offset;
                    foreach(var segment in nearby)
                    {
                        if(segment.feature.category=="water")continue;
                        float distance=Project(segment,sample.x,sample.z,out float height);
                        if(Mathf.Abs(height-point.y)>1 && distance<segment.feature.width*.5f+1.5f)clear=false;
                    }
                }
                if(!clear)continue;
                var direction=Vector3.ProjectOnPlane(feature.points[end==0?1:end-1]-point,Vector3.up).normalized;
                float bottom=Mathf.Min(Ground(point.x,point.z)-point.y-.2f,-1.5f);
                batch.Box(point-direction*.6f,point+direction*.6f,side,side,feature.width*.5f+.35f,bottom,-DeckDepth);
            }
        }

        static bool SharedSourceNode(Feature first,Feature second)
        {
            if(first.sourceId==second.sourceId)return false;
            if(first.originalNodes==null)
                first.originalNodes=new HashSet<string>((first.sourceNodeIds??Array.Empty<string>()).Where(node=>node.StartsWith("osm:",StringComparison.Ordinal)));
            if(second.originalNodes==null)
                second.originalNodes=new HashSet<string>((second.sourceNodeIds??Array.Empty<string>()).Where(node=>node.StartsWith("osm:",StringComparison.Ordinal)));
            return first.originalNodes.Overlaps(second.originalNodes);
        }

        static bool SharedTunnel(Feature owner,Feature other)
        {
            return owner.tunnel && other.tunnel && other.category=="road" &&
                owner.layer==other.layer && (owner.level??"0")== (other.level??"0") && SharedSourceNode(owner,other);
        }

        bool TunnelWallClear(Feature owner,Vector3 a,Vector3 b)
        {
            foreach(var point in new[]{a,(a+b)*.5f,b})
            {
                var nearby=Nearby(point.x,point.z);
                if(nearby==null)continue;
                foreach(var segment in nearby)
                {
                    if(!SharedTunnel(owner,segment.feature))continue;
                    float distance=Project(segment,point.x,point.z,out float height);
                    if(distance<segment.feature.width*.5f+.2f && Mathf.Abs(height-point.y)<1.5f)return false;
                }
            }
            return true;
        }

        // A gameplay envelope inferred from portals; no surveyed bore shape.
        void TunnelLining(Batch batch,Feature owner,Vector3 a,Vector3 b,Vector3 sideA,Vector3 sideB,float halfWidth,float clearance,bool bored)
        {
            // The whole bore, including the verge between asphalt and lining,
            // needs a physical floor even under deep cover.
            float floorOffset=owner.category=="rail"?-.56f:owner.category=="path"?-.14f:-.08f;
            Vector3 Floor(Vector3 point)=> (owner.category=="road"?RoadSurfacePoint(owner,point):point)+Vector3.up*floorOffset;
            var fa=Floor(a-sideA*halfWidth);var fb=Floor(b-sideB*halfWidth);
            var fc=Floor(b+sideB*halfWidth);var fd=Floor(a+sideA*halfWidth);
            RoadTopTriangle(batch,fa,fb,fc);RoadTopTriangle(batch,fa,fc,fd);

            // A walking gallery or joining bore inside the same road tunnel
            // shares its vehicle-height ceiling. Its independent 2.7 m path
            // roof must not hang across the carriageway at their source join.
            foreach(float along in new[]{0f,.5f,1f})
                foreach(float across in new[]{-1f,0f,1f})
                {
                    var floor=Vector3.Lerp(a,b,along)+Vector3.Lerp(sideA,sideB,along)*halfWidth*across;
                    var nearby=Nearby(floor.x,floor.z);
                    if(nearby==null)continue;
                    foreach(var segment in nearby)
                    {
                        if(!SharedTunnel(owner,segment.feature))continue;
                        float distance=Project(segment,floor.x,floor.z,out float height);
                        if(distance<segment.feature.width*.5f+.8f && Mathf.Abs(height-floor.y)<1.5f)
                            clearance=Mathf.Max(clearance,height+segment.feature.tunnelClearance+.02f-floor.y);
                    }
                }
            // The arch is optional visual depth. A shallow underpass must fit
            // beneath overlapping surface roads and higher tunnel layers,
            // including their edges. Underground crossings are still crossings.
            float archRise=bored&&owner.tunnelKind!="avalanche_protector"?1.2f:0;
            foreach(float along in new[]{0f,.5f,1f})
                foreach(float across in new[]{-1f,0f,1f})
                {
                    var floor=Vector3.Lerp(a,b,along)+Vector3.Lerp(sideA,sideB,along)*halfWidth*across;
                    var nearby=Nearby(floor.x,floor.z);if(nearby==null)continue;
                    foreach(var segment in nearby)
                    {
                        var upper=segment.feature;if(upper==owner||upper.category!="road"||(upper.tunnel&&upper.layer<=owner.layer))continue;
                        float distance=Project(segment,floor.x,floor.z,out float height);
                        if(distance<upper.width*.5f+.8f && height>floor.y+.75f)
                            archRise=Mathf.Min(archRise,Mathf.Max(0,height-.35f-floor.y-clearance));
                    }
                }
            Vector2 previous=new Vector2(-halfWidth,-.15f);
            for(int section=0;section<=13;section++)
            {
                Vector2 next;
                if(section==0)next=new Vector2(-halfWidth,clearance);
                else if(section==13)next=new Vector2(halfWidth,-.15f);
                else
                {
                    float angle=Mathf.PI*(1-(section-1)/11f);
                    next=new Vector2(halfWidth*Mathf.Cos(angle),clearance+archRise*Mathf.Sin(angle));
                }
                var p=a+sideA*previous.x+Vector3.up*previous.y;
                var q=b+sideB*previous.x+Vector3.up*previous.y;
                var r=b+sideB*next.x+Vector3.up*next.y;
                var t=a+sideA*next.x+Vector3.up*next.y;
                bool wallClear=section!=0&&section!=13 || TunnelWallClear(owner,
                    a+sideA*(section==0?-halfWidth:halfWidth),b+sideB*(section==0?-halfWidth:halfWidth));
                if(wallClear && !(section==0&&owner.galleryOpenLeft)&&!(section==13&&owner.galleryOpenRight))batch.Quad(t,r,q,p);
                previous=next;
            }
        }

        // A heightfield cannot represent the vertical hole at a portal. Omit
        // shallow-cover cells inside the bore rather than building a cross-wall.
        bool TunnelPortalOpening(float x,float z) => TryPortalFloor(x,z,out _);

        bool TryPortalFloor(float x,float z,out float floor)
        {
            floor=float.PositiveInfinity;
            var nearby=Nearby(x,z);if(nearby==null)return false;
            float ground=Ground(x,z);
            foreach(var segment in nearby)
            {
                var feature=segment.feature;if(!feature.tunnel||feature.category=="water")continue;
                float distance=Project(segment,x,z,out float height);
                if(distance<feature.width*.5f+2.9f && ground<height+feature.tunnelClearance+1.5f)
                    floor=Mathf.Min(floor,height-(feature.category=="rail"?.75f:feature.category=="path"?.32f:.24f));
            }
            return floor<float.PositiveInfinity;
        }

        readonly Dictionary<Vector2Int,float> portalCellFloors=new Dictionary<Vector2Int,float>();
        float PortalCellFloor(float x,float z)
        {
            var key=new Vector2Int(Mathf.RoundToInt(x/TerrainStep),Mathf.RoundToInt(z/TerrainStep));
            if(portalCellFloors.TryGetValue(key,out float floor))return floor;
            floor=float.PositiveInfinity;
            void Probe(float px,float pz){if(TryPortalFloor(px,pz,out float bed))floor=Mathf.Min(floor,bed);}
            Probe(x,z);Probe(x+TerrainStep,z);Probe(x,z+TerrainStep);Probe(x+TerrainStep,z+TerrainStep);Probe(x+TerrainStep*.5f,z+TerrainStep*.5f);
            portalCellFloors.Add(key,floor);return floor;
        }
        bool InsideBore(Vector3 point)
        {
            var nearby=Nearby(point.x,point.z);if(nearby==null)return false;
            foreach(var segment in nearby)
            {
                var f=segment.feature;if(!f.tunnel||f.category=="water")continue;
                if(Project(segment,point.x,point.z,out _)>f.width*.5f+.9f)continue;
                var first=f.points[0];var last=f.points[f.points.Length-1];var at=new Vector3(point.x,0,point.z);
                var begin=Vector3.ProjectOnPlane(f.points[1]-first,Vector3.up);var end=Vector3.ProjectOnPlane(last-f.points[f.points.Length-2],Vector3.up);
                if(Vector3.Dot(at-new Vector3(first.x,0,first.z),begin)>=0 && Vector3.Dot(at-new Vector3(last.x,0,last.z),end)<=0)return true;
            }
            return false;
        }
        // Excavated cells have a bottom and closed outer banks. Interior bore
        // edges stay open so the original portal clearance is preserved.
        void TerrainCell(Batch terrain,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            float floor=PortalCellFloor(a.x,a.z);
            if(float.IsPositiveInfinity(floor)){terrain.Quad(a,b,c,d);return;}
            var oa=a;var ob=b;var oc=c;var od=d;
            a.y=Mathf.Min(a.y,floor);b.y=Mathf.Min(b.y,floor);c.y=Mathf.Min(c.y,floor);d.y=Mathf.Min(d.y,floor);
            terrain.Quad(a,b,c,d);
            void Bank(Vector3 p,Vector3 q,Vector3 op,Vector3 oq,float nx,float nz)
            {
                float adjacent=PortalCellFloor(nx,nz);op.y=Mathf.Min(op.y,adjacent);oq.y=Mathf.Min(oq.y,adjacent);
                if(Mathf.Max(op.y-p.y,oq.y-q.y)<.001f)return;
                for(int i=0;i<4;i++)
                {
                    float lo=i*.25f,hi=(i+1)*.25f;var first=Vector3.Lerp(p,q,lo);var last=Vector3.Lerp(p,q,hi);
                    if(InsideBore((first+last)*.5f))continue;
                    var topFirst=Vector3.Lerp(op,oq,lo);var topLast=Vector3.Lerp(op,oq,hi);
                    terrain.Quad(first,last,topLast,topFirst);terrain.Quad(topFirst,topLast,last,first);
                }
            }
            Bank(a,b,oa,ob,oa.x-TerrainStep,oa.z);Bank(b,c,ob,oc,oa.x,oa.z+TerrainStep);
            Bank(c,d,oc,od,oa.x+TerrainStep,oa.z);Bank(d,a,od,oa,oa.x,oa.z-TerrainStep);
        }

        Material Material(string name,Color colour,float smoothness=0)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material=EarthUnscripted.GameGraphics.Create(shader, _euMaterial => { _euMaterial.name=name; _euMaterial.color=colour; });
            material.SetFloat("_Smoothness",smoothness);
            material.SetFloat("_Cull",0);
            return material;
        }

        void MakeMaterials()
        {
            grass=Material("Cuttings and ground",new Color(.30f,.40f,.22f));
            asphalt=Material("Asphalt",new Color(.21f,.23f,.24f));
            gravel=Material("Ballast and shoulders",new Color(.42f,.40f,.35f));
            steel=Material("Steel rail",new Color(.50f,.52f,.51f),.65f);
            sleeper=Material("Sleepers",new Color(.29f,.26f,.22f));
            concrete=Material("Bridge concrete",new Color(.56f,.55f,.50f));
            water=Material("River Ebbw",new Color(.18f,.39f,.43f),.8f);
            path=Material("Footpath",new Color(.62f,.55f,.43f));
            paint=Material("Road markings",new Color(.88f,.88f,.81f));
            wall=Material("Mapped building massing",new Color(.66f,.60f,.50f));
            roof=Material("Roofs",new Color(.27f,.29f,.29f));
            redSignal=Material("Closed lane signal",new Color(1,.02f,.01f));
            greenSignal=Material("Open lane signal",new Color(.02f,1,.08f));
        }

        void MakeLighting()
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.70f,.78f,.85f);
            RenderSettings.ambientEquatorColor=new Color(.54f,.58f,.58f);
            RenderSettings.ambientGroundColor=new Color(.28f,.30f,.25f);
            RenderSettings.fog=true;
            RenderSettings.fogColor=new Color(.66f,.78f,.87f);
            RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogDensity=.00042f;
            var sun=new GameObject("Afternoon sun").AddComponent<Light>();
            sun.transform.SetParent(transform,false);
            sun.type=LightType.Directional; sun.intensity=2.0f; sun.shadows=LightShadows.Soft;
            sun.transform.rotation=Quaternion.Euler(43,-35,0);
            QualitySettings.shadowDistance=180;
        }

        sealed class Batch
        {
            readonly List<Vector3> vertices=new List<Vector3>();
            readonly List<int> triangles=new List<int>();
            readonly List<PavementTriangleRef> terrainRoads=new List<PavementTriangleRef>();
            public IReadOnlyList<PavementTriangleRef> TerrainRoads=>terrainRoads;
            public void MarkTerrainRoad(int firstVertex,bool ground)=>terrainRoads.Add(new PavementTriangleRef(firstVertex,ground));
            public void ApplyTerrainPavement(TerrainPavementResult result){vertices.AddRange(result.addedVertices);triangles.Clear();triangles.AddRange(result.indices);restoredNormals=null;}
            readonly Material material;
            public readonly string name;
            readonly bool collision;
            public Task Baking {get;private set;}
            Mesh bakedMesh, publishedMesh;MeshCollider bakedCollider;
            Vector3[] restoredNormals;
            public void Restore(FinishedGeometryStore.Part part) { vertices.AddRange(part.vertices); triangles.AddRange(part.indices); restoredNormals = part.normals; }
            public FinishedGeometryStore.Part Capture() => publishedMesh ? new FinishedGeometryStore.Part { vertices = publishedMesh.vertices, normals = publishedMesh.normals, indices = publishedMesh.triangles } : new FinishedGeometryStore.Part { vertices = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>(), indices = Array.Empty<int>() };
            public void Reset(){vertices.Clear();triangles.Clear();terrainRoads.Clear();Baking=null;bakedMesh=null;bakedCollider=null;publishedMesh=null;restoredNormals=null;}
            public void FinishCollider(){Baking?.GetAwaiter().GetResult();if(bakedCollider)bakedCollider.sharedMesh=bakedMesh;}
            public int VertexCount=>vertices.Count;
            public IReadOnlyList<Vector3> Vertices=>vertices;
            public IReadOnlyList<int> Indices=>triangles;
            public Batch(string name,Material material,bool collision) { this.name=name;this.material=material;this.collision=collision; }
            public void Append(Batch other)
            {int offset=vertices.Count;vertices.AddRange(other.vertices);foreach(int index in other.triangles)triangles.Add(offset+index);foreach(var reference in other.terrainRoads)terrainRoads.Add(new PavementTriangleRef(offset+reference.vertex,reference.ground));}
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int start=vertices.Count;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
                triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);
            }
            public void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            }
            public void Strip(Vector3 a,Vector3 b,Vector3 sideA,Vector3 sideB,float half,float offset=0)
            {
                a.y+=offset;b.y+=offset;
                Quad(a-sideA*half,b-sideB*half,b+sideB*half,a+sideA*half);
            }
            public void Box(Vector3 a,Vector3 b,Vector3 sideA,Vector3 sideB,float half,float bottom,float top)
            {
                var al=a-sideA*half;var ar=a+sideA*half;var bl=b-sideB*half;var br=b+sideB*half;
                var up=Vector3.up*top;var down=Vector3.up*bottom;
                Quad(al+up,bl+up,br+up,ar+up);
                Quad(al+down,ar+down,br+down,bl+down);
                Quad(al+down,bl+down,bl+up,al+up);
                Quad(ar+down,ar+up,br+up,br+down);
                Quad(al+down,al+up,ar+up,ar+down);
                Quad(bl+down,br+down,br+up,bl+up);
            }
            public double Publish(Transform root,ChunkTiming timing)
            {
                if (vertices.Count==0) return 0;
                var mesh=new Mesh { name=name,indexFormat=IndexFormat.UInt32 };
                mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);if(restoredNormals!=null)mesh.normals=restoredNormals;else mesh.RecalculateNormals();mesh.RecalculateBounds();publishedMesh=mesh;
                var obj=new GameObject(name);obj.transform.SetParent(root,false);
                if(collision)EarthUnscripted.VehicleObstacleWheels.MarkSuspensionSurface(obj,name);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                obj.AddComponent<EarthUnscripted.GeneratedMeshOwner>().Mesh=mesh;
                obj.AddComponent<MeshRenderer>().sharedMaterial=material;
                double cost=0;
                if (collision)
                {
                    var watch=Stopwatch.StartNew();bakedCollider=obj.AddComponent<MeshCollider>();bakedMesh=mesh;
                    var meshId=mesh.GetEntityId();var options=bakedCollider.cookingOptions;
                    // Bake only this immutable mesh on a worker. Component
                    // access/publication stays on the main thread. The exact
                    // cooking flags match the eventual collider cache lookup.
                    Baking=Task.Run(()=>Physics.BakeMesh(meshId,false,options));
                    cost=watch.Elapsed.TotalMilliseconds;
                }
                timing.vertices+=vertices.Count;timing.triangles+=triangles.Count/3;
                return cost;
            }
        }

        static Vector3 Side(Vector3 a,Vector3 b)
        {
            var delta=b-a;delta.y=0;
            // Original vertices can sit millimetres from a 2 m resample station.
            // Such short valid segments still define direction; a centimetre
            // cutoff rotated the final Bridge Street barrier into the lane.
            return delta.sqrMagnitude<1e-12f?Vector3.zero:new Vector3(delta.z,0,-delta.x).normalized;
        }
        static Vector3 Join(Vector3[] points,int i)
        {
            if (i==0) return Side(points[0],points[1]);
            if (i==points.Length-1) return Side(points[i-1],points[i]);
            var previous=Side(points[i-1],points[i]);var next=Side(points[i],points[i+1]);
            if(previous==Vector3.zero)return next;
            if(next==Vector3.zero)return previous;
            var sum=previous+next;
            if (sum.sqrMagnitude<.01f) return next;
            var normal=sum.normalized;
            return normal/Mathf.Max(.7f,Vector3.Dot(normal,next));
        }
        static bool Clip(float p,float q,ref float lo,ref float hi)
        {
            if (Mathf.Abs(p)<.000001f) return q>=0;
            float r=q/p;
            if (p<0) { if(r>hi)return false;lo=Mathf.Max(lo,r); }
            else { if(r<lo)return false;hi=Mathf.Min(hi,r); }
            return true;
        }
        static bool Range(Vector3 a,Vector3 b,float x,float z,out float lo,out float hi)
        {
            lo=0;hi=1;
            if (Mathf.Abs(a.x-b.x)<.000001f && (a.x<x || a.x>=x+ChunkSize)) return false;
            if (Mathf.Abs(a.z-b.z)<.000001f && (a.z<z || a.z>=z+ChunkSize)) return false;
            return Clip(a.x-b.x,a.x-x,ref lo,ref hi)&&Clip(b.x-a.x,x+ChunkSize-a.x,ref lo,ref hi)&&Clip(a.z-b.z,a.z-z,ref lo,ref hi)&&Clip(b.z-a.z,z+ChunkSize-a.z,ref lo,ref hi)&&hi-lo>.00001f;
        }

        readonly Dictionary<string,Batch> reusableBatches=new Dictionary<string,Batch>();
        readonly List<Task> pendingColliderBakes=new List<Task>();
        Batch ChunkBatch(string name,Material material,bool collision)
        {
            if(!reusableBatches.TryGetValue(name,out var batch))reusableBatches[name]=batch=new Batch(name,material,collision);
            batch.Reset();return batch;
        }

        IEnumerator BuildChunk(Vector2Int key)
        {
            BeginInitialGeometrySlice();
            float x=key.x*ChunkSize,z=key.y*ChunkSize;
            workingMap = AreaAt(x+ChunkSize*.5f,z+ChunkSize*.5f);
            if (workingMap == null) yield break;
            var meshFeatures = spatialByMap[workingMap].features;
            var meshBuildings = spatialByMap[workingMap].buildings;
            float observerStation=ClosestStation(observer,out _);
            var chunkCentre=new Vector3(x+ChunkSize*.5f,observer.y,z+ChunkSize*.5f);
            float chunkStation=ClosestStation(chunkCentre,out _);
            var timing=new ChunkTiming { x=key.x,z=key.y,requestedDistanceMetres=Vector3.Distance(chunkCentre,observer),requestedAheadMetres=chunkStation-observerStation };
            meshFeatures.TryGetValue(key,out var featureSet);
            IEnumerable<Feature> chunkFeatures=featureSet!=null?featureSet.OrderBy(f=>f.id,StringComparer.Ordinal):Array.Empty<Feature>();
            var elapsed=Stopwatch.StartNew();var cpu=Stopwatch.StartNew();double cpuMs=0;
            var root=new GameObject("Generated chunk "+key);root.SetActive(false);
            root.transform.SetParent(transform,false);ownedRoots.Add(root);
            portalCellFloors.Clear();pathGroundSamples.Clear();
            var terrain=ChunkBatch("Terrain",grass,true);
            var roads=ChunkBatch("Road asphalt",asphalt,true);
            var shoulders=ChunkBatch("Road shoulders",gravel,true);
            var paths=ChunkBatch("Footpaths",path,true);
            var raisedPaths=ChunkBatch("Footbridges / raised paths",path,true);
            var ballast=ChunkBatch("Railway ballast",gravel,true);
            var sleepers=ChunkBatch("Railway sleepers",sleeper,false);
            var rails=ChunkBatch("Railway rails",steel,false);
            var structures=ChunkBatch("Bridge decks / barriers",concrete,true);
            var tunnels=ChunkBatch("Tunnel lining",concrete,true);
            var rivers=ChunkBatch("River",water,false);
            var markings=ChunkBatch("Road markings",paint,false);
            var houses=ChunkBatch("Mapped building massing",wall,true);
            var roofs=ChunkBatch("Mapped roofs",roof,true);
            var closedSignals=ChunkBatch("Closed variable lane signals",redSignal,false);
            var openSignals=ChunkBatch("Open lane signals",greenSignal,false);
            var batches=new[]{terrain,roads,shoulders,paths,ballast,sleepers,rails,structures,tunnels,rivers,markings,houses,roofs,closedSignals,openSignals,raisedPaths};
            string finishedKey = FinishedChunkKey(key);
            FinishedGeometryStore.Part[] restored = null;
            if (finishedKey != null)
            {
                cpu.Stop();
                var read = finishedStore.Read(finishedKey);
                while (!read.IsCompleted) yield return null;
                restored = read.GetAwaiter().GetResult();
                cpu.Start();
                if (restored != null && restored.Length == batches.Length)
                {
                    for (int part = 0; part < batches.Length; part++) batches[part].Restore(restored[part]);
                    report.diskGeometryHits++;
                }
                else { restored = null; report.diskGeometryMisses++; }
            }
            List<Building> deferredBuildings=null;
            if (restored == null)
            {
            foreach(var row in JunctionPavingRows(roads,paths,x,z))
                if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds)){RecordGenerationSlice("Junction paving",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();}
            int columns=Mathf.RoundToInt(ChunkSize/TerrainStep)+1;
            var previousRow=new float[columns];var nextRow=new float[columns];
            for(int column=0;column<columns;column++)
            {
                previousRow[column]=Ground(x+column*TerrainStep,z);
                if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds)){RecordGenerationSlice("Terrain height sample",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();}
            }
            for (float dz=0;dz<ChunkSize;dz+=TerrainStep)
            {
                for(int column=0;column<columns;column++)
                {
                    nextRow[column]=Ground(x+column*TerrainStep,z+dz+TerrainStep);
                    if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds)){RecordGenerationSlice("Terrain height sample",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();}
                }
                for (int column=0;column<columns-1;column++)
                {
                    float a=x+column*TerrainStep,b=z+dz;
                    TerrainCell(terrain,new Vector3(a,previousRow[column],b),new Vector3(a,nextRow[column],b+TerrainStep),new Vector3(a+TerrainStep,nextRow[column+1],b+TerrainStep),new Vector3(a+TerrainStep,previousRow[column+1],b));
                    report.terrainSamples+=4;
                    if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds)){RecordGenerationSlice("Terrain cell",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();}
                }
                var swap=previousRow;previousRow=nextRow;nextRow=swap;
                if (GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds)) { RecordGenerationSlice("Terrain/feature row",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart(); }
            }
            foreach (var f in chunkFeatures)
            {
                if(f.bridge&&f.category=="road")BridgeAbutments(structures,f,x,z);
                float station=0;
                for (int i=1;i<f.points.Length;i++)
                {
                    var originalA=f.points[i-1];var originalB=f.points[i];
                    float length=Vector2.Distance(new Vector2(originalA.x,originalA.z),new Vector2(originalB.x,originalB.z));
                    if (!Range(originalA,originalB,x,z,out float lo,out float hi)) { station+=length;continue; }
                    var a=Vector3.Lerp(originalA,originalB,lo);var b=Vector3.Lerp(originalA,originalB,hi);
                    var sideA=Vector3.Lerp(Join(f.points,i-1),Join(f.points,i),lo);var sideB=Vector3.Lerp(Join(f.points,i-1),Join(f.points,i),hi);
                    if (f.category=="road")
                    {
                        foreach(var row in RoadSurfaceRows(roads,shoulders,markings,f,a,b,sideA,sideB,station+lo*length))
                            if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds))
                            {RecordGenerationSlice("Detailed road row",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();}
                        if(f.laneControl)LaneGantry(structures,closedSignals,openSignals,f,a,b,sideA,sideB,station+lo*length,station+hi*length);
                        a+=sideA*f.roadCentreOffset;b+=sideB*f.roadCentreOffset;
                    }
                    else if (f.category=="path")
                    {
                        if(f.bridge){a=BridgeSurfacePoint(f,a);b=BridgeSurfacePoint(f,b);}
                        FootpathStrip(IsGroundPath(f)?paths:raisedPaths,f,a,b,sideA,sideB,f.width*.5f);
                    }
                    else if (f.category=="water") rivers.Strip(a,b,sideA,sideB,f.width*.5f);
                    else if (f.category=="rail")
                    {
                        ballast.Box(a,b,sideA,sideB,f.width*.5f,-.55f,-RailTopToBallast);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            var ra=a+sideA*.7175f*sign;var rb=b+sideB*.7175f*sign;
                            rails.Box(ra,rb,sideA,sideB,.035f,-.06f,0);
                            rails.Box(ra,rb,sideA,sideB,.012f,-.17f,-.06f);
                        }
                        float begin=station+lo*length,end=station+hi*length;
                        for(float s=Mathf.Ceil(begin/.65f)*.65f;s<end;s+=.65f)
                        {
                            float t=(s-station)/Mathf.Max(.0001f,length);
                            var centre=Vector3.Lerp(originalA,originalB,t);var side=Vector3.Lerp(Join(f.points,i-1),Join(f.points,i),t).normalized;
                            var forward=new Vector3(-side.z,0,side.x);
                            sleepers.Box(centre-forward*.12f,centre+forward*.12f,side,side,1.24f,-.30f,-.17f);
                        }
                    }
                    if(f.tunnel&&f.category!="water")TunnelLining(tunnels,f,a,b,sideA,sideB,f.width*.5f+.8f,f.tunnelClearance,Vector3.Distance(f.points[0],f.points.Last())>=80);
                    if (f.bridge)
                    {
                        float deckTop=f.category=="rail"?-.55f:f.category=="path"?-.12f:-.025f;
                        BridgeDeckBox(structures,f,a,b,sideA,sideB,f.width*.5f+.12f,-DeckDepth,deckTop);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            var ba=BridgeSurfacePoint(f,a+sideA*(f.width*.5f+.16f)*sign);var bb=BridgeSurfacePoint(f,b+sideB*(f.width*.5f+.16f)*sign);
                            if(BridgeBarrierClear(ba,bb,f))structures.Box(ba,bb,sideA,sideB,.10f,.05f,.75f);
                        }
                        // Supports use whole mapped corridors as exclusion zones.
                        if(Mathf.FloorToInt((station+lo*length)/30)!=Mathf.FloorToInt((station+hi*length)/30))
                        {
                            var centre=(a+b)*.5f;bool allowed=true;var nearby=Nearby(centre.x,centre.z);
                            if(nearby!=null) foreach(var s in nearby)
                                if(s.feature!=f && Project(s,centre.x,centre.z,out _)<s.feature.width*.5f+3) { allowed=false;break; }
                            float ground=Ground(centre.x,centre.z);
                            if(allowed && centre.y-ground>2)
                            {
                                var forward=(b-a).normalized;
                                structures.Box(centre-forward*.7f,centre+forward*.7f,sideA.normalized,sideB.normalized,1.0f,ground-centre.y-.3f,-DeckDepth);
                            }
                        }
                    }
                    station+=length;
                    if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds))
                    {
                        RecordGenerationSlice("Road/rail/path geometry",cpu.Elapsed.TotalMilliseconds);
                        cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();
                    }
                }
                if (GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds)) { RecordGenerationSlice("Terrain/feature row",cpu.Elapsed.TotalMilliseconds);cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart(); }
            }
            meshBuildings.TryGetValue(key,out var localBuildings);
            // During a drive, publish the complete supported transport surface
            // before optional building preparation. Boot retains its full-scene
            // contract; disk-restored full chunks already contain their buildings.
            if(ready && localBuildings!=null)
                deferredBuildings=localBuildings.Where(house=>house.points.Length>=4&&house.centre.x>=x&&house.centre.x<x+ChunkSize&&house.centre.z>=z&&house.centre.z<z+ChunkSize).ToList();
            else
            {
            foreach(var house in localBuildings??new List<Building>())
            {
                if(house.points.Length<4)continue;
                var centre=house.centre;
                if(centre.x<x || centre.x>=x+ChunkSize || centre.z<z || centre.z>=z+ChunkSize)continue;
                cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();
                yield return PrepareBuilding(house,houses,roofs);cpu.Restart();
                if(GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds))
                {
                    RecordGenerationSlice("Buildings",cpu.Elapsed.TotalMilliseconds);
                    cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();yield return null;cpu.Restart();
                }
            }
            }
            }
            cpuMs+=cpu.Elapsed.TotalMilliseconds;cpu.Stop();
            if(restored==null)yield return ClearTerrainPavement(terrain,roads);
            foreach(var batch in batches)
            {
                if(batch.VertexCount==0)continue;
                cpu.Restart();timing.colliderMilliseconds+=batch.Publish(root.transform,timing);cpuMs+=cpu.Elapsed.TotalMilliseconds;
                RecordGenerationSlice("Mesh/collider publication: "+batch.name,cpu.Elapsed.TotalMilliseconds);
                if(batch.Baking!=null)pendingColliderBakes.Add(batch.Baking);
                bool yieldNow=GeometrySliceExpired(cpu.Elapsed.TotalMilliseconds);
                cpu.Stop();if(yieldNow)yield return null;
            }
            // Keep the whole chunk unpublished until every collider is baked.
            // Never advertise coverage or retire its predecessor early.
            foreach(var bake in pendingColliderBakes)
            {while(!bake.IsCompleted)yield return null;bake.GetAwaiter().GetResult();}
            foreach(var batch in batches)
            {
                if(batch.Baking==null)continue;
                cpu.Restart();batch.FinishCollider();RecordGenerationSlice("Collider cache attach",cpu.Elapsed.TotalMilliseconds);cpu.Stop();
                if (ready || GeometrySliceExpired(0)) yield return null;
            }
            pendingColliderBakes.Clear();
            yield return CreateTrafficControls(root.transform,workingMap,key);
            cpu.Restart();root.SetActive(true);RecordGenerationSlice("Chunk activation",cpu.Elapsed.TotalMilliseconds);cpu.Stop();chunks[key]=root;sceneryGroundRevision++;fineTerrainVisibilityRevision++;
            geometryBytes[key]=EstimateGeometryBytes(root);
            if(deferredBuildings!=null&&deferredBuildings.Count>0)
                QueueChunkScenery(key,root,workingMap,deferredBuildings,finishedKey,
                    finishedKey==null?null:batches.Select(batch=>batch.Capture()).ToArray());
            else if (restored == null && finishedKey != null) finishedStore.Write(finishedKey, batches.Select(batch => batch.Capture()).ToArray());
            if(capturing || embeddedGeometryDiagnostics)
            {
                if(!root.transform.IsChildOf(transform))throw new InvalidOperationException("Diagnostic chunk must belong to its world renderer");
                cpu.Restart();
                var terrainCollider=root.GetComponentsInChildren<MeshCollider>().First(c=>c.name=="Terrain");
                var bridgeCollider=root.GetComponentsInChildren<MeshCollider>().FirstOrDefault(c=>c.name=="Bridge decks / barriers");
                if(bridgeCollider)
                    foreach(var feature in chunkFeatures.Where(f=>f.category=="road"&&f.bridge))
                        for(int i=1;i<feature.points.Length;i++)
                            foreach(float offset in new[]{-.98f,0,.98f})
                            {
                                var p=feature.points[i]+Join(feature.points,i).normalized*offset;
                                if(p.x<x||p.x>=x+ChunkSize||p.z<z||p.z>=z+ChunkSize)continue;
                                report.bridgeCorridorSamples++;
                                if(bridgeCollider.Raycast(new Ray(p+Vector3.up*1.5f,Vector3.down),out var hit,2)&&hit.point.y>p.y+.3f)
                                {report.blockedBridgeCorridorSamples++;RecordGeometryIssue(feature,"bridge corridor",p,hit.point.y-p.y,.3f,hit.collider.name);}
                            }
                foreach(var feature in chunkFeatures.Where(f=>f.tunnel&&f.category=="road"))
                    foreach(var point in feature.points)
                    {
                        if(point.x<x||point.x>=x+ChunkSize||point.z<z||point.z>=z+ChunkSize)continue;
                        report.tunnelColliderSamples++;
                        var hits=Physics.RaycastAll(point+Vector3.up*.5f,Vector3.up,feature.tunnelClearance+3,~(1<<8)).Where(hit=>hit.collider.transform.IsChildOf(transform)).ToArray();
                        float clearance=hits.Length>0?hits.Min(hit=>hit.distance)+.5f:999;
                        report.minimumTunnelClearanceMetres=Mathf.Min(report.minimumTunnelClearanceMetres,clearance);
                        if(clearance<feature.tunnelClearance-.05f)
                        {report.obstructedTunnelSamples++;RecordGeometryIssue(feature,"tunnel clearance",point,clearance,feature.tunnelClearance,hits.OrderBy(hit=>hit.distance).First().collider.name);}
                        report.tunnelTerrainSamples++;
                        if(terrainCollider.Raycast(new Ray(point+Vector3.up*(feature.tunnelClearance+1.5f),Vector3.down),out var terrainHit,feature.tunnelClearance+1.5f) && terrainHit.point.y>point.y+.2f && terrainHit.point.y<point.y+feature.tunnelClearance-.1f)
                        {report.obstructedTunnelTerrainSamples++;RecordGeometryIssue(feature,"terrain in tunnel",point,terrainHit.point.y-point.y,0,terrainHit.collider.name);}
                    }
                foreach(var feature in chunkFeatures.Where(f=>f.category=="road"))
                    for(int index=0;index<feature.points.Length;index++)
                        foreach(float offset in new[]{-.98f,0,.98f})
                        {
                            var point=feature.points[index]+Join(feature.points,index).normalized*offset;
                            if(point.x<x||point.x>=x+ChunkSize||point.z<z||point.z>=z+ChunkSize)continue;
                            report.roadCorridorSamples++;
                            var obstruction=Physics.RaycastAll(point+Vector3.up*1.75f,Vector3.down,1.5f,~(1<<8)).FirstOrDefault(hit=>hit.collider.transform.IsChildOf(transform)&&hit.collider.name!="Road asphalt");
                            if(obstruction.collider!=null)
                            {
                                report.blockedRoadCorridorSamples++;
                                RecordGeometryIssue(feature,"road vehicle corridor",point,obstruction.point.y-point.y,.25f,obstruction.collider.name);
                            }
                        }
                foreach(var feature in chunkFeatures.Where(f=>!f.tunnel&&(f.category=="road"||((f.category=="rail"||f.category=="path")&&!f.bridge))))
                    for(int i=1;i<feature.points.Length;i++)
                        foreach(float offset in new[]{-1f,0,1f})
                        {
                            bool rail=feature.category=="rail";
                            var p=feature.points[i]+Join(feature.points,i).normalized*offset*(rail?1.15f:feature.width*.35f);
                            if(p.x<x||p.x>=x+ChunkSize||p.z<z||p.z>=z+ChunkSize)continue;
                            if(terrainCollider.Raycast(new Ray(p+Vector3.up*20,Vector3.down),out var hit,60))
                            {
                                if(rail)
                                {
                                    report.railColliderSamples++;
                                    if(hit.point.y>p.y-.16f+.01f){report.buriedRailColliderSamples++;RecordGeometryIssue(feature,"rail burial",p,hit.point.y-p.y,-.15f,hit.collider.name);}
                                }
                                else if(feature.category=="path")
                                {
                                    report.pathColliderSamples++;
                                    if(hit.point.y>p.y+.01f){report.buriedPathColliderSamples++;RecordGeometryIssue(feature,"path burial",p,hit.point.y-p.y,.01f,hit.collider.name);}
                                }
                                else
                                {
                                    report.roadColliderSamples++;
                                    if(hit.point.y>p.y+.01f){report.buriedRoadColliderSamples++;RecordGeometryIssue(feature,"road burial",p,hit.point.y-p.y,.01f,hit.collider.name);}
                                }
                            }
                        }
                timing.diagnosticMilliseconds=cpu.Elapsed.TotalMilliseconds;cpu.Stop();
            }
            timing.cpuMilliseconds=cpuMs;timing.elapsedMilliseconds=elapsed.Elapsed.TotalMilliseconds;
            timing.readyAheadMetres=chunkStation-ClosestStation(observer,out _);
            timing.queueToReadyMilliseconds=requestedAt.TryGetValue(key,out double queued)?life.Elapsed.TotalMilliseconds-queued:timing.elapsedMilliseconds;
            requestedAt.Remove(key);
            if(embedded && report.chunks.Count>=8192)report.chunks.RemoveRange(0,1024);
            report.chunks.Add(timing);report.generatedChunks++;report.activeChunks=chunks.Count;
            report.maximumActiveChunks=Mathf.Max(report.maximumActiveChunks,chunks.Count);
        }

        IEnumerator EnsureAround(Vector3 position)
        {
            building=true;
            int revision=geometryRevision,routeRevision=plannedRouteRevision;
            var plannedHeading=embedded?embeddedHeading:car?car.transform.forward:Vector3.forward;
            lastReconcilePosition=position;
            var priorities=GeometryPriorities(position,plannedHeading);
            // Restore recent supported chunks first. They already own their
            // finished meshes/collision and need no source or cooking work.
            var restoring=Stopwatch.StartNew();
            foreach(var key in priorities.Keys.OrderBy(key=>priorities[key]))
                if(!chunks.ContainsKey(key) && RestoreGeometry(key) && restoring.Elapsed.TotalMilliseconds>SliceMilliseconds)
                {yield return null;restoring.Restart();}
            var needed=priorities.Keys.Where(key=>!chunks.ContainsKey(key)).OrderBy(key=>priorities[key]).ThenBy(key=>key.x).ThenBy(key=>key.y).ToArray();
            foreach(var key in needed)requestedAt[key]=life.Elapsed.TotalMilliseconds;
            report.pendingChunks=needed.Length;
            int completed=0;
            var pass=Stopwatch.StartNew();
            foreach(var key in needed)
            {
                yield return BuildChunk(key);
                report.pendingChunks--;
                completed++;
                // A long snapshot queue used to finish scenery around an old
                // position while the car reached missing roads elsewhere.
                // Replan between atomic chunks and let numeric data publish.
                var heading=embedded?embeddedHeading:car?car.transform.forward:plannedHeading;
                if(ready && (completed>=8 || pass.Elapsed.TotalSeconds>=1.5 ||
                    Vector3.ProjectOnPlane(observer-position,Vector3.up).sqrMagnitude>=ReconcileMovement*ReconcileMovement ||
                    Vector3.Dot(plannedHeading,heading)<.75f || geometryRevision!=revision || plannedRouteRevision!=routeRevision))break;
            }
            if(completed<needed.Length)
            {
                foreach(var key in needed)requestedAt.Remove(key);
                lastReconcilePosition=new Vector3(float.MaxValue,0,0);
            }
            // Complete the forecast driving corridor before optional buildings.
            // A terrain-only chunk cannot stand in for its missing asphalt.
            // Both geometry jobs remain serial owners of workingMap.
            bool urgentReady=true;
            // An extrapolated point beyond a road graph endpoint belongs to
            // data acquisition, not an asphalt job that could never complete.
            for(int i=0;i<immediateCorridorRequiredPoints;i++)
                if(immediateCorridorNeedsRoad?!HasDrivingSurfaceAt(immediateDrivingCorridor[i]):!HasGeometryAt(immediateDrivingCorridor[i])){urgentReady=false;break;}
            if(urgentReady&&PendingSceneryChunks>0)yield return CompleteNextScenery();
            // Retire against the current observer; never the stale pass origin.
            foreach(var key in chunks.Keys.ToArray())
            {
                var location=new Vector3((key.x+.5f)*ChunkSize,observer.y,(key.y+.5f)*ChunkSize);
                if(!priorities.ContainsKey(key)&&Vector3.Distance(location,observer)>RetireRadius)
                {
                    RetainGeometry(key,chunks[key]);chunks.Remove(key);sceneryGroundColliders.Remove(key);sceneryGroundRevision++;fineTerrainVisibilityRevision++;report.retiredChunks++;
                    // Retire one completed chunk per frame; bulk destruction
                    // otherwise piles mesh and collider release onto one frame.
                    yield return null;
                }
            }
            report.activeChunks=chunks.Count;
            building=false;
            // Leave a complete frame between passes so a completed numeric
            // index can publish before Update claims the geometry gate again.
            geometryResumeFrame=Time.frameCount+2;
        }

        void DestroyChunk(GameObject root)
        {
            if(!root)return;
            ForgetChunkScenery(root);
            foreach(var owner in root.GetComponentsInChildren<EarthUnscripted.GeneratedMeshOwner>(true))owner.Release();
            root.SetActive(false);Destroy(root);ownedRoots.Remove(root);
        }

        void MakeCar()
        {
            var root=new GameObject("Test driver — isolated, no career");root.layer=8;
            root.transform.SetParent(transform,false);
            var chassis=root.AddComponent<BoxCollider>();
            chassis.center=new Vector3(0,.86f,0);chassis.size=new Vector3(1.95f,.85f,4.15f);
            var visual=root.AddComponent<CarVisual>();visual.Build(new Color(.12f,.40f,.67f),true);
            car=root.AddComponent<VehicleController>();car.Visual=visual;car.ApplyTuning(new Core.OwnedCar());
            input=root.AddComponent<DriverInput>();input.Profile=new PlayerProfile { volume=0,rumble=0,wheelEnabled=false,wheelForceEnabled=false };
            car.Input=input;car.ParkingAutomaticEnabled=true;
            SpawnRoute(0);
        }

        void SpawnRoute(int route)
        {
            routeIndex=route;
            car.Body.isKinematic=false;
            var points=map.routes[route].points;
            int i=Mathf.Min(5,points.Length-2);
            guideSegment=i;
            var forward=(points[i+1]-points[i]).normalized;
            car.Recover(points[i],Quaternion.LookRotation(forward));
            car.TryInitializeSupportedRest(points[i],Quaternion.LookRotation(forward),out _,out _);
            observer=points[i];
        }

        void Update()
        {
            UpdateDistantBuildingVisibility();
            using var frameWork=EarthUnscripted.WorkTelemetry.Measure("frame.world-scheduler",null,8);
            if(!ready)return;
            if(embedded)
            {
                if(NeedsReconcile(observer))StartCoroutine(EnsureAround(observer));
                return;
            }
            if(measuringDrive&&report.frameMilliseconds.Count<200000)report.frameMilliseconds.Add(Time.unscaledDeltaTime*1000);
            if(capturing)return;
            var keyboard=Keyboard.current;
            if(keyboard?.fKey.wasPressedThisFrame==true&&!changingFlight)StartCoroutine(SetFlight(!fly));
            if(keyboard?.oKey.wasPressedThisFrame==true)overview=!overview;
            if(keyboard?.tabKey.wasPressedThisFrame==true)StartCoroutine(ChangeRoute((routeIndex+1)%map.routes.Length));
            if(keyboard?.rKey.wasPressedThisFrame==true)StartCoroutine(ChangeRoute(routeIndex));
            if(fly)
            {
                float speed=keyboard?.leftShiftKey.isPressed==true?100:35;
                float horizontal=(keyboard?.dKey.isPressed==true?1:0)-(keyboard?.aKey.isPressed==true?1:0);
                float forward=(keyboard?.wKey.isPressed==true?1:0)-(keyboard?.sKey.isPressed==true?1:0);
                float vertical=(keyboard?.eKey.isPressed==true?1:0)-(keyboard?.qKey.isPressed==true?1:0);
                observer+=(Vector3.right*horizontal+Vector3.forward*forward+Vector3.up*vertical)*speed*Time.unscaledDeltaTime;
                view.transform.position=observer+new Vector3(0,25,-35);view.transform.rotation=Quaternion.Euler(30,0,0);
            }
            else
            {
                observer=car.transform.position;
                var offset=overview?new Vector3(85,120,-100):-car.transform.forward*8+Vector3.up*3.6f;
                view.transform.position=Vector3.Lerp(view.transform.position,car.transform.position+offset,1-Mathf.Exp(-7*Time.unscaledDeltaTime));
                view.transform.LookAt(car.transform.position+Vector3.up*1.2f+car.transform.forward*5);
            }
            if(NeedsReconcile(observer))StartCoroutine(EnsureAround(observer));
        }

        IEnumerator ChangeRoute(int route)
        {
            if(changingRoute)yield break;
            changingRoute=true;
            while(building)yield return null;
            car.StartLocked=true;
            car.Body.isKinematic=true;
            var start=map.routes[route].points[Mathf.Min(5,map.routes[route].points.Length-2)];
            if(AreaAt(start.x,start.z)==null)
                yield return AcquireArea(new AreaRequest{x=start.x,z=start.z,start=start,headingX=1,headingZ=0,coverageOnly=true},false);
            observer=start;yield return RefreshRoadFrontier();
            yield return EnsureAround(start);
            SpawnRoute(route);car.StartLocked=fly;car.Body.isKinematic=fly;
            changingRoute=false;
        }

        IEnumerator SetFlight(bool enabled)
        {
            if(changingFlight)yield break;
            changingFlight=true;
            fly=enabled;car.StartLocked=true;car.Body.isKinematic=true;
            if(!enabled)
            {
                while(building)yield return null;
                yield return EnsureAround(car.transform.position);
                car.Body.isKinematic=false;car.StartLocked=false;car.Body.WakeUp();
            }
            changingFlight=false;
        }

        IEnumerator Capture(string name,Vector3 eye,Vector3 target)
        {
            yield return EnsureAround(target);
            view.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
            yield return null;yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            yield return null;
            File.WriteAllText(Path.Combine(output,"capture-progress.json"),JsonUtility.ToJson(report,true));
        }

        // Scripted acceptance driver only: cap lateral acceleration at 2.6 m/s²
        // and crest acceleration at 4 m/s², looking up to 90 m along the profile.
        // This sets ordinary VehicleController inputs; it never moves the body.
        float AllowedSpeedAt(float station, out string provenance)
        {
            var route=map.routes[0];
            int found=Array.BinarySearch(route.stations,Mathf.Clamp(station,0,route.stations.Last()));
            int point=found>=0?found:Mathf.Clamp(~found-1,0,route.stations.Length-1);
            provenance=route.speedLimitSources!=null && point<route.speedLimitSources.Length?route.speedLimitSources[point]:"unavailable";
            return route.speedLimits!=null && point<route.speedLimits.Length?route.speedLimits[point]:13.4112f;
        }

        float CurveSpeed(float station)
        {
            const float BrakingDeceleration=3f; // m/s², anticipation rather than emergency braking
            float target=AllowedSpeedAt(station,out _)-.05f;
            for(float ahead=0;ahead<=220;ahead+=10)
            {
                var a=RouteAt(station+ahead);var b=RouteAt(station+ahead+10);var c=RouteAt(station+ahead+20);
                var before=Vector3.ProjectOnPlane(b-a,Vector3.up);
                var after=Vector3.ProjectOnPlane(c-b,Vector3.up);
                float localLimit=AllowedSpeedAt(station+ahead,out _)-.05f;
                if(before.sqrMagnitude>1 && after.sqrMagnitude>1)
                {
                    float curvature=Vector3.Angle(before,after)*Mathf.Deg2Rad/10;
                    if(curvature>.0001f)localLimit=Mathf.Min(localLimit,Mathf.Sqrt(2.6f/curvature));
                    // Plan vertical curves over 30 m chords. Ten-metre
                    // differences exaggerated short raster/way-boundary noise.
                    var va=RouteAt(station+ahead-15);var vb=RouteAt(station+ahead+15);var vc=RouteAt(station+ahead+45);
                    float firstRun=Vector3.ProjectOnPlane(vb-va,Vector3.up).magnitude;
                    float secondRun=Vector3.ProjectOnPlane(vc-vb,Vector3.up).magnitude;
                    float crest=firstRun>5 && secondRun>5?Mathf.Max(0,(vb.y-va.y)/firstRun-(vc.y-vb.y)/secondRun)/30:0;
                    if(crest>.0001f)localLimit=Mathf.Min(localLimit,Mathf.Sqrt(4f/crest));
                }
                float permittedNow=Mathf.Sqrt(localLimit*localLimit+2*BrakingDeceleration*Mathf.Max(0,ahead-12));
                target=Mathf.Min(target,permittedNow);
            }
            return Mathf.Max(0,target);
        }

        bool RoadReadyAt(float station)
        {
            var point=RouteAt(station);
            return Physics.RaycastAll(point+Vector3.up*2,Vector3.down,4,~(1<<8)).Any(hit=>hit.collider.name=="Road asphalt");
        }

        // Chunk presence measures the continuous ready horizon; independent
        // downward collider probes verify actual asphalt at fixed look-aheads.
        // A probe beyond the destination is unavailable, not a streaming failure.
        void RecordFrontier(float seconds,float progress)
        {
            float remaining=map.routes[0].stations.Last()-progress;
            float horizon=Mathf.Min(ForwardGenerationDistance,remaining);
            float readyDistance=0;
            for(float distance=0;distance<=horizon;distance+=32)
            {
                if(!chunks.ContainsKey(ChunkKey(RouteAt(progress+distance))))break;
                readyDistance=Mathf.Min(horizon,distance+32);
            }
            report.frontier.Add(new FrontierSample
            {
                seconds=seconds,progressMetres=progress,remainingMetres=remaining,readyAheadMetres=readyDistance,
                speed=car.AbsoluteSpeed,activeChunks=chunks.Count,pendingChunks=report.pendingChunks,
                road128=remaining>=128&&RoadReadyAt(progress+128),
                road256=remaining>=256&&RoadReadyAt(progress+256),
                road512=remaining>=512&&RoadReadyAt(progress+512),
                road768=remaining>=768&&RoadReadyAt(progress+768)
            });
        }

        IEnumerator Drive(int route)
        {
            var path=map.routes[route];
            yield return EnsureAround(path.points[5]);
            SpawnRoute(route);car.StartLocked=true;
            yield return new WaitForSeconds(1);
            car.StartLocked=false;car.NetworkDriven=true;measuringDrive=true;
            car.NetworkThrottle=car.NetworkBrake=car.NetworkHandbrake=0;
            var result=new DriveResult { name=path.name };
            float elapsed=0,lastProgressTime=0,bestProgress=0,lastTrace=-1,lastSupport=0;
            float progress=path.stations[5];int captureKilometre=0;
            var timeout=Stopwatch.StartNew();
            string trace=Path.Combine(output,"drive.csv");
            File.WriteAllText(trace,"seconds,progress,x,y,z,speed,targetSpeed,damage,grounded,activeChunks,pendingChunks,lateralError\n");
            var last=car.transform.position;
            while(progress<path.stations.Last()-6&&elapsed<1000&&timeout.Elapsed.TotalSeconds<1200)
            {
                var position=car.transform.position;
                progress=ClosestStation(position,out int nearest,true);guideSegment=nearest;
                if(progress>bestProgress+1){bestProgress=progress;lastProgressTime=elapsed;}
                float speed=car.AbsoluteSpeed;
                float lookAhead=Mathf.Clamp(7+speed*.8f,8,28);
                var local=car.transform.InverseTransformPoint(RouteAt(progress+lookAhead));
                float desiredDegrees=Mathf.Atan2(2*car.Metrics.Wheelbase*local.x,Mathf.Max(1,local.x*local.x+local.z*local.z))*Mathf.Rad2Deg;
                // Same speed-dependent steering limit as the existing vehicle controller.
                float steeringLimit=Mathf.Lerp(30,10,Mathf.Clamp01(speed/48));
                car.NetworkSteer=Mathf.Clamp(desiredDegrees/steeringLimit,-1,1);
                float targetSpeed=CurveSpeed(progress);
                car.NetworkThrottle=speed<targetSpeed?Mathf.Clamp((targetSpeed-speed)*.35f,0,.8f):0;
                car.NetworkBrake=speed>targetSpeed+.5f?Mathf.Clamp((speed-targetSpeed)*.3f,0,.65f):0;
                float step=Vector3.Distance(position,last);result.distance+=step;last=position;
                result.maxDamage=Mathf.Max(result.maxDamage,car.Damage);
                result.maximumSpeed=Mathf.Max(result.maximumSpeed,speed);
                if(!car.Grounded)result.airborneSeconds+=Time.deltaTime;
                var roadPoint=RouteAt(progress);
                float lateral=Vector2.Distance(new Vector2(position.x,position.z),new Vector2(roadPoint.x,roadPoint.z));
                result.maxLateralError=Mathf.Max(result.maxLateralError,lateral);
                observer=position;
                view.transform.position=position-car.transform.forward*8+Vector3.up*3.8f;
                view.transform.LookAt(position+Vector3.up+car.transform.forward*10);
                if(NeedsReconcile(position))StartCoroutine(EnsureAround(position));
                if(elapsed-lastSupport>.5f)
                {
                    lastSupport=elapsed;
                    if(!RoadReadyAt(progress+12))result.missingRoadSamples++;
                }
                if(elapsed-lastTrace>=1)
                {
                    lastTrace=elapsed;RecordFrontier(elapsed,progress);
                    File.AppendAllText(trace,string.Join(",",new object[]{elapsed,progress,position.x,position.y,position.z,speed,targetSpeed,car.Damage,car.Grounded,chunks.Count,report.pendingChunks,lateral})+"\n");
                    File.WriteAllText(Path.Combine(output,"live.json"),JsonUtility.ToJson(report.frontier.Last(),true));
                }
                if(progress>=(captureKilometre+1)*1000)
                {
                    captureKilometre++;
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"route-"+captureKilometre+"km.png"));
                }
                if(elapsed-lastProgressTime>18){result.error="No physical guide progress for 18 seconds";break;}
                if(car.Damage>.1f){result.error="Physical driving collision: "+car.LastImpactObject;break;}
                if(lateral>10){result.error="Car left the mapped route corridor";break;}
                elapsed+=Time.deltaTime;
                yield return null;
            }
            result.completed=progress>=path.stations.Last()-6&&Vector3.Distance(car.transform.position,path.points.Last())<10;
            result.elapsedSeconds=elapsed;result.lastImpactObject=car.LastImpactObject??"";
            if(!result.completed&&string.IsNullOrEmpty(result.error))result.error="Route timeout";
            report.drives.Add(result);
            car.NetworkThrottle=0;car.NetworkBrake=1;car.StartLocked=true;car.Body.isKinematic=true;measuringDrive=false;
            while(building)yield return null;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,result.completed?"06-destination-drive.png":"failed-drive.png"));
            File.WriteAllText(Path.Combine(output,"runtime-progress.json"),JsonUtility.ToJson(report,true));
        }

        void OnGUI()
        {
            if(embedded || !IsPrototypePlayer)return;
            var style=new GUIStyle(GUI.skin.label) { fontSize=18,normal={textColor=Color.white},wordWrap=true };
            GUI.Box(new Rect(14,14,690,ready?164:90),GUIContent.none);
            GUI.Label(new Rect(28,22,660,30),worldTitle,style);
            GUI.Label(new Rect(28,51,660,98),ready?(fly?"Fly: WASD · Q/E height · Shift faster · F drive":"Drive: WASD / controller · R reset · Tab route · F fly · O overview")+"\n"+map.routes[routeIndex].name+" · "+chunks.Count+" chunks\n"+dataStatus:"Fetching and preparing the initial area…",style);
            if(ready && car!=null)
            {
                float limit=13.4112f,best=float.MaxValue;
                var nearby=Nearby(car.transform.position.x,car.transform.position.z);
                if(nearby!=null)foreach(var segment in nearby)
                {
                    if(segment.feature.category!="road")continue;
                    float distance=Project(segment,car.transform.position.x,car.transform.position.z,out float height)+Mathf.Abs(height-car.transform.position.y)*.5f;
                    if(distance<best){best=distance;limit=segment.feature.speedLimitMps;}
                }
                GUI.Label(new Rect(28,139,660,28),"Speed "+(car.AbsoluteSpeed/.44704f).ToString("F0")+" mph · limit "+(limit/.44704f).ToString("F0")+" mph",style);
            }
            if(ready)GUI.Label(new Rect(20,Screen.height-45,Screen.width-40,40),"Approximate scenery · "+sourceAttribution+" · Relief: Mapzen / Copernicus / EA / USGS",new GUIStyle(style){fontSize=13});
            if(GUI.Button(new Rect(Screen.width-145,18,125,30),"Quit test map"))Application.Quit();
        }

        void OnDestroy()
        {
            pumpRunning=false;
            // Complete outstanding reads before freeing their native mesh data.
            foreach(var bake in pendingColliderBakes)try{bake.GetAwaiter().GetResult();}catch(Exception error){Debug.LogException(error);}
            foreach(var bake in pendingCoastalBakes)try{bake.GetAwaiter().GetResult();}catch(Exception error){UnityEngine.Debug.LogWarning(error.Message);}
            pendingCoastalBakes.Clear();foreach(var preview in pendingCoastalPreviews)if(preview)Destroy(preview);pendingCoastalPreviews.Clear();
            pendingColliderBakes.Clear();reusableBatches.Clear();
            Application.logMessageReceived -= CaptureFailure;
            foreach(var root in ownedRoots.ToArray())DestroyChunk(root);
            foreach(var material in new[]{grass,asphalt,gravel,steel,sleeper,concrete,water,path,paint,wall,roof,redSignal,greenSignal})if(material)Destroy(material);
            if(mappedWaterMaterial)Destroy(mappedWaterMaterial);if(roadWaterMask)Destroy(roadWaterMask);
            if(coastalBeachMask)Destroy(coastalBeachMask);if(waterSubstrate)Destroy(waterSubstrate);
            foreach(var material in coastalMaterials)if(material)Destroy(material);coastalMaterials.Clear();
            ClearControlPlacements();areas.Clear();spatialByMap.Clear();
        }
    }
}
