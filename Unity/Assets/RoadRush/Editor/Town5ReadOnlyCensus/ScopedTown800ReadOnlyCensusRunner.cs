using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using RoadRush.Core800;
using RoadRush.VisualDiagnosticsProposal;

namespace RoadRush.Editor
{
    /// <summary>
    /// Explicit Editor-only entry point for an ADDITIVE read-only census of the pinned
    /// Town5 proof. No initialization attributes or automatic invocation. Root owns
    /// importing, source/binary guards, fresh process allocation and launch permission.
    /// Scene, geometry, materials, lights, camera, physics and runtime owners are ONLY
    /// READ; the runner owns private arming state and writes only its fresh output.
    /// The original verifier retains all native work, isolation, cleanup and exit logic.
    /// </summary>
    public static class ScopedTown800ReadOnlyCensusRunner
    {
        private const string CensusFlag = "-roadRushTown800CensusOutput";
        private const string CensusOutputParent = "/private/tmp";
        private const string CensusOutputPrefix = "/private/tmp/rr019-town-census-";
        private const string OverviewName = "overview";
        private const string ReceiptFileName = "overview-census.json";
        private const string RecordedStatus = "RECORDED_READ_ONLY_PRE_CAPTURE";
        private const string ExpectedScopeMarker = "READ_ONLY_DIAGNOSTIC_NOT_AUTHORITY";
        private const string PinnedBaseSourceSha256 = "3cdd31703e95baf6258e9041e5e210902ae16e8ed71456fa5f5f3b82abe3ad8c";
        private const int CaptureWidthPixels = 1920;
        private const int CaptureHeightPixels = 1200;
        private const double AspectTolerance = 0.000001; // dimensionless; requested1.6 aspect, no mutation
        private static readonly string[] CensusFiles =
        {
            "actual-scene-geometry.tsv", "actual-scene-materials.tsv",
            "actual-scene-lights.tsv", "scope.txt"
        };

        private enum RunnerState { Unarmed, Armed, Recording, Recorded, Failed }
        private static RunnerState state;
        private static string censusOutput;
        private static int entryThread;

        /// <summary>Diagnostic arming state only, never world/provider admission.</summary>
        public static bool IsArmed => state != RunnerState.Unarmed;

        /// <summary>
        /// Root's executeMethod target. Requires exactly one new census flag with a
        /// fresh direct-child/private/tmp path. Does not create output or scene objects
        /// before the original Run. Original proof arguments/early storage isolation
        /// remain mandatory and are handled by the unchanged original verifier.
        /// Repeated entry fails; this is not a restart/resume/reset command.
        /// </summary>
        public static void Run()
        {
            if (state != RunnerState.Unarmed) throw new InvalidOperationException("Census runner already entered");
            string output = ReadFreshOutput(Environment.GetCommandLineArgs());
            entryThread = Thread.CurrentThread.ManagedThreadId;
            censusOutput = output;
            state = RunnerState.Armed;
            try
            {
                // REAL pre-existing Editor type/API; never reimplemented or stubbed.
                ScopedTown800NativeVerification.Run();
            }
            catch
            {
                state = RunnerState.Failed;
                throw;
            }
        }

        /// <summary>
        /// Parent's ONE additive hook, immediately before original camera.Render and
        /// after its actual facade before/after snapshots. Nonoverview/unarmed calls
        /// do nothing. Overview requires exact target/aspect and zero CURRENT capture
        /// facade deltas. Real frame aggregate totals are recorded, not assumedzero;
        /// they do NOT inventory arbitrary Graphics.RenderMesh/command-buffer queues.
        /// Synchronous census uses unchanged work caps plus Root's outer watchdog.
        /// </summary>
        public static void RecordBeforeCapture(string name, GameObject physicalRoot,
            GeoWorld town, Camera camera, PreparedWorldMeshPlan800 plan,
            int submittedDraws, int submittedWindows,
            BuildingFacadeChunk.FrameStatistics before, BuildingFacadeChunk.FrameStatistics after)
        {
            if (state == RunnerState.Unarmed || !string.Equals(name, OverviewName, StringComparison.Ordinal)) return;
            bool censusAttemptStarted = false;
            try
            {
                Require(state == RunnerState.Armed, "Overview census must run exactly once");
                Require(Thread.CurrentThread.ManagedThreadId == entryThread, "Same original Editor entry thread required");
                Require(!Application.isPlaying, "Editor diagnostic only; no Play mode");
                Require(physicalRoot && town && camera && plan != null, "Actual original proof references required");
                Require(physicalRoot.activeInHierarchy && town.gameObject.activeInHierarchy,
                    "Actual proof roots must remain active");
                Require(!Directory.Exists(censusOutput) && !File.Exists(censusOutput), "Census output stopped being fresh");
                RenderTexture target = camera.targetTexture;
                Require(target && target.width == CaptureWidthPixels && target.height == CaptureHeightPixels,
                    "Actual original1920x1200 capture target required");
                Require(camera.pixelWidth == CaptureWidthPixels && camera.pixelHeight == CaptureHeightPixels,
                    "Actual camera viewport must cover1920x1200");
                Require(Math.Abs(camera.aspect - (double)CaptureWidthPixels / CaptureHeightPixels) <= AspectTolerance,
                    "Original capture aspect must already be1.6; runner never sets it");
                int frame = Time.frameCount;
                Require(before.frame == frame && after.frame == frame, "Actual facade snapshots must belong to capture frame");
                ValidateStatistics(before);
                ValidateStatistics(after);
                Require(submittedDraws == (long)after.drawSubmissions - before.drawSubmissions
                    && submittedWindows == (long)after.submittedWindows - before.submittedWindows,
                    "Supplied capture deltas disagree with actual facade snapshots");
                Require(submittedDraws == 0 && submittedWindows == 0
                    && after.submittedDoors == before.submittedDoors
                    && after.submittedTriangles == before.submittedTriangles,
                    "Overview requires zero current-capture facade draw/window/door/triangle deltas");
                Require(after.residentFeatures >= before.residentFeatures, "Facade current-frame resident counter regressed");
                Require(SameStatistics(BuildingFacadeChunk.Statistics, after), "Facade statistics changed after supplied snapshot");

                Matrix4x4 cameraFrame = camera.transform.localToWorldMatrix;
                Matrix4x4 view = camera.worldToCameraMatrix;
                Matrix4x4 projection = camera.projectionMatrix;
                Matrix4x4 physicalFrame = physicalRoot.transform.localToWorldMatrix;
                Matrix4x4 townFrame = town.transform.localToWorldMatrix;
                int townGeneration = town.SurfaceGeneration;
                ValidateMatrix(cameraFrame); ValidateMatrix(view); ValidateMatrix(projection);
                ValidateMatrix(physicalFrame); ValidateMatrix(townFrame);
                state = RunnerState.Recording;
                censusAttemptStarted = true;
                TownSceneCensus.Record(physicalRoot, town.gameObject, camera,
                    packetId => plan.PacketRole(packetId).ToString(), censusOutput, CancellationToken.None);

                Require(Time.frameCount == frame && SameStatistics(BuildingFacadeChunk.Statistics, after),
                    "Capture frame/facade totals changed during read-only census");
                Require(camera && camera.targetTexture == target && target.width == CaptureWidthPixels
                    && target.height == CaptureHeightPixels && camera.pixelWidth == CaptureWidthPixels
                    && camera.pixelHeight == CaptureHeightPixels
                    && Math.Abs(camera.aspect - (double)CaptureWidthPixels / CaptureHeightPixels) <= AspectTolerance,
                    "Capture target/aspect changed during census");
                Require(camera.transform.localToWorldMatrix.Equals(cameraFrame)
                    && camera.worldToCameraMatrix.Equals(view) && camera.projectionMatrix.Equals(projection),
                    "Actual capture camera changed during census");
                Require(physicalRoot && town && town.SurfaceGeneration == townGeneration
                    && physicalRoot.transform.localToWorldMatrix.Equals(physicalFrame)
                    && town.transform.localToWorldMatrix.Equals(townFrame), "Proof root/generation changed during census");

                string scope = File.ReadAllText(Path.Combine(censusOutput, "scope.txt"));
                Require(scope.StartsWith(ExpectedScopeMarker, StringComparison.Ordinal), "Original census scope marker missing");
                long incompleteRows = ReadScopeCount(scope, "IncompleteGeometryRows");
                long rendererRows = ReadScopeCount(scope, "Renderers");
                long vertices = ReadScopeCount(scope, "Vertices");
                long indices = ReadScopeCount(scope, "Indices");
                string receipt = BuildReceipt(camera, target, physicalRoot, town, plan, frame,
                    before, after, submittedDraws, submittedWindows, view, projection, cameraFrame,
                    physicalFrame, townFrame, townGeneration, scope, incompleteRows, rendererRows, vertices, indices);
                WriteNew(Path.Combine(censusOutput, ReceiptFileName), receipt);
                state = RunnerState.Recorded;
            }
            catch (Exception error)
            {
                state = RunnerState.Failed;
                // Do not overwrite an existing path/receipt. A missing/FAILED receipt
                // fails Parent's launcher; no success is inferred from partial TSVs.
                if (censusAttemptStarted && Directory.Exists(censusOutput))
                {
                    try
                    {
                        WriteNew(Path.Combine(censusOutput, ReceiptFileName), "{\"status\":\"FAILED_READ_ONLY_CENSUS\",\"captureName\":\"overview\",\"error\":"
                            + JsonString(error.GetType().Name + ": " + error.Message) + "}");
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                throw;
            }
        }

        private static string ReadFreshOutput(string[] arguments)
        {
            int position = -1;
            for (int index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], CensusFlag, StringComparison.Ordinal)) continue;
                if (position >= 0) throw new ArgumentException("Exactly one census output flag required");
                position = index;
            }
            if (position < 0 || position + 1 >= arguments.Length || !Path.IsPathRooted(arguments[position + 1]))
                throw new ArgumentException("Explicit absolute census output required");
            string output = Path.GetFullPath(arguments[position + 1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!output.StartsWith(CensusOutputPrefix, StringComparison.Ordinal)
                || output.Length == CensusOutputPrefix.Length
                || !string.Equals(Path.GetDirectoryName(output), CensusOutputParent, StringComparison.Ordinal)
                || Directory.Exists(output) || File.Exists(output))
                throw new ArgumentException("Fresh direct-child/private/tmp/rr019-town-census-* output required");
            return output;
        }

        private static void ValidateStatistics(BuildingFacadeChunk.FrameStatistics value)
        {
            Require(value.residentFeatures >= 0 && value.submittedWindows >= 0 && value.submittedDoors >= 0
                && value.drawSubmissions >= 0 && value.submittedTriangles >= 0, "Invalid facade aggregate counters");
        }

        private static bool SameStatistics(BuildingFacadeChunk.FrameStatistics first, BuildingFacadeChunk.FrameStatistics second) =>
            first.frame == second.frame && first.residentFeatures == second.residentFeatures
                && first.submittedWindows == second.submittedWindows && first.submittedDoors == second.submittedDoors
                && first.drawSubmissions == second.drawSubmissions && first.submittedTriangles == second.submittedTriangles;

        private static void ValidateMatrix(Matrix4x4 matrix)
        {
            for (int index = 0; index < 16; index++)
                Require(!float.IsNaN(matrix[index]) && !float.IsInfinity(matrix[index]), "Nonfinite actual capture/root matrix");
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException(reason);
        }

        private static long ReadScopeCount(string scope, string key)
        {
            long parsed = -1;
            int matches = 0;
            foreach (string part in scope.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string field = part.Trim();
                if (!field.StartsWith(key + "=", StringComparison.Ordinal)) continue;
                matches++;
                if (!long.TryParse(field.Substring(key.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                    || parsed < 0) throw new InvalidDataException("Invalid census scope count: " + key);
            }
            Require(matches == 1, "Missing/duplicate census scope count: " + key);
            return parsed;
        }

        private static string BuildReceipt(Camera camera, RenderTexture target, GameObject physicalRoot,
            GeoWorld town, PreparedWorldMeshPlan800 plan, int frame,
            BuildingFacadeChunk.FrameStatistics before, BuildingFacadeChunk.FrameStatistics after,
            int drawDelta, int windowDelta, Matrix4x4 view, Matrix4x4 projection, Matrix4x4 cameraFrame,
            Matrix4x4 physicalFrame, Matrix4x4 townFrame, int townGeneration, string scope,
            long incompleteRows, long rendererRows, long vertices, long indices)
        {
            var json = new StringBuilder();
            json.Append('{');
            Property(json, "status", JsonString(RecordedStatus));
            Property(json, "captureName", JsonString(OverviewName));
            Property(json, "recordedUtc", JsonString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            Property(json, "widthPixels", CaptureWidthPixels.ToString(CultureInfo.InvariantCulture));
            Property(json, "heightPixels", CaptureHeightPixels.ToString(CultureInfo.InvariantCulture));
            Property(json, "facadeDrawDelta", drawDelta.ToString(CultureInfo.InvariantCulture));
            Property(json, "facadeWindowDelta", windowDelta.ToString(CultureInfo.InvariantCulture));
            Property(json, "facadeDoorDelta", ((long)after.submittedDoors - before.submittedDoors).ToString(CultureInfo.InvariantCulture));
            Property(json, "facadeTriangleDelta", (after.submittedTriangles - before.submittedTriangles).ToString(CultureInfo.InvariantCulture));
            Property(json, "facadeBefore", StatisticsJson(before));
            Property(json, "facadeAfter", StatisticsJson(after));
            Property(json, "facadeStatisticsMeaning", JsonString("Current-frame aggregate only; not lifetime totals or arbitrary GPU queue inventory"));
            Property(json, "NoGlobalHiddenQueueClaim", "true");
            Property(json, "globalQueueProved", "false");
            Property(json, "sourceDigest", JsonString(plan.SourceDigest));
            Property(json, "rawDocumentDigest", JsonString(plan.RawDocumentDigest));
            Property(json, "finalBufferDigest", JsonString(plan.FinalBufferDigest));
            Property(json, "geometryDigest", JsonString(plan.GeometryDigest));
            Property(json, "scopeCoverageDigest", JsonString(plan.ScopeCoverageDigest));
            Property(json, "pinnedBaseSourceSha256", JsonString(PinnedBaseSourceSha256));
            Property(json, "pinnedBaseHashMeaning", JsonString("Expected parent-guarded SOURCE baseline, not runtime self-attestation of a loaded binary"));
            Property(json, "physicalPacketCount", plan.PacketCount.ToString(CultureInfo.InvariantCulture));
            Property(json, "physicalRootEntityId", JsonString(Token(physicalRoot)));
            Property(json, "townEntityId", JsonString(Token(town)));
            Property(json, "townSurfaceGeneration", townGeneration.ToString(CultureInfo.InvariantCulture));
            Property(json, "frameCount", frame.ToString(CultureInfo.InvariantCulture));
            Property(json, "renderedFrameCount", Time.renderedFrameCount.ToString(CultureInfo.InvariantCulture));
            Property(json, "realtimeSeconds", Number(Time.realtimeSinceStartupAsDouble));
            Property(json, "unityVersion", JsonString(Application.unityVersion));
            Property(json, "cameraName", JsonString(camera.name));
            Property(json, "cameraEntityId", JsonString(Token(camera)));
            Property(json, "cameraPosition", VectorJson(camera.transform.position));
            Property(json, "cameraRotation", QuaternionJson(camera.transform.rotation));
            Property(json, "cameraAspect", Number(camera.aspect));
            Property(json, "cameraFovDegrees", Number(camera.fieldOfView));
            Property(json, "cameraNearMetres", Number(camera.nearClipPlane));
            Property(json, "cameraFarMetres", Number(camera.farClipPlane));
            Property(json, "cameraCullingMask", camera.cullingMask.ToString(CultureInfo.InvariantCulture));
            Property(json, "renderTargetName", JsonString(target.name));
            Property(json, "renderTargetCreatedBeforeRender", target.IsCreated() ? "true" : "false");
            Property(json, "cameraWorldMatrix", MatrixJson(cameraFrame));
            Property(json, "worldToCameraMatrix", MatrixJson(view));
            Property(json, "projectionMatrix", MatrixJson(projection));
            Property(json, "physicalRootMatrix", MatrixJson(physicalFrame));
            Property(json, "townRootMatrix", MatrixJson(townFrame));
            Property(json, "matrixLayout", JsonString("Unity column-major index0..15; entries promoted from actual binary32"));
            Property(json, "censusScopeMarker", JsonString(ExpectedScopeMarker));
            Property(json, "censusScopeText", JsonString(scope));
            Property(json, "incompleteGeometryRows", incompleteRows.ToString(CultureInfo.InvariantCulture));
            Property(json, "rendererRows", rendererRows.ToString(CultureInfo.InvariantCulture));
            Property(json, "literalVerticesRead", vertices.ToString(CultureInfo.InvariantCulture));
            Property(json, "literalIndicesRead", indices.ToString(CultureInfo.InvariantCulture));
            Property(json, "literalRowEvidenceStatus", JsonString(incompleteRows == 0
                ? "NO_RECORDED_INCOMPLETE_GEOMETRY_ROWS" : "PARTIAL_LITERAL_GEOMETRY_ROWS"));
            Property(json, "scope", JsonString("Read-only PRE-render scene census; not a rendered result, owner/registry grant, visible-pixel proof or visual fix"));
            json.Append(",\"censusFiles\":{");
            for (int index = 0; index < CensusFiles.Length; index++)
            {
                if (index > 0) json.Append(',');
                string file = CensusFiles[index];
                json.Append(JsonString(file)).Append(':').Append(JsonString(HashFile(Path.Combine(censusOutput, file))));
            }
            json.Append("}}\n");
            return json.ToString();
        }

        private static string StatisticsJson(BuildingFacadeChunk.FrameStatistics value) =>
            "{\"frame\":" + value.frame.ToString(CultureInfo.InvariantCulture)
                + ",\"residentFeatures\":" + value.residentFeatures.ToString(CultureInfo.InvariantCulture)
                + ",\"submittedWindows\":" + value.submittedWindows.ToString(CultureInfo.InvariantCulture)
                + ",\"submittedDoors\":" + value.submittedDoors.ToString(CultureInfo.InvariantCulture)
                + ",\"drawSubmissions\":" + value.drawSubmissions.ToString(CultureInfo.InvariantCulture)
                + ",\"submittedTriangles\":" + value.submittedTriangles.ToString(CultureInfo.InvariantCulture) + "}";

        private static string MatrixJson(Matrix4x4 value)
        {
            var json = new StringBuilder("[");
            for (int index = 0; index < 16; index++)
            {
                if (index > 0) json.Append(',');
                json.Append(Number(value[index]));
            }
            return json.Append(']').ToString();
        }

        private static string VectorJson(Vector3 value) => "[" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + "]";
        private static string QuaternionJson(Quaternion value) => "[" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + "," + Number(value.w) + "]";
        private static string Number(double value)
        {
            Require(!double.IsNaN(value) && !double.IsInfinity(value), "Nonfinite receipt value");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
        private static string Token(UnityEngine.Object value) => EntityId.ToULong(value.GetEntityId()).ToString(CultureInfo.InvariantCulture);
        private static void Property(StringBuilder json, string name, string value)
        {
            if (json.Length > 1) json.Append(',');
            json.Append(JsonString(name)).Append(':').Append(value);
        }
        private static string JsonString(string value)
        {
            var json = new StringBuilder("\"");
            foreach (char character in value ?? "")
            {
                if (character == '"' || character == '\\') json.Append('\\').Append(character);
                else if (character < 32 || char.IsSurrogate(character)) json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                else json.Append(character);
            }
            return json.Append('"').ToString();
        }
        private static string HashFile(string path)
        {
            Require(File.Exists(path), "Missing census output: " + Path.GetFileName(path));
            using var algorithm = SHA256.Create();
            using var input = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static void WriteNew(string path, string text)
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(output, new UTF8Encoding(false, true));
            writer.Write(text);
        }
    }
}
