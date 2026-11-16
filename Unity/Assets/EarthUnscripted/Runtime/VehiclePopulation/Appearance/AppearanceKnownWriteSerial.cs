using System;

namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Main-thread private-owner bookkeeping for ALL routed known content/font writes,
    /// not a registry/clean/native capability. A cold capture has not yet indexed every
    /// borrowed mesh, so borrower-specific invalidation alone cannot fence it. Every
    /// actual known writer enters this serial before any native writer-side getters.
    /// Active scopes also refuse a capture nested INSIDE an already-started writer.
    /// Counters are event counts, not frames/time; overflow permanently fails closed.
    /// </summary>
    internal sealed class AppearanceKnownWriteSerial
    {
        private long serial;
        private int activeWrites;
        private bool retired;
        internal bool CanObserve => !retired && activeWrites == 0;

        internal IDisposable BeginWrite()
        {
            if (retired || serial == long.MaxValue || activeWrites == int.MaxValue)
            {
                Retire();
                return EmptyScope.Instance;
            }
            serial++;
            activeWrites++;
            return new WriteScope(this);
        }

        internal bool TryCapture(out long observedSerial)
        {
            observedSerial = serial;
            return CanObserve;
        }

        internal bool IsUnchanged(long observedSerial) => CanObserve && observedSerial == serial;
        internal void Retire() { retired = true; }

        private sealed class WriteScope : IDisposable
        {
            private AppearanceKnownWriteSerial owner;
            internal WriteScope(AppearanceKnownWriteSerial owner) { this.owner = owner; }
            public void Dispose()
            {
                AppearanceKnownWriteSerial previous = owner;
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
