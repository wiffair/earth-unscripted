using System;
using System.Threading;
using RoadRush.FunnyAnimals.Admission;
namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    // Conservative validation for bounded source polygons before reusing the
    // existing full-edge/containment predicate. Near-touching nonadjacent edges
    // refuse; no invalid polygon is repaired into a new source footprint.
    public static class PolygonFootprint800
    {
        // Matches the existing SafetyIndex polygon work limit. Refusing a more
        // detailed polygon is distinct from simplifying it into different source
        // geometry. The checked source's largest actual area has 391 vertices.
        public const int MaximumPredicateVertices = 512;

        // Squared metres: edges/nonadjacent boundaries within one micrometre
        // refuse rather than relying on a poorly conditioned containment test.
        const double MinimumSeparationSquaredMetres = 1e-12;

        // The shoelace accumulator is twice the signed area, in square metres.
        // This pre-existing refusal threshold is not a terrain height tolerance.
        const double MinimumAbsoluteTwiceAreaSquareMetres = 1e-8;

        /// <summary>
        /// Copies a finite simple XZ polygon into an owned hazard shape. A single
        /// repeated closing point is accepted. Degenerate, nearly touching or
        /// self-crossing geometry returns false without repair. Cancellation
        /// throws; work is bounded by MaximumPredicateVertices squared.
        /// </summary>
        /// <remarks>
        /// Coordinates are source/world metres in the reviewed identity frame.
        /// The input remains owned by the caller; the returned shape owns a copy.
        /// Validation is pure managed code and may run on the preparation worker.
        /// Existing Geometry2 predicates deliberately bias near-boundary cases
        /// toward refusal; this is not an exact native-physics equivalence proof.
        /// </remarks>
        public static bool TryCreate(P[] raw, CancellationToken cancellation, out Shape shape)
        {
            shape = null;
            if (raw == null || raw.Length < 3 || raw.Length > MaximumPredicateVertices)
                return false;
            int n = raw.Length;
            if (raw[0].X == raw[n - 1].X && raw[0].Z == raw[n - 1].Z)
                n--;
            if (n < 3)
                return false;
            var p = new P[n];
            double twiceArea = 0;
            for (int i = 0; i < n; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var a = raw[i];
                var b = raw[(i + 1) % n];
                if (double.IsNaN(a.X) || double.IsInfinity(a.X) || double.IsNaN(a.Z)
                    || double.IsInfinity(a.Z)
                    || Math.Abs(a.X) > SourceScope800.MaximumSourceCoordinateMagnitudeMetres
                    || Math.Abs(a.Z) > SourceScope800.MaximumSourceCoordinateMagnitudeMetres
                    || Geometry2.PointSegment(a, b, b) <= MinimumSeparationSquaredMetres)
                    return false;
                p[i] = a;
                twiceArea += a.X * b.Z - b.X * a.Z;
            }
            if (double.IsNaN(twiceArea) || double.IsInfinity(twiceArea)
                || Math.Abs(twiceArea) <= MinimumAbsoluteTwiceAreaSquareMetres)
                return false;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (j == i + 1 || i == 0 && j == n - 1)
                        continue;
                    if (Geometry2.SegmentDistance(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n])
                        <= MinimumSeparationSquaredMetres)
                        return false;
                }
            shape = Shape.Poly(p);
            return true;
        }
    }
}
