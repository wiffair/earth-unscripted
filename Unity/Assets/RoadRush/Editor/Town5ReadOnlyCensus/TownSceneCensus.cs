using System;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadRush.VisualDiagnosticsProposal
{
    /// <summary>
    /// READ-ONLY native fixture census proposal. Never creates GOs, changes renderer/
    /// material/light/Owner3 state or issues semantic authority. Supplied physical/town
    /// roots label actual ancestry ONLY; this is not a complete actor registry. Captures
    /// actual renderer bounds/materials and literal mesh projected-area rankings,
    /// including residual normal-regression roots or oversized access paving.
    /// Main-thread only, fresh explicit Root-owned output, current source/model read
    /// serialization required. Native enumeration/getters are not interruptible and
    /// postchecks do not establish hard underlying engine cost. NOT AUTO-RUN/ADMITTED.
    /// </summary>
    public static class TownSceneCensus
    {
        private const string OutputPrefix = "/private/tmp/rr019-town-census-";
        private const int MaximumRoots = 64;
        private const int MaximumRenderers = 8192;
        private const int MaximumLights = 32;
        private const int MaximumMaterialsPerRenderer = 16;
        private const int MaximumMeshVertices = 500000;
        private const int MaximumSubmeshes = 64;
        private const long MaximumTotalVertices = 2000000;
        private const long MaximumTotalIndices = 6000000;
        private const int CancellationStride = 256;
        private const int WidthPixels = 1920;
        private const int HeightPixels = 1200;

        /// <summary>
        /// Must run at the exact overview pose/aspect used for the capture; snapshots
        /// actual projection/view matrices without changing camera state. Metadata is
        /// TSV with roundtrip numeric fields. No meshes/materials/binaries are written.
        /// Unreadable/unsupported/over-budget geometry rows are explicit, not truncated
        /// successes; no native-visible-pixel coverage is claimed by summed triangle area.
        /// </summary>
        public static void Record(GameObject actualPhysicalRoot, GameObject actualTownRoot,
            Camera actualOverviewCamera, Func<string, string> actualPacketRole,
            string ownedOutput, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string output = Path.GetFullPath(ownedOutput);
            if (!actualPhysicalRoot || !actualTownRoot || !actualOverviewCamera || actualPacketRole == null
                || !output.StartsWith(OutputPrefix, StringComparison.Ordinal)
                || Directory.Exists(output) || File.Exists(output))
                throw new ArgumentException("Explicit actual roots/camera/roles and fresh owned output required");
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            if (roots.Length > MaximumRoots) throw new InvalidDataException("Unexpected scene root cap");
            Matrix4x4 view = actualOverviewCamera.worldToCameraMatrix;
            Matrix4x4 projection = actualOverviewCamera.projectionMatrix;
            for (int index = 0; index < 16; index++)
                if (!Finite(view[index]) || !Finite(projection[index])) throw new InvalidDataException("Nonfinite camera matrix");
            if (Math.Abs(actualOverviewCamera.aspect - (double)WidthPixels / HeightPixels) > 1e-6)
                throw new InvalidDataException("Set the intended1920x1200 overview aspect BEFORE this read-only census");
            float nearClipMetres = actualOverviewCamera.nearClipPlane, farClipMetres = actualOverviewCamera.farClipPlane;
            var clipA = new PixelPoint[ProjectedTriangleArea.ClipBufferPoints];
            var clipB = new PixelPoint[ProjectedTriangleArea.ClipBufferPoints];
            int rendererCount = 0, lightCount = 0, incompleteGeometryRows = 0;
            long totalVertices = 0, totalIndices = 0;
            Directory.CreateDirectory(output);
            using var geometry = new StreamWriter(Path.Combine(output, "actual-scene-geometry.tsv"));
            using var materials = new StreamWriter(Path.Combine(output, "actual-scene-materials.tsv"));
            using var lights = new StreamWriter(Path.Combine(output, "actual-scene-lights.tsv"));
            geometry.WriteLine("path\tancestryBucket\trole\trendererId\tmeshId\tenabled\tactive\tcameraLayerIncluded\tnativeMinX\tnativeMinY\tnativeMinZ\tnativeMaxX\tnativeMaxY\tnativeMaxZ\tvertexMinX\tvertexMinY\tvertexMinZ\tvertexMaxX\tvertexMaxY\tvertexMaxZ\tvertices\tindices\tclippedSummedPixelsSquared\tdepthPlaneSkippedTriangles\tgeometryStatus");
            materials.WriteLine("path\tmaterialIndex\tmaterial\tshader\tqueue\tcolourProperty\tbaseR\tbaseG\tbaseB\tpropertyBlock\treceiveShadows\tcastShadows\tlayer");
            lights.WriteLine("path\ttype\tintensity\tR\tG\tB\tACTUALshadows\tenabled\tactive\tcullingMask\tshadowBias\tshadowNormalBias");
            foreach (GameObject sceneRoot in roots)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (Light light in sceneRoot.GetComponentsInChildren<Light>(true))
                {
                    if (++lightCount > MaximumLights) throw new InvalidDataException("Unexpected light cap");
                    lights.WriteLine(string.Join("\t", PathOf(light.transform), light.type, Number(light.intensity),
                        Number(light.color.r), Number(light.color.g), Number(light.color.b), light.shadows,
                        light.enabled, light.gameObject.activeInHierarchy, light.cullingMask, Number(light.shadowBias), Number(light.shadowNormalBias)));
                }
                foreach (Renderer renderer in sceneRoot.GetComponentsInChildren<Renderer>(true))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++rendererCount > MaximumRenderers) throw new InvalidDataException("Unexpected renderer cap");
                    string path = PathOf(renderer.transform);
                    string ancestry = IsWithin(renderer.transform, actualPhysicalRoot.transform) ? "PHYSICAL_SUPPLIED_ROOT"
                        : IsWithin(renderer.transform, actualTownRoot.transform) ? "TOWN_SUPPLIED_ROOT" : "OTHER_SCENE_ROOT";
                    string role = "NOT_PHYSICAL_PACKET";
                    if (ancestry == "PHYSICAL_SUPPLIED_ROOT")
                    {
                        try { role = actualPacketRole(renderer.name); }
                        catch (ArgumentException) { role = "UNRECOGNISED_CHILD_OF_PHYSICAL_ROOT"; }
                    }
                    Material[] bindings = renderer.sharedMaterials;
                    if (bindings.Length > MaximumMaterialsPerRenderer) throw new InvalidDataException("Material cap");
                    for (int index = 0; index < bindings.Length; index++)
                    {
                        Material material = bindings[index];
                        string colourProperty = material && material.HasProperty("_BaseColor") ? "_BaseColor"
                            : material && material.HasProperty("_Color") ? "_Color" : "NONE";
                        Color colour = colourProperty != "NONE" ? material.GetColor(colourProperty) : Color.clear;
                        materials.WriteLine(string.Join("\t", path, index, Text(material ? material.name : "MISSING"),
                            Text(material && material.shader ? material.shader.name : "MISSING"), material ? material.renderQueue : -1,
                            colourProperty, Number(colour.r), Number(colour.g), Number(colour.b), renderer.HasPropertyBlock(),
                            renderer.receiveShadows, renderer.shadowCastingMode, renderer.gameObject.layer));
                    }
                    Bounds native = renderer.bounds;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter ? filter.sharedMesh : null;
                    string status = "NOT_READABLE_LITERAL_MESH";
                    double minX = double.NaN, minY = double.NaN, minZ = double.NaN;
                    double maxX = double.NaN, maxY = double.NaN, maxZ = double.NaN;
                    long declaredIndices = 0;
                    int verticesCount = mesh ? mesh.vertexCount : 0;
                    double summedAreaPixels = 0;
                    int nearSkipped = 0;
                    if (mesh && mesh.isReadable && verticesCount > 0 && verticesCount <= MaximumMeshVertices
                        && mesh.subMeshCount <= MaximumSubmeshes)
                    {
                        bool topology = true;
                        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        {
                            topology &= mesh.GetTopology(submesh) == MeshTopology.Triangles;
                            declaredIndices += mesh.GetIndexCount(submesh);
                        }
                        if (!topology || declaredIndices % 3 != 0) status = "UNSUPPORTED_TOPOLOGY";
                        else if (totalVertices + verticesCount > MaximumTotalVertices || totalIndices + declaredIndices > MaximumTotalIndices)
                            status = "GEOMETRY_BUDGET_REFUSED";
                        else
                        {
                            totalVertices += verticesCount; totalIndices += declaredIndices;
                            Vector3[] vertices = mesh.vertices;
                            int[] indices = mesh.triangles;
                            if (vertices.Length != verticesCount || indices.LongLength != declaredIndices)
                                throw new InvalidDataException("Mesh changed while diagnostic read");
                            Matrix4x4 frame = renderer.localToWorldMatrix;
                            var projected = new PixelPoint[vertices.Length];
                            var front = new bool[vertices.Length];
                            minX = minY = minZ = double.PositiveInfinity;
                            maxX = maxY = maxZ = double.NegativeInfinity;
                            for (int index = 0; index < vertices.Length; index++)
                            {
                                if (index % CancellationStride == 0) cancellation.ThrowIfCancellationRequested();
                                Vector3 local = vertices[index];
                                double x = frame.m00*(double)local.x + frame.m01*(double)local.y + frame.m02*(double)local.z + frame.m03;
                                double y = frame.m10*(double)local.x + frame.m11*(double)local.y + frame.m12*(double)local.z + frame.m13;
                                double z = frame.m20*(double)local.x + frame.m21*(double)local.y + frame.m22*(double)local.z + frame.m23;
                                if (!Finite(x) || !Finite(y) || !Finite(z)) throw new InvalidDataException("Nonfinite literal vertex");
                                minX = Math.Min(minX,x); minY = Math.Min(minY,y); minZ = Math.Min(minZ,z);
                                maxX = Math.Max(maxX,x); maxY = Math.Max(maxY,y); maxZ = Math.Max(maxZ,z);
                                double viewX=view.m00*x+view.m01*y+view.m02*z+view.m03;
                                double viewY=view.m10*x+view.m11*y+view.m12*z+view.m13;
                                double viewZ=view.m20*x+view.m21*y+view.m22*z+view.m23;
                                double clipW=projection.m30*viewX+projection.m31*viewY+projection.m32*viewZ+projection.m33;
                                front[index]=clipW>0 && -viewZ>=nearClipMetres && -viewZ<=farClipMetres;
                                if (!front[index]) continue;
                                double clipX=projection.m00*viewX+projection.m01*viewY+projection.m02*viewZ+projection.m03;
                                double clipY=projection.m10*viewX+projection.m11*viewY+projection.m12*viewZ+projection.m13;
                                projected[index]=new PixelPoint((clipX/clipW+1)*WidthPixels*.5,(1-clipY/clipW)*HeightPixels*.5);
                            }
                            for (int index = 0; index < indices.Length; index += 3)
                            {
                                if (index % CancellationStride == 0) cancellation.ThrowIfCancellationRequested();
                                int first=indices[index],second=indices[index+1],third=indices[index+2];
                                if (first<0 || second<0 || third<0 || first>=vertices.Length || second>=vertices.Length || third>=vertices.Length)
                                    throw new InvalidDataException("Invalid literal triangle");
                                if (!front[first] || !front[second] || !front[third]) { nearSkipped++; continue; }
                                summedAreaPixels += ProjectedTriangleArea.Clipped(projected[first],projected[second],projected[third],
                                    WidthPixels,HeightPixels,clipA,clipB);
                            }
                            status = nearSkipped == 0 ? "LITERAL_PROJECTED_AREA_NOT_VISIBLE_COVERAGE" : "PARTIAL_OR_OUTSIDE_DEPTH_PLANES_EXCLUDED";
                        }
                    }
                    if (status != "LITERAL_PROJECTED_AREA_NOT_VISIBLE_COVERAGE") incompleteGeometryRows++;
                    geometry.WriteLine(string.Join("\t", path, ancestry, Text(role), Token(renderer), mesh ? Token(mesh) : "NONE",
                        renderer.enabled, renderer.gameObject.activeInHierarchy,
                        (actualOverviewCamera.cullingMask & (1<<renderer.gameObject.layer))!=0,
                        Number(native.min.x),Number(native.min.y),Number(native.min.z),Number(native.max.x),Number(native.max.y),Number(native.max.z),
                        Number(minX),Number(minY),Number(minZ),Number(maxX),Number(maxY),Number(maxZ),verticesCount,declaredIndices,
                        Number(summedAreaPixels),nearSkipped,status));
                }
            }
            geometry.Flush(); materials.Flush(); lights.Flush();
            File.WriteAllText(Path.Combine(output,"scope.txt"),
                "READ_ONLY_DIAGNOSTIC_NOT_AUTHORITY\nManual/instanced draws not enumerated by sceneRenderer census.\n"
                + "Summed projected triangle area includes occluded/backfacing overlap, not visible pixels.\n"
                + "Renderers="+rendererCount+";IncompleteGeometryRows="+incompleteGeometryRows+";Vertices="+totalVertices+";Indices="+totalIndices);
        }

        private static bool IsWithin(Transform item, Transform ancestor) => item == ancestor || item.IsChildOf(ancestor);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Text(string value) => (value??"").Replace("\t"," ").Replace("\r"," ").Replace("\n"," ");
        private static string Token(UnityEngine.Object value) => EntityId.ToULong(value.GetEntityId()).ToString(CultureInfo.InvariantCulture);
        private static string PathOf(Transform item)
        {
            string path = Text(item.name);
            int count=0;
            while (item.parent && ++count<1024) { item=item.parent; path=Text(item.name)+"/"+path; }
            if (count>=1024) throw new InvalidDataException("Unexpected hierarchy depth");
            return path;
        }
    }
}
