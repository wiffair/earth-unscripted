using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using RoadRush.Core;
using Newtonsoft.Json.Linq;
namespace RoadRush.RegionalDraft
{
    // DRAFT: deterministic transaction plan, never edits source DTOs or active world.
    // Decisions use actual polygon intersection areas, never point/centroid deletion.
    public static class RegionalBuildingMerge
    {
        public enum Origin { Country,Regional }
        public readonly struct Point
        {public readonly double X,Y,Z;public Point(double x,double y,double z){X=x;Y=y;Z=z;}}
        public sealed class Feature
        {
            public readonly string Id,SourceKey,Kind,Roof;public readonly double Height;public readonly bool MeasuredHeight;public readonly Origin Source;public readonly IReadOnlyList<Point> Points;
            internal readonly JObject Original;
            public Feature(string id,string source,string kind,string roof,double height,bool measured,Origin origin,IEnumerable<Point> points,JObject original=null)
            {Id=id;SourceKey=source;Kind=kind;Roof=roof;Height=height;MeasuredHeight=measured;Source=origin;Points=new ReadOnlyCollection<Point>(points.ToList());Original=original==null?null:(JObject)original.DeepClone();}
            public bool Important=>Kind=="important_building";
        }
        public sealed class Policy
        {
            public double ExactSnapMetres=.01,BaselineCoverageRequired=.95,ImportantMutualCoverageRequired=.98,AmbiguousOverlapFraction=.15;
            public int MaximumFeatures=32768,MaximumCandidatePairs=250000,MaximumIndexEntries=2000000;public long MaximumTrianglePairs=2000000;
        }
        public sealed class Decision
        {
            public readonly string RemovedSource,RetainedSource,Reason;public readonly double RemovedArea,IntersectionArea,RemovedCoverage,RetainedCoverage;
            internal Decision(Shape removed,Shape retained,string reason,double overlap){RemovedSource=removed.Feature.SourceKey;RetainedSource=retained.Feature.SourceKey;Reason=reason;RemovedArea=removed.Area;IntersectionArea=overlap;RemovedCoverage=Math.Min(1,overlap/removed.Area);RetainedCoverage=Math.Min(1,overlap/retained.Area);}
        }
        public sealed class Overlap
        {
            public readonly string A,B,Outcome;public readonly double Area,FractionA,FractionB;
            internal Overlap(Shape a,Shape b,double area,string outcome){A=a.Feature.SourceKey;B=b.Feature.SourceKey;Area=area;FractionA=area/a.Area;FractionB=area/b.Area;Outcome=outcome;}
        }
        public sealed class Plan
        {
            public readonly IReadOnlyList<Feature> Features;public readonly IReadOnlyList<Decision> Coalescence;public readonly IReadOnlyList<Overlap> Overlaps;
            public readonly int BaselineInput,RegionalInput,CandidatePairs,IndexEntries;public readonly long TrianglePairs;public bool NeedsOverlapReview=>Overlaps.Any(o=>o.Outcome=="RetainedAmbiguous");
            internal Plan(List<Feature> features,List<Decision> decisions,List<Overlap> overlaps,int baseline,int regional,Work work){Features=new ReadOnlyCollection<Feature>(features);Coalescence=new ReadOnlyCollection<Decision>(decisions);Overlaps=new ReadOnlyCollection<Overlap>(overlaps);BaselineInput=baseline;RegionalInput=regional;CandidatePairs=work.Pairs;TrianglePairs=work.TrianglePairs;IndexEntries=work.IndexEntries;}
        }
        internal sealed class Shape
        {
            public Feature Feature;public int Key;public double Area,West,South,East,North;public Point[][] Triangles;public bool Removed;
        }
        internal sealed class Work
        {
            public readonly Policy Policy;public int Pairs,IndexEntries;public long TrianglePairs;readonly Dictionary<long,double> cached=new Dictionary<long,double>();
            public Work(Policy p){Policy=p;}
            public double Intersection(Shape a,Shape b)
            {
                long key=((long)Math.Min(a.Key,b.Key)<<32)|(uint)Math.Max(a.Key,b.Key);if(cached.TryGetValue(key,out double prior))return prior;
                Require(++Pairs<=Policy.MaximumCandidatePairs,"Complete merge candidate-pair work budget exceeded");
                double area=0;if(a.West<=b.East&&a.East>=b.West&&a.South<=b.North&&a.North>=b.South)
                foreach(var ta in a.Triangles)foreach(var tb in b.Triangles)
                {Require(++TrianglePairs<=Policy.MaximumTrianglePairs,"Complete merge triangle work budget exceeded");area+=TriangleIntersection(ta,tb);}
                Require(area<=Math.Min(a.Area,b.Area)+Math.Max(.0001,Math.Min(a.Area,b.Area)*.00001),"Polygon area calculation exceeded source area");area=Math.Min(area,Math.Min(a.Area,b.Area));cached.Add(key,area);return area;
            }
        }
        sealed class Spatial
        {
            const double Cell=32;readonly Dictionary<(int x,int z),List<Shape>> cells=new Dictionary<(int,int),List<Shape>>();
            public Spatial(IEnumerable<Shape> shapes,Work work)
            {foreach(var shape in shapes)for(int z=(int)Math.Floor(shape.South/Cell);z<=(int)Math.Floor(shape.North/Cell);z++)for(int x=(int)Math.Floor(shape.West/Cell);x<=(int)Math.Floor(shape.East/Cell);x++){Require(++work.IndexEntries<=work.Policy.MaximumIndexEntries,"Complete merge spatial-index work budget exceeded");if(!cells.TryGetValue((x,z),out var list))cells[(x,z)]=list=new List<Shape>();list.Add(shape);}}
            public IEnumerable<Shape> Query(Shape s)
            {var seen=new HashSet<int>();for(int z=(int)Math.Floor(s.South/Cell);z<=(int)Math.Floor(s.North/Cell);z++)for(int x=(int)Math.Floor(s.West/Cell);x<=(int)Math.Floor(s.East/Cell);x++)if(cells.TryGetValue((x,z),out var list))foreach(var item in list)if(!item.Removed&&seen.Add(item.Key)&&item.West<=s.East&&item.East>=s.West&&item.South<=s.North&&item.North>=s.South)yield return item;}
        }
        public static Plan Prepare(IEnumerable<Feature> baseline,IEnumerable<Feature> regional,Policy policy,CancellationToken cancellation=default)
        {
            Require(policy!=null&&policy.ExactSnapMetres>0&&policy.ExactSnapMetres<=.02&&policy.BaselineCoverageRequired>=.90&&policy.BaselineCoverageRequired<=1&&policy.ImportantMutualCoverageRequired>=.98&&policy.ImportantMutualCoverageRequired<=1&&policy.AmbiguousOverlapFraction>0&&policy.AmbiguousOverlapFraction<=.5,"Unreviewed merge policy bounds");
            var country=baseline.ToArray();var detail=regional.ToArray();Require(country.All(f=>f.Source==Origin.Country)&&detail.All(f=>f.Source==Origin.Regional),"Merge origin labels do not match input domains");Require(country.Length+detail.Length<=policy.MaximumFeatures,"Merge feature budget exceeded");var all=country.Concat(detail).OrderBy(f=>f.SourceKey,StringComparer.Ordinal).ToArray();Require(all.Select(f=>f.SourceKey).Distinct(StringComparer.Ordinal).Count()==all.Length,"Duplicate source key before merge");
            Require(all.Sum(f=>(long)f.Points.Count)<=400000&&all.Sum(f=>(long)f.Points.Count*f.Points.Count)<=40000000,"Aggregate merge polygon work budget exceeded");
            var shapes=new List<Shape>();for(int i=0;i<all.Length;i++){cancellation.ThrowIfCancellationRequested();shapes.Add(PrepareShape(all[i],i));}
            var decisions=new List<Decision>();var overlaps=new List<Overlap>();var work=new Work(policy);
            foreach(var group in shapes.GroupBy(s=>ExactKey(s.Feature.Points,policy.ExactSnapMetres),StringComparer.Ordinal))
            {
                var ordered=group.OrderByDescending(s=>s.Feature.Important).ThenByDescending(s=>s.Feature.MeasuredHeight).ThenByDescending(s=>s.Feature.Source==Origin.Regional).ThenBy(s=>s.Feature.SourceKey,StringComparer.Ordinal).ToArray();
                for(int i=1;i<ordered.Length;i++){double overlap=work.Intersection(ordered[i],ordered[0]);if(overlap/ordered[i].Area<.98||overlap/ordered[0].Area<.98)continue;ordered[i].Removed=true;decisions.Add(new Decision(ordered[i],ordered[0],"Coincident1cmQuantizedRing",overlap));}
            }
            var important=shapes.Where(s=>!s.Removed&&s.Feature.Source==Origin.Regional&&s.Feature.Important).ToArray();var importantIndex=new Spatial(important,work);
            foreach(var ordinary in shapes.Where(s=>!s.Removed&&s.Feature.Source==Origin.Regional&&!s.Feature.Important))
            {
                cancellation.ThrowIfCancellationRequested();Shape winner=null;double best=0;
                foreach(var candidate in importantIndex.Query(ordinary))
                {double area=work.Intersection(ordinary,candidate);if(area/ordinary.Area+1e-9<policy.ImportantMutualCoverageRequired||area/candidate.Area+1e-9<policy.ImportantMutualCoverageRequired)continue;if(winner==null||area>best+1e-6||(Math.Abs(area-best)<=1e-6&&StringComparer.Ordinal.Compare(candidate.Feature.SourceKey,winner.Feature.SourceKey)<0)){winner=candidate;best=area;}}
                if(winner!=null){ordinary.Removed=true;decisions.Add(new Decision(ordinary,winner,"ImportantMutual98Percent",best));}
            }
            var regionalShapes=shapes.Where(s=>!s.Removed&&s.Feature.Source==Origin.Regional).ToArray();var regionalIndex=new Spatial(regionalShapes,work);
            foreach(var shape in regionalShapes)
            {
                cancellation.ThrowIfCancellationRequested();foreach(var other in regionalIndex.Query(shape))if(other.Key>shape.Key)
                {double area=work.Intersection(shape,other);if(area<.000001)continue;bool ambiguous=area/Math.Min(shape.Area,other.Area)>=policy.AmbiguousOverlapFraction;overlaps.Add(new Overlap(shape,other,area,ambiguous?"RetainedAmbiguous":"RetainedMinorOverlap"));}
            }
            foreach(var original in shapes.Where(s=>!s.Removed&&s.Feature.Source==Origin.Country))
            {
                cancellation.ThrowIfCancellationRequested();var candidates=regionalIndex.Query(original).Select(s=>(shape:s,area:work.Intersection(original,s))).Where(p=>p.area>.000001).ToArray();
                var winner=candidates.Where(p=>p.area/original.Area+1e-9>=policy.BaselineCoverageRequired).OrderByDescending(p=>p.shape.Feature.Important).ThenByDescending(p=>p.area).ThenBy(p=>p.shape.Feature.SourceKey,StringComparer.Ordinal).FirstOrDefault();
                if(winner.shape!=null){original.Removed=true;decisions.Add(new Decision(original,winner.shape,"BaselineCovered95Percent",winner.area));}
                foreach(var pair in candidates)
                {bool ambiguous=pair.area/Math.Min(original.Area,pair.shape.Area)>=policy.AmbiguousOverlapFraction;overlaps.Add(new Overlap(original,pair.shape,pair.area,winner.shape!=null?"BaselineCoalescedTo:"+winner.shape.Feature.SourceKey:ambiguous?"RetainedAmbiguous":"RetainedMinorOverlap"));}
            }
            var output=shapes.Where(s=>!s.Removed).Select(s=>s.Feature).OrderBy(f=>f.Id,StringComparer.Ordinal).ThenBy(f=>f.SourceKey,StringComparer.Ordinal).ToList();Require(output.Select(f=>f.Id).Distinct(StringComparer.Ordinal).Count()==output.Count,"Conflicting output IDs; no automatic source rename");Require(output.Count+decisions.Count==all.Length,"Every removed source must have an explicit provenance decision");
            return new Plan(output,decisions,overlaps,country.Length,detail.Length,work);
        }
        public static IReadOnlyList<Overlap> AuditPhysicalFootprints(IEnumerable<Feature> features,Policy policy,CancellationToken cancellation=default)
        {
            var all=features.OrderBy(f=>f.SourceKey,StringComparer.Ordinal).ToArray();Require(all.Length<=policy.MaximumFeatures&&all.Sum(f=>(long)f.Points.Count)<=400000&&all.Sum(f=>(long)f.Points.Count*f.Points.Count)<=40000000,"Physical audit complete-work budget exceeded");
            var shapes=all.Select((f,i)=>PrepareShape(f,i)).ToArray();var work=new Work(policy);var index=new Spatial(shapes,work);var result=new List<Overlap>();
            foreach(var shape in shapes){cancellation.ThrowIfCancellationRequested();foreach(var other in index.Query(shape))if(other.Key>shape.Key){double area=work.Intersection(shape,other);if(area>.000001)result.Add(new Overlap(shape,other,area,"PhysicalConflict"));}}
            return result.AsReadOnly();
        }
        static Shape PrepareShape(Feature feature,int key)
        {
            Require(feature!=null&&!string.IsNullOrWhiteSpace(feature.Id)&&!string.IsNullOrWhiteSpace(feature.SourceKey)&&feature.Points.Count>=4&&feature.Points.Count<=4096,"Invalid merge source feature");
            foreach(var p in feature.Points)Require(Finite(p.X)&&Finite(p.Y)&&Finite(p.Z)&&Math.Abs(p.X)<100000&&Math.Abs(p.Z)<100000,"Invalid merge coordinate");
            Require(DistanceSquared(feature.Points[0],feature.Points[feature.Points.Count-1])<.000001,"Unclosed merge ring");var triangles=Triangulate(feature.Points);double area=triangles.Sum(t=>Math.Abs(Cross(t[0],t[1],t[2]))*.5);Require(area>.00001,"Degenerate merge source area");
            return new Shape{Feature=feature,Key=key,Area=area,Triangles=triangles.ToArray(),West=feature.Points.Min(p=>p.X),East=feature.Points.Max(p=>p.X),South=feature.Points.Min(p=>p.Z),North=feature.Points.Max(p=>p.Z)};
        }
        static string ExactKey(IReadOnlyList<Point> points,double snap)
        {
            var pairs=points.Take(points.Count-1).Select(p=>(x:(long)Math.Round(p.X/snap,MidpointRounding.AwayFromZero),z:(long)Math.Round(p.Z/snap,MidpointRounding.AwayFromZero))).ToArray();
            var cleaned=new List<(long x,long z)>();foreach(var p in pairs)if(cleaned.Count==0||cleaned[cleaned.Count-1]!=p)cleaned.Add(p);if(cleaned.Count>1&&cleaned[0]==cleaned[cleaned.Count-1])cleaned.RemoveAt(cleaned.Count-1);
            bool changed=true;while(changed&&cleaned.Count>3){changed=false;for(int i=0;i<cleaned.Count;i++){var a=cleaned[(i+cleaned.Count-1)%cleaned.Count];var b=cleaned[i];var c=cleaned[(i+1)%cleaned.Count];if((b.x-a.x)*(c.z-b.z)-(b.z-a.z)*(c.x-b.x)==0&&(b.x-a.x)*(c.x-b.x)+(b.z-a.z)*(c.z-b.z)>=0){cleaned.RemoveAt(i);changed=true;break;}}}
            Require(cleaned.Count>=3,"Degenerate quantized source ring");
            string Minimum(List<(long x,long z)> values)
            {
                int n=values.Count,i=0,j=1,k=0;
                while(i<n&&j<n&&k<n){var a=values[(i+k)%n];var b=values[(j+k)%n];int compare=a.x!=b.x?a.x.CompareTo(b.x):a.z.CompareTo(b.z);if(compare==0){k++;continue;}if(compare>0){i+=k+1;if(i==j)i++;}else{j+=k+1;if(i==j)j++;}k=0;}
                int first=Math.Min(i,j);return string.Join(";",Enumerable.Range(0,n).Select(offset=>{var p=values[(first+offset)%n];return p.x.ToString(CultureInfo.InvariantCulture)+","+p.z.ToString(CultureInfo.InvariantCulture);}));
            }
            string forward=Minimum(cleaned);cleaned.Reverse();string reverse=Minimum(cleaned);return StringComparer.Ordinal.Compare(forward,reverse)<=0?forward:reverse;
        }
        static List<Point[]> Triangulate(IReadOnlyList<Point> source)
        {
            var ring=new List<Point>();for(int i=0;i<source.Count-1;i++)if(ring.Count==0||DistanceSquared(ring[ring.Count-1],source[i])>1e-12)ring.Add(source[i]);
            bool changed=true;while(changed&&ring.Count>3){changed=false;for(int i=0;i<ring.Count;i++)if(Math.Abs(Cross(ring[(i+ring.Count-1)%ring.Count],ring[i],ring[(i+1)%ring.Count]))<1e-8){ring.RemoveAt(i);changed=true;break;}}
            double area=0;for(int i=0;i<ring.Count;i++)area+=ring[i].X*ring[(i+1)%ring.Count].Z-ring[(i+1)%ring.Count].X*ring[i].Z;if(area<0)ring.Reverse();
            var result=new List<Point[]>();int guard=0;while(ring.Count>3)
            {
                bool found=false;for(int i=0;i<ring.Count;i++)
                {var a=ring[(i+ring.Count-1)%ring.Count];var b=ring[i];var c=ring[(i+1)%ring.Count];if(Cross(a,b,c)<=1e-10)continue;bool blocked=false;for(int j=0;j<ring.Count;j++){if(j==i||j==(i+1)%ring.Count||j==(i+ring.Count-1)%ring.Count)continue;var p=ring[j];if(Cross(a,b,p)>=-1e-10&&Cross(b,c,p)>=-1e-10&&Cross(c,a,p)>=-1e-10){blocked=true;break;}}if(blocked)continue;result.Add(new[]{a,b,c});ring.RemoveAt(i);found=true;break;}
                Require(found&&++guard<=source.Count,"Unsupported self-touching/crossing source ring; no merge mutation");
            }
            Require(ring.Count==3,"Incomplete triangulation");result.Add(ring.ToArray());return result;
        }
        static double TriangleIntersection(Point[] a,Point[] b)
        {
            var polygon=new List<Point>(a);for(int edge=0;edge<3&&polygon.Count>0;edge++)
            {var p=b[edge];var q=b[(edge+1)%3];var clipped=new List<Point>();var previous=polygon[polygon.Count-1];double last=Cross(p,q,previous);foreach(var next in polygon){double current=Cross(p,q,next);bool inside=current>=-1e-10,was=last>=-1e-10;if(inside!=was){double t=last/(last-current);clipped.Add(new Point(previous.X+(next.X-previous.X)*t,0,previous.Z+(next.Z-previous.Z)*t));}if(inside)clipped.Add(next);previous=next;last=current;}polygon=clipped;}
            if(polygon.Count<3)return 0;double area=0;for(int i=0;i<polygon.Count;i++)area+=(polygon[i].X-polygon[0].X)*(polygon[(i+1)%polygon.Count].Z-polygon[0].Z)-(polygon[(i+1)%polygon.Count].X-polygon[0].X)*(polygon[i].Z-polygon[0].Z);return Math.Abs(area)*.5;
        }
        static double DistanceSquared(Point a,Point b)=>(a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z);
        static double Cross(Point a,Point b,Point c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
        static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        static void Require(bool valid,string why){if(!valid)throw new InvalidDataException(why);}
    }
}
