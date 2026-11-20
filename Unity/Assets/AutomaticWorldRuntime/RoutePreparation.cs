using System;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    // Snapshot of the active route only: preview/cancelled destinations cannot
    // consume route priority. Arc distance follows curves, not just car heading.
    public sealed class RoutePreparation
    {
        readonly EarthUnscripted.NavigationRoute route;
        public Vector3[] Points=>route?.points;
        public RoutePreparation(Vector3[] points){if(points!=null&&points.Length>=2)route=new EarthUnscripted.NavigationRoute((Vector3[])points.Clone());}
        public float Progress(Vector3 car)=>route==null?0:route.Closest(car,out _);
        public Vector3 At(float station)=>route.At(station);
        public float Length=>route?.Length??0;
        public bool Active=>route!=null;
        // Traverse every crossed source core, including diagonal slivers shorter
        // than the former 64m sampling interval. Returned points only select a
        // source core; clamp them inside it so float round trips keep that ID.
        public bool NextMissing(Vector3 car,float range,float areaSize,Func<int,int,bool> skip,out Vector3 point,out float aheadDistance)
        {
            point=default;aheadDistance=0;
            if(!Active||areaSize<=0||float.IsNaN(range)||float.IsInfinity(range))return false;
            float progress=Progress(car),end=Mathf.Min(Length,progress+Mathf.Max(0,range));
            for(int i=1;i<route.points.Length;i++)
            {
                float from=Mathf.Max(progress,route.stations[i-1]),to=Mathf.Min(end,route.stations[i]);
                float length=route.stations[i]-route.stations[i-1];
                if(to<from||length<.0001f)continue;
                var a=Vector3.Lerp(route.points[i-1],route.points[i],(from-route.stations[i-1])/length);
                var b=Vector3.Lerp(route.points[i-1],route.points[i],(to-route.stations[i-1])/length);
                double dx=(double)b.x-a.x,dz=(double)b.z-a.z;
                int x=Mathf.FloorToInt(a.x/areaSize),z=Mathf.FloorToInt(a.z/areaSize);
                int stepX=Math.Sign(dx),stepZ=Math.Sign(dz);
                double crossX=stepX==0?double.PositiveInfinity:(((stepX>0?x+1:x)*(double)areaSize)-a.x)/dx;
                double crossZ=stepZ==0?double.PositiveInfinity:(((stepZ>0?z+1:z)*(double)areaSize)-a.z)/dz;
                double deltaX=stepX==0?double.PositiveInfinity:areaSize/Math.Abs(dx);
                double deltaZ=stepZ==0?double.PositiveInfinity:areaSize/Math.Abs(dz);
                double entered=0;
                int crossings=Math.Abs(Mathf.FloorToInt(b.x/areaSize)-x)+Math.Abs(Mathf.FloorToInt(b.z/areaSize)-z)+3;
                for(int cell=0;cell<crossings;cell++)
                {
                    double exit=Math.Min(1,Math.Min(crossX,crossZ));
                    if((exit>entered+1e-12||from==to)&&!skip(x,z))
                    {
                        var at=Vector3.Lerp(a,b,(float)((entered+exit)*.5));
                        point=InsideCore(at,x,z,areaSize);aheadDistance=from-progress+(float)entered*(to-from);return true;
                    }
                    if(exit>=1)break;
                    bool advanceX=crossX<=crossZ,advanceZ=crossZ<=crossX;
                    if(advanceX){x+=stepX;crossX+=deltaX;}
                    if(advanceZ){z+=stepZ;crossZ+=deltaZ;}
                    entered=exit;
                }
                if(to>=end)break;
            }
            var last=At(end);int lastX=Mathf.FloorToInt(last.x/areaSize),lastZ=Mathf.FloorToInt(last.z/areaSize);
            if(skip(lastX,lastZ))return false;
            point=InsideCore(last,lastX,lastZ,areaSize);aheadDistance=end-progress;return true;
        }
        static Vector3 InsideCore(Vector3 point,int x,int z,float size)=>new Vector3(
            Mathf.Clamp(point.x,x*size+.125f,(x+1)*size-.125f),point.y,
            Mathf.Clamp(point.z,z*size+.125f,(z+1)*size-.125f));

        // A lane/wheel corridor can touch a neighbouring core even when the
        // route centreline misses it. Analytic clipping has no sampling phase.
        public bool AreaAhead(Vector3 car,float range,float areaSize,int x,int z,float padding,out float ahead)
        {
            ahead=0;if(!Active||range<0||areaSize<=0)return false;
            float progress=Progress(car),end=Mathf.Min(Length,progress+range);
            for(int i=1;i<route.points.Length;i++)
            {
                float length=route.stations[i]-route.stations[i-1];
                if(length<.0001f||route.stations[i]<progress)continue;
                if(route.stations[i-1]>end)break;
                var a=route.points[i-1];var delta=route.points[i]-a;
                float lo=Mathf.Max(0,(progress-route.stations[i-1])/length),hi=Mathf.Min(1,(end-route.stations[i-1])/length);
                if(!ClipAxis(a.x,delta.x,x*areaSize-padding,(x+1)*areaSize+padding,ref lo,ref hi)||
                   !ClipAxis(a.z,delta.z,z*areaSize-padding,(z+1)*areaSize+padding,ref lo,ref hi))continue;
                ahead=Mathf.Max(0,route.stations[i-1]+lo*length-progress);return true;
            }
            return false;
        }
        static bool ClipAxis(float start,float direction,float minimum,float maximum,ref float lo,ref float hi)
        {
            if(Mathf.Abs(direction)<.000001f)return start>=minimum&&start<=maximum;
            float first=(minimum-start)/direction,last=(maximum-start)/direction;
            if(first>last){float swap=first;first=last;last=swap;}
            lo=Mathf.Max(lo,first);hi=Mathf.Min(hi,last);return lo<=hi;
        }
        public static float GeometryPriority(float ahead,float lateral)=>-150+ahead*.02f+lateral*1.5f;

    }
}
