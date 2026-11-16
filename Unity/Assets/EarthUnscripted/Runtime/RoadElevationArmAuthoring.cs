using System;using System.Collections.Generic;using System.IO;
namespace EarthUnscripted
{
 // Host authoring only. Traverses declared/proved continuations; guests only
 // consume the published spans through RoadElevationArmPath and never call this.
 public static class RoadElevationArmAuthoring
 {
  public sealed class Result{public bool Success;public RoadJunctionArmDto Arm;public double AvailableRaw;public string Failure,BlockedRoad,BlockedNode;}
  public static Result Walk(string roadId,double sourceS,int direction,float coreLength,float collarLength,IReadOnlyDictionary<string,GeoRoad> roads,RoadContinuationAuthority registry,IReadOnlyList<RoadContinuationDto> declarations)
  {
   if(roads==null||registry==null||declarations==null||(direction!=1&&direction!=-1)||float.IsNaN(coreLength)||float.IsNaN(collarLength)||coreLength<0||collarLength<=coreLength||collarLength>500)throw new InvalidDataException("Arm authoring request");var spans=new List<RoadJunctionSpanDto>();var seen=new HashSet<string>(StringComparer.Ordinal);string current=roadId;double at=sourceS,remaining=collarLength,total=0;int dir=direction;
   while(remaining>1e-8)
   {
    if(spans.Count>=64)return Fail("span-budget");if(!roads.TryGetValue(current,out var road)||!seen.Add(current))return Fail("unknown-road-or-cycle");var metric=registry.Metric(current);if(at<0||at>metric.SourceLength+1e-8)return Fail("source-domain");double available=dir>0?metric.SourceLength-at:at,take=Math.Min(remaining,available);if(take>1e-8){spans.Add(new RoadJunctionSpanDto{roadId=current,fromSourceDistance=at,toSourceDistance=at+dir*take});remaining-=take;total+=take;}if(remaining<=1e-8)break;
    bool end=dir>0;var declaration=Find(current,end);if(declaration==null)return Fail("unproven-boundary",road.sourceNodeIds[end?road.sourceNodeIds.Length-1:0]);bool fromA=declaration.roadA==current&&declaration.atEndA==end;string next=fromA?declaration.roadB:declaration.roadA;bool nextEnd=fromA?declaration.atEndB:declaration.atEndA;if(!registry.Joins(current,end,next,nextEnd))return Fail("unproved-continuation",declaration.nodeId);current=next;dir=nextEnd?-1:1;at=nextEnd?registry.Metric(current).SourceLength:0;
   }
   var arm=new RoadJunctionArmDto{roadId=roadId,sourceDistance=sourceS,direction=direction,coreLength=coreLength,collarLength=collarLength,spans=spans.ToArray()};RoadJunctionSpanRules.Validate(arm,registry);return new Result{Success=true,Arm=arm,AvailableRaw=total};
   RoadContinuationDto Find(string id,bool end){RoadContinuationDto found=null;foreach(var d in declarations)if(d.roadA==id&&d.atEndA==end||d.roadB==id&&d.atEndB==end){if(found!=null)throw new InvalidDataException("Ambiguous declared continuation");found=d;}return found;}
   Result Fail(string why,string node=null)=>new Result{Success=false,AvailableRaw=total,Failure=why,BlockedRoad=current,BlockedNode=node};
  }
 }
}
