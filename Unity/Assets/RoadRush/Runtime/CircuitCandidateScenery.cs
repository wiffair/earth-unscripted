using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // Optional original architecture only. Does not grant geographic pack admission.
    public sealed class CircuitCandidateScenery : MonoBehaviour
    {
        public static bool Enabled; // Deliberate isolated-preview opt-in; never persisted.
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public sealed class Module { public string Id,Feature,Material; public Vector3 Position,Size; public float Yaw; }
        public sealed class Pack { public string Identity,Track,Provenance; public Module[] Modules; public Dictionary<string,Color> Materials; }
        static string Hash(byte[] bytes){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static void Require(bool ok,string reason){if(!ok)throw new InvalidDataException("Circuit scenery: "+reason);}
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static Vector3 Vector(JToken t){Require(t is JArray a&&a.Count==3,"vector shape");var v=new Vector3((float)t[0],(float)t[1],(float)t[2]);Require(Finite(v.x)&&Finite(v.y)&&Finite(v.z),"finite vector");return v;}
        static JObject Read(byte[] bytes,int max){Require(bytes!=null&&bytes.Length>0&&bytes.Length<=max,"byte budget");using(var r=new Newtonsoft.Json.JsonTextReader(new StringReader(System.Text.Encoding.UTF8.GetString(bytes))){MaxDepth=12})return JObject.Load(r);}
        public static Pack Validate(string track,byte[] manifestBytes,byte[] payloadBytes,byte[] geoBytes,double latitude,double longitude)
        {
            Require(track=="silverstone"||track=="spa","venue allowlist");var m=Read(manifestBytes,16000);
            Require((string)m["format"]=="RoadRushCircuitScenery1"&&(string)m["track"]==track&&(string)m["units"]=="metres","manifest schema");
            Require((string)m["layoutId"]==track+"-gp-existing-approx-v1","layout identity");
            var origin=m["origin"] as JArray;Require(origin!=null&&origin.Count==2&&Math.Abs((double)origin[0]-latitude)<1e-9&&Math.Abs((double)origin[1]-longitude)<1e-9,"origin mismatch");
            Require(geoBytes!=null&&Hash(geoBytes)==(string)m["sourceGeoSha256"],"source geometry identity");
            Require(payloadBytes!=null&&payloadBytes.Length==(int)m["bytes"]&&Hash(payloadBytes)==(string)m["payloadSha256"],"payload identity");
            Require((int)m["maxModules"]==1024,"module budget contract");
            string provenance=(string)m["provenance"],licence=(string)m["licence"];Require(!string.IsNullOrWhiteSpace(provenance)&&provenance.Length<=2000&&!string.IsNullOrWhiteSpace(licence)&&licence.Length<=1000,"provenance");
            var bounds=m["bounds"] as JArray;Require(bounds!=null&&bounds.Count==2,"bounds");Vector3 low=Vector(bounds[0]),high=Vector(bounds[1]);Require(low.x>=-1800&&low.y>=-50&&low.z>=-1800&&high.x<=1800&&high.y<=250&&high.z<=1800&&low.x<high.x&&low.y<high.y&&low.z<high.z,"bounded coverage");
            var p=Read(payloadBytes,256000);Require((string)p["format"]=="RoadRushCircuitModules1"&&(string)p["track"]==track,"payload schema");
            var colours=p["materials"] as JObject;Require(colours!=null&&colours.Count>0&&colours.Count<=16,"material budget");var materials=new Dictionary<string,Color>();
            foreach(var c in colours.Properties()){Require(c.Name.Length>0&&c.Name.Length<=32,"material id");Vector3 v=Vector(c.Value);Require(v.x>=0&&v.x<=1&&v.y>=0&&v.y<=1&&v.z>=0&&v.z<=1,"colour range");materials.Add(c.Name,new Color(v.x,v.y,v.z));}
            var array=p["modules"] as JArray;Require(array!=null&&array.Count>0&&array.Count<=1024,"module count");var modules=new List<Module>();var ids=new HashSet<string>();
            foreach(var token in array)
            {
                string id=(string)token["id"],feature=(string)token["feature"],material=(string)token["material"];Require(!string.IsNullOrWhiteSpace(id)&&id.Length<=120&&ids.Add(id)&&!string.IsNullOrWhiteSpace(feature)&&feature.Length<=80&&material!=null&&materials.ContainsKey(material),"module identity/material");
                Vector3 pos=Vector(token["position"]),size=Vector(token["size"]);float yaw=(float)token["yawRadians"];Require(Finite(yaw)&&Mathf.Abs(yaw)<=Mathf.PI*2,"yaw");Require(size.x>0&&size.y>0&&size.z>0&&size.x<=200&&size.y<=200&&size.z<=200,"metric size");
                float radius=Mathf.Sqrt(size.x*size.x+size.z*size.z)*.5f;Require(pos.x-radius>=low.x&&pos.x+radius<=high.x&&pos.z-radius>=low.z&&pos.z+radius<=high.z&&pos.y>=low.y&&pos.y+size.y<=high.y,"module bounds");
                modules.Add(new Module{Id=id,Feature=feature,Material=material,Position=pos,Size=size,Yaw=yaw});
            }
            return new Pack{Identity=Hash(manifestBytes),Track=track,Provenance=provenance,Modules=modules.ToArray(),Materials=materials};
        }
        public static bool TryInstall(Transform parent,GeoRegion region)
        {
            if(!Enabled||region==null||!region.circuit||(region.id!="spa"&&region.id!="silverstone"))return false;
            GameObject root=null;
            try
            {
                var m=Resources.Load<TextAsset>("CircuitCandidates/"+region.id+"-manifest");var p=Resources.Load<TextAsset>("CircuitCandidates/"+region.id+"-modules");var g=Resources.Load<TextAsset>("Geo/"+region.id);Require(m&&p&&g,"resource missing");
                var pack=Validate(region.id,m.bytes,p.bytes,g.bytes,region.latitude,region.longitude);
                root=new GameObject("Optional original circuit architecture "+pack.Identity.Substring(0,12));root.transform.SetParent(parent,false);root.SetActive(false);var owner=root.AddComponent<CircuitCandidateScenery>();owner.Build(pack);root.SetActive(true);return true;
            }
            catch(Exception e) when(e is InvalidDataException||e is Newtonsoft.Json.JsonException||e is FormatException||e is OverflowException||e is InvalidCastException||e is ArgumentException)
            {if(root)Destroy(root);Debug.LogWarning("Optional circuit scenery refused: "+e.Message);return false;}
        }
        void Build(Pack pack)
        {
            var materials=new Dictionary<string,Material>();foreach(var c in pack.Materials){var m=CircuitOwnedMaterials.Create("Circuit original "+c.Key,c.Value);owned.Add(m);materials.Add(c.Key,m);}
            // Cells bound LOD preparation and avoid a circuit-wide bounding sphere.
            foreach(var cell in pack.Modules.GroupBy(m=>new Vector2Int(Mathf.FloorToInt(m.Position.x/128),Mathf.FloorToInt(m.Position.z/128))))
            {
                var root=new GameObject("Scenery cell "+cell.Key);root.transform.SetParent(transform,false);var levels=new List<LOD>();
                for(int level=0;level<2;level++)
                {
                    var renderers=new List<Renderer>();foreach(var batch in cell.Where(m=>level==0||m.Material!="glass"&&m.Size.x*m.Size.y*m.Size.z>=4).GroupBy(m=>m.Material))
                    {
                        var vertices=new List<Vector3>();var triangles=new List<int>();foreach(var module in batch)AppendBox(module,vertices,triangles);
                        if(vertices.Count==0)continue;var mesh=new Mesh{name="Original circuit modules LOD"+level};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
                        var child=new GameObject("LOD"+level+" "+batch.Key);child.transform.SetParent(root.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[batch.Key];renderers.Add(renderer);
                    }
                    levels.Add(new LOD(level==0?.08f:.015f,renderers.ToArray()));
                }
                var group=root.AddComponent<LODGroup>();group.SetLODs(levels.ToArray());group.RecalculateBounds();
            }
        }
        static void AppendBox(Module m,List<Vector3> v,List<int> t)
        {
            var p=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,1,-.5f),new Vector3(-.5f,1,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,1,.5f),new Vector3(-.5f,1,.5f)};
            int[][] faces={new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,4,7,3},new[]{1,2,6,5},new[]{3,7,6,2},new[]{0,1,5,4}};var rotation=Quaternion.Euler(0,m.Yaw*Mathf.Rad2Deg,0);
            foreach(var face in faces){int start=v.Count;foreach(int i in face)v.Add(m.Position+rotation*Vector3.Scale(p[i],m.Size));t.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});}
        }
        void OnDestroy(){foreach(var item in owned)if(item){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}owned.Clear();}
    }
}
