#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.Scenery800.Diagnostics
{
    /// <summary>Owned literal snapshot of ACTUAL native buffers. No source mesh
    /// reconstruction, recalculated normals/tangents, source authority or collider.
    /// Baked positions use F64 arithmetic on F32 inputs, then one F32 rounding;
    /// this is deliberately not claimed bit-equivalent to GPU/FMA arithmetic.</summary>
    public sealed class FacadeLiteralMesh800
    {
        public const int MaximumVertices = 8192;
        public const int MaximumIndices = 49152;
        public const double MaximumBakedPositionRoundingMetres = .001;
        private const double MinimumAbsoluteLinearDeterminant = 1e-12; // Refuse numerically singular basis, no guessed transform.
        public Vector3[] Vertices, Normals;
        public Vector4[] Tangents;
        public Vector2[] Uv;
        public int[] Indices;
        public Bounds LocalBounds;
        public IndexFormat IndexFormat;
        public string SourceEntityId64, SourceName;
        public JObject Hashes;

        public static FacadeLiteralMesh800 CaptureActual(Mesh mesh, Action<int> reserve,
            Action<int, int> reserveGeometry)
        {
            reserve(256); // BEFORE all native getter expansion and array copies.
            int vertices = mesh.vertexCount;
            if (mesh.subMeshCount != 1) throw new InvalidOperationException("Only exact actual single-submesh layout supported");
            ulong indices = mesh.GetIndexCount(0);
            if (vertices <= 0 || vertices > MaximumVertices || indices == 0 || indices > MaximumIndices
                || indices % 3 != 0 || mesh.subMeshCount != 1 || !mesh.isReadable
                || mesh.blendShapeCount != 0 || mesh.GetTopology(0) != MeshTopology.Triangles || mesh.GetBaseVertex(0) != 0)
                throw new InvalidOperationException("Unsupported actual mesh layout/cap");
            reserveGeometry(vertices, (int)indices); // BEFORE allocating native stream results.
            VertexAttributeDescriptor[] attributes = mesh.GetVertexAttributes();
            if (attributes.Length > 4) throw new InvalidOperationException("Unsupported extra vertex channels");
            foreach (var attribute in attributes)
            {
                bool supported = attribute.attribute == VertexAttribute.Position && attribute.dimension == 3
                    || attribute.attribute == VertexAttribute.Normal && attribute.dimension == 3
                    || attribute.attribute == VertexAttribute.Tangent && attribute.dimension == 4
                    || attribute.attribute == VertexAttribute.TexCoord0 && attribute.dimension == 2;
                if (!supported || attribute.format != VertexAttributeFormat.Float32)
                    throw new InvalidOperationException("Unsupported native vertex attribute; never silently drop it");
            }
            var result = new FacadeLiteralMesh800 {
                Vertices = mesh.vertices, Normals = mesh.normals, Tangents = mesh.tangents,
                Uv = mesh.uv, Indices = mesh.GetIndices(0), LocalBounds = mesh.bounds,
                IndexFormat = mesh.indexFormat,
                SourceEntityId64 = EntityId.ToULong(mesh.GetEntityId()).ToString(CultureInfo.InvariantCulture),
                SourceName = mesh.name };
            if (result.SourceName.Length > 256 || result.Vertices.Length != vertices || result.Indices.Length != (int)indices
                || result.Normals.Length != vertices || result.Uv.Length != vertices
                || result.Tangents.Length != 0 && result.Tangents.Length != vertices)
                throw new InvalidOperationException("Actual native stream lengths changed/unsupported");
            result.Validate();
            result.Hashes = result.StreamHashes();
            return result;
        }

        public Mesh CreateOwned(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat };
            try
            {
                mesh.vertices = Vertices;
                mesh.normals = Normals;
                mesh.uv = Uv;
                if (Tangents.Length != 0) mesh.tangents = Tangents;
                mesh.SetIndices(Indices, MeshTopology.Triangles, 0, false);
                mesh.bounds = LocalBounds;
                // NEVER RecalculateNormals/Tangents/Bounds: literal streams are the witness.
                return mesh;
            }
            catch { UnityEngine.Object.Destroy(mesh); throw; }
        }

        public FacadeLiteralMesh800 Bake(Matrix4x4 matrix, out JObject arithmetic)
        {
            if (matrix.m30 != 0 || matrix.m31 != 0 || matrix.m32 != 0 || matrix.m33 != 1)
                throw new InvalidOperationException("Actual affine matrix required; no perspective fit");
            double determinant = Determinant(matrix);
            if (!Finite(determinant) || Math.Abs(determinant) < MinimumAbsoluteLinearDeterminant)
                throw new InvalidOperationException("Noninvertible actual matrix");
            var baked = new FacadeLiteralMesh800 {
                Vertices = new Vector3[Vertices.Length], Normals = new Vector3[Normals.Length],
                Tangents = new Vector4[Tangents.Length], Uv = (Vector2[])Uv.Clone(), Indices = (int[])Indices.Clone(),
                IndexFormat = IndexFormat, SourceEntityId64 = SourceEntityId64, SourceName = SourceName };
            double maximumRounding = 0;
            for (int index = 0; index < Vertices.Length; index++)
            {
                Vector3 v = Vertices[index];
                double x = matrix.m00*(double)v.x + matrix.m01*(double)v.y + matrix.m02*(double)v.z + matrix.m03;
                double y = matrix.m10*(double)v.x + matrix.m11*(double)v.y + matrix.m12*(double)v.z + matrix.m13;
                double z = matrix.m20*(double)v.x + matrix.m21*(double)v.y + matrix.m22*(double)v.z + matrix.m23;
                baked.Vertices[index] = new Vector3((float)x, (float)y, (float)z);
                double dx = (float)x-x, dy = (float)y-y, dz = (float)z-z;
                maximumRounding = Math.Max(maximumRounding, Math.Sqrt(dx*dx+dy*dy+dz*dz));
                baked.Normals[index] = InverseTransposeNormal(matrix, Normals[index], determinant);
            }
            for (int index = 0; index < Tangents.Length; index++)
            {
                Vector4 tangent = Tangents[index];
                Vector3 direction = Unit(matrix.m00*(double)tangent.x + matrix.m01*(double)tangent.y + matrix.m02*(double)tangent.z,
                    matrix.m10*(double)tangent.x + matrix.m11*(double)tangent.y + matrix.m12*(double)tangent.z,
                    matrix.m20*(double)tangent.x + matrix.m21*(double)tangent.y + matrix.m22*(double)tangent.z);
                baked.Tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w * (determinant < 0 ? -1 : 1));
            }
            if (maximumRounding > MaximumBakedPositionRoundingMetres)
                throw new InvalidOperationException("Baked F32 rounding exceeds diagnostic1mm bound");
            baked.LocalBounds = OutwardBounds(baked.Vertices);
            baked.Validate();
            baked.Hashes = baked.StreamHashes();
            arithmetic = new JObject {
                ["positionOperation"] = "F64 affine on original F32 operands, then one F32 rounding",
                ["normalOperation"] = "F64 inverse transpose, normalized, F32; zero stayszero",
                ["tangentOperation"] = "F64 linear direction normalized; w times determinant sign; zero stayszero",
                ["indicesUvUnchanged"] = true, ["maximumPositionRoundingMetres"] = maximumRounding,
                ["linearDeterminant"] = determinant, ["roundingLedgerScope"] = "F64 evaluated result to F32 only; excludes total conditioning/F64evaluation/GPU error",
                ["gpuBitEquivalenceClaimed"] = false };
            return baked;
        }

        public JObject StreamHashes() => new JObject {
            ["vertexCount"] = Vertices.Length, ["indexCount"] = Indices.Length,
            ["normalCount"] = Normals.Length, ["tangentCount"] = Tangents.Length, ["uvCount"] = Uv.Length,
            ["vertexF32Sha256"] = HashVectors(Vertices), ["normalF32Sha256"] = HashVectors(Normals),
            ["tangentF32Sha256"] = HashTangents(Tangents), ["uvF32Sha256"] = HashUv(Uv), ["indexI32Sha256"] = HashIndices(Indices) };

        private void Validate()
        {
            foreach (Vector3 v in Vertices) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) throw new InvalidOperationException("Nonfinite vertex");
            foreach (Vector3 v in Normals) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) throw new InvalidOperationException("Nonfinite normal");
            foreach (Vector4 v in Tangents) if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z) || !Finite(v.w)) throw new InvalidOperationException("Nonfinite tangent");
            foreach (Vector2 v in Uv) if (!Finite(v.x) || !Finite(v.y)) throw new InvalidOperationException("NonfiniteUV");
            foreach (int i in Indices) if (i < 0 || i >= Vertices.Length) throw new InvalidOperationException("Invalid actual index");
        }
        public static double Determinant(Matrix4x4 m) => m.m00*(m.m11*(double)m.m22-m.m12*(double)m.m21)
            -m.m01*(m.m10*(double)m.m22-m.m12*(double)m.m20)+m.m02*(m.m10*(double)m.m21-m.m11*(double)m.m20);
        private static Vector3 InverseTransposeNormal(Matrix4x4 m, Vector3 n, double determinant) => Unit(
            ((m.m11*(double)m.m22-m.m12*(double)m.m21)*n.x + (m.m12*(double)m.m20-m.m10*(double)m.m22)*n.y + (m.m10*(double)m.m21-m.m11*(double)m.m20)*n.z)/determinant,
            ((m.m02*(double)m.m21-m.m01*(double)m.m22)*n.x + (m.m00*(double)m.m22-m.m02*(double)m.m20)*n.y + (m.m01*(double)m.m20-m.m00*(double)m.m21)*n.z)/determinant,
            ((m.m01*(double)m.m12-m.m02*(double)m.m11)*n.x + (m.m02*(double)m.m10-m.m00*(double)m.m12)*n.y + (m.m00*(double)m.m11-m.m01*(double)m.m10)*n.z)/determinant);
        private static Vector3 Unit(double x, double y, double z)
        {
            double length = Math.Sqrt(x*x+y*y+z*z);
            if (!Finite(length)) throw new InvalidOperationException("Nonfinite transformed direction");
            return length == 0 ? Vector3.zero : new Vector3((float)(x/length), (float)(y/length), (float)(z/length));
        }
        private static Bounds OutwardBounds(Vector3[] vertices)
        {
            Vector3 minimum = vertices[0], maximum = vertices[0];
            foreach (Vector3 v in vertices)
            {
                minimum.x = Math.Min(minimum.x,v.x); minimum.y = Math.Min(minimum.y,v.y); minimum.z = Math.Min(minimum.z,v.z);
                maximum.x = Math.Max(maximum.x,v.x); maximum.y = Math.Max(maximum.y,v.y); maximum.z = Math.Max(maximum.z,v.z);
            }
            Vector3 centre = new Vector3((float)((minimum.x+(double)maximum.x)/2),(float)((minimum.y+(double)maximum.y)/2),(float)((minimum.z+(double)maximum.z)/2));
            return new Bounds { center = centre, extents = new Vector3(
                Up(Math.Max(Math.Abs(minimum.x-(double)centre.x),Math.Abs(maximum.x-(double)centre.x))),
                Up(Math.Max(Math.Abs(minimum.y-(double)centre.y),Math.Abs(maximum.y-(double)centre.y))),
                Up(Math.Max(Math.Abs(minimum.z-(double)centre.z),Math.Abs(maximum.z-(double)centre.z)))) };
        }
        private static float Up(double value)
        {
            float rounded = (float)value;
            if (rounded < value) rounded = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(rounded)+1);
            return rounded;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        private static void Put(float value, byte[] bytes, ref int offset)
        { uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value)); for(int i=0;i<4;i++)bytes[offset++]=(byte)(bits>>(i*8)); }
        private static string HashVectors(Vector3[] values)
        { var bytes=new byte[values.Length*12];int at=0;foreach(var v in values){Put(v.x,bytes,ref at);Put(v.y,bytes,ref at);Put(v.z,bytes,ref at);}return Hash(bytes); }
        private static string HashTangents(Vector4[] values)
        { var bytes=new byte[values.Length*16];int at=0;foreach(var v in values){Put(v.x,bytes,ref at);Put(v.y,bytes,ref at);Put(v.z,bytes,ref at);Put(v.w,bytes,ref at);}return Hash(bytes); }
        private static string HashUv(Vector2[] values)
        { var bytes=new byte[values.Length*8];int at=0;foreach(var v in values){Put(v.x,bytes,ref at);Put(v.y,bytes,ref at);}return Hash(bytes); }
        private static string HashIndices(int[] values)
        { var bytes=new byte[values.Length*4];int at=0;foreach(int v in values)for(int i=0;i<4;i++)bytes[at++]=(byte)(v>>(i*8));return Hash(bytes); }
    }
}
#endif
