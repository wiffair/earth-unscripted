using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // Circuit-only source-feature replacement candidate. Geographic source records remain untouched.
    public sealed class CircuitReplacementArt : MonoBehaviour
    {
        public static bool Enabled;
        static readonly Dictionary<GeoRegion,CircuitReplacementArt> installed=new Dictionary<GeoRegion,CircuitReplacementArt>();
        readonly Dictionary<string,Vector3[]> footprints=new Dictionary<string,Vector3[]>();readonly Dictionary<string,float> heights=new Dictionary<string,float>();readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();GeoRegion region;
        static void Check(bool ok,string s){if(!ok)throw new InvalidDataException("Circuit replacement art: "+s);}
        static string Hash(byte[] b){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
        static JObject Read(byte[] b,int max){Check(b!=null&&b.Length>0&&b.Length<=max,"byte budget");using(var reader=new Newtonsoft.Json.JsonTextReader(new StringReader(System.Text.Encoding.UTF8.GetString(b))){MaxDepth=12})return JObject.Load(reader);}
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static Vector3 V(JToken t){Check(t is JArray a&&a.Count==3,"vector shape");var v=new Vector3((float)t[0],(float)t[1],(float)t[2]);Check(Finite(v.x)&&Finite(v.y)&&Finite(v.z)&&v.x>=-2000&&v.x<=2000&&v.z>=-2000&&v.z<=2000&&v.y>=-100&&v.y<=400,"metric bounds");return v;}
        public static bool ShouldReplace(GeoRegion source,GeoBuilding building)
        {
            if(!Enabled||source==null||!source.circuit||building==null||!installed.TryGetValue(source,out var owner)||!owner)return false;
            return owner.Matches(building);
        }
        bool Matches(GeoBuilding b)
        {
            if(!footprints.TryGetValue(b.id,out var expected)||b.points==null||b.points.Length!=expected.Length+1||b.height!=heights[b.id]||(b.points[0].x!=b.points[b.points.Length-1].x||b.points[0].y!=b.points[b.points.Length-1].y||b.points[0].z!=b.points[b.points.Length-1].z))return false;
            for(int i=0;i<expected.Length;i++)if(b.points[i].x!=expected[i].x||b.points[i].y!=expected[i].y||b.points[i].z!=expected[i].z)return false;return true;
        }
        public static bool TryInstall(Transform parent,GeoRegion source)
        {
            if(!Enabled||parent==null||source==null||!source.circuit||(source.id!="silverstone"&&source.id!="spa"))return false;
            if(installed.TryGetValue(source,out var previous)&&previous)return true;GameObject root=null;
            try
            {
                var manifest=Resources.Load<TextAsset>("CircuitCandidates/"+source.id+"-architecture-v2-manifest");var payload=Resources.Load<TextAsset>("CircuitCandidates/"+source.id+"-architecture-v2");var geographic=Resources.Load<TextAsset>("Geo/"+source.id);Check(manifest&&payload&&geographic,"resources missing");var meta=Read(manifest.bytes,12000);
                Check((string)meta["format"]=="RoadRushCircuitReplacementManifest2"&&(string)meta["venue"]==source.id,"manifest venue/schema");Check(Hash(geographic.bytes)==(string)meta["sourceGeoSha256"]&&Hash(payload.bytes)==(string)meta["payloadSha256"]&&payload.bytes.Length==(int)meta["bytes"],"source/payload identity");Check((int)meta["maxParts"]==256&&(int)meta["maxVertices"]==100000&&(int)meta["maxTriangles"]==50000&&!string.IsNullOrWhiteSpace((string)meta["provenance"]),"budget/provenance contract");
                var data=Read(payload.bytes,6000000);Check((string)data["format"]=="RoadRushCircuitReplacementMesh2"&&(string)data["venue"]==source.id,"payload schema");var features=data["features"] as JArray;var parts=data["parts"] as JArray;var colours=data["materials"] as JObject;Check(features!=null&&features.Count>0&&features.Count<=40&&parts!=null&&parts.Count>0&&parts.Count<=256&&colours!=null&&colours.Count<=16,"feature/part/material budget");
                root=new GameObject("Source-footprint circuit art v2");root.transform.SetParent(parent,false);root.SetActive(false);var owner=root.AddComponent<CircuitReplacementArt>();owner.region=source;
                foreach(var f in features){string id=(string)f["id"];Check(id!=null&&id.Length<40&&!owner.footprints.ContainsKey(id),"feature identity");var points=f["sourceFootprint"] as JArray;Check(points!=null&&points.Count>=3&&points.Count<=256,"source footprint");owner.footprints.Add(id,points.Select(V).ToArray());float height=(float)f["sourceHeight"];Check(Finite(height)&&height>=0&&height<=100,"source height");owner.heights.Add(id,height);var b=source.buildings.SingleOrDefault(x=>x.id==id);Check(b!=null&&owner.Matches(b),"mutated/missing geographic feature");}
                var materials=new Dictionary<string,Material>();foreach(var c in colours.Properties()){var v=V(c.Value);Check(v.x>=0&&v.x<=1&&v.y>=0&&v.y<=1&&v.z>=0&&v.z<=1,"colour range");var mat=CircuitOwnedMaterials.Create("Circuit source art "+c.Name,new Color(v.x,v.y,v.z));owner.owned.Add(mat);materials.Add(c.Name,mat);}
                int totalVertices=0,totalTriangles=0;var covered=new HashSet<string>();
                foreach(var part in parts)
                {
                    string id=(string)part["sourceId"],material=(string)part["material"];Check(id!=null&&owner.footprints.ContainsKey(id)&&material!=null&&materials.ContainsKey(material),"part ownership/material");var vv=part["vertices"] as JArray;var tt=part["triangles"] as JArray;Check(vv!=null&&vv.Count>=3&&vv.Count<=60000&&tt!=null&&tt.Count>=3&&tt.Count%3==0,"part geometry");totalVertices+=vv.Count;totalTriangles+=tt.Count/3;Check(totalVertices<=100000&&totalTriangles<=50000,"aggregate geometry budget");var vertices=vv.Select(V).ToArray();var triangles=tt.Select(x=>{Check(x.Type==JTokenType.Integer,"integer index");int i=(int)x;Check(i>=0&&i<vertices.Length,"index bounds");return i;}).ToArray();for(int i=0;i<triangles.Length;i+=3)Check(Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).sqrMagnitude>1e-12f,"degenerate triangle");
                    var mesh=new Mesh{name="Circuit feature "+id+" "+material};owner.owned.Add(mesh);mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();var g=new GameObject("Replacement "+id+" "+material);g.transform.SetParent(root.transform,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=materials[material];g.AddComponent<MeshCollider>().sharedMesh=mesh;covered.Add(id);
                }
                Check(covered.Count==owner.footprints.Count,"every suppressed feature has installed geometry");root.SetActive(true);installed.Add(source,owner);return true;
            }
            catch(Exception e)when(e is InvalidDataException||e is Newtonsoft.Json.JsonException||e is ArgumentException||e is InvalidCastException||e is FormatException||e is OverflowException||e is InvalidOperationException)
            {if(root){if(Application.isPlaying)Destroy(root);else DestroyImmediate(root);}Debug.LogWarning("Circuit replacement art retained original buildings: "+e.Message);return false;}
        }
        void OnDestroy(){if(region!=null&&installed.TryGetValue(region,out var owner)&&owner==this)installed.Remove(region);foreach(var item in owned)if(item){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}owned.Clear();}
    }
}
