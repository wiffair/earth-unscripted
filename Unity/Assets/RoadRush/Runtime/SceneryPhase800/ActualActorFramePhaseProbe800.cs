using System;
using System.Collections;
using System.Collections.Generic;
using RoadRush.VehiclePopulation;
using RoadRush.FunnyAnimals.ActualRegistryConsumer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadRush.Scenery800.Verification
{
    /// <summary>
    /// Opt-in coordinated native probe of the ACTUAL session/Town/registry phase.
    /// It requires genuine already-issued inputs, not reflected/fabricated tokens.
    /// It never creates a session, source owner, actor roster or gameplay grant.
    /// </summary>
    public static class ActualActorFramePhaseProbe800
    {
        const int RequiredDistinctFrames = 3;
        const double TimeoutSeconds = 10;

        public sealed class Report
        {
            public int ForwardedFrames;
            public int FirstFrame = -1;
            public int LastFrame = -1;
            public int LastActualVehicleCount;
            public int LastPhysicalDiagnosticCount;
            public int LastUnboundPhysicalCount;
            public int LastSupportedPhysicalCount;
            public string LastPhase;
            public string Reason;
            public bool ActualOrderedPhysicalReadPassed;
            public bool GameplayAdmission => false;
        }

        /// <summary>
        /// Run only in primary's exclusive isolated PlayMode/native window after
        /// actual producer enrollment, Ground/Town publication and scope binding.
        /// The caller owns full real-profile guards and the existing inputs.
        /// This probe owns only one sibling host/component and destroys it finally.
        /// </summary>
        public static IEnumerator Run(GameSession actualSession,
            TownSemanticPublication800 actualTown, Action<Report> completed)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(PlayerProfile.StorageOverride))
                throw new InvalidOperationException("Explicit isolated native PlayMode scope required");
            if (!actualSession || actualTown == null || completed == null)
                throw new ArgumentException("Actual session, Town publication and receipt callback required");
            SessionVehiclePopulation population = actualSession.ActualVehicles;
            if (population == null || !actualTown.IsCurrent || !population.MatchesTownPublication800(actualTown))
                throw new InvalidOperationException("Actual enrolled SAME session/Ground/Town scope unavailable");

            var report = new Report();
            var frames = new HashSet<int>();
            GameObject host = null;
            ActorFrameReadPump800 pump = null;
            var diagnosticReader = new RegisteredVehicleEnvelopeReader();
            try
            {
                // Sibling in the actual scene: never add test content beneath the
                // physical owner's exact16-root subtree or Town-owned body roots.
                host = new GameObject("Owned actual actor phase probe800");
                SceneManager.MoveGameObjectToScene(host, actualSession.gameObject.scene);
                pump = host.AddComponent<ActorFrameReadPump800>();
                bool bound = pump.Bind(actualSession, actualTown, (snapshot, publication) =>
                {
                    if (!ReferenceEquals(publication, actualTown)
                        || !ActorFramePhasePolicy800.MatchesAfterActorFrame(snapshot.Frame,
                            snapshot.FixedTimeSeconds, snapshot.Phase, Time.frameCount, Time.fixedTime)
                        || !snapshot.IsCurrent || !publication.IsCurrent
                        || !population.MatchesTownPublication800(publication))
                        throw new InvalidOperationException("Actual physical read stale at immediate probe commit");
                    if (snapshot.Vehicles.Count == 0)
                        throw new InvalidOperationException("Empty physical roster is not an accepted fallback");
                    // Read through the real same-assembly consumer, then commit
                    // immediately in this actual ordered callback. Unsupported
                    // geometry remains an explicit row, never an empty safe roster.
                    if (!diagnosticReader.TryReadAfterActorFrame(population, publication.Ground,
                            publication, out var diagnostics, out string diagnosticReason))
                        throw new InvalidOperationException("Actual physical diagnostics refused: " + diagnosticReason);
                    if (diagnostics.Vehicles.Count != snapshot.Vehicles.Count)
                        throw new InvalidOperationException("Actual diagnostic roster count changed");
                    // All conversion/owner callbacks have finished. Recheck the
                    // exact captured association at this receipt boundary.
                    if (!snapshot.IsCurrent || !publication.IsCurrent
                        || !population.MatchesTownPublication800(publication))
                        throw new InvalidOperationException("Actual receipt association changed during diagnostics");
                    int unboundPhysicalCount = 0;
                    foreach (var vehicle in diagnostics.Vehicles)
                        if (!vehicle.Physical.HasValue) unboundPhysicalCount++;
                    int supportedPhysicalCount = diagnostics.Vehicles.Count - unboundPhysicalCount;
                    if (supportedPhysicalCount < 1)
                        throw new InvalidOperationException("No positive actual physical conversion; all rows UNBOUND");
                    // This is the FINAL owner-dependent call. Its final snapshot
                    // currency and local publication fence follow every prior
                    // owner callback; only pure exact-reference checks follow.
                    if (!diagnosticReader.TryCommitPhysicalDiagnostics(diagnostics, out diagnosticReason))
                        throw new InvalidOperationException("Actual final diagnostic commit refused: " + diagnosticReason);
                    if (!ReferenceEquals(actualSession.ActualVehicles, population)
                        || !ReferenceEquals(publication, actualTown))
                        throw new InvalidOperationException("Actual receipt association changed at final commit");
                    report.LastPhysicalDiagnosticCount = diagnostics.Vehicles.Count;
                    report.LastUnboundPhysicalCount = unboundPhysicalCount;
                    report.LastSupportedPhysicalCount = supportedPhysicalCount;
                    if (frames.Add(snapshot.Frame))
                    {
                        report.ForwardedFrames++;
                        if (report.FirstFrame < 0) report.FirstFrame = snapshot.Frame;
                        report.LastFrame = snapshot.Frame;
                    }
                    report.LastActualVehicleCount = snapshot.Vehicles.Count;
                    report.LastPhase = snapshot.Phase.ToString();
                });
                if (!bound) throw new InvalidOperationException("Actual pump refused binding: " + pump.Reason);
                double deadline = Time.realtimeSinceStartupAsDouble + TimeoutSeconds;
                while (frames.Count < RequiredDistinctFrames && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    if (!actualTown.IsCurrent || !population.MatchesTownPublication800(actualTown))
                        throw new InvalidOperationException("Actual source scope retired during phase probe");
                    report.Reason = pump.Reason;
                    yield return null;
                }
                report.Reason = pump.Reason;
                if (frames.Count < RequiredDistinctFrames)
                    throw new InvalidOperationException("Actual AfterActorFrame forwarding timed out: " + report.Reason);
                pump.Retire("Actual phase probe receipt complete");
                pump.enabled = false;
                report.ActualOrderedPhysicalReadPassed = true;
                completed(report);
            }
            finally
            {
                if (pump)
                {
                    pump.Retire("Actual phase probe cleanup");
                    pump.enabled = false;
                }
                diagnosticReader.Dispose();
                if (host) UnityEngine.Object.Destroy(host);
                // The parent runner confirms the deferred host destruction and
                // actual process-group terminal state; timeout alone is no release.
            }
        }
    }
}
