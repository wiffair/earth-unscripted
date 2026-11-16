using System;using System.IO;using System.Linq;using System.Collections;using UnityEditor;using UnityEngine;
namespace RoadRush.Editor
{
    // Root calls this with its ACTUAL verifier-issued scoped plan. No proof mint,
    // provider request, profile save, persistent preference or application launch.
    public static class ScopedPreviewLifetimeVerification
    {
        [Serializable]public sealed class Report{public string status;public int checks;public string scope="Editor-owned scoped→normal initialization/lifetime only; no completeworld admission";public int partialMeshesBeforeDestroy,survivingMeshesAfterDestroy;public string[] survivorNames;public bool sharedPrimitiveAlive,roadMaterialAlive,groundMaterialAlive;}
        static void Need(Report r,bool condition,string why){if(!condition)throw new Exception("SCOPED LIFETIME FAIL "+why);r.checks++;}
        static void Drain(IEnumerator work){try{while(work.MoveNext()){} }finally{(work as IDisposable)?.Dispose();}}
        public static Report Verify(PreparedWorldMeshPlan plan,string output)
        {
            if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>()||Directory.Exists(output)||!output.StartsWith("/private/tmp/rr019-scoped-lifetime-",StringComparison.Ordinal))throw new Exception("Fresh isolated Editor fixture required");Directory.CreateDirectory(output);var report=new Report();GameObject root=null,cube=null;Material road=null,ground=null;string previous=PlayerProfile.StorageOverride;
            try
            {
                PlayerProfile.StorageOverride=Path.Combine(output,"unused-profile.json");var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)throw new Exception("URP Lit missing");road=new Material(shader);ground=new Material(shader);cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var shared=cube.GetComponent<MeshFilter>().sharedMesh;
                root=new GameObject("Scoped lifetime complete fixture");var world=root.AddComponent<GeoWorld>();Drain(world.BuildVerifiedScopedPreview(plan,road,ground));var point=new Vector3((float)(plan.MinX+16),0,(float)(plan.MinZ+16));var preview=root.GetComponentInChildren<ScopedWorldMeshPreview>(true);var meshes=root.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(o=>o.Mesh).ToArray();int before=world.SurfaceGeneration;
                Need(report,ReferenceEquals(world.ScopedMeshPlan,plan)&&preview.Ready&&world.HasSurface(point),"actual completed scoped surface initially current");
                var invalid=GeoRegion.Load("dee-valley");invalid.roadWidthSchema=1;bool refused=false;try{world.Initialize(invalid,road,true);}catch(InvalidDataException){refused=true;}
                Need(report,refused&&ReferenceEquals(world.ScopedMeshPlan,plan)&&preview.Ready&&meshes.All(m=>m)&&world.SurfaceGeneration==before,"failed preparation guard retains current scoped surface before mutation");
                world.Initialize(GeoRegion.Load("dee-valley"),road,true);
                Need(report,world.ScopedMeshPlan==null&&!world.HasSurface(point)&&world.SurfaceGeneration>before,"valid normal reinitialize revokes scoped queries before deferred coverage");
                Need(report,!preview&&meshes.All(m=>!m)&&root.GetComponentsInChildren<ScopedWorldMeshPreview>(true).Length==0,"old scoped objects and generated meshes released");Need(report,shared&&road&&ground,"shared fixture assets survive teardown");UnityEngine.Object.DestroyImmediate(root);root=null;
                root=new GameObject("Scoped lifetime suspended fixture");world=root.AddComponent<GeoWorld>();var pending=world.BuildVerifiedScopedPreview(plan,road,ground);Need(report,pending.MoveNext(),"actual partial upload suspended after first allocation");var partial=root.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(o=>o.Mesh).ToArray();Need(report,partial.Length>0&&world.ScopedMeshPlan==null,"partial meshes exist only hidden/unpublished");world.Initialize(GeoRegion.Load("dee-valley"),road,true);
                bool cancelled=false;try{pending.MoveNext();}catch(OperationCanceledException){cancelled=true;}finally{(pending as IDisposable)?.Dispose();}
                Need(report,cancelled&&world.ScopedMeshPlan==null&&partial.All(m=>!m),"old suspended iterator cannot publish after normal reinitialize");Need(report,root.GetComponentsInChildren<ScopedWorldMeshPreview>(true).Length==0&&shared&&road&&ground,"superseded iterator cannot erase new world or shared assets");UnityEngine.Object.DestroyImmediate(root);root=null;
                root=new GameObject("Scoped lifetime destruction fixture");world=root.AddComponent<GeoWorld>();pending=world.BuildVerifiedScopedPreview(plan,road,ground);Need(report,pending.MoveNext(),"destruction fixture owns partial allocated geometry");partial=root.GetComponentsInChildren<GeneratedMeshOwner>(true).Select(o=>o.Mesh).ToArray();var partialNames=partial.Select(m=>m?m.name:"<null-before-destroy>").ToArray();report.partialMeshesBeforeDestroy=partial.Length;UnityEngine.Object.DestroyImmediate(root);root=null;(pending as IDisposable)?.Dispose();report.survivingMeshesAfterDestroy=partial.Count(m=>m);report.survivorNames=partial.Select((m,i)=>new{m,i}).Where(p=>p.m).Select(p=>partialNames[p.i]).ToArray();report.sharedPrimitiveAlive=shared;report.roadMaterialAlive=road;report.groundMaterialAlive=ground;Need(report,report.survivingMeshesAfterDestroy==0&&report.sharedPrimitiveAlive&&report.roadMaterialAlive&&report.groundMaterialAlive,"world destruction disposes partial preview and retains shared assets");
                Need(report,!File.Exists(PlayerProfile.StorageOverride),"lifetime verification performed no career writes");report.status="PASS";return report;
            }
            catch(Exception e){report.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}
            finally{PlayerProfile.StorageOverride=previous;if(root)UnityEngine.Object.DestroyImmediate(root);if(cube)UnityEngine.Object.DestroyImmediate(cube);if(road)UnityEngine.Object.DestroyImmediate(road);if(ground)UnityEngine.Object.DestroyImmediate(ground);File.WriteAllText(Path.Combine(output,"receipt.json"),CanonicalGeography13.Write(report,true));}
        }
    }
}
