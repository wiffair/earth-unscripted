using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class CoastalIndexVerification
 {
  static bool Reference(Vector3 p,Vector3[] ring)
  {bool inside=false;for(int i=0,j=ring.Length-1;i<ring.Length;j=i++){var a=ring[i];var b=ring[j];if((a.z>p.z)!=(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;}return inside;}
  public static void Run()
  {
   var root=new GameObject("Exact coastal edge index fixture");
   try
   {
    var world=root.AddComponent<NewbridgeWorld>();var outer=new Vector3[4096];var hole=new Vector3[128];
    for(int i=0;i<outer.Length;i++){float a=i*Mathf.PI*2/outer.Length;float r=5000+300*Mathf.Sin(a*13);outer[i]=new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);}
    for(int i=0;i<hole.Length;i++){float a=i*Mathf.PI*2/hole.Length;hole[i]=new Vector3(Mathf.Cos(a)*300,0,Mathf.Sin(a)*300);}
    typeof(NewbridgeWorld).GetField("coastalPatch",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(world,new NewbridgeWorld.CoastalPatch{shore=new[]{new NewbridgeWorld.CoastRing{points=outer}},sea=new[]{new NewbridgeWorld.SeaPolygon{outline=outer,holes=new[]{new NewbridgeWorld.CoastRing{points=hole}}}}});
    var random=new System.Random(7281);var points=new Vector3[12000];for(int i=0;i<points.Length;i++)points[i]=new Vector3((float)(random.NextDouble()*12000-6000),0,i<300?(i-150)*128:(float)(random.NextDouble()*12000-6000));
    var expected=new bool[points.Length];var watch=Stopwatch.StartNew();for(int i=0;i<points.Length;i++)expected[i]=Reference(points[i],outer)&&!Reference(points[i],hole);double original=watch.Elapsed.TotalMilliseconds;
    world.IsMappedSeaAt(Vector3.zero);watch.Restart();for(int i=0;i<points.Length;i++)if(world.IsMappedSeaAt(points[i])!=expected[i])throw new Exception("Indexed coast changed exact membership at "+points[i]);double indexed=watch.Elapsed.TotalMilliseconds;
    for(int i=0;i<outer.Length;i+=31)foreach(var p in new[]{outer[i],outer[i]+Vector3.right*.0001f,outer[i]-Vector3.right*.0001f})if(world.IsMappedSeaAt(p)!=(Reference(p,outer)&&!Reference(p,hole)))throw new Exception("Indexed coast changed boundary classification");
    var distance=(Func<float,float,float>)typeof(NewbridgeWorld).GetMethod("CoastalShoreDistance",BindingFlags.NonPublic|BindingFlags.Instance).CreateDelegate(typeof(Func<float,float,float>),world);
    float ReferenceDistance(float x,float z)
    {float closest=250;var point=new Vector2(x,z);for(int i=1;i<outer.Length;i++){var a=new Vector2(outer[i-1].x,outer[i-1].z);var delta=new Vector2(outer[i].x-outer[i-1].x,outer[i].z-outer[i-1].z);float t=Mathf.Clamp01(Vector2.Dot(point-a,delta)/Mathf.Max(.0001f,delta.sqrMagnitude));closest=Mathf.Min(closest,Vector2.Distance(point,a+delta*t));}return closest;}
    distance(0,0);watch.Restart();var distances=new float[2000];for(int i=0;i<distances.Length;i++)distances[i]=ReferenceDistance(points[i].x,points[i].z);double originalDistance=watch.Elapsed.TotalMilliseconds;
    watch.Restart();for(int i=0;i<distances.Length;i++)if(Mathf.Abs(distance(points[i].x,points[i].z)-distances[i])>.0001f)throw new Exception("Shore distance index changed bed profile");double indexedDistance=watch.Elapsed.TotalMilliseconds;
    UnityEngine.Debug.Log("COAST SHORE DISTANCE PASS 2000 originalMs="+originalDistance+" indexedMs="+indexedDistance);
    UnityEngine.Debug.Log("COAST INDEX PASS 12399 exact classifications originalMs="+original+" indexedMs="+indexed);
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
