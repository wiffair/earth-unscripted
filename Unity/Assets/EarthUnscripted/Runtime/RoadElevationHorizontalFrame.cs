using System;using System.IO;using UnityEngine;
namespace EarthUnscripted
{
 // Preserve the existing width/miter frame while mapping the new exact source
 // metric by segment+t. Never assume legacy float-delta Arc equals double S.
 public sealed class RoadElevationHorizontalFrame
 {
  readonly RoadElevationMetric metric;readonly RoadMarkingPlan.RoadFrame frame;
  public RoadElevationHorizontalFrame(RoadElevationMetric metric,RoadMarkingPlan.RoadFrame original)
  {
   if(metric==null||original==null||original.Points==null||original.Points.Length!=metric.PointCount||original.Arc==null||original.Arc.Length!=metric.PointCount||original.IncomingRight==null||original.OutgoingRight==null||original.IncomingRight.Length!=metric.PointCount||original.OutgoingRight.Length!=metric.PointCount)throw new InvalidDataException("Elevation horizontal frame shape");this.metric=metric;
   frame=new RoadMarkingPlan.RoadFrame{Forward=original.Forward,Length=original.Length,Points=(Vector3[])original.Points.Clone(),Arc=(double[])original.Arc.Clone(),Right=original.Right==null?null:(Vector3[])original.Right.Clone(),IncomingRight=(Vector3[])original.IncomingRight.Clone(),OutgoingRight=(Vector3[])original.OutgoingRight.Clone(),SharpDistances=original.SharpDistances==null?Array.Empty<double>():(double[])original.SharpDistances.Clone()};for(int i=0;i<metric.PointCount;i++){var expected=metric.PointAt(i);var actual=frame.Points[frame.Forward?i:metric.PointCount-1-i];if(expected.x!=actual.x||expected.y!=actual.y||expected.z!=actual.z)throw new InvalidDataException("Elevation horizontal frame owner mismatch");}
  }
  public void At(double sourceS,out Vector3 centre,out Vector3 right,out Vector3 dCentreDH,out Vector3 dRightDH,bool previousSide=false,bool bevelAll=false)
  {
   metric.SourceSegment(sourceS,out int i,out double t,previousSide);int first=frame.Forward?i:metric.PointCount-1-i,last=frame.Forward?i+1:metric.PointCount-2-i;double directed=frame.Arc[first]+(frame.Arc[last]-frame.Arc[first])*t,mapped=frame.Forward?directed:frame.Length-directed;frame.At(mapped,out centre,out right,previousSide,bevelAll);double dh=metric.HorizontalAtPoint(i+1)-metric.HorizontalAtPoint(i);var a=metric.PointAt(i);var b=metric.PointAt(i+1);dCentreDH=new Vector3((float)(((double)b.x-a.x)/dh),0,(float)(((double)b.z-a.z)/dh));if(bevelAll){dRightDH=Vector3.zero;return;}var rs=frame.Forward?frame.OutgoingRight[first]:-frame.IncomingRight[first];var re=frame.Forward?frame.IncomingRight[last]:-frame.OutgoingRight[last];dRightDH=(re-rs)/(float)dh;
  }
  // Width DTO stations retain the legacy float-delta arc convention. Map by
  // source segment+t; casting exact SourceS to float is not this conversion.
  public double LegacySourceDistance(double sourceS,bool previousSide=false)
  {metric.SourceSegment(sourceS,out int i,out double t,previousSide);int first=frame.Forward?i:metric.PointCount-1-i,last=frame.Forward?i+1:metric.PointCount-2-i;double directed=frame.Arc[first]+(frame.Arc[last]-frame.Arc[first])*t;return frame.Forward?directed:frame.Length-directed;}
 }
}
