namespace EarthUnscripted.VehiclePopulation
{
    /// <summary>
    /// Private work scheduler, not membership/clean authority. Inputs are actual-owner
    /// derived dirty flags in stable roster order. Selects at most one cold attempt per
    /// rendered frame, including refused attempts. A rotating start prevents a permanently
    /// refused first actor from starving later actors. No native access/allocation here.
    /// Changing roster order may delay work; no bounded latency beyond a stable <=512
    /// actor roster and advancing actual player frames is claimed.
    /// </summary>
    internal sealed class AppearanceColdScheduler
    {
        private const int MaximumRoots = 512;
        private int previousFrame = -1;
        private int cursor;

        internal int Select(int actualFrame, bool[] ownerDerivedDirty)
        {
            if (actualFrame < 0 || actualFrame <= previousFrame || ownerDerivedDirty == null
                || ownerDerivedDirty.Length > MaximumRoots) return -1;
            previousFrame = actualFrame; // Failed or empty selections retain the frame spend.
            int count = ownerDerivedDirty.Length;
            if (count == 0) return -1;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (cursor + offset) % count;
                if (!ownerDerivedDirty[index]) continue;
                cursor = (index + 1) % count;
                return index;
            }
            return -1;
        }
    }
}
