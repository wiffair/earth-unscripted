using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    /// <summary>
    /// Opt-in QA observer of the ACTUAL main GameSession/garage. Seeds only a private
    /// profile before bootstrap. Reflection selects existing menu views/handlers for
    /// render baseline; it never replaces session Profile, World or BootReady and is
    /// explicitly NOT keyboard/controller acceptance. Root owns native visual review.
    /// </summary>
    [DefaultExecutionOrder(31000)]
    public sealed class EngineerHelpNativeProof : MonoBehaviour
    {
        public const string Recipe = "IntegratedMainEngineerHelp/ActualGarageScrollCoverage/2";
        const string Flag = "-earthUnscriptedEngineerHelpProof";
        const double DeadlineSeconds = 600;
        const int MaximumJournalRows = 128;
        // Real unscaled camera smoothing/UI layout settling while the garage pauses physics.
        const float PresentationSettleSeconds = .75f;
        [Serializable]
        public sealed class FilePin
        {
            public string path, sha256;
            public long bytes;
        }

        [Serializable]
        public sealed class Binding
        {
            public string recipe, buildGUID, executableSHA256, timeManagerSHA256, dynamicsSHA256;
            public FilePin[] sourceFiles, appFiles;
        }

        static bool requested, ownsOutput;
        static string output, isolationError, bindingSHA;
        static Binding binding;
        static readonly List<string> errors = new List<string>();
        readonly JObject receipt = new JObject();
        readonly JArray journal = new JArray(), captures = new JArray();
        double deadline;
        bool done;
        readonly EngineerHelpScrollAudit scrollAudit = new EngineerHelpScrollAudit();
        readonly JArray coverageGroups = new JArray();
        float? requestedCaptureScroll;
        GameSession session;
        GarageMenu garage;
        RaceSetupMenu setup;
        string beforeFitSHA;
        static string Hash(string path) => File.Exists(path) ? DataPackageRules.HashFile(path) : "missing";
        static string Arg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, flag);
            if (at < 0 || at + 1 >= args.Length)
                throw new ArgumentException(flag);
            return args[at + 1];
        }

        static void Need(bool value, string why)
        {
            if (!value)
                throw new InvalidOperationException(why);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0)
                return;
            requested = true;
            // A malformed command must still never open a production career/cache.
            string safe = Path.Combine(Application.temporaryCachePath, "help-invalid-" + Guid.NewGuid().ToString("N"));
            PlayerProfile.StorageOverride = safe + ".json";
            RegionCache.OverrideFolder = safe + "-regions";
            CountryUpdateService.OverrideFolder = safe + "-updates";
            CountryUpdateService.AllowVerification = false;
            Application.runInBackground = true;
            Application.logMessageReceived += Logged;
            try
            {
                Need(!Application.isEditor && Application.companyName == "EarthUnscripted Verification" && Application.productName == "EngineerHelpQA" && Application.identifier == "com.earthunscripted.qa.engineerhelp", "Separate QA identity");
                output = Path.GetFullPath(Arg(Flag));
                Need(output.StartsWith("/private/tmp/rr019-engineer-help-", StringComparison.Ordinal) && !Directory.Exists(output), "Fresh private output");
                string bindingPath = Path.GetFullPath(Arg("-earthUnscriptedEngineerHelpBinding"));
                bindingSHA = Arg("-earthUnscriptedEngineerHelpBindingSHA");
                Need(new FileInfo(bindingPath).Length <= 1024 * 1024 && Hash(bindingPath) == bindingSHA, "Exact external build binding");
                binding = JsonUtility.FromJson<Binding>(File.ReadAllText(bindingPath));
                Need(binding != null && binding.recipe == Recipe && binding.buildGUID == Application.buildGUID, "Actual buildGUID/recipe");
                Directory.CreateDirectory(output);
                ownsOutput = true;
                PlayerProfile.StorageOverride = Path.Combine(output, "isolated-profile.json");
                RegionCache.OverrideFolder = Path.Combine(output, "isolated-regions");
                CountryUpdateService.OverrideFolder = Path.Combine(output, "isolated-updates");
                var profile = new PlayerProfile
                {
                    credits = 100000,
                    volume = 0,
                    rumble = 0,
                    wheelEnabled = false,
                    wheelForceEnabled = false,
                    livingScenery = false,
                    detailedScenery = false,
                    quality = 2
                };
                var purchase = GarageRules.Buy(profile.garage, profile.credits, 3, false);
                Need(purchase != null, "Legitimate private single-seat purchase");
                profile.garage = purchase.Garage;
                profile.credits = purchase.Credits;
                Need(profile.Save(), "Seed private profile BEFORE normal Load");
            }
            catch (Exception e)
            {
                isolationError = e.ToString();
                GameSession.SuppressAutomaticLaunch = true;
            }
        }

        static void Logged(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 16)
                errors.Add(message + "\n" + stack);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (requested)
                new GameObject("Isolated integrated HELP observer").AddComponent<EngineerHelpNativeProof>();
        }

        // Observe the ACTUAL menu Repaint after DrivingUI. This observer draws no text.
        void OnGUI()
        {
            if (!done && setup != null && garage != null && (bool)Field(garage, "isOpen") && (int)Field(garage, "selectedPage") == 4)
                scrollAudit.ObserveRepaint(setup);
        }

        void Awake()
        {
            deadline = Time.realtimeSinceStartupAsDouble + DeadlineSeconds;
        }

        void Update()
        {
            if (!done && Time.realtimeSinceStartupAsDouble > deadline)
                Finish(new TimeoutException("Integrated HELP watchdog"));
        }

        IEnumerator Start()
        {
            return Guard(Run());
        }

        IEnumerator Guard(IEnumerator run)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(run);
            while (!done && stack.Count != 0)
            {
                var current = stack.Peek();
                bool more = false;
                object value = null;
                Exception failure = null;
                try
                {
                    more = current.MoveNext();
                    if (more)
                        value = current.Current;
                }
                catch (Exception e)
                {
                    failure = e;
                }

                if (failure != null)
                {
                    Finish(failure);
                    yield break;
                }

                if (!more)
                {
                    (current as IDisposable)?.Dispose();
                    stack.Pop();
                    continue;
                }

                if (value is IEnumerator nested)
                    stack.Push(nested);
                else
                    yield return value;
            }

            if (!done)
                Finish(null);
        }

        static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        void Record(string stage)
        {
            Need(journal.Count < MaximumJournalRows, "Bounded journal");
            journal.Add(new JObject { ["stage"] = stage, ["frame"] = Time.frameCount, ["realtimeSeconds"] = Time.realtimeSinceStartupAsDouble, ["profileSHA"] = Hash(PlayerProfile.StoragePath), ["physicalHidden"] = session && session.Car ? session.Car.Visual.PresentationHidden : false });
            File.WriteAllText(Path.Combine(output, "journal.json"), journal.ToString());
        }

        IEnumerator Settle()
        {
            yield return new WaitForSecondsRealtime(PresentationSettleSeconds);
            for (int i = 0; i < 5; i++)
                yield return null;
            yield return new WaitForEndOfFrame();
        }

        IEnumerator Capture(string name)
        {
            yield return Settle();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Need(texture && texture.width >= 1280 && texture.height >= 720, "Actual readable framebuffer");
                string path = Path.Combine(output, name + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Need(captures.Count < 96, "Bounded actual capture count");
                var captured = new JObject
                {
                    ["name"] = name,
                    ["width"] = texture.width,
                    ["height"] = texture.height,
                    ["sha256"] = Hash(path),
                    ["frame"] = Time.frameCount
                };
                if (requestedCaptureScroll.HasValue)
                    captured["scroll"] = scrollAudit.Snapshot(requestedCaptureScroll.Value);
                captures.Add(captured);
            }
            finally
            {
                if (texture)
                    Destroy(texture);
            }

            Record(name);
        }

        IEnumerator CaptureAdvice(string name)
        {
            yield return CaptureScrollRange(name + "-questions", "questions");
            yield return CaptureScrollRange(name + "-results", "results");
        }

        IEnumerator CaptureScrollRange(string name, string kind)
        {
            yield return Settle();
            Need(scrollAudit.Failure == null && scrollAudit.Frame == Time.frameCount, "Actual current menu Repaint measurement: " + scrollAudit.Failure);
            float from = kind == "results" ? scrollAudit.AdviceStart : 0;
            float to = kind == "questions" ? scrollAudit.AdviceStart : scrollAudit.Height;
            var paragraphs = scrollAudit.Rows.Where(row => row.End > from && row.Start < to).Select(row => new JObject { ["id"] = row.Id, ["start"] = row.Start, ["end"] = row.End }).ToArray();
            Need(paragraphs.Length > 0 && to > from, "Nonempty original UI content range");
            var windows = new JArray();
            float target = Mathf.Clamp(from, 0, scrollAudit.MaximumScroll), covered = from;
            try
            {
                for (int page = 0; page < 32; page++)
                {
                    Set(setup, "scrollPosition", new Vector2(0, target));
                    requestedCaptureScroll = target;
                    string image = name + "-p" + page;
                    yield return Capture(image);
                    var observation = scrollAudit.Snapshot(target);
                    float actual = observation.Value<float>("actualReferenceY");
                    float upper = actual + observation.Value<float>("viewportReferenceHeight");
                    Need(actual <= covered + .5f, "No unseen interior scroll interval");
                    covered = Mathf.Max(covered, Mathf.Min(to, upper));
                    windows.Add(new JObject { ["image"] = image, ["observation"] = observation });
                    if (covered >= to - .01f)
                        break;
                    float next = Mathf.Min(scrollAudit.MaximumScroll, target + scrollAudit.Viewport - 32);
                    Need(next > target + .01f, "Actual clamp permits reaching remaining UI content");
                    target = next;
                }

                Need(covered >= to - .01f, "Every original paragraph/control range covered by actual rendered viewports");
                if (kind == "guide")
                    Need(paragraphs.Length == 36, "All12 original guide titles/effects/tradeoffs measured");
                if (kind == "results")
                    Need(paragraphs.Any(row => row.Value<string>("id") == "result/summary"), "Actual generated explanation included");
                var group = new JObject
                {
                    ["name"] = name,
                    ["kind"] = kind,
                    ["from"] = from,
                    ["to"] = to,
                    ["paragraphs"] = new JArray(paragraphs),
                    ["windows"] = windows
                };
                if (kind == "results")
                {
                    var advice = (RaceEngineerAdvice)Field(setup, "engineerAdvice");
                    group["adviceStatus"] = advice.Status.ToString();
                    group["changeCount"] = advice.Changes.Count;
                }

                coverageGroups.Add(group);
                File.WriteAllText(Path.Combine(output, "scroll-coverage.json"), coverageGroups.ToString());
            }
            finally
            {
                requestedCaptureScroll = null;
            }
        }

        IEnumerator Run()
        {
            Need(isolationError == null, isolationError);
            while (!(session = FindAnyObjectByType<GameSession>()) || !session.BootReady)
            {
                Need(!session || !session.BootFailed, session ? session.BootStatus : "Waiting real bootstrap");
                yield return null;
            }

            Need(session.Profile.garage.Active.model == 3 && session.Profile.garage.cars.Count == 2 && session.Profile.credits == 72000, "Real Load consumed private Core purchase");
            Need(!session.Network.Active && !session.Updates.Enabled, "No LAN/update source activation");
            session.SetMenu(true);
            session.TowToGarage();
            Need(session.AtGarage, "Actual production tow/garage eligibility");
            var ui = FindAnyObjectByType<DrivingUI>();
            Need(ui && ui.Session == session, "Actual main DrivingUI");
            ui.ShowGarage();
            garage = (GarageMenu)Field(ui, "garageMenu");
            Set(garage, "selectedPage", 4);
            setup = (RaceSetupMenu)Field(garage, "raceSetupMenu");
            yield return Settle();
            beforeFitSHA = Hash(PlayerProfile.StoragePath);
            string fittedBefore = JsonUtility.ToJson(session.Profile.garage.Active.raceSetup);
            foreach (int width in new[]
            {
                1280,
                1920
            }

            )
            {
                Screen.SetResolution(width, width == 1280 ? 800 : 1080, FullScreenMode.Windowed);
                yield return Settle();
                Need(Screen.width == width, "Actual requested QA resolution");
                Set(setup, "selectedView", 0);
                Set(setup, "scrollPosition", Vector2.zero);
                yield return Capture(width + "-tuning-preview");
                var preview = (GameObject)Field(garage, "previewRoot");
                Need(preview && session.Car.Visual.PresentationHidden && session.Car.Visual.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), "Entire actual physical car hidden, including inactive tyre renderers");
                var camera = session.View.GetComponent<Camera>();
                var renderers = preview.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                Need(renderers.Length > 0, "Actual separate visible preview");
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                foreach (float x in new[]
                {
                    bounds.min.x,
                    bounds.max.x
                }

                )
                    foreach (float y in new[]
                    {
                        bounds.min.y,
                        bounds.max.y
                    }

                    )
                        foreach (float z in new[]
                        {
                            bounds.min.z,
                            bounds.max.z
                        }

                        )
                        {
                            var p = camera.WorldToViewportPoint(new Vector3(x, y, z));
                            Need(p.z > camera.nearClipPlane && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1, "Full preview bounds inside actual camera; UI overlap needs visual review");
                        }

                Set(setup, "selectedView", 1);
                Set(setup, "scrollPosition", Vector2.zero);
                yield return CaptureScrollRange(width + "-setting-guide", "guide");
                Set(setup, "selectedView", 2);
                Set(setup, "scrollPosition", Vector2.zero);
                Set(setup, "selectedSymptom", RaceEngineerSymptom.FrontWash);
                Set(setup, "selectedPhase", RaceEngineerPhase.MidCorner);
                Set(setup, "selectedSurface", RaceEngineerSurface.Dry);
                Set(setup, "reportedHighSpeed", true);
                Call(setup, "RequestEngineerAdvice", session);
                Need(((RaceEngineerAdvice)Field(setup, "engineerAdvice")).Status == RaceEngineerStatus.Recommended, "Actual high-speed advice");
                yield return CaptureAdvice(width + "-engineer-highspeed");
                Set(setup, "scrollPosition", Vector2.zero);
                Set(setup, "reportedHighSpeed", false);
                Call(setup, "RequestEngineerAdvice", session);
                Need(((RaceEngineerAdvice)Field(setup, "engineerAdvice")).Status == RaceEngineerStatus.Unsupported, "Actual unsupported explanation");
                yield return CaptureAdvice(width + "-engineer-unsupported");
                Set(setup, "scrollPosition", Vector2.zero);
                Set(setup, "selectedSymptom", RaceEngineerSymptom.TyreGrip);
                Set(setup, "selectedSurface", RaceEngineerSurface.Wet);
                Call(setup, "RequestEngineerAdvice", session);
                yield return CaptureAdvice(width + "-engineer-wet");
                Need(Hash(PlayerProfile.StoragePath) == beforeFitSHA && JsonUtility.ToJson(session.Profile.garage.Active.raceSetup) == fittedBefore, "Views/advice did not save or fit");
            }

            Call(setup, "ApplyAdviceToDraft");
            var draft = (RaceSetup)Field(setup, "draftSetup");
            Need(draft.compound == RaceCompound.Wet, "Actual Wet suggestion applied ONLY to draft");
            Need(Hash(PlayerProfile.StoragePath) == beforeFitSHA && session.Profile.garage.Active.raceSetup.compound != RaceCompound.Wet, "Draft remains uncommitted");
            yield return Capture("1920-wet-draft-not-fitted");
            Call(setup, "FitDraftAtGarage", session, (Action)(() => Call(garage, "ResetFromActiveOwnedCar")));
            Need(session.Profile.garage.Active.raceSetup.compound == RaceCompound.Wet && session.Car.RaceSetup.compound == RaceCompound.Wet && Hash(PlayerProfile.StoragePath) != beforeFitSHA, "Actual GUI fit handler commits via GarageChange/save before physical apply");
            var reloaded = PlayerProfile.Load();
            Need(reloaded.garage.Active.raceSetup.compound == RaceCompound.Wet && reloaded.credits == 72000 && session.Economy.Credits == 72000 && reloaded.garage.cars.Count == 2 && reloaded.quality == 2 && !reloaded.wheelEnabled && !reloaded.wheelForceEnabled && reloaded.volume == 0, "Actual saved fit reloads while private credits/ownership/control/graphics remain");
            yield return Capture("1920-wet-fitted");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-earthUnscriptedHelpOperator") >= 0)
            {
                Record("OPERATOR_WINDOW: use actual1..5 garage pages; setupTab/arrow/Enter; inspectguide+expert; no automated input claim");
                double until = Time.realtimeSinceStartupAsDouble + 180;
                while (Time.realtimeSinceStartupAsDouble < until && !File.Exists(Path.Combine(output, "operator-complete.json")))
                    yield return null;
            }

            garage.Close();
            Set(ui, "tab", 0);
            yield return Settle();
            Need(session.Car.enabled && !session.Car.Body.isKinematic && session.Car.GetComponent<BoxCollider>().enabled && !session.Car.Visual.PresentationHidden && session.Car.Visual.GetComponentsInChildren<Renderer>(true).All(r => r.enabled) && session.View.Preview == null, "Physical presentation restore inspected separately");
            // Unity destroyed preview may be fake-null; use actual reference truthiness below.
            Need(!(GameObject)Field(garage, "previewRoot"), "Closed showroom owns no preview");
            yield return Capture("1920-closed-physical-car");
            receipt["draftNotPersisted"] = true;
            receipt["fitPersisted"] = true;
            receipt["physicalRestored"] = true;
        }

        void Finish(Exception failure)
        {
            if (done)
                return;
            done = true;
            if (garage != null)
                garage.Close();
            receipt["status"] = failure == null && errors.Count == 0 ? "PASS_RENDER_BASELINE_VISUAL_REVIEW_PENDING" : "FAIL";
            receipt["error"] = failure?.ToString();
            receipt["recipe"] = Recipe;
            receipt["buildGUID"] = Application.buildGUID;
            receipt["bindingSHA256"] = bindingSHA;
            receipt["scope"] = "Actual main boot/garage existing-method render baseline; NOT keyboard/controller acceptance";
            receipt["inputAcceptance"] = false;
            receipt["visualReviewPending"] = true;
            receipt["runtimeErrors"] = new JArray(errors);
            receipt["captures"] = captures;
            receipt["coverageGroups"] = coverageGroups;
            receipt["scrollCoverageObserved"] = failure == null && coverageGroups.Count == 14;
            receipt["weatherRequests"] = session && session.Conditions ? session.Conditions.WeatherRequests : 0;
            receipt["sourceRequests"] = session && session.Updates ? session.Updates.SourceRequests : 0;
            if (receipt.Value<int>("sourceRequests") != 0 || receipt.Value<int>("weatherRequests") != 0)
                receipt["status"] = "FAIL";
            if (ownsOutput)
                File.WriteAllText(Path.Combine(output, "receipt.json"), receipt.ToString());
            Application.Quit(receipt.Value<string>("status") == "FAIL" ? 1 : 0);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Logged;
        }
    }
}
