using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.FacadePersistentCandidate800
{
    /// <summary>One material/LOD/original-feature-page cache. Actual source buffers
    /// are borrowed immutable BFC shared meshes; only combined meshes/nodes are owned.
    /// CPU world bake changes arithmetic/culling relative to original GPU instancing;
    /// fullTown native comparison is REQUIRED, never inferred from these checks.</summary>
    internal sealed class FacadePersistentBatch800 : IDisposable
    {
        internal sealed class SourceData
        {
            internal Vector3[] Positions, Normals;
            internal Vector2[] Uv;
            internal int[] Indices;
            internal Bounds NativeBounds;
            internal string Name,EntityId64;
            internal bool BoundsContain;
            internal double Overflow;
            internal static SourceData Capture(Mesh mesh)
            {
                if (!mesh || !mesh.isReadable || mesh.subMeshCount!=1 || mesh.GetTopology(0)!=MeshTopology.Triangles
                    || mesh.vertexCount<=0 || mesh.vertexCount>4096 || mesh.GetIndexCount(0)==0 || mesh.GetIndexCount(0)>16384
                    ||mesh.GetIndexCount(0)%3!=0||mesh.GetBaseVertex(0)!=0||mesh.blendShapeCount!=0)
                    throw new InvalidOperationException("Unsupported actual shared source mesh");
                VertexAttributeDescriptor[] attributes=mesh.GetVertexAttributes();
                if(attributes.Length!=3)throw new InvalidOperationException("Only actual BFC Position/Normal/UV0 channels supported");
                foreach(var attribute in attributes)
                {
                    bool allowed=attribute.attribute==VertexAttribute.Position&&attribute.dimension==3
                        ||attribute.attribute==VertexAttribute.Normal&&attribute.dimension==3
                        ||attribute.attribute==VertexAttribute.TexCoord0&&attribute.dimension==2;
                    if(!allowed||attribute.format!=VertexAttributeFormat.Float32)throw new InvalidOperationException("Source channel/format refused, never dropped");
                }
                var data=new SourceData { Positions=mesh.vertices,Normals=mesh.normals,Uv=mesh.uv,Indices=mesh.GetIndices(0),NativeBounds=mesh.bounds,Name=mesh.name,EntityId64=UnityEngine.EntityId.ToULong(mesh.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                if(data.Normals.Length!=data.Positions.Length||data.Uv.Length!=data.Positions.Length||mesh.tangents.Length!=0)
                    throw new InvalidOperationException("Unexpected actual BFC source streams; no channel silently dropped");
                foreach(int index in data.Indices)if(index<0||index>=data.Positions.Length)throw new InvalidOperationException("Source index");
                foreach(var normal in data.Normals)if(!Finite(normal))throw new InvalidOperationException("Nonfinite source normal");
                foreach(var value in data.Uv)if(!Finite(value.x)||!Finite(value.y))throw new InvalidOperationException("Nonfinite source UV");
                data.BoundsContain=ValidateBounds(data.NativeBounds,data.Positions,out data.Overflow);
                // Report exact source float-bound overflow rather than blame API.
                // Owned bounds are independently derived OUTWARD from actual streams.
                return data;
            }
        }
        private const int MaximumInstances = 128;
        private readonly Transform stage;
        private readonly List<Matrix4x4> matrices = new List<Matrix4x4>(MaximumInstances);
        private readonly List<int> members = new List<int>(MaximumInstances);
        // Two fixed membership arrays plus the two List backing arrays: no
        // per-generation ToArray retention/churn for committed membership.
        internal const long PersistentManagedRawBytes = MaximumInstances * (64L + 4L) * 2;
        private Matrix4x4[] committedMatrices = new Matrix4x4[MaximumInstances];
        private int[] committedMembers = new int[MaximumInstances];
        private int committedCount;
        private Mesh sourceMesh, committedSource, ownedMesh, standbyMesh;
        private int activeVertexCapacity, activeIndexCapacity, standbyVertexCapacity, standbyIndexCapacity;
        private Material material, committedMaterial;
        private bool near, committedNear;
        private int layer, committedLayer, vertices, indices;
        private GameObject node;
        private MeshRenderer renderer;
        private MeshFilter filter;
        private SourceData data;
        private bool ownedEnclosure,visible,membershipUnchanged,disposed;
        private string materialId,shaderId,shaderName;
        internal int SeenFrame { get; private set; } = -1;
        // Managed generation marker only; it enumerates rows, never proves live
        // native resources. ObserveBounds must still read every actual native row.
        private bool geometryCommitted;
        private Bounds validatedOwnedBounds;
        internal bool HasCommittedGeometry => !disposed && geometryCommitted;
        internal bool OwnedBuffersReleased => !ownedMesh && !standbyMesh && !node;
        internal FacadePersistentBatch800(Transform actualOwnedIdentityStage){stage=actualOwnedIdentityStage;}
        internal void Append(int frame,Mesh mesh,Material actualMaterial,bool isNear,int actualLayer,int member,Matrix4x4 matrix)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FacadePersistentBatch800));
            if(SeenFrame!=frame)
            {
                SeenFrame=frame;matrices.Clear();members.Clear();sourceMesh=mesh;material=actualMaterial;near=isNear;layer=actualLayer;
                membershipUnchanged=ownedMesh&&ReferenceEquals(mesh,committedSource)&&ReferenceEquals(actualMaterial,committedMaterial)
                    &&isNear==committedNear&&actualLayer==committedLayer&&geometryCommitted;
            }
            if(matrices.Count>=MaximumInstances||!ReferenceEquals(sourceMesh,mesh)||!ReferenceEquals(material,actualMaterial)||near!=isNear||layer!=actualLayer)
                throw new InvalidOperationException("Whole page/material/LOD binding refused");
            int slot=matrices.Count;
            if(membershipUnchanged&&(slot>=committedCount||committedMembers[slot]!=member||!committedMatrices[slot].Equals(matrix)))membershipUnchanged=false;
            matrices.Add(matrix);members.Add(member);
        }
        internal bool Prepare(FacadePersistentTown800 budget,int frame)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FacadePersistentBatch800));
            if(SeenFrame!=frame||matrices.Count==0)return false;
            bool unchanged=geometryCommitted&&membershipUnchanged&&committedCount==matrices.Count;
            if(unchanged)return ownedEnclosure;
            // Old committed geometry cannot stand in for changed membership.
            // A refused/rebuilding generation remains hidden and unlatched.
            geometryCommitted = false;
            data=budget.GetSource(sourceMesh);
            int newVertices=checked(data.Positions.Length*matrices.Count),newIndices=checked(data.Indices.Length*matrices.Count);
            if (!budget.ReserveBuild(newVertices, newIndices, standbyVertexCapacity, standbyIndexCapacity)) return false;
            try
            {
                var positions = new Vector3[newVertices]; var normals = new Vector3[newVertices];
                var uv = new Vector2[newVertices]; var triangles = new int[newIndices];
                for (int instance = 0; instance < matrices.Count; instance++)
                {
                    Matrix4x4 matrix = matrices[instance];
                    if (!Affine(matrix) || matrix.determinant <= 1e-12f)
                        throw new InvalidOperationException("Unsupported singular/mirrored source frame");
                    Matrix4x4 normalMatrix = matrix.inverse.transpose;
                    for (int vertex = 0; vertex < data.Positions.Length; vertex++)
                    {
                        int target = instance * data.Positions.Length + vertex;
                        positions[target] = matrix.MultiplyPoint3x4(data.Positions[vertex]);
                        normals[target] = normalMatrix.MultiplyVector(data.Normals[vertex]).normalized;
                        uv[target] = data.Uv[vertex];
                        if (!Finite(positions[target]) || !Finite(normals[target]))
                            throw new InvalidOperationException("Nonfinite actual baked stream");
                    }
                    for (int index = 0; index < data.Indices.Length; index++)
                        triangles[instance * data.Indices.Length + index] = instance * data.Positions.Length + data.Indices[index];
                }
                Bounds outward = OutwardBounds(positions);
                if (!ValidateBounds(outward, positions, out _))
                    throw new InvalidOperationException("Derived owned bounds fail exact stream enclosure");
                budget.ReserveNative(64);
                if (ReferenceEquals(standbyMesh, null))
                {
                    // Retain private ownership BEFORE Track can fail. Batch.Dispose
                    // still visits this buffer even if the global fence refuses it.
                    standbyMesh = new Mesh();
                    budget.Track(standbyMesh);
                    // Ownership is established before ANY fallible native setter.
                    // An initializer expression would assign the field too late.
                    standbyMesh.name = "OWNED reusable facade standby";
                    standbyMesh.hideFlags = HideFlags.DontSave;
                    standbyMesh.indexFormat = IndexFormat.UInt32;
                }
                Mesh next = standbyMesh;
                // Keep failed/partial standby capacity charged and owned. The old
                // active mesh/filter is untouched until complete validation succeeds.
                budget.CommitBufferCapacity(newVertices, newIndices, standbyVertexCapacity, standbyIndexCapacity);
                standbyVertexCapacity = Math.Max(standbyVertexCapacity, newVertices);
                standbyIndexCapacity = Math.Max(standbyIndexCapacity, newIndices);
                next.Clear(false);
                // Restore the declared32-bit index format explicitly after clearing
                // an inactive buffer; every index/vertex stream is replaced below.
                next.indexFormat = IndexFormat.UInt32;
                next.vertices = positions; next.normals = normals; next.uv = uv;
                next.SetIndices(triangles, MeshTopology.Triangles, 0, false); next.bounds = outward;
                Vector3[] copied = next.vertices;
                Bounds actualUploadedBounds = next.bounds;
                bool nextEnclosure = ValidateBounds(actualUploadedBounds, copied, out _);
                if (!nextEnclosure || copied.Length != positions.Length)
                    throw new InvalidOperationException("Actual standby uploaded bounds/stream refused");
                for (int index = 0; index < copied.Length; index++)
                    if (!SameBits(copied[index], positions[index]))
                        throw new InvalidOperationException("Native standby position roundtrip changed");
                if (!material || !material.shader) throw new InvalidOperationException("Actual source material/shader missing");
                string nextMaterialId = UnityEngine.EntityId.ToULong(material.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture);
                string nextShaderId = UnityEngine.EntityId.ToULong(material.shader.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture);
                string nextShaderName = material.shader.name;
                if (!node)
                {
                    node = new GameObject("OWNED reusable facade material page"); node.SetActive(false); node.transform.SetParent(stage, false);
                    filter = node.AddComponent<MeshFilter>(); renderer = node.AddComponent<MeshRenderer>();
                    renderer.enabled = false; node.SetActive(true);
                }
                // All throwable upload/readback work completed on the standby.
                // If a native presentation setter fails, keep the old buffer owned,
                // hide the renderer, and restore its old filter where possible.
                Mesh previous = ownedMesh;
                try
                {
                    node.layer = layer; renderer.enabled = false; renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = near ? ShadowCastingMode.On : ShadowCastingMode.Off; renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera; renderer.renderingLayerMask = 1;
                    filter.sharedMesh = next;
                }
                catch (Exception commitFailure)
                {
                    try { renderer.enabled = false; filter.sharedMesh = previous; renderer.sharedMaterial = committedMaterial; }
                    catch (Exception restoreFailure) { throw new AggregateException(commitFailure, restoreFailure); }
                    throw;
                }
                ownedMesh = next; standbyMesh = previous;
                int previousVertexCapacity = activeVertexCapacity, previousIndexCapacity = activeIndexCapacity;
                activeVertexCapacity = standbyVertexCapacity; activeIndexCapacity = standbyIndexCapacity;
                standbyVertexCapacity = previousVertexCapacity; standbyIndexCapacity = previousIndexCapacity;
                vertices = newVertices; indices = newIndices;
                committedSource = sourceMesh; committedMaterial = material; committedNear = near; committedLayer = layer;
                for (int index = 0; index < matrices.Count; index++)
                { committedMatrices[index] = matrices[index]; committedMembers[index] = members[index]; }
                committedCount = matrices.Count;
                materialId = nextMaterialId; shaderId = nextShaderId; shaderName = nextShaderName;
                validatedOwnedBounds = actualUploadedBounds; ownedEnclosure = nextEnclosure;
                geometryCommitted = true;
                budget.RecordSuccessfulMeshRebuildCommit();
                return true;
            }
            finally { budget.ReleaseBuildReservation(); }
        }

        internal void SetVisible(bool enabled){if(renderer&&visible!=enabled){renderer.enabled=enabled;visible=enabled;}}
        internal FacadeBoundsObservation800 ObserveBounds()
        {
            if (!geometryCommitted || data == null)
                throw new InvalidOperationException("Incomplete facade geometry generation");
            // Exactly two native bounds getters, reserved by BuildSnapshot. Do NOT
            // substitute cached/default bounds for dead resources: these getters
            // throw if a committed mesh or renderer has actually been destroyed.
            Bounds actualOwnedBounds = ownedMesh.bounds;
            Bounds actualRendererBounds = renderer.bounds;
            bool enclosureStillValid = ownedEnclosure
                && SameBits(actualOwnedBounds.center, validatedOwnedBounds.center)
                && SameBits(actualOwnedBounds.extents, validatedOwnedBounds.extents);
            if (!enclosureStillValid)
                throw new InvalidOperationException("Committed facade bounds changed after validated upload");
            return new FacadeBoundsObservation800 {
                SourceMesh=data.Name,SourceMeshEntityId64=data.EntityId64,
                MaterialEntityId64=materialId,ShaderEntityId64=shaderId,ShaderName=shaderName,
                SourceLocalBounds=data.NativeBounds,OwnedLocalBounds=actualOwnedBounds,
                RendererWorldBounds=actualRendererBounds,SourceBoundsContainStreams=data.BoundsContain,
                OwnedBoundsContainStreams=enclosureStillValid,SourceMaximumOverflowMetres=data.Overflow,
                Vertices=vertices,Indices=indices };
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            geometryCommitted = false;
            var failures = new List<Exception>();
            void Release(Action release)
            { try { release(); } catch (Exception error) { failures.Add(error); } }
            Release(() => SetVisible(false));
            Release(() => { if (renderer) renderer.sharedMaterial = null; });
            Release(() => { if (filter) filter.sharedMesh = null; });
            Release(() => { if (ownedMesh) UnityEngine.Object.Destroy(ownedMesh); });
            Release(() => { if (standbyMesh) UnityEngine.Object.Destroy(standbyMesh); });
            Release(() => { if (node) UnityEngine.Object.Destroy(node); });
            // Drop CPU backing arrays even if a caller holds the disposed wrapper.
            // Keep native mesh/node references until real deferred destruction can
            // be observed; nulling them would manufacture an early released flag.
            Release(() => { matrices.Clear(); matrices.Capacity = 0; });
            Release(() => { members.Clear(); members.Capacity = 0; });
            committedMatrices = null; committedMembers = null; data = null;
            sourceMesh = committedSource = null; material = committedMaterial = null;
            materialId = shaderId = shaderName = null;
            if (failures.Count != 0) throw new AggregateException("Both facade buffers received cleanup attempts", failures);
        }
        private static bool Affine(Matrix4x4 m){for(int index=0;index<16;index++)if(!Finite(m[index]))return false;return m.m30==0&&m.m31==0&&m.m32==0&&m.m33==1;}
        private static bool SameBits(Vector3 a,Vector3 b)=>BitConverter.SingleToInt32Bits(a.x)==BitConverter.SingleToInt32Bits(b.x)&&BitConverter.SingleToInt32Bits(a.y)==BitConverter.SingleToInt32Bits(b.y)&&BitConverter.SingleToInt32Bits(a.z)==BitConverter.SingleToInt32Bits(b.z);
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        internal static bool ValidateBounds(Bounds bounds,Vector3[] stream,out double overflow)
        {
            overflow=0;Vector3 centre=bounds.center,extent=bounds.extents;
            if(!Finite(centre)||!Finite(extent)||extent.x<0||extent.y<0||extent.z<0)throw new InvalidOperationException("Nonfinite/negative bounds; planar zero axis allowed");
            foreach(Vector3 vertex in stream)
            {
                if(!Finite(vertex))throw new InvalidOperationException("Nonfinite position");
                overflow=Math.Max(overflow,Math.Max(Math.Abs(vertex.x-(double)centre.x)-extent.x,Math.Max(Math.Abs(vertex.y-(double)centre.y)-extent.y,Math.Abs(vertex.z-(double)centre.z)-extent.z)));
            }
            return overflow<=0;
        }
        private static Bounds OutwardBounds(Vector3[] stream)
        {
            Vector3 minimum=stream[0],maximum=stream[0];
            foreach(var vertex in stream){minimum=Vector3.Min(minimum,vertex);maximum=Vector3.Max(maximum,vertex);}
            var centre=new Vector3((float)((minimum.x+(double)maximum.x)/2),(float)((minimum.y+(double)maximum.y)/2),(float)((minimum.z+(double)maximum.z)/2));
            return new Bounds {center=centre,extents=new Vector3(Up(Math.Max(Math.Abs(minimum.x-(double)centre.x),Math.Abs(maximum.x-(double)centre.x))),Up(Math.Max(Math.Abs(minimum.y-(double)centre.y),Math.Abs(maximum.y-(double)centre.y))),Up(Math.Max(Math.Abs(minimum.z-(double)centre.z),Math.Abs(maximum.z-(double)centre.z))))};
        }
        private static float Up(double value){float result=(float)value;if(result<value)result=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(result)+1);return result;}
    }
}
