using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class BuildingSiteVerification
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception("BUILDING SITE FAIL: "+text);checks++;Debug.Log("BUILDING SITE PASS: "+text);}
        static GeoBuilding House()=>new GeoBuilding{id="site-fixture",kind="building",height=6,roof="gabled",points=new[]{new Vector3(0,700,20),new Vector3(10,700,20),new Vector3(10,700,28),new Vector3(0,700,28),new Vector3(0,700,20)}};
        static GeoRegion Region(float roadY)
        {
            var r=new GeoRegion{id="site-fixture",name="Site fixture",preparedWidths=true,minX=-100,minZ=-100,width=200,depth=200,nx=2,nz=2,heights=new[]{100f,100f,100f,100f},landNx=2,landNz=2,landCover=new int[4],roads=new[]{new GeoRoad{id="site-road",start="a",end="b",width=8,points=new[]{new Vector3(-80,roadY,0),new Vector3(80,roadY,0)}}},buildings=new[]{House()},areas=Array.Empty<GeoArea>(),waters=new[]{new GeoWater{name="untouched river",width=5,points=new[]{new Vector3(-30,96,-30),new Vector3(30,96,-30)}}},route=new[]{Vector3.zero,Vector3.right*40}};r.Prepare();r.HasServicePad=true;r.ServicePad=new Vector3(70,104,70);return r;
        }
        static bool Same(BuildingSitePlan.Data a,BuildingSitePlan.Data b)=>a.LevelFloorY==b.LevelFloorY&&a.GroundMedianY==b.GroundMedianY&&a.BoundaryMinY==b.BoundaryMinY&&a.BoundaryMaxY==b.BoundaryMaxY&&a.Samples.SequenceEqual(b.Samples);
        public static void Run()
        {
            try
            {
                var r=Region(103);var b=House();string before=JsonUtility.ToJson(r),footprint=JsonUtility.ToJson(b);int calls=0;Func<float,float,float> flat=(x,z)=>{calls++;return 100;};
                var above=BuildingSitePlan.Create(r,b,flat);Check(Mathf.Abs(above.LevelFloorY-100.12f)<.00001f&&above.GroundMedianY==100,"level floor follows actual sampled terrain plus0.12m, ignoring original vertex Y and elevated nearby road");
                Check(calls==above.SampleCount&&calls<=64&&above.BoundarySampleCount==above.SampleCount-1&&calls>=4,"bounded perimeter and centre sampling makes at most64 actual surface calls");
                Check(above.BoundaryMinY==100&&above.BoundaryMaxY==100&&Mathf.Abs(above.MaxExposedFoundation-.12f)<.00001f&&above.MaxUphillCut==0,"flat foundation metadata records sampled ground and shallow exposure without stretching storeys");
                Check(above.HasNearbyRoad&&above.NearestRoadY==103&&Mathf.Abs(above.RoadGroundDelta-3)<.00001f&&Mathf.Abs(above.RoadToFloorDelta-2.88f)<.00001f&&above.RequiresAccessReview,"road3m above site is access metadata rather than a placement instruction");
                var lower=BuildingSitePlan.Create(Region(97),b,(x,z)=>100);Check(Same(above,lower)&&lower.RoadGroundDelta==-3&&Mathf.Abs(lower.RoadToFloorDelta+3.12f)<.00001f,"road3m below the same site leaves floor and all sampled ground unchanged");
                var noRoad=Region(500);noRoad.roads=Array.Empty<GeoRoad>();noRoad.Prepare();var isolated=BuildingSitePlan.Create(noRoad,b,(x,z)=>100);Check(!isolated.HasNearbyRoad&&Same(above,isolated),"roadless sites keep the same terrain-derived floor with explicit missing-road metadata");
                Func<float,float,float> gentle=(x,z)=>100+x*.2f+z*.03f;var slope=BuildingSitePlan.Create(r,b,gentle);var repeat=BuildingSitePlan.Create(r,b,gentle);Check(Same(slope,repeat),"same source footprint and surface produce exactly deterministic median and sample descriptors");
                var reversed=House();reversed.points=reversed.points.Reverse().ToArray();var rotated=House();var q=rotated.points.Take(4).Skip(2).Concat(rotated.points.Take(2)).ToArray();rotated.points=q.Concat(new[]{q[0]}).ToArray();Check(Same(slope,BuildingSitePlan.Create(r,reversed,gentle))&&Same(slope,BuildingSitePlan.Create(r,rotated,gentle)),"clockwise counterclockwise and cyclically rotated equivalent rings use identical canonical sample positions and floor");
                var hs=slope.Samples.Select(p=>p.y).OrderBy(y=>y).ToArray();float median=hs.Length%2==1?hs[hs.Length/2]:(hs[hs.Length/2-1]+hs[hs.Length/2])*.5f;Debug.Log($"BUILDING SITE MEDIAN measured={slope.GroundMedianY:R} expected={median:R} floor={slope.LevelFloorY:R} expectedFloor={median+BuildingSitePlan.FloorClearance:R} threshold={slope.EntryThresholdY:R}");Check(Mathf.Abs(slope.GroundMedianY-median)<.00002f&&Mathf.Abs(slope.LevelFloorY-(median+BuildingSitePlan.FloorClearance))<.00002f&&slope.EntryThresholdY==slope.LevelFloorY,"every floor and entrance threshold uses one horizontal median datum within0.02mm float precision");
                Check(slope.MaxExposedFoundation>0&&slope.MaxUphillCut>0&&Mathf.Abs(slope.FoundationBottomY-(slope.BoundaryMinY-.2f))<.00002f,"sloping sites expose downhill foundation and uphill cut metadata without changing the terrain");
                var high=BuildingSitePlan.Create(r,b,(x,z)=>100+x*.5f);Check(high.HighRelief&&high.ConservativeFallback&&high.RequiresAccessReview&&!high.RoofBurialRisk,"high-relief site flags conservative access review while keeping its unraised median floor");
                var lowHouse=House();lowHouse.height=2.8f;var buried=BuildingSitePlan.Create(r,lowHouse,(x,z)=>100+x*.8f);Check(buried.RoofBurialRisk&&buried.ConservativeFallback&&buried.BoundaryMaxY>buried.LevelFloorY+lowHouse.height-.3f,"low building on a steep footprint explicitly flags sampled roof-burial risk for root fallback");
                var concave=House();concave.points=new[]{new Vector3(0,0,20),new Vector3(12,0,20),new Vector3(12,0,32),new Vector3(8,0,32),new Vector3(8,0,24),new Vector3(4,0,24),new Vector3(4,0,32),new Vector3(0,0,32),new Vector3(0,0,20)};var complex=BuildingSitePlan.Create(r,concave,(x,z)=>100);Check(complex.ComplexFootprint&&complex.CentreOutsideFootprint&&complex.ConservativeFallback,"complex U-shaped plot and outside centre are labelled rather than treated as a surveyed garden");
                var many=House();many.points=Enumerable.Range(0,128).Select(i=>new Vector3(Mathf.Cos(i*Mathf.PI/64)*200,0,Mathf.Sin(i*Mathf.PI/64)*200)).Concat(new[]{new Vector3(200,0,0)}).ToArray();calls=0;var bounded=BuildingSitePlan.Create(r,many,flat);Check(bounded.SampleCount==64&&calls==64&&bounded.ComplexFootprint,"long128-corner outline keeps its explicit64-call bound and complex fallback");
                r.roads[0].bridge=true;r.Prepare();var bridge=BuildingSitePlan.Create(r,b,(x,z)=>100);Check(bridge.NearestRoadIsBridge&&bridge.LevelFloorY==above.LevelFloorY,"overhead bridge elevation is identified but never used as the foundation datum");r.roads[0].bridge=false;r.Prepare();
                Check(JsonUtility.ToJson(r)==before&&JsonUtility.ToJson(b)==footprint&&r.HasServicePad&&r.ServicePad==new Vector3(70,104,70)&&r.heights.All(y=>y==100),"planning leaves road water service-pad terrain arrays and original footprint geometry unchanged");
                bool invalid=false;try{BuildingSitePlan.Create(r,b,(x,z)=>float.NaN);}catch(ArgumentException){invalid=true;}Check(invalid,"nonfinite surface data fails explicitly rather than producing a corrupt floor");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/building-site.txt",$"PASS {checks} actual bounded terrain median independent-road level-floor deterministic foundation risk and nonmutation checks\nNo terrain grading, parcels, access geometry or native pixel acceptance claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
