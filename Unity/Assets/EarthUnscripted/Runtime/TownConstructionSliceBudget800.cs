using System;
using System.Diagnostics;

namespace EarthUnscripted
{
    /// <summary>
    /// Main-thread cooperative Town construction scheduler. This owns timing only: it
    /// grants no source, geometry, readiness or publication authority. A caller must
    /// still validate cancellation and the actual ground owner before every step.
    /// The elapsed-wall budget is soft because a single synchronous mesh/collider
    /// operation cannot be interrupted. The step cap also works on coarse clocks.
    /// </summary>
    internal sealed class TownConstructionSliceBudget800
    {
        // Initial experiment only; native output-equivalence/timing acceptance pending.
        internal const double DefaultSliceSeconds = 0.0015;
        internal const int DefaultMaximumSteps = 32;
        private readonly Func<long> readTimestamp;
        private readonly long frequency;
        private readonly double maximumSeconds;
        private readonly int maximumSteps;
        private long sliceStarted;
        private int sliceSteps;

        internal long TotalSteps { get; private set; }
        internal long BudgetYieldCount { get; private set; }
        internal double MaximumObservedSliceSeconds { get; private set; }

        internal TownConstructionSliceBudget800()
            : this(Stopwatch.GetTimestamp, Stopwatch.Frequency,
                DefaultSliceSeconds, DefaultMaximumSteps) { }

        /// <summary>Clock injection is for deterministic scheduler tests, not world authority.</summary>
        internal TownConstructionSliceBudget800(Func<long> readTimestamp, long frequency,
            double maximumSeconds, int maximumSteps)
        {
            if (readTimestamp == null || frequency <= 0 || maximumSteps <= 0 ||
                double.IsNaN(maximumSeconds) || double.IsInfinity(maximumSeconds) || maximumSeconds <= 0)
                throw new ArgumentException("Positive finite Town slice limits and clock required.");
            this.readTimestamp = readTimestamp;
            this.frequency = frequency;
            this.maximumSeconds = maximumSeconds;
            this.maximumSteps = maximumSteps;
            ResumeAfterYield();
        }

        /// <summary>
        /// Called after a complete guarded logical step, including its ledger/capture
        /// work. Returns true when the coroutine must return control to Unity.
        /// No guard is omitted when several logical steps share the same frame.
        /// </summary>
        internal bool CompleteStep()
        {
            TotalSteps++;
            sliceSteps++;
            double elapsedSeconds = (readTimestamp() - sliceStarted) / (double)frequency;
            if (elapsedSeconds < 0)
                throw new InvalidOperationException("Town scheduler clock moved backwards.");
            MaximumObservedSliceSeconds = Math.Max(MaximumObservedSliceSeconds, elapsedSeconds);
            bool mustYield = sliceSteps >= maximumSteps || elapsedSeconds >= maximumSeconds;
            if (mustYield)
                BudgetYieldCount++;
            return mustYield;
        }

        /// <summary>
        /// Call after the coroutine resumes, never before yielding: waiting for Unity
        /// is excluded from the next active slice. Also call after a real waiting item.
        /// </summary>
        internal void ResumeAfterYield()
        {
            sliceStarted = readTimestamp();
            sliceSteps = 0;
        }

        /// <summary>
        /// Only existing null checkpoints and the typed BuildingSteps integer-zero
        /// marker are cheap. Every other value must pass through untouched, including
        /// fixed-step/timed/async/custom wait objects and nonzero integer markers.
        /// </summary>
        internal static bool IsCooperativeCheckpoint(object yielded)
        {
            return yielded == null || (yielded is int marker && marker == 0);
        }
    }
}
