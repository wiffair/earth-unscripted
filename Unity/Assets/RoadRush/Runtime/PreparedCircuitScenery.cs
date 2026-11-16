using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // Explicit CLI authoring proof ONLY. No country-pack, OGL, cache or normal gameplay admission.
    public sealed class PreparedCircuitScenery : MonoBehaviour
    {
        const int MaxManifest=64000,MaxPayload=12000000,MaxVertices=450000,MaxTriangles=150000,MaxParts=512;
        public string Identity {get;private set;}public string Provenance {get;private set;}public int ArtTriangles {get;private set;}public int ExcludedTriangles {get;private set;}
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public sealed class Part {public string Feature,Material;public int Lod;public Vector3[] Vertices;public int[] Triangles;}
        public sealed class Pack {public string Identity,Provenance,Venue,SourceHash;public int ExcludedTriangles;public Dictionary<string,Color> Materials;public Part[] Parts;}
        static void Require(bool ok,string reason){if(!ok)throw new InvalidDataException("Prepared circuit scenery: "+reason);}
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static bool Digest(string s)=>s!=null&&Regex.IsMatch(s,"^[a-f0-9]{64}$");
        static string Hash(byte[] b){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
        static JObject Read(byte[] b,int limit){Require(b!=null&&b.Length>0&&b.Length<=limit,"byte budget");using(var reader=new Newtonsoft.Json.JsonTextReader(new StringReader(Encoding.UTF8.GetString(b))){MaxDepth=12})return JObject.Load(reader);}
        static Vector3 V(JToken t){Require(t is JArray a&&a.Count==3,"vector arity");var v=new Vector3((float)t[0],(float)t[1],(float)t[2]);Require(Finite(v.x)&&Finite(v.y)&&Finite(v.z),"finite vector");return v;}
        static bool InBounds(Vector3 p,Vector3 low,Vector3 high)=>p.x>=low.x-.001f&&p.y>=low.y-.001f&&p.z>=low.z-.001f&&p.x<=high.x+.001f&&p.y<=high.y+.001f&&p.z<=high.z+.001f;
        public static Pack Validate(byte[] manifestBytes,byte[][] payloads,GeoRegion region,string originalGeoSha,bool authorizedFixtureAlias=false)
        {
            Require(region!=null&&region.circuit&&region.latitude>=-80&&region.latitude<=80&&region.longitude>=-180&&region.longitude<=180&&Digest(originalGeoSha),"explicit circuit source identity");var m=Read(manifestBytes,MaxManifest);string originalRegionId=(string)m["sourceRegionId"];string venue=(string)m["venue"],id=(string)m["id"],source=(string)m["sourceGeoSha256"],provenance=(string)m["licence"];
            Require((string)m["format"]=="RoadRushVenueDetailCandidate1"&&originalRegionId!=null&&Regex.IsMatch(originalRegionId,"^[a-zA-Z0-9_-]{1,100}$")&&(region.id==originalRegionId||authorizedFixtureAlias&&region.id=="download-circuit-proof-"+venue)&&venue!=null&&Regex.IsMatch(venue,"^[a-z][a-z0-9-]{0,39}$")&&id!=null&&id.Length<=100,"schema/venue/identity");
            Require(m["runtimeAdmission"]?.Type==JTokenType.Boolean&&(bool)m["runtimeAdmission"]==false,"authoring-only manifest");Require(source==originalGeoSha&&Digest(source),"geographic source hash");Require((string)m["units"]=="metres"&&!string.IsNullOrWhiteSpace(provenance)&&provenance.Length<=3000,"units/provenance");
            var origin=m["originWGS84"] as JArray;Require(origin!=null&&origin.Count==2&&Math.Abs((double)origin[0]-region.latitude)<1e-9&&Math.Abs((double)origin[1]-region.longitude)<1e-9&&(float)m["relativeDatum"]==region.datum,"origin/datum mismatch");
            var bounds=m["boundsXYZ"] as JArray;Require(bounds!=null&&bounds.Count==3&&bounds.All(x=>x is JArray a&&a.Count==2),"bounds arity");var low=new Vector3((float)bounds[0][0],(float)bounds[1][0],(float)bounds[2][0]);var high=new Vector3((float)bounds[0][1],(float)bounds[1][1],(float)bounds[2][1]);Require(Finite(low.x)&&Finite(low.y)&&Finite(low.z)&&Finite(high.x)&&Finite(high.y)&&Finite(high.z)&&low.x<high.x&&low.y<high.y&&low.z<high.z&&low.x>=-6000&&low.z>=-6000&&high.x<=6000&&high.z<=6000&&low.y>=-500&&high.y<=1500,"bounded metric coverage");
            Require(payloads!=null&&payloads.Length==2,"two explicit LODs");var descriptions=m["compiledLODs"] as JArray;var hashes=m["files"] as JObject;Require(descriptions!=null&&descriptions.Count==2&&hashes!=null,"compiled LOD contract");
            var parts=new List<Part>();var materials=new Dictionary<string,Color>();int excluded=0;
            for(int lod=0;lod<2;lod++)
            {
                string file="mesh-lod"+lod+".json";var desc=descriptions[lod];Require((int)desc["lod"]==lod&&(string)desc["file"]==file,"fixed payload name/order");var bytes=payloads[lod];Require(bytes!=null&&bytes.Length==(int)desc["bytes"]&&bytes.Length<=MaxPayload&&Digest((string)hashes[file])&&Hash(bytes)==(string)hashes[file],"payload hash/bytes");var p=Read(bytes,MaxPayload);
                Require((string)p["format"]=="RoadRushVenueDetailMesh1"&&(string)p["venue"]==venue&&(int)p["lod"]==lod&&(string)p["sourceGeoSha256"]==source&&(string)p["sourceRegionId"]==originalRegionId&&p["runtimeAdmission"]?.Type==JTokenType.Boolean&&!(bool)p["runtimeAdmission"],"payload schema/source/admission");
                var po=p["originWGS84"] as JArray;Require(po!=null&&po.Count==2&&(double)po[0]==(double)origin[0]&&(double)po[1]==(double)origin[1]&&(float)p["relativeDatum"]==region.datum,"payload metric frame");var colours=p["materials"] as JObject;Require(colours!=null&&colours.Count>0&&colours.Count<=16,"material budget");
                foreach(var c in colours.Properties()){Require(Regex.IsMatch(c.Name,"^[a-z][a-z0-9_-]{0,31}$"),"material ID");Vector3 v=V(c.Value);Require(v.x>=0&&v.x<=1&&v.y>=0&&v.y<=1&&v.z>=0&&v.z<=1,"material range");var color=new Color(v.x,v.y,v.z);if(materials.TryGetValue(c.Name,out var previous))Require(previous==color,"LOD material consistency");else materials.Add(c.Name,color);}
                var entries=p["parts"] as JArray;Require(entries!=null&&entries.Count>0&&entries.Count<=MaxParts&&entries.Count==(int)desc["parts"],"part budget/count");int nv=0,nt=0;var keys=new HashSet<string>();
                foreach(var entry in entries)
                {
                    string feature=(string)entry["featureId"],role=(string)entry["role"],material=(string)entry["material"];Require(feature!=null&&Regex.IsMatch(feature,"^[a-zA-Z0-9_-]{1,100}$")&&material!=null&&materials.ContainsKey(material)&&keys.Add(feature+"|"+role+"|"+material),"part identity/material");
                    Require(role=="art"||role=="preview-context"||role=="candidate-unverified"||role=="preview-pit-unadmitted","role allowlist");var vv=entry["vertices"] as JArray;var tt=entry["triangles"] as JArray;Require(vv!=null&&vv.Count>=3&&vv.Count<=60000&&tt!=null&&tt.Count>=3&&tt.Count%3==0,"part shape/16bit budget");nv+=vv.Count;nt+=tt.Count/3;Require(nv<=MaxVertices&&nt<=MaxTriangles,"aggregate mesh budget");var vertices=vv.Select(V).ToArray();Require(vertices.All(v=>InBounds(v,low,high)),"vertex coverage");var indices=tt.Select(t=>{Require(t.Type==JTokenType.Integer,"integer index");int i=(int)t;Require(i>=0&&i<vertices.Length,"index bounds");return i;}).ToArray();
                    for(int i=0;i<indices.Length;i+=3)Require(Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude>1e-14f,"nondegenerate triangle");
                    if(role!="art"){excluded+=indices.Length/3;continue;}Require(!feature.StartsWith("inferred-barrier")&&!feature.StartsWith("inferred-runoff")&&!feature.StartsWith("inferred-kerb")&&!feature.StartsWith("gp-road")&&!feature.StartsWith("osm-pit-"),"forbidden drivable feature");parts.Add(new Part{Feature=feature,Material=material,Lod=lod,Vertices=vertices,Triangles=indices});
                }
                Require(nv==(int)desc["vertices"]&&nt==(int)desc["triangles"],"compiled count identity");
            }
            Require(parts.Any(p=>p.Lod==0)&&parts.Any(p=>p.Lod==1),"nonempty art LODs");return new Pack{Identity=Hash(manifestBytes),Venue=venue,SourceHash=source,Provenance=provenance,Materials=materials,Parts=parts.ToArray(),ExcludedTriangles=excluded};
        }
        static byte[] BoundedRead(string path,int max){var f=new FileInfo(path);Require(f.Exists&&f.Length>0&&f.Length<=max,"file budget");return File.ReadAllBytes(path);}
        public static bool TryInstall(Transform parent,GeoRegion region,string manifestPath,string originalGeoSha)
        {
            GameObject root=null;
            try
            {
                Require(parent!=null,"parent");var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushCircuitArtProof");Require(at>=0&&at+1<args.Length&&Path.GetFullPath(args[at+1])==Path.GetFullPath(manifestPath),"explicit exact CLI authoring path required");string path=Path.GetFullPath(manifestPath),folder=Path.GetDirectoryName(path);var manifestBytes=BoundedRead(path,MaxManifest);var sceneryMeta=Read(manifestBytes,MaxManifest);bool authorizedAlias=false;
                if(region!=null)
                {
                    int sourceAt=Array.IndexOf(args,"-roadRushCircuitSourceProof");Require(sourceAt>=0&&sourceAt+1<args.Length,"explicit source CLI trust chain required");string sourceManifestPath=Path.GetFullPath(args[sourceAt+1]);var sourceMeta=Read(BoundedRead(sourceManifestPath,100000),100000);string sourceFile=(string)sourceMeta["payload"];Require((string)sourceMeta["format"]=="RoadRushCircuitGeoCandidate1"&&(string)sourceMeta["venueId"]==(string)sceneryMeta["venue"]&&sourceFile!=null&&Path.GetFileName(sourceFile)==sourceFile,"source manifest venue/payload");var originalBytes=BoundedRead(Path.Combine(Path.GetDirectoryName(sourceManifestPath),sourceFile),4000000);string hash=Hash(originalBytes);var original=Read(originalBytes,4000000);Require(hash==originalGeoSha&&hash==(string)sourceMeta["sha256"]&&hash==(string)sceneryMeta["sourceGeoSha256"]&&(string)original["id"]==(string)sceneryMeta["sourceRegionId"]&&(bool?)original["circuit"]==true,"original source hash/region identity");authorizedAlias=region.id!=(string)sceneryMeta["sourceRegionId"];Require(!authorizedAlias||region.id=="download-circuit-proof-"+(string)sceneryMeta["venue"],"exact fixture alias");
                }
                var pack=Validate(manifestBytes,new[]{BoundedRead(Path.Combine(folder,"mesh-lod0.json"),MaxPayload),BoundedRead(Path.Combine(folder,"mesh-lod1.json"),MaxPayload)},region,originalGeoSha,authorizedAlias);
                root=new GameObject("Authoring-only prepared circuit art "+pack.Identity.Substring(0,12));root.transform.SetParent(parent,false);root.SetActive(false);var owner=root.AddComponent<PreparedCircuitScenery>();owner.Identity=pack.Identity;owner.Provenance=pack.Provenance;owner.ExcludedTriangles=pack.ExcludedTriangles;owner.Build(pack);Require(root.GetComponentsInChildren<Collider>(true).Length==0,"display-only scenery has no colliders");root.SetActive(true);return true;
            }
            catch(Exception e)when(e is InvalidDataException||e is IOException||e is UnauthorizedAccessException||e is Newtonsoft.Json.JsonException||e is ArgumentException||e is InvalidOperationException||e is OverflowException||e is InvalidCastException||e is NotSupportedException)
            {if(root){if(Application.isPlaying)Destroy(root);else DestroyImmediate(root);}Debug.LogWarning("Optional authoring circuit scenery refused: "+e.Message);return false;}
        }
        sealed class Batch {public readonly List<Vector3> Vertices=new List<Vector3>();public readonly List<int> Indices=new List<int>();}
        void Build(Pack pack)
        {
            var materials=new Dictionary<string,Material>();foreach(var c in pack.Materials){var m=CircuitOwnedMaterials.Create("Prepared original art "+c.Key,c.Value);owned.Add(m);materials.Add(c.Key,m);}
            var batches=new Dictionary<(Vector2Int cell,int lod,string material),Batch>();
            foreach(var part in pack.Parts)for(int i=0;i<part.Triangles.Length;i+=3)
            {
                var a=part.Vertices[part.Triangles[i]];var b=part.Vertices[part.Triangles[i+1]];var c=part.Vertices[part.Triangles[i+2]];var centre=(a+b+c)/3;var cell=new Vector2Int(Mathf.FloorToInt(centre.x/128),Mathf.FloorToInt(centre.z/128));var key=(cell,part.Lod,part.Material);if(!batches.TryGetValue(key,out var batch)){Require(batches.Count<2048,"prepared render-batch budget");batch=new Batch();batches.Add(key,batch);}Require(batch.Vertices.Count+3<=60000,"render-batch16bit budget");int start=batch.Vertices.Count;batch.Vertices.AddRange(new[]{a,b,c});batch.Indices.AddRange(new[]{start,start+1,start+2});ArtTriangles++;
            }
            foreach(var cell in batches.Keys.Select(k=>k.cell).Distinct())
            {
                var cellRoot=new GameObject("Prepared art cell "+cell);cellRoot.transform.SetParent(transform,false);var lods=new List<LOD>();
                for(int level=0;level<2;level++)
                {
                    var renderers=new List<Renderer>();foreach(var pair in batches.Where(p=>p.Key.cell==cell&&p.Key.lod==level))
                    {
                        var mesh=new Mesh{name="Prepared circuit art LOD"+level};owned.Add(mesh);mesh.SetVertices(pair.Value.Vertices);mesh.SetTriangles(pair.Value.Indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var child=new GameObject("LOD"+level+" "+pair.Key.material);child.transform.SetParent(cellRoot.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[pair.Key.material];renderers.Add(renderer);
                    }
                    lods.Add(new LOD(level==0?.075f:.012f,renderers.ToArray()));
                }
                var group=cellRoot.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();
            }
        }
        void OnDestroy(){foreach(var item in owned)if(item){if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);}owned.Clear();}
    }
}
