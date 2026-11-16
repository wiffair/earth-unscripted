using System;
using UnityEngine;
using RoadRush.Core800;
using RoadRush.FunnyAnimals.Core800Proposal;

namespace RoadRush.FunnyAnimals.Scheduling800
{
    /// <summary>
    /// Actual-API consumer that memoizes only the fixed-radius ground candidate's
    /// immutable results. It issues no lease, actor registry or ISafeWorld.
    /// </summary>
    /// <remarks>
    /// Main-thread only. Coordinates are exact caller binary32 world XZ metres,
    /// with the candidate's existing0.85m animal footprint. Source hazards, actor
    /// occupancy, Town walls and habitat must be checked separately on every
    /// admission attempt, including cache hits. Their answers are never stored.
    /// </remarks>
    public sealed class LeasedStaticGround800 : IDisposable
    {
        readonly Grounded800Candidate candidate = new Grounded800Candidate();
        readonly StaticGroundMemo800<Grounded800Candidate.Ground> memo = new StaticGroundMemo800<Grounded800Candidate.Ground>();
        readonly Func<bool> ownerCurrent;
        readonly StaticGroundProbe<Grounded800Candidate.Ground> probe;
        GroundLease800 lease;
        IDisposable revocationSubscription;
        private const int MaximumLogicalAttemptsPerFrame = 64;
        int frame = -1;
        int logicalAttemptFrame = -1, logicalAttempts;
        bool refused, stepping, disposeCandidatePending;

        /// <summary>Staging status only; all actual queries still check the owner.</summary>
        public bool PreparationCompleted => !refused && candidate.Ready;
        public bool GameplaySemanticsBound => false;
        public int CacheHitsThisFrame => memo.CacheHitsThisFrame;
        public int ProbeCallsThisFrame => memo.ProbeCallsThisFrame;
        // Pure last stored field, not a new/current owner query. Unknown can retain
        // an earlier reason; callers must label it as such, never infer a fresh hit.
        public string LastCandidateReason => candidate.Reason;
        private PointObservation activePointObservation;
        private const int MaximumDiagnosticReasonCharacters = 512;

        /// <summary>Copied data from this exact query's actual candidate callback.
        /// Cache hits have CandidateProbeRan=false; no stale envelope is substituted.</summary>
        public sealed class PointObservation
        {
            public bool CandidateProbeRan;
            public string LastCandidateReason;
            public Envelope? Envelope;
        }

        public LeasedStaticGround800(GroundLease800 actualIssuedLease)
        {
            ownerCurrent = CheckCurrent;
            probe = Probe;
            if (actualIssuedLease == null || !actualIssuedLease.IsCurrent)
                throw new ArgumentException("Actual current private-issued ground lease required");
            lease = actualIssuedLease;
            revocationSubscription = lease.BeforeRevoke((expired, reason) =>
            {
                if (ReferenceEquals(expired, lease)) Invalidate();
            });
            if (!candidate.Bind(lease))
            {
                Dispose();
                throw new InvalidOperationException("Existing reviewed ground profile refused");
            }
        }

        public void BeginFrame()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (refused || lease == null || !memo.BeginFrame(frame, lease) || !CheckCurrent()) return;
            stepping = true;
            try { candidate.BeginFrame(); }
            finally
            {
                stepping = false;
                if (disposeCandidatePending) { candidate.Dispose(); disposeCandidatePending = false; }
            }
        }

        public StaticProbeStatus Query(float x, float z, out Grounded800Candidate.Ground ground)
        {
            ground = default;
            if (!SpendLogicalAttempt()) return StaticProbeStatus.Unknown;
            BeginFrame();
            return memo.Query(x, z, ownerCurrent, probe, out ground);
        }

        /// <summary>
        /// Same existing lookup/gates with copied diagnostics. The scoped managed
        /// context is restored after synchronous reentrancy; no extra probe/current
        /// call and no cached hazard/owner grant. Cache-hit cause was not retained by
        /// the original memo and remains explicitly unavailable.
        /// </summary>
        public StaticProbeStatus Query(float x, float z, out Grounded800Candidate.Ground ground,
            out PointObservation observation)
        {
            ground = default;
            observation = new PointObservation();
            if (!SpendLogicalAttempt()) return StaticProbeStatus.Unknown;
            BeginFrame();
            PointObservation previous = activePointObservation;
            activePointObservation = observation;
            try { return memo.Query(x, z, ownerCurrent, probe, out ground); }
            finally { activePointObservation = previous; }
        }

        /// <summary>
        /// Fresh whole-capsule source-ground exclusion at exact binary32 endpoints.
        /// Corridor results are NEVER memoized. The existing candidate retains its
        /// footprint/bank/face budgets; point-height memoization remains separate.
        /// Clear is a copied source-geometry fact, not native coverage/contact proof.
        /// </summary>
        public Verdict InspectCorridor(float fromX, float fromZ, float toX, float toZ,
            out Envelope envelope)
        {
            envelope = default;
            if (!SpendLogicalAttempt()) return Verdict.Unknown;
            BeginFrame();
            var capturedLease = lease;
            if (capturedLease == null || !CheckCurrent()) return Verdict.Unknown;
            Verdict result = candidate.InspectCorridor(fromX, fromZ, toX, toZ, out envelope);
            if (!CheckCurrent() || refused || !ReferenceEquals(capturedLease, lease))
            {
                envelope = default;
                return Verdict.Unknown;
            }
            return result;
        }

        bool SpendLogicalAttempt()
        {
            // Shared by cached point reads and fresh corridor reads. Spend BEFORE
            // BeginFrame/any owner query; Dispose never refills this frame's gate.
            int actualFrame = Time.frameCount;
            if (logicalAttemptFrame != actualFrame)
            {
                logicalAttemptFrame = actualFrame;
                logicalAttempts = 0;
            }
            if (logicalAttempts >= MaximumLogicalAttemptsPerFrame) return false;
            logicalAttempts++;
            return true;
        }

        bool CheckCurrent()
        {
            try
            {
                if (!refused && lease != null && lease.IsCurrent) return true;
            }
            catch { }
            Invalidate();
            return false;
        }

        StaticProbeStatus Probe(float x, float z, out Grounded800Candidate.Ground ground)
        {
            ground = default;
            if (!candidate.Ready) return StaticProbeStatus.Unknown;
            PointObservation observed = activePointObservation;
            if (observed != null) observed.CandidateProbeRan = true;
            var verdict = candidate.TryGround(x, z, out ground, out Envelope envelope);
            if (observed != null)
            {
                string copiedReason = candidate.Reason;
                observed.LastCandidateReason = copiedReason == null || copiedReason.Length <= MaximumDiagnosticReasonCharacters
                    ? copiedReason : copiedReason.Substring(0, MaximumDiagnosticReasonCharacters) + "[truncated]";
                observed.Envelope = envelope;
            }
            switch (verdict)
            {
                case Verdict.Clear: return StaticProbeStatus.Clear;
                case Verdict.Blocked: return StaticProbeStatus.Blocked;
                default: return StaticProbeStatus.Unknown;
            }
        }

        void Invalidate()
        {
            refused = true;
            memo.Invalidate();
            // Candidate's own before-revoke handler owns its iterator cleanup.
            // Never dispose it reentrantly from inside an owner query/MoveNext.
        }

        public void Dispose()
        {
            Invalidate();
            revocationSubscription?.Dispose();
            revocationSubscription = null;
            lease = null;
            if (stepping) disposeCandidatePending = true;
            else candidate.Dispose();
        }
    }
}
