using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class PublishedSceneryGroundVerification
 {
  public static void Batch(){try{Run();TerrainWaterRegressionVerification.Run();UnityEditor.EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
  public static void Run()
  {
   int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
   const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
   var root=new GameObject("Published scenery support fixture");var far=new GameObject("Published fan terrain");var fine=new GameObject("Published fine chunk");var mesh=new Mesh();
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();var type=typeof(NewbridgeWorld);
    void Set(string name,object value)=>type.GetField(name,flags).SetValue(world,value);
    var vertices=new List<Vector3>{new Vector3(0,0,0),new Vector3(0,0,64),new Vector3(64,0,64),new Vector3(64,0,0),new Vector3(32,20,32)};
    var triangles=new List<int>{4,0,1,4,1,2,4,2,3,4,3,0};
    Set("farRoot",far);Set("publishedGroundGrid",new NewbridgeWorld.FarPatch{minX=0,minZ=0,step=64,nx=2,nz=2});Set("publishedGroundVertices",vertices);Set("publishedGroundTriangles",triangles);Set("publishedGroundCells",new[]{0,12});
    Check(world.TryPublishedSceneryGround(16,16,out float fan)&&Mathf.Abs(fan-10)<.001f,"Actual centre fan is sampled instead of zero-height bilinear corners");
    Check(!world.TryPublishedSceneryGround(200,200,out _),"Unsupported distant coordinates are not invented");
    ulong fanStamp=world.PublishedScenerySupportStamp(16,16);
    Check(fanStamp!=0,"Published fan support has a cached geometry signature");
    Set("sceneryGroundRevision",1);Check(world.PublishedScenerySupportStamp(16,16)==fanStamp,"Unrelated terrain revision preserves an unchanged far-cell signature");
    Set("publishedGroundVertices",new List<Vector3>(vertices));
    Check(world.PublishedScenerySupportStamp(16,16)==fanStamp,"Equivalent republished far geometry preserves its support signature");
    var changedVertices=new List<Vector3>(vertices);changedVertices[4]=new Vector3(32,21,32);Set("publishedGroundVertices",changedVertices);
    Check(world.PublishedScenerySupportStamp(16,16)!=fanStamp,"Changed far-cell terrain invalidates old tree matrix support");Set("publishedGroundVertices",vertices);
    var terrain=new GameObject("Terrain");terrain.transform.SetParent(fine.transform,false);
    mesh.vertices=new[]{new Vector3(0,7,0),new Vector3(0,7,64),new Vector3(64,7,64),new Vector3(64,7,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
    terrain.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=terrain.AddComponent<MeshRenderer>();terrain.AddComponent<MeshCollider>().sharedMesh=mesh;
    var chunks=(IDictionary)type.GetField("chunks",flags).GetValue(world);chunks.Add(Vector2Int.zero,fine);Physics.SyncTransforms();
    Check(world.TryPublishedSceneryGround(16,16,out float local)&&Mathf.Abs(local-7)<.01f,"Actual published fine collision terrain owns the tree base");
    ulong fineStamp=world.PublishedScenerySupportStamp(16,16);Check(fineStamp!=0&&fineStamp!=fanStamp,"Fine publication replaces the old far support signature");
    var preview=UnityEngine.Object.Instantiate(mesh);var previewVertices=preview.vertices;for(int i=0;i<previewVertices.Length;i++)previewVertices[i].y=3;preview.vertices=previewVertices;terrain.GetComponent<MeshFilter>().sharedMesh=preview;
    Set("sceneryGroundRevision",2);
    Check(!world.TryPublishedSceneryGround(16,16,out _),"A new visible preview never uses its old unmatched collider to ground a tree");
    Check(world.PublishedScenerySupportStamp(16,16)==0,"Uncooked visual preview withholds old rendered tree records immediately");
    terrain.GetComponent<MeshFilter>().sharedMesh=mesh;UnityEngine.Object.DestroyImmediate(preview);
    renderer.enabled=false;
    Check(world.TryPublishedSceneryGround(16,16,out local)&&Mathf.Abs(local-10)<.001f,"Invisible fine terrain does not suppress supported far terrain");
    fine.SetActive(false);
    Check(world.TryPublishedSceneryGround(16,16,out local)&&Mathf.Abs(local-10)<.001f,"Inactive chunk dictionaries do not hide the far terrain");
    Set("publishedGroundCells",new[]{0,0});
    Check(!world.TryPublishedSceneryGround(16,16,out _),"A real unrendered mesh cell is unsupported");
    Set("publishedGroundCells",new[]{0,12});Set("coastalPatch",new NewbridgeWorld.CoastalPatch{seaLevel=30,sea=new[]{new NewbridgeWorld.SeaPolygon{outline=new[]{new Vector3(-100,0,-100),new Vector3(100,0,-100),new Vector3(100,0,100),new Vector3(-100,0,100)}}}});
    Check(!world.TryPublishedSceneryGround(16,16,out _),"Underwater tree placement is rejected using mapped sea");
    // Simulate an old fine loch floor and a newly carved far backfill.
    fine.SetActive(true);renderer.enabled=true;foreach(var index in new[]{0,1,2,3,4})vertices[index]=new Vector3(vertices[index].x,-5,vertices[index].z);
    var suppress=(IEnumerator)type.GetMethod("SuppressUnpreparedCoastalTerrain",flags).Invoke(world,null);while(suppress.MoveNext()){}
    Check(!renderer.enabled&&terrain.GetComponent<MeshCollider>().enabled,"Old coastal visuals are hidden while physical collision support remains");
    Check(world.TryPublishedTerrainHeight(16,16,out float visual)&&Mathf.Abs(visual+5)<.001f,"Carved published far triangles fill suppressed fine visuals immediately");
    type.GetMethod("RestoreCoastalTerrain",flags).Invoke(world,new object[]{renderer});
    Check(renderer.enabled,"Fine terrain visibility can restore atomically after a replacement bake");
    // Ground fitting must still follow the road while only its terrain visual
    // is suppressed; sampling raw100m hillside corners would bury this7m road.
    var road=new NewbridgeWorld.Feature{id="shore-road",sourceId="shore-road",category="road",kind="primary",width=8,points=new[]{new Vector3(0,7,16),new Vector3(64,7,16)}};
    var map=new NewbridgeWorld.Map{coreMinX=0,coreMaxX=1536,coreMinZ=0,coreMaxZ=1536,minX=0,minZ=0,gridStep=64,nx=2,nz=2,heights=new float[]{100,100,100,100},features=new[]{road},buildings=Array.Empty<NewbridgeWorld.Building>(),preparedPaving=new List<NewbridgeWorld.PavingPatch>()};
    ((IDictionary)type.GetField("areas",flags).GetValue(world)).Add(Vector2Int.zero,map);
    ((IDictionary)type.GetField("spatialByMap",flags).GetValue(world)).Add(map,type.GetMethod("BuildSpatialIndex",flags|BindingFlags.Static).Invoke(null,new object[]{map}));
    Set("workingMap",map);renderer.enabled=false;
    float roadGround=(float)type.GetMethod("SceneryHeight",flags).Invoke(world,new object[]{16f,16f});
    Check(Mathf.Abs(roadGround-6.8f)<.01f,"Hidden coastal cell backfill retains exact road earthwork height rather than raw hillside");
    // Actual Fort William anomaly: raw66.745m only0.351m landward of
    // sea10.18m, with a ground road22.965m about24m inland.
    fine.SetActive(false);areasForWedge();
    void areasForWedge()
    {
     var areaDictionary=(IDictionary)type.GetField("areas",flags).GetValue(world);areaDictionary.Clear();var spatialDictionary=(IDictionary)type.GetField("spatialByMap",flags).GetValue(world);spatialDictionary.Clear();
     road.points=new[]{new Vector3(24.536f,22.965f,-100),new Vector3(24.536f,22.965f,100)};road.width=8;map.elevationQuality="approximate terrain";map.features=new[]{road};areaDictionary.Add(Vector2Int.zero,map);
     spatialDictionary.Add(map,type.GetMethod("BuildSpatialIndex",flags|BindingFlags.Static).Invoke(null,new object[]{map}));Set("workingMap",map);
     Set("coastalPatch",new NewbridgeWorld.CoastalPatch{seaLevel=10.18f,shore=new[]{new NewbridgeWorld.CoastRing{points=new[]{new Vector3(0,0,-200),new Vector3(0,0,200)}}},sea=Array.Empty<NewbridgeWorld.SeaPolygon>()});
    }
    var wedge=(Func<float,float,float,float>)type.GetMethod("ConstrainCoastalRoadWedge",flags).CreateDelegate(typeof(Func<float,float,float,float>),world);
    float corrected=wedge(.351f,16,66.745f);Check(corrected>10.18f&&corrected<10.6f,"Exact shoreline needle becomes a continuous sea-to-road bank");
    var constraintCache=(IDictionary)type.GetField("coastalRoadConstraints",flags).GetValue(world);
    Check(wedge(.351f,16,66.745f)==corrected&&constraintCache.Count==1,"Exact coordinate repeats reuse immutable constraint data with identical output");
    float lower=wedge(.351f,16,30);Check(lower!=corrected&&lower>corrected&&lower<30,"Cached evidence still applies the current ground-dependent excess fade");
    var uncached=(object)type.GetMethod("ResolveCoastalRoadConstraint",flags).Invoke(world,new object[]{.351f,16f,map});
    float Reference(float ground)
    {
     var ct=uncached.GetType();float Get(string field)=>(float)ct.GetField(field,BindingFlags.Public|BindingFlags.Instance).GetValue(uncached);
     float influence=Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,12,ground-Mathf.Max(Get("roadY"),world.MappedSeaLevel)))*Get("shoreWeight")*Get("roadWeight")*Get("riseWeight");
     return Mathf.Lerp(ground,Mathf.Min(ground,Get("envelope")),influence);
    }
    foreach(float input in new[]{19f,22f,28f,30f,35f,66.745f,100f})Check(wedge(.351f,16,input)==Reference(input),"Cached/uncached constraint output remains bit-identical for distinct ground inputs");
    Set("roadWaterSourceRevision",99);Check(wedge(.351f,16,66.745f)==corrected&&constraintCache.Count==1,"Road-source revision clears and repopulates cached evidence");
    Set("coastalRevision",1);Check(wedge(.351f,16,66.745f)==corrected&&constraintCache.Count==1,"Coast revision clears and repopulates cached evidence");
    // Replace the owning map at identical coordinates without changing the
    // revision, proving that source identity participates in every cache hit.
    var replacementMap=new NewbridgeWorld.Map{coreMinX=0,coreMaxX=1536,coreMinZ=0,coreMaxZ=1536,elevationQuality="approximate terrain",features=Array.Empty<NewbridgeWorld.Feature>(),buildings=Array.Empty<NewbridgeWorld.Building>(),preparedPaving=new List<NewbridgeWorld.PavingPatch>()};
    var ownerAreas=(IDictionary)type.GetField("areas",flags).GetValue(world);var ownerSpatial=(IDictionary)type.GetField("spatialByMap",flags).GetValue(world);ownerAreas[Vector2Int.zero]=replacementMap;
    ownerSpatial.Add(replacementMap,type.GetMethod("BuildSpatialIndex",flags|BindingFlags.Static).Invoke(null,new object[]{replacementMap}));
    Check(wedge(.351f,16,66.745f)==66.745f,"A replacement source map cannot reuse the old map's low-road constraint");ownerAreas[Vector2Int.zero]=map;
    Check(wedge(.351f,16,66.745f)==corrected,"Returning to the original map recomputes its own evidence");
    Check(Mathf.Abs(wedge(.01f,16,66.745f)-10.18f)<.02f,"Coarse bank envelope approaches sea level continuously at shore");
    Check(wedge(40,16,66.745f)==66.745f,"Inland side of coastal road is preserved");
    Check(wedge(.351f,16,22)==22,"Terrain without an implausible excess above the road is preserved");
    map.elevationQuality="measured survey";Check(wedge(.351f,16,66.745f)==66.745f,"Surveyed cliffs are never constrained by the coarse shoreline inference");map.elevationQuality="approximate terrain";
    Set("roadWaterSourceRevision",1);road.bridge=true;Check(wedge(.351f,16,66.745f)==66.745f,"Bridge decks cannot constrain coastal land");road.bridge=false;Set("roadWaterSourceRevision",2);road.tunnel=true;Check(wedge(.351f,16,66.745f)==66.745f,"Tunnel floors cannot constrain coastal land");road.tunnel=false;
    road.points=new[]{new Vector3(24.536f,40,-100),new Vector3(24.536f,40,100)};areasForWedge();road.points=new[]{new Vector3(24.536f,40,-100),new Vector3(24.536f,40,100)};
    // Rebuild copies to reflect the high road fixture rather than mutate an
    // already-published segment's source heights.
    ((IDictionary)type.GetField("spatialByMap",flags).GetValue(world))[map]=type.GetMethod("BuildSpatialIndex",flags|BindingFlags.Static).Invoke(null,new object[]{map});
    Set("roadWaterSourceRevision",3);Check(wedge(.351f,16,66.745f)==66.745f,"High cliff-top road preserves genuine coastal cliffs");
    map.features=Array.Empty<NewbridgeWorld.Feature>();((IDictionary)type.GetField("spatialByMap",flags).GetValue(world))[map]=type.GetMethod("BuildSpatialIndex",flags|BindingFlags.Static).Invoke(null,new object[]{map});
    Set("roadWaterSourceRevision",4);Check(wedge(.351f,16,66.745f)==66.745f,"Coasts without ground-road evidence retain their natural terrain");
    Debug.Log("PUBLISHED SCENERY GROUND PASS "+checks);
   }
   finally{UnityEngine.Object.DestroyImmediate(fine);UnityEngine.Object.DestroyImmediate(far);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
  }
 }
}
