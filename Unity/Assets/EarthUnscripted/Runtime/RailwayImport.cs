using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public static class RailwayImport
    {
        public static void Append(List<GeoRailway> result,GeoRegion region,JObject tags,string sourceId,JArray nodeIds,List<Vector3> source,float half,ref int total)
        {
            if(!RailwayTags.IsTrack(tags))return;if(nodeIds==null||nodeIds.Count!=source.Count)throw new InvalidDataException("Railway source node alignment missing");
            string kind=RailwayTags.Lifecycle(tags);if(!RailwayMetrics.Supported(kind))return;
            int tracks=int.TryParse((string)tags["tracks"],out int count)?Mathf.Clamp(count,1,16):1;float gauge=Gauge((string)tags["gauge"]);
            float width=Mathf.Clamp(Mathf.Max(RailwayTags.Metres((string)tags["width"],RailwayMetrics.DefaultWidth),gauge+1.2f+(tracks-1)*RailwayMetrics.TrackSpacing),.5f,100);
            var staged=new List<GeoRailway>();var points=new List<Vector3>();var ids=new List<string>();int part=0,budget=total;
            void Flush()
            {
                if(points.Count>=2)
                {
                    if(result.Count+staged.Count>=RailwayMetrics.MaximumWays||points.Count>RailwayMetrics.MaximumPointsPerWay)throw new InvalidDataException("Railway detail exceeds the area budget; choose a smaller area");
                    bool bridge=Flag(tags["bridge"]),tunnel=Flag(tags["tunnel"]);if(bridge&&tunnel)throw new InvalidDataException("Ambiguous railway bridge/tunnel tags");
                    staged.Add(new GeoRailway{id="railway-"+sourceId+"-"+part++,start=ids[0],end=ids[ids.Count-1],name=Text(tags,"name",240),operatorName=Text(tags,"operator",240),electrified=Text(tags,"electrified",64),kind=kind,width=width,gauge=gauge,tracks=tracks,layer=RailwayTags.Layer(tags),bridge=bridge,tunnel=tunnel,points=points.ToArray(),nodeIds=ids.ToArray(),estimatedProfile=true});
                }
                points.Clear();ids.Clear();
            }
            void Add(Vector3 p,string id)
            {
                if(points.Count>0&&ids[ids.Count-1]==id)return;
                if(++budget>RailwayMetrics.MaximumTotalPoints)throw new InvalidDataException("Railway point budget exceeded; choose a smaller area");
                p.y=region.RawHeight(p.x,p.z)+RailwayMetrics.TrackBuildUp;points.Add(p);ids.Add(id);
                if(points.Count>RailwayMetrics.MaximumPointsPerWay)throw new InvalidDataException("Railway fragment exceeds the area budget; choose a smaller area");
            }
            for(int i=0;i<source.Count-1;i++)
            {
                var a=source[i];var b=source[i+1];if(!Finite(a)||!Finite(b)||!Clip(a,b,half,out float from,out float to)){Flush();continue;}
                var p=Vector3.Lerp(a,b,from);var q=Vector3.Lerp(a,b,to);float length=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(q.x,q.z));if(length<.01f)continue;
                string first=from<=.000001f?nodeIds[i].ToString():"railclip-"+sourceId+"-"+i+"-a";
                string last=to>=.999999f?nodeIds[i+1].ToString():"railclip-"+sourceId+"-"+i+"-b";
                if(ids.Count>0&&ids[ids.Count-1]!=first)Flush();Add(p,first);int steps=Mathf.Max(1,Mathf.CeilToInt(length/2.5f));
                for(int j=1;j<=steps;j++)Add(Vector3.Lerp(p,q,j/(float)steps),j==steps?last:"railv-"+sourceId+"-"+i+"-"+j);
                if(to<.999999f)Flush();
            }
            Flush();result.AddRange(staged);total=budget;
        }
        static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z);
        static bool Flag(JToken token)=>token?.Type==JTokenType.String&&!string.IsNullOrEmpty((string)token)&&(string)token!="no";
        static string Text(JObject tags,string key,int limit){string s=tags[key]?.Type==JTokenType.String?(string)tags[key]:null;if(s!=null&&s.Length>limit)s=s.Substring(0,limit);return s;}
        static float Gauge(string source)
        {
            if(string.IsNullOrEmpty(source)||source.Length>80)return RailwayMetrics.DefaultGauge;source=source.Split(';')[0];
            if(source.Contains("'"))return Mathf.Clamp(RailwayTags.Metres(source,RailwayMetrics.DefaultGauge),.3f,3);
            var match=Regex.Match(source,@"[0-9]+(?:[.][0-9]+)?");if(!match.Success||!float.TryParse(match.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float v))return RailwayMetrics.DefaultGauge;
            if(v>=100)v/=1000;return v>=.3f&&v<=3?v:RailwayMetrics.DefaultGauge;
        }
        static bool Clip(Vector3 a,Vector3 b,float half,out float start,out float end)
        {
            float lo=0,hi=1;bool Edge(float p,float q){if(Mathf.Abs(p)<.000001f)return q>=0;float t=q/p;if(p<0){if(t>hi)return false;lo=Mathf.Max(lo,t);}else{if(t<lo)return false;hi=Mathf.Min(hi,t);}return true;}
            bool ok=Edge(-(b.x-a.x),a.x+half)&&Edge(b.x-a.x,half-a.x)&&Edge(-(b.z-a.z),a.z+half)&&Edge(b.z-a.z,half-a.z)&&lo<=hi;start=lo;end=hi;return ok;
        }
    }
}
