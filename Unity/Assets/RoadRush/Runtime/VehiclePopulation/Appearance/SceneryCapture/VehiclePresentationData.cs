using System;
using RoadRush.FunnyAnimals.VehicleCaptureProposal;

namespace RoadRush.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>Diagnostic evidence scope, never authority or a completeness flag.</summary>
    [Flags]
    public enum PresentationEvidenceKind
    {
        LiteralMeshVertices = 1,
        NativeReportedTextBounds = 2
    }

    /// <summary>
    /// Immutable copied direct-world and compatibility-root observations in metres (x right,
    /// y up, z forward). No native handles, caller completeness flag, registry identity
    /// or authority is contained. Actual producer must bind it to its own live sample
    /// and privately validate the associated witness before publishing/committing.
    /// Source vertices and actual reported renderer bounds are included; native/GPU
    /// agreement remains separate. Eye is an anchor, not visibility/head-reach proof.
    /// </summary>
    public sealed class VehiclePresentationData
    {
        // Compatibility coordinates only: native worldToRoot inverse agreement
        // is unproved. Never transform this field back to world for clearance.
        public readonly OccupiedBounds RootBoundsMetres;
        /// <summary>
        /// Direct source-data WORLD enclosure from original renderer maps and copied
        /// reported boxes, all loaded LODs and the native eye anchor. No inverse/root
        /// round trip. Native GPU/text raster agreement remains a separate gate.
        /// </summary>
        public readonly OccupiedBounds DirectWorldBoundsMetres;
        /// <summary>Copied actual native returned world anchor; no eye-visibility claim.</summary>
        public readonly Point3 CockpitEyeWorldMetres;
        public readonly Point3 CockpitEyeRootMetres;
        public readonly int Model;
        public readonly int DamageRevision;
        public readonly float DeformationExtentMetres;
        public readonly int LoadedRendererCount;
        public readonly int DistinctMeshCount;
        public readonly int NativeReportedTextCount;
        public readonly PresentationEvidenceKind EvidenceKind;
        public readonly int ScannedVertices;
        public readonly long DeclaredTriangleIndices;
        public readonly string ContentAndPoseSignature;

        // Not a public data/capability constructor. Root producer owns actual source
        // association; possessing this detached object cannot mint a complete roster.
        internal VehiclePresentationData(OccupiedBounds bounds, Point3 eye, OccupiedBounds directWorldBounds,
            Point3 worldEye, int model,
            int damageRevision, float deformationExtent, int rendererCount, int meshCount,
            int vertices, long indices, string signature, int nativeTextCount, PresentationEvidenceKind evidenceKind)
        {
            if (!directWorldBounds.Valid || !worldEye.Finite)
                throw new ArgumentException("Invalid direct-world appearance observation");
            DirectWorldBoundsMetres = directWorldBounds;
            CockpitEyeWorldMetres = worldEye;
            RootBoundsMetres = bounds;
            CockpitEyeRootMetres = eye;
            Model = model;
            DamageRevision = damageRevision;
            DeformationExtentMetres = deformationExtent;
            LoadedRendererCount = rendererCount;
            DistinctMeshCount = meshCount;
            ScannedVertices = vertices;
            DeclaredTriangleIndices = indices;
            ContentAndPoseSignature = signature;
            NativeReportedTextCount = nativeTextCount;
            EvidenceKind = evidenceKind;
        }
    }
}
