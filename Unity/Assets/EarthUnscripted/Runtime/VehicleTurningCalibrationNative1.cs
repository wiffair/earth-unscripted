using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit private QA calibration of the actual stock VehicleController.
    /// Four independently pre-staged cars receive ordinary NetworkDriven pedal and
    /// steering commands on an owned flat collider. No Physics.Simulate, added
    /// forces, kinematic substitute, velocity setting or observed pose reset occurs.
    /// Measured turning radii apply only to these dry low-speed conditions; they
    /// do not admit a real-road guide, lane policy or arbitrary minimum radius.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VehicleTurningCalibrationNative1 : MonoBehaviour
    {
        public const string Recipe = "ActualStockTurning/FlatDry/FourCases/100Hz/1";
        public const string SurfaceRecipe = "OwnedFlatBox1;topY=0m;sizeXYZ=500m,0.5m,500m;centreY=-0.25m;layer=0;defaultPhysicsMaterial;noWorldData";
        private const string Flag = "-earthUnscriptedTurningProof1";
        private const string Product = "TurningCalibrationQA1";
        private const string Identifier = "com.earthunscripted.qa.turning1";
        private const float ExpectedStepSeconds = 0.01f;
        private const float StepToleranceSeconds = 0.000001f;
        private const double WatchdogSeconds = 360;
        private const int MaximumRows = 140000;
        private const long MaximumTraceBytes = 192L * 1024 * 1024;
        private const float MinimumMeasureSeconds = 20;
        private const float MaximumMeasureSeconds = 35;
        private const float SettleSeconds = 8;
        private const float MaximumCaseSeconds = 65;
        private const float MaximumDamage = 0.0001f;
        private const float MaximumStoppedExcursionMetres = 0.05f;
        private const float MaximumFloorHeightErrorMetres = 0.0002f;
        private const float MaximumDistanceFromStagedMetres = 80;
        private const float MaximumTargetSpeedError = 0.3f;
        private const float MinimumSpeedForMeasure = 0.8f;
        private const float MaximumPhysicalStepMetres = 0.15f;
        // Fit quality flags qualify a measurement; none asserts a minimum radius.
        private const double DiagnosticRmsLimitMetres = 0.15;
        private const double DiagnosticMaximumResidualMetres = 0.35;
        private const double DiagnosticEstimatorAgreementMetres = 0.15;
        private const float MaximumCommandThrottle = 0.55f;
        private const float ProportionalDemand = 0.2f;
        private const float IntegralDemandPerMetre = 0.04f;
        private const float MaximumIntegralDemand = 0.35f;
        private const float BaseRollingDemand = 0.08f;
        private const float PedalHysteresisMetresPerSecond = 0.08f;
        [Serializable]
        public sealed class FilePin
        {
            public string path, sha256;
            public long bytes;
        }

        [Serializable]
        public sealed class Binding
        {
            public string recipe, buildGUID, sourceSHA256, appExecutableSHA256;
            public string gameSessionSHA256, timeManagerSHA256, surfaceRecipeSHA256;
            public FilePin[] sourceFiles, appFiles;
        }

        [Serializable]
        public sealed class Trace
        {
            public int tick, actor, activeCase;
            public string phase;
            public float fixedSeconds, fixedStepSeconds, signedSpeedMetresPerSecond, absoluteSpeedMetresPerSecond;
            public float targetSpeedMetresPerSecond, appliedSteeringDegrees, requestedSteer, requestedThrottle, requestedBrake, requestedHandbrake;
            public float yawDegrees, lateralSlipMetresPerSecond, damage;
            public bool reverse, parkingApplied, parkingHolding;
            public Vector3 position, velocity, angularVelocityRadiansPerSecond, forward;
            public bool[] grounded;
            public Vector3[] contactPoints, contactNormals, currentRayPoints, currentRayNormals;
            public float[] powerSlipDimensionless;
            public string[] currentRayColliderIds;
            public bool commandsApplyToFollowingSimulationStep = true;
        }

        [Serializable]
        public sealed class FactoryShape
        {
            public int actor, model, layer;
            public bool importedAppearance;
            public float massKg, wheelbaseMetres, trackMetres, tyreRadiusMetres;
            public Vector3 centreOfMassLocal, inertiaTensorKgMetresSquared, colliderCentreLocal, colliderSizeLocal, stagedBodyPosition;
            public Quaternion stagedBodyRotation;
            public Vector3[] suspensionAnchorsLocal;
        }

        [Serializable]
        public sealed class CaseResult
        {
            public int actor, steerSign, measuredRows;
            public float requestedSpeedMetresPerSecond, elapsedMeasureSeconds, meanSpeedMetresPerSecond;
            public double absoluteYawRadians, horizontalTravelMetres, radiusFromArcAndYawMetres;
            public double fitCentreX, fitCentreZ, fittedRootRadiusMetres, fitRmsResidualMetres, fitMaximumResidualMetres;
            public float meanAppliedSteeringDegrees, maximumLateralSlipMetresPerSecond;
            public double[] meanTyreRadiiMetres;
            public bool circleFitWithinDiagnosticResiduals;
            public int firstMeasuredTick, lastMeasuredTick;
        }

        [Serializable]
        public sealed class Report
        {
            public string status, error, recipe, scope, bindingSHA256, appBuildGUID, sourceSHA256, surfaceRecipeSHA256;
            public string telemetrySHA256, factoryShapesSHA256;
            public float priorTimeScale, activeTimeScale, fixedStepSeconds;
            public Vector3 gravity;
            public bool bootstrapSuppressed, noGameSession, dryDefaultStock, noWorldData, earlyCameraRendered, driveCameraRendered, earlyCameraRetired;
            public int rawRows, runtimeErrors;
            public string actualFloorColliderId;
            public CaseResult[] cases;
            public FactoryShape[] factoryShapes;
            public string[] errors, captures;
        }

        private static bool requested, outputOwned;
        private static string output, startupError, bindingPath, bindingHash;
        private static Binding binding;
        private static Core800DriveStartupCamera earlyCamera;
        private static readonly List<string> runtimeErrorMessages = new List<string>();
        private static int runtimeErrorCount;
        private readonly List<CaseResult> results = new List<CaseResult>();
        private readonly List<string> captures = new List<string>();
        private readonly List<Trace> measured = new List<Trace>();
        private readonly VehicleController[] cars = new VehicleController[4];
        private readonly DriverInput[] inputs = new DriverInput[4];
        private readonly Vector3[] stagedPositions = new Vector3[4];
        private readonly Vector3[] previousObservedPositions = new Vector3[4];
        private readonly Vector3[] idleAnchors = new Vector3[4];
        private readonly bool[] parked =
        {
            true,
            true,
            true,
            true
        };
        private readonly FactoryShape[] shapes = new FactoryShape[4];
        private readonly float[] targetSpeeds =
        {
            1.5f,
            1.5f,
            2.5f,
            2.5f
        };
        private readonly int[] steerSigns =
        {
            -1,
            1,
            -1,
            1
        };
        private readonly Report report = new Report();
        private GameObject stage;
        private Collider floor;
        private Material floorMaterial;
        private Camera view;
        private DriveCamera follow;
        private StreamWriter trace;
        private int activeCase = -1, tick, rowCount;
        private string phase = "preparing";
        private float phaseStart, caseStart, integralDemand, priorTimeScale;
        private double deadline, lastHeartbeat;
        private bool observing, done, clockOwned, finishing;
        private bool finishMeasuredThisTick;
        private bool resourcesDisposed, ambientOwned;
        private Color priorAmbient;
        private float measuredYawRadians, previousMeasuredYawDegrees;
        public static string SurfaceDigest => World13DocumentCodec.Hash(Encoding.UTF8.GetBytes(SurfaceRecipe));

        private static string Hash(string path) => File.Exists(path) ? Core.DataPackageRules.HashFile(path) : "missing";
        private static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing " + name);
            return args[index + 1];
        }

        private static void Need(bool condition, string why)
        {
            if (!condition)
                throw new InvalidOperationException(why);
        }

        private static void LogError(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            runtimeErrorCount++;
            if (runtimeErrorMessages.Count < 16)
                runtimeErrorMessages.Add(message);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Isolate()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, Flag) < 0)
                return;
            requested = true;
            GameSession.SuppressAutomaticLaunch = true;
            Application.runInBackground = true;
            Application.logMessageReceived += LogError;
            string safe = Path.Combine(Application.temporaryCachePath, "invalid-turning-" + Guid.NewGuid().ToString("N"));
            PlayerProfile.StorageOverride = safe + ".json";
            RegionCache.OverrideFolder = safe + "-regions";
            try
            {
                output = Path.GetFullPath(Argument(args, Flag));
                bindingPath = Path.GetFullPath(Argument(args, "-earthUnscriptedTurningBinding"));
                bindingHash = Argument(args, "-earthUnscriptedTurningBindingSHA");
                Need(output.StartsWith("/private/tmp/rr019-turning-calibration1-", StringComparison.Ordinal) && !Directory.Exists(output), "Fresh private calibration output required");
                Directory.CreateDirectory(output);
                outputOwned = true;
                PlayerProfile.StorageOverride = Path.Combine(output, "isolated-profile.json");
                RegionCache.OverrideFolder = Path.Combine(output, "isolated-regions");
                Need(new FileInfo(bindingPath).Length <= 1024 * 1024 && Hash(bindingPath) == bindingHash, "Exact source/build binding");
                binding = JsonUtility.FromJson<Binding>(File.ReadAllText(bindingPath));
                Need(binding != null && binding.recipe == Recipe && binding.surfaceRecipeSHA256 == SurfaceDigest && binding.buildGUID == Application.buildGUID, "Calibration binary/surface identity");
                Need(!Application.isEditor && Application.companyName == "EarthUnscripted Verification" && Application.productName == Product && Application.identifier == Identifier, "Separate calibration QA product required");
                Need(binding.sourceSHA256?.Length == 64 && binding.gameSessionSHA256?.Length == 64 && binding.timeManagerSHA256?.Length == 64, "Compiled bootstrap/physics source pins required");
            }
            catch (Exception error)
            {
                startupError = error.ToString();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeScene()
        {
            if (requested && startupError == null)
                try
                {
                    earlyCamera = Core800DriveStartupCamera.Create();
                }
                catch (Exception error)
                {
                    startupError = error.ToString();
                }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (requested)
                new GameObject("Explicit isolated actual-turning calibration").AddComponent<VehicleTurningCalibrationNative1>();
        }

        private void Awake()
        {
            deadline = Time.realtimeSinceStartupAsDouble + WatchdogSeconds;
        }

        private IEnumerator Start()
        {
            yield return Guard(Run());
        }

        private IEnumerator Guard(IEnumerator routine)
        {
            while (!done)
            {
                bool more;
                object yielded = null;
                try
                {
                    more = routine.MoveNext();
                    if (more)
                        yielded = routine.Current;
                }
                catch (Exception error)
                {
                    Finish(false, error.ToString());
                    yield break;
                }

                if (!more)
                    yield break;
                if (yielded is IEnumerator nested)
                    yield return Guard(nested);
                else
                    yield return yielded;
            }
        }

        private IEnumerator Run()
        {
            if (startupError != null || !outputOwned)
            {
                Finish(false, startupError ?? "No owned output");
                yield break;
            }

            try
            {
                Prepare();
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
                yield break;
            }

            yield return null;
            while (!done && (earlyCamera == null || !earlyCamera.DriveCameraRendered))
                yield return null;
            if (done)
                yield break;
            float firstPhysicsTime = Time.fixedTime;
            while (!done && Time.fixedTime - firstPhysicsTime < 0.25f)
                yield return null;
            if (done)
                yield break;
            Need(cars.All(car => car.ParkingBrakeApplied && car.ParkingBrakeHolding), "Actual initial parked physics settled before observation");
            observing = true;
            for (int index = 0; index < 4 && !done; index++)
            {
                activeCase = index;
                parked[index] = false;
                follow.Car = cars[index];
                follow.Input = inputs[index];
                integralDemand = 0;
                phase = "initial-supported-rest";
                phaseStart = caseStart = Time.time;
                bool capturedMoving = false;
                while (!done && phase != "case-complete")
                {
                    if (!capturedMoving && phase == "measuring-steady-turn" && Time.time - phaseStart >= 10)
                    {
                        yield return Capture("case-" + index + "-actual-steady-turn");
                        capturedMoving = true;
                    }
                    else
                        yield return null;
                }

                if (done)
                    yield break;
                phase = "case-complete";
                yield return new WaitForEndOfFrame();
                yield return Capture("case-" + index + "-parked-after-measurement");
            }

            if (!done)
                Finish(true, null);
        }

        private void Prepare()
        {
            Need(GameSession.SuppressAutomaticLaunch && !FindAnyObjectByType<GameSession>(), "No real GameSession/bootstrap");
            Need(Math.Abs(Time.fixedDeltaTime - ExpectedStepSeconds) <= StepToleranceSeconds && Physics.simulationMode == SimulationMode.FixedUpdate, "Actual automatic100Hz project physics required; never reassigned");
            Need((Physics.gravity - new Vector3(0, -9.81f, 0)).sqrMagnitude < 0.000001f, "Production gravity unchanged");
            priorTimeScale = Time.timeScale;
            Time.timeScale = 1;
            clockOwned = true;
            stage = new GameObject("Owned flat calibration stage; no geographic world");
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Owned500m flat surface; topY0";
            slab.transform.SetParent(stage.transform, false);
            slab.transform.position = new Vector3(0, -0.25f, 0);
            slab.transform.localScale = new Vector3(500, 0.5f, 500);
            floor = slab.GetComponent<Collider>();
            Physics.SyncTransforms();
            var factory = typeof(GameSession).GetMethod("AddBootCollider", BindingFlags.Static | BindingFlags.NonPublic);
            Need(factory != null, "Production car collider factory");
            for (int index = 0; index < 4; index++)
            {
                var root = new GameObject("Actual stock calibration actor" + index);
                root.transform.SetParent(stage.transform, false);
                root.layer = 8;
                Vector3 start = new Vector3(index % 2 == 0 ? -60 : 60, 0, index < 2 ? -60 : 60);
                root.transform.SetPositionAndRotation(start + Vector3.up * 0.7f, Quaternion.identity);
                factory.Invoke(null, new object[] { root, false });
                var visual = root.AddComponent<CarVisual>();
                var owned = new Core.OwnedCar
                {
                    model = 0,
                    paint = 0
                };
                visual.Build(CarColours.Paint[0], true, 0, owned.wheels, owned.bodyKit, owned.finish);
                var car = root.AddComponent<VehicleController>();
                cars[index] = car;
                car.Visual = visual;
                car.NetworkDriven = true;
                car.ParkingAutomaticEnabled = true;
                car.ApplyTuning(owned);
                var input = root.AddComponent<DriverInput>();
                input.enabled = false;
                input.Profile = new PlayerProfile();
                inputs[index] = input;
                car.Input = input;
                Need(visual.ImportedAppearance && car.Grip == 1 && car.RoadSurface && car.SurfaceWetness == 0 && car.TyreFactor == 1 && car.PowerFactor == 1 && car.BrakeFactor == 1 && car.SpringFactor == 1 && car.DamperFactor == 1 && car.DifferentialFactor == 1 && Math.Abs(car.Body.mass - 1350) < 0.001f, "Actual stock imported model/default dry tuning required");
                Need(car.TryInitializeSupportedRest(start, Quaternion.identity, out string reason, out bool missing), "Actual supported-rest initializer: " + reason + " missing=" + missing);
                stagedPositions[index] = idleAnchors[index] = previousObservedPositions[index] = car.Body.position;
                var box = root.GetComponent<BoxCollider>();
                shapes[index] = new FactoryShape
                {
                    actor = index,
                    model = car.Metrics.Model,
                    layer = root.layer,
                    importedAppearance = visual.ImportedAppearance,
                    massKg = car.Body.mass,
                    wheelbaseMetres = car.Metrics.Wheelbase,
                    trackMetres = car.Metrics.Track,
                    tyreRadiusMetres = car.Metrics.Radius,
                    centreOfMassLocal = car.Body.centerOfMass,
                    inertiaTensorKgMetresSquared = car.Body.inertiaTensor,
                    colliderCentreLocal = box.center,
                    colliderSizeLocal = box.size,
                    stagedBodyPosition = car.Body.position,
                    stagedBodyRotation = car.Body.rotation,
                    suspensionAnchorsLocal = Enumerable.Range(0, 4).Select(i => VehicleGeometry.Anchor(0, i)).ToArray()
                };
                int capturedIndex = index;
                car.Crashed += (severity, where) =>
                {
                    if (runtimeErrorMessages.Count < 16)
                        runtimeErrorMessages.Add("Actor" + capturedIndex + " actual impact " + severity + " at" + where);
                };
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Need(shader && shader.isSupported, "Retained production Lit shader");
            floorMaterial = new Material(shader);
            floorMaterial.SetColor("_BaseColor", new Color(0.18f, 0.2f, 0.22f));
            slab.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var sun = new GameObject("Owned calibration daylight");
            sun.transform.SetParent(stage.transform, false);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2;
            sun.transform.rotation = Quaternion.Euler(45, -35, 0);
            priorAmbient = RenderSettings.ambientLight;
            ambientOwned = true;
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.6f);
            var cameraRoot = new GameObject("Actual production chase camera");
            cameraRoot.transform.SetParent(stage.transform, false);
            view = cameraRoot.AddComponent<Camera>();
            view.nearClipPlane = 0.06f;
            view.farClipPlane = 1000;
            follow = cameraRoot.AddComponent<DriveCamera>();
            follow.Car = cars[0];
            follow.Input = inputs[0];
            follow.Mode = 4;
            view.transform.position = cars[0].transform.position - Vector3.forward * 12 + Vector3.up * 5;
            Need(earlyCamera && earlyCamera.Created, "Validated early rendering camera required");
            earlyCamera.ObserveDriveCamera(view);
            trace = new StreamWriter(Path.Combine(output, "fixed-telemetry.jsonl"), false, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "factory-shapes.json"), JsonUtility.ToJson(new ShapeList { values = shapes }, true));
            report.priorTimeScale = priorTimeScale;
            report.activeTimeScale = Time.timeScale;
            report.fixedStepSeconds = Time.fixedDeltaTime;
            report.gravity = Physics.gravity;
            report.actualFloorColliderId = floor.GetEntityId().ToString();
            phase = "awaiting-real-camera-render";
        }

        private void Update()
        {
            if (done)
                return;
            if (runtimeErrorCount > 0)
            {
                Finish(false, "Captured actual runtime error");
                return;
            }

            if (Time.realtimeSinceStartupAsDouble > deadline)
            {
                Finish(false, "Calibration realtime watchdog");
                return;
            }

            if (Time.realtimeSinceStartupAsDouble - lastHeartbeat >= 2)
            {
                lastHeartbeat = Time.realtimeSinceStartupAsDouble;
                if (outputOwned)
                    File.AppendAllText(Path.Combine(output, "heartbeat.jsonl"), "{\"frame\":" + Time.frameCount + ",\"rendered\":" + Time.renderedFrameCount + ",\"phase\":\"" + phase + "\",\"rows\":" + rowCount + "}\n");
            }
        }

        private void FixedUpdate()
        {
            if (!observing || done || activeCase < 0)
                return;
            try
            {
                tick++;
                finishMeasuredThisTick = false;
                Need(Time.time - caseStart <= MaximumCaseSeconds, "Actual case failed to reach a bounded stable measurement/stop");
                var car = cars[activeCase];
                float elapsed = Time.time - phaseStart;
                if (phase == "initial-supported-rest")
                {
                    Commands(car, 0, 0, 0, 0);
                    if (elapsed >= 2)
                    {
                        Need(car.ParkingBrakeApplied && car.ParkingBrakeHolding, "Automatic initial rest hold");
                        BeginPhase("accelerating-straight");
                    }
                }
                else if (phase == "accelerating-straight")
                {
                    SpeedCommands(car, targetSpeeds[activeCase], 0);
                    if (elapsed >= 2 && Math.Abs(car.Speed - targetSpeeds[activeCase]) <= MaximumTargetSpeedError)
                        BeginPhase("steering-settle");
                }
                else if (phase == "steering-settle")
                {
                    SpeedCommands(car, targetSpeeds[activeCase], steerSigns[activeCase]);
                    if (elapsed >= SettleSeconds && Math.Abs(car.Speed - targetSpeeds[activeCase]) <= MaximumTargetSpeedError)
                    {
                        measured.Clear();
                        measuredYawRadians = 0;
                        previousMeasuredYawDegrees = BodyYaw(car);
                        BeginPhase("measuring-steady-turn");
                    }
                }
                else if (phase == "measuring-steady-turn")
                {
                    SpeedCommands(car, targetSpeeds[activeCase], steerSigns[activeCase]);
                    Need(car.Speed > MinimumSpeedForMeasure && Math.Abs(car.Speed - targetSpeeds[activeCase]) <= MaximumTargetSpeedError, "Measurement speed outside declared stable target band");
                    float yaw = BodyYaw(car);
                    measuredYawRadians += Mathf.DeltaAngle(previousMeasuredYawDegrees, yaw) * Mathf.Deg2Rad;
                    previousMeasuredYawDegrees = yaw;
                    int requiredRows = Mathf.CeilToInt(MinimumMeasureSeconds / Time.fixedDeltaTime) + 1;
                    if (elapsed >= MinimumMeasureSeconds && measured.Count + 1 >= requiredRows && Math.Abs(measuredYawRadians) >= Mathf.PI)
                        finishMeasuredThisTick = true;
                    else
                        Need(elapsed <= MaximumMeasureSeconds, "Insufficient actual yaw for a meaningful steady-circle estimate");
                }
                else if (phase == "controlled-stop")
                {
                    Commands(car, 0, 1, 0, 1);
                    if (car.AbsoluteSpeed < 0.06f)
                    {
                        Commands(car, 0, 0, 0, 0);
                        idleAnchors[activeCase] = car.Body.position;
                        BeginPhase("awaiting-final-automatic-hold");
                    }
                }
                else if (phase == "awaiting-final-automatic-hold")
                {
                    Commands(car, 0, 0, 0, 0);
                    Need(Vector3.Distance(idleAnchors[activeCase], car.Body.position) < MaximumStoppedExcursionMetres, "Automatic latch settling exceeded5cm from the genuine stopped pose");
                    Need(elapsed <= 3, "Automatic parking did not engage after the real controlled stop");
                    if (car.ParkingBrakeApplied && car.ParkingBrakeHolding)
                    {
                        idleAnchors[activeCase] = car.Body.position;
                        BeginPhase("final-automatic-hold");
                    }
                }
                else if (phase == "final-automatic-hold")
                {
                    Commands(car, 0, 0, 0, 0);
                    Need(Vector3.Distance(idleAnchors[activeCase], car.Body.position) < MaximumStoppedExcursionMetres, "Final hold exceeded5cm MAX excursion");
                    if (elapsed >= 2)
                    {
                        Need(car.ParkingBrakeApplied && car.ParkingBrakeHolding, "Actual automatic final parking absent");
                        parked[activeCase] = true;
                        phase = "case-complete";
                    }
                }

                for (int index = 0; index < 4; index++)
                    Observe(index);
                if (finishMeasuredThisTick)
                {
                    results.Add(FitCase(activeCase));
                    BeginPhase("controlled-stop");
                }
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private void BeginPhase(string next)
        {
            phase = next;
            phaseStart = Time.time;
        }

        private static float BodyYaw(VehicleController car)
        {
            Vector3 forward = car.Body.rotation * Vector3.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static void Commands(VehicleController car, float throttle, float brake, float steer, float handbrake)
        {
            car.NetworkThrottle = throttle;
            car.NetworkBrake = brake;
            car.NetworkSteer = steer;
            car.NetworkHandbrake = handbrake;
        }

        private void SpeedCommands(VehicleController car, float target, float steer)
        {
            float error = target - car.Speed;
            integralDemand = Mathf.Clamp(integralDemand + error * Time.fixedDeltaTime * IntegralDemandPerMetre, 0, MaximumIntegralDemand);
            float throttle = error >= -PedalHysteresisMetresPerSecond ? Mathf.Clamp(BaseRollingDemand + error * ProportionalDemand + integralDemand, 0, MaximumCommandThrottle) : 0;
            float brake = error < -PedalHysteresisMetresPerSecond ? Mathf.Clamp(-error * ProportionalDemand, 0, 0.5f) : 0;
            if (brake > 0)
                integralDemand = Math.Max(0, integralDemand - Time.fixedDeltaTime * 0.2f);
            Commands(car, throttle, brake, steer, 0);
        }

        private void Observe(int index)
        {
            var car = cars[index];
            Need(car && car.Body && !car.Body.isKinematic && !car.Reverse && car.Damage <= MaximumDamage, "Actual stock actor reverse/damage/physics invariant");
            Need(Vector3.Distance(stagedPositions[index], car.Body.position) < MaximumDistanceFromStagedMetres, "Bounded private calibration surface trajectory");
            Need(Vector3.Distance(previousObservedPositions[index], car.Body.position) <= MaximumPhysicalStepMetres, "Every actual fixed-step pose stays within the independent15cm movement limit");
            previousObservedPositions[index] = car.Body.position;
            if (parked[index])
            {
                Need(car.ParkingBrakeApplied && car.ParkingBrakeHolding && Vector3.Distance(idleAnchors[index], car.Body.position) < MaximumStoppedExcursionMetres, "Other actor did not remain actually parked");
            }

            var row = new Trace
            {
                tick = tick,
                actor = index,
                activeCase = activeCase,
                phase = index == activeCase ? phase : "supported-parked-idle",
                fixedSeconds = Time.fixedTime,
                fixedStepSeconds = Time.fixedDeltaTime,
                signedSpeedMetresPerSecond = car.Speed,
                absoluteSpeedMetresPerSecond = car.AbsoluteSpeed,
                targetSpeedMetresPerSecond = targetSpeeds[index],
                appliedSteeringDegrees = car.Steering,
                requestedSteer = car.NetworkSteer,
                requestedThrottle = car.NetworkThrottle,
                requestedBrake = car.NetworkBrake,
                requestedHandbrake = car.NetworkHandbrake,
                yawDegrees = BodyYaw(car),
                lateralSlipMetresPerSecond = car.Slip,
                damage = car.Damage,
                reverse = car.Reverse,
                parkingApplied = car.ParkingBrakeApplied,
                parkingHolding = car.ParkingBrakeHolding,
                position = car.Body.position,
                velocity = car.Body.linearVelocity,
                angularVelocityRadiansPerSecond = car.Body.angularVelocity,
                forward = car.Body.rotation * Vector3.forward,
                grounded = (bool[])car.WheelGrounded.Clone(),
                contactPoints = (Vector3[])car.ContactPoints.Clone(),
                contactNormals = (Vector3[])car.ContactNormals.Clone(),
                powerSlipDimensionless = (float[])car.WheelSlip.Clone(),
                currentRayPoints = new Vector3[4],
                currentRayNormals = new Vector3[4],
                currentRayColliderIds = new string[4]
            };
            for (int wheel = 0; wheel < 4; wheel++)
            {
                Vector3 origin = car.Body.position + car.Body.rotation * VehicleGeometry.Anchor(0, wheel);
                Need(car.WheelGrounded[wheel], "Actual controller tyre contact flag");
                bool found = Physics.Raycast(origin, -(car.Body.rotation * Vector3.up), out var hit, car.Metrics.SuspensionRayLength, ~(1 << 8), QueryTriggerInteraction.Ignore);
                Need(found && hit.collider == floor, "Every current actual tyre must hit the owned flat collider");
                Need(Math.Abs(hit.point.y) <= MaximumFloorHeightErrorMetres && hit.normal.y > 0.9999f, "Actual flat contact height/normal");
                row.currentRayPoints[wheel] = hit.point;
                row.currentRayNormals[wheel] = hit.normal;
                row.currentRayColliderIds[wheel] = hit.collider.GetEntityId().ToString();
            }

            Need(++rowCount <= MaximumRows && trace.BaseStream.Length <= MaximumTraceBytes, "Bounded complete raw telemetry; never truncate");
            trace.WriteLine(JsonUtility.ToJson(row));
            if (index == activeCase && phase == "measuring-steady-turn")
                measured.Add(row);
            if (rowCount % 400 == 0)
                trace.Flush();
        }

        private CaseResult FitCase(int index)
        {
            Need(measured.Count >= Mathf.CeilToInt(MinimumMeasureSeconds / Time.fixedDeltaTime) + 1, "Full100Hz stable turning window");
            var fit = Qa.TurningCircleAnalysis1.Fit(measured.Select(row => new Qa.TurningCircleAnalysis1.Sample(row.position.x, row.position.z, row.yawDegrees, row.currentRayPoints.Select(point => (double)point.x).ToArray(), row.currentRayPoints.Select(point => (double)point.z).ToArray())).ToArray());
            Need(Math.Sign(fit.SignedYawRadians) == steerSigns[index] && Math.Abs(fit.SignedYawRadians) >= Math.PI, "Actual intended forward turn direction and>=180degree coverage");
            return new CaseResult
            {
                actor = index,
                steerSign = steerSigns[index],
                measuredRows = measured.Count,
                requestedSpeedMetresPerSecond = targetSpeeds[index],
                elapsedMeasureSeconds = measured[measured.Count - 1].fixedSeconds - measured[0].fixedSeconds,
                meanSpeedMetresPerSecond = measured.Average(row => row.signedSpeedMetresPerSecond),
                absoluteYawRadians = Math.Abs(fit.SignedYawRadians),
                horizontalTravelMetres = fit.TravelMetres,
                radiusFromArcAndYawMetres = fit.RadiusFromArcAndYawMetres,
                fitCentreX = fit.CentreX,
                fitCentreZ = fit.CentreZ,
                fittedRootRadiusMetres = fit.FittedRadiusMetres,
                fitRmsResidualMetres = fit.RmsResidualMetres,
                fitMaximumResidualMetres = fit.MaximumResidualMetres,
                meanAppliedSteeringDegrees = measured.Average(row => row.appliedSteeringDegrees),
                maximumLateralSlipMetresPerSecond = measured.Max(row => row.lateralSlipMetresPerSecond),
                meanTyreRadiiMetres = fit.CopyMeanTyreRadiiMetres(),
                firstMeasuredTick = measured[0].tick,
                lastMeasuredTick = measured[measured.Count - 1].tick,
                circleFitWithinDiagnosticResiduals = fit.RmsResidualMetres <= DiagnosticRmsLimitMetres && fit.MaximumResidualMetres <= DiagnosticMaximumResidualMetres && Math.Abs(fit.FittedRadiusMetres - fit.RadiusFromArcAndYawMetres) <= DiagnosticEstimatorAgreementMetres
            };
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Need(image.width >= 1280 && image.height >= 720, "Readable native framebuffer");
                string path = Path.Combine(output, name + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                captures.Add(path);
            }
            finally
            {
                Destroy(image);
            }
        }

        private void OnGUI()
        {
            if (!observing || done)
                return;
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 20,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            GUI.Box(new Rect(16, 16, Math.Min(Screen.width - 32, 1000), 100), "ACTUAL STOCK TURNING CALIBRATION · PRIVATE FLAT SURFACE\n" + phase + " | actor" + activeCase + " | requested" + (activeCase >= 0 ? targetSpeeds[activeCase].ToString("F1") : "-") + "m/s | ordinary pedal/steering inputs\n" + "No geographic road, scenery, lane or absolute minimum-radius admission", style);
        }

        private void Finish(bool pass, string failure)
        {
            if (done || finishing)
                return;
            finishing = true;
            done = true;
            observing = false;
            try
            {
                trace?.Flush();
                trace?.Dispose();
                trace = null;
                pass = pass && results.Count == 4 && runtimeErrorCount == 0 && runtimeErrorMessages.Count == 0;
                report.status = pass ? "PASS_ACTUAL_FOUR_LOW_SPEED_TURNS_ONLY" : "FAIL";
                report.error = failure;
                report.recipe = Recipe;
                report.scope = "Actual stock dry1.5/2.5m/s steady turns; not absolute minimum/real-road/lane/fullworld admission";
                report.bindingSHA256 = bindingHash;
                report.appBuildGUID = Application.buildGUID;
                report.sourceSHA256 = binding?.sourceSHA256;
                report.surfaceRecipeSHA256 = SurfaceDigest;
                report.bootstrapSuppressed = GameSession.SuppressAutomaticLaunch;
                report.noGameSession = !FindAnyObjectByType<GameSession>();
                report.noWorldData = true;
                report.dryDefaultStock = cars.All(car => car && car.Metrics.Model == 0 && car.Grip == 1 && car.RoadSurface && car.SurfaceWetness == 0);
                report.earlyCameraRendered = earlyCamera && earlyCamera.ClearingRenderCount > 0;
                report.driveCameraRendered = earlyCamera && earlyCamera.DriveCameraRendered;
                report.earlyCameraRetired = earlyCamera && earlyCamera.RetiredAfterDriveRender;
                report.runtimeErrors = runtimeErrorCount;
                report.rawRows = rowCount;
                report.cases = results.ToArray();
                report.factoryShapes = shapes;
                report.captures = captures.ToArray();
                report.errors = runtimeErrorMessages.Take(16).ToArray();
                if (outputOwned)
                {
                    report.telemetrySHA256 = Hash(Path.Combine(output, "fixed-telemetry.jsonl"));
                    report.factoryShapesSHA256 = Hash(Path.Combine(output, "factory-shapes.json"));
                    File.WriteAllText(Path.Combine(output, "receipt.json"), JsonUtility.ToJson(report, true));
                }
            }
            finally
            {
                foreach (var car in cars)
                    if (car)
                        Commands(car, 0, 1, 0, 1);
                DisposeOwned();
                Application.Quit(report.status == "PASS_ACTUAL_FOUR_LOW_SPEED_TURNS_ONLY" ? 0 : 1);
            }
        }

        /// <summary>Idempotent cleanup owns only this private QA stage/camera/material/clock.</summary>
        private void DisposeOwned()
        {
            if (resourcesDisposed)
                return;
            resourcesDisposed = true;
            trace?.Dispose();
            trace = null;
            if (clockOwned)
            {
                Time.timeScale = priorTimeScale;
                clockOwned = false;
            }

            if (ambientOwned)
            {
                RenderSettings.ambientLight = priorAmbient;
                ambientOwned = false;
            }

            if (view)
                view.enabled = false;
            if (earlyCamera)
            {
                earlyCamera.DisposeOwned();
                earlyCamera = null;
            }

            if (stage)
            {
                stage.SetActive(false);
                Destroy(stage);
                stage = null;
            }

            if (floorMaterial)
            {
                Destroy(floorMaterial);
                floorMaterial = null;
            }

            Application.logMessageReceived -= LogError;
        }

        private void OnDestroy()
        {
            DisposeOwned();
        }

        [Serializable]
        private sealed class ShapeList
        {
            public FactoryShape[] values;
        }
    }
}
