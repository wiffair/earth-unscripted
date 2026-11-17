using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class TerrainWaterRegressionVerification
 {
  const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
  public static void Run()
  {
   int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
   var type=typeof(NewbridgeWorld);var valid=type.GetMethod("ValidFarPatch",Hidden);
   bool Valid(NewbridgeWorld.FarPatch p)=>(bool)valid.Invoke(null,new object[]{p});
   var patch=new NewbridgeWorld.FarPatch{nx=2,nz=2,step=64,heights=new float[]{1,2,3,4}};
   Check(Valid(patch),"Valid distant relief accepted");Check(!Valid(null),"Null distant response rejected");
   patch.heights=null;Check(!Valid(patch),"Missing elevation array rejected");patch.heights=new float[]{1,2,3,float.NaN};Check(!Valid(patch),"Nonfinite source heights rejected");
   patch.heights=new float[]{1,2,3,4};patch.step=0;Check(!Valid(patch),"Invalid distant grid rejected");
   var root=new GameObject("Terrain retirement regression");var terrainRoot=new GameObject("Chunk");var mesh=new Mesh();
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();
    var map=new NewbridgeWorld.Map{coreMinX=0,coreMinZ=0,coreMaxX=1536,coreMaxZ=1536,minX=0,minZ=0,maxX=1536,maxZ=1536,gridStep=768,nx=3,nz=3,heights=new float[]{27,27,27,27,27,27,27,27,27},frame=new NewbridgeWorld.FrameInfo{height_origin=-10}};
    type.GetField("map",Hidden).SetValue(world,map);
    var areas=(Dictionary<Vector2Int,NewbridgeWorld.Map>)type.GetField("areas",Hidden).GetValue(world);areas[Vector2Int.zero]=map;
    var resident=(NewbridgeWorld.FarPatch)type.GetMethod("ResidentTerrainPatch",Hidden).Invoke(world,null);
    Check(Valid(resident),"Resident relief can render before network response");Check(resident.nx==25&&resident.nz==25,"Resident fallback retains actual 1536m bounds");
    Check(Mathf.Abs(resident.heights[0]-17)<.001f,"Resident terrain uses regional vertical datum");
    Check(Mathf.Abs(resident.heights[resident.heights.Length-1]-17)<.001f,"Resident boundary samples retain available halo elevations");
    var shoreline=new[]{new Vector3(0,0,0),new Vector3(3000,0,0),new Vector3(3000,0,3000),new Vector3(0,0,3000)};
    var island=new[]{new Vector3(100,0,100),new Vector3(200,0,100),new Vector3(200,0,200),new Vector3(100,0,200)};
    type.GetField("coastalPatch",Hidden).SetValue(world,new NewbridgeWorld.CoastalPatch{seaLevel=0,sea=new[]{new NewbridgeWorld.SeaPolygon{outline=shoreline,holes=new[]{new NewbridgeWorld.CoastRing{points=island}}}}});
    type.GetField("farPatch",Hidden).SetValue(world,new NewbridgeWorld.FarPatch{minX=0,minZ=0,step=3000,nx=2,nz=2,heights=new float[]{123,123,123,123}});
    var scenery=type.GetMethod("SceneryHeight",Hidden);
    Check((float)scenery.Invoke(world,new object[]{64f,64f})<-.08f,"Resident scenery within mapped sea is carved below water");
    Check((float)scenery.Invoke(world,new object[]{2000f,2000f})<-.08f,"Distant coarse scenery within mapped sea is carved below water");
    Check(Mathf.Abs((float)scenery.Invoke(world,new object[]{128f,128f})-27)<.001f,"Mapped island holes preserve original land elevation");
    type.GetField("coastalPatch",Hidden).SetValue(world,null);
    var terrain=new GameObject("Terrain");terrain.transform.SetParent(terrainRoot.transform,false);
    mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=new Vector3[1000000];
    terrain.AddComponent<MeshFilter>().sharedMesh=mesh;terrain.AddComponent<MeshCollider>();
    var chunks=(Dictionary<Vector2Int,GameObject>)type.GetField("chunks",Hidden).GetValue(world);chunks[Vector2Int.zero]=terrainRoot;
    var refresh=(IEnumerator)type.GetMethod("RefreshCoastalBeds",Hidden).Invoke(world,null);
    Check(refresh.MoveNext(),"Long coastal resampling yields before completion");
    UnityEngine.Object.DestroyImmediate(terrainRoot);
    int iterations=0;while(refresh.MoveNext()){if(++iterations>5)throw new Exception("Retired terrain was not skipped");}
    Check(true,"Retired terrain safely skipped after coroutine yield");
   }
   finally{if(terrainRoot)UnityEngine.Object.DestroyImmediate(terrainRoot);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
   Debug.Log("TERRAIN WATER REGRESSION PASS "+checks);
  }
 }
}
