using System.Collections.Generic;
using System.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Owned meshes/materials for a verified circuit pack. Geometry is
    /// batched in 128m cells and two visual LODs; collision lives independently
    /// of renderer LOD so an invisible distant rail never loses its collision.</summary>
    // Explicit initialization only. Editor-owned fixtures also need OnDestroy
    // to release native meshes/materials; a serialized idle owner does no work.
    [ExecuteAlways]
    public sealed partial class CircuitShowcaseScenery : MonoBehaviour
    {
        const float CellMetres = 128;
        readonly List<Object> owned = new List<Object>();
        const int AsphaltTexturePixels = 512;
        /// <summary>Original neutral cement grain, independent of the house
        /// plaster library. A circuit apron should read as concrete, not soil.</summary>
        void ApplyConcrete(Material material)
        {
            const int pixelsPerSide = 128;
            var texture = new Texture2D(pixelsPerSide, pixelsPerSide, TextureFormat.RGB24, true)
                { name = "Original circuit concrete", wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 16, filterMode = FilterMode.Trilinear };
            owned.Add(texture);
            var pixels = new Color[pixelsPerSide * pixelsPerSide];
            var random = new System.Random(2179);
            for (int i = 0; i < pixels.Length; i++)
            {
                float grain = .72f + (float)(random.NextDouble() - .5) * .045f;
                pixels[i] = new Color(grain, grain, grain);
            }
            texture.SetPixels(pixels); texture.Apply(true, true);
            material.mainTexture = texture;
            material.color = new Color(.65f, .66f, .66f);
        }
        /// <summary>Original seamless aggregate texture: a512px tile covers2m
        /// with the existing metric road UVs. No regularly repeating crack is
        /// used, since it made the circuit appear to be concrete paving slabs.</summary>
        void ApplyAsphalt(Material material)
        {
            var texture=new Texture2D(AsphaltTexturePixels,AsphaltTexturePixels,TextureFormat.RGB24,true)
                {name="Original circuit asphalt aggregate",wrapMode=TextureWrapMode.Repeat,anisoLevel=16,filterMode=FilterMode.Trilinear};
            owned.Add(texture);var pixels=new Color[AsphaltTexturePixels*AsphaltTexturePixels];var random=new System.Random(20261109);
            for(int i=0;i<pixels.Length;i++)
            {
                float grain=.25f+(float)(random.NextDouble()-.5)*.075f;
                if(random.NextDouble()<.025)grain+=.06f;
                pixels[i]=new Color(grain,grain*1.015f,grain*1.025f);
            }
            texture.SetPixels(pixels);texture.Apply(true,true);
            material.mainTexture=texture;material.mainTextureScale=Vector2.one*4;
            material.color=new Color(.75f,.75f,.75f);material.SetFloat("_Smoothness",.15f);
            material.DisableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",0);
        }
        public static Material CreateAsphalt(Transform parent)
        {
            var root=new GameObject("Owned circuit asphalt");root.transform.SetParent(parent,false);
            var owner=root.AddComponent<CircuitShowcaseScenery>();
            var material=CircuitOwnedMaterials.Create("Showcase driving asphalt",Color.white,0,.15f);
            owner.owned.Add(material);owner.ApplyAsphalt(material);return material;
        }
        sealed class Batch
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<int> T = new List<int>();
        }
        static readonly int[][] Faces = { new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,4,7,3},new[]{1,2,6,5},new[]{3,7,6,2},new[]{0,1,5,4} };
        static readonly Vector3[] UnitBox = new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)};
        static void Append(CircuitSceneryPlan.Part p, Batch b)
        {
            int start = b.V.Count;
            if (p.Vertices != null)
            {
                // Back-facing sheets have separate vertices so reversed faces
                // cannot cancel the front normals to zero during recalculation.
                foreach(int n in p.Triangles){b.T.Add(b.V.Count);b.V.Add(p.Vertices[n]);} return;
            }
            var rotation = Quaternion.Euler(0,p.Yaw,0);
            foreach (var face in Faces)
            {
                start = b.V.Count;
                foreach (int n in face) b.V.Add(p.Position + rotation * Vector3.Scale(UnitBox[n],p.Size));
                // Avoid six temporary index arrays per repeated seat/box.
                b.T.Add(start);b.T.Add(start+1);b.T.Add(start+2);
                b.T.Add(start);b.T.Add(start+2);b.T.Add(start+3);
            }
        }
        Mesh MakeMesh(string name, Batch b)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            owned.Add(mesh); mesh.SetVertices(b.V); mesh.SetTriangles(b.T,0);
            // Planar metric UVs for the existing licensed surface library; no
            // pack-supplied shader, texture URL or material code is executed.
            mesh.SetUVs(0,b.V.Select(v=>new Vector2(v.x*.25f+v.y*.25f,v.z*.25f+v.y*.25f)).ToList());
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
        public static CircuitShowcaseScenery Install(Transform parent, CircuitSceneryPlan plan, bool incremental = false)
        {
            var root = new GameObject("Verified circuit showcase"); root.transform.SetParent(parent,false);
            var owner = root.AddComponent<CircuitShowcaseScenery>();
            if(incremental)owner.InitializeStreaming(plan);else owner.Build(plan);
            return owner;
        }
        void Build(CircuitSceneryPlan plan)
        {
            var colours = new[]{new Color(.53f,.54f,.52f),new Color(.43f,.47f,.5f),new Color(.65f,.68f,.7f),new Color(.24f,.38f,.46f),new Color(.08f,.24f,.53f),new Color(.65f,.06f,.045f),new Color(.87f,.88f,.86f),new Color(.055f,.065f,.075f),new Color(.12f,.13f,.14f),new Color(.55f,.49f,.39f),new Color(.15f,.31f,.17f),new Color(.9f,.67f,.08f)};
            var materials = new Dictionary<string,Material>();
            for(int i=0;i<colours.Length;i++)
            {
                string key=CircuitSceneryPlan.Palette[i];
                var m=CircuitOwnedMaterials.Create("Showcase "+key,colours[i],key=="steel"||key=="roof"?.7f:key=="glass"?.15f:0,key=="glass"?.85f:key=="steel"?.38f:.23f);
                if(key=="asphalt")ApplyAsphalt(m);
                if(key=="concrete")ApplyConcrete(m);
                owned.Add(m); materials.Add(key,m);
            }
            var batches = new Dictionary<(Vector2Int cell,int lod,string material),Batch>();
            foreach(var p in plan.Parts)
            {
                var centre=p.Vertices==null?p.Position:p.Vertices[0];
                var cell=new Vector2Int(Mathf.FloorToInt(centre.x/CellMetres),Mathf.FloorToInt(centre.z/CellMetres));
                for(int level=0;level<2;level++)
                {
                    if(p.Lod>=0&&p.Lod!=level)continue;
                    var key=(cell,level,p.Material); if(!batches.TryGetValue(key,out var b)){b=new Batch();batches.Add(key,b);} Append(p,b);
                }
                if(p.Collision)
                {
                    var b=new Batch();Append(p,b);var g=new GameObject("Collision "+p.Name);g.transform.SetParent(transform,false);
                    g.AddComponent<MeshCollider>().sharedMesh=MakeMesh(p.Name+" collision",b);
                }
            }
            foreach(var cell in batches.Keys.Select(k=>k.cell).Distinct())
            {
                var root=new GameObject("Circuit art cell "+cell);root.transform.SetParent(transform,false);var lods=new List<LOD>();
                for(int level=0;level<2;level++)
                {
                    var renderers=new List<Renderer>();
                    foreach(var pair in batches.Where(p=>p.Key.cell==cell&&p.Key.lod==level))
                    {
                        var g=new GameObject("LOD"+level+" "+pair.Key.material);g.transform.SetParent(root.transform,false);
                        g.AddComponent<MeshFilter>().sharedMesh=MakeMesh(g.name,pair.Value);
                        var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=materials[pair.Key.material];renderers.Add(r);
                    }
                    lods.Add(new LOD(level==0?.07f:.001f,renderers.ToArray()));
                }
                var group=root.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();
            }
        }
        static void ReleaseObject(Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        void OnDestroy(){StopAllCoroutines();foreach(var value in owned)if(value)ReleaseObject(value);owned.Clear();}
    }
}
