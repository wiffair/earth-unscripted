using System;
using System.Collections.Generic;

// Legacy pure admission geometry helpers retained for compatibility.
// Core800 reuses only bounded source-shape/index math through the separately
// reviewed semantic consumer. No type here can issue ground, source, Town, actor,
// publication or gameplay authority. In particular the triangle/grid/vehicle/
// epoch helpers below are NOT activated as Core800 owner contracts by this file.
// Coordinates are caller-bound X/Z or X/Y/Z metres; no transform/projection,
// source identity, current generation or terrain support is inferred here.
namespace RoadRush.FunnyAnimals.Admission
{
    /// <summary>
    /// Immutable planar point/vector in caller-bound X/Z metres.
    /// No Y, coordinate projection, world identity or ownership is carried.
    /// </summary>
    public readonly struct P
    {
        public readonly double X, Z;
        /// <summary>
        /// Store X/Z metres unchanged; caller supplies the common frame.
        /// </summary>
        public P(double x, double z)
        {
            X = x;
            Z = z;
        }

        public static P operator +(P a, P b) => new P(a.X + b.X, a.Z + b.Z);
        public static P operator -(P a, P b) => new P(a.X - b.X, a.Z - b.Z);
        public static P operator *(P a, double n) => new P(a.X * n, a.Z * n);
    }

    /// <summary>
    /// Clear means only that this bounded predicate found no intersection.
    /// Blocked is a positive geometric hit; Unknown is refusal/incomplete work.
    /// None of these values is a safe-world or source/ground capability.
    /// </summary>
    public enum Verdict
    {
        Clear,
        Blocked,
        Unknown
    }

    /// <summary>
    /// Mutable dimensionless work-token counters with unchanged public defaults.
    /// The caller owns/reset schedules this object; helpers never refresh it.
    /// Share a budget across related queries to bound work. It is not thread-safe.
    /// CopiedVertices/Indices are diagnostic counters, not ownership witnesses.
    /// </summary>
    public sealed class WorkBudget
    {
        // Unchanged dimensionless token defaults; categories are work-accounting
        // counters, not time durations or proof of a completed frame budget.
        private const int DefaultMetadataTokens = 32768;
        private const int DefaultHeightsTokens = 2048;
        private const int DefaultCoverageTokens = 512;
        private const int DefaultSeamsTokens = 256;
        private const int DefaultTrianglesTokens = 4096;
        private const int DefaultObstaclesTokens = 64;
        private const int DefaultQueriesTokens = 128;
        private const int DefaultMeshCopiesTokens = 1;
        private const int DefaultValidationTokens = 16384;
        private const int DefaultGazeTokens = 16;
        public int Metadata = DefaultMetadataTokens,
            Heights = DefaultHeightsTokens,
            Coverage = DefaultCoverageTokens,
            Seams = DefaultSeamsTokens,
            Triangles = DefaultTrianglesTokens,
            Obstacles = DefaultObstaclesTokens,
            Queries = DefaultQueriesTokens,
            MeshCopies = DefaultMeshCopiesTokens,
            Validation = DefaultValidationTokens,
            Gaze = DefaultGazeTokens;
        public int CopiedVertices, CopiedIndices;
        /// <summary>
        /// Spend n nonnegative tokens from the supplied counter; insufficient/negative
        /// cost returns false without decrement. Historic parameter names remain intact.
        /// </summary>
        public bool Use(ref int counter, int n = 1)
        {
            if (n < 0 || counter < n)
                return false;
            counter -= n;
            return true;
        }
    }

    /// <summary>
    /// Mutable capsule/box/whole-polygon description in planar metres.
    /// Factories derive broadphase bounds only; validation belongs to the caller/index.
    /// Poly copies its P array, but Add borrows this Shape object: do not mutate it
    /// after indexing. Bounding boxes are never substituted for polygon hazards.
    /// </summary>
    public sealed class Shape
    {
        public P A, B;
        public P[] Polygon;
        public double Radius;
        public bool Rectangle;
        public double MinX, MinZ, MaxX, MaxZ;
        /// <summary>
        /// Create a capsule and its bounds from endpoints/radius in metres.
        /// Does not validate negative radius or lifetime; index/caller must validate.
        /// </summary>
        public static Shape Capsule(P a, P b, double radius) => new Shape
        {
            A = a,
            B = b,
            Radius = radius,
            MinX = Math.Min(a.X, b.X) - radius,
            MaxX = Math.Max(a.X, b.X) + radius,
            MinZ = Math.Min(a.Z, b.Z) - radius,
            MaxZ = Math.Max(a.Z, b.Z) + radius
        };
        /// <summary>
        /// Create an axis-aligned rectangle from metre bounds; no clipping/repair.
        /// </summary>
        public static Shape Box(double x0, double z0, double x1, double z1) => new Shape
        {
            Rectangle = true,
            MinX = x0,
            MinZ = z0,
            MaxX = x1,
            MaxZ = z1
        };
        /// <summary>
        /// Copy a whole polygon array and compute its bounds without repairing it.
        /// No closure/simple-polygon validation is added to this legacy factory.
        /// </summary>
        public static Shape Poly(P[] p)
        {
            var shape = new Shape
            {
                Polygon = (P[])p.Clone(),
                MinX = double.MaxValue,
                MinZ = double.MaxValue,
                MaxX = double.MinValue,
                MaxZ = double.MinValue
            };
            foreach (var vertex in p)
            {
                shape.MinX = Math.Min(shape.MinX, vertex.X);
                shape.MaxX = Math.Max(shape.MaxX, vertex.X);
                shape.MinZ = Math.Min(shape.MinZ, vertex.Z);
                shape.MaxZ = Math.Max(shape.MaxZ, vertex.Z);
            }

            return shape;
        }
    }

    /// <summary>
    /// Planar distance, containment and intersection math; squared distances use m².
    /// Inputs must be finite and source-bound by the caller. These arithmetic helpers
    /// do not independently validate simple polygons or current owner/lifetime state.
    /// Near-boundary guards retain the exact original tolerances and comparisons.
    /// </summary>
    public static class Geometry2
    {
        // Existing numerical thresholds in square metres; comparisons retain
        // their strict/inclusive operators. These are arithmetic guards, not a
        // source quantization, terrain-fit or gameplay-clearance allowance.
        private const double MinimumProjectionLengthSquaredMetres = 1e-20;
        private const double PointOnSegmentDistanceSquaredMetres = 1e-16;
        private const double ParallelCrossMagnitudeSquareMetres = 1e-16;
        private const double DistanceSquaredGuardMetres = 1e-12;
        private const int MetadataTokensPerPolygonVertex = 3;
        static double DotProduct(P a, P b) => a.X * b.X + a.Z * b.Z;
        static double CrossProduct(P a, P b) => a.X * b.Z - a.Z * b.X;
        /// <summary>
        /// Return squared distance (m²) using clamped projection onto segment AB.
        /// A near-zero squared segment length uses endpoint A, preserving the original
        /// 1e-20m² denominator guard. Mathematical locals/legacy parameters stay intact.
        /// </summary>
        public static double PointSegment(P p, P a, P b)
        {
            var projectionVector = b - a;
            double denominator = DotProduct(projectionVector, projectionVector),
                t = denominator > MinimumProjectionLengthSquaredMetres ? Math.Max(0, Math.Min(1, DotProduct(p - a, projectionVector) / denominator)) : 0;
            var d = p - (a + projectionVector * t);
            return DotProduct(d, d);
        }

        static bool IsOnSegment(P p, P a, P b) => PointSegment(p, a, b) < PointOnSegmentDistanceSquaredMetres;
        /// <summary>
        /// Return squared distance (m²) between complete segments AB and CD.
        /// Cross-product parameter tests detect crossing/collinear contact; otherwise
        /// the minimum endpoint-to-segment projection covers the nonintersecting case.
        /// </summary>
        public static double SegmentDistance(P a, P b, P c, P d)
        {
            var ab = b - a;
            var cd = d - c;
            double denominator = CrossProduct(ab, cd);
            if (Math.Abs(denominator) > ParallelCrossMagnitudeSquareMetres)
            {
                double t = CrossProduct(c - a, cd) / denominator, u = CrossProduct(c - a, ab) / denominator;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                    return 0;
            }
            else if (IsOnSegment(a, c, d) || IsOnSegment(b, c, d) || IsOnSegment(c, a, b) || IsOnSegment(d, a, b))
                return 0;
            return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
        }

        static bool ContainsOrTouchesPoint(P p, P[] polygon)
        {
            bool isInside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                P a = polygon[i], b = polygon[j];
                if (IsOnSegment(p, a, b))
                    return true;
                if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X)
                    isInside = !isInside;
            }

            return isInside;
        }

        /// <summary>
        /// Test the whole query capsule against a rectangle, including containment.
        /// Radius is metres; the existing additive squared-distance guard stays exact.
        /// </summary>
        public static bool BoxHit(P a, P b, double radius, double x0, double z0, double x1, double z1)
        {
            if (a.X >= x0 && a.X <= x1 && a.Z >= z0 && a.Z <= z1 || b.X >= x0 && b.X <= x1 && b.Z >= z0 && b.Z <= z1)
                return true;
            double inflatedRadiusSquared = radius * radius + DistanceSquaredGuardMetres;
            P p = new P(x0, z0), q = new P(x1, z0), r = new P(x1, z1), s = new P(x0, z1);
            return SegmentDistance(a, b, p, q) <= inflatedRadiusSquared
                || SegmentDistance(a, b, q, r) <= inflatedRadiusSquared
                || SegmentDistance(a, b, r, s) <= inflatedRadiusSquared
                || SegmentDistance(a, b, s, p) <= inflatedRadiusSquared;
        }

        /// <summary>
        /// Spend metadata tokens and test a complete shape against capsule AB.
        /// Polygons use endpoint containment plus every edge, preserving concave gaps.
        /// Budget exhaustion is Unknown; caller must validate frame/geometry/radius and
        /// must not interpret Clear as current ground, habitat or actor admission.
        /// </summary>
        public static Verdict Hit(Shape s, P a, P b, double radius, WorkBudget work)
        {
            if (!work.Use(ref work.Metadata))
                return Verdict.Unknown;
            if (s.Rectangle)
                return BoxHit(a, b, radius, s.MinX, s.MinZ, s.MaxX, s.MaxZ) ? Verdict.Blocked : Verdict.Clear;
            if (s.Polygon == null)
                return SegmentDistance(a, b, s.A, s.B) <= (radius + s.Radius) * (radius + s.Radius) + DistanceSquaredGuardMetres ? Verdict.Blocked : Verdict.Clear;
            if (!work.Use(ref work.Metadata, s.Polygon.Length * MetadataTokensPerPolygonVertex))
                return Verdict.Unknown;
            if (ContainsOrTouchesPoint(a, s.Polygon) || ContainsOrTouchesPoint(b, s.Polygon))
                return Verdict.Blocked;
            for (int i = 0; i < s.Polygon.Length; i++)
                if (SegmentDistance(a, b, s.Polygon[i], s.Polygon[(i + 1) % s.Polygon.Length]) <= radius * radius + DistanceSquaredGuardMetres)
                    return Verdict.Blocked;
            return Verdict.Clear;
        }
    }

    /// <summary>
    /// Single-owner bounded broadphase plus complete shape predicates.
    /// Fully enumerate Add on a private preparation index, then call Finish. Query
    /// mutates epoch bookkeeping and a caller budget: serialize queries; no concurrent
    /// read/write or shared-thread use is supported. Failed remains a refused index.
    /// Ready only describes completed index math, not actual uploaded-ground readiness.
    /// This index has no800 scope authority: the semantic consumer separately gates
    /// exact current source/lease identity, whole capsule scope and actual support.
    /// </summary>
    public sealed class SafetyIndex
    {
        private const double MaximumCoordinateMagnitudeMetres = 10000000;
        private const double MaximumShapeRadiusMetres = 64;
        private const int MaximumPolygonVertices = 512;
        private const int MaximumCellsPerShape = 4096;
        private const double MinimumQueryRadiusMetres = .01;
        private const double MaximumQueryRadiusMetres = 1.5;
        private const double MaximumCorridorLengthSquaredMetres = 64.00001;
        private const int MaximumQueryReferenceVisits = 8192;
        // Existing public resource caps and32m broadphase cell size. They bound
        // private index work only; no current terrain/world coverage is implied.
        public const int MaxShapes = 200000, MaxReferences = 1000000, MaxCells = 100000, MaxCandidates = 2048, MaxQueryCells = 64;
        public const double Cell = 32;
        readonly List<Shape> indexedShapes = new List<Shape>();
        readonly Dictionary<(int, int), List<int>> cellShapeIndices = new Dictionary<(int, int), List<int>>();
        int referenceCount, queryEpoch;
        int[] lastVisitedEpochByShape;
        public bool Ready { get; private set; }
        public bool Failed { get; private set; }
        public int ShapeCount => indexedShapes.Count;
        public int References => referenceCount;

        static bool IsFiniteBounded(double n) => !double.IsNaN(n) && !double.IsInfinity(n) && Math.Abs(n) < MaximumCoordinateMagnitudeMetres;
        /// <summary>
        /// Lazily borrow one shape and insert each covered cell reference.
        /// Fully enumerate before Finish. Each yield is one added reference work unit.
        /// Invalid bounds/shape/resource limits mark Failed, including partial insertion;
        /// a failed index must never be finished/published as an empty safe result.
        /// </summary>
        public IEnumerable<int> Add(Shape s)
        {
            if (Failed
                || Ready
                || s == null
                || indexedShapes.Count >= MaxShapes
                || !IsFiniteBounded(s.MinX)
                || !IsFiniteBounded(s.MinZ)
                || !IsFiniteBounded(s.MaxX)
                || !IsFiniteBounded(s.MaxZ)
                || s.MinX > s.MaxX
                || s.MinZ > s.MaxZ
                || s.Radius < 0
                || s.Radius > MaximumShapeRadiusMetres
                || s.Polygon != null
                && (s.Polygon.Length < 3
                || s.Polygon.Length > MaximumPolygonVertices))
            {
                Failed = true;
                yield break;
            }

            int x0 = (int)Math.Floor(s.MinX / Cell), x1 = (int)Math.Floor(s.MaxX / Cell), z0 = (int)Math.Floor(s.MinZ / Cell), z1 = (int)Math.Floor(s.MaxZ / Cell);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaximumCellsPerShape)
            {
                Failed = true;
                yield break;
            }

            int id = indexedShapes.Count;
            indexedShapes.Add(s);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (++referenceCount > MaxReferences)
                    {
                        Failed = true;
                        yield break;
                    }

                    var key = (x, z);
                    if (!cellShapeIndices.TryGetValue(key, out var list))
                    {
                        if (cellShapeIndices.Count >= MaxCells)
                        {
                            Failed = true;
                            yield break;
                        }

                        cellShapeIndices.Add(key, list = new List<int>());
                    }

                    list.Add(id);
                    yield return 1;
                }
        }

        /// <summary>
        /// Allocate query epoch marks only if not failed; Ready is index completion.
        /// No new source/ground authority or actual Unity owner readiness is established.
        /// </summary>
        public void Finish()
        {
            if (!Failed)
            {
                lastVisitedEpochByShape = new int[indexedShapes.Count];
                Ready = true;
            }
        }

        /// <summary>
        /// Return bounded shape intersection for a capsule: radius0.01–1.5m and
        /// legacy length² cap64.00001m², with explicit cells/references/candidate budgets.
        /// No owned800 domain/ground existence is checked here. Epochs de-duplicate
        /// shapes shared across cells; overflow clears marks before the next query.
        /// </summary>
        public Verdict Query(P a, P b, double radius, WorkBudget work)
        {
            if (!Ready
                || Failed
                || work == null
                || !work.Use(ref work.Queries)
                || !IsFiniteBounded(a.X)
                || !IsFiniteBounded(a.Z)
                || !IsFiniteBounded(b.X)
                || !IsFiniteBounded(b.Z)
                || !IsFiniteBounded(radius)
                || radius < MinimumQueryRadiusMetres
                || radius > MaximumQueryRadiusMetres
                || (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) > MaximumCorridorLengthSquaredMetres)
                return Verdict.Unknown;
            int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - radius) / Cell),
                x1 = (int)Math.Floor((Math.Max(a.X, b.X) + radius) / Cell),
                z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - radius) / Cell),
                z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + radius) / Cell);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaxQueryCells)
                return Verdict.Unknown;
            if (queryEpoch == int.MaxValue)
            {
                Array.Clear(lastVisitedEpochByShape, 0, lastVisitedEpochByShape.Length);
                queryEpoch = 0;
            }

            queryEpoch++;
            int candidateCount = 0, referenceVisits = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!cellShapeIndices.TryGetValue((x, z), out var list))
                        continue;
                    foreach (int id in list)
                    {
                        if (++referenceVisits > MaximumQueryReferenceVisits)
                            return Verdict.Unknown;
                        if (lastVisitedEpochByShape[id] == queryEpoch)
                            continue;
                        lastVisitedEpochByShape[id] = queryEpoch;
                        if (++candidateCount > MaxCandidates)
                            return Verdict.Unknown;
                        var hit = Geometry2.Hit(indexedShapes[id], a, b, radius, work);
                        if (hit != Verdict.Clear)
                            return hit;
                    }
                }

            return Verdict.Clear;
        }
    }

    /// <summary>
    /// Immutable 3D point in caller-bound metres. Flat discards Y only;
    /// it does not transform frames, query terrain or establish source identity.
    /// </summary>
    public readonly struct V
    {
        public readonly double X, Y, Z;
        /// <summary>
        /// Store 3D metre coordinates unchanged; no ground/frame inference.
        /// </summary>
        public V(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public P Flat => new P(X, Z);
    }

    /// <summary>
    /// Legacy mathematical result: height/Min/Max in metres, NormalY dimensionless.
    /// Min/Max are intersecting triangle-vertex extrema, not exact footprint extrema.
    /// This value does not carry an actual Core800 ground lease or publication witness.
    /// </summary>
    public readonly struct Support
    {
        public readonly double Height, NormalY, Min, Max;
        /// <summary>
        /// Store legacy scalar height/extrema (metres) and upward normal component.
        /// </summary>
        public Support(double h, double ny, double min, double max)
        {
            Height = h;
            NormalY = ny;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// Legacy triangle accumulator; NOT an active Core800 ground authority.
    /// One caller owns/reset/add/finish sequencing; mutable public state is retained.
    /// Centre/ContainsCentre are historic API names: they refer to the FROM endpoint,
    /// not the corridor midpoint. Add does not prove complete mesh or bank coverage.
    /// Finish compares a supplied height scalar; that scalar alone cannot issue an
    /// actual-source, uploaded-buffer, actor or safe-placement capability.
    /// </summary>
    public sealed class TriangleSupport
    {
        // Legacy numerical/slope policy retained unchanged. Barycentric tolerance
        // is dimensionless; area/normal guards are m²; height/span guards are m.
        private const double MinimumProjectedTriangleAreaSquareMetres = 1e-16;
        private const double BarycentricBoundaryTolerance = 1e-9;
        private const double DistanceSquaredGuardMetres = 1e-12;
        private const double MinimumNormalMagnitudeSquareMetres = 1e-12;
        private const double MaximumHeightDisagreementMetres = .001;
        private const double MinimumUpNormalComponent = .9063;
        private const double MaximumStandingHeightSpanMetres = .35;
        public double Min = double.MaxValue, Max = double.MinValue, MinNormal = 1, Centre;
        public int Intersections;
        public bool ContainsCentre, Invalid;
        /// <summary>
        /// Reset this caller-owned legacy accumulator before another request.
        /// </summary>
        public void Reset()
        {
            Min = double.MaxValue;
            Max = double.MinValue;
            MinNormal = 1;
            Centre = 0;
            Intersections = 0;
            ContainsCentre = false;
            Invalid = false;
        }

        static double CrossProduct(P a, P b) => a.X * b.Z - a.Z * b.X;
        static bool ContainsPointInclusive(P p, P a, P b, P c)
        {
            double den = CrossProduct(b - a, c - a);
            if (Math.Abs(den) < MinimumProjectedTriangleAreaSquareMetres)
                return false;
            double v = CrossProduct(p - a, c - a) / den, w = CrossProduct(b - a, p - a) / den;
            return v >= -BarycentricBoundaryTolerance && w >= -BarycentricBoundaryTolerance && v + w <= 1 + BarycentricBoundaryTolerance;
        }

        /// <summary>
        /// Spend a triangle token, test projected capsule overlap and accumulate
        /// upward-facing triangle slope/heights. Cross products use AB and AC; their
        /// magnitude has square-metre units. Reject degenerate/downward faces, and
        /// refuse conflicting heights at FROM by the original1mm agreement guard.
        /// No final-mesh completeness, current owner or bank exclusion is proved.
        /// </summary>
        public void Add(V a, V b, V c, P from, P to, double radius, WorkBudget work)
        {
            if (Invalid)
                return;
            if (!work.Use(ref work.Triangles))
            {
                Invalid = true;
                return;
            }

            bool hit = ContainsPointInclusive(from, a.Flat, b.Flat, c.Flat)
                || ContainsPointInclusive(to, a.Flat, b.Flat, c.Flat)
                || Geometry2.SegmentDistance(from, to, a.Flat, b.Flat) <= radius * radius + DistanceSquaredGuardMetres
                || Geometry2.SegmentDistance(from, to, b.Flat, c.Flat) <= radius * radius + DistanceSquaredGuardMetres
                || Geometry2.SegmentDistance(from, to, c.Flat, a.Flat) <= radius * radius + DistanceSquaredGuardMetres;
            if (!hit)
                return;
            double ax = b.X - a.X, ay = b.Y - a.Y, az = b.Z - a.Z, bx = c.X - a.X, by = c.Y - a.Y, bz = c.Z - a.Z;
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx, norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (double.IsNaN(norm) || double.IsInfinity(norm) || norm < MinimumNormalMagnitudeSquareMetres || ny <= 0)
            {
                Invalid = true;
                return;
            }

            double normal = ny / norm;
            MinNormal = Math.Min(MinNormal, normal);
            Intersections++;
            Min = Math.Min(Min, Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            Max = Math.Max(Max, Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            if (ContainsPointInclusive(from, a.Flat, b.Flat, c.Flat))
            {
                double h = a.Y - (nx * (from.X - a.X) + nz * (from.Z - a.Z)) / ny;
                if (ContainsCentre && Math.Abs(Centre - h) > MaximumHeightDisagreementMetres)
                {
                    Invalid = true;
                    return;
                }

                ContainsCentre = true;
                Centre = h;
            }
        }

        /// <summary>
        /// Compare supplied height to FROM within1mm, then apply existing0.9063
        /// up-normal threshold (approximately25degrees) and0.35m standing span rule.
        /// The slope ratio is sqrt(1/normalY²−1); diameter times slope estimates span.
        /// This unchanged legacy test is not an active Core800 placement policy.
        /// </summary>
        public Verdict Finish(double radius, bool standing, double admittedApiHeight, out Support support)
        {
            support = default;
            if (Invalid
                || !ContainsCentre
                || Intersections == 0
                || double.IsNaN(admittedApiHeight)
                || double.IsInfinity(admittedApiHeight)
                || Math.Abs(Centre - admittedApiHeight) > MaximumHeightDisagreementMetres)
                return Verdict.Unknown;
            double gradient = Math.Sqrt(Math.Max(0, 1 / (MinNormal * MinNormal) - 1));
            if (MinNormal <= MinimumUpNormalComponent || standing && gradient * 2 * radius > MaximumStandingHeightSpanMetres)
                return Verdict.Blocked;
            support = new Support(admittedApiHeight, MinNormal, Min, Max);
            return Verdict.Clear;
        }
    }

    /// <summary>
    /// Legacy caller-provided chunk predicate, not a Core800 uploaded-owner lease.
    /// A true response does not prove final-buffer support or source lifetime.
    /// </summary>
    public interface IChunkReadiness
    {
        bool ChunkReady(int x, int z);
    }

    /// <summary>
    /// Inclusive legacy integer chunk-key rectangle; not metre coordinates.
    /// Fields and constructor order remain unchanged for API compatibility.
    /// </summary>
    public readonly struct ChunkRange
    {
        public readonly int X0, X1, Z0, Z1;
        /// <summary>
        /// Store inclusive legacy chunk keys unchanged.
        /// </summary>
        public ChunkRange(int x0, int x1, int z0, int z1)
        {
            X0 = x0;
            X1 = x1;
            Z0 = z0;
            Z1 = z1;
        }
    }

    /// <summary>
    /// Legacy320m chunk broadphase helper, NOT Core800 whole-footprint support.
    /// Only the radius is validated by Plan; callers retain coordinate validation.
    /// Complete checks caller booleans, not triangles, buffers, banks or ownership.
    /// </summary>
    public static class ChunkCoverage
    {
        // Fixed legacy layout, retained for compatibility and not an800 policy.
        private const double LegacyChunkSideMetres = 320;
        private const double MaximumQueryRadiusMetres = 1.5;
        private const int MaximumChunksPerQuery = 4;
        /// <summary>
        /// Bound a capsule by inclusive legacy320m chunk keys; reject invalid radius
        /// or more than4chunk cells. Does not validate all coordinates or actual ground.
        /// </summary>
        public static bool Plan(P a, P b, double r, out ChunkRange range)
        {
            range = default;
            if (double.IsNaN(r) || double.IsInfinity(r) || r <= 0 || r > MaximumQueryRadiusMetres)
                return false;
            int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - r) / LegacyChunkSideMetres),
                x1 = (int)Math.Floor((Math.Max(a.X, b.X) + r) / LegacyChunkSideMetres),
                z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - r) / LegacyChunkSideMetres),
                z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + r) / LegacyChunkSideMetres);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaximumChunksPerQuery)
                return false;
            range = new ChunkRange(x0, x1, z0, z1);
            return true;
        }

        /// <summary>
        /// Require every inclusive key from the caller predicate; no buffer authority.
        /// </summary>
        public static bool Complete(ChunkRange r, IChunkReadiness source)
        {
            for (int z = r.Z0; z <= r.Z1; z++)
                for (int x = r.X0; x <= r.X1; x++)
                    if (!source.ChunkReady(x, z))
                        return false;
            return true;
        }
    }

    /// <summary>
    /// Legacy regular-grid index/coordinate arithmetic; NOT Core800 adaptive mesh
    /// topology or its final-buffer owner. Expected/Coordinate require valid caller
    /// indices and nonzero subdivision count; only CompleteIndices validates n.
    /// No runtime/source/coverage authority is introduced by retaining these helpers.
    /// </summary>
    public static class RegularGridTopology
    {
        // Fixed legacy triangle layout; integer order and binary32 arithmetic
        // remain unchanged. Core800 has a distinct actual final-buffer contract.
        private const int IndicesPerGridCell = 6;
        private const int LegacyChunkSideMetres = 320;
        private const int MaximumGridSubdivisions = 256;
        /// <summary>
        /// Return one index of two legacy grid triangles per cell (6indices/cell).
        /// The winding/order is retained exactly; caller supplies valid n and j.
        /// </summary>
        public static int Expected(int n, int j)
        {
            int cell = j / IndicesPerGridCell, x = cell % n, z = cell / n, a = z * (n + 1) + x;
            return j % IndicesPerGridCell == 0 ? a
                : j % IndicesPerGridCell == 1 ? a + n + 1
                : j % IndicesPerGridCell == 2 ? a + 1
                : j % IndicesPerGridCell == 3 ? a + 1
                : j % IndicesPerGridCell == 4 ? a + n + 1
                : a + n + 2;
        }

        /// <summary>
        /// Compare numeric float equality exactly as before; signed zeros compare equal.
        /// </summary>
        public static bool SharedHeight(float a, float b) => a == b;
        /// <summary>
        /// Evaluate the original binary32 legacy chunk/subdivision coordinate order.
        /// Not a Core800 seam or transformed/fitted source coordinate generator.
        /// </summary>
        public static float Coordinate(int key, int step, int n) => (key + (float)step / n) * LegacyChunkSideMetres;
        /// <summary>
        /// Validate n1–256 and the complete exact legacy regular-grid index sequence.
        /// </summary>
        public static bool CompleteIndices(IReadOnlyList<int> indices, int n)
        {
            if (n < 1 || n > MaximumGridSubdivisions || indices == null || indices.Count != n * n * IndicesPerGridCell)
                return false;
            for (int j = 0; j < indices.Count; j++)
                if (indices[j] != Expected(n, j))
                    return false;
            return true;
        }
    }

    /// <summary>
    /// Legacy scalar bounding sphere derived from caller-supplied vehicle dimensions
    /// in metres. It has no live body/pose/collider/registry/frame/fixed-step binding.
    /// Do not substitute it for the separate complete actual actor snapshot consumer.
    /// </summary>
    public readonly struct VehicleEnvelope
    {
        private const double MaximumEnvelopeRadiusMetres = 12;
        private const double DistanceSquaredGuardMetres = 1e-10;
        public readonly double Radius;
        /// <summary>
        /// Derive a conservative sphere from maximum half-width, longitudinal reach
        /// including abs(centreZ), and vertical reach. Dimensions/radius are metres;
        /// unknown/nonpositive dimensions or radius above12m throw. No actor is issued.
        /// </summary>
        public VehicleEnvelope(double bodyWidth, double mirrors, double colliderWidth, double bodyLength, double colliderLength, double centreZ, double verticalReach)
        {
            double maximumHalfWidthMetres = Math.Max(bodyWidth, Math.Max(mirrors, colliderWidth)) * .5;
            double maximumLongitudinalReachMetres = Math.Max(bodyLength, colliderLength) * .5 + Math.Abs(centreZ);
            Radius = Math.Sqrt(maximumHalfWidthMetres * maximumHalfWidthMetres + maximumLongitudinalReachMetres * maximumLongitudinalReachMetres + verticalReach * verticalReach);
            if (double.IsNaN(Radius)
                || double.IsInfinity(Radius)
                || verticalReach <= 0
                || bodyWidth <= 0
                || mirrors <= 0
                || colliderWidth <= 0
                || bodyLength <= 0
                || colliderLength <= 0
                || Radius <= 0
                || Radius > MaximumEnvelopeRadiusMetres)
                throw new ArgumentException("Unknown catalogue vehicle envelope");
        }

        /// <summary>
        /// Test this scalar sphere against a query capsule with unchanged distance guard.
        /// Inputs have no pose/source authority and are not independently validated here.
        /// </summary>
        public bool Occupies(P from, P to, double actorRadius, P car)
            => Geometry2.PointSegment(car, from, to) <= (Radius + actorRadius) * (Radius + actorRadius) + DistanceSquaredGuardMetres;
    }

    /// <summary>
    /// Legacy reference/generation bookkeeping only. Public mutable fields and
    /// unchecked serial increments remain unchanged; one caller serializes access.
    /// Reference matches do not mint source/ground/Town/actor capability or liveness.
    /// </summary>
    public sealed class EpochGuard
    {
        public object Region, Owner;
        public int Generation;
        public long Serial;
        /// <summary>
        /// Replace stored refs/generation and increment serial when binding differs.
        /// Return whether the existing binding matched; no owner liveness is queried.
        /// </summary>
        public bool Begin(object region, int generation, object owner = null)
        {
            bool sameBinding = ReferenceEquals(region, Region) && ReferenceEquals(owner, Owner) && Generation == generation;
            if (!sameBinding)
            {
                Region = region;
                Owner = owner;
                Generation = generation;
                Serial++;
            }

            return sameBinding;
        }

        /// <summary>
        /// Compare only refs/generation/serial; caller owns actual lifetime validation.
        /// </summary>
        public bool Valid(object region, int generation, long serial, object owner = null)
            => ReferenceEquals(region, Region)
            && ReferenceEquals(owner, Owner)
            && generation == Generation
            && serial == Serial;
    }
}
