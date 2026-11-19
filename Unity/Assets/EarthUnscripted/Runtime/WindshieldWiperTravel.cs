using UnityEngine;
namespace EarthUnscripted
{
    // Mechanical travel belongs to each fitted blade, not a fixed model-wide
    // angle that shortens long, shallow windscreens to clear the pillars.
    public sealed class WindshieldWiperTravel : MonoBehaviour
    {
        public float ParkAngle,EndAngle;
    }
}
