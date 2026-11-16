using System;using System.Collections.Generic;using System.IO;using System.Linq;
namespace RoadRush
{
    // Geometry12 host-finalized, validated serialized width controls.
    // Guest evaluates published Hermite data; it never solves new PCHIP slopes.
    [Serializable] public sealed class RoadWidthProfileDto
    {
        public string roadId;public bool bevelBoundary;public float[] distances,widths,derivatives;
        public double Width(double distance)
        {
            distance=Math.Max(0,Math.Min(distances.Last(),distance));int lo=0,hi=distances.Length-1;while(hi-lo>1){int m=(lo+hi)/2;if(distances[m]<=distance)lo=m;else hi=m;}
            double h=distances[hi]-distances[lo],t=(distance-distances[lo])/h,t2=t*t,t3=t2*t;
            return(2*t3-3*t2+1)*widths[lo]+(t3-2*t2+t)*h*derivatives[lo]+(-2*t3+3*t2)*widths[hi]+(t3-t2)*h*derivatives[hi];
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        public static void Validate(IReadOnlyList<RoadWidthProfileDto> profiles,IReadOnlyList<GeoRoad> roads)
        {
            if(profiles==null||profiles.Count>10000)throw new InvalidDataException("Width DTO count budget");if(roads==null||roads.Count>10000)throw new InvalidDataException("Missing or excessive width source roads");var byId=new Dictionary<string,GeoRoad>(StringComparer.Ordinal);var sourceIds=new HashSet<string>(StringComparer.Ordinal);int points=0;double networkLength=0;foreach(var r in roads){if(r==null||string.IsNullOrEmpty(r.id)||r.id.Length>100||!sourceIds.Add(r.id)||r.points==null||r.points.Length<2||r.points.Length>10000||!Finite(r.width)||r.width<2||r.width>80||(points+=r.points.Length)>300000)throw new InvalidDataException("Explicit width schema requires bounded unique source identities");double length=0;for(int i=0;i<r.points.Length;i++){var p=r.points[i];if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new InvalidDataException("Width source coordinate");if(i>0){double x=(double)p.x-r.points[i-1].x,y=(double)p.y-r.points[i-1].y,z=(double)p.z-r.points[i-1].z;length+=Math.Sqrt(x*x+y*y+z*z);}}if(r.tunnel)continue;networkLength+=length;if(length<.01||length>100000||networkLength>2000000)throw new InvalidDataException("Width source length");byId.Add(r.id,r);}var seen=new HashSet<string>(StringComparer.Ordinal);int total=0;
            if(profiles.Count!=byId.Count)throw new InvalidDataException("Finalized width profiles must cover all visible canonical roads");
            foreach(var p in profiles)
            {
                if(p==null||p.roadId==null||p.roadId.Length>100||!seen.Add(p.roadId)||!byId.TryGetValue(p.roadId,out var r)||p.distances==null||p.widths==null||p.derivatives==null||p.distances.Length<2||p.distances.Length>512||p.widths.Length!=p.distances.Length||p.derivatives.Length!=p.distances.Length)throw new InvalidDataException("Invalid width DTO shape");
                total+=p.distances.Length;if(total>80000)throw new InvalidDataException("Width DTO station budget");double length=0;for(int i=1;i<r.points.Length;i++){double x=(double)r.points[i].x-r.points[i-1].x,y=(double)r.points[i].y-r.points[i-1].y,z=(double)r.points[i].z-r.points[i-1].z;length+=Math.Sqrt(x*x+y*y+z*z);}
                if(p.distances[0]!=0||Math.Abs(p.distances.Last()-length)>Math.Max(.002,length*.000001))throw new InvalidDataException("Width DTO distance domain");
                for(int i=0;i<p.distances.Length;i++)
                {
                    if(!Finite(p.distances[i])||!Finite(p.widths[i])||!Finite(p.derivatives[i])||p.widths[i]<2||p.widths[i]>80||Math.Abs(p.derivatives[i])>8||i>0&&p.distances[i]-p.distances[i-1]<.0001)throw new InvalidDataException("Width DTO numeric range: "+p.roadId+" station "+i);
                    if(i==0)continue;double h=p.distances[i]-p.distances[i-1],w0=p.widths[i-1],w1=p.widths[i],m0=p.derivatives[i-1],m1=p.derivatives[i];
                    double a=2*w0-2*w1+h*(m0+m1),b=-3*w0+3*w1-h*(2*m0+m1),c=h*m0;
                    void Check(double t){if(t>0&&t<1){double w=((a*t+b)*t+c)*t+w0;if(w<1.99999||w>80.00001)throw new InvalidDataException("Width DTO cubic overshoot");}}
                    if(Math.Abs(a)<1e-12){if(Math.Abs(b)>1e-12)Check(-c/(2*b));}
                    else{double d=4*b*b-12*a*c;if(d>=0){Check((-2*b-Math.Sqrt(d))/(6*a));Check((-2*b+Math.Sqrt(d))/(6*a));}}
                }
            }
        }
    }
}
