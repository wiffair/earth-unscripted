using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GeoRegion
    {
        // User-approved gameplay estimates, not surveyed/statutory train gauge.
        // The current proposed body is4.05m; allow another.45m, plus the same
        //1.6m slab/beam reserve used by the road clearance presentation.
        const float EstimatedLowerRailEnvelope = 4.5f;
        const float EstimatedRailCrossingPlateau = 25;
        const float EstimatedRailFeather = 350;
        const float EstimatedRailShoulder = 6;
        const int EstimatedRailMaximumConstraints = 64;
        readonly Dictionary<GeoRailway, EffectiveRailProfile> effectiveRailProfiles = new Dictionary<GeoRailway, EffectiveRailProfile>();
        readonly Dictionary<Vector2Int, List<EffectiveRailCut>> effectiveRailCuts = new Dictionary<Vector2Int, List<EffectiveRailCut>>();
        internal string EstimatedRailClearanceStatus { get; private set; } = "Original estimated rail profiles";
        sealed class EffectiveRailProfile { internal Vector3[] Points; internal float Length; }
        sealed class RailNode
        {
            internal string Id;
            internal Vector3 Original;
            internal bool Fixed;
            internal float Drop;
            internal readonly List<RailEdge> Edges = new List<RailEdge>();
        }
        sealed class RailEdge { internal RailNode Other; internal float Length; }
        sealed class RailLowering
        {
            internal GeoRailway Rail;
            internal GeoRoad Road;
            internal int Segment;
            internal float Fraction, Drop;
            internal Dictionary<RailNode, float> Distances;
        }
        sealed class EffectiveRailCut
        {
            internal Vector3 A, B;
            internal float Inner, Outer;
        }
        internal Vector3[] CopyEffectiveRailwayPoints(GeoRailway railway)
        {
            if (railway == null) throw new ArgumentNullException(nameof(railway));
            return (Vector3[])(effectiveRailProfiles.TryGetValue(railway, out var profile) ? profile.Points : railway.points).Clone();
        }
        internal float EffectiveRailwayLength(GeoRailway railway)
            => effectiveRailProfiles.TryGetValue(railway, out var profile) ? profile.Length : railway.length;
        internal float EffectiveRailwayHeight(GeoRailway railway, float x, float z)
            => RailHeightOnPoints(effectiveRailProfiles.TryGetValue(railway, out var profile) ? profile.Points : railway.points, x, z);
        static float RailHeightOnPoints(Vector3[] points, float x, float z)
        {
            float best = float.PositiveInfinity, height = points[0].y;
            for (int i = 1; i < points.Length; i++)
            {
                var a = points[i - 1]; var b = points[i]; var flat = new Vector2(b.x - a.x, b.z - a.z);
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x - a.x, z - a.z), flat) / Mathf.Max(.000001f, flat.sqrMagnitude));
                float distance = (new Vector2(x - a.x, z - a.z) - flat * t).sqrMagnitude;
                if (distance < best) { best = distance; height = Mathf.Lerp(a.y, b.y, t); }
            }
            return height;
        }
        void ResetEstimatedRailClearances()
        {
            effectiveRailProfiles.Clear(); effectiveRailCuts.Clear(); EstimatedRailClearanceStatus = "Original estimated rail profiles";
        }
        static float RailDisplayWidth(GeoRailway rail) => Mathf.Max(RailwayMetrics.DefaultWidth + (Mathf.Clamp(rail.tracks, 1, 16) - 1) * RailwayMetrics.TrackSpacing, Mathf.Clamp(rail.width, 1, 100));
        static bool RailSourceRows(GeoRailway rail) => rail != null && rail.points != null && rail.points.Length >= 2
            && rail.nodeIds != null && rail.nodeIds.Length == rail.points.Length;
        static bool EstimatedRailEligible(GeoRailway rail) => RailSourceRows(rail) && rail.estimatedProfile
            && !rail.bridge && !rail.tunnel && RailwayMetrics.Supported(rail.kind);
        static Dictionary<RailNode, float> RailGraphDistances(RailLowering constraint, Dictionary<string, RailNode> nodes)
        {
            var comparer = Comparer<Tuple<float, string>>.Create((a, b) =>
            { int order = a.Item1.CompareTo(b.Item1); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Item2, b.Item2); });
            var queue = new SortedSet<Tuple<float, string>>(comparer);
            var distances = new Dictionary<RailNode, float>();
            var first = nodes[constraint.Rail.nodeIds[constraint.Segment]];
            var last = nodes[constraint.Rail.nodeIds[constraint.Segment + 1]];
            float length = new Vector2(last.Original.x - first.Original.x, last.Original.z - first.Original.z).magnitude;
            void Seed(RailNode node, float distance)
            {
                if (distances.TryGetValue(node, out float old) && old <= distance) return;
                distances[node] = distance; queue.Add(Tuple.Create(distance, node.Id));
            }
            Seed(first, length * constraint.Fraction); Seed(last, length * (1 - constraint.Fraction));
            // Each source node can settle once; edges are the actual bounded
            // source rows, not nearest-track/XY guesses. No forced endpoint pin.
            while (queue.Count > 0)
            {
                var item = queue.Min; queue.Remove(item); var node = nodes[item.Item2];
                if (distances[node] != item.Item1 || item.Item1 >= EstimatedRailFeather) continue;
                foreach (var edge in node.Edges) Seed(edge.Other, item.Item1 + edge.Length);
            }
            return distances;
        }
        void RebuildEstimatedRailClearances()
        {
            if (circuit || roadWidthSchema != 0) return;
            var eligible = railways.Where(EstimatedRailEligible).OrderBy(r => r.id, StringComparer.Ordinal).ToArray();
            var nodes = new Dictionary<string, RailNode>(StringComparer.Ordinal);
            // Include historical source-connected rows to keep shared-node heights
            // consistent; renderer visibility/operational kinds are UNCHANGED.
            // Layer is local structure metadata, not a height conversion or a
            // veto on a genuine shared source node between adjacent rail ways.
            foreach (var rail in eligible)
            for (int i = 0; i < rail.points.Length; i++)
            {
                string id = rail.nodeIds[i]; if (string.IsNullOrEmpty(id)) continue;
                if (!nodes.TryGetValue(id, out var node)) nodes.Add(id, node = new RailNode { Id = id, Original = rail.points[i] });
                var p = rail.points[i];
                if (node.Original.x != p.x || node.Original.y != p.y || node.Original.z != p.z) node.Fixed = true;
            }
            // Actual fixed structures/explicit profiles remain anchors. Cropped
            // graph terminals are NOT declared surveyed or fixed buffer stops.
            foreach (var rail in railways.Where(r => RailSourceRows(r) && !EstimatedRailEligible(r)))
                foreach (string id in rail.nodeIds) if (id != null && nodes.TryGetValue(id, out var node)) node.Fixed = true;
            foreach (var rail in eligible)
            for (int i = 1; i < rail.points.Length; i++)
            {
                if (!nodes.TryGetValue(rail.nodeIds[i - 1] ?? "", out var a) || !nodes.TryGetValue(rail.nodeIds[i] ?? "", out var b)) continue;
                float length = new Vector2(b.Original.x - a.Original.x, b.Original.z - a.Original.z).magnitude;
                if (length <= .000001f) continue;
                a.Edges.Add(new RailEdge { Other = b, Length = length }); b.Edges.Add(new RailEdge { Other = a, Length = length });
            }
            var constraints = new List<RailLowering>(); int deferredFixed = 0, deferredBudget = 0;
            foreach (var road in roads.Where(r => r.bridge && !r.tunnel && r.points != null && r.points.Length >= 2).OrderBy(r => r.id, StringComparer.Ordinal))
            for (int i = 1; i < road.points.Length; i++)
            foreach (var rail in eligible.Where(r => RailwayMetrics.Visible(r.kind) && r.layer < 0 && r.layer < road.layer))
            for (int j = 1; j < rail.points.Length; j++)
            {
                if (!EstimatedIntersection(road.points[i - 1], road.points[i], rail.points[j - 1], rail.points[j], out var crossing)) continue;
                if (constraints.Count >= EstimatedRailMaximumConstraints) { deferredBudget++; continue; }
                float drop = 0;
                foreach (var sample in EstimatedCrossingSamples(crossing, road.points[i - 1], road.points[i], road.width, rail.points[j - 1], rail.points[j], RailDisplayWidth(rail)))
                    drop = Mathf.Max(drop, RailHeightOnPoints(rail.points, sample.x, sample.y) + EstimatedLowerRailEnvelope + EstimatedStructureReserveMetres
                        - RoadSurfaceHeightForRoad(road, sample.x, sample.y));
                if (drop <= 0) continue;
                var a = rail.points[j - 1]; var b = rail.points[j]; var flat = new Vector2(b.x - a.x, b.z - a.z);
                var constraint = new RailLowering { Rail = rail, Road = road, Segment = j - 1, Drop = drop,
                    Fraction = Mathf.Clamp01(Vector2.Dot(crossing - new Vector2(a.x, a.z), flat) / Mathf.Max(.000001f, flat.sqrMagnitude)) };
                if (!nodes.ContainsKey(rail.nodeIds[j - 1] ?? "") || !nodes.ContainsKey(rail.nodeIds[j] ?? "")) { deferredFixed++; continue; }
                constraint.Distances = RailGraphDistances(constraint, nodes);
                if (constraint.Distances.Any(pair => pair.Key.Fixed && pair.Value < EstimatedRailFeather)) { deferredFixed++; continue; }
                constraints.Add(constraint);
            }
            foreach (var node in nodes.Values)
            foreach (var constraint in constraints)
            {
                if (!constraint.Distances.TryGetValue(node, out float distance)) continue;
                float weight = 1 - MainGradeFade((distance - EstimatedRailCrossingPlateau) / (EstimatedRailFeather - EstimatedRailCrossingPlateau));
                float desired = constraint.Drop * weight;
                // A.10m smooth-max band removes a derivative kink where two
                // nearby correction fields exchange priority. It can add at
                // most.025m per merge, always in the safe downward direction.
                if (desired <= 0) continue;
                float band = Mathf.Min(.10f, 2 * Mathf.Min(node.Drop, desired));
                float overlap = band > 0 ? Mathf.Clamp01(1 - Mathf.Abs(node.Drop - desired) / band) : 0;
                node.Drop = Mathf.Max(node.Drop, desired) + overlap * overlap * band * .25f;
            }
            float maximumDrop = 0; int changedPoints = 0;
            foreach (var rail in eligible)
            {
                var points = (Vector3[])rail.points.Clone(); bool changed = false;
                for (int i = 0; i < points.Length; i++)
                {
                    if (!nodes.TryGetValue(rail.nodeIds[i] ?? "", out var node) || node.Fixed || node.Drop <= 0) continue;
                    points[i].y -= node.Drop; changed = true; changedPoints++; maximumDrop = Mathf.Max(maximumDrop, node.Drop);
                }
                if (!changed) continue;
                float length = 0; for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
                effectiveRailProfiles.Add(rail, new EffectiveRailProfile { Points = points, Length = length });
                if (RailwayMetrics.Visible(rail.kind)) AddEffectiveRailCuts(rail, points);
            }
            EstimatedRailClearanceStatus = "Estimated lower-rail constraints=" + constraints.Count + "; changed ways=" + effectiveRailProfiles.Count
                + "; changed point memberships=" + changedPoints + "; max drop=" + maximumDrop.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)
                + "m; fixed/source conflicts deferred=" + deferredFixed + "; budget deferred=" + deferredBudget + "; gameplay estimate, not surveyed/native train clearance";
        }
        void AddEffectiveRailCuts(GeoRailway rail, Vector3[] points)
        {
            float width = RailDisplayWidth(rail);
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i - 1].y == rail.points[i - 1].y && points[i].y == rail.points[i].y) continue;
                // Include one terrain triangle diagonal to keep its interpolation
                // out of sleepers/ballast. Shoulders transition to existing soil.
                float inner = width * .5f + 2.5f * Mathf.Sqrt(2);
                var cut = new EffectiveRailCut { A = points[i - 1], B = points[i], Inner = inner, Outer = inner + EstimatedRailShoulder };
                for (int z = Mathf.FloorToInt((Mathf.Min(cut.A.z, cut.B.z) - cut.Outer) / Cell); z <= Mathf.FloorToInt((Mathf.Max(cut.A.z, cut.B.z) + cut.Outer) / Cell); z++)
                for (int x = Mathf.FloorToInt((Mathf.Min(cut.A.x, cut.B.x) - cut.Outer) / Cell); x <= Mathf.FloorToInt((Mathf.Max(cut.A.x, cut.B.x) + cut.Outer) / Cell); x++)
                {
                    var key = new Vector2Int(x, z); if (!effectiveRailCuts.TryGetValue(key, out var list)) effectiveRailCuts.Add(key, list = new List<EffectiveRailCut>());
                    list.Add(cut);
                }
            }
        }
        float ApplyEstimatedRailCut(float x, float z, float original)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
            if (!effectiveRailCuts.TryGetValue(key, out var cuts)) return original;
            float answer = original;
            foreach (var cut in cuts)
            {
                var flat = new Vector2(cut.B.x - cut.A.x, cut.B.z - cut.A.z);
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x - cut.A.x, z - cut.A.z), flat) / Mathf.Max(.000001f, flat.sqrMagnitude));
                float distance = (new Vector2(x - cut.A.x, z - cut.A.z) - flat * t).magnitude;
                if (distance >= cut.Outer) continue;
                float bed = Mathf.Lerp(cut.A.y, cut.B.y, t) - RailwayMetrics.TrackBuildUp - .02f;
                answer = Mathf.Min(answer, Mathf.Lerp(bed, original, MainGradeFade((distance - cut.Inner) / (cut.Outer - cut.Inner))));
            }
            return answer;
        }
    }
}
