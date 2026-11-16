using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Clipper2Lib;

namespace EarthUnscripted.Editor
{
    public static class TerrainGeometryDependencyVerification
    {
        static int checks;
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("TERRAIN DEPENDENCY FAIL "+why);checks++;Debug.Log("TERRAIN DEPENDENCY PASS "+why);}
        static Path64 Rect(long x,long y,long w,long h)=>new Path64{new Point64(x,y),new Point64(x+w,y),new Point64(x+w,y+h),new Point64(x,y+h)};
        static string Key(Point64 p)=>p.X+","+p.Y;
        static long Cross(Point64 a,Point64 b,Point64 c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        static long TwiceArea(Paths64 paths){long result=0;foreach(var p in paths)for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];result+=a.X*b.Y-a.Y*b.X;}return Math.Abs(result);}
        static Paths64 Case(string name,Paths64 subject,Paths64 cuts,long expectedArea)
        {
            var domain=Clipper.Difference(subject,cuts,FillRule.NonZero);
            Check(TwiceArea(domain)==expectedArea*2,"exact Boolean target area "+name);
            var status=Clipper.Triangulate(domain,out var triangles,true);
            Check(status==TriangulateResult.success&&triangles.Count>0,"CDT completes "+name);
            long sum=0;var edges=new Dictionary<string,int>(StringComparer.Ordinal);bool allTriangles=true;
            foreach(var t in triangles)
            {
                allTriangles&=t.Count==3;if(t.Count!=3)continue;long area=Math.Abs(Cross(t[0],t[1],t[2]));allTriangles&=area>0;sum+=area;
                for(int i=0;i<3;i++){string a=Key(t[i]),b=Key(t[(i+1)%3]);string edge=string.CompareOrdinal(a,b)<0?a+"|"+b:b+"|"+a;edges.TryGetValue(edge,out int n);edges[edge]=n+1;}
            }
            Check(allTriangles&&sum==expectedArea*2,"nonzero triangle areas cover exact target area "+name);
            Check(edges.Values.All(v=>v==1||v==2),"triangle edges have bounded manifold incidence "+name);
            foreach(var t in triangles)
            {
                double x=(t[0].X+t[1].X+t[2].X)/3d,y=(t[0].Y+t[1].Y+t[2].Y)/3d;
                bool Inside(Path64 p)
                {bool inside=false;for(int i=0,j=p.Count-1;i<p.Count;j=i++){var a=p[i];var b=p[j];if((a.Y>y)!=(b.Y>y)&&x<(b.X-a.X)*(y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;}return inside;}
                Check(subject.Any(Inside)&&!cuts.Any(Inside),"triangle centroid lies in intended terrain domain "+name);
            }
            return triangles;
        }
        static string Hash(string path){if(!File.Exists(path))return "missing";using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path)));}
        public static void Run()
        {
            int exit=1;checks=0;string output=Path.GetFullPath("Builds/Verification/TerrainGeometryDependency/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=Hash(career);
            try
            {
                Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"no real gameplay session");
                Case("overlapping asphalt holes",new Paths64{Rect(0,0,1000,1000)},new Paths64{Rect(200,200,200,200),Rect(300,300,200,200)},930000);
                Case("road cuts through cell",new Paths64{Rect(0,0,1000,1000)},new Paths64{Rect(-100,400,1200,200)},800000);
                Case("disjoint terrain regions",new Paths64{Rect(0,0,300,300),Rect(600,600,300,300)},new Paths64(),180000);
                var collinear=new Path64{new Point64(0,0),new Point64(500,0),new Point64(1000,0),new Point64(1000,1000),new Point64(0,1000)};
                var status=Clipper.Triangulate(new Paths64{collinear},out var result,true);Check(status==TriangulateResult.success&&TwiceArea(result)==2000000,"collinear-boundary target triangulates without area loss");
                bool knotRetained=result.Any(p=>p.Any(v=>v.X==500&&v.Y==0));
                Check(Hash(career)==before,"real career unchanged");
                File.WriteAllText(Path.Combine(output,"receipt.json"),"{\"status\":\"PASS_DEPENDENCY_ONLY\",\"checks\":"+checks+",\"rawCDTRetainedMandatoryCollinearKnot\":"+(knotRetained?"true":"false")+",\"requiresIndependentBoundaryRestorationAndAdmission\":true,\"normalTerrainActivated\":false}");
                Debug.Log("TERRAIN DEPENDENCY raw mandatory collinear knot retained="+knotRetained+"; caller must restore/check all authoritative knots");exit=0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}Debug.Log("TERRAIN DEPENDENCY "+(exit==0?"PASS ":"FAIL ")+checks);EditorApplication.Exit(exit);
        }
    }
}
