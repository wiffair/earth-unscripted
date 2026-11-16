using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    // Immutable numeric graph shared by wheel sampling and the shader atlas.
    // Only a same-height connection to an open section is a weather portal.
    public sealed class TunnelWeatherIndex
    {
        public struct Source {public Vector3 a,b;public float width,clearance;public bool tunnel;public string kind,aId,bId;}
        public readonly struct Exposure
        {
            public readonly bool Covered;public readonly float Wet,Snow,Rain,Floor,Roof;
            public Exposure(float wet,float snow,float rain,float floor,float roof){Covered=true;Wet=wet;Snow=snow;Rain=rain;Floor=floor;Roof=roof;}
        }
        sealed class Node {public Vector3 p;public string kind;public bool open;public float distance=float.PositiveInfinity,runoff;public readonly List<Link> links=new List<Link>();}
        readonly struct Link {public readonly int to;public readonly float length;public Link(int n,float d){to=n;length=d;}}
        sealed class Segment {public int a,b;public float width,clearance,length;}
        readonly List<Node> nodes=new List<Node>();readonly List<Segment> segments=new List<Segment>();
        readonly Dictionary<Vector2Int,List<Segment>> cells=new Dictionary<Vector2Int,List<Segment>>();
        public int SegmentCount=>segments.Count;
        public int PortalCount {get;private set;}
        public TunnelWeatherIndex(IEnumerable<Source> sources)
        {
            var identities=new Dictionary<(string,string),List<int>>();var bins=new Dictionary<Vector3Int,List<int>>();var seen=new HashSet<(int,int)>();
            int NodeAt(Vector3 p,string kind,bool open,bool create=true,string identity=null)
            {
                if(identity!=null&&!identity.StartsWith("osm:",StringComparison.Ordinal))identity=null;
                if(identity!=null&&identities.TryGetValue((kind,identity),out var known))foreach(int i in known)
                    if(Mathf.Abs(nodes[i].p.y-p.y)<.4f&&new Vector2(nodes[i].p.x-p.x,nodes[i].p.z-p.z).sqrMagnitude<4){nodes[i].open|=open;return i;}
                var key=new Vector3Int(Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y*2),Mathf.FloorToInt(p.z));
                int chosen=-1;float best=create?.04f*.04f:.65f*.65f;
                for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                    if(bins.TryGetValue(key+new Vector3Int(x,y,z),out var near))foreach(int i in near)
                    {var n=nodes[i];float d=new Vector2(n.p.x-p.x,n.p.z-p.z).sqrMagnitude;if(n.kind==kind&&Mathf.Abs(n.p.y-p.y)<.4f&&d<best){best=d;chosen=i;}}
                if(chosen>=0){nodes[chosen].open|=open;if(identity!=null){if(!identities.TryGetValue((kind,identity),out var aliases))identities[(kind,identity)]=aliases=new List<int>();if(!aliases.Contains(chosen))aliases.Add(chosen);}return chosen;}
                if(!create)return -1;
                int index=nodes.Count;nodes.Add(new Node{p=p,kind=kind,open=open});if(!bins.TryGetValue(key,out var list))bins[key]=list=new List<int>();list.Add(index);
                if(identity!=null){if(!identities.TryGetValue((kind,identity),out var ids))identities[(kind,identity)]=ids=new List<int>();ids.Add(index);}return index;
            }
            foreach(var source in sources.Where(s=>s.tunnel))
            {
                int a=NodeAt(source.a,source.kind??"road",false,true,source.aId),b=NodeAt(source.b,source.kind??"road",false,true,source.bId);
                if(!source.tunnel||a==b||!seen.Add((Math.Min(a,b),Math.Max(a,b))))continue;
                float length=Vector3.Distance(nodes[a].p,nodes[b].p);nodes[a].links.Add(new Link(b,length));nodes[b].links.Add(new Link(a,length));
                var segment=new Segment{a=a,b=b,width=Mathf.Max(1,source.width),clearance=Mathf.Max(2.5f,source.clearance),length=length};segments.Add(segment);
                float radius=segment.width*.5f+1;
                for(int z=Mathf.FloorToInt((Mathf.Min(source.a.z,source.b.z)-radius)/32);z<=Mathf.FloorToInt((Mathf.Max(source.a.z,source.b.z)+radius)/32);z++)
                for(int x=Mathf.FloorToInt((Mathf.Min(source.a.x,source.b.x)-radius)/32);x<=Mathf.FloorToInt((Mathf.Max(source.a.x,source.b.x)+radius)/32);x++)
                {var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<Segment>();list.Add(segment);}
            }
            if(segments.Count==0)return;
            foreach(var source in sources.Where(s=>!s.tunnel)){NodeAt(source.a,source.kind??"road",true,false,source.aId);NodeAt(source.b,source.kind??"road",true,false,source.bId);}
            var portals=Enumerable.Range(0,nodes.Count).Where(i=>nodes[i].open&&nodes[i].links.Count>0).ToArray();PortalCount=portals.Length;
            int serial=0;var pending=new SortedSet<(float,int,int)>();
            foreach(int i in portals){nodes[i].distance=0;pending.Add((0,serial++,i));}
            while(pending.Count>0)
            {
                var item=pending.Min;pending.Remove(item);int i=item.Item3;if(item.Item1>nodes[i].distance+.001f)continue;
                foreach(var edge in nodes[i].links){float d=item.Item1+edge.length;if(d<nodes[edge.to].distance){nodes[edge.to].distance=d;pending.Add((d,serial++,edge.to));}}
            }
            // Conservative runoff approximation: water enters at a confirmed portal,
            // follows level/downhill connected pavement and decays over200m. It
            // never climbs a crest or invents a permanent flooded tunnel.
            foreach(int portal in portals)
            {
                var distances=new Dictionary<int,float>{{portal,0}};var queue=new Queue<int>();queue.Enqueue(portal);float level=nodes[portal].p.y;
                while(queue.Count>0)
                {
                    int i=queue.Dequeue();float distance=distances[i],drop=level-nodes[i].p.y;
                    nodes[i].runoff=Mathf.Max(nodes[i].runoff,.32f*Mathf.Exp(-distance/160)*Mathf.Clamp01(drop/.35f));
                    foreach(var edge in nodes[i].links)
                    {float next=distance+edge.length;if(next>200||nodes[edge.to].p.y>nodes[i].p.y+.005f)continue;if(distances.TryGetValue(edge.to,out float old)&&old<=next)continue;distances[edge.to]=next;queue.Enqueue(edge.to);}
                }
            }
        }
        Exposure At(Segment s,float t)
        {
            var a=nodes[s.a];var b=nodes[s.b];float distance=Mathf.Min(a.distance+t*s.length,b.distance+(1-t)*s.length);
            float wet=Mathf.Clamp01(.9f*Mathf.Exp(-distance/18)+.13f*Mathf.Exp(-distance/55));wet=Mathf.Max(wet,Mathf.Lerp(a.runoff,b.runoff,t));
            float snow=Mathf.Clamp01(.65f*Mathf.Exp(-distance/7)+.08f*Mathf.Exp(-distance/22));float floor=Mathf.Lerp(a.p.y,b.p.y,t);
            return new Exposure(wet,snow,Mathf.Exp(-distance/3),floor,floor+s.clearance);
        }
        float Projection(Segment s,Vector3 p,out float distance)
        {var a=nodes[s.a].p;var d=nodes[s.b].p-a;float t=Mathf.Clamp01(((p.x-a.x)*d.x+(p.z-a.z)*d.z)/Mathf.Max(.0001f,d.x*d.x+d.z*d.z));distance=new Vector2(p.x-a.x-d.x*t,p.z-a.z-d.z*t).magnitude;return t;}
        public Exposure Probe(Vector3 position)
        {
            if(!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(position.x/32),Mathf.FloorToInt(position.z/32)),out var list))return default;
            Exposure best=default;float gap=float.PositiveInfinity;
            foreach(var s in list)
            {
                float t=Projection(s,position,out float distance);if(distance>s.width*.5f+.75f)continue;var e=At(s,t);
                if(position.y<e.Floor-.7f||position.y>e.Roof+.1f)continue;float dy=Mathf.Abs(position.y-e.Floor);
                float score=dy*100+distance;
                if(score<gap){gap=score;best=e;}
            }
            return best;
        }
        public bool Intersects(Vector2 minimum,float span)
        {foreach(var s in segments){var a=nodes[s.a].p;var b=nodes[s.b].p;float r=s.width*.5f+4;if(Mathf.Max(a.x,b.x)+r>=minimum.x&&Mathf.Min(a.x,b.x)-r<=minimum.x+span&&Mathf.Max(a.z,b.z)+r>=minimum.y&&Mathf.Min(a.z,b.z)-r<=minimum.y+span)return true;}return false;}
        public Color[][] Raster(Vector2 minimum,float span,int size,float observerHeight)
        {
            var layers=new Color[4][];for(int i=0;i<4;i++)layers[i]=new Color[size*size];float step=span/size;
            foreach(var s in segments)
            {
                Vector3 a=nodes[s.a].p,b=nodes[s.b].p;float half=s.width*.5f+step*.71f;
                int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x)-half-minimum.x)/step),0,size-1),x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x,b.x)+half-minimum.x)/step),0,size-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z,b.z)-half-minimum.y)/step),0,size-1),z1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.z,b.z)+half-minimum.y)/step),0,size-1);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    var p=new Vector3(minimum.x+(x+.5f)*step,0,minimum.y+(z+.5f)*step);float t=Projection(s,p,out float distance);if(distance>half)continue;var e=At(s,t);int cell=z*size+x,slot=-1;float far=-1;
                    for(int i=0;i<4;i++)
                    {
                        var old=layers[i][cell];if(old.g<=old.r){slot=i;break;}
                        if(Mathf.Abs(old.r-e.Floor)<.6f){slot=i;break;}
                        float delta=Mathf.Abs(old.r-observerHeight);if(delta>far){far=delta;slot=i;}
                    }
                    var previous=layers[slot][cell];if(previous.g>previous.r&&Mathf.Abs(previous.r-e.Floor)>=.6f&&Mathf.Abs(e.Floor-observerHeight)>Mathf.Abs(previous.r-observerHeight))continue;
                    bool same=previous.g>previous.r&&Mathf.Abs(previous.r-e.Floor)<.6f;
                    layers[slot][cell]=new Color(e.Floor,e.Roof,same?Mathf.Max(previous.b,e.Wet):e.Wet,same?Mathf.Max(previous.a,e.Snow):e.Snow);
                }
            }
            return layers;
        }
    }
}
