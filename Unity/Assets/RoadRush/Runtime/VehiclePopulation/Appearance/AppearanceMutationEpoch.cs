using System;

namespace RoadRush.VehiclePopulation
{
    /// <summary>
    /// Private population bookkeeping, NOT an owner/provider capability. Main-thread
    /// callers fence actual before-write scopes and candidate identity. Revision units
    /// are monotonically increasing events; exhaustion, retirement and unmatched scope
    /// completion permanently refuse publication. No Unity object or external clean
    /// flag can create a publication through this class.
    /// </summary>
    internal sealed class AppearanceMutationEpoch
    {
        internal readonly struct Observation
        {
            internal readonly long Serial;
            internal readonly long Content;
            internal Observation(long serial, long content) { Serial = serial; Content = content; }
        }

        private long serial;
        private long content;
        private int activeWrites;
        private bool retired;
        private object candidate;
        private long candidateSerial = -1;
        internal long ContentRevision => content;
        internal bool CanObserve => !retired && activeWrites == 0;

        /// <summary>
        /// Must execute before the first scratch/native/hierarchy write. Nested scopes
        /// are supported. Content changes invalidate immutable mesh templates; pose,
        /// LOD/visibility/text changes invalidate the observation without forcing a
        /// literal-vertex rescan. A thrown writer still closes its using scope.
        /// </summary>
        internal IDisposable BeginWrite(bool changesContent)
        {
            candidate = null;
            candidateSerial = -1;
            if (retired || serial == long.MaxValue || changesContent && content == long.MaxValue
                || activeWrites == int.MaxValue)
            {
                Retire();
                return EmptyScope.Instance;
            }
            serial++;
            if (changesContent) content++;
            activeWrites++;
            return new WriteScope(this);
        }

        internal bool TryObserve(out Observation observation)
        {
            observation = default;
            if (!CanObserve || serial == long.MaxValue) { if (serial == long.MaxValue) Retire(); return false; }
            // Reserve this capture attempt. A reentrant newer capture must prevent an
            // older result from overwriting its candidate even without a writer.
            serial++;
            candidate = null;
            candidateSerial = -1;
            observation = new Observation(serial, content);
            return true;
        }

        internal bool IsUnchanged(Observation observation) => CanObserve
            && observation.Serial == serial && observation.Content == content;

        internal bool TryPublishCandidate(Observation observation, object actualCandidate)
        {
            if (actualCandidate == null || !IsUnchanged(observation)) return false;
            candidate = actualCandidate;
            candidateSerial = serial;
            return true;
        }

        internal bool IsExpectedCandidate(object expected, Observation observation) =>
            expected != null && ReferenceEquals(candidate, expected) && candidateSerial == serial
            && IsUnchanged(observation);

        internal void Retire()
        {
            retired = true;
            candidate = null;
            candidateSerial = -1;
        }

        private sealed class WriteScope : IDisposable
        {
            private AppearanceMutationEpoch owner;
            internal WriteScope(AppearanceMutationEpoch owner) { this.owner = owner; }
            public void Dispose()
            {
                AppearanceMutationEpoch previous = owner;
                owner = null;
                if (previous == null) return;
                if (previous.activeWrites <= 0) previous.Retire();
                else previous.activeWrites--;
            }
        }

        private sealed class EmptyScope : IDisposable
        {
            internal static readonly EmptyScope Instance = new EmptyScope();
            public void Dispose() { }
        }
    }
}
