using System;using System.IO;using UnityEngine;
namespace RoadRush
{
 // One immutable map used by authoring, sampler and codec. Wire stations retain
 // raw XYZ arc S; physical longitudinal derivatives are always with respect to H.
 public sealed class RoadElevationMetric
 {
  readonly Vector3[] points;readonly double[] source,horizontal;
  public string RoadId{get;}public int PointCount=>points.Length;
  public double SourceLength=>source[source.Length-1];public double HorizontalLength=>horizontal[horizontal.Length-1];
  public RoadElevationMetric(GeoRoad road)
  {
   if(road==null||road.points==null||road.points.Length<2||road.points.Length>10000)throw new InvalidDataException("Elevation source point budget");RoadId=road.id;points=(Vector3[])road.points.Clone();source=new double[points.Length];horizontal=new double[points.Length];
   for(int i=0;i<points.Length;i++){var p=points[i];if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.z))throw new InvalidDataException("Nonfinite elevation source");if(i==0)continue;double dx=(double)p.x-points[i-1].x,dy=(double)p.y-points[i-1].y,dz=(double)p.z-points[i-1].z,dh=Math.Sqrt(dx*dx+dz*dz),ds=Math.Sqrt(dx*dx+dy*dy+dz*dz);if(dh<=.000001||ds<=.000001)throw new InvalidDataException("Unsupported degenerate road segment "+RoadId);source[i]=source[i-1]+ds;horizontal[i]=horizontal[i-1]+dh;}
   if(SourceLength<.01||SourceLength>100000)throw new InvalidDataException("Elevation source length");
  }
  public double SourceAtPoint(int index)=>source[index];public double HorizontalAtPoint(int index)=>horizontal[index];public Vector3 PointAt(int index)=>points[index];
  static int Segment(double[] controls,double value){int lo=0,hi=controls.Length-1;while(hi-lo>1){int m=(hi+lo)/2;if(controls[m]<=value)lo=m;else hi=m;}return lo;}
  static double Bound(double value,double end){if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));return Math.Max(0,Math.Min(end,value));}
  public void SourceSegment(double sourceDistance,out int index,out double t,bool previousSide=false)
  {double s=Bound(sourceDistance,SourceLength);index=Segment(source,s);if(previousSide&&index>0&&Math.Abs(s-source[index])<.000001)index--;t=Math.Max(0,Math.Min(1,(s-source[index])/(source[index+1]-source[index])));}
  public double ToHorizontal(double sourceDistance){double s=Bound(sourceDistance,SourceLength);int i=Segment(source,s);return horizontal[i]+(horizontal[i+1]-horizontal[i])*(s-source[i])/(source[i+1]-source[i]);}
  public double ToSource(double horizontalDistance){double h=Bound(horizontalDistance,HorizontalLength);int i=Segment(horizontal,h);return source[i]+(source[i+1]-source[i])*(h-horizontal[i])/(horizontal[i+1]-horizontal[i]);}
  public void FrameAtSource(double sourceDistance,out Vector3 centre,out Vector3 forward,out Vector3 right)
  {double s=Bound(sourceDistance,SourceLength);int i=Segment(source,s);double t=(s-source[i])/(source[i+1]-source[i]);var a=points[i];var b=points[i+1];centre=new Vector3((float)(a.x+((double)b.x-a.x)*t),(float)(a.y+((double)b.y-a.y)*t),(float)(a.z+((double)b.z-a.z)*t));double h=horizontal[i+1]-horizontal[i];forward=new Vector3((float)(((double)b.x-a.x)/h),0,(float)(((double)b.z-a.z)/h));right=new Vector3(forward.z,0,-forward.x);}
 }
}
