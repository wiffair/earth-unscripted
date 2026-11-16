using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GeoRegion
    {
        // Legacy road ribbons sample a shared height field; physical bridge decks
        // retain a source-linked derived profile. Bind genuine source-node
        // approaches to that deck plane, never a nearby XY crossing.
        // This private cache changes no original road points/IDs/tags.
        const int BridgeApproachMaximumRoads = 64;
        const float BridgeApproachAlignmentMetres = .001f;
        const float BridgeSeamPlaneBandMetres = 4.5f; // Existing 2.5m rows + <=2m columns.
        const float BridgeWeightLengthMetres = .001f; // Stable inverse-distance blend, not a grade cap.
        readonly Dictionary<Vector2Int, List<BridgeApproach>> bridgeApproachCells =
            new Dictionary<Vector2Int, List<BridgeApproach>>();
        readonly List<BridgeApproach> bridgeApproaches = new List<BridgeApproach>();
        readonly Dictionary<Vector2Int, List<BridgeTerrainCut>> bridgeTerrainCuts = new Dictionary<Vector2Int, List<BridgeTerrainCut>>();
        // Match the unchanged legacy physical slab(.75m) and beam(.65m).
        // Extra5cm is soil clearance, NOT a newly claimed surveyed underpass.
        const float BridgeStructureDropMetres = .75f + .65f + .05f;
        sealed class BridgeTerrainCut
        {
            internal Vector3 Start;
            internal Vector2 Direction;
            internal float Length, Grade, Radius, MiterHeightAllowance;
        }

        sealed class BridgeApproach
        {
            internal GeoRoad Bridge;
            internal string Node;
            internal int GroundLayer;
            internal Vector3 End;
            internal Vector2 IntoDeck;
            internal float Crossfall, Grade, HalfWidth, CoreRadius, OuterRadius, PlaneBand;
            internal HashSet<GeoRoad> Roads;
            internal readonly Dictionary<GeoRoad, BridgeArmDistance> Distances = new Dictionary<GeoRoad, BridgeArmDistance>();
        }

        sealed class BridgeArmDistance
        {
            internal bool Forward;
            internal float Offset, Length, EndDistance, CoreDistance;
        }

        void RebuildBridgeApproaches()
        {
            bridgeApproachCells.Clear(); bridgeApproaches.Clear(); bridgeTerrainCuts.Clear(); effectiveBridgeProfiles.Clear();
            if (circuit) return;
            foreach (var bridge in roads)
            {
                if (!bridge.bridge || bridge.tunnel || bridge.points == null || bridge.points.Length < 2) continue;
                AddBridgeTerrainCuts(bridge);
                foreach (bool start in new[] { true, false })
                {
                    string node = start ? bridge.start : bridge.end;
                    if (!junctions.TryGetValue(node, out var connected)) continue;
                    Vector3 end = bridge.points[start ? 0 : bridge.points.Length - 1];
                    Vector3 next = bridge.points[start ? 1 : bridge.points.Length - 2];
                    var delta = new Vector2(next.x - end.x, next.z - end.z);
                    float span = delta.magnitude;
                    if (span < .001f) continue;
                    var layers = new Dictionary<int, HashSet<GeoRoad>>();
                    foreach (var road in connected)
                    {
                        if (road.bridge || road.tunnel || road.points == null || road.points.Length < 2) continue;
                        Vector3 p = road.points[road.start == node ? 0 : road.points.Length - 1];
                        if (new Vector2(p.x - end.x, p.z - end.z).magnitude > BridgeApproachAlignmentMetres) continue;
                        if (!layers.TryGetValue(road.layer, out var members)) layers.Add(road.layer, members = new HashSet<GeoRoad>());
                        members.Add(road);
                    }
                    foreach (var layer in layers)
                    {
                        float halfWidth = bridge.width * .5f;
                        var approach = new BridgeApproach { Bridge = bridge, Node = node, End = end,
                            GroundLayer = layer.Key, IntoDeck = delta / span, Grade = (next.y - end.y) / span,
                            HalfWidth = halfWidth, CoreRadius = halfWidth + BridgeSeamPlaneBandMetres,
                            OuterRadius = halfWidth + BridgeSeamPlaneBandMetres + Mathf.Clamp(bridge.width * 3, 12, 40),
                            PlaneBand = BridgeSeamPlaneBandMetres, Roads = layer.Value };
                        ExtendBridgeApproach(approach);
                        bridgeApproaches.Add(approach);
                    }
                }
            }
            AdaptCrowdedBridgeEnds();
            // Two nearby bridge ends on one ground chain blend continuously.
            // Keep exact-plane neighborhoods disjoint, so nearest-anchor choice
            // cannot introduce a step midway between closely spaced bridges.
            for (int a = 0; a < bridgeApproaches.Count; a++)
            for (int b = a + 1; b < bridgeApproaches.Count; b++)
            {
                var first = bridgeApproaches[a]; var second = bridgeApproaches[b];
                if (first.GroundLayer != second.GroundLayer || !first.Roads.Overlaps(second.Roads)) continue;
                float separation = Mathf.Max(.005f, BridgeTerminalSeparation(first, second));
                first.PlaneBand = Mathf.Min(first.PlaneBand, separation * .2f);
                second.PlaneBand = Mathf.Min(second.PlaneBand, separation * .2f);
            }
            foreach (var approach in bridgeApproaches)
            {
                float radius = approach.OuterRadius;
                for (int z = Mathf.FloorToInt((approach.End.z - radius) / Cell); z <= Mathf.FloorToInt((approach.End.z + radius) / Cell); z++)
                for (int x = Mathf.FloorToInt((approach.End.x - radius) / Cell); x <= Mathf.FloorToInt((approach.End.x + radius) / Cell); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!bridgeApproachCells.TryGetValue(key, out var list)) bridgeApproachCells.Add(key, list = new List<BridgeApproach>());
                    list.Add(approach);
                }
            }
            RebuildEstimatedStructureClearances();
        }

        // Derived presentation/driving rows. Original GeoRoad.points remain the
        // source record. Only a genuinely connected short approach terminating
        // inside a wider junction selects this profile, never an XY crossing.
        readonly Dictionary<GeoRoad, EffectiveBridgeProfile> effectiveBridgeProfiles =
            new Dictionary<GeoRoad, EffectiveBridgeProfile>();

        sealed class EffectiveBridgeProfile
        {
            internal readonly Vector3[] Points;
            internal readonly float[] Crossfalls;
            internal EffectiveBridgeProfile(Vector3[] points, float[] crossfalls)
            { Points = points; Crossfalls = crossfalls; }
        }

        static float BridgeApproachPlane(BridgeApproach approach, float x, float z)
        {
            float dx = x - approach.End.x, dz = z - approach.End.z;
            return approach.End.y + approach.Grade * (dx * approach.IntoDeck.x + dz * approach.IntoDeck.y)
                + approach.Crossfall * (dx * approach.IntoDeck.y - dz * approach.IntoDeck.x);
        }

        internal Vector3[] CopyEffectiveBridgePoints(GeoRoad bridge)
        {
            if (estimatedDecks.TryGetValue(bridge, out var clearance)) return clearance.CopyPoints();
            var points = effectiveBridgeProfiles.TryGetValue(bridge, out var profile) ? profile.Points : bridge.points;
            return (Vector3[])points.Clone();
        }

        float EffectiveBridgeHeight(GeoRoad bridge, float x, float z)
        {
            if(AutomaticWorld)return NativeProfileHeight(bridge,x,z);
            if (TryEstimatedDeckHeight(bridge, x, z, out float clearance)) return clearance;
            var points = effectiveBridgeProfiles.TryGetValue(bridge, out var profile) ? profile.Points : bridge.points;
            float best = float.PositiveInfinity, height = points[0].y;
            for (int index = 1; index < points.Length; index++)
            {
                var a = points[index - 1]; var b = points[index];
                float dx = b.x - a.x, dz = b.z - a.z, squaredLength = dx * dx + dz * dz;
                if (squaredLength < .000001f) continue;
                float t = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / squaredLength);
                float ex = x - a.x - dx * t, ez = z - a.z - dz * t;
                float squared = ex * ex + ez * ez;
                if (squared >= best) continue;
                best = squared;
                float crossfall = profile == null ? 0 : Mathf.Lerp(profile.Crossfalls[index - 1], profile.Crossfalls[index], t);
                height = Mathf.Lerp(a.y, b.y, t) + crossfall * (ex * dz - ez * dx) / Mathf.Sqrt(squaredLength);
            }
            return height;
        }

        // Match the existing, unmodified main-road field only when the short
        // non-bridge arm has insufficient space before another genuine branch.
        // Width-derived rolling reach is a local geometric choice, not a grade
        // limit. Ambiguous layers/nonplanar junction ends retain the old profile.
        void AdaptCrowdedBridgeEnds()
        {
            foreach (var bridge in roads)
            {
                if (!bridge.bridge || bridge.tunnel || bridge.points == null || bridge.points.Length < 2) continue;
                var correction = new float[2]; var derivative = new float[2]; var crossfall = new float[2];
                bool changed = false;
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    string node = endpoint == 0 ? bridge.start : bridge.end;
                    BridgeApproach selected = null; bool ambiguous = false;
                    foreach (var approach in bridgeApproaches)
                        if (ReferenceEquals(approach.Bridge, bridge) && approach.Node == node)
                        { if (selected != null) ambiguous = true; selected = approach; }
                    if (selected == null || ambiguous) continue;
                    float requested = Mathf.Clamp(bridge.width * 3, 12, 40);
                    // A short deck cannot absorb this transition either. Keep
                    // its existing profile rather than create a second ramp.
                    if (MainGradeHorizontalLength(bridge, requested) < requested) continue;
                    bool crowded = false;
                    foreach (var pair in selected.Distances)
                    {
                        var arm = pair.Value;
                        if (arm.EndDistance >= requested || arm.Offset + arm.Length > arm.EndDistance + .001f) continue;
                        string farNode = arm.Forward ? pair.Key.end : pair.Key.start;
                        if (!junctions.TryGetValue(farNode, out var incident)) continue;
                        int branches = 0; float otherHalfWidth = 0;
                        foreach (var road in incident)
                        {
                            if (road.layer != selected.GroundLayer || road.bridge || road.tunnel) continue;
                            branches++;
                            if (!ReferenceEquals(road, pair.Key)) otherHalfWidth = Mathf.Max(otherHalfWidth, road.width * .5f);
                        }
                        if (branches >= 3 && arm.EndDistance - otherHalfWidth < bridge.width) crowded = true;
                    }
                    if (!crowded) continue;
                    var end = selected.End;
                    // Forward road orientation is retained even at the far end.
                    Vector2 direction = endpoint == 0 ? selected.IntoDeck : -selected.IntoDeck;
                    Vector2 right = new Vector2(direction.y, -direction.x);
                    float half = bridge.width * .5f;
                    if (half < .01f) continue;
                    float left = RoadSurfaceHeightOnLayerUnjoined(end.x - right.x * half, end.z - right.y * half, selected.GroundLayer);
                    float rightY = RoadSurfaceHeightOnLayerUnjoined(end.x + right.x * half, end.z + right.y * half, selected.GroundLayer);
                    float centre = (left + rightY) * .5f, roll = (rightY - left) / (2 * half);
                    bool planar = true;
                    foreach (float lateral in new[] { -.24f, 0, .24f })
                    {
                        float distance = lateral * bridge.width;
                        float sample = RoadSurfaceHeightOnLayerUnjoined(end.x + right.x * distance, end.z + right.y * distance, selected.GroundLayer);
                        if (Mathf.Abs(sample - (centre + roll * distance)) > .005f) planar = false;
                    }
                    if (!planar || Mathf.Abs(centre - end.y) <= .005f) continue;
                    float ahead = RoadSurfaceHeightOnLayerUnjoined(end.x + direction.x, end.z + direction.y, selected.GroundLayer);
                    float behind = RoadSurfaceHeightOnLayerUnjoined(end.x - direction.x, end.z - direction.y, selected.GroundLayer);
                    float originalGrade = endpoint == 0 ? selected.Grade : -selected.Grade;
                    correction[endpoint] = centre - end.y;
                    derivative[endpoint] = (ahead - behind) * .5f - originalGrade;
                    crossfall[endpoint] = roll; changed = true;
                }
                if (!changed) continue;
                float total = MainGradeHorizontalLength(bridge, float.PositiveInfinity), travelled = 0;
                if (total < .001f) continue;
                var points = new List<Vector3>(); var rolls = new List<float>();
                for (int index = 1; index < bridge.points.Length; index++)
                {
                    var a = bridge.points[index - 1]; var b = bridge.points[index];
                    float dx = b.x - a.x, dz = b.z - a.z, length = Mathf.Sqrt(dx * dx + dz * dz);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2.5f));
                    for (int row = index == 1 ? 0 : 1; row <= steps; row++)
                    {
                        float fraction = row / (float)steps, t = (travelled + fraction * length) / total;
                        float t2 = t * t, t3 = t2 * t;
                        var point = Vector3.Lerp(a, b, fraction);
                        // Cubic endpoint value/tangent correction distributed
                        // over the complete bridge, avoiding a short steep ramp.
                        point.y += (2 * t3 - 3 * t2 + 1) * correction[0]
                            + (t3 - 2 * t2 + t) * total * derivative[0]
                            + (-2 * t3 + 3 * t2) * correction[1]
                            + (t3 - t2) * total * derivative[1];
                        points.Add(point);
                        rolls.Add(Mathf.Lerp(crossfall[0], crossfall[1], MainGradeFade(t)));
                    }
                    travelled += length;
                }
                var profile = new EffectiveBridgeProfile(points.ToArray(), rolls.ToArray());
                effectiveBridgeProfiles.Add(bridge, profile);
                foreach (var approach in bridgeApproaches)
                {
                    if (!ReferenceEquals(approach.Bridge, bridge)) continue;
                    bool start = approach.Node == bridge.start;
                    int endIndex = start ? 0 : profile.Points.Length - 1, nextIndex = start ? 1 : profile.Points.Length - 2;
                    approach.End = profile.Points[endIndex];
                    var next = profile.Points[nextIndex];
                    float span = new Vector2(next.x - approach.End.x, next.z - approach.End.z).magnitude;
                    approach.Grade = (next.y - approach.End.y) / Mathf.Max(.001f, span);
                    approach.Crossfall = profile.Crossfalls[endIndex] * (start ? 1 : -1);
                }
                // Original cuts remain: adapting a deck upwards must not fill
                // terrain back into the space previously cleared below it.
                AddBridgeTerrainCuts(bridge, profile.Points);
            }
        }

        void AddBridgeTerrainCuts(GeoRoad bridge, Vector3[] derivedPoints = null)
        {
            var source = derivedPoints ?? bridge.points;
            // Enlarge by ONE terrain-triangle diagonal so interpolation cannot
            // pull an outside high grid vertex through the deck/side barriers.
            float grid = roadWidthSchema == 1 ? 1.25f : 2.5f;
            for (int index = 0; index < source.Length - 1; index++)
            {
                var a = source[index]; var b = source[index + 1];
                var flat = new Vector2(b.x - a.x, b.z - a.z); float length = flat.magnitude;
                if (length <= .001f) continue;
                var direction = (b - a).normalized;
                var before = index > 0 ? (a - source[index - 1]).normalized : direction;
                var after = index + 2 < source.Length ? (source[index + 2] - b).normalized : direction;
                // Same bounded miter/bevel geometry as existing RibbonSteps.
                var right = Vector3.Cross(Vector3.up, direction).normalized;
                var r0 = Vector3.Cross(Vector3.up, (before + direction).normalized).normalized;
                var r1 = Vector3.Cross(Vector3.up, (direction + after).normalized).normalized;
                r0 /= Mathf.Max(.6f, Vector3.Dot(r0, right)); r1 /= Mathf.Max(.6f, Vector3.Dot(r1, right));
                if (Vector3.Angle(before, direction) > 60) r0 = right;
                if (Vector3.Angle(direction, after) > 60) r1 = right;
                var unit = flat / length; float half = bridge.width * .5f, grade = (b.y - a.y) / length;
                float along = half * Mathf.Max(Mathf.Abs(r0.x * unit.x + r0.z * unit.y), Mathf.Abs(r1.x * unit.x + r1.z * unit.y));
                var cut = new BridgeTerrainCut { Start = a, Direction = unit, Length = length, Grade = grade,
                    Radius = half * Mathf.Max(r0.magnitude, r1.magnitude) + grid * Mathf.Sqrt(2),
                    MiterHeightAllowance = Mathf.Abs(grade) * along };
                for (int z = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - cut.Radius) / Cell); z <= Mathf.FloorToInt((Mathf.Max(a.z, b.z) + cut.Radius) / Cell); z++)
                for (int x = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - cut.Radius) / Cell); x <= Mathf.FloorToInt((Mathf.Max(a.x, b.x) + cut.Radius) / Cell); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!bridgeTerrainCuts.TryGetValue(key, out var list)) bridgeTerrainCuts.Add(key, list = new List<BridgeTerrainCut>());
                    list.Add(cut);
                }
            }
        }

        bool TryBridgeTerrainCeiling(float x, float z, out float ceiling)
        {
            ceiling = float.PositiveInfinity;
            var key = new Vector2Int(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
            if (!bridgeTerrainCuts.TryGetValue(key, out var list)) return false;
            foreach (var cut in list)
            {
                float dx = x - cut.Start.x, dz = z - cut.Start.z;
                float along = dx * cut.Direction.x + dz * cut.Direction.y;
                float clamped = Mathf.Clamp(along, 0, cut.Length);
                float ex = dx - cut.Direction.x * clamped, ez = dz - cut.Direction.y * clamped;
                if (ex * ex + ez * ez > cut.Radius * cut.Radius) continue;
                // Extrapolate the segment plane over the grid stencil, then take
                // the minimum at bends. This is a local terrain CUT only; never
                // use bridge height as newly filled ground beneath the span.
                ceiling = Mathf.Min(ceiling, cut.Start.y + cut.Grade * along - BridgeStructureDropMetres - cut.MiterHeightAllowance);
            }
            return !float.IsPositiveInfinity(ceiling);
        }

        void ExtendBridgeApproach(BridgeApproach approach)
        {
            var incident = new List<GeoRoad>(approach.Roads);
            float requested = Mathf.Clamp(approach.Bridge.width * 3, 12, 40);
            foreach (var first in incident)
            {
                var chain = new List<KeyValuePair<GeoRoad, BridgeArmDistance>>();
                GeoRoad current = first;
                bool forward = current.start == approach.Node;
                float offset = 0;
                while (true)
                {
                    float length = MainGradeHorizontalLength(current, float.PositiveInfinity);
                    var entry = new BridgeArmDistance { Forward = forward, Offset = offset, Length = length };
                    chain.Add(new KeyValuePair<GeoRoad, BridgeArmDistance>(current, entry));
                    offset += length;
                    string node = forward ? current.end : current.start;
                    if (offset >= requested || approach.Roads.Count >= BridgeApproachMaximumRoads || !junctions.TryGetValue(node, out var connected)) break;
                    GeoRoad next = null; bool branch = false;
                    foreach (var road in connected)
                    {
                        if (road.layer != approach.GroundLayer || ReferenceEquals(road, current)) continue;
                        if (next != null && !ReferenceEquals(next, road)) { branch = true; break; }
                        next = road;
                    }
                    if (branch || next == null || next.bridge || next.tunnel || approach.Roads.Contains(next)) break;
                    bool nextForward = next.start == node;
                    Vector3 far = current.points[forward ? current.points.Length - 1 : 0];
                    Vector3 near = next.points[nextForward ? 0 : next.points.Length - 1];
                    if (new Vector2(near.x - far.x, near.z - far.z).magnitude > BridgeApproachAlignmentMetres) break;
                    approach.Roads.Add(next);
                    current = next; forward = nextForward;
                }
                // Finish the correction BEFORE the next genuine branch. This
                // preserves that branch's common old field and avoids moving a
                // bridge gap one short span farther along the approach.
                float available = Mathf.Min(requested, offset);
                var firstPoints = first.points;
                Vector3 away = firstPoints[first.start == approach.Node ? 1 : firstPoints.Length - 2] - approach.End;
                var outward = new Vector2(away.x, away.z).normalized;
                float seamProjection = approach.HalfWidth * Mathf.Abs(outward.x * approach.IntoDeck.y - outward.y * approach.IntoDeck.x);
                float core = Mathf.Min(available * .5f, seamProjection + 2.5f);
                foreach (var pair in chain)
                {
                    pair.Value.EndDistance = available; pair.Value.CoreDistance = core;
                    if (!approach.Distances.ContainsKey(pair.Key)) approach.Distances.Add(pair.Key, pair.Value);
                }
            }
        }

        static float BridgeArmStation(GeoRoad road, BridgeArmDistance arm, float x, float z)
        {
            float best = float.PositiveInfinity, station = 0, travelled = 0;
            for (int index = 1; index < road.points.Length; index++)
            {
                var a = road.points[index - 1]; var b = road.points[index];
                float dx = b.x - a.x, dz = b.z - a.z;
                float length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length <= .0001f) continue;
                float t = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / (length * length));
                float ex = x - a.x - dx * t, ez = z - a.z - dz * t;
                float squared = ex * ex + ez * ez;
                if (squared < best) { best = squared; station = travelled + t * length; }
                travelled += length;
            }
            return arm.Offset + (arm.Forward ? station : arm.Length - station);
        }

        static float BridgeTerminalDistanceSquared(BridgeApproach approach, float x, float z)
        {
            float dx = x - approach.End.x, dz = z - approach.End.z;
            float along = dx * approach.IntoDeck.x + dz * approach.IntoDeck.y;
            float lateral = dx * approach.IntoDeck.y - dz * approach.IntoDeck.x;
            float outside = Mathf.Max(0, Mathf.Abs(lateral) - approach.HalfWidth);
            return along * along + outside * outside;
        }

        static float BridgeTerminalSeparation(BridgeApproach first, BridgeApproach second)
        {
            float best = float.PositiveInfinity;
            foreach (float sign in new[] { -1f, 1f })
            {
                best = Mathf.Min(best, BridgeTerminalDistanceSquared(second,
                    first.End.x + first.IntoDeck.y * first.HalfWidth * sign,
                    first.End.z - first.IntoDeck.x * first.HalfWidth * sign));
                best = Mathf.Min(best, BridgeTerminalDistanceSquared(first,
                    second.End.x + second.IntoDeck.y * second.HalfWidth * sign,
                    second.End.z - second.IntoDeck.x * second.HalfWidth * sign));
            }
            return Mathf.Sqrt(best);
        }

        // Circumscribe the existing 32-sided JunctionCap circle so a bridge
        // edge does not fall just outside an inscribed disk at arbitrary angles.
        const float BridgeCapCircleScale = 1.004839f;

        internal bool HasBridgeApproach(GeoRoad road, float x, float z)
        {
            if (road == null) return false;
            var key = new Vector2Int(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
            if (bridgeApproachCells.TryGetValue(key, out var list))
                foreach (var approach in list)
                    if (approach.GroundLayer == road.layer && approach.Roads.Contains(road)) return true;
            return false;
        }

        internal float RoadSurfaceHeightForRoad(GeoRoad road, float x, float z)
        {
            if(AutomaticWorld && road!=null)return NativeProfileHeight(road,x,z);
            if(road!=null&&CircuitRoadSurfaces?.Covers(road.id)==true&&CircuitRoadSurfaces.TryHeight(x,z,out float authoredHeight))return authoredHeight;
            return TryEstimatedDeckHeight(road, x, z, out float height) ? height : RoadSurfaceHeightForRoadBase(road, x, z);
        }

        float RoadSurfaceHeightForRoadBase(GeoRoad road, float x, float z)
        {
            if (road != null && road.bridge) return EffectiveBridgeHeight(road, x, z);
            if (road == null || road.tunnel) return RoadSurfaceHeightCore(x, z, false);
            var key = new Vector2Int(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
            if (bridgeApproachCells.TryGetValue(key, out var list))
                foreach (var approach in list)
                    if (approach.GroundLayer == road.layer && approach.Roads.Contains(road))
                        return MatchBridgeApproach(x, z, road.layer, road, RoadSurfaceHeightOnLayerUnjoined(x, z, road.layer));
            // Do not assign a bridge profile to an unrelated road merely because
            // another road's bridge approach is closest to one edge vertex.
            return RoadSurfaceHeightCore(x, z, false);
        }

        internal float JunctionSurfaceHeight(string node, int layer, float x, float z)
        {
            BridgeApproach chosen = null;
            foreach (var approach in bridgeApproaches)
                if (approach.Node == node && approach.GroundLayer == layer &&
                    (chosen == null || approach.Bridge.width > chosen.Bridge.width)) chosen = approach;
            if (chosen == null) return RoadSurfaceHeight(x, z);
            return BridgeApproachPlane(chosen, x, z);
        }

        float MatchBridgeApproach(float x, float z, int layer, GeoRoad nearest, float oldHeight)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
            if (!bridgeApproachCells.TryGetValue(key, out var list)) return oldHeight;
            if (nearest == null)
            {
                // Explicit-layer callers must choose a road on THAT layer,
                // not whichever road happens to be nearest in another level.
                float best = float.PositiveInfinity;
                if (surfaceCells.TryGetValue(key, out var candidates)) foreach (var candidate in candidates)
                {
                    if (candidate.road.bridge || candidate.road.tunnel || candidate.road.layer != layer) continue;
                    foreach (var segment in candidate.segments)
                    {
                        float dx = segment.b.x - segment.a.x, dz = segment.b.z - segment.a.z;
                        float t = Mathf.Clamp01(((x - segment.a.x) * dx + (z - segment.a.z) * dz) / Mathf.Max(.001f, dx * dx + dz * dz));
                        float ex = x - segment.a.x - dx * t, ez = z - segment.a.z - dz * t;
                        float squared = ex * ex + ez * ez;
                        if (squared < best) { best = squared; nearest = candidate.road; }
                    }
                }
            }
            double weights = 0, heights = 0;
            float strength = 0, nearestSquared = float.PositiveInfinity;
            BridgeApproach closest = null;
            foreach (var approach in list)
            {
                if (approach.GroundLayer != layer || nearest == null || !approach.Distances.TryGetValue(nearest, out var arm)) continue;
                float dx = x - approach.End.x, dz = z - approach.End.z;
                float station = BridgeArmStation(nearest, arm, x, z);
                float alpha = 1 - MainGradeFade((station - arm.CoreDistance) / Mathf.Max(.001f, arm.EndDistance - arm.CoreDistance));
                if (alpha <= 0) continue;
                float squared = BridgeTerminalDistanceSquared(approach, x, z);
                double weight = alpha / ((double)squared + BridgeWeightLengthMetres * BridgeWeightLengthMetres);
                float plane = BridgeApproachPlane(approach, x, z);
                weights += weight; heights += weight * plane; strength = Mathf.Max(strength, alpha);
                if (squared < nearestSquared) { nearestSquared = squared; closest = approach; }
            }
            if (closest == null) return oldHeight;
            float preferred = (float)(heights / weights);
            float exactPlane = BridgeApproachPlane(closest, x, z);
            float exactStrength = 1 - MainGradeFade((Mathf.Sqrt(nearestSquared) - closest.PlaneBand) / closest.PlaneBand);
            preferred = Mathf.Lerp(preferred, exactPlane, exactStrength);
            return Mathf.Lerp(oldHeight, preferred, strength);
        }
    }
}
