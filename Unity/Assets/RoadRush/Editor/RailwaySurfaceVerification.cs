using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class RailwaySurfaceVerification
    {
        public static void Run()
        {
            int checks=0;var roots=new List<GameObject>();void Check(bool value,string why){if(!value)throw new Exception("RAIL SURFACE FAIL "+why);checks++;Debug.Log("RAIL SURFACE PASS "+why);}
            bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
            var source=new[]{new Vector3(260,8,80),new Vector3(380,8,80)};
            var rail=new GeoRailway{id="rail-surface-fixture",kind="rail",width=3.8f,gauge=1.435f,bridge=true,points=source};
            GameObject Build(Vector2Int key)
            {var root=new GameObject("rail surface fixture "+key);roots.Add(root);foreach(var step in RailwayChunkGeometry.Build(root.transform,null,new[]{rail},key,320,(x,z)=>0,(p,r)=>false)){}return root;}
            Dictionary<string,Vector2> Seam(GameObject root)
            {
                var result=new Dictionary<string,Vector2>();foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=filter.sharedMesh;var v=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;string material=filter.GetComponent<Renderer>().sharedMaterial.name;
                    for(int i=0;i<v.Length;i++)if(Mathf.Abs(v[i].x-320)<.0001f&&Mathf.Abs(n[i].x)<.001f)
                    {string key=material+"|"+v[i].ToString("F4")+"|"+n[i].ToString("F4");if(result.TryGetValue(key,out var earlier))Check((earlier-uv[i]).sqrMagnitude<.0000001f,"same-plane duplicate vertices retain single UV phase");result[key]=uv[i];}
                }
                return result;
            }
            try
            {
                var structure=RailwayMaterials.Structure;var earth=RailwayMaterials.Earth;var ballast=RailwayMaterials.Ballast(false);var sleeper=RailwayMaterials.Sleepers;
                foreach(var binding in new[]{(structure,"BuildingAppearance/Stone",1/2.57f,.65f),(earth,"Textures/Grass",.20f,.4f),(ballast,"Textures/Rock",2.7f,.35f)})
                {
                    Check(binding.Item1.mainTexture==Resources.Load<Texture2D>(binding.Item2+"Colour"),"exact reviewed colour texture binding "+binding.Item1.name);
                    Check(binding.Item1.GetTexture("_BumpMap")==Resources.Load<Texture2D>(binding.Item2+"Normal")&&binding.Item1.IsKeywordEnabled("_NORMALMAP")&&Mathf.Abs(binding.Item1.GetFloat("_BumpScale")-binding.Item4)<.000001f,"exact reviewed normal texture and scale "+binding.Item1.name);
                    Check((binding.Item1.mainTextureScale-Vector2.one*binding.Item3).sqrMagnitude<.0000001f,"world-metre material repeat "+binding.Item1.name);
                }
                Check(structure.GetTexture("_MetallicGlossMap")==Resources.Load<Texture2D>("BuildingAppearance/StoneMetalSmoothness")&&structure.IsKeywordEnabled("_METALLICSPECGLOSSMAP"),"masonry uses existing reviewed packed roughness material");
                Check(sleeper.mainTexture&&sleeper.mainTexture.width==128&&sleeper.mainTexture.height==128&&!((Texture2D)sleeper.mainTexture).isReadable,"original bounded sleeper grain uploads once without retained CPU pixels");
                Check(ReferenceEquals(structure,RailwayMaterials.Structure)&&ReferenceEquals(earth,RailwayMaterials.Earth)&&ReferenceEquals(ballast,RailwayMaterials.Ballast(false))&&ReferenceEquals(sleeper,RailwayMaterials.Sleepers),"repeated getters share cached materials and original texture");
                var west=Build(Vector2Int.zero);var east=Build(new Vector2Int(1,0));var westMeshes=west.GetComponentsInChildren<MeshFilter>();var eastMeshes=east.GetComponentsInChildren<MeshFilter>();
                foreach(var root in new[]{west,east})
                {
                    var filters=root.GetComponentsInChildren<MeshFilter>();Check(filters.Length>0&&filters.Length<=8,"rail geometry remains bounded in existing batches "+root.name);
                    Check(root.GetComponentsInChildren<GeneratedMeshOwner>().Length==filters.Length&&root.GetComponent<RailwayChunkMeshOwner>(),"each generated mesh retains explicit chunk ownership "+root.name);
                    Check(root.GetComponentsInChildren<MeshCollider>().Length==2,"only closed deck and barrier collision batches present "+root.name);
                    bool horizontal=false,vertical=false;
                    foreach(var filter in filters)
                    {
                        var mesh=filter.sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var tangents=mesh.tangents;
                        Check(vertices.Length==uv.Length&&vertices.Length==normals.Length&&vertices.Length==tangents.Length&&uv.All(p=>Finite(p.x)&&Finite(p.y)),"finite complete UV/normal/tangent arrays "+mesh.name+root.name);
                        Check(tangents.Select((t,i)=>Finite(t.x)&&Finite(t.y)&&Finite(t.z)&&Finite(t.w)&&Mathf.Abs(t.w)==1&&new Vector3(t.x,t.y,t.z).sqrMagnitude>.9f&&Mathf.Abs(Vector3.Dot(new Vector3(t.x,t.y,t.z),normals[i]))<.002f).All(b=>b),"normal maps have usable orthogonal tangent basis "+mesh.name+root.name);
                        var mat=filter.GetComponent<Renderer>().sharedMaterial;Check(mat&&new[]{RailwayMaterials.Rail(false),sleeper,ballast,structure,RailwayMaterials.Barrier}.Any(m=>ReferenceEquals(m,mat)),"renderers use shared production materials "+mesh.name);
                        if(!ReferenceEquals(mat,structure))continue;
                        for(int i=0;i<vertices.Length;i++)
                        {
                            if(normals[i].y>.99f){horizontal=true;Check((uv[i]-new Vector2(vertices[i].x,vertices[i].z)).sqrMagnitude<.000001f,"deck top texture follows world XZ metric plane");}
                            else if(Mathf.Abs(normals[i].y)<.01f){vertical=true;Check(Mathf.Abs(uv[i].y-vertices[i].y)<.00001f,"masonry courses remain horizontal at world-height phase");}
                        }
                    }
                    Check(horizontal&&vertical,"fixture exercises top and vertical masonry projection "+root.name);
                }
                var a=Seam(west);var b=Seam(east);var shared=a.Keys.Intersect(b.Keys).ToArray();Check(shared.Length>=8,"actual chunk cuts expose matching same-plane vertices");Check(shared.All(key=>(a[key]-b[key]).sqrMagnitude<.0000001f),"global UV phase is exactly continuous across chunk cuts");
                Physics.SyncTransforms();foreach(float x in new[]{280f,340f})
                {
                    var root=x<320?west:east;var deck=root.GetComponentsInChildren<MeshCollider>().Single(c=>c.name.StartsWith("Railway closed bridge deck",StringComparison.Ordinal));Check(deck.Raycast(new Ray(new Vector3(x,1,80),Vector3.up),out var under,20)&&Mathf.Abs(under.point.y-6.8f)<.002f,"surface changes preserve actual underside winding and 1.20m drop");Check(deck.Raycast(new Ray(new Vector3(x,12,80),Vector3.down),out var top,20)&&Mathf.Abs(top.point.y-7.55f)<.002f,"surface changes preserve actual deck top and .75m depth");
                }
                var saved=westMeshes.Select(f=>(v:f.sharedMesh.vertices,t:f.sharedMesh.triangles,n:f.sharedMesh.normals,uv:f.sharedMesh.uv)).ToArray();var oldMeshes=westMeshes.Select(f=>f.sharedMesh).ToArray();Geometry.ReleaseMeshes(west);UnityEngine.Object.DestroyImmediate(west);Check(oldMeshes.All(m=>!m)&&structure&&sleeper&&structure.mainTexture&&sleeper.mainTexture,"chunk disposal destroys owned meshes and preserves shared materials/textures");
                var rebuilt=Build(Vector2Int.zero).GetComponentsInChildren<MeshFilter>();Check(saved.Length==rebuilt.Length&&saved.Select((old,i)=>old.v.SequenceEqual(rebuilt[i].sharedMesh.vertices)&&old.t.SequenceEqual(rebuilt[i].sharedMesh.triangles)&&old.n.SequenceEqual(rebuilt[i].sharedMesh.normals)&&old.uv.SequenceEqual(rebuilt[i].sharedMesh.uv)).All(x=>x),"retire/rebuild retains exact geometry and global texture phase");
                Check(rail.points==source&&source[0]==new Vector3(260,8,80)&&source[1]==new Vector3(380,8,80),"visual surface path leaves authoritative railway profile untouched");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/railway-surface.txt","PASS "+checks+" shared material/resource/phase/tangent/collider/ownership checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{foreach(var root in roots)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}}
        }
    }
}
