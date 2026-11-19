using System;using Unity.Burst;using Unity.Collections;using Unity.Jobs;using UnityEngine;
namespace EarthUnscripted {
 public struct TreeCullGroup {public Vector3 centre,extents;}
 public struct TreeCullInput {public Vector3 position;public float born;public int group,seed;}
 public struct TreeCullOutput {public float visibility,nearWeight;public byte mask;}
 [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
 public struct TreeGroupCullJob:IJobParallelFor {
  [ReadOnly]public NativeArray<TreeCullGroup> groups;[ReadOnly]public NativeArray<Plane> planes;public NativeArray<byte> visible;public Vector3 eye;public float rangeSquared;
  public void Execute(int i){var g=groups[i];float x=Math.Max(0,Math.Abs(eye.x-g.centre.x)-g.extents.x),y=Math.Max(0,Math.Abs(eye.y-g.centre.y)-g.extents.y),z=Math.Max(0,Math.Abs(eye.z-g.centre.z)-g.extents.z);if(x*x+y*y+z*z>rangeSquared){visible[i]=0;return;}for(int p=0;p<6;p++){var plane=planes[p];var n=plane.normal;float d=n.x*g.centre.x+n.y*g.centre.y+n.z*g.centre.z+plane.distance;float r=Math.Abs(n.x)*g.extents.x+Math.Abs(n.y)*g.extents.y+Math.Abs(n.z)*g.extents.z;if(d+r<-.001f){visible[i]=0;return;}}visible[i]=1;}
 }
 [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
 public struct TreeVisibilityJob:IJobParallelFor {
  [ReadOnly]public NativeArray<TreeCullInput> trees;[ReadOnly]public NativeArray<byte> allowed,groups;public NativeArray<TreeCullOutput> output;public Vector3 eye;public float time,range,detail;public int stride;
  public void Execute(int i){var t=trees[i];var o=default(TreeCullOutput);if(allowed[i]==0||groups[t.group]==0||t.seed%stride!=0){output[i]=o;return;}float x=t.position.x-eye.x,y=t.position.y-eye.y,z=t.position.z-eye.z;float sq=x*x+y*y+z*z;if(sq>range*range){output[i]=o;return;}float metres=(float)Math.Sqrt(sq);float age=(time-t.born)/5,fade=(range-metres)/600;age=Math.Max(0,Math.Min(1,age));fade=Math.Max(0,Math.Min(1,fade));o.visibility=Math.Min(age,fade);o.nearWeight=detail>0?1-Math.Max(0,Math.Min(1,(metres-detail)/25)):0;float end=detail>0?detail+25:0;if(sq<end*end)o.mask|=1;if(sq>=detail*detail)o.mask|=2;output[i]=o;}
 }
}
