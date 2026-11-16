using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GeoRegion
    {
        // User-approved gameplay estimates, NOT statutory/surveyed clearances.
        // 1.6m reserves the existing .75m slab/.65m beam plus pitch/row margin.
        const float EstimatedRoadFreeMetres = 5.2f;
        const float EstimatedTrainFreeMetres = 5.5f;
        const float EstimatedStructureReserveMetres = 1.6f;
        const float EstimatedFeatherMetresPerLiftMetre = 24;
        const int EstimatedApproachMaximumRoads = 128;
        const int EstimatedClearancePasses = 8;
        readonly HashSet<GeoRoad> estimatedOpenDecks = new HashSet<GeoRoad>();
        readonly Dictionary<GeoRoad, EstimatedDeck> estimatedDecks = new Dictionary<GeoRoad, EstimatedDeck>();
        internal string EstimatedClearanceStatus { get; private set; } = "Original estimated source profiles";
        internal bool IsEffectiveBridgeDeck(GeoRoad road) => road != null && (road.bridge || estimatedOpenDecks.Contains(road));
        internal int EffectiveBridgeColumns(GeoRoad road) => estimatedDecks.TryGetValue(road, out var profile) ? profile.Columns : 1;

        sealed class EstimatedComponent
        {
            internal readonly List<GeoRoad> Roads = new List<GeoRoad>();
            internal readonly Dictionary<GeoRoad, EstimatedDeck> Base = new Dictionary<GeoRoad, EstimatedDeck>();
            internal float Lift;
        }
        sealed class EstimatedWeight
        {
            internal EstimatedComponent Component;
            internal float[] Values;
        }
        sealed class EstimatedDeck
        {
            internal GeoRoad Road;
            internal Vector3[] Points;
            internal float[] Stations;
            internal float[,] Base;
            internal int Columns;
            internal readonly List<EstimatedWeight> Weights = new List<EstimatedWeight>();
            internal float RowHeight(int row, int column)
            {
                float total = 0, height = 0;
                foreach (var weight in Weights) { total += weight.Values[row]; height += weight.Values[row] * weight.Component.Lift; }
                return Base[row, column] + height / Mathf.Max(1, total);
            }
            internal float Query(float x, float z, bool baseOnly = false)
            {
                float best = float.PositiveInfinity, answer = Points[0].y;
                for (int row = 1; row < Points.Length; row++)
                {
                    var a = Points[row - 1]; var b = Points[row];
                    float dx = b.x - a.x, dz = b.z - a.z, squared = dx * dx + dz * dz;
                    if (squared < .000001f) continue;
                    float t = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / squared);
                    float ex = x - a.x - t * dx, ez = z - a.z - t * dz;
                    float distance = ex * ex + ez * ez;
                    if (distance >= best) continue;
                    best = distance;
                    float across = Mathf.Clamp01((ex * dz - ez * dx) / Mathf.Sqrt(squared) / Road.width + .5f) * Columns;
                    int column = Mathf.Min(Columns - 1, Mathf.FloorToInt(across));
                    float u = across - column;
                    float a0 = baseOnly ? Base[row - 1, column] : RowHeight(row - 1, column);
                    float b0 = baseOnly ? Base[row - 1, column + 1] : RowHeight(row - 1, column + 1);
                    float a1 = baseOnly ? Base[row, column] : RowHeight(row, column);
                    float b1 = baseOnly ? Base[row, column + 1] : RowHeight(row, column + 1);
                    // Same diagonal as the existing ribbon quad: a,c,b / b,c,d.
                    answer = u + t <= 1 ? a0 * (1 - u - t) + b0 * u + a1 * t
                        : b0 * (1 - t) + a1 * (1 - u) + b1 * (u + t - 1);
                }
                return answer;
            }
            internal Vector3[] CopyPoints()
            {
                var copy = (Vector3[])Points.Clone();
                for (int row = 0; row < copy.Length; row++)
                {
                    float centre = Columns * .5f; int left = Mathf.FloorToInt(centre);
                    copy[row].y = left == Columns ? RowHeight(row, left) : Mathf.Lerp(RowHeight(row, left), RowHeight(row, left + 1), centre - left);
                }
                return copy;
            }
        }
        sealed class EstimatedConstraint
        {
            internal EstimatedComponent Upper;
            internal GeoRoad UpperRoad, LowerRoad;
            internal GeoRailway LowerRail;
            internal Vector2[] Samples;
            internal float Gap;
        }
        sealed class EstimatedArm
        {
            internal EstimatedComponent Component;
            internal readonly List<Tuple<GeoRoad, bool, float, float>> Roads = new List<Tuple<GeoRoad, bool, float, float>>();
            internal float Length;
        }

        void ResetEstimatedClearanceProfiles()
        {
            estimatedOpenDecks.Clear(); estimatedDecks.Clear(); EstimatedClearanceStatus = "Original estimated source profiles";
        }
        bool TryEstimatedDeckHeight(GeoRoad road, float x, float z, out float height)
        {
            if (road != null && estimatedDecks.TryGetValue(road, out var profile)) { height = profile.Query(x, z); return true; }
            height = 0; return false;
        }
        EstimatedDeck CaptureEstimatedDeck(GeoRoad road, Vector3[] source, bool bridge)
        {
            var points = new List<Vector3>(); var stations = new List<float>(); float travelled = 0;
            for (int index = 1; index < source.Length; index++)
            {
                var a = source[index - 1]; var b = source[index];
                float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2.5f));
                for (int row = index == 1 ? 0 : 1; row <= steps; row++)
                { points.Add(Vector3.Lerp(a, b, row / (float)steps)); stations.Add(travelled + length * row / steps); }
                travelled += length;
            }
            var profile = new EstimatedDeck { Road = road, Points = points.ToArray(), Stations = stations.ToArray(), Columns = 2 * Mathf.Max(1, Mathf.CeilToInt(road.width / 4)) };
            profile.Base = new float[profile.Points.Length, profile.Columns + 1];
            for (int row = 0; row < profile.Points.Length; row++)
            {
                var point = profile.Points[row]; var tangent = profile.Points[Mathf.Min(row + 1, profile.Points.Length - 1)] - profile.Points[Mathf.Max(row - 1, 0)];
                var right = Vector3.Cross(Vector3.up, tangent).normalized;
                for (int column = 0; column <= profile.Columns; column++)
                {
                    var p = point + right * road.width * (column / (float)profile.Columns - .5f);
                    profile.Base[row, column] = bridge ? EffectiveBridgeHeight(road, p.x, p.z) : RoadSurfaceHeightForRoadBase(road, p.x, p.z);
                }
            }
            return profile;
        }
        static bool EstimatedIntersection(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector2 point)
        {
            double ux = (double)b.x - a.x, uz = (double)b.z - a.z, vx = (double)d.x - c.x, vz = (double)d.z - c.z;
            double determinant = ux * vz - uz * vx; point = default;
            if (determinant == 0) return false;
            double dx = (double)c.x - a.x, dz = (double)c.z - a.z;
            double t = (dx * vz - dz * vx) / determinant, u = (dx * uz - dz * ux) / determinant;
            if (t <= 0 || t >= 1 || u <= 0 || u >= 1) return false;
            point = new Vector2((float)(a.x + t * ux), (float)(a.z + t * uz)); return true;
        }
        static bool EstimatedSharedEndpoint(GeoRoad first, GeoRoad second) => first.start == second.start || first.start == second.end || first.end == second.start || first.end == second.end;
        static Vector2[] EstimatedCrossingSamples(Vector2 centre, Vector3 upperA, Vector3 upperB, float upperWidth, Vector3 lowerA, Vector3 lowerB, float lowerWidth)
        {
            var u = new Vector2(upperB.x - upperA.x, upperB.z - upperA.z).normalized;
            var l = new Vector2(lowerB.x - lowerA.x, lowerB.z - lowerA.z).normalized;
            var un = new Vector2(u.y, -u.x); var ln = new Vector2(l.y, -l.x);
            float det = un.x * ln.y - un.y * ln.x;
            var samples = new List<Vector2> { centre };
            if (Mathf.Abs(det) > .0001f)
            foreach (float a in new[] { -.5f, 0, .5f }) foreach (float b in new[] { -.5f, 0, .5f })
            {
                float x = (a * upperWidth * ln.y - b * lowerWidth * un.y) / det;
                float z = (un.x * b * lowerWidth - ln.x * a * upperWidth) / det;
                samples.Add(centre + new Vector2(x, z));
            }
            return samples.ToArray();
        }
        IEnumerable<GeoRoad> EstimatedRoadCandidates(Vector3 a, Vector3 b)
        {
            var result = new HashSet<GeoRoad>();
            for (int z = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / Cell); z <= Mathf.FloorToInt(Mathf.Max(a.z, b.z) / Cell); z++)
            for (int x = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell); x <= Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell); x++)
                if (sourceCells.TryGetValue(new Vector2Int(x, z), out var segments)) foreach (var segment in segments) result.Add(segment.road);
            return result.OrderBy(r => r.id, StringComparer.Ordinal);
        }
        float EstimatedRailHeight(GeoRailway rail, float x, float z)
        {
            float best = float.PositiveInfinity, height = rail.points[0].y;
            for (int index = 1; index < rail.points.Length; index++)
            {
                var a = rail.points[index - 1]; var b = rail.points[index]; var delta = new Vector2(b.x - a.x, b.z - a.z);
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x - a.x, z - a.z), delta) / Mathf.Max(.000001f, delta.sqrMagnitude));
                float distance = (new Vector2(x - a.x, z - a.z) - delta * t).sqrMagnitude;
                if (distance < best) { best = distance; height = Mathf.Lerp(a.y, b.y, t); }
            }
            return height;
        }
        float EstimatedUpperBase(EstimatedComponent component, Vector2 point)
        {
            float best = float.PositiveInfinity, height = 0;
            foreach (var pair in component.Base)
            {
                var points = pair.Value.Points;
                for (int index = 1; index < points.Length; index++)
                {
                    var a = points[index - 1]; var b = points[index]; var delta = new Vector2(b.x - a.x, b.z - a.z);
                    float t = Mathf.Clamp01(Vector2.Dot(point - new Vector2(a.x, a.z), delta) / Mathf.Max(.000001f, delta.sqrMagnitude));
                    float distance = (point - new Vector2(a.x, a.z) - delta * t).sqrMagnitude;
                    if (distance < best) { best = distance; height = pair.Value.Query(point.x, point.y, true); }
                }
            }
            return height;
        }
        EstimatedArm EstimatedApproach(EstimatedComponent component, GeoRoad first, string origin)
        {
            var arm = new EstimatedArm { Component = component }; var visited = new HashSet<GeoRoad>();
            var road = first; bool forward = road.start == origin;
            // A spatial feather choice, not a total/additional grade cap.
            float requested = Mathf.Max(road.width * 3, component.Lift * EstimatedFeatherMetresPerLiftMetre);
            while (visited.Count < EstimatedApproachMaximumRoads && visited.Add(road))
            {
                float length = MainGradeHorizontalLength(road, float.PositiveInfinity);
                arm.Roads.Add(Tuple.Create(road, forward, arm.Length, length)); arm.Length += length;
                string node = forward ? road.end : road.start;
                if (arm.Length >= requested || !junctions.TryGetValue(node, out var connected)) break;
                GeoRoad next = null; bool branch = false;
                foreach (var candidate in connected)
                {
                    if (candidate.layer != road.layer || ReferenceEquals(candidate, road)) continue;
                    if (next != null && !ReferenceEquals(next, candidate)) { branch = true; break; }
                    next = candidate;
                }
                if (branch || next == null || next.bridge || next.tunnel || next.points == null || next.points.Length < 2) break;
                bool nextForward = next.start == node;
                var far = road.points[forward ? road.points.Length - 1 : 0]; var near = next.points[nextForward ? 0 : next.points.Length - 1];
                if (new Vector2(far.x - near.x, far.z - near.z).magnitude > BridgeApproachAlignmentMetres) break;
                road = next; forward = nextForward;
            }
            return arm;
        }

        void RebuildEstimatedStructureClearances()
        {
            if (circuit || roadWidthSchema != 0) return;
            var bridges = roads.Where(r => r.bridge && !r.tunnel && r.points != null && r.points.Length >= 2).OrderBy(r => r.id, StringComparer.Ordinal).ToArray();
            var components = new List<EstimatedComponent>(); var map = new Dictionary<GeoRoad, EstimatedComponent>();
            foreach (var seed in bridges)
            {
                if (map.ContainsKey(seed)) continue;
                var component = new EstimatedComponent(); var pending = new Queue<GeoRoad>(); pending.Enqueue(seed); map.Add(seed, component);
                while (pending.Count > 0)
                {
                    var road = pending.Dequeue(); component.Roads.Add(road);
                    foreach (var other in bridges) if (!map.ContainsKey(other) && other.layer == road.layer && EstimatedSharedEndpoint(road, other))
                    { map.Add(other, component); pending.Enqueue(other); }
                }
                foreach (var road in component.Roads) component.Base.Add(road, CaptureEstimatedDeck(road, CopyEffectiveBridgePoints(road), true));
                components.Add(component);
            }
            var constraints = new List<EstimatedConstraint>(); int deferredNegativeRails = 0;
            foreach (var upper in bridges)
            for (int i = 1; i < upper.points.Length; i++)
            {
                var a = upper.points[i - 1]; var b = upper.points[i];
                foreach (var lower in EstimatedRoadCandidates(a, b))
                {
                    if (lower.bridge || lower.tunnel || lower.layer >= upper.layer || EstimatedSharedEndpoint(upper, lower)) continue;
                    for (int j = 1; j < lower.points.Length; j++) if (EstimatedIntersection(a, b, lower.points[j - 1], lower.points[j], out var point))
                        constraints.Add(new EstimatedConstraint { Upper = map[upper], UpperRoad = upper, LowerRoad = lower,
                            Samples = EstimatedCrossingSamples(point, a, b, upper.width, lower.points[j - 1], lower.points[j], lower.width),
                            Gap = (lower.maxVehicleHeight > 0 ? lower.maxVehicleHeight + .25f : EstimatedRoadFreeMetres) + EstimatedStructureReserveMetres });
                }
                foreach (var rail in railways.Where(r => !r.bridge && !r.tunnel && RailwayMetrics.Visible(r.kind) && r.layer < upper.layer))
                for (int j = 1; j < rail.points.Length; j++) if (EstimatedIntersection(a, b, rail.points[j - 1], rail.points[j], out var point))
                {
                    if (rail.layer < 0 && rail.estimatedProfile) { deferredNegativeRails++; continue; }
                    constraints.Add(new EstimatedConstraint { Upper = map[upper], UpperRoad = upper, LowerRail = rail,
                        Samples = EstimatedCrossingSamples(point, a, b, upper.width, rail.points[j - 1], rail.points[j], rail.width),
                        Gap = EstimatedTrainFreeMetres + EstimatedStructureReserveMetres });
                }
            }
            // First visual stage handles road undercrossings and all train constraints
            // on those same components. Railway-only structures need a separate
            // approach policy; do not create a short, steep ramp merely to pass.
            int deferredRailOnly = constraints.Count(c => !constraints.Any(other => ReferenceEquals(other.Upper, c.Upper) && other.LowerRoad != null));
            constraints.RemoveAll(c => !constraints.Any(other => ReferenceEquals(other.Upper, c.Upper) && other.LowerRoad != null));
            foreach (var constraint in constraints) foreach (var point in constraint.Samples)
            {
                float lower = constraint.LowerRoad != null ? RoadSurfaceHeightForRoadBase(constraint.LowerRoad, point.x, point.y) : EstimatedRailHeight(constraint.LowerRail, point.x, point.y);
                constraint.Upper.Lift = Mathf.Max(constraint.Upper.Lift, lower + constraint.Gap - EstimatedUpperBase(constraint.Upper, point));
            }
            var graphs = PrepareEstimatedGraphs(components, constraints);
            var arms = new List<EstimatedArm>();
            foreach (var component in components.Where(c => c.Lift > .005f))
            foreach (var bridge in component.Roads)
            foreach (string node in new[] { bridge.start, bridge.end })
            {
                if (component.Roads.Any(r => !ReferenceEquals(r, bridge) && (r.start == node || r.end == node))) continue;
                if (!junctions.TryGetValue(node, out var connected)) continue;
                foreach (var road in connected.Where(r => !r.bridge && !r.tunnel && r.points != null && r.points.Length >= 2).OrderBy(r => r.id, StringComparer.Ordinal))
                { arms.Add(EstimatedApproach(component, road, node)); }
            }
            // Capture the existing field BEFORE changing ground/deck membership.
            // Excluding an approach first would sample unrelated lower roads/DEM.
            var approachBases = new Dictionary<GeoRoad, EstimatedDeck>();
            foreach (var graph in graphs) foreach (var road in graph.Roads)
                if (!approachBases.ContainsKey(road)) approachBases.Add(road, CaptureEstimatedDeck(road, road.points, false));
            // Only the first approach-to-branch is structural open deck. Beyond
            // that junction the same source-connected lift is a ground road.
            foreach (var arm in arms) foreach (var item in arm.Roads) estimatedOpenDecks.Add(item.Item1);
            // A promoted viaduct is not a ground through-road preference.
            RebuildMainGradeJunctions();
            var profiles = new Dictionary<GeoRoad, EstimatedDeck>();
            foreach (var component in components.Where(c => c.Lift > .005f)) foreach (var pair in component.Base)
            {
                var profile = pair.Value; profiles.Add(pair.Key, profile);
                profile.Weights.Add(new EstimatedWeight { Component = component, Values = Enumerable.Repeat(1f, profile.Points.Length).ToArray() });
            }
            foreach (var graph in graphs) foreach (var road in graph.Roads)
            {
                if (!profiles.TryGetValue(road, out var profile)) { profile = approachBases[road]; profiles.Add(road, profile); }
                var values = new float[profile.Points.Length];
                float length = profile.Stations[profile.Stations.Length - 1];
                for (int row = 0; row < values.Length; row++) values[row] = EstimatedGraphWeight(graph, road, profile.Stations[row], length);
                profile.Weights.Add(new EstimatedWeight { Component = graph.Component, Values = values });
            }
            foreach (var pair in profiles) estimatedDecks.Add(pair.Key, pair.Value);
            for (int pass = 0; pass < EstimatedClearancePasses; pass++)
            {
                bool changed = false;
                foreach (var constraint in constraints) foreach (var point in constraint.Samples)
                {
                    float lower = constraint.LowerRoad != null ? RoadSurfaceHeightForRoad(constraint.LowerRoad, point.x, point.y) : EstimatedRailHeight(constraint.LowerRail, point.x, point.y);
                    float required = lower + constraint.Gap - EstimatedUpperBase(constraint.Upper, point);
                    if (required > constraint.Upper.Lift + .0001f) { constraint.Upper.Lift = required; changed = true; }
                }
                if (!changed) break;
            }
            // Raised decks must not retain a low ORIGINAL cut ceiling beneath
            // the lower carriageway. Rebuild changed cuts from final profiles;
            // unestimated bridges retain exactly their old/raw+Bridge2 minima.
            bridgeTerrainCuts.Clear();
            foreach (var bridge in bridges)
            {
                if (estimatedDecks.TryGetValue(bridge, out var changedDeck)) AddBridgeTerrainCuts(bridge, changedDeck.CopyPoints());
                else
                {
                    AddBridgeTerrainCuts(bridge);
                    if (effectiveBridgeProfiles.TryGetValue(bridge, out var originalDerived)) AddBridgeTerrainCuts(bridge, originalDerived.Points);
                }
            }
            foreach (var pair in estimatedDecks) if (estimatedOpenDecks.Contains(pair.Key)) AddBridgeTerrainCuts(pair.Key, pair.Value.CopyPoints());
            float worst = 0;
            foreach (var constraint in constraints) foreach (var point in constraint.Samples)
            {
                float lower = constraint.LowerRoad != null ? RoadSurfaceHeightForRoad(constraint.LowerRoad, point.x, point.y) : EstimatedRailHeight(constraint.LowerRail, point.x, point.y);
                worst = Mathf.Max(worst, lower + constraint.Gap - (EstimatedUpperBase(constraint.Upper, point) + constraint.Upper.Lift));
            }
            EstimatedClearanceStatus = "Estimated road/rail constraints=" + constraints.Count + "; lifted components=" + components.Count(c => c.Lift > .005f)
                + "; derived open approaches=" + estimatedOpenDecks.Count + "; lifted ground roads=" + estimatedDecks.Keys.Count(r => !IsEffectiveBridgeDeck(r)) + "; graph roads=" + graphs.Sum(g => g.Roads.Count) + "; graph deferred components=" + estimatedGraphDeferred + "; residual=" + worst.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)
                + "m; rail-only constraints deferred=" + deferredRailOnly + "; negative estimated rail cases deferred=" + deferredNegativeRails + "; not surveyed/native acceptance";
        }
    }
}
