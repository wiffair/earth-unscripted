using UnityEngine;
namespace EarthUnscripted
{
    public static class CameraLookRules
    {
        // Integrate a rate; releasing the stick keeps the chosen elevation.
        public static float Pitch(float pitch,float stick,float seconds)
            =>Mathf.Clamp(pitch+(Mathf.Abs(stick)>.12f?stick:0)*45*Mathf.Max(0,seconds),-12,48);
    }
}
