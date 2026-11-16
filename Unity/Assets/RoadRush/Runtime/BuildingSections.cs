using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    // Fictional level sections, not surveyed dwelling/parcel boundaries. The source
    // footprint and canonical world are never modified. Boundary vertices quantise
    // to1mm solely to cancel shared triangulation edges deterministically.
    public static class BuildingSections
    {
        public const int MaximumStrips=24,MaximumParts=48;
        const float StripWidth=10,MinimumSpan=15;
        public sealed class Part
        {
            public string SourceId;public int StripIndex,PartIndex;public int SecondaryStripIndex=-1;
            public GeoBuilding Building;public BuildingSitePlan.Data Site;
        }
        public sealed class Data
        {
            public Part[] Parts=Array.Empty<Part>();public Vector3 MajorAxis;
            public bool Sectioned;public string FallbackReason;
            public bool HasRoofBurialRisk;public int StripCount,EdgeWork;
        }
        readonly struct Key:IEquatable<Key>,IComparable<Key>
        {
            public readonly long X,Z;
            public Key(Vector3 p){X=(long)Math.Round((double)p.x*1000,MidpointRounding.AwayFromZero);Z=(long)Math.Round((double)p.z*1000,MidpointRounding.AwayFromZero);}
            public Vector3 Point=>new Vector3((float)(X*.001),0,(float)(Z*.001));
            public bool Equals(Key b)=>X==b.X&&Z==b.Z;
            public override bool Equals(object obj)=>obj is Key b&&Equals(b);
            public override int GetHashCode(){unchecked{return X.GetHashCode()*397^Z.GetHashCode();}}
            public int CompareTo(Key b){int x=X.CompareTo(b.X);return x!=0?x:Z.CompareTo(b.Z);}
        }
        readonly struct Edge:IEquatable<Edge>
        {
            public readonly Key A,B;public Edge(Key a,Key b){A=a;B=b;}
            public bool Equals(Edge b)=>A.Equals(b.A)&&B.Equals(b.B);
            public override bool Equals(object obj)=>obj is Edge b&&Equals(b);
            public override int GetHashCode(){unchecked{return A.GetHashCode()*397^B.GetHashCode();}}
        }
        public static Data Create(GeoRegion region,GeoBuilding source,Func<float,float,float> surface)=>CreateCore(region,source,surface,false);
        public static Data CreateScoped(GeoRegion region,GeoBuilding source,Func<float,float,float> surface,Func<Vector3,GeoRegion.RoadProjection> roadQuery)=>CreateRecipe(region,source,surface,false,Vector3.zero,StripWidth,true,"site",roadQuery);
        // Private order reversal is exercised through reflection by the geometry test;
        // it does not add an alternative production geometry or change the recipe.
        static Data CreateCore(GeoRegion region,GeoBuilding source,Func<float,float,float> surface,bool reverseTriangles)
            =>CreateRecipe(region,source,surface,reverseTriangles,Vector3.zero,StripWidth,true,"site");
        static Data CreateRecipe(GeoRegion region,GeoBuilding source,Func<float,float,float> surface,bool reverseTriangles,Vector3 axisHint,float stripWidth,bool refine,string suffix,Func<Vector3,GeoRegion.RoadProjection> roadQuery=null)
        {
            var result=new Data();
            try
            {
                if(region==null||source==null||surface==null)throw new ArgumentException("Missing section input");
                var p=Canonical(source.points);int n=p.Length-1;double area=Math.Abs(Area(p));
                var centre=Vector3.zero;for(int i=0;i<n;i++)centre+=p[i];centre/=n;
                double xx=0,zz=0,xz=0;for(int i=0;i<n;i++){double x=p[i].x-centre.x,z=p[i].z-centre.z;xx+=x*x;zz+=z*z;xz+=x*z;}
                double angle=.5*Math.Atan2(2*xz,xx-zz);var axis=new Vector3((float)Math.Cos(angle),0,(float)Math.Sin(angle));
                if(axisHint.sqrMagnitude>.5f)axis=axisHint.normalized;
                if(axis.x<0||(Math.Abs(axis.x)<.000001f&&axis.z<0))axis=-axis;result.MajorAxis=axis;
                double low=double.PositiveInfinity,high=double.NegativeInfinity;for(int i=0;i<n;i++){double value=Dot(p[i],axis);low=Math.Min(low,value);high=Math.Max(high,value);}
                double span=high-low;if(span<=(refine?MinimumSpan:stripWidth))throw new InvalidOperationException("Not a long-row outline; retain original level building");
                int strips=(int)Math.Ceiling(span/stripWidth);if(strips>MaximumStrips)throw new InvalidOperationException("Long row exceeds24-strip budget");
                var top=p.Take(n).ToArray();var triangles=GeoWorld.Triangulate(top);if(triangles.Length<3)throw new InvalidOperationException("Footprint triangulation failed");
                double triangulated=0;for(int i=0;i<triangles.Length;i+=3)triangulated+=Math.Abs(Cross(top[triangles[i]],top[triangles[i+1]],top[triangles[i+2]]))*.5;
                if(Math.Abs(triangulated-area)>Math.Max(.001,area*.00001))throw new InvalidOperationException("Triangulation does not preserve the complete source outline");
                var parts=new List<Part>();int work=0;
                for(int strip=0;strip<strips;strip++)
                {
                    double a=low+span*strip/strips,b=low+span*(strip+1)/strips;var edges=new HashSet<Edge>();
                    for(int step=0;step<triangles.Length;step+=3)
                    {
                        int t=reverseTriangles?triangles.Length-step-3:step;var polygon=new List<Vector3>{top[triangles[t]],top[triangles[t+1]],top[triangles[t+2]]};
                        if(Cross(polygon[0],polygon[1],polygon[2])<0)polygon.Reverse();polygon=Clip(Clip(polygon,axis,a,true),axis,b,false);
                        if(polygon.Count<3)continue;
                        var keys=new List<Key>();foreach(var point in polygon){var key=new Key(point);if(keys.Count==0||!keys[keys.Count-1].Equals(key))keys.Add(key);}if(keys.Count>1&&keys[0].Equals(keys[keys.Count-1]))keys.RemoveAt(keys.Count-1);
                        if(keys.Count<3||Math.Abs(Area(keys))<.0000001)continue;
                        for(int i=0;i<keys.Count;i++)
                        {
                            if(++work>32768)throw new InvalidOperationException("Section edge-work budget exceeded");
                            var from=keys[i];var to=keys[(i+1)%keys.Count];if(from.Equals(to))continue;
                            var edge=new Edge(from,to);if(edges.Remove(new Edge(to,from)))continue;
                            if(!edges.Add(edge))throw new InvalidOperationException("Ambiguous duplicate directed edge");
                        }
                    }
                    var loops=Loops(edges);int index=0;
                    foreach(var loop in loops)
                    {
                        if(parts.Count>=MaximumParts)throw new InvalidOperationException("Section part budget exceeded");
                        var footprint=loop.Select(k=>k.Point).Concat(new[]{loop[0].Point}).ToArray();
                        var building=new GeoBuilding{id=source.id+"/"+suffix+"-"+strip+"-"+index,kind=source.kind,height=source.height,roof=source.roof,measuredHeight=source.measuredHeight,points=footprint};
                        parts.Add(new Part{SourceId=source.id,StripIndex=strip,PartIndex=index++,Building=building,Site=BuildingSitePlan.Create(region,building,surface,roadQuery)});
                    }
                }
                result.HasRoofBurialRisk=parts.Any(part=>part.Site.RoofBurialRisk);int totalStrips=strips,totalWork=work;
                if(refine&&result.HasRoofBurialRisk)
                {
                    var final=new List<Part>();var minor=new Vector3(-axis.z,0,axis.x);
                    foreach(var part in parts)
                    {
                        if(!part.Site.RoofBurialRisk){final.Add(part);continue;}
                        // One bounded secondary contour pass only. The returned top-level
                        // ridge hint stays the ORIGINAL row axis, even on these pieces.
                        var contour=CreateRecipe(region,part.Building,surface,reverseTriangles,minor,5,false,"contour",roadQuery);
                        if(!contour.Sectioned||contour.HasRoofBurialRisk)throw new InvalidOperationException("Secondary contour cannot safely resolve sampled roof risk: "+(contour.FallbackReason??part.Building.id));
                        totalStrips+=contour.StripCount;totalWork+=contour.EdgeWork;
                        if(totalStrips>MaximumStrips||totalWork>32768)throw new InvalidOperationException("Secondary contour exceeds shared strip/work budget");
                        foreach(var derived in contour.Parts)
                        {derived.SourceId=source.id;derived.SecondaryStripIndex=derived.StripIndex;derived.StripIndex=part.StripIndex;final.Add(derived);}
                        if(final.Count>MaximumParts)throw new InvalidOperationException("Secondary contour exceeds shared48-part budget");
                    }
                    parts=final;
                }
                if(parts.Count>MaximumParts)throw new InvalidOperationException("Combined section part budget exceeded");
                double sum=parts.Sum(part=>Math.Abs(Area(part.Building.points))),perimeter=0;for(int i=0;i<n;i++)perimeter+=Vector3.Distance(p[i],p[i+1]);
                if(parts.Count<2||Math.Abs(sum-area)>Math.Max(.02,perimeter*.002))throw new InvalidOperationException("Quantised sections failed the footprint union-area tolerance");
                foreach(var part in parts)for(int i=0;i<part.Building.points.Length-1;i++)
                {var q=part.Building.points[i];var middle=(q+part.Building.points[i+1])*.5f;if(!InsideBudget(q,p)||!InsideBudget(middle,p))throw new InvalidOperationException("Derived section leaves the source footprint tolerance");}
                result.Parts=parts.ToArray();result.Sectioned=true;result.StripCount=totalStrips;result.EdgeWork=totalWork;result.HasRoofBurialRisk=parts.Any(part=>part.Site.RoofBurialRisk);return result;
            }
            catch(ArgumentException e){result.FallbackReason=e.Message;return result;}
            catch(InvalidOperationException e){result.FallbackReason=e.Message;return result;}
        }
        static Vector3[] Canonical(Vector3[] input)
        {
            int n=input==null?0:input.Length-1;if(n<3||n>256)throw new ArgumentException("Invalid bounded source outline");
            for(int i=0;i<=n;i++)if(!Finite(input[i].x)||!Finite(input[i].z)||Math.Abs(input[i].x)>1000000||Math.Abs(input[i].z)>1000000)throw new ArgumentException("Invalid section coordinates");
            if((new Vector2(input[0].x,input[0].z)-new Vector2(input[n].x,input[n].z)).sqrMagnitude>1e-8f)throw new ArgumentException("Source outline is not closed");
            double area=Area(input);if(Math.Abs(area)<.00001)throw new ArgumentException("Degenerate source outline");
            int first=0;for(int i=1;i<n;i++)if(input[i].x<input[first].x||(input[i].x==input[first].x&&input[i].z<input[first].z))first=i;
            var result=new Vector3[n+1];int sign=area>0?1:-1;for(int i=0;i<n;i++){var q=input[(first+sign*i+n)%n];q.y=0;result[i]=q;}result[n]=result[0];return result;
        }
        static List<Vector3> Clip(List<Vector3> polygon,Vector3 axis,double boundary,bool above)
        {
            var result=new List<Vector3>();for(int i=0;i<polygon.Count;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];double da=Dot(a,axis)-boundary,db=Dot(b,axis)-boundary;bool ia=above?da>=0:da<=0,ib=above?db>=0:db<=0;
                if(ia)result.Add(a);
                if(ia!=ib){double t=da/(da-db);result.Add(new Vector3((float)(a.x+((double)b.x-a.x)*t),0,(float)(a.z+((double)b.z-a.z)*t)));}
            }
            return result;
        }
        static List<List<Key>> Loops(HashSet<Edge> edges)
        {
            var next=new Dictionary<Key,Key>();var incoming=new HashSet<Key>();
            foreach(var edge in edges)
            {if(next.ContainsKey(edge.A)||!incoming.Add(edge.B))throw new InvalidOperationException("Ambiguous section boundary graph");next.Add(edge.A,edge.B);}
            if(next.Count!=incoming.Count||next.Keys.Any(k=>!incoming.Contains(k)))throw new InvalidOperationException("Open section boundary graph");
            var result=new List<List<Key>>();var remaining=new SortedSet<Key>(next.Keys);
            while(remaining.Count>0)
            {
                Key start=remaining.Min,point=start;var loop=new List<Key>();
                do{if(!remaining.Remove(point))throw new InvalidOperationException("Section loops intersect ambiguously");loop.Add(point);point=next[point];if(loop.Count>32768)throw new InvalidOperationException("Boundary loop budget exceeded");}while(!point.Equals(start));
                bool changed=true;while(changed&&loop.Count>3)
                {changed=false;for(int i=0;i<loop.Count;i++){var a=loop[(i+loop.Count-1)%loop.Count];var b=loop[i];var c=loop[(i+1)%loop.Count];if(OnChord(a,b,c)){loop.RemoveAt(i);changed=true;break;}}}
                if(loop.Count<3||loop.Count>256||Area(loop)<=.0000001)throw new InvalidOperationException("Invalid or holed section boundary; preserve original outline");
                // Removing collinear points can remove the original minimum key.
                int minimum=0;for(int i=1;i<loop.Count;i++)if(loop[i].CompareTo(loop[minimum])<0)minimum=i;
                result.Add(loop.Skip(minimum).Concat(loop.Take(minimum)).ToList());
            }
            result.Sort((a,b)=>a[0].CompareTo(b[0]));return result;
        }
        static bool InsideBudget(Vector3 p,Vector3[] outline)
        {
            if(GeoRegion.Contains(outline,new Vector2(p.x,p.z)))return true;
            for(int i=0;i<outline.Length-1;i++)
            {var a=outline[i];var d=outline[i+1]-a;float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude):0;if((p-(a+d*t)).sqrMagnitude<=.000004f)return true;}
            return false;
        }
        static bool OnChord(Key a,Key b,Key c)
        {
            double dx=c.X-a.X,dz=c.Z-a.Z,bx=b.X-a.X,bz=b.Z-a.Z,length=dx*dx+dz*dz;
            if(length<1)return false;double t=(bx*dx+bz*dz)/length,cross=dx*bz-dz*bx;
            return t>=0&&t<=1&&cross*cross<=length; // one quantised unit =1mm
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static double Dot(Vector3 p,Vector3 axis)=>(double)p.x*axis.x+(double)p.z*axis.z;
        static double Cross(Vector3 a,Vector3 b,Vector3 c)=>((double)b.x-a.x)*(c.z-a.z)-((double)b.z-a.z)*(c.x-a.x);
        static double Area(Vector3[] p){double a=0;for(int i=0;i<p.Length-1;i++)a+=(double)p[i].x*p[i+1].z-(double)p[i+1].x*p[i].z;return a*.5;}
        static double Area(List<Key> p){double a=0;for(int i=0;i<p.Count;i++){var b=p[(i+1)%p.Count];a+=(double)p[i].X*b.Z-(double)b.X*p[i].Z;}return a*.0000005;}
    }
}
