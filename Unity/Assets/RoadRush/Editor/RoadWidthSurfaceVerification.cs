using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;
using Newtonsoft.Json.Linq;using RoadRush.Core;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class RoadWidthSurfaceVerification
    {
        const string Flag="RRRoadWidthSurfaceVerification";static string output,fixtures;static int phase,checks,caseIndex,steps,retireFrame,errors;static double deadline;static GeoWorld sync,staged,constant;static GeoRegion regionA,regionB;static FinalizedRoadSurfacePlan planA,planB;static readonly Vector2Int[] keys={new Vector2Int(-2,0),new Vector2Int(-1,-1),new Vector2Int(-1,0)};static object job;static Transform hidden;static Mesh cancelledMesh,primitiveMesh;static Material sharedAsphalt;static readonly List<string> evidence=new List<string>();
        static RoadWidthSurfaceVerification(){if(!SessionState.GetBool(Flag,false))return;output=SessionState.GetString(Flag+"Output","");fixtures=SessionState.GetString(Flag+"Fixtures","");PlayerProfile.StorageOverride=Path.Combine(output,"isolated-profile.json");RegionCache.OverrideFolder=Path.Combine(output,"Regions");deadline=EditorApplication.timeSinceStartup+720;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;}
        public static void Run()
        {
            fixtures=Argument("-roadWidthFixtureRoot")??"/private/tmp/rr018-road-drafts/unity-fixtures";fixtures=Path.GetFullPath(fixtures);output=Path.GetFullPath("Builds/Verification/road-width-surfaces-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);foreach(var entry in JObject.Parse(File.ReadAllText(Path.Combine(fixtures,"manifest.json")))["files"]){string path=(string)entry["path"];var bytes=File.ReadAllBytes(Path.Combine(fixtures,path));if(DataPackageRules.Hash(bytes)!=(string)entry["sha256"])throw new Exception("Fixture checksum mismatch: "+path);File.WriteAllBytes(Path.Combine(output,path),bytes);}File.Copy(Path.Combine(fixtures,"manifest.json"),Path.Combine(output,"manifest.json"));fixtures=output;
            File.WriteAllText(Path.Combine(output,"isolated-profile.json"),JsonUtility.ToJson(new PlayerProfile{quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetString(Flag+"Output",output);SessionState.SetString(Flag+"Fixtures",fixtures);SessionState.SetBool(Flag,true);ProjectSetup.Configure();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static string Argument(string flag){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,flag);return i>=0&&i+1<a.Length?a[i+1]:null;}
        static void Log(string text,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert){errors++;File.AppendAllText(Path.Combine(output,"unity-errors.txt"),text+"\n"+stack+"\n");}}
        static void Check(bool value,string name){if(!value)throw new Exception("WIDTH SURFACE FAIL: "+name);checks++;evidence.Add(name);Debug.Log("WIDTH SURFACE PASS: "+name);}
        static object Invoke(GeoWorld world,string name,params object[] args)=>typeof(GeoWorld).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(world,args);
        static Dictionary<Vector2Int,GameObject> Chunks(GeoWorld world)=>(Dictionary<Vector2Int,GameObject>)typeof(GeoWorld).GetField("loaded",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
        static GeoRegion Read(bool constantWidth)
        {
            var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(Path.Combine(fixtures,constantWidth?"pantside-original.json":"pantside-admitted.json")));
            if(constantWidth){region.roadWidthSchema=1;region.roadWidthProfiles=region.roads.Where(r=>!r.tunnel).Select(r=>{double length=0;for(int i=1;i<r.points.Length;i++){double x=(double)r.points[i].x-r.points[i-1].x,y=(double)r.points[i].y-r.points[i-1].y,z=(double)r.points[i].z-r.points[i-1].z;length+=Math.Sqrt(x*x+y*y+z*z);}return new RoadWidthProfileDto{roadId=r.id,distances=new[]{0f,(float)length},widths=new[]{r.width,r.width},derivatives=new[]{0f,0f}};}).ToArray();}region.Prepare();return region;
        }
        static FinalizedRoadSurfacePlan Prepare(GeoRegion region){FinalizedRoadSurfacePlan result=null;foreach(var checkpoint in FinalizedRoadSurfacePlan.PrepareSteps(region,RoadMarkingPlan.Build(region.roads),region.roadWidthProfiles,region.RoadSurfaceHeight,r=>result=r)){}return result;}
        static GeoWorld Create(string name,GeoRegion region,FinalizedRoadSurfacePlan prepared)
        {var world=new GameObject(name).AddComponent<GeoWorld>();world.InitializePrepared(region,sharedAsphalt,prepared);world.gameObject.SetActive(false);return world;}
        static string Fingerprint(GameObject root)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){foreach(var t in root.GetComponentsInChildren<Transform>(true)){w.Write(t.name);foreach(float v in new[]{t.localPosition.x,t.localPosition.y,t.localPosition.z,t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w,t.localScale.x,t.localScale.y,t.localScale.z})w.Write(v);var f=t.GetComponent<MeshFilter>();if(f&&f.sharedMesh){var mesh=f.sharedMesh;foreach(var p in mesh.vertices){w.Write(p.x);w.Write(p.y);w.Write(p.z);}foreach(var n in mesh.normals){w.Write(n.x);w.Write(n.y);w.Write(n.z);}foreach(var uv in mesh.uv){w.Write(uv.x);w.Write(uv.y);}foreach(int i in mesh.triangles)w.Write(i);}var mc=t.GetComponent<MeshCollider>();if(mc){w.Write("collider");w.Write(mc.enabled);w.Write(mc.convex);w.Write(mc.sharedMesh==f.sharedMesh);}var box=t.GetComponent<BoxCollider>();if(box){w.Write("box");foreach(float v in new[]{box.center.x,box.center.y,box.center.z,box.size.x,box.size.y,box.size.z})w.Write(v);}}w.Flush();return DataPackageRules.Hash(stream.ToArray());}
        }
        static bool Rejected(Action action){try{action();return false;}catch(InvalidDataException){return true;}catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){return true;}}
        static object BeginStage(GeoWorld world,Vector2Int key){var value=Invoke(world,"StartStage",key,"verification");typeof(GeoWorld).GetField("stage",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(world,value);return value;}
        static bool Advance(GeoWorld world,object value)=>(bool)Invoke(world,"AdvanceStage",value);
        static void ValidateMeshes(GameObject root)
        {
            var filters=root.GetComponentsInChildren<MeshFilter>(true);bool geometry=true,paired=true,bounded=true;int asphalts=0,banks=0;foreach(var f in filters){bool asphalt=f.name=="Mapped road"||f.name.StartsWith("Finalized mapped asphalt")||f.name=="Junction apron";bool bank=f.name.Contains("embankments");if(!asphalt&&!bank)continue;var mesh=f.sharedMesh;var mc=f.GetComponent<MeshCollider>();paired&=mc&&ReferenceEquals(mesh,mc.sharedMesh)&&!mc.convex;if(asphalt)asphalts++;if(bank)banks++;if(f.name.StartsWith("Finalized")||bank)bounded&=mesh.vertexCount<=8192;var v=mesh.vertices;var uv=mesh.uv;geometry&=uv.Length==v.Length&&v.All(p=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z));}Check(asphalts>0&&banks>0,"actual chunk has asphalt and clipped bank meshes");Check(paired,"all tested road/bank renderers and colliders share exact owned mesh arrays");Check(geometry&&bounded,"road/bank vertices finite and upload batches bounded8192");
        }
        static void NoBankInsideAsphalt(GeoWorld world,GameObject root)
        {
            // This selected source chunk has only layer0 ground roads; bridge
            // decks/different-level crossings are intentionally excluded.
            var banks=root.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.Contains("embankments")).ToArray();int samples=0,overlap=0;foreach(var bank in banks){var mesh=bank.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;int stride=Math.Max(1,t.Length/3/300);for(int i=0;i<t.Length/3;i+=stride){var a=bank.transform.TransformPoint(v[t[i*3]]);var b=bank.transform.TransformPoint(v[t[i*3+1]]);var c=bank.transform.TransformPoint(v[t[i*3+2]]);if(Mathf.Abs(Vector3.Cross(b-a,c-a).y)<.000001f)continue;var p=(a+b+c)/3;var hits=Physics.RaycastAll(p+Vector3.up*500,Vector3.down,1000);samples++;foreach(var hit in hits){if(hit.collider.GetComponentInParent<GeoWorld>()!=world)continue;string n=hit.collider.name;if(n!="Mapped road"&&n!="Junction apron"&&!n.StartsWith("Finalized mapped asphalt"))continue;var roadMesh=hit.collider as MeshCollider;if(!roadMesh||hit.triangleIndex<0)continue;var rm=roadMesh.sharedMesh;var rt=rm.triangles;var rv=rm.vertices;int index=hit.triangleIndex*3;var ra=roadMesh.transform.TransformPoint(rv[rt[index]]);var rb=roadMesh.transform.TransformPoint(rv[rt[index+1]]);var rc=roadMesh.transform.TransformPoint(rv[rt[index+2]]);float Edge(Vector3 x,Vector3 y){var d=new Vector2(y.x-x.x,y.z-x.z);return Mathf.Abs(d.x*(p.z-x.z)-d.y*(p.x-x.x))/Mathf.Max(.000001f,d.magnitude);}if(Mathf.Min(Edge(ra,rb),Edge(rb,rc),Edge(rc,ra))>.002f){overlap++;break;}}}}Check(samples>0&&overlap==0,"sampled actual clipped-bank centroids never lie strictly inside same-level asphalt/apron colliders");evidence.Add("Independent Physics.Raycast bank probes="+samples+", overlaps="+overlap+"; deterministic per-mesh sampling,2mm edge tolerance, selected layer0 chunk only.");
        }
        static void Tick()
        {
            try
            {
                if(!EditorApplication.isPlaying)return;if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Width mesh verification timeout phase"+phase);
                if(phase==0)
                {
                    sharedAsphalt=Geometry.Material("Width surface verification asphalt",Color.gray);var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Shared primitive survivor";cube.transform.position=Vector3.down*10000;primitiveMesh=cube.GetComponent<MeshFilter>().sharedMesh;
                    var baseline=Read(true);var basePlan=Prepare(baseline);Check(basePlan.Parts.Count==0,"constant DTO baseline retains original strip geometry");constant=Create("Constant DTO baseline",baseline,basePlan);ValidateMeshes(Chunks(constant)[keys[0]]);UnityEngine.Object.Destroy(constant.gameObject);phase=1;return;
                }
                if(phase==1)
                {
                    regionA=Read(false);regionB=Read(false);planA=Prepare(regionA);planB=Prepare(regionB);Check(planA.Parts.Count==61&&planB.Parts.Count==61,"pre-admitted actual Pantside generates61 changed-road surface descriptors");sync=Create("Synchronous width fixture",regionA,planA);staged=Create("Staged width fixture",regionB,planB);var before=Chunks(sync).ToDictionary(p=>p.Key,p=>p.Value);string hash=Fingerprint(before[keys[0]]);
                    Check(Rejected(()=>sync.InitializePrepared(regionB,sharedAsphalt,planA)),"mismatched owner is rejected before world replacement");Check(Rejected(()=>sync.Initialize(regionA,sharedAsphalt)),"missing prepared descriptor plan is rejected before world replacement");Check(ReferenceEquals(sync.Region,regionA)&&before.All(p=>Chunks(sync).TryGetValue(p.Key,out var v)&&v==p.Value)&&hash==Fingerprint(Chunks(sync)[keys[0]]),"rejected initialization preserves previous region chunk objects and exact meshes");caseIndex=0;phase=2;return;
                }
                if(phase==2)
                {
                    var key=keys[caseIndex];if(Chunks(sync).ContainsKey(key))Invoke(sync,"Retire",key,"verification");if(Chunks(staged).ContainsKey(key))Invoke(staged,"Retire",key,"verification");Invoke(sync,"Build",key,"verification");job=BeginStage(staged,key);hidden=(Transform)job.GetType().GetField("root").GetValue(job);Check(!hidden.gameObject.activeSelf&&!staged.HasSurface(new Vector3(key.x*320+160,0,key.y*320+160)),"staged actual source chunk remains unpublished during construction");steps=0;phase=3;return;
                }
                if(phase==3)
                {
                    int slice=0;bool more=true;while(slice++<2048&&(more=Advance(staged,job)))steps++;if(more)return;typeof(GeoWorld).GetField("stage",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(staged,null);var key=keys[caseIndex];Check(Fingerprint(Chunks(sync)[key])==Fingerprint(Chunks(staged)[key]),"sync/staged actual width/T/Y chunk meshes UVs normals colliders bit-identical "+key);ValidateMeshes(Chunks(sync)[key]);if(caseIndex==0){sync.gameObject.SetActive(true);Physics.SyncTransforms();NoBankInsideAsphalt(sync,Chunks(sync)[key]);sync.gameObject.SetActive(false);}evidence.Add("Chunk "+key+" staged checkpoints="+steps);caseIndex++;phase=caseIndex<keys.Length?2:4;return;
                }
                if(phase==4)
                {
                    var key=keys[0];Invoke(staged,"Retire",key,"verification-cancel");job=BeginStage(staged,key);hidden=(Transform)job.GetType().GetField("root").GetValue(job);steps=0;phase=5;return;
                }
                if(phase==5)
                {
                    int slice=0;MeshFilter filter=null;while(slice++<2048){Advance(staged,job);steps++;filter=hidden.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.name.StartsWith("Finalized mapped asphalt"));if(filter)break;if(steps>1000000)throw new Exception("Cancellation allocation fixture budget");}if(!filter)return;cancelledMesh=filter.sharedMesh;Check(!hidden.gameObject.activeSelf&&filter.GetComponent<MeshCollider>().sharedMesh==cancelledMesh,"cancelled hidden stage has actual new-width asphalt render/collider allocation");Invoke(staged,"CancelStage","verification");Check(!staged.HasSurface(new Vector3(keys[0].x*320+160,0,keys[0].y*320+160)),"cancelled width stage never publishes physical surface");retireFrame=Time.frameCount;phase=6;return;
                }
                if(phase==6)
                {
                    if(Time.frameCount<=retireFrame)return;Check(!cancelledMesh&&primitiveMesh&&sharedAsphalt,"cancel destroys owned width mesh while shared primitive/material survive");Check(errors==0,"no Unity error assertion or exception logged during mesh/collider proof");File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"result","PASS"},{"checks",checks},{"evidence",new JArray(evidence)},{"scope","Actual Unity mesh/collider/staging/ownership checks; no rendered pixel or full lane-grounding claim."}}.ToString());Finish(0);
                }
            }catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Flag,false);Application.logMessageReceived-=Log;EditorApplication.update-=Tick;GeoWorld.VerificationBaselineScheduling=false;EditorApplication.Exit(code);}
    }
}
