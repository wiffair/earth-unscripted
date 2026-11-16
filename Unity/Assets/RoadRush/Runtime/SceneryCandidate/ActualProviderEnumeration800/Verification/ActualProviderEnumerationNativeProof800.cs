using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using RoadRush.Scenery800.CandidateEnumeration;
using RoadRush.Core800;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoadRush
{
    /// <summary>
    /// Isolated observer of REAL automatic GameSession bootstrap and its scoped
    /// factory. No injected Profile/World/Car/Ready, second roster, manufactured
    /// render or phase callbacks. External reviewed process/privacy guard required.
    /// TimeScale stays0: this proves reader phase, not driving/tyre contact.
    /// </summary>
    [DefaultExecutionOrder(32010)]
    public sealed class ActualProviderEnumerationNativeProof800 : MonoBehaviour
    {
        public const string Recipe = "ActualProviderEnumeration800/AutomaticBootProbeRetire/1";
        public const string Company = GameSession.ScopedCompany800;
        public const string Product = GameSession.ScopedProduct800;
        public const string Identifier = GameSession.ScopedIdentifier800;
        public const string Flag = "-roadRushActualProviderEnumerationProof800";
        public const string EnumerationDataSha256 = "b76b3cd96685d37fd4aab142ba8509ebe4a3137f507bff19e51b7a8e0f0f6da9";
        static ReadOnlyCollection<DeterministicCandidateInputs800.Candidate> enumerationCandidates;
        const double TotalTimeoutSeconds = 600;
        const double CaseTimeoutSeconds = 195;
        const int MaximumFiles = 4096, MaximumEvents = 4096;
        const long MaximumBindingBytes = 2 * 1024 * 1024;
        static bool requested, ownsOutput;
        static string output, project, app, bindingPath, bindingHash, profile, profileHash, isolationError;
        static Binding binding;
        static ActualProviderEnumerationNativeProof800 instance;

        [Serializable] public sealed class FilePin { public string path, sha256; public long bytes; }
        [Serializable] public sealed class Binding
        {
            public string recipe, buildGUID, executableSHA256, timeManagerSHA256, dynamicsSHA256, sourceContext, profileTemplateSHA256;
            public FilePin[] sourceFiles, appFiles, preparedInputs;
            public FilePin placementJobs, enumerationData;
        }
        [Serializable] public sealed class Claim { public string recipe, bindingSHA256, profileSHA256, nonce; }
        [Serializable] public sealed class CaseReport
        {
            public string name, status;
            public bool actualAutomaticBoot, clearedBeforeReady, oldOwnerRetired, actualWorldDestroyed;
            public int clearingRenders, drivingRenders;
        }
        [Serializable] public sealed class EventRow
        {
            public string caseName, kind, camera;
            public int frame, traffic, police;
            public double realtimeSeconds;
            public bool ready, failed;
            public int actualVehicles, physicalSupported, physicalUnbound, actualPlayers, actualTraffic, actualPolice;
            public float fixedTimeSeconds;
            public string samplePhase, sourceDigest, physicalDigest;
        }
        [Serializable] public sealed class Report
        {
            public string recipe = Recipe, status, error, buildGUID, bindingSHA256, eventsSHA256;
            public string scope = "Same-session bounded deterministic individual provider queries after unchanged20historical observations, authentic bulk appearance commit and issuer retirement; no driving/contact/animal/full-world admission.";
            public bool automaticBootstrap, isolatedProfileUnchanged, exactInputsUnchanged, updaterDisabled;
            public bool actualSceneRetiredBeforeQuit, noOrphanCamerasOrListeners, teardownOnlyBootstrapSuppression;
            public int runtimeErrors, sceneBoots, rawPhysicalFrames;
            public float fixedDeltaTimeSeconds, timeScale;
            public CaseReport[] cases;
            public ActualScopedSessionPhaseProof800.Report phase;
            public FilePin providerReport;
            public bool providerPositiveObservation, providerHeldRefused, providerFreshCaptureRefused, providerRetirementObserved, providerClean;
            public int providerCompletedHistoricalJobs, providerCommittedClearQueries;
            public FilePin[] captures;
            public FilePin savedProfile;
        }
        readonly List<CaseReport> cases = new List<CaseReport>();
        readonly List<FilePin> captures = new List<FilePin>();
        readonly Report report = new Report();
        StreamWriter events;
        GameSession session;
        CaseReport current;
        double deadline;
        int eventCount, runtimeErrors;
        bool done, phaseReading;
        int lastPhysicalFrame = -1;
        Scenery800.Verification.NativeEnumerationProbe800 providerProbe;
        readonly FunnyAnimals.ActualRegistryConsumer.RegisteredVehicleEnvelopeReader physicalReader =
            new FunnyAnimals.ActualRegistryConsumer.RegisteredVehicleEnvelopeReader();

        static void Need(bool okay, string reason)
        { if (!okay) throw new InvalidOperationException("ACTUAL SCOPED800 QA: " + reason); }
        static string Hash(string path) => Core.DataPackageRules.HashFile(path);
        static string Argument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0 || index != Array.LastIndexOf(args, key) || index + 1 >= args.Length)
                throw new ArgumentException("Exactly one explicit argument " + key);
            return args[index + 1];
        }
        static string Child(string root, string relative)
        {
            Need(!Path.IsPathRooted(relative) && relative.Split('/','\\').All(part => part != ".."), "Relative pin path");
            string full = Path.GetFullPath(Path.Combine(root, relative));
            Need(full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Pin stays inside root");
            return full;
        }
        static bool PinsMatch(string root, FilePin[] pins, bool absolute = false)
        {
            if (pins == null || pins.Length == 0 || pins.Length > MaximumFiles) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pin in pins)
            {
                if (pin == null || !unique.Add(pin.path)) return false;
                string path = absolute ? Path.GetFullPath(pin.path) : Child(root, pin.path);
                if (absolute && path != pin.path) return false;
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != pin.bytes || Hash(path) != pin.sha256) return false;
            }
            return true;
        }
        static bool ExactCurrentInputs()
        {
            return PinsMatch(project, binding.sourceFiles) && PinsMatch(app, binding.appFiles)
                && PinsMatch("", binding.preparedInputs, true)
                && Directory.GetFiles(Path.Combine(project,"Assets/RoadRush"),"*.cs",SearchOption.AllDirectories).Length == binding.sourceFiles.Length
                && Directory.GetFiles(app,"*",SearchOption.AllDirectories).Length == binding.appFiles.Length
                && Hash(Path.Combine(project,"ProjectSettings/TimeManager.asset")) == binding.timeManagerSHA256
                && Hash(Path.Combine(project,"ProjectSettings/DynamicsManager.asset")) == binding.dynamicsSHA256
                && Hash(bindingPath) == bindingHash
                && PinsMatch("",new[]{binding.placementJobs,binding.enumerationData},true);
        }

        static ReadOnlyCollection<DeterministicCandidateInputs800.Candidate> LoadUnboundCandidates(string path)
        {
            var data = JObject.Parse(File.ReadAllText(path));
            Need((string)data["schema"] == "RoadRushDeterministicCandidateInputs1"
                && (string)data["availability"] == "UNBOUND_DATA_ONLY"
                && (string)data["algorithm"] == DeterministicCandidateInputs800.Algorithm
                && (bool?)data["historical20Rewritten"] == false
                && (bool?)data["runtimeFeasibilityProved"] == false
                && (bool?)data["gameplayAdmission"] == false, "Expected unbound DATA labels, never runtime authority");
            int[] Ordinals(string key)
            {
                var array = data[key] as JArray;
                Need(array != null && array.Count <= DeterministicCandidateInputs800.MaximumRetainedPerSpecies,
                    "Bounded deterministic species ordinal list");
                var result = new int[array.Count];
                for (int i=0;i<result.Length;i++)
                {
                    Need(array[i].Type == JTokenType.Integer && (long)array[i] >= 0
                        && (long)array[i] < DeterministicCandidateInputs800.GridAnchorCount, "Exact in-range integer ordinal");
                    result[i] = (int)array[i];
                }
                return result;
            }
            return DeterministicCandidateInputs800.ReconstructUnboundShortlist(
                Ordinals("sheepAnchorOrdinals"), Ordinals("deerAnchorOrdinals"));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, Flag) < 0) return;
            if (requested) { isolationError = "Duplicate diagnostic bootstrap"; GameSession.SuppressAutomaticLaunch = true; return; }
            requested = true;
            try
            {
                Need(!Application.isEditor && Application.companyName == GameSession.ScopedCompany800 &&
                    Application.productName == GameSession.ScopedProduct800 && Application.identifier == GameSession.ScopedIdentifier800,
                    "Exact separate QA application domain");
                Need(Array.IndexOf(args,"-roadRushScopedNativeProof800") < 0
                    && Array.IndexOf(args,"-roadRushActualProviderProof800") < 0, "Original/provider/enumeration observers cannot co-launch");
                Need(!GameSession.SuppressAutomaticLaunch, "Only ordinary automatic session bootstrap may run");
                Need(Array.IndexOf(args, GameSession.ScopedSessionFlag800) >= 0, "Actual scoped boot explicitly selected");
                output = Path.GetFullPath(Argument(args, Flag));
                Need(output.StartsWith("/private/tmp/rr019-actual-scoped-session-", StringComparison.Ordinal) &&
                    Directory.Exists(output) && !File.Exists(Path.Combine(output,"receipt.json")), "Fresh externally claimed private output");
                bindingPath = Path.GetFullPath(Argument(args,"-roadRushScopedNativeBinding800"));
                bindingHash = Argument(args,"-roadRushScopedNativeBindingSHA800");
                Need(new FileInfo(bindingPath).Length <= MaximumBindingBytes && Hash(bindingPath) == bindingHash,"Exact bounded build binding");
                binding = JsonUtility.FromJson<Binding>(File.ReadAllText(bindingPath));
                Need(binding != null && binding.recipe == Recipe && binding.buildGUID == Application.buildGUID,"Actual build identity");
                project = Path.GetFullPath(Argument(args,"-roadRushScopedNativeProject800"));
                app = Path.GetDirectoryName(Application.dataPath);
                profile = Path.Combine(output,"profile.json");
                Need(Path.GetFullPath(Argument(args,"-roadRushScopedProfile800")) == profile,"Actual boot profile path matches claim");
                var claim = JsonUtility.FromJson<Claim>(File.ReadAllText(Path.Combine(output,"launch-claim.json")));
                Need(claim != null && claim.recipe == Recipe && claim.bindingSHA256 == bindingHash &&
                    claim.nonce != null && claim.nonce.Length == 32 && Hash(profile) == claim.profileSHA256,"Fresh external launch/profile claim");
                profileHash = claim.profileSHA256;
                Need(binding.placementJobs != null &&
                    binding.placementJobs.path == Path.GetFullPath(Argument(args,"-roadRushProviderPlacementJobs800")) &&
                    binding.placementJobs.sha256 == Scenery800.Verification.PlacementProbeJobs800.SourceJsonSha256 &&
                    binding.placementJobs.bytes > 0 && binding.placementJobs.bytes <= 1024*1024 &&
                    PinsMatch("",new[]{binding.placementJobs},true),"Exact original bounded placement query JSON");
                Need(binding.enumerationData != null && binding.enumerationData.path ==
                    Path.GetFullPath(Argument(args,"-roadRushProviderEnumerationData800"))
                    && binding.enumerationData.sha256 == EnumerationDataSha256
                    && binding.enumerationData.bytes > 0 && binding.enumerationData.bytes <= 1024*1024
                    && PinsMatch("",new[]{binding.enumerationData},true), "Exact bounded unbound enumeration DATA pin");
                enumerationCandidates = LoadUnboundCandidates(binding.enumerationData.path);
                Need(profileHash == binding.profileTemplateSHA256, "Exact build-authored fresh QA profile template");
                Need(binding.preparedInputs != null && binding.preparedInputs.Length == 8 && ExactCurrentInputs(),"Source/app/physics/prepared pins before automatic boot");
                Need(binding.sourceContext == Path.GetFullPath(Argument(args,"-roadRushScopedSourceContext800")) &&
                    binding.preparedInputs[0].path == Path.GetFullPath(Argument(args,"-roadRushScopedCooked800")),"Actual context/cooked args bound");
                Need(binding.preparedInputs[0].sha256 == TerrainDraft.Core800.VerifiedCoreTerrain.ReviewedCookedArtifactSHA256 &&
                    binding.preparedInputs[1].sha256 == TerrainDraft.Core800.VerifiedCoreTerrain.ReviewedCookedPackageSHA256 &&
                    binding.preparedInputs[2].sha256 == ReviewedCanonicalRaw13Input.ExpectedBodySHA256,"Reviewed cooked/raw identities");
                PlayerProfile.StorageOverride = profile;
                RegionCache.OverrideFolder = Path.Combine(output,"region-cache");
                CountryUpdateService.OverrideFolder = Path.Combine(output,"update-cache");
                CountryUpdateService.AllowVerification = false;
                Application.runInBackground = true;
                ownsOutput = true;
                // Do NOT suppress/create GameSession. Its real scene-loaded callback
                // must launch exactly one instance after this isolation fence.
            }
            catch (Exception error)
            {
                isolationError = error.ToString();
                GameSession.SuppressAutomaticLaunch = true;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Observe()
        {
            if (!requested || instance) return;
            var root = new GameObject("Actual scoped800 automatic boot observer");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<ActualProviderEnumerationNativeProof800>();
        }
        void Awake()
        {
            deadline = Time.realtimeSinceStartupAsDouble + TotalTimeoutSeconds;
            Application.logMessageReceived += Logged;
            RenderPipelineManager.endCameraRendering += Rendered;
            if (ownsOutput) events = new StreamWriter(Path.Combine(output,"render-events.jsonl"),false);
            StartCoroutine(Guard(Run()));
        }
        void Logged(string message,string stack,LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors++; }
        void Update() { if (!done && Time.realtimeSinceStartupAsDouble > deadline) Finish(false,"600-second actual scoped proof watchdog"); }
        void Record(string kind, Camera camera = null)
        {
            Need(eventCount++ < MaximumEvents,"Bounded raw event trace");
            events?.WriteLine(JsonUtility.ToJson(new EventRow {
                caseName=current?.name??"bootstrap",kind=kind,camera=camera?camera.name:"",frame=Time.frameCount,
                realtimeSeconds=Time.realtimeSinceStartupAsDouble,traffic=session?session.BootTrafficPrepared:0,
                police=session?session.BootPatrolPrepared:0,ready=session&&session.BootReady,failed=session&&session.BootFailed }));
            events?.Flush();
        }
        void Rendered(ScriptableRenderContext context,Camera camera)
        {
            if (done || current == null || !session) return;
            if (camera.name.StartsWith("Loading clear only;",StringComparison.Ordinal) && camera.transform.IsChildOf(session.transform))
            { current.clearingRenders++; if (current.clearingRenders <= 2) Record("actual-clearing-render",camera); }
            if (session.View && camera == session.View.GetComponent<Camera>())
            { current.drivingRenders++; if (current.drivingRenders <= 2) Record("actual-drive-render",camera); }
        }
        IEnumerator Guard(IEnumerator work)
        {
            try
            {
                while (!done)
                {
                    bool more=false; object next=null; Exception error=null;
                    try { more=work.MoveNext(); if(more)next=work.Current; } catch(Exception caught) {error=caught;}
                    if(error!=null){Finish(false,error.ToString());yield break;}
                    if(!more)yield break;
                    if(next is IEnumerator nested)yield return Guard(nested);else yield return next;
                }
            }
            finally { (work as IDisposable)?.Dispose(); }
        }
        IEnumerator Wait(Func<bool> condition,string description)
        {
            double end=Time.realtimeSinceStartupAsDouble+CaseTimeoutSeconds;
            while(!condition()&&!done){Need(Time.realtimeSinceStartupAsDouble<end,description+" timed out");yield return null;}
            Need(!done,"Observer ended before "+description);
        }
        IEnumerator FindAutomatic(GameSession previous,string name)
        {
            yield return Wait(()=>{ var found=FindObjectsByType<GameSession>(FindObjectsSortMode.None);
                Need(found.Length<=1,"No double automatic GameSession bootstrap");
                if(found.Length==1 && found[0]!=previous){session=found[0];return true;}return false;},"Actual automatic GameSession");
            current=new CaseReport{name=name,actualAutomaticBoot=true,status="RUNNING"}; cases.Add(current);report.sceneBoots++;
            Need(session.BootClearingCameraPresent,"Actual early loading clearer before actor Ready");
            Record("actual-automatic-session-observed");
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Need(texture.width>=1280&&texture.height>=720,"Actual native framebuffer dimensions");
                string path=Path.Combine(output,name+".png");File.WriteAllBytes(path,texture.EncodeToPNG());
                captures.Add(new FilePin{path=Path.GetFileName(path),bytes=new FileInfo(path).Length,sha256=Hash(path)});
            }
            finally{Destroy(texture);}
        }
        IEnumerator CancelAndRetry(int traffic,int police,string next)
        {
            yield return Wait(()=>session.BootFailed||session.BootReady||session.BootTrafficPrepared>=traffic&&session.BootPatrolPrepared>=police,"Actual staged cancellation checkpoint");
            Need(!session.BootReady&&!session.BootFailed&&current.clearingRenders>0,"Actual staged boot remains cancellable after real render");
            var world=session.World;var ground=world.ScopedGround800;var town=world.GeographicWorld.CurrentTownPublication800;var population=session.ActualVehicles;
            Need(ground!=null&&ground.IsCurrent&&town!=null&&town.IsCurrent&&population!=null,"Actual pre-actor ground/Town/registry exist");
            current.clearedBeforeReady=true; session.CancelBoot(); Record("actual-public-cancel");
            yield return null;yield return null;
            Need(session.BootFailed&&!session.BootReady&&!session.World&&!world&&!ground.IsCurrent&&!town.IsCurrent&&
                !population.TryCapture(out _,out _),"Actual cancel revokes world/Ground/Town/population");
            current.oldOwnerRetired=true;current.actualWorldDestroyed=true;current.status="PASS_ACTUAL_CANCEL";
            yield return Capture(current.name);
            GameSession previous=session;LocationLoading.Retry();
            yield return FindAutomatic(previous,next);
            Need(!previous,"Actual previous session destroyed by legitimate reload");
        }
        void LateUpdate()
        {
            if (done || !phaseReading || !session || !session.BootReady || lastPhysicalFrame == Time.frameCount) return;
            try
            {
                var publication = session.World.GeographicWorld.CurrentTownPublication800;
                var population = session.ActualVehicles;
                if (publication == null || population == null) return; // Genuine retirement ends the sampling window.
                Need(population.MatchesTownPublication800(publication), "Raw frame exact actual Town association");
                Need(population.TryCapture(out var snapshot, out string reason), "Raw actual snapshot: " + reason);
                Need(snapshot.Frame == Time.frameCount && snapshot.Phase == VehiclePopulation.VehicleSamplePhase.AfterActorFrame,
                    "Raw same-frame actual AfterActorFrame");
                Need(physicalReader.TryReadAfterActorFrame(population, publication.Ground, publication,
                    out var diagnostic, out reason), "Raw actual physical diagnostics: " + reason);
                int supported = diagnostic.Vehicles.Count(item => item.Physical.HasValue);
                int players = snapshot.Vehicles.Count(item => item.Role == "Player");
                int traffic = snapshot.Vehicles.Count(item => item.Role == "Traffic");
                int police = snapshot.Vehicles.Count(item => item.Role == "Police");
                Need(snapshot.Vehicles.Count == 28 && supported == 28 && diagnostic.Vehicles.Count == 28 &&
                    players == 1 && traffic == 24 && police == 3, "Raw actual full28 physical/role counts");
                Need(physicalReader.TryCommitPhysicalDiagnostics(diagnostic, out reason) && snapshot.IsCurrent &&
                    population.MatchesTownPublication800(publication), "Raw actual immediate commit: " + reason);
                Need(eventCount++ < MaximumEvents, "Bounded raw physical event trace");
                events.WriteLine(JsonUtility.ToJson(new EventRow { caseName=current.name, kind="actual-full28-afteractorframe",
                    frame=snapshot.Frame, realtimeSeconds=Time.realtimeSinceStartupAsDouble, fixedTimeSeconds=snapshot.FixedTimeSeconds,
                    samplePhase=snapshot.Phase.ToString(), actualVehicles=snapshot.Vehicles.Count, physicalSupported=supported,
                    physicalUnbound=diagnostic.Vehicles.Count-supported,actualPlayers=players,actualTraffic=traffic,actualPolice=police,
                    sourceDigest=snapshot.SourceDigest,physicalDigest=snapshot.PhysicalDigest,ready=session.BootReady,failed=session.BootFailed }));
                events.Flush();lastPhysicalFrame=Time.frameCount;report.rawPhysicalFrames++;
            }
            catch(Exception error){Finish(false,error.ToString());}
        }
        IEnumerator Run()
        {
            Need(isolationError==null&&ownsOutput,"Before-bootstrap isolation refused: "+isolationError);
            yield return FindAutomatic(null,"ready-full28-provider-retire");
            yield return Wait(()=>session.BootReady||session.BootFailed,"Actual complete scoped boot");
            Need(session.BootReady&&!session.BootFailed&&session.Menu&&Time.timeScale==0,"Actual ready closed reader mode");
            yield return Wait(()=>session.BootDriveCameraRenderConfirmed&&current.drivingRenders>0,"Actual render-confirmed camera handoff");
            Need(current.clearingRenders>0,"Real clear rendered before actual ready view");current.clearedBeforeReady=true;
            yield return Capture("actual-ready-full28");
            Need(session.Updates&&!session.Updates.Enabled,"Actual updater disabled in isolated profile");report.updaterDisabled=true;
            phaseReading=true;
            providerProbe = new Scenery800.Verification.NativeEnumerationProbe800();
            // Factory validates/copies reviewed canonical bytes; consumed Town Source is never reused.
            var genuineInput = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(binding.sourceContext);
            yield return providerProbe.Run(session,genuineInput,
                enumerationCandidates,System.Threading.CancellationToken.None);
            SaveProviderReport();
            Need(providerProbe.Result.PositiveObservation && providerProbe.Result.CompletedHistoricalJobs == 20,
                "Enumeration requires warm actual28, exact20 historical queries completed with authentic commits (Blocked/Unknown allowed), and minimumone genuinely committed individual Clear query; optionalpair separate");
            yield return Capture("actual-provider-observation-before-retirement");
            yield return ActualScopedSessionPhaseProof800.Run(session,value=>report.phase=value);
            yield return null; // Retirement refusal must not be a same-frame attempt cap.
            providerProbe.ObserveAfterRetirement();
            SaveProviderReport();
            Need(report.providerRetirementObserved && report.providerClean && report.providerHeldRefused && report.providerFreshCaptureRefused,
                "Actual held/fresh provider reads refuse after real issuer retirement");
            phaseReading=false;
            Need(report.rawPhysicalFrames>=3,"Three independently recorded actual full28 frames");
            Need(report.phase!=null&&report.phase.FullRosterFrames>=3&&report.phase.ActualVehicles==28&&
                report.phase.SupportedPhysical==28&&report.phase.UnboundPhysical==0&&report.phase.PublicationRetired&&
                report.phase.GroundRetired&&report.phase.HeldSnapshotRefused&&report.phase.FreshCaptureRefused,"Actual full28 ordered read and issuer retirement");
            current.oldOwnerRetired=true;current.status="PASS_ACTUAL_PROVIDER_AND_FULL28_PHASE_RETIRE";
            Record("actual-full28-phase-and-retirement");
            report.fixedDeltaTimeSeconds=Time.fixedDeltaTime;report.timeScale=Time.timeScale;
            Need(!GameSession.SuppressAutomaticLaunch,"Automatic bootstrap stayed enabled through the single actual full boot");
            report.automaticBootstrap=true;
            // GameSession.OnApplicationQuit legitimately saves a READY career.
            // Retire the actual scene FIRST, never patch Save or restore bytes.
            // Suppression begins only after all positive tests are complete, to
            // prevent this cleanup reload from creating an unwanted second boot.
            GameSession.SuppressAutomaticLaunch=true;
            report.teardownOnlyBootstrapSuppression=true;
            var unload=SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
            Need(unload!=null,"Actual cleanup scene reload started");
            yield return Wait(()=>unload.isDone,"Actual scene retirement");
            yield return null;yield return null;
            report.actualSceneRetiredBeforeQuit=!session && FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==0;
            report.noOrphanCamerasOrListeners=FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0 &&
                FindObjectsByType<AudioListener>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0;
            Need(report.actualSceneRetiredBeforeQuit&&report.noOrphanCamerasOrListeners,"Actual scene cleanup before application quit");
            Finish(true,null);
        }
        void SaveProviderReport()
        {
            if (!ownsOutput || providerProbe == null) return;
            var data = providerProbe.Result; // Only copied managed report fields/properties.
            string path = Path.Combine(output,"provider-observations.json");
            File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(data,Newtonsoft.Json.Formatting.Indented));
            report.providerReport = new FilePin {path=Path.GetFileName(path),bytes=new FileInfo(path).Length,sha256=Hash(path)};
            report.providerPositiveObservation=data.PositiveObservation;
            report.providerCompletedHistoricalJobs=data.CompletedHistoricalJobs;
            report.providerCommittedClearQueries=data.CommittedClearQueries;
            report.providerHeldRefused=data.HeldRefusedAfterRetirement;
            report.providerFreshCaptureRefused=data.FreshCaptureRefusedAfterRetirement;
            report.providerRetirementObserved=data.RetirementObserved;
            report.providerClean=!data.Cancelled&&!data.DeadlineExpired&&!data.CleanupFailed&&!data.DisposedBeforeRunCompleted;
        }
        void Finish(bool passed,string error)
        {
            if(done)return;
            done=true; // Reentrant completion cannot publish a second receipt.
            int exitCode=1;
            void Failed(Exception failure,string stage)
            { passed=false;error=(error??"")+"; "+stage+": "+failure.GetType().Name; }
            try
            {
                try { RenderPipelineManager.endCameraRendering-=Rendered; } catch(Exception failure){Failed(failure,"render-unsubscribe");}
                // Each cleanup is attempted independently. Dispose BEFORE serializing
                // the managed report, so cleanup/early-disposal vetoes are current.
                try { providerProbe?.Dispose(); } catch(Exception failure){Failed(failure,"provider-dispose");}
                try { SaveProviderReport(); } catch(Exception failure){Failed(failure,"provider-report");}
                StreamWriter writer=events;events=null;
                try { writer?.Flush(); } catch(Exception failure){Failed(failure,"events-flush");}
                try { writer?.Dispose(); } catch(Exception failure){Failed(failure,"events-dispose");}
                try { physicalReader.Dispose(); } catch(Exception failure){Failed(failure,"physical-reader-dispose");}
                report.runtimeErrors=runtimeErrors;report.buildGUID=Application.buildGUID;report.bindingSHA256=bindingHash;
                try { report.isolatedProfileUnchanged=profile!=null&&File.Exists(profile)&&Hash(profile)==profileHash; }
                catch(Exception failure){report.isolatedProfileUnchanged=false;Failed(failure,"profile-verify");}
                try { report.exactInputsUnchanged=binding!=null&&ExactCurrentInputs(); }
                catch(Exception failure){report.exactInputsUnchanged=false;Failed(failure,"source-app-input-verify");}
                report.cases=cases.ToArray();report.captures=captures.ToArray();
                if(ownsOutput)
                {
                    try { report.eventsSHA256=Hash(Path.Combine(output,"render-events.jsonl")); }
                    catch(Exception failure){Failed(failure,"events-hash");}
                    try { report.savedProfile=new FilePin{path="profile.json",bytes=new FileInfo(profile).Length,sha256=Hash(profile)}; }
                    catch(Exception failure){Failed(failure,"saved-profile-pin");}
                }
                report.error=error;
                report.status=passed&&ownsOutput&&runtimeErrors==0&&report.automaticBootstrap&&report.isolatedProfileUnchanged&&report.exactInputsUnchanged
                    &&report.providerPositiveObservation&&report.providerRetirementObserved&&report.providerClean
                    &&report.providerHeldRefused&&report.providerFreshCaptureRefused?
                    "PASS_ACTUAL_PROVIDER_ENUMERATION_QUERY_OBSERVATION_ONLY":"FAIL";
                bool receiptWritten=false;
                if(ownsOutput)
                {
                    try { File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(report,true));receiptWritten=true; }
                    catch(Exception failure){Failed(failure,"receipt-write");report.status="FAIL";report.error=error;}
                }
                exitCode=receiptWritten&&report.status=="PASS_ACTUAL_PROVIDER_ENUMERATION_QUERY_OBSERVATION_ONLY"?0:1;
            }
            catch(Exception failure)
            {
                // No recursive Finish: done is already latched. Any unexpected
                // finalization exception still reaches the unconditional quit.
                report.status="FAIL";report.error=(error??"")+"; finalization: "+failure.GetType().Name;
                exitCode=1;
            }
            finally { Application.Quit(exitCode); } // External owned-process guard still mandatory.
        }
        void OnDestroy()
        {
            try { RenderPipelineManager.endCameraRendering-=Rendered; } catch { }
            try { Application.logMessageReceived-=Logged; } catch { }
            try { events?.Dispose(); } catch { }
            try { physicalReader.Dispose(); } catch { }
            try { providerProbe?.Dispose(); } catch { }
        }
    }
}
