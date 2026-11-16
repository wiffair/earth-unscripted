using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using RoadRush.TerrainDraft;using UnityEditor;using UnityEngine;
namespace RoadRush.Editor
{
 public static class FinalMeshBufferChecks
 {
  [Serializable]public sealed class Report{public string status,scope="Two separately declared source fixtures: NEWRAW Pantside5-link roads and legacy Dee realcell. They are NOT joined or admitted as one world.";public int checks,probes;}
  static void Need(Report r,bool okay,string why){if(!okay)throw new Exception("FINAL BUFFER FAIL "+why);r.checks++;}
  static LocalTerrainMesh[] Load(string path,string expected)
  {
   byte[] bytes=File.ReadAllBytes(path);if(World13DocumentCodec.Hash(bytes)!=expected)throw new InvalidDataException("Frozen numeric fixture identity changed");JObject doc;using(var reader=new JsonTextReader(new StringReader(new System.Text.UTF8Encoding(false,true).GetString(bytes))){DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double})doc=JObject.Load(reader);
   return ((JArray)doc["packets"]).Select(p=>new LocalTerrainMesh((string)p["id"],0,0,0,((JArray)p["vertices"]).SelectMany(v=>new[]{v["x"].Value<float>(),v["y"].Value<float>(),v["z"].Value<float>()}).ToArray(),((JArray)p["triangles"]).Select(i=>i.Value<int>()).ToArray())).ToArray();
  }
  public static Report Execute(string output)
  {
   if(Directory.Exists(output)||!output.StartsWith("/private/tmp/rr019-final-mesh-buffers-",StringComparison.Ordinal))throw new Exception("Fresh owned output required");Directory.CreateDirectory(output);var r=new Report();try{
    var fixtures=new[]{new[]{"/private/tmp/rr019-authoring-candidate/newraw-pantside-v1/coupled-five/meshes.json","5828e232ebb5b877127d521c8f72fe2775c8ea53ca179e3dea016f82496d60db"},new[]{(Environment.GetEnvironmentVariable("EARTH_UNSCRIPTED_MESH_FIXTURE")??"Fixtures/realcell-4--8/meshes.json"),"9617adf7399c37a84718688c413fefc870fbe7fc0013bd06b537baa392dd8c74"}};
    foreach(var fixture in fixtures)
    {
     var packets=Load(fixture[0],fixture[1]);var index=new TerrainTriangleSurface(packets,fixture[1],fixture[1],new TerrainSurfaceLimits{MaximumPackets=32,MaximumVertices=250000,MaximumTriangles=500000,MaximumReferences=2000000,MaximumCandidatesPerCell=24000,IndexCellMetres=8});Need(r,index.PacketCount==packets.Length,"all fixed packets indexed without dropping geometry");string digest=index.GeometryDigest;
     foreach(var p in packets)
     {
      var upload=index.CopyForRenderer(p.Id);Need(r,upload.XYZ.SequenceEqual(p.XYZ)&&upload.Indices.SequenceEqual(p.Indices)&&upload.OriginX==p.OriginX&&upload.OriginY==p.OriginY&&upload.OriginZ==p.OriginZ,"query snapshot returns exact upload/collider buffers");
      int stride=Math.Max(3,(p.Indices.Length/120)/3*3);for(int i=0;i<p.Indices.Length;i+=stride)
      {
       if(i+2>=p.Indices.Length)break;int a=p.Indices[i]*3,b=p.Indices[i+1]*3,c=p.Indices[i+2]*3;double x=p.OriginX+((double)p.XYZ[a]+p.XYZ[b]+p.XYZ[c])/3,z=p.OriginZ+((double)p.XYZ[a+2]+p.XYZ[b+2]+p.XYZ[c+2])/3,y=p.OriginY+((double)p.XYZ[a+1]+p.XYZ[b+1]+p.XYZ[c+1])/3;
       Need(r,index.TryHeight(x,z,out var hit)&&Math.Abs(hit.Height-y)<.0002,"final centroid query agrees with independent final triangle barycentre");r.probes++;
      }
      if(upload.XYZ.Length>0)upload.XYZ[0]+=100;Need(r,index.CopyForRenderer(p.Id).XYZ.SequenceEqual(p.XYZ),"renderer-owned clone cannot mutate final query snapshot");
     }
     packets[0].XYZ[0]+=100;Need(r,index.GeometryDigest==digest,"input alias mutation cannot alter immutable final snapshot/digest");
    }
    r.status="PASS";return r;
   }catch(Exception e){r.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}finally{File.WriteAllText(Path.Combine(output,"receipt.json"),CanonicalGeography13.Write(r,true));}
  }
 }
 public static class FinalMeshBufferVerification{public static void Run(){int code=1;try{if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new Exception("Isolated final-buffer proof required");var r=FinalMeshBufferChecks.Execute("/private/tmp/rr019-final-mesh-buffers-unity-"+DateTime.UtcNow.Ticks);Debug.Log("FINAL MESH BUFFER PASS "+r.checks+" probes "+r.probes);code=0;}catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(code);}}
}
