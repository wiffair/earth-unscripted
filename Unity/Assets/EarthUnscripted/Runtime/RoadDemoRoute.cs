using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Route for road footage: begin on the nearest road to the requested origin,
    // honour directed road edges and keep a long run ahead of the camera.
    public static class RoadDemoRoute
    {
        sealed class Edge {public GeoRoad road;public bool forward;public string end;}
        public static RoutePath Build(GeoRegion region,Vector3 origin,float requested=1000)
        {
            var first=region.Nearest(origin,out var nearest,out _);if(first==null||first.tunnel)throw new InvalidOperationException("No suitable starting road");
            var graph=new Dictionary<string,List<Edge>>();
            void Add(string node,Edge edge){if(!graph.TryGetValue(node,out var edges))graph[node]=edges=new List<Edge>();edges.Add(edge);}
            foreach(var road in region.roads)
            {
                if(road.tunnel||road.length<.01f)continue;
                if(road.oneWay>=0)Add(road.start,new Edge{road=road,forward=true,end=road.end});
                if(road.oneWay<=0)Add(road.end,new Edge{road=road,forward=false,end=road.start});
            }
            List<Vector3> best=null;List<string> bestOwners=null;float bestScore=float.MaxValue;
            foreach(bool forward in new[]{true,false})
            {
                if(first.oneWay!=0&&(first.oneWay>0)!=forward)continue;
                float along=region.RoadProgress(first,nearest);var prefix=new List<Vector3>{nearest};float distance=0;
                for(int i=1;i<first.points.Length;i++)
                {
                    distance+=Vector3.Distance(first.points[i-1],first.points[i]);
                    if(forward&&distance>along+.01f)prefix.Add(first.points[i]);
                }
                if(!forward)
                {
                    distance=0;var before=new List<Vector3>();for(int i=0;i<first.points.Length;i++){if(distance<along-.01f)before.Add(first.points[i]);if(i<first.points.Length-1)distance+=Vector3.Distance(first.points[i],first.points[i+1]);}before.Reverse();prefix.AddRange(before);
                }
                float prefixLength=0;for(int i=1;i<prefix.Count;i++)prefixLength+=Vector3.Distance(prefix[i-1],prefix[i]);
                string root=forward?first.end:first.start;var costs=new Dictionary<string,float>{{root,0}};var previous=new Dictionary<string,Tuple<string,Edge>>();
                var pending=new SortedSet<Tuple<float,int,string>>();int ticket=0;pending.Add(Tuple.Create(0f,ticket++,root));
                while(pending.Count>0)
                {
                    var current=pending.Min;pending.Remove(current);if(current.Item1>costs[current.Item3]+.001f)continue;
                    if(!graph.TryGetValue(current.Item3,out var edges))continue;
                    foreach(var edge in edges)
                    {
                        if(edge.road==first)continue;float cost=current.Item1+edge.road.length;
                        if(cost>requested*2||costs.TryGetValue(edge.end,out float old)&&old<=cost)continue;
                        costs[edge.end]=cost;previous[edge.end]=Tuple.Create(current.Item3,edge);pending.Add(Tuple.Create(cost,ticket++,edge.end));
                    }
                }
                foreach(var end in costs.Where(p=>p.Value+prefixLength>=500).OrderBy(p=>Math.Abs(p.Value+prefixLength-requested)).Take(1))
                {
                    var steps=new List<Edge>();for(string node=end.Key;node!=root;){var parent=previous[node];steps.Add(parent.Item2);node=parent.Item1;}steps.Reverse();var points=new List<Vector3>(prefix);var owners=Enumerable.Repeat(first.id,prefix.Count-1).ToList();
                    foreach(var edge in steps){var path=edge.forward?edge.road.points:edge.road.points.Reverse().ToArray();foreach(var point in path.Skip(1))if(points.Count==0||Vector3.Distance(points[points.Count-1],point)>.01f){points.Add(point);owners.Add(edge.road.id);}}
                    float score=Math.Abs(end.Value+prefixLength-requested);if(score<bestScore){bestScore=score;best=points;bestOwners=owners;}
                }
            }
            if(best==null||best.Count<2)throw new InvalidOperationException("No long connected road route from the selected street");
            var copy=region.SnapshotCopy();copy.route=best.ToArray();return new RoutePath(copy,bestOwners.ToArray());
        }
    }
}
