using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class BoundedQueryIndexVerification
 {
  const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
  static object Field(object owner,string name)=>owner.GetType().GetField(name,Hidden|BindingFlags.Public).GetValue(owner);
  static int Count(object rows)=>rows==null?0:(int)rows.GetType().GetProperty("Count").GetValue(rows);
  static object Item(object rows,int index)=>rows.GetType().GetProperty("Item").GetValue(rows,new object[]{index});
  static long Payload(object spatial)=>((Array)Field(spatial,"segmentStorage")).LongLength*NativeSegmentStrideProbe.Measure()+(long)Field(spatial,"indexedMembershipSlots")*sizeof(int);
  static NewbridgeWorld.Map Map(float west,float east,float height)
  {
   var points=new Vector3[1501];for(int i=0;i<points.Length;i++)points[i]=new Vector3(-700+i*2,height,128);
   return new NewbridgeWorld.Map{preparedPaving=new List<NewbridgeWorld.PavingPatch>(),coreMinX=west,coreMaxX=east,coreMinZ=0,coreMaxZ=1536,features=new[]{
    new NewbridgeWorld.Feature{id="road",sourceId="road",category="road",kind="primary",width=8,roadCentreOffset=2,points=points},
    new NewbridgeWorld.Feature{id="cross",sourceId="cross",category="road",kind="secondary",width=6,points=new[]{new Vector3(-700,height,-700),new Vector3(2300,height,2300)}},
    new NewbridgeWorld.Feature{id="path",sourceId="path",category="path",kind="footway",width=2,points=new[]{new Vector3(-700,height,800),new Vector3(2300,height,800)}}},
    buildings=new[]{new NewbridgeWorld.Building{id="long-building",points=new[]{new Vector3(-400,0,1000),new Vector3(2000,0,1000),new Vector3(2000,0,1100),new Vector3(-400,0,1100)}}}};
  }
  public static void Batch(){try{Run();UnityEditor.EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
  public static void Run()
  {
   int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
   var type=typeof(NewbridgeWorld);var build=type.GetMethod("BuildSpatialIndex",Hidden);var map=Map(0,1536,5);var reference=Map(0,0,5);
   // Warm both branches and supply identical precomputed paving so this
   // measures spatial indexing, rather than unrelated cold junction planning.
   build.Invoke(null,new object[]{Map(0,0,5)});build.Invoke(null,new object[]{Map(0,1536,5)});
   object Measure(NewbridgeWorld.Map input,out long retained,out long allocations)
   {
    long before=GC.GetTotalMemory(true);object result;
    using(var recorder=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory,"GC.Alloc",1,Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread|Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame))
    {
     result=build.Invoke(null,new object[]{input});recorder.Stop();allocations=0;
     for(int i=0;i<recorder.Count;i++)allocations+=recorder.GetSample(i).Count;
    }
    retained=GC.GetTotalMemory(true)-before;GC.KeepAlive(result);return result;
   }
   object baseline=Measure(reference,out long unboundedBytes,out long unboundedAllocations);
   object indexed=Measure(map,out long boundedBytes,out long boundedAllocations);
   var old=(IDictionary)Field(baseline,"segments");var bounded=(IDictionary)Field(indexed,"segments");
   int Entries(IDictionary source){int count=0;foreach(DictionaryEntry row in source)count+=Count(row.Value);return count;}
   Check(Entries(bounded)<Entries(old)*.7,"Bounded core index removes unused planning halo copies");
   long unboundedPayload=Payload(baseline),boundedPayload=Payload(indexed);
   Check(boundedPayload>0&&boundedPayload<unboundedPayload,"Bounded core retains fewer shared-value and membership-array payload bytes");
   Debug.Log("BOUNDED ALLOCATION DIAGNOSTIC retainedUnbounded="+unboundedBytes+" bounded="+boundedBytes+" payloadUnbounded="+unboundedPayload+" payloadBounded="+boundedPayload+" oldCopies="+Entries(old)+" newCopies="+Entries(bounded)+" allocationEventsUnbounded="+unboundedAllocations+" bounded="+boundedAllocations);
   Check(unboundedAllocations>0&&boundedAllocations>0&&boundedAllocations<unboundedAllocations,"Native profiler confirms fewer allocation events");
   Check(boundedBytes>0&&boundedBytes<unboundedBytes,"Bounded core fixture retains fewer managed bytes");
   Check(map.features[0].points.Length==1501&&map.features[2].points[1].x==2300&&map.buildings[0].points[0].x==-400,"Long road/path/building source geometry retained intact");
   var root=new GameObject("Bounded query ownership fixture");
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();var areas=(IDictionary)Field(world,"areas");var spatial=(IDictionary)Field(world,"spatialByMap");areas.Add(Vector2Int.zero,map);spatial.Add(map,indexed);type.GetField("workingMap",Hidden).SetValue(world,map);
    var nearby=type.GetMethod("Nearby",Hidden);
    void Same(float x,float z)
    {
     var cell=new Vector2Int(Mathf.FloorToInt(x/16),Mathf.FloorToInt(z/16));var expected=old.Contains(cell)?old[cell]:null;var actual=nearby.Invoke(world,new object[]{x,z});
     Check(Count(expected)==Count(actual),"Query candidate count retained at "+x+","+z);
     for(int i=0;i<Count(expected);i++)
     {
      var a=Item(actual,i);var b=Item(expected,i);
      Check(((NewbridgeWorld.Feature)Field(a,"feature")).id==((NewbridgeWorld.Feature)Field(b,"feature")).id&&(int)Field(a,"pointIndex")== (int)Field(b,"pointIndex")&&(Vector3)Field(a,"a")== (Vector3)Field(b,"a")&&(Vector3)Field(a,"b")== (Vector3)Field(b,"b")&&(float)Field(a,"station")== (float)Field(b,"station"),"Exact candidate geometry/order retained");
     }
    }
    foreach(float x in new[]{-64f,-.001f,0,16,1535.999f,1536,1600,1640,-512})Same(x,128);
    Same(2200,800);Same(0,0);Same(1536,1536);
    var view=type.GetMethod("OwnSegments",Hidden).Invoke(world,new object[]{map.features[0],-512f,128f});
    Check((int)view.GetType().GetProperty("Count").GetValue(view)>0,"Source-local views retain uncommon far-halo queries");
    for(int i=0;i<10;i++)Same(-700+i*16,128);
    Check(((IDictionary)Field(indexed,"fallbackCells")).Count<=4,"Exceptional halo cache remains bounded");
    var neighbour=Map(1536,3072,20);areas.Add(new Vector2Int(1,0),neighbour);spatial.Add(neighbour,build.Invoke(null,new object[]{neighbour}));
    var chosen=nearby.Invoke(world,new object[]{1536f,128f});
    Check(Count(chosen)>0&&((Vector3)Field(Item(chosen,0),"a")).y==20,"Exact shared edge chooses the resident neighbour owner");
    Debug.Log("BOUNDED QUERY INDEX PASS "+checks+" oldCopies="+Entries(old)+" boundedCopies="+Entries(bounded)+" unboundedRetainedBytes="+unboundedBytes+" boundedRetainedBytes="+boundedBytes);
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
