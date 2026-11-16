using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoadRush.Packs
{
    /// <summary>
    /// A user's local pack preference, stored independently of career saves. This is
    /// intent, never a validation certificate or permission to publish unverified content.
    /// The selected revision is pinned; installing an update cannot silently switch it.
    /// </summary>
    public sealed class WorldPackSelection
    {
        const int MaximumBytes = 8192;
        const string FileName = "selection.json";
        public readonly bool Enabled, AllowCandidatePreview;
        public readonly string Id, ManifestSha256;
        public readonly long Revision;
        public static WorldPackSelection Disabled => new WorldPackSelection();
        WorldPackSelection() { }
        WorldPackSelection(string id, long revision, string hash, bool preview)
        {
            WorldPackPolicy.Require(WorldPackPolicy.SafeId(id) && revision >= 1,
                "saved pack selection identity");
            WorldPackPolicy.Require(hash != null && Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z"), "saved pack selection hash");
            Enabled = true; Id = id; Revision = revision; ManifestSha256 = hash; AllowCandidatePreview = preview;
        }
        public static WorldPackSelection For(WorldPackManifest manifest, bool allowCandidatePreview)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            return new WorldPackSelection(manifest.Id, manifest.Revision, manifest.ManifestSha256, allowCandidatePreview);
        }
        /// <summary>Construct an exact immutable lookup pin; this confers no activation/validation authority.</summary>
        public static WorldPackSelection Pinned(string id, long revision, string manifestSha256)
            => new WorldPackSelection(id, revision, manifestSha256, false);
        public bool Matches(WorldPackManifest manifest) => Enabled && manifest != null && Id == manifest.Id
            && Revision == manifest.Revision && ManifestSha256 == manifest.ManifestSha256;

        public static WorldPackSelection Load(string libraryRoot)
        {
            string root = Path.GetFullPath(libraryRoot), path = Path.Combine(root, FileName);
            WorldPackStore.CheckAncestors(path);
            if (!File.Exists(path)) return Disabled; // A read never creates a settings/cache directory.
            byte[] bytes;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                WorldPackPolicy.Require(input.Length > 0 && input.Length <= MaximumBytes, "saved pack selection budget");
                bytes = new byte[(int)input.Length]; int at = 0;
                while (at < bytes.Length) { int n = input.Read(bytes, at, bytes.Length - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                WorldPackPolicy.Require(input.ReadByte() == -1, "saved pack selection changed during read");
            }
            using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
                { MaxDepth = 4, DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                WorldPackPolicy.Require(!reader.Read() && value["format"]?.Type == JTokenType.Integer
                    && (int)value["format"] == 1 && value["enabled"]?.Type == JTokenType.Boolean, "saved pack selection format");
                bool enabled = (bool)value["enabled"];
                string[] fields = enabled ? new[] { "format", "enabled", "id", "revision", "manifestSha256", "allowCandidatePreview" }
                    : new[] { "format", "enabled" };
                WorldPackPolicy.Require(value.Count == fields.Length && fields.All(f => value.Property(f) != null), "saved pack selection fields");
                if (!enabled) return Disabled;
                WorldPackPolicy.Require(value["id"].Type == JTokenType.String && value["revision"].Type == JTokenType.Integer
                    && value["manifestSha256"].Type == JTokenType.String && value["allowCandidatePreview"].Type == JTokenType.Boolean,
                    "saved pack selection value types");
                return new WorldPackSelection((string)value["id"], (long)value["revision"],
                    (string)value["manifestSha256"], (bool)value["allowCandidatePreview"]);
            }
        }

        /// <summary>Publish a small preference atomically; preserve the previous file for recovery.
        /// Failure or cancellation before replacement leaves the previous selection intact.</summary>
        public void Save(string libraryRoot, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            string root = Path.GetFullPath(libraryRoot), path = Path.Combine(root, FileName), previous = path + ".previous";
            WorldPackStore.CheckAncestors(root); Directory.CreateDirectory(root); WorldPackStore.CheckAncestors(root);
            string lockPath = Path.Combine(root, ".selection.lock"); WorldPackStore.CheckAncestors(lockPath);
            using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                WorldPackStore.CheckAncestors(path); WorldPackStore.CheckAncestors(previous);
                var value = new JObject { ["format"] = 1, ["enabled"] = Enabled };
                if (Enabled) { value["id"] = Id; value["revision"] = Revision; value["manifestSha256"] = ManifestSha256; value["allowCandidatePreview"] = AllowCandidatePreview; }
                byte[] bytes = Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
                WorldPackPolicy.Require(bytes.Length <= MaximumBytes, "saved pack selection budget");
                string stage = Path.Combine(root, ".selection-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                    cancellation.ThrowIfCancellationRequested();
                    WorldPackStore.CheckAncestors(path); WorldPackStore.CheckAncestors(previous);
                    if (File.Exists(path)) File.Replace(stage, path, previous); else File.Move(stage, path);
                }
                finally { if (File.Exists(stage)) File.Delete(stage); }
            }
        }
    }
}
