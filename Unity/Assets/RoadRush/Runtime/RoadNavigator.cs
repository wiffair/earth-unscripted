using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    public sealed class RoadNavigator
    {
        readonly GeoRegion region;readonly Dictionary<string,List<GeoRoad>> nodes=new Dictionary<string,List<GeoRoad>>();
        public RoadNavigator(GeoRegion region)
        {
            this.region=region;
            foreach(var r in region.roads)if(!r.tunnel)foreach(var key in new[]{r.start,r.end}){if(!nodes.TryGetValue(key,out var list))nodes[key]=list=new List<GeoRoad>();list.Add(r);}
        }
        public List<Vector3> Find(Vector3 from,Vector3 to)
        {
            var path=new List<Vector3>();var a=region.Nearest(from,out var pa,out _);var b=region.Nearest(to,out var pb,out _);if(a==null||b==null||a.tunnel||b.tunnel)return path;
            float sa=region.RoadProgress(a,pa),sb=region.RoadProgress(b,pb);
            if(a==b&&(sb>=sa?a.oneWay!=-1:a.oneWay!=1)){Arc(path,a,sa,sb);return path;}
            var cost=new Dictionary<string,float>();var parent=new Dictionary<string,string>();var edge=new Dictionary<string,GeoRoad>();var closed=new HashSet<string>();
            if(a.oneWay!=1){cost[a.start]=sa;parent[a.start]=null;}if(a.oneWay!=-1){cost[a.end]=a.length-sa;parent[a.end]=null;}
            while(true)
            {
                string current=null;float best=float.MaxValue;foreach(var pair in cost)if(!closed.Contains(pair.Key)&&pair.Value<best){best=pair.Value;current=pair.Key;}
                if(current==null)break;closed.Add(current);if(!nodes.TryGetValue(current,out var links))continue;
                foreach(var r in links)
                {
                    bool forward=current==r.start;if(forward?r.oneWay==-1:r.oneWay==1)continue;string next=forward?r.end:r.start;float candidate=best+r.length;
                    if(!cost.TryGetValue(next,out float old)||candidate<old){cost[next]=candidate;parent[next]=current;edge[next]=r;}
                }
            }
            string goal=null;float distance=float.MaxValue;
            if(b.oneWay!=-1&&cost.TryGetValue(b.start,out float before)){goal=b.start;distance=before+sb;}
            if(b.oneWay!=1&&cost.TryGetValue(b.end,out float after)&&after+b.length-sb<distance)goal=b.end;
            if(goal==null)return path;
            var route=new List<(GeoRoad road,string source)>();string node=goal;
            while(parent.TryGetValue(node,out var previous)&&previous!=null){route.Add((edge[node],previous));node=previous;}
            Arc(path,a,sa,node==a.start?0:a.length);route.Reverse();
            foreach(var step in route)Arc(path,step.road,step.source==step.road.start?0:step.road.length,step.source==step.road.start?step.road.length:0);
            Arc(path,b,goal==b.start?0:b.length,sb);return path;
        }
        void Arc(List<Vector3> result,GeoRoad road,float from,float to)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(to-from)/8));float direction=to>=from?1:-1;
            for(int i=0;i<=count;i++){var p=region.RoadPoint(road,Mathf.Lerp(from,to,(float)i/count),out var forward);p+=Vector3.Cross(Vector3.up,forward).normalized*Mathf.Min(region.EffectiveWidth(road,Mathf.Lerp(from,to,(float)i/count))*.23f,2.5f)*region.drivingSide*direction;if(result.Count==0||Vector3.Distance(p,result[result.Count-1])>.3f)result.Add(p);}
        }
    }
}
