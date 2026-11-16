using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using RoadRush.Core;
namespace RoadRush.RegionalDraft
{
    // DRAFT policy: reconcile source representations for gameplay, not source truth.
    // Original country/regional records remain immutable and fully receipted.
    public static class RegionalRenderAdmission
    {
        public const string PolicyIdentity="regional-os-visual-priority-v1-area50percent";
        public sealed class Suppression
        {
            public readonly string Source,Winner,SourceId,WinnerId,SourceKind,WinnerKind;
            public readonly double IntersectionArea,SourceCoverage,WinnerCoverage,SourceHeight,WinnerHeight;public readonly bool SourceHadHeightEvidence;
            public string Reason=>"ProviderPriorityVisualConflict";public bool ClaimsSameBuilding=>false;
            internal Suppression(RegionalBuildingMerge.Feature source,RegionalBuildingMerge.Feature winner,RegionalBuildingMerge.Overlap pair)
            {Source=source.SourceKey;Winner=winner.SourceKey;SourceId=source.Id;WinnerId=winner.Id;SourceKind=source.Kind;WinnerKind=winner.Kind;SourceHeight=source.Height;WinnerHeight=winner.Height;SourceHadHeightEvidence=source.MeasuredHeight;IntersectionArea=pair.Area;bool first=pair.A==source.SourceKey;SourceCoverage=first?pair.FractionA:pair.FractionB;WinnerCoverage=first?pair.FractionB:pair.FractionA;}
        }
        public sealed class Result
        {
            public readonly IReadOnlyList<RegionalBuildingMerge.Feature> OriginalRecords,Admitted;
            public readonly IReadOnlyList<Suppression> SourcePrioritySuppression;public readonly IReadOnlyList<RegionalBuildingMerge.Overlap> LocalReviewCases;
            public readonly IReadOnlyDictionary<string,string> ResolvedSourceRepresentatives;public readonly string Identity;
            internal Result(List<RegionalBuildingMerge.Feature> original,List<RegionalBuildingMerge.Feature> admitted,List<Suppression> suppressed,List<RegionalBuildingMerge.Overlap> local,Dictionary<string,string> representatives,string identity)
            {OriginalRecords=original.AsReadOnly();Admitted=admitted.AsReadOnly();SourcePrioritySuppression=suppressed.AsReadOnly();LocalReviewCases=local.AsReadOnly();ResolvedSourceRepresentatives=new ReadOnlyDictionary<string,string>(representatives);Identity=identity;}
        }
        static bool ProtectedLandmark(RegionalBuildingMerge.Feature f)=>f.Important||new[]{"school","church","chapel","cathedral","mosque","temple","hospital","train_station","public"}.Contains(f.Kind,StringComparer.Ordinal);
        static bool ReviewedOS(RegionalBuildingMerge.Feature f)
        {if(f.Source!=RegionalBuildingMerge.Origin.Regional)return false;try{return Encoding.UTF8.GetString(Convert.FromBase64String(f.SourceKey.Split('|')[0]))=="OS OpenMap Local";}catch(FormatException){return false;}}
        public static Result Prepare(RegionalBuildingMerge.Plan strict,IEnumerable<RegionalBuildingMerge.Feature> originalRecords,string sourceSetSHA256)
        {
            if(sourceSetSHA256==null||sourceSetSHA256.Length!=64||sourceSetSHA256.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new InvalidDataException("Exact original source-set identity required");
            var original=originalRecords.OrderBy(f=>f.SourceKey,StringComparer.Ordinal).ToList();if(original.Count!=strict.BaselineInput+strict.RegionalInput)throw new InvalidDataException("Complete original records required for render admission");var all=original.ToDictionary(f=>f.SourceKey,StringComparer.Ordinal);foreach(var source in strict.Features)if(!ReferenceEquals(all[source.SourceKey],source))throw new InvalidDataException("Admission source records differ from immutable merge inputs");var remaining=strict.Features.ToDictionary(f=>f.SourceKey,StringComparer.Ordinal);var candidates=new Dictionary<string,List<Suppression>>(StringComparer.Ordinal);
            foreach(var pair in strict.Overlaps.Where(p=>p.Outcome=="RetainedAmbiguous"))
            {
                var a=all[pair.A];var b=all[pair.B];RegionalBuildingMerge.Feature country,regional;
                if(a.Source==RegionalBuildingMerge.Origin.Country&&ReviewedOS(b)){country=a;regional=b;}else if(b.Source==RegionalBuildingMerge.Origin.Country&&ReviewedOS(a)){country=b;regional=a;}else continue;
                if(!remaining.ContainsKey(country.SourceKey)||!remaining.ContainsKey(regional.SourceKey)||ProtectedLandmark(country))continue;
                // This is explicitly a visual provider choice on measured area
                // overlap >=50%of the smaller outline, not a matched-building claim.
                if(Math.Max(pair.FractionA,pair.FractionB)<.50)continue;
                if(!candidates.TryGetValue(country.SourceKey,out var list))candidates[country.SourceKey]=list=new List<Suppression>();list.Add(new Suppression(country,regional,pair));
            }
            var suppressed=new List<Suppression>();foreach(var entry in candidates.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {var winner=entry.Value.OrderByDescending(p=>all[p.Winner].Important).ThenByDescending(p=>p.SourceCoverage).ThenByDescending(p=>p.IntersectionArea).ThenBy(p=>p.Winner,StringComparer.Ordinal).First();remaining.Remove(winner.Source);suppressed.Add(winner);}
            var admitted=remaining.Values.OrderBy(f=>f.Id,StringComparer.Ordinal).ThenBy(f=>f.SourceKey,StringComparer.Ordinal).ToList();
            var local=strict.Overlaps.Where(p=>p.Outcome=="RetainedAmbiguous"&&remaining.ContainsKey(p.A)&&remaining.ContainsKey(p.B)).ToList();
            // Local source disagreements remain visible/receipted. They do not
            // veto unrelated streets. No important source footprint is clipped.
            int beforeImportant=strict.Features.Count(f=>f.Source==RegionalBuildingMerge.Origin.Regional&&f.Important);if(admitted.Count(f=>f.Source==RegionalBuildingMerge.Origin.Regional&&f.Important)!=beforeImportant)throw new InvalidDataException("Admission suppressed an important regional feature");
            var aliases=strict.Coalescence.ToDictionary(d=>d.RemovedSource,d=>d.RetainedSource,StringComparer.Ordinal);foreach(var item in suppressed)aliases.Add(item.Source,item.Winner);var resolved=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var source in original)
            {string key=source.SourceKey;var visited=new HashSet<string>(StringComparer.Ordinal);while(aliases.TryGetValue(key,out var next)){if(!visited.Add(key))throw new InvalidDataException("Cyclic source admission provenance");key=next;}if(!remaining.ContainsKey(key))throw new InvalidDataException("Source record has no preserved rendering representative");resolved.Add(source.SourceKey,key);}
            if(admitted.Count+strict.Coalescence.Count+suppressed.Count!=original.Count)throw new InvalidDataException("Incomplete source accounting in admission");
            string identity=DataPackageRules.Hash(Encoding.UTF8.GetBytes(PolicyIdentity+"|"+sourceSetSHA256+"|"+string.Join("|",admitted.Select(f=>f.SourceKey))+"|"+string.Join("|",resolved.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value))));
            return new Result(original,admitted,suppressed,local,resolved,identity);
        }
    }
}
