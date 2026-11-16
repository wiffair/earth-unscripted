using System;using System.IO;using UnityEngine;
namespace EarthUnscripted
{
 public sealed class RoadElevationCurve
 {
  readonly RoadElevationMetric metric;readonly double[] source;readonly float[] z,grade,bank,crown;readonly double[] h;readonly float smooth;
  public RoadElevationMetric Metric=>metric;public string RoadId=>metric.RoadId;
  public readonly struct Sample
  {public readonly double Elevation,LongitudinalGrade,LateralGrade,CentreElevation,CentreGrade,Bank,Crown;public Sample(double y,double longitudinal,double lateral,double centre,double centreGrade,double bank,double crown){Elevation=y;LongitudinalGrade=longitudinal;LateralGrade=lateral;CentreElevation=centre;CentreGrade=centreGrade;Bank=bank;Crown=crown;}}
  public RoadElevationCurve(GeoRoad road,RoadElevationProfileDto data)
  {
   if(data==null||data.roadId!=road.id||data.distances==null||data.distances.Length<2||data.distances.Length>512||data.elevations==null||data.gradeDerivatives==null||data.crossfalls==null||data.crownRates==null||data.elevations.Length!=data.distances.Length||data.gradeDerivatives.Length!=data.distances.Length||data.crossfalls.Length!=data.distances.Length||data.crownRates.Length!=data.distances.Length)throw new InvalidDataException("Elevation curve shape");metric=new RoadElevationMetric(road);source=(double[])data.distances.Clone();z=(float[])data.elevations.Clone();grade=(float[])data.gradeDerivatives.Clone();bank=(float[])data.crossfalls.Clone();crown=(float[])data.crownRates.Clone();smooth=data.crownSmooth;h=new double[source.Length];
   if(source[0]!=0||Math.Abs(source[source.Length-1]-metric.SourceLength)>1e-8||!Finite(smooth)||smooth<.01f||smooth>1)throw new InvalidDataException("Elevation curve domain");
   for(int i=0;i<source.Length;i++){if(!Finite(source[i])||!Finite(z[i])||Math.Abs(z[i])>=20000||!Finite(grade[i])||Math.Abs(grade[i])>1||!Finite(bank[i])||Math.Abs(bank[i])>RoadElevationProfileDto.MaximumCrossfall||!Finite(crown[i])||crown[i]<0||crown[i]>RoadElevationProfileDto.MaximumCrown||i>0&&source[i]-source[i-1]<.0001)throw new InvalidDataException("Elevation curve control");h[i]=metric.ToHorizontal(source[i]);if(i>0&&h[i]<=h[i-1])throw new InvalidDataException("Elevation horizontal controls");}
  }
  static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
  static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
  public Sample Evaluate(double sourceDistance,double lateral)
  {
   if(double.IsNaN(lateral)||double.IsInfinity(lateral)||Math.Abs(lateral)>1000)throw new ArgumentOutOfRangeException(nameof(lateral));double at=metric.ToHorizontal(sourceDistance);int lo=0,hi=h.Length-1;while(hi-lo>1){int m=(hi+lo)/2;if(h[m]<=at)lo=m;else hi=m;}double span=h[hi]-h[lo],t=Math.Max(0,Math.Min(1,(at-h[lo])/span)),t2=t*t,t3=t2*t;
   double centre=(2*t3-3*t2+1)*z[lo]+(t3-2*t2+t)*span*grade[lo]+(-2*t3+3*t2)*z[hi]+(t3-t2)*span*grade[hi];double centreGrade=((6*t2-6*t)*z[lo]+(3*t2-4*t+1)*span*grade[lo]+(-6*t2+6*t)*z[hi]+(3*t2-2*t)*span*grade[hi])/span;
   double w=t2*(3-2*t),dw=6*t*(1-t)/span,b=bank[lo]+(bank[hi]-bank[lo])*w,c=crown[lo]+(crown[hi]-crown[lo])*w,offset=Math.Sqrt(lateral*lateral+smooth*smooth)-smooth;
   double y=centre+b*lateral-c*offset,longitudinal=centreGrade+dw*((bank[hi]-bank[lo])*lateral-(crown[hi]-crown[lo])*offset),cross=b-c*lateral/Math.Sqrt(lateral*lateral+smooth*smooth);return new Sample(y,longitudinal,cross,centre,centreGrade,b,c);
  }
  public Sample EvaluateAtFrame(double sourceDistance,double nominalLateral,Vector3 right,Vector3 dRightDH,double dNominalLateralDH=0)
  {if(!Finite(dRightDH.x)||!Finite(dRightDH.y)||!Finite(dRightDH.z)||double.IsNaN(dNominalLateralDH)||double.IsInfinity(dNominalLateralDH))throw new InvalidDataException("Nonfinite elevation frame derivative");double n=Math.Sqrt((double)right.x*right.x+(double)right.z*right.z);if(double.IsNaN(n)||double.IsInfinity(n)||n<.000001||n>10)throw new InvalidDataException("Elevation row frame");double physical=nominalLateral*n,dn=((double)right.x*dRightDH.x+(double)right.z*dRightDH.z)/n;var value=Evaluate(sourceDistance,physical);return new Sample(value.Elevation,value.LongitudinalGrade+value.LateralGrade*(nominalLateral*dn+dNominalLateralDH*n),value.LateralGrade,value.CentreElevation,value.CentreGrade,value.Bank,value.Crown);}
  public Vector3 Point(double sourceDistance,double lateral)
  {metric.FrameAtSource(sourceDistance,out var centre,out _,out var right);return PointAtFrame(sourceDistance,lateral,centre,right);}
  // Runtime width-frame upload can supply its EXACT existing XZ/right vectors;
  // replacing Y here does not require changing horizontal pavement geometry.
  public Vector3 PointAtFrame(double sourceDistance,double lateral,Vector3 centre,Vector3 right)
  {var p=centre+right*(float)lateral;p.y=(float)EvaluateAtFrame(sourceDistance,lateral,right,Vector3.zero).Elevation;return p;}
 }
}
