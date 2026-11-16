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
using EarthUnscripted.TerrainDraft.Core800;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit NativeDrive2.2 QA entry point, never attached during ordinary play.
    /// Independently binds reviewed source and cooked final arrays, uploads the same
    /// immutable plan, then drives the production stock VehicleController under real
    /// automatic 100Hz FixedUpdate. No simulation mirror or velocity/pose pinning.
    /// Art/scenery, junction-network coverage and FPS are outside this proof's scope.
    /// </summary>
    public sealed class Core800DrivingNativeProof : MonoBehaviour
    {
        /// <summary>Versioned diagnostic/binding identity; frozen2.1 remains separate.</summary>
        public const string Recipe = "Core800VehicleDrive/CoronationCrescent263m/Cooked2.2.1";
        const string Flag = "-earthUnscriptedCore800DriveProof22", Road = "59973878-span-0", Final = "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf";
        // Source-frame X/Z/Y are metres, Y up. UK left lane is 1.1m to the
        // negative source-right side. This does not widen or alter the real road.
        const float Lane = -1.1f;
        const double ProofTimeoutSeconds = 600;
        const float ExpectedFixedStepSeconds = 0.01f; // Actual production100Hz; never assigned here.
        const float FixedStepToleranceSeconds = 0.000001f;
        const float MinimumDriveSeconds = 60;
        const float MinimumHoldSeconds = 10;
        const double MaximumContactHeightErrorMetres = 0.0002;
        const float MaximumLateralErrorMetres = 0.9f;
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

        // One record per actual FixedUpdate: seconds, metres, m/s, bounded inputs,
        // real four-wheel suspension/PhysX support and current parking/gear state.
        [Serializable]
        sealed class Sample
        {
            public string phase, road;
            public float time, speed, damage, steer, throttle, brake, handbrake, sourceS, lateralError;
            public Vector3 position, velocity, forward;
            public bool parkingApplied, parkingHolding, reverse;
            public bool[] grounded;
            public Vector3[] contactPoints, contactNormals;
            public string[] contactPackets;
            public int[] rayTriangles;
            public Vector3[] rayPoints, rayNormals;
        }

        [Serializable]
        sealed class Report
        {
            public string status, error, scope, recipe, finalBufferDigest, sourceDigest, appBuildGUID,
                bindingSHA256, company, product, identifier, telemetrySHA256, gameSessionSHA256, timeManagerSHA256;
            public Vector3 holdAnchor;
            public float holdStartTime, holdEndTime, maxHoldDisplacement;
            public int holdSamples;
            public int fixedSamples, supportFailures, offRoadContacts, airborneWheelSamples, runtimeErrors, driveSamples;
            public float priorQATimeScale, activeQATimeScale;
            public int minimumDriveSamples, minimumHoldSamples;
            public Vector3 physicsGravity;
            public float dryGrip, dryWetness;
            public bool dryRoadSurface;
            public float physicalFixedDeltaTime;
            public float driveSeconds, distance, maxSpeed, maxDamage, maxLateralError, parkingCreep;
            public bool bootstrapSuppressed, noGameSession, qaDefaultCareerUnchanged, externalProductionGuardRequired = true,
                inputsUnchanged, initialRest, parkingReleasedForward, automaticStoppedHold, parkingReleasedReverse;
            public string[] captures, errors;
            public double sourceCaptureSeconds, cachedValidationSeconds, indexSeconds, uploadSeconds;
            public string cookedPayloadSHA256, cookedMetadataSHA256, canonicalRawSHA256, heartbeatSHA256;
            public bool earlyCameraCreated, driveCameraRendered, earlyCameraRetiredAfterDriveRender;
            public int earlyCameraRenderCount, heartbeatRows, updateCount;
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
        EarthUnscripted.TerrainDraft.Core800.SourceRoadRow[] rows;
        float station, endStation, driveBegan, travel, maxLateral;
        Vector3 previous, holdAnchor;
        float holdMaximum;
        bool holdMeasuring;
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
                inputs = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800DriveInputs"));
                bindingPath = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800DriveBinding"));
                cookedPath = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800DriveCooked"));
                sourceContext = Path.GetFullPath(Arg(a, "-earthUnscriptedCore800DriveSourceContext"));
                bindingHash = Arg(a, "-earthUnscriptedCore800DriveBindingSHA");
                if (!output.StartsWith("/private/tmp/rr019-core800-drive-", StringComparison.Ordinal) || Directory.Exists(output))
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
                if (binding == null || binding.recipe != Recipe || binding.finalBufferDigest != Final
                    || binding.buildGUID != Application.buildGUID || binding.sourceSHA256?.Length != 64 || binding.appExecutableSHA256?.Length != 64
                    || binding.gameSessionSHA256?.Length != 64 || binding.timeManagerSHA256?.Length != 64)
                    throw new IOException("Loaded QA build/source identity differs");
                if (Application.isEditor || Application.companyName != "EarthUnscripted Verification"
                    || Application.productName != "Core800DriveQA" || Application.identifier != "com.earthunscripted.qa.core800drive")
                    throw new IOException("Separate QA product identity required");
                rawHash = Hash(Path.Combine(inputs, "original-envelope.json"));
                cookedHash = Hash(cookedPath);
                metadataHash = Hash(cookedPath + ".eumeta");
                Need(binding.cookedPayloadSHA256 == VerifiedCoreTerrain.ReviewedCookedArtifactSHA256
                    && cookedHash == binding.cookedPayloadSHA256 && metadataHash == binding.cookedMetadataSHA256
                    && metadataHash == VerifiedCoreTerrain.ReviewedCookedPackageSHA256 && rawHash == binding.canonicalRawSHA256
                    && rawHash == ReviewedCanonicalRaw13Input.ExpectedBodySHA256 && sourceContext == binding.sourceContext,
                    "Pinned cooked/raw/source context before QA camera");
                Need(binding.preparedInputs != null && binding.preparedInputs.Length == 8, "Exactly payload, metadata, raw and five independently captured source inputs");
                foreach (var pin in binding.preparedInputs)
                    Need(Path.IsPathFullyQualified(pin.path) && File.Exists(pin.path) && new FileInfo(pin.path).Length == pin.bytes
                        && Hash(pin.path) == pin.sha256, "Prepared input differs before QA camera: " + pin.path);
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
                new GameObject("Isolated Core800 real vehicle proof").AddComponent<Core800DrivingNativeProof>();
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
                    Debug.Log("CORE800 DRIVE2.2 heartbeat " + currentPhase + " " + complete + "/" + total + " frames=" + beat.frame + " rendered=" + beat.renderedFrame);
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
            public double SourceCaptureSeconds, CachedValidationSeconds, IndexSeconds;
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
            SetPreparationProgress("prepared-index", 1, 1);
            return result;
        }

        Vector3 Point(float s)
        {
            Need(plan.TrySourceFrame(Road, s, RoadRowSide.Next, out var f), "Explicit outgoing source-side frame");
            double x = f.CenterX + Lane * f.RightX, z = f.CenterZ + Lane * f.RightZ;
            Need(plan.Contains(x, z), "No offscope fallback");
            Need(plan.TryRoad(Road, x, z, out var hit), "No nearest-road fallback");
            return new Vector3((float)x, (float)hit.Height, (float)z);
        }

        Vector3 Tangent(float s)
        {
            Need(plan.TrySourceFrame(Road, s, RoadRowSide.Next, out var f) && f.HasLongitudinal, "Named road has actual longitudinal frame");
            return new Vector3((float)(f.DCenterXPerH + Lane * f.DRightXPerH), 0, (float)(f.DCenterZPerH + Lane * f.DRightZPerH)).normalized;
        }

        void Preflight()
        {
            rows = plan.CopySourceRows(Road);
            Need(rows.Length > 2, "Literal source rows");
            station = (float)rows[0].SourceS + 15;
            endStation = (float)rows[rows.Length - 1].SourceS - 15;
            Need(endStation - station > 225, "Reviewed263m single-source corridor permits60s bounded drive");
            for (float s = station; s <= endStation; s += 1)
            {
                var p = Point(s);
                var f = Tangent(s);
                var right = Vector3.Cross(Vector3.up, f);
                for (int w = 0; w < 4; w++)
                {
                    var a = VehicleGeometry.Anchor(0, w);
                    var q = p + right * a.x + f * a.z;
                    Need(plan.Contains(q.x, q.z) && plan.TryRoad(Road, q.x, q.z, out var hit)
                        && Math.Abs(hit.Height - p.y) < .8, "Every preflight wheel footprint has named finalized asphalt support");
                }
            }
        }

        // Use the production collider factory, imported visual, tuning and supported
        // rest initializer exactly once before observation. Thereafter only ordinary
        // NetworkDriven input authority drives physics; reverse uses positive brake.
        void MakeCar()
        {
            carRoot = new GameObject("Production stock player coupe QA");
            carRoot.layer = 8;
            var p = Point(station);
            carRoot.transform.SetPositionAndRotation(p + Vector3.up * .7f, Quaternion.LookRotation(Tangent(station), Vector3.up));
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
            Need(car.TryInitializeSupportedRest(p, car.Body.rotation, out var reason, out bool missing),
                "Production supported-rest failed: " + reason + " missing=" + missing);
            Need(car.Grip == 1 && car.RoadSurface && car.SurfaceWetness == 0 && car.TyreFactor == 1,
                "Production dry-onroad/default-tyre conditions match GameSession.Update");
            report.dryGrip = car.Grip;
            report.dryRoadSurface = car.RoadSurface;
            report.dryWetness = car.SurfaceWetness;
            report.initialRest = true;
            previous = car.Body.position;
            car.Crashed += (severity, where) =>
            {
                if (errors.Count < 16)
                    errors.Add("Actual collision severity=" + severity + " at" + where);
            };
        }

        float NearestProgress()
        {
            var p = car.Body.position;
            double best = double.PositiveInfinity;
            float selected = station;
            for (int i = 1; i < rows.Length; i++)
            {
                var a = rows[i - 1];
                var b = rows[i];
                if (b.SourceS <= a.SourceS || b.SourceS < station - 12 || a.SourceS > station + 15)
                    continue;
                var aa = new Vector2(a.CenterX + Lane * a.RightX, a.CenterZ + Lane * a.RightZ);
                var bb = new Vector2(b.CenterX + Lane * b.RightX, b.CenterZ + Lane * b.RightZ);
                var d = bb - aa;
                if (d.sqrMagnitude < 1e-8)
                    continue;
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x, p.z) - aa, d) / d.sqrMagnitude), distance = (new Vector2(p.x, p.z) - aa - d * t).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    selected = (float)(a.SourceS + (b.SourceS - a.SourceS) * t);
                }
            }

            return selected;
        }

        void Inputs(float gas = 0, float brake = 0, float hand = 0, float steer = 0)
        {
            car.NetworkThrottle = gas;
            car.NetworkBrake = brake;
            car.NetworkHandbrake = hand;
            car.NetworkSteer = steer;
            input.Throttle = gas;
            input.Brake = brake;
            input.Handbrake = hand;
            input.Steer = steer;
        }

        // Observe production contacts and issue bounded steering/pedal input only.
        // Any support/height/damage/corridor failure stops and retains its raw trace.
        void FixedUpdate()
        {
            try
            {
                if (!observing || done || !car)
                    return;
                station = NearestProgress();
                if (phase == "driving60s")
                {
                    Need(station < endStation - 5, "Reached bounded source endpoint before driving completes");
                    var aim = Point(Math.Min(endStation, station + 4.5f));
                    var local = car.transform.InverseTransformPoint(aim);
                    float angle = Mathf.Atan2(2 * car.Metrics.Wheelbase * local.x, local.x * local.x + local.z * local.z) * Mathf.Rad2Deg;
                    float error = 3 - car.Speed;
                    Inputs(error > -.08f ? Mathf.Clamp(.08f + error * .2f, 0, .4f) : 0, error < -.08f ? Mathf.Clamp(-error * .2f,
                        0, .5f) : 0, 0, Mathf.Clamp(angle / Mathf.Lerp(30, 10, Mathf.Clamp01(Mathf.Abs(car.Speed) / 48)),
                        -1, 1));
                    report.driveSamples++;
                }

                var projected = Point(station);
                float lateral = Vector3.Dot(car.Body.position - projected, Vector3.Cross(Vector3.up, Tangent(station)));
                maxLateral = Math.Max(maxLateral, Math.Abs(lateral));
                travel += Vector3.Distance(previous, car.Body.position);
                previous = car.Body.position;
                var packets = new string[4];
                var rayTriangles = new int[4];
                var rayPoints = new Vector3[4];
                var rayNormals = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    var origin = car.transform.TransformPoint(VehicleGeometry.Anchor(0, i));
                    if (!Physics.Raycast(origin, -car.transform.up, out var hit, car.Metrics.SuspensionRayLength, ~(1 << 8), QueryTriggerInteraction.Ignore))
                    {
                        report.supportFailures++;
                        packets[i] = "MISSING";
                        rayTriangles[i] = -1;
                        continue;
                    }

                    packets[i] = hit.collider.name;
                    rayTriangles[i] = hit.triangleIndex;
                    rayPoints[i] = hit.point;
                    rayNormals[i] = hit.normal;
                    if (!(hit.collider is MeshCollider) || plan.PacketRole(packets[i]) != PreparedWorldMeshPlan800.Role.GroundRoad
                        || !plan.TryRoad(Road, hit.point.x, hit.point.z, out var road) || Math.Abs(road.Height - hit.point.y) > MaximumContactHeightErrorMetres)
                        report.offRoadContacts++;
                }

                if (car.WheelGrounded.Any(v => !v))
                    report.airborneWheelSamples++;
                if (holdMeasuring)
                {
                    holdMaximum = Math.Max(holdMaximum, Vector3.Distance(holdAnchor, car.Body.position));
                    report.holdSamples++;
                    if (!car.ParkingBrakeApplied || !car.ParkingBrakeHolding)
                    {
                        Inputs(0, 1, 1);
                        Finish(false, "Actual automatic parking hold lost during measured interval");
                        return;
                    }
                }

                report.fixedSamples++;
                report.maxSpeed = Math.Max(report.maxSpeed, car.AbsoluteSpeed);
                report.maxDamage = Math.Max(report.maxDamage, car.Damage);
                var sample = new Sample
                {
                    phase = phase,
                    road = Road,
                    time = Time.time,
                    speed = car.Speed,
                    damage = car.Damage,
                    steer = car.NetworkSteer,
                    throttle = car.NetworkThrottle,
                    brake = car.NetworkBrake,
                    handbrake = car.NetworkHandbrake,
                    sourceS = station,
                    lateralError = lateral,
                    position = car.Body.position,
                    velocity = car.Body.linearVelocity,
                    forward = car.transform.forward,
                    parkingApplied = car.ParkingBrakeApplied,
                    parkingHolding = car.ParkingBrakeHolding,
                    reverse = car.Reverse,
                    grounded = (bool[])car.WheelGrounded.Clone(),
                    contactPoints = (Vector3[])car.ContactPoints.Clone(),
                    contactNormals = (Vector3[])car.ContactNormals.Clone(),
                    contactPackets = packets,
                    rayTriangles = rayTriangles,
                    rayPoints = rayPoints,
                    rayNormals = rayNormals
                };
                telemetry.WriteLine(JsonUtility.ToJson(sample));
                if (report.supportFailures > 0 || report.offRoadContacts > 0 || report.airborneWheelSamples > 0
                    || car.Damage > MaximumAllowedDamage || Math.Abs(lateral) > MaximumLateralErrorMetres)
                {
                    Inputs(0, 1, 1);
                    Finish(false, "Actual driving support/damage/corridor gate failed; raw telemetry retained");
                }
            }
            catch (Exception failure)
            {
                Finish(false, failure.ToString());
            }
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

        IEnumerator Stop()
        {
            phase = "controlled-stop";
            float began = Time.time;
            while (car.AbsoluteSpeed > .08f)
            {
                Inputs(car.Reverse ? 1 : 0, car.Reverse ? 0 : 1, 1);
                Need(Time.time - began < 10, "Actual controlled stop timed out");
                yield return null;
            }

            Inputs();
        }

        IEnumerator Run()
        {
            Need(error == null, error);
            Need(!FindAnyObjectByType<GameSession>() && GameSession.SuppressAutomaticLaunch, "Explicit bootstrap suppression before any GameSession");
            priorQAScale = Time.timeScale;
            report.priorQATimeScale = priorQAScale;
            Time.timeScale = 1;
            clockOwned = true;
            report.activeQATimeScale = Time.timeScale;
            Need(Physics.simulationMode == SimulationMode.FixedUpdate && Time.timeScale == 1
                && Mathf.Abs(Time.fixedDeltaTime - ExpectedFixedStepSeconds) < FixedStepToleranceSeconds, "Actual production100Hz automatic physics; QA unpaused without changing timestep");
            Need((Physics.gravity - new Vector3(0, -9.81f, 0)).sqrMagnitude < .000001f, "Production gravity unchanged");
            Need(FindObjectsByType<Collider>(FindObjectsSortMode.None).Length == 0, "No foreign collision universe");
            Need(startupCamera && startupCamera.Created, "Validated QA early camera exists before preparation");
            var task = Task.Run(PrepareCachedPlan, cancellation.Token);
            while (!task.IsCompleted)
                yield return null;
            var prepared = task.GetAwaiter().GetResult();
            Need(startupCamera.ClearingRenderCount > 0, "Real early URP camera rendered during cached preparation");
            plan = prepared.Plan;
            report.sourceCaptureSeconds = prepared.SourceCaptureSeconds;
            report.cachedValidationSeconds = prepared.CachedValidationSeconds;
            report.indexSeconds = prepared.IndexSeconds;
            Need(plan.PacketCount == 1063 && plan.CopyRootKeys().Length == 16, "All exact completed scoped packets");
            Preflight();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Need(shader && shader.isSupported, "Actual URP");
            asphalt = new Material(shader)
            {
                color = new Color(.17f, .18f, .19f),
                name = "QA final ROAD grey"
            };
            ground = new Material(shader)
            {
                color = new Color(.25f, .42f, .2f),
                name = "QA final TERRAIN green"
            };
            stage = new GameObject("Actual complete800m finalized mesh");
            stage.SetActive(false);
            SetPreparationProgress("upload-original-packets", 0, 1063);
            double uploadStarted = Time.realtimeSinceStartupAsDouble;
            int uploadedPackets = 0;
            foreach (var key in plan.CopyRootKeys())
                foreach (var step in WorldMeshRootUpload800.BuildHidden(stage.transform, plan,
                    key, r => r == PreparedWorldMeshPlan800.Role.GroundRoad ? asphalt : ground, cancellation.Token))
                {
                    SetPreparationProgress("upload-original-packets", ++uploadedPackets, plan.PacketCount);
                    yield return null;
                }

            Need(uploadedPackets == plan.PacketCount, "Exact original packet upload count");
            report.uploadSeconds = Time.realtimeSinceStartupAsDouble - uploadStarted;
            SetPreparationProgress("ready-for-real-camera", 1063, 1063);
            Need(stage.transform.childCount == 16, "No partial world activation");
            stage.SetActive(true);
            Physics.SyncTransforms();
            MakeCar();
            var camRoot = new GameObject("Production chase camera QA");
            camera = camRoot.AddComponent<Camera>();
            startupCamera.ObserveDriveCamera(camera);
            camera.nearClipPlane = .06f;
            camera.farClipPlane = 1600;
            follow = camRoot.AddComponent<DriveCamera>();
            follow.Car = car;
            follow.Input = input;
            camera.transform.position = car.transform.position - car.transform.forward * 7 + Vector3.up * 3;
            legend = Geometry.FittedLabel("REAL VehicleController | Coronation Crescent | complete scoped800m\nROAD grey / TERRAIN green; no normal-world/scenery admission",
                camera.transform, new Vector3(0, .39f, 1), Quaternion.identity, 1.28f, .075f, Color.white);
            var sun = new GameObject("QA dry diagnostic sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.5f, .55f, .6f);
            telemetry = new StreamWriter(Path.Combine(output, "fixed-telemetry.jsonl"), false, new UTF8Encoding(false));
            double renderStarted = Time.realtimeSinceStartupAsDouble;
            while (!startupCamera.DriveCameraRendered)
            {
                Need(Time.realtimeSinceStartupAsDouble - renderStarted < 15, "Actual drive camera did not render; early clear retained");
                yield return null;
            }

            Need(startupCamera.RetiredAfterDriveRender, "Early camera retires only after real render");
            SetPreparationProgress("actual-driving", 1063, 1063);
            observing = true;
            phase = "initial-supported-rest";
            Inputs();
            yield return WaitSimulation(2);
            Need(car.ParkingBrakeApplied && car.ParkingBrakeHolding && car.AbsoluteSpeed < .05f, "Production supported rest stays parked");
            yield return Capture("initial-supported-rest");
            phase = "driving60s";
            driveBegan = Time.time;
            float beginTravel = travel, beginStation = station;
            yield return WaitSimulation(60.05f);
            report.driveSeconds = Time.time - driveBegan;
            report.distance = travel - beginTravel;
            report.parkingReleasedForward = !car.ParkingBrakeApplied && !car.Reverse;
            Need(report.driveSeconds >= 60 && report.distance >= 100 && station - beginStation >= 100
                && report.driveSamples >= Mathf.CeilToInt(MinimumDriveSeconds / Time.fixedDeltaTime) + 1 && report.parkingReleasedForward,
                "Actual >=60s/>=100m production driving and forward parking release");
            yield return Capture("driving60s-end");
            yield return Stop();
            phase = "automatic-stopped-hold";
            yield return WaitSimulation(2);
            Need(car.ParkingBrakeApplied && car.ParkingBrakeHolding, "Automatic zero-pedal hold after actual stop");
            holdAnchor = car.Body.position;
            holdMaximum = 0;
            holdMeasuring = true;
            report.holdAnchor = holdAnchor;
            report.holdStartTime = Time.time;
            yield return WaitSimulation(10.03f);
            holdMaximum = Math.Max(holdMaximum, Vector3.Distance(holdAnchor, car.Body.position));
            holdMeasuring = false;
            report.holdEndTime = Time.time;
            report.maxHoldDisplacement = holdMaximum;
            report.parkingCreep = Vector3.Distance(holdAnchor, car.Body.position);
            report.automaticStoppedHold = car.ParkingBrakeApplied && car.ParkingBrakeHolding
                && report.holdEndTime - report.holdStartTime >= 10 && report.holdSamples >= Mathf.CeilToInt(MinimumHoldSeconds / Time.fixedDeltaTime) + 1
                && holdMaximum < .05f;
            Need(report.automaticStoppedHold, "Actual10s automatic stopped hold MAX displacement<=5cm at every physics sample");
            yield return Capture("automatic-holding");
            phase = "intentional-reverse";
            var reverseStart = car.Body.position;
            var reverseForward = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            float reverseBegan = Time.time;
            Inputs(0, 1);
            yield return WaitSimulation(.2f);
            Need(!car.Reverse && car.ParkingBrakeApplied, "Actual brake-to-reverse delay retains parking before intentional gear change");
            while (-Vector3.Dot(car.Body.position - reverseStart, reverseForward) < 2)
            {
                Need(Time.time - reverseBegan < 10, "Intentional reverse did not move");
                yield return null;
            }

            report.parkingReleasedReverse = car.Reverse && !car.ParkingBrakeApplied && car.Speed < -.15f;
            Need(report.parkingReleasedReverse, "Intentional reverse releases parking through actual AutomaticPedals");
            yield return Capture("reverse-released");
            yield return Stop();
            Need(runtimeErrors == 0 && !FindAnyObjectByType<GameSession>(), "No runtime errors or accidental normal bootstrap");
            phase = "final-static-inspection";
            yield return WaitSimulation(1);
            follow.enabled = false;
            BuildBankOverlay();
            Destroy(legend.gameObject);
            legend = Geometry.FittedLabel("FINAL scoped800m bank / ROAD grey / TERRAIN green / BANK orange\nFictional derived cut bank; SAME collision surface. No scenery-art claim",
                camera.transform, new Vector3(0, .39f, 1), Quaternion.identity, 1.28f, .075f, Color.white);
            camera.transform.position = new Vector3(-17, 88, -109);
            camera.transform.LookAt(new Vector3(-10.5f, 83, -101.5f));
            yield return Capture("final-bank-closeup");
            camera.transform.position = new Vector3(650, 900, -700);
            camera.transform.LookAt(new Vector3(0, 80, 0));
            yield return Capture("final-overview");
            Finish(true, null);
        }

        void BuildBankOverlay()
        {
            // Borrow the Lit shader already verified on the actual road material.
            // A name-only Unlit lookup can be stripped from standalone builds;
            // this diagnostic changes colour only and never collision geometry.
            var shader = asphalt ? asphalt.shader : null;
            Need(shader && shader.isSupported, "Existing road shader for diagnostic overlay");
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

        void OnGUI()
        {
            if (!car)
                return;
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft
            };
            GUI.Box(new Rect(18, Screen.height - 140, 1000, 112), "SCOPED800m REAL VEHICLE TEST — " + phase + "\n" + Mathf.Abs(car.Speed).ToString("F1") + "m/s | damage" + car.Damage.ToString("F2") + " | " + (car.ParkingBrakeHolding ? "PARKING BRAKE · HOLDING" : car.ParkingBrakeApplied ? "PARKING BRAKE" : "DRIVING") + "\nProduction springs/CG/tyres/controller, dry road. This is not final world art.",
                style);
        }

        void Finish(bool pass, string failure)
        {
            if (done)
                return;
            done = true;
            observing = false;
            if (car)
                Inputs(0, 1, 1);
            try
            {
                telemetry?.Flush();
                telemetry?.Dispose();
                heartbeats?.Flush();
                heartbeats?.Dispose();
                report.heartbeatSHA256 = outputOwned ? Hash(Path.Combine(output, "preparation-heartbeat.jsonl")) : "missing";
                report.heartbeatRows = heartbeatRows;
                report.updateCount = updateCount;
                report.cookedPayloadSHA256 = cookedHash;
                report.cookedMetadataSHA256 = metadataHash;
                report.canonicalRawSHA256 = rawHash;
                report.earlyCameraCreated = startupCamera && startupCamera.Created;
                report.earlyCameraRenderCount = startupCamera ? startupCamera.ClearingRenderCount : 0;
                report.driveCameraRendered = startupCamera && startupCamera.DriveCameraRendered;
                report.earlyCameraRetiredAfterDriveRender = startupCamera && startupCamera.RetiredAfterDriveRender;
                report.status = pass ? "PASS_SCOPED_REAL_VEHICLE_ONLY" : "FAIL";
                report.error = failure;
                report.scope = "Production stock car on exact complete800m ground; single named263m source road and>=60s driving. No junction-network/full847m/normal-world/performance admission.";
                report.minimumDriveSamples = Mathf.CeilToInt(MinimumDriveSeconds / Time.fixedDeltaTime) + 1;
                report.minimumHoldSamples = Mathf.CeilToInt(MinimumHoldSeconds / Time.fixedDeltaTime) + 1;
                report.physicsGravity = Physics.gravity;
                report.physicalFixedDeltaTime = Time.fixedDeltaTime;
                report.recipe = Recipe;
                report.finalBufferDigest = plan?.FinalBufferDigest;
                report.sourceDigest = plan?.SourceDigest;
                report.appBuildGUID = Application.buildGUID;
                report.bindingSHA256 = bindingHash;
                report.gameSessionSHA256 = binding?.gameSessionSHA256;
                report.timeManagerSHA256 = binding?.timeManagerSHA256;
                report.company = Application.companyName;
                report.product = Application.productName;
                report.identifier = Application.identifier;
                report.runtimeErrors = runtimeErrors;
                report.maxLateralError = maxLateral;
                report.bootstrapSuppressed = GameSession.SuppressAutomaticLaunch;
                report.noGameSession = !FindAnyObjectByType<GameSession>();
                report.qaDefaultCareerUnchanged = Hash(career) == careerHash;
                report.inputsUnchanged = inputs != null && binding?.preparedInputs != null
                    && Hash(cookedPath) == cookedHash && Hash(cookedPath + ".eumeta") == metadataHash && binding.preparedInputs.All(pin => File.Exists(pin.path)
                    && new FileInfo(pin.path).Length == pin.bytes && Hash(pin.path) == pin.sha256) && Hash(Path.Combine(inputs,
                    "original-envelope.json")) == rawHash && Hash(bindingPath) == bindingHash;
                report.telemetrySHA256 = outputOwned ? Hash(Path.Combine(output, "fixed-telemetry.jsonl")) : "missing";
                report.captures = captures.ToArray();
                report.errors = errors.ToArray();
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
                if (carRoot)
                    Geometry.ReleaseMeshes(carRoot);
                if (clockOwned)
                {
                    Time.timeScale = priorQAScale;
                    clockOwned = false;
                }

                Application.Quit(report.status == "PASS_SCOPED_REAL_VEHICLE_ONLY" ? 0 : 1);
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
