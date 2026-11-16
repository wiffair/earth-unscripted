using UnityEngine;

namespace RoadRush
{
    public sealed partial class GeoRegion
    {
        static float NativeProfileHeight(GeoRoad road, float x, float z)
        {
            float best = float.PositiveInfinity, height = road.points[0].y;
            for (int i = 1; i < road.points.Length; i++)
            {
                var a = road.points[i - 1]; var delta = road.points[i] - a;
                float lengthSquared = delta.x * delta.x + delta.z * delta.z;
                if (lengthSquared < .000001f) continue;
                float t = Mathf.Clamp01(((x - a.x) * delta.x + (z - a.z) * delta.z) / lengthSquared);
                var point = a + delta * t;
                float squared = (x - point.x) * (x - point.x) + (z - point.z) * (z - point.z);
                if (squared >= best) continue;
                best = squared; height = point.y;
            }
            return height;
        }
    }
}
