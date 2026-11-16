using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GeoRegion
    {
        // Preparation-only graph work. These bounds limit visits, not road grade.
        const int EstimatedGraphMaximumRoads = 1024;
        const int EstimatedGraphMaximumNodes = 4096;
        int estimatedGraphDeferred;
        sealed class EstimatedGraph
        {
            internal EstimatedComponent Component;
            internal float Radius;
            internal readonly Dictionary<Tuple<int, string>, float> Distances = new Dictionary<Tuple<int, string>, float>();
            internal readonly HashSet<GeoRoad> Roads = new HashSet<GeoRoad>();
            internal bool VisitLimit;
        }
        // Add only the derived displacement to the global old field. Feeding
        // absolute cached heights back through the old blend would double-apply
        // junction preferences and leave a step where a feather reaches zero.
        float EstimatedGroundLift(GeoRoad road, float x, float z)
        {
            if (road == null || IsEffectiveBridgeDeck(road) || !estimatedDecks.TryGetValue(road, out var profile)) return 0;
            return profile.Query(x, z) - profile.Query(x, z, true);
        }
        EstimatedGraph BuildEstimatedGraph(EstimatedComponent component)
        {
            var graph = new EstimatedGraph { Component = component, Radius = component.Lift * EstimatedFeatherMetresPerLiftMetre };
            var comparer = Comparer<Tuple<float, int, string>>.Create((a, b) =>
            {
                int order = a.Item1.CompareTo(b.Item1);
                if (order == 0) order = a.Item2.CompareTo(b.Item2);
                return order != 0 ? order : StringComparer.Ordinal.Compare(a.Item3, b.Item3);
            });
            var pending = new SortedSet<Tuple<float, int, string>>(comparer);
            foreach (var bridge in component.Roads)
            foreach (string node in new[] { bridge.start, bridge.end })
            {
                if (component.Roads.Any(r => !ReferenceEquals(r, bridge) && (r.start == node || r.end == node))) continue;
                if (!junctions.TryGetValue(node, out var incident)) continue;
                foreach (var road in incident)
                {
                    if (road.bridge || road.tunnel || road.points == null || road.points.Length < 2) continue;
                    var key = Tuple.Create(road.layer, node);
                    if (graph.Distances.ContainsKey(key)) continue;
                    graph.Distances.Add(key, 0); pending.Add(Tuple.Create(0f, road.layer, node));
                }
            }
            while (pending.Count > 0)
            {
                var item = pending.Min; pending.Remove(item);
                var key = Tuple.Create(item.Item2, item.Item3);
                if (item.Item1 != graph.Distances[key] || item.Item1 >= graph.Radius) continue;
                if (!junctions.TryGetValue(item.Item3, out var incident)) continue;
                foreach (var road in incident.OrderBy(r => r.id, StringComparer.Ordinal))
                {
                    if (road.layer != item.Item2 || road.bridge || road.tunnel || road.points == null || road.points.Length < 2) continue;
                    if (graph.Roads.Count >= EstimatedGraphMaximumRoads || graph.Distances.Count >= EstimatedGraphMaximumNodes)
                    { graph.VisitLimit = true; return graph; }
                    graph.Roads.Add(road);
                    float nextDistance = item.Item1 + MainGradeHorizontalLength(road, float.PositiveInfinity);
                    var next = Tuple.Create(road.layer, road.start == item.Item3 ? road.end : road.start);
                    if (graph.Distances.TryGetValue(next, out float old) && old <= nextDistance) continue;
                    graph.Distances[next] = nextDistance; pending.Add(Tuple.Create(nextDistance, next.Item1, next.Item2));
                }
            }
            return graph;
        }
        static float EstimatedGraphWeight(EstimatedGraph graph, GeoRoad road, float station, float length)
        {
            float first = graph.Distances.TryGetValue(Tuple.Create(road.layer, road.start), out float a) ? a + station : float.PositiveInfinity;
            float last = graph.Distances.TryGetValue(Tuple.Create(road.layer, road.end), out float b) ? b + length - station : float.PositiveInfinity;
            return 1 - MainGradeFade(Mathf.Min(first, last) / Mathf.Max(.001f, graph.Radius));
        }
        static float EstimatedRoadStation(GeoRoad road, float x, float z, out float length)
        {
            float best = float.PositiveInfinity, station = 0; length = 0;
            for (int index = 1; index < road.points.Length; index++)
            {
                var a = road.points[index - 1]; var b = road.points[index];
                var delta = new Vector2(b.x - a.x, b.z - a.z); float segment = delta.magnitude;
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x - a.x, z - a.z), delta) / Mathf.Max(.000001f, delta.sqrMagnitude));
                float distance = (new Vector2(x - a.x, z - a.z) - delta * t).sqrMagnitude;
                if (distance < best) { best = distance; station = length + segment * t; }
                length += segment;
            }
            return station;
        }
        float EstimatedGraphOffset(List<EstimatedGraph> graphs, GeoRoad road, float x, float z)
        {
            float station = EstimatedRoadStation(road, x, z, out float length), total = 0, lift = 0;
            foreach (var graph in graphs)
            {
                if (!graph.Roads.Contains(road)) continue;
                float weight = EstimatedGraphWeight(graph, road, station, length);
                total += weight; lift += weight * graph.Component.Lift;
            }
            return lift / Mathf.Max(1, total);
        }
        List<EstimatedGraph> PrepareEstimatedGraphs(List<EstimatedComponent> components, List<EstimatedConstraint> constraints)
        {
            estimatedGraphDeferred = 0;
            var active = components.Where(c => c.Lift > .005f).ToList();
            List<EstimatedGraph> graphs = null;
            for (int pass = 0; pass < EstimatedClearancePasses; pass++)
            {
                graphs = active.Select(BuildEstimatedGraph).ToList();
                // Never apply an incomplete graph feather: retain that component's
                // original estimated source profile, without refusing the world.
                foreach (var incomplete in graphs.Where(g => g.VisitLimit).ToArray())
                {
                    incomplete.Component.Lift = 0; active.Remove(incomplete.Component);
                    constraints.RemoveAll(c => ReferenceEquals(c.Upper, incomplete.Component));
                    graphs.Remove(incomplete); estimatedGraphDeferred++;
                }
                // The two carriageways can meet through a genuine roundabout.
                // Couple their offset, not their physical geometry/topology.
                bool merged = false;
                for (int i = 0; i < graphs.Count && !merged; i++)
                for (int j = i + 1; j < graphs.Count && !merged; j++)
                {
                    bool connected = graphs[j].Component.Roads.Any(r => graphs[i].Distances.Any(pair =>
                        (pair.Key.Item2 == r.start || pair.Key.Item2 == r.end) && pair.Value < graphs[i].Radius));
                    if (!connected) continue;
                    var keep = graphs[i].Component; var remove = graphs[j].Component;
                    keep.Lift = Mathf.Max(keep.Lift, remove.Lift);
                    keep.Roads.AddRange(remove.Roads);
                    foreach (var pair in remove.Base) keep.Base.Add(pair.Key, pair.Value);
                    foreach (var constraint in constraints) if (ReferenceEquals(constraint.Upper, remove)) constraint.Upper = keep;
                    active.Remove(remove); components.Remove(remove); merged = true;
                }
                if (merged) continue;
                bool changed = false;
                foreach (var constraint in constraints) foreach (var point in constraint.Samples)
                {
                    float lower = constraint.LowerRoad != null
                        ? RoadSurfaceHeightForRoadBase(constraint.LowerRoad, point.x, point.y) + EstimatedGraphOffset(graphs, constraint.LowerRoad, point.x, point.y)
                        : EstimatedRailHeight(constraint.LowerRail, point.x, point.y);
                    float required = lower + constraint.Gap - EstimatedUpperBase(constraint.Upper, point);
                    if (required > constraint.Upper.Lift + .0001f) { constraint.Upper.Lift = required; changed = true; }
                }
                if (!changed) return graphs;
            }
            var final = active.Select(BuildEstimatedGraph).ToList();
            foreach (var incomplete in final.Where(g => g.VisitLimit).ToArray())
            {
                incomplete.Component.Lift = 0;
                constraints.RemoveAll(c => ReferenceEquals(c.Upper, incomplete.Component));
                final.Remove(incomplete); estimatedGraphDeferred++;
            }
            return final;
        }
    }
}
