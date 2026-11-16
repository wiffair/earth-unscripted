using System;

namespace RoadRush.Core
{
    /// <summary>Continuous approximate air-cooled engine speed for the recorded
    /// audio bank. Speed is m/s; throttle and driven-wheel slip are 0–1.
    /// This is an audio mapping, not a measured transmission model. It must not
    /// wrap at a road speed: that abruptly crossfades loaded audio back to idle.</summary>
    public static class CamperAudioRules
    {
        public static float Rpm(float speedMetresPerSecond, float throttle, float wheelSlip)
        {
            float speed = Math.Max(0, speedMetresPerSecond);
            float load = DrivingMath.Clamp(throttle, 0, 1);
            float slip = DrivingMath.Clamp(wheelSlip, 0, 1);
            const float idleRpm = 850;
            const float roadRpmPerMetreSecond = 135;
            const float maximumRoadContribution = 3500;
            return DrivingMath.Clamp(idleRpm + Math.Min(speed * roadRpmPerMetreSecond, maximumRoadContribution)
                + load * 650 + slip * 600, idleRpm, 4800);
        }
    }
}
