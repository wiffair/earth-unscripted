using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace RoadRush.Packs
{
    /// <summary>A stored, authenticated claim. Importing a claim never trusts its issuer or activates a pack.</summary>
    public sealed class PackAttestationItem
    {
        public readonly string RecordSha256, Problem;
        public readonly WorldPackValidation Record;
        internal PackAttestationItem(string hash, WorldPackValidation record, string problem)
        { RecordSha256 = hash; Record = record; Problem = problem; }
    }

    /// <summary>
    /// Detached records live outside immutable pack revisions, avoiding a manifest/signature hash cycle.
    /// Call on an I/O worker with a manifest identity obtained from freshly verified pack bytes.
    /// This store has no issuer trust list, signing keys, career access or scene activation authority.
    /// </summary>
    public sealed class WorldPackAttestationStore
    {
        // A short bucket keeps Windows paths manageable; every record still checks the full manifest digest.
        public const int MaximumRecordsPerBucket = 256;
        readonly string root;
        public WorldPackAttestationStore(string libraryRoot)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot)) throw new ArgumentException("Local library required");
            root = Path.Combine(Path.GetFullPath(libraryRoot), "validations", "records");
            WorldPackStore.CheckAncestors(root);
        }
        static void Digest(string value)
        { WorldPackPolicy.Require(value != null && Regex.IsMatch(value, "\\A[a-f0-9]{64}\\z"), "attestation manifest digest"); }
        string Bucket(string manifest)
        { Digest(manifest); return Path.Combine(root, manifest.Substring(0, 16)); }
        static byte[] Read(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); WorldPackStore.CheckAncestors(path);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                WorldPackPolicy.Require(input.Length > 0 && input.Length <= WorldPackValidation.MaximumRecordBytes, "attestation byte budget");
                var bytes = new byte[(int)input.Length]; int offset = 0;
                while (offset < bytes.Length)
                {
                    token.ThrowIfCancellationRequested(); int count = input.Read(bytes, offset, bytes.Length - offset);
                    WorldPackPolicy.Require(count > 0, "truncated attestation"); offset += count;
                }
                WorldPackPolicy.Require(input.ReadByte() == -1, "attestation length changed"); return bytes;
            }
        }
        static WorldPackValidation Authenticate(byte[] bytes, string manifest)
        {
            var record = WorldPackValidation.Parse(bytes);
            WorldPackPolicy.Require(record.ManifestSha256 == manifest, "attestation belongs to another pack");
            WorldPackPolicy.Require(record.SignatureIsValid(), "attestation signature invalid"); return record;
        }
        /// <summary>Read a bounded, signed source claim before locating its installed pack. This grants no trust.</summary>
        public static WorldPackValidation ReadSourceClaim(string sourcePath, CancellationToken token = default)
        {
            byte[] bytes = Read(sourcePath, token); var record = WorldPackValidation.Parse(bytes);
            WorldPackPolicy.Require(record.SignatureIsValid(), "attestation signature invalid"); return record;
        }
        static List<string> Paths(string bucket, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var paths = new List<string>(); WorldPackStore.CheckAncestors(bucket);
            if (!Directory.Exists(bucket)) return paths;
            foreach (string path in Directory.EnumerateFileSystemEntries(bucket))
            {
                token.ThrowIfCancellationRequested();
                WorldPackPolicy.Require(paths.Count < MaximumRecordsPerBucket, "attestation inventory budget");
                paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal); return paths;
        }
        public PackAttestationItem Import(string sourcePath, string verifiedManifestSha256, CancellationToken token = default)
        {
            string bucket = Bucket(verifiedManifestSha256);
            byte[] bytes = Read(sourcePath, token); var record = Authenticate(bytes, verifiedManifestSha256);
            string hash = WorldPackPolicy.Hash(bytes), destination = Path.Combine(bucket, hash + ".json");
            token.ThrowIfCancellationRequested(); WorldPackStore.CheckAncestors(bucket);
            Directory.CreateDirectory(bucket); WorldPackStore.CheckAncestors(bucket);
            // Serialize imports across processes. The lock is outside the bounded record inventory.
            string lockPath = Path.Combine(root, ".import.lock"); WorldPackStore.CheckAncestors(lockPath);
            using (var gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var paths = Paths(bucket, token); WorldPackStore.CheckAncestors(destination);
                if (File.Exists(destination))
                {
                    byte[] existing = Read(destination, token);
                    WorldPackPolicy.Require(WorldPackPolicy.Hash(existing) == hash, "stored attestation was altered");
                    Authenticate(existing, verifiedManifestSha256);
                    return new PackAttestationItem(hash, record, null);
                }
                WorldPackPolicy.Require(paths.Count < MaximumRecordsPerBucket, "attestation inventory budget");
                string temporary = Path.Combine(root, ".record-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                    token.ThrowIfCancellationRequested(); WorldPackStore.CheckAncestors(destination);
                    File.Move(temporary, destination); // Immutable, atomic publication; never overwrite an existing record.
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return new PackAttestationItem(hash, record, null);
        }
        public IReadOnlyList<PackAttestationItem> Inspect(string verifiedManifestSha256, CancellationToken token = default)
        {
            var rows = new List<PackAttestationItem>();
            foreach (string path in Paths(Bucket(verifiedManifestSha256), token))
            {
                token.ThrowIfCancellationRequested(); string name = Path.GetFileName(path);
                try
                {
                    WorldPackPolicy.Require(Regex.IsMatch(name, "\\A[a-f0-9]{64}\\.json\\z"), "unexpected attestation entry");
                    byte[] bytes = Read(path, token); string hash = WorldPackPolicy.Hash(bytes);
                    WorldPackPolicy.Require(name == hash + ".json", "stored attestation digest mismatch");
                    rows.Add(new PackAttestationItem(hash, Authenticate(bytes, verifiedManifestSha256), null));
                }
                catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is Newtonsoft.Json.JsonException || error is ArgumentException)
                { rows.Add(new PackAttestationItem(null, null, error.Message)); }
            }
            return rows.AsReadOnly();
        }
    }
}
