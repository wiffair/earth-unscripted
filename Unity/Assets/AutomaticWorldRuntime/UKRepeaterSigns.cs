using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace RoadRush.NewbridgePrototype
{
    // DfT TSM3 table8-4, amended for the Welsh default20mph. Unknown lighting
    // is not silently treated as lit/unlit; national-limit exceptions are explicit.
    public static class UKRepeaterSigns
    {
        public readonly struct Rule
        {
            public readonly float first,interval,diameter;public bool Enabled=>interval>0;
            public Rule(float first,float interval,float diameter){this.first=first;this.interval=interval;this.diameter=diameter;}
        }
        public static Rule Policy(string region,int mph,string lighting,bool national,bool motorway,bool dual,string zone)
        {
            if(region!="GB-WLS"&&region!="GB-ENG"&&region!="GB-SCT"&&region!="GB-NIR")return default;
            if(!string.IsNullOrEmpty(zone)&&(zone.Contains("20")||zone.Contains("40")))return default;
            if(national)
            {if(motorway||lighting!="yes")return default;return dual?new Rule(450,600,.6f):new Rule(400,500,.45f);}
            if(mph==(region=="GB-WLS"?20:30)&&lighting!="no")return default;
            switch(mph)
            {
                case 20:return new Rule(200,200,.3f);
                case 30:return new Rule(200,250,.3f);
                case 40:return new Rule(250,350,.3f);
                case 50:return new Rule(350,450,.45f);
                case 60:return new Rule(400,500,.6f);
                default:return default;
            }
        }
        public sealed class Item {public NewbridgeWorld.Feature road;public NewbridgeWorld.Control control;}
        sealed class Edge
        {
            public NewbridgeWorld.Feature road;public float lo,hi;public int direction;public string value,key,id;public Rule rule;
            public Vector3 start,end,heading;public Edge next;public int predecessors;
        }
        static Vector3 At(NewbridgeWorld.Feature road,float station)
        {
            for(int i=1;i<road.points.Length;i++){float length=Vector2.Distance(new Vector2(road.points[i-1].x,road.points[i-1].z),new Vector2(road.points[i].x,road.points[i].z));if(station<=length)return Vector3.Lerp(road.points[i-1],road.points[i],station/Mathf.Max(.001f,length));station-=length;}return road.points.Last();
        }
        static float Length(NewbridgeWorld.Feature f)=>f.points.Zip(f.points.Skip(1),(a,b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z))).Sum();
        public static List<Item> Build(IEnumerable<NewbridgeWorld.Feature> roads)
        {
            var sourceRoads=roads.ToArray();var edges=new List<Edge>();
            foreach(var f in sourceRoads)
            {
                if(f.points==null||f.points.Length<2||f.signContexts==null)continue;
                float length=Length(f);
                foreach(var span in f.signContexts)foreach(int d in f.oneway?new[]{1}:new[]{1,-1})
                {
                    string raw=d>0?f.signLimitForward:f.signLimitBackward;bool national=f.signNational||(raw??"").Contains("nsl")||raw=="national";
                    float mps=d>0?f.speedLimitForwardMps:f.speedLimitBackwardMps;if(mps<=0)mps=f.speedLimitMps;
                    int mph=Mathf.RoundToInt(mps/ .44704f);var rule=Policy(span.region,mph,f.signLighting,national,(f.kind??"").StartsWith("motorway"),f.signDual,f.signZone);
                    if(!rule.Enabled||string.IsNullOrEmpty(raw)&&!national)continue;
                    float lo=Mathf.Clamp(span.start,0,length),hi=Mathf.Clamp(span.end,0,length);if(hi-lo<.01f)continue;
                    var a=At(f,d>0?lo:hi);var b=At(f,d>0?hi:lo);var tangent=At(f,d>0?Mathf.Min(hi,lo+2):Mathf.Max(lo,hi-2))-a;tangent.y=0;
                    string value=national?"national":mph.ToString();
                    edges.Add(new Edge{road=f,lo=lo,hi=hi,direction=d,start=a,end=b,heading=tangent.normalized,value=value,rule=rule,id=f.id+":"+d+":"+lo,key=span.region+"/"+value+"/"+f.signLighting+"/"+f.signDual+"/"+f.signZone});
                }
            }
            // Join source-way fragments before measuring spacing. Never reset
            // the interval merely because OSM divided a road into another way.
            var starts=new Dictionary<Vector2Int,List<Edge>>();Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/4),Mathf.FloorToInt(p.z/4));
            foreach(var e in edges){var k=Cell(e.start);if(!starts.TryGetValue(k,out var list))starts[k]=list=new List<Edge>();list.Add(e);}
            foreach(var e in edges)
            {
                var k=Cell(e.end);var following=new List<Edge>();var incoming=At(e.road,e.direction>0?Mathf.Max(e.lo,e.hi-2):Mathf.Min(e.hi,e.lo+2));var heading=e.end-incoming;heading.y=0;
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(starts.TryGetValue(k+new Vector2Int(x,z),out var list))foreach(var other in list)
                    if(other!=e&&other.key==e.key&&(other.start-e.end).sqrMagnitude<.25f&&Vector3.Dot(heading.normalized,other.heading)>.65f)following.Add(other);
                if(following.Count==1){e.next=following[0];e.next.predecessors++;}
            }
            var seen=new HashSet<Edge>();var result=new List<Item>();
            foreach(var root in edges.OrderBy(e=>e.predecessors==0?0:1).ThenBy(e=>e.id,StringComparer.Ordinal))
            {
                if(seen.Contains(root))continue;float offset=0,next=root.rule.first;int index=0;var edge=root;
                // A newly lit non-default road needs a reminder at the lighting
                // transition. Likewise an unlit default-speed continuation must
                // not leave drivers relying on street lamps that have ended.
                foreach(var before in sourceRoads)
                {
                    if(before==root.road||before.points==null||before.points.Length<2||before.signLighting==root.road.signLighting||before.signLighting=="unknown"||root.road.signLighting=="unknown")continue;
                    bool joined=(before.points.Last()-root.start).sqrMagnitude<.25f||(!before.oneway&&(before.points[0]-root.start).sqrMagnitude<.25f);
                    if(!joined)continue;
                    float prior=before.speedLimitMps;float current=root.direction>0?root.road.speedLimitForwardMps:root.road.speedLimitBackwardMps;if(current<=0)current=root.road.speedLimitMps;
                    if(Mathf.Abs(prior-current)<.2f&&before.signNational==root.road.signNational){next=0;break;}
                }
                while(edge!=null&&seen.Add(edge))
                {
                    float length=edge.hi-edge.lo;
                    while(next<=offset+length-.01f)
                    {
                        float station=edge.direction>0?edge.lo+next-offset:edge.hi-(next-offset);var p=At(edge.road,station);var tangent=At(edge.road,Mathf.Min(edge.hi,station+1))-At(edge.road,Mathf.Max(edge.lo,station-1));tangent.y=0;tangent=tangent.normalized*edge.direction;
                        var c=new NewbridgeWorld.Control{id="uk-repeater:"+root.id+":"+index,kind="speed",value=edge.value,x=p.x,y=p.y,z=p.z,dx=tangent.x,dz=tangent.z,direction=edge.direction,station=station,repeater=true,diameter=edge.rule.diameter,mountSide=index%2==0?-1:1};result.Add(new Item{road=edge.road,control=c});index++;next+=edge.rule.interval;
                    }
                    offset+=length;edge=edge.next;
                }
            }
            return result;
        }
    }
}
