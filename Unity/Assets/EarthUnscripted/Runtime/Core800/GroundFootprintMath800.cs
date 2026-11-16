using System;
using System.IO;
using System.Linq;
using BigInteger = System.Numerics.BigInteger;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace EarthUnscripted.Core800
{
    // Generic exact binary-coordinate predicates, NOT an admission/capability factory.
    public static class GroundFootprintMath800
    {
        // Coordinates are metres in the prepared region's Unity X/Z frame.
        // Double origin + local float geometry is retained without a world-float cast.
        internal readonly struct PointXZ
        {
            internal readonly double X, Z;
            internal PointXZ(double x, double z)
            {
                X = x;
                Z = z;
            }

            internal PointXZ(Vector2 p)
            {
                X = p.x;
                Z = p.y;
            }
        }

        internal readonly struct TriangleXZ
        {
            internal readonly PointXZ A, B, C;
            internal TriangleXZ(PointXZ a, PointXZ b, PointXZ c)
            {
                A = a;
                B = b;
                C = c;
            }
        }

        internal static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        // IEEE754 binary64 constants. Finite numbers equal integerSignificand *2^exponent.
        private const int SignificandBits = 52;
        private const int ExponentBias = 1023;
        private const int SubnormalExponent = -1074;
        private const long ExponentMask = 0x7ff;
        private const ulong SignificandMask = 0xfffffffffffffUL;
        internal const int MaximumFootprintPoints = 257; // Up to256 vertices plus explicit closure.
        internal const double MaximumClearanceMetres = 16;
        static int Exponent(double v)
        {
            long b = BitConverter.DoubleToInt64Bits(v);
            int e = (int)((b >> SignificandBits) & ExponentMask);
            return e == 0 ? SubnormalExponent : e - ExponentBias - SignificandBits;
        }

        static BigInteger Integer(double v, int exponent)
        {
            if (v == 0)
                return BigInteger.Zero;
            long b = BitConverter.DoubleToInt64Bits(v);
            int e = (int)((b >> SignificandBits) & ExponentMask);
            ulong mantissa = (ulong)b & SignificandMask;
            if (e != 0)
                mantissa |= 1UL << SignificandBits;
            BigInteger n = new BigInteger(mantissa);
            n <<= Exponent(v) - exponent;
            return b < 0 ? -n : n;
        }

        // Zero may use any exponent; excluding it avoids expanding every ordinary
        // margin-zero query to1074bits. Nonzero subnormals still keep all source bits.
        static BigInteger[] Scale(params double[] v)
        {
            int e = v.Where(x => x != 0).Select(Exponent).DefaultIfEmpty(0).Min();
            return v.Select(x => Integer(x, e)).ToArray();
        }

        static int Orient(PointXZ a, PointXZ b, PointXZ p)
        {
            var v = Scale(a.X, a.Z, b.X, b.Z, p.X, p.Z);
            return ((v[2] - v[0]) * (v[5] - v[1]) - (v[3] - v[1]) * (v[4] - v[0])).Sign;
        }

        static bool OnSegment(PointXZ p, PointXZ a, PointXZ b) => Orient(a, b, p) == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Z >= Math.Min(a.Z, b.Z) && p.Z <= Math.Max(a.Z, b.Z);
        static bool Intersect(PointXZ a, PointXZ b, PointXZ c, PointXZ d)
        {
            int x = Orient(a, b, c), y = Orient(a, b, d), u = Orient(c, d, a), v = Orient(c, d, b);
            return x * y < 0 && u * v < 0 || x == 0 && OnSegment(c, a, b) || y == 0 && OnSegment(d, a, b) || u == 0 && OnSegment(a, c, d) || v == 0 && OnSegment(b, c, d);
        }

        // Exact point-to-segment squared-distance comparison. Dot products choose
        // the endpoint/interior case; cross^2 <= margin^2*length^2 avoids square roots
        // and epsilon decisions at one-ULP/subnormal clearance boundaries.
        static bool Within(PointXZ p, PointXZ a, PointXZ b, double margin)
        {
            var v = Scale(p.X, p.Z, a.X, a.Z, b.X, b.Z, margin);
            BigInteger x = v[4] - v[2], z = v[5] - v[3], px = v[0] - v[2], pz = v[1] - v[3], length = x * x + z * z, dot = px * x + pz * z, m2 = v[6] * v[6];
            if (dot <= 0 || length.IsZero)
                return px * px + pz * pz <= m2;
            if (dot >= length)
            {
                px = v[0] - v[4];
                pz = v[1] - v[5];
                return px * px + pz * pz <= m2;
            }

            var cross = x * pz - z * px;
            return cross * cross <= m2 * length;
        }

        static bool Near(PointXZ a, PointXZ b, PointXZ c, PointXZ d, double margin) => Intersect(a, b, c, d) || Within(a, c, d, margin) || Within(b, c, d, margin) || Within(c, a, b, margin) || Within(d, a, b, margin);
        static bool InsideTriangle(PointXZ p, TriangleXZ t)
        {
            int a = Orient(t.A, t.B, p), b = Orient(t.B, t.C, p), c = Orient(t.C, t.A, p);
            return a >= 0 && b >= 0 && c >= 0 || a <= 0 && b <= 0 && c <= 0;
        }

        static bool Inside(PointXZ p, PointXZ[] polygon)
        {
            int winding = 0;
            for (int i = 0; i < polygon.Length - 1; i++)
            {
                var a = polygon[i];
                var b = polygon[i + 1];
                if (OnSegment(p, a, b))
                    return true;
                if (a.Z <= p.Z)
                {
                    if (b.Z > p.Z && Orient(a, b, p) > 0)
                        winding++;
                }
                else if (b.Z <= p.Z && Orient(a, b, p) < 0)
                    winding--;
            }

            return winding != 0;
        }

        internal static bool Touches(PointXZ[] polygon, TriangleXZ t, double margin)
        {
            foreach (var p in polygon)
                if (InsideTriangle(p, t))
                    return true;
            if (Inside(t.A, polygon) || Inside(t.B, polygon) || Inside(t.C, polygon))
                return true;
            var tri = new[]
            {
                t.A,
                t.B,
                t.C
            };
            for (int i = 0; i < polygon.Length - 1; i++)
                for (int j = 0; j < 3; j++)
                    if (Near(polygon[i], polygon[i + 1], tri[j], tri[(j + 1) % 3], margin))
                        return true;
            return false;
        }

        internal static bool TryPolygon(Vector2[] input, out PointXZ[] polygon, out string reason)
        {
            polygon = null;
            reason = "InvalidFootprint";
            if (input == null || input.Length < 4 || input.Length > MaximumFootprintPoints || input.Any(p => !Finite(p.x) || !Finite(p.y)))
                return false;
            var p = input.Select(v => new PointXZ(v)).ToArray();
            if (p[0].X != p[p.Length - 1].X || p[0].Z != p[p.Length - 1].Z)
                return false;
            int orientation = 0;
            for (int i = 0; i < p.Length - 1; i++)
            {
                if (p[i].X == p[i + 1].X && p[i].Z == p[i + 1].Z)
                    return false;
                for (int j = i + 1; j < p.Length - 1; j++)
                {
                    if (j == i + 1 || i == 0 && j == p.Length - 2)
                        continue;
                    if (Intersect(p[i], p[i + 1], p[j], p[j + 1]))
                        return false;
                }

                if (i > 0)
                    orientation |= Orient(p[0], p[i], p[i + 1]);
            }

            if (orientation == 0)
                return false;
            polygon = p;
            reason = "";
            return true;
        }

        public static bool TouchesTriangle(Vector2[] closedWorldXZ, double[] triangleXZ, double clearanceMargin = 0)
        {
            if (!Finite(clearanceMargin) || clearanceMargin < 0 || clearanceMargin > MaximumClearanceMetres || triangleXZ == null || triangleXZ.Length != 6 || triangleXZ.Any(x => !Finite(x)) || !TryPolygon(closedWorldXZ, out var p, out _))
                throw new ArgumentException("Finite simple closed footprint, triangle and0..16m margin required");
            var t = new TriangleXZ(new PointXZ(triangleXZ[0], triangleXZ[1]), new PointXZ(triangleXZ[2], triangleXZ[3]), new PointXZ(triangleXZ[4], triangleXZ[5]));
            if (Orient(t.A, t.B, t.C) == 0)
                throw new ArgumentException("Nondegenerate bank triangle required");
            return Touches(p, t, clearanceMargin);
        }
    }

    internal sealed class GroundBankFootprintIndex800
    {
        readonly PreparedWorldMeshPlan800 plan;
        readonly GroundFootprintMath800.TriangleXZ[] faces;
        readonly Dictionary<(int, int), int[]> cells;
        private const double CellSizeMetres = 8;
        private const int MaximumBankFaces = 100000;
        private const int MaximumSpatialReferences = 1000000;
        static int K(double x) => (int)Math.Floor(x / CellSizeMetres);
        internal GroundBankFootprintIndex800(PreparedWorldMeshPlan800 plan, CancellationToken token)
        {
            this.plan = plan;
            var list = new List<GroundFootprintMath800.TriangleXZ>();
            var index = new Dictionary<(int, int), List<int>>();
            int references = 0;
            var seen = new HashSet<(string, int)>();
            foreach (var bank in plan.CopyDerivedRoadBanks())
                foreach (var group in bank.CopyTriangleReferences().GroupBy(r => r.PacketId))
                {
                    token.ThrowIfCancellationRequested();
                    if (bank.FinalBufferDigest != plan.FinalBufferDigest || !bank.BlocksPedestrianAnimalPlacement || plan.PacketRole(group.Key) != PreparedWorldMeshPlan800.Role.GroundTerrain)
                        throw new InvalidDataException("Bound final terrain bank ledger required");
                    var data = plan.CopyForUpload(group.Key);
                    foreach (var reference in group)
                    {
                        token.ThrowIfCancellationRequested();
                        int at = reference.TriangleIndex * 3;
                        if (at < 0 || at + 2 >= data.Indices.Length || !seen.Add((group.Key, reference.TriangleIndex)))
                            throw new InvalidDataException("Unique in-range bank faces required");
                        GroundFootprintMath800.PointXZ Point(int i)
                        {
                            i = data.Indices[i] * 3;
                            return new GroundFootprintMath800.PointXZ(data.OriginX + data.XYZ[i], data.OriginZ + data.XYZ[i + 2]);
                        }

                        var t = new GroundFootprintMath800.TriangleXZ(Point(at), Point(at + 1), Point(at + 2));
                        int id = list.Count;
                        list.Add(t);
                        if (list.Count > MaximumBankFaces)
                            throw new InvalidDataException("Bank face budget");
                        int x0 = K(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), x1 = K(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))), z0 = K(Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z))), z1 = K(Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                if (++references > MaximumSpatialReferences)
                                    throw new InvalidDataException("Bank reference budget");
                                if (!index.TryGetValue((x, z), out var rows))
                                    index.Add((x, z), rows = new List<int>());
                                rows.Add(id);
                            }
                    }
                }

            faces = list.ToArray();
            cells = index.ToDictionary(p => p.Key, p => p.Value.ToArray());
        }

        internal bool SupportedOutsideBanks(Vector2[] input, double margin, out string reason, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!GroundFootprintMath800.Finite(margin) || margin < 0 || margin > GroundFootprintMath800.MaximumClearanceMetres || !GroundFootprintMath800.TryPolygon(input, out var p, out reason))
            {
                reason = "InvalidFootprint";
                return false;
            }

            if (p.Any(v => !plan.Contains(v.X - margin, v.Z - margin) || !plan.Contains(v.X + margin, v.Z + margin)))
            {
                reason = "OutsideVerifiedScope";
                return false;
            }

            double x0 = p.Min(v => v.X) - margin, x1 = p.Max(v => v.X) + margin, z0 = p.Min(v => v.Z) - margin, z1 = p.Max(v => v.Z) + margin;
            var seen = new HashSet<int>();
            for (int z = K(z0); z <= K(z1); z++)
                for (int x = K(x0); x <= K(x1); x++)
                    if (cells.TryGetValue((x, z), out var ids))
                        foreach (int id in ids)
                        {
                            if (!seen.Add(id))
                                continue;
                            token.ThrowIfCancellationRequested();
                            if (GroundFootprintMath800.Touches(p, faces[id], margin))
                            {
                                reason = "TouchesDerivedBank";
                                return false;
                            }
                        }

            reason = "";
            return true;
        }
    }
}
