using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
 public sealed partial class NewbridgeWorld
 {
  [Serializable] public sealed class RailwayBuffer{public string id,sourceWay;public float x,y,z,dx,dz;public bool mapped;}
  [Serializable] public sealed class RailwayPlatform{public string id,name;public Vector3[] triangles,outline,roofTriangles,posts;public float roofHeight,depth,minX,minZ,maxX,maxZ;}
  // Clip prepared surfaces to exact owning chunks. These vertices never modify
  // the canonical track profile, train envelope or source platform footprint.
  static List<Vector3> ClipFurnitureTriangle(Vector3 a,Vector3 b,Vector3 c,Vector2Int key)
  {
   var polygon=new List<Vector3>{a,b,c};float x=key.x*ChunkSize,z=key.y*ChunkSize;
   for(int edge=0;edge<4&&polygon.Count>0;edge++)
   {
    var output=new List<Vector3>();float limit=edge==0?x:edge==1?x+ChunkSize:edge==2?z:z+ChunkSize;
    float Distance(Vector3 p)=>edge<2?(edge==0?p.x-limit:limit-p.x):(edge==2?p.z-limit:limit-p.z);
    var previous=polygon[polygon.Count-1];float before=Distance(previous);
    foreach(var point in polygon){float after=Distance(point);if((before>=0)!=(after>=0))output.Add(Vector3.LerpUnclamped(previous,point,before/(before-after)));if(after>=0)output.Add(point);previous=point;before=after;}
    polygon=output;
   }
   return polygon;
  }
  static void FurnitureTriangle(Batch batch,Vector3 a,Vector3 b,Vector3 c,Vector2Int key,float underside)
  {
   float x=key.x*ChunkSize,z=key.y*ChunkSize;
   if(Mathf.Max(a.x,Mathf.Max(b.x,c.x))<=x||Mathf.Min(a.x,Mathf.Min(b.x,c.x))>=x+ChunkSize||Mathf.Max(a.z,Mathf.Max(b.z,c.z))<=z||Mathf.Min(a.z,Mathf.Min(b.z,c.z))>=z+ChunkSize)return;
   var p=ClipFurnitureTriangle(a,b,c,key);if(p.Count<3)return;
   for(int i=1;i+1<p.Count;i++)
   {
    var first=p[0];var second=p[i];var third=p[i+1];if(Vector3.Cross(second-first,third-first).y<0){var swap=second;second=third;third=swap;}
    if(Vector3.Cross(second-first,third-first).sqrMagnitude<.00000001f)continue;
    batch.Triangle(first,second,third);batch.Triangle(first-Vector3.up*underside,third-Vector3.up*underside,second-Vector3.up*underside);
   }
  }
  IEnumerator AppendRailwayFurniture(Map source,Vector2Int key,Batch platform,Batch frame,Batch canopy,Batch markings)
  {
   var watch=System.Diagnostics.Stopwatch.StartNew();
   foreach(var stop in source.railwayBuffers??Array.Empty<RailwayBuffer>())
   {
    var p=new Vector3(stop.x,stop.y,stop.z);if(ChunkKey(p)!=key)continue;
    var forward=new Vector3(stop.dx,0,stop.dz).normalized;if(forward.sqrMagnitude<.5f)continue;var side=new Vector3(forward.z,0,-forward.x);
    // Compact steel A-frame, sleepers and broad red/white impact beam.
    foreach(float sign in new[]{-1f,1f})
    {var foot=p+side*(sign*.68f);frame.Box(foot-forward*.5f,foot+forward*.7f,side,side,.09f,-.2f,.05f);
     var top=foot+Vector3.up*.78f;var low=foot+forward*.65f;frame.Box(low,top,side,side,.065f,-.05f,.05f);}
    frame.Box(p-forward*.08f,p+forward*.08f,side,side,1.25f,.65f,.98f);
    var face=p+forward*.085f;markings.Quad(face-side*1.18f+Vector3.up*.68f,face+side*1.18f+Vector3.up*.68f,face+side*1.18f+Vector3.up*.92f,face-side*1.18f+Vector3.up*.92f);
    if(watch.Elapsed.TotalMilliseconds>1.5){RecordGenerationSlice("Railway furniture",watch.Elapsed.TotalMilliseconds);yield return null;watch.Restart();}
   }
   foreach(var item in source.railwayPlatforms??Array.Empty<RailwayPlatform>())
   {
    if(item.maxX<=key.x*ChunkSize||item.minX>=(key.x+1)*ChunkSize||item.maxZ<=key.y*ChunkSize||item.minZ>=(key.y+1)*ChunkSize)continue;
    foreach(var pair in new[]{Tuple.Create(item.triangles,platform,Mathf.Max(.35f,item.depth)),Tuple.Create(item.roofTriangles,canopy,.14f)})
    {
     var points=pair.Item1??Array.Empty<Vector3>();for(int i=0;i+2<points.Length;i+=3)
     {FurnitureTriangle(pair.Item2,points[i],points[i+1],points[i+2],key,pair.Item3);if(watch.Elapsed.TotalMilliseconds>1.5){RecordGenerationSlice("Railway furniture",watch.Elapsed.TotalMilliseconds);yield return null;watch.Restart();}}
    }
    var outline=item.outline??Array.Empty<Vector3>();for(int i=1;i<outline.Length;i++)
    {
     var a=outline[i-1];var b=outline[i];if(Range(a,b,key.x*ChunkSize,key.y*ChunkSize,out float lo,out float hi))
     {var first=Vector3.Lerp(a,b,lo);var last=Vector3.Lerp(a,b,hi);platform.Quad(first,last,last-Vector3.up*Mathf.Max(.35f,item.depth),first-Vector3.up*Mathf.Max(.35f,item.depth));}
    }
    foreach(var p in item.posts??Array.Empty<Vector3>())if(ChunkKey(p)==key)
     frame.Box(p-Vector3.forward*.075f,p+Vector3.forward*.075f,Vector3.right,Vector3.right,.075f,0,item.roofHeight-.14f);
   }
  }
 }
}
