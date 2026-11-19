using System;
using System.Reflection;
using System.Threading.Tasks;
using EarthUnscripted.NewbridgePrototype;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.Editor
{
 public static class LoadingPipelineVerification
 {
  public static void Run()
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception("LOADING PIPELINE "+why);}
   var flags=BindingFlags.Static|BindingFlags.NonPublic;
   var budget=typeof(GameSession).GetMethod("LoadingRenderBudget",flags);var restore=typeof(GameSession).GetMethod("RestoreDrivingRendering",flags);
   int original=OnDemandRendering.renderFrameInterval,fps=Application.targetFrameRate;float physics=Time.fixedDeltaTime;
   try{budget.Invoke(null,new object[]{false});Check(Application.targetFrameRate==120&&OnDemandRendering.renderFrameInterval==4,"loading updates stay responsive with30 redraws");budget.Invoke(null,new object[]{true});Check(Application.targetFrameRate==30&&OnDemandRendering.renderFrameInterval==1,"failure uses30 update/render budget");budget.Invoke(null,new object[]{false});restore.Invoke(null,null);Check(OnDemandRendering.renderFrameInterval==1&&Time.fixedDeltaTime==physics,"retry/drive restore full rendering without changing physics");}
   finally{OnDemandRendering.renderFrameInterval=original;Application.targetFrameRate=fps;}
   var root=new GameObject("Deferred building collider proof");
   try
   {
    var batchType=typeof(NewbridgeWorld).GetNestedType("Batch",BindingFlags.NonPublic);var batch=Activator.CreateInstance(batchType,new object[]{"Mapped building massing",null,true});
    var incoming=Activator.CreateInstance(batchType,new object[]{"Incoming boot walls",null,false});
    batchType.GetMethod("Restore").Invoke(batch,new object[]{new FinishedGeometryStore.Part{vertices=Array.Empty<Vector3>(),normals=Array.Empty<Vector3>(),indices=Array.Empty<int>()}});
    batchType.GetMethod("Quad").Invoke(incoming,new object[]{Vector3.zero,Vector3.forward,Vector3.forward+Vector3.right,Vector3.right});
    batchType.GetMethod("Append").Invoke(batch,new object[]{incoming});
    var timing=new NewbridgeWorld.ChunkTiming();batchType.GetMethod("PublishVisual").Invoke(batch,new object[]{root.transform,timing});
    Check(root.GetComponentInChildren<MeshFilter>().sharedMesh.normals.Length==4,"adding boot shells to a transport-only restore regenerates complete normals");
    var collider=root.GetComponentInChildren<MeshCollider>();Check(collider&&collider.sharedMesh==null&&root.GetComponentInChildren<MeshRenderer>().enabled,"shell visible while collider is deferred");
    batchType.GetMethod("StartColliderBake").Invoke(batch,new object[]{true});((Task)batchType.GetProperty("Baking").GetValue(batch)).GetAwaiter().GetResult();batchType.GetMethod("FinishCollider").Invoke(batch,null);
    Check(collider.sharedMesh&&collider.sharedMesh.vertexCount==4,"approaching building receives matching cooked collider");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
   Debug.Log("LOADING PIPELINE PASS "+checks);
  }
 }
}
