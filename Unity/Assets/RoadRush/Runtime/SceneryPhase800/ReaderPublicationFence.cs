using System;

namespace RoadRush.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Pure local read-publication fence for synchronous main-thread reentrancy.
    /// NOT an actor/provider epoch or capability. An owner check can run retirement
    /// callbacks which reenter the reader: an older operation may then neither erase
    /// the replacement nor publish itself. Dispose is terminal. No native access.
    /// </summary>
    internal sealed class ReaderPublicationFence<T> : IDisposable where T : class
    {
        private long operation;
        private bool disposed;
        private T latest;

        internal bool TryBeginRead(out long observedOperation)
        {
            observedOperation = operation;
            if (disposed || operation == long.MaxValue) return false;
            operation++;
            observedOperation = operation;
            latest = null;
            return true;
        }

        internal bool OwnsOperation(long observedOperation) => !disposed && observedOperation == operation;

        internal bool TryPublish(long observedOperation, T candidate)
        {
            if (candidate == null || !OwnsOperation(observedOperation)) return false;
            latest = candidate;
            return true;
        }

        internal bool TryObserveCurrent(T candidate, out long observedOperation)
        {
            observedOperation = operation;
            return candidate != null && !disposed && ReferenceEquals(latest, candidate);
        }

        internal bool StillCurrent(long observedOperation, T candidate) =>
            OwnsOperation(observedOperation) && candidate != null && ReferenceEquals(latest, candidate);

        internal void ClearIfOwned(long observedOperation, T expected = null)
        {
            if (OwnsOperation(observedOperation) && (expected == null || ReferenceEquals(latest, expected))) latest = null;
        }

        public void Dispose()
        {
            disposed = true;
            latest = null;
        }
    }
}
