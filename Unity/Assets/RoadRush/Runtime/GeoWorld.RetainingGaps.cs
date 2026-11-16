using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GeoWorld
    {
        // Per-world derived plan. Source terrain/asphalt are borrowed, never edited.
        LegacyRetainingGapPlan retainingGaps;

        IEnumerable<GeoRoad> RetainingGapNeighbors(Vector3 point)
            => roads.TryGetValue(Key(point), out var candidates) ? candidates : (IEnumerable<GeoRoad>)Array.Empty<GeoRoad>();

        void PrepareRetainingGaps()
        {
            retainingGaps = null;
            if (legacyBankPavement == null || preparedRoadSurfaces != null || Region.circuit || Region.roadWidthSchema != 0) return;
            try
            {
                retainingGaps = new LegacyRetainingGapPlan(Region, legacyBankPavement,
                    road => !Region.IsEffectiveBridgeDeck(road) && !road.tunnel && road.layer == 0 &&
                        RoadBankGeometryReach(road) > RoadBankReach(road), RetainingGapNeighbors);
            }
            catch (System.IO.InvalidDataException error) when (error.Message == "Local retaining-gap panel budget")
            {
                // Optional landscape treatment: retain existing roads/world when
                // its bounded panel budget cannot cover this source, never thin it.
                Debug.LogWarning("Retaining shoulders unavailable: " + error.Message);
            }
        }

        IEnumerable<int> EmitRetainingTerrain(Transform root, Vector2Int key,
            Vector3[] originalVertices, int[] originalTriangles, Vector2[] originalUV)
        {
            if (retainingGaps == null || retainingGaps.Count == 0)
            {
                Geometry.Mesh("Terrain", root, originalVertices, originalTriangles, originalUV, ground, true);
                yield return 0; yield break;
            }
            var vertices = new List<Vector3>(originalVertices);
            var triangles = new List<int>(originalTriangles.Length);
            var uv = new List<Vector2>(originalUV);
            for (int i = 0; i < originalTriangles.Length; i += 3)
            {
                var a = originalVertices[originalTriangles[i]];
                var b = originalVertices[originalTriangles[i + 1]];
                var c = originalVertices[originalTriangles[i + 2]];
                if (!retainingGaps.Intersects(a, b, c, key))
                {
                    triangles.Add(originalTriangles[i]); triangles.Add(originalTriangles[i + 1]); triangles.Add(originalTriangles[i + 2]);
                }
                else
                {
                    var data = retainingGaps.AdaptTerrain(a, b, c, key);
                    int offset = vertices.Count;
                    foreach (var point in data.Vertices)
                    {
                        vertices.Add(point);
                        uv.Add(new Vector2((point.x - Region.minX) / Region.width, (point.z - Region.minZ) / Region.depth));
                    }
                    foreach (int index in data.Triangles) triangles.Add(offset + index);
                }
                if (i % (3 * 256) == 0) yield return 0;
            }
            Geometry.Mesh("Terrain", root, vertices.ToArray(), triangles.ToArray(), uv.ToArray(), ground, true);
            yield return 0;
            foreach (var step in RetainingWallSteps(root, key)) yield return step;
        }

        IEnumerable<int> RetainingWallSteps(Transform root, Vector2Int key)
        {
            var wallVertices = new List<Vector3>(); var wallTriangles = new List<int>(); var wallUV = new List<Vector2>();
            var fenceVertices = new List<Vector3>(); var fenceTriangles = new List<int>(); var fenceUV = new List<Vector2>();
            foreach (var panel in retainingGaps.AtChunk(key))
            {
                // A <=2.5m panel crossing a chunk line has ONE owner, not two walls.
                if (Key((panel.Start.Wall + panel.End.Wall) * .5f) != key) continue;
                foreach (var interval in retainingGaps.WallIntervals(panel))
                {
                    if (interval.y - interval.x < .001f) continue;
                    Vector3 a = Vector3.Lerp(panel.Start.Wall, panel.End.Wall, interval.x);
                    Vector3 b = Vector3.Lerp(panel.Start.Wall, panel.End.Wall, interval.y);
                    float topA = Mathf.Lerp(panel.Start.UpperY, panel.End.UpperY, interval.x) + .02f;
                    float topB = Mathf.Lerp(panel.Start.UpperY, panel.End.UpperY, interval.y) + .02f;
                    float baseA = Mathf.Lerp(panel.Start.LowerY, panel.End.LowerY, interval.x) - .08f;
                    float baseB = Mathf.Lerp(panel.Start.LowerY, panel.End.LowerY, interval.y) - .08f;
                    Vector3 along = b - a; along.y = 0;
                    if (along.sqrMagnitude < .0001f) continue;
                    Vector3 right = Vector3.Cross(Vector3.up, along.normalized) * LegacyRetainingGapPlan.WallHalfThicknessMetres;
                    Vector3 at = new Vector3(a.x, topA, a.z), bt = new Vector3(b.x, topB, b.z);
                    Vector3 ab = new Vector3(a.x, baseA, a.z), bb = new Vector3(b.x, baseB, b.z);
                    RetainingQuad(wallVertices, wallTriangles, wallUV, at - right, bt - right, ab - right, bb - right);
                    RetainingQuad(wallVertices, wallTriangles, wallUV, bt + right, at + right, bb + right, ab + right);
                    RetainingQuad(wallVertices, wallTriangles, wallUV, at + right, bt + right, at - right, bt - right);
                    RetainingQuad(wallVertices, wallTriangles, wallUV, at + right, at - right, ab + right, ab - right);
                    RetainingQuad(wallVertices, wallTriangles, wallUV, bt - right, bt + right, bb - right, bb + right);
                    var rotation = Quaternion.LookRotation(along, Vector3.up);
                    float span = along.magnitude;
                    // Light residential fence: deliberately NOT a concrete vehicle
                    // restraint. Current project has no breakable-fence owner API.
                    // This initial visual fence has no collider; the wall is solid.
                    foreach (float level in new[] { .48f, 1.0f })
                    {
                        var midpoint = (at + bt) * .5f + Vector3.up * level;
                        AddBridgeBox(fenceVertices, fenceTriangles, fenceUV, midpoint,
                            new Vector3(.045f, .055f, span), Quaternion.LookRotation(bt - at, Vector3.up));
                    }
                    AddBridgeBox(fenceVertices, fenceTriangles, fenceUV, at + Vector3.up * .54f,
                        new Vector3(.065f, 1.08f, .065f), rotation);
                }
                if (wallVertices.Count > 8192)
                {
                    FlushRetainingMeshes(root, wallVertices, wallTriangles, wallUV, fenceVertices, fenceTriangles, fenceUV);
                    yield return 0;
                }
            }
            FlushRetainingMeshes(root, wallVertices, wallTriangles, wallUV, fenceVertices, fenceTriangles, fenceUV);
            yield return 0;
        }

        static void RetainingQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uv,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
            uv.AddRange(new[] { new Vector2(0, 1), new Vector2(Vector3.Distance(a, b), 1), Vector2.zero, new Vector2(Vector3.Distance(c, d), 0) });
            triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
        }

        static void FlushRetainingMeshes(Transform root, List<Vector3> wallVertices, List<int> wallTriangles, List<Vector2> wallUV,
            List<Vector3> fenceVertices, List<int> fenceTriangles, List<Vector2> fenceUV)
        {
            if (wallTriangles.Count != 0)
                Geometry.Mesh("Narrow-gap retaining wall", root, wallVertices.ToArray(), wallTriangles.ToArray(), wallUV.ToArray(),
                    Geometry.Surface("Retaining masonry", new Color(.36f, .35f, .31f), .2f, 1), true);
            if (fenceTriangles.Count != 0)
                Geometry.Mesh("Light retaining-edge fence", root, fenceVertices.ToArray(), fenceTriangles.ToArray(), fenceUV.ToArray(),
                    Geometry.Material("Residential fence", new Color(.20f, .19f, .16f)), false);
            wallVertices.Clear(); wallTriangles.Clear(); wallUV.Clear();
            fenceVertices.Clear(); fenceTriangles.Clear(); fenceUV.Clear();
        }
    }
}
