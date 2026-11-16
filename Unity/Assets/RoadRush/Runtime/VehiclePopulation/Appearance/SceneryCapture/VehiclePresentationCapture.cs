using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using RoadRush.FunnyAnimals.VehicleCaptureProposal;

namespace RoadRush.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Actual-root capture helper AND private producer witness. All Unity access must
    /// run on the producer's validated main thread. Root registry owns invocation,
    /// source association, freshness and disposal; this type issues no actor/roster/
    /// semantic authority and never mutates borrowed geometry, settings or physics.
    /// Only the immutable DTO escapes; all native references below remain private.
    /// Full capture/validation scans/allocates: never call per logical safety query.
    /// </summary>
    internal sealed class VehiclePresentationCapture : IDisposable
    {
        // Initial source proposal limits, not measured all-model admission. Fail closed
        // before large getters where counts are available; no source values are clamped.
        private const int MaximumNodes = 1024;
        private const int MaximumRenderers = 512;
        private const int MaximumRenderersPerNode = 4;
        private const int MaximumDistinctMeshes = 256;
        private const int MaximumVertices = 500000;
        private const long MaximumTriangleIndices = 1500000;
        private const int MaximumSubmeshes = 16;
        private const int MaximumMaterialsPerRenderer = 16;
        private const int MaximumTextCharacters = 256;
        private const int MaximumFontSizePixels = 512;
        private const float MaximumTextMetric = 100; // character/line/offset numeric refusal fence
        private const int CancellationStride = 64;
        private const int KnownModelCount = 4;

        private sealed class NodeState
        {
            internal Transform Node;
            internal Transform Parent;
            internal int ChildCount;
            internal Matrix4x4 LocalToWorld;
        }
        private sealed class MeshState
        {
            internal Mesh Mesh;
            internal OccupiedBounds VertexBounds;
            internal int VertexCount;
            internal int SubmeshCount;
            internal long Indices;
            internal byte[] Digest;
        }
        private sealed class RendererState
        {
            internal MeshRenderer Renderer;
            internal MeshFilter Filter;
            internal Mesh Mesh;
            internal TextMesh Text;
            internal Font Font;
            internal Material[] Materials;
            internal Shader[] Shaders;
            internal Matrix4x4 LocalToWorld;
            internal bool Enabled;
            internal bool Active;
        }

        private Transform root;
        private CarVisual visual;
        private NodeState[] nodes;
        private RendererState[] renderers;
        private MeshState[] meshes;
        private VehiclePresentationData capturedData;
        private Matrix4x4 capturedRootFrame;
        private bool disposed;

        private VehiclePresentationCapture() { }

        /// <summary>
        /// Copies complete SUPPORTED loaded geometry below actualRoot, including all
        /// inactive/disabled loaded mesh LODs. Actual visual must be built under root.
        /// Mesh positions/triangle indices are read and hashed, not trusted from cached
        /// Mesh.bounds. Native TextMesh is a SEPARATE reported-bounds evidence path;
        /// font refresh/render correctness needs native acceptance before activation.
        /// Cancellation/unsupported/caps/exceptions return false with null outputs,
        /// never a partial prefix/default box. Only owned temporary memory is changed.
        /// </summary>
        internal static bool TryCapture(Transform actualRoot, CarVisual actualVisual,
            CancellationToken cancellation, out VehiclePresentationData data,
            out VehiclePresentationCapture privateWitness, out string reason)
        {
            data = null;
            privateWitness = null;
            reason = "Actual presentation unavailable";
            try
            {
                cancellation.ThrowIfCancellationRequested();
                Need(actualRoot && actualVisual && actualRoot.gameObject.activeInHierarchy
                    && (actualVisual.transform == actualRoot || actualVisual.transform.IsChildOf(actualRoot))
                    && actualVisual.Body && actualVisual.Model >= 0 && actualVisual.Model < KnownModelCount,
                    "Actual built visual/root binding unavailable");
                Matrix4x4 rootFrame = actualRoot.localToWorldMatrix;
                Matrix4x4 worldToRoot = actualRoot.worldToLocalMatrix;
                Need(TryAffine(rootFrame, out _), "Nonfinite/projective root frame");
                Need(TryAffine(worldToRoot, out Affine3 inverseRoot), "Nonfinite/projective inverse root frame");
                int model = actualVisual.Model;
                int damageRevision = actualVisual.DamageRevision;
                var pendingNodes = new List<Transform>(MaximumNodes) { actualRoot };
                var nodeStates = new List<NodeState>();
                var rendererStates = new List<RendererState>();
                var meshStates = new Dictionary<Mesh, MeshState>();
                var nodeRenderers = new List<Renderer>(MaximumRenderersPerNode);
                var vertexBuffer = new List<Vector3>();
                var indexBuffer = new List<int>();
                int vertexCount = 0;
                long indexCount = 0;
                int nativeTextCount = 0;
                bool hasBounds = false;
                bool hasWorldBounds = false;
                OccupiedBounds rootBounds = default;
                OccupiedBounds directWorldBounds = default;
                PresentationEvidenceKind evidence = 0;
                using var signature = new PresentationSignatureWriter();
                signature.WriteInt(2); // Direct-world numeric witness schema; not an authority version.
                signature.WriteOpaqueIdentity(actualRoot);
                signature.WriteOpaqueIdentity(actualVisual);
                signature.WriteMatrix(rootFrame);
                signature.WriteInt(model);
                signature.WriteInt(damageRevision);

                for (int nodeIndex = 0; nodeIndex < pendingNodes.Count; nodeIndex++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Transform node = pendingNodes[nodeIndex];
                    int children = node.childCount;
                    Need(children <= MaximumNodes - pendingNodes.Count, "Loaded node cap");
                    for (int child = 0; child < children; child++) pendingNodes.Add(node.GetChild(child));
                    Matrix4x4 nodeFrame = node.localToWorldMatrix;
                    Need(TryAffine(nodeFrame, out _), "Nonfinite loaded child frame");
                    nodeStates.Add(new NodeState { Node = node, Parent = node.parent, ChildCount = children, LocalToWorld = nodeFrame });
                    signature.WriteOpaqueIdentity(node);
                    signature.WriteOpaqueIdentity(node.parent);
                    signature.WriteInt(children);
                    signature.WriteMatrix(nodeFrame);
                    nodeRenderers.Clear();
                    // Unity fills ONE node's list before its result count can be checked.
                    // Producer-admitted component counts/native allocation need acceptance.
                    node.GetComponents<Renderer>(nodeRenderers);
                    Need(nodeRenderers.Count <= MaximumRenderersPerNode
                        && rendererStates.Count + nodeRenderers.Count <= MaximumRenderers, "Loaded renderer cap");
                    foreach (Renderer candidate in nodeRenderers)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        Need(candidate is MeshRenderer, "Unsupported skinned/particle/line/sprite renderer");
                        var renderer = (MeshRenderer)candidate;
                        Need(!renderer.isPartOfStaticBatch && !renderer.additionalVertexStreams,
                            "Unsupported static batching/additional vertex stream");
                        // ORIGINAL captured matrices stay separate. A float product here
                        // lost0.414m in Street's unit-root/high-shear cancellation regression.
                        Matrix4x4 rendererWorld = renderer.localToWorldMatrix;
                        Need(TryAffine(rendererWorld, out Affine3 partWorld), "Nonfinite/projective renderer frame");
                        var state = new RendererState
                        {
                            Renderer = renderer, LocalToWorld = renderer.localToWorldMatrix,
                            Enabled = renderer.enabled, Active = renderer.gameObject.activeInHierarchy
                        };
                        CaptureMaterials(state, signature);
                        signature.WriteOpaqueIdentity(renderer);
                        signature.WriteMatrix(rendererWorld);
                        signature.WriteMatrix(worldToRoot);
                        signature.WriteInt(state.Enabled ? 1 : 0);
                        signature.WriteInt(state.Active ? 1 : 0);
                        TextMesh text = renderer.GetComponent<TextMesh>();
                        if (text)
                        {
                            // Native-generated glyph stream is NOT literal mesh coverage.
                            CaptureText(state, text, partWorld, inverseRoot, signature, ref rootBounds, ref hasBounds, ref directWorldBounds, ref hasWorldBounds);
                            nativeTextCount++;
                            evidence |= PresentationEvidenceKind.NativeReportedTextBounds;
                        }
                        else
                        {
                            MeshFilter filter = renderer.GetComponent<MeshFilter>();
                            Need(filter && filter.sharedMesh, "Mesh renderer lacks readable source mesh");
                            Mesh mesh = filter.sharedMesh;
                            if (!meshStates.TryGetValue(mesh, out MeshState meshState))
                            {
                                Need(meshStates.Count < MaximumDistinctMeshes, "Distinct mesh cap");
                                meshState = ScanMesh(mesh, vertexBuffer, indexBuffer, cancellation,
                                    ref vertexCount, ref indexCount);
                                meshStates.Add(mesh, meshState);
                            }
                            state.Filter = filter;
                            state.Mesh = mesh;
                            Need(PresentationGeometryMath.TryTransformOriginalStages(meshState.VertexBounds, partWorld,
                                inverseRoot, out OccupiedBounds sourceWorldBounds, out OccupiedBounds partBounds), "Conditioned original-stage source-mesh transform refused");
                            AddBounds(ref rootBounds, ref hasBounds, partBounds);
                            AddBounds(ref directWorldBounds, ref hasWorldBounds, sourceWorldBounds);
                            signature.WriteOpaqueIdentity(filter);
                            signature.WriteOpaqueIdentity(mesh);
                            signature.WriteDigest(meshState.Digest);
                            evidence |= PresentationEvidenceKind.LiteralMeshVertices;
                        }
                        // Active engine-reported bounds can exceed literal CPU positions.
                        // Inactive LODs use scanned positions, never a default world0 box.
                        if (state.Enabled && state.Active)
                        {
                            Bounds reported = renderer.bounds;
                            Vector3 reportedCentre = reported.center;
                            Vector3 reportedExtents = reported.extents;
                            Need(PresentationGeometryMath.TryEncloseOriginalReportedBox(Point(reportedCentre),
                                Point(reportedExtents), out OccupiedBounds reportedWorldBounds),
                                "Invalid/ill-conditioned original reported renderer box");
                            AddBounds(ref directWorldBounds, ref hasWorldBounds, reportedWorldBounds);
                            Need(PresentationGeometryMath.TryTransformBounds(reportedWorldBounds, inverseRoot,
                                out OccupiedBounds reportedRoot), "Conditioned reported renderer bounds refused");
                            AddBounds(ref rootBounds, ref hasBounds, reportedRoot);
                            signature.WriteVector(reportedCentre);
                            signature.WriteVector(reportedExtents);
                        }
                        rendererStates.Add(state);
                    }
                }
                Need(hasBounds && hasWorldBounds && directWorldBounds.Valid && rendererStates.Count > 0
                    && PresentationGeometryMath.FitsRootRadius(rootBounds),
                    "No supported geometry or oversized root envelope");
                cancellation.ThrowIfCancellationRequested();
                // Actual native returned world point, copied as an anchor observation.
                // It is NOT obtained by round-tripping the root-relative point.
                Vector3 worldEye = actualVisual.transform.TransformPoint(actualVisual.CockpitEye);
                Need(Finite(worldEye), "Nonfinite actual native world cockpit eye");
                AddBounds(ref directWorldBounds, ref hasWorldBounds,
                    new OccupiedBounds(Point(worldEye), Point(worldEye)));
                Vector3 rootEye = actualRoot.InverseTransformPoint(worldEye);
                Need(Finite(rootEye) && PresentationGeometryMath.FitsRootRadius(
                    new OccupiedBounds(Point(rootEye), Point(rootEye))),
                    "Nonfinite/unsupported actual root-relative cockpit eye");
                // This getter itself allocates/scans model meshes. Exactly once per FULL
                // capture, additional to our bounded scans; NOT per logical world query.
                // Its actual underlying work/native cost must be profiled by the owner.
                float deformationExtent = actualVisual.DeformationExtent;
                cancellation.ThrowIfCancellationRequested();
                Need(Finite(deformationExtent) && deformationExtent >= 0, "Invalid actual deformation extent");
                signature.WriteVector(rootEye);
                signature.WriteVector(worldEye);
                signature.WriteBounds(directWorldBounds);
                signature.WriteFloat(deformationExtent);
                Need(actualVisual.Model == model && actualVisual.DamageRevision == damageRevision
                    && actualRoot.localToWorldMatrix.Equals(rootFrame), "Visual/root changed during capture");
                var result = new VehiclePresentationData(rootBounds, Point(rootEye), directWorldBounds, Point(worldEye), model, damageRevision,
                    deformationExtent, rendererStates.Count, meshStates.Count, vertexCount, indexCount,
                    PresentationSignatureWriter.Hex(signature.Finish()), nativeTextCount, evidence);
                var captured = new VehiclePresentationCapture
                {
                    root = actualRoot, visual = actualVisual, capturedRootFrame = rootFrame,
                    nodes = nodeStates.ToArray(), renderers = rendererStates.ToArray(),
                    meshes = new List<MeshState>(meshStates.Values).ToArray(), capturedData = result
                };
                Need(captured.MatchesIdentityAndPoseOnly(cancellation, out reason), reason);
                data = result;
                privateWitness = captured;
                reason = "Copied presentation observation; native text/GPU/freshness activation gates remain";
                return true;
            }
            catch (OperationCanceledException)
            {
                reason = "Presentation capture cancelled";
                return false;
            }
            catch (Exception exception)
            {
                reason = "Presentation UNBOUND: " + exception.Message;
                return false;
            }
        }

        private static MeshState ScanMesh(Mesh mesh, List<Vector3> vertices, List<int> indices,
            CancellationToken cancellation, ref int totalVertices, ref long totalIndices)
        {
            cancellation.ThrowIfCancellationRequested();
            int vertexCount = mesh.vertexCount;
            int submeshCount = mesh.subMeshCount;
            Need(mesh.isReadable && vertexCount > 0 && vertexCount <= MaximumVertices - totalVertices
                && submeshCount > 0 && submeshCount <= MaximumSubmeshes, "Unreadable/empty/oversized source mesh");
            long meshIndices = 0;
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                Need(mesh.GetTopology(submesh) == MeshTopology.Triangles, "Unsupported mesh primitive topology");
                meshIndices += mesh.GetIndexCount(submesh);
                Need(meshIndices <= MaximumTriangleIndices - totalIndices, "Triangle index cap");
            }
            Need(meshIndices > 0 && meshIndices % 3 == 0, "Empty/invalid triangle stream");
            // Native list-fill is synchronous: cancellation cannot interrupt the bounded
            // getter itself. Reused buffers limit extra arrays, but still allocate/grow.
            vertices.Clear();
            mesh.GetVertices(vertices);
            Need(vertices.Count == vertexCount && mesh.vertexCount == vertexCount, "Mesh changed during vertex read");
            OccupiedBounds bounds = default;
            bool first = true;
            using var signature = new PresentationSignatureWriter();
            signature.WriteInt(vertexCount);
            signature.WriteInt(submeshCount);
            for (int index = 0; index < vertices.Count; index++)
            {
                if (index % CancellationStride == 0) cancellation.ThrowIfCancellationRequested();
                Vector3 value = vertices[index];
                Need(Finite(value), "Nonfinite actual mesh vertex");
                Point3 point = Point(value);
                var singleton = new OccupiedBounds(point, point);
                bounds = first ? singleton : EnvelopeMath.Union(bounds, singleton);
                first = false;
                signature.WriteVector(value);
            }
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                cancellation.ThrowIfCancellationRequested();
                indices.Clear();
                mesh.GetIndices(indices, submesh, true);
                Need(indices.Count == mesh.GetIndexCount(submesh), "Mesh changed during index read");
                signature.WriteInt(indices.Count);
                for (int index = 0; index < indices.Count; index++)
                {
                    if (index % CancellationStride == 0) cancellation.ThrowIfCancellationRequested();
                    int vertex = indices[index];
                    Need(vertex >= 0 && vertex < vertexCount, "Invalid actual triangle index");
                    signature.WriteInt(vertex);
                }
            }
            totalVertices += vertexCount;
            totalIndices += meshIndices;
            return new MeshState
            {
                Mesh = mesh, VertexBounds = bounds, VertexCount = vertexCount,
                SubmeshCount = submeshCount, Indices = meshIndices, Digest = signature.Finish()
            };
        }

        /// <summary>
        /// Diagnostic only, using the shader name already read during material capture.
        /// No new native getters/identity grant. Clip unexpected names to bound retained
        /// first-cause text; unsupported profiles remain UNBOUND. Main-thread caller.
        /// </summary>
        private static string DescribeUnsupportedShaderGeometry(string capturedShaderName)
        {
            const int MaximumCapturedShaderNameCharacters = 128;
            string boundedName = capturedShaderName ?? "<null>";
            if (boundedName.Length > MaximumCapturedShaderNameCharacters)
                boundedName = boundedName.Substring(0, MaximumCapturedShaderNameCharacters) + "[truncated]";
            return "Unsupported shader geometry profile [capturedShaderName=" + boundedName + "]";
        }

        private static void CaptureMaterials(RendererState state, PresentationSignatureWriter signature)
        {
            // sharedMaterials returns an owned array copy; no .material instantiation.
            // Count is observable only AFTER that getter. Producer/native budget remains.
            state.Materials = state.Renderer.sharedMaterials;
            Need(state.Materials.Length > 0 && state.Materials.Length <= MaximumMaterialsPerRenderer,
                "Missing/oversized material binding");
            state.Shaders = new Shader[state.Materials.Length];
            signature.WriteInt(state.Materials.Length);
            for (int index = 0; index < state.Materials.Length; index++)
            {
                Material material = state.Materials[index];
                Need(material && material.shader, "Dead actual borrowed material/shader");
                Shader shader = material.shader;
                // Geometry-format restriction, NOT ownership authentication. Actual shader
                // implementation/GPU displacement agreement is a native acceptance gate.
                string shaderName = shader.name;
                // Exact installed Complex Lit source shares LitPassVertex and the
                // literal object-position path with Lit. This is a geometry-format
                // compatibility profile, not shader identity/native draw acceptance.
                bool supportedShaderGeometry = shaderName == "Universal Render Pipeline/Lit"
                    || shaderName == "Standard" || shaderName == "RoadRush/WorldText"
                    || shaderName == "Universal Render Pipeline/Complex Lit";
                Need(supportedShaderGeometry, supportedShaderGeometry ? null
                    : DescribeUnsupportedShaderGeometry(shaderName));
                state.Shaders[index] = shader;
                signature.WriteOpaqueIdentity(material);
                signature.WriteOpaqueIdentity(shader);
            }
        }

        private static void CaptureText(RendererState state, TextMesh text, Affine3 textToWorld, Affine3 worldToRoot,
            PresentationSignatureWriter signature, ref OccupiedBounds rootBounds, ref bool hasBounds,
            ref OccupiedBounds directWorldBounds, ref bool hasWorldBounds)
        {
            Need(text.font && !string.IsNullOrEmpty(text.text) && text.text.Length <= MaximumTextCharacters,
                "Missing font/empty/oversized native text, no default-bounds fallback");
            Need(text.fontSize > 0 && text.fontSize <= MaximumFontSizePixels
                && Finite(text.characterSize) && text.characterSize > 0 && text.characterSize <= MaximumTextMetric
                && Finite(text.lineSpacing) && text.lineSpacing > 0 && text.lineSpacing <= MaximumTextMetric
                && Finite(text.offsetZ) && Math.Abs(text.offsetZ) <= MaximumTextMetric
                && Finite(text.tabSize) && text.tabSize > 0 && text.tabSize <= MaximumTextMetric,
                "Unsupported native text metrics");
            Bounds localBounds = state.Renderer.localBounds;
            Vector3 centre = localBounds.center;
            Vector3 extents = localBounds.extents;
            int positiveAxes = (extents.x > 0 ? 1 : 0) + (extents.y > 0 ? 1 : 0) + (extents.z > 0 ? 1 : 0);
            Need(Finite(centre) && Finite(extents) && positiveAxes >= 2,
                "Native text has empty/default/nonfinite bounds");
            Need(PresentationGeometryMath.TryTransformOriginalBoxStages(Point(centre), Point(extents),
                textToWorld, worldToRoot, out OccupiedBounds textWorldBounds, out OccupiedBounds textBounds),
                "Conditioned original-box native text bounds refused");
            AddBounds(ref rootBounds, ref hasBounds, textBounds);
            AddBounds(ref directWorldBounds, ref hasWorldBounds, textWorldBounds);
            state.Text = text;
            state.Font = text.font;
            signature.WriteOpaqueIdentity(text);
            signature.WriteOpaqueIdentity(text.font);
            signature.WriteInt(text.text.Length);
            foreach (char character in text.text) signature.WriteInt(character);
            signature.WriteInt(text.fontSize);
            signature.WriteInt((int)text.fontStyle);
            signature.WriteInt((int)text.anchor);
            signature.WriteInt((int)text.alignment);
            signature.WriteInt(text.richText ? 1 : 0);
            signature.WriteFloat(text.characterSize);
            signature.WriteFloat(text.lineSpacing);
            signature.WriteFloat(text.offsetZ);
            signature.WriteFloat(text.tabSize);
            signature.WriteVector(centre);
            signature.WriteVector(extents);
            // Font atlas rebuild/renderer refresh may change native glyph geometry.
            // Same-reference font alone is NOT enough; full recapture + actual native
            // refresh/render tests are required before this evidence can be activated.
            Material fontMaterial = text.font.material;
            signature.WriteOpaqueIdentity(fontMaterial);
            signature.WriteOpaqueIdentity(fontMaterial ? fontMaterial.mainTexture : null);
        }

        /// <summary>
        /// EXPENSIVE complete content+pose re-capture for the actual producer's commit
        /// boundary. May allocate/scan every mesh and call DeformationExtent again.
        /// Not a logical-query predicate or permanent capability. It compares actual
        /// private native references as well as content/pose signature; no hash-only
        /// source/actor grant. Cancellation/unknown returns false, never stale success.
        /// Native-reported text refresh/GPU bounds are still NOT validated by this call.
        /// </summary>
        internal bool ValidateFullCurrent(CancellationToken cancellation, out string reason)
        {
            reason = "Presentation witness disposed/unavailable";
            if (disposed || !root || !visual) return false;
            if (!TryCapture(root, visual, cancellation, out VehiclePresentationData fresh,
                out VehiclePresentationCapture freshWitness, out reason)) return false;
            using (freshWitness)
            {
                bool same = SameNativeReferences(freshWitness)
                    && string.Equals(capturedData.ContentAndPoseSignature, fresh.ContentAndPoseSignature,
                        StringComparison.Ordinal);
                if (!same) reason = "Actual presentation content/pose/reference changed";
                return same;
            }
        }

        /// <summary>
        /// Reference/frame-only staging consistency check. Does NOT establish mesh
        /// content or text/font freshness. Owner must not substitute it for full
        /// validation unless separately enforcing all authorized immutable mesh epochs.
        /// </summary>
        private bool MatchesIdentityAndPoseOnly(CancellationToken cancellation, out string reason)
        {
            reason = "Presentation reference/pose changed";
            if (disposed || !root || !visual || !root.gameObject.activeInHierarchy
                || visual.Model != capturedData.Model || visual.DamageRevision != capturedData.DamageRevision
                || !root.localToWorldMatrix.Equals(capturedRootFrame)) return false;
            foreach (NodeState node in nodes)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!node.Node || !ReferenceEquals(node.Node.parent, node.Parent)
                    || node.Node.childCount != node.ChildCount || !node.Node.localToWorldMatrix.Equals(node.LocalToWorld)) return false;
            }
            foreach (RendererState state in renderers)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!state.Renderer || state.Renderer.enabled != state.Enabled
                    || state.Renderer.gameObject.activeInHierarchy != state.Active
                    || !state.Renderer.localToWorldMatrix.Equals(state.LocalToWorld)) return false;
                if (state.Mesh && (!state.Filter || !ReferenceEquals(state.Filter.sharedMesh, state.Mesh))) return false;
                if (state.Text && (!ReferenceEquals(state.Text.font, state.Font))) return false;
            }
            foreach (MeshState mesh in meshes)
                if (!mesh.Mesh || mesh.Mesh.vertexCount != mesh.VertexCount || mesh.Mesh.subMeshCount != mesh.SubmeshCount) return false;
            reason = "Reference/pose only; content freshness is NOT established here";
            return true;
        }

        private bool SameNativeReferences(VehiclePresentationCapture fresh)
        {
            if (!ReferenceEquals(root, fresh.root) || !ReferenceEquals(visual, fresh.visual)
                || nodes.Length != fresh.nodes.Length || renderers.Length != fresh.renderers.Length
                || meshes.Length != fresh.meshes.Length) return false;
            for (int index = 0; index < nodes.Length; index++)
                if (!ReferenceEquals(nodes[index].Node, fresh.nodes[index].Node)) return false;
            for (int index = 0; index < renderers.Length; index++)
            {
                RendererState before = renderers[index], after = fresh.renderers[index];
                if (!ReferenceEquals(before.Renderer, after.Renderer) || !ReferenceEquals(before.Filter, after.Filter)
                    || !ReferenceEquals(before.Mesh, after.Mesh) || !ReferenceEquals(before.Text, after.Text)
                    || !ReferenceEquals(before.Font, after.Font) || before.Materials.Length != after.Materials.Length) return false;
                for (int material = 0; material < before.Materials.Length; material++)
                    if (!ReferenceEquals(before.Materials[material], after.Materials[material])
                        || !ReferenceEquals(before.Shaders[material], after.Shaders[material])) return false;
            }
            return true;
        }

        private static void AddBounds(ref OccupiedBounds total, ref bool hasBounds, OccupiedBounds next)
        {
            Need(next.Valid, "Nonfinite presentation bounds");
            total = hasBounds ? EnvelopeMath.Union(total, next) : next;
            hasBounds = true;
        }
        private static Point3 Point(Vector3 value) => new Point3(value.x, value.y, value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool TryAffine(Matrix4x4 value, out Affine3 affine)
        {
            affine = new Affine3(new Point3(value.m00, value.m10, value.m20),
                new Point3(value.m01, value.m11, value.m21), new Point3(value.m02, value.m12, value.m22),
                new Point3(value.m03, value.m13, value.m23));
            return affine.Finite && value.m30 == 0 && value.m31 == 0 && value.m32 == 0 && value.m33 == 1;
        }
        private static void Need(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }

        /// <summary>
        /// Releases private borrowed references and owned metadata only. Never destroys
        /// meshes/materials, changes renderers, invokes vehicle behavior or resets budgets.
        /// Root producer owns this call when replacing/retiring its private witness.
        /// </summary>
        public void Dispose()
        {
            disposed = true;
            root = null;
            visual = null;
            nodes = null;
            renderers = null;
            meshes = null;
            capturedData = null;
        }
    }
}
