using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.TerrainDraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.Editor
{
    // Explicit source-pinned diagnostic, never a normal world loader/admission.
    public static class LocalTerrainCellNativeVerification
    {
        sealed class Uploaded { public string Role; public MeshCollider Collider; public LocalTerrainMesh Packet; }
        static string Hash(byte[] bytes)=>World13DocumentCodec.Hash(bytes);
        static void Require(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
        static string Arg(string name){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);if(i<0||i+1>=a.Length)throw new ArgumentException(name);return a[i+1];}
        static double Number(JToken t){Require(t!=null&&(t.Type==JTokenType.Float||t.Type==JTokenType.Integer),"Numeric fixture field");double v=(double)t;Require(!double.IsNaN(v)&&!double.IsInfinity(v),"Finite fixture field");return v;}
        static bool Same(float a,float b)=>BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b);
        static string FileHash(string p)=>File.Exists(p)?Hash(File.ReadAllBytes(p)):"missing";
        public static void Run()=>RunCore(null,null,null);
        public static void RunPinnedFixture(string input,string pin,string output)=>RunCore(input,pin,output);
        static void RunCore(string explicitInput,string explicitPin,string explicitOutput)
        {
            int exit=1;string output=null;bool outputCreated=false;var report=new JObject();string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=FileHash(career);
            try
            {
                Require(!Application.isPlaying&&UnityEngine.Object.FindAnyObjectByType<GameSession>()==null,"Isolated editor fixture only");
                output=Path.GetFullPath(explicitOutput??Arg("-earthUnscriptedTerrainCellOutput"));Require(output.StartsWith("/private/tmp/",StringComparison.Ordinal)&&!Directory.Exists(output)&&!File.Exists(output),"Fresh outside output required");Directory.CreateDirectory(output);outputCreated=true;
                string input=Path.GetFullPath(explicitInput??Arg("-earthUnscriptedTerrainCellInput")),pin=explicitPin??Arg("-earthUnscriptedTerrainCellSHA");Require(new FileInfo(input).Length<=16*1024*1024,"Fixture byte budget");byte[] bytes=File.ReadAllBytes(input);Require(Hash(bytes)==pin,"Externally pinned fixture differs");
                JObject data;using(var reader=new JsonTextReader(new StringReader(new UTF8Encoding(false,true).GetString(bytes))){MaxDepth=32,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double}){data=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Require(!reader.Read(),"Trailing fixture content");}
                Require((string)data["format"]=="EarthUnscriptedLocalTerrainCell1"&&(int?)data["version"]==1,"Explicit local-cell format");
                var entries=data["packets"] as JArray;Require(entries!=null&&entries.Count>0&&entries.Count<=32,"Packet budget");
                var terrain=new List<LocalTerrainMesh>();var asphalt=new List<LocalTerrainMesh>();var roles=new Dictionary<string,string>(StringComparer.Ordinal);int totalVertices=0,totalIndices=0;
                foreach(var t in entries)
                {
                    string id=(string)t["id"],role=(string)t["role"];Require(role=="terrain"||role=="asphalt","Top-surface roles only");Require(!string.IsNullOrEmpty(id)&&!roles.ContainsKey(id),"Unique packet IDs");roles.Add(id,role);
                    var origin=t["origin"] as JArray;var xyz=t["xyz"] as JArray;var indices=t["indices"] as JArray;
                    Require(origin!=null&&origin.Count==3&&xyz!=null&&xyz.Count>=9&&xyz.Count%3==0&&indices!=null&&indices.Count>0&&indices.Count%3==0,"Local packet shape");
                    Require((totalVertices+=xyz.Count/3)<=250000&&(totalIndices+=indices.Count)<=1500000,"Complete fixture geometry budget");
                    var v=xyz.Select(n=>(float)Number(n)).ToArray();var ix=indices.Select(n=>{Require(n.Type==JTokenType.Integer,"Integer index");long k=(long)n;Require(k>=0&&k<v.Length/3,"Index bounds");return(int)k;}).ToArray();
                    var p=new LocalTerrainMesh(id,Number(origin[0]),Number(origin[1]),Number(origin[2]),v,ix);(role=="terrain"?terrain:asphalt).Add(p);
                }
                Require(terrain.Count>0&&asphalt.Count>0,"Real terrain and companion asphalt both required");
                string source=(string)data["sourceDigest"],seam=(string)data["seamDigest"];var limits=new TerrainSurfaceLimits{MaximumPackets=32,MaximumVertices=250000,MaximumTriangles=500000,MaximumReferences=2000000,MaximumCandidatesPerCell=100000};
                var ground=new TerrainTriangleSurface(terrain,source,seam,limits);var road=new TerrainTriangleSurface(asphalt,source,seam,limits);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                double ox=terrain[0].OriginX,oy=terrain[0].OriginY,oz=terrain[0].OriginZ;var uploaded=new List<Uploaded>();var bounds=new Bounds();bool first=true;
                foreach(var original in terrain.Concat(asphalt))
                {
                    var surface=roles[original.Id]=="terrain"?ground:road;var p=surface.CopyForRenderer(original.Id);
                    double dx=p.OriginX-ox,dy=p.OriginY-oy,dz=p.OriginZ-oz;Require((double)(float)dx==dx&&(double)(float)dy==dy&&(double)(float)dz==dz,"Render-origin translation must be exactly float-representable");
                    var v=new Vector3[p.XYZ.Length/3];for(int i=0;i<v.Length;i++)v[i]=new Vector3(p.XYZ[3*i],p.XYZ[3*i+1],p.XYZ[3*i+2]);
                    var mesh=new Mesh{name=p.Id,indexFormat=v.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=v;mesh.triangles=p.Indices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                    var go=new GameObject(p.Id);go.transform.position=new Vector3((float)dx,(float)dy,(float)dz);go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();
                    var shader=Shader.Find("Universal Render Pipeline/Lit");Require(shader!=null&&shader.isSupported,"Diagnostic shader unavailable");var material=new Material(shader);material.name="Local terrain diagnostic "+roles[p.Id];material.SetColor("_BaseColor",roles[p.Id]=="terrain"?new Color(.27f,.38f,.18f):new Color(.14f,.15f,.16f));material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.12f);mr.sharedMaterial=material;
                    var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",roles[p.Id]=="terrain"?new Color(.27f,.38f,.18f):new Color(.14f,.15f,.16f));mr.SetPropertyBlock(block);
                    var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;uploaded.Add(new Uploaded{Role=roles[p.Id],Collider=collider,Packet=p});
                    var actual=mesh.vertices;var actualIndices=mesh.triangles;Require(actualIndices.SequenceEqual(p.Indices),"Uploaded indices changed");for(int i=0;i<v.Length;i++)Require(Same(actual[i].x,p.XYZ[i*3])&&Same(actual[i].y,p.XYZ[i*3+1])&&Same(actual[i].z,p.XYZ[i*3+2]),"Uploaded local floats changed");
                    if(first){bounds=mr.bounds;first=false;}else bounds.Encapsulate(mr.bounds);
                }
                Physics.SyncTransforms();var probes=new JArray();double maximumError=0;int failures=0,checks=0;
                // Every emitted triangle centroid: raw PhysX results are retained,
                // never replaced with an analytic height or filtered success.
                foreach(var u in uploaded)for(int j=0;j<u.Packet.Indices.Length;j+=3)
                {
                    var p=u.Packet;int a=p.Indices[j]*3,b=p.Indices[j+1]*3,c=p.Indices[j+2]*3;
                    double x=p.OriginX+((double)p.XYZ[a]+p.XYZ[b]+p.XYZ[c])/3,z=p.OriginZ+((double)p.XYZ[a+2]+p.XYZ[b+2]+p.XYZ[c+2])/3;
                    var surface=u.Role=="terrain"?ground:road;Require(surface.TryHeight(x,z,out var expected),"Final surface centroid missing");
                    var ray=new Ray(new Vector3((float)(x-ox),(float)(expected.Height-oy+10),(float)(z-oz)),Vector3.down);bool hit=false;float top=float.NegativeInfinity;
                    foreach(var other in uploaded)if(other.Role==u.Role&&other.Collider.Raycast(ray,out var h,30)){hit=true;top=Mathf.Max(top,h.point.y);}
                    double error=hit?Math.Abs(top+oy-expected.Height):double.PositiveInfinity;checks++;if(!hit||error>.0002)failures++;if(hit)maximumError=Math.Max(maximumError,error);
                    probes.Add(new JObject{{"packet",p.Id},{"triangle",j/3},{"worldX",x},{"worldZ",z},{"expected",expected.Height},{"hit",hit},{"actual",hit?(JToken)(top+oy):JValue.CreateNull()},{"error",hit?(JToken)error:JValue.CreateNull()}});
                }
                File.WriteAllText(Path.Combine(output,"raw-physics-probes.json"),probes.ToString(Formatting.None));
                var renderFacts=new JArray();foreach(var u in uploaded){var mr=u.Collider.GetComponent<MeshRenderer>();var m=mr.sharedMaterial;var c=m.GetColor("_BaseColor");renderFacts.Add(new JObject{{"id",u.Packet.Id},{"role",u.Role},{"shader",m.shader.name},{"baseColor",new JArray(c.r,c.g,c.b,c.a)},{"active",mr.enabled&&mr.gameObject.activeInHierarchy},{"boundsCentre",new JArray(mr.bounds.center.x,mr.bounds.center.y,mr.bounds.center.z)}});}File.WriteAllText(Path.Combine(output,"render-materials.json"),renderFacts.ToString(Formatting.Indented));
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.55f,.59f,.63f);RenderSettings.ambientEquatorColor=new Color(.4f,.43f,.46f);RenderSettings.ambientGroundColor=new Color(.22f,.24f,.21f);var light=new GameObject("Diagnostic sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(45,-35,0);
                var camera=new GameObject("Diagnostic camera").AddComponent<Camera>();camera.backgroundColor=new Color(.42f,.53f,.63f);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.05f;camera.farClipPlane=1000;camera.fieldOfView=50;
                float span=Mathf.Max(bounds.size.x,bounds.size.z);Capture(camera,bounds.center+new Vector3(span*.8f,span*.7f,-span*.9f),bounds.center,Path.Combine(output,"overview.png"));Capture(camera,bounds.center+new Vector3(0,2,-span*.7f),bounds.center,Path.Combine(output,"ground-eye.png"));
                Require(FileHash(career)==before,"Protected career changed");Require(FileHash(input)==pin,"Fixture changed during proof");
                report=new JObject{{"status",failures==0?"PASS_DIAGNOSTIC_ONLY":"FAIL"},{"inputSHA256",pin},{"terrainGeometrySHA256",ground.GeometryDigest},{"asphaltGeometrySHA256",road.GeometryDigest},{"packets",uploaded.Count},{"vertices",totalVertices},{"triangles",totalIndices/3},{"rawPhysicsChecks",checks},{"failures",failures},{"maximumMetresError",maximumError},{"careerUnchanged",true},{"normalWorldAdmission",false},{"scope","One source-pinned local cell, final local-float terrain/asphalt buffers, actual MeshCollider centroid rays and static GPU captures; no whole-world, driving, performance or geometry13 admission."}};
                Require(failures==0,"Raw collider discrepancy; see retained probe records");exit=0;
            }
            catch(Exception e){report["status"]="FAIL";report["error"]=e.ToString();Debug.LogException(e);}
            finally{if(outputCreated)File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString(Formatting.Indented));Debug.Log("LOCAL TERRAIN CELL "+(exit==0?"PASS":"FAIL")+" "+output);EditorApplication.Exit(exit);}
        }
        static void Capture(Camera camera,Vector3 position,Vector3 target,string path)
        {
            camera.transform.position=position;camera.transform.LookAt(target);var rt=new RenderTexture(1600,1000,24);var old=RenderTexture.active;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
