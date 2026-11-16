using System;using System.Collections.Generic;using System.Linq;
namespace RoadRush
{
    // One immutable admitted field for every road consumer. Host and guest both
    // evaluate the same serialized controls; construction never re-solves slopes.
    public sealed class EffectiveRoadWidthField
    {
        readonly Dictionary<string,RoadWidthProfileDto> profiles=new Dictionary<string,RoadWidthProfileDto>(StringComparer.Ordinal);
        readonly Dictionary<string,float> maximum=new Dictionary<string,float>(StringComparer.Ordinal);
        public EffectiveRoadWidthField(RoadWidthProfileDto[] admitted,GeoRoad[] roads)
        {
            RoadWidthProfileDto.Validate(admitted,roads);foreach(var source in admitted)
            {
                var p=new RoadWidthProfileDto{roadId=source.roadId,bevelBoundary=source.bevelBoundary,distances=(float[])source.distances.Clone(),widths=(float[])source.widths.Clone(),derivatives=(float[])source.derivatives.Clone()};profiles.Add(p.roadId,p);double max=p.widths.Max();
                for(int i=1;i<p.distances.Length;i++){double h=p.distances[i]-p.distances[i-1],w0=p.widths[i-1],w1=p.widths[i],m0=p.derivatives[i-1],m1=p.derivatives[i];double a=2*w0-2*w1+h*(m0+m1),b=-3*w0+3*w1-h*(2*m0+m1),c=h*m0;void Check(double t){if(t>0&&t<1)max=Math.Max(max,((a*t+b)*t+c)*t+w0);}if(Math.Abs(a)<1e-12){if(Math.Abs(b)>1e-12)Check(-c/(2*b));}else{double d=4*b*b-12*a*c;if(d>=0){Check((-2*b-Math.Sqrt(d))/(6*a));Check((-2*b+Math.Sqrt(d))/(6*a));}}}maximum.Add(p.roadId,(float)max);
            }
        }
        public float At(GeoRoad road,double sourceArcDistance)=>road.tunnel?road.width:(float)profiles[road.id].Width(sourceArcDistance);
        public float MaximumBetween(GeoRoad road,double from,double to)
        {
            if(road.tunnel)return road.width;var p=profiles[road.id];from=Math.Max(0,Math.Min(p.distances.Last(),from));to=Math.Max(from,Math.Min(p.distances.Last(),to));double maximum=Math.Max(p.Width(from),p.Width(to));
            for(int i=1;i<p.distances.Length;i++){double x=p.distances[i-1],h=p.distances[i]-x;if(x>to||x+h<from)continue;maximum=Math.Max(maximum,Math.Max(p.Width(Math.Max(from,x)),p.Width(Math.Min(to,x+h))));double w0=p.widths[i-1],w1=p.widths[i],m0=p.derivatives[i-1],m1=p.derivatives[i];double a=2*w0-2*w1+h*(m0+m1),b=-3*w0+3*w1-h*(2*m0+m1),c=h*m0;void Check(double t){double distance=x+t*h;if(t>0&&t<1&&distance>=from&&distance<=to)maximum=Math.Max(maximum,((a*t+b)*t+c)*t+w0);}if(Math.Abs(a)<1e-12){if(Math.Abs(b)>1e-12)Check(-c/(2*b));}else{double d=4*b*b-12*a*c;if(d>=0){Check((-2*b-Math.Sqrt(d))/(6*a));Check((-2*b+Math.Sqrt(d))/(6*a));}}}return(float)maximum;
        }
        public bool UseBevelBoundary(GeoRoad road)=>!road.tunnel&&profiles[road.id].bevelBoundary;
        public float Maximum(GeoRoad road)=>road.tunnel?road.width:maximum[road.id];
        public float EdgeDistance(GeoRoad road,double sourceArcDistance,float centrelineDistance)=>centrelineDistance-At(road,sourceArcDistance)*.5f;
        public RoadWidthProfileDto[] CopySnapshot()=>profiles.Values.OrderBy(p=>p.roadId,StringComparer.Ordinal).Select(p=>new RoadWidthProfileDto{roadId=p.roadId,bevelBoundary=p.bevelBoundary,distances=(float[])p.distances.Clone(),widths=(float[])p.widths.Clone(),derivatives=(float[])p.derivatives.Clone()}).ToArray();
    }
}
