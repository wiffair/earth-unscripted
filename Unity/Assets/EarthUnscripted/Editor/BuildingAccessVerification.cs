using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BuildingAccessVerification
    {
        static int checks;static void Check(bool ok,string text){if(!ok)throw new Exception("ACCESS FAIL: "+text);checks++;Debug.Log("ACCESS PASS: "+text);}
        static bool Create(float drop,float width,out BuildingAccessGeometry.Data data,Quaternion? rotation=null,Func<Vector3,bool> clear=null,Func<float,float,float> surface=null)
            =>BuildingAccessGeometry.TryCreateStepFlight(new Vector3(10,drop,20),rotation??Quaternion.identity,width,0,clear??(_=>true),surface??((x,z)=>0),out data);
        [Serializable] sealed class ShapeBaseline{public string id,positionHash,uvHash,visibleNormalHash;public Vector3 min,max;public float[] rayY;public Vector3[] rayNormal;}
        [Serializable] sealed class Baselines{public ShapeBaseline[] shapes;}
        static string HashFloats(System.Action<System.IO.BinaryWriter> write){using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){write(writer);writer.Flush();return EarthUnscripted.Core.DataPackageRules.Hash(stream.ToArray());}}
        static void V(BinaryWriter writer,Vector3 v){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
        static Baselines CaptureShapes()
        {
            var shapes=new System.Collections.Generic.List<ShapeBaseline>();foreach(float drop in new[]{.08f,.15f,.8f,2.2f})foreach(float yaw in new[]{0f,37f})foreach(bool slope in new[]{false,true})
            {
                float width=drop>=.8f?3:1.04f;var rotation=Quaternion.Euler(0,yaw,0);var threshold=new Vector3(10,drop,20);var forward=rotation*Vector3.forward;int count=Mathf.CeilToInt(drop/BuildingAccessGeometry.MaximumRiser);float reach=count*BuildingAccessGeometry.MinimumTread;
                float Surface(float x,float z)=>slope?(drop-.2f)*(1-((x-10)*forward.x+(z-20)*forward.z)/reach):0;
                if(!BuildingAccessGeometry.TryCreateStepFlight(threshold,rotation,width,0,_=>true,Surface,out var data))throw new Exception("Baseline fixture rejected "+drop+"/"+yaw+"/"+slope);
                var b=new ShapeBaseline{id=drop.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+yaw+"/"+slope,min=data.Bounds.min,max=data.Bounds.max};
                b.positionHash=HashFloats(w=>{foreach(int index in data.Tris)V(w,data.Verts[index]);});b.uvHash=HashFloats(w=>{foreach(int index in data.Tris){w.Write(data.UV[index].x);w.Write(data.UV[index].y);}});b.visibleNormalHash=HashFloats(w=>{foreach(int index in data.Tris)if(data.Normals[index].y>=-.00001f)V(w,data.Normals[index]);});
                var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=data.Verts,triangles=data.Tris};var go=new GameObject("Stair baseline collider");try
                {
                    var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();b.rayY=new float[count];b.rayNormal=new Vector3[count];for(int i=0;i<count;i++){var p=threshold+rotation*new Vector3(0,0,(i+.5f)*data.TreadDepth);if(!collider.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,6))throw new Exception("Baseline collider miss");b.rayY[i]=hit.point.y;b.rayNormal[i]=hit.normal;}
                }
                finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);}shapes.Add(b);
            }
            return new Baselines{shapes=shapes.ToArray()};
        }
        public static void CaptureCompactionBaseline()
        {try{Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/access-unshared-baseline.json",JsonUtility.ToJson(CaptureShapes(),true));EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        static bool SameBits(Vector3 a,Vector3 b)=>BitConverter.SingleToInt32Bits(a.x)==BitConverter.SingleToInt32Bits(b.x)&&BitConverter.SingleToInt32Bits(a.y)==BitConverter.SingleToInt32Bits(b.y)&&BitConverter.SingleToInt32Bits(a.z)==BitConverter.SingleToInt32Bits(b.z);
        static void CheckCompactionBaseline()
        {
            string path="Builds/Verification/access-unshared-baseline.json";if(!File.Exists(path))return;var baseline=JsonUtility.FromJson<Baselines>(File.ReadAllText(path));var current=CaptureShapes();bool equal=baseline.shapes.Length==current.shapes.Length;for(int i=0;i<baseline.shapes.Length&&equal;i++){var a=baseline.shapes[i];var b=current.shapes[i];equal&=a.id==b.id&&a.positionHash==b.positionHash&&a.uvHash==b.uvHash&&a.visibleNormalHash==b.visibleNormalHash&&SameBits(a.min,b.min)&&SameBits(a.max,b.max)&&a.rayY.Length==b.rayY.Length;for(int j=0;j<a.rayY.Length&&equal;j++)equal&=Math.Abs(a.rayY[j]-b.rayY[j])<.00001f&&Vector3.Distance(a.rayNormal[j],b.rayNormal[j])<.00001f;}
            Check(equal,"16pre-compaction world triangle/UV/visible-normal/bounds digests and actual collider rays remain unchanged; hidden underside normals may smooth");
        }
        public static void Run()
        {
            try
            {
                Check(Create(.15f,1,out var minimum)&&minimum.Count==1&&Math.Abs(minimum.Verts[0].y-.15f)<.0001f,"minimum15cm drop creates a supported landing at the doorway threshold");Check(Create(2,3,out var maximum)&&maximum.Count<=12&&maximum.Rise<=.19f&&maximum.TreadDepth>=.28f&&maximum.Reach<=3.5f&&maximum.Verts.Length<=BuildingAccessGeometry.MaximumVertices,"maximum drop and width remain bounded with safe risers treads and reach");
                Check(!Create(.049f,1,out _)&&!Create(2.281f,1,out _)&&!Create(.8f,.49f,out _)&&!Create(.8f,3.01f,out _),"out-of-contract drop and width are rejected");Check(!Create(.8f,1,out _,Quaternion.Euler(1,0,0)),"tilted frames cannot produce sloped treads");
                Check(Create(.08f,1,out var small)&&small.Count==1&&small.Rise<=.19f,"8cm doorstep has a level supported landing within the shared minimum-drop contract");Check(Create(2.2f,1,out var tall)&&tall.Count==12&&tall.Rise<=.19f&&tall.Reach<=3.5f,"2.2m access uses12ordinary safe risers without expanding reach or geometry budgets");
                Check(!Create(.8f,1,out _,surface:(x,z)=>float.NaN),"missing or nonfinite terrain cannot create floating supports");Check(!Create(.8f,1,out _,surface:(x,z)=>x>10?.9f:0),"terrain rising into any tread rejects the flight");Check(!Create(.8f,1,out _,clear:p=>p.x<10.49f),"clearance callback covers the entire step width including outer boundary");Check(!Create(.8f,1,out _,clear:p=>p.z<21),"road water or neighbor obstruction at the far depth rejects the flight");
                Check(!Create(.8f,1,out _,surface:(x,z)=>z>20.7f?-.5f:0),"terrain discontinuity and unplanned final landing drop reject the flight");
                // A continuous downhill garden targets the far end of the flight;
                // the final riser must descend no more than19cm to that ground.
                float drop=.8f;int count=Mathf.CeilToInt(drop/.19f);float back=(count-1)*.28f,slope=(drop-.2f)/back;
                Check(Create(drop,1,out var hillside,surface:(x,z)=>(drop-.2f)-(drop-.2f)/(count*.28f)*(z-20)),"existing downhill garden supports a doorway-level landing and far-end ground target");Check(!Create(drop,1,out _,surface:(x,z)=>(drop-.2f)-slope*(z-20)),"a target above actual far-end ground is rejected when it creates an excessive final drop");
                var searchThreshold=new Vector3(10,.8f,20);Func<float,float,float> lowSurface=(x,z)=>.05f+.10f*(x-10);var trials=new System.Collections.Generic.List<BuildingAccessGeometry.SearchAttempt>();
                Check(!BuildingAccessGeometry.TryCreateStepFlight(searchThreshold,Quaternion.identity,1,.10f,_=>true,lowSurface,out _,out var highFailure)&&highFailure==BuildingAccessGeometry.Rejection.FinalLandingDrop,"maximum far-edge target can fail strict final landing while a lower target remains possible");
                Check(BuildingAccessGeometry.TryFindStepFlight(searchThreshold,Quaternion.identity,1,lowSurface(10,20),_=>true,lowSurface,_=>true,out var lowFlight,out var lowResult,trials.Add)&&lowResult.Variant==0&&lowResult.Accepted&&lowFlight.Rise<=.19f,"actual bounded low-target selector recovers a valid flight while preserving all safety guards");
                var midThreshold=new Vector3(10,.271f,20);Func<float,float,float> midSurface=(x,z)=>.08f+.16f*(x-10);trials.Clear();
                Check(BuildingAccessGeometry.TryFindStepFlight(midThreshold,Quaternion.identity,1,midSurface(10,20),_=>true,midSurface,_=>true,out var midFlight,out var midResult,trials.Add)&&midResult.Variant==1&&trials.Count==2&&trials[0].Reason==BuildingAccessGeometry.Rejection.SurfaceAboveTread&&midFlight.Rise<=.19f,"middle target succeeds when lower target would bury a tread without relaxing burial or landing checks");
                Check(!BuildingAccessGeometry.TryCreateStepFlight(midThreshold,Quaternion.identity,1,.16f,_=>true,midSurface,out _,out var midHighFailure)&&midHighFailure==BuildingAccessGeometry.Rejection.FinalLandingDrop,"same middle-target fixture rejects the maximum alternative as an excessive last drop");
                trials.Clear();Check(!BuildingAccessGeometry.TryFindStepFlight(searchThreshold,Quaternion.identity,1,0,_=>true,(x,z)=>0,_=>false,out _,out _,trials.Add)&&trials.Count==3&&trials.All(t=>t.RoadBlocked),"all target variants retain mandatory whole-flight road clearance and bounded attempt count");
                int samples=0,clearance=0;Check(Create(2,3,out _,clear:p=>{clearance++;return true;},surface:(x,z)=>{samples++;return 0;})&&samples<=1500&&clearance==samples*2,"full-envelope terrain and clearance sampling has an explicit bounded count");
                var rotation=Quaternion.Euler(0,37,0);Check(Create(.8f,1.2f,out var data,rotation),"yaw-rotated world-space flight generates successfully");
                Check(data.Verts.Length==data.Normals.Length&&data.Verts.Length==data.UV.Length&&data.Tris.Length%3==0&&data.Tris.All(i=>i>=0&&i<data.Verts.Length),"batched vertex normal UV and index channels are valid");Check(data.Normals.All(n=>Math.Abs(n.magnitude-1)<.0001f)&&data.Verts.All(p=>data.Bounds.SqrDistance(p)<.000001f),"all triangle normals are finite unit vectors and bounds include supports");
                bool top=true;for(int step=0;step<data.Count;step++)for(int j=0;j<4;j++){int at=step*8+j;top&=Math.Abs(data.Verts[at].y-(.8f-data.Rise*step))<.0001f&&data.Normals[at].y>.999f;}Check(top,"every tread is level with first at threshold and final one safe riser above target ground");
                Check(Math.Abs(data.UV[1].x-data.UV[0].x-1.2f)<.0001f&&Math.Abs(data.UV[2].y-data.UV[0].y-.28f)<.0001f,"concrete/stone UV coordinates retain metre dimensions after yaw rotation");
                var mesh=new Mesh{name="Original access flight test",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};var go=new GameObject("Single batched access collider fixture");try
                {
                    mesh.vertices=data.Verts;mesh.triangles=data.Tris;mesh.uv=data.UV;mesh.normals=data.Normals;mesh.RecalculateBounds();var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
                    bool hits=true;for(int i=0;i<data.Count;i++)foreach(float x in new[]{-.5f,0,.5f})
                    {var point=new Vector3(10,.8f,20)+rotation*new Vector3(x,0,(i+.5f)*data.TreadDepth);hits&=collider.Raycast(new Ray(point+Vector3.up*2,Vector3.down),out var hit,5)&&hit.normal.y>.99f&&Math.Abs(hit.point.y-(.8f-data.Rise*i))<.001f;}
                    Check(hits,"actual single MeshCollider has correctly wound collision across every tread width");var under=new Vector3(10,-1,20)+rotation*new Vector3(0,0,data.Reach*.5f);Check(collider.Raycast(new Ray(under,Vector3.up),out var underside,3)&&Math.Abs(underside.point.y+.05f)<.001f&&underside.normal.y<-.99f,"closed underside embeds5cm into existing flat ground without floating supports");Check(go.GetComponentsInChildren<MeshCollider>().Length==1&&go.transform.childCount==0,"flight needs one batch collider and zero per-step scene objects");
                }
                finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);}
                var roads=new GeoRegion{minX=-100,minZ=-100,width=200,depth=200,nx=2,nz=2,heights=new float[4],waters=Array.Empty<GeoWater>(),roads=new[]{new GeoRoad{id="narrow",start="a",end="b",width=2,points=new[]{new Vector3(-90,0,9.6f),new Vector3(90,0,9.6f)}},new GeoRoad{id="wide",start="c",end="d",width=40,points=new[]{new Vector3(-90,0,30),new Vector3(90,0,30)}}}};roads.Prepare();var footprint=new[]{new Vector3(-.5f,0,6.5f),new Vector3(.5f,0,6.5f),new Vector3(.5f,0,7.5f),new Vector3(-.5f,0,7.5f),new Vector3(-.5f,0,6.5f)};var nearest=roads.Nearest(new Vector3(0,0,7),out _,out float distance);
                Check(nearest.id=="narrow"&&distance>nearest.width*.5f+.61f&&!roads.FootprintOverlapsRoad(footprint,1),"fixture distinguishes a clear nearest narrow lane from another road's wider bank envelope");Check(roads.FootprintOverlapsRoadWithMargins(footprint,r=>r.id=="wide"?3.01f:.61f),"whole-flight spatial clearance catches wider non-nearest road bank with its own margin");
                CheckCompactionBaseline();
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/building-access.txt",$"PASS {checks} original step geometry sampling clearance bounds UV winding and single collider checks\nSample-based terrain/clearance validation at<=.1m; arbitrary continuous functions require conservative caller margins.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
