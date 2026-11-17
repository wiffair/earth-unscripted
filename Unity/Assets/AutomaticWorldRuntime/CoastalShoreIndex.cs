using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
 public sealed partial class NewbridgeWorld
 {
  readonly struct ShoreSegment
  {public readonly Vector2 a,delta;public readonly float denominator;public ShoreSegment(Vector3 first,Vector3 last){a=new Vector2(first.x,first.z);delta=new Vector2(last.x-first.x,last.z-first.z);denominator=Mathf.Max(.0001f,delta.sqrMagnitude);}}
  sealed class ShoreIndex{public readonly Dictionary<Vector2Int,List<ShoreSegment>> cells=new Dictionary<Vector2Int,List<ShoreSegment>>();}
  ShoreIndex coastalShoreIndex;CoastalPatch coastalShoreOwner;
  static ShoreIndex BuildCoastalShoreIndex(CoastalPatch patch)
  {
   var result=new ShoreIndex();
   foreach(var ring in patch?.shore??Array.Empty<CoastRing>())for(int i=1;i<(ring.points?.Length??0);i++)
   {
    var a=ring.points[i-1];var b=ring.points[i];var segment=new ShoreSegment(a,b);
    // The bed already caps distance at250m. Only segments within that exact
    // radius can influence it; expanded AABBs conservatively retain all of them.
    for(int z=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-250)/256);z<=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+250)/256);z++)
     for(int x=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-250)/256);x<=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+250)/256);x++)
     {var key=new Vector2Int(x,z);if(!result.cells.TryGetValue(key,out var list))result.cells[key]=list=new List<ShoreSegment>();list.Add(segment);}
   }
   return result;
  }
  bool TryCoastalShore(float x,float z,float radius,out float distance,out Vector2 closest)
  {
   if(coastalShoreOwner!=coastalPatch||coastalShoreIndex==null){coastalShoreIndex=BuildCoastalShoreIndex(coastalPatch);coastalShoreOwner=coastalPatch;}
   float squared=radius*radius;var point=new Vector2(x,z);closest=point;bool found=false;
   if(coastalShoreIndex.cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(x/256),Mathf.FloorToInt(z/256)),out var segments))foreach(var segment in segments)
   {
    float station=Mathf.Clamp01(Vector2.Dot(point-segment.a,segment.delta)/segment.denominator);var projected=segment.a+segment.delta*station;
    float candidate=(point-projected).sqrMagnitude;if(candidate>squared)continue;squared=candidate;closest=projected;found=true;
   }
   distance=Mathf.Sqrt(squared);return found;
  }
  float CoastalShoreDistance(float x,float z){TryCoastalShore(x,z,250,out float distance,out _);return distance;}
  readonly struct CoastalRoadConstraint
  {
   public readonly Map source;public readonly float roadY,envelope,shoreWeight,roadWeight,riseWeight;
   public CoastalRoadConstraint(Map owner,float road=0,float bank=0,float shore=0,float distance=0,float rise=0){source=owner;roadY=road;envelope=bank;shoreWeight=shore;roadWeight=distance;riseWeight=rise;}
  }
  readonly Dictionary<Vector2,CoastalRoadConstraint> coastalRoadConstraints=new Dictionary<Vector2,CoastalRoadConstraint>();
  CoastalPatch coastalConstraintPatch;int coastalConstraintRevision=-1,coastalConstraintRoadRevision=-1;
  float ConstrainCoastalRoadWedge(float x,float z,float ground)
  {
   if(coastalConstraintPatch!=coastalPatch||coastalConstraintRevision!=coastalRevision||coastalConstraintRoadRevision!=roadWaterSourceRevision)
   {coastalRoadConstraints.Clear();coastalConstraintPatch=coastalPatch;coastalConstraintRevision=coastalRevision;coastalConstraintRoadRevision=roadWaterSourceRevision;}
   var source=AreaAt(x,z);
   if(source==null||source.elevationQuality==null||!source.elevationQuality.StartsWith("approximate terrain",StringComparison.OrdinalIgnoreCase)||ground<MappedSeaLevel+8)return ground;
   var key=new Vector2(x,z);
   if(!coastalRoadConstraints.TryGetValue(key,out var constraint)||constraint.source!=source)
   {constraint=ResolveCoastalRoadConstraint(x,z,source);if(coastalRoadConstraints.Count<200000)coastalRoadConstraints[key]=constraint;}
   float excess=ground-Mathf.Max(constraint.roadY,MappedSeaLevel);
   float influence=Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,12,excess))*constraint.shoreWeight*constraint.roadWeight*constraint.riseWeight;
   return Mathf.Lerp(ground,Mathf.Min(ground,constraint.envelope),influence);
  }
  CoastalRoadConstraint ResolveCoastalRoadConstraint(float x,float z,Map source)
  {
   var unchanged=new CoastalRoadConstraint(source);
   if(!TryCoastalShore(x,z,32,out float shoreDistance,out var shore)||!spatialByMap.TryGetValue(source,out var spatial))return unchanged;
   // A low coastal road and the mapped shore constrain only their intervening
   // coarse-data strip. Do not infer the height of genuine inland/cliff terrain.
   float bestSquared=64*64;Segment nearest=default;Vector2 roadPoint=default;bool found=false;
   int cellX=Mathf.FloorToInt(x/SegmentCellSize),cellZ=Mathf.FloorToInt(z/SegmentCellSize);var point=new Vector2(x,z);
   for(int iz=cellZ-4;iz<=cellZ+4;iz++)for(int ix=cellX-4;ix<=cellX+4;ix++)
   {
    if(!spatial.segments.TryGetValue(new Vector2Int(ix,iz),out var rows))continue;
    foreach(var segment in rows)
    {
     var feature=segment.feature;if(feature.category!="road"||feature.bridge||feature.tunnel)continue;
     var a=new Vector2(segment.a.x,segment.a.z);var delta=new Vector2(segment.b.x-segment.a.x,segment.b.z-segment.a.z);
     float station=Mathf.Clamp01(Vector2.Dot(point-a,delta)/Mathf.Max(.0001f,delta.sqrMagnitude));var projected=a+delta*station;float squared=(point-projected).sqrMagnitude;
     if(squared>=bestSquared||Vector2.Dot(shore-point,projected-point)>=0)continue;
     bestSquared=squared;nearest=segment;roadPoint=projected;found=true;
    }
   }
   if(!found)return unchanged;
   Project(nearest,roadPoint.x,roadPoint.y,out float roadY);roadY=RoadSurfacePoint(nearest.feature,new Vector3(roadPoint.x,roadY,roadPoint.y)).y;
   float rise=roadY-MappedSeaLevel;if(rise<0||rise>=18)return unchanged;
   float roadDistance=Mathf.Sqrt(bestSquared),t=shoreDistance/Mathf.Max(.001f,shoreDistance+roadDistance);
   float envelope=Mathf.Lerp(MappedSeaLevel,roadY,t)+1.5f*Mathf.Sin(Mathf.PI*t);
   return new CoastalRoadConstraint(source,roadY,envelope,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(24,32,shoreDistance)),
      1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(48,64,roadDistance)),1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(14,18,rise)));
  }
 }
}
