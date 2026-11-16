using System;
using System.IO;
using System.Threading;

namespace RoadRush.TerrainDraft.Cooked
{
    /// <summary>
    /// Content-addressed source-format metadata companion. The caller supplies its
    /// independent expected digest and format; this contains no approval/status flag.
    /// Raw-document references retain attribution context, not a new licence grant.
    /// Unknown versions, changed source format and trailing bytes fail closed.
    /// </summary>
    public static class CookedTerrainPackage
    {
        internal static string Write(VerifiedCookedGeometry geometry, string path, string sourceFormat, CancellationToken token = default)
        {
            CookedBinary.Need(geometry != null && !string.IsNullOrEmpty(sourceFormat) && sourceFormat.Length <= 256
                && !File.Exists(path), "Verified buffers/fresh source-format package");
            var data = geometry.Data;
            string temp = path + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var w = new BinaryWriter(file, CookedBinary.Utf8, true))
                {
                    CookedBinary.Text(w, CookedTerrainPolicy.PackageMagic);
                    w.Write(CookedTerrainPolicy.FormatVersion);
                    CookedBinary.Text(w, sourceFormat);
                    CookedBinary.Text(w, geometry.ArtifactSHA256);
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
                        CookedBinary.Text(w, s);
                    w.Write(data.MinX);
                    w.Write(data.MinZ);
                    w.Write(data.MaxX);
                    w.Write(data.MaxZ);
                    w.Write(data.CellMetres);
                    w.Write(data.ChunkMetres);
                    w.Write(data.QuantizationExponent);
                    w.Write(data.Cells.Length);
                    w.Write(data.Packets.Length);
                    CookedBinary.Text(w, data.RawDocumentDigest);
                    w.Flush();
                    file.Flush(true);
                }

                token.ThrowIfCancellationRequested();
                string hash = CookedBinary.Hash(File.ReadAllBytes(temp));
                File.Move(temp, path);
                return hash;
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        internal static void Validate(string path, string expectedPackageSHA256, string expectedSourceFormat,
            VerifiedCookedGeometry geometry, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes = CookedBinary.ReadBounded(path, CookedTerrainPolicy.MaximumPackageBytes, token);
            CookedBinary.Need(CookedBinary.Hash(bytes) == expectedPackageSHA256, "Independent package digest");
            using (var r = new BinaryReader(new MemoryStream(bytes, false), CookedBinary.Utf8, true))
            {
                CookedBinary.Need(CookedBinary.Text(r) == CookedTerrainPolicy.PackageMagic && r.ReadInt32() == CookedTerrainPolicy.FormatVersion, "Explicit package version");
                CookedBinary.Need(CookedBinary.Text(r) == expectedSourceFormat && CookedBinary.Text(r) == geometry.ArtifactSHA256,
                    "Explicit source format/content binding");
                var data = geometry.Data;
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
                    CookedBinary.Need(CookedBinary.Text(r) == s, "Package source/control/coverage/recipe");
                CookedBinary.Need(r.ReadDouble() == data.MinX && r.ReadDouble() == data.MinZ
                    && r.ReadDouble() == data.MaxX && r.ReadDouble() == data.MaxZ && r.ReadDouble() == data.CellMetres
                    && r.ReadDouble() == data.ChunkMetres && r.ReadInt32() == data.QuantizationExponent && r.ReadInt32() == data.Cells.Length
                    && r.ReadInt32() == data.Packets.Length && CookedBinary.Text(r) == data.RawDocumentDigest,
                    "Package area/count/licence-source reference");
                CookedBinary.Need(r.BaseStream.Position == r.BaseStream.Length, "No trailing package data");
            }
        }
    }
}
