using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using NativeWorld=RoadRush.NewbridgePrototype.NewbridgeWorld;
namespace RoadRush
{
    // A public geographic report is admitted only onto an actual loaded source
    // section, with the correct gauge. No straight rail links are synthesised.
    public sealed class PublicTrainData
    {
        sealed class Previous{public Vector3 Point;public double Time;public float Speed;public int Direction;}
        readonly Dictionary<string,Previous> previous=new Dictionary<string,Previous>();
        static double N(JToken token,double fallback=double.NaN){double n=(double?)token??fallback;return double.IsFinite(n)?n:fallback;}
        static Vector3 World(JToken p,double lon,double lat)
        {
            if(!(p is JArray a)||a.Count!=2)throw new FormatException("Invalid public train position");
            double x=N(a[0]),z=N(a[1]);if(!double.IsFinite(x)||!double.IsFinite(z)||Math.Abs(x)>180||Math.Abs(z)>90)throw new FormatException("Invalid public train point");
            return new Vector3((float)((x-lon)*111319.4908*Math.Cos(lat*Math.PI/180)),0,(float)((z-lat)*111319.4908));
        }
        internal static bool Project(Vector3 p,Vector3[] points,out double station,out float gap,out Vector3 tangent)
        {
            station=0;gap=float.PositiveInfinity;tangent=Vector3.forward;double at=0;
            for(int i=1;i<points.Length;i++)
            {
                var d=points[i]-points[i-1];float length=d.magnitude;var flat=new Vector3(d.x,0,d.z);float f=Mathf.Clamp01(Vector3.Dot(p-new Vector3(points[i-1].x,0,points[i-1].z),flat)/Mathf.Max(.00001f,flat.sqrMagnitude));
                var q=points[i-1]+d*f;float distance=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(q.x,q.z));
                if(distance<gap){gap=distance;station=at+length*f;tangent=flat.normalized;}at+=length;
            }
            return float.IsFinite(gap)&&gap<=12;
        }
        public TrainServiceSnapshot Convert(string json,double now,double longitude,double latitude,string country,IEnumerable<NativeWorld.Feature> loaded,bool timetableFallback=true)
        {
            if(json==null||json.Length>2*1024*1024)throw new FormatException("Public rail size limit");
            var root=JObject.Parse(json,new Newtonsoft.Json.Linq.JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if((int?)root["schema"]!=1||N(root["receivedUtc"])>now+30||now-N(root["receivedUtc"])>86400)throw new FormatException("Public rail schema/time");
            string provider=(string)root["provider"]??"",attribution=(string)root["attribution"]??"";
            if(provider.Length==0||attribution.Length==0)return null;
            var features=loaded.Where(f=>f!=null&&f.category=="rail"&&f.kind=="rail"&&!f.tunnel&&f.points?.Length>=2&&!string.IsNullOrEmpty(f.sourceId)).GroupBy(f=>f.sourceId).Select(g=>g.OrderByDescending(f=>f.points.Length).First()).Take(4096).ToArray();
            var rules=WorldProviderRegistry.Rules(country)["railGaugesMm"] as JArray;float defaultGauge=rules?.Count==1?(float)rules[0]:0;
            float Gauge(NativeWorld.Feature f)=>string.IsNullOrWhiteSpace(f.gauge)?defaultGauge:float.TryParse(f.gauge,NumberStyles.Float,CultureInfo.InvariantCulture,out float gauge)?gauge:0;
            var plans=new List<TrainServicePlan>();var observations=root["observations"]?["records"] as JArray;
            if(observations?.Count>100000)throw new FormatException("Public rail record limit");
            foreach(var r in (observations??new JArray()).OfType<JObject>())
            {
                double observed=N(r["observedUtc"]);if(!double.IsFinite(observed)||observed>now+2||now-observed>120||r["position"]==null||(bool?)r["cancelled"]==true)continue;
                string trip=(string)r["tripId"];string id=provider+":"+(!string.IsNullOrEmpty(trip)?trip:(string)r["id"]);float gauge=(float)N(r["gaugeMm"],defaultGauge);var stock=TrainStock.Generic(gauge);if(stock==null)continue;
                var point=World(r["position"],longitude,latitude);var choices=new List<(NativeWorld.Feature f,double at,float gap,Vector3 tangent)>();
                foreach(var f in features)if(Mathf.Abs(Gauge(f)-gauge)<.1f&&Project(point,f.points,out double at,out float gap,out Vector3 tangent))choices.Add((f,at,gap,tangent));
                var ordered=choices.OrderBy(c=>c.gap).ToArray();if(ordered.Length==0||ordered.Length>1&&ordered[1].gap-ordered[0].gap<2)continue; // ambiguous parallel/stacked tracks
                var match=ordered[0];double length=0;bool grade=true;
                for(int i=1;i<match.f.points.Length;i++){var delta=match.f.points[i]-match.f.points[i-1];float flat=new Vector2(delta.x,delta.z).magnitude;if(flat<.001f||Mathf.Abs(delta.y)/flat>.08f){grade=false;break;}length+=delta.magnitude;}
                if(!grade||length<stock.Length+30||match.at<stock.Length+4||match.at>length-stock.Length-4)continue;
                float speed=(float)N(r["speedMps"],0);int direction=1;
                double bearing=N(r["bearingDegrees"]);if(double.IsFinite(bearing))direction=Vector3.Dot(new Vector3((float)Math.Sin(bearing*Math.PI/180),0,(float)Math.Cos(bearing*Math.PI/180)),match.tangent)>=0?1:-1;
                if(previous.TryGetValue(id,out var prev)&&observed-prev.Time<=120)
                {
                    var movement=point-prev.Point;
                    if(observed>prev.Time&&movement.magnitude>2){if(!double.IsFinite(bearing))direction=Vector3.Dot(movement,match.tangent)>=0?1:-1;if(r["speedMps"]==null)speed=movement.magnitude/(float)(observed-prev.Time);}
                    else if(observed==prev.Time){speed=prev.Speed;if(!double.IsFinite(bearing))direction=prev.Direction;}
                }
                previous[id]=new Previous{Point=point,Time=observed,Speed=speed,Direction=direction};
                if(speed<0||speed>stock.MaximumSpeed*1.05f)continue;
                double back=Math.Min(45,Math.Min(match.at-2,length-match.at-2)/Math.Max(.01,speed));
                bool approximate=(string)r["positionQuality"]=="estimated"||(string)r["timestampQuality"]=="receipt-only";
                var p=new TrainServicePlan{Id=id,StockId=stock.Id,Provider=provider,Attribution=attribution,PathId="public:"+match.f.sourceId+":"+match.f.points[0].ToString("F2")+":"+match.f.points.Length,SourceWays=new[]{match.f.sourceId},StockConfirmed=false,Direction=direction,Start=observed-60,End=observed+120,AutomaticReported=!approximate,
                    Calls=new[]{new TrainServiceCall{Metres=match.at-direction*speed*back,Arrival=observed-back,Departure=observed-back,Stop=false},new TrainServiceCall{Metres=match.at+direction*speed*back,Arrival=observed+Math.Max(1,back),Departure=observed+Math.Max(1,back),Stop=false}},
                    Reports=approximate?Array.Empty<TrainServiceReport>():new[]{new TrainServiceReport{Metres=match.at,Observed=observed,Received=Math.Max(observed,N(root["receivedUtc"],now)),Speed=speed}}};
                p.Start=p.Calls[0].Arrival;p.End=p.Calls[1].Departure;plans.Add(p);
            }
            if(previous.Count>1024)previous.Clear();
            // Timetable fallback is admitted only when two real timing stops map
            // onto the same loaded section. Geometry gaps stay unsupported.
            var trips=timetableFallback?root["schedule"]?["records"] as JArray:null;
            foreach(var trip in (trips??new JArray()).OfType<JObject>().Take(64))
            {
                if((bool?)trip["cancelled"]==true||plans.Any(p=>p.Id==provider+":"+(string)trip["tripId"]))continue;
                var stock=TrainStock.Generic(defaultGauge);if(stock==null)continue;
                foreach(var f in features.Where(f=>Mathf.Abs(Gauge(f)-defaultGauge)<.1f))
                {
                    var calls=new List<TrainServiceCall>();
                    foreach(var stop in (trip["stops"] as JArray??new JArray()).OfType<JObject>())
                    {
                        if(stop["arrivalUtc"]==null||!Project(World(stop["position"],longitude,latitude),f.points,out double at,out float gap,out var tangent)||gap>12)continue;
                        calls.Add(new TrainServiceCall{Location=(string)stop["name"],Metres=at,Arrival=N(stop["arrivalUtc"]),Departure=N(stop["departureUtc"]),Stop=true});
                    }
                    if(calls.Count<2||calls[0].Arrival>now||calls[calls.Count-1].Departure<now)continue;
                    int direction=calls[calls.Count-1].Metres>calls[0].Metres?1:-1;bool valid=true;
                    for(int i=1;i<calls.Count;i++){double dt=calls[i].Arrival-calls[i-1].Departure,ds=(calls[i].Metres-calls[i-1].Metres)*direction;if(dt<=0||ds<0||ds/dt>stock.MaximumSpeed)valid=false;}
                    if(!valid)continue;
                    plans.Add(new TrainServicePlan{Id=provider+":"+(string)trip["tripId"],StockId=stock.Id,Provider=provider,Attribution=attribution,PathId="public:"+f.sourceId,SourceWays=new[]{f.sourceId},Direction=direction,Start=calls[0].Arrival,End=calls[calls.Count-1].Departure,Calls=calls.ToArray(),Reports=Array.Empty<TrainServiceReport>()});break;
                }
            }
            return new TrainServiceSnapshot{Provider=provider,Attribution=attribution,Generated=now,Services=plans.Take(64).ToArray()};
        }
    }
}
