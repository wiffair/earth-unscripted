using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RoadRush.Core;

namespace RoadRush
{
    public sealed partial class RoutePath
    {
        // Immutable per-route source ownership; no scene/mesh/terrain authority.
        // Binding is performed on the stored CENTRE path, before a lane offset.
        readonly RoadOwnership routeRoadOwnership;
        public string RoadOwnershipDiagnostic => routeRoadOwnership?.Diagnostic ?? "Not a mapped-road route";
        // Read-only diagnostic; null explicitly means the legacy fallback is used.
        public string SourceRoadIdAt(float distance)
        {
            if (routeRoadOwnership == null) return null;
            return RouteRoad(distance, out _)?.id;
        }

        internal RoutePath(GeoRegion region, string[] sourceRoadIds, bool reverse = false)
            : this(region, reverse, sourceRoadIds) { }

        GeoRoad RouteRoad(float distance, out Vector3 centre)
        {
            float bounded = IsClosed ? DrivingMath.Wrap(distance, Length) : Mathf.Clamp(distance, 0, Length);
            int segment = Segment(bounded);
            centre = Vector3.Lerp(Points[segment], Points[segment + 1],
                (bounded - distances[segment]) / (distances[segment + 1] - distances[segment]));
            return routeRoadOwnership.Get(segment);
        }

        /// <summary>
        /// A small reference index over admitted source polylines. It establishes
        /// source-centre segment identity only; it does not admit vehicle support.
        /// Missing/overlaid ambiguous ownership is explicitly degraded. Geometry
        /// then retains stored route Y rather than guessing a crossing road level;
        /// metadata alone preserves the previous centre-point nearest fallback.
        /// </summary>
        sealed class RoadOwnership
        {
            const float IndexCellMetres = 64;
            // Route authoring/projection allowance, not the finished seam budget
            // or a surveyed source-accuracy assertion. No source point is moved.
            const double LineageToleranceMetres = .05;
            const int MaximumSourceSegments = 262144;
            const int MaximumRouteSegments = 65536;
            const int MaximumMemberships = 2000000;
            const int MaximumCellsPerSourceSegment = 4096;
            readonly GeoRoad[] owners;
            readonly string[] failures;
            internal readonly string Diagnostic;

            readonly struct SourceSegment
            {
                internal readonly GeoRoad Road;
                internal readonly Vector3 Start, End;
                internal SourceSegment(GeoRoad road, Vector3 start, Vector3 end)
                { Road = road; Start = start; End = end; }
            }

            RoadOwnership(string reason)
            { Diagnostic = "Degraded mapped route: " + reason + "; stored route Y / legacy centre metadata"; }

            internal static RoadOwnership Create(GeoRegion region, Vector3[] routePoints, string[] explicitIds, bool reversed)
            {
                try { return new RoadOwnership(region, routePoints, explicitIds, reversed); }
                catch (InvalidDataException error)
                {
                    // New optional ownership capacity/lineage diagnostics must not
                    // turn a previously playable ordinary route into a boot refusal.
                    return new RoadOwnership(error.Message);
                }
            }

            RoadOwnership(GeoRegion region, Vector3[] routePoints, string[] explicitIds, bool reversed)
            {
                int count = routePoints.Length - 1;
                if (count < 1 || count > MaximumRouteSegments)
                    throw new InvalidDataException("Mapped route source segment budget");
                if (explicitIds != null && explicitIds.Length != count)
                    throw new InvalidDataException("Mapped route source identity count");
                owners = new GeoRoad[count]; failures = new string[count];
                var byId = new Dictionary<string, GeoRoad>(StringComparer.Ordinal);
                var cells = new Dictionary<Vector2Int, List<SourceSegment>>();
                int segments = 0, memberships = 0;
                foreach (var road in region.roads)
                {
                    if (road.points == null || road.points.Length < 2) continue;
                    if (road.id == null || byId.ContainsKey(road.id))
                        throw new InvalidDataException("Mapped route duplicate/missing source road ID");
                    byId.Add(road.id, road);
                    for (int index = 1; index < road.points.Length; index++)
                    {
                        if (++segments > MaximumSourceSegments)
                            throw new InvalidDataException("Mapped route source segment budget");
                        Vector3 a = road.points[index - 1], b = road.points[index];
                        if (!Finite(a) || !Finite(b)) throw new InvalidDataException("Nonfinite mapped source road");
                        int west = Cell(Mathf.Min(a.x, b.x) - (float)LineageToleranceMetres);
                        int east = Cell(Mathf.Max(a.x, b.x) + (float)LineageToleranceMetres);
                        int south = Cell(Mathf.Min(a.z, b.z) - (float)LineageToleranceMetres);
                        int north = Cell(Mathf.Max(a.z, b.z) + (float)LineageToleranceMetres);
                        long visits = (long)(east - west + 1) * (north - south + 1);
                        if (visits > MaximumCellsPerSourceSegment || visits + memberships > MaximumMemberships)
                            throw new InvalidDataException("Mapped route source index budget");
                        memberships += (int)visits;
                        var source = new SourceSegment(road, a, b);
                        for (int z = south; z <= north; z++) for (int x = west; x <= east; x++)
                        {
                            var key = new Vector2Int(x, z);
                            if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<SourceSegment>());
                            list.Add(source);
                        }
                    }
                }
                int missing = 0, ambiguous = 0;
                for (int index = 0; index < count; index++)
                {
                    var a = routePoints[index]; var b = routePoints[index + 1];
                    if (!Finite(a) || !Finite(b)) throw new InvalidDataException("Nonfinite mapped route");
                    string requested = explicitIds == null ? null : explicitIds[reversed ? count - index - 1 : index];
                    if (explicitIds != null)
                    {
                        if (requested == null || !byId.TryGetValue(requested, out var road) || !RoadSupports(road, a, b))
                        { missing++; failures[index] = "Explicit source ID/span mismatch"; continue; }
                        owners[index] = road; continue;
                    }
                    GeoRoad match = null; bool multiple = false;
                    // A supporting finite source span MUST contain the first
                    // endpoint; no broad route-box or nearest-road search needed.
                    if (cells.TryGetValue(new Vector2Int(Cell(a.x), Cell(a.z)), out var candidates))
                    {
                        foreach (var candidate in candidates)
                        {
                            if (!Supports(candidate.Start, candidate.End, a, b)) continue;
                            if (match != null && !ReferenceEquals(match, candidate.Road)) { multiple = true; break; }
                            match = candidate.Road;
                        }
                    }
                    if (multiple) { ambiguous++; failures[index] = "Ambiguous collinear source roads"; }
                    else if (match == null) { missing++; failures[index] = "No finite collinear source road"; }
                    else owners[index] = match;
                }
                Diagnostic = "Mapped route owners " + (count - missing - ambiguous) + "/" + count +
                    "; missing=" + missing + "; ambiguous=" + ambiguous +
                    (missing + ambiguous == 0 ? "" : "; degraded: stored route Y / legacy centre metadata");
            }

            static int Cell(float value) => Mathf.FloorToInt(value / IndexCellMetres);
            static bool Finite(Vector3 point) =>
                !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.z);

            static bool RoadSupports(GeoRoad road, Vector3 a, Vector3 b)
            {
                for (int i = 1; i < road.points.Length; i++)
                    if (Supports(road.points[i - 1], road.points[i], a, b)) return true;
                return false;
            }

            // Endpoint distances to the SAME finite straight source span establish
            // both support and tangent (except harmless sub-5mm zero-length spans).
            // Source Y is deliberately unused: coarse heights are not a layer guess.
            static bool Supports(Vector3 start, Vector3 end, Vector3 a, Vector3 b)
            {
                double dx = (double)end.x - start.x, dz = (double)end.z - start.z;
                double square = dx * dx + dz * dz;
                if (square == 0) return false;
                return OnSpan(a) && OnSpan(b);
                bool OnSpan(Vector3 point)
                {
                    double along = (((double)point.x - start.x) * dx + ((double)point.z - start.z) * dz) / square;
                    along = Math.Max(0, Math.Min(1, along));
                    double errorX = point.x - (start.x + along * dx), errorZ = point.z - (start.z + along * dz);
                    return errorX * errorX + errorZ * errorZ <= LineageToleranceMetres * LineageToleranceMetres;
                }
            }

            internal GeoRoad Get(int segment) => owners == null ? null : owners[segment];
        }
    }
}
