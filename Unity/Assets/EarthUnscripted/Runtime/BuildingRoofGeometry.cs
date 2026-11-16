using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    // Original architectural artwork from an approved outline, never a surveyed roof.
    // Convex gables use two exact planar halves; unsupported styles stay flat.
    public static class BuildingRoofGeometry
    {
        public sealed class Data
        {
            public Vector3[] Vertices,GableVertices;
            public int[] Triangles,GableTriangles;
            public Vector2[] UV,GableUV;
            public bool Pitched;public bool Omitted;
            public string FallbackReason;
        }
        public static Data Create(Vector3[] closedFootprint,float eaveY,string roofKind,Vector3 ridgeHint=default)
        {
            if(closedFootprint==null||closedFootprint.Length<4||closedFootprint.Length>4097||!Finite(eaveY))throw new ArgumentException("Invalid bounded roof footprint");
            var outline=new List<Vector3>();
            for(int i=0;i<closedFootprint.Length-1;i++)
            {
                var p=closedFootprint[i];if(!Finite(p.x)||!Finite(p.z))throw new ArgumentException("Nonfinite roof coordinates");
                p.y=eaveY;if(outline.Count==0||(p-outline[outline.Count-1]).sqrMagnitude>1e-10f)outline.Add(p);
            }
            var closing=closedFootprint[closedFootprint.Length-1];if(!Finite(closing.x)||!Finite(closing.z)||(new Vector2(closing.x,closing.z)-new Vector2(closedFootprint[0].x,closedFootprint[0].z)).sqrMagnitude>1e-8f)throw new ArgumentException("Roof footprint must be closed");
            if(outline.Count>1&&(outline[0]-outline[outline.Count-1]).sqrMagnitude<1e-10f)outline.RemoveAt(outline.Count-1);
            if(outline.Count<3)throw new ArgumentException("Degenerate roof footprint");
            double twiceArea=0;var centre=Vector3.zero;
            for(int i=0;i<outline.Count;i++){var a=outline[i];var b=outline[(i+1)%outline.Count];twiceArea+=(double)a.x*b.z-(double)b.x*a.z;centre+=a;}
            if(Math.Abs(twiceArea)<.0001)throw new ArgumentException("Zero-area roof footprint");centre/=outline.Count;
            if(outline.Count>256)return LargeFlat(outline,twiceArea);
            bool convex=true;float sign=twiceArea>0?1:-1;
            for(int i=0;i<outline.Count;i++)
            {var a=outline[i];var b=outline[(i+1)%outline.Count];var c=outline[(i+2)%outline.Count];if(((b.x-a.x)*(c.z-b.z)-(b.z-a.z)*(c.x-b.x))*sign<-.0001f){convex=false;break;}}
            if(convex)for(int i=0;i<outline.Count&&convex;i++)
            {var a=outline[i];var b=outline[(i+1)%outline.Count];foreach(var p in outline)if(((b.x-a.x)*(p.z-a.z)-(b.z-a.z)*(p.x-a.x))*sign<-.0001f){convex=false;break;}}
            float xx=0,zz=0,xz=0;foreach(var p in outline){float x=p.x-centre.x,z=p.z-centre.z;xx+=x*x;zz+=z*z;xz+=x*z;}
            float angle=.5f*Mathf.Atan2(2*xz,xx-zz);var major=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            if(!Finite(ridgeHint.x)||!Finite(ridgeHint.z))throw new ArgumentException("Nonfinite roof ridge hint");
            var hint=new Vector3(ridgeHint.x,0,ridgeHint.z);if(hint.sqrMagnitude>1e-8f)major=hint.normalized;
            var minor=new Vector3(-major.z,0,major.x);
            float half=0,min=float.MaxValue,max=float.MinValue;foreach(var p in outline){float distance=Vector3.Dot(p-centre,minor);half=Mathf.Max(half,Mathf.Abs(distance));min=Mathf.Min(min,distance);max=Mathf.Max(max,distance);}
            string fallback=roofKind!="gabled"?"Unsupported or flat roof style; flat artwork fallback":!convex?"Concave outline; flat artwork fallback":Math.Abs(twiceArea)*.5>=3000?"Large outline; flat artwork fallback":max-min>20?"Wide outline; flat artwork fallback":half<.001f?"Degenerate ridge width; flat artwork fallback":null;
            if(fallback!=null)return Flat(outline,fallback);
            float rise=Mathf.Min(3,half*.55f),slope=rise/half,slopeLength=Mathf.Sqrt(1+slope*slope);
            float Distance(Vector3 p)=>Vector3.Dot(p-centre,minor);
            Vector3 Ridge(Vector3 a,Vector3 b)
            {
                float da=Distance(a),db=Distance(b);var p=Vector3.LerpUnclamped(a,b,da/(da-db));p-=minor*Distance(p);p.y=eaveY;return p;
            }
            List<Vector3> Clip(bool positive)
            {
                var result=new List<Vector3>();
                for(int i=0;i<outline.Count;i++)
                {
                    var a=outline[i];var b=outline[(i+1)%outline.Count];float da=Distance(a),db=Distance(b);bool inside=positive?da>=0:da<=0,next=positive?db>=0:db<=0;
                    if(inside)Append(result,a);if(inside!=next)Append(result,Ridge(a,b));
                }
                if(result.Count>1&&(result[0]-result[result.Count-1]).sqrMagnitude<1e-10f)result.RemoveAt(result.Count-1);
                return result;
            }
            float Rise(Vector3 p)=>rise*Mathf.Clamp01(1-Mathf.Abs(Distance(p))/half);
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            foreach(bool positive in new[]{false,true})
            {
                var polygon=Clip(positive);if(polygon.Count<3)continue;var top=polygon.ToArray();
                for(int i=0;i<top.Length;i++){var p=top[i];p.y=eaveY+Rise(p);top[i]=p;}
                int start=vertices.Count;vertices.AddRange(top);
                foreach(var p in top)uv.Add(new Vector2(Vector3.Dot(p-centre,major)/3,Mathf.Abs(Distance(p))*slopeLength/3));
                AddTriangles(vertices,triangles,GeoWorld.Triangulate(top),start);
            }
            var gv=new List<Vector3>();var gt=new List<int>();var guv=new List<Vector2>();
            for(int edge=0;edge<outline.Count;edge++)
            {
                var a=outline[edge];var b=outline[(edge+1)%outline.Count];
                void Segment(Vector3 p,Vector3 q)
                {
                    float rp=Rise(p),rq=Rise(q);if(rp<.00001f&&rq<.00001f)return;
                    int start=gv.Count;float u=Vector3.Distance(a,p),v=Vector3.Distance(a,q);
                    gv.Add(p);gv.Add(q);guv.Add(new Vector2(u,eaveY));guv.Add(new Vector2(v,eaveY));
                    if(rp<.00001f)
                    {gv.Add(q+Vector3.up*rq);guv.Add(new Vector2(v,eaveY+rq));AddTriangles(gv,gt,sign>0?new[]{0,2,1}:new[]{0,1,2},start);}
                    else if(rq<.00001f)
                    {gv.Add(p+Vector3.up*rp);guv.Add(new Vector2(u,eaveY+rp));AddTriangles(gv,gt,sign>0?new[]{0,2,1}:new[]{0,1,2},start);}
                    else
                    {gv.Add(p+Vector3.up*rp);gv.Add(q+Vector3.up*rq);guv.Add(new Vector2(u,eaveY+rp));guv.Add(new Vector2(v,eaveY+rq));AddTriangles(gv,gt,sign>0?new[]{0,2,1,1,2,3}:new[]{0,1,2,1,3,2},start);}
                }
                if(Distance(a)*Distance(b)<0){var intersection=Ridge(a,b);Segment(a,intersection);Segment(intersection,b);}else Segment(a,b);
            }
            return new Data{Vertices=vertices.ToArray(),Triangles=triangles.ToArray(),UV=uv.ToArray(),GableVertices=gv.ToArray(),GableTriangles=gt.ToArray(),GableUV=guv.ToArray(),Pitched=true,FallbackReason=null};
        }
        static Data LargeFlat(List<Vector3> outline,double twiceArea)
        {
            double turn=0;bool proven=true;double sign=twiceArea>0?1:-1;
            for(int i=0;i<outline.Count;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Count];var c=outline[(i+2)%outline.Count];
                double ux=(double)b.x-a.x,uz=(double)b.z-a.z,vx=(double)c.x-b.x,vz=(double)c.z-b.z;
                double cross=ux*vz-uz*vx,dot=ux*vx+uz*vz;
                if(ux*ux+uz*uz<1e-12||vx*vx+vz*vz<1e-12){proven=false;break;}
                double angle=Math.Atan2(cross,dot);if(angle*sign<0){proven=false;break;}turn+=angle;
            }
            proven&=Math.Abs(turn*sign-2*Math.PI)<.0001;
            if(!proven)return new Data{Vertices=Array.Empty<Vector3>(),Triangles=Array.Empty<int>(),UV=Array.Empty<Vector2>(),GableVertices=Array.Empty<Vector3>(),GableTriangles=Array.Empty<int>(),GableUV=Array.Empty<Vector2>(),Pitched=false,Omitted=true,FallbackReason="Large outline not proven simple convex in linear work; roof omitted, exact physical wall footprint retained"};
            var vertices=outline.ToArray();var uv=new Vector2[vertices.Length];for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(vertices[i].x/3,vertices[i].z/3);var triangles=new List<int>();
            for(int i=1;i<vertices.Length-1;i++)
            {
                int a=0,b=twiceArea>0?i+1:i,c=twiceArea>0?i:i+1;
                if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<=1e-12f)continue;triangles.Add(a);triangles.Add(b);triangles.Add(c);
            }
            return new Data{Vertices=vertices,Triangles=triangles.ToArray(),UV=uv,GableVertices=Array.Empty<Vector3>(),GableTriangles=Array.Empty<int>(),GableUV=Array.Empty<Vector2>(),Pitched=false,FallbackReason="Large proven-convex outline; bounded linear flat-roof artwork fallback"};
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static void Append(List<Vector3> list,Vector3 p){if(list.Count==0||(p-list[list.Count-1]).sqrMagnitude>1e-10f)list.Add(p);}
        static void AddTriangles(List<Vector3> vertices,List<int> target,int[] local,int start)
        {
            for(int i=0;i<local.Length;i+=3)
            {
                int a=local[i]+start,b=local[i+1]+start,c=local[i+2]+start;
                if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<=1e-12f)continue;
                target.Add(a);target.Add(b);target.Add(c);
            }
        }
        static Data Flat(List<Vector3> outline,string reason)
        {
            var vertices=outline.ToArray();var uv=new Vector2[vertices.Length];for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(vertices[i].x/3,vertices[i].z/3);
            var triangles=new List<int>();AddTriangles(outline,triangles,GeoWorld.Triangulate(vertices),0);
            return new Data{Vertices=vertices,Triangles=triangles.ToArray(),UV=uv,GableVertices=Array.Empty<Vector3>(),GableTriangles=Array.Empty<int>(),GableUV=Array.Empty<Vector2>(),Pitched=false,FallbackReason=reason};
        }
    }
}
