using System;

namespace RoadRush.FunnyAnimals.VehicleCaptureProposal
{
    /// <summary>
    /// Immutable double-precision point/vector in metres. The caller determines whether
    /// a value is local or world space; transforms never silently change that convention.
    /// Unity vehicle space uses x right, y up, z forward. No terrain authority is implied.
    /// </summary>
    public readonly struct Point3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Finite => EnvelopeMath.Finite(X) && EnvelopeMath.Finite(Y) && EnvelopeMath.Finite(Z);
    }

    /// <summary>
    /// Value-owned affine transform: X/Y/Z are dimensionless basis columns, T is
    /// translation in metres. It borrows no Unity objects and is safe for numeric tests.
    /// </summary>
    public readonly struct Affine3
    {
        public readonly Point3 X;
        public readonly Point3 Y;
        public readonly Point3 Z;
        public readonly Point3 T;

        public Affine3(Point3 x, Point3 y, Point3 z, Point3 translation)
        {
            X = x;
            Y = y;
            Z = z;
            T = translation;
        }

        public Point3 Apply(Point3 point) => new Point3(
            T.X + X.X * point.X + Y.X * point.Y + Z.X * point.Z,
            T.Y + X.Y * point.X + Y.Y * point.Y + Z.Y * point.Z,
            T.Z + X.Z * point.X + Y.Z * point.Y + Z.Z * point.Z);

        public bool Finite => X.Finite && Y.Finite && Z.Finite && T.Finite;
    }

    /// <summary>
    /// Closed axis-aligned occupied bounds in metres, normally world space. Touching
    /// boundaries count as occupied. A default value is a zero-size box, not authority:
    /// callers must also check the producing method's success result.
    /// </summary>
    public readonly struct OccupiedBounds
    {
        public readonly Point3 Minimum;
        public readonly Point3 Maximum;

        public OccupiedBounds(Point3 minimum, Point3 maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public bool Valid => Minimum.Finite && Maximum.Finite
            && Minimum.X <= Maximum.X && Minimum.Y <= Maximum.Y && Minimum.Z <= Maximum.Z;

        public bool Contains(Point3 point) => Valid && point.Finite
            && point.X >= Minimum.X && point.X <= Maximum.X
            && point.Y >= Minimum.Y && point.Y <= Maximum.Y
            && point.Z >= Minimum.Z && point.Z <= Maximum.Z;

        public bool OverlapsXZ(OccupiedBounds other) => Valid && other.Valid
            && Maximum.X >= other.Minimum.X && other.Maximum.X >= Minimum.X
            && Maximum.Z >= other.Minimum.Z && other.Maximum.Z >= Minimum.Z;
    }

    /// <summary>
    /// Original, allocation-free numeric box witnesses for an individual vehicle.
    /// No Unity object access, mutable state, actor registry, ground query or prediction.
    /// These methods may run off-thread once the caller has copied all inputs by value.
    /// </summary>
    public static class EnvelopeMath
    {
        // Proposal policy caps, not measured vehicle dimensions. Large/invalid inputs
        // refuse instead of making a misleadingly precise occupied envelope.
        private const double MaximumMarginMetres = 1.0;
        private const double MaximumRootRadiusMetres = 20.0;

        // 128 * binary64 unit roundoff (2^-53). This dominates gamma(64) for
        // fewer than64 rounding operations/axis. Magnitudes below are accumulated
        // UPWARD from ORIGINAL absolute products/summands, never cancelled output.
        // Add an absolute64-smallest-subnormal allowance for gradual underflow.
        private const double RoundoffFactor = 1.4210854715202004e-14;
        private const double UnderflowAllowance = 64 * double.Epsilon;
        // Proposal conditioning refusal, in metres, SEPARATE from requested margin.
        // It prevents enormous arithmetic uncertainty being hidden by a small margin.
        // This numeric policy is not a bound on native Unity/GPU transforms.
        private const double MaximumArithmeticAllowanceMetres = 0.001;

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>
        /// Returns a value-owned union, throwing on either invalid input. Refusing an
        /// invalid late member prevents accidentally publishing a valid early prefix.
        /// </summary>
        public static OccupiedBounds Union(OccupiedBounds first, OccupiedBounds second)
        {
            if (!first.Valid || !second.Valid)
                throw new ArgumentException("Invalid envelope cannot be replaced by a valid prefix");

            return new OccupiedBounds(
                new Point3(Math.Min(first.Minimum.X, second.Minimum.X),
                    Math.Min(first.Minimum.Y, second.Minimum.Y), Math.Min(first.Minimum.Z, second.Minimum.Z)),
                new Point3(Math.Max(first.Maximum.X, second.Maximum.X),
                    Math.Max(first.Maximum.Y, second.Maximum.Y), Math.Max(first.Maximum.Z, second.Maximum.Z)));
        }

        /// <summary>
        /// Encloses exact affine images of all eight ORIGINAL-input box corners.
        /// Centre/extents and margin are metres; extents must be nonnegative, margin
        /// 0..1m. Finite affine scale/shear supported only while derived roundoff
        /// allowance stays<=1mm. Cancellation, overflow or excessive uncertainty
        /// REFUSES rather than publishing inward bounds. No engine/allocation calls.
        /// </summary>
        public static bool TryTransformBox(Point3 centre, Point3 extent, Affine3 frame,
            double margin, out OccupiedBounds bounds)
        {
            bounds = default;
            if (!centre.Finite || !extent.Finite || extent.X < 0 || extent.Y < 0 || extent.Z < 0
                || !frame.Finite || !Finite(margin) || margin < 0 || margin > MaximumMarginMetres)
                return false;

            Point3 transformedCentre = frame.Apply(centre);
            Point3 transformedExtent = new Point3(
                Math.Abs(frame.X.X) * extent.X + Math.Abs(frame.Y.X) * extent.Y + Math.Abs(frame.Z.X) * extent.Z,
                Math.Abs(frame.X.Y) * extent.X + Math.Abs(frame.Y.Y) * extent.Y + Math.Abs(frame.Z.Y) * extent.Z,
                Math.Abs(frame.X.Z) * extent.X + Math.Abs(frame.Y.Z) * extent.Y + Math.Abs(frame.Z.Z) * extent.Z);
            if (!TryAxisBounds(transformedCentre.X, transformedExtent.X, frame.T.X,
                    frame.X.X, frame.Y.X, frame.Z.X, centre, extent, margin, out double minimumX, out double maximumX)
                || !TryAxisBounds(transformedCentre.Y, transformedExtent.Y, frame.T.Y,
                    frame.X.Y, frame.Y.Y, frame.Z.Y, centre, extent, margin, out double minimumY, out double maximumY)
                || !TryAxisBounds(transformedCentre.Z, transformedExtent.Z, frame.T.Z,
                    frame.X.Z, frame.Y.Z, frame.Z.Z, centre, extent, margin, out double minimumZ, out double maximumZ))
                return false;
            bounds = new OccupiedBounds(new Point3(minimumX, minimumY, minimumZ),
                new Point3(maximumX, maximumY, maximumZ));
            return bounds.Valid;
        }

        // The centre alone can be zero after catastrophic cancellation even when its
        // input summands are huge. Bound error using ALL absolute original products,
        // including extent terms. Upward additions/products avoid undercounting that
        // magnitude. Original v4 failed Street's exact-F32 2^54+1-2^54 counterexample.
        private static bool TryAxisBounds(double computedCentre, double computedExtent,
            double translation, double basisX, double basisY, double basisZ,
            Point3 centre, Point3 extent, double margin, out double minimum, out double maximum)
        {
            minimum = maximum = 0;
            double magnitude = Math.Abs(translation);
            magnitude = AddUp(magnitude, MultiplyAbsoluteUp(basisX, centre.X));
            magnitude = AddUp(magnitude, MultiplyAbsoluteUp(basisY, centre.Y));
            magnitude = AddUp(magnitude, MultiplyAbsoluteUp(basisZ, centre.Z));
            magnitude = AddUp(magnitude, MultiplyAbsoluteUp(basisX, extent.X));
            magnitude = AddUp(magnitude, MultiplyAbsoluteUp(basisY, extent.Y));
            magnitude = AddUp(magnitude, MultiplyAbsoluteUp(basisZ, extent.Z));
            double allowance = AddUp(NextUp(magnitude * RoundoffFactor), UnderflowAllowance);
            if (!Finite(computedCentre) || !Finite(computedExtent) || !Finite(allowance)
                || allowance > MaximumArithmeticAllowanceMetres) return false;
            double padding = AddUp(margin, allowance);
            // Round EACH endpoint operation outward; padding alone cannot prevent a
            // last addition/subtraction rounding inward at a large binary exponent.
            minimum = NextDown(NextDown(computedCentre - computedExtent) - padding);
            maximum = NextUp(NextUp(computedCentre + computedExtent) + padding);
            return Finite(minimum) && Finite(maximum);
        }

        private static double MultiplyAbsoluteUp(double first, double second) =>
            NextUp(Math.Abs(first) * Math.Abs(second));
        private static double AddUp(double first, double second) => NextUp(first + second);

        /// <summary>
        /// Smallest representable binary64 value strictly above input (NaN/+infinity
        /// unchanged). Signed zeros both move to smallest positive subnormal. Uses
        /// standard BitConverter so Unity need not expose .NET-only Math.BitIncrement.
        /// Internal solely for deterministic precision regression tests; no native call.
        /// </summary>
        internal static double NextUp(double value)
        {
            if (double.IsNaN(value) || value == double.PositiveInfinity) return value;
            if (value == 0) return double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int64BitsToDouble(value > 0 ? bits + 1 : bits - 1);
        }

        /// <summary>Symmetric downward step; signed zeros move to negative subnormal.</summary>
        internal static double NextDown(double value)
        {
            if (double.IsNaN(value) || value == double.NegativeInfinity) return value;
            if (value == 0) return -double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int64BitsToDouble(value > 0 ? bits - 1 : bits + 1);
        }

        /// <summary>
        /// Encloses a root-relative sphere along two OBSERVED root positions. Radius
        /// (0,20]m/margin[0,1]m. All endpoint expansion is directed outward, including
        /// large endpoints and subnormal radius. Refuses infinite expanded endpoints.
        /// No future unobserved translation/acceleration/topology or immunity proof.
        /// This operation uses direct outward interval arithmetic, not centre-error
        /// estimation. Huge but finite broad bounds are numeric witnesses only.
        /// </summary>
        public static bool TryObservedSweep(Point3 previousRoot, Point3 currentRoot,
            double rootRadius, double margin, out OccupiedBounds bounds)
        {
            bounds = default;
            if (!previousRoot.Finite || !currentRoot.Finite || !Finite(rootRadius)
                || rootRadius <= 0 || rootRadius > MaximumRootRadiusMetres
                || !Finite(margin) || margin < 0 || margin > MaximumMarginMetres)
                return false;

            double paddedRadius = AddUp(rootRadius, margin);
            bounds = new OccupiedBounds(
                new Point3(NextDown(Math.Min(previousRoot.X, currentRoot.X) - paddedRadius),
                    NextDown(Math.Min(previousRoot.Y, currentRoot.Y) - paddedRadius),
                    NextDown(Math.Min(previousRoot.Z, currentRoot.Z) - paddedRadius)),
                new Point3(NextUp(Math.Max(previousRoot.X, currentRoot.X) + paddedRadius),
                    NextUp(Math.Max(previousRoot.Y, currentRoot.Y) + paddedRadius),
                    NextUp(Math.Max(previousRoot.Z, currentRoot.Z) + paddedRadius)));
            return bounds.Valid;
        }
    }
}
