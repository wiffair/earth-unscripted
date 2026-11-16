using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class BuildingRoofVerification
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception("BUILDING ROOF FAIL: "+text);checks++;Debug.Log("BUILDING ROOF PASS: "+text);}
        static Vector3[] Loop(params float[] xz){var list=new List<Vector3>();for(int i=0;i<xz.Length;i+=2)list.Add(new Vector3(xz[i],-99,xz[i+1]));list.Add(list[0]);return list.ToArray();}
        static float Area(Vector3[] p){float a=0;for(int i=0;i<p.Length-1;i++)a+=p[i].x*p[i+1].z-p[i+1].x*p[i].z;return Mathf.Abs(a)*.5f;}
        static bool Finite(float x)=>!float.IsInfinity(x)&&!float.IsNaN(x);
        static void Verify(Vector3[] outline,bool pitched,string kind,string name)
        {
            var data=BuildingRoofGeometry.Create(outline,5,kind);Check(data.Pitched==pitched,name+" uses its honest pitched or flat fallback interpretation");
            Check(data.Vertices.Length==data.UV.Length&&data.GableVertices.Length==data.GableUV.Length&&data.UV.Concat(data.GableUV).All(v=>Finite(v.x)&&Finite(v.y)),name+" UV channels are complete and finite");
            bool roof=true,tile=true;float projected=0;
            for(int i=0;i<data.Triangles.Length;i+=3)
            {
                int a=data.Triangles[i],b=data.Triangles[i+1],c=data.Triangles[i+2];var normal=Vector3.Cross(data.Vertices[b]-data.Vertices[a],data.Vertices[c]-data.Vertices[a]);roof&=normal.y>0&&normal.sqrMagnitude>1e-12f;projected+=normal.y*.5f;
                foreach(var pair in new[]{new[]{a,b},new[]{b,c},new[]{c,a}})tile&=Mathf.Abs(Vector3.Distance(data.Vertices[pair[0]],data.Vertices[pair[1]])-Vector2.Distance(data.UV[pair[0]],data.UV[pair[1]])*3)<.001f;
            }
            Check(roof&&Mathf.Abs(projected-Area(outline))<Mathf.Max(.001f,Area(outline)*.00001f),name+" upward nondegenerate roof triangles cover the exact footprint projected area");Check(tile,name+" slate UV distance is an actual3m surface tile on every triangle edge");
            bool gables=true;for(int i=0;i<data.GableTriangles.Length;i+=3)
            {
                var a=data.GableVertices[data.GableTriangles[i]];var b=data.GableVertices[data.GableTriangles[i+1]];var c=data.GableVertices[data.GableTriangles[i+2]];var normal=Vector3.Cross(b-a,c-a);var outside=(a+b+c)/3+normal.normalized*.01f;
                gables&=normal.sqrMagnitude>1e-12f&&Mathf.Abs(normal.y)<.0001f&&!GeoRegion.Contains(outline,new Vector2(outside.x,outside.z));
            }
            Check(gables,name+" gable triangles are nondegenerate vertical outward faces in either outline winding");
            Check(data.Vertices.All(p=>p.y>=4.99999f&&p.y<=8.00001f)&&data.GableVertices.All(p=>p.y>=4.99999f&&p.y<=8.00001f),name+" roof and endwalls remain inside eave to bounded3m-rise envelope");
            if(!pitched)Check(!string.IsNullOrEmpty(data.FallbackReason)&&data.GableTriangles.Length==0&&data.Vertices.All(p=>p.y==5),name+" fallback is explicitly labelled and invents no pitched or hipped roof");
        }
        public static void Run()
        {
            try
            {
                var rectangle=Loop(0,0,20,0,20,8,0,8);var reverse=rectangle.Reverse().ToArray();
                Verify(rectangle,true,"gabled","Rectangle CCW");Verify(reverse,true,"gabled","Rectangle CW");
                Verify(Loop(0,0,20,0,23,5,20,10,0,10),true,"gabled","Convex asymmetric pentagon");Verify(Loop(0,0,30,0,0,8),true,"gabled","Convex triangle");
                Verify(Loop(0,0,12,0,12,4,4,4,4,12,0,12),false,"gabled","Concave outline");Verify(Loop(0,0,40,0,40,25,0,25),false,"gabled","Wide outline");Verify(Loop(0,0,100,0,100,40,0,40),false,"gabled","Large outline");Verify(rectangle,false,"hipped","Unsupported hipped style");Verify(rectangle,false,"flat","Flat style");
                var roof=BuildingRoofGeometry.Create(rectangle,5,"gabled");Check(roof.Vertices.Length==8&&roof.Triangles.Length==12&&roof.GableTriangles.Length==12,"rectangle uses two exact planar quadrilaterals and four clean gable triangles");
                var ridge=roof.Vertices.Select((p,i)=>new{p,uv=roof.UV[i]}).Where(v=>Mathf.Abs(v.p.y-7.2f)<.0001f).ToArray();Check(ridge.Length==4&&ridge.All(v=>Mathf.Abs(v.p.z-4)<.0001f&&Mathf.Abs(v.uv.y)<.0001f)&&ridge.GroupBy(v=>v.p).Count()==2,"two roof halves share both exact ridge endpoints and zero course UV at the ridge");
                Check(Mathf.Abs(roof.UV.Max(v=>v.x)-roof.UV.Min(v=>v.x)-20f/3)<.0001f&&Mathf.Abs(roof.UV.Max(v=>v.y)-4*Mathf.Sqrt(1+.55f*.55f)/3)<.0001f,"ridge-axis and slope-axis tile scale are3m with courses descending both roof faces");
                bool rejected=false;try{BuildingRoofGeometry.Create(new[]{Vector3.zero,Vector3.right,Vector3.forward,Vector3.one},5,"gabled");}catch(ArgumentException){rejected=true;}Check(rejected,"unclosed input is rejected before roof generation");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/building-roof.txt",$"PASS {checks} actual roof clipping mesh topology projected area outward winding finite UV and physical tile checks\nNo native pixel/performance acceptance claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
