using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using RoadRush.Core;
namespace RoadRush.RegionalDraft
{
    // DRAFT bounded non-overlapping physical admission. Raw source records remain
    // in the library/sidecar; no source outline is clipped or rewritten.
    public static class RegionalPhysicalFallback
    {
        public const string PolicyIdentity="regional-physical-v1-contained-important-whole-footprint-independent-set";
        public sealed class Proxy
        {
            public string Id,SourceKey,GeometrySource,CanonicalKind,KindOrigin,HeightOrigin;public double Height;
            public IReadOnlyList<string> RepresentedSources;
        }
        public sealed class Deferred
        {
            public string Source,Proxy,Reason;public double SourceCoverageByProxy;
            public bool ClaimsSameBuilding=>false;
        }
        public sealed class Result
        {
            public IReadOnlyList<RegionalBuildingMerge.Feature> Features;public IReadOnlyList<Proxy> Proxies;public IReadOnlyList<Deferred> DeferredSources;public IReadOnlyDictionary<string,string> ResolvedOriginalSources;
            public string Identity;public int InputPhysicalConflicts,RemainingPhysicalConflicts;
        }
        static bool PublicUse(string kind)=>new[]{"school","church","chapel","cathedral","mosque","temple","hospital","train_station","public","water_tower"}.Contains(kind,StringComparer.Ordinal);
        static double Area(RegionalBuildingMerge.Feature feature){double twice=0;var p=feature.Points;for(int i=0;i<p.Count-1;i++)twice+=(p[i].X-p[0].X)*(p[i+1].Z-p[0].Z)-(p[i+1].X-p[0].X)*(p[i].Z-p[0].Z);return Math.Abs(twice)*.5;}
        static double Coverage(RegionalBuildingMerge.Overlap pair,string source)=>pair.A==source?pair.FractionA:pair.FractionB;
        static string Other(RegionalBuildingMerge.Overlap pair,string source)=>pair.A==source?pair.B:pair.A;
        public static Result Prepare(RegionalRenderAdmission.Result admission,RegionalAttributeEnrichment.Result enriched,RegionalBuildingMerge.Policy workPolicy)
        {
            var active=enriched.Features.ToDictionary(f=>f.SourceKey,StringComparer.Ordinal);var edges=RegionalBuildingMerge.AuditPhysicalFootprints(active.Values,workPolicy);var neighbours=active.Keys.ToDictionary(k=>k,k=>new List<RegionalBuildingMerge.Overlap>(),StringComparer.Ordinal);foreach(var edge in edges){neighbours[edge.A].Add(edge);neighbours[edge.B].Add(edge);}
            bool Importance(RegionalBuildingMerge.Feature feature)=>feature.Important||neighbours[feature.SourceKey].Any(e=>active[Other(e,feature.SourceKey)].Important&&Coverage(e,Other(e,feature.SourceKey))>=.98);
            var order=active.Values.OrderByDescending(f=>Importance(f)).ThenByDescending(f=>PublicUse(f.Kind)).ThenByDescending(f=>f.Source==RegionalBuildingMerge.Origin.Regional).ThenByDescending(Area).ThenBy(f=>f.SourceKey,StringComparer.Ordinal).ToArray();
            var owner=new Dictionary<string,string>(StringComparer.Ordinal);var selected=new List<RegionalBuildingMerge.Feature>();var proxies=new List<Proxy>();var deferred=new List<Deferred>();
            foreach(var geometry in order)
            {
                if(owner.ContainsKey(geometry.SourceKey))continue;
                var group=new List<string>{geometry.SourceKey};foreach(var edge in neighbours[geometry.SourceKey]){string other=Other(edge,geometry.SourceKey);if(!owner.ContainsKey(other))group.Add(other);}
                if(group.Count==1){owner.Add(geometry.SourceKey,geometry.SourceKey);selected.Add(geometry);continue;}
                // Use one COMPLETE original footprint, not a guessed union/hull or
                // centroid cut. Neighbours deferred by this proxy stay fully archived.
                var importanceSources=group.Select(k=>active[k]).Where(f=>f.Important).OrderByDescending(Area).ThenBy(f=>f.SourceKey,StringComparer.Ordinal).ToArray();
                string kind=geometry.Kind;double height=geometry.Height;string heightOrigin="Chosen whole-footprint source model retained";
                if(Importance(geometry))
                {
                    kind="important_building";
                    // Public use is a sourced semantic hint for this conflict proxy,
                    // never a claim that a generalised outline is a surveyed campus.
                    var publicHints=group.Select(k=>active[k]).Where(f=>PublicUse(f.Kind)).OrderByDescending(Area).ThenBy(f=>f.SourceKey,StringComparer.Ordinal).ToArray();if(publicHints.Length>0)kind=publicHints[0].Kind;
                    var contained=importanceSources.Where(f=>f.SourceKey==geometry.SourceKey||neighbours[geometry.SourceKey].Any(e=>Other(e,geometry.SourceKey)==f.SourceKey&&Coverage(e,f.SourceKey)>=.98)).ToArray();
                    if(!geometry.Important&&contained.Length>0){height=contained.Max(f=>f.Height);heightOrigin="Contained important-source model on whole-footprint composite artwork; not surveyed or a terrace-wide OSM height transfer";}
                }
                string digest=DataPackageRules.Hash(Encoding.UTF8.GetBytes(PolicyIdentity+"|"+geometry.SourceKey+"|"+string.Join("|",group.OrderBy(k=>k,StringComparer.Ordinal))));string proxyKey="physical-proxy|"+digest,id="regional-proxy-"+digest.Substring(0,24);
                var feature=new RegionalBuildingMerge.Feature(id,proxyKey,kind,geometry.Roof,height,false,geometry.Source,geometry.Points);selected.Add(feature);proxies.Add(new Proxy{Id=id,SourceKey=proxyKey,GeometrySource=geometry.SourceKey,CanonicalKind=kind,KindOrigin="Derived whole-footprint conflict proxy; protected public use remains a sourced hint, not a surveyed use boundary",Height=height,HeightOrigin=heightOrigin,RepresentedSources=group.OrderBy(k=>k,StringComparer.Ordinal).ToList().AsReadOnly()});
                foreach(string source in group)
                {
                    owner.Add(source,proxyKey);if(source==geometry.SourceKey)continue;var edge=neighbours[geometry.SourceKey].Single(e=>Other(e,geometry.SourceKey)==source);
                    deferred.Add(new Deferred{Source=source,Proxy=proxyKey,Reason="Local physical conflict fallback; complete source retained, not same-building coalescence",SourceCoverageByProxy=Coverage(edge,source)});
                }
            }
            selected=selected.OrderBy(f=>f.Id,StringComparer.Ordinal).ToList();var post=RegionalBuildingMerge.AuditPhysicalFootprints(selected,workPolicy);if(post.Count!=0)throw new System.IO.InvalidDataException("Physical fallback still has overlapping admitted footprints; candidate must not publish");
            var resolved=admission.ResolvedSourceRepresentatives.ToDictionary(p=>p.Key,p=>owner[p.Value],StringComparer.Ordinal);var identities=new HashSet<string>(selected.Select(f=>f.SourceKey),StringComparer.Ordinal);if(resolved.Values.Any(k=>!identities.Contains(k)))throw new System.IO.InvalidDataException("Original source missing a final physical representative");
            return new Result{Features=selected.AsReadOnly(),Proxies=proxies.AsReadOnly(),DeferredSources=deferred.AsReadOnly(),ResolvedOriginalSources=resolved,InputPhysicalConflicts=edges.Count,RemainingPhysicalConflicts=post.Count,Identity=DataPackageRules.Hash(Encoding.UTF8.GetBytes(PolicyIdentity+"|"+enriched.Identity+"|"+string.Join("|",selected.Select(f=>f.SourceKey))))};
        }
    }
}
