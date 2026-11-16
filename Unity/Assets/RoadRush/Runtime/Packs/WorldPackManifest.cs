using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoadRush.Packs
{
    /// <summary>Cross-platform container policy. Budgets bound authoring input, not visible scenery or active physics.</summary>
    public static class WorldPackPolicy
    {
        public const int FormatVersion = 1;
        public const int MaximumManifestBytes = 1024 * 1024;
        public const int MaximumFiles = 16384;
        public const int MaximumElements = 8192;
        public const long MaximumFileBytes = 256L * 1024 * 1024;
        public const long MaximumInstalledBytes = 8L * 1024 * 1024 * 1024;
        public const string ManifestName = "pack.json";

        public static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidDataException("World pack: " + reason); }

        public static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        public static bool SafeId(string text) => text != null && Regex.IsMatch(text, "\\A[a-z0-9][a-z0-9-]{0,63}\\z");

        /// <summary>Use identical portable names on Windows and Mac; reject aliases, device names and traversal.</summary>
        public static bool SafePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 180 || path.IndexOf('\\') >= 0) return false;
            if (string.Equals(path.Split('/')[0], ManifestName, StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith(" ", StringComparison.Ordinal) || !Regex.IsMatch(segment, "\\A[A-Za-z0-9_. -]+\\z")) return false;
                string stem = segment.Split('.')[0];
                if (Regex.IsMatch(stem, "\\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\\z", RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        public static Version EngineVersion(string value)
        {
            Require(value != null && Regex.IsMatch(value, "\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z"), "engine version needs major.minor.patch");
            Require(Version.TryParse(value, out var version) && version.Major <= 65535 && version.Minor <= 65535 && version.Build <= 65535, "engine version range");
            return version;
        }
    }

    /// <summary>WGS84 footprint used for spatial selection; heights remain in the layer's declared coordinate frame.</summary>
    public readonly struct PackBounds
    {
        public readonly double West, South, East, North;
        public PackBounds(double west, double south, double east, double north)
        {
            WorldPackPolicy.Require(new[] { west, south, east, north }.All(v => !double.IsNaN(v) && !double.IsInfinity(v))
                && west >= -180 && east <= 180 && south >= -80 && north <= 80 && west < east && south < north, "geographic bounds");
            West = west; South = south; East = east; North = north;
        }
        public bool Intersects(PackBounds b) => West <= b.East && East >= b.West && South <= b.North && North >= b.South;
        public bool Contains(PackBounds b) => West <= b.West && East >= b.East && South <= b.South && North >= b.North;
    }

    public sealed class PackFile
    {
        public readonly string Path, Sha256;
        public readonly long Bytes;
        internal PackFile(string path, long bytes, string sha256) { Path = path; Bytes = bytes; Sha256 = sha256; }
    }

    /// <summary>Ordered alternatives belong to one logical element: choose one, never both a new feature and its fallback.</summary>
    public sealed class PackVariant
    {
        public readonly string Id, Feature;
        public readonly int Schema;
        public readonly Version MinimumEngine;
        public readonly IReadOnlyList<string> Files;
        internal PackVariant(string id, string feature, int schema, Version engine, List<string> files)
        { Id = id; Feature = feature; Schema = schema; MinimumEngine = engine; Files = files.AsReadOnly(); }
    }

    public sealed class PackElement
    {
        public readonly string Id;
        public readonly bool RequiredForPlay, AffectsGameplay;
        public readonly PackBounds Bounds;
        public readonly IReadOnlyList<string> Dependencies;
        public readonly IReadOnlyList<PackVariant> Variants;
        internal PackElement(string id, bool required, bool gameplay, PackBounds bounds, List<string> dependencies, List<PackVariant> variants)
        { Id = id; RequiredForPlay = required; AffectsGameplay = gameplay; Bounds = bounds; Dependencies = dependencies.AsReadOnly(); Variants = variants.AsReadOnly(); }
    }

    /// <summary>Immutable validated metadata. It never grants permission to instantiate unknown payloads or execute pack code.</summary>
    public sealed class WorldPackManifest
    {
        public readonly string Id, Name, Kind, Licence, Attribution, ManifestSha256;
        public readonly long Revision, InstalledBytes;
        public readonly Version MinimumEngine;
        public readonly PackBounds Bounds;
        public readonly IReadOnlyList<PackFile> Files;
        public readonly IReadOnlyList<PackElement> Elements;

        WorldPackManifest(string id, string name, string kind, long revision, string licence, string attribution,
            Version engine, PackBounds bounds, List<PackFile> files, List<PackElement> elements, string hash, long bytes)
        {
            Id = id; Name = name; Kind = kind; Revision = revision; Licence = licence; Attribution = attribution;
            MinimumEngine = engine; Bounds = bounds; Files = files.AsReadOnly(); Elements = elements.AsReadOnly();
            ManifestSha256 = hash; InstalledBytes = bytes;
        }

        static string Text(JObject item, string key, int maximum)
        {
            var token = item[key]; WorldPackPolicy.Require(token != null && token.Type == JTokenType.String, "missing text " + key);
            string value = (string)token; WorldPackPolicy.Require(!string.IsNullOrWhiteSpace(value) && value.Length <= maximum, "text length " + key); return value;
        }
        static long Integer(JObject item, string key, long low, long high)
        {
            var token = item[key]; WorldPackPolicy.Require(token != null && token.Type == JTokenType.Integer, "missing integer " + key);
            long value = token.Value<long>(); WorldPackPolicy.Require(value >= low && value <= high, "integer range " + key); return value;
        }
        static bool Flag(JObject item, string key)
        { WorldPackPolicy.Require(item[key]?.Type == JTokenType.Boolean, "missing boolean " + key); return (bool)item[key]; }
        static string IdOf(JObject item, string key)
        { string id = Text(item, key, 64); WorldPackPolicy.Require(WorldPackPolicy.SafeId(id), "invalid ID " + key); return id; }
        static PackBounds BoundsOf(JObject item)
        {
            var values = item["bounds"] as JArray; WorldPackPolicy.Require(values != null && values.Count == 4
                && values.All(t => t.Type == JTokenType.Float || t.Type == JTokenType.Integer), "bounds array");
            return new PackBounds((double)values[0], (double)values[1], (double)values[2], (double)values[3]);
        }
        static List<string> Strings(JObject item, string key, int maximum)
        {
            var values = item[key] as JArray; WorldPackPolicy.Require(values != null && values.Count <= maximum && values.All(t => t.Type == JTokenType.String), "array " + key);
            var result = values.Select(t => (string)t).ToList(); WorldPackPolicy.Require(result.Distinct(StringComparer.Ordinal).Count() == result.Count, "duplicate " + key); return result;
        }

        public static WorldPackManifest Parse(byte[] bytes)
        {
            WorldPackPolicy.Require(bytes != null && bytes.Length > 0 && bytes.Length <= WorldPackPolicy.MaximumManifestBytes, "manifest byte budget");
            JObject json;
            using (var input = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                json = JObject.Load(input, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                WorldPackPolicy.Require(!input.Read(), "trailing manifest content");
            }
            WorldPackPolicy.Require(Text(json, "format", 64) == "RoadRushWorldPack"
                && Integer(json, "formatVersion", 1, int.MaxValue) == WorldPackPolicy.FormatVersion, "unsupported container format");
            string id = IdOf(json, "id"), kind = Text(json, "kind", 32);
            WorldPackPolicy.Require(kind == "regional" || kind == "circuit", "unsupported pack kind");
            var engine = WorldPackPolicy.EngineVersion(Text(json, "minEngineVersion", 32)); var bounds = BoundsOf(json);
            var fileRows = json["files"] as JArray; WorldPackPolicy.Require(fileRows != null && fileRows.Count > 0 && fileRows.Count <= WorldPackPolicy.MaximumFiles, "file count");
            var files = new List<PackFile>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var row in fileRows)
            {
                var obj = row as JObject; WorldPackPolicy.Require(obj != null, "file object"); string path = Text(obj, "path", 180), hash = Text(obj, "sha256", 64);
                WorldPackPolicy.Require(WorldPackPolicy.SafePath(path) && paths.Add(path) && Regex.IsMatch(hash, "\\A[a-f0-9]{64}\\z"), "file path/hash or case alias");
                string directory = ""; var components = path.Split('/');
                for (int i = 0; i < components.Length - 1; i++)
                {
                    directory = directory.Length == 0 ? components[i] : directory + "/" + components[i];
                    WorldPackPolicy.Require(!directories.TryGetValue(directory, out string previous) || previous == directory, "inconsistent directory casing");
                    directories[directory] = directory;
                }
                long length = Integer(obj, "bytes", 1, WorldPackPolicy.MaximumFileBytes); total = checked(total + length);
                WorldPackPolicy.Require(total <= WorldPackPolicy.MaximumInstalledBytes, "installed byte budget"); files.Add(new PackFile(path, length, hash));
            }
            // A file and a directory cannot share an alias on either supported platform.
            foreach (var file in files)
            { var parts = file.Path.Split('/'); string prefix = parts[0]; for (int i = 1; i < parts.Length; i++) { WorldPackPolicy.Require(!paths.Contains(prefix), "file/directory collision"); prefix += "/" + parts[i]; } }
            var elementRows = json["elements"] as JArray; WorldPackPolicy.Require(elementRows != null && elementRows.Count > 0 && elementRows.Count <= WorldPackPolicy.MaximumElements, "element count");
            var elements = new List<PackElement>(); var ids = new HashSet<string>(StringComparer.Ordinal); var referenced = new HashSet<string>(StringComparer.Ordinal);
            var exactPaths = new HashSet<string>(files.Select(f => f.Path), StringComparer.Ordinal);
            foreach (var row in elementRows)
            {
                var obj = row as JObject; WorldPackPolicy.Require(obj != null, "element object"); string elementId = IdOf(obj, "id");
                WorldPackPolicy.Require(ids.Add(elementId), "duplicate element"); var localBounds = BoundsOf(obj); WorldPackPolicy.Require(bounds.Contains(localBounds), "element outside pack bounds");
                var dependencies = Strings(obj, "dependsOn", 64); WorldPackPolicy.Require(dependencies.All(WorldPackPolicy.SafeId) && !dependencies.Contains(elementId), "element dependencies");
                var variants = obj["variants"] as JArray; WorldPackPolicy.Require(variants != null && variants.Count > 0 && variants.Count <= 8, "variant count");
                var options = new List<PackVariant>(); var variantIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var variant in variants)
                {
                    var v = variant as JObject; WorldPackPolicy.Require(v != null, "variant object"); string vid = IdOf(v, "id"); WorldPackPolicy.Require(variantIds.Add(vid), "duplicate variant");
                    var vf = Strings(v, "files", 1024); WorldPackPolicy.Require(vf.Count > 0 && vf.All(exactPaths.Contains), "variant file references"); foreach (string f in vf) referenced.Add(f);
                    options.Add(new PackVariant(vid, IdOf(v, "feature"), (int)Integer(v, "schema", 1, 65535), WorldPackPolicy.EngineVersion(Text(v, "minEngineVersion", 32)), vf));
                }
                elements.Add(new PackElement(elementId, Flag(obj, "requiredForPlay"), Flag(obj, "affectsGameplay"), localBounds, dependencies, options));
            }
            WorldPackPolicy.Require(referenced.SetEquals(exactPaths), "unreferenced files");
            WorldPackPolicy.Require(elements.All(e => e.Dependencies.All(ids.Contains)), "missing dependency");
            // Iterative topological validation also bounds malicious/deep dependency graphs.
            var done = new HashSet<string>(StringComparer.Ordinal);
            while (done.Count < elements.Count)
            { int previous = done.Count; foreach (var e in elements) if (e.Dependencies.All(done.Contains)) done.Add(e.Id); WorldPackPolicy.Require(done.Count > previous, "dependency cycle"); }
            return new WorldPackManifest(id, Text(json, "name", 160), kind, Integer(json, "revision", 1, long.MaxValue), Text(json, "licence", 2000), Text(json, "attribution", 4000), engine, bounds, files, elements, WorldPackPolicy.Hash(bytes), total);
        }
    }
}
