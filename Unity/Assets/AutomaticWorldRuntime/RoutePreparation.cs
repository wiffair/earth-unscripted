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
        public bool NextMissing(Vector3 car,float range,float areaSize,Func<int,int,bool> skip,out Vector3 point,out float aheadDistance)
        {
            point=default;aheadDistance=0;if(!Active)return false;float progress=Progress(car);
            float end=Mathf.Min(range,Length-progress);
            for(float ahead=0;;ahead=Mathf.Min(end,ahead+64))
            {var p=At(progress+ahead);int x=Mathf.FloorToInt(p.x/areaSize),z=Mathf.FloorToInt(p.z/areaSize);if(!skip(x,z)){point=p;aheadDistance=ahead;return true;}if(ahead>=end)break;}
            return false;
        }
        public static float GeometryPriority(float ahead,float lateral)=>-150+ahead*.02f+lateral*1.5f;

    }
}
