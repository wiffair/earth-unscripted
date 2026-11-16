using System;
using UnityEngine;
using EarthUnscripted.Core;

namespace EarthUnscripted
{
    /// <summary>
    /// Maps a selected pit bypass onto the main lap without projecting a car
    /// sideways onto an unrelated corner. Only an entry/exit join may engage
    /// the bypass; arriving halfway down the lane cannot manufacture progress.
    /// </summary>
    public sealed class PitRouteProgress
    {
        const float LaneRadiusMetres=4, JoinWindowMetres=8, JoinProgressToleranceMetres=2;
        readonly Vector3[] points;
        readonly float[] stations;
        readonly float mainLength, entry, span;
        public PitRouteProgress(RoutePath main,Vector3[] lane)
        {
            if(main==null||lane==null||lane.Length<2)throw new ArgumentException("Pit progress requires a main route and lane");
            points=(Vector3[])lane.Clone();stations=new float[points.Length];mainLength=main.Length;
            for(int i=1;i<points.Length;i++)stations[i]=stations[i-1]+Vector3.Distance(points[i-1],points[i]);
            if(stations[stations.Length-1]<=0)throw new ArgumentException("Pit progress requires a nonzero lane");
            entry=main.Closest(points[0],out _,out _);
            float exit=main.Closest(points[points.Length-1],out _,out _);
            span=DrivingMath.Wrap(exit-entry,mainLength);
        }
        /// <param name="engaged">Per-car state, reset when a race starts or the car leaves the selected lane.</param>
        public bool TryProgress(Vector3 position,float mainProgress,ref bool engaged,out float progress)
        {
            float best=float.MaxValue,station=0;
            for(int i=0;i<points.Length-1;i++)
            {
                var v=points[i+1]-points[i];v.y=0;var delta=position-points[i];delta.y=0;
                float squared=v.sqrMagnitude;if(squared<=0)continue;
                float t=Math.Max(0,Math.Min(1,Vector3.Dot(delta,v)/squared));
                float error=(delta-v*t).sqrMagnitude;
                if(error>=best)continue;best=error;station=stations[i]+(stations[i+1]-stations[i])*t;
            }
            progress=DrivingMath.Wrap(entry+span*station/stations[stations.Length-1],mainLength);
            if(best>=LaneRadiusMetres*LaneRadiusMetres){engaged=false;return false;}
            if(!engaged)
            {
                bool atJoin=station<=JoinWindowMetres||stations[stations.Length-1]-station<=JoinWindowMetres;
                if(!atJoin||Math.Abs(DrivingMath.SignedRouteDelta(mainProgress,progress,mainLength))>JoinProgressToleranceMetres)return false;
                engaged=true;
            }
            return true;
        }
    }
}
