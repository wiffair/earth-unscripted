using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
    // Conservative v1 preparation prerequisite: every verified shared source-node
    // occurrence has explicit plane/collar authority. No graph-wide height blend.
    public static class RoadTopologyAuthority
    {
        sealed class Occurrence {internal GeoRoad road;internal int index;internal double station;}
        public static void Validate(IReadOnlyList<GeoRoad> roads,IReadOnlyList<RoadJunctionPatchDto> patches,IReadOnlyList<RoadSyntheticAnchorDto> anchors,RoadContinuationAuthority continuations=null)
        {
            var roadById=roads.ToDictionary(r=>r.id,StringComparer.Ordinal);var nodes=new Dictionary<string,List<Occurrence>>(StringComparer.Ordinal);var sources=new Dictionary<string,RoadElevationMetric>(StringComparer.Ordinal);int total=0;
            foreach(var road in roads)
            {
                var metric=new RoadElevationMetric(road);sources.Add(road.id,metric);
                for(int i=0;i<road.points.Length;i++)
                {
                    if(++total>300000)throw new InvalidDataException("Road topology occurrence budget");string id=road.sourceNodeIds[i];
                    if(!nodes.TryGetValue(id,out var list))nodes.Add(id,list=new List<Occurrence>());
                    if(list.Count>0){var q=list[0].road.points[list[0].index];var p=road.points[i];if((new Vector2(q.x,q.z)-new Vector2(p.x,p.z)).sqrMagnitude>.0004)throw new InvalidDataException("Shared source node has inconsistent XZ");}
                    list.Add(new Occurrence{road=road,index=i,station=metric.SourceAtPoint(i)});
                }
            }
            var published=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var patch in patches)foreach(var arm in patch.arms)
            {
                var metric=sources[arm.roadId];int index=NodeIndex(metric,arm.sourceDistance);if(index<0||roadById[arm.roadId].sourceNodeIds[index]!=patch.nodeId)continue;
                string key=ArmKey(arm.roadId,index,arm.direction);published.TryGetValue(key,out int count);published[key]=count+1;
            }
            int required=0;
            foreach(var pair in nodes)
            {
                if(pair.Value.Count<2||continuations!=null&&continuations.IsProvenNode(pair.Key))continue;
                foreach(var occurrence in pair.Value)
                {
                    if(occurrence.index>0)RequireArm(pair.Key,occurrence,-1);
                    if(occurrence.index<occurrence.road.points.Length-1)RequireArm(pair.Key,occurrence,1);
                }
            }
            void RequireArm(string node,Occurrence occurrence,int direction)
            {
                if(++required>RoadJunctionPatchDto.MaximumTotalArms)throw new InvalidDataException("Required shared-node arm budget");
                published.TryGetValue(ArmKey(occurrence.road.id,occurrence.index,direction),out int count);
                if(count!=1)throw new InvalidDataException("Road junction preparation required: shared node arm must have exactly one explicit authority "+node+" / "+occurrence.road.id+" / "+direction);
            }
            foreach(var a in anchors)
            {
                var matching=patches.Where(p=>p.syntheticAnchorId==a.id).ToArray();if(matching.Length!=1)throw new InvalidDataException("Synthetic anchor requires exactly one explicit junction authority");
                var p=matching[0];var pm=sources[a.parentRoadId];var cm=sources[a.connectorRoadId];var expected=new List<Tuple<string,double,int>>();
                if(a.parentSourceDistance>StationTolerance(a.parentSourceDistance))expected.Add(Tuple.Create(a.parentRoadId,(double)a.parentSourceDistance,-1));
                if(a.parentSourceDistance<pm.SourceLength-StationTolerance(pm.SourceLength))expected.Add(Tuple.Create(a.parentRoadId,(double)a.parentSourceDistance,1));
                if(a.connectorSourceDistance>StationTolerance(a.connectorSourceDistance))expected.Add(Tuple.Create(a.connectorRoadId,(double)a.connectorSourceDistance,-1));
                if(a.connectorSourceDistance<cm.SourceLength-StationTolerance(cm.SourceLength))expected.Add(Tuple.Create(a.connectorRoadId,(double)a.connectorSourceDistance,1));
                foreach(var e in expected)if(p.arms.Count(x=>x.roadId==e.Item1&&Math.Abs(x.sourceDistance-e.Item2)<=StationTolerance(e.Item2)&&x.direction==e.Item3)!=1)throw new InvalidDataException("Synthetic anchor arm coverage");
                pm.FrameAtSource(a.parentSourceDistance,out var centre,out _,out _);if((new Vector2(centre.x,centre.z)-new Vector2(p.centre.x,p.centre.z)).sqrMagnitude>.0004)throw new InvalidDataException("Synthetic patch source XZ mismatch");
            }
        }
        static string ArmKey(string road,int index,int direction)=>road+"|"+index+"|"+direction;
        internal static int NodeIndex(RoadElevationMetric metric,double station)
        {
            int lo=0,hi=metric.PointCount-1;while(hi-lo>1){int m=(lo+hi)/2;if(metric.SourceAtPoint(m)<=station)lo=m;else hi=m;}
            if(Math.Abs(metric.SourceAtPoint(lo)-station)<=StationTolerance(station))return lo;
            if(Math.Abs(metric.SourceAtPoint(hi)-station)<=StationTolerance(station))return hi;return -1;
        }
        public static double StationTolerance(double station)=>1e-8;
    }
}
