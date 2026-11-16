using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GeoRegion
    {
        // Local gameplay height preference, not a new saved road schema. Source
        // points/XZ/widths/topology stay unchanged. Rebuilt with existing indexes;
        // all callers (ribbons, caps, terrain) keep using the SAME height field.
        // Only ordinary legacy at-grade roads participate. Explicit prepared
        // fields and circuits retain the old evaluator. Bridges/tunnels are never
        // continuation candidates; their existing emission branches are untouched.
        const float MainGradeThroughDot = -.8f; // Outward arms must differ by >=143 degrees.
        const float MainGradeAmbiguityScore = .5f; // Close scores keep the old blend.
        const float MainGradeNodeAlignmentMetres = .001f; // Check declared node, never a proximity join.
        const float MainGradeForeignSeparationMetres = 2f; // Smoothly end preference before unrelated roads.
        const int MainGradeMaximumContinuationRoads = 64; // Per-junction bounded source-topology walk.
        const int MainGradeMaximumArms = 16; // Complex junctions keep old behavior; never refuse the world.
        readonly Dictionary<Vector2Int, List<MainGradeJunction>> mainGradeCells =
            new Dictionary<Vector2Int, List<MainGradeJunction>>();

        sealed class MainGradeArm
        {
            internal GeoRoad Road;
            internal Vector3[] Points;
            internal float Length, EndGrade;
            internal Vector2 Outward;
        }

        sealed class MainGradeJunction
        {
            internal MainGradeArm First, Second;
            internal HashSet<GeoRoad> Members;
            internal int Layer;
            internal float InnerReach, OuterReach;
            internal Vector2 Axis;
            internal Vector3 Centre;
            internal float FirstStation, SecondStation, FirstAxisGrade, SecondAxisGrade;
        }

        // Smoothstep with zero first/second derivatives at both ends. Unlike a
        // flat junction plane, the preferred main profile keeps its input slopes.
        static float MainGradeFade(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value * (value * (value * 6 - 15) + 10);
        }

        static int MainGradeClass(string kind)
        {
            switch (kind)
            {
                case "motorway": case "motorway_link": return 8;
                case "trunk": case "trunk_link": return 7;
                case "primary": case "primary_link": return 6;
                case "secondary": case "secondary_link": return 5;
                case "tertiary": case "tertiary_link": return 4;
                case "unclassified": return 3;
                case "residential": case "living_street": return 2;
                case "service": return 1;
                default: return 0;
            }
        }

        static bool MainGradeRealName(GeoRoad first, GeoRoad second)
        {
            // The bundled Dee importer uses this literal when BOTH name/ref are
            // absent (tools/import_dee_valley.py). Equal placeholders are not
            // evidence of a continuous named main road.
            return !string.IsNullOrWhiteSpace(first.name) &&
                !string.Equals(first.name, "Country lane", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(first.name, first.kind, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(second.name, second.kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(first.name, second.name, StringComparison.OrdinalIgnoreCase);
        }

        static MainGradeArm MainGradeMakeArm(GeoRoad road, bool fromStart, float requestedLength)
        {
            var points = new List<Vector3>();
            int index = fromStart ? 0 : road.points.Length - 1;
            int direction = fromStart ? 1 : -1;
            points.Add(road.points[index]);
            float length = 0, endGrade = 0;
            Vector2 outward = Vector2.zero;
            for (int next = index + direction; next >= 0 && next < road.points.Length; next += direction)
            {
                var before = road.points[next - direction];
                var after = road.points[next];
                var delta = new Vector2(after.x - before.x, after.z - before.z);
                float span = delta.magnitude;
                if (span <= .0001f) continue; // Existing duplicate XZ contributes no directional evidence.
                if (outward == Vector2.zero) outward = delta / span;
                float take = Mathf.Min(span, requestedLength - length);
                points.Add(Vector3.Lerp(before, after, take / span));
                length += take;
                endGrade = (after.y - before.y) / span;
                if (length >= requestedLength) break;
            }
            if (length < 2 || points.Count < 2) return null; // Too short to prefer; retain old blend.
            return new MainGradeArm { Road = road, Points = points.ToArray(),
                Length = length,
                EndGrade = endGrade, Outward = outward };
        }

        void RebuildMainGradeJunctions()
        {
            mainGradeCells.Clear();
            if (circuit || roadWidthSchema != 0) return;
            foreach (var node in junctions)
            {
                var layers = new Dictionary<int, List<MainGradeArm>>();
                foreach (var road in new HashSet<GeoRoad>(node.Value))
                {
                    if (IsEffectiveBridgeDeck(road) || road.tunnel || road.points == null || road.points.Length < 2) continue;
                    if (!layers.TryGetValue(road.layer, out var arms))
                        layers.Add(road.layer, arms = new List<MainGradeArm>());
                    // Width-derived local span in metres; no grade/height cap.
                    float reach = Mathf.Clamp(road.width * 2 + 6, 12, 32);
                    if (road.start == node.Key)
                    { var arm = MainGradeMakeArm(road, true, reach); if (arm != null) arms.Add(arm); }
                    if (road.end == node.Key)
                    { var arm = MainGradeMakeArm(road, false, reach); if (arm != null) arms.Add(arm); }
                }
                foreach (var layer in layers)
                {
                    var arms = layer.Value;
                    if (arms.Count < 3 || arms.Count > MainGradeMaximumArms) continue;
                    MainGradeArm first = null, second = null;
                    float best = float.NegativeInfinity, runnerUp = float.NegativeInfinity;
                    for (int a = 0; a < arms.Count; a++)
                    for (int b = a + 1; b < arms.Count; b++)
                    {
                        if (ReferenceEquals(arms[a].Road, arms[b].Road)) continue;
                        float dot = Vector2.Dot(arms[a].Outward, arms[b].Outward);
                        if (dot > MainGradeThroughDot) continue;
                        // Prefer a continuous high-class pair; a real matching
                        // name breaks class ties, then straightness. No place IDs.
                        float score = Math.Min(MainGradeClass(arms[a].Road.kind), MainGradeClass(arms[b].Road.kind)) * 100
                            + (MainGradeRealName(arms[a].Road, arms[b].Road) ? 20 : 0) - dot * 10;
                        if (score > best)
                        { runnerUp = best; best = score; first = arms[a]; second = arms[b]; }
                        else runnerUp = Mathf.Max(runnerUp, score);
                    }
                    if (first == null || best - runnerUp <= MainGradeAmbiguityScore) continue;
                    Vector3 centre = first.Points[0];
                    bool aligned = true;
                    foreach (var arm in arms)
                        if (new Vector2(arm.Points[0].x - centre.x, arm.Points[0].z - centre.z).magnitude > MainGradeNodeAlignmentMetres)
                            aligned = false;
                    if (!aligned) continue; // Same ID with incompatible positions: no invented join.
                    // One continuous through-axis avoids a nearest-arm station
                    // switch on curved joins. This does not move road XZ. Match
                    // the endpoint height slopes in that SAME axis coordinate.
                    var axis = (second.Outward - first.Outward).normalized;
                    Vector3 firstEnd = first.Points[first.Points.Length - 1];
                    Vector3 secondEnd = second.Points[second.Points.Length - 1];
                    float firstStation = Vector2.Dot(new Vector2(firstEnd.x - centre.x, firstEnd.z - centre.z), axis);
                    float secondStation = Vector2.Dot(new Vector2(secondEnd.x - centre.x, secondEnd.z - centre.z), axis);
                    Vector3 firstPrevious = first.Points[first.Points.Length - 2];
                    Vector3 secondPrevious = second.Points[second.Points.Length - 2];
                    float firstDirection = Vector2.Dot(new Vector2(firstEnd.x - firstPrevious.x, firstEnd.z - firstPrevious.z).normalized, axis);
                    float secondDirection = Vector2.Dot(new Vector2(secondEnd.x - secondPrevious.x, secondEnd.z - secondPrevious.z).normalized, axis);
                    // A loop/reversal inside the local approach is not a simple
                    // continuation; keep the old blend rather than divide by a
                    // near-perpendicular tangent. This is not a world refusal.
                    if (firstStation >= -1 || secondStation <= 1 || firstDirection >= -.3f || secondDirection <= .3f) continue;
                    float halfWidth = Mathf.Max(first.Road.width, second.Road.width) * .5f;
                    // Hold the actual main pavement width, then roll the side
                    // approach over three nominal widths (12..32 m). A short
                    // six-metre recovery made an ordinary side grade too abrupt.
                    // This is a transition length, not a forced grade cap.
                    var junction = new MainGradeJunction { First = first, Second = second,
                        Members = new HashSet<GeoRoad>(), Layer = layer.Key,
                        Centre = centre, Axis = axis, FirstStation = firstStation, SecondStation = secondStation,
                        FirstAxisGrade = first.EndGrade / firstDirection, SecondAxisGrade = second.EndGrade / secondDirection,
                        InnerReach = halfWidth, OuterReach = halfWidth + Mathf.Clamp(halfWidth * 6, 12, 32) };
                    foreach (var arm in arms) junction.Members.Add(arm.Road);
                    float radius = Mathf.Max(first.Length, second.Length) + junction.OuterReach;
                    if (!ExtendMainGradeMembers(junction, node.Key, radius)) continue;
                    for (int z = Mathf.FloorToInt((centre.z - radius) / Cell); z <= Mathf.FloorToInt((centre.z + radius) / Cell); z++)
                    for (int x = Mathf.FloorToInt((centre.x - radius) / Cell); x <= Mathf.FloorToInt((centre.x + radius) / Cell); x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!mainGradeCells.TryGetValue(key, out var entries)) mainGradeCells.Add(key, entries = new List<MainGradeJunction>());
                        entries.Add(junction);
                    }
                }
            }
        }

        // A short incident span can end halfway through the rolling approach.
        // Its genuine degree-two continuation is still the SAME approach, not
        // foreign geometry. Otherwise the foreign fade cuts a narrow depression
        // at that artificial span boundary (actual Hazelwood regression).
        // Only endpoint topology is followed: no names, XY-nearest joins or
        // layer/bridge/tunnel crossings. This changes cache membership only.
        bool ExtendMainGradeMembers(MainGradeJunction junction, string originNode, float maximumDistance)
        {
            var incident = new List<GeoRoad>(junction.Members);
            int visitedRoads = incident.Count;
            foreach (var firstRoad in incident)
            {
                bool isMainArm = ReferenceEquals(firstRoad, junction.First.Road) || ReferenceEquals(firstRoad, junction.Second.Road);
                bool fromStart = firstRoad.start == originNode;
                if (fromStart && firstRoad.end == originNode) continue; // Loop, not a unique onward arm.
                GeoRoad current = firstRoad;
                string farNode = fromStart ? current.end : current.start;
                Vector3 farPoint = current.points[fromStart ? current.points.Length - 1 : 0];
                float distance = MainGradeHorizontalLength(current, maximumDistance);
                while (distance < maximumDistance && junctions.TryGetValue(farNode, out var connected))
                {
                    GeoRoad next = null;
                    bool currentSeen = false, branch = false;
                    foreach (var road in connected)
                    {
                        if (road.layer != junction.Layer) continue;
                        if (ReferenceEquals(road, current)) { currentSeen = true; continue; }
                        if (next != null && !ReferenceEquals(next, road)) { branch = true; break; }
                        next = road;
                    }
                    // A short side connector cannot independently satisfy TWO
                    // nearby main-road preferences. Keep the original field at
                    // this junction if a real branch is reached before the
                    // existing transverse roll finishes. This is a conservative
                    // local fallback, not a slope cap.
                    // A tightly spaced MAIN connector can also terminate at
                    // another true branch before a side-road roll has room.
                    // Independent preferences then concentrate their height
                    // difference into the short main-axis end collar. Retain
                    // the original common field at both affected junctions,
                    // using source length/width rather than any grade limit.
                    float required = isMainArm ? junction.OuterReach - junction.InnerReach : junction.OuterReach;
                    if (currentSeen && branch && distance < required &&
                        (!isMainArm || MainGradeSideCrossesEndCollar(junction, originNode))) return false;
                    if (!currentSeen || branch || next == null || IsEffectiveBridgeDeck(next) || next.tunnel ||
                        next.points == null || next.points.Length < 2) break;
                    bool nextFromStart = next.start == farNode;
                    if (nextFromStart && next.end == farNode) break;
                    Vector3 nearPoint = next.points[nextFromStart ? 0 : next.points.Length - 1];
                    if (new Vector2(nearPoint.x - farPoint.x, nearPoint.z - farPoint.z).magnitude > MainGradeNodeAlignmentMetres) break;
                    if (junction.Members.Contains(next)) break; // Cycle/already-covered arm.
                    // A truncated walk would reintroduce a false foreign edge.
                    // Keep the old evaluator for THIS junction on budget exhaustion.
                    if (++visitedRoads > MainGradeMaximumContinuationRoads) return false;
                    junction.Members.Add(next);
                    distance += MainGradeHorizontalLength(next, maximumDistance - distance);
                    current = next;
                    farNode = nextFromStart ? next.end : next.start;
                    farPoint = next.points[nextFromStart ? next.points.Length - 1 : 0];
                }
            }
            return true;
        }

        // A short main link is conflicting only if an actual source-connected
        // SIDE approach crosses its longitudinal fade inside the transverse roll.
        // A short bridge approach ending before that collar is not conflicting.
        // Walk the same bounded, same-layer degree-two source topology as above;
        // never infer a join from XY proximity or road names.
        bool MainGradeSideCrossesEndCollar(MainGradeJunction junction, string originNode)
        {
            float maximumDistance = Mathf.Max(junction.First.Length, junction.Second.Length) + junction.OuterReach;
            foreach (var firstRoad in new List<GeoRoad>(junction.Members))
            {
                if (ReferenceEquals(firstRoad, junction.First.Road) || ReferenceEquals(firstRoad, junction.Second.Road) ||
                    firstRoad.start != originNode && firstRoad.end != originNode) continue;
                bool forward = firstRoad.start == originNode;
                GeoRoad road = firstRoad;
                float travelled = 0;
                var visited = new HashSet<GeoRoad>();
                while (road != null && travelled < maximumDistance)
                {
                    if (!visited.Add(road) || visited.Count > MainGradeMaximumContinuationRoads) break;
                    for (int index = 1; index < road.points.Length; index++)
                    {
                        var a = road.points[forward ? index - 1 : road.points.Length - index];
                        var b = road.points[forward ? index : road.points.Length - index - 1];
                        float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                        if (length <= .0001f) continue;
                        b = Vector3.Lerp(a, b, Mathf.Min(1, (maximumDistance - travelled) / length));
                        float before = Vector2.Dot(new Vector2(a.x - junction.Centre.x, a.z - junction.Centre.z), junction.Axis);
                        float after = Vector2.Dot(new Vector2(b.x - junction.Centre.x, b.z - junction.Centre.z), junction.Axis);
                        if (Mathf.Abs(after - before) > .0001f)
                        foreach (float collar in new[] { junction.FirstStation * .875f, junction.SecondStation * .875f })
                        {
                            // Midpoint of the existing75–100% fade collar.
                            float t = (collar - before) / (after - before);
                            if (t < 0 || t > 1) continue;
                            var point = Vector3.Lerp(a, b, t);
                            float lateral = Mathf.Min(MainGradeDistance(junction.First, point.x, point.z),
                                MainGradeDistance(junction.Second, point.x, point.z));
                            if (lateral > junction.InnerReach && lateral < junction.OuterReach) return true;
                        }
                        travelled += length;
                        if (travelled >= maximumDistance) break;
                    }
                    string node = forward ? road.end : road.start;
                    if (!junctions.TryGetValue(node, out var connected)) break;
                    GeoRoad next = null; bool branch = false;
                    foreach (var candidate in connected)
                    {
                        if (candidate.layer != junction.Layer || ReferenceEquals(candidate, road)) continue;
                        if (next != null && !ReferenceEquals(next, candidate)) { branch = true; break; }
                        next = candidate;
                    }
                    if (branch || next == null || IsEffectiveBridgeDeck(next) || next.tunnel || next.points == null || next.points.Length < 2) break;
                    bool nextForward = next.start == node;
                    var far = road.points[forward ? road.points.Length - 1 : 0];
                    var near = next.points[nextForward ? 0 : next.points.Length - 1];
                    if (new Vector2(far.x - near.x, far.z - near.z).magnitude > MainGradeNodeAlignmentMetres) break;
                    road = next; forward = nextForward;
                }
            }
            return false;
        }

        static float MainGradeHorizontalLength(GeoRoad road, float stopAfterMetres)
        {
            float distance = 0;
            for (int index = 1; index < road.points.Length; index++)
            {
                float dx = road.points[index].x - road.points[index - 1].x;
                float dz = road.points[index].z - road.points[index - 1].z;
                distance += Mathf.Sqrt(dx * dx + dz * dz);
                if (distance >= stopAfterMetres) break;
            }
            return distance;
        }

        static float MainGradeDistance(MainGradeArm arm, float x, float z)
        {
            float best = float.PositiveInfinity;
            for (int index = 1; index < arm.Points.Length; index++)
            {
                var before = arm.Points[index - 1]; var after = arm.Points[index];
                float dx = after.x - before.x, dz = after.z - before.z;
                float along = Mathf.Clamp01(((x - before.x) * dx + (z - before.z) * dz) / (dx * dx + dz * dz));
                float ex = x - (before.x + dx * along), ez = z - (before.z + dz * along);
                best = Mathf.Min(best, ex * ex + ez * ez);
            }
            return Mathf.Sqrt(best);
        }

        float PreferMainJunctionGrade(float x, float z, int layer, float oldHeight)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
            if (!mainGradeCells.TryGetValue(key, out var junctionsHere)) return oldHeight;
            float total = 0, preferred = 0;
            foreach (var junction in junctionsHere)
            {
                if (junction.Layer != layer) continue;
                float station = Vector2.Dot(new Vector2(x - junction.Centre.x, z - junction.Centre.z), junction.Axis);
                float sideLength = station < 0 ? -junction.FirstStation : junction.SecondStation;
                float lateral = Mathf.Min(MainGradeDistance(junction.First, x, z), MainGradeDistance(junction.Second, x, z));
                float alongWeight = 1 - MainGradeFade((Mathf.Abs(station) / sideLength - .75f) / .25f);
                float acrossWeight = 1 - MainGradeFade((lateral - junction.InnerReach) / (junction.OuterReach - junction.InnerReach));
                float weight = alongWeight * acrossWeight;
                if (weight <= 0) continue;

                // A nearby unrelated road cannot acquire this node's preference.
                // Fade to the original field before a foreign-road Voronoi edge,
                // avoiding a hard nearest-road switch across verge geometry.
                float memberDistance = float.PositiveInfinity, foreignDistance = float.PositiveInfinity;
                if (surfaceCells.TryGetValue(key, out var candidates))
                foreach (var candidate in candidates)
                {
                    if (IsEffectiveBridgeDeck(candidate.road) || candidate.road.tunnel || candidate.road.layer != layer) continue;
                    foreach (var segment in candidate.segments)
                    {
                        float dx = segment.b.x - segment.a.x, dz = segment.b.z - segment.a.z;
                        float t = Mathf.Clamp01(((x - segment.a.x) * dx + (z - segment.a.z) * dz) / Mathf.Max(.001f, dx * dx + dz * dz));
                        float ex = x - (segment.a.x + dx * t), ez = z - (segment.a.z + dz * t);
                        float distance = Mathf.Sqrt(ex * ex + ez * ez);
                        if (junction.Members.Contains(candidate.road)) memberDistance = Mathf.Min(memberDistance, distance);
                        else foreignDistance = Mathf.Min(foreignDistance, distance);
                    }
                }
                if (!float.IsPositiveInfinity(foreignDistance))
                    weight *= MainGradeFade((foreignDistance - memberDistance) / MainGradeForeignSeparationMetres);
                if (weight <= 0) continue;

                // Cubic Hermite interpolates actual main approach heights AND
                // slopes. It never forces zero grade or alters the source points.
                float length = junction.SecondStation - junction.FirstStation;
                float u = Mathf.Clamp01((station - junction.FirstStation) / length);
                float u2 = u * u, u3 = u2 * u;
                float startY = junction.First.Points[junction.First.Points.Length - 1].y;
                float endY = junction.Second.Points[junction.Second.Points.Length - 1].y;
                float height = (2 * u3 - 3 * u2 + 1) * startY
                    + (u3 - 2 * u2 + u) * length * junction.FirstAxisGrade
                    + (-2 * u3 + 3 * u2) * endY
                    + (u3 - u2) * length * junction.SecondAxisGrade;
                total += weight; preferred += weight * height;
            }
            if (total <= 0) return oldHeight;
            return Mathf.Lerp(oldHeight, preferred / total, Mathf.Clamp01(total));
        }
    }
}
