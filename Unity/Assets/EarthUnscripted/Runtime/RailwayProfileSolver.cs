using System;
using System.Collections.Generic;
using System.IO;using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Estimated rail-head profiles. Only additional bridge lift propagates along
    // the graph; this does not claim surveyed track levels or alter road heights.
    public static class RailwayProfileSolver
    {
        public sealed class Report{public bool AlreadyPrepared;public int Nodes,BridgeConstraints,LevelCrossingSamples,LiftedNodes;public float MaximumLift;}
        sealed class Node{public Vector3 Point;public float Base,Lift;public bool Final;public readonly List<Edge> Edges=new List<Edge>();}
        readonly struct Edge{public readonly int To;public readonly float Cost;public Edge(int to,float cost){To=to;Cost=cost;}}
        readonly struct Item{public readonly int Id;public readonly float Lift;public Item(int id,float lift){Id=id;Lift=lift;}}
        sealed class Heap
        {
            readonly List<Item> entries=new List<Item>();public int Count=>entries.Count;
            public void Push(Item item){int i=entries.Count;entries.Add(item);while(i>0){int p=(i-1)/2;if(entries[p].Lift>=item.Lift)break;entries[i]=entries[p];i=p;}entries[i]=item;}
            public Item Pop(){var result=entries[0];var last=entries[entries.Count-1];entries.RemoveAt(entries.Count-1);if(entries.Count==0)return result;int i=0;while(i*2+1<entries.Count){int c=i*2+1;if(c+1<entries.Count&&entries[c+1].Lift>entries[c].Lift)c++;if(entries[c].Lift<=last.Lift)break;entries[i]=entries[c];i=c;}entries[i]=last;return result;}
        }
        sealed class RoadSegment{public GeoRoad Road;public Vector3 A,B;public float SourceStart,SourceLength,MaximumWidth;}
        sealed class RoadIndex
        {
            const float Cell=80;long entries;readonly Dictionary<Vector2Int,List<RoadSegment>> cells=new Dictionary<Vector2Int,List<RoadSegment>>();
            public RoadIndex(GeoRegion r)
            {
                var controls=r.PreparedRoadWidths?.CopySnapshot().ToDictionary(p=>p.roadId,StringComparer.Ordinal);
                void Add(GeoRoad road,Vector3 a,Vector3 b,float start,float length)
                {
                    var segment=new RoadSegment{Road=road,A=a,B=b,SourceStart=start,SourceLength=length,MaximumWidth=r.PreparedRoadWidths==null?road.width:r.PreparedRoadWidths.MaximumBetween(road,start,start+length)};float margin=segment.MaximumWidth*.5f;
                    int ax=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-margin)/Cell),bx=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+margin)/Cell),az=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-margin)/Cell),bz=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+margin)/Cell);long count=((long)bx-ax+1)*((long)bz-az+1);if(count<0||count>16384||entries+count>1000000)throw new InvalidDataException("Railway road-index budget exceeded; prepare a smaller area");entries+=count;
                    for(int z=az;z<=bz;z++)for(int x=ax;x<=bx;x++){var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<RoadSegment>();list.Add(segment);}
                }
                foreach(var road in r.roads)if(!road.tunnel)
                {
                    float start=0;for(int i=1;i<road.points.Length;i++)
                    {
                        var a=road.points[i-1];var b=road.points[i];float length=Vector3.Distance(a,b),end=start+length;
                        if(r.PreparedRoadWidths==null){Add(road,a,b,start,length);start=end;continue;}
                        var dto=controls[road.id];var knots=new List<float>{start,end};knots.AddRange(dto.distances.Where(d=>d>start+.0001f&&d<end-.0001f));knots.Sort();
                        for(int k=1;k<knots.Count;k++){float from=knots[k-1],to=knots[k],maximum=r.PreparedRoadWidths.MaximumBetween(road,from,to);bool constant=Math.Abs(dto.Width(from)-dto.Width(to))<.000001&&Math.Abs(maximum-dto.Width(from))<.000001&&Math.Abs(dto.Width((2.0*from+to)/3)-dto.Width(from))<.000001&&Math.Abs(dto.Width((from+2.0*to)/3)-dto.Width(from))<.000001;int steps=constant?1:Math.Max(1,Mathf.CeilToInt((to-from)/2.5f));if(steps>4096)throw new InvalidDataException("Railway variable-width interval budget");for(int j=0;j<steps;j++){float u=Mathf.Lerp(from,to,j/(float)steps),v=Mathf.Lerp(from,to,(j+1)/(float)steps);Add(road,Vector3.Lerp(a,b,(u-start)/Mathf.Max(.000001f,length)),Vector3.Lerp(a,b,(v-start)/Mathf.Max(.000001f,length)),u,v-u);}}
                        start=end;
                    }
                }
            }
            public IEnumerable<RoadSegment> At(Vector3 a,Vector3 b,float margin)
            {
                var seen=new HashSet<RoadSegment>();int ax=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-margin)/Cell),bx=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+margin)/Cell),az=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-margin)/Cell),bz=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+margin)/Cell);
                for(int z=az;z<=bz;z++)for(int x=ax;x<=bx;x++)if(cells.TryGetValue(new Vector2Int(x,z),out var list))foreach(var s in list)if(seen.Add(s))yield return s;
            }
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static float Distance(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static float Project(Vector2 p,Vector2 a,Vector2 b)=>Mathf.Clamp01(Vector2.Dot(p-a,b-a)/Mathf.Max(.000001f,(b-a).sqrMagnitude));
        static void Closest(Vector3 aa,Vector3 bb,Vector3 cc,Vector3 dd,out float s,out float t,out float distance)
        {
            var a=new Vector2(aa.x,aa.z);var b=new Vector2(bb.x,bb.z);var c=new Vector2(cc.x,cc.z);var d=new Vector2(dd.x,dd.z);var u=b-a;var v=d-c;
            float Cross(Vector2 x,Vector2 y)=>x.x*y.y-x.y*y.x;float den=Cross(u,v);
            if(Mathf.Abs(den)>.00001f){float x=Cross(c-a,v)/den,y=Cross(c-a,u)/den;if(x>=0&&x<=1&&y>=0&&y<=1){s=x;t=y;distance=0;return;}}
            s=0;t=Project(a,c,d);distance=Vector2.Distance(a,Vector2.Lerp(c,d,t));float ts=Project(b,c,d),ds=Vector2.Distance(b,Vector2.Lerp(c,d,ts));if(ds<distance){s=1;t=ts;distance=ds;}
            ts=Project(c,a,b);ds=Vector2.Distance(c,Vector2.Lerp(a,b,ts));if(ds<distance){s=ts;t=0;distance=ds;}
            ts=Project(d,a,b);ds=Vector2.Distance(d,Vector2.Lerp(a,b,ts));if(ds<distance){s=ts;t=1;distance=ds;}
        }
        public static Report Prepare(GeoRegion region)
        {
            if(region==null)throw new ArgumentNullException(nameof(region));var report=new Report();
            if(region.railwayProfilesPrepared){report.AlreadyPrepared=true;return report;}
            var rails=region.railways??Array.Empty<GeoRailway>();if(rails.Length>RailwayMetrics.MaximumWays)throw new InvalidDataException("Railway way budget exceeded; prepare a smaller area");
            if(rails.Length==0){region.railways=rails;region.railwayProfilesPrepared=true;return report;}
            int validated=0;foreach(var rail in rails)
            {
                if(rail==null||rail.points==null||rail.nodeIds==null||rail.points.Length!=rail.nodeIds.Length||rail.points.Length<2||rail.points.Length>RailwayMetrics.MaximumPointsPerWay||!RailwayMetrics.Supported(rail.kind))throw new InvalidDataException("Invalid railway profile input");
                validated+=rail.points.Length;if(validated>RailwayMetrics.MaximumTotalPoints)throw new InvalidDataException("Railway vertex budget exceeded; prepare a smaller area");
                for(int i=0;i<rail.points.Length;i++){var p=rail.points[i];if(string.IsNullOrEmpty(rail.nodeIds[i])||rail.nodeIds[i].Length>128||!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new InvalidDataException("Invalid railway node");}
            }
            var nodes=new List<Node>();var ids=new Dictionary<string,int>(StringComparer.Ordinal);var bindings=new Dictionary<GeoRailway,int[]>();var roadIndex=new RoadIndex(region);int total=0;
            foreach(var rail in rails)
            {
                if(rail==null||rail.points==null||rail.nodeIds==null||rail.points.Length!=rail.nodeIds.Length||rail.points.Length<2||rail.points.Length>RailwayMetrics.MaximumPointsPerWay||!RailwayMetrics.Supported(rail.kind))throw new InvalidDataException("Invalid railway profile input");
                total+=rail.points.Length;if(total>RailwayMetrics.MaximumTotalPoints)throw new InvalidDataException("Railway vertex budget exceeded; prepare a smaller area");var map=new int[rail.points.Length];bindings.Add(rail,map);
                for(int i=0;i<map.Length;i++)
                {
                    var p=rail.points[i];string id=rail.nodeIds[i];if(string.IsNullOrEmpty(id)||id.Length>128||!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new InvalidDataException("Invalid railway node");
                    float height=p.y;
                    if(!rail.bridge&&!rail.tunnel&&RailwayMetrics.Visible(rail.kind))foreach(var segment in roadIndex.At(p,p,rail.width*.5f))
                    {
                        if(segment.Road.bridge||segment.Road.layer!=rail.layer)continue;Closest(p,p,segment.A,segment.B,out _,out float at,out float separation);
                        if(separation<=region.EffectiveWidth(segment.Road,segment.SourceStart+segment.SourceLength*at)*.5f+.2f){height=region.RoadSurfaceHeightOnLayer(p.x,p.z,rail.layer)+.02f;report.LevelCrossingSamples++;break;}
                    }
                    if(ids.TryGetValue(id,out int n))
                    {if(Distance(nodes[n].Point,p)>.01f)throw new InvalidDataException("Shared railway node has conflicting coordinates");nodes[n].Base=Mathf.Max(nodes[n].Base,height);map[i]=n;}
                    else{map[i]=nodes.Count;ids.Add(id,nodes.Count);nodes.Add(new Node{Point=p,Base=height});}
                    if(i>0&&map[i-1]!=map[i]){float cost=Distance(rail.points[i-1],p)*RailwayMetrics.AdditionalRampGrade;nodes[map[i-1]].Edges.Add(new Edge(map[i],cost));nodes[map[i]].Edges.Add(new Edge(map[i-1],cost));}
                }
            }
            foreach(var rail in rails)if(rail.bridge&&!rail.tunnel&&RailwayMetrics.Visible(rail.kind))
            {
                float minimum=float.NegativeInfinity;
                for(int i=1;i<rail.points.Length;i++)foreach(var road in roadIndex.At(rail.points[i-1],rail.points[i],rail.width*.5f+.5f))
                {
                    if(road.Road.bridge&&rail.layer<=road.Road.layer)continue;
                    Closest(rail.points[i-1],rail.points[i],road.A,road.B,out _,out float at,out float separation);
                    float contactWidth=region.EffectiveWidth(road.Road,road.SourceStart+road.SourceLength*at);
                    // Protect the whole short varying interval, not an arbitrary
                    // closest endpoint of two nearly parallel centre lines.
                    if(separation>rail.width*.5f+Mathf.Max(contactWidth,road.MaximumWidth)*.5f+.35f)continue;
                    float roadHeight=Vector3.Lerp(road.A,road.B,at).y;
                    if(region.PreparedRoadWidths!=null&&road.MaximumWidth>contactWidth+.0001f)roadHeight=Mathf.Max(roadHeight,Mathf.Max(road.A.y,road.B.y));
                    float clearance=road.Road.maxVehicleHeight>0?Mathf.Max(3,road.Road.maxVehicleHeight+.25f):RailwayMetrics.MinimumRoadClearance;
                    minimum=Mathf.Max(minimum,roadHeight+clearance+RailwayMetrics.UnderDeckDrop+.04f);report.BridgeConstraints++;
                }
                if(Finite(minimum))foreach(int n in bindings[rail])nodes[n].Lift=Mathf.Max(nodes[n].Lift,minimum-nodes[n].Base);
            }
            var heap=new Heap();for(int i=0;i<nodes.Count;i++)if(nodes[i].Lift>0)heap.Push(new Item(i,nodes[i].Lift));
            while(heap.Count>0)
            {
                var item=heap.Pop();var n=nodes[item.Id];if(n.Final)continue;n.Final=true;
                foreach(var edge in n.Edges){var next=nodes[edge.To];float candidate=n.Lift-edge.Cost;if(!next.Final&&candidate>next.Lift+.0001f){next.Lift=candidate;heap.Push(new Item(edge.To,candidate));}}
            }
            // Publish only after every bounded input and graph constraint succeeds.
            foreach(var rail in rails){var copy=new Vector3[rail.points.Length];var map=bindings[rail];for(int i=0;i<copy.Length;i++){var node=nodes[map[i]];copy[i]=rail.points[i];copy[i].y=node.Base+Mathf.Max(0,node.Lift);}rail.points=copy;rail.estimatedProfile=true;}
            foreach(var node in nodes){if(node.Lift>.001f)report.LiftedNodes++;report.MaximumLift=Mathf.Max(report.MaximumLift,node.Lift);}report.Nodes=nodes.Count;region.railwayProfilesPrepared=true;region.Prepare();return report;
        }
    }
}
