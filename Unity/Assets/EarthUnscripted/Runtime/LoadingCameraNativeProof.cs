using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EarthUnscripted
{
    /// <summary>
    /// Opt-in isolated QA observer for actual GameSession boot and LocationLoading
    /// retry. It never invokes CameraRendered/Camera.Render, assigns readiness,
    /// substitutes factories or drives a car. Rendering must happen in real URP.
    /// The separate QA product and external owned-process/privacy guard are mandatory.
    /// </summary>
    public sealed class LoadingCameraNativeProof : MonoBehaviour
    {
        public const string Recipe = "ProductionLoadingCamera/ActualBootCancelRetry/1";
        const string Flag = "-earthUnscriptedLoadingCameraProof";
        const string Product = "LoadingCameraQA";
        const string Identifier = "com.earthunscripted.qa.loadingcamera";
        const double TotalTimeoutSeconds = 1200;
        const double CaseTimeoutSeconds = 300;
        const int TrafficCancellationCount = 5;
        const int ExpectedOrdinaryTrafficCount = 24; // Independent oracle for the current production factory.
        const int UnsupportedProfileSchemaVersion = PlayerProfile.CurrentVersion + 1;
        const int PoliceCancellationCount = 1;
        const int MinimumImageWidth = 1280;
        const int MinimumImageHeight = 720;
        const int MaximumEvents = 4096;
        static bool requested, ownsOutput;
        static string output, bindingPath, bindingSHA, isolationError;
        static LoadingCameraNativeProof instance;
        static Binding binding;

        [Serializable] public sealed class FilePin { public string path, sha256; public long bytes; }
        [Serializable] public sealed class Binding
        {
            public string recipe, buildGUID, executableSHA256, timeManagerSHA256, dynamicsSHA256;
            public FilePin[] sourceFiles, appFiles;
        }
        [Serializable] sealed class CaseResult
        {
            public string name, status, bootStatus;
            public int clearingRenders, driveRenders, firstClearFrame = -1, firstDriveFrame = -1;
            public int generationBeforeCancel, generationAfterCancel;
            public bool clearerBeforeFirstYield, ready, failed, handoff, oldSessionDestroyed;
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
            public string scope = "Actual default-world GameSession boot/render lifecycle only; no physics, scenery or full-area acceptance.";
            public bool isolatedProfileUnchanged, refusedProfileUnchanged, noOrphanCameras,
                noOrphanListeners, noOrphanLoadingOwners, bootstrapSuppressed;
            public int runtimeErrors, sceneReloads, totalClearRenders, totalDriveRenders;
            public string[] captures;
            public FilePin[] captureFiles;
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
        string validProfile, validProfileSHA, refusedProfile, refusedProfileSHA, nextCase;
        bool retryExpected, done;
        int runtimeErrors, eventRows;
        double deadline;

        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("LOADING CAMERA QA: " + reason);
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
                "invalid-loading-camera-" + Guid.NewGuid().ToString("N") + ".json");
            RegionCache.OverrideFolder = PlayerProfile.StorageOverride + ".regions";
            try
            {
                Require(!Application.isEditor && Application.companyName == "EarthUnscripted Verification" &&
                    Application.productName == Product && Application.identifier == Identifier,
                    "Separate native QA identity required");
                output = Path.GetFullPath(Argument(arguments, Flag));
                Require(output.StartsWith("/private/tmp/rr019-loading-camera-", StringComparison.Ordinal) &&
                    !Directory.Exists(output), "Fresh private QA output only");
                bindingPath = Path.GetFullPath(Argument(arguments, "-earthUnscriptedLoadingCameraBinding"));
                bindingSHA = Argument(arguments, "-earthUnscriptedLoadingCameraBindingSHA");
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
            var root = new GameObject("Isolated actual loading-camera observer");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<LoadingCameraNativeProof>();
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
        IEnumerator Run()
        {
            Require(isolationError == null, isolationError);
            Require(ownsOutput && GameSession.SuppressAutomaticLaunch && All<GameSession>().Length == 0,
                "Isolation before any actual session bootstrap");
            validProfile = PlayerProfile.StoragePath;
            var profile = new PlayerProfile
            {
                region = "", quality = 0, volume = 0, rumble = 0,
                wheelEnabled = false, wheelForceEnabled = false,
                livingScenery = false, detailedScenery = false, weatherMode = 0, clockMode = 0
            };
            File.WriteAllText(validProfile, JsonUtility.ToJson(profile));
            validProfileSHA = Hash(validProfile);
            CreateActualSession("traffic-cancel");
            yield return WaitUntilActual(() => session.BootTrafficPrepared >= TrafficCancellationCount ||
                session.BootReady || session.BootFailed, "Actual staged regular traffic");
            Require(!session.BootReady && session.BootPatrolPrepared == 0, "Real traffic cancellation checkpoint");
            yield return Capture("traffic-preparing");
            yield return CancelActualPreparation();

            RetryActualLocation("police-cancel");
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootPatrolPrepared >= PoliceCancellationCount || session.BootReady || session.BootFailed),
                "Actual reload and staged patrol");
            Require(!session.BootReady && session.BootTrafficPrepared == ExpectedOrdinaryTrafficCount, "Real patrol cancellation checkpoint");
            yield return CancelActualPreparation();

            RetryActualLocation("successful-reload");
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootReady || session.BootFailed), "Actual successful retry");
            Require(session.BootReady && !session.BootFailed, "Real default world reaches Ready");
            yield return WaitUntilActual(() => session.BootDriveCameraRenderConfirmed && current.driveRenders > 0,
                "Actual current driving-camera render handoff");
            yield return TwoFrames();
            Require(!session.BootClearingCameraPresent && !currentClear && All<Camera>().Length == 1 &&
                All<Camera>().Count(camera => camera.isActiveAndEnabled && camera.CompareTag("MainCamera")) == 1 &&
                All<AudioListener>().Count(listener => listener.isActiveAndEnabled) == 1,
                "Real handoff leaves one driving camera/listener, no orphan clearer");
            Require(current.clearingRenders > 0 && current.firstClearFrame <= current.firstDriveFrame,
                "Real clear preceded actual drive render");
            current.ready = true; current.handoff = true; current.status = "PASS_ACTUAL_HANDOFF";
            Record("actual-current-render-handoff-confirmed");
            yield return Capture("successful-normal-menu");

            RetryActualLocation("destroy-during-prepare");
            yield return WaitUntilActual(() => !retryExpected && session && session != previousSession &&
                (session.BootTrafficPrepared >= TrafficCancellationCount || session.BootReady || session.BootFailed),
                "Actual destruction preparation checkpoint");
            Require(!session.BootReady && !session.BootFailed && current.clearingRenders > 0,
                "Destroy real owner during staged preparation");
            Destroy(session.gameObject);
            yield return TwoFrames();
            Require(!session && All<Camera>().Length == 0 && All<AudioListener>().Length == 0 &&
                All<SessionBootClearingCamera>().Length == 0 && All<VehicleController>().Length == 0,
                "Actual owner destruction leaves no clearer/actors/listener or recreated child");
            current.status = "PASS_ACTUAL_OWNER_DESTRUCTION";

            refusedProfile = Path.Combine(output, "refused-future-profile.json");
            File.WriteAllText(refusedProfile, "{\"version\":" + UnsupportedProfileSchemaVersion + "}");
            refusedProfileSHA = Hash(refusedProfile);
            PlayerProfile.StorageOverride = refusedProfile;
            RetryActualLocation("profile-read-refusal");
            yield return WaitUntilActual(() => !retryExpected && session && session.BootFailed,
                "Actual safe refusal of isolated future profile");
            yield return TwoFrames();
            Require(session.Profile.LoadBlocked && !session.BootReady && session.BootClearingCameraPresent &&
                current.clearingRenders > 0 && All<Camera>().Length == 1 && All<AudioListener>().Length == 0 &&
                All<VehicleController>().Length == 0, "Actual read refusal retains rendered existing error UI");
            Require(Hash(refusedProfile) == refusedProfileSHA, "Refused isolated file was not rewritten");
            current.failed = true; current.bootStatus = session.BootStatus;
            current.status = "PASS_ACTUAL_PROFILE_REFUSAL";
            yield return Capture("profile-read-refusal-error-ui");
            Destroy(session.gameObject);
            yield return TwoFrames();
            report.noOrphanCameras = All<Camera>().Length == 0;
            report.noOrphanListeners = All<AudioListener>().Length == 0;
            report.noOrphanLoadingOwners = All<SessionBootClearingCamera>().Length == 0;
            Require(report.noOrphanCameras && report.noOrphanListeners && report.noOrphanLoadingOwners,
                "Final actual owner cleanup complete");
            Finish(true, null);
        }
        void Finish(bool pass, string error)
        {
            if (done) return;
            done = true;
            RenderPipelineManager.endCameraRendering -= Rendered;
            SceneManager.sceneLoaded -= Reloaded;
            events?.Flush(); events?.Dispose(); events = null;
            report.status = pass ? "PASS_ACTUAL_LOADING_LIFECYCLE_ONLY" : "FAIL";
            report.error = error; report.runtimeErrors = runtimeErrors;
            report.buildGUID = Application.buildGUID; report.bindingSHA256 = bindingSHA;
            report.eventsSHA256 = ownsOutput ? Hash(Path.Combine(output, "render-events.jsonl")) : "missing";
            report.isolatedProfileUnchanged = validProfile != null && Hash(validProfile) == validProfileSHA;
            report.refusedProfileUnchanged = refusedProfile != null && Hash(refusedProfile) == refusedProfileSHA;
            report.bootstrapSuppressed = GameSession.SuppressAutomaticLaunch;
            report.cases = cases.ToArray(); report.captures = captures.ToArray();
            report.captureFiles = capturePins.ToArray();
            if (runtimeErrors != 0 || !report.isolatedProfileUnchanged || !report.refusedProfileUnchanged ||
                !report.bootstrapSuppressed) report.status = "FAIL";
            if (ownsOutput) File.WriteAllText(Path.Combine(output, "receipt.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.status == "PASS_ACTUAL_LOADING_LIFECYCLE_ONLY" ? 0 : 1);
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
