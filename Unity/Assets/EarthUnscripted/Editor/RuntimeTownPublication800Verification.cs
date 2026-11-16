using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using EarthUnscripted.Core800;
using EarthUnscripted.TerrainDraft.Core800;

namespace EarthUnscripted.Editor
{
    /// <summary>
    /// Explicit isolated PLAY MODE verification. Actual runtime MonoBehaviour
    /// lifecycle runs; no SendMessage/Editor simulation of Update or fake issuers.
    /// Root schedules this process after other engine/GUI work. No screenshots:
    /// existing Town5 captures/export already cover the unchanged renderer.
    /// </summary>
    [InitializeOnLoad]
    public static class RuntimeTownPublication800Verification
    {
        private const string SessionKey = "EUActualRuntimeTownPublication800";
        private const string ClearCameraName = "RuntimeTown800 proof clear camera";
        private const string OutputFlag = "-earthUnscriptedRuntimeTown800Output";
        private const double TimeoutSeconds = 900;
        private const double RayToleranceMetres = .0002;
        // Distinct frames, not an assumed Object.Destroy completion frame.
        private const int MaximumInvalidationFrames = 8;
        private const double InvalidationDeadlineSeconds = 5;
        private const int MaximumJournalRows = 128;
        private static readonly JArray invalidationJournal = new JArray();
        private static string output, cookedPath, sourceContext, realCareer, careerHash, cookedHash, packageHash;
        private static double deadline;
        private static int lastFrame = -1, checks;
        private static IEnumerator sequence;
        private static readonly List<string> errors = new List<string>();
        private static readonly List<GameObject> objects = new List<GameObject>();
        private static readonly List<Material> materials = new List<Material>();
        private static readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private static JObject receipt = new JObject();
        private static ReviewedCanonicalRaw13Input input;
        private static PreparedWorldMeshPlan800 plan, equalDifferentPlan;
        private static UploadedGroundBuild800 currentBuild;
        private static UploadedGroundOwner800 diagnosticOwner;
        private static Material road, terrain;
        private static Mesh primitiveMesh;
        private static bool originalAutoSync, finishing;
        static RuntimeTownPublication800Verification()
        {
            if (!SessionState.GetBool(SessionKey, false))
                return;
            output = SessionState.GetString(SessionKey + "Output", "");
            cookedPath = SessionState.GetString(SessionKey + "Cooked", "");
            sourceContext = SessionState.GetString(SessionKey + "SourceContext", "");
            realCareer = SessionState.GetString(SessionKey + "Career", "");
            careerHash = SessionState.GetString(SessionKey + "CareerHash", "");
            originalAutoSync = SessionState.GetBool(SessionKey + "AutoSync", false);
            Isolate();
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void SuppressNormalBootstrap()
        {
            if (SessionState.GetBool(SessionKey, false))
                Isolate();
        }

        private static void Isolate()
        {
            GameSession.SuppressAutomaticLaunch = true;
            PlayerProfile.StorageOverride = Path.Combine(output, "isolated", "profile.json");
            RegionCache.OverrideFolder = Path.Combine(output, "isolated", "Regions");
        }

        public static void Run()
        {
            output = Path.GetFullPath(Argument(OutputFlag));
            cookedPath = Path.GetFullPath(Argument("-earthUnscriptedRuntimeTown800Cooked"));
            sourceContext = Path.GetFullPath(Argument("-earthUnscriptedRuntimeTown800SourceContext"));
            Require(output.StartsWith("/private/tmp/rr019-runtime-town800-", StringComparison.Ordinal) && !Directory.Exists(output), "Fresh owned proof output; earlier failures preserved");
            Require(!EditorApplication.isPlaying && UnityEngine.SceneManagement.SceneManager.sceneCount == 1 && !UnityEngine.SceneManagement.SceneManager.GetSceneAt(0).isDirty && UnityEngine.SceneManagement.SceneManager.GetSceneAt(0).rootCount == 0, "Clean empty Editor scene; do not replace user's scene");
            Directory.CreateDirectory(output);
            realCareer = Path.Combine(Application.persistentDataPath, "earth-unscripted-save.json");
            careerHash = Hash(realCareer);
            SessionState.SetString(SessionKey + "Output", output);
            SessionState.SetString(SessionKey + "Cooked", cookedPath);
            SessionState.SetString(SessionKey + "SourceContext", sourceContext);
            SessionState.SetString(SessionKey + "Career", realCareer);
            SessionState.SetString(SessionKey + "CareerHash", careerHash);
            SessionState.SetBool(SessionKey + "AutoSync", Physics.autoSyncTransforms);
            SessionState.SetBool(SessionKey, true);
            Isolate();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Present a clear URP Base camera before PlayMode or any asynchronous work.
            // No MainCamera tag, audio listener, post-processing or production camera changes.
            var clearCameraObject = new GameObject(ClearCameraName);
            var clearCamera = clearCameraObject.AddComponent<Camera>();
            clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(.055f, .075f, .10f, 1);
            clearCamera.cullingMask = 0;
            var cameraData = clearCameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = false;
            EditorApplication.EnterPlaymode();
        }

        private static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, key);
            if (at < 0 || at + 1 >= args.Length)
                throw new ArgumentException(key);
            return args[at + 1];
        }

        private static string Hash(string path) => File.Exists(path) ? World13DocumentCodec.Hash(File.ReadAllBytes(path)) : "missing";
        private static void Require(bool condition, string reason)
        {
            if (!condition)
                throw new InvalidDataException(reason);
            checks++;
        }

        private static void Log(string text, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 20)
                errors.Add(text + "\n" + stack);
        }

        private static void Tick()
        {
            if (finishing)
                return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Runtime town proof deadline");
                if (!EditorApplication.isPlaying || lastFrame == Time.frameCount)
                    return;
                lastFrame = Time.frameCount;
                Require(!UnityEngine.Object.FindAnyObjectByType<GameSession>(), "No hidden normal GameSession");
                if (sequence == null)
                    sequence = Cases();
                if (!sequence.MoveNext())
                    Finish(null);
            }
            catch (Exception exception)
            {
                Finish(exception);
            }
        }

        private static IEnumerator Cases()
        {
            receipt["status"] = "IN_PROGRESS_NOT_ACCEPTANCE";
            receipt["scope"] = "Runtime owner3 and typed town development subset only";
            receipt["wholeWorldAdmission"] = false;
            receipt["registryIssued"] = false;
            var clearCameraObject = GameObject.Find(ClearCameraName);
            Require(clearCameraObject && clearCameraObject.GetComponent<Camera>().enabled &&
                clearCameraObject.GetComponent<UniversalAdditionalCameraData>().renderType == CameraRenderType.Base &&
                clearCameraObject.GetComponent<AudioListener>() == null && !clearCameraObject.CompareTag("MainCamera"),
                "Early clear Base camera exists before worker/first yield without normal audio/input");
            objects.Add(clearCameraObject);
            Physics.autoSyncTransforms = false; // Isolated fixture only; restored in Finish.
            cookedHash = Hash(cookedPath);
            packageHash = Hash(cookedPath + ".eumeta");
            var prepare = Task.Run(() =>
            {
                input = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(sourceContext, cancellation.Token);
                byte[] bytes = input.CopyDocumentBytes(cancellation.Token);
                var proof = VerifiedCoreTerrain.LoadReviewedCooked(cookedPath, bytes, cancellation.Token);
                plan = PreparedWorldMeshPlan800.FromVerifiedCore(FreshSource(), proof, cancellation.Token);
                equalDifferentPlan = PreparedWorldMeshPlan800.FromVerifiedCore(FreshSource(), proof, cancellation.Token);
            }, cancellation.Token);
            while (!prepare.IsCompleted)
                yield return null;
            prepare.GetAwaiter().GetResult();
            Require(plan.FinalBufferDigest == "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf" && plan.PacketCount == 1063 && plan.VertexCount == 317003 && plan.TriangleCount == 533219, "Genuine cooked complete676 capability and exact original buffers");
            receipt["finalBufferDigest"] = plan.FinalBufferDigest;
            receipt["sourceDigest"] = plan.SourceDigest;
            receipt["rawDocumentDigest"] = plan.RawDocumentDigest;
            receipt["packets"] = plan.PacketCount;
            receipt["vertices"] = plan.VertexCount;
            receipt["triangles"] = plan.TriangleCount;
            Require(!ReferenceEquals(plan, equalDifferentPlan) && plan.GeometryDigest == equalDifferentPlan.GeometryDigest, "Real second plan has equal bytes, different owner reference");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader && shader.isSupported, "Actual URP resources");
            road = OwnMaterial(shader, "Borrowed road fixture");
            terrain = OwnMaterial(shader, "Borrowed terrain fixture");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.SetActive(false);
            objects.Add(primitive);
            primitiveMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            // Destroy actual active lifetime host's parent while PHYSICAL child never activated.
            var partialParent = Parent("Partial upload parent");
            UploadedGroundBuild800 unexpected = null;
            var partial = UploadedGroundOwner800.BuildOwned(partialParent.transform, plan, road, terrain, cancellation.Token, () => partialParent && partialParent.activeInHierarchy, result => unexpected = result);
            Mesh[] partialMeshes = null;
            try
            {
                while (partial.MoveNext())
                {
                    partialMeshes = partialParent.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                    if (partialMeshes.Length > 0)
                        break;
                    yield return null;
                }

                Require(partialMeshes != null && partialMeshes.Length > 0 && unexpected == null, "Real partial allocation, no premature lease/control");
                UnityEngine.Object.Destroy(partialParent);
                yield return null;
                bool refused = false;
                try
                {
                    partial.MoveNext();
                }
                catch (OperationCanceledException)
                {
                    refused = true;
                }

                Require(refused && partialMeshes.All(mesh => !mesh) && primitiveMesh && road && terrain, "Actual active-host destruction cleans never-active physical meshes");
            }
            finally
            {
                (partial as IDisposable)?.Dispose();
            }

            Gate("partialInactiveUploadDestroyClean");
            // Every complete owner checks all1063 actual rays INSIDE readiness callback.
            foreach (var value in BuildActualOwner(road, terrain, true))
                yield return value;
            var lease = currentBuild.Lease;
            var legacyWorld = World("Legacy callback route has no semantic authority");
            var legacy = legacyWorld.BuildScopedTown800(FreshSource(), plan, cancellation.Token, () => lease.IsCurrent);
            try
            {
                while (legacy.MoveNext())
                    yield return null;
            }
            finally
            {
                (legacy as IDisposable)?.Dispose();
            }

            Require(legacyWorld.CurrentTownPublication800 == null && legacyWorld.GetComponentsInChildren<MeshCollider>().Length > 0, "Actual successful Func route NEVER issues semantic publication");
            Gate("legacyFuncNeverIssues");
            var normal = AuthoredNormalFixture();
            legacyWorld.Initialize(normal, road, true);
            VerifyDefaultNormalGeometry(legacyWorld, normal);
            UnityEngine.Object.Destroy(legacyWorld.gameObject);
            yield return null;
            var staleWorld = World("Suspended typed town replacement");
            var pendingTown = staleWorld.BuildScopedTown800(FreshSource(), plan, lease, cancellation.Token);
            Mesh[] staleMeshes = null;
            try
            {
                while (pendingTown.MoveNext())
                {
                    staleMeshes = staleWorld.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                    if (staleMeshes.Length > 0)
                        break;
                    yield return null;
                }

                Require(staleMeshes != null && staleMeshes.Length > 0 && staleWorld.CurrentTownPublication800 == null, "Actual typed partial root cannot publish");
                var replacement = AuthoredNormalFixture();
                staleWorld.Initialize(replacement, road, true);
                int replacementGeneration = staleWorld.SurfaceGeneration;
                bool staleRefused = false;
                try
                {
                    pendingTown.MoveNext();
                }
                catch (OperationCanceledException)
                {
                    staleRefused = true;
                }

                Require(staleRefused && ReferenceEquals(staleWorld.Region, replacement) && staleWorld.SurfaceGeneration == replacementGeneration, "Stale typed iterator cannot reset replacement normal world");
            }
            finally
            {
                (pendingTown as IDisposable)?.Dispose();
            }

            yield return null;
            Require(staleMeshes.All(mesh => !mesh), "Partial typed generated meshes released");
            Gate("staleReplacementPreserved");
            var source = FreshSource();
            var sourceBefore = source.Write();
            var wrongWorld = World("Wrong actual plan reference");
            bool mismatch = false;
            var wrong = wrongWorld.BuildScopedTown800(source, equalDifferentPlan, lease);
            try
            {
                wrong.MoveNext();
            }
            catch (InvalidDataException)
            {
                mismatch = true;
            }
            finally
            {
                (wrong as IDisposable)?.Dispose();
            }

            Require(mismatch && wrongWorld.CurrentTownPublication800 == null && source.Write().SequenceEqual(sourceBefore) && lease.IsCurrent, "Equal digests cannot substitute a different actual plan or consume source on refusal");
            Gate("referenceMismatchRefused");
            UnityEngine.Object.Destroy(wrongWorld.gameObject);
            yield return null;
            // Published world stays pinned after a late PREPARATION cancellation.
            // BuildActualOwner's separate cancellation token is tested in that helper.
            var changedWorld = World("Source bind mutation refusal");
            TownSemanticPublication800 changedPublication = null;
            var changedTown = changedWorld.BuildScopedTown800(FreshSource(), plan, lease, cancellation.Token, token => changedPublication = token);
            try
            {
                while (changedTown.MoveNext())
                    yield return null;
            }
            finally
            {
                (changedTown as IDisposable)?.Dispose();
            }

            Require(changedPublication != null && changedPublication.IsCurrent, "Actual typed publication before source-change fixture");
            changedWorld.Region.buildings[0].height += 1;
            Require(!changedPublication.ValidateSourceNow() && changedWorld.CurrentTownPublication800 == null && lease.IsCurrent, "Illegal in-place owned test-copy edit refuses fresh binding and retires town, not physical source owner");
            Gate("freshSourceMutationRefused");
            UnityEngine.Object.Destroy(changedWorld.gameObject);
            yield return null;
            // An actual owned stair mesh receives a finite box while still staged.
            // The production private issuer must capture it, then retire on shape change.
            // This is not a test issuer or a fabricated lease/publication.
            var boxWorld = World("Actual owned stair Box witness");
            TownSemanticPublication800 boxPublication = null;
            BoxCollider ownedStairBox = null;
            var boxTown = boxWorld.BuildScopedTown800(FreshSource(), plan, lease, cancellation.Token, token => boxPublication = token);
            try
            {
                while (boxTown.MoveNext())
                {
                    if (!ownedStairBox)
                    {
                        var stair = boxWorld.GetComponentsInChildren<MeshCollider>(true)
                            .FirstOrDefault(c => c.name == "Supported garden steps");
                        if (stair)
                        {
                            ownedStairBox = stair.gameObject.AddComponent<BoxCollider>();
                            ownedStairBox.center = stair.sharedMesh.bounds.center;
                            var size = stair.sharedMesh.bounds.size;
                            ownedStairBox.size = new Vector3(Mathf.Max(size.x, .01f),
                                Mathf.Max(size.y, .01f), Mathf.Max(size.z, .01f));
                        }
                    }
                    yield return null;
                }
            }
            finally
            {
                (boxTown as IDisposable)?.Dispose();
            }
            Require(ownedStairBox && boxPublication != null && boxPublication.IsCurrent &&
                boxPublication.ValidateSourceNow(), "Actual staged stair Box is included by private typed issuer");
            ownedStairBox.center += new Vector3(.01f, 0, 0);
            Require(!boxPublication.ValidateSourceNow() && boxWorld.CurrentTownPublication800 == null &&
                lease.IsCurrent, "Changed actual Box centre retires semantic town without revoking unchanged ground");
            Gate("actualOwnedBoxMutationRefused");
            UnityEngine.Object.Destroy(boxWorld.gameObject);
            yield return null;
            int groundCallbacks = 0;
            lease.BeforeRevoke((revoked, reason) =>
            {
                groundCallbacks++;
                throw new InvalidOperationException("Expected ground callback fault");
            });
            var world = World("Typed semantic town");
            TownSemanticPublication800 publication = null;
            var town = world.BuildScopedTown800(FreshSource(), plan, lease, cancellation.Token, token => publication = token);
            try
            {
                while (town.MoveNext())
                    yield return null;
            }
            finally
            {
                (town as IDisposable)?.Dispose();
            }

            Require(publication != null && publication.IsCurrent && ReferenceEquals(world.CurrentTownPublication800, publication) && ReferenceEquals(publication.Ground, lease) && publication.SourceBuildingCount == 1793, "Actual active complete1793 source publishes private typed token");
            Gate("typedPublication1793");
            Require(!publication.WholeWorldAdmitted && !publication.ClosedBuildingVolumes, "No safe-world/closed-volume/provider-registry grant");
            byte[] copy = publication.CopySemanticSourceDocument();
            Require(World13DocumentCodec.Hash(copy) == plan.RawDocumentDigest, "Fresh binding returns exact full envelope");
            copy[0] ^= 1;
            Require(publication.ValidateSourceNow(), "Defensive source-copy mutation cannot change owned source");
            File.WriteAllText(Path.Combine(output, "typed-town-ledger.json"), world.ExportScopedTownLedger800());
            int callbacks = 0;
            bool invalidBeforeNotification = false;
            publication.BeforeRevoke((token, reason) =>
            {
                callbacks++;
                invalidBeforeNotification = !token.IsCurrent && world.CurrentTownPublication800 == null;
                throw new InvalidOperationException("Expected typed lifetime fixture callback fault");
            });
            publication.BeforeRevoke((token, reason) => callbacks++);
            var townMeshes = world.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
            Require(townMeshes.Length > 0 && world.GetComponentsInChildren<MeshCollider>().Length > 0, "Actual published walls/stairs exist before retirement");
            var retirementParent = UnityEngine.Object.FindObjectsByType<UploadedGroundOwner800>(FindObjectsSortMode.None).Single().transform.parent.gameObject;
            currentBuild.Control.Revoke("BeforeParentDisable");
            Require(groundCallbacks == 1 && diagnosticOwner.CopyRevocationCallbackErrors().Length == 1, "Ground callback fault does not stop typed town notification/teardown");
            Require(staleWorld.Region != null && staleWorld.CurrentTownPublication800 == null, "Detached stale ground subscription cannot revoke replacement normal world");
            Require(!lease.IsCurrent && !publication.IsCurrent && callbacks == 2 && invalidBeforeNotification && publication.CopyRevocationCallbackErrors().Length == 1 && world.CurrentTownPublication800 == null, "Private semantic token invalidates first; all callbacks despite earlier throw");
            Require(!world.GetComponentsInChildren<MeshCollider>().Any(c => c.gameObject.activeInHierarchy), "Control revocation removes town physics BEFORE caller parent disables");
            Gate("retirementBeforeParentDisable");
            Gate("callbacksAllDelivered");
            retirementParent.SetActive(false);
            yield return null;
            Require(townMeshes.All(mesh => !mesh) && primitiveMesh && road && terrain, "Owned generated meshes destroyed, borrowed shared assets survive");
            UnityEngine.Object.Destroy(world.gameObject);
            UnityEngine.Object.Destroy(staleWorld.gameObject);
            currentBuild = null;
            yield return null;
            // Independent actual owner invalidation cases; no fake leases.
            foreach (bool materialDeath in new[]
            {
                false,
                true
            }

            )
            {
                var caseRoad = OwnMaterial(shader, materialDeath ? "Deliberately dead borrowed material" : "Renderer invalidation material");
                foreach (var value in BuildActualOwner(caseRoad, terrain, false))
                    yield return value;
                var current = currentBuild.Lease;
                var owner = diagnosticOwner;
                var target = owner.GetComponentsInChildren<MeshRenderer>().First(renderer => renderer.sharedMaterial == caseRoad);
                string targetId = target.GetEntityId().ToString(), materialId = caseRoad.GetEntityId().ToString();
                int mutationFrame = Time.frameCount;
                double mutationTime = EditorApplication.timeSinceStartup;
                int passesAtMutation = owner.FullValidationPasses;
                JournalInvalidation("BeforeMutation", materialDeath, target, targetId, caseRoad, materialId, owner, current);
                if (materialDeath)
                    UnityEngine.Object.Destroy(caseRoad); // Native destruction is deferred.
                else
                    target.enabled = false; // Immediate state change.
                JournalInvalidation("AfterMutation", materialDeath, target, targetId, caseRoad, materialId, owner, current);
                int materialGoneFrame = -1;
                bool retiredNormally = false;
                for (int observedFrame = 0; observedFrame < MaximumInvalidationFrames; observedFrame++)
                {
                    JournalInvalidation("BeforeYield", materialDeath, target, targetId, caseRoad, materialId, owner, current);
                    yield return null;
                    bool isCurrent = JournalInvalidation("AfterYield", materialDeath, target, targetId, caseRoad, materialId, owner, current);
                    if (materialDeath && !caseRoad && materialGoneFrame < 0)
                        materialGoneFrame = Time.frameCount;
                    // A destroy request is not a dead native material. Allow a fresh
                    // distinct frame after fake-null observation for the cached poll.
                    bool destructionObserved = !materialDeath || materialGoneFrame >= 0;
                    bool freshPollFrame = Time.frameCount > mutationFrame &&
                        (!materialDeath || Time.frameCount > materialGoneFrame);
                    if (destructionObserved && freshPollFrame && !isCurrent && owner.FullValidationPasses > passesAtMutation)
                    {
                        retiredNormally = true;
                        break;
                    }
                    if (EditorApplication.timeSinceStartup - mutationTime > InvalidationDeadlineSeconds)
                        break;
                }
                if (!retiredNormally)
                {
                    // Diagnostic only AFTER ordinary polling failed; never converts
                    // an ordinary-lifetime failure into acceptance.
                    bool explicitResult = owner.ValidatePhysicalLayoutNow();
                    receipt[materialDeath ? "materialExplicitValidationAfterFailure" : "rendererExplicitValidationAfterFailure"] = explicitResult;
                    JournalInvalidation("ExplicitDiagnosticAfterFailure", materialDeath, target, targetId, caseRoad, materialId, owner, current);
                }
                Require(retiredNormally, materialDeath ?
                    "Actually destroyed borrowed material invalidates through bounded ordinary polling" :
                    "Actually disabled renderer invalidates through bounded ordinary polling");
                Gate(materialDeath ? "deadBorrowedMaterialInvalidated" : "disabledRendererInvalidated");
                currentBuild.Control.Revoke("IdempotentCleanup");
                currentBuild = null;
                yield return null;
            }

            Require(Hash(realCareer) == careerHash && Hash(cookedPath) == cookedHash && Hash(cookedPath + ".eumeta") == packageHash, "Real career and source/cooked artifacts unchanged");
            Require(errors.Count == 0, "No unexpected runtime errors");
            receipt["status"] = "PASS_RUNTIME_OWNER_AND_TYPED_TOWN_NATIVE";
        }

        private static void Gate(string name)
        {
            receipt[name] = true;
            receipt["checksAtLastCompletedGate"] = checks;
            receipt["lastCompletedGate"] = name;
            receipt["progressStatus"] = "IN_PROGRESS_NOT_ACCEPTANCE";
            File.WriteAllText(Path.Combine(output, "receipt-progress.json"), receipt.ToString(Formatting.Indented));
        }

        /// <summary>
        /// Bounded actual observations. IsCurrent is the ordinary cached public
        /// query and may run its once-per-frame poll. Counts show that side effect;
        /// no forced maintenance validation occurs in this journal.
        /// </summary>
        private static bool JournalInvalidation(string step, bool materialDeath, MeshRenderer target,
            string targetId, Material material, string materialId, UploadedGroundOwner800 owner, GroundLease800 lease)
        {
            int before = owner.FullValidationPasses;
            bool isCurrent = lease.IsCurrent;
            if (invalidationJournal.Count >= MaximumJournalRows)
                throw new InvalidDataException("Invalidation journal exhausted");
            invalidationJournal.Add(new JObject
            {
                ["step"] = step,
                ["case"] = materialDeath ? "DeferredMaterialDestroy" : "ImmediateRendererDisable",
                ["materialDeath"] = materialDeath,
                ["frame"] = Time.frameCount,
                ["renderedFrame"] = Time.renderedFrameCount,
                ["fixedTime"] = Time.fixedTime,
                ["realtimeSeconds"] = Time.realtimeSinceStartupAsDouble,
                ["targetNativeId"] = targetId,
                ["targetAlive"] = (bool)target,
                ["targetEnabled"] = target ? (JToken)new JValue(target.enabled) : JValue.CreateNull(),
                ["targetActiveInHierarchy"] = target ? (JToken)new JValue(target.gameObject.activeInHierarchy) : JValue.CreateNull(),
                ["materialNativeId"] = materialId,
                ["materialAlive"] = (bool)material,
                ["ownerNativeAlive"] = (bool)owner,
                ["fullValidationPassesBeforeQuery"] = before,
                ["fullValidationPassesAfterQuery"] = owner.FullValidationPasses,
                ["leaseIsCurrent"] = isCurrent
            });
            File.WriteAllText(Path.Combine(output, "invalidation-journal.json"), invalidationJournal.ToString(Formatting.Indented));
            receipt["invalidationJournalRows"] = invalidationJournal.Count;
            return isCurrent;
        }

        private static IEnumerable<object> BuildActualOwner(Material sharedRoad, Material sharedTerrain, bool lateCancel)
        {
            Require(UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).All(c => !c.gameObject.activeInHierarchy), "No duplicate physical world/synthetic collider contaminates current instance");
            var parent = Parent("Actual runtime owner parent");
            int requestGeneration = 1;
            int expectedRequest = requestGeneration;
            using (var stageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
            {
                var build = UploadedGroundOwner800.BuildOwned(parent.transform, plan, sharedRoad, sharedTerrain, stageCancellation.Token, () => parent && parent.activeInHierarchy && requestGeneration == expectedRequest, result =>
                {
                    currentBuild = result;
                    var actual = UnityEngine.Object.FindObjectsByType<UploadedGroundOwner800>(FindObjectsSortMode.None).Single();
                    diagnosticOwner = actual;
                    var colliders = new HashSet<Collider>(actual.GetComponentsInChildren<MeshCollider>(true));
                    Require(colliders.Count == 1063, "Actual current-instance physical subtree");
                    int frame = Time.frameCount, validationPasses = actual.FullValidationPasses;
                    float fixedTime = Time.fixedTime;
                    foreach (string id in result.Lease.CopyPhysicalPacketIds())
                    {
                        var packet = result.Lease.CopyPhysicalPacket(id);
                        double x = 0, y = 0, z = 0;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int vertex = packet.Indices[corner] * 3;
                            x += packet.OriginX + packet.XYZ[vertex];
                            y += packet.OriginY + packet.XYZ[vertex + 1];
                            z += packet.OriginZ + packet.XYZ[vertex + 2];
                        }

                        x /= 3;
                        y /= 3;
                        z /= 3;
                        bool hit = Physics.Raycast(new Vector3((float)x, (float)y + 2, (float)z), Vector3.down, out var native, 4, ~0, QueryTriggerInteraction.Ignore);
                        Require(hit && colliders.Contains(native.collider) && Math.Abs(native.point.y - y) <= RayToleranceMetres, "Immediate actual collider ray inside completion callback; no extra SyncTransforms");
                    }

                    Require(Time.frameCount == frame && Time.fixedTime == fixedTime && !Physics.autoSyncTransforms && actual.FullValidationPasses == validationPasses, "1063 queries happen before any FixedUpdate/time advance and reuse full-poll cache");
                    Gate("immediate1063RaysBeforeFixedUpdate");
                });
                try
                {
                    while (build.MoveNext())
                        yield return null;
                }
                finally
                {
                    (build as IDisposable)?.Dispose();
                }

                Require(currentBuild != null && currentBuild.Lease.IsCurrent, "Only actual completed build yields lease/control");
                if (lateCancel)
                {
                    stageCancellation.Cancel();
                    yield return null;
                    Require(currentBuild.Lease.IsCurrent, "Late preparation cancellation cannot unpin published world");
                }
            }
        }

        private static GeoRegion AuthoredNormalFixture()
        {
            // This authored test building is beyond the unchanged18m legacy road-bed influence.
            var region = new GeoRegion
            {
                id = "typed-publication-normal-regression",
                minX = -500,
                minZ = -500,
                width = 1000,
                depth = 1000,
                nx = 2,
                nz = 2,
                heights = new float[4],
                landNx = 2,
                landNz = 2,
                landCover = new int[4],
                roads = new[]
                {
                    new GeoRoad
                    {
                        id = "authored",
                        start = "a",
                        end = "b",
                        kind = "residential",
                        width = 4,
                        points = new[]
                        {
                            new Vector3(-20, 0, 0),
                            new Vector3(20, 0, 0)
                        }
                    }
                },
                waters = Array.Empty<GeoWater>(),
                buildings = new[]
                {
                    new GeoBuilding
                    {
                        id = "normal-building",
                        kind = "building",
                        roof = "flat",
                        height = 6,
                        points = new[]
                        {
                            new Vector3(2, 0, 40),
                            new Vector3(6, 0, 40),
                            new Vector3(6, 0, 44),
                            new Vector3(2, 0, 44),
                            new Vector3(2, 0, 40)
                        }
                    }
                }
            };
            region.Prepare();
            return region;
        }

        private static void VerifyDefaultNormalGeometry(GeoWorld world, GeoRegion region)
        {
            string before = WorldFieldJson.Write(region);
            typeof(GeoWorld).GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(world, new object[] { Vector2Int.zero, "TypedPublicationNormalRegression" });
            var collider = world.GetComponentsInChildren<MeshCollider>(true).Single(c => c.name.StartsWith("Building normal-building", StringComparison.Ordinal));
            var vertices = collider.sharedMesh.vertices;
            Require(vertices.Length == 16 && collider.sharedMesh.triangles.Length == 24, "Existing default four-wall16V/8tri geometry");
            for (int i = 0; i < vertices.Length; i += 4)
                Require(Math.Abs(vertices[i].y + .88f) < 1e-6 && Math.Abs(vertices[i + 1].y + .88f) < 1e-6 && Math.Abs(vertices[i + 2].y - 6.12f) < 1e-6 && Math.Abs(vertices[i + 3].y - 6.12f) < 1e-6, "Hand-computed normal floor/base/top preserved");
            Require(WorldFieldJson.Write(region) == before && world.CurrentTownPublication800 == null, "Normal rendering does not mutate source or issue typed publication");
            Gate("normalDefaultRegression");
        }

        private static CanonicalRaw13Document FreshSource() => CanonicalRaw13Codec.Read(input, input.CopyDocumentBytes());
        private static Material OwnMaterial(Shader shader, string name)
        {
            var material = new Material(shader)
            {
                name = name
            };
            materials.Add(material);
            return material;
        }

        private static GameObject Parent(string name)
        {
            var parent = new GameObject(name);
            objects.Add(parent);
            return parent;
        }

        private static GeoWorld World(string name) => Parent(name).AddComponent<GeoWorld>();
        private static void Finish(Exception failure)
        {
            if (finishing)
                return;
            finishing = true;
            EditorApplication.update -= Tick;
            cancellation.Cancel();
            try
            {
                (sequence as IDisposable)?.Dispose();
                currentBuild?.Control.Revoke("ProofFinally");
            }
            catch (Exception exception)
            {
                failure = failure ?? exception;
            }

            foreach (var gameObject in objects)
                if (gameObject)
                    UnityEngine.Object.Destroy(gameObject);
            foreach (var material in materials)
                if (material)
                    UnityEngine.Object.Destroy(material);
            Physics.autoSyncTransforms = originalAutoSync;
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= Log;
            receipt["status"] = failure == null ? receipt["status"] : "FAIL";
            if (failure != null)
                receipt["failure"] = failure.ToString();
            receipt["progressStatus"] = failure == null ? "COMPLETED" : "FAILED";
            receipt["checks"] = checks;
            receipt["runtimeErrors"] = new JArray(errors);
            receipt["realCareerUnchanged"] = Hash(realCareer) == careerHash;
            receipt["cookedUnchanged"] = Hash(cookedPath) == cookedHash && Hash(cookedPath + ".eumeta") == packageHash;
            if (Directory.Exists(output))
                File.WriteAllText(Path.Combine(output, "receipt.json"), receipt.ToString(Formatting.Indented));
            int exit = failure == null && errors.Count == 0 && receipt.Value<bool>("realCareerUnchanged") && receipt.Value<bool>("cookedUnchanged") ? 0 : 1;
            EditorApplication.Exit(exit);
        }
    }
}
