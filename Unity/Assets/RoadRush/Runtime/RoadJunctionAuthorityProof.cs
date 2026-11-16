using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace RoadRush
{
    // Pure bounded geometry/interval proof. No Mesh, physics, texture or scene API.
    // Structural/source DTO validators run first in WorldSnapshotCodec.
    // Conservative rejection is intentional; finite samples are never authority.
    public static class RoadJunctionAuthorityProof
    {
        const int MaximumWork=20000000,MaximumIndexEntries=2000000;
        const double Cell=80,EnvelopeMargin=.02,MaximumGrade=1;
        struct P {internal double x,z;internal P(double x,double z){this.x=x;this.z=z;}public static P operator+(P a,P b)=>new P(a.x+b.x,a.z+b.z);public static P operator-(P a,P b)=>new P(a.x-b.x,a.z-b.z);public static P operator*(P a,double b)=>new P(a.x*b,a.z*b);}
        sealed class Context
        {internal GeoRoad Road;internal RoadElevationProfileDto Profile;internal RoadWidthProfileDto Width;internal RoadElevationMetric Metric;internal RoadMarkingPlan.RoadFrame Frame;internal RoadElevationHorizontalFrame Adapter;internal bool Bevel;}
        sealed class Influence {internal RoadJunctionPatchDto Patch;internal RoadJunctionArmDto Arm;internal RoadElevationArmPath Path;}
        sealed class Record {internal Context C;internal double A,B,Half;internal P[] Hull;}
        sealed class Budget {int work;internal void Spend(int count=1){if((work+=count)>MaximumWork)throw new InvalidDataException("Road authority geometry proof work budget");}}
        public static void Validate(IReadOnlyList<RoadJunctionPatchDto> patches,IReadOnlyList<GeoRoad> roads,IReadOnlyList<RoadElevationProfileDto> profiles,IReadOnlyList<RoadWidthProfileDto> widths,RoadContinuationAuthority continuations=null)
        {
            if(continuations==null)throw new InvalidDataException("Road authority proof requires explicit validated source/continuation registry");
            var budget=new Budget();var widthById=widths?.ToDictionary(p=>p.roadId,StringComparer.Ordinal)??new Dictionary<string,RoadWidthProfileDto>(StringComparer.Ordinal);EffectiveRoadWidthField field=widths==null?null:new EffectiveRoadWidthField(widths.ToArray(),roads.ToArray());
            var frames=RoadMarkingPlan.BuildFrames(roads,(r,s)=>field==null?r.width:field.At(r,s));var profileById=profiles.ToDictionary(p=>p.roadId,StringComparer.Ordinal);var contexts=new Dictionary<string,Context>(StringComparer.Ordinal);var index=new Dictionary<Tuple<int,int>,List<Record>>();int entries=0;
            var curves=roads.ToDictionary(r=>r.id,r=>new RoadElevationCurve(r,profileById[r.id]),StringComparer.Ordinal);var armsByRoad=new Dictionary<string,List<Influence>>(StringComparer.Ordinal);var byPatch=new Dictionary<string,List<Influence>>(StringComparer.Ordinal);
            foreach(var patch in patches)
            {
                var bindings=new List<Influence>();byPatch.Add(patch.id,bindings);
                foreach(var arm in patch.arms)
                {
                    RoadJunctionSpanRules.Validate(arm,continuations);var influence=new Influence{Patch=patch,Arm=arm,Path=new RoadElevationArmPath(arm,curves)};bindings.Add(influence);
                    for(int i=0;i<influence.Path.SpanCount;i++){string id=influence.Path.GetSpan(i).RoadId;if(!armsByRoad.TryGetValue(id,out var values))armsByRoad.Add(id,values=new List<Influence>());values.Add(influence);}
                }
            }
            foreach(var road in roads)
            {
                budget.Spend();var metric=new RoadElevationMetric(road);widthById.TryGetValue(road.id,out var width);frames.Frames.TryGetValue(road.id,out var frame);var c=new Context{Road=road,Metric=metric,Profile=profileById[road.id],Width=width,Frame=frame,Adapter=frame==null?null:new RoadElevationHorizontalFrame(metric,frame),Bevel=width?.bevelBoundary??false};contexts.Add(road.id,c);
                if(frame==null){if(!road.tunnel)Fail("missing exact road frame",road.id);continue;}
                var armControls=armsByRoad.TryGetValue(road.id,out var controls)?controls.ToArray():Array.Empty<Influence>();var cuts=new SortedSet<double>();for(int i=0;i<metric.PointCount;i++)cuts.Add(metric.SourceAtPoint(i));foreach(double s in c.Profile.distances)cuts.Add(Math.Max(0,Math.Min(metric.SourceLength,s)));if(width!=null)foreach(float s in width.distances)cuts.Add(Math.Max(0,Math.Min(metric.SourceLength,s)));
                foreach(var a in armControls)
                {
                    for(int j=0;j<a.Path.SpanCount;j++){var span=a.Path.GetSpan(j);if(span.RoadId!=road.id)continue;cuts.Add(span.From);cuts.Add(span.To);}
                    a.Path.SourceAtRawOffset(a.Arm.coreLength,out string coreRoad,out double coreS);if(coreRoad==road.id)cuts.Add(coreS);
                }
                var ordered=cuts.ToArray();for(int i=1;i<ordered.Length;i++)
                {
                    double a=ordered[i-1],b=ordered[i];if(b-a<.000001)continue;budget.Spend();var record=Make(c,a,b,budget);budget.Spend(armControls.Length);var active=armControls.Where(x=>x.Path.TryLocate(road.id,(a+b)*.5,out _,out _)).ToArray();if(active.Length>1)Fail("overlapping actual collar influence",road.id);ProveGrades(record,active.Length==0?null:active[0],budget);
                    if(patches.Count==0)continue;
                    double minX=record.Hull.Min(v=>v.x),maxX=record.Hull.Max(v=>v.x),minZ=record.Hull.Min(v=>v.z),maxZ=record.Hull.Max(v=>v.z);int x0=(int)Math.Floor(minX/Cell),x1=(int)Math.Floor(maxX/Cell),z0=(int)Math.Floor(minZ/Cell),z1=(int)Math.Floor(maxZ/Cell);
                    for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++){if(++entries>MaximumIndexEntries)Fail("footprint index budget",road.id);var key=Tuple.Create(x,z);if(!index.TryGetValue(key,out var list))index.Add(key,list=new List<Record>());list.Add(record);}
                }
            }
            foreach(var patch in patches)
            {
                budget.Spend();var bindings=byPatch[patch.id];var namedRoads=new HashSet<string>(bindings.SelectMany(x=>Enumerable.Range(0,x.Path.SpanCount).Select(i=>x.Path.GetSpan(i).RoadId)),StringComparer.Ordinal);foreach(string id in namedRoads)if(contexts[id].Frame==null)Fail("portal/tunnel geometry proof not implemented",id);
                var participatingLayers=new HashSet<int>(namedRoads.Select(id=>contexts[id].Road.layer));bool participatingBridge=namedRoads.Any(id=>contexts[id].Road.bridge);
                var polygon=patch.boundary.Take(patch.boundary.Length-1).Select(v=>new P(v.x,v.y)).ToArray();int x0=(int)Math.Floor(polygon.Min(v=>v.x)/Cell),x1=(int)Math.Floor(polygon.Max(v=>v.x)/Cell),z0=(int)Math.Floor(polygon.Min(v=>v.z)/Cell),z1=(int)Math.Floor(polygon.Max(v=>v.z)/Cell);var visited=new HashSet<Record>();
                for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)if(index.TryGetValue(Tuple.Create(x,z),out var list))foreach(var record in list)
                {
                    budget.Spend(patch.arms.Length+1);if(!visited.Add(record))continue;bool named=namedRoads.Contains(record.C.Road.id);if(!named&&(!participatingLayers.Contains(record.C.Road.layer)||record.C.Road.bridge&&!participatingBridge||record.C.Road.tunnel))continue;
                    if(Separated(record.Hull,polygon,budget))continue;double mid=(record.A+record.B)*.5;bool covered=bindings.Any(a=>a.Path.TryLocate(record.C.Road.id,mid,out double outwardH,out _)&&outwardH<=a.Path.CoreHorizontal+1e-8);
                    if(!covered)Fail("core intersects a row outside explicit core authority",record.C.Road.id+" / "+patch.id+" / "+record.A.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        static Record Make(Context c,double a,double b,Budget budget)
        {
            Frame(c,a,false,out var ca,out var ra);Frame(c,b,true,out var cb,out var rb);double half=HalfWidth(c,a,b,budget);var hull=Hull(new[]{ca+ra*(half+EnvelopeMargin),ca-ra*(half+EnvelopeMargin),cb+rb*(half+EnvelopeMargin),cb-rb*(half+EnvelopeMargin)});if(hull.Length<3)Fail("degenerate physical strip",c.Road.id);return new Record{C=c,A=a,B=b,Half=half,Hull=hull};
        }
        static void ProveGrades(Record r,Influence active,Budget budget)
        {
            var c=r.C;double ha=c.Metric.ToHorizontal(r.A),hb=c.Metric.ToHorizontal(r.B),span=hb-ha;if(span<.0000001)Fail("horizontal proof interval",c.Road.id);Frame(c,r.A,false,out var pa,out var ra);Frame(c,r.B,true,out var pb,out var rb);c.Adapter.At(r.A,out _,out _,out var dc,out var dr,false,c.Bevel);P cp=new P(dc.x,dc.z),rp=new P(dr.x,dr.z);double rn=Math.Max(Norm(ra),Norm(rb)),rmin=MinimumNorm(ra,rb),b=r.Half;if(rmin<.1)Fail("degenerate lateral frame",c.Road.id);
            double[] det={Cross(cp+rp*b,ra),Cross(cp-rp*b,ra),Cross(cp+rp*b,rb),Cross(cp-rp*b,rb)};if(det.Min()<=.000001&&det.Max()>=-.000001)Fail("folded road strip Jacobian",c.Road.id);double dmin=det.Min(v=>Math.Abs(v));if(dmin<.1)Fail("singular road strip Jacobian",c.Road.id);
            double amax=Math.Max(Norm(cp+rp*b),Norm(cp-rp*b));int i=Interval(c.Profile.distances,(r.A+r.B)*.5);double h0=c.Metric.ToHorizontal(c.Profile.distances[i]),h1=c.Metric.ToHorizontal(c.Profile.distances[i+1]),h=h1-h0,t0=(ha-h0)/h,t1=(hb-h0)/h;var poly=RoadElevationProfileDto.CentrePolynomial(c.Profile,i,c.Metric);double zmax=MaxAbs(poly,t0,t1),zgrade=RoadElevationProfileDto.PeakDerivative(poly,t0,t1)/h;double bank=MaxSmooth(c.Profile.crossfalls[i],c.Profile.crossfalls[i+1],t0,t1),crown=MaxSmooth(c.Profile.crownRates[i],c.Profile.crownRates[i+1],t0,t1);double bd=SmoothDerivative(c.Profile.crossfalls[i+1]-c.Profile.crossfalls[i],t0,t1)/h,cd=SmoothDerivative(c.Profile.crownRates[i+1]-c.Profile.crownRates[i],t0,t1)/h,physical=b*rn,offset=RoadElevationProfileDto.CrownOffset(physical,c.Profile.crownSmooth),lateral=bank+crown;
            double yh=zgrade+bd*physical+cd*offset+lateral*b*Norm(rp),yu=lateral*rn,baseGradient=(yh*rn+yu*amax)/dmin;if(zmax+bank*physical+crown*offset>=20000)Fail("full-width height envelope",c.Road.id);
            double proven=baseGradient;
            if(active!=null)
            {
                var p=active.Patch;var arm=active.Arm;double core=active.Path.CoreHorizontal,end=active.Path.HorizontalLength;if(!active.Path.TryLocate(c.Road.id,(r.A+r.B)*.5,out double distance,out _))Fail("foreign span influence",c.Road.id);P g=new P(p.planeGradient.x,p.planeGradient.y);double gradient=Norm(g);
                foreach(var point in r.Hull)if(Math.Abs(p.centre.y+g.x*(point.x-p.centre.x)+g.z*(point.z-p.centre.z))>=20000)Fail("junction plane height envelope",c.Road.id);
                if(distance<=core+.0000001)proven=gradient;
                else
                {
                    if(end-core<.0001)Fail("collar horizontal span",c.Road.id);double planeAtA=p.centre.y+g.x*(pa.x-p.centre.x)+g.z*(pa.z-p.centre.z),planeSlope=Dot(g,cp);var difference=(double[])poly.Clone();difference[0]-=planeAtA-planeSlope*(ha-h0);difference[1]-=planeSlope*h;
                    double gap=MaxAbs(difference,t0,t1)+bank*physical+crown*offset+b*Math.Max(Math.Abs(Dot(g,ra)),Math.Abs(Dot(g,rb)));double alphaPeak=1.5/(end-core);proven=Math.Max(baseGradient,gradient)+alphaPeak*gap*rn/dmin;
                }
            }
            if(!RoadElevationProfileDto.Finite(proven)||proven>MaximumGrade+.000001)Fail("conservative full-width world-grade bound "+proven.ToString("R",System.Globalization.CultureInfo.InvariantCulture),c.Road.id+" @"+r.A.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        }
        static double HalfWidth(Context c,double a,double b,Budget budget)
        {
            if(c.Width==null)return c.Road.width*.5;var p=c.Width;double peak=0;for(int i=1;i<p.distances.Length;i++){budget.Spend();double lo=Math.Max(a,p.distances[i-1]),hi=Math.Min(b,p.distances[i]);if(hi<lo)continue;double h=p.distances[i]-p.distances[i-1],v0=p.widths[i-1],v1=p.widths[i],m0=p.derivatives[i-1],m1=p.derivatives[i];var poly=new[]{v0,h*m0,-3*v0+3*v1-h*(2*m0+m1),2*v0-2*v1+h*(m0+m1)};foreach(double t in RoadElevationProfileDto.Extrema(poly,(lo-p.distances[i-1])/h,(hi-p.distances[i-1])/h))peak=Math.Max(peak,RoadElevationProfileDto.Eval(poly,t));}if(peak<2||peak>80.00001)Fail("width proof envelope",c.Road.id);return peak*.5;
        }
        static void Frame(Context c,double s,bool previous,out P centre,out P right){c.Adapter.At(s,out var p,out var r,out _,out _,previous,c.Bevel);centre=new P(p.x,p.z);right=new P(r.x,r.z);}
        static bool Within(double x,double a,double b)=>x>=Math.Min(a,b)-.0000001&&x<=Math.Max(a,b)+.0000001;
        static int Interval(double[] stations,double s){int lo=0,hi=stations.Length-1;while(hi-lo>1){int m=(lo+hi)/2;if(stations[m]<=s)lo=m;else hi=m;}return lo;}
        static double MaxAbs(double[] p,double a,double b){double peak=0;foreach(double t in RoadElevationProfileDto.Extrema(p,a,b))peak=Math.Max(peak,Math.Abs(RoadElevationProfileDto.Eval(p,t)));return peak;}
        static double MaxSmooth(double a,double b,double lo,double hi){double At(double t)=>a+(b-a)*t*t*(3-2*t);return Math.Max(Math.Abs(At(lo)),Math.Abs(At(hi)));}
        static double SmoothDerivative(double delta,double lo,double hi){double At(double t)=>Math.Abs(delta*6*t*(1-t));double peak=Math.Max(At(lo),At(hi));if(lo<=.5&&hi>=.5)peak=Math.Max(peak,Math.Abs(delta)*1.5);return peak;}
        static double MinimumNorm(P a,P b){var d=b-a;double square=Dot(d,d);double t=square<=1e-20?0:Math.Max(0,Math.Min(1,-Dot(a,d)/square));return Norm(a+d*t);}
        static bool Separated(P[] a,P[] b,Budget budget)
        {foreach(var polygon in new[]{a,b})for(int i=0;i<polygon.Length;i++){budget.Spend();var edge=polygon[(i+1)%polygon.Length]-polygon[i];var n=new P(-edge.z,edge.x);double alo=a.Min(v=>Dot(v,n)),ahi=a.Max(v=>Dot(v,n)),blo=b.Min(v=>Dot(v,n)),bhi=b.Max(v=>Dot(v,n));if(ahi<blo-.0000001||bhi<alo-.0000001)return true;}return false;}
        static P[] Hull(P[] points)
        {
            var sorted=points.OrderBy(p=>p.x).ThenBy(p=>p.z).ToArray();var result=new List<P>();foreach(var p in sorted){while(result.Count>=2&&Cross(result[result.Count-1]-result[result.Count-2],p-result[result.Count-1])<=0)result.RemoveAt(result.Count-1);result.Add(p);}int lower=result.Count;for(int i=sorted.Length-2;i>=0;i--){var p=sorted[i];while(result.Count>lower&&Cross(result[result.Count-1]-result[result.Count-2],p-result[result.Count-1])<=0)result.RemoveAt(result.Count-1);result.Add(p);}if(result.Count>1)result.RemoveAt(result.Count-1);return result.ToArray();
        }
        static double Norm(P a)=>Math.Sqrt(Dot(a,a));static double Dot(P a,P b)=>a.x*b.x+a.z*b.z;static double Cross(P a,P b)=>a.x*b.z-a.z*b.x;
        static void Fail(string why,string id)=>throw new InvalidDataException("Road authority proof: "+why+" / "+id);
    }
}
