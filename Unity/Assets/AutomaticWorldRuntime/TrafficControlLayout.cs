using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace RoadRush.NewbridgePrototype
{
    /// <summary>Presentation only: keep mapped AI control semantics intact while
    /// avoiding synthetic signs at every OSM way split and posts inside roads.</summary>
    public sealed class TrafficControlLayout
    {
        public sealed class Placement { public NewbridgeWorld.Feature road; public NewbridgeWorld.Control control; public Vector3 position; }
        struct Segment { public Vector3 a,b; public float halfWidth; }
        readonly Dictionary<Vector2Int,List<Segment>> cells=new Dictionary<Vector2Int,List<Segment>>();
        readonly NewbridgeWorld.Feature[] roads;
        const float Cell=32;
        static Vector2Int Key(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/Cell),Mathf.FloorToInt(p.z/Cell));
        public readonly List<Placement> Items=new List<Placement>();
        public TrafficControlLayout(NewbridgeWorld.Map map,int side)
        {
            roads=map.features.Where(f=>f.category=="road"&&f.points!=null&&f.points.Length>1).ToArray();
            foreach(var road in roads)for(int i=1;i<road.points.Length;i++)
            {
                var a=road.points[i-1];var b=road.points[i];var right=Vector3.Cross(Vector3.up,(b-a).normalized);
                a+=right*road.roadCentreOffset;b+=right*road.roadCentreOffset;
                var seg=new Segment{a=a,b=b,halfWidth=Mathf.Max(.5f,road.width*.5f)};
                float pad=seg.halfWidth+.7f;var min=Key(Vector3.Min(a,b)-new Vector3(pad,0,pad));var max=Key(Vector3.Max(a,b)+new Vector3(pad,0,pad));
                for(int x=min.x;x<=max.x;x++)for(int z=min.y;z<=max.y;z++)
                {var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<Segment>();list.Add(seg);}
            }
            var repeaters=UKRepeaterSigns.Build(roads).GroupBy(p=>p.road).ToDictionary(g=>g.Key,g=>g.Select(p=>p.control).ToArray());
            var ids=new HashSet<string>();
            foreach(var road in roads)foreach(var c in (road.controls??Array.Empty<NewbridgeWorld.Control>()).Concat(repeaters.TryGetValue(road,out var extra)?extra:Array.Empty<NewbridgeWorld.Control>()))
            {
                if(!ids.Add(c.id)||c.Forward.sqrMagnitude<.1f||!ShouldDisplay(road,c))continue;
                float spacing=c.kind=="speed"?45:c.kind=="oneway"||c.kind=="no_entry"?18:3;
                if(Items.Any(p=>p.control.kind==c.kind&&p.control.value==c.value&&Mathf.Abs(p.control.y-c.y)<3&&(p.control.Position-c.Position).sqrMagnitude<spacing*spacing&&Vector3.Dot(p.control.Forward.normalized,c.Forward.normalized)>.85f))continue;
                if(c.kind=="crossing") {Items.Add(new Placement{road=road,control=c,position=c.Position});continue;}
                if(TryRoadside(road,c,c.repeater?c.mountSide:side,out var position))Items.Add(new Placement{road=road,control=c,position=position});
            }
        }
        static float Limit(NewbridgeWorld.Feature f,bool forward)
        {float directed=forward?f.speedLimitForwardMps:f.speedLimitBackwardMps;return directed>0?directed:f.speedLimitMps;}
        bool ShouldDisplay(NewbridgeWorld.Feature road,NewbridgeWorld.Control c)
        {
            bool synthetic=(c.id??"").Contains(":speed:")||(c.id??"").Contains(":oneway:")||(c.id??"").Contains(":entry:");
            if(!synthetic)return true;
            if(c.kind=="speed")
            {
                float limit=Limit(road,c.direction>=0);
                foreach(var other in roads)
                {
                    if(other==road)continue;
                    foreach(bool atEnd in new[]{true,false})
                    {
                        int n=other.points.Length;var endpoint=other.points[atEnd?n-1:0];
                        if((endpoint-c.Position).sqrMagnitude>16)continue;
                        if(!atEnd&&other.oneway)continue;
                        var incoming=atEnd?endpoint-other.points[n-2]:endpoint-other.points[1];incoming.y=0;
                        if(Vector3.Dot(incoming.normalized,c.Forward.normalized)>.7f&&Mathf.Abs(Limit(other,atEnd)-limit)<.2f&&other.signNational==road.signNational)return false;
                    }
                }
                return true;
            }
            // OSM divides a continuous one-way street into many ways. A new
            // way is not itself an entrance or a prohibited entry.
            var point=road.points[c.kind=="oneway"?0:road.points.Length-1];
            var arms=new List<Vector3>();
            foreach(var other in roads)foreach(bool atEnd in new[]{true,false})
            {
                int n=other.points.Length;var endpoint=other.points[atEnd?n-1:0];if((endpoint-point).sqrMagnitude>16)continue;
                var away=atEnd?other.points[n-2]-endpoint:other.points[1]-endpoint;away.y=0;away.Normalize();
                if(!arms.Any(a=>Vector3.Dot(a,away)>.9f))arms.Add(away);
            }
            return arms.Count>=3;
        }
        public bool ClearOfRoads(Vector3 p)
        {
            if(!cells.TryGetValue(Key(p),out var segments))return true;
            foreach(var s in segments)
            {
                var ab=s.b-s.a;var flat=new Vector2(ab.x,ab.z);float t=Mathf.Clamp01(Vector2.Dot(new Vector2(p.x-s.a.x,p.z-s.a.z),flat)/Mathf.Max(.001f,flat.sqrMagnitude));
                var q=Vector3.Lerp(s.a,s.b,t);
                if(Mathf.Abs(q.y-p.y)<3&&new Vector2(p.x-q.x,p.z-q.z).sqrMagnitude<(s.halfWidth+.65f)*(s.halfWidth+.65f))return false;
            }
            return true;
        }
        bool TryRoadside(NewbridgeWorld.Feature road,NewbridgeWorld.Control c,int side,out Vector3 position)
        {
            var right=Vector3.Cross(Vector3.up,c.Forward.normalized);
            // Centre offset is in the source geometry direction, not travel direction.
            var centre=c.Position+right*road.roadCentreOffset*(c.direction<0?-1:1);
            for(float extra=0;extra<=12;extra+=1.5f)
            {position=centre+right*(road.width*.5f+1+extra)*side;if(ClearOfRoads(position))return true;}
            position=default;return false;
        }
    }
}
