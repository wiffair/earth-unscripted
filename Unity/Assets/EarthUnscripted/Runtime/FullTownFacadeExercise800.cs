#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using EarthUnscripted.FacadePersistentCandidate800;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EarthUnscripted
{
    /// <summary>
    /// Opt-in Editor-QA exercise borrowing the actual full-Town renderer and source.
    /// Creates only its helper and one alternate camera; owns no geometry/material,
    /// owner, source, physical body or readback target. All restore/cleanup attempts
    /// occur independently before the original proof may retire its real world.
    /// Timings include Editor/context/observer cost, never standalone-player FPS.
    /// </summary>
    public sealed class FullTownFacadeExercise800 : MonoBehaviour, IDisposable
    {
        public const string Flag = "-earthUnscriptedFullTownRendererExercise800";
        private const int SteadyWarmFrames = 32;
        private const int SteadySampleFrames = 128;
        private const int TransitionWarmFrames = 8;
        private const int TransitionSampleFrames = 16;
        private const int MaximumCases = 12;
        private const int MaximumProfilerHandles = 16384;
        private const double CaseDeadlineSeconds = 20;
        private const double ExerciseDeadlineSeconds = 180;
        private const float NearDistanceMetres = 180;
        private const float CullDistanceMetres = 900;

        [Serializable] public sealed class Metric
        {
            public string name, category, unit, reason;
            public bool available;
            public long? rawValue;
            public double? milliseconds;
            public string frameAssociation = "Last captured profiler sample; source frame ID is unavailable";
        }
        [Serializable] public sealed class FrameObservation
        {
            public int frame, sourceChunks, residentFeatures, windows, doors, near, far;
            public string cameraEntityId64, targetEntityId64;
            public string[] cullingMatrixF32Bits;
            public int originalSubmissions, nativeReadReservations, ownedMeshes, ownedVertices, ownedIndices;
            public long ownedRawBytes;
            public bool candidate, alternateMain, targetVisible, targetNear;
            public double? completedMainCameraIntervalMilliseconds;
            public double observerValidationWallMilliseconds;
            public List<Metric> metrics;
        }
        [Serializable] public sealed class CaseObservation
        {
            public string name;
            public bool candidate, alternateMain;
            public int requiredWarmFrames, requiredSampleFrames, completedWarmFrames, pendingFrames;
            public Vector3 eye;
            public string sourceCensusSHA256;
            public List<string> pendingReasons = new List<string>();
            public List<FrameObservation> samples = new List<FrameObservation>();
            public FacadePersistentSnapshot800 finalCandidateSnapshot;
        }
        [Serializable] public sealed class Report
        {
            public string schema = "FullTownRendererExercise1";
            public string status = "INCOMPLETE", error;
            public string environment = "Actual Unity Editor PlayMode; not standalone-player FPS";
            public string timingScope = "Whole Editor/main/render-thread markers include context and observer validation. No PNG/readback/encoding during samples.";
            public string originalCameraEntityId64, alternateCameraEntityId64, borrowedTargetEntityId64;
            public bool noPngReadbackDuringSamples = true, standalonePlayerPerformanceAcceptance = false;
            public bool settingsRestored, ownedResourcesReleased, recordersDisposed;
            public bool completedAllCases;
            public List<string> cleanupErrors = new List<string>();
            public List<CaseObservation> cases = new List<CaseObservation>();
        }
        private sealed class RecorderSlot : IDisposable
        {
            internal string Name, Category, Unit, Reason;
            internal ProfilerRecorder Recorder;
            internal bool Created;
            internal Metric Read()
            {
                var result = new Metric { name = Name, category = Category, unit = Unit, reason = Reason };
                if (!Created || !Recorder.Valid || Recorder.Count == 0)
                {
                    result.reason = result.reason ?? "Recorder has no completed sample";
                    return result;
                }
                long value = Recorder.LastValue;
                if (value <= 0)
                {
                    result.reason = "No positive completed sample; not zero-time performance";
                    return result;
                }
                result.available = true;
                result.rawValue = value;
                result.milliseconds = value / 1000000.0;
                result.reason = null;
                return result;
            }
            public void Dispose() { if (Created) { Recorder.Dispose(); Created = false; } }
        }

        public Report Result { get; } = new Report();
        private Camera primaryCamera, alternateCamera, activeCamera;
        private GameObject alternateObject;
        private FacadePersistentTown800 controller;
        private BuildingFacadeChunk[] sources;
        private Matrix4x4[] sourceFrames;
        private Func<bool> current;
        private Func<string> censusHash;
        private string expectedCensus, outputPath;
        private Vector3 savedPosition, anchor, outward;
        private Quaternion savedRotation;
        private string savedTag;
        private bool savedEnabled, savedRequested, disposed, eventsInstalled, observing;
        private int savedMask, lastObservedFrame = -1;
        private RenderTexture savedTarget;
        private readonly List<RecorderSlot> recorders = new List<RecorderSlot>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        private int featureCount, expectedWindows, expectedDoors, expectedNear, expectedFar;
        private int completedWarm;
        private bool expectedTargetVisible, expectedTargetNear, collecting;
        private double previousRenderTime, exerciseStarted;
        private CancellationToken cancellation;
        private CaseObservation activeCase;
        private Exception callbackFailure;

        public static bool Requested
        {
            get
            {
                int count = Environment.GetCommandLineArgs().Count(value => value == Flag);
                if (count > 1) throw new ArgumentException("Renderer exercise flag supplied more than once");
                return count == 1;
            }
        }

        public static FullTownFacadeExercise800 CreateOwned(Transform actualProof,
            Camera actualMainCamera, FacadePersistentTown800 actualController,
            BuildingFacadeChunk[] actualSources, Func<bool> actualOwnerCurrent,
            Func<string> captureActualCensusHash, string actualExpectedHash, string freshOutputPath, string actualRawDigest = null, string actualSourceDigest = null, string actualFinalDigest = null)
        {
            if (!Application.isEditor || !Application.isPlaying || !actualProof || !actualMainCamera
                || !actualController || actualSources == null || actualSources.Length == 0
                || actualOwnerCurrent == null || captureActualCensusHash == null)
                throw new ArgumentException("Actual Editor proof camera/controller/source/owner callbacks required");
            if (!Path.IsPathRooted(freshOutputPath) || File.Exists(freshOutputPath))
                throw new IOException("Fresh explicit isolated exercise report path required");
            var host = new GameObject("OWNED full Town renderer exercise");
            host.transform.SetParent(actualProof, false);
            var exercise = host.AddComponent<FullTownFacadeExercise800>();
            try
            {
                exercise.Initialize(actualMainCamera, actualController, actualSources, actualOwnerCurrent,
                    captureActualCensusHash, actualExpectedHash, freshOutputPath);
                exercise.ConfigureLongevity(actualRawDigest, actualSourceDigest, actualFinalDigest);
                return exercise;
            }
            catch { exercise.Dispose(); throw; }
        }

        private void Initialize(Camera main, FacadePersistentTown800 actualController,
            BuildingFacadeChunk[] actualSources, Func<bool> ownerCurrent, Func<string> captureHash,
            string expectedHash, string reportPath)
        {
            primaryCamera = main; controller = actualController;
            sources = (BuildingFacadeChunk[])actualSources.Clone();
            current = ownerCurrent; censusHash = captureHash; expectedCensus = expectedHash; outputPath = reportPath;
            savedPosition = main.transform.position; savedRotation = main.transform.rotation;
            savedTag = main.tag; savedEnabled = main.enabled; savedMask = main.cullingMask;
            savedTarget = main.targetTexture; savedRequested = controller.CandidateRequested;
            sourceFrames = sources.Select(source => source.transform.localToWorldMatrix).ToArray();
            featureCount = sources.Sum(source => source.FeatureCount);
            var first = sources.First(source => source.FeatureCount > 0);
            var feature = first.Features[0];
            anchor = first.transform.TransformPoint(feature.Position);
            outward = first.transform.TransformDirection(feature.Rotation * Vector3.forward).normalized;
            if (outward.sqrMagnitude < .5f) throw new InvalidOperationException("Actual reference feature normal unavailable");
            alternateObject = new GameObject("OWNED alternate real MainCamera exercise");
            alternateObject.transform.SetParent(transform, false);
            alternateCamera = alternateObject.AddComponent<Camera>();
            alternateCamera.enabled = false;
            alternateCamera.CopyFrom(primaryCamera);
            alternateCamera.enabled = false;
            alternateCamera.tag = "Untagged";
            alternateObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;
            Result.originalCameraEntityId64 = EntityId.ToULong(primaryCamera.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Result.alternateCameraEntityId64 = EntityId.ToULong(alternateCamera.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Result.borrowedTargetEntityId64 = EntityId.ToULong(savedTarget.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var material in new[] { BuildingAppearanceMaterials.Frame, BuildingAppearanceMaterials.Glass,
                BuildingAppearanceMaterials.Door(0), BuildingAppearanceMaterials.Door(1),
                BuildingAppearanceMaterials.Door(2), BuildingAppearanceMaterials.Door(3) })
            {
                string id = EntityId.ToULong(material.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture);
                materials[id] = material;
                shaders[id] = material.shader;
            }
            RenderPipelineManager.endCameraRendering += EndCamera;
            eventsInstalled = true;
            DiscoverRecorders();
        }

        private void DiscoverRecorders()
        {
            string[] requestedNames = { "Main Thread", "Render Thread", "GPU Frame Time" };
            var available = new List<ProfilerRecorderHandle>();
            string discoveryError = null;
            try
            {
                ProfilerRecorderHandle.GetAvailable(available);
                if (available.Count > MaximumProfilerHandles)
                    discoveryError = "Profiler catalog exceeds bounded observer limit; unavailable, not zero";
            }
            catch (Exception error) { discoveryError = "Profiler discovery unavailable: " + error.GetType().Name; }
            foreach (string name in requestedNames)
            {
                var slot = new RecorderSlot { Name = name, Reason = discoveryError ?? "Exact timing metric unavailable" };
                recorders.Add(slot);
                if (discoveryError != null) continue;
                var matches = available.Select(ProfilerRecorderHandle.GetDescription)
                    .Where(description => description.Name == name && description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds).ToArray();
                if (matches.Length != 1)
                {
                    slot.Reason = matches.Length == 0 ? "Exact nanosecond metric unavailable" : "Ambiguous metric name; no arbitrary handle selected";
                    continue;
                }
                try
                {
                    slot.Category = matches[0].Category.ToString(); slot.Unit = matches[0].UnitType.ToString();
                    slot.Recorder = ProfilerRecorder.StartNew(matches[0].Category, matches[0].Name, 1);
                    slot.Created = slot.Recorder.Valid;
                    slot.Reason = slot.Created ? null : "Recorder unavailable";
                }
                catch (Exception error) { slot.Reason = "Recorder unavailable: " + error.GetType().Name; }
            }
        }

        /// <summary>Owns the exercise enumerator. Its finally restores before proof cleanup.</summary>
        public IEnumerator Run(CancellationToken token)
        {
            cancellation = token; exerciseStarted = Time.realtimeSinceStartupAsDouble;
            IEnumerator work = RunRequestedWork();
            try
            {
                while (true)
                {
                    bool more;
                    try { more = work.MoveNext(); }
                    catch (Exception error) { Result.error = error.ToString(); if (longevityRequested) LongevityResult.error = Result.error; break; }
                    if (!more) break;
                    yield return work.Current;
                }
            }
            finally
            {
                try { (work as IDisposable)?.Dispose(); }
                catch (Exception error) { Result.cleanupErrors.Add("Case iterator disposal: " + error); }
                Dispose();
            }
            // Real deferred Unity destruction must complete while original world lives.
            yield return null; yield return null;
            Result.ownedResourcesReleased = !alternateObject && !this;
            Result.status = Result.completedAllCases && Result.error == null && Result.cleanupErrors.Count == 0
                && Result.settingsRestored && Result.ownedResourcesReleased && Result.recordersDisposed
                ? "PASS_BOUNDED_EDITOR_RENDERER_EXERCISE_PENDING_PLAYER_PERFORMANCE_REVIEW" : "FAIL";
            try { WriteLongevityReport(); }
            catch (Exception error) { Result.error = "Longevity report write refused: " + error; Result.status = "FAIL"; LongevityResult.error = Result.error; }
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new ObservationConverter());
            try { File.WriteAllText(outputPath, JsonConvert.SerializeObject(Result, Formatting.Indented, settings)); }
            catch (Exception error) { Result.error = "Exercise report write refused: " + error; Result.status = "FAIL"; }
        }

        private IEnumerator RunCases()
        {
            // These are our own known cases, stepped explicitly so their exceptions
            // reach Run's failure/restore path instead of escaping a Unity nested yield.
            var cases = new List<IEnumerator> {
                RunCase("steady-original", false, false, savedPosition, SteadyWarmFrames, SteadySampleFrames, null, null),
                RunCase("steady-candidate", true, false, savedPosition, SteadyWarmFrames, SteadySampleFrames, null, null) };
            for (int repeat = 1; repeat <= 2; repeat++)
            {
                cases.Add(RunCase("toggle-original-" + repeat, false, false, savedPosition, TransitionWarmFrames, TransitionSampleFrames, null, null));
                cases.Add(RunCase("toggle-candidate-" + repeat, true, false, savedPosition, TransitionWarmFrames, TransitionSampleFrames, null, null));
            }
            cases.Add(RunCase("alternate-main-candidate", true, true, savedPosition, TransitionWarmFrames, TransitionSampleFrames, null, null));
            cases.Add(RunCase("return-main-candidate", true, false, savedPosition, TransitionWarmFrames, TransitionSampleFrames, null, null));
            cases.Add(RunCase("inside180-candidate", true, false, anchor + outward * 179.5f, TransitionWarmFrames, TransitionSampleFrames, true, true));
            cases.Add(RunCase("outside180-candidate", true, false, anchor + outward * 180.5f, TransitionWarmFrames, TransitionSampleFrames, true, false));
            cases.Add(RunCase("inside900-candidate", true, false, anchor + outward * 899.5f, TransitionWarmFrames, TransitionSampleFrames, true, false));
            cases.Add(RunCase("outside900-candidate", true, false, anchor + outward * 900.5f, TransitionWarmFrames, TransitionSampleFrames, false, false));
            foreach (IEnumerator actualCase in cases)
            {
                try
                {
                    while (actualCase.MoveNext())
                        yield return actualCase.Current;
                }
                finally { (actualCase as IDisposable)?.Dispose(); }
            }
            Result.completedAllCases = true;
        }

        private IEnumerator RunCase(string name, bool candidate, bool alternate, Vector3 eye,
            int warmFrames, int sampleFrames, bool? targetVisible, bool? targetNear)
        {
            Require(Result.cases.Count < MaximumCases, "Bounded exercise case count");
            CheckCurrent();
            observing = false;
            primaryCamera.enabled = false; alternateCamera.enabled = false;
            primaryCamera.tag = "Untagged"; alternateCamera.tag = "Untagged";
            activeCamera = alternate ? alternateCamera : primaryCamera;
            activeCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(anchor - eye));
            if (eye == savedPosition) activeCamera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            activeCamera.tag = "MainCamera"; activeCamera.enabled = true;
            controller.SetEnabled(candidate);
            activeCase = new CaseObservation { name = name, candidate = candidate, alternateMain = alternate,
                requiredWarmFrames = warmFrames, requiredSampleFrames = sampleFrames, eye = eye, sourceCensusSHA256 = expectedCensus };
            Result.cases.Add(activeCase);
            ComputeExpected(eye);
            if (targetVisible.HasValue) Require(expectedTargetVisible == targetVisible.Value, "Requested cull-side fixture actually straddles900m");
            if (targetNear.HasValue) Require(expectedTargetNear == targetNear.Value, "Requested LOD-side fixture actually straddles180m");
            completedWarm = 0; collecting = false; previousRenderTime = 0; callbackFailure = null;
            observing = true;
            double caseStarted = Time.realtimeSinceStartupAsDouble;
            while (completedWarm < warmFrames)
            {
                CheckWait(caseStarted);
                yield return null;
            }
            activeCase.completedWarmFrames = completedWarm;
            collecting = true;
            while (activeCase.samples.Count < sampleFrames)
            {
                CheckWait(caseStarted);
                yield return null;
            }
            observing = false; collecting = false;
            CheckCurrent();
            Require(censusHash() == expectedCensus, "Actual full source census unchanged after case");
        }

        private void ComputeExpected(Vector3 eye)
        {
            expectedWindows = expectedDoors = expectedNear = expectedFar = 0;
            float far = Mathf.Min(CullDistanceMetres, activeCamera.farClipPlane);
            foreach (var source in sources)
            {
                Matrix4x4 root = source.transform.localToWorldMatrix;
                foreach (var feature in source.Features)
                {
                    float squared = (root.MultiplyPoint3x4(feature.Position) - eye).sqrMagnitude;
                    if (squared > far * far) continue;
                    if (squared < NearDistanceMetres * NearDistanceMetres) expectedNear++; else expectedFar++;
                    if (feature.IsDoor) expectedDoors++; else expectedWindows++;
                }
            }
            ClassifySquaredDistance((anchor - eye).sqrMagnitude, activeCamera.farClipPlane,
                out expectedTargetVisible, out expectedTargetNear);
        }

        /// <summary>Pure distance policy matching the actual BFC strict-near/inclusive-far thresholds.</summary>
        internal static void ClassifySquaredDistance(float squaredMetres, float farClipMetres,
            out bool visible, out bool near)
        {
            if (float.IsNaN(squaredMetres) || float.IsInfinity(squaredMetres) || squaredMetres < 0
                || float.IsNaN(farClipMetres) || float.IsInfinity(farClipMetres) || farClipMetres <= 0)
                throw new ArgumentException("Finite nonnegative squared distance and positive far clip required");
            float farMetres = Math.Min(CullDistanceMetres, farClipMetres);
            visible = squaredMetres <= farMetres * farMetres;
            near = visible && squaredMetres < NearDistanceMetres * NearDistanceMetres;
        }

        private void EndCamera(ScriptableRenderContext context, Camera actual)
        {
            if (longevityObserving) { ObserveLongevityCamera(actual); return; }
            if (!observing || disposed || !ReferenceEquals(actual, activeCamera) || lastObservedFrame == Time.frameCount) return;
            lastObservedFrame = Time.frameCount;
            double renderTime = Time.realtimeSinceStartupAsDouble;
            var observerClock = Stopwatch.StartNew();
            try
            {
                CheckCurrent();
                Require(ReferenceEquals(Camera.main, activeCamera) && activeCamera.enabled
                    && ReferenceEquals(activeCamera.targetTexture, savedTarget) && activeCamera.cullingMask == savedMask,
                    "Actual intended main camera/borrowed RT/normal mask");
                for (int index = 0; index < sources.Length; index++)
                    Require(sources[index] && sources[index].transform.localToWorldMatrix.Equals(sourceFrames[index]), "Actual source root frame unchanged");
                foreach (var pair in materials)
                    Require(pair.Value && ReferenceEquals(pair.Value.shader, shaders[pair.Key]), "Actual shared material/shader refs unchanged");
                FacadePersistentSnapshot800 snapshot = null;
                if (activeCase.candidate)
                {
                    if (!controller.AllReady)
                    {
                        Require(!collecting, "Steady candidate frame became incomplete: " + controller.Reason);
                        activeCase.pendingFrames++;
                        if (!activeCase.pendingReasons.Contains(controller.Reason) && activeCase.pendingReasons.Count < 32)
                            activeCase.pendingReasons.Add(controller.Reason);
                        completedWarm = 0;
                        return;
                    }
                    snapshot = controller.CaptureSnapshot();
                    Require(snapshot.Frame == Time.frameCount && snapshot.AllReady && !snapshot.Incomplete
                        && snapshot.BoundsValid && snapshot.CameraMainOnly && snapshot.NativeReadReservations <= 4096,
                        "Actual complete current-frame bounded candidate snapshot");
                    Require(snapshot.SourceChunks == sources.Length && snapshot.ResidentFeatures == featureCount
                        && snapshot.VisibleWindows == expectedWindows && snapshot.VisibleDoors == expectedDoors
                        && snapshot.NearFeatures == expectedNear && snapshot.FarFeatures == expectedFar,
                        "Every actual full-Town feature/LOD cohort represented");
                    Require(snapshot.OwnedMeshes <= 4096 && snapshot.OwnedVertices <= 2000000
                        && snapshot.OwnedIndices <= 3000000 && snapshot.RawBytes <= 256L * 1024 * 1024, "Actual persistent cache stays within original caps");
                    foreach (var row in snapshot.SourceAndOwnedBounds)
                        Require(row.OwnedBoundsContainStreams && materials.ContainsKey(row.MaterialEntityId64)
                            && EntityId.ToULong(shaders[row.MaterialEntityId64].GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture) == row.ShaderEntityId64,
                            "Every current bounds row retains actual shared material/shader and valid streams");
                    activeCase.finalCandidateSnapshot = snapshot;
                }
                else
                {
                    snapshot = controller.CaptureSnapshot();
                    Require(!controller.CandidateRequested && snapshot.Frame == Time.frameCount
                        && snapshot.NativeReadReservations >= 0 && snapshot.NativeReadReservations <= 4096,
                        "Actual original-mode completed frame and reported reservations");
                }
                Matrix4x4 actualCulling = activeCamera.cullingMatrix;
                var cullingBits = new string[16];
                for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 4; column++)
                        cullingBits[row * 4 + column] = unchecked((uint)BitConverter.SingleToInt32Bits(actualCulling[row, column])).ToString("x8");
                Require(snapshot.CurrentCameraCullingMatrixF32Bits.SequenceEqual(cullingBits), "Snapshot belongs to actual completed camera culling matrix");
                var statistics = BuildingFacadeChunk.Statistics;
                Require(statistics.frame == Time.frameCount && statistics.residentFeatures == featureCount
                    && statistics.submittedWindows == expectedWindows && statistics.submittedDoors == expectedDoors,
                    "Actual original-source statistics account complete current cohort");
                Require(!activeCase.candidate || statistics.drawSubmissions == 0, "No stale original Graphics draws while candidate owns presentation");
                Require(current(), "Actual owners still current after frame validation");
                if (!collecting) { completedWarm++; return; }
                var rowObservation = new FrameObservation { frame = Time.frameCount, sourceChunks = sources.Length,
                    cameraEntityId64 = EntityId.ToULong(activeCamera.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    targetEntityId64 = EntityId.ToULong(activeCamera.targetTexture.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    cullingMatrixF32Bits = cullingBits,
                    residentFeatures = featureCount, windows = expectedWindows, doors = expectedDoors, near = expectedNear, far = expectedFar,
                    candidate = activeCase.candidate, alternateMain = activeCase.alternateMain,
                    targetVisible = expectedTargetVisible, targetNear = expectedTargetNear,
                    originalSubmissions = statistics.drawSubmissions,
                    nativeReadReservations = snapshot == null ? 0 : snapshot.NativeReadReservations,
                    ownedMeshes = snapshot == null ? 0 : snapshot.OwnedMeshes,
                    ownedVertices = snapshot == null ? 0 : snapshot.OwnedVertices,
                    ownedIndices = snapshot == null ? 0 : snapshot.OwnedIndices,
                    ownedRawBytes = snapshot == null ? 0 : snapshot.RawBytes,
                    completedMainCameraIntervalMilliseconds = previousRenderTime == 0 ? (double?)null : (renderTime - previousRenderTime) * 1000,
                    metrics = recorders.Select(recorder => recorder.Read()).ToList() };
                observerClock.Stop(); rowObservation.observerValidationWallMilliseconds = observerClock.Elapsed.TotalMilliseconds;
                Require(current(), "Actual owners still current after recorder observations");
                activeCase.samples.Add(rowObservation);
            }
            catch (Exception error) { callbackFailure = error; }
            finally { previousRenderTime = renderTime; }
        }

        private void CheckWait(double caseStarted)
        {
            CheckCurrent();
            if (callbackFailure != null) throw new InvalidOperationException("Actual render callback refused", callbackFailure);
            Require(Time.realtimeSinceStartupAsDouble - caseStarted < CaseDeadlineSeconds, "Bounded warm/current-frame exercise case");
        }
        private void CheckCurrent()
        {
            cancellation.ThrowIfCancellationRequested();
            Require(!disposed && current() && primaryCamera && controller
                && (longevityRunning ? Time.realtimeSinceStartupAsDouble - longevityStarted < LongevityDeadlineSeconds
                    : Time.realtimeSinceStartupAsDouble - exerciseStarted < ExerciseDeadlineSeconds),
                "Actual source/Town/Ground/controller lifetime and bounded exercise");
        }
        private static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("FULL TOWN EXERCISE: " + reason); }
        private void Release(Action action, string label)
        {
            try { action(); } catch (Exception error) { Result.cleanupErrors.Add(label + ": " + error); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; observing = false; longevityObserving = false;
            if (eventsInstalled) Release(() => RenderPipelineManager.endCameraRendering -= EndCamera, "Render unsubscribe");
            eventsInstalled = false;
            foreach (var recorder in recorders) Release(recorder.Dispose, "Profiler recorder disposal");
            Result.recordersDisposed = recorders.All(recorder => !recorder.Created);
            Release(() => { if (alternateCamera) { alternateCamera.enabled = false; alternateCamera.tag = "Untagged"; } }, "Alternate camera quiesce");
            Release(() => { if (primaryCamera) primaryCamera.transform.SetPositionAndRotation(savedPosition, savedRotation); }, "Primary pose restore");
            Release(() => { if (primaryCamera) primaryCamera.tag = savedTag; }, "Primary tag restore");
            Release(() => { if (primaryCamera) primaryCamera.enabled = savedEnabled; }, "Primary enabled restore");
            Release(() => { if (controller) controller.SetEnabled(savedRequested); }, "Renderer mode restore");
            Release(() => Result.settingsRestored = primaryCamera && primaryCamera.transform.position.Equals(savedPosition)
                && primaryCamera.transform.rotation.Equals(savedRotation) && primaryCamera.tag == savedTag
                && primaryCamera.enabled == savedEnabled && primaryCamera.cullingMask == savedMask
                && ReferenceEquals(primaryCamera.targetTexture, savedTarget) && controller
                && controller.CandidateRequested == savedRequested, "Actual camera/settings restore verification");
            Release(() => { if (alternateObject) Destroy(alternateObject); }, "Alternate owned camera destruction");
            Release(() => Destroy(gameObject), "Exercise owned helper destruction");
        }
        // This second experiment is separate from the fixed twelve ordinary cases.
        // Only genuine completed-camera snapshots can advance its visited prefix.
        public const string LongevityFlag = "-earthUnscriptedFullTownFacadeLongevity800";
        public const string LongevityPathFlag = "-earthUnscriptedFullTownFacadeLongevityPath800";
        private const string LongevityPathSha256 = "317cc62df99994d8572033c4177b1983a9e5bdaadc8095bc66afa47624f4915e";
        private const int MaximumLongevityLoops = 36, LongevityPathPoints = 55;
        private const long RequiredRebuildDelta = 65536;
        private const double LongevityDeadlineSeconds = 180;
        private const int MaximumLongevityReportBytes = 8 * 1024 * 1024;

        [Serializable] public sealed class LongevityPointObservation
        {
            public int loop, point, frame, sourceChunks, residentFeatures, near, far, windows, doors;
            public int selectedBoundsRows, cachedBatches, bufferMeshes, lifetimeMeshAllocations, nativeReadReservations;
            public long successfulRebuildCommits, rebuildDelta, rawBufferBytes, totalModeledRawBytes;
            public int retainedVertexCapacity, retainedIndexCapacity;
            public string[] cullingMatrixF32Bits;
            public Vector3 actualCameraEye;
        }
        [Serializable] public sealed class LongevityReport
        {
            public string schema = "ActualFullTownFacadeNativeLongevity1", status = "NOT_REQUESTED", error;
            public string scope = "Actual completed-frame renderer camera path; no vehicle/physics/gameplay or player-FPS acceptance";
            public string pathFile, pathSHA256, rawDocumentDigest, sourceDigest, finalBufferDigest;
            public int maximumLoops = MaximumLongevityLoops, pathPoints = LongevityPathPoints;
            public double maximumAdditionalSeconds = LongevityDeadlineSeconds;
            public long requiredSuccessfulRebuildDeltaExclusive = RequiredRebuildDelta;
            public bool requested, phaseCompleted, reachedRebuildGoal, poolStabilityEstablished;
            public bool settingsRestored, helperResourcesReleased, recordersDisposed, finalOwnerCleanupObserved, ownerPoolReleased;
            public int completedLoops, attemptedLoop, attemptedPoint = -1, pendingFrames;
            public long startingSuccessfulRebuildCommits, endingSuccessfulRebuildCommits, successfulRebuildDelta;
            public int startingLifetimeMeshAllocations, endingLifetimeMeshAllocations, maximumLifetimeMeshAllocations;
            public int maximumCachedBatches, maximumOwnedBufferMeshes, maximumRetainedVertices, maximumRetainedIndices;
            public long maximumRawBufferBytes, maximumTotalModeledRawBytes;
            public long maximumPeakBuildManagedRawBytes, maximumSourceCopyManagedRawBytes, maximumBookkeepingManagedRawBytes;
            public int stableLifetimeMeshAllocations, stableCachedBatches, stableRetainedVertices, stableRetainedIndices;
            public long stableRawBufferBytes, stableTotalModeledRawBytes;
            public double elapsedAdditionalWallSeconds;
            public List<string> pendingReasons = new List<string>();
            public List<LongevityPointObservation> visited = new List<LongevityPointObservation>();
        }
        private sealed class LongevityPoint
        {
            internal Vector3 Eye;
            internal int Near, Far, SelectedBatches;
        }
        public LongevityReport LongevityResult { get; } = new LongevityReport();
        private LongevityPoint[] longevityPath;
        private bool longevityRequested, longevityRunning, longevityObserving, longevityPointObserved;
        private int longevityLoop, longevityPointIndex;
        private double longevityStarted;
        private string longevityOutputPath;
        private FacadePersistentSnapshot800 longevityLastSnapshot;

        public static bool LongevityRequested
        {
            get
            {
                int count = Environment.GetCommandLineArgs().Count(value => value == LongevityFlag);
                if (count > 1) throw new ArgumentException("Longevity flag supplied more than once");
                return count == 1;
            }
        }
        private void ConfigureLongevity(string actualRaw, string actualSource, string actualFinal)
        {
            longevityRequested = LongevityRequested;
            if (!longevityRequested) return;
            Require(Requested, "Longevity requires the ordinary twelve-case exercise flag");
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, LongevityPathFlag);
            Require(index >= 0 && index == Array.LastIndexOf(arguments, LongevityPathFlag)
                && index + 1 < arguments.Length, "One explicit pinned longevity data path");
            string path = arguments[index + 1];
            Require(Path.IsPathRooted(path) && Path.GetFullPath(path) == path, "Absolute canonical path data");
            var file = new FileInfo(path);
            Require(file.Exists && file.Length > 0 && file.Length <= 128 * 1024, "Bounded exact55-point data");
            byte[] bytes = File.ReadAllBytes(path);
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                string digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Require(digest == LongevityPathSha256, "Exact reviewed path data; never a new authority grant");
            }
            var reader = new JsonTextReader(new StringReader(new System.Text.UTF8Encoding(false, true).GetString(bytes)))
            { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
            var data = Newtonsoft.Json.Linq.JObject.Load(reader,
                new Newtonsoft.Json.Linq.JsonLoadSettings { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
            Require((string)data["schema"] == "FullTownRendererCameraPathData1"
                && (string)data["rawDocumentDigest"] == actualRaw && (string)data["sourceDigest"] == actualSource
                && (string)data["finalBufferDigest"] == actualFinal, "Actual proof raw/source/final match pinned data");
            var rows = data["points"] as Newtonsoft.Json.Linq.JArray;
            Require(rows != null && rows.Count == LongevityPathPoints, "All55 ordered data positions, no prefix replacement");
            longevityPath = new LongevityPoint[rows.Count];
            for (int point = 0; point < rows.Count; point++)
            {
                var row = rows[point];
                Require((int)row["index"] == point && ((string)row["sourceRoadId"]).StartsWith("59659256-", StringComparison.Ordinal), "Ordered actual OldPant source path");
                var xyz = row["eyeXYZ"] as Newtonsoft.Json.Linq.JArray;
                Require(xyz != null && xyz.Count == 3, "Exact eyeXYZ shape");
                var eye = new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2]);
                Require(Finite(eye.x) && Finite(eye.y) && Finite(eye.z), "Finite observed camera positions");
                longevityPath[point] = new LongevityPoint { Eye = eye, Near = (int)row["expectedNear"],
                    Far = (int)row["expectedFar"], SelectedBatches = (int)row["expectedSelectedBatchCount"] };
            }
            longevityOutputPath = Path.Combine(Path.GetDirectoryName(outputPath), "facade-native-longevity.json");
            Require(!File.Exists(longevityOutputPath), "Fresh separate longevity report");
            LongevityResult.requested = true; LongevityResult.status = "PENDING";
            LongevityResult.pathFile = path; LongevityResult.pathSHA256 = LongevityPathSha256;
            LongevityResult.rawDocumentDigest = actualRaw; LongevityResult.sourceDigest = actualSource;
            LongevityResult.finalBufferDigest = actualFinal;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private IEnumerator RunRequestedWork()
        {
            IEnumerator ordinary = RunCases();
            try { while (ordinary.MoveNext()) yield return ordinary.Current; }
            finally { (ordinary as IDisposable)?.Dispose(); }
            if (!longevityRequested) yield break;
            IEnumerator extended = RunLongevity();
            try { while (extended.MoveNext()) yield return extended.Current; }
            finally { (extended as IDisposable)?.Dispose(); }
        }
        private IEnumerator RunLongevity()
        {
            longevityRunning = true; longevityStarted = Time.realtimeSinceStartupAsDouble;
            LongevityResult.status = "RUNNING";
            observing = false; collecting = false; callbackFailure = null;
            // Last ordinary case already has the genuine primary/candidate alive.
            // Baseline counters are observed BEFORE moving to the first path point.
            Require(ReferenceEquals(activeCamera, primaryCamera) && controller.CandidateRequested,
                "Ordinary cases completed on the real primary candidate");
            longevityLoop = 0; longevityPointIndex = -1; longevityPointObserved = false;
            ComputeExpected(activeCamera.transform.position);
            longevityObserving = true;
            double pointStarted = Time.realtimeSinceStartupAsDouble;
            try
            {
                while (!longevityPointObserved) { CheckLongevityWait(pointStarted); yield return null; }
                CheckLongevityWait(pointStarted);
                for (int loop = 1; loop <= MaximumLongevityLoops; loop++)
                {
                    for (int point = 0; point < longevityPath.Length; point++)
                    {
                        CheckCurrent(); longevityLoop = loop; longevityPointIndex = point;
                        LongevityResult.attemptedLoop = loop; LongevityResult.attemptedPoint = point;
                        var wanted = longevityPath[point];
                        activeCamera.transform.SetPositionAndRotation(wanted.Eye, Quaternion.LookRotation(anchor - wanted.Eye));
                        ComputeExpected(wanted.Eye);
                        Require(expectedNear == wanted.Near && expectedFar == wanted.Far,
                            "Exact pinned path expected full source cohort matches current source");
                        longevityPointObserved = false; pointStarted = Time.realtimeSinceStartupAsDouble;
                        while (!longevityPointObserved) { CheckLongevityWait(pointStarted); yield return null; }
                        CheckLongevityWait(pointStarted);
                        if (point == longevityPath.Length - 1) LongevityResult.completedLoops = loop;
                        if (LongevityResult.successfulRebuildDelta > RequiredRebuildDelta)
                        {
                            Require(LongevityResult.poolStabilityEstablished, "Two full loops establish pool stability before success");
                            LongevityResult.reachedRebuildGoal = true; LongevityResult.phaseCompleted = true;
                            yield break;
                        }
                    }
                    LongevityResult.completedLoops = loop;
                    CheckCurrent();
                    Require(censusHash() == expectedCensus, "Actual full source census unchanged after each longevity loop");
                    CheckCurrent();
                    if (loop == 2)
                    {
                        LongevityResult.stableLifetimeMeshAllocations = longevityLastSnapshot.LifetimeOwnedMeshAllocations;
                        LongevityResult.stableCachedBatches = longevityLastSnapshot.OwnedMeshes;
                        LongevityResult.stableRetainedVertices = longevityLastSnapshot.OwnedVertices;
                        LongevityResult.stableRetainedIndices = longevityLastSnapshot.OwnedIndices;
                        LongevityResult.stableRawBufferBytes = longevityLastSnapshot.RawBytes;
                        LongevityResult.stableTotalModeledRawBytes = longevityLastSnapshot.TotalModeledRawBytes;
                        LongevityResult.poolStabilityEstablished = true;
                    }
                }
                throw new InvalidOperationException("Bounded36-loop prefix did not exceed65536 actual successful commit DELTA");
            }
            finally
            {
                longevityObserving = false;
                LongevityResult.elapsedAdditionalWallSeconds = Time.realtimeSinceStartupAsDouble - longevityStarted;
            }
        }
        private void CheckLongevityWait(double pointStarted)
        {
            CheckCurrent();
            if (callbackFailure != null) throw new InvalidOperationException("Actual longevity render callback refused", callbackFailure);
            Require(Time.realtimeSinceStartupAsDouble - pointStarted < CaseDeadlineSeconds, "Bounded real path-position settle");
        }
        private void ObserveLongevityCamera(Camera actual)
        {
            if (disposed || !ReferenceEquals(actual, activeCamera) || lastObservedFrame == Time.frameCount || longevityPointObserved) return;
            lastObservedFrame = Time.frameCount;
            try
            {
                CheckCurrent();
                Require(ReferenceEquals(Camera.main, primaryCamera) && activeCamera.enabled
                    && ReferenceEquals(activeCamera.targetTexture, savedTarget) && activeCamera.cullingMask == savedMask,
                    "Genuine current primary/borrowed target/normal mask on longevity path");
                for (int i = 0; i < sources.Length; i++)
                    Require(sources[i] && sources[i].transform.localToWorldMatrix.Equals(sourceFrames[i]), "Actual source root frames unchanged");
                foreach (var pair in materials)
                    Require(pair.Value && ReferenceEquals(pair.Value.shader, shaders[pair.Key]), "Actual shared materials/shaders retained");
                if (!controller.AllReady)
                {
                    LongevityResult.pendingFrames++;
                    if (LongevityResult.pendingReasons.Count < 32 && !LongevityResult.pendingReasons.Contains(controller.Reason))
                        LongevityResult.pendingReasons.Add(controller.Reason);
                    return;
                }
                var snapshot = controller.CaptureSnapshot();
                Require(snapshot.Frame == Time.frameCount && snapshot.AllReady && !snapshot.Incomplete && snapshot.BoundsValid
                    && snapshot.CameraMainOnly && snapshot.NativeReadReservations >= 0 && snapshot.NativeReadReservations <= 4096,
                    "Actual completed current-frame full bounded path snapshot");
                Require(snapshot.SourceChunks == sources.Length && snapshot.ResidentFeatures == featureCount
                    && snapshot.VisibleWindows == expectedWindows && snapshot.VisibleDoors == expectedDoors
                    && snapshot.NearFeatures == expectedNear && snapshot.FarFeatures == expectedFar,
                    "Entire actual path source/LOD cohort retained");
                if (longevityPointIndex >= 0)
                {
                    Require(activeCamera.transform.position.Equals(longevityPath[longevityPointIndex].Eye)
                        && snapshot.SourceAndOwnedBounds.Length == longevityPath[longevityPointIndex].SelectedBatches,
                        "Actual requested eye and ALL selected named batches, not a partial prefix");
                }
                foreach (var row in snapshot.SourceAndOwnedBounds)
                    Require(row.OwnedBoundsContainStreams && materials.ContainsKey(row.MaterialEntityId64)
                        && EntityId.ToULong(shaders[row.MaterialEntityId64].GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture) == row.ShaderEntityId64,
                        "Every actual path row uses current shared material and validated bounds");
                var statistics = BuildingFacadeChunk.Statistics;
                Require(statistics.frame == Time.frameCount && statistics.residentFeatures == featureCount
                    && statistics.submittedWindows == expectedWindows && statistics.submittedDoors == expectedDoors
                    && statistics.drawSubmissions == 0, "Actual full-cohort source statistics and no original fallback submissions");
                Matrix4x4 matrix = activeCamera.cullingMatrix; var bits = new string[16];
                for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                    bits[row * 4 + column] = unchecked((uint)BitConverter.SingleToInt32Bits(matrix[row, column])).ToString("x8");
                Require(snapshot.CurrentCameraCullingMatrixF32Bits.SequenceEqual(bits), "Actual completed camera matrix, not a stale snapshot");
                Require(snapshot.OwnedMeshes > 0 && snapshot.OwnedMeshes <= 4096 && snapshot.OwnedBufferMeshes <= snapshot.OwnedMeshes * 2
                    && snapshot.LifetimeOwnedMeshAllocations == snapshot.OwnedBufferMeshes && snapshot.LifetimeOwnedMeshAllocations <= 65536
                    && snapshot.OwnedVertices <= 2000000 && snapshot.OwnedIndices <= 3000000
                    && snapshot.RawBytes <= 256L * 1024 * 1024 && snapshot.TotalModeledRawBytes <= 256L * 1024 * 1024
                    && snapshot.PendingBuildManagedRawBytes == 0, "Actual BOTH-buffer and total modeled capacities remain bounded");
                Require(snapshot.SuccessfulMeshRebuildCommits >= 0, "Actual nonnegative successful commit counter");
                if (longevityPointIndex < 0)
                {
                    LongevityResult.startingSuccessfulRebuildCommits = snapshot.SuccessfulMeshRebuildCommits;
                    LongevityResult.startingLifetimeMeshAllocations = snapshot.LifetimeOwnedMeshAllocations;
                }
                else
                {
                    Require(snapshot.SuccessfulMeshRebuildCommits >= LongevityResult.endingSuccessfulRebuildCommits
                        && snapshot.LifetimeOwnedMeshAllocations >= LongevityResult.endingLifetimeMeshAllocations,
                        "Actual lifetime counters never reset or wrap");
                    if (LongevityResult.poolStabilityEstablished)
                        Require(snapshot.LifetimeOwnedMeshAllocations == LongevityResult.stableLifetimeMeshAllocations
                            && snapshot.OwnedMeshes == LongevityResult.stableCachedBatches
                            && snapshot.OwnedVertices == LongevityResult.stableRetainedVertices
                            && snapshot.OwnedIndices == LongevityResult.stableRetainedIndices
                            && snapshot.RawBytes == LongevityResult.stableRawBufferBytes
                            && snapshot.TotalModeledRawBytes == LongevityResult.stableTotalModeledRawBytes,
                            "No per-flip allocation/cache/pool-capacity growth after two complete loops");
                }
                LongevityResult.endingSuccessfulRebuildCommits = snapshot.SuccessfulMeshRebuildCommits;
                LongevityResult.successfulRebuildDelta = checked(snapshot.SuccessfulMeshRebuildCommits - LongevityResult.startingSuccessfulRebuildCommits);
                LongevityResult.endingLifetimeMeshAllocations = snapshot.LifetimeOwnedMeshAllocations;
                LongevityResult.maximumLifetimeMeshAllocations = Math.Max(LongevityResult.maximumLifetimeMeshAllocations, snapshot.LifetimeOwnedMeshAllocations);
                LongevityResult.maximumCachedBatches = Math.Max(LongevityResult.maximumCachedBatches, snapshot.OwnedMeshes);
                LongevityResult.maximumOwnedBufferMeshes = Math.Max(LongevityResult.maximumOwnedBufferMeshes, snapshot.OwnedBufferMeshes);
                LongevityResult.maximumRetainedVertices = Math.Max(LongevityResult.maximumRetainedVertices, snapshot.OwnedVertices);
                LongevityResult.maximumRetainedIndices = Math.Max(LongevityResult.maximumRetainedIndices, snapshot.OwnedIndices);
                LongevityResult.maximumRawBufferBytes = Math.Max(LongevityResult.maximumRawBufferBytes, snapshot.RawBytes);
                LongevityResult.maximumTotalModeledRawBytes = Math.Max(LongevityResult.maximumTotalModeledRawBytes, snapshot.TotalModeledRawBytes);
                LongevityResult.maximumPeakBuildManagedRawBytes = Math.Max(LongevityResult.maximumPeakBuildManagedRawBytes, snapshot.PeakBuildManagedRawBytes);
                LongevityResult.maximumSourceCopyManagedRawBytes = Math.Max(LongevityResult.maximumSourceCopyManagedRawBytes, snapshot.SourceCopyManagedRawBytes);
                LongevityResult.maximumBookkeepingManagedRawBytes = Math.Max(LongevityResult.maximumBookkeepingManagedRawBytes, snapshot.BookkeepingManagedRawBytes);
                CheckCurrent();
                if (longevityPointIndex >= 0)
                    LongevityResult.visited.Add(new LongevityPointObservation { loop = longevityLoop, point = longevityPointIndex,
                        frame = snapshot.Frame, sourceChunks = snapshot.SourceChunks, residentFeatures = snapshot.ResidentFeatures,
                        near = snapshot.NearFeatures, far = snapshot.FarFeatures, windows = snapshot.VisibleWindows, doors = snapshot.VisibleDoors,
                        selectedBoundsRows = snapshot.SourceAndOwnedBounds.Length, cachedBatches = snapshot.OwnedMeshes,
                        bufferMeshes = snapshot.OwnedBufferMeshes, lifetimeMeshAllocations = snapshot.LifetimeOwnedMeshAllocations,
                        nativeReadReservations = snapshot.NativeReadReservations, successfulRebuildCommits = snapshot.SuccessfulMeshRebuildCommits,
                        rebuildDelta = LongevityResult.successfulRebuildDelta, rawBufferBytes = snapshot.RawBytes,
                        totalModeledRawBytes = snapshot.TotalModeledRawBytes, retainedVertexCapacity = snapshot.OwnedVertices,
                        retainedIndexCapacity = snapshot.OwnedIndices, cullingMatrixF32Bits = bits, actualCameraEye = activeCamera.transform.position });
                longevityLastSnapshot = snapshot;
                longevityPointObserved = true;
            }
            catch (Exception error) { callbackFailure = error; }
        }
        private void WriteLongevityReport()
        {
            if (!longevityRequested) return;
            LongevityResult.settingsRestored = Result.settingsRestored;
            LongevityResult.helperResourcesReleased = Result.ownedResourcesReleased;
            LongevityResult.recordersDisposed = Result.recordersDisposed;
            LongevityResult.error = LongevityResult.error ?? Result.error;
            bool phase = LongevityResult.phaseCompleted && LongevityResult.reachedRebuildGoal
                && LongevityResult.successfulRebuildDelta > RequiredRebuildDelta
                && LongevityResult.poolStabilityEstablished && LongevityResult.error == null
                && Result.cleanupErrors.Count == 0 && Result.settingsRestored && Result.ownedResourcesReleased && Result.recordersDisposed;
            LongevityResult.status = !phase ? "FAIL" : LongevityResult.finalOwnerCleanupObserved
                ? LongevityResult.ownerPoolReleased ? "PASS_ACTUAL_NATIVE_REBUILD_LONGEVITY_NO_GAMEPLAY_ADMISSION" : "FAIL"
                : "PASS_LONGEVITY_PHASE_PENDING_ACTUAL_OWNER_CLEANUP";
            var settings = new JsonSerializerSettings(); settings.Converters.Add(new ObservationConverter());
            string json = JsonConvert.SerializeObject(LongevityResult, Formatting.Indented, settings);
            Require(System.Text.Encoding.UTF8.GetByteCount(json) <= MaximumLongevityReportBytes, "Bounded complete longevity prefix report");
            File.WriteAllText(longevityOutputPath, json);
        }
        internal void RecordInterruptedPrefix(string reason)
        {
            if (!longevityRequested) return;
            LongevityResult.error = reason; WriteLongevityReport();
        }
        internal string LongevityReportPath => longevityRequested ? longevityOutputPath : null;
        internal void ObserveFinalOwnerCleanup(bool actualReleased)
        {
            if (!longevityRequested) return;
            // Called ONLY by the exact proof after its genuine comparison release
            // observation. This is QA report DATA, never a runtime owner issuer.
            LongevityResult.finalOwnerCleanupObserved = true; LongevityResult.ownerPoolReleased = actualReleased;
            WriteLongevityReport();
        }

        private sealed class ObservationConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(Vector3) || type == typeof(Bounds);
            public override bool CanRead => false;
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
                => throw new NotSupportedException("Observation export only");
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value is Vector3 vector)
                {
                    writer.WriteStartArray(); writer.WriteValue(vector.x); writer.WriteValue(vector.y); writer.WriteValue(vector.z); writer.WriteEndArray();
                }
                else
                {
                    Bounds bounds = (Bounds)value;
                    writer.WriteStartObject(); writer.WritePropertyName("center"); serializer.Serialize(writer, bounds.center);
                    writer.WritePropertyName("extents"); serializer.Serialize(writer, bounds.extents); writer.WriteEndObject();
                }
            }
        }
        private void OnDestroy() { Dispose(); }
    }
}
#endif
