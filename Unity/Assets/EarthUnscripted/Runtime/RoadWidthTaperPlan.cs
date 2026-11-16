using System;using System.Collections.Generic;using System.IO;using System.Linq;
namespace EarthUnscripted
{
    // Proposed shared carriageway field. It may be admitted only when asphalt,
    // verge, terrain/collision extensions and scenery clearance consume it too.
    // Source road.width is immutable. Outward proposals preserve it as a minimum;
    // explicitly admitted inward fallbacks may narrow wider ground approaches.
    public sealed class RoadWidthTaperPlan
    {
        public sealed class Station{public double Distance,Width,Derivative;}
        sealed class Influence{public double At,Low,High,Run;public int Direction;}
        public sealed class Chain
        {
            public string Id;public bool Closed,HasBridge,Inward;public float InwardInsetMetres;public double Length;public Station[] Stations;public RoadMarkingPlan.ChainPart[] Parts;
            static double Smooth(double t)=>t*t*(3-2*t);
            public double CanonicalWidth(double distance)
            {
                int lo=0,hi=Parts.Length-1;while(lo<hi){int m=(lo+hi+1)/2;if(Parts[m].Start<=distance)lo=m;else hi=m-1;}
                double Choose(double a,double b)=>Inward?Math.Min(a,b):Math.Max(a,b);double best=Parts[lo].Road.width;if(lo>0&&Math.Abs(distance-Parts[lo].Start)<.000001)best=Choose(best,Parts[lo-1].Road.width);
                if(lo+1<Parts.Length&&Math.Abs(distance-Parts[lo+1].Start)<.000001)best=Choose(best,Parts[lo+1].Road.width);
                if(Closed&&(distance<.000001||distance>Length-.000001))best=Choose(best,Choose(Parts[0].Road.width,Parts.Last().Road.width));return best;
            }
            public double Sample(double distance)
            {
                distance=Math.Max(0,Math.Min(Length,distance));int lo=0,hi=Stations.Length-1;
                while(hi-lo>1){int m=(lo+hi)/2;if(Stations[m].Distance<=distance)lo=m;else hi=m;}
                var a=Stations[lo];var b=Stations[hi];double h=b.Distance-a.Distance,t=(distance-a.Distance)/h,t2=t*t,t3=t2*t;
                double width=(2*t3-3*t2+1)*a.Width+(t3-2*t2+t)*h*a.Derivative+(-2*t3+3*t2)*b.Width+(t3-t2)*h*b.Derivative;
                return Inward?width:Math.Max(CanonicalWidth(distance),width);
            }
            public double Derivative(double distance)
            {
                distance=Math.Max(0,Math.Min(Length,distance));int lo=0,hi=Stations.Length-1;while(hi-lo>1){int m=(lo+hi)/2;if(Stations[m].Distance<=distance)lo=m;else hi=m;}
                var a=Stations[lo];var b=Stations[hi];double h=b.Distance-a.Distance,t=(distance-a.Distance)/h;
                return(6*t*t-6*t)/h*a.Width+(3*t*t-4*t+1)*a.Derivative+(-6*t*t+6*t)/h*b.Width+(3*t*t-2*t)*b.Derivative;
            }
            internal static Chain Make(RoadMarkingPlan.ChainDescription input,bool inward=false,float collarInset=0)
            {
                var result=new Chain{Id=input.Id,Closed=input.Closed,Length=input.Length,Parts=input.Parts,Inward=inward,InwardInsetMetres=inward?collarInset:0,HasBridge=input.Parts.Any(p=>p.Road.bridge)};
                var controls=new SortedSet<double>{0,input.Length};var changes=new List<Influence>();
                void Transition(double at,double left,double right)
                {
                    if(Math.Abs(left-right)<.001)return;double run=Math.Max(12,Math.Min(40,Math.Abs(left-right)*4));if(input.Closed)run=Math.Min(run,input.Length*.45);
                    var change=new Influence{At=at,Low=Math.Min(left,right),High=Math.Max(left,right),Run=run,Direction=(right<left?1:-1)*(inward?-1:1)};changes.Add(change);controls.Add(at);
                    double end=at+change.Direction*run;if(input.Closed){end%=input.Length;if(end<0)end+=input.Length;}else end=Math.Max(0,Math.Min(input.Length,end));controls.Add(end);
                }
                for(int i=1;i<input.Parts.Length;i++)Transition(input.Parts[i].Start,input.Parts[i-1].Road.width,input.Parts[i].Road.width);
                if(input.Closed)Transition(0,input.Parts.Last().Road.width,input.Parts[0].Road.width);
                if(controls.Count>30002)throw new InvalidDataException("Width taper station budget");
                if(inward)foreach(var p in input.Parts)controls.Add(p.Start);var stations=new List<Station>();
                foreach(double x in controls)
                {
                    double required=result.CanonicalWidth(x);
                    foreach(var c in changes)
                    {
                        double d=(x-c.At)*c.Direction;if(input.Closed){d%=input.Length;if(d<0)d+=input.Length;}
                        if(d>=0&&d<=c.Run){double value=c.Low+(c.High-c.Low)*Smooth(inward?d/c.Run:1-d/c.Run);required=inward?Math.Min(required,value):Math.Max(required,value);}
                    }
                    if(inward&&collarInset>0){double floor=0;foreach(var part in input.Parts)if(x>=part.Start-.000001&&x<=part.Start+part.Length+.000001)floor=Math.Max(floor,Math.Max(2,Math.Min(part.Road.width,(part.Road.kind=="service"?6.5:8.5)-collarInset)));required=Math.Max(floor,required-collarInset);}
                    if(stations.Count>0&&x-stations.Last().Distance<.000001){stations.Last().Width=Math.Max(stations.Last().Width,required);if(x==input.Length)stations.Last().Distance=x;continue;}
                    stations.Add(new Station{Distance=x,Width=required});
                }
                if(stations.Count<2)throw new InvalidDataException("Invalid width chain length");
                var h=new double[stations.Count-1];var dSlope=new double[h.Length];for(int i=0;i<h.Length;i++){h[i]=stations[i+1].Distance-stations[i].Distance;dSlope[i]=(stations[i+1].Width-stations[i].Width)/h[i];}
                double Slope(double previous,double next,double hp,double hn)
                {if(previous*next<=0)return 0;double w1=2*hn+hp,w2=hn+2*hp;return(w1+w2)/(w1/previous+w2/next);}
                for(int i=1;i<stations.Count-1;i++)stations[i].Derivative=Slope(dSlope[i-1],dSlope[i],h[i-1],h[i]);
                if(input.Closed)
                {double seam=inward?Math.Min(stations[0].Width,stations.Last().Width):Math.Max(stations[0].Width,stations.Last().Width);stations[0].Width=stations.Last().Width=seam;double m=Slope(dSlope.Last(),dSlope[0],h.Last(),h[0]);stations[0].Derivative=stations.Last().Derivative=m;}
                result.Stations=stations.ToArray();return result;
            }
        }
        public sealed class RoadProfile
        {
            public GeoRoad Road;public Chain Chain;public double Start,Length;public bool Forward;
            public double WidthAt(double sourceDistance)=>Chain.Sample(Start+(Forward?Math.Max(0,Math.Min(Length,sourceDistance)):Length-Math.Max(0,Math.Min(Length,sourceDistance))));
            public RoadWidthProfileDto ToDto()
            {
                double epsilon=Math.Max(.0001,Length*.0000002);var knots=new SortedSet<double>{0,Length};
                foreach(var station in Chain.Stations){double local=station.Distance-Start;if(local>epsilon&&local<Length-epsilon)knots.Add(Forward?local:Length-local);}
                var distances=new List<float>();foreach(double distance in knots){float x=(float)distance;if(distances.Count>0&&x-distances.Last()<epsilon){if(distance==Length){if(distances.Count>1)distances.RemoveAt(distances.Count-1);distances.Add(x);}continue;}distances.Add(x);}
                var localDistances=distances.Select(x=>Math.Max(0,Math.Min(Length,x))).ToArray();return new RoadWidthProfileDto{roadId=Road.id,bevelBoundary=Chain.Inward,distances=distances.ToArray(),widths=localDistances.Select(x=>(float)WidthAt(x)).ToArray(),derivatives=localDistances.Select(x=>(float)(Chain.Derivative(Start+(Forward?x:Length-x))*(Forward?1:-1))).ToArray()};
            }
            public bool RequiresExpandedFootprint=>Math.Max(Chain.Sample(Start),Chain.Sample(Start+Length))>Road.width+.001||Chain.Stations.Any(s=>s.Distance>=Start&&s.Distance<=Start+Length&&s.Width>Road.width+.001);
        }
        public readonly List<Chain> Chains=new List<Chain>();public readonly Dictionary<string,RoadProfile> Roads=new Dictionary<string,RoadProfile>(StringComparer.Ordinal);
        public static RoadWidthTaperPlan Build(RoadMarkingPlan continuation,bool inward=false,float collarInset=0)
        {
            if(float.IsNaN(collarInset)||float.IsInfinity(collarInset)||collarInset<0||collarInset>.02f||!inward&&collarInset!=0)throw new InvalidDataException("Inward collar inset must be0..20mm");
            var result=new RoadWidthTaperPlan();int stations=0;
            foreach(var c in continuation.Continuations)
            {
                var chain=Chain.Make(c,inward,collarInset);stations+=chain.Stations.Length;if(stations>100000)throw new InvalidDataException("Global width taper station budget");result.Chains.Add(chain);
                foreach(var part in c.Parts)result.Roads.Add(part.Road.id,new RoadProfile{Road=part.Road,Chain=chain,Start=part.Start,Length=part.Length,Forward=part.Forward});
            }
            return result;
        }
    }
}
