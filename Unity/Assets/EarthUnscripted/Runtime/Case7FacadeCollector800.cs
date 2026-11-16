#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EarthUnscripted.Scenery800.Diagnostics
{
    /// <summary>
    /// Explicit two-view submission ledger for the real Case7 inspector. It observes
    /// actual Queue/Flush values and the exact endCameraRendering camera. No pixels,
    /// source/placement authority, shader edits, drawing, colliders or world retirement.
    /// Main thread only; Dispose owns only this observer and render-event subscription.
    /// None returns before Unity reads, hooks and file I/O. No automatic installation.
    /// </summary>
    public sealed class Case7FacadeCollector800 : IDisposable, FacadeSubmissionHooks800.IObserver
    {
        public enum Mode { None, Full }
        public const int RequiredTargets = 25;
        public const int MaximumViews = 2;
        public const int MaximumFramesPerView = 4;
        public const int MaximumQueueRowsPerFrame = RequiredTargets * 3; // Door has frame/panel/handle.
        public const int MaximumBatchesPerView = MaximumQueueRowsPerFrame * MaximumFramesPerView;
        public const int MaximumMatricesPerBatch = 128;
        public const int MaximumGroups = 24;
        public const int MaximumMeshesPerFrame = 8;
        public const int MaximumMaterialsPerFrame = 8;
        public const int MaximumCamerasPerFrame = 4;
        public const int MaximumMeshVertices = 4096;
        public const int MaximumMeshIndices = 24576;
        public const int MaximumMaterialPasses = 16;
        public const int MaximumKeywords = 64;
        public const int MaximumTextCharacters = 256;
        public const int MaximumNativeGetterReservationsPerView = 32768;
        public const int MaximumOutputBytesPerView = 2 * 1024 * 1024;
        // Conservative reservations precede every native snapshot. These count
        // logical getter slots, not opaque engine-internal work or hard CPU time.
        private const int CameraReadReservation = 128;
        private const int MeshReadReservation = 256;
        private const int MaterialReadReservation = 256;
        private const int ChunkReadReservation = 32;
        private const double BoundsComparisonAllowanceMetres = .0001;
        public const string ExpectedTargetCohortSha256 = "752f87995c7c4d367ed1f8bb2c73b7572b82524c16db3d4680d7865fa72bc6c1";
        private const string Target1697 = "os-F8B1F936-0537-449A-B9E4-90DDA82669AE-p72221e4a90078e5ea19c-0/";
        private const string Target1005 = "os-87A8F192-726B-484D-8117-51F624AD46E8-pfc6fbd1655527f5792f6-0/";

        public Mode ObservationMode { get; }
        public bool Incomplete { get; private set; }
        public string Reason { get; private set; } = "Diagnostic not installed";
        public IReadOnlyList<string> OutputFiles => outputFiles;
        private readonly List<string> outputFiles = new List<string>();
        private readonly HashSet<string> targets;
        private readonly string outputDirectory;
        private readonly int threadId;
        private readonly HashSet<string> begunViews = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> faults = new List<string>();
        private readonly Dictionary<GroupKey, PendingBatch> pending = new Dictionary<GroupKey, PendingBatch>();
        private ViewState active;
        private long nextBatch;
        private bool installed, disposed;

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            internal readonly BuildingFacadeChunk Chunk;
            internal readonly int Group;
            internal GroupKey(BuildingFacadeChunk chunk, int group) { Chunk = chunk; Group = group; }
            public bool Equals(GroupKey other) => ReferenceEquals(Chunk, other.Chunk) && Group == other.Group;
            public override bool Equals(object other) => other is GroupKey key && Equals(key);
            public override int GetHashCode() => RuntimeHelpers.GetHashCode(Chunk) ^ Group;
        }
        private sealed class ViewState
        {
            internal string Name;
            internal Camera Camera;
            internal RenderTexture ReadbackTarget;
            internal int ReservedReads, Batches;
            internal readonly Dictionary<int, FrameState> Frames = new Dictionary<int, FrameState>();
        }
        private sealed class FrameState
        {
            internal int Number;
            internal bool Ended, AssociationMatches = true;
            internal readonly Dictionary<string, JObject> Rows = new Dictionary<string, JObject>(StringComparer.Ordinal);
            internal readonly Dictionary<string, HashSet<int>> ActualParts = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            internal readonly Dictionary<string, HashSet<int>> ExpectedParts = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            internal readonly JArray Batches = new JArray(), Meshes = new JArray(), Materials = new JArray(), Cameras = new JArray();
            internal readonly Dictionary<Mesh, string> MeshIds = new Dictionary<Mesh, string>(ReferenceComparer<Mesh>.Instance);
            internal readonly Dictionary<Material, string> MaterialIds = new Dictionary<Material, string>(ReferenceComparer<Material>.Instance);
            internal readonly List<CameraState> CameraObservations = new List<CameraState>();
            internal readonly Dictionary<Camera, CameraState> CameraStates = new Dictionary<Camera, CameraState>(ReferenceComparer<Camera>.Instance);
            internal JObject EndCamera;
        }
        private sealed class CameraState
        {
            internal string Id;
            internal Camera Camera;
            internal RenderTexture Target;
            internal JObject Data;
        }
        private sealed class PendingBatch
        {
            internal long Number;
            internal int Frame;
            internal readonly List<QueuedRow> Rows = new List<QueuedRow>();
        }
        private sealed class QueuedRow
        {
            internal int Slot;
            internal Matrix4x4 Matrix;
            internal Bounds Bounds;
            internal JObject Data;
        }

        private Case7FacadeCollector800(Mode mode, string directory, HashSet<string> ids)
        {
            ObservationMode = mode;
            outputDirectory = directory;
            targets = ids;
            threadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Full requires a fresh caller-owned existing private rr019 output directory
        /// and25 distinct IDs derived from the actual newly emitted Town (10+15).
        /// These IDs select observations, never mint an owner/source capability.
        /// None deliberately ignores even invalid paths/IDs without file/Unity reads.
        /// </summary>
        public static Case7FacadeCollector800 Create(Mode mode, string ownedOutputDirectory,
            IReadOnlyList<string> exactTargetIds)
        {
            if (mode == Mode.None) return new Case7FacadeCollector800(mode, null, null);
            if (mode != Mode.Full || exactTargetIds == null || exactTargetIds.Count != RequiredTargets)
                throw new ArgumentException("Full diagnostic requires25 actual target IDs");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int first = 0, second = 0;
            foreach (string id in exactTargetIds)
            {
                if (string.IsNullOrEmpty(id) || id.Length > MaximumTextCharacters || !ids.Add(id))
                    throw new ArgumentException("Target IDs must be bounded and distinct");
                if (id.StartsWith(Target1697, StringComparison.Ordinal)) first++;
                else if (id.StartsWith(Target1005, StringComparison.Ordinal)) second++;
                else throw new ArgumentException("Unexpected target source prefix");
            }
            if (first != 10 || second != 15) throw new ArgumentException("Actual target10/15 feature inventory changed");
            var orderedIds = new List<string>(ids);
            orderedIds.Sort(StringComparer.Ordinal);
            if (Hash(Encoding.UTF8.GetBytes(string.Join("\n", orderedIds) + "\n")) != ExpectedTargetCohortSha256)
                throw new ArgumentException("Actual selected feature IDs differ from reviewed diagnostic cohort");
            string fullPath = Path.GetFullPath(ownedOutputDirectory ?? "");
            if (!fullPath.StartsWith("/private/tmp/rr019-", StringComparison.Ordinal) || !Directory.Exists(fullPath))
                throw new ArgumentException("Existing private caller-owned rr019 output required");
            return new Case7FacadeCollector800(mode, fullPath, ids);
        }

        public void Install()
        {
            if (ObservationMode == Mode.None) return;
            RequireThread();
            if (disposed || installed || !FacadeSubmissionHooks800.TryInstall(this))
                throw new InvalidOperationException("Diagnostic observer slot unavailable");
            installed = true;
            RenderPipelineManager.endCameraRendering += RenderEnded;
            Reason = "Installed; awaiting two explicit close views";
        }

        /// <summary>Call immediately AFTER setting the actual camera pose, BEFORE
        /// the existing yielded render wait. No camera mutation is performed.</summary>
        public void BeginView(string viewName, Camera actualRtCamera)
            => BeginView(viewName, actualRtCamera, null);

        public void BeginView(string viewName, Camera actualRtCamera, RenderTexture actualReadbackTarget)
        {
            if (ObservationMode == Mode.None) return;
            RequireThread();
            if (disposed || !installed || active != null || begunViews.Count >= MaximumViews
                || (viewName != "affected-house" && viewName != "street")
                || !begunViews.Add(viewName) || ReferenceEquals(actualRtCamera, null) || ReferenceEquals(actualReadbackTarget, null))
                throw new InvalidOperationException("One explicit actual close view at a time");
            active = new ViewState { Name = viewName, Camera = actualRtCamera, ReadbackTarget = actualReadbackTarget };
        }

        private FrameState Frame(int frame)
        {
            if (active.Frames.TryGetValue(frame, out FrameState found)) return found;
            if (active.Frames.Count >= MaximumFramesPerView) throw new InvalidOperationException("Frame budget exhausted");
            found = new FrameState { Number = frame };
            active.Frames.Add(frame, found);
            return found;
        }
        private void Reserve(int logicalGetterSlots)
        {
            RequireThread();
            if (active == null || logicalGetterSlots < 0
                || active.ReservedReads > MaximumNativeGetterReservationsPerView - logicalGetterSlots)
                throw new InvalidOperationException("Native getter reservation budget exhausted");
            active.ReservedReads += logicalGetterSlots;
        }
        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Main-thread diagnostic only");
        }
        public void ObserverFault(string reason)
        {
            Incomplete = true;
            reason = reason ?? "Unknown diagnostic fault";
            Reason = reason.Length <= MaximumTextCharacters ? reason : reason.Substring(0, MaximumTextCharacters);
            if (faults.Count < 16) faults.Add(Reason);
        }

        public void Queued(BuildingFacadeChunk chunk, int group, int slot, int featureIndex,
            BuildingFacadeChunk.Feature feature, Matrix4x4 root, Matrix4x4 actualMatrix,
            Vector3 actualPosition, Bounds actualBounds, Camera capturedCamera, int frame)
        {
            if (disposed || active == null || !targets.Contains(feature.Id)) return;
            try
            {
                RequireThread();
                if (group < 0 || group >= MaximumGroups || slot < 0 || slot >= MaximumMatricesPerBatch)
                    throw new InvalidOperationException("Unsupported group/slot");
                FrameState data = Frame(frame);
                string partKey = feature.Id + "/diagnostic-group-" + group;
                if (data.Rows.Count >= MaximumQueueRowsPerFrame || data.Rows.ContainsKey(partKey))
                    throw new InvalidOperationException("Duplicate/overflow selected feature");
                if (!Finite(actualMatrix) || !Finite(root) || !Finite(actualBounds))
                    throw new InvalidOperationException("Nonfinite actual matrix/bounds");
                var key = new GroupKey(chunk, group);
                if (!pending.TryGetValue(key, out PendingBatch batch))
                {
                    if (active.Batches >= MaximumBatchesPerView) throw new InvalidOperationException("Batch budget exhausted");
                    active.Batches++;
                    batch = new PendingBatch { Number = ++nextBatch, Frame = frame };
                    pending.Add(key, batch);
                }
                if (batch.Frame != frame) throw new InvalidOperationException("Selected batch crossed a frame without Flush");
                Reserve(ChunkReadReservation); // Before GetEntityId or gameObject/layer getters.
                var row = new JObject {
                    ["featureId"] = feature.Id, ["featureIndex"] = featureIndex,
                    ["chunkEntityId64"] = Identity(chunk), ["group"] = group, ["slot"] = slot,
                    ["batch"] = batch.Number, ["door"] = feature.IsDoor, ["palette"] = feature.Palette,
                    ["widthMetres"] = feature.Width, ["heightMetres"] = feature.Height,
                    ["sourcePosition"] = Vector(feature.Position), ["rotation"] = QuaternionValues(feature.Rotation),
                    ["actualPosition"] = Vector(actualPosition), ["actualFeatureBounds"] = BoundsValues(actualBounds),
                    ["chunkRootF32Bits"] = MatrixBits(root), ["chunkRootLinearDeterminant"] = LinearDeterminant(root),
                    ["actualQueueMatrixF32Bits"] = MatrixBits(actualMatrix), ["flushed"] = false,
                    ["queueCameraExactExpectedReference"] = ReferenceEquals(capturedCamera, active.Camera) };
                HashSet<int> expectedParts = ExpectedParts(feature, group);
                if (!data.ActualParts.TryGetValue(feature.Id, out HashSet<int> actualParts))
                {
                    actualParts = new HashSet<int>();
                    data.ActualParts.Add(feature.Id, actualParts);
                    data.ExpectedParts.Add(feature.Id, expectedParts);
                }
                if (!data.ExpectedParts[feature.Id].SetEquals(expectedParts) || !actualParts.Add(group))
                    throw new InvalidOperationException("Selected feature has mixed/duplicate LOD parts");
                data.Rows.Add(partKey, row);
                batch.Rows.Add(new QueuedRow { Slot = slot, Matrix = actualMatrix, Bounds = actualBounds, Data = row });
                if (!ReferenceEquals(capturedCamera, active.Camera)) data.AssociationMatches = false;
            }
            catch (Exception error) { ObserverFault("Queue observation: " + error.GetType().Name + ": " + error.Message); }
        }

        public void Flushing(BuildingFacadeChunk chunk, int group, Mesh mesh, Material material,
            Matrix4x4[] borrowedMatrices, int count, Bounds wholeGroupBounds, Camera capturedCamera,
            bool instanced, ShadowCastingMode shadow, RenderParams? actualRenderParameters, int frame)
        {
            if (disposed || active == null) return;
            var key = new GroupKey(chunk, group);
            if (!pending.TryGetValue(key, out PendingBatch batch)) return; // No selected rows: no native getters.
            pending.Remove(key);
            try
            {
                RequireThread();
                if (count <= 0 || count > MaximumMatricesPerBatch || borrowedMatrices == null
                    || borrowedMatrices.Length < count || frame != batch.Frame || !Finite(wholeGroupBounds))
                    throw new InvalidOperationException("Unsupported actual Flush layout");
                FrameState data = Frame(frame);
                CameraState camera = ReadCamera(data, capturedCamera, false);
                string meshId = ReadMesh(data, mesh);
                string materialId = ReadMaterial(data, material);
                bool exactCamera = ReferenceEquals(capturedCamera, active.Camera);
                if (!exactCamera || !ReferenceEquals(camera.Target, active.ReadbackTarget)) data.AssociationMatches = false;
                // Copy/hash the actual bounded array synchronously; NEVER retain it.
                var matrixBytes = new byte[count * 16 * sizeof(float)];
                for (int index = 0; index < count; index++)
                {
                    if (!Finite(borrowedMatrices[index])) throw new InvalidOperationException("Nonfinite batch matrix");
                    CopyMatrixBits(borrowedMatrices[index], matrixBytes, index * 16 * sizeof(float));
                }
                foreach (QueuedRow row in batch.Rows)
                {
                    if (row.Slot >= count || !SameBits(row.Matrix, borrowedMatrices[row.Slot]))
                        throw new InvalidOperationException("Queue/Flush actual matrix mismatch");
                    if (!Encloses(wholeGroupBounds, row.Bounds))
                        throw new InvalidOperationException("Actual batch bounds omit selected feature bounds");
                    row.Data["flushed"] = true;
                    row.Data["actualFlushMatrixF32Bits"] = MatrixBits(borrowedMatrices[row.Slot]);
                }
                var flushed = new JObject {
                    ["batch"] = batch.Number, ["group"] = group, ["count"] = count,
                    ["mesh"] = meshId, ["material"] = materialId, ["camera"] = camera.Id,
                    ["exactExpectedCameraReference"] = exactCamera,
                    ["branch"] = instanced ? "RenderMeshInstanced" : "DrawMesh",
                    ["wholeGroupBounds"] = BoundsValues(wholeGroupBounds), ["shadow"] = shadow.ToString(),
                    ["actualMatrixArraySha256"] = Hash(matrixBytes), ["actualRenderParamsPresent"] = actualRenderParameters.HasValue };
                if (actualRenderParameters.HasValue)
                {
                    RenderParams parameters = actualRenderParameters.Value;
                    flushed["actualRenderParamsLayer"] = parameters.layer;
                    flushed["actualRenderParamsRenderingLayerMask"] = parameters.renderingLayerMask;
                    flushed["actualRenderParamsCameraExactReference"] = ReferenceEquals(parameters.camera, capturedCamera);
                    if (!ReferenceEquals(parameters.camera, capturedCamera)) data.AssociationMatches = false;
                }
                data.Batches.Add(flushed);
            }
            catch (Exception error) { ObserverFault("Flush observation: " + error.GetType().Name + ": " + error.Message); }
        }

        private CameraState ReadCamera(FrameState frame, Camera camera, bool forceEndSnapshot)
        {
            // Camera target/pose may change even with the same object reference.
            // Capture each selected Flush independently; never memoize association.
            if (!forceEndSnapshot && frame.Cameras.Count >= MaximumQueueRowsPerFrame)
                throw new InvalidOperationException("Camera snapshot budget exhausted");
            if (!frame.CameraStates.ContainsKey(camera) && frame.CameraStates.Count >= MaximumCamerasPerFrame)
                throw new InvalidOperationException("Camera budget exhausted");
            Reserve(CameraReadReservation); // Before every expanded native property argument below.
            RenderTexture target = camera.targetTexture;
            Transform transform = camera.transform;
            bool hasAdditional = camera.TryGetComponent(out UniversalAdditionalCameraData additional);
            string id = Identity(camera);
            var values = new JObject {
                ["entityId64"] = id, ["enabled"] = camera.enabled,
                ["activeInHierarchy"] = camera.gameObject.activeInHierarchy, ["tag"] = BoundText(camera.tag),
                ["cullingMask"] = camera.cullingMask, ["cameraType"] = camera.cameraType.ToString(),
                ["urpRenderType"] = hasAdditional ? additional.renderType.ToString() : "MISSING",
                ["targetTextureEntityId64"] = Identity(target),
                ["actualReadbackTargetEntityId64"] = Identity(active.ReadbackTarget),
                ["matchesActualReadbackTarget"] = ReferenceEquals(target, active.ReadbackTarget),
                ["targetWidth"] = ReferenceEquals(target, null) ? 0 : target.width,
                ["targetHeight"] = ReferenceEquals(target, null) ? 0 : target.height,
                ["position"] = Vector(transform.position), ["rotation"] = QuaternionValues(transform.rotation),
                ["worldToCameraF32Bits"] = MatrixBits(camera.worldToCameraMatrix),
                ["projectionF32Bits"] = MatrixBits(camera.projectionMatrix),
                ["cullingMatrixF32Bits"] = MatrixBits(camera.cullingMatrix),
                ["nearClipMetres"] = camera.nearClipPlane, ["farClipMetres"] = camera.farClipPlane,
                ["aspect"] = camera.aspect, ["fieldOfViewDegrees"] = camera.fieldOfView,
                ["forceEndSnapshot"] = forceEndSnapshot };
            if (!hasAdditional || !ReferenceEquals(target, active.ReadbackTarget)) frame.AssociationMatches = false;
            string snapshotId = id + "/diagnostic-snapshot-" + frame.Cameras.Count;
            values["diagnosticSnapshotId"] = snapshotId;
            var result = new CameraState { Id = snapshotId, Camera = camera, Target = target, Data = values };
            if (!forceEndSnapshot)
            {
                if (!frame.CameraStates.ContainsKey(camera)) frame.CameraStates.Add(camera, result);
                frame.CameraObservations.Add(result);
                frame.Cameras.Add(values);
            }
            return result;
        }

        private string ReadMesh(FrameState frame, Mesh mesh)
        {
            if (frame.MeshIds.TryGetValue(mesh, out string cached)) return cached;
            if (frame.MeshIds.Count >= MaximumMeshesPerFrame) throw new InvalidOperationException("Mesh budget exhausted");
            Reserve(MeshReadReservation);
            int count = mesh.vertexCount;
            if (count <= 0 || count > MaximumMeshVertices || mesh.subMeshCount != 1 || !mesh.isReadable)
                throw new InvalidOperationException("Unsupported mesh vertex/submesh/readability cap");
            ulong indexCount = mesh.GetIndexCount(0);
            if (indexCount > MaximumMeshIndices) throw new InvalidOperationException("Mesh index cap exhausted");
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.GetIndices(0);
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            if (vertices.Length != count || indices.LongLength != (long)indexCount
                || normals.Length > MaximumMeshVertices || uv.Length > MaximumMeshVertices)
                throw new InvalidOperationException("Mesh changed/unsupported buffer layout");
            foreach (Vector3 vertex in vertices)
                if (!Finite(vertex.x) || !Finite(vertex.y) || !Finite(vertex.z))
                    throw new InvalidOperationException("Nonfinite actual mesh vertex");
            foreach (int index in indices)
                if (index < 0 || index >= count) throw new InvalidOperationException("Actual mesh index outside vertex buffer");
            string id = Identity(mesh);
            frame.MeshIds.Add(mesh, id);
            frame.Meshes.Add(new JObject { ["entityId64"] = id, ["name"] = BoundText(mesh.name),
                ["vertices"] = count, ["indices"] = indexCount, ["bounds"] = BoundsValues(mesh.bounds),
                ["vertexF32Sha256"] = HashVectors(vertices), ["indexI32Sha256"] = HashInts(indices),
                ["normalF32Sha256"] = HashVectors(normals), ["uvF32Sha256"] = HashUv(uv) });
            return id;
        }

        private string ReadMaterial(FrameState frame, Material material)
        {
            if (frame.MaterialIds.TryGetValue(material, out string cached)) return cached;
            if (frame.MaterialIds.Count >= MaximumMaterialsPerFrame) throw new InvalidOperationException("Material budget exhausted");
            Reserve(MaterialReadReservation);
            Shader shader = material.shader;
            int passes = material.passCount;
            string[] keywords = material.shaderKeywords;
            if (passes < 0 || passes > MaximumMaterialPasses || keywords.Length > MaximumKeywords)
                throw new InvalidOperationException("Material pass/keyword cap exhausted");
            var passRows = new JArray();
            for (int index = 0; index < passes; index++)
            {
                string name = BoundText(material.GetPassName(index)); // Pass NAME, not LightMode tag.
                passRows.Add(new JObject { ["name"] = name, ["enabled"] = material.GetShaderPassEnabled(name) });
            }
            var keywordRows = new JArray();
            foreach (string keyword in keywords) keywordRows.Add(BoundText(keyword));
            var properties = new JObject();
            string[] scalarNames = { "_Surface", "_Cull", "_ZWrite", "_SrcBlend", "_DstBlend", "_AlphaClip" };
            foreach (string name in scalarNames) if (material.HasProperty(name)) properties[name] = material.GetFloat(name);
            foreach (string name in new[] { "_BaseColor", "_Color" })
                if (material.HasProperty(name)) { Color colour = material.GetColor(name); properties[name] = new JArray(colour.r, colour.g, colour.b, colour.a); }
            string id = Identity(material);
            frame.MaterialIds.Add(material, id);
            frame.Materials.Add(new JObject { ["entityId64"] = id, ["name"] = BoundText(material.name),
                ["shaderEntityId64"] = Identity(shader), ["shaderName"] = BoundText(shader.name),
                ["shaderSupported"] = shader.isSupported, ["enableInstancing"] = material.enableInstancing,
                ["systemSupportsInstancing"] = SystemInfo.supportsInstancing, ["renderQueue"] = material.renderQueue,
                ["passes"] = passRows, ["keywords"] = keywordRows, ["properties"] = properties });
            return id;
        }

        private void RenderEnded(ScriptableRenderContext context, Camera rendered)
        {
            if (disposed || active == null || !ReferenceEquals(rendered, active.Camera)) return;
            try
            {
                Reserve(4); // BEFORE Time.frameCount native access.
                FrameState frame = Frame(Time.frameCount);
                CameraState ended = ReadCamera(frame, rendered, true);
                frame.EndCamera = ended.Data;
                frame.Ended = true;
                if (!ReferenceEquals(ended.Target, active.ReadbackTarget)) frame.AssociationMatches = false;
                foreach (CameraState submission in frame.CameraObservations)
                    if (!ReferenceEquals(submission.Camera, rendered) || !ReferenceEquals(submission.Target, ended.Target))
                        frame.AssociationMatches = false;
            }
            catch (Exception error) { ObserverFault("endCameraRendering observation: " + error.GetType().Name + ": " + error.Message); }
        }

        public bool CompleteView(string viewName, Camera actualRtCamera, int actualRenderedFrame)
            => CompleteView(viewName, actualRtCamera, null, actualRenderedFrame, out _);

        /// <summary>Call AFTER the original RT readback, supplying its real rendered
        /// frame. True means selected submissions match that camera/RT/frame ONLY;
        /// it never means any pixel is visible. Writes a bounded CreateNew sidecar.</summary>
        public bool CompleteView(string viewName, Camera actualRtCamera, int actualRenderedFrame, out string reason)
            => CompleteView(viewName, actualRtCamera, null, actualRenderedFrame, out reason);

        public bool CompleteView(string viewName, Camera actualRtCamera, RenderTexture actualReadbackTarget,
            int actualRenderedFrame, out string reason)
        {
            if (ObservationMode == Mode.None) { reason = Reason = "Disabled; camera association not proved"; return false; }
            RequireThread();
            bool valid = false;
            try
            {
                if (disposed || active == null || active.Name != viewName || !ReferenceEquals(active.Camera, actualRtCamera)
                    || ReferenceEquals(actualReadbackTarget, null) || !ReferenceEquals(active.ReadbackTarget, actualReadbackTarget))
                    throw new InvalidOperationException("Actual close view/reference mismatch");
                if (!active.Frames.TryGetValue(actualRenderedFrame, out FrameState captured))
                    throw new InvalidOperationException("Actual captured frame has no selected submission ledger");
                valid = !Incomplete && captured.Ended && captured.AssociationMatches && captured.ActualParts.Count == RequiredTargets;
                foreach (string id in targets)
                    valid &= captured.ActualParts.TryGetValue(id, out HashSet<int> parts)
                        && parts.SetEquals(captured.ExpectedParts[id]);
                foreach (JObject row in captured.Rows.Values) valid &= row.Value<bool>("flushed");
                if (!valid) ObserverFault("Incomplete selected submission/camera/frame association");
                var frames = new JArray();
                foreach (FrameState frame in active.Frames.Values)
                    frames.Add(new JObject { ["frame"] = frame.Number, ["endedActualCamera"] = frame.Ended,
                        ["associationMatches"] = frame.AssociationMatches, ["targetFeatureCount"] = frame.ActualParts.Count,
                        ["queuePartRowCount"] = frame.Rows.Count, ["features"] = new JArray(frame.Rows.Values),
                        ["batches"] = frame.Batches, ["meshes"] = frame.Meshes, ["materials"] = frame.Materials,
                        ["submissionCameras"] = frame.Cameras, ["endCamera"] = frame.EndCamera });
                var result = new JObject { ["schema"] = "EarthUnscriptedCase7FacadeSubmissionLedger1", ["mode"] = "full",
                    ["selectionCohortSha256"] = ExpectedTargetCohortSha256,
                    ["meshMaterialSnapshotPolicy"] = "First selected Flush per object per frame; not a GPU variant witness",
                    ["view"] = viewName, ["actualCapturedFrame"] = actualRenderedFrame,
                    ["status"] = valid ? "PASS_SUBMISSION_CAMERA_ASSOCIATION_ONLY" : "INCOMPLETE",
                    ["visiblePixelsProved"] = false, ["gameplayAdmission"] = false,
                    ["nativeGetterReservations"] = active.ReservedReads, ["faults"] = new JArray(faults), ["frames"] = frames };
                Write(viewName, result);
            }
            catch (Exception error) { ObserverFault("CompleteView: " + error.GetType().Name + ": " + error.Message); valid = false; }
            finally { active = null; pending.Clear(); }
            reason = Reason = valid ? "Exact selected submission/camera association; no pixel acceptance" : Reason;
            return valid && !Incomplete;
        }

        private void Write(string name, JObject result)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(result.ToString(Formatting.Indented));
            if (bytes.Length > MaximumOutputBytesPerView)
            {
                ObserverFault("Output byte cap exceeded");
                bytes = Encoding.UTF8.GetBytes("{\"status\":\"INCOMPLETE_OUTPUT_LIMIT\",\"visiblePixelsProved\":false}");
            }
            string path = Path.Combine(outputDirectory, "case7-facade-" + name + ".json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
            outputFiles.Add(path);
        }

        public void Dispose()
        {
            if (disposed) return;
            if (ObservationMode == Mode.None) { disposed = true; return; }
            RequireThread(); // Wrong-thread caller may retry cleanup on the owner thread.
            disposed = true;
            try { if (installed && !FacadeSubmissionHooks800.Remove(this)) ObserverFault("Observer ownership changed before cleanup"); }
            catch (Exception error) { ObserverFault("Observer cleanup: " + error.GetType().Name); }
            try { RenderPipelineManager.endCameraRendering -= RenderEnded; }
            catch (Exception error) { ObserverFault("Render subscription cleanup: " + error.GetType().Name); }
            installed = false; active = null; pending.Clear();
        }

        // Queue is called per mesh part, not per feature: near window2, near
        // door3, far window2 and far door2. Require the whole actual LOD part set.
        private static HashSet<int> ExpectedParts(BuildingFacadeChunk.Feature feature, int group)
        {
            if (!feature.IsDoor)
            {
                if (group == 0 || group == 1) return new HashSet<int> { 0, 1 };
                if (group == 2 || group == 3) return new HashSet<int> { 2, 3 };
            }
            else if (feature.Palette >= 0 && feature.Palette < 4)
            {
                int first = 4 + feature.Palette * 5;
                if (group >= first && group <= first + 2) return new HashSet<int> { first, first + 1, first + 2 };
                if (group == first + 3 || group == first + 4) return new HashSet<int> { first + 3, first + 4 };
            }
            throw new InvalidOperationException("Unsupported feature/group association");
        }

        private static string BoundText(string text)
        {
            text = text ?? "";
            if (text.Length > MaximumTextCharacters) throw new InvalidOperationException("Native text cap exceeded");
            return text;
        }
        private static string Identity(UnityEngine.Object value) => ReferenceEquals(value, null) ? "NONE"
            : EntityId.ToULong(value.GetEntityId()).ToString(CultureInfo.InvariantCulture);
        private static JArray Vector(Vector3 value) => new JArray(value.x, value.y, value.z);
        private static JArray QuaternionValues(Quaternion value) => new JArray(value.x, value.y, value.z, value.w);
        private static JObject BoundsValues(Bounds value) => new JObject { ["centre"] = Vector(value.center), ["extents"] = Vector(value.extents) };
        private static float[] Elements(Matrix4x4 value) => new[] { value.m00,value.m01,value.m02,value.m03,
            value.m10,value.m11,value.m12,value.m13,value.m20,value.m21,value.m22,value.m23,value.m30,value.m31,value.m32,value.m33 };
        private static JArray MatrixBits(Matrix4x4 value)
        {
            var result = new JArray();
            foreach (float element in Elements(value)) result.Add(BitConverter.SingleToInt32Bits(element).ToString("x8", CultureInfo.InvariantCulture));
            return result;
        }
        private static void CopyMatrixBits(Matrix4x4 value, byte[] output, int offset)
        {
            foreach (float element in Elements(value))
            {
                uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(element));
                for (int index = 0; index < sizeof(float); index++) output[offset++] = (byte)(bits >> (index * 8));
            }
        }
        private static bool SameBits(Matrix4x4 a, Matrix4x4 b)
        {
            float[] left = Elements(a), right = Elements(b);
            for (int index = 0; index < left.Length; index++)
                if (BitConverter.SingleToInt32Bits(left[index]) != BitConverter.SingleToInt32Bits(right[index])) return false;
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Matrix4x4 value)
        { foreach (float element in Elements(value)) if (!Finite(element)) return false; return true; }
        private static bool Finite(Bounds value)
        {
            Vector3 centre = value.center, extent = value.extents;
            return Finite(centre.x) && Finite(centre.y) && Finite(centre.z) && Finite(extent.x) && Finite(extent.y) && Finite(extent.z)
                && extent.x >= 0 && extent.y >= 0 && extent.z >= 0;
        }
        private static bool Encloses(Bounds outer, Bounds inner)
        {
            Vector3 a = outer.center, ae = outer.extents, b = inner.center, be = inner.extents;
            return Math.Abs((double)a.x-b.x)+be.x <= ae.x+BoundsComparisonAllowanceMetres
                && Math.Abs((double)a.y-b.y)+be.y <= ae.y+BoundsComparisonAllowanceMetres
                && Math.Abs((double)a.z-b.z)+be.z <= ae.z+BoundsComparisonAllowanceMetres;
        }
        private static double LinearDeterminant(Matrix4x4 m) => (double)m.m00*(m.m11*(double)m.m22-m.m12*(double)m.m21)
            -m.m01*(m.m10*(double)m.m22-m.m12*(double)m.m20)+m.m02*(m.m10*(double)m.m21-m.m11*(double)m.m20);
        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static string HashVectors(Vector3[] values)
        {
            var bytes = new byte[values.Length * 3 * sizeof(float)]; int offset = 0;
            foreach (Vector3 value in values) foreach (float component in new[] { value.x,value.y,value.z }) Put(component, bytes, ref offset);
            return Hash(bytes);
        }
        private static string HashUv(Vector2[] values)
        {
            var bytes = new byte[values.Length * 2 * sizeof(float)]; int offset = 0;
            foreach (Vector2 value in values) { Put(value.x, bytes, ref offset); Put(value.y, bytes, ref offset); }
            return Hash(bytes);
        }
        private static string HashInts(int[] values)
        {
            var bytes = new byte[values.Length * sizeof(int)]; int offset = 0;
            foreach (int value in values) for (int index = 0; index < sizeof(int); index++) bytes[offset++] = (byte)(value >> (index * 8));
            return Hash(bytes);
        }
        private static void Put(float value, byte[] bytes, ref int offset)
        {
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            for (int index = 0; index < sizeof(float); index++) bytes[offset++] = (byte)(bits >> (index * 8));
        }
    }
}

#endif
