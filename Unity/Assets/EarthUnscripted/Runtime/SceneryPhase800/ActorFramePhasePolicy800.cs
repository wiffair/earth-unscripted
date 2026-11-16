using System;
using EarthUnscripted.VehiclePopulation;

namespace EarthUnscripted.Scenery800
{
    /// <summary>
    /// Pure phase/stamp precondition, not a population or source capability. The
    /// pump additionally checks actual snapshot currency and owner association.
    /// Fixed-time values are Unity seconds copied at the actual sampling boundary.
    /// </summary>
    public static class ActorFramePhasePolicy800
    {
        public const int ProducerLateUpdateOrder = 30000;
        public const int ConsumerLateUpdateOrder = 31000;

        public static bool MatchesAfterActorFrame(int sampledFrame, float sampledFixedSeconds,
            VehicleSamplePhase actualSamplePhase, int currentFrame, float currentFixedSeconds)
        {
            return sampledFrame >= 0 && sampledFrame == currentFrame
                && !float.IsNaN(sampledFixedSeconds) && !float.IsInfinity(sampledFixedSeconds)
                && !float.IsNaN(currentFixedSeconds) && !float.IsInfinity(currentFixedSeconds)
                && sampledFixedSeconds >= 0 && currentFixedSeconds >= 0
                && sampledFixedSeconds == currentFixedSeconds
                && actualSamplePhase == VehicleSamplePhase.AfterActorFrame;
        }
    }
}
