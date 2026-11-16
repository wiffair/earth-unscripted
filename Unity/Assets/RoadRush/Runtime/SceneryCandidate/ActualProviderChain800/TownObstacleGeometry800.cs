using RoadRush.FunnyAnimals.VehicleCaptureProposal;

namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Pure original-input affine bounds for copied Town collider geometry. Bounds
    /// include the entire XZ projection at every height; false-positive exclusion
    /// is intentional. This does not reproduce PhysX cooking/contact offsets or
    /// establish interiors, door passage, roof support or native contact clearance.
    /// </summary>
    internal static class TownObstacleGeometry800
    {
        /// <summary>
        /// Enclose an ORIGINAL local vertex, not a previously rounded world point.
        /// The unchanged conditioned EnvelopeMath supplies outward binary64 error
        /// bounds from the original affine operands; excessive uncertainty refuses.
        /// </summary>
        internal static bool TryIncludeVertex(Point3 originalLocalVertex, Affine3 originalFrame,
            ref OccupiedBounds bounds, ref bool hasBounds)
        {
            if (!EnvelopeMath.TryTransformBox(originalLocalVertex, new Point3(0, 0, 0),
                    originalFrame, 0, out OccupiedBounds pointBounds))
                return false;
            bounds = hasBounds ? EnvelopeMath.Union(bounds, pointBounds) : pointBounds;
            hasBounds = true;
            return true;
        }

        /// <summary>Original local metre centre/size; positive full sizes required.</summary>
        internal static bool TryBox(Point3 originalCentre, Point3 originalSize, Affine3 originalFrame,
            out OccupiedBounds bounds)
        {
            bounds = default;
            if (!originalSize.Finite || originalSize.X <= 0 || originalSize.Y <= 0 || originalSize.Z <= 0)
                return false;
            // Binary32 sizes promoted to binary64 are exact; division by two is
            // exact here. Do not precompose a new binary32 transform or world box.
            var halfSize = new Point3(originalSize.X * .5, originalSize.Y * .5, originalSize.Z * .5);
            return EnvelopeMath.TryTransformBox(originalCentre, halfSize, originalFrame, 0, out bounds);
        }

        /// <summary>
        /// A closed capsule is contained in this outward swept-sphere AABB. XZ
        /// overlap is conservatively Blocked, even in a diagonal AABB corner.
        /// Radius/segment/scope policy is enforced by the bound consumer separately.
        /// </summary>
        internal static bool TryCapsuleBounds(double fromX, double fromZ, double toX, double toZ,
            double radiusMetres, out OccupiedBounds bounds)
        {
            return EnvelopeMath.TryObservedSweep(new Point3(fromX, 0, fromZ),
                new Point3(toX, 0, toZ), radiusMetres, 0, out bounds);
        }
    }
}
