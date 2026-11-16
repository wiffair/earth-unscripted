using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted.Packs
{
    /// <summary>One independently selectable driving layout. Unsupported siblings remain
    /// discoverable metadata; they cannot supply driving geometry through a decorative fallback.</summary>
    public sealed class PackCircuitLayout
    {
        public readonly string Id, Name, ElementId, WorldId, UnavailableReason, LaunchToken, ManifestSha256;
        public readonly bool Readable;
        internal readonly PackFile Payload;
        internal PackCircuitLayout(string id, string name, string element, string worldId,
            PackFile payload, bool readable, string reason, string manifestHash)
        {
            Id = id; Name = name; ElementId = element; WorldId = worldId; Payload = payload;
            Readable = readable; UnavailableReason = readable ? null : reason;
            ManifestSha256 = manifestHash;
            LaunchToken = "packcircuit-" + WorldPackPolicy.Hash(Encoding.UTF8.GetBytes(
                "EarthUnscriptedCircuitLaunch1|" + manifestHash + "|" + id + "|" + payload.Path + "|" + payload.Sha256));
        }
    }

    /// <summary>
    /// A verified standalone venue catalogue with independent layout payloads. This is a
    /// data reader, not a second track renderer or permission to start an unvalidated race.
    /// Normal boot must still decode/finalize the chosen owned GeoRegion and validate its
    /// circuit/route contract before publishing actors. Racing rules and event presets are
    /// separate features; none can execute code or silently replace a missing driving layout.
    /// </summary>
    public sealed partial class WorldPackCircuitReader
    {
        const int MaximumCatalogueBytes = 256 * 1024, MaximumLayouts = 64;
        const int MaximumLayoutBytes = EarthUnscripted.Core.DataPackageRules.MaximumFileBytes;
        readonly string directory;
        public readonly string VenueId, Name, ManifestSha256, CompatibilityIdentity;
        public readonly IReadOnlyList<PackCircuitLayout> Layouts;

        WorldPackCircuitReader(string path, WorldPackManifest manifest, PackCompatibility compatible, CancellationToken cancellation)
        {
            directory = path; ManifestSha256 = manifest.ManifestSha256; CompatibilityIdentity = compatible.SubsetIdentity;
            WorldPackPolicy.Require(manifest.Kind == "circuit", "circuit venue requires a circuit pack");
            var catalogue = compatible.Decisions.SingleOrDefault(d => d.Loaded && d.Variant.Feature == "circuit-catalogue" && d.Variant.Schema == 1);
            WorldPackPolicy.Require(catalogue != null && catalogue.Variant.Files.Contains("circuit.json"), "supported circuit catalogue contract");
            var descriptorFile = manifest.Files.Single(f => f.Path == "circuit.json");
            JObject value;
            using (var input = new JsonTextReader(new StringReader(ReadChecked(descriptorFile, MaximumCatalogueBytes, cancellation)))
                { MaxDepth = 8, DateParseHandling = DateParseHandling.None })
            {
                value = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                WorldPackPolicy.Require(!input.Read(), "trailing circuit catalogue data");
            }
            WorldPackPolicy.Require((string)value["format"] == "EarthUnscriptedCircuitPack1", "circuit catalogue format");
            VenueId = Id(value, "venueId"); Name = Text(value, "name", 160);
            var declared = value["layouts"] as JArray;
            WorldPackPolicy.Require(declared != null && declared.Count > 0 && declared.Count <= MaximumLayouts, "circuit layout catalogue budget");
            var rows = new List<PackCircuitLayout>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            var elements = new HashSet<string>(StringComparer.Ordinal); var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in declared)
            {
                cancellation.ThrowIfCancellationRequested();
                WorldPackPolicy.Require(item is JObject, "circuit layout descriptor object"); var row = (JObject)item;
                string id = Id(row, "id"), elementId = Id(row, "elementId"), filePath = Text(row, "payload", 180);
                string worldId = Text(row, "worldId", 100);
                WorldPackPolicy.Require(WorldPackPolicy.SafePath(filePath) && ids.Add(id) && elements.Add(elementId) && paths.Add(filePath), "duplicate/unsafe circuit layout identity");
                var decision = compatible.Decisions.SingleOrDefault(d => d.Element.Id == elementId);
                WorldPackPolicy.Require(decision != null && decision.Element.Dependencies.Contains(catalogue.Element.Id)
                    && decision.Element.Variants.Any(v => v.Files.Contains(filePath)), "circuit layout/index dependency or payload binding");
                var payload = manifest.Files.Single(f => f.Path == filePath);
                bool readable = decision.Loaded && decision.Variant.Feature == "circuit-layout" && decision.Variant.Schema == 1;
                if (readable) WorldPackPolicy.Require(decision.Element.AffectsGameplay && decision.Variant.Files.Count == 1 && decision.Variant.Files[0] == filePath
                    && payload.Bytes > 0 && payload.Bytes <= MaximumLayoutBytes, "circuit layout payload contract/budget");
                rows.Add(new PackCircuitLayout(id, Text(row, "name", 160), elementId, worldId, payload, readable,
                    decision.Loaded ? "Selected representation is not supported driving geometry" : decision.Reason, manifest.ManifestSha256));
            }
            Layouts = new ReadOnlyCollection<PackCircuitLayout>(rows);
            ReadEventCatalogue(value, manifest, compatible, catalogue.Element.Id);
            ReadSceneryCatalogue(manifest, compatible);
            ReadRoadSurfaceCatalogue(manifest, compatible);
            ReadPitComplexCatalogue(manifest, compatible);
        }
        static string Text(JObject value, string key, int maximum)
        {
            var token = value[key]; WorldPackPolicy.Require(token?.Type == JTokenType.String, "circuit " + key + " text");
            string text = (string)token; WorldPackPolicy.Require(!string.IsNullOrWhiteSpace(text) && text.Length <= maximum
                && !text.Any(char.IsControl), "circuit " + key + " length/characters"); return text;
        }
        static string Id(JObject value, string key)
        { string id = Text(value, key, 64); WorldPackPolicy.Require(WorldPackPolicy.SafeId(id), "circuit " + key + " identifier"); return id; }

        string ReadChecked(PackFile file, int maximum, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); string path = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            WorldPackStore.CheckAncestors(path); byte[] bytes;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                WorldPackPolicy.Require(input.Length == file.Bytes && input.Length > 0 && input.Length <= maximum, "circuit payload size");
                bytes = new byte[(int)input.Length]; int at = 0;
                while (at < bytes.Length) { cancellation.ThrowIfCancellationRequested(); int n = input.Read(bytes, at, Math.Min(65536, bytes.Length - at)); if (n == 0) throw new EndOfStreamException(); at += n; }
                WorldPackPolicy.Require(input.ReadByte() == -1, "circuit payload changed while reading");
            }
            WorldPackPolicy.Require(WorldPackPolicy.Hash(bytes) == file.Sha256, "circuit payload content changed");
            cancellation.ThrowIfCancellationRequested(); return new UTF8Encoding(false, true).GetString(bytes);
        }
        public static WorldPackCircuitReader OpenVerified(WorldPackStore store, InstalledWorldPack installed,
            PackEngineSupport engine, CancellationToken cancellation = default)
        {
            if (store == null || installed == null || engine == null) throw new ArgumentNullException();
            var manifest = store.VerifyInstalled(installed.Directory, installed.Manifest.ManifestSha256, cancellation);
            var compatible = WorldPackCompatibility.Resolve(manifest, engine);
            WorldPackPolicy.Require(compatible.CanActivate, compatible.Reason);
            cancellation.ThrowIfCancellationRequested(); return new WorldPackCircuitReader(installed.Directory, manifest, compatible, cancellation);
        }
        public PackCircuitLayout Layout(string id)
        {
            var layout = Layouts.SingleOrDefault(l => l.Id == id);
            WorldPackPolicy.Require(layout != null, "selected circuit layout is absent"); return layout;
        }
        public string ReadRawLayoutJson(string id, CancellationToken cancellation = default)
        {
            var layout = Layout(id); WorldPackPolicy.Require(layout.Readable, "selected circuit layout unavailable: " + layout.UnavailableReason);
            return ReadChecked(layout.Payload, MaximumLayoutBytes, cancellation);
        }
    }
}
