using System;
using RoadRush.FunnyAnimals.VehicleCaptureProposal;

namespace RoadRush.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Pure geometry helpers; positions/bounds are metres. Reuses unchanged conditioned
    /// v5 arithmetic. No native objects, authority, material or source-completeness claim.
    /// </summary>
    internal static class PresentationGeometryMath
    {
        internal const double BoundsPaddingMetres = 0.0001; // numeric witness policy, not native proof
        internal const double MaximumRootRadiusMetres = 20; // refusal, never geometry clamp

        internal static bool TryTransformBounds(OccupiedBounds local, Affine3 localToRoot,
            out OccupiedBounds rootBounds)
        {
            rootBounds = default;
            if (!local.Valid) return false;
            Point3 centre = new Point3(local.Minimum.X * 0.5 + local.Maximum.X * 0.5,
                local.Minimum.Y * 0.5 + local.Maximum.Y * 0.5,
                local.Minimum.Z * 0.5 + local.Maximum.Z * 0.5);
            Point3 extent = new Point3(ExtentUp(centre.X, local.Minimum.X, local.Maximum.X),
                ExtentUp(centre.Y, local.Minimum.Y, local.Maximum.Y),
                ExtentUp(centre.Z, local.Minimum.Z, local.Maximum.Z));
            return EnvelopeMath.TryTransformBox(centre, extent, localToRoot, BoundsPaddingMetres, out rootBounds);
        }

        /// <summary>
        /// Encloses the TWO ORIGINAL affine stages without ever first multiplying
        /// them into a rounded float matrix. The intermediate world AABB can be loose;
        /// its lost correlation causes conservative refusal, never lost source corners.
        /// All inputs remain their original captured float values promoted to double.
        /// Native inverse/render agreement is separate; this proves those input maps.
        /// </summary>
        internal static bool TryTransformOriginalStages(OccupiedBounds local, Affine3 localToWorld,
            Affine3 worldToRoot, out OccupiedBounds rootBounds)
        {
            return TryTransformOriginalStages(local, localToWorld, worldToRoot, out _, out rootBounds);
        }

        /// <summary>
        /// Returns the first ORIGINAL stage as a direct WORLD witness before applying
        /// the compatibility inverse. It is never reconstructed from rootBounds.
        /// All success outputs enclose original source inputs, not a float product.
        /// </summary>
        internal static bool TryTransformOriginalStages(OccupiedBounds local, Affine3 localToWorld,
            Affine3 worldToRoot, out OccupiedBounds worldBounds, out OccupiedBounds rootBounds)
        {
            worldBounds = rootBounds = default;
            if (!TryTransformBounds(local, localToWorld, out worldBounds)) return false;
            return TryTransformBounds(worldBounds, worldToRoot, out rootBounds);
        }

        /// <summary>
        /// Native reported box inputs are ORIGINAL copied float centre/extents promoted
        /// to double. Avoid Bounds.min/max's intermediate float subtraction/addition.
        /// Proves those source box values only, not engine font/GPU actual coverage.
        /// </summary>
        internal static bool TryTransformOriginalBoxStages(Point3 centre, Point3 extents,
            Affine3 localToWorld, Affine3 worldToRoot, out OccupiedBounds worldBounds,
            out OccupiedBounds rootBounds)
        {
            worldBounds = rootBounds = default;
            if (!EnvelopeMath.TryTransformBox(centre, extents, localToWorld, BoundsPaddingMetres, out worldBounds)) return false;
            return TryTransformBounds(worldBounds, worldToRoot, out rootBounds);
        }

        internal static bool TryEncloseOriginalReportedBox(Point3 centre, Point3 extents,
            out OccupiedBounds worldBounds)
        {
            var identity = new Affine3(new Point3(1, 0, 0), new Point3(0, 1, 0),
                new Point3(0, 0, 1), new Point3(0, 0, 0));
            return EnvelopeMath.TryTransformBox(centre, extents, identity, BoundsPaddingMetres, out worldBounds);
        }

        internal static bool FitsRootRadius(OccupiedBounds bounds)
        {
            if (!bounds.Valid) return false;
            double x = Math.Max(Math.Abs(bounds.Minimum.X), Math.Abs(bounds.Maximum.X));
            double y = Math.Max(Math.Abs(bounds.Minimum.Y), Math.Abs(bounds.Maximum.Y));
            double z = Math.Max(Math.Abs(bounds.Minimum.Z), Math.Abs(bounds.Maximum.Z));
            double square = EnvelopeMath.NextUp(EnvelopeMath.NextUp(x * x) + EnvelopeMath.NextUp(y * y));
            square = EnvelopeMath.NextUp(square + EnvelopeMath.NextUp(z * z));
            return EnvelopeMath.Finite(square)
                && EnvelopeMath.NextUp(Math.Sqrt(square)) <= MaximumRootRadiusMetres;
        }

        private static double ExtentUp(double centre, double minimum, double maximum) =>
            EnvelopeMath.NextUp(Math.Max(EnvelopeMath.NextUp(Math.Abs(minimum - centre)),
                EnvelopeMath.NextUp(Math.Abs(maximum - centre))));
    }
}
