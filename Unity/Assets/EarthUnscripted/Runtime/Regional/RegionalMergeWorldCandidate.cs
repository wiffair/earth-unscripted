using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
namespace EarthUnscripted.RegionalDraft
{
    public static class RegionalMergeWorldCandidate
    {
        public sealed class Candidate
        {
            public readonly JObject World;public readonly RegionalBuildingMerge.Plan Merge;public readonly IReadOnlyList<RegionalBuildingMerge.Feature> OriginalRecords;public readonly string NonBuildingSHA256;public readonly bool NonBuildingUnchanged,AuthoringBlocked;public readonly long BuildingPoints,RoofCost,Utf8Bytes;
            // Final real codec/renderer/native acceptance is mandatory even if false.
            public readonly IReadOnlyList<string> Blockers;
            internal Candidate(JObject world,RegionalBuildingMerge.Plan plan,bool blocked,int maximumBuildings,string originalOther,List<RegionalBuildingMerge.Feature> originalRecords)
            {
                World=world;Merge=plan;OriginalRecords=originalRecords.AsReadOnly();AuthoringBlocked=blocked;BuildingPoints=plan.Features.Sum(f=>(long)f.Points.Count);RoofCost=plan.Features.Sum(f=>(long)f.Points.Count*f.Points.Count);Utf8Bytes=Encoding.UTF8.GetByteCount(world.ToString(Formatting.None));string after=Other(world);NonBuildingUnchanged=after==originalOther;NonBuildingSHA256=DataPackageRules.Hash(Encoding.UTF8.GetBytes(after));
                var problems=new List<string>();if(blocked)problems.Add("Collection is authoring-only");if(plan.NeedsOverlapReview)problems.Add("Significant ambiguous source overlap retained; explicit policy review needed");if(plan.Features.Count>maximumBuildings)problems.Add("Final building count exceeds declared profile");if(BuildingPoints>120000)problems.Add("Building points exceed reserved120k budget");if(RoofCost>20000000)problems.Add("Building roof cost exceeds20M");if(Utf8Bytes>DataPackageRules.MaximumFileBytes)problems.Add("Exact JSON UTF8 exceeds32MiB");if(!NonBuildingUnchanged)throw new InvalidDataException("Transaction altered non-building world values");Blockers=problems.AsReadOnly();
            }
        }
        public static Candidate Prepare(JObject original,RegionalCollection.Selection selection,int maximumBuildings=8192,RegionalBuildingMerge.Policy policy=null,CancellationToken cancel=default)
        {
            // No bypass of authoring or overlap blockers; the returned object is an
            // unpublished review candidate, never saved to RegionCache or installed.
            string before=Other(original);var world=(JObject)original.DeepClone();double latitude=(double)original["latitude"],longitude=(double)original["longitude"];float minX=(float)original["minX"],minZ=(float)original["minZ"],width=(float)original["width"],depth=(float)original["depth"];
            var country=CountryFeatures(original);var detail=new List<RegionalBuildingMerge.Feature>();var ground=Ground(original);double cos=Math.Cos(latitude*Math.PI/180);
            foreach(var building in selection.Buildings)
            {
                cancel.ThrowIfCancellationRequested();var points=building.Points.Select(p=>{float x=(float)((p.Longitude-longitude)*111319.4908*cos),z=(float)((p.Latitude-latitude)*111319.4908);return new RegionalBuildingMerge.Point(x,ground(x,z),z);}).ToArray();
                if(points.Max(p=>p.X)<minX||points.Min(p=>p.X)>minX+width||points.Max(p=>p.Z)<minZ||points.Min(p=>p.Z)>minZ+depth)throw new InvalidDataException("Selection contains features outside this preparation rectangle; do not attach6km selection to3km world");
                detail.Add(new RegionalBuildingMerge.Feature(building.Id,building.SourceKey,building.Kind,building.Roof,building.Height,false,RegionalBuildingMerge.Origin.Regional,points));
            }
            var plan=RegionalBuildingMerge.Prepare(country,detail,policy??new RegionalBuildingMerge.Policy(),cancel);world["buildings"]=new JArray(plan.Features.Select(ToJson));return new Candidate(world,plan,selection.RuntimeAuthoringGate,maximumBuildings,before,country.Concat(detail).ToList());
        }
        public static RegionalBuildingMerge.Feature[] CountryFeatures(JObject world)
        {
            string version=(string)world["datasetVersion"]??"unversioned",country=(string)world["countryCode"]??"unknown";var input=world["buildings"] as JArray??throw new InvalidDataException("Missing baseline buildings");
            return input.Select(t=>{var b=(JObject)t;string id=(string)b["id"];var points=((JArray)b["points"]).Select(p=>new RegionalBuildingMerge.Point((double)p["x"],(double)p["y"],(double)p["z"]));return new RegionalBuildingMerge.Feature(id,"country|"+country+"|"+version+"|"+id,(string)b["kind"],(string)b["roof"],(double)b["height"],(bool?)b["measuredHeight"]==true,RegionalBuildingMerge.Origin.Country,points,b);}).ToArray();
        }
        public static JObject ToJson(RegionalBuildingMerge.Feature feature)
        {
            if(feature.Original!=null)return (JObject)feature.Original.DeepClone();
            return new JObject{{"id",feature.Id},{"kind",feature.Kind},{"roof",feature.Roof},{"height",(float)feature.Height},{"measuredHeight",feature.MeasuredHeight},{"points",new JArray(feature.Points.Select(p=>new JObject{{"x",(float)p.X},{"y",(float)p.Y},{"z",(float)p.Z}}))}};
        }
        static string Other(JObject world){var clone=(JObject)world.DeepClone();clone.Remove("buildings");return clone.ToString(Formatting.None);}
        static Func<float,float,float> Ground(JObject world)
        {
            int nx=(int)world["nx"],nz=(int)world["nz"];float minX=(float)world["minX"],minZ=(float)world["minZ"],width=(float)world["width"],depth=(float)world["depth"];var heights=((JArray)world["heights"]).Select(h=>(float)h).ToArray();if(nx<2||nz<2||heights.Length!=nx*nz||width<=0||depth<=0)throw new InvalidDataException("Invalid actual baseline ground grid");
            float Lerp(float a,float b,float t)=>a+(b-a)*Math.Max(0,Math.Min(1,t));
            return (x,z)=>{float gx=Math.Max(0,Math.Min(nx-1,(x-minX)/width*(nx-1))),gz=Math.Max(0,Math.Min(nz-1,(z-minZ)/depth*(nz-1)));int ix=Math.Min((int)gx,nx-2),iz=Math.Min((int)gz,nz-2);return Lerp(Lerp(heights[iz*nx+ix],heights[iz*nx+ix+1],gx-ix),Lerp(heights[(iz+1)*nx+ix],heights[(iz+1)*nx+ix+1],gx-ix),gz-iz);};
        }
    }
}
