using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Published weekly clock times: no disruption/actual-position claim. The
    // bundled South Wales subset has explicit validity and source provenance.
    public sealed class PublishedTrainPath
    {
        public string Id;public Vector2[] Points;public double[] Metres;public HashSet<string> Ways;
        Dictionary<long,List<int>> buckets;
        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        public Vector2 PointAt(double metres)
        {int i=Array.BinarySearch(Metres,metres);if(i>=0)return Points[i];i=Math.Max(1,Math.Min(Points.Length-1,~i));return Vector2.Lerp(Points[i-1],Points[i],(float)((metres-Metres[i-1])/(Metres[i]-Metres[i-1])));}
        void Index()
        {
            if(buckets!=null)return;buckets=new Dictionary<long,List<int>>();
            for(int i=1;i<Points.Length;i++)
                for(int x=Mathf.FloorToInt(Mathf.Min(Points[i-1].x,Points[i].x)/250);x<=Mathf.FloorToInt(Mathf.Max(Points[i-1].x,Points[i].x)/250);x++)
                    for(int z=Mathf.FloorToInt(Mathf.Min(Points[i-1].y,Points[i].y)/250);z<=Mathf.FloorToInt(Mathf.Max(Points[i-1].y,Points[i].y)/250);z++)
                    {long key=Key(x,z);if(!buckets.TryGetValue(key,out var list))buckets.Add(key,list=new List<int>());list.Add(i);}
        }
        public Vector2 DatasetPoint(GeoRegion region,Vector3 point)
        {
            const double scale=111319.4908;double cosine=Math.Cos(51.5*Math.PI/180);
            return new Vector2((float)((region.longitude+3)*scale*cosine+point.x*cosine/Math.Cos(region.latitude*Math.PI/180)),(float)((region.latitude-51.5)*scale+point.z));
        }
        public double Project(Vector2 point,out float separation,float radius=20)
        {
            Index();separation=float.PositiveInfinity;double result=0;int range=Mathf.CeilToInt(radius/250),cx=Mathf.FloorToInt(point.x/250),cz=Mathf.FloorToInt(point.y/250);
            for(int x=cx-range;x<=cx+range;x++)for(int z=cz-range;z<=cz+range;z++)
            {
                if(!buckets.TryGetValue(Key(x,z),out var indices))continue;foreach(int i in indices){
                var delta=Points[i]-Points[i-1];float length=delta.sqrMagnitude;if(length<.001f)continue;
                float f=Mathf.Clamp01(Vector2.Dot(point-Points[i-1],delta)/length),distance=(point-Points[i-1]-delta*f).sqrMagnitude;
                if(distance<separation){separation=distance;result=Metres[i-1]+(Metres[i]-Metres[i-1])*f;}
                }
            }
            separation=Mathf.Sqrt(separation);return result;
        }
    }
    public static class PublishedTrainTimetable
    {
        static JObject data;static Dictionary<string,PublishedTrainPath> paths;static bool attempted;
        public static string Coverage=>"Published South Wales subset · Ebbw Vale / Newbridge, Cardiff–Newport · 14 Sep–12 Dec 2026";
        public static int TripCount{get{Load();return (data?["trips"] as JArray)?.Count??0;}}
        public static IReadOnlyDictionary<string,PublishedTrainPath> Paths{get{Load();return paths;}}
        static void Load()
        {
            if(attempted)return;attempted=true;paths=new Dictionary<string,PublishedTrainPath>();
            var asset=Resources.Load<TextAsset>("PublishedTrainsGB");if(!asset)return;
            data=JObject.Parse(asset.text);if((int?)data["schema"]!=1){data=null;return;}
            foreach(var p in data["routes"].OfType<JObject>())
            {
                var rows=p["points"].OfType<JArray>().ToArray();var path=new PublishedTrainPath{Id=(string)p["id"],
                    Points=rows.Select(r=>new Vector2((float)r[0],(float)r[1])).ToArray(),Metres=rows.Select(r=>(double)r[2]).ToArray(),
                    Ways=new HashSet<string>(p["ways"].Select(v=>(string)v),StringComparer.Ordinal)};
                paths.Add(path.Id,path);
            }
        }
        static readonly TimeZoneInfo BritishZone=Zone();
        static TimeZoneInfo Zone()
        {
            foreach(string name in new[]{"Europe/London","GMT Standard Time"})try{return TimeZoneInfo.FindSystemTimeZoneById(name);}catch(TimeZoneNotFoundException){}catch(InvalidTimeZoneException){}
            return null;
        }
        public static bool TryUtc(DateTime day,int minutes,out double utc)
        {
            utc=0;if(BritishZone==null)return false;
            var clock=DateTime.SpecifyKind(day.Date.AddMinutes(minutes),DateTimeKind.Unspecified);
            if(BritishZone.IsInvalidTime(clock)||BritishZone.IsAmbiguousTime(clock))return false;
            utc=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(clock,BritishZone)).ToUnixTimeSeconds();return true;
        }
        public static TrainServiceSnapshot Create(double now,out string status)
        {
            Load();status="Published timetable unavailable";if(data==null||BritishZone==null)return null;
            DateTime day=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds((long)now),BritishZone).Date;
            DateTime first=DateTime.ParseExact((string)data["validFrom"],"yyyy-MM-dd",CultureInfo.InvariantCulture),last=DateTime.ParseExact((string)data["validUntil"],"yyyy-MM-dd",CultureInfo.InvariantCulture);
            if(day<first||day>last.AddDays(1)){status="Published timetable expired or not yet valid · choose Local Simulation";return null;}
            var result=new List<TrainServicePlan>();string attribution=(string)data["attribution"];
            foreach(DateTime operatingDay in new[]{day.AddDays(-1),day})
            {
                string date=operatingDay.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                if(operatingDay<first||operatingDay>last||data["excludedDates"].Any(v=>(string)v==date))continue;
                int weekday=((int)operatingDay.DayOfWeek+6)%7;
                foreach(var trip in data["trips"].OfType<JObject>())
                {
                    if(!trip["days"].Any(v=>(int)v==weekday))continue;
                    var rows=trip["calls"].OfType<JArray>().ToArray();var calls=new List<TrainServiceCall>();bool valid=true;
                    var path=paths[(string)trip["route"]];var stations=(JObject)data["routes"].First(r=>(string)r["id"]==path.Id)["stations"];
                    for(int i=0;i<rows.Length;i++)
                    {
                        string station=(string)rows[i][0];if(!TryUtc(operatingDay,(int)rows[i][1],out double instant)){valid=false;break;}
                        // Booklets give departures at intermediate calls; the
                        // 20-second arrival dwell is a visual estimate only.
                        calls.Add(new TrainServiceCall{Location=station,Metres=(double)stations[station],Arrival=instant-(i>0&&i+1<rows.Length?20:0),Departure=instant,Stop=true});
                    }
                    if(!valid||calls.Count<2||calls[0].Arrival>now+120||calls[calls.Count-1].Departure<now)continue;
                    bool increasing=calls[calls.Count-1].Metres>calls[0].Metres;
                    result.Add(new TrainServicePlan{Id="published-"+(string)trip["id"]+"-"+date,StockId=(string)trip["stock"],StockConfirmed=false,
                        PathId="published-"+path.Id,PublishedPath=path,Provider="Published TfW / GWR timetable",Attribution=attribution,SourceWays=path.Ways.ToArray(),Direction=increasing?1:-1,
                        Calls=calls.ToArray(),Reports=Array.Empty<TrainServiceReport>(),Start=calls[0].Arrival,End=calls[calls.Count-1].Departure});
                }
            }
            status="Published timetable estimates · no live delays or cancellations · valid to 12 Dec 2026";
            return new TrainServiceSnapshot{Provider="Published TfW / GWR timetable",Attribution=attribution,Generated=now,Published=true,Services=result.OrderBy(p=>p.Start).Take(64).ToArray()};
        }
    }
}
