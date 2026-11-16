using System;
using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    // Immutable source-node graph. Crossing X/Z coordinates alone never make
    // a junction; one-way links keep the numeric source's travel direction.
    public sealed class RoadNavigationGraph
    {
        struct Edge {public int to;public float cost;public Edge(int t,float c){to=t;cost=c;}}
        sealed class Node {public Vector3 point;public readonly List<Edge> links=new List<Edge>();}
        sealed class Road {public GeoRoad source;public int[] nodes;}
        struct Snap {public Road road;public int index;public float fraction,distance;public Vector3 point;}
        readonly List<Node> nodes=new List<Node>();readonly List<Road> roads=new List<Road>();
        readonly Dictionary<string,List<int>> identities=new Dictionary<string,List<int>>(StringComparer.Ordinal);
        int NodeFor(string key,Vector3 point)
        {
            if(!identities.TryGetValue(key,out var matches))identities[key]=matches=new List<int>();
            foreach(int n in matches)if(Mathf.Abs(nodes[n].point.y-point.y)<.75f && new Vector2(nodes[n].point.x-point.x,nodes[n].point.z-point.z).sqrMagnitude<.25f)return n;
            int id=nodes.Count;nodes.Add(new Node{point=point});matches.Add(id);return id;
        }
        public RoadNavigationGraph(GeoRoad[] source)
        {
            foreach(var road in source??Array.Empty<GeoRoad>())
            {
                if(road.points==null||road.points.Length<2||road.kind=="footway"||road.kind=="steps"||road.kind=="cycleway"||road.kind=="path"||road.kind=="pedestrian")continue;
                var row=new Road{source=road,nodes=new int[road.points.Length]};
                for(int i=0;i<row.nodes.Length;i++)
                {
                    string key=road.sourceNodeIds!=null&&i<road.sourceNodeIds.Length?road.sourceNodeIds[i]:i==0?road.start:i==row.nodes.Length-1?road.end:null;
                    row.nodes[i]=NodeFor(string.IsNullOrEmpty(key)?road.id+":"+i:key,road.points[i]);
                    if(i==0)continue;int a=row.nodes[i-1],b=row.nodes[i];float cost=Vector3.Distance(road.points[i-1],road.points[i]);
                    nodes[a].links.Add(new Edge(b,cost));if(road.oneWay==0)nodes[b].links.Add(new Edge(a,cost));
                }
                roads.Add(row);
            }
        }
        Snap Nearest(Vector3 point,bool heightAware)
        {
            var best=new Snap{distance=float.PositiveInfinity};float score=float.PositiveInfinity;
            foreach(var road in roads)for(int i=1;i<road.nodes.Length;i++)
            {
                var a=road.source.points[i-1];var b=road.source.points[i];var delta=b-a;
                float t=Mathf.Clamp01(((point.x-a.x)*delta.x+(point.z-a.z)*delta.z)/Mathf.Max(.0001f,delta.x*delta.x+delta.z*delta.z));
                var p=Vector3.Lerp(a,b,t);float distance=new Vector2(point.x-p.x,point.z-p.z).magnitude;float candidate=distance+(heightAware?Mathf.Abs(point.y-p.y)*2:0);
                if(candidate>=score)continue;score=candidate;best=new Snap{road=road,index=i-1,fraction=t,point=p,distance=distance};
            }
            return best;
        }
        sealed class Queue
        {
            readonly List<(int node,float cost)> heap=new List<(int,float)>();public int Count=>heap.Count;
            public void Push(int node,float cost){heap.Add((node,cost));int i=heap.Count-1;while(i>0){int p=(i-1)/2;if(heap[p].cost<=cost)break;heap[i]=heap[p];i=p;}heap[i]=(node,cost);}
            public (int node,float cost) Pop(){var result=heap[0];var last=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);if(heap.Count==0)return result;int i=0;while(i*2+1<heap.Count){int child=i*2+1;if(child+1<heap.Count&&heap[child+1].cost<heap[child].cost)child++;if(last.cost<=heap[child].cost)break;heap[i]=heap[child];i=child;}heap[i]=last;return result;}
        }
        public NavigationRoute Plan(Vector3 start,Vector3 destination)
        {
            var from=Nearest(start,true);var to=Nearest(destination,false);
            if(from.road==null||from.distance>100)throw new InvalidOperationException("Move onto a mapped road before planning a route.");
            if(to.road==null||to.distance>150)throw new InvalidOperationException("Choose a destination on a road shown on this map.");
            int sa=from.road.nodes[from.index],sb=from.road.nodes[from.index+1],ta=to.road.nodes[to.index],tb=to.road.nodes[to.index+1];
            float sl=Vector3.Distance(from.road.source.points[from.index],from.road.source.points[from.index+1]);float tl=Vector3.Distance(to.road.source.points[to.index],to.road.source.points[to.index+1]);
            float best=float.PositiveInfinity;int finish=-1;
            bool direct=from.road==to.road&&from.index==to.index&&(from.road.source.oneWay==0||to.fraction>=from.fraction);
            if(direct)best=Vector3.Distance(from.point,to.point);
            var distances=new float[nodes.Count];var previous=new int[nodes.Count];for(int i=0;i<nodes.Count;i++){distances[i]=float.PositiveInfinity;previous[i]=-1;}
            var queue=new Queue();void Seed(int n,float cost){if(cost>=distances[n])return;distances[n]=cost;queue.Push(n,cost);}
            Seed(sb,(1-from.fraction)*sl);if(from.road.source.oneWay==0||from.fraction<.00001f)Seed(sa,from.fraction*sl);
            while(queue.Count>0)
            {
                var current=queue.Pop();if(current.cost!=distances[current.node])continue;if(current.cost>=best)break;
                float tail=current.node==ta?to.fraction*tl:current.node==tb&&(to.road.source.oneWay==0||to.fraction>.99999f)?(1-to.fraction)*tl:float.PositiveInfinity;
                if(current.cost+tail<best){best=current.cost+tail;finish=current.node;direct=false;}
                foreach(var edge in nodes[current.node].links)
                {float candidate=current.cost+edge.cost;if(candidate>=distances[edge.to])continue;distances[edge.to]=candidate;previous[edge.to]=current.node;queue.Push(edge.to,candidate);}
            }
            if(float.IsPositiveInfinity(best))throw new InvalidOperationException("Those roads are not connected in the available map. Choose another road or prepare more of this area.");
            var points=new List<Vector3>{from.point};
            if(!direct){var path=new List<Vector3>();for(int n=finish;n>=0;n=previous[n])path.Add(nodes[n].point);path.Reverse();points.AddRange(path);}
            points.Add(to.point);for(int i=points.Count-1;i>0;i--)if(Vector3.Distance(points[i],points[i-1])<.01f)points.RemoveAt(i);
            if(points.Count<2)points.Add(points[0]);return new NavigationRoute(points.ToArray());
        }
    }
    public sealed class NavigationRoute
    {
        public readonly Vector3[] points;public readonly float[] stations;public float Length=>stations[stations.Length-1];
        public readonly struct Turn {public readonly float station,angle;public Turn(float s,float a){station=s;angle=a;}}
        public readonly Turn[] turns;
        public NavigationRoute(Vector3[] path)
        {
            points=path;stations=new float[path.Length];for(int i=1;i<path.Length;i++)stations[i]=stations[i-1]+Vector3.Distance(path[i-1],path[i]);
            var list=new List<Turn>();float last=-100;
            for(int i=1;i<path.Length-1;i++)
            {
                float at=stations[i];if(at<20||at>Length-15||at-last<35)continue;
                var a=At(at)-At(at-15);var b=At(at+15)-At(at);a.y=b.y=0;
                float angle=Mathf.Atan2(a.z*b.x-a.x*b.z,Vector3.Dot(a,b))*Mathf.Rad2Deg;
                if(Mathf.Abs(angle)<30)continue;list.Add(new Turn(at,angle));last=at;
            }
            turns=list.ToArray();
        }
        public Vector3 At(float station)
        {
            station=Mathf.Clamp(station,0,Length);int i=Array.BinarySearch(stations,station);if(i>=0)return points[i];i=Mathf.Clamp(~i-1,0,points.Length-2);
            return Vector3.Lerp(points[i],points[i+1],(station-stations[i])/Mathf.Max(.0001f,stations[i+1]-stations[i]));
        }
        public float Closest(Vector3 position,out float separation)
        {
            float best=float.PositiveInfinity,station=0;separation=best;
            for(int i=1;i<points.Length;i++)
            {
                var a=points[i-1];var delta=points[i]-a;float t=Mathf.Clamp01(((position.x-a.x)*delta.x+(position.z-a.z)*delta.z)/Mathf.Max(.0001f,delta.x*delta.x+delta.z*delta.z));var p=a+delta*t;
                float error=(new Vector2(p.x-position.x,p.z-position.z)).sqrMagnitude+Mathf.Pow(p.y-position.y,2);
                if(error>=best)continue;best=error;station=Mathf.Lerp(stations[i-1],stations[i],t);separation=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(position.x,position.z));
            }
            return station;
        }
    }
}
