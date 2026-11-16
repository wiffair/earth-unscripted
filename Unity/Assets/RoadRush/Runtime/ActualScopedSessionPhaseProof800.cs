using System;
using System.Collections;
using System.Collections.Generic;
using RoadRush.FunnyAnimals.ActualRegistryConsumer;
using RoadRush.Scenery800;
using RoadRush.Scenery800.Verification;
using RoadRush.VehiclePopulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadRush
{
    /// <summary>
    /// Main-thread actual-session observer, run by a separately guarded isolated
    /// native wrapper. It never creates/injects World, Profile, Car, Ready, roster,
    /// publication or rendered callbacks. No native result is claimed by compile.
    /// This deliberately RETIRES the supplied actual world at the end.
    /// </summary>
    public static class ActualScopedSessionPhaseProof800
    {
        const int RequiredFullRosterFrames = 3;
        const double TimeoutSeconds = 15;
        const int ExpectedVehicles = 28, ExpectedTraffic = 24, ExpectedPolice = 3;

        [Serializable]
        public sealed class Report
        {
            public int FullRosterFrames, ActualVehicles, SupportedPhysical, UnboundPhysical;
            public int Players, Traffic, Police;
            public bool MinimalSceneryProbePassed, ActualSameSessionAssociation;
            public bool PublicationRetired, GroundRetired, HeldSnapshotRefused, FreshCaptureRefused;
            public bool GameplayAdmission => false;
            public string RawDocumentDigest, SourceDigest, FinalBufferDigest;
        }

        public static IEnumerator Run(GameSession actualSession, Action<Report> completed)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(PlayerProfile.StorageOverride) ||
                !actualSession || !actualSession.BootReady || !actualSession.IsScopedReaderSession800 ||
                !actualSession.Menu || Time.timeScale != 0 || completed == null)
                throw new InvalidOperationException("Actual ready isolated scoped reader boot required.");
            WorldBuilder world = actualSession.World;
            var publication = world.GeographicWorld.CurrentTownPublication800;
            var population = actualSession.ActualVehicles;
            if (publication == null || population == null || !population.MatchesTownPublication800(publication) ||
                !ReferenceEquals(publication.Ground, world.ScopedGround800))
                throw new InvalidOperationException("Actual same session/Ground/Town references unavailable.");
            var report = new Report
            {
                ActualSameSessionAssociation = true,
                RawDocumentDigest = world.ScopedSession800.Plan.RawDocumentDigest,
                SourceDigest = world.ScopedSession800.Plan.SourceDigest,
                FinalBufferDigest = world.ScopedSession800.Plan.FinalBufferDigest
            };
            ActualActorFramePhaseProbe800.Report minimal = null;
            yield return ActualActorFramePhaseProbe800.Run(actualSession, publication, value => minimal = value);
            if (minimal == null || !minimal.ActualOrderedPhysicalReadPassed)
                throw new InvalidOperationException("Actual minimal Scenery phase proof failed.");
            report.MinimalSceneryProbePassed = true;

            GameObject observer = null;
            ActorFrameReadPump800 pump = null;
            ActualVehiclePopulationSnapshot held = null;
            var distinctFrames = new HashSet<int>();
            var reader = new RegisteredVehicleEnvelopeReader();
            IDisposable retirement = null;
            bool retired = false;
            try
            {
                retirement = publication.BeforeRevoke((_, reason) => retired = true);
                observer = new GameObject("Actual full28 scoped reader observer800");
                SceneManager.MoveGameObjectToScene(observer, actualSession.gameObject.scene);
                pump = observer.AddComponent<ActorFrameReadPump800>();
                if (!pump.Bind(actualSession, publication, (snapshot, samePublication) =>
                {
                    if (!ReferenceEquals(samePublication, publication) || !snapshot.IsCurrent ||
                        !population.MatchesTownPublication800(publication) || !publication.ValidateSourceNow())
                        throw new InvalidOperationException("Full roster phase association stale.");
                    int players = 0, traffic = 0, police = 0;
                    foreach (var vehicle in snapshot.Vehicles)
                    {
                        if (vehicle.Role == "Player") players++;
                        else if (vehicle.Role == "Traffic") traffic++;
                        else if (vehicle.Role == "Police") police++;
                        else throw new InvalidOperationException("Unexpected actual vehicle role in default boot.");
                    }
                    if (snapshot.Vehicles.Count != ExpectedVehicles || players != 1 ||
                        traffic != ExpectedTraffic || police != ExpectedPolice)
                        throw new InvalidOperationException("Actual full28 role roster incomplete.");
                    if (!reader.TryReadAfterActorFrame(population, publication.Ground, publication,
                        out var diagnostics, out string reason))
                        throw new InvalidOperationException("Actual full roster diagnostics refused: " + reason);
                    int unbound = 0;
                    foreach (var diagnostic in diagnostics.Vehicles)
                        if (!diagnostic.Physical.HasValue) unbound++;
                    if (diagnostics.Vehicles.Count != ExpectedVehicles || unbound != 0)
                        throw new InvalidOperationException("Full28 claim refused: unsupported actual physical rows " + unbound);
                    if (!reader.TryCommitPhysicalDiagnostics(diagnostics, out reason) || !snapshot.IsCurrent ||
                        !population.MatchesTownPublication800(publication))
                        throw new InvalidOperationException("Full28 immediate commit refused: " + reason);
                    report.Players = players; report.Traffic = traffic; report.Police = police;
                    report.ActualVehicles = diagnostics.Vehicles.Count;
                    report.UnboundPhysical = unbound;
                    report.SupportedPhysical = ExpectedVehicles - unbound;
                    held = snapshot;
                    distinctFrames.Add(snapshot.Frame);
                    report.FullRosterFrames = distinctFrames.Count;
                })) throw new InvalidOperationException("Actual full28 pump refused: " + pump.Reason);

                double deadline = Time.realtimeSinceStartupAsDouble + TimeoutSeconds;
                while (distinctFrames.Count < RequiredFullRosterFrames && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                if (distinctFrames.Count < RequiredFullRosterFrames)
                    throw new TimeoutException("Actual full28 phase frames missing: " + pump.Reason);
                pump.Retire("Full28 scoped read complete before actual ground retirement");
                pump.enabled = false;
                // Genuine issuer cleanup, not a fake liveness toggle. BeforeRevoke
                // must invalidate semantic consumers synchronously before destruction.
                world.RetireScopedSession800("Actual scoped phase proof retirement");
                report.PublicationRetired = retired && !publication.IsCurrent;
                report.GroundRetired = !publication.Ground.IsCurrent;
                report.HeldSnapshotRefused = held != null && !held.IsCurrent;
                report.FreshCaptureRefused = !population.TryCapture(out _, out _);
                if (!report.PublicationRetired || !report.GroundRetired ||
                    !report.HeldSnapshotRefused || !report.FreshCaptureRefused)
                    throw new InvalidOperationException("Actual retirement failed to revoke existing scope reads.");
                completed(report);
            }
            finally
            {
                retirement?.Dispose();
                if (pump) { pump.Retire("Scoped phase observer cleanup"); pump.enabled = false; }
                reader.Dispose();
                if (observer) UnityEngine.Object.Destroy(observer);
            }
        }
    }
}
