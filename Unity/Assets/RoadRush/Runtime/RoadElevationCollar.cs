using System;using System.Collections.Generic;using System.IO;using UnityEngine;
namespace RoadRush
{
 // The path is explicit authority, never a receiver-side graph solve.
 public sealed class RoadElevationCollar
 {
  readonly Vector3 centre;readonly Vector2 gradient;public RoadElevationArmPath Path{get;}
  public RoadElevationCollar(IReadOnlyDictionary<string,RoadElevationCurve> curves,RoadJunctionPatchDto patch,RoadJunctionArmDto arm)
  {if(patch==null||!Finite(patch.centre.x)||!Finite(patch.centre.y)||!Finite(patch.centre.z)||!Finite(patch.planeGradient.x)||!Finite(patch.planeGradient.y)||patch.planeGradient.sqrMagnitude>1.000001f)throw new InvalidDataException("Collar patch plane");Path=new RoadElevationArmPath(arm,curves);centre=patch.centre;gradient=patch.planeGradient;}
  static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
  public RoadElevationCurve.Sample Evaluate(string roadId,double sourceS,double lateral)
  {var curve=Path.GetCurve(roadId);curve.Metric.FrameAtSource(sourceS,out var p,out var forward,out var right);return EvaluateAtFrame(roadId,sourceS,lateral,p,right,forward,Vector3.zero);}
  public RoadElevationCurve.Sample EvaluateAtFrame(string roadId,double sourceS,double nominalLateral,Vector3 p,Vector3 right,Vector3 dCentreDH,Vector3 dRightDH,double dNominalLateralDH=0)
  {
   if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)||!Finite(dCentreDH.x)||!Finite(dCentreDH.y)||!Finite(dCentreDH.z))throw new InvalidDataException("Nonfinite collar frame");var baseline=Path.GetCurve(roadId).EvaluateAtFrame(sourceS,nominalLateral,right,dRightDH,dNominalLateralDH);if(!Path.TryLocate(roadId,sourceS,out double distance,out int direction))return baseline;double norm=Math.Sqrt((double)right.x*right.x+(double)right.z*right.z);var point=p+right*(float)nominalLateral;
   double planeCentre=centre.y+gradient.x*((double)p.x-centre.x)+gradient.y*((double)p.z-centre.z),plane=centre.y+gradient.x*((double)point.x-centre.x)+gradient.y*((double)point.z-centre.z),planeCentreGrade=gradient.x*dCentreDH.x+gradient.y*dCentreDH.z,planeGrade=planeCentreGrade+nominalLateral*(gradient.x*dRightDH.x+gradient.y*dRightDH.z)+dNominalLateralDH*(gradient.x*right.x+gradient.y*right.z),planeBank=(gradient.x*right.x+gradient.y*right.z)/norm,t=Math.Max(0,Math.Min(1,(distance-Path.CoreHorizontal)/(Path.HorizontalLength-Path.CoreHorizontal))),w=t*t*(3-2*t),dw=direction*6*t*(1-t)/(Path.HorizontalLength-Path.CoreHorizontal);
   return new RoadElevationCurve.Sample(plane+(baseline.Elevation-plane)*w,planeGrade+(baseline.LongitudinalGrade-planeGrade)*w+dw*(baseline.Elevation-plane),planeBank+(baseline.LateralGrade-planeBank)*w,planeCentre+(baseline.CentreElevation-planeCentre)*w,planeCentreGrade+(baseline.CentreGrade-planeCentreGrade)*w+dw*(baseline.CentreElevation-planeCentre),planeBank+(baseline.Bank-planeBank)*w,baseline.Crown*w);
  }
 }
}
