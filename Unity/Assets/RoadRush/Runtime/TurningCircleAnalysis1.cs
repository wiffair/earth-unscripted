using System;
using System.Collections.Generic;
using System.Threading;

namespace RoadRush.Qa
{
    /// <summary>
    /// Pure bounded analysis of observed horizontal body/tyre paths, not vehicle
    /// physics or proof of a theoretical minimum turning radius. Fits an algebraic
    /// circle and independently integrates observed travel/unwrapped body yaw.
    /// Coordinates and distances are metres; heading is degrees around global Y.
    /// </summary>
    public static class TurningCircleAnalysis1
    {
        public const int MaximumSamples = 20000;
        private const double MaximumCoordinateMetres = 1000000;
        private const double MaximumWrappedHeadingDegrees = 360;
        private const double MinimumNormalEquationDeterminant = 0.000001;
        /// <summary>Immutable observed sample; constructor owns copies of four tyre X/Z pairs.</summary>
        public sealed class Sample
        {
            public double X { get; }
            public double Z { get; }
            public double HeadingDegrees { get; }

            private readonly double[] tyreX, tyreZ;
            public Sample(double x, double z, double headingDegrees, double[] tyreXMetres, double[] tyreZMetres)
            {
                if (!Coordinate(x) || !Coordinate(z) || !Finite(headingDegrees) || Math.Abs(headingDegrees) > MaximumWrappedHeadingDegrees || tyreXMetres == null || tyreZMetres == null || tyreXMetres.Length != 4 || tyreZMetres.Length != 4)
                    throw new ArgumentException("Finite observed body pose and exactly four tyre pairs required");
                X = x;
                Z = z;
                HeadingDegrees = headingDegrees;
                tyreX = (double[])tyreXMetres.Clone();
                tyreZ = (double[])tyreZMetres.Clone();
                for (int wheel = 0; wheel < 4; wheel++)
                    if (!Coordinate(tyreX[wheel]) || !Coordinate(tyreZ[wheel]))
                        throw new ArgumentException("Finite bounded tyre coordinates");
            }

            internal double TyreX(int wheel) => tyreX[wheel];
            internal double TyreZ(int wheel) => tyreZ[wheel];
        }

        /// <summary>Owned analysis values. Wheel radius arrays are copied on every export.</summary>
        public sealed class Result
        {
            public double CentreX { get; }
            public double CentreZ { get; }
            public double FittedRadiusMetres { get; }
            public double TravelMetres { get; }
            public double SignedYawRadians { get; }
            public double RadiusFromArcAndYawMetres { get; }
            public double RmsResidualMetres { get; }
            public double MaximumResidualMetres { get; }

            private readonly double[] tyreRadii;
            internal Result(double x, double z, double radius, double travel, double yaw, double rms, double maximum, double[] tyres)
            {
                CentreX = x;
                CentreZ = z;
                FittedRadiusMetres = radius;
                TravelMetres = travel;
                SignedYawRadians = yaw;
                RadiusFromArcAndYawMetres = travel / Math.Abs(yaw);
                RmsResidualMetres = rms;
                MaximumResidualMetres = maximum;
                tyreRadii = (double[])tyres.Clone();
            }

            public double[] CopyMeanTyreRadiiMetres() => (double[])tyreRadii.Clone();
        }

        /// <summary>
        /// Synchronous, read-only mathematical fit. The caller must not mutate its
        /// list during this operation. Refuses nonfinite/degenerate data and work
        /// over the sample cap; checks cancellation throughout. It assumes no target
        /// radius, source road, speed or car dimensions. Those remain caller gates.
        /// </summary>
        public static Result Fit(IReadOnlyList<Sample> samples, CancellationToken cancellation = default)
        {
            if (samples == null || samples.Count < 3 || samples.Count > MaximumSamples)
                throw new ArgumentException("Bounded observed sample sequence required");
            double meanX = 0, meanZ = 0;
            foreach (var sample in samples)
            {
                cancellation.ThrowIfCancellationRequested();
                if (sample == null)
                    throw new ArgumentException("Null observed sample");
                meanX += sample.X / samples.Count;
                meanZ += sample.Z / samples.Count;
            }

            double xx = 0, zz = 0, xz = 0, xr = 0, zr = 0;
            foreach (var sample in samples)
            {
                cancellation.ThrowIfCancellationRequested();
                double x = sample.X - meanX, z = sample.Z - meanZ, square = x * x + z * z;
                xx += x * x;
                zz += z * z;
                xz += x * z;
                xr += x * square * 0.5;
                zr += z * square * 0.5;
            }

            double determinant = xx * zz - xz * xz;
            if (!Finite(determinant) || determinant <= MinimumNormalEquationDeterminant)
                throw new ArgumentException("Turn path is degenerate; cannot infer a finite circle");
            double centreX = meanX + (xr * zz - zr * xz) / determinant;
            double centreZ = meanZ + (zr * xx - xr * xz) / determinant;
            var radii = new double[samples.Count];
            double radius = 0, travel = 0, yaw = 0;
            var tyres = new double[4];
            for (int index = 0; index < samples.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var sample = samples[index];
                radii[index] = Distance(sample.X, sample.Z, centreX, centreZ);
                radius += radii[index] / samples.Count;
                for (int wheel = 0; wheel < 4; wheel++)
                    tyres[wheel] += Distance(sample.TyreX(wheel), sample.TyreZ(wheel), centreX, centreZ) / samples.Count;
                if (index == 0)
                    continue;
                var previous = samples[index - 1];
                travel += Distance(sample.X, sample.Z, previous.X, previous.Z);
                double delta = sample.HeadingDegrees - previous.HeadingDegrees;
                while (delta > 180)
                    delta -= 360;
                while (delta <= -180)
                    delta += 360;
                yaw += delta * Math.PI / 180;
            }

            if (!Finite(radius) || radius <= 0 || !Finite(yaw) || Math.Abs(yaw) < 0.000001)
                throw new ArgumentException("No finite observed turn radius/yaw");
            double residualSquared = 0, maximum = 0;
            foreach (double value in radii)
            {
                double residual = Math.Abs(value - radius);
                residualSquared += residual * residual / samples.Count;
                maximum = Math.Max(maximum, residual);
            }

            return new Result(centreX, centreZ, radius, travel, yaw, Math.Sqrt(residualSquared), maximum, tyres);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Coordinate(double value) => Finite(value) && Math.Abs(value) <= MaximumCoordinateMetres;
        private static double Distance(double x, double z, double otherX, double otherZ) => Math.Sqrt((x - otherX) * (x - otherX) + (z - otherZ) * (z - otherZ));
    }
}
