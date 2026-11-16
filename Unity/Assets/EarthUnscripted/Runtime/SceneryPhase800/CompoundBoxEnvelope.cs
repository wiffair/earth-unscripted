using System;
using System.Collections.Generic;
using EarthUnscripted.FunnyAnimals.VehicleCaptureProposal;

namespace EarthUnscripted.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Numeric copy of one published box, in vehicle-root metres. It is NOT a registry
    /// sample, ownership token, or completeness grant. Synthetic tests can exercise
    /// equations using this value without manufacturing actual producer snapshots.
    /// </summary>
    internal readonly struct BoxWitness
    {
        internal readonly Point3 Centre;
        internal readonly Point3 HalfExtent;
        internal readonly Affine3 BoxToRoot;
        internal readonly bool ActiveForPhysics;

        internal BoxWitness(Point3 centre, Point3 halfExtent, Affine3 boxToRoot, bool active)
        {
            Centre = centre;
            HalfExtent = halfExtent;
            BoxToRoot = boxToRoot;
            ActiveForPhysics = active;
        }
    }

    /// <summary>
    /// Detached numeric source-geometry witness. Physical boxes do not cover mirrors,
    /// body kits, visible wheels, imported shells or a driver's eye. Consequently this
    /// type can NEVER authorize whole-vehicle clearance or produce an empty safe world.
    /// Native transform agreement is not established by the pure equations below.
    /// </summary>
    public readonly struct PhysicalEnvelopeWitness
    {
        public readonly OccupiedBounds Bounds;
        public readonly double RootRadiusMetres;
        public readonly double BodyEnvelopeRadiusMetres;
        public readonly int ActiveBoxes;
        public bool WholeVehicleClearanceBound => false;

        internal PhysicalEnvelopeWitness(OccupiedBounds bounds, double rootRadius,
            double bodyRadius, int activeBoxes)
        {
            Bounds = bounds;
            RootRadiusMetres = rootRadius;
            BodyEnvelopeRadiusMetres = bodyRadius;
            ActiveBoxes = activeBoxes;
        }
    }

    /// <summary>
    /// Pure, single-call numeric processing of already-copied compound boxes. Original
    /// conditioned-v5 affine bounds are reused unchanged. No Unity/scene discovery,
    /// issuer, registry-completeness test, source hazard query or future prediction.
    /// Metric axes: x right/y up/z forward. Inputs are ROOT-local boxes plus an actual
    /// captured render-root world affine and physics body world position. Publication
    /// ownership/currentness and native physics agreement must be checked elsewhere.
    /// </summary>
    internal static class CompoundBoxEnvelope
    {
        // Source-profile observations, not a caller-provided readiness flag. These
        // follow frozen Registry1 + VehicleDimensions/EnsureRaceWheelColliders rules.
        private const int KnownModelCount = 4;
        private const int SingleSeatModel = 3;
        private const int SingleSeatParts = 5;
        private const int StockActiveParts = 1;
        private const int RegistryMaximumParts = 8;
        // Numeric proposal refusal/padding policies; never clamp actual geometry.
        private const double NumericPaddingMetres = 0.0001;
        private const double MaximumRadiusMetres = 20;
        private const double MaximumBoundsSpanMetres = 40;

        internal static bool TryCompute(int model, IReadOnlyList<BoxWitness> boxes,
            Affine3 visibleRootToWorld, Point3 bodyPositionMetres,
            out PhysicalEnvelopeWitness witness, out string reason)
        {
            witness = default;
            reason = "Physical sample geometry unavailable";
            if (model < 0 || model >= KnownModelCount || boxes == null || boxes.Count == 0
                || boxes.Count > RegistryMaximumParts || !visibleRootToWorld.Finite || !bodyPositionMetres.Finite)
                return false;
            // Stock roots can retain4disabled F1 boxes after a model change. Unknown
            // future compound layouts refuse; do not silently drop an unsupported part.
            if (model == SingleSeatModel ? boxes.Count != SingleSeatParts
                : boxes.Count != StockActiveParts && boxes.Count != SingleSeatParts)
            {
                reason = "Unknown source compound layout";
                return false;
            }

            OccupiedBounds rootBounds = default;
            bool hasRootBounds = false;
            int activeBoxes = 0;
            for (int index = 0; index < boxes.Count; index++)
            {
                BoxWitness box = boxes[index];
                if (!box.Centre.Finite || !box.HalfExtent.Finite || !box.BoxToRoot.Finite
                    || box.HalfExtent.X <= 0 || box.HalfExtent.Y <= 0 || box.HalfExtent.Z <= 0)
                {
                    reason = "Nonfinite/degenerate box, including inactive source parts";
                    return false;
                }
                if (!box.ActiveForPhysics) continue;
                activeBoxes++;
                if (!EnvelopeMath.TryTransformBox(box.Centre, box.HalfExtent, box.BoxToRoot,
                    NumericPaddingMetres, out OccupiedBounds partBounds))
                {
                    reason = "Conditioned original-input box-to-root envelope refused";
                    return false;
                }
                rootBounds = hasRootBounds ? EnvelopeMath.Union(rootBounds, partBounds) : partBounds;
                hasRootBounds = true;
            }
            int expectedActive = model == SingleSeatModel ? SingleSeatParts : StockActiveParts;
            if (!hasRootBounds || activeBoxes != expectedActive)
            {
                reason = "Actual source model active-part layout unavailable";
                return false;
            }

            double radiusX = Math.Max(Math.Abs(rootBounds.Minimum.X), Math.Abs(rootBounds.Maximum.X));
            double radiusY = Math.Max(Math.Abs(rootBounds.Minimum.Y), Math.Abs(rootBounds.Maximum.Y));
            double radiusZ = Math.Max(Math.Abs(rootBounds.Minimum.Z), Math.Abs(rootBounds.Maximum.Z));
            double rootRadius = HypotUp(radiusX, radiusY, radiusZ);
            if (!EnvelopeMath.Finite(rootRadius) || rootRadius > MaximumRadiusMetres)
            {
                reason = "Root-local physical radius exceeds numeric policy";
                return false;
            }

            // Recover a centre/extent WITHOUT shrinking the original AABB. Midpoint
            // halves avoid overflow; directed distances cover midpoint/subnormal error.
            Point3 centre = new Point3(rootBounds.Minimum.X * 0.5 + rootBounds.Maximum.X * 0.5,
                rootBounds.Minimum.Y * 0.5 + rootBounds.Maximum.Y * 0.5,
                rootBounds.Minimum.Z * 0.5 + rootBounds.Maximum.Z * 0.5);
            Point3 extent = new Point3(ExtentUp(centre.X, rootBounds.Minimum.X, rootBounds.Maximum.X),
                ExtentUp(centre.Y, rootBounds.Minimum.Y, rootBounds.Maximum.Y),
                ExtentUp(centre.Z, rootBounds.Minimum.Z, rootBounds.Maximum.Z));
            if (!EnvelopeMath.TryTransformBox(centre, extent, visibleRootToWorld,
                NumericPaddingMetres, out OccupiedBounds renderedPhysicalBounds))
            {
                reason = "Conditioned render-root transform refused";
                return false;
            }

            // A Frobenius norm upper-bounds the linear operator norm by Cauchy-Schwarz.
            // Using it (at least1) deliberately over-encloses every physical orientation
            // of the root box sphere, instead of inventing a native quaternion matrix.
            // The resulting envelope is loose; actual native/render scale agreement is
            // still a producer/native acceptance gate. No future translation is assumed.
            double scaleUpper = LinearFrobeniusNormUp(visibleRootToWorld);
            double bodyRadius = EnvelopeMath.NextUp(rootRadius * Math.Max(1, scaleUpper));
            if (!EnvelopeMath.Finite(bodyRadius) || bodyRadius > MaximumRadiusMetres
                || !EnvelopeMath.TryObservedSweep(bodyPositionMetres, bodyPositionMetres,
                    bodyRadius, NumericPaddingMetres, out OccupiedBounds physicalBodyBounds))
            {
                reason = "Conservative physical-orientation sphere refused";
                return false;
            }
            OccupiedBounds combined = EnvelopeMath.Union(renderedPhysicalBounds, physicalBodyBounds);
            if (combined.Maximum.X - combined.Minimum.X > MaximumBoundsSpanMetres
                || combined.Maximum.Y - combined.Minimum.Y > MaximumBoundsSpanMetres
                || combined.Maximum.Z - combined.Minimum.Z > MaximumBoundsSpanMetres)
            {
                reason = "Conservative physical envelope exceeds private span policy";
                return false;
            }
            witness = new PhysicalEnvelopeWitness(combined, rootRadius, bodyRadius, activeBoxes);
            reason = "Physical source-geometry witness ONLY; presentation/native/coherence grants remain separate";
            return true;
        }

        private static double ExtentUp(double centre, double minimum, double maximum) =>
            EnvelopeMath.NextUp(Math.Max(EnvelopeMath.NextUp(Math.Abs(minimum - centre)),
                EnvelopeMath.NextUp(Math.Abs(maximum - centre))));

        // Every nonnegative product/sum and the final sqrt are rounded upward. This
        // bounds original input magnitudes, not a cancelled floating output centre.
        private static double HypotUp(double x, double y, double z)
        {
            double sum = EnvelopeMath.NextUp(EnvelopeMath.NextUp(x * x) + EnvelopeMath.NextUp(y * y));
            sum = EnvelopeMath.NextUp(sum + EnvelopeMath.NextUp(z * z));
            return EnvelopeMath.NextUp(Math.Sqrt(sum));
        }

        private static double LinearFrobeniusNormUp(Affine3 frame)
        {
            double first = HypotUp(frame.X.X, frame.X.Y, frame.X.Z);
            double second = HypotUp(frame.Y.X, frame.Y.Y, frame.Y.Z);
            double third = HypotUp(frame.Z.X, frame.Z.Y, frame.Z.Z);
            return HypotUp(first, second, third);
        }
    }
}
