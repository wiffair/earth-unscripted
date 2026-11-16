using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace EarthUnscripted.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Pure bounded copying/association checks. These helpers consume data, not
    /// leases, and cannot construct a TownSemanticSourceBinding800. Positive unit
    /// data cases therefore are not real publication/ground/native admission.
    /// </summary>
    internal static class TownSourceCapturePolicy800
    {
        // Match the existing actual Town render-input/source resource envelope.
        // No thinning, source inference or partial-success fallback is permitted.
        internal const int MaximumSourceBuildings = 2500;
        internal const int MaximumSourceVertices = 400000;
        internal const int MaximumPointsPerBuilding = 4096;
        internal const int MaximumEntries = 2500;
        internal const int MaximumColliders = 8192;
        internal const int MaximumMeshVertices = 2000000;
        internal const int MaximumTriangleIndices = 12000000; // Existing4M-triangle Town cap.
        internal const int MaximumSubmeshesPerCollider = 64;
        internal const int FrameElementCount = 16;
        // Defensive finite-coordinate limit, not a fit/clamp or world extent.
        internal const float MaximumCoordinateMagnitudeMetres = 1000000;
        // Collider transform bases are dimensionless. Tiny/singular, mirrored or
        // sheared layouts refuse instead of approximating Unity/PhysX geometry.
        private const double MinimumBasisLengthSquared = 1e-12;
        private const double MaximumRelativeBasisDot = 1e-5;
        private const float MaximumBasisCoefficientMagnitude = 1000000; // Dimensionless defensive scale cap.

        internal static bool SameFrame(Matrix4x4 first, Matrix4x4 second)
        {
            for (int index = 0; index < FrameElementCount; index++)
                if (BitConverter.SingleToInt32Bits(first[index]) != BitConverter.SingleToInt32Bits(second[index]))
                    return false;
            return true;
        }

        internal static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value)
                && Math.Abs(value) <= MaximumCoordinateMagnitudeMetres;
        }

        internal static void RequirePoint(Vector3 point, string description)
        {
            if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                throw new InvalidDataException("Unsupported/nonfinite " + description);
        }

        /// <summary>
        /// Accept finite affine, positive orthogonal bases (including nonuniform
        /// scale). Relative dot products bound shear from ordinary float rotation
        /// rounding; no basis is normalized or repaired in copied output.
        /// </summary>
        internal static void RequireColliderFrame(Matrix4x4 frame)
        {
            for (int index = 0; index < FrameElementCount; index++)
                if (float.IsNaN(frame[index]) || float.IsInfinity(frame[index]))
                    throw new InvalidDataException("Nonfinite Town collider frame");
            RequirePoint(new Vector3(frame.m03, frame.m13, frame.m23), "Town collider translation");
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    if (Math.Abs(frame[row, column]) > MaximumBasisCoefficientMagnitude)
                        throw new InvalidDataException("Town collider basis scale budget exceeded");
            if (frame.m30 != 0 || frame.m31 != 0 || frame.m32 != 0 || frame.m33 != 1)
                throw new InvalidDataException("Non-affine Town collider frame unsupported");
            double xx = (double)frame.m00 * frame.m00 + (double)frame.m10 * frame.m10 + (double)frame.m20 * frame.m20;
            double yy = (double)frame.m01 * frame.m01 + (double)frame.m11 * frame.m11 + (double)frame.m21 * frame.m21;
            double zz = (double)frame.m02 * frame.m02 + (double)frame.m12 * frame.m12 + (double)frame.m22 * frame.m22;
            double xy = (double)frame.m00 * frame.m01 + (double)frame.m10 * frame.m11 + (double)frame.m20 * frame.m21;
            double xz = (double)frame.m00 * frame.m02 + (double)frame.m10 * frame.m12 + (double)frame.m20 * frame.m22;
            double yz = (double)frame.m01 * frame.m02 + (double)frame.m11 * frame.m12 + (double)frame.m21 * frame.m22;
            if (xx <= MinimumBasisLengthSquared || yy <= MinimumBasisLengthSquared || zz <= MinimumBasisLengthSquared
                || Math.Abs(xy) > MaximumRelativeBasisDot * Math.Sqrt(xx * yy)
                || Math.Abs(xz) > MaximumRelativeBasisDot * Math.Sqrt(xx * zz)
                || Math.Abs(yz) > MaximumRelativeBasisDot * Math.Sqrt(yy * zz))
                throw new InvalidDataException("Singular/sheared Town collider frame unsupported");
            double determinant = frame.m00 * ((double)frame.m11 * frame.m22 - (double)frame.m12 * frame.m21)
                - frame.m01 * ((double)frame.m10 * frame.m22 - (double)frame.m12 * frame.m20)
                + frame.m02 * ((double)frame.m10 * frame.m21 - (double)frame.m11 * frame.m20);
            if (!(determinant > 0))
                throw new InvalidDataException("Mirrored Town collider frame unsupported");
        }

        internal static void RequireBox(Vector3 centre, Vector3 size, Matrix4x4 frame)
        {
            RequirePoint(centre, "Town box centre");
            RequirePoint(size, "Town box size");
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new InvalidDataException("Nonpositive Town box dimensions unsupported");
            RequireColliderFrame(frame);
        }

        internal static void RequireTriangleIndices(Vector3[] vertices, int[] indices)
        {
            if (vertices == null || vertices.Length < 3 || vertices.Length > MaximumMeshVertices
                || indices == null || indices.Length < 3 || indices.Length > MaximumTriangleIndices || indices.Length % 3 != 0)
                throw new InvalidDataException("Complete bounded triangle mesh input required");
            foreach (var vertex in vertices) RequirePoint(vertex, "Town mesh vertex");
            foreach (int index in indices)
                if (index < 0 || index >= vertices.Length)
                    throw new InvalidDataException("Town triangle index outside captured vertex buffer");
        }

        internal static void AddCount(ref int total, int count, int maximum, string description)
        {
            if (count < 0 || total < 0 || count > maximum - total)
                throw new InvalidDataException("Town capture " + description + " budget/refusal");
            total += count;
        }

        /// <summary>
        /// Copy every source record without normalization. Build original-reference
        /// indices only within this synchronous borrow; the dictionary never leaves
        /// the factory stack. Ordinals are not provider IDs or identity hashes.
        /// </summary>
        internal static TownSourceBuilding800[] CopyOriginalBuildings(GeoBuilding[] buildings,
            CancellationToken cancellation, out Dictionary<GeoBuilding, int> sourceIndices)
        {
            if (buildings == null || buildings.Length > MaximumSourceBuildings)
                throw new InvalidDataException("Complete bounded original Town source required");
            sourceIndices = new Dictionary<GeoBuilding, int>(TownSourceReferenceIdentityComparer800<GeoBuilding>.Instance);
            var rows = new TownSourceBuilding800[buildings.Length];
            int totalVertices = 0;
            for (int index = 0; index < buildings.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var source = buildings[index];
                if (source == null || source.points == null || source.points.Length < 3 || source.points.Length > MaximumPointsPerBuilding
                    || sourceIndices.ContainsKey(source))
                    throw new InvalidDataException("Missing/duplicate/unbounded original Town source reference");
                AddCount(ref totalVertices, source.points.Length, MaximumSourceVertices, "source vertices");
                foreach (var point in source.points) RequirePoint(point, "original raw source point");
                sourceIndices.Add(source, index);
                rows[index] = new TownSourceBuilding800(index, source);
            }
            return rows;
        }

        /// <summary>
        /// Map ALL original references exactly once to actual Entry references.
        /// The supplied body lookup is invoked synchronously by the native factory;
        /// absent body means suppressed, never an invented physical wall/volume.
        /// No borrowed source/entry references are stored in the returned rows.
        /// </summary>
        internal static TownSourceEntry800[] CopyEntries(BuildingRenderInput.Plan plan,
            Dictionary<GeoBuilding, int> sourceIndices,
            Func<BuildingRenderInput.Entry, TownPublishedBody800> copyActualBody,
            CancellationToken cancellation)
        {
            if (plan == null || plan.Entries == null || plan.Entries.Length > MaximumEntries
                || plan.SourceCount != sourceIndices.Count || copyActualBody == null)
                throw new InvalidDataException("Complete bounded source-entry plan required");
            var seenEntries = new HashSet<BuildingRenderInput.Entry>(TownSourceReferenceIdentityComparer800<BuildingRenderInput.Entry>.Instance);
            var seenSources = new bool[sourceIndices.Count];
            var seenBodyIds = new HashSet<ulong>();
            var rows = new TownSourceEntry800[plan.Entries.Length];
            for (int ordinal = 0; ordinal < plan.Entries.Length; ordinal++)
            {
                cancellation.ThrowIfCancellationRequested();
                var entry = plan.Entries[ordinal];
                if (entry == null || !seenEntries.Add(entry) || entry.OriginalSources == null || entry.OriginalSources.Length == 0
                    || entry.OriginalSources.Length > sourceIndices.Count)
                    throw new InvalidDataException("Missing/aliased exact Town Entry/source references");
                var originals = new int[entry.OriginalSources.Length];
                for (int index = 0; index < originals.Length; index++)
                {
                    var original = entry.OriginalSources[index];
                    if (original == null || !sourceIndices.TryGetValue(original, out int sourceIndex) || seenSources[sourceIndex])
                        throw new InvalidDataException("Foreign/duplicate original Town source association");
                    seenSources[sourceIndex] = true;
                    originals[index] = sourceIndex;
                }
                var body = copyActualBody(entry);
                if (body != null && (entry.Skipped || body.RuntimeBodyEntityId == 0 || !seenBodyIds.Add(body.RuntimeBodyEntityId)))
                    throw new InvalidDataException("Actual emitted Town body aliases/skipped entry unsupported");
                rows[ordinal] = new TownSourceEntry800(ordinal, originals, entry.ContentKey, body);
            }
            foreach (bool seen in seenSources)
                if (!seen) throw new InvalidDataException("Incomplete original Town source coverage");
            return rows;
        }
    }

    internal sealed class TownSourceReferenceIdentityComparer800<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly TownSourceReferenceIdentityComparer800<T> Instance = new TownSourceReferenceIdentityComparer800<T>();
        public bool Equals(T first, T second) => ReferenceEquals(first, second);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
}
