using System;

namespace RoadRush.Core
{
    /// <summary>Game damage approximation based on normal closing speed, mass
    /// ratio and structure. Glancing tangential speed is excluded. This does not
    /// replace Unity's momentum/contact solver or make heavy cars indestructible.</summary>
    public static class VehicleImpactRules
    {
        public static float CollisionDamage(float closingMetresPerSecond, float ownMassKg, float otherMassKg, float structuralStrength, float vulnerableArea = 1)
        {
            double share = float.IsPositiveInfinity(otherMassKg) ? 1 : otherMassKg / Math.Max(1, ownMassKg + otherMassKg);
            double speed = Math.Max(0, closingMetresPerSecond - 1.5f);
            return (float)Math.Min(100, speed * speed * .14 * share * vulnerableArea / Math.Max(.5, structuralStrength));
        }
        public static float LandingDamage(float normalSpeed, float safeSpeed, float strength)
        {
            double excess = Math.Max(0, normalSpeed - safeSpeed);
            return (float)Math.Min(100, excess * excess * .7 / Math.Max(.5, strength));
        }
        public static float TreeBreakEnergy(float radiusMetres) => (float)(180000 * Math.Pow(Math.Max(.04, radiusMetres) / .18, 3));
        public static float TreeEffectiveMass(float radiusMetres) => (float)Math.Min(20000, 70 * Math.Pow(radiusMetres / .08, 2.4));
    }
}
