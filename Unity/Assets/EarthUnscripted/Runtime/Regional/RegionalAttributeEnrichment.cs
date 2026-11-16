using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
namespace EarthUnscripted.RegionalDraft
{
    // DRAFT: never equates OSM-reported heights/levels with surveyed eaves heights.
    // Whole-footprint one-to-one matching is mandatory before attributes transfer.
    public static class RegionalAttributeEnrichment
    {
        public const string PolicyIdentity="regional-attributes-v1-mutual90-one-to-one-flat-total-height";
        public sealed class Receipt
        {
            public string TargetId,SourceId,Outcome,HeightOrigin,Confidence,KindOrigin;
            public string HeightTag,LevelsTag;public double SourceCoverage,TargetCoverage,OldHeight,NewHeight;public string OldKind,NewKind,OldRoof,NewRoof;
        }
        public sealed class Result
        {
            public readonly IReadOnlyList<RegionalBuildingMerge.Feature> Features;public readonly IReadOnlyList<Receipt> Receipts;public readonly string Identity;
            internal Result(List<RegionalBuildingMerge.Feature> features,List<Receipt> receipts,string identity){Features=features.AsReadOnly();Receipts=receipts.AsReadOnly();Identity=identity;}
        }
        readonly struct Match
        {public readonly string Target;public readonly double Country,Regional;public Match(string target,double country,double regional){Target=target;Country=country;Regional=regional;}}
        static bool KindKnown(string kind)=>new[]{"house","detached","semidetached_house","terrace","residential","apartments","garage","garages","shed","retail","industrial","commercial","warehouse","water_tower","school","church","hospital","public"}.Contains(kind,StringComparer.Ordinal);
        static double? Positive(string raw,double max)
        {if(raw==null||!Regex.IsMatch(raw,@"^[0-9]+(?:\.[0-9]+)?(?:\s*m)?$"))return null;string value=raw.Trim().TrimEnd('m').Trim();return double.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double number)&&number>0&&number<=max?(double?)number:null;}
        public static Result Prepare(RegionalBuildingMerge.Plan strict,RegionalRenderAdmission.Result admission,JObject countrySourceTags,JObject regionalHeightMetadata,string exactEvidenceSHA256)
        {
            if(exactEvidenceSHA256==null||!Regex.IsMatch(exactEvidenceSHA256,"^[a-f0-9]{64}$"))throw new InvalidDataException("Pinned attribute evidence hash required");
            var all=admission.OriginalRecords.ToDictionary(f=>f.SourceKey,StringComparer.Ordinal);var active=admission.Admitted.ToDictionary(f=>f.SourceKey,StringComparer.Ordinal);var matches=new Dictionary<string,Match>(StringComparer.Ordinal);
            foreach(var pair in strict.Overlaps)
            {
                var a=all[pair.A];var b=all[pair.B];RegionalBuildingMerge.Feature country,regional;double covC,covR;
                if(a.Source==RegionalBuildingMerge.Origin.Country&&b.Source==RegionalBuildingMerge.Origin.Regional){country=a;regional=b;covC=pair.FractionA;covR=pair.FractionB;}else if(b.Source==RegionalBuildingMerge.Origin.Country&&a.Source==RegionalBuildingMerge.Origin.Regional){country=b;regional=a;covC=pair.FractionB;covR=pair.FractionA;}else continue;
                if(admission.ResolvedSourceRepresentatives[country.SourceKey]==regional.SourceKey&&active.ContainsKey(regional.SourceKey))matches[country.SourceKey]=new Match(regional.SourceKey,covC,covR);
            }
            foreach(var item in strict.Coalescence)
            {
                var country=all[item.RemovedSource];if(country.Source!=RegionalBuildingMerge.Origin.Country||!active.TryGetValue(item.RetainedSource,out var target)||target.Source!=RegionalBuildingMerge.Origin.Regional)continue;
                if(admission.ResolvedSourceRepresentatives[country.SourceKey]==target.SourceKey)matches[country.SourceKey]=new Match(target.SourceKey,item.RemovedCoverage,item.RetainedCoverage);
            }
            var groups=admission.OriginalRecords.Where(f=>f.Source==RegionalBuildingMerge.Origin.Country).GroupBy(f=>admission.ResolvedSourceRepresentatives[f.SourceKey]).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
            var receipts=new List<Receipt>();var changed=new Dictionary<string,RegionalBuildingMerge.Feature>(StringComparer.Ordinal);
            foreach(var entry in matches.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                var source=all[entry.Key];var target=active[entry.Value.Target];var tags=countrySourceTags[source.Id] as JObject;var meta=regionalHeightMetadata[target.Id] as JObject;
                if(tags==null||meta==null)continue;
                string heightTag=(string)tags["height"],levelsTag=(string)tags["building:levels"];bool sourceHeightEvidence=heightTag!=null||levelsTag!=null;
                bool relative=(string)meta["heightEnrichment"]=="pilot-relative-DSM-DTM";bool strong=entry.Value.Country>=.90&&entry.Value.Regional>=.90&&groups[target.SourceKey]==1;
                var receipt=new Receipt{TargetId=target.Id,SourceId=source.Id,HeightTag=heightTag,LevelsTag=levelsTag,SourceCoverage=entry.Value.Country,TargetCoverage=entry.Value.Regional,OldHeight=target.Height,NewHeight=target.Height,OldKind=target.Kind,NewKind=target.Kind,OldRoof=target.Roof,NewRoof=target.Roof,Confidence=strong?"Strong whole-footprint correspondence; source attributes remain reported/modelled":"Divergent or aggregated footprint; no same-building transfer",HeightOrigin=relative?"Relative DSM-DTM wall-height model retained; not surveyed eaves":"Original fictional height retained",KindOrigin="Regional source class retained",Outcome="Unchanged"};
                double height=target.Height;string kind=target.Kind,roof=target.Roof;
                if(strong&&target.Kind=="building"&&KindKnown(source.Kind)){kind=source.Kind;receipt.KindOrigin="OSM building-use tag, strong one-to-one footprint correspondence";}
                if(sourceHeightEvidence&&!strong){receipt.Outcome="DeferredDivergentOrAggregateHeight";}
                else if(sourceHeightEvidence&&relative){receipt.Outcome="RelativeHeightModelRetained";}
                else if(strong)
                {
                    double? total=Positive(heightTag,80),levels=Positive(levelsTag,26);
                    if(total.HasValue)
                    {height=total.Value;roof="flat";receipt.HeightOrigin="OSM reported total height; flat artwork avoids inventing added ridge height; not surveyed";receipt.Outcome="ReportedHeightWithFlatArtwork";}
                    else if(heightTag==null&&levels.HasValue)
                    {height=levels.Value*3;receipt.HeightOrigin="OSM building:levels times assumed3metres/floor: modelled wall height, not surveyed";receipt.Outcome="LevelsHeightEstimate";}
                    else if(sourceHeightEvidence){receipt.Outcome="UnsupportedReportedHeightSyntaxRetained";}
                }
                receipt.NewHeight=height;receipt.NewKind=kind;receipt.NewRoof=roof;
                if(kind!=target.Kind||height!=target.Height||roof!=target.Roof)
                {changed[target.SourceKey]=new RegionalBuildingMerge.Feature(target.Id,target.SourceKey,kind,roof,height,false,target.Source,target.Points);if(receipt.Outcome=="Unchanged")receipt.Outcome="SourceGameKindEnriched";}
                if(sourceHeightEvidence||receipt.NewKind!=receipt.OldKind)receipts.Add(receipt);
            }
            var features=admission.Admitted.Select(f=>changed.TryGetValue(f.SourceKey,out var value)?value:f).ToList();
            string identity=DataPackageRules.Hash(Encoding.UTF8.GetBytes(PolicyIdentity+"|"+admission.Identity+"|"+exactEvidenceSHA256+"|"+Newtonsoft.Json.JsonConvert.SerializeObject(receipts)));
            return new Result(features,receipts,identity);
        }
    }
}
