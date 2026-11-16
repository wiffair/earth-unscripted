using System;

namespace EarthUnscripted.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Pure LOCAL candidate identity/version fence, not an actual actor/source epoch or
    /// capability. Only explicit candidate publication changes the baseline. Observing
    /// or validating a different pose never accepts it. Invalidation/disposal retires
    /// observations before reentrant readers can dereference old private state.
    /// Single-threaded with synchronous reentrancy, matching producer call ownership.
    /// </summary>
    internal sealed class PresentationCandidateBaseline<T> : IDisposable where T : class
    {
        private T candidate;
        private long version;
        private bool disposed;
        internal T Candidate => candidate;

        internal bool TrySetCandidate(T replacement)
        {
            if (replacement == null || disposed || version == long.MaxValue) return false;
            version++;
            candidate = replacement;
            return true;
        }

        internal void Clear()
        {
            candidate = null;
            if (version == long.MaxValue) disposed = true;
            else version++;
        }

        internal bool TryObserveExpected(T expected, out long observedVersion)
        {
            observedVersion = version;
            return !disposed && expected != null && ReferenceEquals(expected, candidate);
        }

        internal bool MatchesExpected(T expected, long observedVersion) =>
            !disposed && observedVersion == version && expected != null && ReferenceEquals(expected, candidate);

        /// <summary>Checks an observation without changing the accepted candidate.</summary>
        internal bool MatchesObserved(T expected, long observedVersion,
            string expectedSignature, string observedSignature) =>
            MatchesExpected(expected, observedVersion) && expectedSignature != null
                && string.Equals(expectedSignature, observedSignature, StringComparison.Ordinal);

        public void Dispose()
        {
            disposed = true;
            Clear();
        }
    }
}
