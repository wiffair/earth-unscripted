using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    /// <summary>
    /// Derived, local soil/wall geometry for a narrow gap between two parallel
    /// at-grade roads. The immutable source and asphalt remain untouched. Built
    /// once from the existing road/pavement indices; owned by one GeoWorld.
    /// All coordinates and lengths are local map metres. This is an estimated
    /// game retaining treatment, not a surveyed structure or a crash barrier.
    /// </summary>
    internal sealed class LegacyRetainingGapPlan
    {
        internal const float RowSpacingMetres = 2.5f;
        const float MaximumGapMetres = 3.5f;
        const float MinimumGapMetres = .45f;
        const float MinimumRiseMetres = .65f;
        const float MaximumRiseMetres = 2.1f;
        const float MinimumParallelCosine = .96f;
        const float MaximumAlongRoadCosine = .15f;
        const float VergeBelowAsphaltMetres = .06f;
        const float PavementInsetMetres = .03f;
        const float CorridorOverlapMetres = .04f;
        const float WallSetbackMetres = .10f;
        // Shared with emitted wall sides. A further 5mm opening margin protects
        // connector pavement without deleting the wall 10cm outside its own road.
        internal const float WallHalfThicknessMetres = .09f;
        const float OpeningMarginMetres = .005f;
        const float OpeningQueryPaddingMetres = .12f;
        const float ChunkMetres = 320f;
        const int MaximumPanels = 32768;

        readonly GeoRegion region;
        readonly RoadBankExteriorClip.SpatialIndex pavement;
        readonly Dictionary<Vector2Int, List<Panel>> cells = new Dictionary<Vector2Int, List<Panel>>();
        readonly List<Panel> panels = new List<Panel>();
        internal int Count => panels.Count;

        internal sealed class Row
        {
            internal GeoRoad Lower;
            internal Vector3 UpperEdge, LowerEdge, Wall;
            internal float UpperY, LowerY;
        }

        internal sealed class Panel
        {
            internal readonly Row Start, End;
            internal readonly Vector3[] Ring;
            internal readonly Vector3[][] Planes;
            internal readonly float West, East, South, North;
            internal Panel(Row start, Row end)
            {
                Start = start; End = end;
                // The whole narrow gap has a lower-verge surface. A wall carries
                // the discontinuity beside the upper road, instead of soil peaks.
                Ring = new[] { start.UpperEdge, end.UpperEdge, end.LowerEdge, start.LowerEdge };
                for (int i = 0; i < Ring.Length; i++)
                {
                    var point = Ring[i];
                    point.y = (i == 0 || i == 3 ? start.LowerY : end.LowerY) - VergeBelowAsphaltMetres;
                    Ring[i] = point;
                }
                Planes = new[] { new[] { Ring[0], Ring[1], Ring[3] }, new[] { Ring[1], Ring[2], Ring[3] } };
                West = East = Ring[0].x; South = North = Ring[0].z;
                foreach (var point in Ring)
                {
                    West = Mathf.Min(West, point.x); East = Mathf.Max(East, point.x);
                    South = Mathf.Min(South, point.z); North = Mathf.Max(North, point.z);
                }
            }
        }

        internal LegacyRetainingGapPlan(GeoRegion region,
            RoadBankExteriorClip.SpatialIndex pavement, Func<GeoRoad, bool> eligible,
            Func<Vector3, IEnumerable<GeoRoad>> nearby)
        {
            this.region = region; this.pavement = pavement;
            var ordered = new List<GeoRoad>(region.roads);
            ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.id, b.id));
            foreach (var upper in ordered)
            {
                if (!eligible(upper)) continue;
                Row previous = null;
                for (int segment = 1; segment < upper.points.Length; segment++)
                {
                    Vector3 start = upper.points[segment - 1], end = upper.points[segment];
                    Vector3 direction = end - start; direction.y = 0;
                    float length = direction.magnitude;
                    if (length < .01f) { previous = null; continue; }
                    direction /= length;
                    int steps = Mathf.CeilToInt(length / RowSpacingMetres);
                    for (int step = 0; step <= steps; step++)
                    {
                        // Reuse a preceding shared source knot rather than emit
                        // overlapping zero-length panels at a segment boundary.
                        if (segment > 1 && step == 0) continue;
                        Vector3 centre = Vector3.Lerp(start, end, step / (float)steps);
                        Row current = FindRow(upper, centre, direction, nearby(centre), eligible);
                        if (previous != null && current != null && Connected(previous.Lower, current.Lower) &&
                            Vector3.Distance(previous.UpperEdge, current.UpperEdge) <= RowSpacingMetres * 2 &&
                            Vector3.Distance(previous.LowerEdge, current.LowerEdge) <= RowSpacingMetres * 3)
                        {
                            if (panels.Count == MaximumPanels)
                                throw new System.IO.InvalidDataException("Local retaining-gap panel budget");
                            Add(new Panel(previous, current));
                        }
                        previous = current;
                    }
                }
            }
        }

        static bool Connected(GeoRoad a, GeoRoad b)
            => ReferenceEquals(a, b) || a.start == b.start || a.start == b.end || a.end == b.start || a.end == b.end;

        Row FindRow(GeoRoad upper, Vector3 centre, Vector3 upperDirection,
            IEnumerable<GeoRoad> candidates, Func<GeoRoad, bool> eligible)
        {
            Row result = null;
            float bestDistance = float.PositiveInfinity;
            foreach (var lower in candidates)
            {
                if (ReferenceEquals(upper, lower) || !eligible(lower)) continue;
                for (int i = 1; i < lower.points.Length; i++)
                {
                    Vector3 a = lower.points[i - 1], delta = lower.points[i] - a; delta.y = 0;
                    float lengthSquared = delta.sqrMagnitude;
                    if (lengthSquared < .0001f) continue;
                    Vector3 lowerDirection = delta / Mathf.Sqrt(lengthSquared);
                    if (Mathf.Abs(Vector3.Dot(upperDirection, lowerDirection)) < MinimumParallelCosine) continue;
                    float station = Mathf.Clamp01(Vector3.Dot(centre - a, delta) / lengthSquared);
                    Vector3 projection = a + delta * station;
                    Vector3 across = projection - centre; across.y = 0;
                    float distance = across.magnitude;
                    if (distance < .01f || distance >= bestDistance) continue;
                    across /= distance;
                    if (Mathf.Abs(Vector3.Dot(across, upperDirection)) > MaximumAlongRoadCosine) continue;
                    float gap = distance - (upper.width + lower.width) * .5f;
                    if (gap < MinimumGapMetres || gap > MaximumGapMetres) continue;
                    Vector3 upperEdge = centre + across * (upper.width * .5f);
                    Vector3 lowerEdge = projection - across * (lower.width * .5f);
                    Vector3 upperSample = upperEdge - across * PavementInsetMetres;
                    Vector3 lowerSample = lowerEdge + across * PavementInsetMetres;
                    float upperY = region.RoadSurfaceHeightForRoad(upper, upperSample.x, upperSample.z);
                    float lowerY = region.RoadSurfaceHeightForRoad(lower, lowerSample.x, lowerSample.z);
                    float rise = upperY - lowerY;
                    if (rise < MinimumRiseMetres || rise > MaximumRiseMetres) continue;
                    // A soil shoulder shallower than this already has room: do
                    // not introduce a wall solely because two roads are nearby.
                    if (rise / gap < .35f) continue;
                    upperEdge -= across * CorridorOverlapMetres;
                    lowerEdge += across * CorridorOverlapMetres;
                    result = new Row
                    {
                        Lower = lower, UpperEdge = upperEdge, LowerEdge = lowerEdge,
                        Wall = centre + across * (upper.width * .5f + WallSetbackMetres),
                        UpperY = upperY, LowerY = lowerY
                    };
                    bestDistance = distance;
                }
            }
            return result;
        }

        void Add(Panel panel)
        {
            panels.Add(panel);
            int west = Mathf.FloorToInt(panel.West / ChunkMetres), east = Mathf.FloorToInt(panel.East / ChunkMetres);
            int south = Mathf.FloorToInt(panel.South / ChunkMetres), north = Mathf.FloorToInt(panel.North / ChunkMetres);
            for (int z = south; z <= north; z++) for (int x = west; x <= east; x++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<Panel>());
                list.Add(panel);
            }
        }

        internal IEnumerable<Panel> AtChunk(Vector2Int key)
            => cells.TryGetValue(key, out var found) ? found : (IEnumerable<Panel>)Array.Empty<Panel>();

        static bool Overlaps(Panel panel, Vector3 a, Vector3 b, Vector3 c)
            => Mathf.Max(a.x, b.x, c.x) >= panel.West && Mathf.Min(a.x, b.x, c.x) <= panel.East &&
               Mathf.Max(a.z, b.z, c.z) >= panel.South && Mathf.Min(a.z, b.z, c.z) <= panel.North;

        static double Side(Vector3 a, Vector3 b, Vector3 point)
            => ((double)b.x - a.x) * ((double)point.z - a.z) - ((double)b.z - a.z) * ((double)point.x - a.x);

        static double Orientation(Vector3[] ring)
        {
            double area = 0;
            for (int i = 0; i < ring.Length; i++)
            { var a = ring[i]; var b = ring[(i + 1) % ring.Length]; area += (double)a.x * b.z - (double)b.x * a.z; }
            return area < 0 ? -1 : 1;
        }

        static List<Vector3> Half(List<Vector3> input, Vector3 a, Vector3 b, double orientation, bool inside)
        {
            var output = new List<Vector3>();
            if (input.Count == 0) return output;
            Vector3 previous = input[input.Count - 1];
            double before = Side(a, b, previous) * orientation;
            foreach (var next in input)
            {
                double after = Side(a, b, next) * orientation;
                bool wasInside = inside ? before >= 0 : before <= 0;
                bool isInside = inside ? after >= 0 : after <= 0;
                if (wasInside != isInside)
                {
                    double fraction = before / (before - after);
                    output.Add(new Vector3((float)(previous.x + fraction * ((double)next.x - previous.x)),
                        (float)(previous.y + fraction * ((double)next.y - previous.y)),
                        (float)(previous.z + fraction * ((double)next.z - previous.z))));
                }
                if (isInside) output.Add(next);
                previous = next; before = after;
            }
            return output;
        }

        // Disjoint convex subtraction: each outside fragment is removed exactly
        // once, and the final inside fragment retains source-triangle Y values.
        static List<Vector3> Split(List<Vector3> source, Vector3[] ring, List<List<Vector3>> outside)
        {
            var remainder = source; double orientation = Orientation(ring);
            for (int edge = 0; edge < ring.Length && remainder.Count >= 3; edge++)
            {
                var a = ring[edge]; var b = ring[(edge + 1) % ring.Length];
                var piece = Half(remainder, a, b, orientation, false);
                if (piece.Count >= 3) outside.Add(piece);
                remainder = Half(remainder, a, b, orientation, true);
            }
            return remainder;
        }

        static Vector3[] Ring(RoadBankExteriorClip.Footprint footprint)
        {
            var ring = new Vector3[footprint.Polygon.Length];
            for (int i = 0; i < ring.Length; i++) ring[i] = new Vector3(footprint.Polygon[i].x, 0, footprint.Polygon[i].y);
            return ring;
        }

        static float Plane(Vector3[] triangle, float x, float z)
        {
            Vector3 a = triangle[0], b = triangle[1], c = triangle[2];
            double denominator = Side(a, b, c);
            double u = (((double)x - a.x) * (c.z - a.z) - ((double)z - a.z) * (c.x - a.x)) / denominator;
            double v = (((double)b.x - a.x) * (z - a.z) - ((double)b.z - a.z) * (x - a.x)) / denominator;
            return (float)(a.y + u * (b.y - a.y) + v * (c.y - a.y));
        }

        static bool Inside(Vector3[] ring, Vector3 point)
        {
            double orientation = Orientation(ring);
            for (int i = 0; i < ring.Length; i++)
                if (Side(ring[i], ring[(i + 1) % ring.Length], point) * orientation < 0) return false;
            return true;
        }

        bool PavementContains(float x, float z)
        {
            var point = new Vector3(x, 0, z);
            foreach (var footprint in pavement.At(new[] { point, point, point }, 0, false))
                if (Inside(Ring(footprint), point)) return true;
            return false;
        }

        internal bool TryHeight(float x, float z, out float height)
        {
            height = 0;
            if (!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(x / ChunkMetres), Mathf.FloorToInt(z / ChunkMetres)), out var candidates)) return false;
            var point = new Vector3(x, 0, z);
            bool pavementChecked = false;
            foreach (var panel in candidates)
            {
                if (x < panel.West || x > panel.East || z < panel.South || z > panel.North) continue;
                foreach (var triangle in panel.Planes)
                {
                    if (!Inside(triangle, point)) continue;
                    if (!pavementChecked) { if (PavementContains(x, z)) return false; pavementChecked = true; }
                    height = Plane(triangle, x, z); return true;
                }
            }
            return false;
        }

        internal bool Intersects(Vector3 a, Vector3 b, Vector3 c, Vector2Int key)
        {
            foreach (var panel in AtChunk(key)) if (Overlaps(panel, a, b, c)) return true;
            return false;
        }

        /// <summary>Split actual terrain triangles; no coarse-grid widening of the treatment.
        /// Unaffected and pavement-covered fragments retain their original plane exactly.</summary>
        internal RoadBankExteriorClip.Data AdaptTerrain(Vector3 a, Vector3 b, Vector3 c, Vector2Int key)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            var remaining = new List<List<Vector3>> { new List<Vector3> { a, b, c } };
            foreach (var panel in AtChunk(key))
            {
                if (!Overlaps(panel, a, b, c)) continue;
                foreach (var plane in panel.Planes)
                {
                    var next = new List<List<Vector3>>();
                    foreach (var polygon in remaining)
                    {
                        var inside = Split(polygon, plane, next);
                        if (inside.Count < 3) continue;
                        var soil = new List<List<Vector3>> { inside };
                        foreach (var footprint in pavement.At(new[] { a, b, c }, 0, false))
                        {
                            var uncovered = new List<List<Vector3>>();
                            foreach (var piece in soil)
                            {
                                var covered = Split(piece, Ring(footprint), uncovered);
                                Append(covered, vertices, triangles, uv, null);
                            }
                            soil = uncovered;
                        }
                        foreach (var piece in soil) Append(piece, vertices, triangles, uv, plane);
                    }
                    remaining = next;
                }
            }
            foreach (var piece in remaining) Append(piece, vertices, triangles, uv, null);
            return new RoadBankExteriorClip.Data { Vertices = vertices.ToArray(), Triangles = triangles.ToArray(), UV = uv.ToArray() };
        }

        internal RoadBankExteriorClip.Data RemoveGapSoil(Vector3 a, Vector3 b, Vector3 c, Vector2Int key)
        {
            var remaining = new List<List<Vector3>> { new List<Vector3> { a, b, c } };
            foreach (var panel in AtChunk(key))
            {
                if (!Overlaps(panel, a, b, c)) continue;
                var next = new List<List<Vector3>>();
                foreach (var polygon in remaining) Split(polygon, panel.Ring, next);
                remaining = next;
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            foreach (var piece in remaining) Append(piece, vertices, triangles, uv, null);
            return new RoadBankExteriorClip.Data { Vertices = vertices.ToArray(), Triangles = triangles.ToArray(), UV = uv.ToArray() };
        }

        static void Append(List<Vector3> polygon, List<Vector3> vertices, List<int> triangles,
            List<Vector2> uv, Vector3[] plane)
        {
            if (polygon.Count < 3) return;
            int start = vertices.Count;
            foreach (var original in polygon)
            {
                Vector3 point = original;
                if (plane != null) point.y = Plane(plane, point.x, point.z);
                vertices.Add(point); uv.Add(new Vector2(point.x / 8, point.z / 8));
            }
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                if (Side(polygon[0], polygon[i], polygon[i + 1]) == 0) continue;
                triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1);
            }
        }

        /// <summary>Keep boundary-aligned exposed bank planes, split where they
        /// meet actual terrain planes. This fills terrain poke-through without
        /// feeding a nearest-road height into a separate regular soil grid.</summary>
        internal static RoadBankExteriorClip.Data ConformExposedBank(Vector3 a, Vector3 b, Vector3 c,
            Func<float, float, float> terrainHeight, float spacing)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            int west = Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) / spacing);
            int east = Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) / spacing);
            int south = Mathf.FloorToInt(Mathf.Min(a.z, b.z, c.z) / spacing);
            int north = Mathf.FloorToInt(Mathf.Max(a.z, b.z, c.z) / spacing);
            for (int z = south; z <= north; z++) for (int x = west; x <= east; x++)
            {
                Vector3 p = new Vector3(x * spacing, 0, z * spacing),
                    q = p + Vector3.right * spacing, r = p + Vector3.forward * spacing,
                    t = q + Vector3.forward * spacing;
                foreach (var terrain in new[] { new[] { p, r, q }, new[] { q, r, t } })
                {
                    for (int i = 0; i < terrain.Length; i++)
                    { var point = terrain[i]; point.y = terrainHeight(point.x, point.z) + .012f; terrain[i] = point; }
                    var unused = new List<List<Vector3>>();
                    var polygon = Split(new List<Vector3> { a, b, c }, terrain, unused);
                    if (polygon.Count < 3) continue;
                    bool hasAbove = false, hasBelow = false;
                    foreach (var point in polygon)
                    {
                        float difference = point.y - Plane(terrain, point.x, point.z);
                        hasAbove |= difference > 0; hasBelow |= difference < 0;
                    }
                    // Fully coplanar polygons belong to ONE side. Sending them
                    // to both closed half-planes duplicates visible/collider faces.
                    if (!hasBelow) Append(polygon, vertices, triangles, uv, null);
                    else if (!hasAbove) Append(polygon, vertices, triangles, uv, terrain);
                    else
                    {
                        Append(HeightHalf(polygon, terrain, true), vertices, triangles, uv, null);
                        Append(HeightHalf(polygon, terrain, false), vertices, triangles, uv, terrain);
                    }
                }
            }
            return new RoadBankExteriorClip.Data { Vertices = vertices.ToArray(), Triangles = triangles.ToArray(), UV = uv.ToArray() };
        }

        static List<Vector3> HeightHalf(List<Vector3> input, Vector3[] plane, bool above)
        {
            var output = new List<Vector3>();
            if (input.Count == 0) return output;
            var previous = input[input.Count - 1];
            double before = previous.y - Plane(plane, previous.x, previous.z);
            foreach (var next in input)
            {
                double after = next.y - Plane(plane, next.x, next.z);
                bool beforeInside = above ? before >= 0 : before <= 0;
                bool afterInside = above ? after >= 0 : after <= 0;
                if (beforeInside != afterInside)
                {
                    double fraction = before / (before - after);
                    output.Add(new Vector3((float)(previous.x + fraction * ((double)next.x - previous.x)),
                        (float)(previous.y + fraction * ((double)next.y - previous.y)),
                        (float)(previous.z + fraction * ((double)next.z - previous.z))));
                }
                if (afterInside) output.Add(next);
                previous = next; before = after;
            }
            return output;
        }

        // Clip the ENTIRE solid wall width at pavement openings. Each convex
        // pavement half-plane expands by the wall's projected perpendicular half-
        // width plus 5mm. Unlike a fixed end trim, this protects oblique openings;
        // unlike a 12cm isotropic offset, it preserves our own 10cm road setback.
        // Wall end caps use these exact endpoints (no extra span extension).
        internal List<Vector2> WallIntervals(Panel panel)
        {
            Vector3 a = panel.Start.Wall, b = panel.End.Wall;
            var intervals = new List<Vector2> { new Vector2(0, 1) };
            Vector3 direction = b - a; direction.y = 0;
            Vector3 right = Vector3.Cross(Vector3.up, direction.normalized);
            Vector3 padding = new Vector3(OpeningQueryPaddingMetres, 0, OpeningQueryPaddingMetres);
            foreach (var footprint in pavement.At(new[] { a - padding, a + padding, b - padding, b + padding }, 0, false))
            {
                var ring = Ring(footprint); double orientation = Orientation(ring), low = 0, high = 1;
                for (int edge = 0; edge < ring.Length && low <= high; edge++)
                {
                    Vector3 edgeVector = ring[(edge + 1) % ring.Length] - ring[edge];
                    double edgeLength = Math.Sqrt((double)edgeVector.x * edgeVector.x + (double)edgeVector.z * edgeVector.z);
                    if (edgeLength == 0) continue;
                    double normalProjection = Math.Abs((-edgeVector.z * right.x + edgeVector.x * right.z) / edgeLength);
                    double expansion = (WallHalfThicknessMetres * normalProjection + OpeningMarginMetres) * edgeLength;
                    double before = Side(ring[edge], ring[(edge + 1) % ring.Length], a) * orientation + expansion;
                    double after = Side(ring[edge], ring[(edge + 1) % ring.Length], b) * orientation + expansion;
                    if (before < 0 && after < 0) { low = 1; high = 0; break; }
                    if (before < 0) low = Math.Max(low, before / (before - after));
                    if (after < 0) high = Math.Min(high, before / (before - after));
                }
                if (low > high) continue;
                var outside = new List<Vector2>();
                foreach (var interval in intervals)
                {
                    if (low > interval.x) outside.Add(new Vector2(interval.x, Mathf.Min(interval.y, (float)low)));
                    if (high < interval.y) outside.Add(new Vector2(Mathf.Max(interval.x, (float)high), interval.y));
                }
                intervals = outside;
            }
            return intervals;
        }
    }
}
