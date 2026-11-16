using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;
using EarthUnscripted.TerrainDraft;

namespace EarthUnscripted.TerrainDraft.Cooked
{
    // Binary cache DTOs belong to the terrain assembly, not Unity scene state.
    // Geometry is final derived game geometry, not a claim of surveyed elevation.
    // No serialized boolean, receipt or package status can construct a capability.
    /// <summary>
    /// Per-request defensive allocation budgets. Counts are aggregate over the
    /// requested scope, not truncation limits: exceeding any cap rejects the load.
    /// Defaults permit this first complete-core fixture and larger bounded windows;
    /// they are not an assertion of acceptable resident rendering performance.
    /// </summary>
    public sealed class CookedTerrainLimits
    {
        /// <summary>Maximum complete regular-grid cell descriptors.</summary>
        public int Cells = 8192;
        /// <summary>Maximum final mesh packets, preserving each local origin.</summary>
        public int Packets = 16384;
        /// <summary>Maximum total XYZ vertices (not float-component count).</summary>
        public int Vertices = 2_000_000;
        /// <summary>Maximum total triangle faces (not index count).</summary>
        public int Triangles = 4_000_000;
        /// <summary>Maximum literal named-road station rows, including both corner sides.</summary>
        public int Rows = 100_000;
        /// <summary>Maximum aggregate embedding-index references across cells.</summary>
        public int References = 12_000_000;
        /// <summary>Maximum encoded payload bytes before allocation; default 128 MiB.</summary>
        public long Bytes = CookedTerrainPolicy.MaximumPayloadBytes;
    }

    /// <summary>
    /// Independently bound source/control/area context, created by a reviewed caller.
    /// The artifact cannot self-authorize its hashes, bank policy or requested area.
    /// Bounds and cell/chunk sizes are metres in the source X/Z frame; grade is rise/run.
    /// No public constructor accepts an arbitrary disk approval record.
    /// </summary>
    public sealed class CookedTerrainRequest
    {
        // Lowercase SHA-256 values bind the complete raw envelope, source geography,
        // DEM, controls, owner/structure/coverage ledgers and exact final float buffers.
        // GeometryRecipe is a versioned recipe identifier, not necessarily a digest.
        public readonly string RawDocumentDigest, SourceIdentityDigest, DemInputDigest, ControlDigest,
            OwnerLedgerDigest, StructureLedgerDigest, ScopeCoverageDigest, FinalBufferDigest, GeometryRecipe,
            ExpectedArtifactDigest;
        public readonly double MinX, MinZ, MaxX, MaxZ;
        // Final local X/Z coordinates must lie exactly on the 2^-exponent metre
        // lattice. Verification tests representability; it never rounds mesh data.
        public readonly int QuantizationExponent;
        public readonly double CellMetres, ChunkMetres, RoadGrade, TerrainGrade;
        readonly CookedTerrainLimits limits;
        /// <summary>Returns a defensive budget copy; callers cannot mutate an active request.</summary>
        public CookedTerrainLimits Limits => new CookedTerrainLimits
        {
            Cells = limits.Cells,
            Packets = limits.Packets,
            Vertices = limits.Vertices,
            Triangles = limits.Triangles,
            Rows = limits.Rows,
            References = limits.References,
            Bytes = limits.Bytes
        };

        internal readonly CookedBank[] ExpectedBanks;
        internal CookedTerrainRequest(CookedData data, string artifact, CookedTerrainLimits limits)
        {
            RawDocumentDigest = data.RawDocumentDigest;
            SourceIdentityDigest = data.SourceIdentityDigest;
            DemInputDigest = data.DemInputDigest;
            ControlDigest = data.ControlDigest;
            OwnerLedgerDigest = data.OwnerLedgerDigest;
            StructureLedgerDigest = data.StructureLedgerDigest;
            ScopeCoverageDigest = data.ScopeCoverageDigest;
            FinalBufferDigest = data.FinalBufferDigest;
            GeometryRecipe = data.GeometryRecipe;
            ExpectedArtifactDigest = artifact;
            MinX = data.MinX;
            MinZ = data.MinZ;
            MaxX = data.MaxX;
            MaxZ = data.MaxZ;
            QuantizationExponent = data.QuantizationExponent;
            CellMetres = data.CellMetres;
            ChunkMetres = data.ChunkMetres;
            RoadGrade = data.RoadGrade;
            TerrainGrade = data.TerrainGrade;
            var budget = limits ?? new CookedTerrainLimits();
            this.limits = new CookedTerrainLimits
            {
                Cells = budget.Cells,
                Packets = budget.Packets,
                Vertices = budget.Vertices,
                Triangles = budget.Triangles,
                Rows = budget.Rows,
                References = budget.References,
                Bytes = budget.Bytes
            };
            ExpectedBanks = data.Banks.Select(b => b.Clone()).ToArray();
        }
    }

    /// <summary>
    /// Literal derived road frame. S is raw source-chain metres; H is derived
    /// horizontal station metres. Equal S/H pairs retain incoming Previous=true
    /// followed by outgoing false, so sharp corners are never silently smoothed.
    /// XYZ is source-frame centre position; RX/RY/RZ is its unit right vector.
    /// </summary>
    public readonly struct CookedRoadRow
    {
        public readonly double S, H;
        public readonly bool Previous;
        public readonly float X, Y, Z, RX, RY, RZ;
        internal CookedRoadRow(double s, double h, bool previous, float x, float y, float z, float rx, float ry, float rz)
        {
            S = s;
            H = h;
            Previous = previous;
            X = x;
            Y = y;
            Z = z;
            RX = rx;
            RY = ry;
            RZ = rz;
        }
    }

    // Each descriptor owns complete final packet indices for its rectangular cell.
    // A partial edge cell retains its exact clipped bounds; no 32m origin repacking.
    internal sealed class CookedCell
    {
        internal int X, Z, RootX, RootZ;
        internal double MinX, MinZ, MaxX, MaxZ;
        internal string Coverage, FinalBufferDigest;
        internal int[] Packets;
    }

    // Explicit derived cut-bank classification binds selected existing terrain
    // triangle indices, not a second collision surface or a packet-wide exemption.
    // Area is projected square metres; Maximum/AllowedGrade are rise/run ratios.
    internal sealed class CookedBank
    {
        internal string Id, GeometryRecipe, SourceEdge, FinalBufferDigest;
        internal double Area, Maximum, AllowedGrade = CookedTerrainPolicy.DefaultBankGradeLimit;
        internal string[] Sources;
        internal (string Packet, int Triangle)[] Faces;
        internal double[][] Loops;
        internal CookedBank Clone() => new CookedBank
        {
            Id = Id,
            GeometryRecipe = GeometryRecipe,
            SourceEdge = SourceEdge,
            FinalBufferDigest = FinalBufferDigest,
            Area = Area,
            Maximum = Maximum,
            AllowedGrade = AllowedGrade,
            Sources = (string[])Sources.Clone(),
            Faces = Faces.ToArray(),
            Loops = Loops.Select(p => (double[])p.Clone()).ToArray()
        };
    }

    // Exclusively owned decoded arrays. These remain internal and are not exposed
    // to scene/UI code; the source-authority bridge copies them into its capability.
    internal sealed class CookedData
    {
        internal string RawDocumentDigest, SourceIdentityDigest, DemInputDigest, ControlDigest,
            OwnerLedgerDigest, StructureLedgerDigest, ScopeCoverageDigest, FinalBufferDigest, GeometryRecipe;
        internal double MinX, MinZ, MaxX, MaxZ;
        internal int QuantizationExponent = 18;
        internal double CellMetres = 32, ChunkMetres = 320, RoadGrade = .180001, TerrainGrade = 1.200001;
        internal LocalTerrainMesh[] Packets;
        internal bool[] Road;
        internal Dictionary<string, string[]> Members;
        internal Dictionary<string, CookedRoadRow[]> Rows;
        internal CookedCell[] Cells;
        internal CookedBank[] Banks;
    }

    /// <summary>
    /// Private-constructor result issued only after complete buffer/topology checks.
    /// This proves a bounded geometry snapshot; it grants no source-admission or
    /// whole-world authority. The reviewed source caller performs that binding.
    /// </summary>
    public sealed partial class VerifiedCookedGeometry
    {
        internal readonly CookedData Data;
        public string ArtifactSHA256 { get; }
        public bool WholeWorldAdmitted => false;
        public bool SourceAuthorityGranted => false;

        private VerifiedCookedGeometry(CookedData data, string artifact)
        {
            Data = data;
            ArtifactSHA256 = artifact;
        }

        public int Vertices => Data.Packets.Sum(p => p.XYZ.Length / 3);
        public int Triangles => Data.Packets.Sum(p => p.Indices.Length / 3);
    }

    /// <summary>
    /// Receipt for an immutable, atomically published artifact written from an
    /// existing verified producer capability. It is not itself load approval.
    /// </summary>
    public sealed class CookedTerrainExport
    {
        public string Path { get; }
        public string SHA256 { get; }
        public long Bytes { get; }

        internal readonly CookedTerrainRequest Request;
        internal CookedTerrainExport(string path, string sha, long bytes, CookedTerrainRequest request)
        {
            Path = path;
            SHA256 = sha;
            Bytes = bytes;
            Request = request;
        }
    }

    // Explicit little-endian binary layout, version 1. Strings are bounded strict
    // UTF-8. Double origins, literal float XYZ and int indices retain their bits;
    // changing member names must not change field order or payload identity.
    internal static class CookedBinary
    {
        internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal static void Need(bool yes, string why)
        {
            if (!yes)
                throw new InvalidDataException("Cooked terrain: " + why);
        }

        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        internal static string Hash(byte[] b)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(b).Select(x => x.ToString("x2")));
        }

        internal static bool SHA(string s) => s != null && s.Length == 64 && s.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        internal static void Text(BinaryWriter w, string value)
        {
            byte[] b = Utf8.GetBytes(value ?? "");
            Need(b.Length <= CookedTerrainPolicy.MaximumStringUtf8Bytes, "String cap");
            Need(w.BaseStream.Position + 4 + b.Length <= CookedTerrainPolicy.MaximumPayloadBytes, "Export byte budget");
            w.Write(b.Length);
            w.Write(b);
        }

        internal static string Text(BinaryReader r)
        {
            int n = r.ReadInt32();
            Need(n >= 0 && n <= CookedTerrainPolicy.MaximumStringUtf8Bytes && n <= r.BaseStream.Length - r.BaseStream.Position, "Bounded string");
            return Utf8.GetString(r.ReadBytes(n));
        }

        internal static byte[] ReadBounded(string path, long maximum, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = stream.Length;
                Need(length > 0 && length <= maximum && length <= int.MaxValue, "Bounded owned file");
                var bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    token.ThrowIfCancellationRequested();
                    int n = stream.Read(bytes, offset, Math.Min(CookedTerrainPolicy.ReadBlockBytes, bytes.Length - offset));
                    Need(n > 0, "Partial owned file");
                    offset += n;
                }

                Need(stream.ReadByte() == -1, "Growing owned file");
                return bytes;
            }
        }

        internal static bool Flag(BinaryReader r)
        {
            byte value = r.ReadByte();
            Need(value <= 1, "Canonical boolean byte");
            return value == 1;
        }

        internal static int Count(BinaryReader r, int max)
        {
            int n = r.ReadInt32();
            Need(n >= 0 && n <= max && n <= r.BaseStream.Length - r.BaseStream.Position, "Count cap");
            return n;
        }

        internal static void Strings(BinaryWriter w, string[] values)
        {
            w.Write(values.Length);
            foreach (var v in values)
                Text(w, v);
        }

        internal static string[] Strings(BinaryReader r, int max)
        {
            int n = Count(r, max);
            var result = new string[n];
            for (int i = 0; i < n; i++)
                result[i] = Text(r);
            return result;
        }

        internal static string BufferDigest(IEnumerable<LocalTerrainMesh> meshes)
        {
            using (var sha = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
            {
                using (var w = new BinaryWriter(stream, Utf8, true))
                {
                    foreach (var p in meshes.OrderBy(p => p.Id, StringComparer.Ordinal))
                    {
                        w.Write(p.Id);
                        w.Write(p.OriginX);
                        w.Write(p.OriginY);
                        w.Write(p.OriginZ);
                        w.Write(p.XYZ.Length);
                        foreach (float f in p.XYZ)
                            w.Write(f);
                        w.Write(p.Indices.Length);
                        foreach (int i in p.Indices)
                            w.Write(i);
                    }

                    w.Flush();
                }

                stream.FlushFinalBlock();
                return string.Concat(sha.Hash.Select(b => b.ToString("x2")));
            }
        }

        // The fresh-file transaction validates its encoded bytes before publishing.
        // Cancellation or validation failure removes only this call's private temp;
        // it never replaces an existing cache or edits the source/career library.
        internal static CookedTerrainExport Write(CookedData data, string path, CancellationToken token, Action<string, int, int> progress)
        {
            Need(!File.Exists(path) && !Directory.Exists(path), "Immutable fresh output");
            string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var w = new BinaryWriter(file, Utf8, true))
                {
                    Text(w, CookedTerrainPolicy.PayloadMagic);
                    w.Write(CookedTerrainPolicy.FormatVersion);
                    foreach (var s in new[]
                    {
                        data.RawDocumentDigest,
                        data.SourceIdentityDigest,
                        data.DemInputDigest,
                        data.ControlDigest,
                        data.OwnerLedgerDigest,
                        data.StructureLedgerDigest,
                        data.ScopeCoverageDigest,
                        data.FinalBufferDigest,
                        data.GeometryRecipe
                    }

                    )
                        Text(w, s);
                    w.Write(data.MinX);
                    w.Write(data.MinZ);
                    w.Write(data.MaxX);
                    w.Write(data.MaxZ);
                    w.Write(data.QuantizationExponent);
                    w.Write(data.CellMetres);
                    w.Write(data.ChunkMetres);
                    w.Write(data.RoadGrade);
                    w.Write(data.TerrainGrade);
                    w.Write(data.Packets.Length);
                    for (int p = 0; p < data.Packets.Length; p++)
                    {
                        token.ThrowIfCancellationRequested();
                        progress?.Invoke("export-buffer", p, data.Packets.Length);
                        var m = data.Packets[p];
                        Text(w, m.Id);
                        w.Write(data.Road[p]);
                        w.Write(m.OriginX);
                        w.Write(m.OriginY);
                        w.Write(m.OriginZ);
                        w.Write(m.XYZ.Length);
                        foreach (float f in m.XYZ)
                            w.Write(f);
                        w.Write(m.Indices.Length);
                        foreach (int i in m.Indices)
                            w.Write(i);
                    }

                    w.Write(data.Members.Count);
                    foreach (var pair in data.Members.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        Text(w, pair.Key);
                        Strings(w, pair.Value);
                    }

                    w.Write(data.Rows.Count);
                    foreach (var pair in data.Rows.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        Text(w, pair.Key);
                        w.Write(pair.Value.Length);
                        foreach (var r in pair.Value)
                        {
                            w.Write(r.S);
                            w.Write(r.H);
                            w.Write(r.Previous);
                            w.Write(r.X);
                            w.Write(r.Y);
                            w.Write(r.Z);
                            w.Write(r.RX);
                            w.Write(r.RY);
                            w.Write(r.RZ);
                        }
                    }

                    w.Write(data.Cells.Length);
                    foreach (var c in data.Cells)
                    {
                        token.ThrowIfCancellationRequested();
                        w.Write(c.X);
                        w.Write(c.Z);
                        w.Write(c.RootX);
                        w.Write(c.RootZ);
                        w.Write(c.MinX);
                        w.Write(c.MinZ);
                        w.Write(c.MaxX);
                        w.Write(c.MaxZ);
                        Text(w, c.Coverage);
                        Text(w, c.FinalBufferDigest);
                        w.Write(c.Packets.Length);
                        foreach (int i in c.Packets)
                            w.Write(i);
                    }

                    w.Write(data.Banks.Length);
                    foreach (var b in data.Banks)
                    {
                        Text(w, b.Id);
                        Text(w, b.GeometryRecipe);
                        Text(w, b.SourceEdge);
                        Text(w, b.FinalBufferDigest);
                        w.Write(b.Area);
                        w.Write(b.Maximum);
                        w.Write(b.AllowedGrade);
                        Strings(w, b.Sources);
                        w.Write(b.Faces.Length);
                        foreach (var f in b.Faces)
                        {
                            Text(w, f.Packet);
                            w.Write(f.Triangle);
                        }

                        w.Write(b.Loops.Length);
                        foreach (var loop in b.Loops)
                        {
                            w.Write(loop.Length);
                            foreach (double x in loop)
                                w.Write(x);
                        }
                    }

                    w.Flush();
                    file.Flush(true);
                }

                token.ThrowIfCancellationRequested();
                byte[] bytes = ReadBounded(temporary, CookedTerrainPolicy.MaximumPayloadBytes, token);
                string hash = Hash(bytes);
                var request = new CookedTerrainRequest(data, hash, new CookedTerrainLimits());
                CookedTerrainVerifier.VerifyOwned(bytes, request, token, progress);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path);
                return new CookedTerrainExport(path, hash, bytes.Length, request);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        internal static CookedData Read(byte[] bytes, CookedTerrainRequest request, CancellationToken token,
            Action<string, int, int> progress) => Read(bytes, request.Limits, token, progress);
        internal static CookedData Read(byte[] bytes, CookedTerrainLimits l, CancellationToken token, Action<string, int, int> progress)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes, false), Utf8, true))
            {
                Need(Text(r) == CookedTerrainPolicy.PayloadMagic && r.ReadInt32() == CookedTerrainPolicy.FormatVersion, "Explicit version");
                var data = new CookedData
                {
                    RawDocumentDigest = Text(r),
                    SourceIdentityDigest = Text(r),
                    DemInputDigest = Text(r),
                    ControlDigest = Text(r),
                    OwnerLedgerDigest = Text(r),
                    StructureLedgerDigest = Text(r),
                    ScopeCoverageDigest = Text(r),
                    FinalBufferDigest = Text(r),
                    GeometryRecipe = Text(r),
                    MinX = r.ReadDouble(),
                    MinZ = r.ReadDouble(),
                    MaxX = r.ReadDouble(),
                    MaxZ = r.ReadDouble(),
                    QuantizationExponent = r.ReadInt32(),
                    CellMetres = r.ReadDouble(),
                    ChunkMetres = r.ReadDouble(),
                    RoadGrade = r.ReadDouble(),
                    TerrainGrade = r.ReadDouble()
                };
                int n = Count(r, l.Packets);
                Need(n > 0, "Nonempty buffers");
                data.Packets = new LocalTerrainMesh[n];
                data.Road = new bool[n];
                int vertices = 0, triangles = 0;
                for (int p = 0; p < n; p++)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Invoke("decode-buffer", p, n);
                    string id = Text(r);
                    data.Road[p] = Flag(r);
                    double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                    int nv = Count(r, checked((l.Vertices - vertices) * 3));
                    Need(nv >= 9 && nv % 3 == 0, "Complete XYZ");
                    vertices += nv / 3;
                    var xyz = new float[nv];
                    for (int i = 0; i < nv; i++)
                        xyz[i] = r.ReadSingle();
                    int ni = Count(r, checked((l.Triangles - triangles) * 3));
                    Need(ni > 0 && ni % 3 == 0, "Complete triangles");
                    triangles += ni / 3;
                    var ix = new int[ni];
                    for (int i = 0; i < ni; i++)
                        ix[i] = r.ReadInt32();
                    data.Packets[p] = new LocalTerrainMesh(id, x, y, z, xyz, ix);
                }

                data.Members = new Dictionary<string, string[]>(StringComparer.Ordinal);
                for (int i = Count(r, l.Packets); i > 0; i--)
                    data.Members.Add(Text(r), Strings(r, 32));
                data.Rows = new Dictionary<string, CookedRoadRow[]>(StringComparer.Ordinal);
                int rows = 0;
                for (int i = Count(r, CookedTerrainPolicy.MaximumStringUtf8Bytes); i > 0; i--)
                {
                    token.ThrowIfCancellationRequested();
                    string id = Text(r);
                    int count = Count(r, l.Rows - rows);
                    rows += count;
                    var values = new CookedRoadRow[count];
                    for (int j = 0; j < count; j++)
                        values[j] = new CookedRoadRow(r.ReadDouble(), r.ReadDouble(), Flag(r),
                            r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    data.Rows.Add(id, values);
                }

                n = Count(r, l.Cells);
                data.Cells = new CookedCell[n];
                for (int i = 0; i < n; i++)
                {
                    var c = new CookedCell
                    {
                        X = r.ReadInt32(),
                        Z = r.ReadInt32(),
                        RootX = r.ReadInt32(),
                        RootZ = r.ReadInt32(),
                        MinX = r.ReadDouble(),
                        MinZ = r.ReadDouble(),
                        MaxX = r.ReadDouble(),
                        MaxZ = r.ReadDouble(),
                        Coverage = Text(r),
                        FinalBufferDigest = Text(r)
                    };
                    c.Packets = new int[Count(r, l.Packets)];
                    for (int j = 0; j < c.Packets.Length; j++)
                        c.Packets[j] = r.ReadInt32();
                    data.Cells[i] = c;
                }

                n = Count(r, CookedTerrainPolicy.MaximumStringUtf8Bytes);
                data.Banks = new CookedBank[n];
                int bankFaces = 0, bankKnots = 0;
                for (int i = 0; i < n; i++)
                {
                    var b = new CookedBank
                    {
                        Id = Text(r),
                        GeometryRecipe = Text(r),
                        SourceEdge = Text(r),
                        FinalBufferDigest = Text(r),
                        Area = r.ReadDouble(),
                        Maximum = r.ReadDouble(),
                        AllowedGrade = r.ReadDouble(),
                        Sources = Strings(r, 128)
                    };
                    b.Faces = new (string, int)[Count(r, l.Triangles - bankFaces)];
                    bankFaces += b.Faces.Length;
                    for (int j = 0; j < b.Faces.Length; j++)
                        b.Faces[j] = (Text(r), r.ReadInt32());
                    b.Loops = new double[Count(r, 8192)][];
                    for (int j = 0; j < b.Loops.Length; j++)
                    {
                        int k = Count(r, 1000000 - bankKnots);
                        Need(k >= 6 && k % 2 == 0, "Footprint pairs");
                        bankKnots += k;
                        b.Loops[j] = new double[k];
                        for (int a = 0; a < k; a++)
                            b.Loops[j][a] = r.ReadDouble();
                    }

                    data.Banks[i] = b;
                }

                Need(r.BaseStream.Position == r.BaseStream.Length, "No trailing/partial cache");
                return data;
            }
        }
    }
}
