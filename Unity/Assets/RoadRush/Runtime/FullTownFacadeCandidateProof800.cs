#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RoadRush.Core800;
using RoadRush.TerrainDraft.Core800;
using RoadRush.FacadePersistentCandidate800;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadRush
{
    /// <summary>
    /// Explicit actorless pixel diagnostic over the genuine curated Case7 factory.
    /// No normal boot, actor creation, motion, collider/raster/material modifications
    /// or substitute issuer. Native buffers are checked against the actual plan;
    /// supplementary full-face physics proof is deliberately not repeated here.
    /// The caller owns an isolated Editor process, source/input/protected-state
    /// binding and hard process deadline. No result establishes visible pixels.
    /// </summary>
    public sealed class FullTownFacadeCandidateProof800 : MonoBehaviour
    {
        public const string Recipe = "ReviewedCase7R2/FullTownPersistentFacadeCandidate/1";
        public const string ClearCameraName = "Derived Case7 early Base clear camera";
        public const string OutputPrefix = "/private/tmp/rr019-fulltown-facade-candidate-";
        const double TimeoutSeconds = 600;
        const int Width = 1920, Height = 1080, ExpectedPackets = 1064;
        const int MaximumObservedRenderFramesPerCapture = 1024;
        const int ExpectedVertices = 322356, ExpectedTriangles = 541479;
        const long MaximumJsonBytes = 32 * 1024 * 1024;
        static readonly int[] TargetIndices = { 1697, 1005 };
        static readonly string[] TargetSourceIds = {
            "os-F8B1F936-0537-449A-B9E4-90DDA82669AE-p72221e4a90078e5ea19c-0",
            "os-87A8F192-726B-484D-8117-51F624AD46E8-pfc6fbd1655527f5792f6-0" };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly List<GameObject> owned = new List<GameObject>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<string> errors = new List<string>();
        readonly JObject receipt = new JObject();
        readonly JArray views = new JArray();
        UploadedGroundBuild800 ground;
        PreparedWorldMeshPlan800 plan;
        Task<ReviewedDerivedInspector800> preparationWork;
        GeoWorld town;
        Material road, terrain;
        Camera camera;
        RenderTexture target;
        FacadePersistentTown800 comparison;
        FullTownFacadeExercise800 rendererExercise;
        bool rendererExerciseRequested;
        bool rendererLongevityRequested;
        IDisposable candidateLifetimeSubscription;
        BuildingFacadeChunk[] sourceChunks;
        string originalCensusHash;
        JObject lastRenderedCamera, lastCandidateSnapshot;
        static readonly JsonSerializer SnapshotSerializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new UnityObservationConverter() } });
        BuildingFacadeChunk.FrameStatistics lastRenderedStatistics;
        int renderSequence, comparisonOriginalMask, observedRenderFrames;
        bool observeRender, renderObservationBudgetExceeded;
        bool comparisonMaskCaptured;
        Action<bool, string> completed;
        string output, context, regional;
        double began;
        float originalScale;
        int renderedFrame = -1;
        bool started, ending, completionCalled, sourceCurrent = true;

        public void Begin(string ownedOutput, string reviewedContext, string reviewedRegional,
            Action<bool, string> callback)
        {
            Need(!started && Application.isPlaying && GameSession.SuppressAutomaticLaunch
                && !string.IsNullOrEmpty(PlayerProfile.StorageOverride)
                && !FindAnyObjectByType<GameSession>() && callback != null,
                "Fresh isolated actorless PlayMode required");
            Need(Path.GetFullPath(ownedOutput) == ownedOutput && ownedOutput.StartsWith(OutputPrefix, StringComparison.Ordinal)
                && Directory.Exists(ownedOutput) && !File.Exists(Path.Combine(ownedOutput, "receipt.json")), "Fresh claimed private output");
            started = true; output = ownedOutput; context = reviewedContext; regional = reviewedRegional;
            completed = callback; began = Time.realtimeSinceStartupAsDouble;
            originalScale = Time.timeScale;
            Application.logMessageReceived += Logged;
            RenderPipelineManager.endCameraRendering += Rendered;
            rendererExerciseRequested = FullTownFacadeExercise800.Requested;
            rendererLongevityRequested = FullTownFacadeExercise800.LongevityRequested;
            Need(!rendererLongevityRequested || rendererExerciseRequested, "Long path requires ordinary twelve-case flag");
            receipt["nativeLongevityRequested"] = rendererLongevityRequested;
            receipt["nativeLongevityCompleted"] = false;
            receipt["rendererExerciseRequested"] = rendererExerciseRequested;
            receipt["rendererExerciseCompleted"] = false;
            receipt["recipe"] = Recipe; receipt["mode"] = "FullTownOriginalVersusPersistentCandidate";
            receipt["fullTownNormalContext"] = true;
            receipt["observationMatrixBitOrder"] = "row-major r0c0..r3c3 lowercase F32 hex; native-buffer identity retains original contract";
            receipt["nativeGpuPerformanceAcceptance"] = false;
            receipt["performanceQualification"] = "Process CPU includes all threads; proof readback timing is main-thread observer overhead, not renderer CPU or GPU time.";
            receipt["visualReviewPending"] = true; receipt["pixelCauseProved"] = false;
            receipt["motionAdmitted"] = false; receipt["supportedPathIntervalsIssued"] = false;
            receipt["supplementaryPhysicsRaysRepeated"] = false;
            receipt["nativeBufferHashSchema"] = "LengthPrefixedLocalF32BitsAndI32Indices/1";
            receipt["physicsAcceptance"] = "Not established by this pixel diagnostic; preserve separate historical0354 evidence";
            StartCoroutine(Guard(Inspect()));
        }
        static void Need(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("CASE7 FACADE PIXELS: " + reason); }
        void Logged(string message, string stack, LogType type)
        { if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 32) errors.Add(message + "\n" + stack); }
        void Rendered(ScriptableRenderContext _, Camera actual)
        {
            if (actual != camera) return;
            renderedFrame = Time.frameCount;
            renderSequence++;
            if (!observeRender) return;
            // Reserve a bounded observation BEFORE camera native property reads.
            if (observedRenderFrames >= MaximumObservedRenderFramesPerCapture)
            { renderObservationBudgetExceeded = true; return; }
            observedRenderFrames++;
            lastRenderedCamera = CameraObservation();
            lastRenderedStatistics = BuildingFacadeChunk.Statistics;
        }
        void Update()
        { if (started && !ending && Time.realtimeSinceStartupAsDouble - began > TimeoutSeconds) Finish(new TimeoutException("600-second pixel watchdog")); }
        GameObject Own(string name)
        { var value = new GameObject(name); owned.Add(value); return value; }
        IEnumerator Guard(IEnumerator work)
        {
            try
            {
                while (!ending)
                {
                    bool more = false; object next = null; Exception failure = null;
                    try { more = work.MoveNext(); if (more) next = work.Current; } catch (Exception error) { failure = error; }
                    if (failure != null) { Finish(failure); yield break; }
                    if (!more) yield break;
                    if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
                }
            }
            finally { try { (work as IDisposable)?.Dispose(); } catch (Exception error) { Finish(error); } }
        }
        IEnumerator Inspect()
        {
            var early = GameObject.Find(ClearCameraName);
            Need(early && early.GetComponent<Camera>() && early.GetComponent<Camera>().cullingMask == 0,
                "Actual early clear camera exists before first async yield");
            owned.Add(early); Time.timeScale = 0;
            Need(FindObjectsByType<Rigidbody>().Length == 0, "No moving actors");
            var token = cancellation.Token;
            string capturedContext = context, capturedRegional = regional;
            var preparation = Task.Run(() => {
                var input = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(capturedContext, token);
                return ReviewedDerivedCore800Request.CaptureReviewedCase7(input, capturedRegional, token).LoadInspector(token);
            }, token);
            preparationWork = preparation;
            _ = preparation.ContinueWith(task => { var ignored = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            while (!preparation.IsCompleted) yield return null;
            var prepared = preparation.GetAwaiter().GetResult(); plan = prepared.Plan;
            Need(!prepared.MotionAdmitted && !prepared.SupportedPathIntervalsIssued
                && plan.FinalBufferDigest == ReviewedDerivedCase7Policy.FinalDigest
                && plan.PacketCount == ExpectedPackets && plan.VertexCount == ExpectedVertices
                && plan.TriangleCount == ExpectedTriangles, "Exact curated actorless derived artifact");
            foreach (var item in new Dictionary<string,string> { {"rawDocumentDigest",plan.RawDocumentDigest},
                {"sourceDigest",plan.SourceDigest},{"controlDigest",plan.ControlDigest},
                {"finalBufferDigest",plan.FinalBufferDigest},{"ownerLedgerDigest",plan.OwnerLedgerDigest},
                {"scopeCoverageDigest",plan.ScopeCoverageDigest},{"geometryDigest",plan.GeometryDigest} }) receipt[item.Key] = item.Value;
            var rawOwner = prepared.TakeTownSource(token);
            byte[] original = rawOwner.Write(token);
            Need(World13DocumentCodec.Hash(original) == plan.RawDocumentDigest, "Exact original source bytes");
            File.WriteAllBytes(Path.Combine(output,"owned-original-envelope.json"), original);
            var shader = Shader.Find("Universal Render Pipeline/Lit"); Need(shader && shader.isSupported,"Actual URP Lit");
            road = new Material(shader) {name="Derived inspector shared road",color=new Color(.13f,.145f,.16f)};
            terrain = new Material(shader) {name="Derived inspector shared terrain",color=new Color(.28f,.34f,.20f)};
            var physical = Own("Derived Case7 physical owner parent");
            // Factory-owned activation/registration checks remain untouched.
            yield return UploadedGroundOwner800.BuildOwned(physical.transform, plan, road, terrain, token,
                () => sourceCurrent && this && physical && !ending, value => ground = value);
            Need(ground != null && ground.Lease.IsCurrent, "Genuine actual issued ground lease");
            yield return VerifyNativeBuffers(physical);
            town = Own("Derived Case7 actual typed Town").AddComponent<GeoWorld>();
            town.EntryAudit.Enabled = true;
            IEnumerator actualTownProducer = town.BuildScopedTown800(rawOwner, plan, ground.Lease, token);
            if (rendererExerciseRequested)
                yield return TownConstructionTiming800.Run(actualTownProducer,
                    Path.Combine(output, "town-construction-timing.json"), "ActualTown-3d1b2d83-slice1p5ms-step32-candidate", token);
            else
                yield return actualTownProducer;
            var publication = town.CurrentTownPublication800;
            Need(publication != null && publication.IsCurrent && publication.ValidateSourceNow()
                && ReferenceEquals(publication.Ground,ground.Lease)
                && publication.SourceBuildingCount == 1793, "Genuine same-plan Town/source/Ground");
            Need(World13DocumentCodec.Hash(publication.CopySemanticSourceDocument()) == plan.RawDocumentDigest,
                "Source unchanged after real Town factory");
            File.WriteAllText(Path.Combine(output,"town-ledger.json"),town.ExportScopedTownLedger800());
            File.WriteAllText(Path.Combine(output,"entry-audit.json"),town.EntryAudit.ExportJson());
            var chunks = town.GetComponentsInChildren<BuildingFacadeChunk>(true);
            Need(chunks.Length <= 2500,"Bounded actual Town facade chunks");
            var features = chunks.SelectMany(chunk => chunk.Features).ToArray();
            Need(features.Length <= 100000,"Bounded actual facade feature census");
            var targets = new List<string>();
            for (int index=0;index<TargetIndices.Length;index++)
            {
                Need(town.Region.buildings[TargetIndices[index]].id == TargetSourceIds[index],"Exact actual target source association");
                string prefix = TargetSourceIds[index]+"/";
                var selected = features.Where(feature => feature.Id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
                Need(selected.Length == (index==0?10:15),"Exact real10+15 target features");
                targets.AddRange(selected.Select(feature => feature.Id));
            }
            Need(targets.Distinct(StringComparer.Ordinal).Count()==25,"Exactly25 distinct actual target feature IDs");
            File.WriteAllText(Path.Combine(output,"actual-target-features.json"),new JArray(features.Where(f=>targets.Contains(f.Id)).Select(f=>
                new JObject {{"id",f.Id},{"door",f.IsDoor},{"position",new JArray(f.Position.x,f.Position.y,f.Position.z)},
                    {"width",f.Width},{"height",f.Height},{"palette",f.Palette}})).ToString(Formatting.Indented));
            ownedMeshes.AddRange(town.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(owner=>owner.Mesh).Where(mesh=>mesh));
            ConfigureCamera(early);
            var affected = town.Region.buildings[TargetIndices[0]];
            Vector3 centre = affected.points.Aggregate(Vector3.zero, (sum, value) => sum + value) / affected.points.Length;
            Need(plan.TryGround(centre.x, centre.z, out var centreGround), "Target actual final ground");
            centre.y = (float)centreGround.Height + 2;
            Vector3 position = centre + new Vector3(-14, 11, -17);
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(centre - position));
            sourceChunks = chunks;
            var census = CaptureSourceCensus();
            originalCensusHash = HashText(census.ToString(Formatting.None));
            File.WriteAllText(Path.Combine(output, "all-town-facade-source.json"), census.ToString(Formatting.Indented));
            receipt["sourceChunks"] = chunks.Length;
            receipt["residentFeatures"] = features.Length;
            receipt["residentWindows"] = features.Count(feature => !feature.IsDoor);
            receipt["residentDoors"] = features.Count(feature => feature.IsDoor);
            receipt["nearDistanceMetres"] = 180;
            receipt["farDistanceMetres"] = 900;
            var host = Own("Owned full Town persistent candidate"); // Outside actual Town/Ground roots.
            candidateLifetimeSubscription = ground.Lease.BeforeRevoke((revoked, reason) =>
            {
                sourceCurrent = false;
                try { comparison?.Dispose(); }
                catch (Exception error) { if (errors.Count < 32) errors.Add("Consumer retirement cleanup: " + error); }
            });
            Need(ground.Lease.IsCurrent && publication.IsCurrent, "Actual owners current after lifetime subscription");
            Need(FacadePersistentTown800.TryAttach(host, chunks, out comparison, out string attachReason),
                "Attach actual all-Town candidate: " + attachReason);
            comparisonOriginalMask = camera.cullingMask;
            comparisonMaskCaptured = true;
            Vector3[] offsets = { new Vector3(-14,11,-17), new Vector3(-37,20,-50), new Vector3(-280,140,-340) };
            string[] names = { "near-house", "street", "far-lod" };
            for (int view = 0; view < names.Length; view++)
            {
                position = centre + offsets[view];
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(centre-position));
                yield return CaptureTown(false, names[view]+"-original", position, centre);
                yield return CaptureTown(true, names[view]+"-candidate", position, centre);
            }
            comparison.SetEnabled(false);
            yield return null; yield return null;
            SaveComparison();
            receipt["sixFullTownCapturesCompleted"] = !comparison.Incomplete && views.Count == 6;
            if (rendererExerciseRequested)
            {
                Need((bool)receipt["sixFullTownCapturesCompleted"], "Original six captures complete before optional exercise");
                rendererExercise = FullTownFacadeExercise800.CreateOwned(transform, camera, comparison, sourceChunks,
                    () => sourceCurrent && !ending && this && town && ground != null && ground.Lease.IsCurrent
                        && publication.IsCurrent && ReferenceEquals(town.CurrentTownPublication800, publication),
                    () => HashText(CaptureSourceCensus().ToString(Formatting.None)), originalCensusHash,
                    Path.Combine(output, "renderer-exercise.json"), plan.RawDocumentDigest, plan.SourceDigest, plan.FinalBufferDigest);
                var exerciseReport = rendererExercise.Result;
                yield return rendererExercise.Run(token);
                receipt["rendererExercise"] = JObject.FromObject(exerciseReport, SnapshotSerializer);
                receipt["rendererExerciseCompleted"] = exerciseReport.completedAllCases && exerciseReport.settingsRestored
                    && exerciseReport.ownedResourcesReleased && exerciseReport.recordersDisposed && exerciseReport.error == null
                    && exerciseReport.cleanupErrors.Count == 0;
                Need((bool)receipt["rendererExerciseCompleted"]
                    && exerciseReport.status == "PASS_BOUNDED_EDITOR_RENDERER_EXERCISE_PENDING_PLAYER_PERFORMANCE_REVIEW",
                    "Optional actual renderer exercise completed and restored before original owner cleanup: " + exerciseReport.error);
                if (rendererLongevityRequested)
                {
                    var longevity = rendererExercise.LongevityResult;
                    Need(longevity.phaseCompleted && longevity.reachedRebuildGoal && longevity.successfulRebuildDelta > 65536
                        && longevity.poolStabilityEstablished && longevity.error == null,
                        "Actual bounded renderer rebuild DELTA and stable pool, not a simulated counter");
                    SaveLongevityPin();
                }
            }
            receipt["sourceBeforeAfterUnchanged"] = World13DocumentCodec.Hash(publication.CopySemanticSourceDocument())==plan.RawDocumentDigest;
            Need((bool)receipt["sourceBeforeAfterUnchanged"] && errors.Count==0,"Actual source retained and no runtime errors");
            // Cooperative final-slice fence: ReadPixels/PNG/native calls cannot
            // be interrupted here. If a blocking call returns late, it cannot
            // publish PASS before the next Update watchdog gets a chance to run.
            Need(Time.realtimeSinceStartupAsDouble - began <= TimeoutSeconds,
                "Final synchronous slice exceeded cooperative proof deadline");
            Finish(null);
        }
        IEnumerator VerifyNativeBuffers(GameObject physical)
        {
            var filters=physical.GetComponentsInChildren<MeshFilter>(true).OrderBy(f=>f.name,StringComparer.Ordinal).ToArray();
            Need(filters.Length==ExpectedPackets,"Exact actual native packet count");
            var ids=new HashSet<string>(StringComparer.Ordinal);var rows=new JArray();long vertices=0,triangles=0;
            for(int ordinal=0;ordinal<filters.Length;ordinal++)
            {
                var filter=filters[ordinal];Need(ids.Add(filter.name),"No duplicate actual packet ID");
                var expected=plan.CopyForUpload(filter.name);
                Need(expected.XYZ.Length%3==0&&expected.Indices.Length%3==0&&expected.XYZ.Length<=3*65536&&expected.Indices.Length<=3*131072,
                    "Packet readback budget before bulk native getters");
                var mesh=filter.sharedMesh;var collider=filter.GetComponent<MeshCollider>();
                Need(mesh&&collider&&collider.sharedMesh==mesh&&filter.GetComponent<GeneratedMeshOwner>()?.Mesh==mesh,
                    "Actual MeshFilter/Collider/owner share final buffer");
                Need(mesh.vertexCount==expected.XYZ.Length/3&&mesh.GetIndexCount(0)==expected.Indices.Length,
                    "Actual native counts before arrays");
                var actual=mesh.vertices;var indices=mesh.triangles;
                var xyz=new float[actual.Length*3];
                for(int i=0;i<actual.Length;i++){xyz[3*i]=actual[i].x;xyz[3*i+1]=actual[i].y;xyz[3*i+2]=actual[i].z;}
                string actualHash=BufferHash(xyz,indices),expectedHash=BufferHash(expected.XYZ,expected.Indices);
                Need(actualHash==expectedHash,"Actual native local float bits and indices equal original final plan");
                var matrix=filter.transform.localToWorldMatrix;
                var wanted=Matrix4x4.Translate(new Vector3((float)expected.OriginX,(float)expected.OriginY,(float)expected.OriginZ));
                for(int i=0;i<16;i++)Need(BitConverter.SingleToInt32Bits(matrix[i])==BitConverter.SingleToInt32Bits(wanted[i]),"Exact native packet source frame");
                rows.Add(new JObject{{"id",expected.Id},{"actualBufferSHA256",actualHash},{"expectedBufferSHA256",expectedHash},
                    {"meshId",mesh.GetEntityId().ToString()},{"colliderId",collider.GetEntityId().ToString()},
                    {"matrixFloatBits",new JArray(Enumerable.Range(0,16).Select(i=>BitConverter.SingleToInt32Bits(matrix[i])))},
                    {"vertices",actual.Length},{"triangles",indices.Length/3}});
                ownedMeshes.Add(mesh);vertices+=actual.Length;triangles+=indices.Length/3;
                if(ordinal%32==31)yield return null;
            }
            Need(vertices==ExpectedVertices&&triangles==ExpectedTriangles,"All native buffers accounted; no supplemental physics ray");
            File.WriteAllText(Path.Combine(output,"native-buffer-identities.json"),rows.ToString(Formatting.Indented));
            Need(new FileInfo(Path.Combine(output,"native-buffer-identities.json")).Length<=MaximumJsonBytes,"Bounded identity export");
            receipt["actualPackets"]=filters.Length;receipt["actualVertices"]=vertices;receipt["actualTriangles"]=triangles;
            receipt["actualNativeBufferIdentityVerified"]=true;
        }
        public static string BufferHash(float[] xyz,int[] indices)
        {
            Need(xyz!=null&&indices!=null&&xyz.Length%3==0&&indices.Length%3==0,"Finite complete buffer shape");
            using(var memory=new MemoryStream())
            {
                using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))
                {
                    writer.Write(xyz.Length);writer.Write(indices.Length);
                    foreach(float value in xyz){Need(!float.IsNaN(value)&&!float.IsInfinity(value),"Finite buffer coordinate");writer.Write(BitConverter.SingleToInt32Bits(value));}
                    foreach(int value in indices){Need(value>=0&&value<xyz.Length/3,"In-range native index");writer.Write(value);}
                }
                using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(memory.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
        void ConfigureCamera(GameObject early)
        {
            early.SetActive(false);
            var sun=Own("Derived inspector daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;
            sun.transform.rotation=Quaternion.Euler(42,-35,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.58f,.60f);
            camera=Own("Derived inspector real URP camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.nearClipPlane=.05f;camera.farClipPlane=4000;camera.fieldOfView=50;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.40f,.52f,.65f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderType=CameraRenderType.Base;
            target=new RenderTexture(Width,Height,24);target.Create();camera.targetTexture=target;camera.aspect=(float)Width/Height;
        }
        // All data below describe observations. Counts never establish pixels or visibility.
        static string[] MatrixBits(Matrix4x4 matrix)
            => Enumerable.Range(0,16).Select(index => unchecked((uint)BitConverter.SingleToInt32Bits(matrix[index/4,index%4])).ToString("x8")).ToArray();
        static string HashText(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        JObject CameraObservation()
        {
            return new JObject {
                {"frame",Time.frameCount},{"cameraEntityId",camera.GetEntityId().ToString()},
                {"targetEntityId",camera.targetTexture ? camera.targetTexture.GetEntityId().ToString() : null},
                {"enabled",camera.enabled},{"cullingMask",camera.cullingMask},
                {"cullingMatrixF32bits",new JArray(MatrixBits(camera.cullingMatrix))},
                {"viewF32bits",new JArray(MatrixBits(camera.worldToCameraMatrix))},
                {"projectionF32bits",new JArray(MatrixBits(camera.projectionMatrix))}
            };
        }
        JArray CaptureSourceCensus()
        {
            Need(sourceChunks.Length <= 2500, "Source census chunk budget before native transforms");
            var rows = new JArray();
            foreach (var chunk in sourceChunks)
            {
                Need(chunk && chunk.FeatureCount <= BuildingFacadeChunk.Capacity, "Live bounded source chunk");
                var root = chunk.transform.localToWorldMatrix;
                var features = new JArray(chunk.Features.Select(feature => new JObject {
                    {"id",feature.Id},{"door",feature.IsDoor},{"palette",feature.Palette},
                    {"position",new JArray(feature.Position.x,feature.Position.y,feature.Position.z)},
                    {"rotation",new JArray(feature.Rotation.x,feature.Rotation.y,feature.Rotation.z,feature.Rotation.w)},
                    {"width",feature.Width},{"height",feature.Height}}));
                rows.Add(new JObject {{"sourceEntityId",chunk.GetEntityId().ToString()},
                    {"rootF32bits",new JArray(MatrixBits(root))},{"features",features}});
            }
            return rows;
        }
        JObject ExpectedLodCounts(Vector3 eye)
        {
            int near = 0, far = 0, windows = 0, doors = 0;
            float farMetres = Mathf.Min(900, camera.farClipPlane);
            foreach (var chunk in sourceChunks)
            {
                var root = chunk.transform.localToWorldMatrix;
                foreach (var feature in chunk.Features)
                {
                    float squared = (root.MultiplyPoint3x4(feature.Position)-eye).sqrMagnitude;
                    if (squared > farMetres*farMetres) continue;
                    if (squared < 180*180) near++; else far++;
                    if (feature.IsDoor) doors++; else windows++;
                }
            }
            return new JObject {{"near",near},{"far",far},{"windows",windows},{"doors",doors},
                {"qualification","Distance selection, not frustum/occlusion/pixel visibility"}};
        }
        void SaveComparison()
        {
            if (ReferenceEquals(comparison, null)) return;
            string json = (lastCandidateSnapshot ?? new JObject {{"notYetCaptured",true}}).ToString(Formatting.Indented);
            Need(Encoding.UTF8.GetByteCount(json) <= MaximumJsonBytes, "Bounded candidate snapshot export");
            File.WriteAllText(Path.Combine(output,"persistent-candidate-observations.json"),json);
        }
        IEnumerator CaptureTown(bool candidateMode, string name, Vector3 position, Vector3 lookAt)
        {
            // Both modes retain normal Town context and camera layer mask. A mode
            // transition settles before choosing a completed URP render frame.
            comparison.SetEnabled(candidateMode);
            yield return null; yield return null;
            double deadline = Time.realtimeSinceStartupAsDouble + 15;
            int before = renderSequence;
            observedRenderFrames = 0; renderObservationBudgetExceeded = false; observeRender = true;
            while (renderSequence <= before || (candidateMode && !comparison.AllReady))
            {
                // Incomplete can describe recoverable cold-cache budget progress.
                // Only the finite wait budget refuses pending work; readiness is
                // accepted from the completed-render snapshot below.
                Need(!renderObservationBudgetExceeded && Time.realtimeSinceStartupAsDouble < deadline,
                    "Bounded all-source candidate readiness: " + comparison.Reason);
                yield return null;
            }
            // CaptureSnapshot is the candidate's completed-main-frame latch, not
            // a fresh current-frame readiness assertion before LateUpdate runs.
            observeRender = false;
            Need(!renderObservationBudgetExceeded && lastRenderedCamera != null, "Bounded completed-render camera observation");
            int frame = renderedFrame;
            var snapshot = candidateMode
                ? JObject.FromObject(comparison.CaptureSnapshot(), SnapshotSerializer)
                : new JObject {{"CandidateDisabled",true},{"Frame",frame}};
            if (candidateMode) lastCandidateSnapshot = snapshot;
            Need(!candidateMode || ((int?)snapshot["Frame"] == frame && (bool?)snapshot["AllReady"] == true
                && (bool?)snapshot["Incomplete"] == false),
                "Candidate snapshot belongs to actual completed readback frame");
            Need(ground.Lease.IsCurrent && town.CurrentTownPublication800.IsCurrent
                && camera.cullingMask == comparisonOriginalMask && ReferenceEquals(camera.targetTexture,target)
                && Camera.main == camera, "Actual owner/camera/normal mask current");
            if (candidateMode)
            {
                Need(views.Count > 0, "Original full-Town reference precedes candidate");
                var originalCamera = views[views.Count-1]["completedCamera"];
                foreach (string field in new[] { "cameraEntityId", "targetEntityId", "cullingMask", "cullingMatrixF32bits", "viewF32bits", "projectionF32bits" })
                    Need(JToken.DeepEquals(originalCamera[field],lastRenderedCamera[field]), "Paired actual camera invariant: " + field);
            }
            var expected = ExpectedLodCounts(position);
            Need(lastRenderedStatistics.submittedWindows == (int)expected["windows"]
                && lastRenderedStatistics.submittedDoors == (int)expected["doors"], "Actual full Town distance cohort");
            if (candidateMode)
            {
                Need(lastRenderedStatistics.drawSubmissions == 0, "No original Graphics submissions in candidate frame");
                Need(JToken.DeepEquals(snapshot["CurrentCameraCullingMatrixF32Bits"],lastRenderedCamera["cullingMatrixF32bits"]),
                    "Candidate latched actual culling matrix matches actual rendered camera");
                Need((int?)snapshot["SourceChunks"] == sourceChunks.Length
                    && (int?)snapshot["ResidentFeatures"] == (int)receipt["residentFeatures"]
                    && (int?)snapshot["VisibleWindows"] == (int)expected["windows"]
                    && (int?)snapshot["VisibleDoors"] == (int)expected["doors"]
                    && (int?)snapshot["NearFeatures"] == (int)expected["near"]
                    && (int?)snapshot["FarFeatures"] == (int)expected["far"]
                    && (bool?)snapshot["BoundsValid"] == true, "All-source candidate census/LOD/bounds agreement");
            }
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                double cpuBefore = process.TotalProcessorTime.TotalMilliseconds;
                long allocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                long reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                var previous = RenderTexture.active;
                var image = new Texture2D(Width,Height,TextureFormat.RGB24,false);
                try
                {
                    RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,Width,Height),0,0); image.Apply();
                    File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
                }
                finally { RenderTexture.active=previous; Destroy(image); }
                stopwatch.Stop();
                var finalCamera = CameraObservation();
                Need(JToken.DeepEquals(lastRenderedCamera["cullingMatrixF32bits"],finalCamera["cullingMatrixF32bits"])
                    && JToken.DeepEquals(lastRenderedCamera["viewF32bits"],finalCamera["viewF32bits"])
                    && JToken.DeepEquals(lastRenderedCamera["projectionF32bits"],finalCamera["projectionF32bits"]),
                    "Actual completed-render/readback camera matrices unchanged");
                Need(originalCensusHash == HashText(CaptureSourceCensus().ToString(Formatting.None)),
                    "Full actual source features/root frames unchanged");
                views.Add(new JObject {{"name",name},{"candidateMode",candidateMode},{"frame",frame},
                    {"position",new JArray(position.x,position.y,position.z)},
                    {"target",new JArray(lookAt.x,lookAt.y,lookAt.z)},
                    {"completedCamera",lastRenderedCamera.DeepClone()},{"readbackCamera",finalCamera},
                    {"expectedDistanceCohort",expected},{"actualSourceStatistics",JObject.FromObject(lastRenderedStatistics)},
                    {"candidateSnapshot",snapshot},{"sourceCensusSHA256",originalCensusHash},
                    {"observerReadbackEncodeWriteMainThreadWallMilliseconds",stopwatch.Elapsed.TotalMilliseconds},
                    {"observerIntervalProcessAllThreadCpuMilliseconds",process.TotalProcessorTime.TotalMilliseconds-cpuBefore},
                    {"unityAllocatedBytesBeforeReadback",allocated},{"unityReservedBytesBeforeReadback",reserved},
                    {"rendererCpuMilliseconds",JValue.CreateNull()},{"gpuMilliseconds",JValue.CreateNull()},
                    {"visiblePixelsProved",false},{"finalBufferDigest",plan.FinalBufferDigest}});
            }
            File.WriteAllText(Path.Combine(output,"capture-views.json"),views.ToString(Formatting.Indented));
            SaveComparison();
        }
        void SaveLongevityPin()
        {
            if (!rendererLongevityRequested || ReferenceEquals(rendererExercise, null)) return;
            string path = rendererExercise.LongevityReportPath;
            Need(path != null && File.Exists(path) && new FileInfo(path).Length <= 8 * 1024 * 1024,
                "Bounded separate full native longevity prefix");
            receipt["nativeLongevityReport"] = new JObject { {"path",Path.GetFileName(path)},
                {"bytes",new FileInfo(path).Length},{"sha256",World13DocumentCodec.Hash(File.ReadAllBytes(path))} };
        }
        sealed class UnityObservationConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(Vector3) || type == typeof(Bounds);
            public override bool CanRead => false;
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)
                => throw new NotSupportedException("Observation export only");
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                if (value is Vector3 vector)
                {
                    writer.WriteStartArray();writer.WriteValue(vector.x);writer.WriteValue(vector.y);writer.WriteValue(vector.z);writer.WriteEndArray();
                    return;
                }
                var bounds = (Bounds)value;
                writer.WriteStartObject();writer.WritePropertyName("center");serializer.Serialize(writer,bounds.center);
                writer.WritePropertyName("extents");serializer.Serialize(writer,bounds.extents);writer.WriteEndObject();
            }
        }
        void Finish(Exception failure)
        {if(ending)return;ending=true;StartCoroutine(GuardCleanup(Cleanup(failure)));}
        IEnumerator GuardCleanup(IEnumerator work)
        {
            Exception failure = null;
            try
            {
                while (true)
                {
                    bool more = false; object next = null;
                    try { more = work.MoveNext(); if (more) next = work.Current; }
                    catch (Exception error) { failure = error; }
                    if (failure != null || !more) yield break;
                    yield return next;
                }
            }
            finally
            {
                try { (work as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
                if (!completionCalled) EmergencyComplete(failure ?? new InvalidOperationException("Cleanup ended without completion"));
            }
        }
        void CompleteOnce(bool passed, Exception failure)
        {
            if (completionCalled) return;
            completionCalled = true;
            var callback = completed; completed = null;
            callback?.Invoke(passed, failure?.ToString());
        }
        void EmergencyComplete(Exception failure)
        {
            // Unexpected cleanup failure must still notify the owning Editor.
            // No claim that this non-yielding fallback verified native destruction.
            void Attempt(Action work) { try { work(); } catch { } }
            Attempt(() => rendererExercise?.Dispose());
            Attempt(() => rendererExercise?.RecordInterruptedPrefix(failure.ToString()));
            Attempt(SaveLongevityPin);
            Attempt(() => comparison?.Dispose());
            Attempt(() => candidateLifetimeSubscription?.Dispose());
            Attempt(() => SaveComparison());
            Attempt(() => { sourceCurrent = false; cancellation.Cancel(); });
            Attempt(() => ground?.Control.Revoke("Emergency pixel diagnostic cleanup"));
            foreach (var value in owned) Attempt(() => { if (value) Destroy(value); });
            Attempt(() => Time.timeScale = originalScale);
            Attempt(() => RenderPipelineManager.endCameraRendering -= Rendered);
            Attempt(() => Application.logMessageReceived -= Logged);
            Attempt(() => { receipt["status"]="FAIL";receipt["cleanupComplete"]=false;receipt["error"]=failure.ToString();
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString(Formatting.Indented)); });
            CompleteOnce(false, failure);
        }
        IEnumerator Cleanup(Exception failure)
        {
            void Attempt(Action work){try{work();}catch(Exception error){if(failure==null)failure=error;}}
            Attempt(()=>rendererExercise?.Dispose());
            Attempt(()=>comparison?.Dispose());
            Attempt(()=>candidateLifetimeSubscription?.Dispose());
            // Verify consumer-owned copies independently while the host, camera
            // and genuine Town/Ground still live. Host/world destruction cannot
            // hide a leaked copy or manufacture a successful Dispose receipt.
            yield return null; yield return null;
            bool comparisonReleased=false;
            Attempt(()=>comparisonReleased=!ReferenceEquals(comparison,null)&&comparison.OwnedResourcesReleased);
            receipt["persistentOwnedResourcesReleased"]=comparisonReleased;
            if (rendererLongevityRequested && !ReferenceEquals(rendererExercise, null))
            {
                Attempt(() => rendererExercise.ObserveFinalOwnerCleanup(comparisonReleased));
                Attempt(SaveLongevityPin);
                receipt["nativeLongevityCompleted"] = rendererExercise.LongevityResult.status == "PASS_ACTUAL_NATIVE_REBUILD_LONGEVITY_NO_GAMEPLAY_ADMISSION";
                if (!(bool)receipt["nativeLongevityCompleted"] && failure == null)
                    failure = new InvalidOperationException("Actual longevity phase and both-buffer owner cleanup did not complete");
            }
            Attempt(()=>SaveComparison());
            if(!comparisonReleased&&failure==null)failure=new InvalidOperationException("Owned persistent copies not independently released");
            bool comparisonMaskRestored=false;
            Attempt(()=>comparisonMaskRestored=comparisonMaskCaptured&&camera&&camera.cullingMask==comparisonOriginalMask);
            receipt["normalCameraMaskUnchanged"]=comparisonMaskRestored;
            if(!comparisonMaskRestored&&failure==null)failure=new InvalidOperationException("Normal camera mask changed before actual scene teardown");
            Attempt(()=>{sourceCurrent=false;cancellation.Cancel();});
            double workerDeadline=Time.realtimeSinceStartupAsDouble+5;
            while(preparationWork!=null&&!preparationWork.IsCompleted&&Time.realtimeSinceStartupAsDouble<workerDeadline)yield return null;
            bool workerTerminal=preparationWork==null||preparationWork.IsCompleted;
            receipt["pureWorkerTerminal"]=workerTerminal;
            if(!workerTerminal&&failure==null)failure=new TimeoutException("Pure preparation worker did not terminate after cancellation");
            Attempt(()=>ground?.Control.Revoke("Case7 pixel diagnostic cleanup before teardown"));
            bool retired=false;Attempt(()=>retired=ground==null||!ground.Lease.IsCurrent);
            receipt["actualOwnerRetiredBeforeTeardown"]=retired;
            if(!retired&&failure==null)failure=new InvalidOperationException("Owned issuer failed retirement");
            foreach(var value in owned)if(value)Attempt(()=>Destroy(value));
            yield return null;yield return null;
            bool meshesGone=false;Attempt(()=>meshesGone=ownedMeshes.All(mesh=>!mesh));
            receipt["ownedMeshesReleased"]=meshesGone;
            if(!meshesGone&&failure==null)failure=new InvalidOperationException("Owned native meshes survived cleanup");
            Attempt(()=>{if(road)Destroy(road);});Attempt(()=>{if(terrain)Destroy(terrain);});
            Attempt(()=>{if(target){target.Release();Destroy(target);}});
            yield return null;yield return null;
            bool objectsGone=false,materialsGone=false;
            Attempt(()=>objectsGone=owned.All(value=>!value));
            Attempt(()=>materialsGone=!road&&!terrain&&!target);
            receipt["ownedObjectsReleased"]=objectsGone;receipt["ownedMaterialsAndRTReleased"]=materialsGone;

            receipt["cleanupComplete"]=meshesGone&&objectsGone&&materialsGone&&workerTerminal&&comparisonReleased&&comparisonMaskRestored;
            if((!objectsGone||!materialsGone)&&failure==null)failure=new InvalidOperationException("Owned objects/material/RT survived cleanup");
            Attempt(()=>Time.timeScale=originalScale);
            Attempt(()=>RenderPipelineManager.endCameraRendering-=Rendered);Attempt(()=>Application.logMessageReceived-=Logged);
            receipt["runtimeErrors"]=new JArray(errors);receipt["error"]=failure?.ToString();
            bool passed=failure==null&&errors.Count==0&&retired&&meshesGone&&objectsGone&&materialsGone&&workerTerminal&&comparisonReleased&&comparisonMaskRestored&&views.Count==6;
            receipt["status"]=passed?"PASS_FULL_TOWN_FACADE_CANDIDATE_PENDING_PIXEL_AND_PERFORMANCE_REVIEW":"FAIL";
            try{File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString(Formatting.Indented));}
            catch(Exception error){passed=false;failure=error;}
            finally{CompleteOnce(passed,failure);}
        }
        void OnDestroy()
        {
            if (!started) return;
            try{comparison?.Dispose();}catch{}try{candidateLifetimeSubscription?.Dispose();}catch{}try{sourceCurrent=false;cancellation.Cancel();}catch{}
            try{ground?.Control.Revoke("Pixel runner destroyed");}catch{}
            foreach (var value in owned) try { if (value) Destroy(value); } catch { }
            try { if (road) Destroy(road); } catch { }
            try { if (terrain) Destroy(terrain); } catch { }
            try { if (target) { target.Release(); Destroy(target); } } catch { }
            try{Time.timeScale=originalScale;}catch{}
            RenderPipelineManager.endCameraRendering-=Rendered;Application.logMessageReceived-=Logged;
        }
    }
}

#endif
