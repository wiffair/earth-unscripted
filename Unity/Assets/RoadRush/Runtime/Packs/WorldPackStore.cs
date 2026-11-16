using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace RoadRush.Packs
{
    public sealed class InstalledWorldPack
    {
        public readonly string Directory;
        public readonly WorldPackManifest Manifest;
        public readonly bool AlreadyInstalled;
        internal InstalledWorldPack(string directory, WorldPackManifest manifest, bool alreadyInstalled)
        { Directory = directory; Manifest = manifest; AlreadyInstalled = alreadyInstalled; }
    }

    public sealed class WorldPackInventoryItem
    {
        public readonly string Directory, Problem;
        public readonly WorldPackManifest Manifest;
        internal WorldPackInventoryItem(string directory, WorldPackManifest manifest, string problem)
        { Directory = directory; Manifest = manifest; Problem = problem; }
    }

    /// <summary>
    /// Data-only archive installation. Revisions are immutable and discovered separately from session activation.
    /// This class never enables content, modifies a career, fetches URLs or interprets unknown feature payloads.
    /// Call on an I/O worker; cancellation and bounded buffers prevent loading a regional archive into RAM.
    /// </summary>
    public sealed class WorldPackStore
    {
        const int BufferBytes = 64 * 1024;
        const int MaximumDiscoveredRevisions = 4096;
        readonly string root;
        public WorldPackStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Local pack directory required");
            root = Path.GetFullPath(directory); CheckAncestors(root);
        }

        internal static void CheckAncestors(string path)
        {
            for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                if (File.Exists(current) || System.IO.Directory.Exists(current))
                    WorldPackPolicy.Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "linked path is not an owned pack store");
                else
                {
                    // GetAttributes also detects dangling links on supported runtimes.
                    try { WorldPackPolicy.Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "dangling linked path"); }
                    catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
                }
            }
        }

        static byte[] ReadManifest(Stream stream, long length)
        {
            WorldPackPolicy.Require(length > 0 && length <= WorldPackPolicy.MaximumManifestBytes, "manifest byte budget");
            var bytes = new byte[(int)length]; int offset = 0;
            while (offset < bytes.Length)
            { int n = stream.Read(bytes, offset, bytes.Length - offset); WorldPackPolicy.Require(n > 0, "truncated manifest"); offset += n; }
            WorldPackPolicy.Require(stream.ReadByte() == -1, "manifest length differs"); return bytes;
        }

        static Dictionary<string, ZipArchiveEntry> Entries(ZipArchive archive)
        {
            WorldPackPolicy.Require(archive.Entries.Count > 0 && archive.Entries.Count <= WorldPackPolicy.MaximumFiles * 2 + 1, "archive entry budget");
            var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                string name = directory ? entry.FullName.Substring(0, entry.FullName.Length - 1) : entry.FullName;
                WorldPackPolicy.Require(name == WorldPackPolicy.ManifestName && !directory || WorldPackPolicy.SafePath(name), "unsafe archive entry");
                WorldPackPolicy.Require(aliases.Add(entry.FullName), "duplicate/case-aliased archive entry");
                int type = (entry.ExternalAttributes >> 16) & 0xf000;
                WorldPackPolicy.Require(type == 0 || type == (directory ? 0x4000 : 0x8000), "archive links/devices unsupported");
                if (directory) { WorldPackPolicy.Require(entry.Length == 0, "directory has payload"); continue; }
                WorldPackPolicy.Require(result.TryAdd(name, entry), "duplicate archive file");
            }
            WorldPackPolicy.Require(result.ContainsKey(WorldPackPolicy.ManifestName), "missing pack.json"); return result;
        }

        static WorldPackManifest OpenManifest(Dictionary<string, ZipArchiveEntry> entries, out byte[] bytes)
        {
            var entry = entries[WorldPackPolicy.ManifestName];
            using (var stream = entry.Open()) bytes = ReadManifest(stream, entry.Length);
            var manifest = WorldPackManifest.Parse(bytes);
            WorldPackPolicy.Require(entries.Count == manifest.Files.Count + 1 && manifest.Files.All(f => entries.ContainsKey(f.Path)), "unlisted/missing archive files");
            foreach (var file in manifest.Files) WorldPackPolicy.Require(entries[file.Path].Length == file.Bytes, "declared/archive length differs: " + file.Path);
            return manifest;
        }

        static void CopyChecked(Stream input, Stream output, PackFile file, CancellationToken token)
        {
            byte[] buffer = new byte[BufferBytes]; long copied = 0;
            using (var sha = SHA256.Create())
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested(); int read = input.Read(buffer, 0, buffer.Length); if (read == 0) break;
                    copied = checked(copied + read); WorldPackPolicy.Require(copied <= file.Bytes, "expanded file byte budget");
                    sha.TransformBlock(buffer, 0, read, buffer, 0); output.Write(buffer, 0, read);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                WorldPackPolicy.Require(copied == file.Bytes && BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant() == file.Sha256, "payload hash/length mismatch: " + file.Path);
            }
        }

        public WorldPackManifest InspectArchive(string archivePath, CancellationToken token = default)
        {
            CheckAncestors(archivePath);
            using (var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
            {
                var entries = Entries(archive); var manifest = OpenManifest(entries, out _);
                foreach (var file in manifest.Files)
                    using (var input = entries[file.Path].Open()) CopyChecked(input, Stream.Null, file, token);
                return manifest;
            }
        }

        /// <summary>Verify every byte while staging; publish the new revision with one same-volume directory rename.</summary>
        public InstalledWorldPack Install(string archivePath, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); CheckAncestors(root); CheckAncestors(archivePath);
            System.IO.Directory.CreateDirectory(root); CheckAncestors(root);
            // One process may install at a time. The persistent lock file contains no credentials or active selection.
            string lockPath = Path.Combine(root, ".install.lock"); CheckAncestors(lockPath);
            using (var installationLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
            {
                var entries = Entries(archive); var manifest = OpenManifest(entries, out byte[] bytes);
                string packRoot = Path.Combine(root, "packs", manifest.Id);
                string destination = Path.Combine(packRoot, manifest.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + manifest.ManifestSha256);
                CheckAncestors(destination);
                if (System.IO.Directory.Exists(destination))
                { VerifyInstalled(destination, manifest.ManifestSha256, token); return new InstalledWorldPack(destination, manifest, true); }
                string staging = Path.Combine(root, ".staging", Guid.NewGuid().ToString("N")); CheckAncestors(staging);
                System.IO.Directory.CreateDirectory(staging);
                try
                {
                    foreach (var file in manifest.Files)
                    {
                        token.ThrowIfCancellationRequested(); string target = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var input = entries[file.Path].Open())
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { CopyChecked(input, output, file, token); output.Flush(true); }
                    }
                    string manifestPath = Path.Combine(staging, WorldPackPolicy.ManifestName);
                    using (var output = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                    VerifyInstalled(staging, manifest.ManifestSha256, token);
                    token.ThrowIfCancellationRequested(); System.IO.Directory.CreateDirectory(packRoot); CheckAncestors(packRoot);
                    System.IO.Directory.Move(staging, destination);
                    return new InstalledWorldPack(destination, manifest, false);
                }
                finally { if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true); }
            }
        }

        public WorldPackManifest VerifyInstalled(string directory, string expectedManifestHash, CancellationToken token = default)
        {
            CheckAncestors(directory); string manifestPath = Path.Combine(directory, WorldPackPolicy.ManifestName); CheckAncestors(manifestPath);
            WorldPackManifest manifest;
            using (var input = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read)) manifest = WorldPackManifest.Parse(ReadManifest(input, input.Length));
            WorldPackPolicy.Require(manifest.ManifestSha256 == expectedManifestHash, "installed manifest identity differs");
            var allowed = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.Ordinal) { WorldPackPolicy.ManifestName };
            foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            { CheckAncestors(path); if (File.Exists(path)) WorldPackPolicy.Require(allowed.Contains(path.Substring(directory.Length + 1).Replace(Path.DirectorySeparatorChar, '/')), "unlisted installed file"); }
            foreach (var file in manifest.Files)
            {
                string path = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar)); CheckAncestors(path);
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) CopyChecked(input, Stream.Null, file, token);
            }
            return manifest;
        }

        /// <summary>Locate an exact saved revision without scanning every other installed manifest.
        /// Metadata only: OpenVerified must still check all content before activation.</summary>
        public InstalledWorldPack Locate(WorldPackSelection selection)
        {
            if (selection == null || !selection.Enabled) throw new ArgumentException("An enabled pack selection is required");
            string directory = Path.Combine(root, "packs", selection.Id,
                selection.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + selection.ManifestSha256);
            CheckAncestors(directory);
            string path = Path.Combine(directory, WorldPackPolicy.ManifestName); CheckAncestors(path);
            WorldPackManifest manifest;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                manifest = WorldPackManifest.Parse(ReadManifest(input, input.Length));
            WorldPackPolicy.Require(manifest.Id == selection.Id && manifest.Revision == selection.Revision
                && manifest.ManifestSha256 == selection.ManifestSha256, "saved pack revision identity differs");
            return new InstalledWorldPack(directory, manifest, true);
        }

        /// <summary>Metadata discovery only. Activation must separately verify content, capabilities, validation records and current session pins.</summary>
        public IReadOnlyList<WorldPackInventoryItem> Discover()
        {
            var rows = new List<WorldPackInventoryItem>(); string packs = Path.Combine(root, "packs"); CheckAncestors(packs);
            if (!System.IO.Directory.Exists(packs)) return rows.AsReadOnly();
            foreach (string family in System.IO.Directory.EnumerateDirectories(packs).OrderBy(p => p, StringComparer.Ordinal))
            {
                CheckAncestors(family);
                foreach (string revision in System.IO.Directory.EnumerateDirectories(family).OrderBy(p => p, StringComparer.Ordinal))
                {
                    WorldPackPolicy.Require(rows.Count < MaximumDiscoveredRevisions, "inventory revision budget");
                    try
                    {
                        CheckAncestors(revision); string path = Path.Combine(revision, WorldPackPolicy.ManifestName); CheckAncestors(path); WorldPackManifest manifest;
                        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) manifest = WorldPackManifest.Parse(ReadManifest(input, input.Length));
                        WorldPackPolicy.Require(Path.GetFileName(family) == manifest.Id && Path.GetFileName(revision) == manifest.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + manifest.ManifestSha256, "installed directory identity");
                        rows.Add(new WorldPackInventoryItem(revision, manifest, null));
                    }
                    catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException
                        || error is Newtonsoft.Json.JsonException || error is ArgumentException || error is OverflowException)
                    { rows.Add(new WorldPackInventoryItem(revision, null, error.Message)); }
                }
            }
            return rows.AsReadOnly();
        }
    }
}
