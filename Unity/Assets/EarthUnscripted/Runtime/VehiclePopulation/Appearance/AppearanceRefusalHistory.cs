using System;

namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Bounded, main-thread diagnostic history owned by one actual appearance state.
    /// It records an observed helper failure, not currentness or a permission. A later
    /// generic scheduling/retirement refusal must not erase the original observation.
    /// No engine objects, owner grants, epoch transitions or admission decisions occur.
    /// </summary>
    internal sealed class AppearanceRefusalHistory
    {
        // Character limits bound diagnostic strings, not geometry/admission budgets.
        internal const int MaximumReasonCharacters = 512;
        private const int MaximumStageCharacters = 64;
        private string firstReason;
        private string firstStage;
        private int firstFrame;
        private string lastReason;
        private string lastStage;
        private int lastFrame;

        internal void Record(string stage, int observedFrame, string reason)
        {
            string boundedStage = Limit(stage, MaximumStageCharacters);
            string boundedReason = Limit(reason, MaximumReasonCharacters);
            // First failure belongs to the actual producer lifetime, not probe start.
            // No later transient/helper failure may overwrite this original cause.
            if (firstReason == null)
            {
                firstStage = boundedStage;
                firstReason = boundedReason;
                firstFrame = observedFrame;
            }
            lastStage = boundedStage;
            lastReason = boundedReason;
            lastFrame = observedFrame;
        }

        internal string Describe(string currentReason)
        {
            string current = Limit(currentReason, MaximumReasonCharacters);
            if (firstReason == null) return current;
            return current + Historical("historicalFirstHelperFailure", firstStage, firstFrame, firstReason)
                + Historical("historicalLastHelperFailure", lastStage, lastFrame, lastReason);
        }

        private static string Historical(string label, string stage, int frame, string reason)
        {
            return "; " + label + "[stage=" + stage + ", producerFrame="
                + frame.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]=" + reason;
        }

        private static string Limit(string value, int maximumCharacters)
        {
            if (value == null) return "<null diagnostic>";
            return value.Length <= maximumCharacters ? value
                : value.Substring(0, maximumCharacters) + "[truncated]";
        }
    }
}
