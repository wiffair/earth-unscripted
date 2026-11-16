using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using RoadRush.TerrainDraft;

namespace RoadRush.TerrainDraft.Cooked
{
    /// <summary>
    /// Worker-safe validation entry point. It reads owned bytes, checks the caller's
    /// independently bound request and exact final mesh, then issues geometry only.
    /// Cancellation or malformed input throws without publishing partial output.
    /// </summary>
    public static class CookedTerrainVerifier
    {
        /// <summary>
        /// Validate a bounded local artifact without triangulating, projecting or
        /// modifying its float buffers. Progress callbacks run on this calling thread;
        /// callers must marshal UI updates. No Unity API or source database is touched.
        /// </summary>
        public static VerifiedCookedGeometry Load(string path, CookedTerrainRequest request, CancellationToken token = default, Action<string, int, int> progress = null)
        {
            return VerifiedCookedGeometry.LoadVerified(path, request, token, progress);
        }

        internal static VerifiedCookedGeometry VerifyOwned(byte[] bytes, CookedTerrainRequest request, CancellationToken token, Action<string, int, int> progress)
        {
            return VerifiedCookedGeometry.VerifyOwned(bytes, request, token, progress);
        }
    }

    // Only this fully validating partial can invoke the private result constructor.
    public sealed partial class VerifiedCookedGeometry
    {
        static void Need(bool ok, string why) => CookedBinary.Need(ok, why);
        static bool Finite(double x) => CookedBinary.Finite(x);
        internal static VerifiedCookedGeometry LoadVerified(string path, CookedTerrainRequest request,
            CancellationToken token = default, Action<string, int, int> progress = null)
        {
            Need(request != null, "Typed caller-bound request");
            token.ThrowIfCancellationRequested();
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = file.Length;
                Need(length > 0 && length <= request.Limits.Bytes, "Bounded complete cache file");
                byte[] bytes = new byte[checked((int)length)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    token.ThrowIfCancellationRequested();
                    int n = file.Read(bytes, offset, Math.Min(CookedTerrainPolicy.ReadBlockBytes, bytes.Length - offset));
                    Need(n > 0, "Partial file");
                    offset += n;
                    progress?.Invoke("read-hash", offset, bytes.Length);
                }

                Need(file.ReadByte() == -1, "Growing cache rejected");
                return VerifyOwned(bytes, request, token, progress);
            }
        }

        internal static VerifiedCookedGeometry VerifyOwned(byte[] bytes, CookedTerrainRequest request, CancellationToken token, Action<string, int, int> progress)
        {
            Need(request != null && bytes != null && bytes.LongLength <= request.Limits.Bytes
                && CookedBinary.SHA(request.ExpectedArtifactDigest), "Owned bytes/request budget");
            token.ThrowIfCancellationRequested();
            Need(CookedBinary.Hash(bytes) == request.ExpectedArtifactDigest, "Content-addressed artifact differs");
            var data = CookedBinary.Read(bytes, request, token, progress);
            Need(data.RawDocumentDigest == request.RawDocumentDigest && data.SourceIdentityDigest == request.SourceIdentityDigest
                && data.DemInputDigest == request.DemInputDigest && data.ControlDigest == request.ControlDigest
                && data.OwnerLedgerDigest == request.OwnerLedgerDigest && data.StructureLedgerDigest == request.StructureLedgerDigest
                && data.ScopeCoverageDigest == request.ScopeCoverageDigest && data.FinalBufferDigest == request.FinalBufferDigest
                && data.GeometryRecipe == request.GeometryRecipe, "SourceIdentityDigest/control/recipe/coverage binding");
            Need(new[] { data.RawDocumentDigest, data.SourceIdentityDigest, data.DemInputDigest,
                data.ControlDigest, data.OwnerLedgerDigest, data.StructureLedgerDigest, data.ScopeCoverageDigest,
                data.FinalBufferDigest }.All(CookedBinary.SHA), "Complete source digests");
            Need(data.MinX == request.MinX && data.MinZ == request.MinZ && data.MaxX == request.MaxX
                && data.MaxZ == request.MaxZ && data.QuantizationExponent == request.QuantizationExponent
                && data.CellMetres == request.CellMetres && data.ChunkMetres == request.ChunkMetres && data.RoadGrade == request.RoadGrade
                && data.TerrainGrade == request.TerrainGrade, "Exact requested scope/frame");
            Need(new[] { data.MinX, data.MinZ, data.MaxX, data.MaxZ, data.CellMetres, data.ChunkMetres,
                data.RoadGrade, data.TerrainGrade }.All(Finite) && data.MaxX > data.MinX && data.MaxZ > data.MinZ
                && Math.Abs(data.MinX) <= CookedTerrainPolicy.MaximumCoordinateMagnitudeMetres && Math.Abs(data.MinZ) <= CookedTerrainPolicy.MaximumCoordinateMagnitudeMetres
                && Math.Abs(data.MaxX) <= CookedTerrainPolicy.MaximumCoordinateMagnitudeMetres && Math.Abs(data.MaxZ) <= CookedTerrainPolicy.MaximumCoordinateMagnitudeMetres
                && data.CellMetres >= 1 && data.CellMetres <= 320 && data.ChunkMetres >= data.CellMetres && data.ChunkMetres <= 3200
                && data.QuantizationExponent >= 0 && data.QuantizationExponent <= 20 && data.RoadGrade > 0
                && data.RoadGrade <= 10 && data.TerrainGrade > 0 && data.TerrainGrade <= 10, "Bounded coordinate profile");
            var packets = data.Packets.ToDictionary(p => p.Id, StringComparer.Ordinal);
            Need(packets.Count == data.Packets.Length && data.Packets.All(p => !string.IsNullOrEmpty(p.Id)
                && p.Id.Length <= 512 && !p.Id.Any(char.IsControl) && new[] { p.OriginX, p.OriginY, p.OriginZ }.All(Finite)
                && Math.Abs(p.OriginY) <= CookedTerrainPolicy.MaximumHeightMagnitudeMetres), "Unique finite packets");
            Need(CookedBinary.BufferDigest(data.Packets) == data.FinalBufferDigest, "Actual complete final-buffer digest");
            var roadIds = new HashSet<string>(data.Packets.Where((p, i) => data.Road[i]).Select(p => p.Id));
            Need(data.Members.Count == roadIds.Count && data.Members.Keys.All(roadIds.Contains)
                && data.Members.Values.All(v => v.Length > 0 && v.Length <= 32 && v.Distinct(StringComparer.Ordinal).Count() == v.Length
                && v.All(s => !string.IsNullOrEmpty(s) && s.Length <= 128)), "Exact road/member lineage");
            var named = new HashSet<string>(data.Members.Values.SelectMany(v => v));
            Need(named.SetEquals(data.Rows.Keys), "Named source rows match physical membership");
            int rowCount = 0;
            foreach (var pair in data.Rows)
            {
                Need(pair.Value.Length >= 2 && (rowCount += pair.Value.Length) <= request.Limits.Rows, "Bounded source rows");
                for (int i = 0; i < pair.Value.Length; i++)
                {
                    var r = pair.Value[i];
                    Need(new[] { r.S, r.H, r.X, r.Y, r.Z, r.RX, r.RY, r.RZ }.All(Finite) && r.S >= 0 && r.H >= 0, "Finite literal source rows");
                    Need(Math.Abs(Math.Sqrt(r.RX * r.RX + r.RY * r.RY + r.RZ * r.RZ) - 1) < CookedTerrainPolicy.SourceLateralUnitTolerance,
                        "SourceIdentityDigest lateral basis");
                    if (i > 0)
                    {
                        var p = pair.Value[i - 1];
                        Need(r.S >= p.S && r.H >= p.H && (r.S == p.S) == (r.H == p.H), "Monotone source/H correspondence");
                        if (r.S == p.S)
                            Need(p.Previous && !r.Previous && (i < 2 || pair.Value[i - 2].S != r.S), "Explicit previous/next corner pair");
                    }

                    if (r.Previous)
                        Need(i + 1 < pair.Value.Length && pair.Value[i + 1].S == r.S && pair.Value[i + 1].H == r.H
                            && !pair.Value[i + 1].Previous, "Incoming corner requires outgoing partner");
                }
            }

            var bankGrades = CheckBanks(data, request, packets, roadIds, token);
            var assigned = new bool[data.Packets.Length];
            var cellIds = new HashSet<(int, int)>();
            var byCell = new Dictionary<(int, int), CookedCell>();
            int minX = (int)Math.Floor(data.MinX / data.CellMetres), minZ = (int)Math.Floor(data.MinZ / data.CellMetres),
                maxX = (int)Math.Ceiling(data.MaxX / data.CellMetres) - 1, maxZ = (int)Math.Ceiling(data.MaxZ / data.CellMetres) - 1;
            Need(((long)maxX - minX + 1) * ((long)maxZ - minZ + 1) == data.Cells.Length && data.Cells.Length <= request.Limits.Cells,
                "Complete requested grid, no truncated cells");
            long totalReferences = 0, totalVisits = 0;
            for (int i = 0; i < data.Cells.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke("validate-cell-coverage-embedding", i, data.Cells.Length);
                var c = data.Cells[i];
                Need(cellIds.Add((c.X, c.Z)) && c.X >= minX && c.X <= maxX && c.Z >= minZ && c.Z <= maxZ, "Unique requested cells");
                Need(c.MinX == Math.Max(data.MinX, c.X * data.CellMetres) && c.MinZ == Math.Max(data.MinZ,
                    c.Z * data.CellMetres) && c.MaxX == Math.Min(data.MaxX, (c.X + 1) * data.CellMetres) && c.MaxZ == Math.Min(data.MaxZ,
                    (c.Z + 1) * data.CellMetres) && c.MaxX > c.MinX && c.MaxZ > c.MinZ, "Exact partial-cell boundaries");
                Need(c.RootX == (int)Math.Floor((c.MinX + c.MaxX) * .5 / data.ChunkMetres)
                    && c.RootZ == (int)Math.Floor((c.MinZ + c.MaxZ) * .5 / data.ChunkMetres) && CookedBinary.SHA(c.Coverage)
                    && CookedBinary.SHA(c.FinalBufferDigest), "Chunk/coverage lineage");
                Need(c.Packets.Length > 0, "No missing cell buffers");
                var r = new List<LocalTerrainMesh>();
                var t = new List<LocalTerrainMesh>();
                foreach (int p in c.Packets)
                {
                    Need(p >= 0 && p < assigned.Length && !assigned[p], "One explicit cell owner per packet");
                    assigned[p] = true;
                    (data.Road[p] ? r : t).Add(data.Packets[p]);
                }

                Need(CookedBinary.BufferDigest(r.Concat(t)) == c.FinalBufferDigest, "Actual cell digest");
                var work = CookedCellGeometryVerifier.Check(c, r.ToArray(), t.ToArray(), bankGrades, data.QuantizationExponent, data.RoadGrade, data.TerrainGrade, token);
                totalReferences += work.References;
                totalVisits += work.Visits;
                Need(totalReferences <= request.Limits.References && totalVisits <= CookedTerrainPolicy.AggregateEmbeddingPairBudget,
                    "Aggregate finite embedding work budget");
                byCell.Add((c.X, c.Z), c);
            }

            Need(assigned.All(v => v), "Every buffer accounted; no detached geometry");
            foreach (var c in data.Cells)
            {
                token.ThrowIfCancellationRequested();
                foreach (var key in new[]
                {
                    (c.X + 1, c.Z),
                    (c.X, c.Z + 1)
                }

                )
                    if (byCell.TryGetValue(key, out var other))
                    {
                        bool x = key.Item1 != c.X;
                        double at = x ? c.MaxX : c.MaxZ;
                        Need(Border(data, c, x, at).SequenceEqual(Border(data, other, x, at)), "Actual shared-cell/root seam topology/Y");
                    }
            }

            token.ThrowIfCancellationRequested();
            progress?.Invoke("verified-ready", data.Cells.Length, data.Cells.Length);
            return new VerifiedCookedGeometry(data, request.ExpectedArtifactDigest);
        }

        static string P(double x, double y, double z) => (x == 0 ? 0 : x).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + (y == 0 ? 0 : y).ToString("R",
            System.Globalization.CultureInfo.InvariantCulture) + "," + (z == 0 ? 0 : z).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string[] Border(CookedData data, CookedCell c, bool x, double at)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (int i in c.Packets)
            {
                var p = data.Packets[i];
                for (int j = 0; j < p.Indices.Length; j += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = p.Indices[j + k] * 3, b = p.Indices[j + (k + 1) % 3] * 3;
                        double ax = p.OriginX + p.XYZ[a], az = p.OriginZ + p.XYZ[a + 2], bx = p.OriginX + p.XYZ[b], bz = p.OriginZ + p.XYZ[b + 2];
                        if ((x ? ax : az) != at || (x ? bx : bz) != at)
                            continue;
                        string aa = P(ax, p.OriginY + p.XYZ[a + 1], az), bb = P(bx, p.OriginY + p.XYZ[b + 1], bz);
                        keys.Add(string.CompareOrdinal(aa, bb) < 0 ? aa + "|" + bb : bb + "|" + aa);
                    }
            }

            return keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        static Dictionary<(string, int), double> CheckBanks(CookedData data, CookedTerrainRequest request,
            Dictionary<string, LocalTerrainMesh> packets, HashSet<string> roadIds, CancellationToken token)
        {
            Need(data.Banks.Length == request.ExpectedBanks.Length, "Explicit allowed bank set");
            var result = new Dictionary<(string, int), double>();
            foreach (var b in data.Banks)
            {
                token.ThrowIfCancellationRequested();
                var expected = request.ExpectedBanks.SingleOrDefault(a => a.Id == b.Id);
                Need(expected != null && b.GeometryRecipe == expected.GeometryRecipe && b.SourceEdge == expected.SourceEdge
                    && b.AllowedGrade == expected.AllowedGrade && Finite(b.AllowedGrade) && b.AllowedGrade > 0
                    && b.AllowedGrade <= 10 && b.FinalBufferDigest == data.FinalBufferDigest && b.Sources.SequenceEqual(expected.Sources)
                    && CookedBinary.SHA(b.SourceEdge) && b.Faces.Length > 0 && b.Loops.Length > 0, "Bound source-bank provenance, no global slope exception");
                var edges = new Dictionary<string, (int Count, string Start, string End)>();
                var neighbours = new Dictionary<string, List<int>>();
                double area = 0, max = 0;
                for (int i = 0; i < b.Faces.Length; i++)
                {
                    if (i % CookedTerrainPolicy.CancellationCheckInterval == 0)
                        token.ThrowIfCancellationRequested();
                    var f = b.Faces[i];
                    Need(packets.TryGetValue(f.Packet, out var p) && !roadIds.Contains(f.Packet)
                        && f.Triangle >= 0 && f.Triangle < p.Indices.Length / 3 && !result.ContainsKey((f.Packet, f.Triangle)),
                        "Unique terrain-only bank face");
                    result.Add((f.Packet, f.Triangle), b.AllowedGrade);
                    var v = new (double X, double Y, double Z)[3];
                    for (int k = 0; k < 3; k++)
                    {
                        int j = p.Indices[f.Triangle * 3 + k] * 3;
                        v[k] = (p.OriginX + p.XYZ[j], p.OriginY + p.XYZ[j + 1], p.OriginZ + p.XYZ[j + 2]);
                    }

                    var a = v[0];
                    var z = v[1];
                    var c = v[2];
                    double ux = z.X - a.X, uz = z.Z - a.Z, vx = c.X - a.X, vz = c.Z - a.Z, den = ux * vz - uz * vx;
                    Need(den < 0, "Bank upward face");
                    area -= den * .5;
                    double gx = ((z.Y - a.Y) * vz - (c.Y - a.Y) * uz) / den, gz = (ux * (c.Y - a.Y) - vx * (z.Y - a.Y)) / den;
                    max = Math.Max(max, Math.Sqrt(gx * gx + gz * gz));
                    for (int k = 0; k < 3; k++)
                    {
                        var aa = v[k];
                        var bb = v[(k + 1) % 3];
                        string av = P(aa.X, aa.Y, aa.Z), bv = P(bb.X, bb.Y, bb.Z), key = string.CompareOrdinal(av, bv) < 0 ? av + "|" + bv : bv + "|" + av;
                        if (edges.TryGetValue(key, out var old))
                        {
                            Need(old.Count == 1 && old.Start == bv && old.End == av, "Bank manifold orientation");
                            edges[key] = (2, old.Start, old.End);
                        }
                        else
                            edges[key] = (1, av, bv);
                        if (!neighbours.TryGetValue(key, out var ids))
                            neighbours[key] = ids = new List<int>();
                        ids.Add(i);
                    }
                }

                Need(Finite(area) && Finite(max) && Math.Abs(area - b.Area) < CookedTerrainPolicy.BankAreaAgreementSquareMetres
                    && Math.Abs(max - b.Maximum) < CookedTerrainPolicy.BankGradeAgreementRatio && max <= b.AllowedGrade,
                    "Actual bank area/grade");
                var seen = new HashSet<int>
                {
                    0
                };
                var queue = new Queue<int>();
                queue.Enqueue(0);
                var adjacency = new List<int>[b.Faces.Length];
                for (int i = 0; i < adjacency.Length; i++)
                    adjacency[i] = new List<int>();
                foreach (var pair in neighbours.Values)
                    if (pair.Count == 2)
                    {
                        adjacency[pair[0]].Add(pair[1]);
                        adjacency[pair[1]].Add(pair[0]);
                    }

                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    foreach (int n in adjacency[id])
                        if (seen.Add(n))
                            queue.Enqueue(n);
                }

                Need(seen.Count == b.Faces.Length, "Connected bank component");
                var boundary = new HashSet<string>(edges.Where(p => p.Value.Count == 1).Select(p => p.Key));
                var declared = new HashSet<string>();
                foreach (var loop in b.Loops)
                {
                    Need(loop.Length >= 6 && loop.Length % 2 == 0 && loop.All(Finite), "Bounded actual bank loop");
                    for (int i = 0; i < loop.Length; i += 2)
                    {
                        int next = (i + 2) % loop.Length;
                        string edge = BoundaryXZ(loop[i], loop[i + 1], loop[next], loop[next + 1]);
                        Need(declared.Add(edge), "Unique bank outline edge");
                    }
                }

                var actual = new HashSet<string>();
                foreach (var pair in edges.Where(p => p.Value.Count == 1))
                {
                    var f = pair.Value;
                    var a = f.Start.Split(',');
                    var c = f.End.Split(',');
                    actual.Add(BoundaryXZ(double.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), double.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture)));
                }

                Need(actual.SetEquals(declared), "Bank loops match exact final triangles");
            }

            return result;
        }

        static string BoundaryXZ(double x, double z, double xx, double zz)
        {
            string a = P(x, 0, z), b = P(xx, 0, zz);
            return string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        }
    }
}
