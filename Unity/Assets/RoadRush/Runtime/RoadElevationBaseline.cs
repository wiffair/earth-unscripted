using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
 public static class RoadElevationBaseline
 {
  // Preserve the existing DEM-authored centre heights. No new terrain envelope,
  // global road-height averaging, or second 18% lowering pass occurs here.
  public static RoadElevationProfileDto Create(GeoRoad road,float crownRate=.025f)
  {
   var metric=new RoadElevationMetric(road);if(metric.PointCount>512)throw new InvalidDataException("Baseline exceeds profile station budget "+road.id);var s=new double[metric.PointCount];var z=new float[s.Length];var h=new double[s.Length];for(int i=0;i<s.Length;i++){s[i]=metric.SourceAtPoint(i);z[i]=metric.PointAt(i).y;h[i]=metric.ToHorizontal(s[i]);if(i>0&&s[i]-s[i-1]<.0001)throw new InvalidDataException("Baseline source controls collapse "+road.id);}var m=Slopes(h,z);return new RoadElevationProfileDto{roadId=road.id,distances=s,elevations=z,gradeDerivatives=m,crossfalls=new float[s.Length],crownRates=Enumerable.Repeat(crownRate,s.Length).ToArray(),crownSmooth=.15f};
  }
  public static float[] Slopes(double[] x,float[] y)
  {
   int n=x.Length;if(n<2||y.Length!=n)throw new InvalidDataException("Grade slope shape");var d=new double[n-1];var h=new double[n-1];for(int i=0;i<n-1;i++){h[i]=x[i+1]-x[i];if(h[i]<=0)throw new InvalidDataException("Grade station order");d[i]=(y[i+1]-y[i])/h[i];}var m=new float[n];if(n==2){m[0]=m[1]=(float)d[0];return m;}
   for(int i=1;i<n-1;i++){if(d[i-1]*d[i]<=0)m[i]=0;else{double w1=2*h[i]+h[i-1],w2=h[i]+2*h[i-1];m[i]=(float)((w1+w2)/(w1/d[i-1]+w2/d[i]));}}double End(double a,double b,double ha,double hb){double value=((2*ha+hb)*a-ha*b)/(ha+hb);if(value*a<=0)return 0;if(a*b<0&&Math.Abs(value)>3*Math.Abs(a))return 3*a;return value;}m[0]=(float)End(d[0],d[1],h[0],h[1]);m[n-1]=(float)End(d[n-2],d[n-3],h[n-2],h[n-3]);return m;
  }
  sealed class End{public GeoRoad Road;public RoadElevationProfileDto Profile;public bool Start;public Vector3 Outward;public double OutGrade;}
  public static RoadElevationProfileDto[] CreateNetwork(IReadOnlyList<GeoRoad> roads)
  {
   var sourceNodes=new Dictionary<string,Vector3>(StringComparer.Ordinal);foreach(var road in roads){if(road.sourceNodeIds==null||road.sourceNodeIds.Length!=road.points.Length)throw new InvalidDataException("Missing source topology");for(int j=0;j<road.points.Length;j++){var id=road.sourceNodeIds[j];if(string.IsNullOrEmpty(id))throw new InvalidDataException("Missing source node");if(sourceNodes.TryGetValue(id,out var previous)&&Vector3.Distance(previous,road.points[j])>.001f)throw new InvalidDataException("Inconsistent shared source node "+id);sourceNodes[id]=road.points[j];}}
   var profiles=roads.Select(r=>Create(r)).ToArray();var nodes=new Dictionary<string,List<End>>(StringComparer.Ordinal);
   for(int i=0;i<roads.Count;i++){var r=roads[i];if(r.sourceNodeIds==null||r.sourceNodeIds.Length!=r.points.Length)throw new InvalidDataException("Missing exact topology "+r.id);for(int end=0;end<2;end++){bool start=end==0;int p=start?0:r.points.Length-1,q=start?1:r.points.Length-2;var dir=r.points[q]-r.points[p];dir.y=0;dir.Normalize();var e=new End{Road=r,Profile=profiles[i],Start=start,Outward=dir,OutGrade=(start?1:-1)*profiles[i].gradeDerivatives[start?0:r.points.Length-1]};string id=r.sourceNodeIds[p];if(!nodes.TryGetValue(id,out var list))nodes[id]=list=new List<End>();list.Add(e);}}
   foreach(var node in nodes.Values){if(node.Count!=2)continue;var a=node[0];var b=node[1];if(a.Road.layer!=b.Road.layer||a.Road.bridge!=b.Road.bridge||a.Road.tunnel!=b.Road.tunnel||Vector3.Dot(a.Outward,b.Outward)>-.95)continue;int ai=a.Start?0:a.Profile.elevations.Length-1,bi=b.Start?0:b.Profile.elevations.Length-1;if(Math.Abs(a.Profile.elevations[ai]-b.Profile.elevations[bi])>.001)throw new InvalidDataException("Shared source height conflict");double x=a.OutGrade,y=-b.OutGrade,g=x*y<=0?0:2/(1/x+1/y);a.Profile.gradeDerivatives[ai]=(float)(g*(a.Start?1:-1));b.Profile.gradeDerivatives[bi]=(float)(-g*(b.Start?1:-1));}
   return profiles;
  }
 }
}
