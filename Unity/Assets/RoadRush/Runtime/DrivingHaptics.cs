using System;
using UnityEngine;

namespace RoadRush
{
    public enum HapticSurface { Asphalt, Concrete, Gravel, Grass, Mud, Sand, Pebbles, Water }
    public struct HapticMotors
    {
        public float Low, High;
        public HapticMotors(float low, float high) { Low = low; High = high; }
    }

    /// <summary>
    /// Bounded two-motor gameplay feedback. Strengths are dimensionless [0,1],
    /// speed is m/s, bump speed is measured suspension compression m/s. Surface
    /// texture follows distance travelled rather than steering input. Device
    /// output, focus and pause ownership remain with FeedbackDriver.
    /// </summary>
    public sealed class DrivingHaptics
    {
        double distanceMetres;
        float impact, menuTime, menuDuration;
        MenuPulse menuKind;
        public void Impact(float normalSpeed)
        { impact = Mathf.Max(impact, Mathf.Clamp01(normalSpeed / 12)); }
        public void Menu(MenuPulse kind)
        {
            menuKind = kind; menuTime = 0;
            menuDuration = kind == MenuPulse.Move ? .035f : kind == MenuPulse.Confirm ? .085f : kind == MenuPulse.Back ? .055f : .16f;
        }
        public void Reset() { impact = menuDuration = menuTime = 0; }

        public static HapticSurface Surface(string name, bool onRoad, float wetness, float waterDepth)
        {
            if (waterDepth > .025f) return HapticSurface.Water;
            bool Has(string text) => name != null && name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("pebble") || Has("cobble")) return HapticSurface.Pebbles;
            if (Has("sand") || Has("beach")) return HapticSurface.Sand;
            if (Has("gravel") || Has("ballast") || Has("shoulder")) return HapticSurface.Gravel;
            if (Has("asphalt") || Has("tarmac") || Has("Road bank")) return HapticSurface.Asphalt;
            if (Has("concrete") || Has("Footpath") || Has("paving") || Has("kerb")) return HapticSurface.Concrete;
            if (Has("mud")) return HapticSurface.Mud;
            return onRoad ? HapticSurface.Asphalt : wetness > .4f ? HapticSurface.Mud : HapticSurface.Grass;
        }

        public HapticMotors Tick(float seconds, bool driving, bool grounded, float speed,
            float throttle, float suspensionSpeed, float slip, HapticSurface surface, float strength, float engineStrength = 0)
        {
            seconds = Mathf.Clamp(seconds, 0, .1f);
            float low = 0, high = 0;
            if (driving)
            {
                distanceMetres += Mathf.Abs(speed) * seconds;
                float moving = Mathf.Clamp01((Mathf.Abs(speed) - .2f) / 9);
                float fast = Mathf.Clamp01(Mathf.Abs(speed) / 35);
                if (grounded)
                {
                    float carrier = .5f + .5f * Mathf.Sin((float)(distanceMetres % 1000) * 5.7f);
                    float grain = .5f + .5f * Mathf.Sin((float)(distanceMetres % 1000) * 13.1f + 1.3f);
                    switch (surface)
                    {
                        case HapticSurface.Asphalt: break; // Smooth tarmac has no continuous motor texture.
                        case HapticSurface.Concrete: low = carrier * .004f; high = grain * .006f; break;
                        case HapticSurface.Gravel: low = .10f + carrier * .12f; high = .16f + grain * .19f; break;
                        case HapticSurface.Grass: low = .11f + carrier * .11f; high = .035f + grain * .05f; break;
                        case HapticSurface.Mud: low = .19f + carrier * .13f; high = .04f + grain * .035f; break;
                        case HapticSurface.Sand: low = .10f + carrier * .045f; high = .025f; break;
                        case HapticSurface.Pebbles: low = .15f + carrier * .16f; high = .20f + grain * .20f; break;
                        case HapticSurface.Water: low = .15f + carrier * .06f; high = .09f + grain * .075f; break;
                    }
                    low *= moving; high *= moving;
                    // Ignore small spring chatter; only meaningful suspension movement is a bump.
                    float bump = Mathf.Clamp01((suspensionSpeed - .65f) / 3f);
                    low += bump * .42f; high += bump * .7f;
                    high += Mathf.Clamp01((slip - .8f) / 7) * .16f * moving;
                }
                // Restrained engine/load feedback; no continuous idle rumble.
                float engine = float.IsNaN(engineStrength) ? 0 : Mathf.Clamp01(engineStrength);
                low += Mathf.Clamp01(throttle) * (.025f + fast * .035f) * .25f * engine;
                low += impact; high += impact * .8f;
            }
            if (menuTime < menuDuration)
            {
                bool gap = menuKind == MenuPulse.Unavailable && menuTime > .055f && menuTime < .10f;
                if (!gap)
                {
                    low = Mathf.Max(low, menuKind == MenuPulse.Move ? .045f : menuKind == MenuPulse.Confirm ? .14f : .09f);
                    high = Mathf.Max(high, menuKind == MenuPulse.Move ? .10f : menuKind == MenuPulse.Confirm ? .22f : menuKind == MenuPulse.Back ? .07f : .18f);
                }
                menuTime += seconds;
            }
            impact = Mathf.MoveTowards(impact, 0, seconds * 4);
            strength = float.IsNaN(strength) ? 0 : Mathf.Clamp01(strength);
            return new HapticMotors(Mathf.Clamp01(low) * strength, Mathf.Clamp01(high) * strength);
        }
    }
}
