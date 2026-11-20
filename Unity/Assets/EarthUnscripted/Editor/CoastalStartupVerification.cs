using System;
using System.Collections;
using System.IO;
using System.Reflection;
using EarthUnscripted.NewbridgePrototype;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class CoastalStartupVerification
 {
  const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
  static int checks;
  static void Check(bool ok,string label){if(!ok)throw new Exception("COASTAL STARTUP FAIL: "+label);checks++;}
  static object Get(object o,string name)=>o.GetType().GetField(name,Hidden).GetValue(o);
  static void Set(object o,string name,object value)=>o.GetType().GetField(name,Hidden).SetValue(o,value);
  static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Hidden).Invoke(o,args);
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
  public static void Run()
  {
   checks=0;var root=new GameObject("Initial coastline before terrain fixture");var folder=Path.Combine(Path.GetTempPath(),"EarthUnscriptedCoastStartup-"+Guid.NewGuid().ToString("N"));
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();Set(world,"finishedStore",new FinishedGeometryStore(folder));
    // A finished chunk always has verified owned source and its spatial index.
    // Keep that real prerequisite while varying only coastline availability.
    var source=new NewbridgeWorld.Map{tileX=0,tileZ=0,coreMinX=0,coreMinZ=0,coreMaxX=1536,coreMaxZ=1536,features=Array.Empty<NewbridgeWorld.Feature>(),buildings=Array.Empty<NewbridgeWorld.Building>()};
    ((System.Collections.Generic.Dictionary<Vector2Int,NewbridgeWorld.Map>)Get(world,"areas"))[Vector2Int.zero]=source;
    ((System.Collections.Generic.Dictionary<NewbridgeWorld.Map,string>)Get(world,"finishedSourceHashes"))[source]=new string('a',64);
    Call(world,"IndexMap",source);
    string before=(string)Call(world,"FinishedChunkKey",Vector2Int.zero);
    Check(before!=null,"owned verified source can produce a finished key before coastline arrives");
    var outline=new[]{new Vector3(-100,0,-100),new Vector3(100,0,-100),new Vector3(100,0,100),new Vector3(-100,0,100)};
    var hole=new[]{new Vector3(-5,0,-5),new Vector3(5,0,-5),new Vector3(5,0,5),new Vector3(-5,0,5)};
    var patch=new NewbridgeWorld.CoastalPatch{centreX=0,centreZ=0,radius=16000,seaLevel=8.18f,
     sea=new[]{new NewbridgeWorld.SeaPolygon{outline=outline,vertices=new[]{outline[0],outline[1],outline[2]},holes=new[]{new NewbridgeWorld.CoastRing{points=hole}}}},
     shore=new[]{new NewbridgeWorld.CoastRing{points=outline}},beaches=Array.Empty<NewbridgeWorld.BeachPolygon>()};
    string json=JsonUtility.ToJson(patch);var parser=typeof(NewbridgeWorld).GetMethod("ParseCoastalPatch",BindingFlags.NonPublic|BindingFlags.Static);
    var prepared=parser.Invoke(null,new object[]{json});Check(prepared!=null,"real shared parser accepts coastline");
    Call(world,"AdoptCoastalPatch",prepared);
    Check(world.IsMappedSeaAt(new Vector3(40,0,30)),"first terrain generation already sees offshore sea");
    Check(!world.IsMappedSeaAt(Vector3.zero),"island holes remain land");Check(!world.IsMappedSeaAt(new Vector3(120,0,0)),"inland remains land");
    float bed=(float)Call(world,"LowerSeaBed",40f,30f,22f);Check(bed<world.MappedSeaLevel-.5f,"first offshore terrain is below water, before any refresh");
    Check((float)Call(world,"LowerSeaBed",0f,0f,22f)==22f,"island ground preserved");
    Check((float)Call(world,"LowerSeaBed",120f,0f,22f)==22f,"inland ground preserved");
    string after=(string)Call(world,"FinishedChunkKey",Vector2Int.zero);Check(before!=after,"pre-coast finished meshes cannot alias initial coastal geometry");
    Check((string)Get(world,"finishedCoastHash")==FinishedGeometryStore.Digest(json),"original immutable source checksum determines cache identity");
    Check(ReferenceEquals(Get(world,"finishedCoastIdentity"),((NewbridgeWorld.CoastalPatch)Get(world,"coastalPatch")).shore),"cache identity adopts same immutable shoreline");
    Check(!world.CoastalBedsReady,"later adoption never claims old beds are ready");Check((int)Get(world,"coastalRevision")==1,"one adoption creates one revision");
    Check(parser.Invoke(null,new object[]{"{}"})==null,"invalid optional source is ignored");
    patch.radius=-1;Check(parser.Invoke(null,new object[]{JsonUtility.ToJson(patch)})==null,"negative coverage rejected");
    var budget=(IEnumerator)Call(world,"FetchCoastalPatch",0f,null);Check(!budget.MoveNext(),"exhausted optional budget performs no request and gracefully returns");
    var cancelled=(IEnumerator)Call(world,"FetchCoastalPatch",4f,new Func<bool>(()=>true));bool canceled=false;try{cancelled.MoveNext();}catch(OperationCanceledException){canceled=true;}
    Check(canceled,"cancellation precedes optional network request");
    // Actual first driving-loop step must sleep, not refetch/rebuild a surface
    // that the loading sequence has already published for this observer.
    Set(world,"embedded",true);Set(world,"ready",true);Set(world,"observer",Vector3.zero);Set(world,"lastCoastalSurfaceCentre",Vector3.zero);
    var seaRoot=new GameObject("Already published initial sea");seaRoot.transform.SetParent(root.transform,false);Set(world,"coastalRoot",seaRoot);
    var loop=(IEnumerator)Call(world,"CoastalLoop");Check(loop.MoveNext()&&loop.Current is WaitForSecondsRealtime,"first ready frame reuses initial sea without duplicate rebuild");
    (loop as IDisposable)?.Dispose();Check((string)Call(world,"FinishedChunkKey",Vector2Int.zero)==after,"first loop preserves exact geometry identity");
    Debug.Log("COASTAL STARTUP PASS "+checks);
   }
   finally{UnityEngine.Object.DestroyImmediate(root);if(Directory.Exists(folder))Directory.Delete(folder,true);}
  }
 }
}
