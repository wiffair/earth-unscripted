using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    /// <summary>Versioned, bounded, lossless mesh cache. Worker threads own file
    /// compression and validation; callers capture/publish Unity objects only on
    /// the main thread. Materials/shared scenery assets are rebound by batch ID.
    /// Collision meshes are retained, but PhysX cooking still runs on reload.</summary>
    public sealed class FinishedGeometryStore
    {
        public sealed class Part
        {
            public Vector3[] vertices, normals;
            public int[] indices;
        }
        const int Magic = 0x52464731, MaximumPayloadBytes = 64 * 1024 * 1024;
        const long FreeSpaceReserveBytes = 2000000000;
        static readonly object FileGate = new object();
        sealed class FileRecord { public string path; public long bytes; public DateTime age; }
        sealed class Inventory
        {
            public readonly Dictionary<string, FileRecord> files = new Dictionary<string, FileRecord>();
            public readonly SortedSet<FileRecord> oldest = new SortedSet<FileRecord>(Comparer<FileRecord>.Create((a, b) =>
            { int order = a.age.CompareTo(b.age); return order != 0 ? order : StringComparer.Ordinal.Compare(a.path, b.path); }));
            public long bytes;
            public void Remove(string path)
            {
                if (!files.TryGetValue(path, out var item)) return;
                oldest.Remove(item); files.Remove(path); bytes -= item.bytes;
            }
            public void Set(string path, long length, DateTime age)
            {
                Remove(path); var item = new FileRecord { path = path, bytes = length, age = age };
                files.Add(path, item); oldest.Add(item); bytes += length;
            }
        }
        static readonly Dictionary<string, Inventory> Ages = new Dictionary<string, Inventory>();
        readonly string folder, settings;
        int pending;
        public long Hits, Misses, BytesRead, Writes, BytesWritten;
        public int Pending => Volatile.Read(ref pending);
        public FinishedGeometryStore(string worldCache)
        {
            folder = Path.Combine(worldCache, "FinishedGeometry");
            settings = Path.Combine(worldCache, "storage-settings.json");
        }
        public static string Digest(string text)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        static byte[] Digest(byte[] data) { using var sha = SHA256.Create(); return sha.ComputeHash(data); }
        static byte[] KeyBytes(string key)
        {
            if (key.Length != 64) throw new InvalidDataException("Finished cache key");
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(key.Substring(i * 2, 2), 16);
            return bytes;
        }
        [Serializable] sealed class Settings { public float finishedGigabytes; }
        static double Limit(string path)
        {
            try
            {
                if (!File.Exists(path)) return 5e9;
                if (new FileInfo(path).Length > 32768) return 5e9;
                string json = File.ReadAllText(path);
                if (!json.Contains("\"finishedGigabytes\"")) return 5e9;
                float value = JsonUtility.FromJson<Settings>(json).finishedGigabytes;
                return float.IsNaN(value) || float.IsInfinity(value) ? 5e9 : Math.Max(0, Math.Min(50, value)) * 1e9;
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException) { return 5e9; }
        }
        public Task<Part[]> Read(string key) => Task.Run(() =>
        {
            string path = Path.Combine(folder, key + ".eugeo");
            try
            {
                if (Limit(settings) == 0 || !File.Exists(path)) { Interlocked.Increment(ref Misses); return null; }
                var info = new FileInfo(path);
                if (info.Length > MaximumPayloadBytes) throw new InvalidDataException("Finished cache file budget");
                byte[] payload;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic || !reader.ReadBytes(32).SequenceEqual(KeyBytes(key))) throw new InvalidDataException("Finished cache identity");
                    int length = reader.ReadInt32();
                    if (length < 4 || length > MaximumPayloadBytes) throw new InvalidDataException("Finished cache payload budget");
                    var expected = reader.ReadBytes(32); payload = new byte[length];
                    using var gzip = new GZipStream(stream, CompressionMode.Decompress, true);
                    int offset = 0;
                    while (offset < length)
                    {
                        int count = gzip.Read(payload, offset, length - offset);
                        if (count == 0) throw new EndOfStreamException(); offset += count;
                    }
                    if (gzip.ReadByte() != -1 || !Digest(payload).SequenceEqual(expected)) throw new InvalidDataException("Finished cache checksum");
                }
                var parts = Decode(payload);
                lock (FileGate)
                {
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                    if (Ages.TryGetValue(folder, out var ages)) ages.Set(path, info.Length, DateTime.UtcNow);
                }
                Interlocked.Increment(ref Hits); Interlocked.Add(ref BytesRead, info.Length); return parts;
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is OverflowException)
            {
                // Derived cache failures are misses, never a failed world load.
                lock (FileGate) { try { if (File.Exists(path)) File.Delete(path); if (Ages.TryGetValue(folder, out var inventory)) inventory.Remove(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                Interlocked.Increment(ref Misses); return null;
            }
        });
        static Vector3[] Vectors(BinaryReader reader, int count)
        {
            var data = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(z) || float.IsInfinity(z)) throw new InvalidDataException("Non-finite finished mesh");
                data[i] = new Vector3(x, y, z);
            }
            return data;
        }
        static Part[] Decode(byte[] payload)
        {
            using var stream = new MemoryStream(payload, false); using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            if (count < 1 || count > 16) throw new InvalidDataException("Finished batch count");
            var parts = new Part[count];
            for (int i = 0; i < count; i++)
            {
                int vertices = reader.ReadInt32(), indices = reader.ReadInt32();
                if (vertices < 0 || vertices > 1000000 || indices < 0 || indices > 6000000 || indices % 3 != 0 || (long)vertices * 24 + (long)indices * 4 > stream.Length - stream.Position) throw new InvalidDataException("Finished mesh counts");
                var part = new Part { vertices = Vectors(reader, vertices), normals = Vectors(reader, vertices), indices = new int[indices] };
                for (int j = 0; j < indices; j++) { int value = reader.ReadInt32(); if (value < 0 || value >= vertices) throw new InvalidDataException("Finished mesh index"); part.indices[j] = value; }
                parts[i] = part;
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing finished mesh bytes");
            return parts;
        }
        static byte[] Encode(Part[] parts)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(parts.Length);
            foreach (var part in parts)
            {
                writer.Write(part.vertices.Length); writer.Write(part.indices.Length);
                foreach (var array in new[] { part.vertices, part.normals }) foreach (var point in array) { writer.Write(point.x); writer.Write(point.y); writer.Write(point.z); }
                foreach (int index in part.indices) writer.Write(index);
            }
            if (stream.Length > MaximumPayloadBytes) throw new InvalidDataException("Finished mesh write budget");
            return stream.ToArray();
        }
        public void Write(string key, Part[] parts)
        {
            if (Interlocked.Increment(ref pending) > 8) { Interlocked.Decrement(ref pending); return; }
            _ = Task.Run(() =>
            {
                string temporary = null;
                try
                {
                    var payload = Encode(parts);
                    using var compressed = new MemoryStream();
                    using (var writer = new BinaryWriter(compressed, Encoding.UTF8, true)) { writer.Write(Magic); writer.Write(KeyBytes(key)); writer.Write(payload.Length); writer.Write(Digest(payload)); }
                    using (var gzip = new GZipStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true)) gzip.Write(payload, 0, payload.Length);
                    lock (FileGate)
                    {
                        long limit = (long)Limit(settings);
                        if (compressed.Length > limit || new DriveInfo(Path.GetPathRoot(folder)).AvailableFreeSpace < FreeSpaceReserveBytes + compressed.Length) return;
                        Directory.CreateDirectory(folder);
                        string target = Path.Combine(folder, key + ".eugeo");
                        temporary = target + ".partial-" + Guid.NewGuid().ToString("N");
                        File.WriteAllBytes(temporary, compressed.ToArray());
                        if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                        temporary = null;
                        if (Ages.TryGetValue(folder, out var ages)) ages.Set(target, compressed.Length, DateTime.UtcNow);
                        Prune(folder, limit);
                        Interlocked.Increment(ref Writes); Interlocked.Add(ref BytesWritten, compressed.Length);
                    }
                }
                catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { }
                finally
                {
                    if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                    Interlocked.Decrement(ref pending);
                }
            });
        }
        static void Prune(string folder, long limit)
        {
            // One background directory scan, then O(log N) admission/eviction.
            // Never rescan a multi-GB cache for every generated chunk.
            if (!Ages.TryGetValue(folder, out var ages))
            {
                ages = new Inventory();
                foreach (string path in Directory.EnumerateFiles(folder, "*.eugeo"))
                {
                    var file = new FileInfo(path);
                    ages.Set(path, file.Length, file.LastWriteTimeUtc);
                }
                Ages[folder] = ages;
            }
            while (ages.bytes > limit && ages.oldest.Count > 0)
            {
                var file = ages.oldest.Min;
                File.Delete(file.path); ages.Remove(file.path);
            }
        }
        public static Task ApplyLimit(string worldCache) => Task.Run(() =>
        {
            string root = Path.Combine(worldCache, "FinishedGeometry");
            lock (FileGate) { if (Directory.Exists(root)) Prune(root, (long)Limit(Path.Combine(worldCache, "storage-settings.json"))); }
        });
    }
}
