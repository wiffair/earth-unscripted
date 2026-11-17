using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BetaLocationRouteVerification
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        static GeoRoad Road(int index,float width=8)=>new GeoRoad{id="short-"+index,start="n"+index,end="n"+(index+1),name="Short source "+index,width=width,oneWay=1,points=new[]{new Vector3(0,0,index*80),new Vector3(0,0,(index+1)*80)}};
        static GeoRegion Region(params GeoRoad[] roads)
        {var r=new GeoRegion{id="connected-cardiff-fixture",drivingSide=-1,minX=-100,minZ=-100,width=500,depth=500,nx=2,nz=2,heights=new float[4],roads=roads,route=roads[0].points,waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>()};r.Prepare();return r;}
        public static int Exercise()
        {
            checks=0;var a=Road(0);var b=Road(1);var c=Road(2);var region=Region(a,b,c);var original=region.route;
            Check(region.roads.All(r=>r.length<230),"All source roads fail the former single-road length heuristic");
            Check(BetaLocationVerification.TrySurfaceRoute(region,Vector3.zero,out var route),"Three connected short roads provide a real 240m route");
            Check(route.Roads.Length==3&&route.Length>=230&&route.Directions.All(d=>d==1),"Route retains legal forward source legs");
            Check(ReferenceEquals(region.route,original)&&route.Roads.SequenceEqual(new[]{a,b,c}),"Selection preserves the world's guide and exact road objects");
            foreach(float d in new[]{10f,79f,80f,81f,159f,160f,195f,230f})
            {int leg=Mathf.Min(2,(int)(d/80));var owner=region.roads[leg];var centre=region.RoadPoint(owner,d-leg*80,out var forward);var expected=region.DrivingLanePoint(owner,centre,forward,out _);Check(Vector3.Distance(route.At(d),expected)<.001f,"Lane/height point retains source ownership at "+d);}
            Check(Mathf.Abs(route.Closest(route.At(195))-195)<.01f,"Actual lane progress crosses short-road joins");
            b=Road(1);b.oneWay=-1;Check(!BetaLocationVerification.TrySurfaceRoute(Region(Road(0),b,Road(2)),Vector3.zero,out _),"Wrong-way middle leg cannot manufacture a connected route");
            b=Road(1);b.start="disconnected";Check(!BetaLocationVerification.TrySurfaceRoute(Region(Road(0),b,Road(2)),Vector3.zero,out _),"Nearby roads without source-node connectivity are rejected");
            b=Road(1);b.points[0]+=Vector3.right*2;Check(!BetaLocationVerification.TrySurfaceRoute(Region(Road(0),b,Road(2)),Vector3.zero,out _),"Shared node names cannot bridge a physical source gap");
            b=Road(1);b.points=Array.ConvertAll(b.points,p=>p+Vector3.up*8);Check(!BetaLocationVerification.TrySurfaceRoute(Region(Road(0),b,Road(2)),Vector3.zero,out _),"Coincident XZ cannot join separate road heights");
            Check(!BetaLocationVerification.TrySurfaceRoute(Region(Road(0),Road(1,4),Road(2)),Vector3.zero,out _),"Narrow future legs are excluded");
            a=Road(0);a.NativeTurnLanes="left";Check(!BetaLocationVerification.TrySurfaceRoute(Region(a,Road(1),Road(2)),Vector3.zero,out _),"Mapped turn lanes cannot be ignored to continue straight");
            b=Road(1);b.tunnel=true;Check(!BetaLocationVerification.TrySurfaceRoute(Region(Road(0),b,Road(2)),Vector3.zero,out _),"Unsupported tunnel fixture legs remain excluded");
            a=Road(0);b=Road(1);c=Road(2);foreach(var r in new[]{a,b,c}){r.oneWay=-1;Array.Reverse(r.points);var node=r.start;r.start=r.end;r.end=node;}
            Check(BetaLocationVerification.TrySurfaceRoute(Region(a,b,c),Vector3.zero,out route)&&route.Directions.All(d=>d==-1),"Reverse-authored one-way geometry retains legal travel direction");
            Check(route.At(195).z>194,"Reverse-authored legs still advance physically along the source chain");
            a=Road(0);b=Road(1);c=Road(2);foreach(var r in new[]{a,b,c}){r.bridge=true;r.points=Array.ConvertAll(r.points,p=>p+Vector3.up*8);}
            var lower=new GeoRoad{id="lower-wide",start="l0",end="l1",width=40,oneWay=1,points=new[]{new Vector3(-100,0,120),new Vector3(100,0,120)}};
            region=Region(a,b,c,lower);Check(BetaLocationVerification.TrySurfaceRoute(region,new Vector3(0,8,0),out route),"Upper source chain can be selected above a wider lower crossing");
            var owned=region.DrivingLanePoint(b,region.RoadPoint(b,40,out var tangent),tangent,out _);Check(Mathf.Abs(route.At(120).y-owned.y)<.001f,"Lane sampling retains upper source height instead of planar lower-road selection");
            return checks;
        }
        public static void Run()
        {try{Debug.Log("CARDIFF CONNECTED ROUTE PASS "+Exercise());EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
