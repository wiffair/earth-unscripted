using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Native = EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class VehicleProjectionCostVerification
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        struct Candidate { public GeoRoad road; public Vector3 a,b; public float start,length; }
        static Candidate[] Candidates(GeoRegion region, Vector3 at)
        {
            var cells=(IDictionary)typeof(GeoRegion).GetField("cells",Hidden).GetValue(region);
            var key=new Vector2Int(Mathf.FloorToInt(at.x/80),Mathf.FloorToInt(at.z/80));
            if(!cells.Contains(key))return Array.Empty<Candidate>();
            var result=new List<Candidate>();
            foreach(var item in (IEnumerable)cells[key])
            {
                var t=item.GetType();object Get(string n)=>t.GetField(n,Hidden).GetValue(item);
                result.Add(new Candidate{road=(GeoRoad)Get("road"),a=(Vector3)Get("a"),b=(Vector3)Get("b"),start=(float)Get("start"),length=(float)Get("length")});
            }
            return result.ToArray();
        }
        // Independent copy of the original scan; candidate memberships are
        // captured before timing so reflection is not part of either sample.
        static GeoRegion.VehicleRoadProjection Original(GeoRegion region,Candidate[] candidates,Vector3 at,float support,Func<GeoRoad,float,float,float> height)
        {
            var best=new GeoRegion.RoadProjection(null,at,float.MaxValue,0,0);
            float bestSurface=0,bestGap=float.MaxValue,bestScore=float.MaxValue;bool bestPlausible=false;
            foreach(var c in candidates)
            {
                var delta=c.b-c.a;float square=delta.x*delta.x+delta.z*delta.z;
                float fraction=Mathf.Clamp01(((at.x-c.a.x)*delta.x+(at.z-c.a.z)*delta.z)/Mathf.Max(.001f,square));
                var centre=Vector3.Lerp(c.a,c.b,fraction);float separation=Vector2.Distance(new Vector2(at.x,at.z),new Vector2(centre.x,centre.z));
                float station=c.start+c.length*fraction,width=region.EffectiveWidth(c.road,station),surface=height(c.road,at.x,at.z);
                if(float.IsNaN(surface)||float.IsInfinity(surface))continue;
                float gap=Mathf.Abs(support-surface);bool plausible=gap<=GeoRegion.VehicleRoadHeightToleranceMetres;
                float score=separation-width*.5f+gap*2;
                if(best.Road!=null){if(bestPlausible&&!plausible)continue;if(bestPlausible==plausible&&score>=bestScore)continue;}
                best=new GeoRegion.RoadProjection(c.road,centre,separation,station,width);bestSurface=surface;bestGap=gap;bestScore=score;bestPlausible=plausible;
            }
            // The result constructor is runtime-internal, so compare a private
            // reference row instead of constructing one across assembly bounds.
            return Make(best,bestSurface,bestGap);
        }
        static readonly ConstructorInfo Result=typeof(GeoRegion.VehicleRoadProjection).GetConstructors(Hidden).Single();
        static GeoRegion.VehicleRoadProjection Make(GeoRegion.RoadProjection p,float y,float gap)=>(GeoRegion.VehicleRoadProjection)Result.Invoke(new object[]{p,y,gap});
        public static void Run()
        {
            int checks=0;void Need(bool ok,string why){checks++;if(!ok)throw new Exception("VEHICLE PROJECTION COST "+why);}
            var root=new GameObject("Dense road projection regression");
            try
            {
                GeoRoad Road(string id,float x,float y,float width,int n)=>new GeoRoad{id=id,start=id+"-a",end=id+"-b",width=width,points=Enumerable.Range(0,n).Select(i=>new Vector3(x,y+(float)Math.Sin(i*.003)*.2f,-1500+i*3000f/(n-1))).ToArray()};
                var lower=Road("same-id",0,0,8,6001);var upper=Road("same-id",0,8,32,6001);var side=Road("side",12,0,7,3001);
                var region=new GeoRegion{roads=new[]{lower,upper,side,lower},waters=Array.Empty<GeoWater>()};
                typeof(GeoRegion).GetField("NativeWorld",Hidden).SetValue(region,root.AddComponent<Native>());
                region.RebuildRenderQueryIndexesOnly(true);
                var height=(Func<GeoRoad,float,float,float>)typeof(GeoRegion).GetMethod("RoadSurfaceHeightForRoad",Hidden).CreateDelegate(typeof(Func<GeoRoad,float,float,float>),region);
                var random=new System.Random(26011);
                for(int i=0;i<100;i++)
                {
                    var at=new Vector3((float)random.NextDouble()*90-45,0,(float)random.NextDouble()*3600-1800);float support=i%3==0?8:0;
                    var expected=Original(region,Candidates(region,at),at,support,height);var actual=region.ProjectRoadForVehicle(at,support);
                    Need(ReferenceEquals(expected.Road,actual.Road),"road occurrence identity, including duplicate IDs");
                    Need(expected.Source.Point==actual.Source.Point&&expected.Source.SourceDistance==actual.Source.SourceDistance&&expected.Source.Width==actual.Source.Width&&expected.Source.Separation==actual.Source.Separation,"exact source projection and width");
                    Need(expected.SurfaceHeight==actual.SurfaceHeight&&expected.SurfaceHeightGap==actual.SurfaceHeightGap&&expected.OnRoad==actual.OnRoad,"exact height and vertical-level classification");
                }
                var point=Vector3.zero;Need(ReferenceEquals(region.ProjectRoadForVehicle(point,0).Road,lower),"wide upper deck never wins over actual lower road");
                Need(ReferenceEquals(region.ProjectRoadForVehicle(point,8).Road,upper),"upper deck remains selected at its true height");
                var before=region.ProjectRoadForVehicle(point,0).SurfaceHeight;
                for(int i=0;i<lower.points.Length;i++)lower.points[i].y+=2;
                var changed=region.ProjectRoadForVehicle(point,2);
                var changedOracle=Original(region,Candidates(region,point),point,2,height);
                Need(changed.SurfaceHeight==changedOracle.SurfaceHeight&&Mathf.Abs(changed.SurfaceHeight-before-2)<.00001f,"query-local cache cannot retain an earlier profile height");
                Need(region.ProjectRoadForVehicle(new Vector3(float.NaN,0,0),0).Road==null,"invalid coordinate still rejected");
                Need(region.ProjectRoadForVehicle(point,float.PositiveInfinity).Road==null,"invalid support still rejected");
                var candidates=Candidates(region,point);int groups=0;GeoRoad previous=null;foreach(var c in candidates){if(!ReferenceEquals(previous,c.road))groups++;previous=c.road;}
                Need(candidates.Length>500&&groups==4,"dense native fixture exercises hundreds of repeated profiles and duplicate road occurrences");
                for(int i=0;i<3;i++){Original(region,candidates,point,2,height);region.ProjectRoadForVehicle(point,2);}
                var clock=Stopwatch.StartNew();for(int i=0;i<12;i++)Original(region,candidates,point,2,height);double originalMs=clock.Elapsed.TotalMilliseconds/12;
                clock.Restart();for(int i=0;i<120;i++)region.ProjectRoadForVehicle(point,2);double candidateMs=clock.Elapsed.TotalMilliseconds/120;
                UnityEngine.Debug.Log($"VEHICLE_PROJECTION_COST_PASS {checks} candidates={candidates.Length} profileRuns={groups} originalMs={originalMs:F4} candidateMs={candidateMs:F4}");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
