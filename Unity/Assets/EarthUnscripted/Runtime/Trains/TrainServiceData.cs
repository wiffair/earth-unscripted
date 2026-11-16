using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.IO;
namespace EarthUnscripted
{
    public enum TrainPositionQuality { Unavailable, Timetable, Interpolated, Reported }
    public sealed class TrainServiceCall
    {public string Location;public double Metres,Arrival,Departure;public bool Stop;}
    public sealed class TrainServiceReport
    {public double Metres,Observed,Received;public float Speed;}
    public readonly struct TrainServicePosition
    {
        public readonly bool Available,Dwelling;public readonly double Metres,NextStop,ReportAge;public readonly float Speed;public readonly TrainPositionQuality Quality;
        public TrainServicePosition(bool available,double metres,float speed,bool dwell,double next,TrainPositionQuality quality,double age=0)
        {Available=available;Metres=metres;Speed=speed;Dwelling=dwell;NextStop=next;Quality=quality;ReportAge=age;}
    }
    public sealed class TrainServicePlan
    {
        public string Id,StockId,Provider,Attribution,PathId;public string[] SourceWays;
        public PublishedTrainPath PublishedPath;public bool Cancelled,StockConfirmed,AutomaticReported;public int Direction=1;public double Start,End;
        public TrainServiceCall[] Calls;public TrainServiceReport[] Reports;
        public TrainServicePosition Sample(double utc,bool live,float maximumSpeed)
        {
            if(Cancelled||utc<Start||utc>End)return default;
            var timetable=Timetable(utc,maximumSpeed);
            if(!(live||AutomaticReported)||Reports.Length==0)return timetable;
            // A received observation is never used before its receipt time. Signal
            // updates are not continuous GPS: bounded between-report motion is labelled.
            var reports=Reports.Where(r=>r.Received<=utc&&r.Observed<=utc+2).ToArray();
            if(reports.Length==0)return timetable;var last=reports[reports.Length-1];double age=utc-last.Observed;
            if(age>120)return timetable;
            double time=utc-5;
            for(int i=1;i<reports.Length;i++)if(time>=reports[i-1].Observed&&time<=reports[i].Observed)
            {
                var a=reports[i-1];var b=reports[i];double span=b.Observed-a.Observed;
                float speed=(float)Math.Abs((b.Metres-a.Metres)/span);
                return new TrainServicePosition(true,a.Metres+(b.Metres-a.Metres)*(time-a.Observed)/span,speed,false,timetable.NextStop,TrainPositionQuality.Interpolated,age);
            }
            float v=Math.Min(maximumSpeed,Math.Max(0,last.Speed));double metres=last.Metres+Direction*v*Math.Min(15,Math.Max(0,time-last.Observed));
            if(double.IsFinite(timetable.NextStop))metres=Direction>0?Math.Min(metres,timetable.NextStop):Math.Max(metres,timetable.NextStop);
            return new TrainServicePosition(true,metres,v,false,timetable.NextStop,age<2?TrainPositionQuality.Reported:TrainPositionQuality.Interpolated,age);
        }
        TrainServicePosition Timetable(double utc,float maximumSpeed)
        {
            for(int i=0;i<Calls.Length;i++)
            {
                var call=Calls[i];
                if(utc>=call.Arrival&&utc<=call.Departure)
                    return new TrainServicePosition(true,call.Metres,0,true,call.Metres,TrainPositionQuality.Timetable);
                if(i+1>=Calls.Length)continue;var next=Calls[i+1];
                if(utc>call.Departure&&utc<next.Arrival)
                {
                    double duration=next.Arrival-call.Departure,f=(utc-call.Departure)/duration,delta=next.Metres-call.Metres;
                    // Rest-to-rest interpolation for station calls; physical train
                    // motion separately applies stock acceleration/braking limits.
                    double position=call.Stop&&next.Stop?f*f*(3-2*f):f;
                    double slope=call.Stop&&next.Stop?6*f*(1-f):1;
                    double stop=Calls.Skip(i+1).Where(c=>c.Stop).Select(c=>c.Metres).DefaultIfEmpty(double.NaN).First();
                    return new TrainServicePosition(true,call.Metres+delta*position,(float)Math.Min(maximumSpeed,Math.Abs(delta/duration*slope)),false,stop,TrainPositionQuality.Timetable);
                }
            }
            return default;
        }
    }
    public sealed class TrainServiceSnapshot
    {
        public bool Published;public string Provider,Attribution;public double Generated;public TrainServicePlan[] Services;
        public static double Utc(string text)
        {
            if(string.IsNullOrWhiteSpace(text)||text.IndexOf('T')<0||!(text.EndsWith("Z",StringComparison.OrdinalIgnoreCase)||text.Length>=25&&text[text.Length-3]==':'&&(text[text.Length-6]=='+'||text[text.Length-6]=='-'))||!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var instant))throw new FormatException("Train times must have an explicit UTC offset");
            return instant.ToUnixTimeMilliseconds()/1000.0;
        }
        static double Number(JToken value,string name)
        {double v=(double?)value??double.NaN;if(!double.IsFinite(v))throw new FormatException("Invalid train "+name);return v;}
        public static TrainServiceSnapshot Parse(string json,double receivedUtc)
        {
            if(json==null||json.Length>2*1024*1024)throw new FormatException("Rail snapshot exceeds its size limit");
            JObject root;using(var reader=new JsonTextReader(new StringReader(json)){DateParseHandling=DateParseHandling.None,MaxDepth=32})root=JObject.Load(reader);if((int?)root["schema"]!=1)throw new FormatException("Unsupported rail snapshot schema");
            var result=new TrainServiceSnapshot{Provider=(string)root["provider"],Attribution=(string)root["attribution"],Generated=Utc((string)root["generatedUtc"])};
            if(string.IsNullOrWhiteSpace(result.Provider)||string.IsNullOrWhiteSpace(result.Attribution)||result.Generated>receivedUtc+30||receivedUtc-result.Generated>86400)throw new FormatException("Rail snapshot identity/time is unavailable or expired");
            var services=root["services"] as JArray;if(services==null||services.Count>64)throw new FormatException("Rail service count limit");if(services.OfType<JObject>().Count()!=services.Count)throw new FormatException("Invalid train service record");var plans=new List<TrainServicePlan>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in services.OfType<JObject>())
            {
                var p=new TrainServicePlan{Id=(string)item["id"],StockId=(string)item["stockId"],StockConfirmed=(bool?)item["stockConfirmed"]??false,PathId=(string)item["pathId"],Provider=result.Provider,Attribution=result.Attribution,Cancelled=(bool?)item["cancelled"]??false,Direction=(int?)item["direction"]??1};
                if(string.IsNullOrEmpty(p.Id)||!ids.Add(p.Id)||string.IsNullOrEmpty(p.PathId)||p.Direction!=1&&p.Direction!=-1)throw new FormatException("Invalid or duplicate train service identity");
                if(TrainStock.Find(p.StockId)==null)throw new FormatException("Unsupported stock type");
                p.SourceWays=(item["sourceWays"] as JArray)?.Select(v=>(string)v).ToArray();
                if(p.SourceWays==null||p.SourceWays.Length==0||p.SourceWays.Length>128||p.SourceWays.Any(string.IsNullOrWhiteSpace))throw new FormatException("Train requires an explicit prepared source-way chain");
                var calls=item["calls"] as JArray;if(calls==null||calls.Count<2||calls.Count>256)throw new FormatException("Train requires bounded timing points");
                if(calls.OfType<JObject>().Count()!=calls.Count)throw new FormatException("Invalid train timing point");
                p.Calls=calls.OfType<JObject>().Select(c=>new TrainServiceCall{Location=(string)c["location"]??"",Metres=Number(c["metres"],"station"),Arrival=Utc((string)c["arrivalUtc"]),Departure=Utc((string)c["departureUtc"]),Stop=(bool?)c["stop"]??true}).ToArray();
                for(int i=0;i<p.Calls.Length;i++)
                {var c=p.Calls[i];if(c.Metres<0||c.Departure<c.Arrival||c.Departure-c.Arrival>14400)throw new FormatException("Invalid train dwell/position");if(i>0){var prev=p.Calls[i-1];double dt=c.Arrival-prev.Departure,metres=(c.Metres-prev.Metres)*p.Direction;if(dt<=0||metres<0||metres/dt>TrainStock.Find(p.StockId).MaximumSpeed*1.02)throw new FormatException("Invalid train timetable order/speed");}}
                p.Start=p.Calls[0].Arrival;p.End=p.Calls[p.Calls.Length-1].Departure;
                if(p.End-p.Start>48*3600)throw new FormatException("Train service duration limit");
                var reports=item["reports"] as JArray;if(reports!=null&&reports.Count>256)throw new FormatException("Train observation count limit");
                if(reports!=null&&reports.OfType<JObject>().Count()!=reports.Count)throw new FormatException("Invalid train report record");
                p.Reports=(reports??new JArray()).OfType<JObject>().Select(o=>new TrainServiceReport{Metres=Number(o["metres"],"reported position"),Observed=Utc((string)o["observedUtc"]),Received=Utc((string)o["receivedUtc"]),Speed=(float)Number(o["speedMps"],"reported speed")}).ToArray();
                for(int i=0;i<p.Reports.Length;i++)
                {var o=p.Reports[i];if(o.Metres<0||o.Speed<0||o.Speed>TrainStock.Find(p.StockId).MaximumSpeed*1.02||o.Observed>o.Received+2||o.Received>receivedUtc+30)throw new FormatException("Invalid train observation");if(i>0){var a=p.Reports[i-1];double dt=o.Observed-a.Observed;if(dt<=0||o.Received<a.Received||(o.Metres-a.Metres)*p.Direction<-.5||Math.Abs(o.Metres-a.Metres)/dt>TrainStock.Find(p.StockId).MaximumSpeed*1.05)throw new FormatException("Impossible or out-of-order train report");}}
                plans.Add(p);
            }
            result.Services=plans.ToArray();return result;
        }
    }
}
