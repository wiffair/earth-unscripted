using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        const float GroundPathLiftMetres = .04f;
        const float KerbLiftMetres = .10f;
        readonly Dictionary<Vector2Int, float> pathGroundSamples = new Dictionary<Vector2Int, float>();

        /// <summary>Ordinary paths follow the actual terrain triangles, including
        /// road earthworks. Prepared centreline heights alone cannot describe the
        /// ground after an adjacent road has cut into it. Bridge/tunnel/layered
        /// paths keep their independent profiles and structural support.</summary>
        // Ground paths and elevated paths have separate collision batches. Height
        // alone cannot identify their level where a bridge crosses another path.
        static bool IsGroundPath(Feature feature) => !feature.bridge && !feature.tunnel && feature.layer == 0 && (string.IsNullOrEmpty(feature.level) || feature.level == "0");

        float PathTerrainCorner(int x, int z)
        {
            var key = new Vector2Int(x, z);
            if (!pathGroundSamples.TryGetValue(key, out float height))
            {
                height = Ground(x * TerrainStep, z * TerrainStep);
                pathGroundSamples.Add(key, height);
            }
            return height;
        }

        float PathSurfaceLift(Feature owner, Vector3 point)
        {
            var nearby = Nearby(point.x, point.z);
            if (nearby != null)
                foreach (var segment in nearby)
                    if (segment.feature.category == "road" && PathSharesRoadLevel(owner, segment.feature) &&
                        Project(segment, point.x, point.z, out _) < segment.feature.width * .5f + 2.9f)
                        return .20f + KerbLiftMetres;
            return GroundPathLiftMetres;
        }

        static List<Vector3> ClipFootpathPolygon(List<Vector3> input, Vector3 first, Vector3 last)
        {
            var output = new List<Vector3>(8);
            if (input.Count == 0) return output;
            // Terrain triangles wind clockwise in XZ: retain their right-hand half planes.
            var previous = input[input.Count - 1];
            float previousSide = CrossXZ(first, last, previous);
            foreach (var point in input)
            {
                float side = CrossXZ(first, last, point);
                if ((side <= .00001f) != (previousSide <= .00001f))
                    output.Add(Vector3.Lerp(previous, point, previousSide / (previousSide - side)));
                if (side <= .00001f) output.Add(point);
                previous = point; previousSide = side;
            }
            return output;
        }

        void GroundPathTriangle(Batch batch, Feature owner, Vector3[] quad, Vector3 a, Vector3 b, Vector3 c)
        {
            var polygon = ClipFootpathPolygon(new List<Vector3>(quad), a, b);
            polygon = ClipFootpathPolygon(polygon, b, c);
            polygon = ClipFootpathPolygon(polygon, c, a);
            if (polygon.Count < 3) return;
            float denominator = CrossXZ(a, b, c);
            if (Mathf.Abs(denominator) < .00001f) return;
            var bottoms = new Vector3[polygon.Count];
            for (int index = 0; index < polygon.Count; index++)
            {
                var point = polygon[index];
                float wa = CrossXZ(b, c, point) / denominator;
                float wb = CrossXZ(c, a, point) / denominator;
                point.y = a.y * wa + b.y * wb + c.y * (1 - wa - wb);
                bottoms[index] = point;
                point.y += PathSurfaceLift(owner, point);
                polygon[index] = point;
            }
            for (int index = 1; index < polygon.Count - 1; index++)
                batch.Triangle(polygon[0], polygon[index], polygon[index + 1]);
            // Solid pavement edges reach their supporting terrain. No unsupported
            // ribbons, even on steep slopes or across terrain-cell diagonals.
            for (int index = 0; index < polygon.Count; index++)
            {
                int next = (index + 1) % polygon.Count;
                batch.Quad(bottoms[index], bottoms[next], polygon[next], polygon[index]);
            }
        }

        void GroundedPathQuad(Batch batch, Feature owner, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var quad = new[] { a, b, c, d };
            float minimumX = Mathf.Min(a.x, b.x, c.x, d.x), maximumX = Mathf.Max(a.x, b.x, c.x, d.x);
            float minimumZ = Mathf.Min(a.z, b.z, c.z, d.z), maximumZ = Mathf.Max(a.z, b.z, c.z, d.z);
            for (int x = Mathf.FloorToInt(minimumX / TerrainStep); x <= Mathf.FloorToInt(maximumX / TerrainStep); x++)
                for (int z = Mathf.FloorToInt(minimumZ / TerrainStep); z <= Mathf.FloorToInt(maximumZ / TerrainStep); z++)
                {
                    float floor = PortalCellFloor(x * TerrainStep, z * TerrainStep);
                    var p00 = new Vector3(x * TerrainStep, Mathf.Min(PathTerrainCorner(x, z), floor), z * TerrainStep);
                    var p01 = new Vector3(x * TerrainStep, Mathf.Min(PathTerrainCorner(x, z + 1), floor), (z + 1) * TerrainStep);
                    var p11 = new Vector3((x + 1) * TerrainStep, Mathf.Min(PathTerrainCorner(x + 1, z + 1), floor), (z + 1) * TerrainStep);
                    var p10 = new Vector3((x + 1) * TerrainStep, Mathf.Min(PathTerrainCorner(x + 1, z), floor), z * TerrainStep);
                    GroundPathTriangle(batch, owner, quad, p00, p01, p11);
                    GroundPathTriangle(batch, owner, quad, p00, p11, p10);
                }
        }
    }
}
