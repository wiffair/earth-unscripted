using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoadRush.FacadePersistentCandidate800
{
    internal sealed class FacadeBudgetExhausted800 : Exception
    { internal FacadeBudgetExhausted800(string reason):base(reason){} }
    [Serializable] public sealed class FacadeBoundsObservation800
    {
        public string SourceMesh, SourceMeshEntityId64, MaterialEntityId64, ShaderEntityId64, ShaderName;
        public Bounds SourceLocalBounds, OwnedLocalBounds, RendererWorldBounds;
        public bool SourceBoundsContainStreams, OwnedBoundsContainStreams;
        public double SourceMaximumOverflowMetres;
        public int Vertices, Indices;
    }
    [Serializable] public sealed class FacadePersistentSnapshot800
    {
        public int Frame, SourceChunks, ResidentFeatures, VisibleWindows, VisibleDoors, NearFeatures, FarFeatures;
        public int NativeReadReservations; // Reserved native observations in this exact completed frame; not CPU/GPU time.
        public int OwnedMeshes, OwnedVertices, OwnedIndices, RebuiltVerticesThisFrame;
        public long RawBytes;
        // Existing OwnedMeshes is the legacy BATCH count, not the two-buffer pool.
        public int OwnedBufferMeshes;
        // Diagnostic history only: never used to authorize readiness/rendering.
        public int LifetimeOwnedMeshAllocations;
        public long SuccessfulMeshRebuildCommits;
        public long SourceCopyManagedRawBytes, BookkeepingManagedRawBytes;
        public long PendingBuildManagedRawBytes, PeakBuildManagedRawBytes, TotalModeledRawBytes;
        public bool AllReady, Incomplete, BoundsValid, SourceBoundsAllContainStreams, CameraMainOnly;
        public string Reason;
        public string[] CurrentCameraCullingMatrixF32Bits;
        public FacadeBoundsObservation800[] SourceAndOwnedBounds;
    }

    /// <summary>
    /// DEFAULT-OFF visual experiment over actual BFCs, never a world/owner issuer.
    /// Requires the guarded same-assembly BFC proposal. No reflection, scene search,
    /// one-feature fixture, material/offset/shader/collider changes or fallback claim.
    /// ALL current-frame selected batches must be ready before ANY owned renderer
    /// is enabled. Cold progress survives budget refusal, but partial/stale visuals
    /// remain hidden. Main-thread only; fullTown native/performance adoption pending.
    /// </summary>
    [DefaultExecutionOrder(350)]
    public sealed class FacadePersistentTown800 : MonoBehaviour, IDisposable
    {
        public const int MaximumSources = 2500;
        public const int MaximumCachedBatches = 4096;
        public const int MaximumRebuildVerticesPerFrame = 200000;
        public const int MaximumResidentVertices = 2000000;
        public const int MaximumResidentIndices = 3000000;
        public const long MaximumResidentRawBytes = 256L * 1024 * 1024;
        public const int MaximumNativeReadsPerFrame = 4096;
        // Each source check reads Unity-object liveness and isActiveAndEnabled.
        private const int NativeReadReservationsPerSource = 2;
        // Every selected snapshot row reads BOTH native bounds, with no preceding
        // fake-null tests. Missing/destroyed objects throw and refuse the latch.
        private const int NativeReadReservationsPerBoundsRow = 2;
        private const int SnapshotFixedNativeReadReservations = 8;
        public const int MaximumLifetimeMeshAllocations = 65536;
        public const double MaximumMainThreadBuildMillisecondsPerFrame = 6;
        private readonly System.Diagnostics.Stopwatch buildClock = new System.Diagnostics.Stopwatch();
        private readonly Dictionary<BuildingFacadeChunk,int> sourceOrdinals = new Dictionary<BuildingFacadeChunk,int>();
        private readonly Dictionary<long,FacadePersistentBatch800> batches = new Dictionary<long,FacadePersistentBatch800>();
        private readonly Dictionary<Mesh,FacadePersistentBatch800.SourceData> sourceData = new Dictionary<Mesh,FacadePersistentBatch800.SourceData>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private BuildingFacadeChunk[] sources;
        private int[] sourceFrames;
        private GameObject stage;
        private Camera frameCamera;
        private int thread, frame = -1, activationFrame, nativeReads, rebuiltVertices, residentVertices, residentIndices;
        private long residentRawBytes;
        private long successfulMeshRebuildCommits;
        private long sourceCopyManagedRawBytes, bookkeepingManagedRawBytes;
        private long pendingBuildManagedRawBytes, peakBuildManagedRawBytes;
        // Bounded raw-payload allowance for the controller's small fixed arrays,
        // dictionary/list references and two simultaneous snapshot row generations.
        // This is NOT a claim about Unity/driver/GC-reserved OS memory.
        private const long ControllerManagedRawAllowance = 4L * 1024 * 1024;
        private const long ManagedBuildMetadataAllowanceBytes = 4096;
        private const long MaximumSourceCopyRawBytes = 4096L * 32 + 16384L * 4;
        private long TotalModeledRawBytes => residentRawBytes + sourceCopyManagedRawBytes
            + bookkeepingManagedRawBytes + ControllerManagedRawAllowance + pendingBuildManagedRawBytes;
        private bool initialized, requested, disposed, frameFailed, eventsInstalled, terminalFailure;
        private int residentFeatures, windows, doors, nearFeatures, farFeatures;
        private FacadePersistentSnapshot800 lastRenderedSnapshot;
        public int LastReadyFrame { get; private set; } = -1;
        public int LastMainRenderedFrame { get; private set; } = -1;
        public bool CandidateRequested => requested && !disposed;
        public bool AllReady { get; private set; }
        public bool Incomplete { get; private set; }
        public string Reason { get; private set; } = "Default OFF, not attached";
        public bool OwnedResourcesReleased
        {
            get
            {
                if (!disposed) return false;
                foreach (Mesh mesh in ownedMeshes) if (mesh) return false;
                foreach (var batch in batches.Values) if (!batch.OwnedBuffersReleased) return false;
                return ReferenceEquals(stage,null) || !stage;
            }
        }

        public static bool TryAttach(GameObject ownedHost, BuildingFacadeChunk[] actualSources,
            out FacadePersistentTown800 controller, out string reason)
        {
            controller = null; reason = "Bounded actual source list required";
            if (!ownedHost || actualSources == null || actualSources.Length == 0 || actualSources.Length > MaximumSources) return false;
            FacadePersistentTown800 pending = null;
            try
            {
                pending = ownedHost.AddComponent<FacadePersistentTown800>();
                pending.Initialize(actualSources);
                controller = pending; reason = "Attached default OFF; visual candidate not accepted"; return true;
            }
            catch (Exception error)
            {
                reason = "Persistent candidate attach refused: " + error.Message;
                if (pending) pending.Dispose();
                return false;
            }
        }
        private void Initialize(BuildingFacadeChunk[] actualSources)
        {
            thread = Thread.CurrentThread.ManagedThreadId;
            sources = (BuildingFacadeChunk[])actualSources.Clone();
            sourceFrames = new int[sources.Length];
            for (int index=0;index<sources.Length;index++)
            {
                var source = sources[index];
                if (!source || source.gameObject.scene != gameObject.scene || sourceOrdinals.ContainsKey(source))
                    throw new InvalidOperationException("Actual distinct same-scene BFC required");
                sourceOrdinals.Add(source,index); sourceFrames[index] = -1;
            }
            stage = new GameObject("OWNED persistent facade candidate batches");
            SceneManager.MoveGameObjectToScene(stage,gameObject.scene);
            // World-baked vertices use an INDEPENDENT identity stage. No TRS fit.
            stage.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            stage.transform.localScale = Vector3.one;
            foreach (var source in sources) source.AttachPersistentCandidate(this);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            eventsInstalled = true; initialized = true;
            Reason = "Attached DEFAULT OFF; original BFC behavior retained";
        }
        public void SetEnabled(bool enabled)
        {
            RequireThread(); if (disposed) throw new ObjectDisposedException(nameof(FacadePersistentTown800));
            HideAll(); requested = enabled; AllReady = false; Incomplete = false;
            lastRenderedSnapshot=null;LastReadyFrame=LastMainRenderedFrame=-1;
            activationFrame = Time.frameCount + 1;
            Reason = enabled ? "Candidate requested; settle next frame and await ALL batches" : "Original requested; settle one complete frame";
        }
        private void BeginFrame()
        {
            int current = Time.frameCount;
            if (current == frame) return;
            frame = current; nativeReads = rebuiltVertices = 0; frameFailed = false;
            residentFeatures = windows = doors = nearFeatures = farFeatures = 0;
            frameCamera = null; AllReady = false; Incomplete = terminalFailure;frameFailed=terminalFailure; HideAll();
        }
        internal void BeginSource(BuildingFacadeChunk source, Camera camera, int featureCount)
        {
            RequireThread(); BeginFrame();
            if (!sourceOrdinals.TryGetValue(source,out int ordinal)) { Refuse("Unknown source chunk"); return; }
            sourceFrames[ordinal] = frame; residentFeatures += featureCount;
            if (!camera) { Refuse("No Camera.main: old source submits nothing"); return; }
            if (frameCamera && !ReferenceEquals(frameCamera,camera)) { Refuse("Camera.main changed during source selection"); return; }
            frameCamera = camera;
        }
        internal void SelectedFeature(BuildingFacadeChunk source, bool door, bool near)
        { if (door) doors++; else windows++; if (near) nearFeatures++; else farFeatures++; }
        internal void Append(BuildingFacadeChunk source, int group, Mesh mesh, Material material, bool near, int layer,
            Matrix4x4[] matrices, int[] featureIndices, int count)
        {
            RequireThread();
            if (disposed || !requested || !sourceOrdinals.TryGetValue(source,out int ordinal)) return;
            if (count <= 0 || count > 128 || group < 0 || group >= 24) { Refuse("Whole bounded group required"); return; }
            try
            {
                // Fixed original feature pages avoid shifting every later batch when
                // one feature crosses near/far/cull thresholds. Each page<=128parts.
                for (int index=0;index<count;index++)
                {
                    int feature = featureIndices[index];
                    if (feature < 0 || feature >= BuildingFacadeChunk.Capacity) throw new InvalidOperationException("Feature ordinal refused");
                    int page = feature / 128;
                    long key = ((long)ordinal * 24 + group) * 32 + page;
                    if (!batches.TryGetValue(key,out var batch))
                    {
                        if (batches.Count >= MaximumCachedBatches) throw new InvalidOperationException("Whole candidate cache cap");
                        ReserveBatchBookkeeping();
                        batch = new FacadePersistentBatch800(stage.transform); batches.Add(key,batch);
                    }
                    batch.Append(frame,mesh,material,near,layer,feature,matrices[index]);
                }
            }
            catch (Exception error) { Poison(error.Message); }
        }
        private void LateUpdate()
        {
            if (!initialized || disposed) return;
            if (!requested) return;
            RequireThread(); BeginFrame();
            if (frame < activationFrame) return;
            if(terminalFailure){Refuse("Terminal candidate failure; original mode or explicit new lifetime required");return;}
            try
            {
                ReserveNative(checked(sources.Length * NativeReadReservationsPerSource));
                for (int index=0;index<sources.Length;index++)
                    if (sources[index] && sources[index].isActiveAndEnabled && sourceFrames[index] != frame)
                        Refuse("Active source did not deliver entire current frame");
                // Build quantum starts AFTER gathering/old source selection. Starting
                // earlier could starve every cold mesh on a large unchanged Town.
                buildClock.Restart();
                foreach (var batch in batches.Values)
                {
                    if (batch.SeenFrame != frame) continue;
                    if (!batch.Prepare(this,frame)) Refuse("Cold/rebuild work pending; no partial rendering");
                }
                AllReady = !frameFailed && frameCamera;
                Incomplete = !AllReady;
                if(AllReady)LastReadyFrame=frame;
                Reason = AllReady ? "ALL selected batches staged; only current Camera.main may render" : Reason;
            }
            catch (Exception error)
            { if(error is FacadeBudgetExhausted800)Refuse(error.Message);else Poison("Persistent stage failed: "+error.Message); }
        }
        private bool SupportedPipeline()
        {
            ReserveNative(2);
            var asset = GraphicsSettings.currentRenderPipeline;
            return asset && asset.GetType().FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
        }
        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (disposed || !requested) return;
            HideAll();
            try
            {
                ReserveNative(4);
                if (!AllReady || frame != Time.frameCount || !ReferenceEquals(camera,frameCamera)
                    || !ReferenceEquals(camera,Camera.main)) return;
                if (!SupportedPipeline()) { Refuse("Only reviewed URP begin-before-cull pipeline supported"); return; }
                foreach (var batch in batches.Values) if (batch.SeenFrame == frame) batch.SetVisible(true);
            }
            catch (Exception error) { Refuse("Main-only rendering gate: " + error.Message); }
        }
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if(disposed||!initialized)return;
            try
            {
                if(!requested)
                {
                    int actual=Time.frameCount;
                    if(actual!=frame){frame=actual;nativeReads=0;}
                    ReserveNative(4);
                    if(ReferenceEquals(camera,Camera.main))
                    {
                        frameCamera=camera;
                        var original=BuildingFacadeChunk.Statistics;
                        windows=original.submittedWindows;doors=original.submittedDoors;residentFeatures=0;
                        foreach(var source in sources)if(source)residentFeatures+=source.FeatureCount;
                        lastRenderedSnapshot=BuildSnapshot();LastMainRenderedFrame=frame;
                    }
                }
                else if(AllReady&&ReferenceEquals(camera,frameCamera)&&frame==Time.frameCount)
                { lastRenderedSnapshot=BuildSnapshot();LastMainRenderedFrame=frame; }
            }
            catch(Exception error){Refuse("Snapshot latch refused: "+error.Message);}
            finally{HideAll();}
        }
        internal void SourceDisabled(BuildingFacadeChunk source)
        { if (!disposed) Refuse("Actual source disabled; no stale owned visuals"); }
        internal void ReserveNative(int count)
        {
            if (count < 0 || nativeReads > MaximumNativeReadsPerFrame-count) throw new FacadeBudgetExhausted800("Native read budget");
            nativeReads += count;
        }
        internal bool ReserveBuild(int vertices, int indices, int standbyVertexCapacity, int standbyIndexCapacity)
        {
            if (pendingBuildManagedRawBytes != 0)
                throw new InvalidOperationException("A synchronous facade upload reservation is already active");
            int vertexGrowth = Math.Max(vertices, standbyVertexCapacity) - standbyVertexCapacity;
            int indexGrowth = Math.Max(indices, standbyIndexCapacity) - standbyIndexCapacity;
            long nativeGrowth = vertexGrowth * 32L + indexGrowth * 4L;
            // Position/normal/UV upload arrays (32B/vertex), exact position readback
            // (12B/vertex), and index array. Fixed membership arrays are separate.
            // Small array headers and per-upload metadata strings are included as
            // a bounded conservative raw allowance, not allocator measurement.
            long managedBuild = vertices * 44L + indices * 4L + ManagedBuildMetadataAllowanceBytes;
            if (buildClock.Elapsed.TotalMilliseconds > MaximumMainThreadBuildMillisecondsPerFrame) return false;
            if (vertices > MaximumRebuildVerticesPerFrame - rebuiltVertices) return false;
            if (residentVertices + vertexGrowth > MaximumResidentVertices
                || residentIndices + indexGrowth > MaximumResidentIndices
                || TotalModeledRawBytes + nativeGrowth + managedBuild > MaximumResidentRawBytes) return false;
            rebuiltVertices += vertices;
            pendingBuildManagedRawBytes = managedBuild;
            peakBuildManagedRawBytes = Math.Max(peakBuildManagedRawBytes, managedBuild);
            return true;
        }
        internal void CommitBufferCapacity(int vertices, int indices, int previousVertices, int previousIndices)
        {
            // Resource accounting commits BEFORE upload. A failed partial upload
            // remains charged conservatively and owned until final Dispose.
            int vertexGrowth = Math.Max(vertices, previousVertices) - previousVertices;
            int indexGrowth = Math.Max(indices, previousIndices) - previousIndices;
            residentVertices += vertexGrowth; residentIndices += indexGrowth;
            residentRawBytes += vertexGrowth * 32L + indexGrowth * 4L;
        }
        internal void RecordSuccessfulMeshRebuildCommit()
        {
            // Called only after the batch's complete validated publication. An
            // unchanged reuse or any refused/failed generation never increments.
            successfulMeshRebuildCommits = checked(successfulMeshRebuildCommits + 1);
        }
        internal void ReleaseBuildReservation() { pendingBuildManagedRawBytes = 0; }
        private void ReserveBatchBookkeeping()
        {
            long bytes = FacadePersistentBatch800.PersistentManagedRawBytes;
            if (TotalModeledRawBytes + bytes > MaximumResidentRawBytes)
                throw new InvalidOperationException("Complete facade bookkeeping raw-memory cap");
            bookkeepingManagedRawBytes += bytes;
        }
        private void Poison(string reason){terminalFailure=true;Refuse(reason);}
        internal FacadePersistentBatch800.SourceData GetSource(Mesh mesh)
        {
            if (sourceData.TryGetValue(mesh, out var data)) return data;
            // SourceData's existing layout limits bound the temporary managed copy
            // before Capture allocates. No native source data is mutated/replaced.
            if (TotalModeledRawBytes + MaximumSourceCopyRawBytes > MaximumResidentRawBytes)
                throw new InvalidOperationException("Complete facade source-copy raw-memory cap");
            ReserveNative(32);
            data = FacadePersistentBatch800.SourceData.Capture(mesh);
            sourceCopyManagedRawBytes += data.Positions.Length * 32L + data.Indices.Length * 4L;
            sourceData.Add(mesh, data);
            return data;
        }
        internal void Track(Mesh mesh)
        {
            // At most two actual allocations per cached batch. The original finite
            // lifetime fence remains as an additional error bound, never reset.
            if (ownedMeshes.Count >= MaximumCachedBatches * 2
                || ownedMeshes.Count >= MaximumLifetimeMeshAllocations)
                throw new InvalidOperationException("Owned two-buffer/lifetime mesh allocation cap");
            ownedMeshes.Add(mesh);
        }
        private void HideAll() { foreach (var batch in batches.Values) batch.SetVisible(false); }
        private void Refuse(string reason)
        { if(!frameFailed)HideAll();frameFailed = true; AllReady = false; Incomplete = true; Reason = reason; }
        public FacadePersistentSnapshot800 CaptureSnapshot()
        {
            RequireThread();if(disposed)throw new ObjectDisposedException(nameof(FacadePersistentTown800));
            if(lastRenderedSnapshot!=null)return lastRenderedSnapshot;
            return BuildSnapshot();
        }
        private FacadePersistentSnapshot800 BuildSnapshot()
        {
            RequireThread();int visibleBatches=0;foreach(var batch in batches.Values)if(batch.SeenFrame==frame&&batch.HasCommittedGeometry)visibleBatches++;
            ReserveNative(checked(SnapshotFixedNativeReadReservations + visibleBatches * NativeReadReservationsPerBoundsRow));
            var rows = new List<FacadeBoundsObservation800>();
            bool valid = true,sourceValid=true;
            foreach (var batch in batches.Values)
                if (batch.SeenFrame == frame && batch.HasCommittedGeometry)
                { var row = batch.ObserveBounds(); rows.Add(row); valid &= row.OwnedBoundsContainStreams;sourceValid &= row.SourceBoundsContainStreams; }
            string[] bits = null;
            if (frameCamera)
            {
                Matrix4x4 matrix = frameCamera.cullingMatrix;
                bits = new string[16];for(int row=0;row<4;row++)for(int column=0;column<4;column++)bits[row*4+column]=BitConverter.SingleToInt32Bits(matrix[row,column]).ToString("x8",System.Globalization.CultureInfo.InvariantCulture);
            }
            return new FacadePersistentSnapshot800 { Frame=frame,SourceChunks=sources==null?0:sources.Length,
                ResidentFeatures=residentFeatures,VisibleWindows=windows,VisibleDoors=doors,NearFeatures=nearFeatures,FarFeatures=farFeatures,
                OwnedMeshes=batches.Count,OwnedBufferMeshes=ownedMeshes.Count,
                LifetimeOwnedMeshAllocations=ownedMeshes.Count,SuccessfulMeshRebuildCommits=successfulMeshRebuildCommits,
                SourceCopyManagedRawBytes=sourceCopyManagedRawBytes,BookkeepingManagedRawBytes=bookkeepingManagedRawBytes,
                PendingBuildManagedRawBytes=pendingBuildManagedRawBytes,PeakBuildManagedRawBytes=peakBuildManagedRawBytes,
                TotalModeledRawBytes=TotalModeledRawBytes,OwnedVertices=residentVertices,OwnedIndices=residentIndices,RawBytes=residentRawBytes,
                RebuiltVerticesThisFrame=rebuiltVertices,NativeReadReservations=nativeReads,AllReady=AllReady,Incomplete=Incomplete,BoundsValid=valid,
                SourceBoundsAllContainStreams=sourceValid,CameraMainOnly=true,Reason=Reason,CurrentCameraCullingMatrixF32Bits=bits,SourceAndOwnedBounds=rows.ToArray() };
        }
        public void Dispose()
        {
            if (disposed) return;
            RequireThread(); disposed = true; requested = false; AllReady = false;
            var failures = new List<Exception>();
            void Release(Action release)
            { try { release(); } catch (Exception error) { failures.Add(error); } }
            Release(HideAll);
            if (eventsInstalled)
            {
                Release(() => RenderPipelineManager.beginCameraRendering -= BeginCamera);
                Release(() => RenderPipelineManager.endCameraRendering -= EndCamera);
                eventsInstalled = false;
            }
            if (sources != null)
                foreach (var source in sources)
                    Release(() => { if (source) source.DetachPersistentCandidate(this); });
            // One failed batch cannot prevent BOTH buffers in all other batches
            // from receiving independent cleanup attempts before source retirement.
            foreach (var batch in batches.Values) Release(batch.Dispose);
            Release(() => { if (stage) Destroy(stage); });
            sourceData.Clear(); sourceOrdinals.Clear(); sources = null; sourceFrames = null;
            frameCamera = null; lastRenderedSnapshot = null;
            // Keep batch/native references for truthful deferred-release checks;
            // their Dispose has already released CPU backing arrays/source refs.
            Reason = "Owned two-buffer facade disposed; borrowed shared resources untouched";
            if (failures.Count != 0) throw new AggregateException("Facade resource cleanup failed", failures);
        }
        private void OnDisable(){if(initialized&&!disposed)Dispose();}
        private void OnDestroy(){if(initialized&&!disposed)Dispose();}
        private void RequireThread(){if(thread==0||thread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Actual main thread required");}
    }
}
