using System;
using System.IO;
using System.Threading;
using RoadRush.TerrainDraft.Core800;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>
    /// Root-run actual Unity capability/readback check. No scene/physics/car build,
    /// recook, source DB/provider or normal activation. Always use the existing
    /// external protected engine guard; this fixture never claims those outer checks.
    /// </summary>
    public static class ReviewedDerivedCase7Verification
    {
        [Serializable]
        sealed class Receipt
        {
            public string status, error, revision, sourceDigest, rawDigest, finalDigest, controlDigest;
            public int checks, packets, vertices, triangles, roots, targetRows;
            public bool engineGeometryReadbackOnly = true;
            public bool motionAdmitted = false, supportedPathIntervalsIssued = false, physicalAcceptance = false;
        }

        static string Required(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0 || index != Array.LastIndexOf(args, key) || index + 1 >= args.Length)
                throw new ArgumentException("Exactly one explicit " + key);
            return Path.GetFullPath(args[index + 1]);
        }

        public static void Run()
        {
            var receipt = new Receipt();
            string output = null;
            string previousStorage = PlayerProfile.StorageOverride;
            string previousRegions = RegionCache.OverrideFolder;
            string previousUpdates = CountryUpdateService.OverrideFolder;
            bool previousAllow = CountryUpdateService.AllowVerification;
            bool previousSuppression = GameSession.SuppressAutomaticLaunch;
            int exit = 1;
            void Need(bool okay, string label)
            {
                if (!okay)
                    throw new InvalidDataException(label);
                receipt.checks++;
            }

            void Refuses(Action action, string label)
            {
                try
                {
                    action();
                }
                catch (Exception error)when (error is InvalidDataException || error is ArgumentException || error is OperationCanceledException || error is InvalidOperationException)
                {
                    receipt.checks++;
                    return;
                }

                throw new InvalidDataException("Unexpected acceptance: " + label);
            }

            try
            {
                Need(!Application.isPlaying && !UnityEngine.Object.FindAnyObjectByType<GameSession>(), "Editor data-only fixture");
                var args = Environment.GetCommandLineArgs();
                output = Required(args, "-roadRushDerivedCase7Output");
                Need(output.StartsWith("/private/tmp/rr019-derived-case7-unity-", StringComparison.Ordinal) && !Directory.Exists(output) && !File.Exists(output), "Fresh private proof output");
                Directory.CreateDirectory(output);
                GameSession.SuppressAutomaticLaunch = true;
                PlayerProfile.StorageOverride = Path.Combine(output, "unused-profile.json");
                RegionCache.OverrideFolder = Path.Combine(output, "unused-regions");
                CountryUpdateService.OverrideFolder = Path.Combine(output, "unused-updates");
                CountryUpdateService.AllowVerification = false;
                string context = Required(args, "-roadRushDerivedOriginalContext");
                string regional = Required(args, "-roadRushDerivedCase7Root");
                Need(typeof(ReviewedDerivedCore800Request).GetConstructors().Length == 0, "No public request grant");
                Refuses(() => ReviewedDerivedCore800Request.CaptureReviewedCase7(null, "unread"), "Unverified request");
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Refuses(() => VerifiedCoreTerrain.ReadReviewedCase7DerivedGeometryOnly("unread", null, cancellation.Token), "Cancelled before IO");
                var original = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(context);
                var request = ReviewedDerivedCore800Request.CaptureReviewedCase7(original, regional);
                Refuses(() => request.LoadInspector(cancellation.Token), "Cancelled typed read");
                var wrongRaw = original.CopyDocumentBytes();
                wrongRaw[0] ^= 1;
                Refuses(() => VerifiedCoreTerrain.ReadReviewedCase7DerivedGeometryOnly("unread", wrongRaw), "Changed original source bytes");
                var result = request.LoadInspector();
                var plan = result.Plan;
                Need(plan.PacketCount == ReviewedDerivedCase7Policy.PacketCount && plan.VertexCount == ReviewedDerivedCase7Policy.VertexCount && plan.TriangleCount == ReviewedDerivedCase7Policy.TriangleCount, "Actual complete final buffers");
                Need(plan.CopyRootKeys().Length == 16 && plan.RawDocumentDigest == ReviewedDerivedCase7Policy.RawDigest && plan.SourceDigest == ReviewedDerivedCase7Policy.SourceDigest && plan.ControlDigest == ReviewedDerivedCase7Policy.ControlDigest && plan.FinalBufferDigest == ReviewedDerivedCase7Policy.FinalDigest, "Actual owner identity/roots");
                Need(!request.MotionAdmitted && !result.MotionAdmitted && !result.SupportedPathIntervalsIssued && !plan.FullWorldAdmitted, "No motion/path/full-world authority");
                Need(plan.CopySourceRows(ReviewedDerivedCase7Policy.TargetRoad).Length == 690, "Actual target frame rows");
                Refuses(() => result.TakeTownSource(cancellation.Token), "Cancelled transfer retains source owner");
                var source = result.TakeTownSource();
                Need(World13DocumentCodec.Hash(source.Write()) == ReviewedDerivedCase7Policy.RawDigest, "Original exact source owner");
                Refuses(() => result.TakeTownSource(), "One-time Town ownership transfer");
                Need(!File.Exists(PlayerProfile.StorageOverride) && !Directory.Exists(RegionCache.OverrideFolder) && !Directory.Exists(CountryUpdateService.OverrideFolder), "No profile/cache/provider artifacts");
                receipt.revision = result.Revision;
                receipt.sourceDigest = plan.SourceDigest;
                receipt.rawDigest = plan.RawDocumentDigest;
                receipt.finalDigest = plan.FinalBufferDigest;
                receipt.controlDigest = plan.ControlDigest;
                receipt.packets = plan.PacketCount;
                receipt.vertices = plan.VertexCount;
                receipt.triangles = plan.TriangleCount;
                receipt.roots = plan.CopyRootKeys().Length;
                receipt.targetRows = plan.CopySourceRows(ReviewedDerivedCase7Policy.TargetRoad).Length;
                receipt.status = "PASS_DERIVED_CASE7_DATA_READBACK_ONLY";
                exit = 0;
            }
            catch (Exception error)
            {
                receipt.status = "FAIL";
                receipt.error = error.ToString();
                Debug.LogException(error);
            }
            finally
            {
                PlayerProfile.StorageOverride = previousStorage;
                RegionCache.OverrideFolder = previousRegions;
                CountryUpdateService.OverrideFolder = previousUpdates;
                CountryUpdateService.AllowVerification = previousAllow;
                GameSession.SuppressAutomaticLaunch = previousSuppression;
                if (output != null && Directory.Exists(output))
                    File.WriteAllText(Path.Combine(output, "receipt.json"), JsonUtility.ToJson(receipt, true));
            }

            EditorApplication.Exit(exit);
        }
    }
}
