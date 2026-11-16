using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EarthUnscripted.Scenery800.CandidateEnumeration;
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
    public sealed class NativeEnumerationProbe800 : IDisposable
    {
        public const int RequiredActualActors = 28;
        public const int MaximumWarmAttempts = 120;
        public const double MaximumTotalSeconds = 420;

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
            public int CompletedHistoricalJobs, CommittedClearQueries;
            public bool Warm28Committed, AllHistoricalJobsObserved, DeadlineExpired, Cancelled, CleanupFailed, DisposedBeforeRunCompleted, CallerRefused;
            public DeterministicProviderEnumeration800 Enumeration;
            public bool HeldRefusedAfterRetirement, FreshCaptureRefusedAfterRetirement, RetirementObserved;
            public string Reason = "Not run";
            public bool PositiveObservation => AllHistoricalJobsObserved && !DeadlineExpired && !Cancelled && !CleanupFailed && !DisposedBeforeRunCompleted
                && !CallerRefused && Warm28Committed && Enumeration != null && Enumeration.AnyCommittedClearQueryObserved;
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
        /// Exact20 historical queries may be Blocked/Unknown; minimumoneadditionalorhistorical genuinelycommittedClear query is positive provider-query scope only. Optionalpair is separate evidence.
        /// </summary>
        public IEnumerator Run(GameSession actualSession, ReviewedCanonicalRaw13Input genuineReviewedInput,
            ReadOnlyCollection<DeterministicCandidateInputs800.Candidate> placementCandidates, CancellationToken cancellation)
        {
            if (started || disposed) throw new InvalidOperationException("One-shot probe already used/disposed");
            started = true;
            if (!Application.isPlaying || string.IsNullOrEmpty(PlayerProfile.StorageOverride))
                throw new InvalidOperationException("Isolated native PlayMode/save override required");
            if (!actualSession || genuineReviewedInput == null || placementCandidates == null
                || placementCandidates.Count == 0 || placementCandidates.Count > DeterministicCandidateInputs800.MaximumRetainedBundles)
                throw new ArgumentException("Genuine session/input and bounded nonempty unbound candidates required");
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
                // Existing genuine chain and real31000 pump only. Original20 run
                // inside this caller exactly ONCE before any additional grid request.
                Result.Enumeration = new DeterministicProviderEnumeration800(chain, placementCandidates, cancellation);
                bool bound = pump.Bind(session, actualTown, (snapshot, publication) =>
                {
                    if (disposed || Result.CallerRefused || Result.Enumeration.Stopped) return;
                    // All native/snapshot diagnostics precede the enum's authentic
                    // finalcommit; only copied managed state follows its return.
                    if (Time.timeScale != 0 || snapshot.Vehicles.Count != RequiredActualActors)
                    { Result.CallerRefused = true; Result.Reason = "Requires actual Clock0 complete28 roster"; return; }
                    try
                    {
                    Attempt warmDiagnostic = null;
                    if (!Result.Warm28Committed)
                    {
                        if (Result.Attempts.Count >= MaximumWarmAttempts)
                        { Result.CallerRefused = true; Result.Reason = "Warm diagnostic cap"; return; }
                        warmDiagnostic = new Attempt { WarmOnly = true, Frame = snapshot.Frame,
                            ActualRows = snapshot.Vehicles.Count, JobId = "warm-existing-actual-reader",
                            SamplePhase = snapshot.Phase.ToString(), SampleFixedTimeSeconds = snapshot.FixedTimeSeconds };
                        Result.Attempts.Add(warmDiagnostic);
                        warmDiagnostic.Actors = ActorRefusalObservation800.CopyOrderedRows(snapshot,
                            out warmDiagnostic.ActorDiagnosticReason);
                    }
                        if (Result.Enumeration.ObserveNextInOrderedCallback(out var observed) && observed != null)
                        {
                            if (warmDiagnostic != null)
                            { warmDiagnostic.Began = observed.Began; warmDiagnostic.Committed = observed.Committed; warmDiagnostic.Reason = observed.Reason; }
                            Result.Warm28Committed = Result.Enumeration.Attempts.Count > 0
                                && Result.Enumeration.Attempts[0].Phase == "WARM_EXISTING_CHAIN"
                                && Result.Enumeration.Attempts[0].Committed;
                            // Warm may legitimately need several frames; actual
                            // committed warm observation, not a guessed first frame.
                            foreach (var attempt in Result.Enumeration.Attempts)
                                if (attempt.Phase == "WARM_EXISTING_CHAIN" && attempt.Committed) Result.Warm28Committed = true;
                            Result.CompletedHistoricalJobs = Result.Enumeration.HistoricalCompleted;
                            Result.CommittedClearQueries = Result.Enumeration.CommittedClearQueryCount;
                        }
                    }
                    catch (Exception error)
                    { Result.CallerRefused = true; Result.Reason = "Enumeration callback refused: " + error.GetType().Name; }
                });
                if (!bound) { Result.Reason = pump.Reason; yield break; }
                while (!Result.Enumeration.Stopped && !Result.CallerRefused && !Expired(began, cancellation)) yield return null;
                Expired(began, cancellation); // Include final synchronous callback slice.
                Result.DeadlineExpired |= Result.Enumeration.StopReason == "COOPERATIVE_DEADLINE";
                Result.Cancelled |= Result.Enumeration.StopReason == "CANCELLED";
                Result.AllHistoricalJobsObserved = Result.Enumeration.HistoricalCompleted == 20;
                Result.Reason = Result.CallerRefused ? Result.Reason : Result.Enumeration.StopReason;
                // Retain this consumer for authentic retirement after a bounded
                // terminal observation; minimumoneclear is separate from optionalpair.
                runCompleted = Result.AllHistoricalJobsObserved && !Result.CallerRefused;
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
            if (Result.Enumeration == null || Result.Enumeration.Attempts.Count == 0
                || Time.frameCount <= Result.Enumeration.Attempts[Result.Enumeration.Attempts.Count - 1].Frame)
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
