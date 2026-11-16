using System;
using System.Collections.Generic;
using System.Threading;
using EarthUnscripted.FunnyAnimals.Admission;

namespace EarthUnscripted.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Intersected source-tag categories. None means no tag, not safe habitat;
    /// combinations preserve overlapping original polygons without inventing a
    /// preferred category. These values are not a persisted/network schema.
    /// </summary>
    [Flags]
    public enum AreaFacts800
    {
        None = 0, WaterHazard = 1, GrassAgriculture = 2, Woodland = 4,
        Urban = 8, OtherKnownDry = 16, Unknown = 32
    }

    /// <summary>
    /// Builds immutable original land-use facts in anchored world XZ metres.
    /// Bounds accelerate lookup; full polygon edges and containment decide facts.
    /// </summary>
    /// <remarks>
    /// Build exclusively on one worker, then publish the completed object to the
    /// main thread. Query stamps and WorkBudget are mutable: concurrent query or
    /// build/query access is unsupported. This model issues neither a source
    /// capability nor habitat/gameplay authority. Unknown input is not empty land.
    /// </remarks>
    public sealed class AreaReadModel800
    {
        // Existing source-envelope defence limits, shared with the source-hazard
        // builder's original policy. They are counts, not world dimensions.
        const int MaximumSourceAreas = 2000;
        const int MaximumSourceAreaVertices = 500000;

        // One micrometre of additional inward boundary clearance preserves the
        // functional-v4 conservative geometry predicate. It does not approximate
        // native collider precision or establish a terrain error bound.
        const double BoundaryClearanceGuardMetres = .000001;

        readonly Dictionary<AreaKind800, SafetyIndex> categoryIndexes = new Dictionary<AreaKind800, SafetyIndex>();
        readonly List<(AreaKind800 Kind, Shape Polygon)> originalPolygons = new List<(AreaKind800, Shape)>();
        bool isReady, isRefused;
        int areaCount, vertexCount;
        readonly SafetyIndex emptyQueryIndex = new SafetyIndex();
        /// <summary>Creates an unready builder with no inferred source features.</summary>
        public AreaReadModel800()
        {
            emptyQueryIndex.Finish();
        }
        /// <summary>Number of original polygon records successfully added.</summary>
        public int Count => areaCount;
        /// <summary>Total spatial-index references across all categories.</summary>
        public int References
        {
            get
            {
                int sum = 0;
                foreach (var i in categoryIndexes.Values)
                    sum += i.References;
                return sum;
            }
        }
        /// <summary>
        /// Copies and classifies one complete original polygon. Kind strings are
        /// exact source values; geometry remains in source XZ metres. Invalid,
        /// unsupported or over-budget input throws and makes this builder refused.
        /// The caller retains its input array and can subsequently change it.
        /// </summary>
        public void Add(string exactSourceKind, P[] originalPolygon, CancellationToken cancellation = default)
        {
            if (isReady || isRefused)
                throw new InvalidOperationException("Area model is frozen/refused");
            isRefused = true;
            if (++areaCount > MaximumSourceAreas || originalPolygon == null
                || (vertexCount += originalPolygon.Length) > MaximumSourceAreaVertices
                || !PolygonFootprint800.TryCreate(originalPolygon, cancellation, out var shape))
                throw new InvalidOperationException("Unsupported complete area geometry; no empty fallback");
            var kind = AreaTaxonomy800.Classify(exactSourceKind);
            if (!categoryIndexes.TryGetValue(kind, out var index))
                categoryIndexes.Add(kind, index = new SafetyIndex());
            foreach (var unit in index.Add(shape))
                cancellation.ThrowIfCancellationRequested();
            if (index.Failed || References > SafetyIndex.MaxReferences)
                throw new InvalidOperationException("Area model aggregate reference budget");
            originalPolygons.Add((kind, shape));
            isRefused = false;
        }
        /// <summary>
        /// Freezes all category indexes. Further Add/Finish calls throw; failure
        /// never publishes a partial set. No source or Unity object is modified.
        /// </summary>
        public void Finish()
        {
            if (isRefused || isReady)
                throw new InvalidOperationException("Area model is refused/already frozen");
            foreach (var index in categoryIndexes.Values)
            {
                index.Finish();
                if (!index.Ready || index.Failed)
                {
                    isRefused = true;
                    throw new InvalidOperationException("Incomplete area model");
                }
            }
            isReady = true;
        }
        static AreaFacts800 Fact(AreaKind800 kind)
        {
            switch (kind)
            {
                case AreaKind800.WaterHazard:
                    return AreaFacts800.WaterHazard;
                case AreaKind800.GrassAgriculture:
                    return AreaFacts800.GrassAgriculture;
                case AreaKind800.Woodland:
                    return AreaFacts800.Woodland;
                case AreaKind800.Urban:
                    return AreaFacts800.Urban;
                case AreaKind800.OtherKnownDry:
                    return AreaFacts800.OtherKnownDry;
                default:
                    return AreaFacts800.Unknown;
            }
        }
        /// <summary>
        /// Returns Unknown when the capsule intersects an unsupported source kind
        /// or the query cannot finish. Clear concerns unknown-area tags only, not
        /// roads, water, buildings, terrain, actors or species suitability.
        /// </summary>
        public Verdict InspectUnknown(P from, P to, double radius, WorkBudget work)
        {
            if (!isReady || isRefused || work == null)
                return Verdict.Unknown;
            if (!categoryIndexes.TryGetValue(AreaKind800.Unknown, out var index))
                return emptyQueryIndex.Query(from, to, radius, work);
            return index.Query(from, to, radius, work) == Verdict.Clear ? Verdict.Clear : Verdict.Unknown;
        }
        /// <summary>
        /// Collects every intersected original category under a caller-owned work
        /// budget. False means incomplete/unknown, so partial output cannot admit
        /// placement. Intersection facts do not prove whole-capsule coverage.
        /// The geometry-only input range may exceed the admitted rectangle; the
        /// SourceHazards800 consumer separately enforces its issued scope.
        /// </summary>
        public bool TryInspect(P from, P to, double radius, WorkBudget work, out AreaFacts800 facts)
        {
            facts = AreaFacts800.None;
            if (!isReady || isRefused || work == null || !work.Use(ref work.Queries))
                return false;
            // Validate even when no source area covers the query. None reports
            // absence of an area tag, never absence of water/buildings/actors.
            if (double.IsNaN(radius) || double.IsInfinity(radius)
                || radius < SourceScope800.MinimumQueryRadiusMetres || radius > SourceScope800.MaximumQueryRadiusMetres
                || double.IsNaN(from.X) || double.IsInfinity(from.X) || double.IsNaN(from.Z) || double.IsInfinity(from.Z)
                || double.IsNaN(to.X) || double.IsInfinity(to.X) || double.IsNaN(to.Z) || double.IsInfinity(to.Z)
                || Math.Abs(from.X) > SourceScope800.MaximumSourceCoordinateMagnitudeMetres
                || Math.Abs(from.Z) > SourceScope800.MaximumSourceCoordinateMagnitudeMetres
                || Math.Abs(to.X) > SourceScope800.MaximumSourceCoordinateMagnitudeMetres
                || Math.Abs(to.Z) > SourceScope800.MaximumSourceCoordinateMagnitudeMetres
                || (from.X - to.X) * (from.X - to.X) + (from.Z - to.Z) * (from.Z - to.Z)
                    > SourceScope800.MaximumCorridorLengthMetres * SourceScope800.MaximumCorridorLengthMetres)
                return false;
            foreach (var pair in categoryIndexes)
            {
                var result = pair.Value.Query(from, to, radius, work);
                if (result == Verdict.Unknown)
                    return false;
                if (result == Verdict.Blocked)
                    facts |= Fact(pair.Key);
            }
            return (facts & AreaFacts800.Unknown) == 0;
        }
        /// <summary>
        /// Separately checks whether one whole capsule lies inside one original
        /// polygon of the required known kind, with boundary clearance. A true
        /// return means evaluation completed; inside reports the geometric result.
        /// False means unready/unknown/invalid/over-budget, never a positive proof.
        /// </summary>
        /// <remarks>
        /// Both endpoints must lie inside the simple polygon, and every polygon
        /// edge must stay farther than the expanded radius from the centre segment.
        /// This prevents a segment crossing a concave gap even when its endpoints
        /// lie inside. Separate polygons are not joined into a new source union.
        /// Distances are metres; the small guard is defined above. Terrain, actor
        /// and species suitability remain separate from this geometry witness.
        /// </remarks>
        public bool TryWholeCapsuleInside(AreaKind800 requiredKind, P from, P to,
            double radius, WorkBudget work, out bool inside)
        {
            inside = false;
            if (!isReady || isRefused || work == null || !work.Use(ref work.Queries)
                || requiredKind == AreaKind800.Unknown || !SourceScope800.Allows(from, to, radius))
                return false;
            double clearanceSquared = (radius + BoundaryClearanceGuardMetres) * (radius + BoundaryClearanceGuardMetres);
            foreach (var areaRecord in originalPolygons)
            {
                if (!work.Use(ref work.Metadata))
                    return false;
                if (areaRecord.Kind != requiredKind)
                    continue;
                var sourcePolygon = areaRecord.Polygon;
                if (Math.Min(from.X, to.X) - radius < sourcePolygon.MinX || Math.Max(from.X, to.X) + radius > sourcePolygon.MaxX
                    || Math.Min(from.Z, to.Z) - radius < sourcePolygon.MinZ || Math.Max(from.Z, to.Z) + radius > sourcePolygon.MaxZ)
                    continue;
                var fromContainment = Geometry2.Hit(sourcePolygon, from, from, 0, work);
                var toContainment = Geometry2.Hit(sourcePolygon, to, to, 0, work);
                if (fromContainment == Verdict.Unknown || toContainment == Verdict.Unknown)
                    return false;
                if (fromContainment != Verdict.Blocked || toContainment != Verdict.Blocked)
                    continue;
                bool crosses = false;
                for (int i = 0; i < sourcePolygon.Polygon.Length; i++)
                {
                    if (!work.Use(ref work.Metadata))
                        return false;
                    if (Geometry2.SegmentDistance(from, to, sourcePolygon.Polygon[i], sourcePolygon.Polygon[(i + 1) % sourcePolygon.Polygon.Length])
                        <= clearanceSquared)
                    {
                        crosses = true;
                        break;
                    }
                }
                if (!crosses)
                {
                    inside = true;
                    return true;
                }
            }
            return true;
        }
    }
}
