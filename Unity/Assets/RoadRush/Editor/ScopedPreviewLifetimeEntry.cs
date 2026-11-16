using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class ScopedPreviewLifetimeEntry
    {
        public static void Run()
        {
            string output = "/private/tmp/rr019-scoped-lifetime-" + DateTime.UtcNow.Ticks;
            string previous = PlayerProfile.StorageOverride;
            int code = 1;
            try
            {
                if (!Application.isBatchMode || Application.isPlaying || UnityEngine.Object.FindAnyObjectByType<GameSession>())
                    throw new InvalidOperationException("Isolated batch edit-mode verification required");
                PlayerProfile.StorageOverride = output + "-unused-profile.json";
                var proof = global::FiveLinkTerrainPreparation.BuildReviewedFiveLinkFixture(default);
                var input = ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot("/private/tmp/rr019-integration-drafts/ActualPantside");
                var owner = CanonicalRaw13Codec.Read(input, input.CopyDocumentBytes());
                var plan = PreparedWorldMeshPlan.FromVerifiedJoinedScope(owner, proof);
                var report = ScopedPreviewLifetimeVerification.Verify(plan, output);
                if (report.status != "PASS") throw new InvalidOperationException("Scoped lifetime verification failed");
                code = 0;
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "entry-failure.txt"), error.ToString());
                Debug.LogException(error);
            }
            finally
            {
                PlayerProfile.StorageOverride = previous;
                Debug.Log("Scoped preview lifetime output: " + output);
                EditorApplication.Exit(code);
            }
        }
    }
}
