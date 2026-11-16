using System;
using EarthUnscripted.FunnyAnimals.Admission;
namespace EarthUnscripted.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Describes the already reviewed 800 metre source subset in the identity
    /// Unity world frame: positive X east, positive Z north, origin at the source anchor.
    /// This is a consumer policy, not a source or physical-ground capability.
    /// </summary>
    /// <remarks>
    /// A caller must match ReviewedDigest to the actual issued ground lease.
    /// These pure checks never issue a lease or prove walking support. Constants
    /// preserve the functional-v4 limits; they are not new terrain calibration.
    /// All operations are read-only and use source coordinates in metres.
    /// </remarks>
    public static class SourceScope800
    {
        // Exact typed plan scope digest. It does not identify every source
        // feature or establish admission of the surrounding 3 kilometre capture.
        public const string ReviewedDigest = "9955e93cf991f88ebc3079064de2bc7db85fe82724b746f821b6c01e66b5ef55";

        public const double HalfExtentMetres = 400;
        public const double MinimumQueryRadiusMetres = .01;
        public const double MaximumQueryRadiusMetres = 1.5;
        public const double MaximumCorridorLengthMetres = 8;

        // Raw source may extend beyond the admitted rectangle. This separate
        // input sanity bound rejects implausible/nonfinite coordinate payloads.
        public const double MaximumSourceCoordinateMagnitudeMetres = 10000;

        // OriginalWayRaw13Converter accepts at most 4096 area vertices. Later
        // full-polygon predicates deliberately have their own smaller work cap.
        public const int MaximumSourcePolygonVertices = 4096;

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>
        /// Returns whether the entire closed capsule fits the reviewed rectangle
        /// and the existing radius/corridor limits. Invalid input returns false.
        /// Touching the rectangle boundary is allowed; crossing it is refused.
        /// </summary>
        public static bool Allows(P a, P b, double radius)
        {
            if (!Finite(a.X) || !Finite(a.Z) || !Finite(b.X) || !Finite(b.Z) || !Finite(radius)
                || radius < MinimumQueryRadiusMetres || radius > MaximumQueryRadiusMetres)
                return false;
            double dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz <= MaximumCorridorLengthMetres * MaximumCorridorLengthMetres
                && Math.Min(a.X, b.X) - radius >= -HalfExtentMetres
                && Math.Max(a.X, b.X) + radius <= HalfExtentMetres
                && Math.Min(a.Z, b.Z) - radius >= -HalfExtentMetres
                && Math.Max(a.Z, b.Z) + radius <= HalfExtentMetres;
        }

        /// <summary>
        /// Conservatively keeps a whole source polygon unless its finite bounds
        /// prove it cannot intersect the reviewed rectangle. Invalid or oversized
        /// input throws; an unknown polygon must not silently become empty space.
        /// </summary>
        /// <remarks>
        /// The box is only a rejection test. A true result still requires the
        /// original polygon's edges and containment predicates. No clipping,
        /// geometry repair or bounding-box replacement takes place here.
        /// </remarks>
        public static bool MayAffect(P[] polygon)
        {
            if (polygon == null || polygon.Length < 3 || polygon.Length > MaximumSourcePolygonVertices)
                throw new InvalidOperationException("Complete bounded source polygon required");
            double x0 = double.PositiveInfinity, z0 = x0, x1 = double.NegativeInfinity, z1 = x1;
            foreach (var p in polygon)
            {
                if (!Finite(p.X) || !Finite(p.Z)
                    || Math.Abs(p.X) > MaximumSourceCoordinateMagnitudeMetres
                    || Math.Abs(p.Z) > MaximumSourceCoordinateMagnitudeMetres)
                    throw new InvalidOperationException("Finite source polygon required");
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }
            return x0 <= HalfExtentMetres && x1 >= -HalfExtentMetres
                && z0 <= HalfExtentMetres && z1 >= -HalfExtentMetres;
        }
    }
}
