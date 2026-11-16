#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json.Linq;
using RoadRush.Core800;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRush.Scenery800.Diagnostics
{
    /// <summary>
    /// Explicit matched actual-window/door scene-context diagnostic, never automatic.
    /// InstancedOriginal versus InstancedSceneMask both submit in SAME LateUpdate400.
    /// No Update/timing move; all original capture/copy prerequisites remain.
    /// Own meshes/renderers live outside actual Town/Ground. Original Town is never
    /// disabled. BeforeRevoke invalidates borrowed state BEFORE own renderer cleanup.
    /// Parent owns the genuine factory, real RT readback, native approval and issuers.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public sealed class Case7FacadeSceneContext800 : MonoBehaviour, IDisposable, FacadeSubmissionHooks800.IObserver
    {
        public enum DrawPath { None, InstancedOriginal, InstancedSceneMask }
        public const int RequiredParts = 5;
        public const int RequiredPaths = 2;
        public const int PrivateLayer = 30;
        public const int MaximumSceneNodes = 8192;
        public const int MaximumRootObjects = 256;
        public const int MaximumChildrenPerNode = 512;
        public const int MaximumContextFilters = 16;
        public const int MaximumContextNodes = 256;
        public const int MaximumTotalVertices = 65536;
        public const int MaximumTotalIndices = 196608;
        public const int MaximumNativeReservations = 65536;
        public const double MaximumPreparationSeconds = 60;
        public const string SourcePrefix = "os-F8B1F936-0537-449A-B9E4-90DDA82669AE-p72221e4a90078e5ea19c-0";
        public const string SelectedWindowId = SourcePrefix + "/e3/b1/w0";
        public const string SelectedDoorId = SourcePrefix + "/e3/b1/door";

        public bool Prepared { get; private set; }
        public bool Incomplete { get; private set; }
        public string Reason { get; private set; } = "Unarmed actual three-path diagnostic";
        public JObject Receipt
        {
            get
            {
                var result = (JObject)receipt.DeepClone();
                result["prepared"] = Prepared; result["incomplete"] = Incomplete; result["reason"] = Reason;
                result["completedPaths"] = completedPaths;
                result["requiredPaths"] = RequiredPaths;
                // Preserve already-copied observations even if Prepare/path fails.
                result["parts"] = partReceipts.DeepClone(); result["context"] = contextReceipts.DeepClone();
                result["paths"] = pathReceipts.DeepClone();
                result["sceneObservations"] = sceneObservations.DeepClone();
                result["timingObservations"] = timingObservations.DeepClone();
                result["submissions"] = submissions.DeepClone(); result["ownedMeshAllocations"] = ownedMeshes.Count;
                result["nativeReservations"] = nativeReservations;
                return result;
            }
        }
        public int OwnedMeshCount => ownedMeshes.Count;
        // Retain OWN references through deferred destruction: clearing a list must
        // never manufacture a successful release receipt. No publisher getters.
        public bool OwnedResourcesReleased
        {
            get
            {
                if (!disposed) return false;
                foreach (Mesh mesh in ownedMeshes) if (mesh) return false;
                foreach (MeshRenderer renderer in allOwnedRenderers) if (renderer) return false;
                return ReferenceEquals(ownedStage,null) || !ownedStage;
            }
        }
        private readonly JObject receipt = new JObject { ["schema"] = "RoadRushActualFacadeSceneContext1",
            ["visiblePixelsProved"] = false, ["gameplayAdmission"] = false,
            ["windowCohort"] = "REDUCEDCOHORT1_FROM_ORIGINAL6", ["doorCohort"] = "1_UNCHANGED",
            ["privateLayerDifference"] = "Same private30 camera mask/copies acrossall3paths; originalTown unchanged",
            ["caseDifference"] = "SAME LateUpdate order400/5parts/copies/matrices/materialrefs/count1/worldBounds/cameraRT/private30; secondcase ONLY RenderParams overrideSceneCullingMask=true, sceneCullingMask=observed OWNED GO mask",
            ["sceneMasksProveCause"] = false, ["beforeCullInclusionProved"] = false,
            ["timingPhaseChanged"] = false, ["standalonePlayerAcceptance"] = false };
        // Diagnostic copies only. No camera/scene culling mask is written here.
        private const int MaximumSceneObservations = 32;
        private const int MaximumTimingObservations = 128;
        private readonly JArray sceneObservations = new JArray(), timingObservations = new JArray(), submissions = new JArray();
        private bool sceneBaselineObserved;
        private ulong observedOwnedSceneMask, observedCameraOverrideMask;
        private ulong observedSceneHandle;
        private readonly Dictionary<int, QueuedPart> queued = new Dictionary<int, QueuedPart>();
        private readonly Dictionary<int, Part> parts = new Dictionary<int, Part>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<MeshRenderer> contextRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> featureRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> allOwnedRenderers = new List<MeshRenderer>();
        private readonly List<Material> borrowedContextMaterials = new List<Material>();
        private readonly JArray partReceipts = new JArray(), contextReceipts = new JArray(), pathReceipts = new JArray();
        private GeoWorld town;
        private TownSemanticPublication800 publication;
        private GroundLease800 ground;
        private Camera camera;
        private RenderTexture readbackTarget;
        private BuildingFacadeChunk capturedChunk;
        private IDisposable townSubscription, groundSubscription;
        private CancellationToken cancellation;
        private GameObject ownedStage;
        private int mainThread, nativeReservations, totalVertices, totalIndices, originalMask;
        private int capturedFrame = -1;
        private int revision, beginFrame = -1, drawnFrame = -1, endedFrame = -1, drawnParts, completedPaths;
        private Matrix4x4 originalView, originalProjection, endView, endProjection;
        private DrawPath activePath;
        private bool begun, disposed, retired, observerInstalled, eventInstalled, maskChanged;

        private sealed class QueuedPart
        {
            internal BuildingFacadeChunk Chunk;
            internal string Id;
            internal int Slot, Frame;
            internal Matrix4x4 Matrix;
        }
        private sealed class Part
        {
            internal int Group, OriginalCount;
            internal string Id;
            internal string MaterialId;
            internal Matrix4x4 Matrix;
            internal Matrix4x4[] SingleMatrix;
            internal RenderParams Parameters;
            internal Material Material;
            internal FacadeLiteralMesh800 Data;
            internal Mesh LocalCopy, BakedCopy;
            internal MeshRenderer Renderer;
        }

        /// <summary>Parent sets affected-house SAME pose BEFORE Prepare. This method
        /// captures genuine live Queue/Flush, removes its exclusive observer before
        /// private mask staging, copies actual selected body roofs/stairs/walls, and
        /// returns without drawing any diagnostic path. Main thread coroutine only.</summary>
        public IEnumerator Prepare(GeoWorld actualTown, Camera actualCamera, RenderTexture actualReadbackTarget,
            string actualWindowId, string actualDoorId, int privateLayer, CancellationToken token)
        {
            if (begun || disposed) throw new InvalidOperationException("One-shot component");
            begun = true;
            mainThread = Thread.CurrentThread.ManagedThreadId;
            cancellation = token;
            if (privateLayer != PrivateLayer || actualWindowId != SelectedWindowId || actualDoorId != SelectedDoorId)
                throw new ArgumentException("Explicit reviewed window/door/private30 selection required");
            town = actualTown; camera = actualCamera; readbackTarget = actualReadbackTarget;
            Reserve(128);
            if (!Application.isPlaying || string.IsNullOrEmpty(PlayerProfile.StorageOverride)
                || !town || !camera || !readbackTarget || !ReferenceEquals(camera.targetTexture,readbackTarget)
                || gameObject.scene != town.gameObject.scene || camera.gameObject.scene != town.gameObject.scene
                || transform.IsChildOf(town.transform) || !IdentityMatrix(transform.localToWorldMatrix))
                throw new InvalidOperationException("Actual matching scene/cameraRT and independent identity host required");
            publication = town.CurrentTownPublication800;
            ground = publication?.Ground;
            if (publication == null || ground == null || !Current()) throw new InvalidOperationException("Genuine current typedTown/Ground required");
            var exactPublication = publication;
            var exactGround = ground;
            bool success = false;
            double started = Time.realtimeSinceStartupAsDouble;
            try
            {
                townSubscription = publication.BeforeRevoke((expired, reason) => {
                    if (ReferenceEquals(expired, exactPublication)) Retire("Actual Town BeforeRevoke: " + reason); });
                groundSubscription = ground.BeforeRevoke((expired, reason) => {
                    if (ReferenceEquals(expired, exactGround)) Retire("Actual Ground BeforeRevoke: " + reason); });
                RenderPipelineManager.endCameraRendering += RenderEnded;
                RenderPipelineManager.beginCameraRendering += RenderBegan;
                eventInstalled = true;
                if (!Current() || !FacadeSubmissionHooks800.TryInstall(this))
                    throw new InvalidOperationException("Exclusive original observer slot/currentness unavailable");
                observerInstalled = true;
                while (parts.Count < RequiredParts && !Incomplete)
                {
                    NeedPreparing(started);
                    yield return null;
                }
                RemoveObserver();
                if (Incomplete || parts.Count != RequiredParts || !Current())
                    throw new InvalidOperationException("Actual5part source capture incomplete");
                // All five actual source buffers were copied synchronously in Flush.
                // No borrowed matrix array survives into this yielded preparation.
                var sceneWalk = CheckUnusedLayer();
                try { while (sceneWalk.MoveNext()) { NeedPreparing(started); yield return sceneWalk.Current; } }
                finally { (sceneWalk as IDisposable)?.Dispose(); }
                ownedStage = new GameObject("Case7 OWNED private facade copies");
                ownedStage.layer = PrivateLayer;
                ownedStage.transform.SetParent(transform, false);
                foreach (Part part in OrderedParts())
                {
                    NeedPreparing(started);
                    Reserve(256);
                    part.LocalCopy = Own(part.Data.CreateOwned("Case7 literal " + part.Group));
                    VerifyLiteralReadback(part.LocalCopy, part.Data);
                    FacadeLiteralMesh800 baked = part.Data.Bake(part.Matrix, out JObject arithmetic);
                    part.BakedCopy = Own(baked.CreateOwned("Case7 bakedworld " + part.Group));
                    VerifyLiteralReadback(part.BakedCopy, baked);
                    part.Renderer = MakeRenderer("Case7 feature " + part.Group, part.BakedCopy, part.Material,
                        part.Parameters.shadowCastingMode, part.Parameters.receiveShadows,
                        part.Parameters.lightProbeUsage, part.Parameters.reflectionProbeUsage, part.Parameters.renderingLayerMask);
                    featureRenderers.Add(part.Renderer);
                    partReceipts.Add(new JObject { ["id"] = part.Id, ["group"] = part.Group,
                        ["actualMatrixF32Bits"] = MatrixBits(part.Matrix), ["actualSourceStreams"] = part.Data.Hashes,
                        ["actualSourceMeshEntityId64"] = part.Data.SourceEntityId64, ["actualMaterialEntityId64"] = part.MaterialId,
                        ["originalRenderWorldBounds"] = BoundsValues(part.Parameters.worldBounds),
                        ["originalRenderParams"] = ParameterValues(part.Parameters),
                        ["bakedOwnRendererState"] = RendererValues(part.Renderer),
                        ["bakedStreams"] = baked.Hashes, ["bakeArithmetic"] = arithmetic,
                        ["originalInstanceCount"] = part.OriginalCount, ["diagnosticInstanceCount"] = 1 });
                }
                var contextWalk = CopyActualContext();
                try { while (contextWalk.MoveNext()) { NeedPreparing(started); yield return contextWalk.Current; } }
                finally { (contextWalk as IDisposable)?.Dispose(); }
                Reserve(128);
                originalMask = camera.cullingMask;
                originalView = camera.worldToCameraMatrix;
                originalProjection = camera.projectionMatrix;
                ObserveSceneContext("preparation-commit", DrawPath.None, true);
                if (!publication.ValidateSourceNow() || !Current()) throw new InvalidOperationException("Actual source/body changed before preparation commit");
                NeedPreparing(started); // Terminal synchronous slice also obeys cooperative deadline.
                Prepared = success = true;
                receipt["parts"] = partReceipts; receipt["context"] = contextReceipts; receipt["paths"] = pathReceipts;
                receipt["originalCameraMask"] = originalMask;
                receipt["sceneObservations"] = sceneObservations;
                receipt["timingObservations"] = timingObservations;
                receipt["submissions"] = submissions;
                Reason = "Actual5literal parts/context prepared; no path armed";
            }
            finally
            {
                RemoveObserver();
                if (!success) { Incomplete = true; RestoreCameraAndStop(); }
            }
        }

        public void Queued(BuildingFacadeChunk chunk, int group, int slot, int featureIndex,
            BuildingFacadeChunk.Feature feature, Matrix4x4 root, Matrix4x4 matrix,
            Vector3 position, Bounds bounds, Camera capturedCamera, int frame)
        {
            if (disposed || retired || parts.Count == RequiredParts || !Selected(feature.Id, group)) return;
            try
            {
                if (Incomplete || !ReferenceEquals(capturedCamera, camera) || slot < 0 || slot >= 128
                    || queued.ContainsKey(group) || parts.ContainsKey(group)) throw new InvalidOperationException("Selected Queue mismatch/duplicate");
                queued.Add(group, new QueuedPart { Chunk = chunk, Id = feature.Id, Slot = slot, Frame = frame, Matrix = matrix });
            }
            catch (Exception error) { ObserverFault("Selected Queue: " + error.GetType().Name); }
        }

        public void Flushing(BuildingFacadeChunk chunk, int group, Mesh mesh, Material material,
            Matrix4x4[] borrowedMatrices, int count, Bounds bounds, Camera capturedCamera,
            bool instanced, ShadowCastingMode shadow, RenderParams? actualParameters, int frame)
        {
            if (disposed || retired || !queued.TryGetValue(group, out QueuedPart row)) return;
            queued.Remove(group);
            try
            {
                Reserve(128);
                int attempt = revision;
                if (!Current() || !ReferenceEquals(row.Chunk, chunk) || !ReferenceEquals(capturedCamera, camera)
                    || !ReferenceEquals(camera.targetTexture,readbackTarget) || frame != row.Frame || !instanced || !actualParameters.HasValue
                    || count != (group < 2 ? 6 : 1) || borrowedMatrices == null || row.Slot >= count
                    || !SameBits(row.Matrix, borrowedMatrices[row.Slot])
                    || !chunk.transform.IsChildOf(town.transform) || chunk.gameObject.name != "Source building " + SourcePrefix)
                    throw new InvalidOperationException("Actual selected Flush/context/layout mismatch");
                RenderParams parameters = actualParameters.Value;
                if (!ReferenceEquals(parameters.camera, camera) || !ReferenceEquals(parameters.material, material)
                    || !material.enableInstancing || !SystemInfo.supportsInstancing
                    || parameters.matProps != null || parameters.lightProbeUsage != LightProbeUsage.Off)
                    throw new InvalidOperationException("Unsupported actual parameter block/probe/camera/material");
                if (capturedChunk != null && !ReferenceEquals(capturedChunk, chunk))
                    throw new InvalidOperationException("Window/door not from same actual source body");
                FacadeLiteralMesh800 data = FacadeLiteralMesh800.CaptureActual(mesh, Reserve, ReserveGeometry);
                Matrix4x4 copied = borrowedMatrices[row.Slot]; // VALUE copied now, never retain borrowed array.
                string materialId = Identity(material);
                if (!Current() || revision != attempt || retired) throw new InvalidOperationException("Owner retired during literal copy");
                if (capturedFrame != -1 && capturedFrame != frame) throw new InvalidOperationException("Selected5parts span different actual frames");
                capturedFrame = frame;
                capturedChunk = chunk;
                parts.Add(group, new Part { Group = group, OriginalCount = count, Id = row.Id, Matrix = copied,
                    SingleMatrix = new[] { copied }, Parameters = parameters, Material = material, MaterialId = materialId, Data = data });
                if (parts.Count == RequiredParts) RemoveObserver();
            }
            catch (Exception error) { ObserverFault("Selected Flush: " + error.GetType().Name + ": " + error.Message); }
        }
        public void ObserverFault(string reason)
        {
            Incomplete = true;
            reason = reason ?? "Unknown diagnostic fault";
            Reason = reason.Length <= 256 ? reason : reason.Substring(0,256);
        }

        private IEnumerator CheckUnusedLayer()
        {
            Reserve(32);
            var scene = gameObject.scene;
            if (scene.rootCount > MaximumRootObjects) throw new InvalidOperationException("Scene root cap");
            var roots = new List<GameObject>(MaximumRootObjects);
            scene.GetRootGameObjects(roots);
            if (roots.Count > MaximumRootObjects) throw new InvalidOperationException("Scene root count changed/cap");
            Reserve(roots.Count);
            var stack = new Stack<Transform>();
            foreach (var root in roots) stack.Push(root.transform);
            int visited = 0;
            while (stack.Count != 0)
            {
                if (++visited > MaximumSceneNodes) throw new InvalidOperationException("Scene traversal cap");
                Reserve(8);
                Transform node = stack.Pop();
                if (node.gameObject.layer == PrivateLayer) throw new InvalidOperationException("Private30 already occupied");
                int children = node.childCount;
                if (children > MaximumChildrenPerNode || visited + stack.Count + children > MaximumSceneNodes)
                    throw new InvalidOperationException("Scene discovered-node/child cap");
                Reserve(children);
                for (int index=0;index<children;index++) stack.Push(node.GetChild(index));
                if (visited % 64 == 0) yield return null;
            }
            receipt["privateLayerVerifiedUnusedNodes"] = visited;
        }

        private IEnumerator CopyActualContext()
        {
            var stack = new Stack<Transform>();
            Reserve(8); stack.Push(capturedChunk.transform);
            int nodes = 0;
            while (stack.Count != 0)
            {
                if (++nodes > MaximumContextNodes) throw new InvalidOperationException("Selected source body traversal cap");
                Reserve(32);
                Transform node = stack.Pop();
                int children = node.childCount;
                if (children > MaximumChildrenPerNode || nodes + stack.Count + children > MaximumContextNodes)
                    throw new InvalidOperationException("Context discovered-node/child cap");
                Reserve(children);
                for (int index=0;index<children;index++) stack.Push(node.GetChild(index));
                if (node.TryGetComponent(out MeshFilter filter))
                {
                    if (contextRenderers.Count >= MaximumContextFilters || !node.TryGetComponent(out MeshRenderer renderer)
                        || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.HasPropertyBlock())
                        throw new InvalidOperationException("Unsupported actual context renderer/membership");
                    Matrix4x4 matrix = node.localToWorldMatrix;
                    Material material = renderer.sharedMaterial;
                    var data = FacadeLiteralMesh800.CaptureActual(filter.sharedMesh, Reserve, ReserveGeometry);
                    var baked = data.Bake(matrix, out JObject arithmetic);
                    Reserve(256);
                    Mesh owned = Own(baked.CreateOwned("Case7 copiedcontext " + contextRenderers.Count));
                    VerifyLiteralReadback(owned,baked);
                    var copy = MakeRenderer("Case7 copiedcontext " + contextRenderers.Count, owned, material,
                        renderer.shadowCastingMode, renderer.receiveShadows, renderer.lightProbeUsage,
                        renderer.reflectionProbeUsage, renderer.renderingLayerMask);
                    contextRenderers.Add(copy); borrowedContextMaterials.Add(material);
                    contextReceipts.Add(new JObject { ["actualFilterEntityId64"] = Identity(filter),
                        ["actualSourceMeshEntityId64"] = data.SourceEntityId64, ["actualSourceName"] = data.SourceName,
                        ["actualMaterialEntityId64"] = Identity(material),
                        ["actualMatrixF32Bits"] = MatrixBits(matrix), ["actualNativeStreams"] = data.Hashes,
                        ["bakedStreams"] = baked.Hashes, ["bakeArithmetic"] = arithmetic });
                }
                if (nodes % 16 == 0) yield return null;
            }
            if (contextRenderers.Count == 0) throw new InvalidOperationException("No actual body context copied");
        }

        /// <summary>Explicit only; requires InstancedOriginal->InstancedSceneMask in order.
        /// Changes only this proof camera's mask and OWN renderers on private30.</summary>
        public void BeginPath(DrawPath path)
        {
            RequireThread();
            if (!Prepared || Incomplete || activePath != DrawPath.None
                || path != DrawPath.InstancedOriginal && path != DrawPath.InstancedSceneMask
                || (int)path != completedPaths+1 || !Current())
                throw new InvalidOperationException("Explicit prepared next path/currentness required");
            Reserve(128);
            if (!SameBits(camera.worldToCameraMatrix, originalView) || !SameBits(camera.projectionMatrix, originalProjection)
                || !ReferenceEquals(camera.targetTexture,readbackTarget) || FacadeSubmissionHooks800.Current != null)
                throw new InvalidOperationException("Actual camera/RT changed or observer remains");
            maskChanged = true;
            camera.cullingMask = 1 << PrivateLayer;
            foreach (var renderer in contextRenderers) renderer.enabled = true;
            // Preserve the predecessor's literal/baked preparation and seals, but
            // both matched cases draw only instanced local copies. C is historical.
            foreach (var renderer in featureRenderers) renderer.enabled = false;
            ObserveSceneContext("path-armed", path);
            beginFrame = Time.frameCount; drawnFrame = endedFrame = -1; drawnParts = 0;
            if (!Current()) { Retire("Owner changed while arming path"); throw new InvalidOperationException(Reason); }
            activePath = path;
            Reason = "Explicit path armed: " + path;
        }

        private void LateUpdate()
        {
            if (activePath == DrawPath.None || disposed || retired) return;
            DrawPath expectedPath = activePath;
            try
            {
                Reserve(128);
                if (!Current() || !IdentityMatrix(transform.localToWorldMatrix) || !ReferenceEquals(camera.targetTexture,readbackTarget)
                    || camera.cullingMask != 1 << PrivateLayer || !SameBits(camera.worldToCameraMatrix,originalView)
                    || !SameBits(camera.projectionMatrix,originalProjection) || FacadeSubmissionHooks800.Current != null)
                    throw new InvalidOperationException("Actual draw context changed");
                ObserveSceneContext("late-before-submissions", activePath);
                int emitted = 0;
                int attempt = revision;
                foreach (Part part in OrderedParts())
                {
                    if (!Current() || revision != attempt) throw new InvalidOperationException("Owner changed before diagnostic part");
                    RenderParams parameters = part.Parameters;
                    parameters.layer = PrivateLayer; // SAME explicit diagnostic difference in BOTH cases.
                    if (activePath == DrawPath.InstancedSceneMask)
                    {
                        parameters.overrideSceneCullingMask = true;
                        parameters.sceneCullingMask = observedOwnedSceneMask;
                    }
                    RecordSubmission(part, parameters);
                    // New readonly clock observations cannot permit an old stack to
                    // submit after a callback stopped/replaced/retired this case.
                    // Pure fence only; do not add another owner/native query.
                    if (revision != attempt || disposed || retired || cancellation.IsCancellationRequested
                        || activePath != expectedPath)
                        throw new InvalidOperationException("Consumer/path changed during submission observation");
                    Graphics.RenderMeshInstanced(parameters, part.LocalCopy, 0, part.SingleMatrix, 1);
                    emitted++;
                }
                if (!Current()) throw new InvalidOperationException("Owner changed after diagnostic submissions");
                RecordTiming("late-submissions-returned", activePath);
                drawnFrame = Time.frameCount; drawnParts = emitted;
            }
            catch (Exception error) { ObserverFault("Diagnostic draw: " + error.GetType().Name + ": " + error.Message); StopPath(); }
        }

        private void RenderBegan(ScriptableRenderContext context, Camera rendered)
        {
            if (activePath == DrawPath.None || disposed || retired || !ReferenceEquals(rendered,camera)) return;
            try { ObserveSceneContext("begin-camera-rendering", activePath); }
            catch (Exception error) { ObserverFault("Scene begin observation: " + error.GetType().Name); }
        }

        private void RenderEnded(ScriptableRenderContext context, Camera rendered)
        {
            if (activePath == DrawPath.None || disposed || retired || !ReferenceEquals(rendered,camera)) return;
            try
            {
                Reserve(64);
                int frame = Time.frameCount;
                Matrix4x4 observedView = camera.worldToCameraMatrix, observedProjection = camera.projectionMatrix;
                ObserveSceneContext("end-camera-rendering", activePath);
                if (drawnFrame != frame || drawnParts != RequiredParts || !ReferenceEquals(camera.targetTexture,readbackTarget)
                    || camera.cullingMask != 1 << PrivateLayer || !SameBits(observedView,originalView)
                    || !SameBits(observedProjection,originalProjection) || !Current())
                    throw new InvalidOperationException("Actual endCameraRendering does not match path submissions");
                endView = observedView; endProjection = observedProjection;
                endedFrame = frame;
            }
            catch (Exception error) { ObserverFault("Diagnostic render callback: " + error.GetType().Name); }
        }

        public bool CompletePath(DrawPath path, int actualRenderedFrame, out string reason)
        {
            RequireThread();
            bool valid = false;
            try
            {
                Reserve(64);
                string cameraId = Identity(camera), targetId = Identity(readbackTarget);
                Matrix4x4 completeView = camera.worldToCameraMatrix, completeProjection = camera.projectionMatrix;
                ObserveSceneContext("complete-path", activePath);
                valid = !Incomplete && Prepared && path == activePath && (int)path == completedPaths+1 && actualRenderedFrame >= beginFrame
                    && actualRenderedFrame == drawnFrame && actualRenderedFrame == endedFrame && drawnParts == RequiredParts
                    && ReferenceEquals(camera.targetTexture,readbackTarget) && camera.cullingMask == 1 << PrivateLayer
                    && SameBits(completeView,originalView) && SameBits(completeProjection,originalProjection) && Current();
                if (!valid) ObserverFault("Incomplete actual path/camera/readback frame");
                else
                {
                    completedPaths++;
                    pathReceipts.Add(new JObject { ["path"] = path.ToString(), ["frame"] = actualRenderedFrame,
                        ["cameraEntityId64"] = cameraId, ["readbackTargetEntityId64"] = targetId,
                        ["privateCameraMask"] = 1 << PrivateLayer, ["parts"] = RequiredParts,
                        ["endWorldToCameraF32Bits"] = MatrixBits(endView), ["endProjectionF32Bits"] = MatrixBits(endProjection),
                        ["completeWorldToCameraF32Bits"] = MatrixBits(completeView), ["completeProjectionF32Bits"] = MatrixBits(completeProjection),
                        ["visiblePixelsProved"] = false, ["status"] = "COMPLETED_ACTUAL_RENDER_FRAME_NOT_PIXEL_ACCEPTANCE" });
                }
            }
            catch (Exception error) { ObserverFault("CompletePath: " + error.GetType().Name); valid = false; }
            reason = Reason;
            return valid && !Incomplete;
        }
        public void StopPath()
        {
            activePath = DrawPath.None;
            foreach (var renderer in allOwnedRenderers) Cleanup(() => { if (renderer) renderer.enabled = false; });
        }
        public void RestoreCameraAndStop()
        {
            StopPath();
            if (maskChanged)
            {
                maskChanged = false;
                Cleanup(() => { if (camera) camera.cullingMask = originalMask; });
            }
        }
        private void Retire(string reason, bool markIncomplete = true)
        {
            retired = true; Prepared = false; revision++;
            if (markIncomplete) ObserverFault(reason); else Reason = reason; // No IsCurrent query inside cleanup.
            RemoveObserver(); RestoreCameraAndStop();
            foreach (var renderer in allOwnedRenderers) Cleanup(() => { if (renderer) { renderer.enabled = false; renderer.sharedMaterial = null; } });
            foreach (Part part in parts.Values) { part.Material = null; part.Parameters = default; }
            borrowedContextMaterials.Clear(); queued.Clear(); capturedChunk = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            if (!begun) { disposed = true; return; }
            RequireThread(); disposed = true;
            Retire("Owned scene-context consumer disposed", completedPaths != RequiredPaths);
            Cleanup(() => townSubscription?.Dispose()); Cleanup(() => groundSubscription?.Dispose());
            townSubscription = groundSubscription = null;
            if (eventInstalled)
            {
                eventInstalled = false;
                Cleanup(() => RenderPipelineManager.endCameraRendering -= RenderEnded);
                Cleanup(() => RenderPipelineManager.beginCameraRendering -= RenderBegan);
            }
            foreach (Mesh mesh in ownedMeshes) Cleanup(() => { if (mesh) Destroy(mesh); });
            Cleanup(() => { if (ownedStage) Destroy(ownedStage); });
            receipt["disposeRequested"] = true;
            receipt["ownedMeshAllocations"] = ownedMeshes.Count;
            // ownedMeshes/allOwnedRenderers/ownedStage intentionally retained for
            // post-two-frame Native release verification by the parent.
            parts.Clear(); queued.Clear(); contextRenderers.Clear(); featureRenderers.Clear();
            town = null; publication = null; ground = null; camera = null; readbackTarget = null;
        }
        private void OnDisable() { if (begun && !disposed) Dispose(); }
        private void OnDestroy() { if (begun && !disposed) Dispose(); }

        private bool Current()
        {
            Reserve(64);
            int attempt = revision;
            TownSemanticPublication800 actualPublication = publication;
            GroundLease800 actualGround = ground;
            GeoWorld actualTown = town;
            return !disposed && !retired && !cancellation.IsCancellationRequested && actualPublication != null && actualGround != null
                && actualGround.IsCurrent && actualPublication.IsCurrent && ReferenceEquals(actualPublication.Ground,actualGround)
                && ReferenceEquals(actualTown.CurrentTownPublication800,actualPublication)
                && attempt == revision && !disposed && !retired && !cancellation.IsCancellationRequested
                && ReferenceEquals(publication,actualPublication) && ReferenceEquals(ground,actualGround) && ReferenceEquals(town,actualTown);
        }
        private void NeedPreparing(double beganAt)
        {
            Reserve(4);
            int attempt = revision;
            bool current = Current();
            double elapsed = Time.realtimeSinceStartupAsDouble-beganAt;
            if (Incomplete || !current || elapsed > MaximumPreparationSeconds || attempt != revision
                || disposed || retired || cancellation.IsCancellationRequested)
                throw new InvalidOperationException("Preparation cancelled/retired/deadline/incomplete");
        }
        private void Reserve(int count)
        {
            RequireThread();
            if (count < 0 || nativeReservations > MaximumNativeReservations-count)
                throw new InvalidOperationException("Diagnostic native reservation cap");
            nativeReservations += count;
        }
        private void ReserveGeometry(int vertices, int indices)
        {
            if (vertices < 0 || indices < 0 || totalVertices > MaximumTotalVertices-vertices || totalIndices > MaximumTotalIndices-indices)
                throw new InvalidOperationException("Diagnostic literal geometry cap");
            totalVertices += vertices; totalIndices += indices;
        }
        private void RequireThread()
        { if (mainThread == 0 || mainThread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Main-thread explicit Prepare required"); }
        private void RemoveObserver()
        {
            if (!observerInstalled) return;
            observerInstalled = false;
            Cleanup(() => { if (!FacadeSubmissionHooks800.Remove(this)) ObserverFault("Observer ownership changed"); });
        }
        private Mesh Own(Mesh mesh) { ownedMeshes.Add(mesh); return mesh; }
        private MeshRenderer MakeRenderer(string name, Mesh mesh, Material material, ShadowCastingMode shadow,
            bool receive, LightProbeUsage lightProbe, ReflectionProbeUsage reflectionProbe, uint renderingMask)
        {
            var node = new GameObject(name); node.SetActive(false); node.layer = PrivateLayer; node.transform.SetParent(ownedStage.transform,false);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>(); allOwnedRenderers.Add(renderer); renderer.enabled = false; renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadow; renderer.receiveShadows = receive; renderer.lightProbeUsage = lightProbe;
            renderer.reflectionProbeUsage = reflectionProbe; renderer.renderingLayerMask = renderingMask;
            node.SetActive(true);
            if (!Current()) { renderer.sharedMaterial = null; throw new InvalidOperationException("Owner retired during own renderer setup"); }
            return renderer;
        }
        private void VerifyLiteralReadback(Mesh mesh, FacadeLiteralMesh800 expected)
        {
            var actual = FacadeLiteralMesh800.CaptureActual(mesh,Reserve,ReserveGeometry);
            if (expected.IndexFormat != actual.IndexFormat || !JToken.DeepEquals(expected.Hashes,actual.Hashes)) throw new InvalidOperationException("Owned native literal roundtrip changed actual streams");
        }
        private IEnumerable<Part> OrderedParts()
        { foreach (int group in new[]{0,1,19,20,21}) if (parts.TryGetValue(group,out Part part)) yield return part; }
        private static bool Selected(string id, int group) => id == SelectedWindowId && (group == 0 || group == 1)
            || id == SelectedDoorId && (group == 19 || group == 20 || group == 21);
        private void Cleanup(Action action)
        { try { action(); } catch (Exception error) { ObserverFault("Owned cleanup: " + error.GetType().Name); } }
        /// <summary>
        /// Main-thread bounded READONLY scene-context observations. GO scene identity
        /// and masks must agree; camera override is observed separately, never inferred
        /// from its GO. No all-bits substitute, owner query, scene/mask writer or cause
        /// grant. Native callbacks are fenced by the consumer's existing revision and
        /// references; caller keeps its original final owner fence after this method.
        /// </summary>
        private void ObserveSceneContext(string phase, DrawPath path, bool establishBaseline = false)
        {
            if (sceneObservations.Count >= MaximumSceneObservations)
                throw new InvalidOperationException("Scene observation row cap");
            int rendererCount = allOwnedRenderers.Count;
            if (rendererCount > MaximumContextFilters + RequiredParts)
                throw new InvalidOperationException("Owned renderer observation cap");
            Reserve(128 + rendererCount * 16); // BEFORE native camera/GO/renderer reads.
            int attempt = revision;
            Camera actualCamera = camera;
            GeoWorld actualTown = town;
            GameObject actualStage = ownedStage;
            if (disposed || retired || cancellation.IsCancellationRequested || !actualCamera || !actualTown || !actualStage)
                throw new InvalidOperationException("Scene observation consumer unavailable");
            GameObject cameraObject = actualCamera.gameObject, townObject = actualTown.gameObject;
            var scene = actualStage.scene;
            GameObject hostObject = gameObject;
            ulong stageMask = actualStage.sceneCullingMask;
            ulong cameraObjectMask = cameraObject.sceneCullingMask;
            ulong townMask = townObject.sceneCullingMask, hostMask = hostObject.sceneCullingMask;
            ulong cameraOverride = actualCamera.overrideSceneCullingMask;
            var rows = new JArray();
            bool sameScene = cameraObject.scene == scene && townObject.scene == scene && hostObject.scene == scene;
            bool sameMasks = cameraObjectMask == stageMask && townMask == stageMask && hostMask == stageMask;
            foreach (MeshRenderer renderer in allOwnedRenderers)
            {
                if (!renderer) throw new InvalidOperationException("Owned observed renderer missing");
                GameObject rendererObject = renderer.gameObject;
                ulong rendererMask = rendererObject.sceneCullingMask;
                sameScene &= rendererObject.scene == scene;
                sameMasks &= rendererMask == stageMask;
                rows.Add(new JObject { ["rendererEntityId64"] = Identity(renderer),
                    ["gameObjectSceneHandle"] = rendererObject.scene.handle.GetRawData().ToString(CultureInfo.InvariantCulture),
                    ["gameObjectSceneCullingMaskHex64"] = HexMask(rendererMask),
                    ["gameObjectLayer"] = rendererObject.layer,
                    ["renderingLayerMask"] = renderer.renderingLayerMask, ["enabled"] = renderer.enabled });
            }
            int actualLayerMask = actualCamera.cullingMask;
            if (attempt != revision || disposed || retired || cancellation.IsCancellationRequested
                || !ReferenceEquals(camera,actualCamera) || !ReferenceEquals(town,actualTown) || !ReferenceEquals(ownedStage,actualStage))
                throw new InvalidOperationException("Consumer changed during readonly scene observations");
            var timing = RecordTiming(phase, path);
            sceneObservations.Add(new JObject { ["timing"] = timing.DeepClone(), ["sceneHandle"] = scene.handle.GetRawData().ToString(CultureInfo.InvariantCulture),
                ["cameraCullingMask"] = actualLayerMask, ["cameraOverrideSceneCullingMaskHex64"] = HexMask(cameraOverride),
                ["cameraGameObjectSceneCullingMaskHex64"] = HexMask(cameraObjectMask),
                ["townGameObjectSceneCullingMaskHex64"] = HexMask(townMask),
                ["hostGameObjectSceneCullingMaskHex64"] = HexMask(hostMask),
                ["ownedStageGameObjectSceneCullingMaskHex64"] = HexMask(stageMask),
                ["sameSceneObjects"] = sameScene, ["sameObservedGoSceneMasks"] = sameMasks, ["ownRenderers"] = rows });
            if (!sameScene || !sameMasks) throw new InvalidOperationException("Actual scene/GO mask agreement refused");
            if (establishBaseline)
            {
                observedOwnedSceneMask = stageMask; observedCameraOverrideMask = cameraOverride;
                observedSceneHandle = scene.handle.GetRawData(); sceneBaselineObserved = true;
            }
            else if (!sceneBaselineObserved || observedOwnedSceneMask != stageMask
                || observedCameraOverrideMask != cameraOverride || observedSceneHandle != scene.handle.GetRawData())
                throw new InvalidOperationException("Observed scene-mask context changed between matched cases");
        }

        private JObject RecordTiming(string phase, DrawPath path)
        {
            if (timingObservations.Count >= MaximumTimingObservations)
                throw new InvalidOperationException("Timing observation row cap");
            Reserve(8); // BEFORE actual Unity clocks; records order, not pre-cull proof.
            var row = new JObject { ["sequence"] = timingObservations.Count + 1, ["phase"] = phase,
                ["path"] = path.ToString(), ["frame"] = Time.frameCount,
                ["realtimeSeconds"] = Time.realtimeSinceStartupAsDouble, ["submissionLateUpdateOrder"] = 400 };
            timingObservations.Add(row);
            return row;
        }

        private void RecordSubmission(Part part, RenderParams parameters)
        {
            if (submissions.Count >= RequiredPaths * RequiredParts * 4)
                throw new InvalidOperationException("Submission diagnostic cap");
            JObject timing = RecordTiming("late-before-RenderMeshInstanced", activePath);
            submissions.Add(new JObject { ["timing"] = timing.DeepClone(), ["group"] = part.Group,
                ["id"] = part.Id, ["originalRenderParams"] = ParameterValues(part.Parameters),
                ["submittedRenderParams"] = ParameterValues(parameters),
                ["actualMatrixF32Bits"] = MatrixBits(part.Matrix), ["count"] = 1,
                ["sceneMaskSource"] = activePath == DrawPath.InstancedSceneMask
                    ? "observed OWNED stage GameObject.sceneCullingMask" : "original copied RenderParams unchanged" });
        }

        private static string HexMask(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

        private static JObject ParameterValues(RenderParams parameters) => new JObject {
            ["layer"] = parameters.layer, ["renderingLayerMask"] = parameters.renderingLayerMask,
            ["overrideSceneCullingMask"] = parameters.overrideSceneCullingMask,
            ["sceneCullingMaskHex64"] = HexMask(parameters.sceneCullingMask),
            ["shadowCastingMode"] = parameters.shadowCastingMode.ToString(), ["receiveShadows"] = parameters.receiveShadows,
            ["lightProbeUsage"] = parameters.lightProbeUsage.ToString(), ["reflectionProbeUsage"] = parameters.reflectionProbeUsage.ToString(),
            ["motionVectorMode"] = parameters.motionVectorMode.ToString() };
        private JObject RendererValues(MeshRenderer renderer)
        {
            Reserve(32); // Actual OWN renderer readback, not assumed GPU/API parity.
            return new JObject { ["renderingLayerMask"] = renderer.renderingLayerMask,
                ["shadowCastingMode"] = renderer.shadowCastingMode.ToString(), ["receiveShadows"] = renderer.receiveShadows,
                ["lightProbeUsage"] = renderer.lightProbeUsage.ToString(), ["reflectionProbeUsage"] = renderer.reflectionProbeUsage.ToString(),
                ["motionVectorGenerationMode"] = renderer.motionVectorGenerationMode.ToString(), ["enabledDuringPreparation"] = renderer.enabled };
        }
        private static JObject BoundsValues(Bounds bounds) => new JObject { ["centre"] = new JArray(bounds.center.x,bounds.center.y,bounds.center.z), ["extents"] = new JArray(bounds.extents.x,bounds.extents.y,bounds.extents.z) };
        private static string Identity(UnityEngine.Object value) => EntityId.ToULong(value.GetEntityId()).ToString(CultureInfo.InvariantCulture);
        private static float[] Values(Matrix4x4 m) => new[]{m.m00,m.m01,m.m02,m.m03,m.m10,m.m11,m.m12,m.m13,m.m20,m.m21,m.m22,m.m23,m.m30,m.m31,m.m32,m.m33};
        private static JArray MatrixBits(Matrix4x4 matrix)
        { var result=new JArray();foreach(float v in Values(matrix))result.Add(BitConverter.SingleToInt32Bits(v).ToString("x8",CultureInfo.InvariantCulture));return result; }
        private static bool SameBits(Matrix4x4 a, Matrix4x4 b)
        { float[] x=Values(a),y=Values(b);for(int i=0;i<16;i++)if(BitConverter.SingleToInt32Bits(x[i])!=BitConverter.SingleToInt32Bits(y[i]))return false;return true; }
        private static bool IdentityMatrix(Matrix4x4 m)
        { float[] a=Values(m);for(int i=0;i<16;i++)if(a[i]!=(i%5==0?1:0))return false;return true; }
    }
}
#endif
