using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoadRush.FunnyAnimals.Admission
{
    /// <summary>
    /// Lazily build legacy source-road/water/raster/footprint shapes in X/Z metres.
    /// This utility reads supplied GeoRegion arrays without mutation; caller owns
    /// a coherent private source snapshot, schema/provenance/lifetime and indexing.
    /// Fully enumerate and handle refusal before publication. The separately reviewed
    /// Core800 builder controls exact canonical clone/digests/scope and whole polygons.
    /// No source/ground/actor/Town capability or complete water taxonomy is issued.
    /// </summary>
    public static class RegionIndexBuilder
    {
        // Existing defensive array/vertex/raster counts, not observed source
        // totals. All data must fit; no feature thinning/partial success is added.
        private const int MaximumLineFeatures = 100000;
        private const int MaximumBuildingFeatures = 50000;
        private const int MaximumPolygonVertices = 512;
        private const int MaximumRoadWidthMetres = 80;
        private const int MaximumWaterWidthMetres = 128;
        // Unchanged legacy source-shape expansion, in metres. The1.51m road
        // margin is not proof of current bank/pavement/actor safety; those gates
        // remain separate. Water/raster guards preserve existing rounding policy.
        private const double RoadExtraClearanceMetres = 1.51;
        private const double WaterLineGuardMetres = .01;
        private const double RasterAbsoluteGuardMetres = .01;
        // Dimensionless scale term for regional-coordinate rounding magnitude.
        private const double RasterRelativeGuard = 1e-6;
        private const int MaximumSegmentPieceLengthMetres = 32;
        private const int MaximumSegmentPieces = 4096;
        const int MaximumSourceVertices = 500000, MaximumLandRasterSamples = 2000000;
        static bool IsFiniteNumber(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        /// <summary>
        /// Read source arrays with unchanged caps/schema/width checks. Y is ignored.
        /// Original or explicitly prepared widths are accepted only by the existing
        /// schema branch; this function does not mint prepared/source authority.
        /// Yield0 marks incremental metadata progress; index yields are reference units.
        /// Invalid source, source/index caps or unsupported widths throw. Caller must
        /// fully enumerate and refuse partial work, then Finish its private index.
        /// No Unity objects are touched here; supplied arrays/index must not be mutated
        /// concurrently. Cancellation/worker scheduling belong to the enumerating owner.
        /// </summary>
        public static IEnumerable<int> Build(GeoRegion r, SafetyIndex index)
        {
            if ((r.roadWidthSchema != 0 && r.roadWidthSchema != 1) || r.roadWidthSchema == 1 && r.PreparedRoadWidths == null)
                throw new InvalidDataException("Unsupported prepared terrain/width schema");
            if (r.roads == null
                || r.waters == null
                || r.buildings == null
                || r.roads.Length > MaximumLineFeatures
                || r.waters.Length > MaximumLineFeatures
                || r.buildings.Length > MaximumBuildingFeatures
                || !IsFiniteNumber(r.minX)
                || !IsFiniteNumber(r.minZ)
                || !IsFiniteNumber(r.width)
                || !IsFiniteNumber(r.depth)
                || r.width <= 0
                || r.depth <= 0)
                throw new InvalidDataException("Admitted source bounds/arrays");
            int sourceVertexCount = 0;
            foreach (var road in r.roads)
            {
                yield return 0;
                if (road == null || road.points == null || road.points.Length < 2)
                    throw new InvalidDataException("Road geometry");
                sourceVertexCount += road.points.Length;
                if (sourceVertexCount > MaximumSourceVertices)
                    throw new InvalidDataException("Source vertex cap");
                float maximumRoadWidthMetres = r.MaximumRoadWidth(road);
                if (!IsFiniteNumber(maximumRoadWidthMetres) || maximumRoadWidthMetres <= 0 || maximumRoadWidthMetres > MaximumRoadWidthMetres)
                    throw new InvalidDataException("Admitted maximum width");
                for (int i = 1; i < road.points.Length; i++)
                    foreach (var shape in SplitSourceSegment(road.points[i - 1], road.points[i], maximumRoadWidthMetres * .5 + RoadExtraClearanceMetres))
                        foreach (int unit in AddWithinRegion(r, index, shape))
                            yield return unit;
            }

            foreach (var water in r.waters)
            {
                yield return 0;
                if (water == null || water.points == null || water.points.Length < 2 || !IsFiniteNumber(water.width) || water.width < 0 || water.width > MaximumWaterWidthMetres)
                    throw new InvalidDataException("River geometry");
                sourceVertexCount += water.points.Length;
                if (sourceVertexCount > MaximumSourceVertices)
                    throw new InvalidDataException("Source vertex cap");
                for (int i = 1; i < water.points.Length; i++)
                    foreach (var shape in SplitSourceSegment(water.points[i - 1], water.points[i], water.width * .5 + WaterLineGuardMetres))
                        foreach (int unit in AddWithinRegion(r, index, shape))
                            yield return unit;
            }

            foreach (var b in r.buildings)
            {
                yield return 0;
                if (b == null || b.points == null || b.points.Length < 3 || b.points.Length > MaximumPolygonVertices)
                    throw new InvalidDataException("Building footprint");
                sourceVertexCount += b.points.Length;
                if (sourceVertexCount > MaximumSourceVertices)
                    throw new InvalidDataException("Source vertex cap");
                var polygon = new P[b.points.Length];
                for (int i = 0; i < polygon.Length; i++)
                {
                    polygon[i] = new P(b.points[i].x, b.points[i].z);
                    yield return 0;
                }

                foreach (int unit in AddWithinRegion(r, index, Shape.Poly(polygon)))
                    yield return unit;
            }

            // Preserve ONLY this legacy raster-code4 contribution. An absent or
            // invalid raster contributes no shapes here; that is not proof of dry
            // land. Actual reviewed canonical water is often code1, so Core800's
            // separate complete water polygons remain mandatory. Known/unknown
            // area facts belong to the semantic builder, not this raster utility.
            if (r.landCover != null && r.landNx >= 2 && r.landNz >= 2 && (long)r.landNx * r.landNz == r.landCover.Length)
            {
                if (r.landCover.Length > MaximumLandRasterSamples)
                    throw new InvalidDataException("Raster cap");
                double stepXMetres = r.width / (double)(r.landNx - 1), stepZMetres = r.depth / (double)(r.landNz - 1);
                double guardBandMetres = RasterAbsoluteGuardMetres
                    + RasterRelativeGuard * Math.Max(Math.Max(Math.Abs(r.minX), Math.Abs(r.minZ)), Math.Max(Math.Abs(r.minX + r.width), Math.Abs(r.minZ + r.depth)));
                for (int z = 0; z < r.landNz; z++)
                    for (int x = 0; x < r.landNx; x++)
                    {
                        yield return 0;
                        if (r.landCover[z * r.landNx + x] != 4)
                            continue;
                        int start = x;
                        while (x + 1 < r.landNx && r.landCover[z * r.landNx + x + 1] == 4)
                        {
                            x++;
                            yield return 0;
                        }

                        var shape = Shape.Box(
                            Math.Max(r.minX, r.minX + (start - .5) * stepXMetres - guardBandMetres),
                            Math.Max(r.minZ, r.minZ + (z - .5) * stepZMetres - guardBandMetres),
                            Math.Min(r.minX + r.width, r.minX + (x + .5) * stepXMetres + guardBandMetres),
                            Math.Min(r.minZ + r.depth, r.minZ + (z + .5) * stepZMetres + guardBandMetres));
                        foreach (int unit in AddWithinRegion(r, index, shape))
                            yield return unit;
                    }
            }
        }

        /// <summary>
        /// Skip only shape bounds strictly outside source-region bounds, otherwise
        /// enumerate exact shape references. Bounding tests never replace a polygon.
        /// </summary>
        static IEnumerable<int> AddWithinRegion(GeoRegion region, SafetyIndex index, Shape shape)
        {
            if (shape.MaxX < region.minX || shape.MinX > region.minX + region.width || shape.MaxZ < region.minZ || shape.MinZ > region.minZ + region.depth)
            {
                yield return 0;
                yield break;
            }

            foreach (int i in index.Add(shape))
                yield return i;
            if (index.Failed)
                throw new InvalidDataException("Index cap");
        }

        /// <summary>
        /// Split a full original segment into at-most32m collinear capsule pieces.
        /// The original source endpoints remain untouched. Inflation radius is metres;
        /// too many pieces refuse rather than dropping part of a long source segment.
        /// </summary>
        static IEnumerable<Shape> SplitSourceSegment(Vector3 a, Vector3 b, double radius)
        {
            if (!IsFiniteNumber(a.x) || !IsFiniteNumber(a.z) || !IsFiniteNumber(b.x) || !IsFiniteNumber(b.z))
                throw new InvalidDataException("Nonfinite source");
            double deltaXMetres = b.x - (double)a.x, deltaZMetres = b.z - (double)a.z;
            int pieceCount = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(deltaXMetres * deltaXMetres + deltaZMetres * deltaZMetres) / MaximumSegmentPieceLengthMetres));
            if (pieceCount > MaximumSegmentPieces)
                throw new InvalidDataException("Long source edge");
            for (int i = 0; i < pieceCount; i++)
                yield return Shape.Capsule(
                    new P(a.x + deltaXMetres * i / pieceCount, a.z + deltaZMetres * i / pieceCount),
                    new P(a.x + deltaXMetres * (i + 1) / pieceCount, a.z + deltaZMetres * (i + 1) / pieceCount),
                    radius);
        }
    }
}
