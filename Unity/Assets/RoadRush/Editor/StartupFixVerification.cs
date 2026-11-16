using System;
using System.IO;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class StartupFixVerification
    {
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-startupFixture");
            if(at<0)throw new InvalidOperationException("Explicit fixture required");
            string raw=File.ReadAllText(args[at+1]);var original=JsonUtility.FromJson<GeoRegion>(raw);
            int checks=0;
            void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);checks++;}
            var decoded=WorldSnapshotCodec.DecodeBootSource(raw,out bool authoritative);
            Check(!authoritative,"Raw map classification");Check(decoded.waters[34].points.Length==86,"Real five-point river tail clipped with interpolated boundary vertex");
            Check(JsonUtility.ToJson(original.roads[0])==JsonUtility.ToJson(decoded.roads[0]),"Driving road untouched");
            Check(File.ReadAllText(args[at+1])==raw,"Source bytes retained");
            var points=decoded.waters[34].points;Check(points[points.Length-1].z>=decoded.minZ-RawWaterwayBoundary.GeometryMarginMetres,"Exact boundary after clipping");
            var type=typeof(GeoRegion).Assembly.GetType("RoadRush.RawWaterwayBoundary");var clip=type.GetMethod("ClipOwned");
            GeoRegion Map(Vector3[] p)=>new GeoRegion{minX=0,minZ=0,width=100,depth=100,waters=new[]{new GeoWater{name="fixture",width=8,points=p}}};
            float edge=-RawWaterwayBoundary.GeometryMarginMetres;
            var crossing=Map(new[]{new Vector3(edge-10,10,0),new Vector3(edge+10,20,0)});clip.Invoke(null,new object[]{crossing});
            Check(crossing.waters.Length==1&&crossing.waters[0].points[0].x==edge&&Mathf.Abs(crossing.waters[0].points[0].y-15)<.0001f,"Boundary interpolates height");
            var outside=Map(new[]{new Vector3(edge-20,0,0),new Vector3(edge-10,0,1)});clip.Invoke(null,new object[]{outside});Check(outside.waters.Length==0,"Entire outside reach omitted");
            var split=Map(new[]{new Vector3(0,0,0),new Vector3(edge-20,0,0),new Vector3(edge-20,0,20),new Vector3(0,0,20)});clip.Invoke(null,new object[]{split});Check(split.waters.Length==2,"Outside gap never becomes a connecting river");
            var inBounds=Map(new[]{Vector3.zero,Vector3.one});var same=inBounds.waters;clip.Invoke(null,new object[]{inBounds});Check(ReferenceEquals(same,inBounds.waters),"Entire in-bound geometry retained exactly");
            var bad=Map(new[]{Vector3.zero,new Vector3(float.NaN,0,0)});bool refused=false;try{clip.Invoke(null,new object[]{bad});}catch(System.Reflection.TargetInvocationException){refused=true;}Check(refused,"Nonfinite coordinate is refused, not clipped away");
            original.roadWidthSchema=1;refused=false;try{WorldSnapshotCodec.DecodeBootSource(JsonUtility.ToJson(original),out _);}catch(InvalidDataException){refused=true;}Check(refused,"Authoritative snapshot not repaired or silently changed");
            File.WriteAllText(args[at+1]+"-checks.json","{\"status\":\"PASS_STARTUP_BOUNDARIES\",\"checks\":"+checks+"}");
        }
        public static void RunAndBuild(){Run();ProjectSetup.BuildAll();}
    }
}
