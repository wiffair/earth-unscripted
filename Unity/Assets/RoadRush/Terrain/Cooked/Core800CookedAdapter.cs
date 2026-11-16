using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using RoadRush.TerrainDraft.Cooked;

namespace RoadRush.TerrainDraft.Core800
{
    public sealed partial class VerifiedCoreTerrain
    {
        /// <summary>
        /// Offline export from this genuine complete producer capability. Owns copied
        /// final arrays; does not modify source, meshes, bank classification or cells.
        /// Writes a fresh content-addressed artifact and validates it before commit.
        /// Cancellation/progress use the calling worker thread; no Unity API is used.
        /// </summary>
        public CookedTerrainExport ExportCooked(string path, CancellationToken token = default, Action<string, int, int> progress = null)
        {
            var r = CopyFinalRoadMeshes();
            var t = CopyFinalTerrainMeshes();
            var data = new CookedData
            {
                RawDocumentDigest = RawDocumentDigest,
                SourceIdentityDigest = SourceDigest,
                DemInputDigest = DemInputDigest,
                ControlDigest = ControlDigest,
                OwnerLedgerDigest = OwnerLedgerDigest,
                StructureLedgerDigest = StructureLedgerDigest,
                ScopeCoverageDigest = ScopeCoverageDigest,
                FinalBufferDigest = FinalBufferDigest,
                GeometryRecipe = CoreCellPreparation.Recipe,
                MinX = MinX,
                MinZ = MinZ,
                MaxX = MaxX,
                MaxZ = MaxZ,
                Packets = r.Concat(t).ToArray(),
                Road = Enumerable.Repeat(true, r.Length).Concat(Enumerable.Repeat(false, t.Length)).ToArray(),
                Members = CopyRoadMembers(),
                Rows = CopyRoadCenterFrames().ToDictionary(p => p.Key, p => p.Value.Select(a => new CookedRoadRow(a.SourceS,
                    a.DerivedH, a.PreviousSide, a.CenterX, a.CenterY, a.CenterZ, a.RightX, a.RightY, a.RightZ)).ToArray()),
                    
                Banks = CopyDerivedRoadBanks().Select(b => new CookedBank { Id = b.Id, GeometryRecipe = b.Recipe,
                    SourceEdge = b.SourceEdgeDigest, FinalBufferDigest = b.FinalBufferDigest, Area = b.ProjectedArea,
                    Maximum = b.MaximumGrade, Sources = b.CopySourcePacketIds(), Faces = b.CopyTriangleReferences().Select(a => (a.PacketId,
                    a.TriangleIndex)).ToArray(), Loops = b.CopyFootprintLoops() }).ToArray()
            };
            data.Cells = CopyCells().Select(c => new CookedCell { X = c.CellX, Z = c.CellZ,
                RootX = c.RootX, RootZ = c.RootZ, MinX = c.MinX, MinZ = c.MinZ, MaxX = c.MaxX, MaxZ = c.MaxZ,
                Coverage = c.CoverageDigest, FinalBufferDigest = c.FinalBufferDigest, Packets = Enumerable.Range(0,
                data.Packets.Length).Where(i => data.Packets[i].OriginX == c.MinX && data.Packets[i].OriginZ == c.MinZ).ToArray() }).ToArray();
            return CookedBinary.Write(data, path, token, progress);
        }

        // Offline roundtrip requires the actual oracle capability; cannot be called with a JSON receipt.
        /// <summary>
        /// Offline oracle comparison path. Requires this actual capability, its
        /// captured raw bytes and its own export; an arbitrary receipt is insufficient.
        /// </summary>
        public VerifiedCoreTerrain LoadExportForRoundtrip(CookedTerrainExport export, byte[] actualRawBytes,
            CancellationToken token = default, Action<string, int, int> progress = null)
        {
            if (export == null || CookedBinary.Hash(actualRawBytes) != RawDocumentDigest || export.Request.RawDocumentDigest != RawDocumentDigest
                || export.Request.FinalBufferDigest != FinalBufferDigest || export.Request.ControlDigest != ControlDigest
                || export.Request.ScopeCoverageDigest != ScopeCoverageDigest)
                throw new InvalidDataException("Actual oracle/export/raw context required");
            return FromCooked(CookedTerrainVerifier.Load(export.Path, export.Request, token, progress));
        }

        static VerifiedCoreTerrain FromCooked(VerifiedCookedGeometry verified)
        {
            var data = verified.Data;
            return new VerifiedCoreTerrain(IssuerKey, data.SourceIdentityDigest, data.RawDocumentDigest,
                data.DemInputDigest, data.ControlDigest, data.StructureLedgerDigest, data.Rows.ToDictionary(p => p.Key,
                p => p.Value.Select(a => new SourceRoadRow(a.S, a.H, a.X, a.Y, a.Z, a.RX, a.RY, a.RZ, a.Previous)).ToArray()),
                data.Packets.Where((p, i) => data.Road[i]).ToArray(), data.Packets.Where((p, i) => !data.Road[i]).ToArray(),
                data.Members, data.Cells.Select(c => new CoreCellDescriptor(c.X, c.Z, c.MinX, c.MinZ, c.MaxX,
                c.MaxZ, c.Coverage, c.FinalBufferDigest)).ToArray(), data.OwnerLedgerDigest, data.ScopeCoverageDigest,
                data.FinalBufferDigest, data.Banks.Select(b => new DerivedRoadBankDescriptor(b.Id, b.GeometryRecipe,
                b.SourceEdge, b.FinalBufferDigest, b.Sources, b.Faces.Select(f => new DerivedBankTriangleReference(f.Packet,
                f.Triangle)).ToArray(), b.Loops, b.Area, b.Maximum)).ToArray());
        }
    // Fixture-specific runtime bridge is generated only AFTER genuine oracle export
    // and exact-byte roundtrip review. Generic verifier above has no area/digest pins.
    }
}
