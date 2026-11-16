using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RoadRush.Packs
{
    /// <summary>Capabilities describe implemented payload readers, not a promise inferred from an engine version.</summary>
    public sealed class PackEngineSupport
    {
        public readonly Version EngineVersion;
        public readonly IReadOnlyDictionary<string, int> Features;
        public PackEngineSupport(string version, IDictionary<string, int> features)
        {
            EngineVersion = WorldPackPolicy.EngineVersion(version);
            WorldPackPolicy.Require(features != null && features.All(f => WorldPackPolicy.SafeId(f.Key) && f.Value >= 1 && f.Value <= 65535), "engine capabilities");
            Features = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(features, StringComparer.Ordinal));
        }
    }

    public sealed class PackElementDecision
    {
        public readonly PackElement Element;
        public readonly PackVariant Variant;
        public readonly string Reason;
        public bool Loaded => Variant != null;
        internal PackElementDecision(PackElement element, PackVariant variant, string reason)
        { Element = element; Variant = variant; Reason = reason; }
    }

    public sealed class PackCompatibility
    {
        public readonly bool CanActivate;
        public readonly IReadOnlyList<PackElementDecision> Decisions;
        public readonly string Reason, SubsetIdentity, GameplayIdentity, ManifestSha256;
        internal PackCompatibility(bool canActivate, List<PackElementDecision> decisions, string reason, WorldPackManifest manifest)
        {
            CanActivate = canActivate; Decisions = decisions.AsReadOnly(); Reason = reason; ManifestSha256 = manifest.ManifestSha256;
            // Full manifest identity protects content; resolved identities distinguish authored fallbacks/omissions.
            SubsetIdentity = Identity(manifest, decisions, false); GameplayIdentity = Identity(manifest, decisions, true);
        }
        static string Identity(WorldPackManifest manifest, IEnumerable<PackElementDecision> decisions, bool gameplayOnly)
        {
            var files = manifest.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var byId = decisions.ToDictionary(d => d.Element.Id, StringComparer.Ordinal);
            var included = new HashSet<string>(byId.Values.Where(d => !gameplayOnly || d.Element.AffectsGameplay).Select(d => d.Element.Id), StringComparer.Ordinal);
            var pending = new Queue<string>(included);
            // A visual-labelled shared asset can still be a dependency of physical content.
            // Include the complete dependency closure rather than trusting the root role alone.
            while (pending.Count > 0)
                foreach (string dependency in byId[pending.Dequeue()].Element.Dependencies)
                    if (included.Add(dependency)) pending.Enqueue(dependency);
            var rows = included.OrderBy(id => id, StringComparer.Ordinal).Select(id =>
            {
                var d = byId[id]; var b = d.Element.Bounds;
                string bounds = string.Join(",", new[] { b.West, b.South, b.East, b.North }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                string dependencies = string.Join(",", d.Element.Dependencies.OrderBy(value => value, StringComparer.Ordinal));
                return id + ":" + bounds + ":" + (d.Element.RequiredForPlay ? "required" : "optional") + ":" + (d.Element.AffectsGameplay ? "gameplay" : "visual") + ":" + dependencies
                    + ":" + (d.Loaded ? d.Variant.Id + ":" + d.Variant.Feature + ":" + d.Variant.Schema.ToString(CultureInfo.InvariantCulture)
                        + ":" + string.Join(",", d.Variant.Files.OrderBy(f => f, StringComparer.Ordinal).Select(f => f + "=" + files[f].Bytes.ToString(CultureInfo.InvariantCulture) + "=" + files[f].Sha256)) : "SKIPPED");
            });
            // Decorative metadata/content revisions do not invalidate unchanged racing geometry.
            // Payload consumers must still validate the declared gameplay/visual role before activation.
            string source = gameplayOnly ? manifest.Id : manifest.ManifestSha256;
            return WorldPackPolicy.Hash(Encoding.UTF8.GetBytes("RoadRushPackSubset1|" + source + "|" + string.Join("|", rows)));
        }
    }

    public static class WorldPackCompatibility
    {
        /// <summary>Resolve once before payload parsing/activation. Spatial streaming then selects from this stable session subset.</summary>
        public static PackCompatibility Resolve(WorldPackManifest pack, PackEngineSupport engine)
        {
            if (pack == null || engine == null) throw new ArgumentNullException();
            bool containerSupported = engine.EngineVersion.CompareTo(pack.MinimumEngine) >= 0;
            var decisions = new List<PackElementDecision>(); var resolved = new Dictionary<string, PackElementDecision>(StringComparer.Ordinal);
            while (decisions.Count < pack.Elements.Count)
            {
                foreach (var element in pack.Elements)
                {
                    if (resolved.ContainsKey(element.Id) || !element.Dependencies.All(resolved.ContainsKey)) continue;
                    PackVariant chosen = null; string reason;
                    if (!containerSupported) reason = "Requires engine " + pack.MinimumEngine;
                    else if (element.Dependencies.Any(id => !resolved[id].Loaded)) reason = "Unavailable dependency: " + string.Join(", ", element.Dependencies.Where(id => !resolved[id].Loaded));
                    else
                    {
                        chosen = element.Variants.FirstOrDefault(v => engine.EngineVersion.CompareTo(v.MinimumEngine) >= 0
                            && engine.Features.TryGetValue(v.Feature, out int schema) && schema >= v.Schema);
                        reason = chosen == null ? "Unsupported variants: " + string.Join(", ", element.Variants.Select(v => v.Feature + "/" + v.Schema + " (engine " + v.MinimumEngine + ")"))
                            : (ReferenceEquals(chosen, element.Variants[0]) ? "Supported" : "Supported authored fallback " + chosen.Id);
                    }
                    var decision = new PackElementDecision(element, chosen, reason); resolved.Add(element.Id, decision); decisions.Add(decision);
                }
            }
            var missing = decisions.Where(d => d.Element.RequiredForPlay && !d.Loaded).ToArray();
            bool valid = containerSupported && missing.Length == 0;
            string summary = !containerSupported ? "Pack requires engine " + pack.MinimumEngine : missing.Length > 0
                ? "Missing essential content: " + string.Join(", ", missing.Select(d => d.Element.Id))
                : decisions.Any(d => !d.Loaded) ? "Compatible; optional content unavailable" : "Compatible";
            return new PackCompatibility(valid, decisions, summary, pack);
        }
    }
}
