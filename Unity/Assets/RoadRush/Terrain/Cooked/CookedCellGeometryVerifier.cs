using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using RoadRush.TerrainDraft;

namespace RoadRush.TerrainDraft.Cooked
{
    internal sealed class CookedGeometryFailure : Exception
    {
        internal object Witness;
        internal CookedGeometryFailure(string message, object witness) : base(message)
        {
            Witness = witness;
        }
    }

    /// <summary>
    /// Independent final-buffer planar embedding check, forked from the reviewed
    /// authoring verifier without re-triangulation. Integer X/Z predicates are exact
    /// on the declared local lattice; actual double world Y must agree at shared X/Z.
    /// Retains every positive face: no tiny-face deletion, snap, grade clamp or repair.
    /// </summary>
    internal static class CookedCellGeometryVerifier
    {
        readonly struct Edge : IEquatable<Edge>
        {
            public readonly int A, B;
            public Edge(int a, int b)
            {
                A = Math.Min(a, b);
                B = Math.Max(a, b);
            }

            public bool Equals(Edge e) => A == e.A && B == e.B;
            public override bool Equals(object o) => o is Edge e && Equals(e);
            public override int GetHashCode() => unchecked(A * 397 ^ B);
        }

        // The returned counts feed aggregate budgets. Per-cell bounds are checked
        // during work; aggregate overflow is refused after each completed cell, so
        // cancellation/work overhead is bounded by at most one cell's local cap.
        internal static (int References, int Visits) Check(CookedCell scope, LocalTerrainMesh[] road,
            LocalTerrainMesh[] terrain, Dictionary<(string, int), double> bankGrades, int exponent, double roadGrade,
            double terrainGrade, CancellationToken cancellation)
        {
            if (scope.MaxX - scope.MinX > CookedTerrainPolicy.MaximumCellSideMetres || scope.MaxZ - scope.MinZ > CookedTerrainPolicy.MaximumCellSideMetres
                || exponent < 0 || exponent > CookedTerrainPolicy.MaximumQuantizationExponent)
                throw new InvalidDataException("CheckedInt64 localcell/exponent bounds");
            var roadIds = new HashSet<string>(road.Select(p => p.Id));
            var xy = new List<(long X, long Z)>();
            var yy = new List<double>();
            var welded = new Dictionary<(long, long), int>();
            var adjacency = new Dictionary<Edge, List<(int A, int B, int C, string Tag)>>();
            // Translate to cell-local X/Z before integer conversion. The <=320m,
            // exponent<=20 policy proves orientation products fit signed Int64;
            // checked arithmetic still refuses malicious accumulated area overflow.
            long scale = 1L << exponent;
            long totalArea = 0;
            int triangles = 0;
            long Orient(int a, int b, int c) => checked((xy[b].X - xy[a].X) * (xy[c].Z - xy[a].Z) - (xy[b].Z - xy[a].Z) * (xy[c].X - xy[a].X));
            foreach (var packet in road.Concat(terrain))
            {
                cancellation.ThrowIfCancellationRequested();
                bool isRoad = roadIds.Contains(packet.Id);
                if (packet.XYZ.Length % 3 != 0 || packet.Indices.Length % 3 != 0 || packet.XYZ.Length > CookedTerrainPolicy.MaximumCellVertices * 3
                    || packet.Indices.Length > CookedTerrainPolicy.MaximumCellTriangles * 3)
                    throw new ArgumentException("Joined buffer budget");
                var remap = new int[packet.XYZ.Length / 3];
                for (int i = 0; i < remap.Length; i++)
                {
                    if (i % CookedTerrainPolicy.CancellationCheckInterval == 0)
                        cancellation.ThrowIfCancellationRequested();
                    double x = (packet.OriginX - scope.MinX) + packet.XYZ[i * 3], z = (packet.OriginZ - scope.MinZ) + packet.XYZ[i * 3 + 2],
                        y = packet.OriginY + packet.XYZ[i * 3 + 1];
                    if (!TerrainFieldLimits.Finite(x) || !TerrainFieldLimits.Finite(y) || Math.Abs(y) > CookedTerrainPolicy.MaximumHeightMagnitudeMetres
                        || !TerrainFieldLimits.Finite(z) || x < 0 || x > scope.MaxX - scope.MinX || z < 0 || z > scope.MaxZ - scope.MinZ)
                        throw new ArgumentException("Joined nonfinite/outside scoped buffers");
                    long xx = (long)Math.Round(x * scale), zz = (long)Math.Round(z * scale);
                    if ((double)xx / scale != x || (double)zz / scale != z)
                        throw new ArgumentException("Joined noncanonical finalXZ");
                    var key = (xx, zz);
                    if (welded.TryGetValue(key, out int id))
                    {
                        if (yy[id] != y)
                            throw new ArgumentException("Joined duplicateXZ has DIFFERENT actualworldY; no seamgap admitted");
                    }
                    else
                    {
                        if (xy.Count >= CookedTerrainPolicy.MaximumCellVertices)
                            throw new ArgumentException("Joined totalvertexcap");
                        id = xy.Count;
                        welded.Add(key, id);
                        xy.Add(key);
                        yy.Add(y);
                    }

                    remap[i] = id;
                }

                for (int i = 0; i < packet.Indices.Length; i += 3)
                {
                    if ((i / 3) % CookedTerrainPolicy.CancellationCheckInterval == 0)
                        cancellation.ThrowIfCancellationRequested();
                    if (++triangles > CookedTerrainPolicy.MaximumCellTriangles)
                        throw new ArgumentException("Joined totaltrianglecap");
                    int a = packet.Indices[i], b = packet.Indices[i + 1], c = packet.Indices[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= remap.Length || b >= remap.Length || c >= remap.Length)
                        throw new ArgumentException("Joined finalindex invalid");
                    a = remap[a];
                    b = remap[b];
                    c = remap[c];
                    var area = Orient(a, b, c);
                    if (area >= 0)
                        throw new ArgumentException("Joined zero/backward face cannot be dropped");
                    totalArea = checked(totalArea - area);
                    // Solve the triangle plane gradient dY/dX, dY/dZ. Its magnitude
                    // is physical rise/run, independent of tessellation orientation.
                    double ux = (double)(xy[b].X - xy[a].X) / scale, uz = (double)(xy[b].Z - xy[a].Z) / scale,
                        vx = (double)(xy[c].X - xy[a].X) / scale, vz = (double)(xy[c].Z - xy[a].Z) / scale, den = ux * vz - uz * vx,
                        gx = ((yy[b] - yy[a]) * vz - (yy[c] - yy[a]) * uz) / den, gz = (ux * (yy[c] - yy[a]) - vx * (yy[b] - yy[a])) / den;
                    if (Math.Sqrt(gx * gx + gz * gz) > (isRoad ? roadGrade : bankGrades.TryGetValue((packet.Id, i / 3), out double permitted) ? permitted : terrainGrade))
                        throw new CookedGeometryFailure("Joined finalface exceeds physical road18%/terrain1.2grade; noflattening",
                            new { role = isRoad ? "road" : "terrain", packet = packet.Id, triangle = i / 3, grade = Math.Sqrt(gx * gx + gz * gz),
                            points = new[] { new[] { (double)xy[a].X / scale, yy[a], (double)xy[a].Z / scale }, new[] { (double)xy[b].X / scale,
                            yy[b], (double)xy[b].Z / scale }, new[] { (double)xy[c].X / scale, yy[c], (double)xy[c].Z / scale } } });
                    foreach (var t in new[]
                    {
                        (a, b, c),
                        (b, c, a),
                        (c, a, b)
                    }

                    )
                    {
                        var edge = new Edge(t.Item1, t.Item2);
                        if (!adjacency.TryGetValue(edge, out var list))
                            adjacency[edge] = list = new List<(int, int, int, string)>();
                        list.Add((t.Item1, t.Item2, t.Item3, packet.Id + "/triangle/" + (i / 3)));
                        if (list.Count > 2)
                            throw new CookedGeometryFailure("Joined nonmanifold edge", new { a = new[] { scope.MinX + (double)xy[edge.A].X / scale,
                                yy[edge.A], scope.MinZ + (double)xy[edge.A].Z / scale }, b = new[] { scope.MinX + (double)xy[edge.B].X / scale,
                                yy[edge.B], scope.MinZ + (double)xy[edge.B].Z / scale }, faces = list.Select(p => new { p.Tag,
                                points = new[] { new[] { (double)xy[p.A].X / scale, yy[p.A], (double)xy[p.A].Z / scale }, new[] { (double)xy[p.B].X / scale,
                                yy[p.B], (double)xy[p.B].Z / scale }, new[] { (double)xy[p.C].X / scale, yy[p.C], (double)xy[p.C].Z / scale } } }).ToArray() });
                    }
                }
            }

            if (totalArea != checked(2 * (long)Math.Round((scope.MaxX - scope.MinX) * scale) * (long)Math.Round((scope.MaxZ - scope.MinZ) * scale)))
                throw new ArgumentException("Joined finalpositivearea does not cover complete requested cell");
            var incoming = new Dictionary<int, int>();
            var outgoing = new Dictionary<int, int>();
            foreach (var pair in adjacency)
            {
                var a = pair.Value[0];
                if (pair.Value.Count == 2)
                {
                    var b = pair.Value[1];
                    if (a.A != b.B || a.B != b.A || Math.Sign(Orient(a.A, a.B, a.C)) * Math.Sign(Orient(a.A, a.B, b.C)) >= 0)
                        throw new ArgumentException("Joined inconsistent sharededge/winding");
                }
                else
                {
                    var p = xy[a.A];
                    var q = xy[a.B];
                    if (!((p.X == q.X && (p.X == 0 || p.X == (long)Math.Round((scope.MaxX - scope.MinX) * scale)))
                        || (p.Z == q.Z && (p.Z == 0 || p.Z == (long)Math.Round((scope.MaxZ - scope.MinZ) * scale)))))
                        throw new ArgumentException("Joined exposed innergap/hole; no deletion tolerance");
                    incoming.TryGetValue(a.B, out int ii);
                    incoming[a.B] = ii + 1;
                    outgoing.TryGetValue(a.A, out int oo);
                    outgoing[a.A] = oo + 1;
                }
            }

            if (incoming.Keys.Union(outgoing.Keys).Any(i => !incoming.ContainsKey(i) || !outgoing.ContainsKey(i) || incoming[i] != 1 || outgoing[i] != 1))
                throw new ArgumentException("Joined boundarybranch/open topology");
            var edges = adjacency.Keys.ToArray();
            var grid = new Dictionary<(int, int), List<int>>();
            int refs = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                var a = xy[edges[i].A];
                var b = xy[edges[i].B];
                for (int z = (int)Math.Floor((double)Math.Min(a.Z, b.Z) / scale); z <= (int)Math.Floor((double)Math.Max(a.Z, b.Z) / scale); z++)
                    for (int x = (int)Math.Floor((double)Math.Min(a.X, b.X) / scale); x <= (int)Math.Floor((double)Math.Max(a.X, b.X) / scale); x++)
                    {
                        if (++refs > CookedTerrainPolicy.MaximumCellEmbeddingReferences)
                            throw new ArgumentException("Joined embedding indexcap");
                        if (!grid.TryGetValue((x, z), out var list))
                            grid[(x, z)] = list = new List<int>();
                        list.Add(i);
                    }
            }

            var pairs = new HashSet<(int, int)>();
            int visits = 0;
            foreach (var list in grid.Values)
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if ((visits & 127) == 0)
                            cancellation.ThrowIfCancellationRequested();
                        if (++visits > CookedTerrainPolicy.MaximumCellEmbeddingPairVisits)
                            throw new ArgumentException("Joined embedding rawpaircap");
                        int u = Math.Min(list[i], list[j]), v = Math.Max(list[i], list[j]);
                        if (!pairs.Add((u, v)))
                            continue;
                        if (pairs.Count > CookedTerrainPolicy.MaximumCellEmbeddingUniquePairs)
                            throw new ArgumentException("Joined embedding paircap");
                        var e = edges[u];
                        var f = edges[v];
                        var a = xy[e.A];
                        var b = xy[e.B];
                        var c = xy[f.A];
                        var d = xy[f.B];
                        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X,
                            b.X) || Math.Max(a.Z, b.Z) < Math.Min(c.Z, d.Z) || Math.Max(c.Z, d.Z) < Math.Min(a.Z, b.Z))
                            continue;
                        int abC = Math.Sign(Orient(e.A, e.B, f.A)), abD = Math.Sign(Orient(e.A,
                            e.B, f.B)), cdA = Math.Sign(Orient(f.A, f.B, e.A)), cdB = Math.Sign(Orient(f.A, f.B, e.B));
                        bool shared = e.A == f.A || e.A == f.B || e.B == f.A || e.B == f.B;
                        if (abC == 0 && abD == 0)
                        {
                            bool axis = Math.Abs(b.X - a.X) >= Math.Abs(b.Z - a.Z);
                            long aa = axis ? a.X : a.Z, bb = axis ? b.X : b.Z, cc = axis ? c.X : c.Z, dd = axis ? d.X : d.Z;
                            if (Math.Min(Math.Max(aa, bb), Math.Max(cc, dd)) > Math.Max(Math.Min(aa, bb), Math.Min(cc, dd)))
                                throw new ArgumentException("Joined exact collinear overlap");
                        }
                        else if (!shared && abC * abD <= 0 && cdA * cdB <= 0)
                            throw new ArgumentException("Joined propercrossing/Tjunction");
                    }

            return (refs, visits);
        }
    }
}
