using System;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GeoRegion
    {
        /// <summary>
        /// Loose gameplay classification tolerance in metres. It accommodates
        /// suspension/grade and a tyre-height reference measured away from body XZ.
        /// This is NOT a mesh seam, source accuracy or physical collision tolerance.
        /// </summary>
        public const float VehicleRoadHeightToleranceMetres = .9f;
        const float VehicleRoadVerticalScoreWeight = 2;

        public readonly struct VehicleRoadProjection
        {
            public readonly RoadProjection Source;
            public readonly float SurfaceHeight, SurfaceHeightGap;
            public GeoRoad Road => Source.Road;
            public float EdgeDistance => Source.EdgeDistance;
            public bool HasPlausibleHeight => Road != null && SurfaceHeightGap <= VehicleRoadHeightToleranceMetres;
            public bool OnRoad => HasPlausibleHeight && EdgeDistance < 0;

            internal VehicleRoadProjection(RoadProjection source, float surfaceHeight, float surfaceHeightGap)
            { Source = source; SurfaceHeight = surfaceHeight; SurfaceHeightGap = surfaceHeightGap; }
        }

        /// <summary>
        /// Vehicle-only physical-level classification using the existing bounded
        /// road candidate cells. Body position is in the same local map frame used
        /// by legacy ProjectRoad. SupportHeightY is mean actual grounded tyre Y,
        /// or body Y minus the actual vehicle's loaded rest-root height.
        /// No source/physics state is changed; no route owner or altitude-nearest
        /// global map query is introduced. Source.Point remains the source-centre
        /// projection; SurfaceHeight is the named final surface at BODY XZ.
        /// </summary>
        public VehicleRoadProjection ProjectRoadForVehicle(Vector3 bodyPosition, float supportHeightY)
        {
            var none = new RoadProjection(null, bodyPosition, float.MaxValue, 0, 0);
            if (!FiniteVehicleCoordinate(bodyPosition.x) || !FiniteVehicleCoordinate(bodyPosition.z) ||
                !FiniteVehicleCoordinate(supportHeightY))
                return new VehicleRoadProjection(none, 0, float.MaxValue);
            var key = new Vector2Int(Mathf.FloorToInt(bodyPosition.x / Cell), Mathf.FloorToInt(bodyPosition.z / Cell));
            if (!cells.TryGetValue(key, out var candidates))
                return new VehicleRoadProjection(none, 0, float.MaxValue);

            RoadProjection best = none;
            float bestSurface = 0, bestGap = float.MaxValue, bestScore = float.MaxValue;
            bool bestPlausible = false;
            // Cell memberships preserve source-road insertion order. Several
            // adjacent segments can therefore name the same road. Its final
            // surface at this query's fixed XZ is identical for each occurrence;
            // scan the authoritative profile once per run, not per segment.
            // Locals deliberately expire with the query: no stale terrain,
            // cross-road ID reuse, allocation or shared mutable cache.
            GeoRoad heightRoad = null;
            float roadSurface = 0;
            bool haveRoadSurface = false;
            foreach (var candidate in candidates)
            {
                Vector3 delta = candidate.b - candidate.a;
                float horizontalSquare = delta.x * delta.x + delta.z * delta.z;
                float fraction = Mathf.Clamp01(((bodyPosition.x - candidate.a.x) * delta.x +
                    (bodyPosition.z - candidate.a.z) * delta.z) / Mathf.Max(.001f, horizontalSquare));
                Vector3 centre = Vector3.Lerp(candidate.a, candidate.b, fraction);
                float separation = Vector2.Distance(new Vector2(bodyPosition.x, bodyPosition.z), new Vector2(centre.x, centre.z));
                float sourceDistance = candidate.start + candidate.length * fraction;
                float width = EffectiveWidth(candidate.road, sourceDistance);
                if (!haveRoadSurface || !ReferenceEquals(heightRoad, candidate.road))
                {
                    heightRoad = candidate.road;
                    roadSurface = RoadSurfaceHeightForRoad(heightRoad, bodyPosition.x, bodyPosition.z);
                    haveRoadSurface = true;
                }
                float surface = roadSurface;
                if (!FiniteVehicleCoordinate(surface)) continue;
                float gap = Mathf.Abs(supportHeightY - surface);
                bool plausible = gap <= VehicleRoadHeightToleranceMetres;
                // Preserve the legacy width-aware preference on a flat level;
                // vertical distance breaks competing-level ties. A plausible
                // level always wins over an impossible deck eight metres away,
                // even if that remote road is exceptionally wide.
                float score = separation - width * .5f + gap * VehicleRoadVerticalScoreWeight;
                if (best.Road != null)
                {
                    if (bestPlausible && !plausible) continue;
                    if (bestPlausible == plausible && score >= bestScore) continue;
                }
                best = new RoadProjection(candidate.road, centre, separation, sourceDistance, width);
                bestSurface = surface; bestGap = gap; bestScore = score; bestPlausible = plausible;
            }
            return new VehicleRoadProjection(best, bestSurface, bestGap);
        }

        static bool FiniteVehicleCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
