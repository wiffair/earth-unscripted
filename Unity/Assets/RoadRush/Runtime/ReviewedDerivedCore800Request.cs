using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RoadRush.Core800;
using RoadRush.TerrainDraft.Core800;

namespace RoadRush
{
    /// <summary>
    /// Private-issued Runtime request for the reviewed R2 Case7 inspector product.
    /// It retains the genuine original raw capability across the no-engine Terrain
    /// assembly boundary. Hashes from disk or caller-supplied metadata cannot mint it.
    /// Worker-safe; no scene, profile, cache, provider, route or motion activation.
    /// </summary>
    public sealed class ReviewedDerivedCore800Request
    {
        const int OriginalTriangleCount = 368; // Complete original body-face disposition census.
        const int EvidenceReadBlockBytes = 64 * 1024;
        const string ProposalRecipe = "OriginalV7ExactCut-FinalPortalDerivedCollar-SourceColumnCrownOrientationMonotoneHermite-F32/3";
        const string DescriptorDigest = "1dec382879fc1ddc15c4f08f1b85b860dd68c22c24df2ece0d15df5ef9aae9e2";
        public const string RetirementLedgerDigest = "8b67e38d1fa53bc6d63140041825c563de4a4bd31cd6a7e73ae226a5a37ff8f6";
        public const string ProposalDigest = "fe3d36fcab6a4c214246534f0b48e82df4944df0599ba1b41557c035fc5c5ad3";
        public const string GeneratorDigest = "d64d60db0f8876d8ee5d612fadd5b2da06ff45bf07a9bb1be6db0232fe0b69f3";
        public const string OriginalGroundPacketDigest = "d7837532b503431a72f2afd1900c87127589ad275acfbe6b6def9b4a126dd8af";
        public const string ActivePolicyDigest = "27cb9625a579f87dd27975a9b0167cf289ca6585b7b390b83ed29999fdcacff4";
        // Seven small reviewed evidence inputs total under4MiB. The10.8MiB cooked
        // payload is separately read/hash/verified by Terrain with its exact limit.
        const long MaximumEvidenceBytes = 4 * 1024 * 1024;
        const string CookedRelativePath = "Producer/Execution-v2/Output/candidate.cooked";
        readonly ReviewedCanonicalRaw13Input original;
        readonly string reviewedRoot;
        readonly byte[] originalRaw;
        sealed class EvidencePin
        {
            internal readonly string Path, Sha;
            internal readonly long Bytes;
            internal EvidencePin(string path, long bytes, string sha)
            {
                Path = path;
                Bytes = bytes;
                Sha = sha;
            }
        }

        static readonly EvidencePin[] Evidence =
        {
            new EvidencePin("Inputs/Controls/Case7Offset1/descriptor.json", 1055234, "1dec382879fc1ddc15c4f08f1b85b860dd68c22c24df2ece0d15df5ef9aae9e2"),
            new EvidencePin("Producer/Execution-v2/independently-regenerated-proposal.json", 1958629, "fe3d36fcab6a4c214246534f0b48e82df4944df0599ba1b41557c035fc5c5ad3"),
            new EvidencePin("Producer/Execution-v2/ExecutedProducer/build_case7_source_plan.py", 25616, "d64d60db0f8876d8ee5d612fadd5b2da06ff45bf07a9bb1be6db0232fe0b69f3"),
            new EvidencePin("Producer/Execution-v2/ExecutedProducer/DerivedCandidateCook.cs", 41070, "d92d9e89d892b78971cf0be6a853b1e656f0d2a0d3f25af5231768c329677325"),
            new EvidencePin("Producer/Execution-v2/ExecutedProducer/RecipeImplementation.cs", 301, "d2b59694c00290bb800808f39ff2604f62f41fdb59dfe8c0dbd2b6b0b0b08599"),
            new EvidencePin("Validation/Execution2AdmissionAdversary-v1/result.json", 1729, "f3684d152a79d2c7d53a72296dd1b70ae2d0af3aa35d05aa3a2d132e3c99e8c7"),
            new EvidencePin("RuntimeContract/CoverageAndStationQualification-v1/qualification.json", 82460, "2c8c2875c17847dca2c9e6e6c65a300af9b7ec0ff5b2e162b51b05d2c60dab29"),
        };
        ReviewedDerivedCore800Request(ReviewedCanonicalRaw13Input input, string root, byte[] raw)
        {
            Need(!string.IsNullOrEmpty(root), "Reviewed bundle cannot be filesystem root");
            original = input;
            reviewedRoot = root;
            originalRaw = (byte[])raw.Clone();
        }

        public string Revision => ReviewedDerivedCase7Policy.Revision;
        public string DerivedProductDigest => ReviewedDerivedCase7Policy.ProductDigest;
        public string DerivedControlDigest => ReviewedDerivedCase7Policy.ControlDigest;
        public bool MotionAdmitted => false;
        public bool SupportedPathIntervalsIssued => false;

        /// <summary>
        /// Root-curated prototype catalog entry, not a generic proposal approval API.
        /// The seven evidence hashes name the already independently replayed R2
        /// recipe/ledger/frame qualification. This method does NOT rerun Python or
        /// recook. Future products need an explicit separately reviewed factory entry;
        /// replacing old approval hashes or accepting a receipt flag is forbidden.
        /// </summary>
        public static ReviewedDerivedCore800Request CaptureReviewedCase7(ReviewedCanonicalRaw13Input originalInput, string reviewedCase7Root, CancellationToken cancellation = default)
        {
            if (originalInput == null)
                throw new ArgumentNullException(nameof(originalInput));
            cancellation.ThrowIfCancellationRequested();
            byte[] raw = originalInput.CopyDocumentBytes(cancellation);
            Need(Hash(raw) == ReviewedDerivedCase7Policy.RawDigest, "Original genuine canonical capability mismatch");
            var result = new ReviewedDerivedCore800Request(originalInput, Path.GetFullPath(reviewedCase7Root ?? throw new ArgumentNullException(nameof(reviewedCase7Root))).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), raw);
            result.ValidateEvidence(cancellation);
            return result;
        }

        static void Need(bool value, string reason)
        {
            if (!value)
                throw new InvalidDataException("Reviewed derived Case7: " + reason);
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(v => v.ToString("x2")));
        }

        string Child(string relative)
        {
            Need(!Path.IsPathRooted(relative) && !relative.Split('/', '\\').Any(p => p == ".."), "Relative evidence path");
            string full = Path.GetFullPath(Path.Combine(reviewedRoot, relative));
            Need(full.StartsWith(reviewedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Evidence escapes owned bundle");
            // A copied reviewed bundle is permitted, redirected/symlink metadata is
            // not. This protects file ownership, not provider/source authenticity.
            for (FileSystemInfo at = new FileInfo(full); at != null; at = at is FileInfo file ? file.Directory : ((DirectoryInfo)at).Parent)
                Need((at.Attributes & FileAttributes.ReparsePoint) == 0, "Symlink/reparse evidence path");
            return full;
        }

        byte[] ReadPin(EvidencePin pin, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Need(pin.Bytes > 0 && pin.Bytes <= MaximumEvidenceBytes, "Evidence byte budget");
            using (var stream = new FileStream(Child(pin.Path), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Need(stream.Length == pin.Bytes, "Evidence byte length: " + pin.Path);
                var bytes = new byte[checked((int)pin.Bytes)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int count = stream.Read(bytes, offset, Math.Min(EvidenceReadBlockBytes, bytes.Length - offset));
                    Need(count > 0, "Partial evidence: " + pin.Path);
                    offset += count;
                }

                Need(stream.ReadByte() == -1 && Hash(bytes) == pin.Sha, "Changed reviewed evidence: " + pin.Path);
                return bytes;
            }
        }

        void ValidateEvidence(CancellationToken cancellation)
        {
            long total = 0;
            foreach (var pin in Evidence)
            {
                total += pin.Bytes;
                Need(total <= MaximumEvidenceBytes, "Aggregate evidence byte budget");
                byte[] bytes = ReadPin(pin, cancellation);
                if (pin.Sha == ProposalDigest)
                {
                    // Integers/rational strings only in this sub-ledger; deterministic
                    // compact JSON is independently pinned, not used as a new grant.
                    using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))))
                    {
                        reader.DateParseHandling = DateParseHandling.None;
                        reader.MaxDepth = 128;
                        var proposal = JObject.Load(reader);
                        Need((string)proposal["format"] == "DerivedSourcePacketProposal1" && (string)proposal["recipe"] == ProposalRecipe && (string)proposal["rawRoadDigest"] == ReviewedDerivedCase7Policy.SourceDigest && (string)proposal["rawEnvelopeDigest"] == ReviewedDerivedCase7Policy.RawDigest && (string)proposal["activePolicySHA256"] == ActivePolicyDigest && (string)proposal["sourceDescriptorSHA256"] == DescriptorDigest && (string)proposal["targetRoadId"] == ReviewedDerivedCase7Policy.TargetRoad && (string)proposal["originalPacketId"] == "ground-body-" + ReviewedDerivedCase7Policy.TargetRoad, "Typed source/recipe/policy/descriptor/target context");
                        var ledger = proposal["originalTriangleDispositions"] as JArray;
                        Need(ledger != null && ledger.Count == OriginalTriangleCount && Hash(Encoding.UTF8.GetBytes(ledger.ToString(Formatting.None))) == RetirementLedgerDigest, "Exact complete original368-face retirement ledger");
                    }
                }
            }

            cancellation.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Revalidate genuine source and evidence, then use the distinct code-curated
        /// Terrain bridge and unchanged full geometry verifier. There is deliberately
        /// no arbitrary path/hash/control argument on this public load method.
        /// </summary>
        public ReviewedDerivedInspector800 LoadInspector(CancellationToken cancellation = default, Action<string, int, int> progress = null)
        {
            cancellation.ThrowIfCancellationRequested();
            Need(original.CopyDocumentBytes(cancellation).SequenceEqual(originalRaw), "Original capability changed");
            ValidateEvidence(cancellation);
            var geometry = VerifiedCoreTerrain.ReadReviewedCase7DerivedGeometryOnly(Child(CookedRelativePath), (byte[])originalRaw.Clone(), cancellation, progress);
            var sourceOwner = CanonicalRaw13Codec.Read(original, originalRaw, cancellation);
            var plan = PreparedWorldMeshPlan800.FromVerifiedCore(sourceOwner, geometry, cancellation);
            ValidateEvidence(cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new ReviewedDerivedInspector800(sourceOwner, plan, cancellation);
        }
    }

    /// <summary>
    /// Owned ground+Town inspector handoff. Raw owner transfer is explicit/one-time;
    /// no mutable source arrays are exposed. Plan buffers are immutable snapshots.
    /// No path interval, lane, traffic, cache/network or gameplay admission is issued.
    /// </summary>
    public sealed class ReviewedDerivedInspector800
    {
        readonly object gate = new object ();
        CanonicalRaw13Document townSource;
        public PreparedWorldMeshPlan800 Plan { get; }
        public bool MotionAdmitted => false;
        public bool SupportedPathIntervalsIssued => false;
        public string Revision => ReviewedDerivedCase7Policy.Revision;

        internal ReviewedDerivedInspector800(CanonicalRaw13Document source, PreparedWorldMeshPlan800 plan, CancellationToken cancellation)
        {
            if (source == null || plan == null || plan.ControlDigest != ReviewedDerivedCase7Policy.ControlDigest || plan.FinalBufferDigest != ReviewedDerivedCase7Policy.FinalDigest || plan.RawDocumentDigest != ReviewedDerivedCase7Policy.RawDigest || World13DocumentCodec.Hash(source.Write(cancellation)) != plan.RawDocumentDigest)
                throw new InvalidDataException("Exact reviewed derived inspector owner required");
            cancellation.ThrowIfCancellationRequested();
            townSource = source;
            Plan = plan;
        }

        public CanonicalRaw13Document TakeTownSource(CancellationToken cancellation = default)
        {
            lock (gate)
            {
                cancellation.ThrowIfCancellationRequested();
                if (townSource == null)
                    throw new InvalidOperationException("Town source already transferred");
                // Validate ownership/source bits before releasing the only reference.
                townSource.Write(cancellation);
                var result = townSource;
                townSource = null;
                return result;
            }
        }
    }
}
