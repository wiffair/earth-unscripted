using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using EarthUnscripted.Core800;
using EarthUnscripted.FunnyAnimals.Admission;
using EarthUnscripted.FunnyAnimals.Semantics800Proposal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EarthUnscripted.Scenery800.Verification
{
    /// <summary>
    /// Explicit one-shot diagnostic for a genuine already-booted isolated session.
    /// Main thread only. It creates one sibling observer, never a world or actor.
    /// Run BEFORE the existing retirement proof; call ObserveAfterRetirement AFTER
    /// that proof, then Dispose in the wrapper's finally even if either test fails.
    /// No result is gameplay admission, native contact or animal-profile proof.
    /// </summary>
    public sealed class NativeProviderProbe800 : IDisposable
    {
        public const int RequiredActualActors = 28;
        public const int MaximumJobs = 32;
        public const int MaximumWarmAttempts = 120;
        public const double MaximumTotalSeconds = 420;

        /// <summary>Copied input only: exact binary32 source XZ metres, species1/2,
        /// and an explicit whole-source-area requirement. No Y or owner capability.</summary>
        public readonly struct Job
        {
            public readonly string Id;
            public readonly int RasterKind;
            public readonly AreaKind800 Area;
            public readonly float FromX, FromZ, ToX, ToZ;
            public Job(string id, int rasterKind, AreaKind800 area,
                float fromX, float fromZ, float toX, float toZ)
            {
                Id = id; RasterKind = rasterKind; Area = area;
                FromX = fromX; FromZ = fromZ; ToX = toX; ToZ = toZ;
            }
        }

        public sealed class Attempt
        {
            public string JobId, Reason;
            public int Frame, ActualRows;
            // Copied diagnostics only; never substitutes for authentic chain admission.
            public ActorRefusalObservation800[] Actors;
            public string ActorDiagnosticReason, SamplePhase;
            public double SampleFixedTimeSeconds;
            public ExclusionObservation800 ExclusionObservation;
            public bool WarmOnly, Began, AreaKnown, InsideArea, Committed;
            public Verdict Habitat = Verdict.Unknown, Exclusions = Verdict.Unknown;
            public bool CommittedClear => Committed && AreaKnown && InsideArea
                && Habitat == Verdict.Clear && Exclusions == Verdict.Clear;
        }

        public sealed class Report
        {
            public readonly List<Attempt> Attempts = new List<Attempt>();
            public int CompletedJobs, CommittedClearJobs;
            public bool Warm28Committed, AllJobsAttempted, DeadlineExpired, Cancelled, CleanupFailed, DisposedBeforeRunCompleted;
            public bool HeldRefusedAfterRetirement, FreshCaptureRefusedAfterRetirement, RetirementObserved;
            public string Reason = "Not run";
            public bool PositiveObservation => AllJobsAttempted && !DeadlineExpired && !Cancelled && !CleanupFailed && !DisposedBeforeRunCompleted
                && Warm28Committed && CommittedClearJobs > 0;
            public bool GameplayAdmission => false;
            public bool NativeContactProved => false;
        }

        public Report Result { get; } = new Report();
        private ActualSceneryReadChain800 chain;
        private GameSession session;
        private TownSemanticPublication800 capturedTown;
        private CancellationToken runCancellation;
        private IDisposable retirementSubscription;
        private bool started, disposed, runCompleted;

        /// <summary>
        /// Caller must keep the existing real context alive through completion.
        /// Preparation cooperatively yields; deadline cannot interrupt a native call.
        /// Jobs are defensively copied before the first yield. Every attempted warm
        /// or job callback records a row, including Unknown/Blocked/refused commits.
        /// Completion without a committed Clear is explicitly nonpositive.
        /// </summary>
        public IEnumerator Run(GameSession actualSession, ReviewedCanonicalRaw13Input genuineReviewedInput,
            Job[] placementJobs, CancellationToken cancellation)
        {
            if (started || disposed) throw new InvalidOperationException("One-shot probe already used/disposed");
            started = true;
            if (!Application.isPlaying || string.IsNullOrEmpty(PlayerProfile.StorageOverride))
                throw new InvalidOperationException("Isolated native PlayMode/save override required");
            if (!actualSession || genuineReviewedInput == null || placementJobs == null
                || placementJobs.Length == 0 || placementJobs.Length > MaximumJobs)
                throw new ArgumentException("Genuine session/input and bounded nonempty jobs required");
            Job[] jobs = (Job[])placementJobs.Clone();
            foreach (Job job in jobs)
                if (!ValidJob(job)) throw new ArgumentException("Unsupported data-only job");
            session = actualSession;
            runCancellation = cancellation;
            double began = Time.realtimeSinceStartupAsDouble;
            GameObject observer = null;
            ActorFrameReadPump800 pump = null;
            IEnumerator preparation = null;
            try
            {
                if (!ActualSceneryReadChain800.TryCapture(session, out chain, out string reason))
                { Result.Reason = reason; yield break; }
                capturedTown = session.World.GeographicWorld.CurrentTownPublication800;
                TownSemanticPublication800 actualTown = capturedTown;
                retirementSubscription = actualTown.BeforeRevoke((expired, reasonText) =>
                {
                    if (ReferenceEquals(expired, capturedTown)) Result.RetirementObserved = true;
                });
                preparation = chain.Prepare(genuineReviewedInput, cancellation);
                while (!Expired(began, cancellation) && Step(preparation, out object yielded, out reason))
                    yield return yielded;
                Cleanup(() => (preparation as IDisposable)?.Dispose());
                preparation = null;
                if (Expired(began, cancellation) || !chain.Prepared)
                { Result.Reason = "Preparation incomplete: " + (reason ?? chain.Reason); yield break; }
                observer = new GameObject("Owned native provider observation800");
                SceneManager.MoveGameObjectToScene(observer, session.gameObject.scene);
                pump = observer.AddComponent<ActorFrameReadPump800>();
                int warmAttempts = 0;
                bool bound = pump.Bind(session, actualTown, (snapshot, publication) =>
                {
                    if (disposed || Expired(began, cancellation) || Result.CompletedJobs >= jobs.Length) return;
                    bool warming = !Result.Warm28Committed;
                    if (warming && warmAttempts++ >= MaximumWarmAttempts) return;
                    var row = new Attempt { WarmOnly = warming, Frame = snapshot.Frame,
                        ActualRows = snapshot.Vehicles.Count,
                        JobId = warming ? "warm-28-appearance" : jobs[Result.CompletedJobs].Id };
                    Result.Attempts.Add(row); // Preserve the attempt even on optional consumer exceptions.
                    try
                    {
                        // Snapshot fields and pure conversion only, before a refused
                        // chain read can hide which real ordered actor was unbound.
                        row.SamplePhase = snapshot.Phase.ToString();
                        row.SampleFixedTimeSeconds = snapshot.FixedTimeSeconds;
                        row.Actors = ActorRefusalObservation800.CopyOrderedRows(snapshot,
                            out row.ActorDiagnosticReason);
                        if (Time.timeScale != 0 || snapshot.Vehicles.Count != RequiredActualActors)
                        { row.Reason = "Requires actual Clock0 complete28 roster"; return; }
                        row.Began = chain.TryBeginAfterActorFrame(out string readReason);
                        if (!row.Began) { row.Reason = readReason; return; }
                        if (!warming)
                        {
                            Job job = jobs[Result.CompletedJobs];
                            var from = new P(job.FromX, job.FromZ);
                            var to = new P(job.ToX, job.ToZ);
                            row.Habitat = chain.InspectRasterHabitat(job.RasterKind,
                                job.FromX, job.FromZ, job.ToX, job.ToZ);
                            row.AreaKnown = chain.TryWholeCapsuleInside(job.Area, from, to, out bool inside);
                            row.InsideArea = inside;
                            row.Exclusions = chain.InspectExclusions(from, to, out row.ExclusionObservation);
                        }
                        // FINAL owner-dependent call in this callback. Only plain
                        // managed result mutations follow, including deadline/cancel fences.
                        row.Committed = chain.TryCommitCurrentProbe(out string commitReason);
                        row.Reason = commitReason;
                        if (disposed || cancellation.IsCancellationRequested) row.Committed = false;
                        if (warming) Result.Warm28Committed = row.Committed;
                    }
                    catch (Exception error) { row.Committed = false; row.Reason = error.GetType().Name; }
                    finally
                    {
                        if (!warming)
                        {
                            Result.CompletedJobs++;
                            if (row.CommittedClear) Result.CommittedClearJobs++;
                        }
                    }
                });
                if (!bound) { Result.Reason = pump.Reason; yield break; }
                while (Result.CompletedJobs < jobs.Length && warmAttempts <= MaximumWarmAttempts
                    && !Expired(began, cancellation)) yield return null;
                Expired(began, cancellation); // Include the final synchronous callback slice.
                Result.AllJobsAttempted = Result.CompletedJobs == jobs.Length;
                Result.Reason = Result.AllJobsAttempted ? "All jobs observed; inspect every commit/verdict"
                    : "Incomplete/nonpositive provider observation: " + pump.Reason;
                runCompleted = Result.AllJobsAttempted;
            }
            finally
            {
                Cleanup(() => (preparation as IDisposable)?.Dispose());
                if (pump)
                {
                    Cleanup(() => pump.Retire("Provider probe finished"));
                    Cleanup(() => pump.enabled = false);
                }
                if (observer) Cleanup(() => UnityEngine.Object.Destroy(observer));
                // On completed observation retain the CONSUMER ONLY for the actual
                // retirement test. Caller must always Dispose; no owner is retired.
                if (!runCompleted)
                {
                    ActualSceneryReadChain800 localChain = chain;
                    chain = null;
                    Cleanup(() => localChain?.Dispose());
                    IDisposable localRetirementSubscription = retirementSubscription;
                    retirementSubscription = null;
                    Cleanup(() => localRetirementSubscription?.Dispose());
                }
            }
        }

        /// <summary>Invoke synchronously AFTER the existing real retirement proof.
        /// No new source factory/input is consumed: fresh capture must refuse the
        /// retired actual session. A merely disposed held consumer is not accepted.</summary>
        public void ObserveAfterRetirement()
        {
            if (disposed || !runCompleted || chain == null)
                throw new InvalidOperationException("Completed undisposed consumer required");
            if (runCancellation.IsCancellationRequested)
            {
                Result.Cancelled = true;
                throw new InvalidOperationException("Cancellation cannot witness retirement refusal");
            }
            if (!Result.RetirementObserved)
                throw new InvalidOperationException("Actual publication BeforeRevoke event not observed");
            if (capturedTown == null || capturedTown.IsCurrent)
                throw new InvalidOperationException("Actual captured publication has not retired");
            if (Result.Attempts.Count == 0 || Time.frameCount <= Result.Attempts[Result.Attempts.Count - 1].Frame)
                throw new InvalidOperationException("Retirement refusal needs a later actual frame, not the per-frame attempt cap");
            Result.HeldRefusedAfterRetirement = !chain.TryBeginAfterActorFrame(out _);
            bool captured = ActualSceneryReadChain800.TryCapture(session, out var fresh, out _);
            Cleanup(() => fresh?.Dispose());
            if (runCancellation.IsCancellationRequested)
            {
                Result.Cancelled = true;
                Result.HeldRefusedAfterRetirement = false;
                throw new InvalidOperationException("Cancellation changed during retirement observation");
            }
            Result.FreshCaptureRefusedAfterRetirement = !captured;
            Dispose();
        }

        private bool Expired(double began, CancellationToken token)
        {
            bool elapsed = Time.realtimeSinceStartupAsDouble - began > MaximumTotalSeconds;
            Result.DeadlineExpired |= elapsed;
            Result.Cancelled |= token.IsCancellationRequested;
            return disposed || token.IsCancellationRequested || elapsed;
        }

        private static bool Step(IEnumerator iterator, out object yielded, out string reason)
        {
            yielded = null; reason = null;
            try { if (!iterator.MoveNext()) return false; yielded = iterator.Current; return true; }
            catch (Exception error) { reason = error.GetType().Name + ": " + error.Message; return false; }
        }

        private static bool ValidJob(Job job)
        {
            double radius = (double)0.85f;
            double dx = (double)job.ToX - job.FromX, dz = (double)job.ToZ - job.FromZ;
            return !string.IsNullOrEmpty(job.Id) && job.Id.Length <= 128
                && (job.RasterKind == 1 || job.RasterKind == 2)
                && (job.Area == AreaKind800.GrassAgriculture || job.Area == AreaKind800.Woodland)
                && !float.IsNaN(job.FromX) && !float.IsNaN(job.FromZ)
                && !float.IsNaN(job.ToX) && !float.IsNaN(job.ToZ)
                && Math.Abs(job.FromX) + radius <= 400 && Math.Abs(job.FromZ) + radius <= 400
                && Math.Abs(job.ToX) + radius <= 400 && Math.Abs(job.ToZ) + radius <= 400
                && dx * dx + dz * dz <= 8 * 8;
        }

        // Each owned resource gets an independent cleanup attempt. A faulty
        // optional consumer cannot skip observer teardown or manufacture PASS.
        private void Cleanup(Action release)
        {
            try { release(); }
            catch (Exception error)
            {
                Result.CleanupFailed = true;
                Result.Reason = "Owned consumer cleanup refused: " + error.GetType().Name;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Result.DisposedBeforeRunCompleted |= started && !runCompleted;
            disposed = true;
            ActualSceneryReadChain800 localChain = chain;
            IDisposable localRetirementSubscription = retirementSubscription;
            retirementSubscription = null;
            chain = null; session = null; capturedTown = null;
            Cleanup(() => localRetirementSubscription?.Dispose());
            Cleanup(() => localChain?.Dispose());
        }
    }
}
