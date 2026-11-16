using System;
using System.IO;
using System.Threading;

namespace EarthUnscripted.TerrainDraft.Cooked
{
    /// <summary>
    /// Process-local geometry reload capability from an actual producer export.
    /// Its private request retains independently bound identities and budgets.
    /// It cannot be constructed from metadata, hashes, JSON or a detached snapshot.
    /// No source, effective-path, interval, native-owner or world authority follows.
    /// </summary>
    public sealed class CookedGeometryReloadHandle
    {
        readonly CookedTerrainRequest request;
        public string ArtifactSHA256 => request.ExpectedArtifactDigest;
        public bool SourceAuthorityGranted => false;
        public bool WholeWorldAdmitted => false;

        internal CookedGeometryReloadHandle(CookedTerrainRequest request)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
        }

        /// <summary>
        /// Validate an owned artifact path against the original producer's private
        /// request, using the unchanged complete verifier. Copies of identical
        /// bytes may have another path; changed or partial bytes fail closed.
        /// Cancellation/progress run on the calling worker; no scene API is used.
        /// </summary>
        public VerifiedCookedGeometry Load(string path, CancellationToken cancellation = default,
            Action<string, int, int> progress = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("An owned artifact path is required", nameof(path));
            return CookedTerrainVerifier.Load(path, request, cancellation, progress);
        }
    }

    public static class CookedTerrainExportGeometryExtensions
    {
        /// <summary>
        /// Derive a geometry-only reload handle from this genuine producer export.
        /// No caller-supplied request or metadata is accepted. The handle is an
        /// in-process capability, not a serializable approval receipt; production
        /// source/cache admission remains with its separate designated issuer.
        /// </summary>
        public static CookedGeometryReloadHandle CreateGeometryReloadHandle(this CookedTerrainExport export)
        {
            if (export == null || export.Request == null || export.Request.ExpectedArtifactDigest != export.SHA256
                || export.Bytes <= 0 || export.Bytes > export.Request.Limits.Bytes)
                throw new InvalidDataException("A genuine complete producer export is required");
            return new CookedGeometryReloadHandle(export.Request);
        }
    }
}
