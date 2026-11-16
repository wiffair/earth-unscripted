using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public sealed class ActualScopedSessionNativeProof800 : MonoBehaviour
    {
        public const string Recipe = "ActualScopedSession800/PartialTownThenActorCancelPhaseRetire/PassiveGamePacing/4";
        public const string Company = GameSession.ScopedCompany800;
        public const string Product = GameSession.ScopedProduct800;
        public const string Identifier = GameSession.ScopedIdentifier800;
        public const string Flag = "-roadRushScopedNativeProof800";
        public const string PacingObservationFlag = "-roadRushScopedNativeObserveGamePacing800";
        // Assertion against actual GameSession policy, NOT an observer assignment.
        const int ExpectedGameTargetFramesPerSecond = 120;
        static PacingObservationReport pacing;
        const double TotalTimeoutSeconds = 600;
        const double CaseTimeoutSeconds = 195;
        // This is the exact reviewed fixture source census, not a runtime world limit.
        const int ExpectedTownEntries = 1793;
        const string PartialTownRootName = "Scoped800 town (walls-only collision)";
        const int MaximumFiles = 4096, MaximumEvents = 4096;
        const long MaximumBindingBytes = 2 * 1024 * 1024;
        static bool requested, ownsOutput;
        static string output, project, app, bindingPath, bindingHash, profile, profileHash, isolationError;
        static Binding binding;
        static ActualScopedSessionNativeProof800 instance;

        [Serializable] public sealed class FilePin { public string path, sha256; public long bytes; }
        [Serializable] public sealed class Binding
        {
            public string recipe, buildGUID, executableSHA256, timeManagerSHA256, dynamicsSHA256, sourceContext, profileTemplateSHA256;
            public FilePin[] sourceFiles, appFiles, preparedInputs;
        }
        [Serializable] public sealed class PacingObservationReport
        {
            public string policy = "Passive observation of real GameSession startup policy; observer NEVER sets/restores pacing";
            public int isolationFrame, initialVSyncCount, initialTargetFrameRate, initialQualityLevel;
            public string graphicsDeviceType, renderingThreadingMode;
            public bool firstCallbackObserved, actualGameStartupPacingObserved, observerSetsOrRestoresPacing = false;
            public int firstCallbackFrame, firstCallbackVSyncCount, firstCallbackTargetFrameRate, firstCallbackQualityLevel;
            public int qualifiedCallbackSamples;
        }
        [Serializable] public sealed class Claim { public string recipe, bindingSHA256, profileSHA256, nonce; }
        [Serializable] public sealed class CaseReport
        {
            public string name, status;
            public bool actualAutomaticBoot, clearedBeforeReady, oldOwnerRetired, actualWorldDestroyed;
            public int clearingRenders, drivingRenders;
        }
        [Serializable] public sealed class PartialOwnedResource
        {
            public string ownerEntityId, meshEntityId, colliderEntityId, name;
            public int vertices, triangles;
        }
        [Serializable] public sealed class PartialTownReport
        {
            public int totalRecords, emittedRecords, pendingRecords, suppressedRecords;
            public int generatedMeshes, colliders, borrowedMeshes, sharedMaterials;
            public int groundGeneration, retryGroundGeneration;
            public string groundOwnerHandle, retryGroundOwnerHandle;
            public string rawDocumentDigest, sourceDigest, groundGeometryDigest, partialLedgerSHA256;
            public bool unpublished, inactiveRoot, noActorFactories, groundCurrentBeforeCancel;
            public bool revokedAndDestroyed, newGenerationOwner, noLatePublicationOrResourceResurrection;
            public PartialOwnedResource[] ownedResources;
        }
        [Serializable] public sealed class EventRow
        {
            public string caseName, kind, camera;
            public int frame, traffic, police;
            public int vSyncCount, targetFrameRate, actualQualityLevel;
            public bool applicationFocused;
            public double realtimeSeconds;
            public bool ready, failed;
            public int actualVehicles, physicalSupported, physicalUnbound, actualPlayers, actualTraffic, actualPolice;
            public float fixedTimeSeconds;
            public string samplePhase, sourceDigest, physicalDigest;
        }
        [Serializable] public sealed class Report
        {
            public string recipe = Recipe, status, error, buildGUID, bindingSHA256, eventsSHA256;
            public string scope = "Same-session typed ground/Town and full28 physical-layout conversion at real AfterActorFrame; no driving/contact/full-world/appearance admission.";
            public bool automaticBootstrap, isolatedProfileUnchanged, exactInputsUnchanged, updaterDisabled;
            public bool actualSceneRetiredBeforeQuit, noOrphanCamerasOrListeners, teardownOnlyBootstrapSuppression;
            public int runtimeErrors, sceneBoots, rawPhysicalFrames;
            public float fixedDeltaTimeSeconds, timeScale;
            public CaseReport[] cases;
            public PartialTownReport partialTown;
            public PacingObservationReport startupPacing;
            public ActualScopedSessionPhaseProof800.Report phase;
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
        WorldBuilder retiredPartialWorld;
        GeoWorld retiredPartialTown;
        GameObject retiredPartialRoot;
        GroundLease800 retiredPartialGround;
        Mesh[] partialOwnedMeshes, partialBorrowedMeshes;
        MeshCollider[] partialColliders;
        Material[] partialSharedMaterials;
        bool partialRetirementObserved;
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
                && Hash(bindingPath) == bindingHash;
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
                Need(args.Count(value => value == PacingObservationFlag) == 1,
                    "Explicit passive game-pacing observation selected exactly once");
                // Read actual engine state before automatic GameSession.Awake.
                // Supplying this observation flag NEVER changes pacing or Ready.
                pacing = new PacingObservationReport {
                    isolationFrame = Time.frameCount,
                    initialVSyncCount = QualitySettings.vSyncCount,
                    initialTargetFrameRate = Application.targetFrameRate,
                    initialQualityLevel = QualitySettings.GetQualityLevel(),
                    graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                    renderingThreadingMode = SystemInfo.renderingThreadingMode.ToString()
                };
                Application.runInBackground = true;
                ownsOutput = true;
                File.WriteAllText(Path.Combine(output, "qa-pacing-isolation.json"), JsonUtility.ToJson(pacing, true));
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
            instance = root.AddComponent<ActualScopedSessionNativeProof800>();
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
                police=session?session.BootPatrolPrepared:0,ready=session&&session.BootReady,failed=session&&session.BootFailed,
                vSyncCount=QualitySettings.vSyncCount,targetFrameRate=Application.targetFrameRate,
                applicationFocused=Application.isFocused,actualQualityLevel=QualitySettings.GetQualityLevel() }));
            events?.Flush();
        }
        void Rendered(ScriptableRenderContext context,Camera camera)
        {
            if (done || current == null || !session) return;
            bool clearing = camera.name.StartsWith("Loading clear only;",StringComparison.Ordinal) &&
                camera.transform.IsChildOf(session.transform);
            bool driving = session.View && camera == session.View.GetComponent<Camera>();
            if (!clearing && !driving) return;
            try
            {
                Need(pacing != null && QualitySettings.vSyncCount == 0 &&
                    Application.targetFrameRate == ExpectedGameTargetFramesPerSecond &&
                    QualitySettings.GetQualityLevel() == pacing.initialQualityLevel,
                    "Real GameSession supplied vSync0/target120 before qualified callback; observer has no pacing setter");
                pacing.qualifiedCallbackSamples++;
                if (!pacing.firstCallbackObserved)
                {
                    pacing.firstCallbackObserved = true;
                    pacing.firstCallbackFrame = Time.frameCount;
                    pacing.firstCallbackVSyncCount = QualitySettings.vSyncCount;
                    pacing.firstCallbackTargetFrameRate = Application.targetFrameRate;
                    pacing.firstCallbackQualityLevel = QualitySettings.GetQualityLevel();
                    pacing.actualGameStartupPacingObserved = true;
                    File.WriteAllText(Path.Combine(output,"qa-pacing-first-callback.json"),JsonUtility.ToJson(pacing,true));
                }
                if (clearing)
                {
                    current.clearingRenders++;
                    if (current.clearingRenders <= 2) Record("actual-clearing-render",camera);
                }
                if (driving)
                {
                    current.drivingRenders++;
                    if (current.drivingRenders <= 2) Record("actual-drive-render",camera);
                }
            }
            catch (Exception error) { Finish(false,error.ToString()); }
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
        /// <summary>Pure diagnostic admission: a complete source ledger with real mixed
        /// unfinished/Emitted entries, never a terminal-only or unknown-status ledger.
        /// Unity objects and ownership are independently required by the caller.</summary>
        internal static bool HasMixedPartialTownLedger(ScopedTownLedger800 ledger)
        {
            if (ledger == null || ledger.buildings == null || ledger.buildings.Count == 0)
                return false;
            Need(ledger.buildings.Count == ExpectedTownEntries, "Complete1793 source-entry ledger required");
            Need(ledger.buildings.All(row => row != null &&
                (row.status == "Queued" || row.status == "Pending" || row.status == "Emitted" || row.status == "Suppressed")),
                "Only actual producer statuses admit a partial checkpoint");
            return ledger.buildings.Any(row => row.status == "Emitted") &&
                ledger.buildings.Any(row => row.status == "Pending" || row.status == "Queued");
        }

        /// <summary>
        /// Predicate over the actual unpublished Town producer, never a guessed
        /// elapsed delay or caller-supplied readiness flag. Returning false retains
        /// the bounded existing native timeout; a completely built Town refuses.
        /// </summary>
        bool HasPartialTownCheckpoint()
        {
            Need(session && !session.BootFailed && !session.BootReady,
                "Partial Town checkpoint must precede failure/Ready");
            var world = session.World;
            if (!world || world.ScopedGround800 == null || !world.ScopedGround800.IsCurrent || !world.GeographicWorld)
                return false;
            var town = world.GeographicWorld;
            Need(town.CurrentTownPublication800 == null,
                "Partial Town checkpoint missed: a complete publication already exists");
            Need(!session.Car && session.ActualVehicles == null &&
                session.BootTrafficPrepared == 0 && session.BootPatrolPrepared == 0,
                "Partial Town is before genuine actor factories");
            string json = town.ExportScopedTownLedger800();
            var ledger = JsonUtility.FromJson<ScopedTownLedger800>(json);
            if (!HasMixedPartialTownLedger(ledger))
                return false; // Pure prep or a terminal-only ledger is not partial physical emission.
            var roots = town.GetComponentsInChildren<Transform>(true)
                .Where(item => item.parent == town.transform && item.name == PartialTownRootName).ToArray();
            Need(roots.Length == 1 && !roots[0].gameObject.activeSelf && !roots[0].gameObject.activeInHierarchy,
                "Actual partially built Town subtree remains inactive/unpublished");
            var owners = roots[0].GetComponentsInChildren<GeneratedMeshOwner>(true);
            var colliders = roots[0].GetComponentsInChildren<MeshCollider>(true);
            // Require real physical partial output, not just a worker/empty root.
            return owners.Any(owner => owner.Mesh) && colliders.Any(collider =>
                collider.sharedMesh && collider.GetComponent<GeneratedMeshOwner>()?.Mesh == collider.sharedMesh);
        }

        /// <summary>
        /// Cancels ONLY the genuine automatic GameSession via its public API.
        /// GeneratedMeshOwner is the ownership authority for mesh destruction;
        /// unowned/shared meshes and shared materials must survive retirement.
        /// All references are captured synchronously with the accepted predicate.
        /// </summary>
        IEnumerator CancelPartialTownAndRetry()
        {
            yield return Wait(HasPartialTownCheckpoint, "Actual mixed emitted/pending Town with owned physical output");
            Need(HasPartialTownCheckpoint() && current.clearingRenders > 0,
                "Same actual partial checkpoint after a genuine loading-camera render");
            retiredPartialWorld = session.World;
            retiredPartialTown = retiredPartialWorld.GeographicWorld;
            retiredPartialGround = retiredPartialWorld.ScopedGround800;
            retiredPartialRoot = retiredPartialTown.GetComponentsInChildren<Transform>(true)
                .Single(item => item.parent == retiredPartialTown.transform && item.name == PartialTownRootName).gameObject;
            // A just-suppressed body may still await deferred object destruction;
            // its owner already released/nullified Mesh, so it owns no live buffer.
            var owners = retiredPartialRoot.GetComponentsInChildren<GeneratedMeshOwner>(true)
                .Where(owner => owner.Mesh).ToArray();
            partialOwnedMeshes = owners.Select(owner => owner.Mesh).Where(mesh => mesh).Distinct().ToArray();
            partialColliders = retiredPartialRoot.GetComponentsInChildren<MeshCollider>(true);
            partialBorrowedMeshes = retiredPartialRoot.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => !filter.GetComponent<GeneratedMeshOwner>())
                .Select(filter => filter.sharedMesh).Where(mesh => mesh).Distinct().ToArray();
            partialSharedMaterials = retiredPartialRoot.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct().ToArray();
            Need(partialOwnedMeshes.Length > 0 && partialColliders.Length > 0 && partialSharedMaterials.Length > 0,
                "Actual generated mesh/collider/material references captured before cancellation");
            var ownership = new List<PartialOwnedResource>();
            foreach (var owner in owners)
            {
                var mesh = owner.Mesh;
                var filter = owner.GetComponent<MeshFilter>();
                Need(mesh && filter && filter.sharedMesh == mesh,
                    "Every captured generated owner owns its actual MeshFilter buffer");
                var collider = owner.GetComponent<MeshCollider>();
                Need(!collider || collider.sharedMesh == mesh, "Actual partial collider uses the exact owned generated mesh");
                ownership.Add(new PartialOwnedResource {
                    ownerEntityId = owner.GetEntityId().ToString(), meshEntityId = mesh.GetEntityId().ToString(),
                    colliderEntityId = collider ? collider.GetEntityId().ToString() : null,
                    name = owner.name, vertices = mesh.vertexCount,
                    triangles = checked((int)(mesh.GetIndexCount(0) / 3))
                });
            }
            string ledgerJson = retiredPartialTown.ExportScopedTownLedger800();
            var ledger = JsonUtility.FromJson<ScopedTownLedger800>(ledgerJson);
            Need(ledger.rawBefore == ledger.rawAfter && ledger.rawDocumentDigest == retiredPartialGround.RawDocumentDigest &&
                ledger.sourceDigest == retiredPartialGround.SourceDigest, "Actual partial ledger/raw source remains bound and unchanged");
            string ledgerPath = Path.Combine(output, "partial-town-before-cancel.json");
            File.WriteAllText(ledgerPath, ledgerJson);
            report.partialTown = new PartialTownReport {
                totalRecords = ledger.buildings.Count,
                emittedRecords = ledger.buildings.Count(row => row.status == "Emitted"),
                pendingRecords = ledger.buildings.Count(row => row.status == "Pending" || row.status == "Queued"),
                suppressedRecords = ledger.buildings.Count(row => row.status == "Suppressed"),
                unpublished = retiredPartialTown.CurrentTownPublication800 == null,
                inactiveRoot = !retiredPartialRoot.activeSelf && !retiredPartialRoot.activeInHierarchy,
                noActorFactories = !session.Car && session.ActualVehicles == null && session.BootTrafficPrepared == 0 && session.BootPatrolPrepared == 0,
                groundCurrentBeforeCancel = retiredPartialGround.IsCurrent,
                groundOwnerHandle = retiredPartialGround.RuntimeOwnerHandle,
                groundGeneration = retiredPartialGround.Generation,
                rawDocumentDigest = retiredPartialGround.RawDocumentDigest, sourceDigest = retiredPartialGround.SourceDigest,
                groundGeometryDigest = retiredPartialGround.GeometryDigest,
                partialLedgerSHA256 = Hash(ledgerPath), generatedMeshes = partialOwnedMeshes.Length,
                colliders = partialColliders.Length, borrowedMeshes = partialBorrowedMeshes.Length,
                sharedMaterials = partialSharedMaterials.Length, ownedResources = ownership.ToArray()
            };
            Need(report.partialTown.emittedRecords > 0 && report.partialTown.pendingRecords > 0 &&
                report.partialTown.totalRecords == ExpectedTownEntries && report.partialTown.unpublished &&
                report.partialTown.inactiveRoot && report.partialTown.noActorFactories && report.partialTown.groundCurrentBeforeCancel,
                "Accepted complete mixed source/emission witness immediately before public cancellation");
            current.clearedBeforeReady = true;
            Record("actual-partial-town-before-public-cancel");
            session.CancelBoot();
            Record("actual-partial-town-public-cancel");
            yield return null;
            yield return null; // Ordinary Unity deferred native-object destruction.
            Need(session.BootFailed && !session.BootReady && !session.World && !session.Car &&
                session.ActualVehicles == null && session.BootTrafficPrepared == 0 && session.BootPatrolPrepared == 0,
                "Public cancellation leaves no actor cohort or incomplete world");
            partialRetirementObserved = true;
            VerifyPartialRetirement();
            report.partialTown.revokedAndDestroyed = true;
            current.oldOwnerRetired = true;
            current.actualWorldDestroyed = true;
            current.status = "PASS_ACTUAL_PARTIAL_TOWN_CANCEL";
            yield return Capture(current.name);
            GameSession previous = session;
            LocationLoading.Retry();
            yield return FindAutomatic(previous, "traffic-cancel");
            Need(!previous, "Cancelled partial-Town session retired by public retry");
            yield return Wait(() => session.BootFailed || session.BootReady ||
                session.World && session.World.ScopedGround800 != null, "Actual fresh retry physical owner");
            Need(!session.BootFailed && !session.BootReady && session.World != retiredPartialWorld &&
                session.World.ScopedGround800.IsCurrent &&
                session.World.ScopedGround800.RuntimeOwnerHandle != report.partialTown.groundOwnerHandle,
                "Retry creates a genuinely distinct current physical owner; not equal-hash reuse");
            report.partialTown.retryGroundOwnerHandle = session.World.ScopedGround800.RuntimeOwnerHandle;
            report.partialTown.retryGroundGeneration = session.World.ScopedGround800.Generation;
            report.partialTown.newGenerationOwner = true;
            VerifyPartialRetirement();
        }

        // Called again after all later cases: a retired producer/resource must not
        // reappear while new owner generations complete. Shared objects stay live.
        void VerifyPartialRetirement()
        {
            Need(partialRetirementObserved && !retiredPartialWorld && !retiredPartialTown && !retiredPartialRoot &&
                !retiredPartialGround.IsCurrent && partialOwnedMeshes.All(mesh => !mesh) &&
                partialColliders.All(collider => !collider) && partialBorrowedMeshes.All(mesh => mesh) &&
                partialSharedMaterials.All(material => material),
                "Retired partial owner/resources remain dead; borrowed meshes/shared materials survive");
            report.partialTown.noLatePublicationOrResourceResurrection = true;
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
            yield return FindAutomatic(null,"partial-town-cancel");
            yield return CancelPartialTownAndRetry();
            yield return CancelAndRetry(5,0,"police-cancel");
            yield return CancelAndRetry(24,1,"ready-full28-retire");
            yield return Wait(()=>session.BootReady||session.BootFailed,"Actual complete scoped boot");
            Need(session.BootReady&&!session.BootFailed&&session.Menu&&Time.timeScale==0,"Actual ready closed reader mode");
            yield return Wait(()=>session.BootDriveCameraRenderConfirmed&&current.drivingRenders>0,"Actual render-confirmed camera handoff");
            Need(current.clearingRenders>0,"Real clear rendered before actual ready view");current.clearedBeforeReady=true;
            yield return Capture("actual-ready-full28");
            Need(session.Updates&&!session.Updates.Enabled,"Actual updater disabled in isolated profile");report.updaterDisabled=true;
            phaseReading=true;
            yield return ActualScopedSessionPhaseProof800.Run(session,value=>report.phase=value);
            phaseReading=false;
            Need(report.rawPhysicalFrames>=3,"Three independently recorded actual full28 frames");
            Need(report.phase!=null&&report.phase.FullRosterFrames>=3&&report.phase.ActualVehicles==28&&
                report.phase.SupportedPhysical==28&&report.phase.UnboundPhysical==0&&report.phase.PublicationRetired&&
                report.phase.GroundRetired&&report.phase.HeldSnapshotRefused&&report.phase.FreshCaptureRefused,"Actual full28 ordered read and issuer retirement");
            current.oldOwnerRetired=true;current.status="PASS_ACTUAL_FULL28_PHASE_RETIRE";
            Record("actual-full28-phase-and-retirement");
            VerifyPartialRetirement();
            report.fixedDeltaTimeSeconds=Time.fixedDeltaTime;report.timeScale=Time.timeScale;
            Need(!GameSession.SuppressAutomaticLaunch,"Automatic bootstrap stayed enabled through all four actual boots");
            report.automaticBootstrap=true;
            // GameSession.OnApplicationQuit legitimately saves a READY career.
            // Retire the actual scene FIRST, never patch Save or restore bytes.
            // Suppression begins only after all positive tests are complete, to
            // prevent this cleanup reload from creating an unwanted fifth boot.
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
        void Finish(bool passed,string error)
        {
            if(done)return;done=true;RenderPipelineManager.endCameraRendering-=Rendered;
            events?.Flush();events?.Dispose();events=null;physicalReader.Dispose();
            report.error=error;report.runtimeErrors=runtimeErrors;report.buildGUID=Application.buildGUID;report.bindingSHA256=bindingHash;
            report.startupPacing = pacing;
            // Passive evidence only. Neither failure nor success restores/sets
            // pacing; the real game owns that policy throughout its lifetime.
            passed &= pacing != null && pacing.firstCallbackObserved &&
                pacing.actualGameStartupPacingObserved && !pacing.observerSetsOrRestoresPacing &&
                pacing.qualifiedCallbackSamples > 0;
            // automaticBootstrap records the verified four-boot positive window,
            // before the explicit cleanup-only suppression latch. Never forge it here.
            report.isolatedProfileUnchanged=profile!=null&&File.Exists(profile)&&Hash(profile)==profileHash;
            try{report.exactInputsUnchanged=binding!=null&&ExactCurrentInputs();}catch{report.exactInputsUnchanged=false;}
            report.cases=cases.ToArray();report.captures=captures.ToArray();
            if(ownsOutput)
            {
                report.eventsSHA256=Hash(Path.Combine(output,"render-events.jsonl"));
                report.savedProfile=new FilePin{path="profile.json",bytes=new FileInfo(profile).Length,sha256=Hash(profile)};
                report.status=passed&&runtimeErrors==0&&report.automaticBootstrap&&report.isolatedProfileUnchanged&&report.exactInputsUnchanged?
                    "PASS_ACTUAL_SCOPED_SESSION_READER_ONLY":"FAIL";
                File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(report,true));
            }
            Application.Quit(report.status=="PASS_ACTUAL_SCOPED_SESSION_READER_ONLY"?0:1);
        }
        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering-=Rendered;Application.logMessageReceived-=Logged;events?.Dispose();physicalReader.Dispose();
        }
    }
}
