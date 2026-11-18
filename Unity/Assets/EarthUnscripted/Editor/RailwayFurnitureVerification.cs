using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EarthUnscripted.NewbridgePrototype;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class RailwayFurnitureVerification
 {
  const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
  public static void Run()
  {
   int checks=0;void Check(bool ok,string reason){if(!ok)throw new Exception("RAIL FURNITURE FAIL "+reason);checks++;}
   var root=new GameObject("Actual railway furniture batching fixture");
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();var batch=typeof(NewbridgeWorld).GetNestedType("Batch",BindingFlags.NonPublic);
    object NewBatch(string name)=>Activator.CreateInstance(batch,new object[]{name,null,true});
    IReadOnlyList<Vector3> Vertices(object b)=>(IReadOnlyList<Vector3>)batch.GetProperty("Vertices").GetValue(b);
    IReadOnlyList<int> Indices(object b)=>(IReadOnlyList<int>)batch.GetProperty("Indices").GetValue(b);
    (object slab,object frame,object roof,object paint) Build(NewbridgeWorld.Map map,Vector2Int key)
    {
     var slab=NewBatch("platform");var frame=NewBatch("frames");var roof=NewBatch("roof");var paint=NewBatch("paint");
     var iterator=(IEnumerator)typeof(NewbridgeWorld).GetMethod("AppendRailwayFurniture",Hidden).Invoke(world,new[]{(object)map,key,slab,frame,roof,paint});
     int steps=0;while(iterator.MoveNext())if(++steps>10000)throw new Exception("Furniture coroutine did not terminate");return(slab,frame,roof,paint);
    }
    var points=new[]{new Vector3(-4,1,10),new Vector3(68,1,10),new Vector3(68,1,16),new Vector3(-4,1,10),new Vector3(68,1,16),new Vector3(-4,1,16)};
    var platform=new NewbridgeWorld.RailwayPlatform{id="mapped",name="Mapped Station · 2",triangles=points,outline=new[]{points[0],points[1],points[2],points[5],points[0]},roofTriangles=points.Select(p=>p+Vector3.up*5).ToArray(),posts=new[]{new Vector3(20,1,15)},roofHeight=5,depth=1.5f,minX=-4,maxX=68,minZ=10,maxZ=16};
    var original=(Vector3[])points.Clone();var map=new NewbridgeWorld.Map{railwayPlatforms=new[]{platform},railwayBuffers=new[]{new NewbridgeWorld.RailwayBuffer{id="terminal",x=0,y=0,z=30,dx=1,dz=0}}};
    double floorArea=0,roofArea=0;int frames=0;
    foreach(int x in new[]{-1,0,1})
    {
     var key=new Vector2Int(x,0);var built=Build(map,key);var vertices=Vertices(built.slab);var indices=Indices(built.slab);
     Check(vertices.Count>0,"cross-boundary mapped platform remains present in chunk "+key);
     Check(vertices.All(p=>p.x>=x*64-.001f&&p.x<=(x+1)*64+.001f),"platform mesh is clipped to its owning chunk "+key);
     Check(vertices.All(p=>p.y>=-.501f&&p.y<=1.001f),"solid platform body reaches below track bed without moving its top");
     double TopArea(object payload)
     {double area=0;var v=Vertices(payload);var t=Indices(payload);for(int i=0;i<t.Count;i+=3){var cross=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);if(cross.y>0)area+=cross.y*.5;}return area;}
     floorArea+=TopArea(built.slab);roofArea+=TopArea(built.roof);
     if(Vertices(built.frame).Count>0)frames++;
     Check(Vertices(built.roof).All(p=>p.y>=5.859f&&p.y<=6.001f),"canopy top/underside preserve prepared clearance");
    }
    Check(Math.Abs(floorArea-432)<.001,"actual emitted platform covers exact mapped area once across three chunks");
    Check(Math.Abs(roofArea-432)<.001,"actual emitted canopy covers exact mapped area once across three chunks");
    Check(frames==1,"buffer on exact chunk boundary and canopy post publish under one owner only");
    Check(points.SequenceEqual(original),"batch generation never mutates source platform or track-height data");
    var far=Build(map,new Vector2Int(100,100));Check(Vertices(far.slab).Count==0&&Vertices(far.frame).Count==0&&Vertices(far.roof).Count==0,"unrelated chunk emits no station furniture");
    var empty=Build(new NewbridgeWorld.Map(),Vector2Int.zero);Check(Vertices(empty.slab).Count==0&&Vertices(empty.frame).Count==0,"old prepared maps remain valid without optional furniture");
    var stopOnly=new NewbridgeWorld.Map{railwayBuffers=new[]{new NewbridgeWorld.RailwayBuffer{id="negative-boundary",x=-64,y=2,z=30,dx=-1,dz=0}}};
    Check(Vertices(Build(stopOnly,new Vector2Int(-1,0)).frame).Count>0&&Vertices(Build(stopOnly,new Vector2Int(-2,0)).frame).Count==0,"negative exact-boundary terminal buffer has unique half-open ownership");
    Debug.Log("RAIL FURNITURE PASS "+checks);
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
