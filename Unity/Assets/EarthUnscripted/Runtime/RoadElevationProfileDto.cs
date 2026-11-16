using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    [Serializable] public sealed class RoadElevationProfileDto
    {
        public string roadId;
        public double[] distances;public float[] elevations,gradeDerivatives,crossfalls,crownRates;
        public float crownSmooth=.15f;
        public const int MaximumProfiles=10000,MaximumStations=512,MaximumTotalStations=80000;
        public const double MaximumGrade=1;public const float MaximumCrossfall=.15f,MaximumCrown=.05f;
        public static void Validate(IReadOnlyList<RoadElevationProfileDto> profiles,IReadOnlyList<GeoRoad> roads,IReadOnlyList<RoadWidthProfileDto> widths=null)
        {
            if(profiles==null||profiles.Count>MaximumProfiles||roads==null||roads.Count>MaximumProfiles)Fail("profile/road count");
            RoadSourceIdentity.Compute(roads);var byId=roads.ToDictionary(r=>r.id,StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);int total=0;
            if(profiles.Count!=roads.Count)Fail("complete road coverage");
            foreach(var p in profiles)
            {
                if(p==null||!RoadSourceIdentity.Identifier(p.roadId)||!seen.Add(p.roadId)||!byId.TryGetValue(p.roadId,out var r)||p.distances==null||p.distances.Length<2||p.distances.Length>MaximumStations||p.elevations==null||p.gradeDerivatives==null||p.crossfalls==null||p.crownRates==null||p.elevations.Length!=p.distances.Length||p.gradeDerivatives.Length!=p.distances.Length||p.crossfalls.Length!=p.distances.Length||p.crownRates.Length!=p.distances.Length)throw new InvalidDataException("Road elevation: profile shape/identity");
                if((total+=p.distances.Length)>MaximumTotalStations)Fail("total station budget");
                var metric=new RoadElevationMetric(r);double length=metric.SourceLength;if(length<.01||length>100000||p.distances[0]!=0||Math.Abs(p.distances[p.distances.Length-1]-length)>1e-8)Fail("source distance domain");
                if(!Finite(p.crownSmooth)||p.crownSmooth<.01f||p.crownSmooth>1)Fail("crown smoothing range");
                for(int i=0;i<p.distances.Length;i++)
                {
                    if(!Finite(p.distances[i])||!Finite(p.elevations[i])||Math.Abs(p.elevations[i])>=20000||!Finite(p.gradeDerivatives[i])||Math.Abs(p.gradeDerivatives[i])>1||!Finite(p.crossfalls[i])||Math.Abs(p.crossfalls[i])>MaximumCrossfall||!Finite(p.crownRates[i])||p.crownRates[i]<0||p.crownRates[i]>MaximumCrown||i>0&&p.distances[i]-p.distances[i-1]<.0001)Fail("control range/order");
                }
                double half=HalfWidth(r,widths);
                for(int i=1;i<p.distances.Length;i++)
                {
                    double h=metric.ToHorizontal(p.distances[i])-metric.ToHorizontal(p.distances[i-1]);if(h<.0001)Fail("horizontal station spacing");var polynomial=CentrePolynomial(p,i-1,metric);
                    foreach(double t in Extrema(polynomial))if(Math.Abs(Eval(polynomial,t))>=20000)Fail("interior elevation overshoot");
                    double extra=1.5/h*(Math.Abs(p.crossfalls[i]-p.crossfalls[i-1])*half+Math.Abs(p.crownRates[i]-p.crownRates[i-1])*CrownOffset(half,p.crownSmooth));
                    double peak=PeakDerivative(polynomial,0,1)/h;
                    if(peak+extra>MaximumGrade+.000001)Fail("full-width physical grade bound");
                }
            }
        }
        internal static double HalfWidth(GeoRoad road,IReadOnlyList<RoadWidthProfileDto> widths)
        {
            var p=widths?.FirstOrDefault(w=>w.roadId==road.id);double width=p==null?road.width:0;
            if(p!=null)for(int i=1;i<p.distances.Length;i++)
            {
                double h=p.distances[i]-p.distances[i-1],v0=p.widths[i-1],v1=p.widths[i],m0=p.derivatives[i-1],m1=p.derivatives[i];
                var c=new[]{v0,h*m0,-3*v0+3*v1-h*(2*m0+m1),2*v0-2*v1+h*(m0+m1)};
                foreach(double t in Extrema(c))width=Math.Max(width,Eval(c,t));
            }
            return width*.5;
        }
        internal static double[] CentrePolynomial(RoadElevationProfileDto p,int i,RoadElevationMetric metric)
        {double h=metric.ToHorizontal(p.distances[i+1])-metric.ToHorizontal(p.distances[i]),a=p.elevations[i],b=p.elevations[i+1],m=p.gradeDerivatives[i],n=p.gradeDerivatives[i+1];return new[]{a,h*m,-3*a+3*b-h*(2*m+n),2*a-2*b+h*(m+n)};}
        internal static double SourceLength(GeoRoad road){double sum=0;for(int i=1;i<road.points.Length;i++){var d=road.points[i]-road.points[i-1];sum+=Math.Sqrt((double)d.x*d.x+(double)d.y*d.y+(double)d.z*d.z);}return sum;}
        internal static double CrownOffset(double lateral,double smooth)=>Math.Sqrt(lateral*lateral+smooth*smooth)-smooth;
        internal static double Eval(double[] c,double t)=>((c[3]*t+c[2])*t+c[1])*t+c[0];
        internal static IEnumerable<double> Extrema(double[] c,double lo=0,double hi=1)
        {
            yield return lo;yield return hi;double a=3*c[3],b=2*c[2],d=c[1];
            if(Math.Abs(a)<1e-14){if(Math.Abs(b)>1e-14){double t=-d/b;if(t>lo&&t<hi)yield return t;}}
            else {double disc=b*b-4*a*d;if(disc>=0){double root=Math.Sqrt(disc);double t=(-b-root)/(2*a);if(t>lo&&t<hi)yield return t;t=(-b+root)/(2*a);if(t>lo&&t<hi)yield return t;}}
        }
        internal static double PeakDerivative(double[] c,double lo,double hi)
        {double At(double t)=>Math.Abs((3*c[3]*t+2*c[2])*t+c[1]);double peak=Math.Max(At(lo),At(hi));if(Math.Abs(c[3])>1e-14){double t=-c[2]/(3*c[3]);if(t>lo&&t<hi)peak=Math.Max(peak,At(t));}return peak;}
        internal static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        internal static void Fail(string reason)=>throw new InvalidDataException("Road elevation: "+reason);
    }
    [Serializable] public sealed class RoadJunctionArmDto
    {public string roadId;public double sourceDistance;public int direction;public float coreLength,collarLength;public RoadJunctionSpanDto[] spans=Array.Empty<RoadJunctionSpanDto>();}
    [Serializable] public sealed class RoadJunctionPatchDto
    {
        public string id,nodeId,kind,syntheticAnchorId;public int layer;public Vector3 centre;public Vector2[] boundary;public Vector2 planeGradient;public RoadJunctionArmDto[] arms;
        public const int MaximumPatches=10000,MaximumBoundary=65,MaximumArms=32,MaximumTotalBoundary=65536,MaximumTotalArms=65536;
        public static void Validate(IReadOnlyList<RoadJunctionPatchDto> patches,IReadOnlyList<GeoRoad> roads,IReadOnlyList<RoadElevationProfileDto> profiles,IReadOnlyList<RoadWidthProfileDto> widths=null,IReadOnlyList<RoadSyntheticAnchorDto> syntheticAnchors=null,RoadContinuationAuthority continuations=null)
        {
            if(patches==null||patches.Count>MaximumPatches)RoadElevationProfileDto.Fail("junction count");
            continuations=continuations??new RoadContinuationAuthority(roads,profiles,widths,syntheticAnchors,Array.Empty<RoadContinuationDto>());
            var byId=roads.ToDictionary(r=>r.id,StringComparer.Ordinal);var metrics=roads.ToDictionary(r=>r.id,r=>new RoadElevationMetric(r),StringComparer.Ordinal);var ids=new HashSet<string>(StringComparer.Ordinal);var intervals=new Dictionary<string,List<Tuple<double,double,string>>>(StringComparer.Ordinal);int boundaries=0,arms=0,spans=0;
            foreach(var p in patches)
            {
                if(p==null||!RoadSourceIdentity.Identifier(p.id)||!ids.Add(p.id)||!RoadSourceIdentity.Identifier(p.nodeId)||(p.kind!="at-grade"&&p.kind!="bridge-transition"&&p.kind!="tunnel-portal"&&p.kind!="synthetic-service"&&p.kind!="synthetic-pit")||p.layer < -20||p.layer>20||!Finite(p.centre.x)||!Finite(p.centre.y)||!Finite(p.centre.z)||Math.Abs(p.centre.y)>=20000||!Finite(p.planeGradient.x)||!Finite(p.planeGradient.y)||p.planeGradient.sqrMagnitude>1.000001||p.boundary==null||p.boundary.Length<4||p.boundary.Length>MaximumBoundary||p.arms==null||p.arms.Length<2||p.arms.Length>MaximumArms)RoadElevationProfileDto.Fail("junction shape/range");
                if((boundaries+=p.boundary.Length)>MaximumTotalBoundary||(arms+=p.arms.Length)>MaximumTotalArms)RoadElevationProfileDto.Fail("junction aggregate budget");
                if(p.boundary[0]!=p.boundary[p.boundary.Length-1])RoadElevationProfileDto.Fail("junction closure");double sign=0,area=0;
                for(int i=0;i<p.boundary.Length-1;i++)
                {
                    var a=p.boundary[i];var b=p.boundary[i+1];if(!Finite(a.x)||!Finite(a.y)||(a-new Vector2(p.centre.x,p.centre.z)).sqrMagnitude>40000||(b-a).sqrMagnitude<.00000001)RoadElevationProfileDto.Fail("junction boundary coordinate");
                    area+=(double)a.x*b.y-(double)b.x*a.y;var c=p.boundary[(i+2)%(p.boundary.Length-1)];double cross=Cross(b-a,c-b);if(Math.Abs(cross)>.00000001){if(sign==0)sign=Math.Sign(cross);else if(sign*cross<0)RoadElevationProfileDto.Fail("junction convexity");}
                }
                for(int i=0;i<p.boundary.Length-1;i++)for(int j=0;j<p.boundary.Length-1;j++)if(sign*Cross(p.boundary[i+1]-p.boundary[i],p.boundary[j]-p.boundary[i])<-.0001)RoadElevationProfileDto.Fail("junction nonconvex/self-intersecting boundary");
                if(Math.Abs(area)<.0001||sign==0)RoadElevationProfileDto.Fail("junction degenerate boundary");
                for(int i=0;i<p.boundary.Length-1;i++)if(sign*Cross(p.boundary[i+1]-p.boundary[i],new Vector2(p.centre.x,p.centre.z)-p.boundary[i])<-.0001)RoadElevationProfileDto.Fail("junction centre outside core");
                if(p.kind=="at-grade"&&continuations.Degree(p.nodeId)<2)RoadElevationProfileDto.Fail("ordinary patch needs a genuine source connection with at least two incidence directions");
                var armIds=new HashSet<string>(StringComparer.Ordinal);bool bridge=false,tunnel=false;var layers=new HashSet<int>();
                foreach(var a in p.arms)
                {
                    if(a==null||!byId.TryGetValue(a.roadId??"",out var road)||!Finite(a.sourceDistance)||!Finite(a.coreLength)||!Finite(a.collarLength)||(a.direction!=1&&a.direction!=-1)||a.coreLength<0||a.collarLength<=a.coreLength||a.collarLength>500)throw new InvalidDataException("Road elevation: junction arm shape/range");
                    double length=metrics[road.id].SourceLength;RoadJunctionSpanRules.Validate(a,continuations);if((spans+=a.spans.Length)>RoadJunctionSpanRules.MaximumTotalSpans)RoadElevationProfileDto.Fail("junction span aggregate budget");
                    if(a.sourceDistance<0||a.sourceDistance>length+1e-8||!armIds.Add(a.roadId+"|"+a.sourceDistance.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+a.direction))RoadElevationProfileDto.Fail("junction arm source domain/duplicate");
                    var metric=metrics[road.id];int sourceNodeIndex=RoadTopologyAuthority.NodeIndex(metric,a.sourceDistance);bool node=sourceNodeIndex>=0&&road.sourceNodeIds[sourceNodeIndex]==p.nodeId;
                    if(node){var q=road.points[sourceNodeIndex];if((new Vector2(q.x,q.z)-new Vector2(p.centre.x,p.centre.z)).sqrMagnitude>.0004)RoadElevationProfileDto.Fail("junction node coordinate mismatch");}
                    if(!node&&p.kind.StartsWith("synthetic-",StringComparison.Ordinal)&&syntheticAnchors!=null)
                    {
                        var anchor=syntheticAnchors.FirstOrDefault(x=>x.id==p.syntheticAnchorId);if(anchor!=null&&((a.roadId==anchor.parentRoadId&&Math.Abs(a.sourceDistance-anchor.parentSourceDistance)<=1e-8)||(a.roadId==anchor.connectorRoadId&&Math.Abs(a.sourceDistance-anchor.connectorSourceDistance)<=1e-8)))node=true;
                    }
                    if(!node)RoadElevationProfileDto.Fail("junction arm not a source node occurrence or explicit synthetic anchor");
                    bridge|=road.bridge;tunnel|=road.tunnel;layers.Add(road.layer);if((p.kind=="at-grade"||p.kind.StartsWith("synthetic-",StringComparison.Ordinal))&&(road.layer!=p.layer||road.bridge||road.tunnel))RoadElevationProfileDto.Fail("junction incompatible at-grade layer");
                    foreach(var span in a.spans){if(!intervals.TryGetValue(span.roadId,out var values))intervals.Add(span.roadId,values=new List<Tuple<double,double,string>>());values.Add(Tuple.Create(Math.Min(span.fromSourceDistance,span.toSourceDistance),Math.Max(span.fromSourceDistance,span.toSourceDistance),p.id));}
                }
                if(!layers.Contains(p.layer))RoadElevationProfileDto.Fail("junction layer is not a participating source layer");
                if(p.kind.StartsWith("synthetic-",StringComparison.Ordinal))
                {var anchor=syntheticAnchors?.FirstOrDefault(x=>x.id==p.syntheticAnchorId);if(anchor==null||p.kind!="synthetic-"+anchor.kind||!p.arms.Any(a=>a.roadId==anchor.parentRoadId)||!p.arms.Any(a=>a.roadId==anchor.connectorRoadId))RoadElevationProfileDto.Fail("synthetic junction declaration");}
                else if(!string.IsNullOrEmpty(p.syntheticAnchorId))RoadElevationProfileDto.Fail("ordinary junction has synthetic declaration");
                if(p.kind=="bridge-transition"&&!bridge||p.kind=="tunnel-portal"&&!tunnel)RoadElevationProfileDto.Fail("junction transition lacks source structure");
            }
            foreach(var values in intervals.Values){values.Sort((a,b)=>a.Item1.CompareTo(b.Item1));for(int i=1;i<values.Count;i++)if(values[i].Item1<values[i-1].Item2-1e-8)RoadElevationProfileDto.Fail("overlapping junction collars");}
        }
        static bool Finite(double x)=>RoadElevationProfileDto.Finite(x);
        static double Cross(Vector2 a,Vector2 b)=>(double)a.x*b.y-(double)a.y*b.x;
    }
}
