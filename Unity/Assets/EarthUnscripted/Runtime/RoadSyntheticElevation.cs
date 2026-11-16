using System;using System.IO;using UnityEngine;
namespace EarthUnscripted
{
 public static class RoadSyntheticElevation
 {
  public static RoadElevationProfileDto Create(GeoRoad connector,RoadSyntheticAnchorDto anchor,RoadElevationCurve parent,bool flatAuthoredOtherEnd)
  {
   if(anchor==null||parent==null||anchor.connectorRoadId!=connector.id||anchor.parentRoadId!=parent.RoadId)throw new InvalidDataException("Synthetic profile relationship");var metric=new RoadElevationMetric(connector);int endpoint=Math.Abs(anchor.connectorSourceDistance)<1e-8?0:Math.Abs(anchor.connectorSourceDistance-metric.SourceLength)<1e-8?metric.PointCount-1:-1;if(endpoint<0)throw new InvalidDataException("Synthetic profile requires endpoint");parent.Metric.FrameAtSource(anchor.parentSourceDistance,out var parentPoint,out var parentForward,out var parentRight);metric.FrameAtSource(anchor.connectorSourceDistance,out var connectorPoint,out var connectorForward,out _);if((new Vector2(parentPoint.x,parentPoint.z)-new Vector2(connectorPoint.x,connectorPoint.z)).sqrMagnitude>.0004f)throw new InvalidDataException("Synthetic source attachment XZ");
   var data=RoadElevationBaseline.Create(connector);var value=parent.Evaluate(anchor.parentSourceDistance,0);data.elevations[endpoint]=(float)value.Elevation;var h=new double[data.distances.Length];for(int i=0;i<h.Length;i++)h[i]=metric.ToHorizontal(data.distances[i]);data.gradeDerivatives=RoadElevationBaseline.Slopes(h,data.elevations);var gradient=parentForward*(float)value.CentreGrade+parentRight*(float)value.Bank;data.gradeDerivatives[endpoint]=Vector3.Dot(gradient,connectorForward);if(flatAuthoredOtherEnd){int other=endpoint==0?data.distances.Length-1:0;data.gradeDerivatives[other]=0;data.crownRates[other]=0;data.crossfalls[other]=0;}return data;
  }
 }
}
