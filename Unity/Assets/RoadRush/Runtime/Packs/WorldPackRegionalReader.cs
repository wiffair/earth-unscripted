using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using R = RoadRush.RegionalDraft.RegionalCollection;

namespace RoadRush.Packs
{
    /// <summary>
    /// Typed source-footprint reader over an installed pack. Reuses the existing lineage/whole-footprint validator;
    /// it neither activates an authoring collection nor modifies roads, terrain or the current world snapshot.
    /// Renderer/scene admission and estimated-height treatment remain separate from parsing source records.
    /// </summary>
    public sealed class WorldPackRegionalReader
    {
        readonly R collection;
        public readonly PackBounds Coverage, VerificationWindow;
        public readonly string ManifestSha256, SourceIndexSha256;
        public readonly bool SourceAuthoringBlocked;
        public readonly int ExpectedVerificationParts;
        public readonly string VerificationSourceKeysSha256;
        /// <summary>
        /// False when any authored building element is omitted or uses a different representation.
        /// The descriptor's count/hash describes the complete export, not an older engine's subset.
        /// Conservative across the whole pack: an omitted distant shard cannot confer full-audit status.
        /// </summary>
        public readonly bool CompleteBuildingSupport;

        public sealed class ReferenceAudit
        {
            public readonly R.Selection Selection;
            public readonly bool CompleteExportVerified;
            public readonly string Message;
            internal ReferenceAudit(R.Selection selection, bool complete)
            {
                Selection = selection; CompleteExportVerified = complete;
                Message = complete ? "Complete reference area matches the export."
                    : "Supported building subset checked; complete export comparison unavailable because some building content is unsupported.";
            }
        }

        WorldPackRegionalReader(string directory, WorldPackManifest manifest, PackCompatibility compatible)
        {
            ManifestSha256 = manifest.ManifestSha256;
            var index = compatible.Decisions.SingleOrDefault(d => d.Loaded && d.Variant.Feature == "regional-index" && d.Variant.Schema == 1);
            WorldPackPolicy.Require(index != null && new[] { "coverage-index.json", "spec.json", "region.json" }.All(index.Variant.Files.Contains), "regional index feature contract");
            foreach (var decision in compatible.Decisions.Where(d => d.Loaded && d.Variant.Feature == "regional-buildings"))
                WorldPackPolicy.Require(decision.Variant.Schema == 1 && decision.Element.Dependencies.Contains(index.Element.Id), "regional leaf/index dependency");
            var file = manifest.Files.Single(f => f.Path == "region.json"); WorldPackPolicy.Require(file.Bytes <= 64000, "regional descriptor budget");
            var raw = File.ReadAllBytes(Path.Combine(directory, "region.json")); JObject descriptor;
            using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(raw))) { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
            {
                descriptor = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                WorldPackPolicy.Require(!reader.Read(), "trailing regional descriptor");
            }
            WorldPackPolicy.Require((string)descriptor["format"] == "RoadRushRegionalPack1" && descriptor["coverageBounds"] is JArray b && b.Count == 4, "regional descriptor format");
            var bounds = (JArray)descriptor["coverageBounds"]; Coverage = new PackBounds((double)bounds[0], (double)bounds[1], (double)bounds[2], (double)bounds[3]);
            var verification = descriptor["verificationWindow"] as JArray;
            WorldPackPolicy.Require(verification != null && verification.Count == 4, "regional reference window");
            VerificationWindow = new PackBounds((double)verification[0], (double)verification[1], (double)verification[2], (double)verification[3]);
            WorldPackPolicy.Require(Coverage.Contains(VerificationWindow), "reference window outside coverage");
            WorldPackPolicy.Require(manifest.Bounds.Contains(Coverage), "regional coverage outside pack");
            WorldPackPolicy.Require(descriptor["sourceAuthoringGate"]?.Type == JTokenType.Boolean && descriptor["expectedSelectedWholeParts"]?.Type == JTokenType.Integer, "regional source status/count");
            SourceAuthoringBlocked = (bool)descriptor["sourceAuthoringGate"]; ExpectedVerificationParts = (int)descriptor["expectedSelectedWholeParts"];
            SourceIndexSha256 = (string)descriptor["sourceIndexSha256"]; VerificationSourceKeysSha256 = (string)descriptor["expectedSelectedSourceKeysSha256"];
            string compiled = manifest.Files.Single(f => f.Path == "coverage-index.json").Sha256;
            WorldPackPolicy.Require((string)descriptor["compiledIndexSha256"] == compiled, "compiled index identity");
            WorldPackPolicy.Require(ExpectedVerificationParts >= 0 && ExpectedVerificationParts <= 32768 && manifest.Files.Any(f => f.Path == "provenance/original-coverage-index.json" && f.Sha256 == SourceIndexSha256), "regional source commitment");
            var supportedLeaves = compatible.Decisions.Where(d => d.Loaded && d.Variant.Feature == "regional-buildings" && d.Variant.Schema == 1).ToDictionary(d => d.Element.Id, StringComparer.Ordinal);
            bool originalRepresentations = compatible.Decisions
                .Where(d => d.Element.Variants.Any(v => v.Feature == "regional-buildings"))
                .All(d => supportedLeaves.ContainsKey(d.Element.Id) && ReferenceEquals(d.Variant, d.Element.Variants[0]));
            collection = R.LoadSupportedLeaves(directory, compiled, supportedLeaves.Keys);
            CompleteBuildingSupport = originalRepresentations && collection.DeclaredBuildingLeafIds.All(supportedLeaves.ContainsKey);
            foreach (var leaf in collection.IndexedLeaves)
            {
                var decision = supportedLeaves[leaf.Id];
                WorldPackPolicy.Require(decision.Variant.Files.Contains(leaf.ManifestPath) && decision.Variant.Files.Contains(leaf.PayloadPath), "regional source files differ from supported element");
                WorldPackPolicy.Require(manifest.Files.Any(f => f.Path == leaf.ManifestPath && f.Sha256 == leaf.ManifestHash && f.Bytes == leaf.ManifestBytes)
                    && manifest.Files.Any(f => f.Path == leaf.PayloadPath && f.Sha256 == leaf.PayloadHash && f.Bytes == leaf.PayloadBytes), "regional source files differ from pack commitments");
            }
            WorldPackPolicy.Require(SourceAuthoringBlocked == !collection.RuntimeActivationAllowed, "source authoring restriction changed");
        }

        public static WorldPackRegionalReader OpenVerified(WorldPackStore store, InstalledWorldPack installed,
            PackEngineSupport engine, CancellationToken cancellation = default)
        {
            if (store == null || installed == null || engine == null) throw new ArgumentNullException();
            var manifest = store.VerifyInstalled(installed.Directory, installed.Manifest.ManifestSha256, cancellation);
            var compatible = WorldPackCompatibility.Resolve(manifest, engine);
            WorldPackPolicy.Require(compatible.CanActivate, compatible.Reason);
            // Opening a region prepares its metadata, not every building in its coverage.
            // The caller requests bounded windows as the observer moves; a reference audit is explicit.
            return new WorldPackRegionalReader(installed.Directory, manifest, compatible);
        }

        public R.Selection VerifyReferenceWindow(R.Limits limits, CancellationToken cancellation = default)
        {
            var audit = AuditReferenceWindow(limits, cancellation);
            WorldPackPolicy.Require(audit.CompleteExportVerified, "complete reference audit requires all original building representations; use the qualified subset audit");
            return audit.Selection;
        }

        /// <summary>
        /// Checks supported leaf contracts and bounded source selection even when optional future content
        /// is skipped. Only a complete original representation may claim the export-wide count/hash match.
        /// File hashes for every installed payload are still verified by OpenVerified, including skipped bytes.
        /// </summary>
        public ReferenceAudit AuditReferenceWindow(R.Limits limits, CancellationToken cancellation = default)
        {
            var selection = Query(VerificationWindow, limits, cancellation);
            if (CompleteBuildingSupport)
                WorldPackPolicy.Require(selection.Buildings.Count == ExpectedVerificationParts && SourceKeyHash(selection) == VerificationSourceKeysSha256,
                    "exported reference window/source records differ after installation");
            return new ReferenceAudit(selection, CompleteBuildingSupport);
        }

        public R.Selection Query(PackBounds window, R.Limits limits, CancellationToken cancellation = default)
        {
            WorldPackPolicy.Require(Coverage.Contains(window), "requested area exceeds exported regional coverage");
            var selection = collection.QueryAll(new R.Rect(window.West, window.South, window.East, window.North), limits, cancellation);
            WorldPackPolicy.Require(selection.FitsDeclaredBuildingBudget, string.Join("; ", selection.BudgetProblems));
            return selection;
        }
        public static string SourceKeyHash(R.Selection selection) => WorldPackPolicy.Hash(Encoding.UTF8.GetBytes(new JArray(selection.Buildings.Select(b => b.SourceKey)).ToString(Formatting.None)));
    }
}
