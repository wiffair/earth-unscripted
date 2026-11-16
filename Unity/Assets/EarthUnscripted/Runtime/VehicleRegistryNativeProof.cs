using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using EarthUnscripted.Core;
using EarthUnscripted.VehiclePopulation;

namespace EarthUnscripted
{
    /// <summary>
    /// Opt-in actual-default-boot vehicle registry observer. Actual saved profile,
    /// world, actors and Ready originate only from normal GameSession factories.
    /// Garage/race changes use legitimate gameplay APIs, never injected fields.
    /// It never invokes a render callback, fabricates context or drives physics.
    /// Rendering must happen in real URP.
    /// The separate QA product and external owned-process/privacy guard are mandatory.
    /// </summary>
    public sealed class VehicleRegistryNativeProof : MonoBehaviour
    {
        public const string Recipe = "ActualVehicleRegistry/NormalBootGarageRace/1";
        const string Flag = "-earthUnscriptedVehicleRegistryProof";
        const string Product = "VehicleRegistryQA";
        const string Identifier = "com.earthunscripted.qa.vehicleregistry";
        const double TotalTimeoutSeconds = 1200;
        const double CaseTimeoutSeconds = 300;
        const int TrafficCancellationCount = 5;
        const int ExpectedOrdinaryTrafficCount = 24; // Independent oracle for the current production factory.
        const int SeedCredits = 40000; // Isolated saved setup; enough for the real 28000-credit car transaction.
        const int SingleSeatModel = 3;
        const int ExpectedDefaultRoots = 28;
        const int ExpectedRaceOpponents = 3;
        const int MaximumObservedHandles = 256;
        const int PoliceCancellationCount = 1;
        const int MinimumImageWidth = 1280;
        const int MinimumImageHeight = 720;
        const int MaximumEvents = 4096;
        static bool requested, ownsOutput;
        static string output, bindingPath, bindingSHA, isolationError;
        static VehicleRegistryNativeProof instance;
        static Binding binding;

        [Serializable] public sealed class FilePin { public string path, sha256; public long bytes; }
        [Serializable] public sealed class Binding
        {
            public string recipe, buildGUID, executableSHA256, timeManagerSHA256, dynamicsSHA256;
            public FilePin[] sourceFiles, appFiles;
        }
        [Serializable] sealed class CaseResult
        {
            public string name, status, bootStatus, domainReason;
            public int clearingRenders, driveRenders, firstClearFrame = -1, firstDriveFrame = -1;
            public int generationBeforeCancel, generationAfterCancel;
            public bool clearerBeforeFirstYield, ready, failed, handoff, oldSessionDestroyed;
            public bool bootSealed, domainInitiallyComplete, captureRefusedWithoutContext, ownerRevoked,
                handlesNotReused, patrolDeduplicated, garageTransactionsSaved, shapeInvalidated,
                poseInvalidated, rootHandleStable, raceOpponentsRetired;
            public int roots, players, traffic, patrols, raceOpponents, shapeBefore, shapeAfter, poseBefore, poseAfter;
            public int circuitBootRoots, rootsAfterRaceCancel, activeBoxesAfterUpgrade, modelAfterUpgrade;
            public MemberRecord[] members;
        }
        [Serializable] sealed class MemberRecord
        {
            public string slot, handle, role, name;
            public int model, activeBoxes, shapeRevision, poseRevision;
        }
        [Serializable] sealed class EventRow
        {
            public string kind, caseName, cameraName, bootStatus;
            public int frame, traffic, police;
            public bool ready, failed, clearPresent, handoff;
            public double realtimeSeconds;
        }
        [Serializable] sealed class Report
        {
            public string recipe = Recipe, status, error, buildGUID, bindingSHA256, eventsSHA256;
            public string scope = "Actual normal default/circuit boot, registry membership/revocation and legitimate garage/race APIs only; typed-context capture, physics and full-world admission excluded.";
            public bool isolatedProfileValid, expectedGarageSaved, noOrphanCameras,
                noOrphanListeners, noOrphanLoadingOwners, bootstrapSuppressed;
            public int initialCredits, expectedCredits, savedCredits;
            public bool allObservedCapturesUnbound;
            public int runtimeErrors, sceneReloads, totalClearRenders, totalDriveRenders;
            public string[] captures;
            public FilePin[] captureFiles;
            public FilePin savedProfile;
            public CaseResult[] cases;
        }

        readonly List<CaseResult> cases = new List<CaseResult>();
        readonly List<string> captures = new List<string>();
        readonly List<FilePin> capturePins = new List<FilePin>();
        readonly Report report = new Report();
        GameSession session;
        CaseResult current;
        Camera currentClear;
        GameSession previousSession;
        StreamWriter events;
        string validProfile, nextCase;
        int expectedCredits;
        SessionVehiclePopulation previousPopulation, lastPopulation;
        readonly Dictionary<string, SessionVehiclePopulation> handleOwners =
            new Dictionary<string, SessionVehiclePopulation>(StringComparer.Ordinal);
        bool retryExpected, done;
        int runtimeErrors, eventRows;
        double deadline;

        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("VEHICLE REGISTRY QA: " + reason);
        }
        static string Hash(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static string Argument(string[] arguments, string flag)
        {
            int index = Array.IndexOf(arguments, flag);
            if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException(flag);
            return arguments[index + 1];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void IsolateBeforeBootstrap()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, Flag) < 0) return;
            requested = true;
            // Set before validating arguments: even a refused test may never launch
            // the ordinary production career via GameSession's scene callback.
            GameSession.SuppressAutomaticLaunch = true;
            PlayerProfile.StorageOverride = Path.Combine(Application.temporaryCachePath,
                "invalid-registry-" + Guid.NewGuid().ToString("N") + ".json");
            RegionCache.OverrideFolder = PlayerProfile.StorageOverride + ".regions";
            try
            {
                Require(!Application.isEditor && Application.companyName == "EarthUnscripted Verification" &&
                    Application.productName == Product && Application.identifier == Identifier,
                    "Separate native QA identity required");
                output = Path.GetFullPath(Argument(arguments, Flag));
                Require(output.StartsWith("/private/tmp/rr019-vehicle-registry-", StringComparison.Ordinal) &&
                    !Directory.Exists(output), "Fresh private QA output only");
                bindingPath = Path.GetFullPath(Argument(arguments, "-earthUnscriptedVehicleRegistryBinding"));
                bindingSHA = Argument(arguments, "-earthUnscriptedVehicleRegistryBindingSHA");
                Require(new FileInfo(bindingPath).Length <= 1024 * 1024 && Hash(bindingPath) == bindingSHA,
                    "Pinned build binding required");
                binding = JsonUtility.FromJson<Binding>(File.ReadAllText(bindingPath));
                Require(binding != null && binding.recipe == Recipe && binding.buildGUID == Application.buildGUID,
                    "Actual isolated build identity");
                Directory.CreateDirectory(output);
                ownsOutput = true;
                PlayerProfile.StorageOverride = Path.Combine(output, "isolated-profile.json");
                RegionCache.OverrideFolder = Path.Combine(output, "isolated-regions");
                CountryUpdateService.OverrideFolder = Path.Combine(output, "isolated-updates");
                CountryUpdateService.AllowVerification = false;
                Application.runInBackground = true;
            }
            catch (Exception exception) { isolationError = exception.ToString(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!requested || instance) return;
            var root = new GameObject("Isolated actual vehicle-registry observer");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<VehicleRegistryNativeProof>();
        }

        void Awake()
        {
            deadline = Time.realtimeSinceStartupAsDouble + TotalTimeoutSeconds;
            RenderPipelineManager.endCameraRendering += Rendered;
            SceneManager.sceneLoaded += Reloaded;
            Application.logMessageReceived += Logged;
            if (ownsOutput)
                events = new StreamWriter(Path.Combine(output, "render-events.jsonl"), false);
        }
        void Logged(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors++;
        }
        void Update()
        {
            if (!done && Time.realtimeSinceStartupAsDouble > deadline)
                Finish(false, "Actual boot lifecycle watchdog");
        }
        void Record(string kind, Camera camera = null)
        {
            if (events == null || current == null) return;
            Require(eventRows++ < MaximumEvents, "Bounded lifecycle event count");
            events.WriteLine(JsonUtility.ToJson(new EventRow
            {
                kind = kind, caseName = current.name, cameraName = camera ? camera.name : "",
                frame = Time.frameCount, realtimeSeconds = Time.realtimeSinceStartupAsDouble,
                ready = session && session.BootReady, failed = session && session.BootFailed,
                clearPresent = session && session.BootClearingCameraPresent,
                handoff = session && session.BootDriveCameraRenderConfirmed,
                traffic = session ? session.BootTrafficPrepared : 0,
                police = session ? session.BootPatrolPrepared : 0,
                bootStatus = session ? session.BootStatus : ""
            }));
            events.Flush();
        }
        void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (done || current == null || !session) return;
            if (camera == currentClear)
            {
                if (current.firstClearFrame < 0) current.firstClearFrame = Time.frameCount;
                current.clearingRenders++; report.totalClearRenders++;
                // Record first clear and sparse follow-up, keeping traces bounded.
                if (current.clearingRenders <= 2 || current.clearingRenders % 120 == 0)
                    Record("actual-clearing-render", camera);
            }
            if (session.View && camera == session.View.GetComponent<Camera>())
            {
                if (current.firstDriveFrame < 0) current.firstDriveFrame = Time.frameCount;
                current.driveRenders++; report.totalDriveRenders++;
                if (current.driveRenders <= 2) Record("actual-drive-render", camera);
            }
        }
        static T[] All<T>() where T : UnityEngine.Object =>
            FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        void CreateActualSession(string caseName)
        {
            Require(All<GameSession>().Length == 0, "No duplicate normal session");
            current = new CaseResult { name = caseName, status = "RUNNING" };
            cases.Add(current);
            session = new GameObject("Actual isolated GameSession " + caseName).AddComponent<GameSession>();
            // AddComponent has run the actual Awake synchronously. No test assigns
            // BootReady, camera state, world, profile or private factory fields.
            currentClear = session.GetComponentsInChildren<Camera>(true).Single(camera =>
                camera.name.StartsWith("Loading clear only;", StringComparison.Ordinal));
            current.clearerBeforeFirstYield = session.BootClearingCameraPresent;
            Require(current.clearerBeforeFirstYield && !currentClear.CompareTag("MainCamera") &&
                !currentClear.GetComponent<AudioListener>() && currentClear.cullingMask == 0,
                "Actual early clearer exists before first observer yield; no audio/main-camera/world draw");
            Record("actual-session-created-before-yield");
        }
        void Reloaded(Scene scene, LoadSceneMode mode)
        {
            if (!retryExpected || done) return;
            retryExpected = false;
            try
            {
                Require(!previousSession, "Prior session really destroyed by scene reload");
                report.sceneReloads++;
                CreateActualSession(nextCase);
                current.oldSessionDestroyed = true;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }
        void RetryActualLocation(string caseName)
        {
            previousSession = session;
            previousPopulation = session && session.ActualVehicles != null ? session.ActualVehicles : lastPopulation;
            nextCase = caseName;
            retryExpected = true;
            Record("actual-location-retry-request");
            LocationLoading.Retry();
        }
        IEnumerator WaitUntilActual(Func<bool> predicate, string description)
        {
            double until = Time.realtimeSinceStartupAsDouble + CaseTimeoutSeconds;
            while (!predicate() && !done)
            {
                Require(Time.realtimeSinceStartupAsDouble < until, description + " timed out");
                yield return null;
            }
            Require(!done, "Proof ended before " + description);
        }
        IEnumerator TwoFrames() { yield return null; yield return null; }
        IEnumerator Capture(string label)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Require(image.width >= MinimumImageWidth && image.height >= MinimumImageHeight,
                    "Actual native framebuffer dimensions");
                string name = label + ".png";
                File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
                captures.Add(name);
                capturePins.Add(new FilePin { path = name, bytes = new FileInfo(Path.Combine(output, name)).Length,
                    sha256 = Hash(Path.Combine(output, name)) });
            }
            finally { Destroy(image); }
        }
        static int Generation(GameSession actual) => (int)typeof(GameSession)
            .GetField("bootGeneration", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).GetValue(actual);
        IEnumerator CancelActualPreparation()
        {
            Require(session && !session.BootReady && !session.BootFailed && current.clearingRenders > 0,
                "Real clear rendered while actual boot was still preparing");
            var retiredPopulation = session.ActualVehicles;
            Require(retiredPopulation != null, "Actual registry exists during actor preparation");
            ObserveRegistry(false);
            var retiredRoots = retiredPopulation.Domain.CopyMembers().Select(member => member.Root as GameObject).ToArray();
            current.generationBeforeCancel = Generation(session);
            session.CancelBoot();
            current.generationAfterCancel = Generation(session);
            Require(current.generationAfterCancel > current.generationBeforeCancel && session.BootFailed &&
                !session.BootReady, "Actual public CancelBoot revokes generation");
            int rendersBefore = current.clearingRenders;
            yield return TwoFrames();
            Require(!session.World && !session.Car && All<VehicleController>().Length == 0 &&
                All<TrafficCar>().Length == 0 && All<PoliceDriver>().Length == 0,
                "Actual cancellation removes private world/actor roots");
            Require(session.BootClearingCameraPresent && current.clearingRenders > rendersBefore &&
                All<Camera>().Length == 1 && All<AudioListener>().Length == 0,
                "One actual clearer remains rendering unchanged cancellation/error UI");
            Require(session.ActualVehicles == null && !retiredPopulation.TryCapture(out _, out var revokedReason) &&
                revokedReason == "WorldRevoked" && !retiredPopulation.Domain.IsComplete && retiredRoots.All(root => !root),
                "Actual cancellation revokes old owner and destroys all its physical roots");
            current.ownerRevoked = true;
            current.failed = session.BootFailed; current.bootStatus = session.BootStatus;
            current.status = "PASS_ACTUAL_CANCEL";
            Record("actual-cancelled-generation-remains-cleared");
            yield return Capture(current.name + "-error-ui");
        }

        IEnumerator Start() { yield return Guard(Run()); }
        IEnumerator Guard(IEnumerator work)
        {
            while (!done)
            {
                bool more = false; object next = null; Exception failure = null;
                try { more = work.MoveNext(); if (more) next = work.Current; }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { Finish(false, failure.ToString()); yield break; }
                if (!more) yield break;
                if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
            }
        }
        static bool BootSealed(ActualVehicleRosterDomain domain) => (bool)typeof(ActualVehicleRosterDomain)
            .GetField("bootSealed", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).GetValue(domain);

        ActualVehicleRosterDomain.Member PlayerMember() => session.ActualVehicles.Domain.CopyMembers()
            .Single(member => member.VehicleRole == ActualVehicleRosterDomain.Role.Player);

        void ObserveRegistry(bool requireDefaultComplete)
        {
            var population = session.ActualVehicles;
            Require(population != null, "Actual session privately published its population service");
            Require(previousPopulation == null || !ReferenceEquals(population, previousPopulation),
                "New boot uses a different actual population owner");
            lastPopulation = population;
            var domain = population.Domain;
            var members = domain.CopyMembers();
            current.domainReason = domain.UnboundReason;
            Require(current.domainReason == "" || current.domainReason == "BootRosterUnsealed" ||
                current.domainReason == "FreshActualSampleRequired", "Actual observed registry has no poison/unknown factory gap");
            Require(members.Select(member => member.Root).Distinct().Count() == members.Length,
                "Actual registry roots are unique");
            foreach (var member in members)
            {
                Require(member.Root is GameObject root && root && root.GetComponent<Rigidbody>() &&
                    root.GetComponent<CarVisual>(), "Actual member has its live real body/model");
                if (handleOwners.TryGetValue(member.RuntimeHandle, out var prior))
                    Require(ReferenceEquals(prior, population), "Runtime handle not reused across owners");
                else
                {
                    Require(handleOwners.Count < MaximumObservedHandles, "Bounded actual handle observations");
                    handleOwners.Add(member.RuntimeHandle, population);
                }
            }
            current.handlesNotReused = true;
            current.roots = members.Length;
            current.players = members.Count(member => member.VehicleRole == ActualVehicleRosterDomain.Role.Player);
            current.traffic = members.Count(member => member.VehicleRole == ActualVehicleRosterDomain.Role.Traffic);
            current.patrols = members.Count(member => member.VehicleRole == ActualVehicleRosterDomain.Role.Police);
            current.raceOpponents = members.Count(member => member.VehicleRole == ActualVehicleRosterDomain.Role.RaceOpponent);
            current.bootSealed = BootSealed(domain);
            current.domainInitiallyComplete = domain.IsComplete;
            current.members = members.Select(member =>
            {
                var root = (GameObject)member.Root;
                return new MemberRecord { slot = member.Slot, handle = member.RuntimeHandle,
                    role = member.VehicleRole.ToString(), name = root.name,
                    model = root.GetComponent<CarVisual>().Model,
                    activeBoxes = root.GetComponentsInChildren<BoxCollider>(true).Count(box =>
                        box.enabled && box.gameObject.activeInHierarchy),
                    shapeRevision = member.ShapeRevision, poseRevision = member.PoseRevision };
            }).ToArray();
            Require(!population.TryCapture(out var snapshot, out var reason) && snapshot == null &&
                reason == "ActualGroundTownBindingUnavailable" && !population.MatchesTownPublication800(null),
                "No typed physical Town context means UNBOUND, not an empty fake complete snapshot");
            current.captureRefusedWithoutContext = true;
            if (requireDefaultComplete)
            {
                Require(members.Length == ExpectedDefaultRoots && current.players == 1 &&
                    current.traffic == ExpectedOrdinaryTrafficCount && current.patrols == 3 &&
                    current.raceOpponents == 0 && current.bootSealed && current.domainInitiallyComplete,
                    "Actual default boot has sealed complete player+24traffic+3patrol roots");
                var patrols = members.Where(member => member.VehicleRole == ActualVehicleRosterDomain.Role.Police).ToArray();
                Require(patrols.All(member => ((GameObject)member.Root).GetComponent<TrafficCar>() &&
                    ((GameObject)member.Root).GetComponent<VehicleController>() &&
                    ((GameObject)member.Root).GetComponent<PoliceDriver>()),
                    "Actual patrol components are deduplicated into three roots");
                current.patrolDeduplicated = true;
            }
            Record("actual-registry-observed");
        }

        void PrepareExpectedReload(string caseName)
        {
            previousSession = session;
            previousPopulation = session.ActualVehicles;
            nextCase = caseName;
            retryExpected = true;
        }

        IEnumerator Run()
        {
            Require(isolationError == null, isolationError);
            Require(ownsOutput && GameSession.SuppressAutomaticLaunch && All<GameSession>().Length == 0,
                "Separate QA identity/storage before any actual session bootstrap");
            validProfile = PlayerProfile.StoragePath;
            var profile = new PlayerProfile
            {
                region = "", credits = SeedCredits, quality = 0, volume = 0, rumble = 0,
                wheelEnabled = false, wheelForceEnabled = false,
                livingScenery = false, detailedScenery = false, weatherMode = 0, clockMode = 0
            };
            File.WriteAllText(validProfile, JsonUtility.ToJson(profile));
            expectedCredits = SeedCredits;
            report.initialCredits = SeedCredits;
            CreateActualSession("traffic-cancel");
            yield return WaitUntilActual(() => session.BootTrafficPrepared >= TrafficCancellationCount ||
                session.BootReady || session.BootFailed, "Actual staged regular traffic");
            Require(!session.BootReady && session.BootPatrolPrepared == 0, "Actual traffic cancellation checkpoint");
            yield return CancelActualPreparation();

            RetryActualLocation("police-cancel");
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootPatrolPrepared >= PoliceCancellationCount || session.BootReady || session.BootFailed),
                "Actual staged police after real retry/reload");
            Require(!session.BootReady && session.BootTrafficPrepared == ExpectedOrdinaryTrafficCount,
                "Actual patrol cancellation checkpoint");
            yield return CancelActualPreparation();

            RetryActualLocation("default-ready-and-garage");
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootReady || session.BootFailed), "Actual default-world Ready");
            Require(session.BootReady && !session.BootFailed && !session.World.IsCircuit,
                "Actual loaded profile/world/default boot reaches Ready");
            yield return WaitUntilActual(() => session.BootDriveCameraRenderConfirmed && current.driveRenders > 0,
                "Actual normal View render handoff");
            yield return WaitUntilActual(() => !LocationLoading.Busy, "Actual location loader completes");
            ObserveRegistry(true);
            var player = PlayerMember();
            string playerHandle = player.RuntimeHandle;
            var bodyRoot = session.Car.gameObject;
            string stockId = session.Profile.garage.Active.id;
            current.poseBefore = player.PoseRevision;
            session.TowToGarage(); // Legitimate gameplay API, not a body/world-field assignment.
            Require(session.AtGarage && session.Car.AbsoluteSpeed <= 1 && player.PoseRevision > current.poseBefore,
                "Actual garage tow invalidates pose version");
            current.poseAfter = player.PoseRevision; current.poseInvalidated = true;
            current.shapeBefore = player.ShapeRevision;
            Require(session.GarageChange((garage, credits) => GarageRules.Buy(garage, credits, SingleSeatModel, false)),
                "Actual saved GarageChange/GarageRules purchase succeeds");
            expectedCredits -= GarageRules.Catalogue[SingleSeatModel].Price;
            string raceId = session.Profile.garage.Active.id;
            Require(ReferenceEquals(bodyRoot, session.Car.gameObject) && PlayerMember().RuntimeHandle == playerHandle &&
                session.Car.Metrics.Model == SingleSeatModel && session.Car.Visual.Model == SingleSeatModel &&
                session.Car.GetComponentsInChildren<BoxCollider>(true).Count(box => box.enabled &&
                    box.gameObject.activeInHierarchy) == 5 && player.ShapeRevision > current.shapeBefore && player.ShapePending,
                "Actual F1 rebuild keeps actor incarnation and changes its five-box physical shape");
            Require(session.GarageChange((garage, credits) => GarageRules.Select(garage, credits, stockId)) &&
                session.Car.Metrics.Model == 0 && session.Car.GetComponentsInChildren<BoxCollider>(true)
                    .Count(box => box.enabled && box.gameObject.activeInHierarchy) == 1,
                "Actual garage selection returns to stock collider layout");
            Require(session.GarageChange((garage, credits) => GarageRules.Select(garage, credits, raceId)),
                "Actual garage selection reuses owned single-seat car");
            int upgradeCost = GarageRules.UpgradePrice(session.Profile.garage.Active, 0);
            Require(session.GarageChange((garage, credits) => GarageRules.Upgrade(garage, credits, 0)),
                "Actual saved engine upgrade transaction");
            expectedCredits -= upgradeCost;
            Require(session.Economy.Credits == expectedCredits && session.Profile.garage.Active.engine == 1 &&
                session.Car.PowerFactor.Equals(GarageRules.Power(session.Profile.garage.Active)),
                "Actual economy and fitted controller match legitimate upgrade");
            current.shapeAfter = player.ShapeRevision;
            current.activeBoxesAfterUpgrade = session.Car.GetComponentsInChildren<BoxCollider>(true)
                .Count(box => box.enabled && box.gameObject.activeInHierarchy);
            current.modelAfterUpgrade = session.Car.Metrics.Model;
            current.shapeInvalidated = player.ShapePending && !session.ActualVehicles.Domain.IsComplete;
            current.rootHandleStable = PlayerMember().RuntimeHandle == playerHandle &&
                ReferenceEquals(bodyRoot, session.Car.gameObject) && session.ActualVehicles.Domain.MemberCount == ExpectedDefaultRoots;
            current.garageTransactionsSaved = true;
            Require(current.shapeInvalidated && current.rootHandleStable &&
                !session.ActualVehicles.TryCapture(out _, out _), "Mutation cannot publish a stale/fake context capture");
            current.ready = true; current.handoff = true; current.status = "PASS_ACTUAL_DEFAULT_GARAGE";
            Record("actual-garage-transaction-and-shape-invalidation");
            yield return new WaitForSecondsRealtime(.25f);
            yield return Capture("default-garage-single-seat");

            PrepareExpectedReload("actual-circuit-race");
            session.SelectCircuit("silverstone", 0, 1, 1); // Real profile save and normal scene reload.
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootReady || session.BootFailed), "Actual circuit profile/world reload");
            Require(session.BootReady && session.World.IsCircuit, "Actual circuit world prepared by normal factories");
            yield return WaitUntilActual(() => session.BootDriveCameraRenderConfirmed && current.driveRenders > 0 &&
                !LocationLoading.Busy, "Actual circuit View handoff");
            session.SetMenu(true); // Pause via normal UI API; this is enrollment, not race physics acceptance.
            ObserveRegistry(false);
            Require(current.roots == 1 && current.players == 1 && current.bootSealed,
                "Actual circuit boot has only its sealed player root");
            current.circuitBootRoots = current.roots;
            session.Racing.Begin(2);
            var opponents = session.ActualVehicles.Domain.CopyMembers().Where(member =>
                member.VehicleRole == ActualVehicleRosterDomain.Role.RaceOpponent).ToArray();
            Require(opponents.Length == ExpectedRaceOpponents && session.ActualVehicles.Domain.MemberCount == 4 &&
                session.Racing.Entrants == 4, "Actual RaceBegin registers its three real opponents");
            Require(opponents.All(member => ((GameObject)member.Root).GetComponent<VehicleController>() &&
                ((GameObject)member.Root).GetComponent<CarVisual>().Model == SingleSeatModel),
                "Actual opponents use real loaded garage model/components");
            ObserveRegistry(false);
            Record("actual-race-opponents-enrolled");
            yield return Capture("actual-single-seat-race-grid");
            session.Racing.Cancel();
            Require(session.ActualVehicles.Domain.MemberCount == 1 && opponents.All(member =>
                !((GameObject)member.Root).activeInHierarchy), "Actual RaceCancel retires inactive opponent membership immediately");
            yield return TwoFrames();
            Require(opponents.All(member => !(member.Root as GameObject)), "Actual opponents destroyed after deferred lifecycle");
            current.rootsAfterRaceCancel = session.ActualVehicles.Domain.MemberCount;
            current.raceOpponents = ExpectedRaceOpponents;
            current.raceOpponentsRetired = true; current.ready = true; current.handoff = true;
            current.status = "PASS_ACTUAL_CIRCUIT_RACE";

            PrepareExpectedReload("destroy-during-default-prepare");
            Require(session.SelectRegion(""), "Actual public default-area selection succeeds");
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootTrafficPrepared >= TrafficCancellationCount || session.BootReady || session.BootFailed),
                "Actual returned default preparation");
            Require(!session.BootReady && !session.BootFailed, "Destroy only a genuinely pending default session");
            ObserveRegistry(false);
            var destroyedPopulation = session.ActualVehicles;
            var destroyedRoots = destroyedPopulation.Domain.CopyMembers().Select(member => member.Root as GameObject).ToArray();
            Destroy(session.gameObject);
            yield return TwoFrames();
            Require(!session && !destroyedPopulation.TryCapture(out _, out var destroyedReason) &&
                destroyedReason == "WorldRevoked" && !destroyedPopulation.Domain.IsComplete &&
                destroyedRoots.All(root => !root), "Actual session destruction revokes population and pending roots");
            current.ownerRevoked = true; current.status = "PASS_ACTUAL_OWNER_DESTRUCTION";
            report.noOrphanCameras = All<Camera>().Length == 0;
            report.noOrphanListeners = All<AudioListener>().Length == 0;
            report.noOrphanLoadingOwners = All<SessionBootClearingCamera>().Length == 0;
            Require(report.noOrphanCameras && report.noOrphanListeners && report.noOrphanLoadingOwners,
                "Actual final owner cleanup has no orphan loading/render/audio roots");
            Finish(true, null);
        }
        void Finish(bool pass, string error)
        {
            if (done) return;
            done = true;
            RenderPipelineManager.endCameraRendering -= Rendered;
            SceneManager.sceneLoaded -= Reloaded;
            events?.Flush(); events?.Dispose(); events = null;
            report.status = pass ? "PASS_ACTUAL_REGISTRY_DEFAULT_FLOW_ONLY" : "FAIL";
            report.error = error; report.runtimeErrors = runtimeErrors;
            report.buildGUID = Application.buildGUID; report.bindingSHA256 = bindingSHA;
            report.eventsSHA256 = ownsOutput ? Hash(Path.Combine(output, "render-events.jsonl")) : "missing";
            report.bootstrapSuppressed = GameSession.SuppressAutomaticLaunch;
            report.allObservedCapturesUnbound = cases.Count == 5 &&
                cases.All(actualCase => actualCase.captureRefusedWithoutContext);
            report.expectedCredits = expectedCredits;
            if (validProfile != null && File.Exists(validProfile))
            {
                var saved = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(validProfile));
                report.savedCredits = saved.credits;
                report.savedProfile = new FilePin { path = Path.GetFileName(validProfile),
                    bytes = new FileInfo(validProfile).Length, sha256 = Hash(validProfile) };
                report.isolatedProfileValid = saved.version == PlayerProfile.CurrentVersion;
                report.expectedGarageSaved = saved.garage.cars.Count == 2 &&
                    saved.garage.Active.model == SingleSeatModel && saved.garage.Active.engine == 1 &&
                    saved.credits == expectedCredits;
            }
            report.cases = cases.ToArray(); report.captures = captures.ToArray();
            report.captureFiles = capturePins.ToArray();
            if (runtimeErrors != 0 || !report.isolatedProfileValid || !report.expectedGarageSaved ||
                !report.bootstrapSuppressed) report.status = "FAIL";
            if (ownsOutput) File.WriteAllText(Path.Combine(output, "receipt.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.status == "PASS_ACTUAL_REGISTRY_DEFAULT_FLOW_ONLY" ? 0 : 1);
        }
        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= Rendered;
            SceneManager.sceneLoaded -= Reloaded;
            Application.logMessageReceived -= Logged;
            events?.Dispose();
        }
    }
}
