using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using EarthUnscripted.Core800;
using EarthUnscripted.RouteProof3;
using Newtonsoft.Json;
using EarthUnscripted.TerrainDraft.Core800;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit source-connected Route3 QA entry point, never attached during ordinary play.
    /// Independently binds reviewed source and cooked final arrays, uploads the same
    /// immutable plan, then drives the production stock VehicleController under real
    /// automatic 100Hz FixedUpdate. No simulation mirror or velocity/pose pinning.
    /// Art/scenery, unrequested roads, normal-world activation and FPS remain outside scope.
    /// </summary>
    public sealed class Core800JoinedRouteNativeProof : MonoBehaviour
    {
        /// <summary>Versioned diagnostic/binding identity; frozen2.1 remains separate.</summary>
        public const string Recipe = "Core800VehicleDrive/ConnectedSixRoadLoop/BothDirections/Cooked32";
        const string Flag = "-earthUnscriptedCore800RouteProof3", Final = "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf";
        // Source-frame XYZ are metres, Y up. Each source width determines its
        // UK-left quarter-width lane offset; no pavement is widened or moved.
        const double ProofTimeoutSeconds = 900;
        const float ExpectedFixedStepSeconds = 0.01f; // Actual production100Hz; never assigned here.
        const float FixedStepToleranceSeconds = 0.000001f;
        const double MaximumContactHeightErrorMetres = 0.0002;
        const float MaximumAllowedDamage = 0.05f;
        /// <summary>One exact file: bytes and lowercase SHA-256, not an approval flag.</summary>
        [Serializable]
        public sealed class FilePin
        {
            public string path, sha256;
            public long bytes;
        }

        /// <summary>
        /// Build-generated external pins for this isolated binary, all compiled C#,
        /// bootstrap/timestep and eight immutable source/cache inputs. Native guards
        /// compare these again before launch and after the exact owned process exits.
        /// </summary>
        [Serializable]
        public sealed class Binding
        {
            public string recipe, buildGUID, sourceSHA256, appExecutableSHA256, finalBufferDigest, gameSessionSHA256, timeManagerSHA256;
            public FilePin[] sourceFiles, appFiles, preparedInputs;
            public string cookedPayloadSHA256, cookedMetadataSHA256, canonicalRawSHA256, sourceContext;
        }

        static string output, inputs, bindingPath, bindingHash, error, career, careerHash, rawHash, cookedPath, cookedHash, metadataHash, sourceContext;
        static Core800DriveStartupCamera startupCamera;
        static bool requested, outputOwned;
        static Binding binding;
        static readonly List<string> errors = new List<string>();
        static int runtimeErrors;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly Report report = new Report();
        readonly object preparationGate = new object ();
        StreamWriter heartbeats;
        string preparationPhase = "bootstrap";
        int preparationCompleted, preparationTotal, heartbeatRows, updateCount;
        double nextHeartbeat;
        readonly List<string> captures = new List<string>();
        PreparedWorldMeshPlan800 plan;
        VehicleController car;
        DriverInput input;
        Camera camera;
        DriveCamera follow;
        GameObject stage, carRoot;
        Material asphalt, ground, bankMaterial;
        TextMesh legend;
        StreamWriter telemetry;
        const float InitialRouteArcMetres = 15;
        JoinedLoopRoute currentRoute;
        string phase = "preparing";
        bool observing, done, clockOwned;
        float priorQAScale;
        double deadline;
        static string Arg(string[] a, string n)
        {
            int k = Array.IndexOf(a, n);
            if (k < 0 || k + 1 >= a.Length)
                throw new ArgumentException(n);
            return a[k + 1];
        }

        static string Hash(string p) => File.Exists(p) ? Core.DataPackageRules.HashFile(p) : "missing";
        static void Log(string text, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
                return;
            runtimeErrors++;
            if (errors.Count < 16)
                errors.Add(text);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate()
        {
            var a = Environment.GetCommandLineArgs();
            if (Array.IndexOf(a, Flag) < 0)
                return;
            requested = true;
            GameSession.SuppressAutomaticLaunch = true;
            Application.runInBackground = true;
            Application.logMessageReceived += Log;
            string safe = Path.Combine(Application.temporaryCachePath, "invalid-core800-drive-" + Guid.NewGuid().ToString("N"));
            PlayerProfile.StorageOverride = safe + ".json";
            RegionCache.OverrideFolder = safe + "-regions";
            try
            {
                output = Path.GetFullPath(Arg(a, Flag));
                inputs = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800RouteInputs"));
                bindingPath = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800RouteBinding"));
                cookedPath = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800RouteCooked"));
                sourceContext = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800RouteSourceContext"));
                bindingHash = Arg(a, "-earthUnscriptedCore800RouteBindingSHA");
                if (!output.StartsWith("/private/tmp/rr019-core800-route3-", StringComparison.Ordinal) || Directory.Exists(output))
                    throw new IOException("Fresh private output required");
                Directory.CreateDirectory(output);
                outputOwned = true;
                PlayerProfile.StorageOverride = Path.Combine(output, "isolated-profile.json");
                RegionCache.OverrideFolder = Path.Combine(output, "isolated-regions");
                career = Path.Combine(Application.persistentDataPath, "earth-unscripted-save.json");
                careerHash = Hash(career);
                if (new FileInfo(bindingPath).Length > 1024 * 1024 || Hash(bindingPath) != bindingHash)
                    throw new IOException("Pinned QA source/build binding mismatch");
                binding = JsonUtility.FromJson<Binding>(File.ReadAllText(bindingPath));
                if (binding == null || binding.recipe != Recipe || binding.finalBufferDigest != Final || binding.buildGUID != Application.buildGUID || binding.sourceSHA256?.Length != 64 || binding.appExecutableSHA256?.Length != 64 || binding.gameSessionSHA256?.Length != 64 || binding.timeManagerSHA256?.Length != 64)
                    throw new IOException("Loaded QA build/source identity differs");
                if (Application.isEditor || Application.companyName != "EarthUnscripted Verification" || Application.productName != "Core800RouteQA3" || Application.identifier != "com.earthunscripted.qa.core800route3")
                    throw new IOException("Separate QA product identity required");
                rawHash = Hash(Path.Combine(inputs, "original-envelope.json"));
                cookedHash = Hash(cookedPath);
                metadataHash = Hash(cookedPath + ".eumeta");
                Need(binding.cookedPayloadSHA256 == VerifiedCoreTerrain.ReviewedCookedArtifactSHA256 && cookedHash == binding.cookedPayloadSHA256 && metadataHash == binding.cookedMetadataSHA256 && metadataHash == VerifiedCoreTerrain.ReviewedCookedPackageSHA256 && rawHash == binding.canonicalRawSHA256 && rawHash == ReviewedCanonicalRaw13Input.ExpectedBodySHA256 && sourceContext == binding.sourceContext, "Pinned cooked/raw/source context before QA camera");
                Need(binding.preparedInputs != null && binding.preparedInputs.Length == 8, "Exactly payload, metadata, raw and five independently captured source inputs");
                foreach (var pin in binding.preparedInputs)
                    Need(Path.IsPathFullyQualified(pin.path) && File.Exists(pin.path) && new FileInfo(pin.path).Length == pin.bytes && Hash(pin.path) == pin.sha256, "Prepared input differs before QA camera: " + pin.path);
            }
            catch (Exception e)
            {
                error = e.ToString();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallEarlyCamera()
        {
            if (!requested || error != null)
                return;
            try
            {
                startupCamera = Core800DriveStartupCamera.Create();
            }
            catch (Exception failure)
            {
                error = failure.ToString();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (requested)
                new GameObject("Isolated Core800 real vehicle proof").AddComponent<Core800JoinedRouteNativeProof>();
        }

        void Awake()
        {
            deadline = Time.realtimeSinceStartupAsDouble + ProofTimeoutSeconds;
            if (!outputOwned || error != null)
                return;
            heartbeats = new StreamWriter(Path.Combine(output, "preparation-heartbeat.jsonl"), false, new UTF8Encoding(false));
        }

        void Update()
        {
            if (done)
                return;
            updateCount++;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextHeartbeat && heartbeatRows < MaximumHeartbeatRows)
            {
                nextHeartbeat = now + HeartbeatIntervalSeconds;
                string currentPhase;
                int complete, total;
                lock (preparationGate)
                {
                    currentPhase = preparationPhase;
                    complete = preparationCompleted;
                    total = preparationTotal;
                }

                var beat = new Heartbeat
                {
                    realtimeSeconds = now,
                    phase = currentPhase,
                    completed = complete,
                    total = total,
                    frame = Time.frameCount,
                    renderedFrame = Time.renderedFrameCount,
                    update = updateCount,
                    earlyCameraRenders = startupCamera ? startupCamera.ClearingRenderCount : 0,
                    driveCameraRendered = startupCamera && startupCamera.DriveCameraRendered
                };
                heartbeats?.WriteLine(JsonUtility.ToJson(beat));
                heartbeats?.Flush();
                heartbeatRows++;
                if (heartbeatRows % 5 == 0)
                    Debug.Log("CORE800 DRIVE3 heartbeat " + currentPhase + " " + complete + "/" + total + " frames=" + beat.frame + " rendered=" + beat.renderedFrame);
            }

            if (now > deadline)
                Finish(false, "Proof watchdog");
        }

        IEnumerator Start()
        {
            yield return Guard(Run());
        }

        IEnumerator Guard(IEnumerator iterator)
        {
            while (!done)
            {
                bool more = false;
                object next = null;
                Exception failure = null;
                try
                {
                    more = iterator.MoveNext();
                    if (more)
                        next = iterator.Current;
                }
                catch (Exception e)
                {
                    failure = e;
                }

                if (failure != null)
                {
                    Finish(false, failure.ToString());
                    yield break;
                }

                if (!more)
                    yield break;
                if (next is IEnumerator nested)
                    yield return Guard(nested);
                else
                    yield return next;
            }
        }

        static void Need(bool value, string why)
        {
            if (!value)
                throw new InvalidOperationException(why);
        }

        // Seconds and bounded row counts describe diagnostics, not simulation policy or FPS.
        const double HeartbeatIntervalSeconds = 2;
        const int MaximumHeartbeatRows = 450;
        [Serializable]
        sealed class Heartbeat
        {
            public double realtimeSeconds;
            public string phase;
            public int completed, total, frame, renderedFrame, update, earlyCameraRenders;
            public bool driveCameraRendered;
        }

        sealed class PreparedResult
        {
            public PreparedWorldMeshPlan800 Plan;
            public double SourceCaptureSeconds, CachedValidationSeconds, IndexSeconds, RoutePlanningSeconds;
            public JoinedLoopRoute[] Routes;
        }

        void SetPreparationProgress(string value, int completed, int total)
        {
            lock (preparationGate)
            {
                preparationPhase = value;
                preparationCompleted = completed;
                preparationTotal = total;
            }
        }

        // Worker-only: independently recapture the reviewed raw source, not a cache-provided
        // receipt, then validate cached exact final arrays before constructing the real index.
        PreparedResult PrepareCachedPlan()
        {
            var result = new PreparedResult();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            SetPreparationProgress("independent-source-capture", 0, 1);
            var snapshot = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(sourceContext, cancellation.Token);
            byte[] sourceBytes = snapshot.CopyDocumentBytes(cancellation.Token);
            Need(World13DocumentCodec.Hash(sourceBytes) == rawHash, "Independent raw capture equals separately pinned canonical envelope");
            result.SourceCaptureSeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            var proof = VerifiedCoreTerrain.LoadReviewedCooked(cookedPath, sourceBytes, cancellation.Token, SetPreparationProgress);
            result.CachedValidationSeconds = timer.Elapsed.TotalSeconds;
            Need(proof.FinalBufferDigest == Final, "Reviewed final mesh identity");
            timer.Restart();
            SetPreparationProgress("canonical-owner-and-final-index", 0, 1);
            var owner = CanonicalRaw13Codec.Read(snapshot, sourceBytes);
            result.Plan = PreparedWorldMeshPlan800.FromVerifiedCore(owner, proof, cancellation.Token);
            result.IndexSeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            SetPreparationProgress("source-connected-loop-paths", 0, 2);
            result.Routes = new[]
            {
                JoinedLoopRoute.Build(result.Plan, sourceBytes, JoinedLoopRoute.ReviewedPantsideLegs(false), cancellation.Token),
                JoinedLoopRoute.Build(result.Plan, sourceBytes, JoinedLoopRoute.ReviewedPantsideLegs(true), cancellation.Token)
            };
            result.RoutePlanningSeconds = timer.Elapsed.TotalSeconds;
            SetPreparationProgress("prepared-index", 1, 1);
            return result;
        }

        // Use the production collider factory, imported visual, tuning and supported
        // rest initializer exactly once before observation. Thereafter only ordinary
        // NetworkDriven input authority drives physics; reverse uses positive brake.
        void MakeCar()
        {
            carRoot = new GameObject("Production stock player coupe QA");
            carRoot.layer = 8;
            var p = Point(InitialRouteArcMetres);
            carRoot.transform.SetPositionAndRotation(p + Vector3.up * .7f, Quaternion.LookRotation(Tangent(InitialRouteArcMetres), Vector3.up));
            var factory = typeof(GameSession).GetMethod("AddBootCollider", BindingFlags.Static | BindingFlags.NonPublic);
            Need(factory != null, "Actual production collider factory");
            factory.Invoke(null, new object[] { carRoot, false });
            var visual = carRoot.AddComponent<CarVisual>();
            var owned = new Core.OwnedCar
            {
                model = 0,
                paint = 0
            };
            visual.Build(CarColours.Paint[owned.paint], true, owned.model, owned.wheels, owned.bodyKit, owned.finish);
            car = carRoot.AddComponent<VehicleController>();
            car.Visual = visual;
            car.ParkingAutomaticEnabled = true;
            car.NetworkDriven = true;
            car.ApplyTuning(owned);
            input = gameObject.AddComponent<DriverInput>();
            input.enabled = false;
            input.Profile = new PlayerProfile();
            car.Input = input;
            Need(car.Visual.ImportedAppearance, "Actual production imported stock model");
            Need(car.TryInitializeSupportedRest(p, car.Body.rotation, out var reason, out bool missing), "Production supported-rest failed: " + reason + " missing=" + missing);
            Need(car.Grip == 1 && car.RoadSurface && car.SurfaceWetness == 0 && car.TyreFactor == 1, "Production dry-onroad/default-tyre conditions match GameSession.Update");
            report.dryGrip = car.Grip;
            report.dryRoadSurface = car.RoadSurface;
            report.dryWetness = car.SurfaceWetness;
            report.initialRest = true;
            car.Crashed += (severity, where) =>
            {
                if (errors.Count < 16)
                    errors.Add("Actual collision severity=" + severity + " at" + where);
            };
        }

        IEnumerator WaitSimulation(float seconds)
        {
            float start = Time.time;
            double wall = Time.realtimeSinceStartupAsDouble;
            while (Time.time - start < seconds && !done)
            {
                Need(Time.realtimeSinceStartupAsDouble - wall < seconds * 3 + 10, "Automatic physics did not progress");
                yield return null;
            }
        }

        IEnumerator Capture(string label)
        {
            for (int i = 0; i < 3; i++)
                yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Need(image.width >= 1280 && image.height >= 720, "Native QA framebuffer>=1280x720");
                string path = Path.Combine(output, label + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                captures.Add(path);
            }
            finally
            {
                Destroy(image);
            }
        }

        void BuildBankOverlay()
        {
            var shader = asphalt.shader;
            Need(shader && shader.isSupported, "Diagnostic overlay shader");
            bankMaterial = new Material(shader)
            {
                name = "NONPHYSICAL derived bank role",
                renderQueue = 3000
            };
            bankMaterial.SetColor("_BaseColor", new Color(1, .42f, .06f));
            bankMaterial.SetFloat("_ZWrite", 0);
            bankMaterial.SetFloat("_Cull", 0);
            foreach (var bank in plan.CopyDerivedRoadBanks())
                foreach (var group in bank.CopyTriangleReferences().GroupBy(r => r.PacketId))
                {
                    var p = plan.CopyForUpload(group.Key);
                    var v = new Vector3[p.XYZ.Length / 3];
                    for (int i = 0; i < v.Length; i++)
                        v[i] = new Vector3(p.XYZ[i * 3], p.XYZ[i * 3 + 1], p.XYZ[i * 3 + 2]);
                    var ix = group.SelectMany(r => p.Indices.Skip(r.TriangleIndex * 3).Take(3)).ToArray();
                    var go = Geometry.Mesh("NONPHYSICAL role overlay", stage.transform, v, ix, new Vector2[v.Length], bankMaterial, false);
                    go.transform.localPosition = new Vector3((float)p.OriginX, (float)p.OriginY, (float)p.OriginZ);
                    Need(!go.GetComponent<Collider>(), "Role colour does not add collision");
                }
        }

        // Every actor is staged before observation. Switching the active driver never
        // relocates a Rigidbody; the other car stays physically parked in the scene.
        readonly List<VehicleController> actors = new List<VehicleController>();
        readonly List<DriverInput> actorInputs = new List<DriverInput>();
        readonly List<GameObject> actorRoots = new List<GameObject>();
        readonly List<CaseResult> cases = new List<CaseResult>();
        readonly JoinedLoopDriver[] drivers = new JoinedLoopDriver[2];
        readonly double[] lastTraceTick =
        {
            double.NegativeInfinity,
            double.NegativeInfinity
        };
        readonly IdleAnchorState[] idleAnchors = new IdleAnchorState[2];
        readonly float[] maximumIdleExcursion = new float[2];
        readonly int[] actorRecords = new int[2];
        JoinedLoopRoute[] routes;
        HashSet<string> roadPacketIds;
        int activeActor = -1, retainedRows;
        const int MaximumTraceRows = 200_000;
        const long MaximumTraceBytes = 256L * 1024 * 1024;
        /// <summary>
        /// Diagnostic ownership state, independent of vehicle physics and render
        /// coroutine scheduling. Staged and final witness positions never mutate.
        /// Completion is captured from the actual retained final-hold fixed record;
        /// the first post-completion idle tick selects it once BEFORE comparison.
        /// A moved first-idle pose can therefore never become a fresh zero anchor.
        /// </summary>
        internal sealed class IdleAnchorState
        {
            internal readonly struct Pose
            {
                internal readonly float X, Y, Z;
                internal Pose(float x, float y, float z)
                {
                    if (!Finite(x) || !Finite(y) || !Finite(z))
                        throw new InvalidOperationException("Finite actual anchor position required");
                    X = x;
                    Y = y;
                    Z = z;
                }

                private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
            }

            internal Pose StagedPose { get; }
            internal double StagedTick { get; }
            internal Pose CompletionPose { get; private set; }
            internal double CompletionTick { get; private set; }
            internal bool CompletionWitnessSeen { get; private set; }
            internal bool FinalSelected { get; private set; }
            internal double FinalSelectedTick { get; private set; }
            internal int FinalSelectionCount { get; private set; }

            internal IdleAnchorState(Pose staged, double stagedTick)
            {
                CheckTick(stagedTick);
                StagedPose = staged;
                StagedTick = stagedTick;
            }

            internal void RecordCompletion(Pose finalHeldPose, double fixedTick)
            {
                CheckTick(fixedTick);
                if (CompletionWitnessSeen || fixedTick < StagedTick)
                    throw new InvalidOperationException("Completion witness must be captured once in fixed-step order");
                CompletionPose = finalHeldPose;
                CompletionTick = fixedTick;
                CompletionWitnessSeen = true;
            }

            internal Pose ForIdle(bool completed, double fixedTick)
            {
                CheckTick(fixedTick);
                if (fixedTick < StagedTick)
                    throw new InvalidOperationException("Idle clock cannot precede staging");
                if (!completed)
                {
                    if (FinalSelected)
                        throw new InvalidOperationException("Cannot return to a staged anchor after completion");
                    return StagedPose;
                }

                if (!CompletionWitnessSeen || fixedTick < CompletionTick)
                    throw new InvalidOperationException("Actual completed-hold witness required before final idle");
                if (!FinalSelected)
                {
                    FinalSelected = true;
                    FinalSelectedTick = fixedTick;
                    FinalSelectionCount++;
                }

                return CompletionPose;
            }

            private static void CheckTick(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                    throw new InvalidOperationException("Finite nonnegative fixed-time seconds required");
            }
        }

        private static IdleAnchorState.Pose AnchorPose(Vector3 value) => new IdleAnchorState.Pose(value.x, value.y, value.z);
        private static Vector3 Vector(IdleAnchorState.Pose value) => new Vector3(value.X, value.Y, value.Z);
        [Serializable]
        sealed class IdleAnchorEntry
        {
            public string actor;
            public Vector3 stagedPosition, completionPosition;
            public double stagedFixedTick, completionFixedTick, finalSelectedFixedTick;
            public bool completionWitnessSeen, finalSelected;
            public int finalSelectionCount;
            public float maximumIdleExcursionMetres;
        }

        [Serializable]
        sealed class IdleAnchorList
        {
            public string schema = "EarthUnscriptedParkedIdleAnchors1";
            public IdleAnchorEntry[] values;
        }

        [Serializable]
        sealed class CaseResult
        {
            public string actor;
            public double sampledLaneLengthMetres;
            public float actualTravelMetres, driveSeconds, maximumHoldExcursionMetres;
            public string[] crossedActualSourceNodes;
            public bool complete;
        }

        [Serializable]
        sealed class ShapeList
        {
            public ActualRouteCarShape.Snapshot[] values;
        }

        [Serializable]
        sealed class Report
        {
            public string status, error, scope, recipe, finalBufferDigest, sourceDigest;
            public string appBuildGUID, bindingSHA256, company, product, identifier;
            public string telemetrySHA256, heartbeatSHA256, routeLedgerSHA256, actualCarShapesSHA256, idleAnchorsSHA256;
            public string cookedPayloadSHA256, cookedMetadataSHA256, canonicalRawSHA256;
            public string gameSessionSHA256, timeManagerSHA256;
            public double sourceCaptureSeconds, cachedValidationSeconds, indexSeconds, routePlanningSeconds, uploadSeconds;
            public float priorQATimeScale, activeQATimeScale, physicalFixedDeltaTime;
            public Vector3 physicsGravity;
            public bool earlyCameraCreated, driveCameraRendered, earlyCameraRetiredAfterDriveRender;
            public int earlyCameraRenderCount, heartbeatRows, updateCount, retainedFixedRows, runtimeErrors;
            public bool bootstrapSuppressed, noGameSession, qaDefaultCareerUnchanged, inputsUnchanged;
            public bool externalProductionGuardRequired = true;
            public float dryGrip, dryWetness;
            public bool dryRoadSurface, initialRest;
            public CaseResult[] cases;
            public int[] actorFixedRows;
            public float[] maximumIdleExcursionMetres;
            public string[] captures, errors;
        }

        Vector3 Point(float arc)
        {
            var sample = currentRoute.AtArc(arc);
            foreach (string id in sample.CopyRoadIds())
                if (plan.TryRoad(id, sample.X, sample.Z, out var hit))
                    return new Vector3((float)sample.X, (float)hit.Height, (float)sample.Z);
            throw new InvalidOperationException("Exact named route target missing; no fallback");
        }

        Vector3 Tangent(float arc)
        {
            float halfWheelbase = Core.VehicleDimensions.ForModel(0).Wheelbase * 0.5f;
            var direction = Vector3.ProjectOnPlane(Point(arc + halfWheelbase) - Point(arc - halfWheelbase), Vector3.up).normalized;
            Need(direction.sqrMagnitude > 0.9f, "Actual source path heading unavailable");
            return direction;
        }

        IEnumerator Run()
        {
            Need(error == null, error);
            Need(!FindAnyObjectByType<GameSession>() && GameSession.SuppressAutomaticLaunch, "Normal bootstrap suppressed before any actor or source prepare");
            priorQAScale = Time.timeScale;
            report.priorQATimeScale = priorQAScale;
            Time.timeScale = 1;
            clockOwned = true;
            report.activeQATimeScale = Time.timeScale;
            Need(Physics.simulationMode == SimulationMode.FixedUpdate && Mathf.Abs(Time.fixedDeltaTime - ExpectedFixedStepSeconds) < FixedStepToleranceSeconds && (Physics.gravity - new Vector3(0, -9.81f, 0)).sqrMagnitude < 0.000001f, "Actual unchanged100Hz automatic physics/gravity required");
            Need(FindObjectsByType<Collider>(FindObjectsSortMode.None).Length == 0, "No foreign collision universe");
            Need(startupCamera && startupCamera.Created, "Validated pre-yield URP clear camera required");
            var task = Task.Run(PrepareCachedPlan, cancellation.Token);
            while (!task.IsCompleted)
                yield return null;
            var prepared = task.GetAwaiter().GetResult();
            plan = prepared.Plan;
            routes = prepared.Routes;
            report.sourceCaptureSeconds = prepared.SourceCaptureSeconds;
            report.cachedValidationSeconds = prepared.CachedValidationSeconds;
            report.indexSeconds = prepared.IndexSeconds;
            report.routePlanningSeconds = prepared.RoutePlanningSeconds;
            Need(startupCamera.ClearingRenderCount > 0 && plan.PacketCount == 1063 && plan.CopyRootKeys().Length == 16 && routes.Length == 2, "Actual early rendering and complete source-bound plan/routes required");
            roadPacketIds = new HashSet<string>(plan.CopyRootKeys().SelectMany(key => plan.CopyRootPacketIds(key)).Where(id => plan.PacketRole(id) == PreparedWorldMeshPlan800.Role.GroundRoad), StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(output, "route-ledger.json"), JsonConvert.SerializeObject(new { recipe = JoinedLoopRoute.Recipe, plan.RawDocumentDigest, plan.FinalBufferDigest, scope = "Six exact source-connected roads; sampled driver path is NOT replacement road geometry", routes = routes.Select(route => new { route.LengthMetres, controls = route.CopyControls(), samples = route.CopySamples().Select(sample => new { sample.ArcMetres, sample.X, sample.Y, sample.Z, sample.NodeId, roads = sample.CopyRoadIds() }), joins = route.CopyJunctions().Select(junction => new { junction.NodeId, junction.IncomingRoad, junction.OutgoingRoad, junction.GeometryDescription, arms = junction.CopyArmRoadIds(), angularGapsDegrees = junction.CopyArmGapsDegrees() }) }) }, Formatting.Indented));
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Need(shader && shader.isSupported, "Actual built URP/Lit shader required");
            asphalt = new Material(shader)
            {
                color = new Color(0.17f, 0.18f, 0.19f),
                name = "QA final ROAD grey"
            };
            ground = new Material(shader)
            {
                color = new Color(0.25f, 0.42f, 0.2f),
                name = "QA final TERRAIN green"
            };
            stage = new GameObject("Actual complete800m final ground; two real loop actors");
            stage.SetActive(false);
            int uploaded = 0;
            double uploadStarted = Time.realtimeSinceStartupAsDouble;
            foreach (var key in plan.CopyRootKeys())
                foreach (var step in WorldMeshRootUpload800.BuildHidden(stage.transform, plan, key, role => role == PreparedWorldMeshPlan800.Role.GroundRoad ? asphalt : ground, cancellation.Token))
                {
                    SetPreparationProgress("upload-original-packets", ++uploaded, plan.PacketCount);
                    yield return null;
                }

            report.uploadSeconds = Time.realtimeSinceStartupAsDouble - uploadStarted;
            Need(uploaded == plan.PacketCount && stage.transform.childCount == 16, "No partial world activation");
            stage.SetActive(true);
            Physics.SyncTransforms();
            foreach (var route in routes)
            {
                currentRoute = route;
                MakeCar();
                actors.Add(car);
                actorInputs.Add(input);
                actorRoots.Add(carRoot);
            }

            car = actors[0];
            input = actorInputs[0];
            currentRoute = routes[0];
            var cameraRoot = new GameObject("Real loop chase camera QA");
            camera = cameraRoot.AddComponent<Camera>();
            camera.nearClipPlane = 0.06f;
            camera.farClipPlane = 1600;
            follow = cameraRoot.AddComponent<DriveCamera>();
            follow.Car = car;
            follow.Input = input;
            camera.transform.position = car.transform.position - car.transform.forward * 7 + Vector3.up * 3;
            startupCamera.ObserveDriveCamera(camera);
            legend = Geometry.FittedLabel("TWO REAL STOCK CARS | source-connected six-road loops\nROAD grey / TERRAIN green; scoped geometry proof, no scenery-art claim", camera.transform, new Vector3(0, 0.39f, 1), Quaternion.identity, 1.28f, 0.075f, Color.white);
            var sun = new GameObject("QA dry diagnostic sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.6f);
            telemetry = new StreamWriter(Path.Combine(output, "fixed-telemetry.jsonl"), false, new UTF8Encoding(false));
            double cameraDeadline = Time.realtimeSinceStartupAsDouble + 15;
            while (!startupCamera.DriveCameraRendered)
            {
                Need(Time.realtimeSinceStartupAsDouble < cameraDeadline, "Actual chase camera render missing");
                yield return null;
            }

            Need(startupCamera.RetiredAfterDriveRender, "Early camera handoff must follow actual render");
            yield return WaitSimulation(2);
            var shapes = new ShapeList
            {
                values = actors.Select(actor => ActualRouteCarShape.Capture(actor, plan.SourceDigest, plan.FinalBufferDigest, cancellation.Token)).ToArray()
            };
            File.WriteAllText(Path.Combine(output, "actual-stock-shapes.json"), JsonUtility.ToJson(shapes, true));
            for (int index = 0; index < actors.Count; index++)
            {
                Need(actors[index].ParkingBrakeHolding && actors[index].WheelGrounded.All(value => value), "Both real cars must rest supported before observation");
                idleAnchors[index] = new IdleAnchorState(AnchorPose(shapes.values[index].rigidbodyWorldPosition), Time.fixedTimeAsDouble);
            }

            observing = true;
            yield return Capture("both-initially-staged-supported");
            for (int index = 0; index < actors.Count; index++)
            {
                activeActor = index;
                car = actors[index];
                input = actorInputs[index];
                currentRoute = routes[index];
                follow.Car = car;
                follow.Input = input;
                phase = index == 0 ? "source-loop-clockwise" : "source-loop-anticlockwise";
                var driver = car.gameObject.AddComponent<JoinedLoopDriver>();
                drivers[index] = driver;
                int capturedIndex = index;
                driver.Initialize(plan, currentRoute, car, input, "stock-" + index, InitialRouteArcMetres, trace => RetainTrace(capturedIndex, trace), cancellation.Token);
                SetPreparationProgress(phase, index, 2);
                while (!driver.Complete && driver.Failure == null)
                {
                    Need(runtimeErrors == 0, "Actual runtime error; trace retained");
                    yield return null;
                }

                Need(driver.Complete && driver.Failure == null, driver.Failure ?? "Incomplete real source loop");
                cases.Add(new CaseResult { actor = "stock-" + index, complete = true, sampledLaneLengthMetres = currentRoute.LengthMetres, actualTravelMetres = driver.ActualTravelMetres, driveSeconds = driver.DriveSeconds, maximumHoldExcursionMetres = driver.MaximumHoldExcursion, crossedActualSourceNodes = driver.CopyCrossedSourceNodes() });
                // Final idle anchor is selected in FixedUpdate, never rebased by this coroutine.
                activeActor = -1;
                yield return Capture("loop-" + index + "-complete-automatically-held");
            }

            Need(cases.Count == 2 && cases.All(item => item.crossedActualSourceNodes.Length == 6) && runtimeErrors == 0, "Both complete real source-connected directions required");
            observing = false;
            phase = "final-static-inspection";
            follow.enabled = false;
            BuildBankOverlay();
            Destroy(legend.gameObject);
            legend = Geometry.FittedLabel("SCOPED800m | two real source-connected loops complete\nBANK orange is nonphysical role overlay; same final collision ground", camera.transform, new Vector3(0, 0.39f, 1), Quaternion.identity, 1.28f, 0.075f, Color.white);
            camera.transform.position = new Vector3(-17, 88, -109);
            camera.transform.LookAt(new Vector3(-10.5f, 83, -101.5f));
            yield return Capture("final-bank-closeup");
            camera.transform.position = new Vector3(650, 900, -700);
            camera.transform.LookAt(new Vector3(0, 80, 0));
            yield return Capture("final-overview");
            Finish(true, null);
        }

        void RetainTrace(int index, JoinedLoopDriver.Trace trace)
        {
            if (++retainedRows > MaximumTraceRows)
                throw new InvalidOperationException("Raw trace row cap exceeded");
            telemetry.WriteLine(JsonUtility.ToJson(trace));
            actorRecords[index]++;
            lastTraceTick[index] = Time.fixedTimeAsDouble;
            if (trace.phase == "complete-parked")
                idleAnchors[index].RecordCompletion(AnchorPose(trace.position), Time.fixedTimeAsDouble);
            if ((retainedRows & 255) == 0)
            {
                telemetry.Flush();
                Need(telemetry.BaseStream.Position <= MaximumTraceBytes, "Raw trace byte cap exceeded");
            }
        }

        // Active drivers retain their own failed record before refusing. The parent
        // independently records every parked actor each fixed tick as well.
        void FixedUpdate()
        {
            if (!observing || done)
                return;
            try
            {
                for (int index = 0; index < actors.Count; index++)
                {
                    if (lastTraceTick[index] == Time.fixedTimeAsDouble)
                        continue;
                    var actor = actors[index];
                    var driver = drivers[index];
                    if (index == activeActor && driver && driver.Failure != null)
                    {
                        Finish(false, driver.Failure);
                        return;
                    }

                    if (index == activeActor && driver && !driver.Complete && driver.Failure == null)
                        continue;
                    double arc = driver ? driver.ProgressArcMetres : InitialRouteArcMetres;
                    var allowed = routes[index].CopyContactRoadIds(arc, 3);
                    var trace = new JoinedLoopDriver.Trace
                    {
                        vehicle = "stock-" + index,
                        phase = "supported-parked-idle",
                        roadIds = allowed,
                        routeArcMetres = arc,
                        simulationSeconds = Time.time,
                        speedMetresPerSecond = actor.Speed,
                        travelMetres = driver ? driver.ActualTravelMetres : 0,
                        position = actor.Body.position,
                        velocity = actor.Body.linearVelocity,
                        forward = actor.transform.forward,
                        grounded = (bool[])actor.WheelGrounded.Clone(),
                        damage = actor.Damage,
                        parkingApplied = actor.ParkingBrakeApplied,
                        parkingHolding = actor.ParkingBrakeHolding,
                        reverse = actor.Reverse,
                        throttle = actor.NetworkThrottle,
                        brake = actor.NetworkBrake,
                        steer = actor.NetworkSteer,
                        contactPoints = (Vector3[])actor.ContactPoints.Clone(),
                        contactNormals = (Vector3[])actor.ContactNormals.Clone(),
                        contactPackets = new string[4],
                        rayTriangles = new int[4],
                        rayPoints = new Vector3[4],
                        rayNormals = new Vector3[4]
                    };
                    bool support = true;
                    for (int wheel = 0; wheel < 4; wheel++)
                    {
                        var origin = actor.transform.TransformPoint(VehicleGeometry.Anchor(0, wheel));
                        if (!Physics.Raycast(origin, -actor.transform.up, out var hit, actor.Metrics.SuspensionRayLength, ~(1 << 8), QueryTriggerInteraction.Ignore))
                        {
                            trace.contactPackets[wheel] = "MISSING";
                            trace.rayTriangles[wheel] = -1;
                            support = false;
                            continue;
                        }

                        trace.contactPackets[wheel] = hit.collider.name;
                        trace.rayTriangles[wheel] = hit.triangleIndex;
                        trace.rayPoints[wheel] = hit.point;
                        trace.rayNormals[wheel] = hit.normal;
                        support &= hit.collider is MeshCollider && roadPacketIds.Contains(hit.collider.name) && allowed.Any(id => plan.TryRoad(id, hit.point.x, hit.point.z, out var road) && Math.Abs(road.Height - hit.point.y) <= MaximumContactHeightErrorMetres);
                    }

                    // A render coroutine may not resume between multiple physics
                    // ticks. Select the captured completion pose HERE before any
                    // idle distance accounting, exactly once; never current pose.
                    var anchor = idleAnchors[index].ForIdle(driver && driver.Complete, Time.fixedTimeAsDouble);
                    maximumIdleExcursion[index] = Math.Max(maximumIdleExcursion[index], Vector3.Distance(new Vector3(anchor.X, anchor.Y, anchor.Z), actor.Body.position));
                    RetainTrace(index, trace);
                    Need(support && trace.grounded.All(value => value) && actor.ParkingBrakeHolding && actor.Damage <= MaximumAllowedDamage && maximumIdleExcursion[index] < 0.05f, "Parked real actor lost actual support/automatic hold; raw trace retained");
                }
            }
            catch (Exception failure)
            {
                Finish(false, failure.ToString());
            }
        }

        void OnGUI()
        {
            if (!car)
                return;
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft
            };
            var driver = activeActor >= 0 ? drivers[activeActor] : null;
            GUI.Box(new Rect(18, Screen.height - 140, 1100, 112), "REAL SOURCE-CONNECTED LOOP — " + phase + "\n" + Mathf.Abs(car.Speed).ToString("F1") + "m/s | damage " + car.Damage.ToString("F2") + " | nodes " + (driver ? driver.CopyCrossedSourceNodes().Length : 0) + "/6" + " | " + (car.ParkingBrakeHolding ? "PARKING BRAKE · HOLDING" : "DRIVING") + "\nActual stock physics; scoped final ground. No normal-world/scenery/FPS claim.", style);
        }

        void Finish(bool pass, string failure)
        {
            if (done)
                return;
            done = true;
            observing = false;
            foreach (var actor in actors)
                if (actor)
                {
                    actor.NetworkThrottle = 0;
                    actor.NetworkBrake = 1;
                    actor.NetworkHandbrake = 1;
                }

            try
            {
                telemetry?.Flush();
                telemetry?.Dispose();
                heartbeats?.Flush();
                heartbeats?.Dispose();
                report.status = pass ? "PASS_SCOPED_TWO_SOURCE_LOOPS_ONLY" : "FAIL";
                report.error = failure;
                report.recipe = Recipe;
                report.scope = "Actual stock vehicles on exact complete800m ground; six exact source-node joins each direction. No normal world, scenery, LAN or FPS admission.";
                report.finalBufferDigest = plan?.FinalBufferDigest;
                report.sourceDigest = plan?.SourceDigest;
                report.appBuildGUID = Application.buildGUID;
                report.bindingSHA256 = bindingHash;
                report.gameSessionSHA256 = binding?.gameSessionSHA256;
                report.timeManagerSHA256 = binding?.timeManagerSHA256;
                report.company = Application.companyName;
                report.product = Application.productName;
                report.identifier = Application.identifier;
                report.physicalFixedDeltaTime = Time.fixedDeltaTime;
                report.physicsGravity = Physics.gravity;
                report.heartbeatRows = heartbeatRows;
                report.updateCount = updateCount;
                report.earlyCameraCreated = startupCamera && startupCamera.Created;
                report.earlyCameraRenderCount = startupCamera ? startupCamera.ClearingRenderCount : 0;
                report.driveCameraRendered = startupCamera && startupCamera.DriveCameraRendered;
                report.earlyCameraRetiredAfterDriveRender = startupCamera && startupCamera.RetiredAfterDriveRender;
                report.cookedPayloadSHA256 = cookedHash;
                report.cookedMetadataSHA256 = metadataHash;
                report.canonicalRawSHA256 = rawHash;
                report.runtimeErrors = runtimeErrors;
                report.retainedFixedRows = retainedRows;
                report.actorFixedRows = actorRecords;
                report.maximumIdleExcursionMetres = maximumIdleExcursion;
                report.cases = cases.ToArray();
                report.captures = captures.ToArray();
                report.errors = errors.ToArray();
                report.bootstrapSuppressed = GameSession.SuppressAutomaticLaunch;
                report.noGameSession = !FindAnyObjectByType<GameSession>();
                report.qaDefaultCareerUnchanged = Hash(career) == careerHash;
                report.inputsUnchanged = inputs != null && binding?.preparedInputs != null && binding.preparedInputs.All(pin => File.Exists(pin.path) && new FileInfo(pin.path).Length == pin.bytes && Hash(pin.path) == pin.sha256) && Hash(bindingPath) == bindingHash;
                if (outputOwned)
                {
                    var anchorEntries = idleAnchors.Select((state, index) =>
                    {
                        if (state == null)
                            return null;
                        return new IdleAnchorEntry
                        {
                            actor = "stock-" + index,
                            stagedPosition = Vector(state.StagedPose),
                            stagedFixedTick = state.StagedTick,
                            completionWitnessSeen = state.CompletionWitnessSeen,
                            completionPosition = Vector(state.CompletionPose),
                            completionFixedTick = state.CompletionTick,
                            finalSelected = state.FinalSelected,
                            finalSelectedFixedTick = state.FinalSelectedTick,
                            finalSelectionCount = state.FinalSelectionCount,
                            maximumIdleExcursionMetres = maximumIdleExcursion[index]
                        };
                    }).Where(entry => entry != null).ToArray();
                    File.WriteAllText(Path.Combine(output, "idle-anchors.json"), JsonUtility.ToJson(new IdleAnchorList { values = anchorEntries }, true));
                    report.idleAnchorsSHA256 = Hash(Path.Combine(output, "idle-anchors.json"));
                    report.telemetrySHA256 = Hash(Path.Combine(output, "fixed-telemetry.jsonl"));
                    report.heartbeatSHA256 = Hash(Path.Combine(output, "preparation-heartbeat.jsonl"));
                    report.routeLedgerSHA256 = Hash(Path.Combine(output, "route-ledger.json"));
                    report.actualCarShapesSHA256 = Hash(Path.Combine(output, "actual-stock-shapes.json"));
                }

                if (!report.qaDefaultCareerUnchanged || !report.inputsUnchanged || runtimeErrors > 0 || !report.noGameSession || !report.bootstrapSuppressed)
                    report.status = "FAIL";
                if (outputOwned)
                    File.WriteAllText(Path.Combine(output, "receipt.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                cancellation.Cancel();
                if (startupCamera)
                    startupCamera.DisposeOwned();
                if (stage)
                    Geometry.ReleaseMeshes(stage);
                foreach (var root in actorRoots)
                    if (root)
                        Geometry.ReleaseMeshes(root);
                if (clockOwned)
                {
                    Time.timeScale = priorQAScale;
                    clockOwned = false;
                }

                Application.Quit(report.status == "PASS_SCOPED_TWO_SOURCE_LOOPS_ONLY" ? 0 : 1);
            }
        }

        void OnDestroy()
        {
            if (clockOwned)
            {
                Time.timeScale = priorQAScale;
                clockOwned = false;
            }

            cancellation.Cancel();
            telemetry?.Dispose();
            heartbeats?.Dispose();
            if (startupCamera)
                startupCamera.DisposeOwned();
            Application.logMessageReceived -= Log;
        }
    }
}
