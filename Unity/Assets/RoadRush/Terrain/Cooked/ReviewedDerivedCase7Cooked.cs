using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using RoadRush.TerrainDraft.Cooked;

namespace RoadRush.TerrainDraft.Core800
{
    /// <summary>
    /// Exact reviewed R2 geometry policy. These constants select ONE independently
    /// reviewed derived product; disk metadata cannot change the selection. This
    /// no-engine assembly proves geometry only, never provider/raw trust or motion.
    /// </summary>
    public static class ReviewedDerivedCase7Policy
    {
        public const string Revision = "ReviewedCase7R2/InspectorOnly/1";
        public const string ArtifactDigest = "3c1f3a011c5474037ea2e2e506c60fa022a13dc2e512059291fb41010b5679ef";
        public const long ArtifactBytes = 10829472;
        // Observed complete Execution2 census, not rendering performance approval.
        public const int CellCount = 676, PacketCount = 1064, VertexCount = 322356;
        public const int TriangleCount = 541479, NamedRoadCount = 66, TargetRowCount = 690;
        public const int MaximumRows = 100000, MaximumEmbeddingReferences = 12000000;
        public const long MaximumRawBytes = 64L * 1024 * 1024;
        // Metres/source-local XZ; Y-up final local-f32 + double packet origins.
        public const double MinimumXZ = -400, MaximumXZ = 400, CellMetres = 32, RootMetres = 320;
        public const int QuantizationExponent = 18;
        // Rise/run numerical limits inherited unchanged from the reviewed verifier.
        public const double RoadGradeLimit = .180001, TerrainGradeLimit = 1.200001;
        public const string RawDigest = "fd3b6201584eac1e3303b1392c21896a2a0c76774149f389cc3ace17022803cb";
        public const string SourceDigest = "f16323b89e4818e6ee57e6847a1798e742b47195a311fb3facd3a8682c8ed62d";
        public const string ControlDigest = "578083e2368f9b670546d7579873737b3388bf3e594d374ba50ae046f55b7613";
        public const string ProductDigest = "3a98c692ae09fe34741c17c24b04e10a407b12ff00779b9827a287d3be46566f";
        public const string FinalDigest = "e9e6d47df8f2c466e4a1c19da538c9bcf782e60950a7a4853571346da3abd0fd";
        public const string OwnerDigest = "3cf3d5094f7640386b033ca4660ade024246b8e3387b3374f25f7ee5aae54498";
        public const string ScopeDigest = "c1045f6de2923d307f64cc116eb78cd7cb8117e4c61a5cdbe07d0567aa8f65f1";
        public const string DemDigest = "d7ad49a2563d97ec2a6bc6b63b1dc0b360f85ef872d95d3d42e531916049e49f";
        public const string StructureDigest = "5871431c1c344126e139d0ca5856c41a70999d5d28c1756935ccf6eeb3f7d0df";
        public const string TargetRoad = "59973883-span-0";
        public const string TargetFrameDigest = "80dd6954ebf1fc8d1450b09b6105cf065415d8aa08ae3b02e069a62bbdb47acd";
        public const string ControlRecipe = "DerivedSourceStage/ExactCut-PortalCollar-SourceColumnCrownHermite-PersistedAxisSourceMap-F32/3";
    }

    public sealed partial class VerifiedCoreTerrain
    {
        /// <summary>
        /// Assembly-safe geometry bridge for the Runtime private-issued request.
        /// Public raw bytes do NOT grant source authority here: both byte families
        /// are code-curated and the result is scoped geometry only. Runtime retains
        /// the separate genuine canonical-source capability for Town construction.
        /// The existing ReviewedCooked2 loader/approval/package policy is untouched.
        /// All work is synchronous on the calling worker; callbacks must marshal UI.
        /// </summary>
        public static VerifiedCoreTerrain ReadReviewedCase7DerivedGeometryOnly(string path, byte[] originalRawBytes, CancellationToken cancellation = default, Action<string, int, int> progress = null)
        {
            cancellation.ThrowIfCancellationRequested();
            CookedBinary.Need(originalRawBytes != null && originalRawBytes.Length <= ReviewedDerivedCase7Policy.MaximumRawBytes && CookedBinary.Hash(originalRawBytes) == ReviewedDerivedCase7Policy.RawDigest, "Exact original raw bytes; geometry bridge grants no source authority");
            // Read owned final bytes once. Length/hash precede decoder allocations;
            // cancellation/partial/growing files fail without a partially issued owner.
            var bytes = CookedBinary.ReadBounded(path, ReviewedDerivedCase7Policy.ArtifactBytes, cancellation);
            CookedBinary.Need(bytes.LongLength == ReviewedDerivedCase7Policy.ArtifactBytes && CookedBinary.Hash(bytes) == ReviewedDerivedCase7Policy.ArtifactDigest, "Exact reviewed R2 artifact; legacy/other derived products are not aliases");
            var limits = new CookedTerrainLimits
            {
                Cells = ReviewedDerivedCase7Policy.CellCount,
                Packets = ReviewedDerivedCase7Policy.PacketCount,
                Vertices = ReviewedDerivedCase7Policy.VertexCount,
                Triangles = ReviewedDerivedCase7Policy.TriangleCount,
                Rows = ReviewedDerivedCase7Policy.MaximumRows,
                References = ReviewedDerivedCase7Policy.MaximumEmbeddingReferences,
                Bytes = ReviewedDerivedCase7Policy.ArtifactBytes
            };
            var data = CookedBinary.Read(bytes, limits, cancellation, progress);
            CookedBinary.Need(data.RawDocumentDigest == ReviewedDerivedCase7Policy.RawDigest && data.SourceIdentityDigest == ReviewedDerivedCase7Policy.SourceDigest && data.DemInputDigest == ReviewedDerivedCase7Policy.DemDigest && data.ControlDigest == ReviewedDerivedCase7Policy.ControlDigest && data.OwnerLedgerDigest == ReviewedDerivedCase7Policy.OwnerDigest && data.StructureLedgerDigest == ReviewedDerivedCase7Policy.StructureDigest && data.ScopeCoverageDigest == ReviewedDerivedCase7Policy.ScopeDigest && data.FinalBufferDigest == ReviewedDerivedCase7Policy.FinalDigest && data.GeometryRecipe == CoreCellPreparation.Recipe, "Reviewed source/control/owner/scope/final/geometry recipe binding");
            CookedBinary.Need(data.MinX == ReviewedDerivedCase7Policy.MinimumXZ && data.MinZ == ReviewedDerivedCase7Policy.MinimumXZ && data.MaxX == ReviewedDerivedCase7Policy.MaximumXZ && data.MaxZ == ReviewedDerivedCase7Policy.MaximumXZ && data.CellMetres == ReviewedDerivedCase7Policy.CellMetres && data.ChunkMetres == ReviewedDerivedCase7Policy.RootMetres && data.QuantizationExponent == ReviewedDerivedCase7Policy.QuantizationExponent && data.RoadGrade == ReviewedDerivedCase7Policy.RoadGradeLimit && data.TerrainGrade == ReviewedDerivedCase7Policy.TerrainGradeLimit && data.Cells.Length == ReviewedDerivedCase7Policy.CellCount && data.Packets.Length == ReviewedDerivedCase7Policy.PacketCount && data.Packets.Sum(p => p.XYZ.Length / 3) == ReviewedDerivedCase7Policy.VertexCount && data.Packets.Sum(p => p.Indices.Length / 3) == ReviewedDerivedCase7Policy.TriangleCount && data.Rows.Count == ReviewedDerivedCase7Policy.NamedRoadCount && data.Rows.TryGetValue(ReviewedDerivedCase7Policy.TargetRoad, out var rows) && rows.Length == ReviewedDerivedCase7Policy.TargetRowCount, "Exact reviewed subset/complete allocation census");
            // One separately pinned row digest prevents a future bridge from silently
            // treating target NEW XZ-H semantics as the 65 original V7-H frame sets.
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, CookedBinary.Utf8, true))
                    foreach (var row in data.Rows[ReviewedDerivedCase7Policy.TargetRoad])
                    {
                        writer.Write(row.S);
                        writer.Write(row.H);
                        writer.Write(row.Previous);
                        writer.Write(row.X);
                        writer.Write(row.Y);
                        writer.Write(row.Z);
                        writer.Write(row.RX);
                        writer.Write(row.RY);
                        writer.Write(row.RZ);
                    }

                CookedBinary.Need(CookedBinary.Hash(stream.ToArray()) == ReviewedDerivedCase7Policy.TargetFrameDigest, "Literal persisted-F64/source-side target frame commitment");
            }

            // Banks are read only AFTER the independently curated full artifact hash.
            // The generic verifier then re-proves final buffers, all676 cells, seams,
            // membership, embedding and exact bank faces/loops/grades. No constructor
            // or issuer key is accessed by this adapter; FromCooked is existing bridge.
            var request = new CookedTerrainRequest(data, ReviewedDerivedCase7Policy.ArtifactDigest, limits);
            return FromCooked(CookedTerrainVerifier.VerifyOwned(bytes, request, cancellation, progress));
        }
    }
}
