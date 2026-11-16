using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class LanBuildingDigestVerification
    {
        [Serializable] sealed class Fixture{public GeoBuilding[] buildings;}
        static int checks;static void Check(bool ok,string text){if(!ok)throw new Exception("LAN BUILDING DIGEST FAIL: "+text);checks++;Debug.Log("LAN BUILDING DIGEST PASS: "+text);}
        static GeoBuilding House(string id)=>new GeoBuilding{id=id,kind="important_building",roof="gabled",height=6,points=new[]{Vector3.zero,Vector3.right*8,new Vector3(8,0,8),Vector3.forward*8,Vector3.zero}};
        static string D(params GeoBuilding[] buildings)=>LanVerificationDriver.BuildingDigest(buildings);
        static BuildingFacadeChunk.Feature Feature(string id,float x=0)=>new BuildingFacadeChunk.Feature(id,new Vector3(x,2,3),Quaternion.Euler(0,37,0),.92f,2.08f,true,2);
        public static void Run()
        {
            GameObject go=null;Mesh mesh=null;
            try
            {
                var a=House("a");var b=House("b");Check(D(a,b)==D(b,a),"canonical building order is ordinal-ID stable");string original=D(a,b);
                var round=JsonUtility.FromJson<Fixture>(JsonUtility.ToJson(new Fixture{buildings=new[]{a,b}}));Check(D(round.buildings)==original,"full Unity snapshot roundtrip preserves canonical digest");
                var nullable=new GeoBuilding{id="nullable",height=4};var empty=new GeoBuilding{id="nullable",kind="",roof="",height=4,points=Array.Empty<Vector3>()};Check(D(nullable)==D(empty),"null strings and missing footprints normalize to Unity serialized empty values");
                a.kind="building";Check(D(a,b)!=original,"important class alteration is detected");a.kind="important_building";a.roof="flat";Check(D(a,b)!=original,"roof alteration is detected");a.roof="gabled";a.height+=.001f;Check(D(a,b)!=original,"small height alteration is detected without rounding");a.height=6;a.measuredHeight=true;Check(D(a,b)!=original,"measured versus artistic height status is detected");a.measuredHeight=false;
                a.points[2].y=.001f;Check(D(a,b)!=original,"footprint Y is retained rather than flattened by digest");a.points[2].y=0;Array.Reverse(a.points);Check(D(a,b)!=original,"ordered footprint winding is retained");Array.Reverse(a.points);Check(D(a,b)==original,"digest leaves source data unchanged");
                var f=Feature("a/e0/b0/door");var g=Feature("b/e0/b0/door");Check(LanVerificationDriver.FeatureDigest(new[]{f,g})==LanVerificationDriver.FeatureDigest(new[]{g,f}),"actual accepted feature records hash independently of chunk enumeration");Check(LanVerificationDriver.FeatureDigest(new[]{f})!=LanVerificationDriver.FeatureDigest(new[]{Feature(f.Id,.001f)}),"actual feature displacement is detected without source-plan duplication");
                Check(typeof(LanVerificationDriver).GetCustomAttribute<DefaultExecutionOrder>().order>typeof(BuildingFacadeChunk).GetCustomAttribute<DefaultExecutionOrder>().order,"proof observes current-frame facade submissions after renderer LateUpdate");
                go=new GameObject("Physical wall proof fixture");mesh=new Mesh{vertices=new[]{new Vector3(0,0,0),new Vector3(0,4,0),new Vector3(4,0,0)},triangles=new[]{0,1,2}};var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();var witness=typeof(LanVerificationDriver).GetMethod("ColliderWitness",BindingFlags.Static|BindingFlags.NonPublic);
                Check((bool)witness.Invoke(null,new object[]{collider,false}),"physical wall witness rays intersect actual enabled mesh collider");collider.enabled=false;Check(!(bool)witness.Invoke(null,new object[]{collider,false}),"disabled physical walls cannot satisfy collision evidence");collider.enabled=true;
                Check(!(bool)witness.Invoke(null,new object[]{collider,true}),"vertical wall cannot satisfy horizontal stair-tread evidence");
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);go=null;mesh=null;
                Check(BuildingAccessGeometry.TryCreateStepFlight(new Vector3(10,.8f,20),Quaternion.identity,1,0,_=>true,(x,z)=>0,out var steps),"actual supported stair fixture is valid");go=new GameObject("Physical stair proof fixture");mesh=new Mesh{vertices=steps.Verts,triangles=steps.Tris};collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();Check((bool)witness.Invoke(null,new object[]{collider,true}),"stair witness uses actual upward tread collision geometry");
                var actual=JsonUtility.FromJson<GeoRegion>(File.ReadAllText("Builds/Verification/BuildingFacade/native-world.json"));Check(actual.buildings.Length==945&&actual.buildings.Count(h=>h.kind=="important_building")==7,"actual classified Pantside fixture contains945canonical buildings including7important source records");var reconstructed=WorldSnapshotCodec.Decode(WorldSnapshotCodec.CanonicalJson(actual),WorldSnapshotCodec.NetworkId(actual),WorldSnapshotCodec.Epoch(actual));Check(D(actual.buildings)==D(reconstructed.buildings),"actual945building canonical snapshot reconstruction preserves every source field in the digest");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/lan-building-digest.txt",$"PASS {checks} canonical snapshot feature digest and actual collider witness checks\nNo native host/guest reconstruction or GPU-submission acceptance claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{if(go)UnityEngine.Object.DestroyImmediate(go);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
}
