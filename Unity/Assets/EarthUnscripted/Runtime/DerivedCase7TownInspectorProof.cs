using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using EarthUnscripted.Core800;
using EarthUnscripted.TerrainDraft.Core800;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit actorless PlayMode inspector, launched only by the isolated Editor
    /// coordinator. It uses actual Runtime request/ground/Town issuers, final native
    /// buffers, real PhysX and URP frames. No normal boot or path/driving authority.
    /// </summary>
    public sealed partial class DerivedCase7TownInspectorProof : MonoBehaviour
    {
        public const string Recipe = "ReviewedCase7R2/ActualGroundTownInspector/2";
        public const string ClearCameraName = "Derived Case7 early Base clear camera";
        const double TimeoutSeconds = 900;
        const double RayToleranceMetres = .0002;
        const int RayBatch = 2048, ExpectedSources = 1793;
        // Vertical-only probes in metres, centred on the exact expected surface.
        const float FaceRayStartMetres = 1, FaceRayLengthMetres = 2;
        const int CaptureWidth = 1920, CaptureHeight = 1080;
        const int MaximumDiagnosticBytes = 64 * 1024 * 1024;
        // Fixed original-array positions plus exact existing raw IDs; never infer provider identity from an ordinal.
        static readonly int[] TargetIndices =
        {
            1697,
            1005
        };
        static readonly string[] TargetIds =
        {
            "os-F8B1F936-0537-449A-B9E4-90DDA82669AE-p72221e4a90078e5ea19c-0",
            "os-87A8F192-726B-484D-8117-51F624AD46E8-pfc6fbd1655527f5792f6-0"
        };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<string> errors = new List<string>();
        readonly JObject receipt = new JObject();
        readonly JArray views = new JArray();
        UploadedGroundBuild800 ground;
        GeoWorld town;
        Dictionary<string, string[]> packetRoads;
        HashSet<Collider> physicalColliders;
        PreparedWorldMeshPlan800 plan;
        GameObject physicalParent;
        Material roadMaterial, terrainMaterial;
        Camera camera;
        RenderTexture target;
        IDisposable townSubscription;
        Action<bool, string> completed;
        string output, sourceContext, regionalRoot;
        double began;
        int checks, renderedFrame = -1, nativeFaceRays, immediateReadyRays;
        BuildingFacadeChunk.FrameStatistics renderedFacades;
        bool ending, sourceCurrent = true, originalAutoSync, beganProof;
        float originalTimeScale;
        /// <summary>Calling coordinator already isolated/suppressed bootstrap before PlayMode.</summary>
        public void Begin(string ownedOutput, string originalContext, string reviewedRegionalRoot, Action<bool, string> finished)
        {
            if (!Application.isPlaying || !GameSession.SuppressAutomaticLaunch || string.IsNullOrEmpty(PlayerProfile.StorageOverride) || !ownedOutput.StartsWith("/private/tmp/rr019-derived-town-inspector-", StringComparison.Ordinal) || !Directory.Exists(ownedOutput) || FindAnyObjectByType<GameSession>() || finished == null)
                throw new InvalidOperationException("Explicit isolated actorless PlayMode coordinator required");
            output = ownedOutput;
            sourceContext = originalContext;
            regionalRoot = reviewedRegionalRoot;
            completed = finished;
            originalAutoSync = Physics.autoSyncTransforms;
            originalTimeScale = Time.timeScale;
            began = Time.realtimeSinceStartupAsDouble;
            beganProof = true;
            Application.logMessageReceived += Logged;
            RenderPipelineManager.endCameraRendering += Rendered;
            receipt["recipe"] = Recipe;
            receipt["status"] = "IN_PROGRESS_NOT_ACCEPTANCE";
            receipt["motionAdmitted"] = false;
            receipt["supportedPathIntervalsIssued"] = false;
            receipt["visualReviewPending"] = true;
            receipt["witnessContractRevision"] = "LargestExistingAreaOwnColliderRegistration+WholeOwnedGroundCoverage/2";
            receipt["individualTriangleCookingRetentionClaimed"] = false;
            receipt["collisionPolicy"] = "WALLS_ONLY";
            StartCoroutine(Guard(Inspect()));
        }

        void Need(bool okay, string reason)
        {
            if (!okay)
                throw new InvalidDataException(reason);
            checks++;
        }

        void Logged(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 32)
                errors.Add(message + "\n" + stack);
        }

        void Rendered(ScriptableRenderContext context, Camera rendered)
        {
            if (rendered == camera)
            {
                renderedFrame = Time.frameCount;
                renderedFacades = BuildingFacadeChunk.Statistics;
            }
        }

        void Update()
        {
            if (!ending && beganProof && Time.realtimeSinceStartupAsDouble - began > TimeoutSeconds)
                Finish(new TimeoutException("900-second derived inspector watchdog"));
        }

        IEnumerator Guard(IEnumerator job)
        {
            try
            {
                while (!ending)
                {
                    bool more = false;
                    object next = null;
                    Exception error = null;
                    try
                    {
                        more = job.MoveNext();
                        if (more)
                            next = job.Current;
                    }
                    catch (Exception caught)
                    {
                        error = caught;
                    }

                    if (error != null)
                    {
                        Finish(error);
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
            finally
            {
                (job as IDisposable)?.Dispose();
            }
        }

        IEnumerator Inspect()
        {
            Need(GameObject.Find(ClearCameraName)?.GetComponent<Camera>()is Camera early && early.enabled && early.cullingMask == 0 && !early.GetComponent<AudioListener>(), "Early clear camera before first async yield");
            Need(!FindAnyObjectByType<GameSession>() && FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length == 0, "No normal session or moving actors");
            objects.Add(GameObject.Find(ClearCameraName));
            Time.timeScale = 0;
            // Isolated proof only; production settings/assets/timestep are not changed.
            // The real owner must sync on activation BEFORE immediate callback rays.
            Physics.autoSyncTransforms = false;
            var prepare = Task.Run(() =>
            {
                var source = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(sourceContext, cancellation.Token);
                return ReviewedDerivedCore800Request.CaptureReviewedCase7(source, regionalRoot, cancellation.Token).LoadInspector(cancellation.Token);
            }, cancellation.Token);
            _ = prepare.ContinueWith(task =>
            {
                var observed = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            while (!prepare.IsCompleted)
                yield return null;
            var prepared = prepare.GetAwaiter().GetResult();
            plan = prepared.Plan;
            Need(!prepared.MotionAdmitted && !prepared.SupportedPathIntervalsIssued && plan.FinalBufferDigest == ReviewedDerivedCase7Policy.FinalDigest && plan.PacketCount == ReviewedDerivedCase7Policy.PacketCount, "Actual curated derived inspector result; no path admission");
            receipt["rawDocumentDigest"] = plan.RawDocumentDigest;
            receipt["sourceDigest"] = plan.SourceDigest;
            receipt["controlDigest"] = plan.ControlDigest;
            receipt["finalBufferDigest"] = plan.FinalBufferDigest;
            receipt["ownerLedgerDigest"] = plan.OwnerLedgerDigest;
            receipt["scopeCoverageDigest"] = plan.ScopeCoverageDigest;
            receipt["geometryDigest"] = plan.GeometryDigest;
            var rawOwner = prepared.TakeTownSource(cancellation.Token);
            byte[] rawBefore = rawOwner.Write(cancellation.Token);
            File.WriteAllBytes(Path.Combine(output, "owned-original-envelope.json"), rawBefore);
            var rawDocument = WorldFieldJson.Read<World13Document>(new UTF8Encoding(false, true).GetString(rawBefore));
            var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var sourceRoad in rawDocument.geography.roads)
                foreach (string packet in plan.CopyNamedRoadPacketIds(sourceRoad.id))
                {
                    if (!members.TryGetValue(packet, out var roads))
                        members.Add(packet, roads = new List<string>());
                    roads.Add(sourceRoad.id);
                }

            packetRoads = members.ToDictionary(p => p.Key, p => p.Value.OrderBy(v => v, StringComparer.Ordinal).ToArray());
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Need(shader && shader.isSupported, "Actual URP Lit shader");
            roadMaterial = new Material(shader)
            {
                name = "Derived inspector shared road",
                color = new Color(.13f, .145f, .16f)
            };
            terrainMaterial = new Material(shader)
            {
                name = "Derived inspector shared terrain",
                color = new Color(.28f, .34f, .20f)
            };
            physicalParent = Own("Derived Case7 physical owner parent");
            yield return UploadedGroundOwner800.BuildOwned(physicalParent.transform, plan, roadMaterial, terrainMaterial, cancellation.Token, () => sourceCurrent && this && physicalParent && !ending, value =>
            {
                ground = value;
                // RAW immediate PhysX, no extra SyncTransforms/yield/FixedUpdate.
                int ordinal = 0;
                foreach (var filter in physicalParent.GetComponentsInChildren<MeshFilter>(true))
                {
                    VerifyImmediateRegistration(ordinal++, filter);
                    immediateReadyRays++;
                }
            });
            Need(ground != null && ground.Lease.IsCurrent && immediateReadyRays == ReviewedDerivedCase7Policy.PacketCount, "Actual GroundLease and immediate1064 registration");
            receipt["groundHandle"] = ground.Lease.RuntimeOwnerHandle;
            receipt["groundGeneration"] = ground.Lease.Generation;
            registrationLog?.Dispose();
            registrationLog = null;
            receipt["registrationWitnessRows"] = registrationRows;
            yield return VerifyPhysical();
            NativeWitnessSandboxNegatives();
            town = Own("Derived Case7 actual typed Town").AddComponent<GeoWorld>();
            town.EntryAudit.Enabled = true;
            yield return town.BuildScopedTown800(rawOwner, plan, ground.Lease, cancellation.Token);
            var publication = town.CurrentTownPublication800;
            Need(publication != null && publication.IsCurrent && publication.ValidateSourceNow() && ReferenceEquals(publication.Ground, ground.Lease) && publication.SourceBuildingCount == ExpectedSources, "Genuine same-plan GroundLease/Town semantic publication");
            receipt["townWorldHandle"] = publication.RuntimeWorldHandle;
            receipt["townWorldGeneration"] = publication.WorldGeneration;
            receipt["townGeneration"] = publication.TownGeneration;
            Need(World13DocumentCodec.Hash(publication.CopySemanticSourceDocument()) == plan.RawDocumentDigest, "All original raw source unchanged after actual Town generation");
            Physics.SyncTransforms(); // Wall/access tests below explicitly follow this sync, not an immediate-Town-cook claim.
            receipt["townNativeQueriesAfterExplicitSync"] = true;
            ExportTown();
            ConfigureCamera();
            var renderers = town.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
            Need(renderers.Length > 0, "Actually emitted visible Town, not road-gap receipt");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            yield return Capture("overview", bounds.center + new Vector3(1, .9f, -1).normalized * (bounds.extents.magnitude / Mathf.Sin(25 * Mathf.Deg2Rad) * 1.2f), bounds.center);
            var affected = town.Region.buildings[TargetIndices[0]];
            Vector3 centre = affected.points.Aggregate(Vector3.zero, (a, b) => a + b) / affected.points.Length;
            Need(plan.TryGround(centre.x, centre.z, out var centreGround), "Affected source footprint centre has final ground");
            centre.y = (float)centreGround.Height + 2;
            yield return Capture("affected-house", centre + new Vector3(-14, 11, -17), centre);
            Vector3 eye = centre + new Vector3(-14, 0, -13);
            Need(plan.TryGround(eye.x, eye.z, out var streetGround), "Street camera has actual final ground");
            eye.y = (float)streetGround.Height + 1.7f;
            yield return Capture("street", eye, centre);
            bool retired = false, invalidInsideCallback = false;
            townSubscription = publication.BeforeRevoke((_, reason) =>
            {
                retired = true;
                invalidInsideCallback = !publication.IsCurrent;
            });
            ownedMeshes.AddRange(town.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(x => x.Mesh).Where(x => x));
            ground.Control.Revoke("Derived inspector actual owner retirement");
            Need(retired && invalidInsideCallback && publication.CopyRevocationCallbackErrors().Length == 0 && !publication.IsCurrent && !ground.Lease.IsCurrent && !town.HasSurface(centre) && !ground.Lease.TryGround(centre.x, centre.z, out _), "Genuine physical retirement revokes typed Town/query support synchronously");
            yield return null;
            yield return null;
            Need(ownedMeshes.All(mesh => !mesh) && town.GetComponentsInChildren<Collider>(true).Length == 0 && !town.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.gameObject.activeInHierarchy), "Actual retired ground/Town meshes/colliders released");
            Need(roadMaterial && terrainMaterial && errors.Count == 0 && !FindAnyObjectByType<GameSession>(), "Borrowed materials retained, no hidden session/runtime errors");
            receipt["actualGroundTownRevocationPassed"] = true;
            Finish(null);
        }

        GameObject Own(string name)
        {
            var value = new GameObject(name);
            objects.Add(value);
            return value;
        }

        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        IEnumerator VerifyPhysical()
        {
            var filters = physicalParent.GetComponentsInChildren<MeshFilter>(true).OrderBy(f => f.name, StringComparer.Ordinal).ToArray();
            Need(filters.Length == plan.PacketCount, "Physical subtree contains only exact1064 packets");
            Need(filters.Select(f => f.transform.parent).Distinct().Count() == 16, "Actual16 physical root containers");
            receipt["physicalRootContainers"] = 16;
            physicalColliders = new HashSet<Collider>(filters.Select(f => (Collider)f.GetComponent<MeshCollider>()));
            for (int ordinal = 0; ordinal < filters.Length; ordinal++)
                physicalOrdinals.Add(filters[ordinal].GetComponent<MeshCollider>(), ordinal);
            File.WriteAllText(Path.Combine(output, "coverage-packet-order.json"), new JArray(filters.Select(f => f.name)).ToString(Formatting.None));
            long verticesChecked = 0, trianglesChecked = 0;
            using var rawCoverage = new BinaryWriter(File.Create(Path.Combine(output, "raw-native-coverage-v2.bin")));
            rawCoverage.Write(System.Text.Encoding.ASCII.GetBytes("EUCASE7COVERAGE2\0"));
            rawCoverage.Write(80); // Fixed bytes/row below; declaration checked by independent replay.
            rawCoverage.Write(plan.PacketCount);
            rawCoverage.Write(plan.TriangleCount);
            using (var writer = new JsonTextWriter(new StreamWriter(Path.Combine(output, "actual-ground-packets.json"), false, new UTF8Encoding(false))))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("finalBufferDigest");
                writer.WriteValue(plan.FinalBufferDigest);
                writer.WritePropertyName("controlDigest");
                writer.WriteValue(plan.ControlDigest);
                writer.WritePropertyName("groundOwnerHandle");
                writer.WriteValue(ground.Lease.RuntimeOwnerHandle);
                writer.WritePropertyName("groundGeneration");
                writer.WriteValue(ground.Lease.Generation);
                writer.WritePropertyName("packets");
                writer.WriteStartArray();
                foreach (var filter in filters)
                {
                    var expected = plan.CopyForUpload(filter.name);
                    var mesh = filter.sharedMesh;
                    var collider = filter.GetComponent<MeshCollider>();
                    Need(mesh && collider && collider.sharedMesh == mesh && filter.GetComponent<GeneratedMeshOwner>()?.Mesh == mesh, "Actual final MF/MC/owner identity");
                    ownedMeshes.Add(mesh);
                    var vertices = mesh.vertices;
                    var indices = mesh.triangles;
                    Need(vertices.Length * 3 == expected.XYZ.Length && indices.SequenceEqual(expected.Indices), "All actual indices equal final buffers");
                    for (int i = 0; i < vertices.Length; i++)
                        Need(Bits(vertices[i].x, expected.XYZ[i * 3]) && Bits(vertices[i].y, expected.XYZ[i * 3 + 1]) && Bits(vertices[i].z, expected.XYZ[i * 3 + 2]), "Actual local vertex floatbits equal final buffers");
                    var matrix = filter.transform.localToWorldMatrix;
                    var expectedMatrix = Matrix4x4.Translate(new Vector3((float)expected.OriginX, (float)expected.OriginY, (float)expected.OriginZ));
                    for (int i = 0; i < 16; i++)
                        Need(matrix[i] == expectedMatrix[i], "Exact packet source frame/origin");
                    writer.WriteStartObject();
                    writer.WritePropertyName("id");
                    writer.WriteValue(expected.Id);
                    writer.WritePropertyName("role");
                    writer.WriteValue(plan.PacketRole(expected.Id).ToString());
                    writer.WritePropertyName("origin");
                    Write(writer, new[] { expected.OriginX, expected.OriginY, expected.OriginZ });
                    writer.WritePropertyName("colliderNativeId");
                    writer.WriteValue(collider.GetEntityId().ToString());
                    writer.WritePropertyName("actualMatrixFloatBits");
                    writer.WriteStartArray();
                    for (int m = 0; m < 16; m++)
                        writer.WriteValue(BitConverter.SingleToInt32Bits(matrix[m]));
                    writer.WriteEndArray();
                    writer.WritePropertyName("actualLocalFloatBits");
                    writer.WriteStartArray();
                    foreach (var v in vertices)
                    {
                        writer.WriteValue(BitConverter.SingleToInt32Bits(v.x));
                        writer.WriteValue(BitConverter.SingleToInt32Bits(v.y));
                        writer.WriteValue(BitConverter.SingleToInt32Bits(v.z));
                    }

                    writer.WriteEndArray();
                    writer.WritePropertyName("indices");
                    writer.WriteStartArray();
                    foreach (int i in indices)
                        writer.WriteValue(i);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        var face = FaceAt(filter, expected, vertices, indices, i / 3);
                        var oldRay = new Ray(face.LegacyPoint + Vector3.up * FaceRayStartMetres, Vector3.down);
                        bool ownHit = collider.Raycast(oldRay, out var own, FaceRayLengthMetres);
                        if (!ownHit)
                            legacyOwnMisses++;
                        else if (Math.Abs(own.point.y - face.LegacyPoint.y) > RayToleranceMetres)
                            legacyOwnHeightDisagreements++;
                        if (!Bits(face.LegacyPoint.x, face.CoveragePoint.x) || !Bits(face.LegacyPoint.z, face.CoveragePoint.z))
                            changedCentroidCoordinates++;
                        if (face.FloatWorldZeroArea)
                            sourceWorldF32ZeroArea++;
                        if (face.FloatWorldReversed)
                            sourceWorldF32ReversedArea++;
                        minimumFaceArea = Math.Min(minimumFaceArea, face.AreaXZ);
                        minimumFaceEdge = Math.Min(minimumFaceEdge, face.MinimumEdge);
                        bool query = plan.TryGround(face.CoveragePoint.x, face.CoveragePoint.z, out var merged);
                        double expectedY = query ? merged.Height : face.ExactY;
                        bool physical = TryOnlyOwnedRay(CoverageRay(face, expectedY), physicalColliders, out var actual, out int rawHits, out bool overflow);
                        double error = physical ? Math.Abs(actual.point.y - expectedY) : -1;
                        int actualOrdinal = physical && physicalOrdinals.TryGetValue(actual.collider, out int foundOrdinal) ? foundOrdinal : -1;
                        rawCoverage.Write(physicalOrdinals[collider]);
                        rawCoverage.Write(i / 3);
                        rawCoverage.Write(face.LegacyPoint.x);
                        rawCoverage.Write(face.LegacyPoint.y);
                        rawCoverage.Write(face.LegacyPoint.z);
                        rawCoverage.Write(ownHit);
                        rawCoverage.Write(ownHit ? own.point.y : 0);
                        rawCoverage.Write(ownHit ? own.triangleIndex : -1);
                        rawCoverage.Write(face.CoveragePoint.x);
                        rawCoverage.Write(face.CoveragePoint.z);
                        rawCoverage.Write(query);
                        rawCoverage.Write(expectedY);
                        rawCoverage.Write(physical);
                        rawCoverage.Write(actualOrdinal);
                        rawCoverage.Write(physical ? actual.point.y : 0);
                        rawCoverage.Write(error);
                        rawCoverage.Write(face.AreaXZ);
                        rawCoverage.Write(face.MinimumEdge);
                        rawCoverage.Write((byte)((face.FloatWorldZeroArea ? 1 : 0) | (face.FloatWorldReversed ? 2 : 0) | (overflow ? 4 : 0)));
                        Need(rawCoverage.BaseStream.Length <= MaximumRawCoverageBytes, "Bounded complete raw native coverage, never truncate");
                        nativeFaceRays++;
                        if (!query || !physical || overflow || actualOrdinal < 0 || error > RayToleranceMetres)
                        {
                            rawCoverage.Flush();
                            File.WriteAllText(Path.Combine(output, "coverage-failure.json"), new JObject { { "packet", expected.Id }, { "triangle", i / 3 }, { "coverageXZ", new JArray(face.CoveragePoint.x, face.CoveragePoint.z) }, { "legacyPoint", new JArray(face.LegacyPoint.x, face.LegacyPoint.y, face.LegacyPoint.z) }, { "legacyOwnHit", ownHit }, { "planHit", query }, { "expectedHeight", expectedY }, { "ownedHit", physical }, { "actualPacketOrdinal", actualOrdinal }, { "actualHeight", physical ? (JToken)actual.point.y : JValue.CreateNull() }, { "error", error }, { "overflow", overflow }, { "rawHits", rawHits } }.ToString(Formatting.Indented));
                        }

                        Need(query && physical && !overflow && actualOrdinal >= 0 && error <= RayToleranceMetres, "Actual final OWNED ground coverage agrees with final plan at same float-cast world XZ");
                        if (nativeFaceRays % RayBatch == 0)
                        {
                            rawCoverage.Flush();
                            yield return null;
                        }
                    }

                    verticesChecked += vertices.Length;
                    trianglesChecked += indices.Length / 3;
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            Need(verticesChecked == plan.VertexCount && trianglesChecked == plan.TriangleCount && nativeFaceRays == ReviewedDerivedCase7Policy.TriangleCount, "Every new physical buffer and face accounted");
            Need(new FileInfo(Path.Combine(output, "actual-ground-packets.json")).Length <= MaximumDiagnosticBytes, "Bounded physical export");
            rawCoverage.Flush();
            rawCoverage.Dispose();
            receipt["coverageContract"] = "DoubleWorldCentroid-OneFloatXZ-GlobalExclusiveOwnedSet-SameXZFinalPlan/2";
            receipt["legacyOwnFaceMissesDiagnostic"] = legacyOwnMisses;
            receipt["legacyOwnHeightDisagreementsDiagnostic"] = legacyOwnHeightDisagreements;
            receipt["centroidCoordinatesChangedByWorldFirstArithmetic"] = changedCentroidCoordinates;
            receipt["worldF32ZeroProjectedAreaDiagnostic"] = sourceWorldF32ZeroArea;
            receipt["worldF32ReversedProjectedAreaDiagnostic"] = sourceWorldF32ReversedArea;
            receipt["minimumSourceProjectedArea"] = minimumFaceArea;
            receipt["minimumSourceXZEdge"] = minimumFaceEdge;
            receipt["rawNativeCoverageSHA256"] = World13DocumentCodec.Hash(File.ReadAllBytes(Path.Combine(output, "raw-native-coverage-v2.bin")));
            receipt["actualPackets"] = filters.Length;
            receipt["actualVertices"] = verticesChecked;
            receipt["actualTriangles"] = trianglesChecked;
            receipt["nativeFaceRays"] = nativeFaceRays;
            receipt["immediateReadyRays"] = immediateReadyRays;
        }

        static void Write(JsonWriter writer, IEnumerable<double> values)
        {
            writer.WriteStartArray();
            foreach (double v in values)
                writer.WriteValue(v);
            writer.WriteEndArray();
        }

        void ConfigureCamera()
        {
            var early = GameObject.Find(ClearCameraName);
            if (early)
                early.SetActive(false);
            var sun = Own("Derived inspector daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .58f, .60f);
            camera = Own("Derived inspector real URP camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 4000;
            camera.fieldOfView = 50;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.40f, .52f, .65f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;
            target = new RenderTexture(CaptureWidth, CaptureHeight, 24);
            target.Create();
            camera.targetTexture = target;
            camera.aspect = (float)CaptureWidth / CaptureHeight;
        }

        IEnumerator Capture(string name, Vector3 position, Vector3 lookAt)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            int start = Time.frameCount;
            double timeout = Time.realtimeSinceStartupAsDouble + 10;
            while (renderedFrame <= start)
            {
                Need(Time.realtimeSinceStartupAsDouble < timeout, "Actual URP view rendered");
                yield return null;
            }

            // Read the completed persistent camera RT on the following update.
            // This does not depend on Editor batchmode WaitForEndOfFrame dispatch.
            Need(town.CurrentTownPublication800.IsCurrent && ground.Lease.IsCurrent, "Capture retains exact live source/ground/Town");
            var old = RenderTexture.active;
            var image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                Destroy(image);
            }

            views.Add(new JObject { { "name", name }, { "frame", renderedFrame }, { "position", new JArray(position.x, position.y, position.z) }, { "target", new JArray(lookAt.x, lookAt.y, lookAt.z) }, { "fov", camera.fieldOfView }, { "width", CaptureWidth }, { "height", CaptureHeight }, { "actualURPRenderTexture", true }, { "labelsDrawn", false }, { "finalBufferDigest", plan.FinalBufferDigest }, { "actualFacadeFrame", renderedFacades.frame }, { "actualFacadeDrawSubmissions", renderedFacades.drawSubmissions }, { "actualSubmittedWindows", renderedFacades.submittedWindows }, { "actualSubmittedDoors", renderedFacades.submittedDoors }, { "target1697ActuallyEmitted", receipt["target1697ActuallyEmitted"] }, { "target1005ActuallyEmitted", receipt["target1005ActuallyEmitted"] }, { "captureNameDoesNotEstablishTargetEmission", true } });
            File.WriteAllText(Path.Combine(output, "capture-views.json"), views.ToString(Formatting.Indented));
        }

        void ExportTown()
        {
            // Filled by the bounded actual source/part/collider census below.
            ExportTownEvidence();
        }

        void Finish(Exception error)
        {
            if (ending)
                return;
            ending = true;
            cancellation.Cancel();
            registrationLog?.Dispose();
            registrationLog = null;
            registrationLog?.Dispose();
            registrationLog = null;
            receipt["error"] = error?.ToString();
            receipt["checks"] = checks;
            StartCoroutine(Cleanup(error));
        }

        IEnumerator Cleanup(Exception error)
        {
            Exception failure = error;
            try
            {
                sourceCurrent = false;
                ground?.Control.Revoke("Derived inspector cleanup before parent disable");
            }
            catch (Exception caught)
            {
                if (failure == null)
                    failure = caught;
            }

            void Attempt(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception caught)
                {
                    if (failure == null)
                        failure = caught;
                }
            }

            Attempt(() =>
            {
                townSubscription?.Dispose();
                townSubscription = null;
            });
            foreach (var value in objects)
                if (value)
                    Attempt(() => Destroy(value));
            yield return null;
            yield return null;
            if (ownedMeshes.Any(mesh => mesh))
            {
                if (failure == null)
                    failure = new InvalidDataException("Owned native meshes survived normal cleanup");
                foreach (var mesh in ownedMeshes)
                    if (mesh)
                        Attempt(() => Destroy(mesh));
                yield return null;
                yield return null;
            }

            if (roadMaterial)
                Attempt(() => Destroy(roadMaterial));
            if (terrainMaterial)
                Attempt(() => Destroy(terrainMaterial));
            if (target)
                Attempt(() =>
                {
                    target.Release();
                    Destroy(target);
                });
            Attempt(() =>
            {
                Physics.autoSyncTransforms = originalAutoSync;
                Time.timeScale = originalTimeScale;
            });
            RenderPipelineManager.endCameraRendering -= Rendered;
            Application.logMessageReceived -= Logged;
            receipt["runtimeErrors"] = new JArray(errors);
            receipt["cleanupComplete"] = ownedMeshes.All(mesh => !mesh);
            receipt["error"] = failure?.ToString();
            receipt["checks"] = checks;
            receipt["status"] = failure == null && errors.Count == 0 ? "PASS_DERIVED_GROUND_TOWN_INSPECTOR_PENDING_VISUAL_REVIEW" : "FAIL";
            File.WriteAllText(Path.Combine(output, "receipt.json"), receipt.ToString(Formatting.Indented));
            completed?.Invoke(failure == null && errors.Count == 0, failure?.ToString());
        }

        void OnDestroy()
        {
            registrationLog?.Dispose();
            registrationLog = null;
            if (!beganProof)
            {
                cancellation.Dispose();
                return;
            }

            cancellation.Cancel();
            sourceCurrent = false;
            try
            {
                ground?.Control.Revoke("Inspector component destroyed");
            }
            catch
            {
            }

            Physics.autoSyncTransforms = originalAutoSync;
            Time.timeScale = originalTimeScale;
            RenderPipelineManager.endCameraRendering -= Rendered;
            Application.logMessageReceived -= Logged;
        }
    }
}
