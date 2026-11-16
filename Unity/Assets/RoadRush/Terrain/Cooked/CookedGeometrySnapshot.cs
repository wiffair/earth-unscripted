using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using RoadRush.TerrainDraft;

namespace RoadRush.TerrainDraft.Cooked
{
    /// <summary>
    /// Detached, immutable literal geometry from a genuine verified result.
    /// Every collection owns a deep copy, including nested bank loops and named
    /// road rows. Metadata describes geometry; it grants no source, effective-path,
    /// supported-interval, scene-owner or whole-world authority.
    /// </summary>
    public sealed class CookedGeometrySnapshot
    {
        public CookedGeometryMetadata Metadata { get; }
        public IReadOnlyList<CookedPacketSnapshot> Packets { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> RoadMembers { get; }
        /// <summary>Literal stored S/H and corner sides; recipe-specific station semantics are preserved.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<CookedRoadRow>> RoadRows { get; }
        public IReadOnlyList<CookedCellSnapshot> Cells { get; }
        public IReadOnlyList<CookedBankSnapshot> Banks { get; }
        public bool SourceAuthorityGranted => false;
        public bool WholeWorldAdmitted => false;

        internal CookedGeometrySnapshot(CookedData data, string artifactSHA256, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Metadata = new CookedGeometryMetadata(data, artifactSHA256);
            var packets = new CookedPacketSnapshot[data.Packets.Length];
            for (int index = 0; index < packets.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                packets[index] = new CookedPacketSnapshot(data.Packets[index], data.Road[index]);
            }
            Packets = Array.AsReadOnly(packets);

            var members = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var pair in data.Members)
            {
                cancellation.ThrowIfCancellationRequested();
                members.Add(pair.Key, Array.AsReadOnly((string[])pair.Value.Clone()));
            }
            RoadMembers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(members);

            var rows = new Dictionary<string, IReadOnlyList<CookedRoadRow>>(StringComparer.Ordinal);
            foreach (var pair in data.Rows)
            {
                cancellation.ThrowIfCancellationRequested();
                rows.Add(pair.Key, Array.AsReadOnly((CookedRoadRow[])pair.Value.Clone()));
            }
            RoadRows = new ReadOnlyDictionary<string, IReadOnlyList<CookedRoadRow>>(rows);

            var cells = new CookedCellSnapshot[data.Cells.Length];
            for (int index = 0; index < cells.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                cells[index] = new CookedCellSnapshot(data.Cells[index]);
            }
            Cells = Array.AsReadOnly(cells);

            var banks = new CookedBankSnapshot[data.Banks.Length];
            for (int index = 0; index < banks.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                banks[index] = new CookedBankSnapshot(data.Banks[index], cancellation);
            }
            Banks = Array.AsReadOnly(banks);
            cancellation.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Immutable descriptive header. Bounds/origins are game-frame metres,
    /// grade limits are rise/run, and quantization is the local X/Z lattice
    /// exponent. Raw source identity is distinct from derived controls/final
    /// buffers. These fields cannot construct a request or a reload handle.
    /// </summary>
    public sealed class CookedGeometryMetadata
    {
        public string ArtifactSHA256 { get; }
        public string RawDocumentDigest { get; }
        public string SourceIdentityDigest { get; }
        public string DemInputDigest { get; }
        public string ControlDigest { get; }
        public string OwnerLedgerDigest { get; }
        public string StructureLedgerDigest { get; }
        public string ScopeCoverageDigest { get; }
        public string FinalBufferDigest { get; }
        public string GeometryRecipe { get; }
        public double MinX { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxZ { get; }
        public double CellMetres { get; }
        public double ChunkMetres { get; }
        public int QuantizationExponent { get; }
        public double RoadGradeLimit { get; }
        public double TerrainGradeLimit { get; }

        internal CookedGeometryMetadata(CookedData data, string artifactSHA256)
        {
            ArtifactSHA256 = artifactSHA256;
            RawDocumentDigest = data.RawDocumentDigest;
            SourceIdentityDigest = data.SourceIdentityDigest;
            DemInputDigest = data.DemInputDigest;
            ControlDigest = data.ControlDigest;
            OwnerLedgerDigest = data.OwnerLedgerDigest;
            StructureLedgerDigest = data.StructureLedgerDigest;
            ScopeCoverageDigest = data.ScopeCoverageDigest;
            FinalBufferDigest = data.FinalBufferDigest;
            GeometryRecipe = data.GeometryRecipe;
            MinX = data.MinX;
            MinZ = data.MinZ;
            MaxX = data.MaxX;
            MaxZ = data.MaxZ;
            CellMetres = data.CellMetres;
            ChunkMetres = data.ChunkMetres;
            QuantizationExponent = data.QuantizationExponent;
            RoadGradeLimit = data.RoadGrade;
            TerrainGradeLimit = data.TerrainGrade;
        }
    }

    /// <summary>
    /// One detached final packet. LocalXYZ contains literal binary32 X/Y/Z
    /// triples in game-frame metres, and TriangleIndices contains ordered index
    /// triples. Preserve the binary64 origin; no repacking or rounding occurs.
    /// </summary>
    public sealed class CookedPacketSnapshot
    {
        public string Id { get; }
        public double OriginX { get; }
        public double OriginY { get; }
        public double OriginZ { get; }
        public bool IsRoad { get; }
        public IReadOnlyList<float> LocalXYZ { get; }
        public IReadOnlyList<int> TriangleIndices { get; }

        internal CookedPacketSnapshot(LocalTerrainMesh packet, bool isRoad)
        {
            Id = packet.Id;
            OriginX = packet.OriginX;
            OriginY = packet.OriginY;
            OriginZ = packet.OriginZ;
            IsRoad = isRoad;
            LocalXYZ = Array.AsReadOnly((float[])packet.XYZ.Clone());
            TriangleIndices = Array.AsReadOnly((int[])packet.Indices.Clone());
        }
    }

    /// <summary>
    /// Detached cell ownership descriptor. PacketIndices addresses this
    /// snapshot's packet order, not a global road index or scene object.
    /// Exact partial-cell bounds and the existing root identifiers are retained.
    /// </summary>
    public sealed class CookedCellSnapshot
    {
        public int CellX { get; }
        public int CellZ { get; }
        public int RootX { get; }
        public int RootZ { get; }
        public double MinX { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxZ { get; }
        public string CoverageDigest { get; }
        public string FinalBufferDigest { get; }
        public IReadOnlyList<int> PacketIndices { get; }

        internal CookedCellSnapshot(CookedCell cell)
        {
            CellX = cell.X;
            CellZ = cell.Z;
            RootX = cell.RootX;
            RootZ = cell.RootZ;
            MinX = cell.MinX;
            MinZ = cell.MinZ;
            MaxX = cell.MaxX;
            MaxZ = cell.MaxZ;
            CoverageDigest = cell.Coverage;
            FinalBufferDigest = cell.FinalBufferDigest;
            PacketIndices = Array.AsReadOnly((int[])cell.Packets.Clone());
        }
    }

    /// <summary>Literal zero-based triangle reference within the named packet.</summary>
    public readonly struct CookedBankFaceSnapshot
    {
        public string PacketId { get; }
        public int TriangleIndex { get; }

        internal CookedBankFaceSnapshot(string packetId, int triangleIndex)
        {
            PacketId = packetId;
            TriangleIndex = triangleIndex;
        }
    }

    /// <summary>
    /// Detached explicit bank classification of existing terrain triangles.
    /// Projected area is square metres; grades are rise/run. FootprintLoops
    /// contain literal game-frame X/Z pairs, deeply copied at both levels.
    /// Bank metadata does not establish surveyed or traversable ground.
    /// </summary>
    public sealed class CookedBankSnapshot
    {
        public string Id { get; }
        public string GeometryRecipe { get; }
        public string SourceEdgeDigest { get; }
        public string FinalBufferDigest { get; }
        public double ProjectedArea { get; }
        public double MaximumGrade { get; }
        public double AllowedGrade { get; }
        public IReadOnlyList<string> SourcePacketIds { get; }
        public IReadOnlyList<CookedBankFaceSnapshot> Faces { get; }
        public IReadOnlyList<IReadOnlyList<double>> FootprintLoops { get; }

        internal CookedBankSnapshot(CookedBank bank, CancellationToken cancellation)
        {
            Id = bank.Id;
            GeometryRecipe = bank.GeometryRecipe;
            SourceEdgeDigest = bank.SourceEdge;
            FinalBufferDigest = bank.FinalBufferDigest;
            ProjectedArea = bank.Area;
            MaximumGrade = bank.Maximum;
            AllowedGrade = bank.AllowedGrade;
            SourcePacketIds = Array.AsReadOnly((string[])bank.Sources.Clone());
            var faces = new CookedBankFaceSnapshot[bank.Faces.Length];
            for (int index = 0; index < faces.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                faces[index] = new CookedBankFaceSnapshot(bank.Faces[index].Packet, bank.Faces[index].Triangle);
            }
            Faces = Array.AsReadOnly(faces);
            var loops = new IReadOnlyList<double>[bank.Loops.Length];
            for (int index = 0; index < loops.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                loops[index] = Array.AsReadOnly((double[])bank.Loops[index].Clone());
            }
            FootprintLoops = Array.AsReadOnly(loops);
        }
    }

    public sealed partial class VerifiedCookedGeometry
    {
        /// <summary>
        /// Copy this fully checked geometry into worker-safe immutable managed
        /// data. Allocations are bounded by the already verified request; large
        /// snapshots still consume additional memory. Cancellation throws without
        /// returning a partial snapshot. No private verified storage is exposed.
        /// </summary>
        public CookedGeometrySnapshot CreateDetachedSnapshot(CancellationToken cancellation = default)
        {
            return new CookedGeometrySnapshot(Data, ArtifactSHA256, cancellation);
        }
    }
}
