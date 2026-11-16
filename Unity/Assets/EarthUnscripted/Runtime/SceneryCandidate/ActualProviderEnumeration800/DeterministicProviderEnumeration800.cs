using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using EarthUnscripted.FunnyAnimals.Admission;
using EarthUnscripted.FunnyAnimals.Semantics800Proposal;
using EarthUnscripted.Scenery800.Verification;
using UnityEngine;
using Inputs = EarthUnscripted.Scenery800.CandidateEnumeration.DeterministicCandidateInputs800;

namespace EarthUnscripted.Scenery800.CandidateEnumeration
{
    /// <summary>
    /// Explicit bounded caller over ONE existing genuine prepared chain. No factory,
    /// session, observer, MonoBehaviour, terrain, lease or ISafeWorld. Parent invokes
    /// ONLY inside its actual ActorFrameReadPump80031000 callback and verifies actual
    /// Cooked04391/Scope9955/source bindings independently of this DATA input.
    /// Historical20 run FIRST unchanged. All results are provisional observations
    /// until that SAME callback's final authentic chain commit; never actor admission.
    /// </summary>
    public sealed class DeterministicProviderEnumeration800
    {
        public const int MaximumWarmAttempts = 120;
        public const int RequiredHistoricalJobs = 20;
        public const double MaximumSeconds = 120;
        public const int MaximumRequestsPerCallback = 6; // Two3request bundles,<=18 chainqueries and<=10 unique0.5m samples.
        public const int MaximumAttemptRows = MaximumWarmAttempts + RequiredHistoricalJobs + Inputs.MaximumRetainedBundles;
        public const string HistoricalInputSha256 = "511046597b742f901d0bb9f5a444b77d299f0e1686e254b282310bf073a325f4";

        public sealed class QueryObservation
        {
            public string Id, Purpose;
            public int Kind;
            public float FromX, FromZ, ToX, ToZ;
            public Verdict Raster = Verdict.Unknown, Exclusions = Verdict.Unknown;
            public bool Requested, AllPredicatesReturned, AreaKnown, WholeAreaInside;
            public ExclusionObservation800 FirstStage; // Copied diagnostic ONLY; never permission.
            public bool AllClear => Requested && AllPredicatesReturned && Raster == Verdict.Clear && AreaKnown && WholeAreaInside && Exclusions == Verdict.Clear;
        }
        public sealed class Attempt
        {
            public string Phase, CandidateId, Reason;
            public int Frame;
            public bool Began, AuthenticCommitReturnedTrue, Committed, Expired, Cancelled;
            public readonly List<QueryObservation> Queries = new List<QueryObservation>();
            public bool CandidateAllClear, RetestedOtherSpeciesAllClear, SameFramePairConfirmed;
        }
        public IReadOnlyList<Attempt> Attempts => attempts;
        public bool Stopped { get; private set; }
        public string StopReason { get; private set; } = "UNBOUND_NOT_STARTED";
        public int HistoricalCompleted => historicalIndex;
        public int HistoricalAttempted { get; private set; }
        public int AdditionalAttempted { get; private set; }
        public int AdditionalProcessed => additionalIndex;
        public bool SameFrameCommittedPairObserved { get; private set; }
        public int CommittedClearQueryCount { get; private set; }
        public bool AnyCommittedClearQueryObserved => CommittedClearQueryCount > 0;
        public bool GameplayAdmission => false;
        public bool PhysicsContactProved => false;

        private readonly List<Attempt> attempts = new List<Attempt>();
        private readonly ReadOnlyCollection<Inputs.Candidate> candidates;
        private readonly ActualSceneryReadChain800 actualChain;
        private readonly NativeProviderProbe800.Job[] historical;
        private readonly CancellationToken cancellation;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private int lastAttemptFrame = -1, warmAttempts, historicalIndex, additionalIndex;
        private bool warmed;
        private Inputs.Candidate lastSheep, lastDeer;

        /// <summary>Input must be the builder's stable fair shortlist (or canonical
        /// reconstructed ordinal lists). Hash-pin original historical JSON and new
        /// DATA file in the parent input binding. Headers are not source authority.</summary>
        public DeterministicProviderEnumeration800(ActualSceneryReadChain800 existingGenuineChain,
            ReadOnlyCollection<Inputs.Candidate> unboundCandidates,
            CancellationToken token = default)
        {
            if (unboundCandidates == null || unboundCandidates.Count > Inputs.MaximumRetainedBundles)
                throw new ArgumentException("Bounded unbound shortlist required");
            if (existingGenuineChain == null) throw new ArgumentNullException(nameof(existingGenuineChain));
            actualChain = existingGenuineChain; // BORROWED consumer; never disposed or rebound here.
            candidates = unboundCandidates;
            cancellation = token;
            historical = PlacementProbeJobs800.Create(); // EXACT existing20 data factory, never regenerated/moved.
            if (historical.Length != RequiredHistoricalJobs || PlacementProbeJobs800.SourceJsonSha256 != HistoricalInputSha256)
                throw new InvalidOperationException("Historical20 source dependency changed");
        }

        /// <summary>
        /// One warm/history/candidate bundle per real31000callback/frame; budget is
        /// spent BEFORE chain/owner calls. Retests the previously successful OTHER
        /// species in the SAME callback before claiming a pair, then ONE finalcommit.
        /// Thus moving actor bounds cannot turn two historical frames into a current
        /// simultaneous pair. No dynamic verdict is cached as permission.
        /// </summary>
        public bool ObserveNextInOrderedCallback(out Attempt row)
        {
            row = null;
            ActualSceneryReadChain800 actualPreparedChain = actualChain;
            if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Main-thread caller only");
            if (Stopped) return false;
            if (cancellation.IsCancellationRequested) { Stop("CANCELLED"); return false; }
            if (elapsed.Elapsed.TotalSeconds > MaximumSeconds) { Stop("COOPERATIVE_DEADLINE"); return false; }
            int frame = Time.frameCount;
            if (frame <= lastAttemptFrame) return false;
            lastAttemptFrame = frame; // BEFORE actualPreparedChain and its owner predicates.
            if (attempts.Count >= MaximumAttemptRows) { Stop("ATTEMPT_ROW_CAP"); return false; }
            if (actualPreparedChain == null) { Stop("ACTUAL_CHAIN_UNAVAILABLE"); return false; }
            row = new Attempt { Frame = frame, Phase = !warmed ? "WARM_EXISTING_CHAIN" : historicalIndex<RequiredHistoricalJobs ? "HISTORICAL20_UNCHANGED" : Inputs.Algorithm };
            attempts.Add(row);
            Inputs.Candidate candidate = null, other = null;
            if (!warmed) row.CandidateId = "warm-existing-actual-reader";
            else if (historicalIndex < RequiredHistoricalJobs)
            {
                row.CandidateId = historical[historicalIndex].Id;
                HistoricalAttempted++; // Planned original attempt, never mistaken for completed execution.
            }
            else if (additionalIndex < candidates.Count)
            {
                candidate = candidates[additionalIndex];
                AdditionalAttempted++;
                row.CandidateId = candidate.Id;
                other = candidate.Kind == 1 ? lastDeer : lastSheep;
            }
            else
            {
                attempts.RemoveAt(attempts.Count-1);
                row = null;
                Stop("RETAINED_PREFIX_EXHAUSTED_NOT_CONTINUOUS_WORLD_EXHAUSTION");
                return false; // No owner query when there is no next request.
            }
            try
            {
                row.Began = actualPreparedChain.TryBeginAfterActorFrame(out string reason);
                row.Reason = reason;
                if (row.Began)
                {
                    if (!warmed) row.CandidateId = "warm-existing-actual-reader";
                    else if (historicalIndex < RequiredHistoricalJobs)
                    {
                        NativeProviderProbe800.Job job = historical[historicalIndex];
                        row.CandidateId = job.Id;
                        row.CandidateAllClear = Query(actualPreparedChain,row,job.Id,"historical-original",job.RasterKind,job.Area,
                            job.FromX,job.FromZ,job.ToX,job.ToZ).AllClear;
                    }
                    else if (additionalIndex < candidates.Count)
                    {
                        candidate = candidates[additionalIndex];
                        row.CandidateId = candidate.Id;
                        row.CandidateAllClear = Bundle(actualPreparedChain,row,candidate,"new-grid-candidate");
                        other = candidate.Kind == 1 ? lastDeer : lastSheep;
                        if (other != null) row.RetestedOtherSpeciesAllClear = Bundle(actualPreparedChain,row,other,"fresh-retest-other-species");
                    }
                    else { Stop("RETAINED_PREFIX_EXHAUSTED_NOT_CONTINUOUS_WORLD_EXHAUSTION"); return false; }
                    // FINAL OWNER-DEPENDENT CALL. No chain/Unity/owner getter after.
                    row.AuthenticCommitReturnedTrue = actualPreparedChain.TryCommitCurrentProbe(out reason);
                    row.Committed = row.AuthenticCommitReturnedTrue;
                    row.Reason = reason;
                }
            }
            catch (Exception error)
            {
                row.Committed = false;
                row.Reason = "Optional enumeration consumer refused: " + error.GetType().Name;
                // A retained earlier Clear cannot hide a later partial callback.
                // Mark terminal before rethrow so every caller sees the failure;
                // the genuine native caller sets its independent CallerRefused veto.
                Stop("ENUMERATION_CONSUMER_EXCEPTION");
                throw;
            }
            // Stopwatch is an OS monotonic timing observation, not a game/owner
            // callback. Cooperative terminal-slice deadline; never hard preemption.
            row.Expired = elapsed.Elapsed.TotalSeconds > MaximumSeconds;
            row.Cancelled = cancellation.IsCancellationRequested;
            if (row.Expired || row.Cancelled) { row.Committed = false; Stop(row.Cancelled ? "CANCELLED" : "COOPERATIVE_DEADLINE"); return true; }
            if (row.Committed)
                foreach (QueryObservation query in row.Queries)
                    if (query.AllClear) CommittedClearQueryCount++;
            if (!warmed)
            {
                warmed = row.Began && row.Committed;
                if (!warmed && ++warmAttempts >= MaximumWarmAttempts) Stop("WARM_READ_OR_COMMIT_REFUSED");
                return true;
            }
            if (historicalIndex < RequiredHistoricalJobs)
            {
                if (!HistoricalExecutionComplete(row))
                {
                    Stop("HISTORICAL_ATTEMPT_INCOMPLETE_NO_GRID_ENUMERATION");
                    return true; // Failed row retained; no hidden retry and no fake20completion.
                }
                historicalIndex++;
                return true;
            }
            additionalIndex++;
            if (candidate != null && row.Committed)
            {
                if (other != null && !row.RetestedOtherSpeciesAllClear)
                { if (other.Kind == 1) lastSheep = null; else lastDeer = null; }
                if (row.CandidateAllClear)
                {
                    if (candidate.Kind == 1) lastSheep = candidate; else lastDeer = candidate;
                    if (other != null && row.RetestedOtherSpeciesAllClear)
                    {
                        row.SameFramePairConfirmed = SameFrameCommittedPairObserved = true;
                        Stop("SAME_FRAME_COMMITTED_PROBE_PAIR_NOT_GAMEPLAY_ADMISSION");
                    }
                }
            }
            if (!Stopped && additionalIndex >= candidates.Count) Stop("RETAINED_PREFIX_EXHAUSTED_NOT_CONTINUOUS_WORLD_EXHAUSTION");
            return true;
        }

        // Pure execution accounting, never an owner/currentness grant. Blocked or
        // Unknown is a valid completed historical observation when authentically committed.
        private static bool HistoricalExecutionComplete(Attempt row) => row.Began && row.Committed
            && row.Queries.Count == 1 && row.Queries[0].AllPredicatesReturned;

        private static bool Bundle(ActualSceneryReadChain800 chain, Attempt row, Inputs.Candidate candidate, string purpose)
        {
            bool clear = true;
            foreach (Inputs.Request request in candidate.Requests)
                clear &= Query(chain,row,candidate.Id+"/"+request.Label,purpose,candidate.Kind,candidate.Area,
                    request.FromX,request.FromZ,request.ToX,request.ToZ).AllClear;
            return clear;
        }
        private static QueryObservation Query(ActualSceneryReadChain800 chain, Attempt row, string id, string purpose,
            int kind, AreaKind800 area, float ax, float az, float bx, float bz)
        {
            if (row.Queries.Count >= MaximumRequestsPerCallback) throw new InvalidOperationException("Per-callback request cap");
            var result = new QueryObservation { Id=id,Purpose=purpose,Kind=kind,FromX=ax,FromZ=az,ToX=bx,ToZ=bz };
            row.Queries.Add(result); // Retain even if any optional reader fails.
            result.Requested = true;
            result.Raster = chain.InspectRasterHabitat(kind,ax,az,bx,bz);
            result.AreaKnown = chain.TryWholeCapsuleInside(area,new P(ax,az),new P(bx,bz),out bool inside);
            result.WholeAreaInside = inside;
            result.Exclusions = chain.InspectExclusions(new P(ax,az),new P(bx,bz),out ExclusionObservation800 firstStage);
            result.FirstStage = firstStage;
            result.AllPredicatesReturned = true;
            return result;
        }
        private void Stop(string reason) { Stopped = true; StopReason = reason; }
    }
}
