using System;

namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Immutable DATA-only strict raster predicate from a private canonical source
    /// clone. It is neither an owner nor a source publication/placement grant.
    /// Every possible nearest-rounded sample in an outward capsule AABB is checked;
    /// this deliberately over-refuses cells outside the curved capsule itself.
    /// Coordinates are Unity x/z metres and original source fields are binary32.
    /// Safe to read concurrently after construction; no borrowed array is retained.
    /// Actual publication/currentness and aggregate scheduling belong to the owner.
    /// </summary>
    internal sealed class RasterHabitat800
    {
        internal enum Verdict { Unknown, Blocked, Clear }
        internal const int MaximumCopiedCells = 1048576;
        internal const int MaximumAxisSamples = 4096;
        internal const int MaximumQueryCells = 256;
        internal const float MinimumRadiusMetres = .01f;
        internal const float MaximumRadiusMetres = 1.5f;
        internal const double MaximumSegmentMetres = 8;
        internal const double ScopeMinimumMetres = -400;
        internal const double ScopeMaximumMetres = 400;

        internal struct Observation
        {
            internal Verdict Result;
            internal string Reason;
            internal int FirstColumn, LastColumn, FirstRow, LastRow;
            internal int PlannedCells, VisitedCells, UnknownCells, WrongSpeciesCells;
        }

        private readonly float minimumX, minimumZ, width, depth;
        private readonly int columns, rows;
        private readonly int[] codes;

        private RasterHabitat800(float minimumX, float minimumZ, float width, float depth,
                                 int columns, int rows, int[] ownedCodes)
        {
            this.minimumX = minimumX;
            this.minimumZ = minimumZ;
            this.width = width;
            this.depth = depth;
            this.columns = columns;
            this.rows = rows;
            codes = ownedCodes;
        }

        /// <summary>
        /// Call only from the actual source worker on its FULL private canonical
        /// clone. Values are copied once, with checked allocation caps. This method
        /// cannot attest where its ordinary data came from and creates no lifetime
        /// token. Invalid source data yields null/false, which the owner holds Unknown.
        /// Unsupported individual codes remain local Unknown, not global rejection.
        /// </summary>
        internal static bool TryCopy(float minimumX, float minimumZ, float width, float depth,
                                     int columns, int rows, int[] privateCloneCodes,
                                     out RasterHabitat800 copied)
        {
            copied = null;
            long count = (long)columns * rows;
            if (!Finite(minimumX) || !Finite(minimumZ) || !Finite(width) || !Finite(depth) ||
                width <= 0 || depth <= 0 || columns < 2 || rows < 2 ||
                columns > MaximumAxisSamples || rows > MaximumAxisSamples ||
                count > MaximumCopiedCells || privateCloneCodes == null || privateCloneCodes.Length != count)
                return false;
            copied = new RasterHabitat800(minimumX, minimumZ, width, depth, columns, rows,
                                          (int[])privateCloneCodes.Clone());
            return true;
        }

        /// <summary>
        /// Actual kind1=Sheep: reject source Land3; kind2=Deer: require Land2.
        /// Strict activation additionally refuses code0 and unsupported codes.
        /// Sheep Land4 passes ONLY this legacy raster predicate; separate whole
        /// water/source-area/ground/Town/vehicle gates remain mandatory.
        /// Unknown anywhere dominates this raster observation. No owner, runtime
        /// state or native query is consulted and no result is cached dynamically.
        /// </summary>
        internal Observation Query(int actualKind, float fromX, float fromZ,
                                   float toX, float toZ, float radiusMetres)
        {
            var result = new Observation { Result = Verdict.Unknown, Reason = "Invalid query/profile/domain" };
            if ((actualKind != 1 && actualKind != 2) || !Finite(fromX) || !Finite(fromZ) ||
                !Finite(toX) || !Finite(toZ) || !Finite(radiusMetres) ||
                radiusMetres < MinimumRadiusMetres || radiusMetres > MaximumRadiusMetres)
                return result;
            double deltaX = (double)toX - fromX, deltaZ = (double)toZ - fromZ;
            if (deltaX * deltaX + deltaZ * deltaZ > MaximumSegmentMetres * MaximumSegmentMetres)
                return result;
            // A sum/difference of these bounded original float inputs is exact in
            // binary64. Next-float expansion below also includes input conversion
            // and each original source operation's rounding/tie uncertainty.
            double lowerX = Math.Min(fromX, toX) - (double)radiusMetres;
            double upperX = Math.Max(fromX, toX) + (double)radiusMetres;
            double lowerZ = Math.Min(fromZ, toZ) - (double)radiusMetres;
            double upperZ = Math.Max(fromZ, toZ) + (double)radiusMetres;
            if (lowerX < ScopeMinimumMetres || upperX > ScopeMaximumMetres ||
                lowerZ < ScopeMinimumMetres || upperZ > ScopeMaximumMetres ||
                !TryAxis(lowerX, upperX, minimumX, width, columns, out int firstColumn, out int lastColumn) ||
                !TryAxis(lowerZ, upperZ, minimumZ, depth, rows, out int firstRow, out int lastRow))
                return result;
            long plannedCells = (long)(lastColumn - firstColumn + 1) * (lastRow - firstRow + 1);
            result.FirstColumn = firstColumn; result.LastColumn = lastColumn;
            result.FirstRow = firstRow; result.LastRow = lastRow;
            result.PlannedCells = (int)plannedCells;
            // No cell loop or partial Clear result before this complete work check.
            if (plannedCells > MaximumQueryCells)
            {
                result.Reason = "Whole possible-cell set exceeds query work cap";
                return result;
            }
            for (int row = firstRow; row <= lastRow; row++)
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    int code = codes[row * columns + column];
                    result.VisitedCells++;
                    if (code < 1 || code > 4) result.UnknownCells++;
                    else if (actualKind == 1 ? code == 3 : code != 2) result.WrongSpeciesCells++;
                }
            if (result.UnknownCells != 0) result.Reason = "Unknown/unsupported source raster cell";
            else if (result.WrongSpeciesCells != 0)
            {
                result.Result = Verdict.Blocked;
                result.Reason = "Known source cell violates actual species raster predicate";
            }
            else
            {
                result.Result = Verdict.Clear;
                result.Reason = "RASTER_PREDICATE_ONLY; all placement/currentness gates remain separate";
            }
            return result;
        }

        private static bool TryAxis(double lowerMetres, double upperMetres, float sourceMinimum,
                                    float sourceSpan, int sampleCount, out int first, out int last)
        {
            first = last = 0;
            double domainMaximum = (double)sourceMinimum + sourceSpan;
            float lower = NextDown((float)lowerMetres);
            float upper = NextUp((float)upperMetres);
            // Never reproduce GeoRegion's outside-domain clamping. Even an
            // uncertainty interval touching outside the known domain is Unknown.
            if (!Finite(lower) || !Finite(upper) || lower < sourceMinimum || upper > domainMaximum)
                return false;
            lower = NextDown(lower - sourceMinimum);
            upper = NextUp(upper - sourceMinimum);
            lower = NextDown(lower / sourceSpan);
            upper = NextUp(upper / sourceSpan);
            lower = NextDown(lower * (sampleCount - 1));
            upper = NextUp(upper * (sampleCount - 1));
            if (!Finite(lower) || !Finite(upper)) return false;
            double roundedFirst = Math.Round((double)lower, MidpointRounding.ToEven);
            double roundedLast = Math.Round((double)upper, MidpointRounding.ToEven);
            if (roundedFirst < 0 || roundedLast >= sampleCount || roundedFirst > roundedLast) return false;
            first = (int)roundedFirst;
            last = (int)roundedLast;
            return true;
        }

        // Exact neighbouring binary32 numbers, including both signed zeros and
        // subnormal transitions. No Math.BitIncrement dependency on Unity profile.
        private static float NextUp(float value)
        {
            if (float.IsNaN(value) || value == float.PositiveInfinity) return value;
            if (value == 0) return float.Epsilon;
            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(value > 0 ? bits + 1 : bits - 1);
        }
        private static float NextDown(float value)
        {
            if (float.IsNaN(value) || value == float.NegativeInfinity) return value;
            if (value == 0) return -float.Epsilon;
            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(value > 0 ? bits - 1 : bits + 1);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
