using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    // All geography is local and deterministic. Individual 320m chunks own their meshes
    // and colliders; distant geometry is released rather than accumulating as you drive.
    public sealed partial class GeoWorld : MonoBehaviour
    {
        public BuildingEntryAudit EntryAudit {get;}=new BuildingEntryAudit();
        public GeoRegion Region; public Transform Observer,SecondaryObserver;
        public readonly List<Transform> RaceObservers=new List<Transform>();
        public Material Asphalt;
        readonly Dictionary<Vector2Int,GameObject> loaded=new Dictionary<Vector2Int,GameObject>();
        readonly Dictionary<Vector2Int,List<GeoRoad>> roads=new Dictionary<Vector2Int,List<GeoRoad>>();
        readonly Dictionary<Vector2Int,List<GeoRailway>> railways=new Dictionary<Vector2Int,List<GeoRailway>>();
        Dictionary<Vector2Int,List<GeoBuilding>> buildings=new Dictionary<Vector2Int,List<GeoBuilding>>();
        // Fixed baseline exclusions retain road-bank geometry when render windows change.
        readonly Dictionary<Vector2Int,List<GeoBuilding>> roadBankBuildings=new Dictionary<Vector2Int,List<GeoBuilding>>();
        readonly Dictionary<Vector2Int,List<GeoWater>> water=new Dictionary<Vector2Int,List<GeoWater>>();
        readonly Dictionary<Vector2Int,List<GeoTree>> mappedTrees=new Dictionary<Vector2Int,List<GeoTree>>();
        Dictionary<string,BuildingSitePlan.Data> buildingSites=new Dictionary<string,BuildingSitePlan.Data>();
        Dictionary<string,string> buildingSiteOwners=new Dictionary<string,string>();
        public IReadOnlyDictionary<string,BuildingSitePlan.Data> BuildingSites=>buildingSites;
        public BuildingRenderInput.Plan BuildingInputs {get;private set;}
        public int CosmeticFeatureFallbacks {get;private set;}
        // Prepared off-thread/pure CPU before Initialize; never solve in chunk rendering.
        FinalizedRoadSurfacePlan preparedRoadSurfaces,pendingRoadSurfaces;
        RoadBankExteriorClip.SpatialIndex legacyBankPavement;
        public string RoadBankTerrainDiagnostic {get;private set;} = "Original narrow road shoulders";
        public void InitializePrepared(GeoRegion region,Material asphalt,FinalizedRoadSurfacePlan surfaces,bool deferInitialCoverage=false)
        {
            if(surfaces==null||surfaces.Owner!=region||surfaces.WidthField!=region.PreparedRoadWidths)throw new System.IO.InvalidDataException("Prepared width surface/region identity mismatch");
            pendingRoadSurfaces=surfaces;try{Initialize(region,asphalt,deferInitialCoverage);}finally{pendingRoadSurfaces=null;}
        }
        IEnumerable<int> MappedRoadSteps(Transform root,Vector2Int key,GeoRoad road)
        {
            if(Region.IsEffectiveBridgeDeck(road)||preparedRoadSurfaces==null||!preparedRoadSurfaces.Parts.TryGetValue(road.id,out var part))
            {foreach(var step in RibbonSteps(root,key,Region.IsEffectiveBridgeDeck(road)?Region.CopyEffectiveBridgePoints(road):road.points,road.width,Asphalt,true,Region.IsEffectiveBridgeDeck(road)?"Bridge deck":"Mapped road",0,!Region.circuit&&preparedMarkings==null,RoadBankGeometryReach(road),road))yield return step;yield break;}
            // Bridge structures deliberately remain on the old, unchanged path.
            var data=part.Surface;int serial=0;foreach(var batch in RoadSurfaceChunkClip.Batches(data.Vertices,data.Triangles,key,Size))
            {if(batch!=null)Geometry.Mesh("Finalized mapped asphalt "+serial++,root,batch.Vertices,batch.Triangles,batch.UV,Asphalt,true);yield return 0;}
            var bankV=new List<Vector3>();var bankT=new List<int>();var bankU=new List<Vector2>();
            void FlushBanks(){if(bankT.Count>0)Geometry.Mesh("Finalized road embankments",root,bankV.ToArray(),bankT.ToArray(),bankU.ToArray(),Geometry.Surface("Verge gravel",new Color(.27f,.29f,.2f),.3f,2),true);bankV.Clear();bankT.Clear();bankU.Clear();}
            for(int row=1;row<data.Distances.Length;row++)
            {
                if(data.Distances[row]-data.Distances[row-1]<.000001)continue;
                foreach(int side in new[]{-1,1})
                {
                    int previous=(row-1)*(data.Columns+1),next=row*(data.Columns+1);var leftP=data.Vertices[previous];var rightP=data.Vertices[previous+data.Columns];var leftQ=data.Vertices[next];var rightQ=data.Vertices[next+data.Columns];var rp=(rightP-leftP)/data.Widths[row-1];var rq=(rightQ-leftQ)/data.Widths[row];rp.y=rq.y=0;
                    var edgeP=side<0?leftP:rightP;var edgeQ=side<0?leftQ:rightQ;var innerP=edgeP-rp*side*.03f;var innerQ=edgeQ-rq*side*.03f;innerP.y=Region.RoadSurfaceHeightForRoad(road,innerP.x,innerP.z)-.035f;innerQ.y=Region.RoadSurfaceHeightForRoad(road,innerQ.x,innerQ.z)-.035f;
                    var outerP=edgeP+rp*side*RoadBankGeometryReach(road);var outerQ=edgeQ+rq*side*RoadBankGeometryReach(road);outerP.y=MeshSurfaceHeight(outerP.x,outerP.z)+.012f;outerQ.y=MeshSurfaceHeight(outerQ.x,outerQ.z)+.012f;
                    for(int slice=0;slice<4;slice++)
                    {
                        float u=slice/4f,w=(slice+1)/4f;var a=Vector3.Lerp(innerP,outerP,u);var b=Vector3.Lerp(innerP,outerP,w);var c=Vector3.Lerp(innerQ,outerQ,u);var d=Vector3.Lerp(innerQ,outerQ,w);if(side<0){var swap=a;a=b;b=swap;swap=c;c=d;d=swap;}
                        foreach(var step in AppendBankExteriorSteps(bankV,bankT,bankU,a,b,c,d,key,road.layer,false,FlushBanks))yield return step;
                    }
                }
            }
            FlushBanks();yield return 0;
            // Endpoint caps are emitted once by the shared effective JunctionRadius loop.
            foreach(var corner in data.Corners)if(corner.Distance>.001&&corner.Distance<road.length-.001&&Key(corner.Centre)==key){JunctionCap(root,corner.Centre,corner.Radius);yield return 0;}
        }
        IEnumerable<int> AppendBankExteriorSteps(List<Vector3> vertices,List<int> triangles,List<Vector2> uv,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2Int key,int layer,bool bridge,System.Action flush,bool conformLegacy=false)
        {
            foreach(var tri in new[]{new[]{a,c,b},new[]{b,c,d}})
            {
                RoadBankExteriorClip.Data exterior=null;foreach(var step in RoadBankExteriorClip.ClipSteps(tri,layer,bridge,(preparedRoadSurfaces?.Pavement??legacyBankPavement),x=>exterior=x))yield return step;
                for(int i=0;i<exterior.Triangles.Length;i+=3)
                {
                    var a0=exterior.Vertices[exterior.Triangles[i]];var b0=exterior.Vertices[exterior.Triangles[i+1]];var c0=exterior.Vertices[exterior.Triangles[i+2]];
                    if(conformLegacy)
                    {
                        var exposed=retainingGaps!=null?retainingGaps.RemoveGapSoil(a0,b0,c0,key):new RoadBankExteriorClip.Data{Vertices=new[]{a0,b0,c0},Triangles=new[]{0,1,2}};
                        for(int exposedIndex=0;exposedIndex<exposed.Triangles.Length;exposedIndex+=3)
                        {
                            var soil=LegacyRetainingGapPlan.ConformExposedBank(exposed.Vertices[exposed.Triangles[exposedIndex]],exposed.Vertices[exposed.Triangles[exposedIndex+1]],exposed.Vertices[exposed.Triangles[exposedIndex+2]],LegacyTerrainMeshHeight,TerrainGridSpacing);
                            for(int j=0;j<soil.Triangles.Length;j+=3)
                            {
                                var polygon=RoadSurfaceChunkClip.Triangle(soil.Vertices[soil.Triangles[j]],soil.Vertices[soil.Triangles[j+1]],soil.Vertices[soil.Triangles[j+2]],key,Size);
                                if(polygon!=null&&vertices.Count+polygon.Count>8192){flush();yield return 0;}
                                RoadSurfaceChunkClip.Append(vertices,triangles,uv,polygon);
                            }
                        }
                    }
                    else
                    {
                        var polygon=RoadSurfaceChunkClip.Triangle(a0,b0,c0,key,Size);
                        if(polygon!=null&&vertices.Count+polygon.Count>8192){flush();yield return 0;}
                        RoadSurfaceChunkClip.Append(vertices,triangles,uv,polygon);
                    }
                }
                yield return 0;
            }
        }

        RoadMarkingChunkGeometry.Prepared preparedMarkings;
        public bool ContinuousMarkingsReady=>IsAutomaticWorld||preparedMarkings!=null;
        public int PreparedMarkingStrokes=>preparedMarkings?.StrokeCount??0;
        public long RejectedMarkingStrokes=>preparedMarkings?.RejectedStrokes??0;
        public string MarkingDiagnostic {get;private set;}
        public bool SimplifiedRoadMarkings=>!IsAutomaticWorld&&Region!=null&&!Region.circuit&&(preparedMarkings==null||RejectedMarkingStrokes>0);
        Material ground,paint,roof,waterMaterial,bark,leaves,kerbWhite,kerbRed,runoff,rail;
        [System.Serializable] public struct StreamingEvent
        {
            public int frame,renderedFrame,keyX,keyZ,pendingBuilds,pendingRetires,residents;public double realtimeSeconds;public string phase,reason;public float milliseconds;
        }
        [System.Serializable] public sealed class StreamingSnapshot
        {
            public bool baselineScheduling;public int generation,totalBuilds,prefetchBuilds,requiredBuilds,forceBuilds,baselineBuilds,totalRetired,forceRefreshes,requiredMisses,pendingBuilds,pendingRetires,maxResidentChunks,maxQueuedBuildsPerFrame,maxTotalBuildsPerFrame,dropped;
            public long terrainCacheHits,terrainCacheMisses,terrainCacheBypasses;public int terrainCacheGrids,terrainCachePeakGrids,terrainCacheEvictions;
            public float maxChunkMilliseconds,maxRefreshMilliseconds,maxRetireMilliseconds,maxSliceMilliseconds;public int stagedSlices,cancelledStages,urgentStageFinishes;
            public string notes="Preparatory work has a 4ms soft frame budget with row/sample checkpoints and one queued retirement. Mesh upload/cooking and individual objects are indivisible. Chunks activate only after completion. Forced/required coverage is synchronous and may exceed that count. Retire time measures scheduling; engine destruction can complete later.";
            public StreamingEvent[] events;
        }
        const int EventCapacity=4096;readonly StreamingEvent[] streamEvents=new StreamingEvent[EventCapacity];int eventCursor,eventCount,eventDropped;
        StreamingSnapshot streamStats=new StreamingSnapshot();int generation,buildFrame=int.MinValue,buildsThisFrame,prefetchThisFrame,pumpFrame=int.MinValue;
        readonly HashSet<RoadRush.Core.ChunkCell> loadedCells=new HashSet<RoadRush.Core.ChunkCell>();
        readonly List<RoadRush.Core.ChunkCell> currentOrigins=new List<RoadRush.Core.ChunkCell>(),previousOrigins=new List<RoadRush.Core.ChunkCell>();
        readonly Queue<RoadRush.Core.ChunkCell> pendingBuilds=new Queue<RoadRush.Core.ChunkCell>(),pendingRetires=new Queue<RoadRush.Core.ChunkCell>();RoadRush.Core.ChunkStreamingPlan streamPlan;
        sealed class ChunkStage
        {
            public Vector2Int key;public Transform root;public IEnumerator<int> work;public string phase="terrain",reason="prefetch";public float cpuMilliseconds;public bool complete;
        }
        RoadRush.Core.ExactTerrainMemo terrainMemo=new RoadRush.Core.ExactTerrainMemo();GeoRegion memoRegion;System.Func<float,float,float> terrainSampler;
        readonly List<RoadRush.Core.ChunkMotion> motion=new List<RoadRush.Core.ChunkMotion>();
        struct Observation{public Vector3 position;public double time;}
        readonly Dictionary<Transform,Observation> observations=new Dictionary<Transform,Observation>();
        readonly HashSet<RoadRush.Core.ChunkCell> driveFrontier=new HashSet<RoadRush.Core.ChunkCell>();
        int frontierGeneration,motionSignature,prioritySignature;double priorityAt;bool priorityDirty;double frontierTimeoutSeconds=90;
        public bool DriveFrontierReady {get;private set;}
        public string DriveFrontierStatus {get;private set;}="";
        ChunkStage stage;const float PreparationBudgetMilliseconds=4;
        public bool BaselineScheduling {get;private set;}
#if UNITY_EDITOR
        public static bool VerificationBaselineScheduling;
#endif
        static readonly Unity.Profiling.ProfilerMarker refreshMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Refresh"),buildMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.BuildChunk"),terrainMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Terrain"),roadsMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Roads"),waterMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Water"),buildingsMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Buildings"),circuitMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Circuit"),vegetationMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Vegetation"),retireMarker=new Unity.Profiling.ProfilerMarker("RoadRush.GeoWorld.Retire");
        static long Timestamp()=>System.Diagnostics.Stopwatch.GetTimestamp();
        static float Elapsed(long start)=>(float)((Timestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency);
        static RoadRush.Core.ChunkCell Cell(Vector2Int key)=>new RoadRush.Core.ChunkCell(key.x,key.y);
        static Vector2Int Key(RoadRush.Core.ChunkCell key)=>new Vector2Int(key.X,key.Z);
        public static bool BaselineProofAllowed(string[] args,string storage)
        {
            if(string.IsNullOrEmpty(storage)||System.Array.IndexOf(args,"-roadRushStreamingBaseline")<0)return false;
            int at=System.Array.IndexOf(args,"-roadRushAppearanceProof");
            try{return at>=0&&at+1<args.Length&&System.IO.Path.GetFullPath(storage)==System.IO.Path.GetFullPath(args[at+1]+"-profile.json");}catch{return false;}
        }
        void Record(Vector2Int key,string phase,string reason,float milliseconds)
        {
            streamEvents[eventCursor]=new StreamingEvent{frame=Time.frameCount,renderedFrame=Time.renderedFrameCount,keyX=key.x,keyZ=key.y,realtimeSeconds=Time.realtimeSinceStartupAsDouble,phase=phase,reason=reason,milliseconds=milliseconds,pendingBuilds=pendingBuilds.Count+(stage!=null?1:0),pendingRetires=pendingRetires.Count,residents=loaded.Count};
            eventCursor=(eventCursor+1)%EventCapacity;if(eventCount<EventCapacity)eventCount++;else eventDropped++;
        }
        public void ClearStreamingTelemetry()
        {terrainMemo.ResetStatistics();streamStats=new StreamingSnapshot{maxResidentChunks=loaded.Count};eventCursor=eventCount=eventDropped=0;buildFrame=int.MinValue;buildsThisFrame=prefetchThisFrame=0;}
        public StreamingSnapshot CaptureStreamingTelemetry()
        {
            var result=new StreamingSnapshot{terrainCacheHits=terrainMemo.Hits,terrainCacheMisses=terrainMemo.Misses,terrainCacheBypasses=terrainMemo.Bypasses,terrainCacheGrids=terrainMemo.TileCount,terrainCachePeakGrids=terrainMemo.PeakTiles,terrainCacheEvictions=terrainMemo.Evictions,baselineScheduling=BaselineScheduling,generation=generation,totalBuilds=streamStats.totalBuilds,prefetchBuilds=streamStats.prefetchBuilds,requiredBuilds=streamStats.requiredBuilds,forceBuilds=streamStats.forceBuilds,baselineBuilds=streamStats.baselineBuilds,totalRetired=streamStats.totalRetired,forceRefreshes=streamStats.forceRefreshes,requiredMisses=streamStats.requiredMisses,pendingBuilds=pendingBuilds.Count+(stage!=null?1:0),pendingRetires=pendingRetires.Count,maxResidentChunks=streamStats.maxResidentChunks,maxQueuedBuildsPerFrame=streamStats.maxQueuedBuildsPerFrame,maxTotalBuildsPerFrame=streamStats.maxTotalBuildsPerFrame,maxChunkMilliseconds=streamStats.maxChunkMilliseconds,maxRefreshMilliseconds=streamStats.maxRefreshMilliseconds,maxRetireMilliseconds=streamStats.maxRetireMilliseconds,maxSliceMilliseconds=streamStats.maxSliceMilliseconds,stagedSlices=streamStats.stagedSlices,cancelledStages=streamStats.cancelledStages,urgentStageFinishes=streamStats.urgentStageFinishes,dropped=eventDropped,events=new StreamingEvent[eventCount]};
            int first=eventCount==EventCapacity?eventCursor:0;for(int i=0;i<eventCount;i++)result.events[i]=streamEvents[(first+i)%EventCapacity];return result;
        }
        void CountBuild(string reason,float elapsed)
        {
            streamStats.totalBuilds++;if(reason=="prefetch")streamStats.prefetchBuilds++;if(reason=="required")streamStats.requiredBuilds++;if(reason.Contains("force"))streamStats.forceBuilds++;if(reason.StartsWith("baseline",System.StringComparison.Ordinal))streamStats.baselineBuilds++;
            streamStats.maxChunkMilliseconds=Mathf.Max(streamStats.maxChunkMilliseconds,elapsed);streamStats.maxResidentChunks=Mathf.Max(streamStats.maxResidentChunks,loaded.Count);
            if(buildFrame!=Time.frameCount){buildFrame=Time.frameCount;buildsThisFrame=prefetchThisFrame=0;}buildsThisFrame++;if(reason=="prefetch")prefetchThisFrame++;streamStats.maxTotalBuildsPerFrame=Mathf.Max(streamStats.maxTotalBuildsPerFrame,buildsThisFrame);streamStats.maxQueuedBuildsPerFrame=Mathf.Max(streamStats.maxQueuedBuildsPerFrame,prefetchThisFrame);
        }

        int observerSignature;
        const float Size=320; Vector2Int last=new Vector2Int(int.MinValue,0),lastSecondary=new Vector2Int(int.MinValue,0);
        public int LoadedChunks=>loaded.Count;
        public bool HasSurface(Vector3 position)=>IsAutomaticWorld?automaticWorld.Renderer.HasGeometryAt(position):scopedTownSupport800!=null?(townSupportCurrent800!=null&&townSupportCurrent800()&&scopedTownSupport800.HasSurface(position)):scopedTownRejectQueries800?false:ScopedMeshPlan!=null?ScopedMeshHasSurface(position):loaded.TryGetValue(Key(position),out var chunk)&&chunk&&chunk.activeInHierarchy;
        Vector2Int Key(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/Size),Mathf.FloorToInt(p.z/Size));
        void Index<T>(Dictionary<Vector2Int,List<T>> target,T value,Vector3[] points,float margin=0)
        {
            float x0=float.MaxValue,z0=float.MaxValue,x1=float.MinValue,z1=float.MinValue;
            foreach(var p in points){x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);z0=Mathf.Min(z0,p.z);z1=Mathf.Max(z1,p.z);}
            x0-=margin;x1+=margin;z0-=margin;z1+=margin;
            for(int z=Mathf.FloorToInt(z0/Size);z<=Mathf.FloorToInt(z1/Size);z++)for(int x=Mathf.FloorToInt(x0/Size);x<=Mathf.FloorToInt(x1/Size);x++)
            {var key=new Vector2Int(x,z);if(!target.TryGetValue(key,out var list))target[key]=list=new List<T>();list.Add(value);}
        }
        public void Initialize(GeoRegion region,Material asphalt,bool deferInitialCoverage=false)
        {
            // A callback may clean up, but must schedule replacement after retirement completes.
            if(townResetInProgress800)throw new System.InvalidOperationException("Initialize must be scheduled after town retirement callbacks return");
            // Fail before retiring the visible world if preparation is incomplete.
            if(region.roadWidthSchema==1&&region.PreparedRoadWidths==null)throw new System.IO.InvalidDataException("Explicit width controls were not prepared");
            if(region.PreparedRoadWidths!=null&&(pendingRoadSurfaces==null||pendingRoadSurfaces.Owner!=region||pendingRoadSurfaces.WidthField!=region.PreparedRoadWidths))throw new System.IO.InvalidDataException("Finalized width world requires matching prepared surface descriptors");
            ResetScopedTown800(true);
            ResetScopedPreview();
            bootPreparing=deferInitialCoverage;bootActorHold=false;bootTiming=BootTelemetry.Current;using var initializeTiming=BootTelemetry.Measure("main.geo.initialize.total");
            preparedRoadSurfaces=pendingRoadSurfaces;legacyBankPavement=null;retainingGaps=null;RoadBankTerrainDiagnostic="Original narrow road shoulders";
            double lattice=region.roadWidthSchema==1?1.25:2.5;if(terrainMemo.Spacing!=lattice)terrainMemo=new RoadRush.Core.ExactTerrainMemo(spacing:lattice);
            ResetBuildingWindow();BuildingWindowIdentity=null;BuildingWindowStatus="Default scenery";CancelDriveFrontier();CancelStage("initialize");terrainMemo.Clear();memoRegion=region;terrainSampler=region.TerrainHeight;observations.Clear();motion.Clear();generation++;pendingBuilds.Clear();pendingRetires.Clear();streamPlan=null;previousOrigins.Clear();currentOrigins.Clear();
            foreach(var chunk in loaded.Values)Release(chunk);loaded.Clear();buildingChildSignatures.Clear();loadedCells.Clear();roads.Clear();railways.Clear();buildings.Clear();roadBankBuildings.Clear();water.Clear();mappedTrees.Clear();buildingSites.Clear();buildingSiteOwners.Clear();bankGeometryReach.Clear();last=new Vector2Int(int.MinValue,0);lastSecondary=last;observerSignature=0;pumpFrame=int.MinValue;
            BaselineScheduling=BaselineProofAllowed(System.Environment.GetCommandLineArgs(),PlayerProfile.StorageOverride);
#if UNITY_EDITOR
            if(VerificationBaselineScheduling&&!string.IsNullOrEmpty(PlayerProfile.StorageOverride))BaselineScheduling=true;
#endif
            ClearStreamingTelemetry();Region=region;Asphalt=asphalt;using(BootTelemetry.Measure("main.geo.buildinginputs"))BuildingInputs=BuildingRenderInput.Create(region);CosmeticFeatureFallbacks=0;
            paint=Geometry.Material("Map road paint",new Color(.82f,.81f,.73f));roof=BuildingAppearanceMaterials.Roof;
            preparedMarkings=null;MarkingDiagnostic=null;
            if(!Region.circuit)
            {
                try
                {
                    RoadMarkingPlan plan;using(BootTelemetry.Measure("main.geo.markingplan"))plan=RoadMarkingPlan.Build(Region.roads,(road,distance)=>Region.EffectiveWidth(road,(float)distance));
                    using(BootTelemetry.Measure("main.geo.markingprepare"))if(!RoadMarkingChunkGeometry.TryPrepare(plan,Region.roads,Size,out preparedMarkings,out string error))MarkingDiagnostic=error;
                }
                catch(System.Exception e)when(e is System.IO.InvalidDataException||e is System.ArgumentException||e is System.OverflowException){MarkingDiagnostic=e.Message;}
                if(preparedMarkings==null)Debug.LogWarning("Simplified road markings: "+MarkingDiagnostic);
            }
            using(BootTelemetry.Measure("main.geo.landtexture"))ground=ArtSurfaces.Terrain(LandTexture());if(Region.CircuitScenery!=null||Region.CircuitRoadSurfaces!=null)ground.SetFloat("_CircuitTurf",1);
            waterMaterial=Geometry.Surface("Dee river",new Color(.14f,.26f,.29f),.12f,1);waterMaterial.SetFloat("_Smoothness",.88f);
            bark=Geometry.Material("Mapped woodland bark",new Color(.22f,.16f,.1f));leaves=Geometry.Material("Mapped woodland foliage",new Color(.15f,.25f,.09f));
using(BootTelemetry.Measure("main.geo.indexfeatures")){
            foreach(var r in Region.roads)if(!r.tunnel)Index(roads,r,r.points,Region.MaximumRoadWidth(r)*.9f+8);
            foreach(var r in Region.railways??System.Array.Empty<GeoRailway>())if(!r.tunnel&&RailwayMetrics.Visible(r.kind))Index(railways,r,r.points,r.width*.5f+3);
            foreach(var input in BuildingInputs.Renderable){Index(buildings,input.Geometry,input.Geometry.points);Index(roadBankBuildings,input.Geometry,input.Geometry.points);}
            foreach(var w in Region.waters)Index(water,w,w.points,w.width*.5f+8);
            foreach(var tree in Region.trees??System.Array.Empty<GeoTree>()){var key=Key(new Vector3(tree.x,0,tree.z));if(!mappedTrees.TryGetValue(key,out var group))mappedTrees[key]=group=new List<GeoTree>();group.Add(tree);}

}
            // Schema0 has no admitted-width surface plan. Reuse the existing
            // release footprint builder/index once, never construct one per bank
            // triangle. Its existing primitive/membership bounds remain unchanged.
            if(!Region.circuit&&Region.roadWidthSchema==0)
            {
                using(BootTelemetry.Measure("main.geo.legacy-bank-pavement"))
                try
                {
                    legacyBankPavement=new RoadBankExteriorClip.SpatialIndex(LegacyRoadPavementFootprints.Build(Region));
                    PrepareRetainingGaps();
                    RoadBankTerrainDiagnostic="Open road shoulders available; building access and water exclusions retained";
                }
                catch(System.IO.InvalidDataException error)when(
                    error.Message=="Pavement primitive budget"||
                    error.Message=="Pavement index membership budget"||
                    error.Message=="Pavement primitive must be locally tessellated")
                {
                    // Optional visual improvement cannot refuse a previously
                    // supported world solely for its known indexing capacity.
                    // Invalid geometry/unrelated failures are NOT swallowed.
                    legacyBankPavement=null;
                    RoadBankTerrainDiagnostic="Original narrow shoulders retained: "+error.Message;
                    Debug.LogWarning(RoadBankTerrainDiagnostic);
                }
            }
            kerbWhite=Geometry.Material("White circuit kerb",new Color(.87f,.86f,.8f));kerbRed=Geometry.Material("Red circuit kerb",new Color(.62f,.05f,.035f));runoff=Geometry.Surface("Circuit gravel runoff",new Color(.52f,.45f,.33f),.4f,3);rail=Geometry.Material("Circuit safety barrier",new Color(.5f,.53f,.55f),.6f,.32f);
            if(!deferInitialCoverage)Refresh(Region.route[0],true);
        }
        bool RailSupportAllowed(Vector3 centre,float radius)
        {
            if(float.IsNaN(radius)||float.IsInfinity(radius)||radius<0||radius>100)return false;
            var outline=new[]{centre+new Vector3(-radius,0,-radius),centre+new Vector3(radius,0,-radius),centre+new Vector3(radius,0,radius),centre+new Vector3(-radius,0,radius),centre+new Vector3(-radius,0,-radius)};
            if(Region.FootprintOverlapsRoad(outline,.5f))return false;
            foreach(var p in outline)if(Region.IsWater(p.x,p.z))return false;
            var point=new Vector2(centre.x,centre.z);
            foreach(var building in Region.buildings)
            {
                if(GeoRegion.Contains(building.points,point))return false;
                for(int i=1;i<building.points.Length;i++)
                {
                    var a=new Vector2(building.points[i-1].x,building.points[i-1].z);var b=new Vector2(building.points[i].x,building.points[i].z);var delta=b-a;
                    if(Vector2.Distance(point,a+delta*Mathf.Clamp01(Vector2.Dot(point-a,delta)/Mathf.Max(.000001f,delta.sqrMagnitude)))<=radius+.3f)return false;
                }
            }
            return true;
        }
        Texture2D LandTexture()
        {
            int n=Region.landNx;var t=new Texture2D(n,n,TextureFormat.RGB24,true){name="OSM land cover",wrapMode=TextureWrapMode.Clamp,anisoLevel=4};var c=new Color[n*n];
            var random=new System.Random(912);Color[] colours={new Color(.3f,.35f,.19f),new Color(.37f,.4f,.19f),new Color(.18f,.26f,.12f),new Color(.4f,.39f,.32f),new Color(.2f,.29f,.22f)};
            for(int i=0;i<c.Length;i++)c[i]=colours[Mathf.Clamp(Region.landCover[i],0,4)]*(.88f+(float)random.NextDouble()*.17f);
            t.SetPixels(c);t.Apply();return t;
        }
        void Update(){if(IsAutomaticWorld){UpdateAutomaticWorld();return;}using var bootUpdate=BootTelemetry.Enter(bootTiming,"main.geo.postboot.update");if(IsScopedTown800){PollScopedTownLease800();return;}if(bootPreparing||bootActorHold)return;if(Observer)Refresh(Observer.position);else if(!BaselineScheduling&&streamPlan!=null)PumpOuterWork();PumpBuildingWindow();}
        public void Refresh(Vector3 position,bool force=false)
        {
            if(IsAutomaticWorld){automaticWorld.Renderer.SetEmbeddedObserver(position,Observer?Observer.forward:Vector3.forward,automaticWorld.View);return;}
            if(IsScopedTown800)throw new System.InvalidOperationException("Scoped800 town uses final support; normal49chunk streaming is unavailable");
            using(refreshMarker.Auto())
            {
                long began=Timestamp();if(force)streamStats.forceRefreshes++;
                if(BaselineScheduling)RefreshBaseline(position,force);else RefreshQueued(position,force);
                float elapsed=Elapsed(began);streamStats.maxRefreshMilliseconds=Mathf.Max(streamStats.maxRefreshMilliseconds,elapsed);
                if(force||elapsed>.5f)Record(Key(position),"refresh",force?"force":BaselineScheduling?"baseline":"queued",elapsed);
            }
        }
        void RefreshBaseline(Vector3 position,bool force)
        {
            var centre=Key(position);var secondary=SecondaryObserver?Key(SecondaryObserver.position):centre;
            var origins=new List<Vector2Int>{centre,secondary};foreach(var observer in RaceObservers)if(observer)origins.Add(Key(observer.position));
            int signature=0;foreach(var origin in origins)signature=unchecked(signature*31+origin.GetHashCode());
            if(!force&&centre==last&&secondary==lastSecondary&&signature==observerSignature)return;last=centre;lastSecondary=secondary;observerSignature=signature;
            // Deliberately retain0.12 synchronous scheduling for isolated comparison.
            foreach(var origin in origins)for(int z=origin.y-3;z<=origin.y+3;z++)for(int x=origin.x-3;x<=origin.x+3;x++)
            {var key=new Vector2Int(x,z);if(!loaded.ContainsKey(key)&&Intersects(key))Build(key,force?"baseline-force":"baseline-required");}
            var remove=new List<Vector2Int>();foreach(var pair in loaded){bool keep=false;foreach(var origin in origins)if(Mathf.Abs(pair.Key.x-origin.x)<=4&&Mathf.Abs(pair.Key.y-origin.y)<=4){keep=true;break;}if(!keep)remove.Add(pair.Key);}
            foreach(var key in remove)Retire(key,force?"baseline-force":"baseline");
        }
        void RefreshQueued(Vector3 position,bool force)
        {
            currentOrigins.Clear();AddOrigin(position);if(SecondaryObserver)AddOrigin(SecondaryObserver.position);foreach(var observer in RaceObservers)if(observer)AddOrigin(observer.position);currentOrigins.Sort();UpdateMotions(position,force);
            bool changed=streamPlan==null||currentOrigins.Count!=previousOrigins.Count;
            if(!changed)for(int i=0;i<currentOrigins.Count;i++)if(!currentOrigins[i].Equals(previousOrigins[i])){changed=true;break;}
            if(changed||force)
            {
                long began=Timestamp();streamPlan=new RoadRush.Core.ChunkStreamingPlan(currentOrigins,cell=>Intersects(Key(cell)));previousOrigins.Clear();previousOrigins.AddRange(currentOrigins);Record(Key(position),"reconcile",force?"force":"origins",Elapsed(began));
                var missing=streamPlan.MissingRequired(loadedCells);if(!force)streamStats.requiredMisses+=missing.Length;
                if(stage!=null&&!streamPlan.IsResident(Cell(stage.key)))CancelStage("outside-resident");
                foreach(var cell in missing)BuildStagedSynchronously(Key(cell),force?"force":"required");
                pendingBuilds.Clear();pendingRetires.Clear();foreach(var cell in streamPlan.MissingPrefetch(loadedCells,motion))if(stage==null||stage.key!=Key(cell))pendingBuilds.Enqueue(cell);
                foreach(var cell in streamPlan.Retire(loadedCells)){if(force)Retire(Key(cell),"force");else pendingRetires.Enqueue(cell);}
            }
            // Force keeps its old immediate7x7 return contract. Outer work starts
            // on a subsequent frame, never during the force/loading operation.
            if(force){priorityDirty=true;pumpFrame=Time.frameCount;return;}ReprioritizePending();PumpOuterWork();
        }
        void AddOrigin(Vector3 point){var cell=Cell(Key(point));if(!currentOrigins.Contains(cell))currentOrigins.Add(cell);}
        void UpdateMotions(Vector3 primary,bool force)
        {
            motion.Clear();if(observations.Count>8)observations.Clear();AddMotion(primary,Observer,force);if(SecondaryObserver)AddMotion(SecondaryObserver.position,SecondaryObserver,force);foreach(var actor in RaceObservers)if(actor)AddMotion(actor.position,actor,force);
            unchecked{motionSignature=17;foreach(var m in motion){motionSignature=motionSignature*31+(int)System.Math.Floor(m.X/16);motionSignature=motionSignature*31+(int)System.Math.Floor(m.Z/16);motionSignature=motionSignature*31+(int)System.Math.Floor(System.Math.Atan2(m.VelocityZ,m.VelocityX)*8/System.Math.PI);}}
        }
        void AddMotion(Vector3 position,Transform actor,bool force)
        {
            double now=Time.realtimeSinceStartupAsDouble;Vector3 velocity=Vector3.zero;
            if(actor){var body=actor.GetComponent<Rigidbody>();if(body)velocity=body.linearVelocity;}
            if(velocity.sqrMagnitude<.25f&&actor&&!force&&observations.TryGetValue(actor,out var previous))
            {double dt=now-previous.time;if(dt>.00001&&dt<1){var delta=position-previous.position;if(delta.sqrMagnitude<10000)velocity=delta/(float)dt;}}
            if(actor)observations[actor]=new Observation{position=position,time=now};
            if(velocity.sqrMagnitude<.25f&&actor)velocity=actor.forward;motion.Add(new RoadRush.Core.ChunkMotion(position.x,position.z,velocity.x,velocity.z));
        }
        void ReprioritizePending(bool force=false)
        {
            double now=Time.realtimeSinceStartupAsDouble;
            if(streamPlan==null||(!force&&!priorityDirty&&(motionSignature==prioritySignature||now-priorityAt<.125)))return;
            prioritySignature=motionSignature;priorityAt=now;priorityDirty=false;pendingBuilds.Clear();var ranked=streamPlan.MissingPrefetch(loadedCells,motion);
            foreach(var key in ranked)if(driveFrontier.Contains(key)&&(stage==null||stage.key!=Key(key)))pendingBuilds.Enqueue(key);
            foreach(var key in ranked)if(!driveFrontier.Contains(key)&&(stage==null||stage.key!=Key(key)))pendingBuilds.Enqueue(key);
        }
        public void CancelDriveFrontier()
        {
            frontierGeneration++;DriveFrontierReady=false;DriveFrontierStatus="Preparation cancelled";
            if(stage!=null&&driveFrontier.Contains(Cell(stage.key)))CancelStage("drive-frontier-cancel");driveFrontier.Clear();priorityDirty=true;
        }
        public System.Collections.IEnumerator PrepareDriveFrontier(Vector3 position,System.Action<int,int> progress,System.Func<bool> cancelled=null)
        {
            if(IsAutomaticWorld){yield return PrepareAutomaticFrontier(position,progress,cancelled);yield break;}
            CancelDriveFrontier();int request=frontierGeneration,worldGeneration=generation;bool finished=false;
            try
            {
                if(cancelled!=null&&cancelled()){DriveFrontierStatus="Preparation cancelled";yield break;}
                if(Region==null){DriveFrontierStatus="No geographic world";yield break;}
                var wanted=RoadRush.Core.ChunkStreamingPlan.NearBoundaryFrontier(position.x,position.z,cell=>Intersects(Key(cell)));
                foreach(var key in wanted)driveFrontier.Add(key);priorityDirty=true;Refresh(position);int total=wanted.Length;double started=Time.realtimeSinceStartupAsDouble;
                while(request==frontierGeneration&&worldGeneration==generation)
                {
                    if(cancelled!=null&&cancelled()){DriveFrontierStatus="Preparation cancelled";yield break;}
                    int ready=0;foreach(var key in wanted)if(loadedCells.Contains(key))ready++;progress?.Invoke(ready,total);if(request!=frontierGeneration||(cancelled!=null&&cancelled())){DriveFrontierStatus="Preparation cancelled";yield break;}DriveFrontierStatus=total==0?"Starting roads ready":$"Preparing nearby roads {ready}/{total}";
                    if(ready==total){DriveFrontierReady=true;finished=true;yield break;}
                    if(Time.realtimeSinceStartupAsDouble-started>=frontierTimeoutSeconds){DriveFrontierStatus="Road preparation timed out · still paused";yield break;}
                    bool failed=false;try{ReprioritizePending();PumpOuterWork();}catch(System.Exception e){DriveFrontierStatus="Road preparation interrupted · still paused";Debug.LogWarning(DriveFrontierStatus+": "+e.Message);failed=true;}if(failed)yield break;yield return null;
                }
            }
            finally
            {
                if(request==frontierGeneration)
                {
                    if(!finished&&stage!=null&&driveFrontier.Contains(Cell(stage.key)))CancelStage("drive-frontier-incomplete");driveFrontier.Clear();priorityDirty=true;
                }
            }
        }
        float TerrainHeightAtGrid(float x,float z)
        {
            if(BaselineScheduling)return Region.TerrainHeight(x,z);
            if(!ReferenceEquals(memoRegion,Region)){terrainMemo.Clear();memoRegion=Region;terrainSampler=Region.TerrainHeight;}
            return terrainMemo.Read(x,z,terrainSampler);
        }
        void PumpOuterWork()
        {
            if(pumpFrame==Time.frameCount||streamPlan==null)return;pumpFrame=Time.frameCount;
            if(stage==null&&pendingBuilds.Count>0)
            {
                ReprioritizePending(true);
            }
            if(stage==null)while(pendingBuilds.Count>0)
            {
                var key=pendingBuilds.Dequeue();if(loadedCells.Contains(key)||!streamPlan.IsResident(key))continue;stage=StartStage(Key(key),"prefetch");break;
            }
            if(stage!=null)
            {
                long began=Timestamp();bool more=true;
                do {more=AdvanceStage(stage);}while(more&&Elapsed(began)<PreparationBudgetMilliseconds);
                float elapsed=Elapsed(began);streamStats.stagedSlices++;streamStats.maxSliceMilliseconds=Mathf.Max(streamStats.maxSliceMilliseconds,elapsed);
                Record(stage.key,"prepare-"+stage.phase,stage.reason,elapsed);if(!more)stage=null;
            }
            while(pendingRetires.Count>0)
            {
                var key=pendingRetires.Dequeue();if(!loadedCells.Contains(key)||streamPlan.IsResident(key))continue;Retire(Key(key),"queued");break;
            }
        }
        void Retire(Vector2Int key,string reason)
        {
            if(!loaded.TryGetValue(key,out var root))return;long began=Timestamp();using(retireMarker.Auto())Release(root);loaded.Remove(key);loadedCells.Remove(Cell(key));float elapsed=Elapsed(began);streamStats.totalRetired++;streamStats.maxRetireMilliseconds=Mathf.Max(streamStats.maxRetireMilliseconds,elapsed);Record(key,"retire",reason,elapsed);
        }
        bool Intersects(Vector2Int k)=>k.x*Size<Region.minX+Region.width&&(k.x+1)*Size>Region.minX&&k.y*Size<Region.depth+Region.minZ&&(k.y+1)*Size>Region.minZ;
        void Release(GameObject root)
        {
            if(!root)return;
            buildingChildSignatures.Remove(root);
            foreach(string childName in new[]{"Building scenery","Pending building scenery"})
            {var child=root.transform.Find(childName);if(child)buildingChildSignatures.Remove(child.gameObject);}
            root.SetActive(false);Geometry.ReleaseMeshes(root);
            if(Application.isPlaying)Destroy(root);else DestroyImmediate(root);
        }
        void OnDestroy(){ResetBuildingWindow();ResetScopedTown800();ResetScopedPreview();CancelDriveFrontier();terrainMemo.Clear();observations.Clear();motion.Clear();CancelStage("destroy");legacyBankPavement=null;retainingGaps=null;bankGeometryReach.Clear();pendingBuilds.Clear();pendingRetires.Clear();streamPlan=null;generation++;}
        void CancelStage(string reason)
        {
            var cancelled=stage;if(cancelled==null)return;stage=null;
            // Detach ownership before cleanup: a disposed iterator must never be
            // resumed by the streaming pump, even when one cleanup operation fails.
            var errors=new List<System.Exception>();
            try{cancelled.work?.Dispose();}catch(System.Exception error){errors.Add(error);}
            try{if(cancelled.root)Release(cancelled.root.gameObject);}catch(System.Exception error){errors.Add(error);}
            Record(cancelled.key,"cancel",reason,cancelled.cpuMilliseconds);streamStats.cancelledStages++;
            if(errors.Count>0)throw new System.AggregateException("Chunk cancellation cleanup failed",errors);
        }
        ChunkStage StartStage(Vector2Int key,string reason)
        {
            var job=new ChunkStage{key=key,reason=reason,root=new GameObject("Dee chunk "+key).transform};
            job.root.gameObject.SetActive(false);job.root.SetParent(transform,false);job.work=PrepareChunk(job).GetEnumerator();return job;
        }
        bool AdvanceStage(ChunkStage job)
        {
            long began=Timestamp();bool more;
            try {using(buildMarker.Auto())more=job.work.MoveNext();}
            catch {job.work.Dispose();Release(job.root.gameObject);if(stage==job)stage=null;Record(job.key,"build-failed",job.reason,Elapsed(began));throw;}
            float elapsed=Elapsed(began);job.cpuMilliseconds+=elapsed;
            // Time is work on the main thread only: suspended frame intervals are excluded.
            if(elapsed>=PreparationBudgetMilliseconds)Record(job.key,"step-"+job.phase,job.reason,elapsed);
            if(!more)
            {
                using var bootPublication=BootTelemetry.Measure("main.chunk.publish",job.key.x+","+job.key.y);job.work.Dispose();long publication=Timestamp();job.root.gameObject.SetActive(true);loaded.Add(job.key,job.root.gameObject);loadedCells.Add(Cell(job.key));job.complete=true;float publicationMs=Elapsed(publication);job.cpuMilliseconds+=publicationMs;Record(job.key,"publish",job.reason,publicationMs);
                CountBuild(job.reason,job.cpuMilliseconds);Record(job.key,"build",job.reason,job.cpuMilliseconds);
            }
            return more;
        }
        void BuildStagedSynchronously(Vector2Int key,string reason)
        {
            if(loaded.ContainsKey(key))return;
            ChunkStage job;
            if(stage!=null&&stage.key==key){job=stage;stage=null;job.reason=reason;streamStats.urgentStageFinishes++;}
            else job=StartStage(key,reason);
            while(AdvanceStage(job)){}
        }
        IEnumerable<int> PrepareChunk(ChunkStage job)
        {
            var root=job.root;var key=job.key;job.phase="terrain";
            foreach(var step in TerrainSteps(root,key))yield return step;
            foreach(var step in AuthoredRoadSteps(root,key))yield return step;
            job.phase="roads";var capped=new HashSet<string>();
            if(roads.TryGetValue(key,out var rr))foreach(var road in rr)
            {
                if(Region.CircuitRoadSurfaces?.Covers(road.id)==true)continue;
                foreach(var step in MappedRoadSteps(root,key,road))yield return step;
                if(!Region.IsEffectiveBridgeDeck(road))foreach(int endpoint in new[]{0,1})
                {
                    var p=endpoint==0?road.points[0]:road.points[road.points.Length-1];string id=endpoint==0?road.start:road.end;
                    if(Key(p)==key&&capped.Add(road.layer+"|"+id)){JunctionCap(root,p,Region.JunctionRadius(id,Region.EffectiveWidth(road,endpoint==0?0:road.length)*.5f,road.layer),id,road.layer);yield return 0;}
                }
                if(!Region.IsEffectiveBridgeDeck(road)&&(preparedRoadSurfaces==null||!preparedRoadSurfaces.Parts.ContainsKey(road.id)))for(int i=1;i<road.points.Length-1;i++)if(Key(road.points[i])==key&&Vector3.Angle(road.points[i]-road.points[i-1],road.points[i+1]-road.points[i])>60){JunctionCap(root,road.points[i],Region.EffectiveWidthNear(road,road.points[i])*.5f);yield return 0;}
            }
            foreach(var step in RoadMarkingChunkGeometry.Build(root,preparedMarkings,key,paint,Region.RoadSurfaceHeight,Region.RoadSurfaceHeightForRoad))yield return step;
            job.phase="railways";if(railways.TryGetValue(key,out var tracks))foreach(var step in RailwayChunkGeometry.Build(root,Region,tracks,key,Size,MeshSurfaceHeight,RailSupportAllowed))yield return step;
            job.phase="water";if(water.TryGetValue(key,out var ww))foreach(var w in ww)foreach(var step in RibbonSteps(root,key,w.points,w.width,waterMaterial,false,w.name,-3,false))yield return step;
            job.phase="buildings";var buildingRoot=CreateBuildingRoot(root,key);
            if(buildings.TryGetValue(key,out var bb))foreach(var building in bb)if(Key(Centre(building.points))==key)foreach(var step in BuildingSteps(buildingRoot,building))yield return step;
            buildingRoot.GetComponent<BuildingAccessChunk>()?.Commit();yield return 0;
            job.phase="circuit";if(Region.circuit)foreach(var step in CircuitSteps(root,key))yield return step;
            job.phase="vegetation";foreach(var step in VegetationSteps(root,key))yield return step;
        }
        void Build(Vector2Int key,string reason)
        {
            if(loaded.ContainsKey(key))return;long whole=Timestamp();var root=new GameObject("Dee chunk "+key).transform;root.SetParent(transform,false);
            try
            {
                using(buildMarker.Auto())
                {
                    long part=Timestamp();using(terrainMarker.Auto())Terrain(root,key);Record(key,"terrain",reason,Elapsed(part));
                    part=Timestamp();using(roadsMarker.Auto())
                    {
                        foreach(var step in AuthoredRoadSteps(root,key)){}
                        var capped=new HashSet<string>();
                        if(roads.TryGetValue(key,out var rr))foreach(var road in rr)
                        {
                            if(Region.CircuitRoadSurfaces?.Covers(road.id)==true)continue;
                            foreach(var step in MappedRoadSteps(root,key,road)){}
                            if(!Region.IsEffectiveBridgeDeck(road))foreach(int endpoint in new[]{0,1}){var p=endpoint==0?road.points[0]:road.points[road.points.Length-1];string id=endpoint==0?road.start:road.end;if(Key(p)==key&&capped.Add(road.layer+"|"+id))JunctionCap(root,p,Region.JunctionRadius(id,Region.EffectiveWidth(road,endpoint==0?0:road.length)*.5f,road.layer),id,road.layer);}
                            if(!Region.IsEffectiveBridgeDeck(road)&&(preparedRoadSurfaces==null||!preparedRoadSurfaces.Parts.ContainsKey(road.id)))for(int i=1;i<road.points.Length-1;i++)if(Key(road.points[i])==key&&Vector3.Angle(road.points[i]-road.points[i-1],road.points[i+1]-road.points[i])>60)JunctionCap(root,road.points[i],Region.EffectiveWidthNear(road,road.points[i])*.5f);
                        }
                        foreach(var step in RoadMarkingChunkGeometry.Build(root,preparedMarkings,key,paint,Region.RoadSurfaceHeight,Region.RoadSurfaceHeightForRoad)){}
                    }
                    Record(key,"roads",reason,Elapsed(part));part=Timestamp();if(railways.TryGetValue(key,out var tracks))foreach(var step in RailwayChunkGeometry.Build(root,Region,tracks,key,Size,MeshSurfaceHeight,RailSupportAllowed)){}Record(key,"railways",reason,Elapsed(part));part=Timestamp();using(waterMarker.Auto())if(water.TryGetValue(key,out var ww))foreach(var w in ww)Ribbon(root,key,w.points,w.width,waterMaterial,false,w.name,-3,false);Record(key,"water",reason,Elapsed(part));
                    part=Timestamp();var buildingRoot=CreateBuildingRoot(root,key);using(buildingsMarker.Auto())if(buildings.TryGetValue(key,out var bb))foreach(var building in bb)if(Key(Centre(building.points))==key)Building(buildingRoot,building);buildingRoot.GetComponent<BuildingAccessChunk>()?.Commit();Record(key,"buildings",reason,Elapsed(part));
                    part=Timestamp();using(circuitMarker.Auto())if(Region.circuit)CircuitDetails(root,key);Record(key,"circuit",reason,Elapsed(part));
                    part=Timestamp();using(vegetationMarker.Auto())Vegetation(root,key);Record(key,"vegetation",reason,Elapsed(part));
                }
                // HasSurface never exposes a partially constructed chunk.
                loaded.Add(key,root.gameObject);loadedCells.Add(Cell(key));float elapsed=Elapsed(whole);CountBuild(reason,elapsed);Record(key,"build",reason,elapsed);
            }
            catch{Release(root.gameObject);Record(key,"build-failed",reason,Elapsed(whole));throw;}
        }

        Vector3 Centre(Vector3[] p){var c=Vector3.zero;for(int i=0;i<p.Length-1;i++)c+=p[i];return c/Mathf.Max(1,p.Length-1);}
        void Terrain(Transform root,Vector2Int key){foreach(var step in TerrainSteps(root,key)){} }
        IEnumerable<int> TerrainSteps(Transform root,Vector2Int key)
        {
            int n=Mathf.RoundToInt(Size/TerrainGridSpacing);var v=new Vector3[(n+1)*(n+1)];var uv=new Vector2[v.Length];var t=new int[n*n*6];int j=0;
            for(int z=0;z<=n;z++){for(int x=0;x<=n;x++)
            {float wx=(key.x+(float)x/n)*Size,wz=(key.y+(float)z/n)*Size;int i=z*(n+1)+x;v[i]=new Vector3(wx,TerrainHeightAtGrid(wx,wz),wz);uv[i]=new Vector2((wx-Region.minX)/Region.width,(wz-Region.minZ)/Region.depth);}yield return 0;}
            for(int z=0;z<n;z++){for(int x=0;x<n;x++){int a=z*(n+1)+x;foreach(int i in new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2})t[j++]=i;}yield return 0;}
            if(Region.CircuitRoadSurfaces!=null)
            {foreach(var step in AuthoredTerrainSteps(root,key,v,t))yield return step;}
            else foreach(var step in EmitRetainingTerrain(root,key,v,t,uv))yield return step;
        }
        static float RoadBankReach(GeoRoad road)=>road.kind=="residential"||road.kind=="service"?.6f:3;
        // Keep the original narrow clearance for existing entrances. Geometry may
        // use a gentler shoulder ONLY where no building/access, channel or stacked
        // road can occupy its added footprint. The hidden terrain bed is unchanged.
        const float OpenRoadShoulderMetres = 2.5f;
        const float BuildingThresholdAllowanceMetres = .25f;
        readonly Dictionary<GeoRoad,float> bankGeometryReach = new Dictionary<GeoRoad,float>();

        static bool BankBoundsOverlap(Vector3[] points,float margin,float x0,float z0,float x1,float z1)
        {
            if(points==null||points.Length==0)return false;
            float left=float.PositiveInfinity,south=left,right=float.NegativeInfinity,north=right;
            foreach(var point in points)
            {left=Mathf.Min(left,point.x);right=Mathf.Max(right,point.x);south=Mathf.Min(south,point.z);north=Mathf.Max(north,point.z);}
            return right+margin>=x0&&left-margin<=x1&&north+margin>=z0&&south-margin<=z1;
        }

        // Linear features need segment boxes: a winding river's WHOLE box can
        // enclose an unrelated hillside even when its actual channel is far away.
        static bool BankLineBoundsOverlap(Vector3[] points,float margin,float x0,float z0,float x1,float z1)
        {
            if(points==null)return false;
            for(int i=1;i<points.Length;i++)
            {
                var a=points[i-1];var b=points[i];
                if(Mathf.Max(a.x,b.x)+margin>=x0&&Mathf.Min(a.x,b.x)-margin<=x1&&
                    Mathf.Max(a.z,b.z)+margin>=z0&&Mathf.Min(a.z,b.z)-margin<=z1)return true;
            }
            return false;
        }

        float RoadBankGeometryReach(GeoRoad road)
        {
            float original=RoadBankReach(road);
            if(original>=OpenRoadShoulderMetres||Region==null||(preparedRoadSurfaces==null&&legacyBankPavement==null)||Region.circuit||Region.roadWidthSchema!=0||Region.IsEffectiveBridgeDeck(road)||road.tunnel||road.layer!=0)
                return original;
            if(bankGeometryReach.TryGetValue(road,out float cached))return cached;
            float x0=float.PositiveInfinity,z0=x0,x1=float.NegativeInfinity,z1=x1;
            foreach(var point in road.points)
            {x0=Mathf.Min(x0,point.x);x1=Mathf.Max(x1,point.x);z0=Mathf.Min(z0,point.z);z1=Mathf.Max(z1,point.z);}
            // RibbonSteps bounds its miter denominator below by .6; cover that
            // same maximum amplification, not just an unmitred nominal width.
            float padding=(Region.MaximumRoadWidth(road)*.5f+OpenRoadShoulderMetres)/.6f;
            x0-=padding;x1+=padding;z0-=padding;z1+=padding;
            // This intentionally conservative whole-road box keeps old geometry
            // even when only one end approaches a building. No source house or
            // existing access flight is hidden/dropped to make room for the bank.
            float access=BuildingAccessGeometry.MaximumReach+BuildingThresholdAllowanceMetres;
            bool clear=true;
            int west=Mathf.FloorToInt((x0-access)/Size),east=Mathf.FloorToInt((x1+access)/Size);
            int south=Mathf.FloorToInt((z0-access)/Size),north=Mathf.FloorToInt((z1+access)/Size);
            for(int z=south;z<=north&&clear;z++)for(int x=west;x<=east&&clear;x++)
            {
                var cell=new Vector2Int(x,z);
                if(roadBankBuildings.TryGetValue(cell,out var houses))foreach(var house in houses)
                    if(BankBoundsOverlap(house.points,access,x0,z0,x1,z1)){clear=false;break;}
                if(railways.TryGetValue(cell,out var tracks))foreach(var track in tracks)
                    if(BankLineBoundsOverlap(track.points,track.width*.5f+3,x0,z0,x1,z1)){clear=false;break;}
                if(water.TryGetValue(cell,out var channels))foreach(var channel in channels)
                    if(BankLineBoundsOverlap(channel.points,channel.width*.5f+4,x0,z0,x1,z1)){clear=false;break;}
                if(roads.TryGetValue(cell,out var nearby))foreach(var other in nearby)
                    if(other!=road&&(Region.IsEffectiveBridgeDeck(other)||other.tunnel||other.layer!=road.layer)&&
                        BankLineBoundsOverlap(other.points,Region.MaximumRoadWidth(other)*.5f,x0,z0,x1,z1))
                    {clear=false;break;}
            }
            float result=clear?OpenRoadShoulderMetres:original;
            bankGeometryReach.Add(road,result);
            return result;
        }

        void Ribbon(Transform root,Vector2Int key,Vector3[] source,float width,Material material,bool collision,string name,float lift,bool markings,float bankReach=0)
        {foreach(var step in RibbonSteps(root,key,source,width,material,collision,name,lift,markings,bankReach)){} }
        IEnumerable<int> RibbonSteps(Transform root,Vector2Int key,Vector3[] source,float width,Material material,bool collision,string name,float lift,bool markings,float bankReach=0,GeoRoad sourceRoad=null)
        {
            void Add(List<Vector3> vertices,List<int> triangles,List<Vector2> tex,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float distance)
            {
                float west=key.x*Size,east=west+Size,south=key.y*Size,north=south+Size;
                if(a.x>=west&&a.x<=east&&b.x>=west&&b.x<=east&&c.x>=west&&c.x<=east&&d.x>=west&&d.x<=east&&a.z>=south&&a.z<=north&&b.z>=south&&b.z<=north&&c.z>=south&&c.z<=north&&d.z>=south&&d.z<=north){Quad(vertices,triangles,tex,a,b,c,d,distance);return;}
                if(Mathf.Max(Mathf.Max(a.x,b.x),Mathf.Max(c.x,d.x))<west||Mathf.Min(Mathf.Min(a.x,b.x),Mathf.Min(c.x,d.x))>east||Mathf.Max(Mathf.Max(a.z,b.z),Mathf.Max(c.z,d.z))<south||Mathf.Min(Mathf.Min(a.z,b.z),Mathf.Min(c.z,d.z))>north)return;
                // Clip each original triangle, retaining its winding and plane.
                foreach(var triangle in new[]{new[]{a,c,b},new[]{b,c,d}})
                {
                    var polygon=new List<Vector3>(triangle);
                    for(int plane=0;plane<4;plane++)
                    {
                        var output=new List<Vector3>();if(polygon.Count==0)break;
                        float Signed(Vector3 p)=>plane==0?p.x-west:plane==1?east-p.x:plane==2?p.z-south:north-p.z;
                        var previous=polygon[polygon.Count-1];float pd=Signed(previous);
                        foreach(var next in polygon){float nd=Signed(next);if((pd>=0)!=(nd>=0))output.Add(Vector3.Lerp(previous,next,pd/(pd-nd)));if(nd>=0)output.Add(next);previous=next;pd=nd;}
                        polygon=output;
                    }
                    int start=vertices.Count;vertices.AddRange(polygon);foreach(var p in polygon)tex.Add(new Vector2(p.x/8,p.z/8));
                    for(int i=1;i<polygon.Count-1;i++){triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
                }
            }
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            var structureV=new List<Vector3>();var structureT=new List<int>();var structureU=new List<Vector2>();
            var barrierV=new List<Vector3>();var barrierT=new List<int>();var barrierU=new List<Vector2>();
            var bankV=new List<Vector3>();var bankT=new List<int>();var bankU=new List<Vector2>();
            void FlushClippedBanks(){if(bankT.Count>0)Geometry.Mesh(collision?"Road embankments":"River banks",root,bankV.ToArray(),bankT.ToArray(),bankU.ToArray(),Geometry.Surface(collision?"Verge gravel":"River earth",collision?new Color(.27f,.29f,.2f):new Color(.26f,.24f,.17f),.3f,2),collision);bankV.Clear();bankT.Clear();bankU.Clear();}
            var mv=new List<Vector3>();var mu=new List<Vector2>();var mt=new List<int>();float travelled=0;
            for(int i=0;i<source.Length-1;i++)
            {
                var a=source[i]+Vector3.up*lift;var b=source[i+1]+Vector3.up*lift;float length=Vector3.Distance(a,b);var dir=(b-a).normalized;
                var before=i>0?(source[i]-source[i-1]).normalized:dir;var after=i+2<source.Length?(source[i+2]-source[i+1]).normalized:dir;
                var r0=Vector3.Cross(Vector3.up,(before+dir).normalized).normalized;var r1=Vector3.Cross(Vector3.up,(dir+after).normalized).normalized;
                // A bounded miter keeps both sides continuous through mapped bends.
                r0/=Mathf.Max(.6f,Vector3.Dot(r0,Vector3.Cross(Vector3.up,dir).normalized));r1/=Mathf.Max(.6f,Vector3.Dot(r1,Vector3.Cross(Vector3.up,dir).normalized));
                // At a hairpin use a bevel plus the round join, rather than a
                // clamped miter that pinches away part of the driving lane.
                if(Vector3.Angle(before,dir)>60)r0=Vector3.Cross(Vector3.up,dir).normalized;
                if(Vector3.Angle(dir,after)>60)r1=Vector3.Cross(Vector3.up,dir).normalized;
                int steps=Mathf.Max(1,Mathf.CeilToInt(length/2.5f));
                for(int j=0;j<steps;j++)
                {
                    yield return 0;
                    var p=Vector3.Lerp(a,b,(float)j/steps);var q=Vector3.Lerp(a,b,(float)(j+1)/steps);
                    float reach=width*.9f+8;if(Mathf.Max(p.x,q.x)<key.x*Size-reach||Mathf.Min(p.x,q.x)>(key.x+1)*Size+reach||Mathf.Max(p.z,q.z)<key.y*Size-reach||Mathf.Min(p.z,q.z)>(key.y+1)*Size+reach)continue;
                    var rp=Vector3.Lerp(r0,r1,(float)j/steps);var rq=Vector3.Lerp(r0,r1,(float)(j+1)/steps);
                    Vector3 Surface(Vector3 value){if(collision)value.y=sourceRoad==null?Region.RoadSurfaceHeight(value.x,value.z):Region.RoadSurfaceHeightForRoad(sourceRoad,value.x,value.z);return value;}
                    int across=name=="Bridge deck"&&sourceRoad!=null?Region.EffectiveBridgeColumns(sourceRoad):collision?Mathf.Max(2,Mathf.CeilToInt(width/2)):1;
                    for(int column=0;column<across;column++)
                    {
                        float left=width*(column/(float)across-.5f),right=width*((column+1)/(float)across-.5f);
                        Add(v,t,uv,Surface(p+rp*left),Surface(p+rp*right),Surface(q+rq*left),Surface(q+rq*right),travelled+j*length/steps);
                    }
                    if(collision&&name=="Bridge deck")
                    {
                        const float slab=.75f;var down=Vector3.up*slab;
                        var leftP=Surface(p-rp*width*.5f);var rightP=Surface(p+rp*width*.5f);var leftQ=Surface(q-rq*width*.5f);var rightQ=Surface(q+rq*width*.5f);
                        Add(structureV,structureT,structureU,rightP-down,leftP-down,rightQ-down,leftQ-down,0);
                        foreach(int side in new[]{-1,1})
                        {
                            var edgeP=Surface(p+rp*side*width*.5f);var edgeQ=Surface(q+rq*side*width*.5f);
                            if(side>0)Add(structureV,structureT,structureU,edgeQ,edgeP,edgeQ-down,edgeP-down,0);
                            else Add(structureV,structureT,structureU,edgeP,edgeQ,edgeP-down,edgeQ-down,0);
                            // Continuous concrete plinth and steel rails are solid
                            // colliders; posts sit beyond the usable asphalt edge.
                            if(Key((p+q)*.5f)==key)
                            {
                                var axis=(q-p).normalized;var rotation=Quaternion.LookRotation(axis,Vector3.up);float span=Vector3.Distance(p,q)+.035f;var outward=(rp+rq)*.5f*side;
                                var edge=Surface((p+q)*.5f+outward*(width*.5f+.18f));
                                AddBridgeBox(barrierV,barrierT,barrierU,edge+Vector3.up*.16f,new Vector3(.36f,.32f,span),rotation);
                                foreach(float y in new[]{.63f,1.08f})AddBridgeBox(barrierV,barrierT,barrierU,edge+Vector3.up*y,new Vector3(.1f,.1f,span),rotation);
                                AddBridgeBox(barrierV,barrierT,barrierU,edge+Vector3.up*.65f,new Vector3(.1f,1.3f,.1f),rotation);
                                var beam=Surface((p+q)*.5f+outward*(width*.28f))-Vector3.up*(slab+.325f);
                                AddBridgeBox(structureV,structureT,structureU,beam,new Vector3(.5f,.65f,span),rotation);
                            }
                        }
                        if(i==0&&j==0)Add(structureV,structureT,structureU,rightP,leftP,rightP-down,leftP-down,0);
                        if(i==source.Length-2&&j==steps-1)Add(structureV,structureT,structureU,leftQ,rightQ,leftQ-down,rightQ-down,0);
                    }
                    if(name!="Bridge deck")foreach(int side in new[]{-1,1})
                    {
                        var innerP=Surface(p+rp*side*(width*.5f-.03f))-Vector3.up*.035f;var innerQ=Surface(q+rq*side*(width*.5f-.03f))-Vector3.up*.035f;
                        var outerP=p+rp*side*(width*.5f+(bankReach>0?bankReach:collision?3:6));var outerQ=q+rq*side*(width*.5f+(bankReach>0?bankReach:collision?3:6));outerP.y=MeshSurfaceHeight(outerP.x,outerP.z)+.012f;outerQ.y=MeshSurfaceHeight(outerQ.x,outerQ.z)+.012f;
                        for(int slice=0;slice<4;slice++)
                        {
                            float u=slice/4f,w=(slice+1)/4f;
                            var ba=Vector3.Lerp(innerP,outerP,u);var bb=Vector3.Lerp(innerP,outerP,w);var bc=Vector3.Lerp(innerQ,outerQ,u);var bd=Vector3.Lerp(innerQ,outerQ,w);if(side<0){var swap=ba;ba=bb;bb=swap;swap=bc;bc=bd;bd=swap;}
                            if(preparedRoadSurfaces!=null||(legacyBankPavement!=null&&sourceRoad!=null&&bankReach>RoadBankReach(sourceRoad))){foreach(var step in AppendBankExteriorSteps(bankV,bankT,bankU,ba,bb,bc,bd,key,sourceRoad?.layer??0,false,FlushClippedBanks,preparedRoadSurfaces==null&&legacyBankPavement!=null&&bankReach>RoadBankReach(sourceRoad)))yield return step;}
                            else Add(bankV,bankT,bankU,ba,bb,bc,bd,travelled);
                        }
                    }
                    if(name=="Bridge deck")
                    {
                        // Profile rows may be resampled; place supports by metres
                        // travelled, not a row counter that resets every segment.
                        // Half-open arc intervals give each regular station one owner.
                        const float pierSpacingMetres=20f;
                        float arcStart=travelled+j*length/steps,arcEnd=travelled+(j+1)*length/steps;
                        float station=Mathf.Ceil(arcStart/pierSpacingMetres)*pierSpacingMetres;
                        if(station<arcEnd)
                        {
                            var anchor=Surface(Vector3.Lerp(p,q,(station-arcStart)/Mathf.Max(.000001f,arcEnd-arcStart)));
                            if(Key(anchor)==key)
                            {
                                var transverse=Vector3.Cross(Vector3.up,dir).normalized;float groundY=MeshSurfaceHeight(anchor.x,anchor.z);
                                foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){var foot=anchor+transverse*side*width*.275f+new Vector3(dir.x,0,dir.z).normalized*end*.65f;groundY=Mathf.Min(groundY,MeshSurfaceHeight(foot.x,foot.z));}
                                groundY-=.5f;float height=anchor.y-.8f-groundY;
                                float pierWidth=width*.55f;const float pierLength=1.3f;
                                if(height>.7f&&height<200&&BridgePierFootprintClear(anchor,dir,pierWidth,pierLength,sourceRoad))
                                {var pier=Geometry.Box("Bridge pier",root,new Vector3(anchor.x,groundY+height*.5f,anchor.z),new Vector3(pierWidth,height,pierLength),Geometry.Material("Bridge concrete",new Color(.4f,.4f,.37f)),true);pier.transform.rotation=Quaternion.LookRotation(new Vector3(dir.x,0,dir.z),Vector3.up);}
                            }
                        }
                    }
                    if(markings&&width>=5.4f&&Mathf.Repeat(travelled+j*length/steps,12)<5)
                    {var off=Vector3.up*.025f;Add(mv,mt,mu,Surface(p-rp*.065f)+off,Surface(p+rp*.065f)+off,Surface(q-rq*.065f)+off,Surface(q+rq*.065f)+off,0);}
                }
                travelled+=length;
            }
            if(v.Count>0)Geometry.Mesh(name,root,v.ToArray(),t.ToArray(),uv.ToArray(),material,collision);yield return 0;
            if(structureV.Count>0)Geometry.Mesh("Bridge concrete structure",root,structureV.ToArray(),structureT.ToArray(),structureU.ToArray(),Geometry.Surface("Bridge concrete structure",new Color(.39f,.4f,.39f),.16f,1),true);yield return 0;
            if(barrierV.Count>0)Geometry.Mesh("Bridge safety barriers",root,barrierV.ToArray(),barrierT.ToArray(),barrierU.ToArray(),Geometry.Material("Bridge galvanized barriers",new Color(.52f,.55f,.56f),.65f,.28f),true);yield return 0;
            if(bankV.Count>0)Geometry.Mesh(collision?"Road embankments":"River banks",root,bankV.ToArray(),bankT.ToArray(),bankU.ToArray(),Geometry.Surface(collision?"Verge gravel":"River earth",collision?new Color(.27f,.29f,.2f):new Color(.26f,.24f,.17f),.3f,2),collision);yield return 0;
            if(mv.Count>0)Geometry.Mesh("Mapped road markings",root,mv.ToArray(),mt.ToArray(),mu.ToArray(),paint,false);yield return 0;
        }
        // Piers are optional visual/physical supports, never authority for the
        // deck. Refuse a whole rotated footing that overlaps a mapped corridor;
        // source layer/centreline Y alone cannot prove a solid underpass clear.
        // This deliberately treats every layer conservatively, including tunnels
        // whose roofs a foundation could penetrate. No road/deck is removed.
        bool BridgePierFootprintClear(Vector3 centre,Vector3 forward,float pierWidth,float pierLength,GeoRoad deckRoad)
        {
            if(Region==null||!(!float.IsNaN(pierWidth)&&!float.IsInfinity(pierWidth))||!(!float.IsNaN(pierLength)&&!float.IsInfinity(pierLength))||pierWidth<=0||pierLength<=0)return false;
            forward.y=0;if(forward.sqrMagnitude<.000001f)return false;forward.Normalize();
            var right=Vector3.Cross(Vector3.up,forward);
            float halfWidth=pierWidth*.5f,halfLength=pierLength*.5f;
            float extentX=Mathf.Abs(right.x)*halfWidth+Mathf.Abs(forward.x)*halfLength;
            float extentZ=Mathf.Abs(right.z)*halfWidth+Mathf.Abs(forward.z)*halfLength;
            // Maximum ribbon miter = 1/.6, hence half-width/.6 = width/1.2.
            // Add .5m road-edge clearance; rail3m/water4m match existing bank
            // exclusion corridors. These are conservative placement margins,
            // not measured legal or hydraulic clearance claims.
            var seenRoads=new HashSet<GeoRoad>();var seenRails=new HashSet<GeoRailway>();var seenWater=new HashSet<GeoWater>();
            bool Overlaps(Vector3[] points,float corridorRadius)
            {
                if(points==null||!(!float.IsNaN(corridorRadius)&&!float.IsInfinity(corridorRadius))||corridorRadius<0)return true;
                for(int segment=1;segment<points.Length;segment++)
                    if(BridgePierSegmentOverlaps(points[segment-1],points[segment],centre,right,forward,
                        halfWidth+corridorRadius,halfLength+corridorRadius))return true;
                return false;
            }
            // Feature indices already expand source segments by their widths.
            // Visit every cell touched by the complete pier, not only its centre.
            int west=Mathf.FloorToInt((centre.x-extentX)/Size),east=Mathf.FloorToInt((centre.x+extentX)/Size);
            int south=Mathf.FloorToInt((centre.z-extentZ)/Size),north=Mathf.FloorToInt((centre.z+extentZ)/Size);
            for(int z=south;z<=north;z++)for(int x=west;x<=east;x++)
            {
                var cell=new Vector2Int(x,z);
                if(roads.TryGetValue(cell,out var nearbyRoads))foreach(var road in nearbyRoads)
                    if(!SameBridgeSourceWay(road,deckRoad)&&seenRoads.Add(road)&&Overlaps(road.points,Region.MaximumRoadWidth(road)/1.2f+.5f))return false;
                if(railways.TryGetValue(cell,out var tracks))foreach(var track in tracks)
                    if(seenRails.Add(track)&&Overlaps(track.points,track.width*.5f+3))return false;
                if(water.TryGetValue(cell,out var channels))foreach(var channel in channels)
                    if(seenWater.Add(channel)&&Overlaps(channel.points,channel.width*.5f+4))return false;
            }
            // Underground features are intentionally absent from visible indices.
            foreach(var road in Region.roads)if(!SameBridgeSourceWay(road,deckRoad)&&road.tunnel&&Overlaps(road.points,Region.MaximumRoadWidth(road)/1.2f+.5f))return false;
            foreach(var track in Region.railways??System.Array.Empty<GeoRailway>())
                if(track.tunnel&&Overlaps(track.points,track.width*.5f+3))return false;
            // Land-cover water is a nearest-cell field. Test every grid cell
            // touching the footing AABB, so a wide lake needs no centreline.
            if(Region.landCover!=null&&Region.landNx>=2&&Region.landNz>=2&&Region.width>0&&Region.depth>0)
            {
                int x0=Mathf.Clamp(Mathf.FloorToInt((centre.x-extentX-Region.minX)/Region.width*(Region.landNx-1)),0,Region.landNx-1);
                int x1=Mathf.Clamp(Mathf.CeilToInt((centre.x+extentX-Region.minX)/Region.width*(Region.landNx-1)),0,Region.landNx-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((centre.z-extentZ-Region.minZ)/Region.depth*(Region.landNz-1)),0,Region.landNz-1);
                int z1=Mathf.Clamp(Mathf.CeilToInt((centre.z+extentZ-Region.minZ)/Region.depth*(Region.landNz-1)),0,Region.landNz-1);
                if(Region.landCover.Length!=Region.landNx*Region.landNz)return false;
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)if(Region.landCover[z*Region.landNx+x]==4)return false;
            }
            return true;
        }

        // Legacy exporter splits one numeric OSM way into <way>-<segment>.
        // Exclude that owning bridgeway, not merely one GeoRoad object, so its
        // neighbouring segments do not veto every support. Unrecognised IDs get
        // no guessed exemption; other source ways/layers remain obstacles.
        bool SameBridgeSourceWay(GeoRoad candidate,GeoRoad owner)
        {
            if(ReferenceEquals(candidate,owner))return true;
            if(candidate==null||owner==null||!Region.IsEffectiveBridgeDeck(candidate)||!Region.IsEffectiveBridgeDeck(owner)||candidate.layer!=owner.layer)return false;
            string Way(string id)
            {
                if(string.IsNullOrEmpty(id))return null;int split=id.LastIndexOf('-');
                if(split<=0||split==id.Length-1)return null;
                for(int i=0;i<id.Length;i++)if(i!=split&&(id[i]<'0'||id[i]>'9'))return null;
                return id.Substring(0,split);
            }
            string source=Way(owner.id);return source!=null&&source==Way(candidate.id);
        }

        // Closed segment versus expanded local rectangle (Liang-Barsky). Width
        // expansion in BOTH axes is conservative for round swept road/rail/water
        // corridors. Double arithmetic retains the literal float input positions;
        // closed contact is refused rather than leaving a pier at a road edge.
        internal static bool BridgePierSegmentOverlaps(Vector3 a,Vector3 b,Vector3 centre,Vector3 right,Vector3 forward,float halfWidth,float halfLength)
        {
            double ax=((double)a.x-centre.x)*right.x+((double)a.z-centre.z)*right.z;
            double az=((double)a.x-centre.x)*forward.x+((double)a.z-centre.z)*forward.z;
            double bx=((double)b.x-centre.x)*right.x+((double)b.z-centre.z)*right.z;
            double bz=((double)b.x-centre.x)*forward.x+((double)b.z-centre.z)*forward.z;
            if(double.IsNaN(ax)||double.IsNaN(az)||double.IsNaN(bx)||double.IsNaN(bz)||
                double.IsInfinity(ax)||double.IsInfinity(az)||double.IsInfinity(bx)||double.IsInfinity(bz))return true;
            double enter=0,exit=1;
            bool Axis(double start,double delta,double extent)
            {
                if(delta==0)return start>=-extent&&start<=extent;
                double first=(-extent-start)/delta,last=(extent-start)/delta;
                if(first>last){double swap=first;first=last;last=swap;}
                enter=System.Math.Max(enter,first);exit=System.Math.Min(exit,last);return enter<=exit;
            }
            return Axis(ax,bx-ax,halfWidth)&&Axis(az,bz-az,halfLength);
        }

        void JunctionCap(Transform root,Vector3 p,float radius,string sourceNode=null,int sourceLayer=0)
        {
            const int sides=32;int rings=Mathf.Max(1,Mathf.CeilToInt(radius/1.5f));var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            Vector3 Point(float x,float z)=>new Vector3(x,(sourceNode==null?Region.RoadSurfaceHeight(x,z):Region.JunctionSurfaceHeight(sourceNode,sourceLayer,x,z))+.003f,z);
            v.Add(Point(p.x,p.z));uv.Add(new Vector2(p.x/8,p.z/8));
            for(int ring=1;ring<=rings;ring++)for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,r=radius*ring/rings;var point=Point(p.x+Mathf.Cos(a)*r,p.z+Mathf.Sin(a)*r);v.Add(point);uv.Add(new Vector2(point.x/8,point.z/8));
                int current=1+(ring-1)*sides+i,next=1+(ring-1)*sides+(i+1)%sides;
                if(ring==1)t.AddRange(new[]{0,next,current});
                else {int inner=current-sides,innerNext=next-sides;t.AddRange(new[]{inner,innerNext,current,innerNext,next,current});}
            }
            Geometry.Mesh("Junction apron",root,v.ToArray(),t.ToArray(),uv.ToArray(),Asphalt,true);
        }
        void CircuitDetails(Transform root,Vector2Int key){foreach(var step in CircuitSteps(root,key)){} }
        IEnumerable<int> CircuitSteps(Transform root,Vector2Int key)
        {
            if(Region.CircuitScenery!=null)yield break;
            var redV=new List<Vector3>();var redT=new List<int>();var redU=new List<Vector2>();var whiteV=new List<Vector3>();var whiteT=new List<int>();var whiteU=new List<Vector2>();
            var edgeV=new List<Vector3>();var edgeT=new List<int>();var edgeU=new List<Vector2>();var runoffV=new List<Vector3>();var runoffT=new List<int>();var runoffU=new List<Vector2>();var railV=new List<Vector3>();var railT=new List<int>();var railU=new List<Vector2>();
            var points=Region.route;float half=Region.roads[0].width*.5f;
            for(int i=0;i<points.Length-1;i++)
            {
                yield return 0;
                var p=points[i];var q=points[i+1];if(Key((p+q)*.5f)!=key)continue;var direction=(q-p).normalized;
                var before=i>0?(p-points[i-1]).normalized:(points[points.Length-1]-points[points.Length-2]).normalized;
                var after=i+2<points.Length?(points[i+2]-q).normalized:(points[1]-points[0]).normalized;
                var a=Vector3.Cross(Vector3.up,(before+direction).normalized).normalized;var b=Vector3.Cross(Vector3.up,(direction+after).normalized).normalized;
                a/=Mathf.Max(.65f,Vector3.Dot(a,Vector3.Cross(Vector3.up,direction).normalized));b/=Mathf.Max(.65f,Vector3.Dot(b,Vector3.Cross(Vector3.up,direction).normalized));
                foreach(int side in new[]{-1,1})
                {
                    var rp=a*side;var rq=b*side;var lift=Vector3.up*.035f;
                    SideQuad(edgeV,edgeT,edgeU,p+rp*(half-.22f)+lift,p+rp*(half-.08f)+lift,q+rq*(half-.22f)+lift,q+rq*(half-.08f)+lift,0,side);
                    bool red=i%2==0;SideQuad(red?redV:whiteV,red?redT:whiteT,red?redU:whiteU,p+rp*half+lift,p+rp*(half+.75f)+lift,q+rq*half+lift,q+rq*(half+.75f)+lift,0,side);
                    SideQuad(runoffV,runoffT,runoffU,p+rp*(half+.75f)-Vector3.up*.035f,p+rp*(half+10)-Vector3.up*.035f,q+rq*(half+.75f)-Vector3.up*.035f,q+rq*(half+10)-Vector3.up*.035f,i*4,side);
                    if(i%2!=0)continue;float barrierOffset=half+13;float turn=Vector3.SignedAngle(before,after,Vector3.up);float angle=Mathf.Abs(turn)*Mathf.Deg2Rad;float radius=angle>.001f?Vector3.Distance(p,q)*2/angle:1000;
                    if(Mathf.Sign(turn)==side)barrierOffset=Mathf.Min(barrierOffset,radius*.65f);if(barrierOffset<half+2.5f)continue;
                    var end=points[Mathf.Min(i+2,points.Length-1)];var centre=(p+end)*.5f+rp*barrierOffset+Vector3.up*.65f;
                    // Leave a service access opening opposite the forecourt.
                    if(Region.HasServicePad&&Vector2.Distance(new Vector2(centre.x,centre.z),new Vector2(Region.ServicePad.x,Region.ServicePad.z))<32)continue;
                    AddBox(railV,railT,railU,centre,new Vector3(.25f,1.05f,Vector3.Distance(p,end)+.4f),Quaternion.LookRotation(end-p,Vector3.up));
                }
            }
            void Emit(string name,List<Vector3> v,List<int> t,List<Vector2> uv,Material material,bool collision){if(v.Count>0)Geometry.Mesh(name,root,v.ToArray(),t.ToArray(),uv.ToArray(),material,collision);}
            Emit("Red kerbs",redV,redT,redU,kerbRed,true);yield return 0;Emit("White kerbs",whiteV,whiteT,whiteU,kerbWhite,true);yield return 0;Emit("Track edge paint",edgeV,edgeT,edgeU,paint,false);yield return 0;Emit("Runoff",runoffV,runoffT,runoffU,runoff,true);yield return 0;Emit("Safety barriers",railV,railT,railU,rail,true);yield return 0;
        }
        static void SideQuad(List<Vector3> v,List<int> t,List<Vector2> uv,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float distance,int side)
        {if(side>0)Quad(v,t,uv,a,b,c,d,distance);else Quad(v,t,uv,b,a,d,c,distance);}
        static void AddBridgeBox(List<Vector3> vertices,List<int> triangles,List<Vector2> uv,Vector3 centre,Vector3 size,Quaternion rotation)
        {
            var corners=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
            foreach(var face in new[]{new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,4,7,3},new[]{1,2,6,5},new[]{3,7,6,2},new[]{0,1,5,4}})
            {
                int start=vertices.Count;foreach(int i in face){var p=centre+rotation*Vector3.Scale(corners[i],size*.5f);vertices.Add(p);uv.Add(new Vector2(p.x/4,(p.y+p.z)/4));}
                triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
        }
        static void AddBox(List<Vector3> v,List<int> t,List<Vector2> uv,Vector3 centre,Vector3 size,Quaternion rotation)
        {
            int start=v.Count;foreach(var p in new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)}){v.Add(centre+rotation*Vector3.Scale(p,size*.5f));uv.Add(Vector2.zero);}
            foreach(int i in new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,0,1,5,0,5,4,3,7,6,3,6,2})t.Add(start+i);
        }
        static void Quad(List<Vector3> v,List<int> t,List<Vector2> uv,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float distance)
        {int i=v.Count;v.AddRange(new[]{a,b,c,d});t.AddRange(new[]{i,i+2,i+1,i+1,i+2,i+3});foreach(var p in new[]{a,b,c,d})uv.Add(new Vector2(p.x/8,p.z/8));}
        // Meshes/facades/access share a replaceable child; terrain and roads keep their owner.
        Transform CreateBuildingRoot(Transform chunk,Vector2Int key,string signature=null)
        {var child=new GameObject("Building scenery").transform;child.SetParent(chunk,false);buildingChildSignatures[child.gameObject]=signature??BuildingFingerprint(key,BuildingInputs,buildings);return child;}
        void Building(Transform root,GeoBuilding b){foreach(var step in BuildingSteps(root,b)){} }
        IEnumerable<int> BuildingSteps(Transform root,GeoBuilding b)
        {
            if(CircuitReplacementArt.ShouldReplace(Region,b))yield break;
            if(TownRoadOverlap(b.points,1))yield break;
            if(!buildingSites.TryGetValue(b.id,out var site)){if(!TryBuildingSite(b,out site))yield break;buildingSites[b.id]=site;}buildingSiteOwners[b.id]=b.id;
            var sections=site.HighRelief||site.RoofBurialRisk?(scopedTownSupport800==null?BuildingSections.Create(Region,b,MeshSurfaceHeight):BuildingSections.CreateScoped(Region,b,MeshSurfaceHeight,scopedTownSupport800.NearestPavement)):null;
            if(sections!=null&&sections.Sectioned)
            {
                foreach(var part in sections.Parts)foreach(var step in BuildingSectionSteps(root,part.Building,b,part.Site,sections.MajorAxis))yield return step;
            }
            else foreach(var step in BuildingSectionSteps(root,b,b,site,Vector3.zero))yield return step;
        }
        bool TryBuildingSite(GeoBuilding building,out BuildingSitePlan.Data site)
        {
            try{site=BuildingSitePlan.Create(Region,building,MeshSurfaceHeight,scopedTownSupport800==null?null:scopedTownSupport800.NearestPavement);return true;}
            catch(System.ArgumentException){site=null;CosmeticFeatureFallbacks++;return false;}
        }
        IEnumerable<int> BuildingSectionSteps(Transform root,GeoBuilding b,GeoBuilding source,BuildingSitePlan.Data site,Vector3 ridge)
        {
            buildingSites[b.id]=site;buildingSiteOwners[b.id]=source.id;float baseY=site.LevelFloorY;int n=b.points.Length-1;if(n<3)yield break;
            var facade=BuildingAppearanceMaterials.Wall(b);float repeat=BuildingAppearanceMaterials.WallRepeat(b);
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            for(int i=0;i<n;i++)
            {
                var a=b.points[i];var c=b.points[(i+1)%n];float length=new Vector2(c.x-a.x,c.z-a.z).magnitude;
                var topA=new Vector3(a.x,baseY+b.height,a.z);var topC=new Vector3(c.x,baseY+b.height,c.z);
                a.y=Mathf.Min(baseY-1,MeshSurfaceHeight(a.x,a.z)-.2f);c.y=Mathf.Min(baseY-1,MeshSurfaceHeight(c.x,c.z)-.2f);
                int at=uv.Count;Quad(v,t,uv,a,c,topA,topC,0);
                uv[at]=new Vector2(0,a.y/repeat);uv[at+1]=new Vector2(length/repeat,c.y/repeat);
                uv[at+2]=new Vector2(0,topA.y/repeat);uv[at+3]=new Vector2(length/repeat,topC.y/repeat);
                yield return 0;
            }
            float winding=0;for(int i=0;i<n;i++){var a=b.points[i];var c=b.points[(i+1)%n];winding+=a.x*c.z-c.x*a.z;}
            if(winding<0)for(int i=0;i<t.Count;i+=3){int swap=t[i];t[i]=t[i+2];t[i+2]=swap;}
            var walls=Geometry.Mesh("Building "+b.id+(b.measuredHeight?" (mapped height)":" (estimated height)"),root,v.ToArray(),t.ToArray(),uv.ToArray(),facade,true);
            walls.GetComponent<MeshFilter>().sharedMesh.RecalculateTangents();yield return 0;
            BuildingRoofGeometry.Data roofData=null;
            try{roofData=BuildingRoofGeometry.Create(b.points,baseY+b.height,b.roof,ridge);}
            catch(System.ArgumentException){CosmeticFeatureFallbacks++;}
            if(roofData!=null&&!roofData.Omitted)
            {
                var roofObject=Geometry.Mesh("Slate roof"+(roofData.Pitched?" (pitched)":" (conservative flat)"),walls.transform,roofData.Vertices,roofData.Triangles,roofData.UV,roof,false);
                roofObject.GetComponent<MeshFilter>().sharedMesh.RecalculateTangents();yield return 0;
                if(roofData.GableTriangles.Length>0)
                {
                    var gableUV=roofData.GableUV;for(int i=0;i<gableUV.Length;i++)gableUV[i]/=repeat;
                    var ends=Geometry.Mesh("Roof gable ends",walls.transform,roofData.GableVertices,roofData.GableTriangles,gableUV,facade,false);
                    ends.GetComponent<MeshFilter>().sharedMesh.RecalculateTangents();yield return 0;
                }
            }
            else if(roofData!=null)CosmeticFeatureFallbacks++;
            bool Exterior(Vector3 point)
            {
                if(GeoRegion.Contains(source.points,new Vector2(point.x,point.z)))return false;
                if(buildings.TryGetValue(Key(point),out var neighbors))foreach(var other in neighbors)if(other!=source&&GeoRegion.Contains(other.points,new Vector2(point.x,point.z)))return false;
                if(scopedTownSupport800!=null)return scopedTownSupport800.ClearPoint(point,.14f);var adjacent=Region.ProjectRoad(point);return adjacent.Road==null||adjacent.EdgeDistance>.14f;
            }
            BuildingFacadeChunk details=null;
            foreach(var opening in BuildingFacadePlan.Create(Region,b,baseY,Exterior,MeshSurfaceHeight,EntryAudit.Enabled?EntryAudit:null,source.id,scopedTownSupport800==null?null:scopedTownSupport800.NearestPavement))
            {
                if(!details){details=root.GetComponent<BuildingFacadeChunk>();if(!details)details=root.gameObject.AddComponent<BuildingFacadeChunk>();}
                var entry=opening.Door?EntryAudit.Find(opening.Id):null;
                if(!details.CheckCapacity()){EntryAudit.Finish(entry,"FacadeCapacity");yield return 0;continue;}
                if(opening.Door)
                {
                    var threshold=opening.Position-Vector3.up*(opening.Height*.5f);var forward=opening.Rotation*Vector3.forward;threshold+=forward*.1f;
                    bool ClearAccess(Vector3 point)
                    {
                        if(!Exterior(point)||Region.IsWater(point.x,point.z))return false;
                        if(scopedTownSupport800!=null)return scopedTownSupport800.ClearAccess(point);
                        var adjacent=Region.ProjectRoad(point,false);
                        return adjacent.Road==null||adjacent.EdgeDistance>RoadBankReach(adjacent.Road)+.01f;
                    }
                    float nearGround=MeshSurfaceHeight(threshold.x,threshold.z),target=nearGround;
                    if(entry!=null){entry.accessKnown=true;entry.nearGap=threshold.y-nearGround;entry.targetDrop=threshold.y-target;}
                    BuildingAccessGeometry.Data flight=null;
                    if(threshold.y-nearGround>=BuildingAccessGeometry.MinimumDrop)
                    {
                        System.Action<BuildingAccessGeometry.SearchAttempt> observe=entry==null?null:new System.Action<BuildingAccessGeometry.SearchAttempt>(entry.AddAttempt);
                        bool found=BuildingAccessGeometry.TryFindStepFlight(threshold,opening.Rotation,opening.Width+.12f,nearGround,ClearAccess,MeshSurfaceHeight,polygon=>scopedTownSupport800!=null?!scopedTownSupport800.IntersectsAccessPavement(polygon):!Region.FootprintOverlapsRoadWithMargins(polygon,r=>RoadBankReach(r)+.01f),out flight,out var attempt,observe);
                        if(entry!=null){entry.targetDrop=attempt.TargetDrop;entry.stairCount=attempt.Count;entry.targetIterations=attempt.Iterations;entry.selectedVariant=attempt.Variant==0?"Low":attempt.Variant==1?"Mid":"High";entry.geometryReason=attempt.Reason.ToString();}
                        if(!found){EntryAudit.Finish(entry,attempt.RoadBlocked?"RoadBankReject":attempt.Reason==BuildingAccessGeometry.Rejection.Count?"ReachReject":"GeometryReject");yield return 0;continue;}
                        var access=root.GetComponent<BuildingAccessChunk>();if(!access)access=root.gameObject.AddComponent<BuildingAccessChunk>();
                        if(!access.Append(flight)){EntryAudit.Finish(entry,"BatchCapacity");yield return 0;continue;}
                    }
                    bool accepted=details.AddDoor(opening.Id,opening.Position,opening.Rotation,opening.Width,opening.Height,opening.Palette);EntryAudit.Finish(entry,accepted?(flight==null?"AcceptedDirect":"AcceptedFlight"):"AddDoorReject");
                }
                else details.AddWindow(opening.Id,opening.Position,opening.Rotation,opening.Width,opening.Height);
                yield return 0;
            }
        }
        static int[] DoubleSided(List<int> t){int count=t.Count;for(int i=0;i<count;i+=3)t.AddRange(new[]{t[i+2],t[i+1],t[i]});return t.ToArray();}
        public static int[] Triangulate(Vector3[] p)
        {
            var indices=new List<int>();float area=0;for(int i=0;i<p.Length;i++){var a=p[i];var b=p[(i+1)%p.Length];area+=a.x*b.z-b.x*a.z;}
            if(area<0)for(int i=0;i<p.Length;i++)indices.Add(i);else for(int i=p.Length-1;i>=0;i--)indices.Add(i);
            var t=new List<int>();int guard=p.Length*p.Length;
            while(indices.Count>2&&guard-->0)
            {
                bool cut=false;for(int i=0;i<indices.Count;i++)
                {
                    int a=indices[(i+indices.Count-1)%indices.Count],b=indices[i],c=indices[(i+1)%indices.Count];
                    if(Cross(p[b]-p[a],p[c]-p[b])>=-.0001f)continue;
                    bool inside=false;foreach(int q in indices)if(q!=a&&q!=b&&q!=c&&Cross(p[b]-p[a],p[q]-p[a])<0&&Cross(p[c]-p[b],p[q]-p[b])<0&&Cross(p[a]-p[c],p[q]-p[c])<0){inside=true;break;}
                    if(inside)continue;t.AddRange(new[]{a,b,c});indices.RemoveAt(i);cut=true;break;
                }
                if(!cut)break;
            }
            return t.ToArray();
        }
        static float Cross(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
        float TerrainGridSpacing=>Region!=null&&Region.roadWidthSchema==1?1.25f:2.5f;
        public float MeshSurfaceHeight(float x,float z)
        {
            if(scopedTownSupport800!=null){if(townSupportCurrent800==null||!townSupportCurrent800())throw new MissingScopedSupportException(x,z);return scopedTownSupport800.RequireHeight(x,z);}
            if(scopedTownRejectQueries800)throw new MissingScopedSupportException(x,z);
            if(TryScopedMeshGround(x,z,out float verifiedY))return verifiedY;
            if(retainingGaps!=null&&retainingGaps.TryHeight(x,z,out float retainingHeight))return retainingHeight;
            return LegacyTerrainMeshHeight(x,z);
        }
        // The unmodified terrain triangle evaluator is also used to intersect
        // exposed bank planes; it must not recursively query the retaining overlay.
        float LegacyTerrainMeshHeight(float x,float z)
        {
            float spacing=TerrainGridSpacing,x0=Mathf.Floor(x/spacing)*spacing,z0=Mathf.Floor(z/spacing)*spacing,tx=(x-x0)/spacing,tz=(z-z0)/spacing;
            float a=TerrainHeightAtGrid(x0,z0),b=TerrainHeightAtGrid(x0+spacing,z0),c=TerrainHeightAtGrid(x0, z0+spacing),d=TerrainHeightAtGrid(x0+spacing,z0+spacing);
            return tx+tz<=1?a*(1-tx-tz)+b*tx+c*tz:d*(tx+tz-1)+b*(1-tz)+c*(1-tx);
        }
        void Vegetation(Transform root,Vector2Int key){foreach(var step in VegetationSteps(root,key)){} }
        IEnumerable<int> VegetationSteps(Transform root,Vector2Int key)
        {
            if(mappedTrees.TryGetValue(key,out var accepted))
            {
                var woodland=root.gameObject.AddComponent<DetailedWoodlandChunk>();
                foreach(var tree in accepted){if(Region.CircuitScenery?.BlocksVegetation(tree.x,tree.z)==true||Region.CircuitRoadSurfaces?.TryHeight(tree.x,tree.z,out _)==true)continue;woodland.Add(tree,new Vector3(tree.x,MeshSurfaceHeight(tree.x,tree.z),tree.z));yield return 0;}
            }
            var random=new System.Random(unchecked(key.x*73856093^key.y*19349663^721));
            for(int i=0;i<85;i++)
            {
                yield return 0;
                float x=(key.x+(float)random.NextDouble())*Size,z=(key.y+(float)random.NextDouble())*Size;int land=Region.Land(x,z);
                if(land!=2&&(land!=0||random.NextDouble()>.09))continue;
                bool compiledCoverage=false;foreach(var coverage in Region.canopyCoverage??System.Array.Empty<GeoCanopyCoverage>())if(DetailCanopyPacks.Contains(coverage,x,z)){compiledCoverage=true;break;}if(compiledCoverage)continue;
                if(Region.IsWater(x,z))continue;
                if(Region.CircuitScenery?.BlocksVegetation(x,z)==true||Region.CircuitRoadSurfaces?.TryHeight(x,z,out _)==true)continue;
                var p=new Vector3(x,MeshSurfaceHeight(x,z),z);var roadProjection=Region.ProjectRoad(p);
                if(roadProjection.Road!=null&&roadProjection.EdgeDistance<12)continue;
                if(Region.RailwayIntersectsCircle(p,6.5f))continue;
                if(Mathf.Abs(Region.RawHeight(x+10,z)-Region.RawHeight(x-10,z))>14)continue;
                bool occupied=false;if(buildings.TryGetValue(key,out var nearby))foreach(var building in nearby)if(GeoRegion.Contains(building.points,new Vector2(x,z))){occupied=true;break;}if(occupied)continue;
                float h=7+(float)random.NextDouble()*8;
                NatureMeshes.Tree(root,p,h,i+key.x*7+key.y*13,bark,leaves);
            }
        }
    }
}
