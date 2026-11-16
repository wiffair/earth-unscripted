using System;using System.IO;using System.Linq;using System.Threading;using UnityEditor;using UnityEngine;using RoadRush.TerrainDraft;
namespace RoadRush.Editor
{
 // Called by actual terrain verifier fixture AFTER it returns its privately
 // constructed typed result. This test has no public proof/receipt minting path.
 public static class WorldMeshAdapterVerification
 {
  [Serializable]public sealed class Report{public string status,scope="Verifier-issued bounded joined cell only; no complete-world/boot admission",geometryDigest,sourceDigest;public int checks,packets;}
  static void Check(Report r,bool okay,string why){if(!okay)throw new Exception("WORLD MESH ADAPTER FAIL "+why);r.checks++;}
  static bool Bits(float a,float b)=>BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b);
  public static Report Verify(VerifiedJoinedTerrain proof,string output)
  {
   if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>()||Directory.Exists(output)||!output.StartsWith("/private/tmp/rr019-world-mesh-adapter-unity-",StringComparison.Ordinal))throw new Exception("Fresh isolated Editor scope proof required");Directory.CreateDirectory(output);var r=new Report();GameObject stage=null,primitive=null;Material road=null,terrain=null;try{
    var input=ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot("/private/tmp/rr019-integration-drafts/ActualPantside");var owner=CanonicalRaw13Codec.Read(input,input.CopyDocumentBytes());var plan=PreparedWorldMeshPlan.FromVerifiedJoinedScope(owner,proof);r.geometryDigest=plan.GeometryDigest;r.sourceDigest=plan.SourceDigest;r.packets=plan.PacketCount;
    Check(r,!plan.FullWorldAdmitted&&plan.SourceSubsetOnly,"bounded source subset cannot impersonate complete world");Check(r,!plan.TryGround(plan.MinX-1,plan.MinZ-1,out _),"outside scope has no analytic fallback");
    var packets=plan.CopyPackets();Check(r,packets.Length==plan.PacketCount&&plan.RawDocumentDigest==ReviewedCanonicalRaw13Input.ExpectedBodySHA256,"raw/source/DEM-bound exact packet inventory");
    var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)throw new Exception("URP Lit shader missing");road=new Material(shader);terrain=new Material(shader);primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);var sharedPrimitive=primitive.GetComponent<MeshFilter>().sharedMesh;
    stage=new GameObject("Adapter owned stage");stage.SetActive(false);var upload=WorldMeshUpload.BuildHidden(stage.transform,plan,role=>role==PreparedWorldMeshPlan.Role.GroundRoad?road:terrain).GetEnumerator();
    try{Check(r,upload.MoveNext()&&!stage.activeInHierarchy,"first upload yields inside inactive stage");while(upload.MoveNext()){} }finally{upload.Dispose();}
    var meshes=stage.GetComponentsInChildren<MeshFilter>(true);Check(r,meshes.Length==packets.Length,"every final packet uploaded exactly once");
    foreach(var packet in packets)
    {
     var filter=meshes.Single(m=>m.gameObject.name==packet.Id);var expected=packet.CopyFinalBuffers();var actual=filter.sharedMesh.vertices;Check(r,filter.GetComponent<MeshCollider>().sharedMesh==filter.sharedMesh,"renderer and collider share one owned Mesh");Check(r,filter.GetComponent<GeneratedMeshOwner>().Mesh==filter.sharedMesh,"generated mesh ownership explicit");
     Check(r,actual.Length*3==expected.XYZ.Length&&filter.sharedMesh.triangles.SequenceEqual(expected.Indices),"upload keeps complete exact index/vertex buffers");for(int i=0;i<actual.Length;i++)if(!Bits(actual[i].x,expected.XYZ[i*3])||!Bits(actual[i].y,expected.XYZ[i*3+1])||!Bits(actual[i].z,expected.XYZ[i*3+2]))throw new Exception("Final upload vertex bits changed");r.checks++;
     Check(r,(double)filter.transform.localPosition.x==expected.OriginX&&(double)filter.transform.localPosition.y==expected.OriginY&&(double)filter.transform.localPosition.z==expected.OriginZ,"exact packet origin applied once");Check(r,filter.GetComponent<MeshRenderer>().sharedMaterial==(packet.SurfaceRole==PreparedWorldMeshPlan.Role.GroundRoad?road:terrain),"shared role material reused without clones");
     int a=expected.Indices[0]*3,b=expected.Indices[1]*3,c=expected.Indices[2]*3;double x=expected.OriginX+((double)expected.XYZ[a]+expected.XYZ[b]+expected.XYZ[c])/3,z=expected.OriginZ+((double)expected.XYZ[a+2]+expected.XYZ[b+2]+expected.XYZ[c+2])/3,y=expected.OriginY+((double)expected.XYZ[a+1]+expected.XYZ[b+1]+expected.XYZ[c+1])/3;
     Check(r,plan.TryGround(x,z,out var hit)&&Math.Abs(hit.Height-y)<=.0002,"query uses same final float triangle plane as uploaded collider");
    }
    var ownedMeshes=meshes.Select(m=>m.sharedMesh).ToArray();Geometry.ReleaseMeshes(stage);UnityEngine.Object.DestroyImmediate(stage);stage=null;Check(r,ownedMeshes.All(m=>!m)&&sharedPrimitive&&road&&terrain,"release destroys generated meshes and retains shared assets");
    stage=new GameObject("Adapter cancelled stage");stage.SetActive(false);using(var token=new CancellationTokenSource())
    {var work=WorldMeshUpload.BuildHidden(stage.transform,plan,role=>road,token.Token).GetEnumerator();Check(r,work.MoveNext(),"cancel fixture allocates actual mesh/collider before suspension");var allocated=stage.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(o=>o.Mesh).ToArray();token.Cancel();bool cancelled=false;try{work.MoveNext();}catch(OperationCanceledException){cancelled=true;}finally{work.Dispose();}Check(r,cancelled&&allocated.All(m=>!m)&&stage.transform.childCount==0&&sharedPrimitive&&road,"cancel releases only owned partial upload after actual mesh allocation");}
    Check(r,owner.Write().SequenceEqual(input.CopyDocumentBytes()),"upload/query/cancel do not mutate raw canonical owner");r.status="PASS";return r;
   }catch(Exception e){r.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}finally{if(stage){Geometry.ReleaseMeshes(stage);UnityEngine.Object.DestroyImmediate(stage);}if(primitive)UnityEngine.Object.DestroyImmediate(primitive);if(road)UnityEngine.Object.DestroyImmediate(road);if(terrain)UnityEngine.Object.DestroyImmediate(terrain);File.WriteAllText(Path.Combine(output,"receipt.json"),CanonicalGeography13.Write(r,true));}
  }
 }
}
