using System;
using System.IO;
using System.Threading;
using RoadRush.TerrainDraft.Cooked;

namespace RoadRush.TerrainDraft.Core800
{
    public sealed partial class VerifiedCoreTerrain
    {
        // Fixture-specific caller policy: these exact pins identify the reviewed
        // complete 800m Pantside producer export. They intentionally live outside
        // the generic cache decoder and must not become a global map restriction.
        public const string ReviewedCookedPackageSHA256 = "0cb6ac17d7d6e1b5acbb942a8b66375958ec4434ea0d285d4a47405e4cad4a89";
        public const string ReviewedCookedSourceFormat = "RoadRushWorld13/Raw13Canonical-v1/ieee-exact-decimal-v1";
        public const string ReviewedCookedArtifactSHA256 = "8a08ea0627010f4d0dee38694f15d44abfc8da832ccc9ef52a71697392112fd0";
        /// <summary>
        /// Load only the independently reviewed fixture package and actual canonical
        /// raw envelope. Runs complete geometry/bank/cell/seam checks before issuing
        /// the existing private scoped source capability. Never grants whole-world
        /// coverage, downloads data, changes recipe or falls back to analytic terrain.
        /// The caller owns raw bytes; callbacks execute on its worker thread.
        /// </summary>
        public static VerifiedCoreTerrain LoadReviewedCooked(string path, byte[] independentlyCapturedCanonicalRaw,
            CancellationToken token = default, Action<string, int, int> progress = null)
        {
            var geometry = LoadReviewedGeometry(path, independentlyCapturedCanonicalRaw, token, progress);
            CookedTerrainPackage.Validate(path + ".rrmeta", ReviewedCookedPackageSHA256, ReviewedCookedSourceFormat, geometry, token);
            return FromCooked(geometry);
        }

        /// <summary>
        /// Offline companion-package writer for the already reviewed payload. Fresh
        /// path only; preserves source-format/licence-context references, not a licence
        /// grant. Production pack publication and trust distribution remain separate.
        /// </summary>
        public static string WriteReviewedCookedPackage(string artifactPath, byte[] raw, CancellationToken token = default)
        {
            var verified = LoadReviewedGeometry(artifactPath, raw, token, null);
            return CookedTerrainPackage.Write(verified, artifactPath + ".rrmeta", ReviewedCookedSourceFormat, token);
        }

        internal static VerifiedCookedGeometry LoadReviewedGeometry(string path, byte[] raw, CancellationToken token, Action<string, int, int> progress)
        {
            token.ThrowIfCancellationRequested();
            CookedBinary.Need(raw != null && raw.Length <= 64 * 1024 * 1024 && CookedBinary.Hash(raw) == "fd3b6201584eac1e3303b1392c21896a2a0c76774149f389cc3ace17022803cb",
                "Actual reviewed source bytes; never a receipt status");
            var info = new FileInfo(path);
            CookedBinary.Need(info.Length == 10639678, "Exact reviewed artifact byte count");
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bytes = new byte[checked((int)info.Length)];
                int read = 0;
                while (read < bytes.Length)
                {
                    token.ThrowIfCancellationRequested();
                    int n = stream.Read(bytes, read, Math.Min(CookedTerrainPolicy.ReadBlockBytes, bytes.Length - read));
                    CookedBinary.Need(n > 0, "Partial cache");
                    read += n;
                    progress?.Invoke("reviewed-source-read", read, bytes.Length);
                }

                CookedBinary.Need(stream.ReadByte() == -1, "Growing cache");
            }

            CookedBinary.Need(CookedBinary.Hash(bytes) == ReviewedCookedArtifactSHA256, "Independent real-oracle export artifact pin");
            var data = CookedBinary.Read(bytes, new CookedTerrainLimits(), token, progress);
            CookedBinary.Need(data.RawDocumentDigest == CookedBinary.Hash(raw) && data.SourceIdentityDigest == "f16323b89e4818e6ee57e6847a1798e742b47195a311fb3facd3a8682c8ed62d"
                && data.FinalBufferDigest == "04391e3747fc9ae52327e1a4565ed37fc1de65334fa6eaf00afc4f73c4ee62cf"
                && data.GeometryRecipe == CoreCellPreparation.Recipe && data.ScopeCoverageDigest == "9955e93cf991f88ebc3079064de2bc7db85fe82724b746f821b6c01e66b5ef55"
                && data.MinX == -400 && data.MinZ == -400 && data.MaxX == 400 && data.MaxZ == 400, "SourceIdentityDigest/area-specific fixture bridge, no whole-world authority");
            var request = new CookedTerrainRequest(data, ReviewedCookedArtifactSHA256, new CookedTerrainLimits());
            return CookedTerrainVerifier.VerifyOwned(bytes, request, token, progress);
        }
    }
}
