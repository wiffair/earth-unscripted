using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace EarthUnscripted
{
    // Validates serialized continuation claims. This performs no graph discovery
    // for arms, no profile fitting and no source mutation.
    public sealed class RoadContinuationAuthority
    {
        public const double StationEpsilon=1e-8,ValueEpsilon=1e-6;
        sealed class RoadData {internal GeoRoad Road;internal RoadElevationMetric Metric;internal RoadElevationProfileDto Profile;internal RoadWidthProfileDto Width;internal RoadElevationHorizontalFrame Frame;}
        sealed class Node {internal int Degree;internal List<Tuple<string,int,double>> Occurrences=new List<Tuple<string,int,double>>();}
        readonly Dictionary<string,RoadData> roads=new Dictionary<string,RoadData>(StringComparer.Ordinal);
        readonly Dictionary<string,Node> nodes=new Dictionary<string,Node>(StringComparer.Ordinal);
        readonly Dictionary<string,List<double>> branches=new Dictionary<string,List<double>>(StringComparer.Ordinal);
        readonly HashSet<string> pairs=new HashSet<string>(StringComparer.Ordinal),provenNodes=new HashSet<string>(StringComparer.Ordinal),blockedEnds=new HashSet<string>(StringComparer.Ordinal);
        public RoadContinuationAuthority(IReadOnlyList<GeoRoad> source,IReadOnlyList<RoadElevationProfileDto> profiles,IReadOnlyList<RoadWidthProfileDto> widths,IReadOnlyList<RoadSyntheticAnchorDto> anchors,IReadOnlyList<RoadContinuationDto> declarations)
        {
            if(source==null||source.Count>10000||profiles==null||declarations==null||declarations.Count>RoadContinuationDto.MaximumContinuations)Fail("count budget");
            var controls=profiles.ToDictionary(p=>p.roadId,StringComparer.Ordinal);var widthControls=widths?.ToDictionary(p=>p.roadId,StringComparer.Ordinal)??new Dictionary<string,RoadWidthProfileDto>(StringComparer.Ordinal);EffectiveRoadWidthField wf=widths==null?null:new EffectiveRoadWidthField(widths.ToArray(),source.ToArray());var frames=RoadMarkingPlan.BuildFrames(source,(r,s)=>wf==null?r.width:wf.At(r,s));int points=0;
            foreach(var road in source)
            {
                if((points+=road.points.Length)>300000)Fail("source point budget");var metric=new RoadElevationMetric(road);frames.Frames.TryGetValue(road.id,out var frame);widthControls.TryGetValue(road.id,out var width);roads.Add(road.id,new RoadData{Road=road,Metric=metric,Profile=controls[road.id],Width=width,Frame=frame==null?null:new RoadElevationHorizontalFrame(metric,frame)});branches.Add(road.id,new List<double>());
                for(int i=0;i<road.points.Length;i++){string id=road.sourceNodeIds[i];if(!nodes.TryGetValue(id,out var n))nodes.Add(id,n=new Node());n.Degree+=(i>0?1:0)+(i+1<road.points.Length?1:0);n.Occurrences.Add(Tuple.Create(road.id,i,metric.SourceAtPoint(i)));}
            }
            foreach(var n in nodes.Values)if(n.Degree>2)foreach(var o in n.Occurrences)branches[o.Item1].Add(o.Item3);
            foreach(var a in anchors??Array.Empty<RoadSyntheticAnchorDto>())
            {
                AddSyntheticBranch(a.parentRoadId,a.parentSourceDistance);AddSyntheticBranch(a.connectorRoadId,a.connectorSourceDistance);
            }
            foreach(var values in branches.Values)values.Sort();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var d in declarations)
            {
                if(d==null||!RoadSourceIdentity.Identifier(d.id)||!ids.Add(d.id)||!RoadSourceIdentity.Identifier(d.nodeId)||!roads.TryGetValue(d.roadA??"",out var a)||!roads.TryGetValue(d.roadB??"",out var b)||d.roadA==d.roadB&&d.atEndA==d.atEndB)throw new InvalidDataException("Road continuation identity/endpoints");
                if(!nodes.TryGetValue(d.nodeId,out var node)||node.Degree!=2||node.Occurrences.Count!=2||EndNode(a,d.atEndA)!=d.nodeId||EndNode(b,d.atEndB)!=d.nodeId||blockedEnds.Contains(EndKey(d.roadA,d.atEndA))||blockedEnds.Contains(EndKey(d.roadB,d.atEndB)))Fail("not a proven degree2 source endpoint");
                if(a.Road.layer!=b.Road.layer||a.Road.bridge!=b.Road.bridge||a.Road.tunnel!=b.Road.tunnel)Fail("incompatible source layer/structure");
                var pa=a.Metric.PointAt(d.atEndA?a.Metric.PointCount-1:0);var pb=b.Metric.PointAt(d.atEndB?b.Metric.PointCount-1:0);if(Math.Abs((double)pa.x-pb.x)>StationEpsilon||Math.Abs((double)pa.z-pb.z)>StationEpsilon)Fail("source XZ mismatch");
                // Raw Y is retained and committed independently; derived height is
                // the physical repair authority and must agree at this boundary.
                ProveSeam(a,d.atEndA,b,d.atEndB);
                if(!pairs.Add(PairKey(d.roadA,d.atEndA,d.roadB,d.atEndB))||!provenNodes.Add(d.nodeId))Fail("duplicate continuation authority");
            }
        }
        void AddSyntheticBranch(string id,double station)
        {if(!roads.TryGetValue(id??"",out var r))Fail("unknown synthetic branch road");branches[id].Add(station);if(Math.Abs(station)<=StationEpsilon)blockedEnds.Add(EndKey(id,false));if(Math.Abs(station-r.Metric.SourceLength)<=StationEpsilon)blockedEnds.Add(EndKey(id,true));}
        void ProveSeam(RoadData a,bool endA,RoadData b,bool endB)
        {
            if(a.Frame==null||b.Frame==null)Fail("tunnel continuation needs explicit horizontal recipe");int da=endA?1:-1,db=endB?-1:1;double sa=endA?a.Metric.SourceLength:0,sb=endB?b.Metric.SourceLength:0;a.Frame.At(sa,out var pa,out var ra,out var ca,out var rpa,endA,a.Width?.bevelBoundary??false);b.Frame.At(sb,out var pb,out var rb,out var cb,out var rpb,endB,b.Width?.bevelBoundary??false);
            if(!NearXZ(pa,pb,StationEpsilon)||!NearXZ(ra*da,rb*db,ValueEpsilon))Fail("incompatible endpoint row frame");int ia=endA?a.Profile.elevations.Length-1:0,ib=endB?b.Profile.elevations.Length-1:0;Near(a.Profile.elevations[ia],b.Profile.elevations[ib],"derived height");Near(a.Profile.gradeDerivatives[ia]*da,b.Profile.gradeDerivatives[ib]*db,"oriented Hgrade");Near(a.Profile.crossfalls[ia]*da,b.Profile.crossfalls[ib]*db,"oriented bank");Near(a.Profile.crownRates[ia],b.Profile.crownRates[ib],"crown");Near(a.Profile.crownSmooth,b.Profile.crownSmooth,"crown smoothing");
            WidthEnd(a,endA,out double wa,out double wga);WidthEnd(b,endB,out double wb,out double wgb);Near(wa,wb,"effective width");Near(wga*da,wgb*db,"oriented physical width derivative");
            // Equal row map derivatives prove all-lateral C1 algebraically. A
            // truly flat end field has zero world gradient independent of turn.
            bool flat=a.Profile.gradeDerivatives[ia]==0&&b.Profile.gradeDerivatives[ib]==0&&a.Profile.crossfalls[ia]==0&&b.Profile.crossfalls[ib]==0&&a.Profile.crownRates[ia]==0&&b.Profile.crownRates[ib]==0;
            if(!flat&&(!NearXZ(ca*da,cb*db,ValueEpsilon)||!NearXZ(rpa,rpb,ValueEpsilon)))Fail("full row derivative seam is not proved");
        }
        static void WidthEnd(RoadData r,bool end,out double width,out double grade)
        {
            if(r.Width==null){width=r.Road.width;grade=0;return;}int i=end?r.Width.widths.Length-1:0;width=r.Width.widths[i];int k=end?r.Metric.PointCount-2:0;double ds=r.Metric.SourceAtPoint(k+1)-r.Metric.SourceAtPoint(k),dh=r.Metric.HorizontalAtPoint(k+1)-r.Metric.HorizontalAtPoint(k);grade=r.Width.derivatives[i]*ds/dh;
        }
        static string EndNode(RoadData r,bool end)=>r.Road.sourceNodeIds[end?r.Road.sourceNodeIds.Length-1:0];
        public int Degree(string id)=>id!=null&&nodes.TryGetValue(id,out var n)?n.Degree:0;
        public bool IsProvenNode(string id)=>id!=null&&provenNodes.Contains(id);
        public bool Joins(string roadA,bool atEndA,string roadB,bool atEndB)=>pairs.Contains(PairKey(roadA,atEndA,roadB,atEndB));
        public RoadElevationMetric Metric(string id){if(!roads.TryGetValue(id??"",out var r))Fail("unknown span road");return r.Metric;}
        public bool HasBranchBetween(string id,double from,double to)
        {if(!branches.TryGetValue(id??"",out var values))Fail("unknown branch road");double lo=Math.Min(from,to),hi=Math.Max(from,to);int a=0,b=values.Count;while(a<b){int m=(a+b)/2;if(values[m]<=lo+StationEpsilon)a=m+1;else b=m;}return a<values.Count&&values[a]<hi-StationEpsilon;}
        static bool NearXZ(Vector3 a,Vector3 b,double tolerance)=>Math.Abs((double)a.x-b.x)<=tolerance&&Math.Abs((double)a.z-b.z)<=tolerance;
        static void Near(double a,double b,string what){if(!RoadElevationProfileDto.Finite(a)||!RoadElevationProfileDto.Finite(b)||Math.Abs(a-b)>ValueEpsilon)Fail("incompatible "+what);}
        static string EndKey(string id,bool end)=>(id??"").Length+":"+id+(end?"#1":"#0");
        static string PairKey(string a,bool ae,string b,bool be){string x=EndKey(a,ae),y=EndKey(b,be);return StringComparer.Ordinal.Compare(x,y)<=0?x+"|"+y:y+"|"+x;}
        static void Fail(string message)=>throw new InvalidDataException("Road continuation: "+message);
    }
    public static class RoadJunctionSpanRules
    {
        public const int MaximumSpans=64,MaximumTotalSpans=262144;
        public static void Validate(RoadJunctionArmDto arm,RoadContinuationAuthority authority)
        {
            if(arm==null||authority==null||arm.spans==null||arm.spans.Length<1||arm.spans.Length>MaximumSpans)Fail("ordered span count");double total=0;var seen=new HashSet<string>(StringComparer.Ordinal);RoadJunctionSpanDto previous=null;
            for(int i=0;i<arm.spans.Length;i++)
            {
                var span=arm.spans[i];if(span==null||!RoadSourceIdentity.Identifier(span.roadId)||!seen.Add(span.roadId)||!RoadElevationProfileDto.Finite(span.fromSourceDistance)||!RoadElevationProfileDto.Finite(span.toSourceDistance))Fail("span identity/cycle/numeric");var metric=authority.Metric(span.roadId);double length=Math.Abs(span.toSourceDistance-span.fromSourceDistance);if(length<.000001||span.fromSourceDistance<0||span.toSourceDistance<0||span.fromSourceDistance>metric.SourceLength+1e-8||span.toSourceDistance>metric.SourceLength+1e-8)Fail("span source domain");
                if(i==0&&(span.roadId!=arm.roadId||Math.Abs(span.fromSourceDistance-arm.sourceDistance)>1e-8||Math.Sign(span.toSourceDistance-span.fromSourceDistance)!=arm.direction))Fail("initial arm source binding");
                if(authority.HasBranchBetween(span.roadId,span.fromSourceDistance,span.toSourceDistance))Fail("span crosses true or synthetic branch");
                if(previous!=null){var prevMetric=authority.Metric(previous.roadId);bool prevEnd=Endpoint(previous.toSourceDistance,prevMetric.SourceLength),nextEnd=Endpoint(span.fromSourceDistance,metric.SourceLength);if(!authority.Joins(previous.roadId,prevEnd,span.roadId,nextEnd))Fail("span join has no proved continuation");}
                total+=length;if(total>500.00000001)Fail("cumulative source budget");previous=span;
            }
            if(Math.Abs(total-arm.collarLength)>1e-8)Fail("collar terminal/cumulative source length");
        }
        static bool Endpoint(double value,double length){if(Math.Abs(value)<=1e-8)return false;if(Math.Abs(value-length)<=1e-8)return true;Fail("join is not an exact endpoint");return false;}
        static void Fail(string message)=>throw new InvalidDataException("Road arm spans: "+message);
    }
}
