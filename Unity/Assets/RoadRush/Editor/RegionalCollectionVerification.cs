using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using RoadRush.Core;
using RoadRush.RegionalDraft;
using R=RoadRush.RegionalDraft.RegionalCollection;
using M=RoadRush.RegionalDraft.RegionalBuildingMerge;
namespace RoadRush.Editor
{
    // Explicit offline data verification. No discovery, source activation, country
    // database, PlayerProfile, GameSession, WorldBuilder or scene mutation.
    public static class RegionalCollectionVerification
    {
        const string IndexPin="77d2d1d407fb7de76404ec36bfe6b2460b25a8ae76b3ec997eb85914ab8d70b9";
        static readonly string[] RuntimeFiles={"RegionalCollection.cs","RegionalCollectionWorker.cs","RegionalBuildingMerge.cs","RegionalMergeWorldCandidate.cs","RegionalRenderAdmission.cs","RegionalAttributeEnrichment.cs","RegionalPhysicalFallback.cs"};
        static string Argument(string name)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);if(i<0||i+1>=args.Length||args[i+1].StartsWith("-",StringComparison.Ordinal))throw new ArgumentException("Explicit "+name+" required; source paths are never auto-discovered by this test");return Path.GetFullPath(args[i+1]);}
        static byte[] Evidence(string root,string name,int length,string hash)
        {string path=Path.Combine(root,name);if(new FileInfo(path).Length!=length)throw new InvalidDataException("Fixture size differs: "+name);byte[] bytes=File.ReadAllBytes(path);if(DataPackageRules.Hash(bytes)!=hash)throw new InvalidDataException("Fixture hash differs: "+name);return bytes;}
        static JObject Parse(byte[] bytes)
        {using var reader=new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes))){MaxDepth=32,DateParseHandling=DateParseHandling.None};return JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});}
        static R.Rect Window(double latitude,double longitude,double side)
        {double dy=side*.5/111319.4908,dx=dy/Math.Cos(latitude*Math.PI/180);return new R.Rect(longitude-dx,latitude-dy,longitude+dx,latitude+dy);}
        static string Other(JObject world){var clone=(JObject)world.DeepClone();clone.Remove("buildings");return clone.ToString(Formatting.None);}
        static JObject PhysicalWorld(JObject original,RegionalPhysicalFallback.Result physical)
        {var candidate=(JObject)original.DeepClone();candidate["buildings"]=new JArray(physical.Features.Select(RegionalMergeWorldCandidate.ToJson));return candidate;}
        static bool PublicKind(string kind)=>new[]{"important_building","school","church","hospital","public"}.Contains(kind,StringComparer.Ordinal);
        public static void Run()
        {
            int checks=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            void Check(bool value,string reason){if(!value)throw new Exception("REGIONAL UNITY FAIL "+reason);checks++;Debug.Log("REGIONAL UNITY PASS "+reason);}
            try
            {
                // Captured once on the main thread. This test never mutates these
                // inputs or publishes a world, even when analysis fits a budget.
                string library=Argument("-roadRushRegionalLibrary"),evidence=Argument("-roadRushRegionalEvidence");
                string sourceFolder=Path.Combine(Application.dataPath,"RoadRush/Runtime/Regional");var beforeAssets=RuntimeFiles.ToDictionary(f=>f,f=>DataPackageRules.HashFile(Path.Combine(sourceFolder,f)),StringComparer.Ordinal);
                var baselineBytes=Evidence(evidence,"native5-original-canonical.json",2114506,"084c98a0fa197c4b0f95edb003ff8777a4732ab9122243017331d1e34f665fb0");
                var raw6Bytes=Evidence(evidence,"country-6km-footprints-only.json",801440,"a0c1ed71fe83cf0a5d84df50f644babe1558e45363bc2629073afd9d2a6ac95b");
                var tagsBytes=Evidence(evidence,"all-country-building-source-tags.json",111406,"0807980eef52752b040d57bba0ce79862ec069a1de9c868dc40ed4c563221f0b");
                var heightBytes=Evidence(evidence,"height-source-diagnosis.json",4020442,"de4b152a17415d98c5092d93444645a38df082623223207d0406945f12ee236b");
                var original=Parse(baselineBytes);string originalJson=original.ToString(Formatting.None);var raw6=Parse(raw6Bytes);var tags=Parse(tagsBytes);var heightMeta=(JObject)Parse(heightBytes)["lookup"];
                var catalogue=R.Load(library,IndexPin);Check(catalogue.Id=="pantside-10mi"&&catalogue.LeafCount==868,"actual reviewed immutable collection indexes all868leaves");
                Check(!catalogue.RuntimeActivationAllowed,"collection remains authoring-only; import is not activation");
                const double lat=51.671156,lon=-3.131748;var three=catalogue.QueryAll(Window(lat,lon,3000),R.Limits.Proposed8192());var six=catalogue.QueryAll(Window(lat,lon,6000),new R.Limits());
                Check(three.Leaves.Count==14&&three.Buildings.Count==1793,"actual3km selects all14shards/1793whole source parts");
                Check(six.Leaves.Count==43&&six.Buildings.Count==4766,"actual6km selects all43shards/4766parts beyond legacy8limit");
                Check(six.BuildingPoints==25247&&six.RoofCost==146761&&six.SelectedBytes==5288601,"actual6km byte/point/roof accounting remains exact");
                Check(!six.FitsDeclaredBuildingBudget&&six.Buildings.Count==4766,"current2500analysis budget refuses full6km without top-N loss");
                var proposed=catalogue.QueryAll(Window(lat,lon,6000),R.Limits.Proposed8192());Check(proposed.FitsDeclaredBuildingBudget&&proposed.RuntimeAuthoringGate,"proposed8kpreflight fits while authoring activation gate stays closed");
                var asyncJob=RegionalCollectionWorker.Analyze(library,IndexPin,Window(lat,lon,3000),R.Limits.Proposed8192(),default);var asyncThree=asyncJob.GetAwaiter().GetResult();Check(asyncThree.SelectionIdentity==three.SelectionIdentity,"worker-only path produces identical immutable selection identity");
                Check(three.Buildings.Any(b=>b.Points.Any(p=>!Window(lat,lon,3000).Contains(p.Longitude,p.Latitude))),"whole feature overhang remains retained beyond requested window");
                var transaction=RegionalMergeWorldCandidate.Prepare(original,three);Check(transaction.Merge.BaselineInput==650&&transaction.Merge.RegionalInput==1793,"real Native5baseline is3km650, not an invented6kmcanonical world");
                Check(transaction.Merge.Features.Count==2264&&transaction.Merge.Coalescence.Count==179,"strict spatial transaction accounts for all2443input records");
                Check(transaction.NonBuildingUnchanged&&original.ToString(Formatting.None)==originalJson,"strict transaction never mutates original or non-building JSON");
                string sources3=DataPackageRules.Hash(Encoding.UTF8.GetBytes(DataPackageRules.Hash(baselineBytes)+"|"+three.SelectionIdentity));var admission3=RegionalRenderAdmission.Prepare(transaction.Merge,transaction.OriginalRecords,sources3);
                Check(admission3.Admitted.Count==1810&&admission3.SourcePrioritySuppression.Count==454&&admission3.LocalReviewCases.Count==30,"provider conflicts use explicit admission instead of global ambiguity blockade");
                Check(admission3.OriginalRecords.Count==2443&&admission3.ResolvedSourceRepresentatives.Count==2443,"all3kmoriginal source records remain preserved and resolved");
                var country6=RegionalMergeWorldCandidate.CountryFeatures(raw6);double cos=Math.Cos(lat*Math.PI/180);var regional6=proposed.Buildings.Select(b=>new M.Feature(b.Id,b.SourceKey,b.Kind,b.Roof,b.Height,false,M.Origin.Regional,b.Points.Select(p=>new M.Point((float)((p.Longitude-lon)*111319.4908*cos),0,(float)((p.Latitude-lat)*111319.4908))))).ToArray();
                var strict6=M.Prepare(country6,regional6,new M.Policy());Check(country6.Length==1969&&regional6.Length==4766&&strict6.Features.Count==6318,"actual uncapped6kmfootprint fixture consumes all6735source records");
                var admission6=RegionalRenderAdmission.Prepare(strict6,country6.Concat(regional6),DataPackageRules.Hash(Encoding.UTF8.GetBytes(DataPackageRules.Hash(raw6Bytes)+"|"+proposed.SelectionIdentity)));
                Check(admission6.Admitted.Count==4835&&admission6.OriginalRecords.Count==6735,"6kmprovider admission preserves full original library records");
                string AttrPin(RegionalRenderAdmission.Result admission)=>DataPackageRules.Hash(Encoding.UTF8.GetBytes(admission.Identity+"|"+DataPackageRules.Hash(tagsBytes)+"|"+DataPackageRules.Hash(heightBytes)));
                var enriched3=RegionalAttributeEnrichment.Prepare(transaction.Merge,admission3,tags,heightMeta,AttrPin(admission3));var enriched6=RegionalAttributeEnrichment.Prepare(strict6,admission6,tags,heightMeta,AttrPin(admission6));
                var tower=enriched6.Features.Single(f=>f.Id=="os-2EDB91EF-0A41-4FE5-AA41-7223392DA65E-pb4ca89fdb5abed5141a4-0");Check(tower.Kind=="water_tower"&&tower.Height==10&&tower.Roof=="flat"&&!tower.MeasuredHeight,"reported10mwater tower/gamekind retained without surveyed-height claim");
                Check(enriched6.Receipts.Any(r=>r.Outcome=="DeferredDivergentOrAggregateHeight")&&enriched6.Receipts.Where(r=>r.Outcome=="DeferredDivergentOrAggregateHeight").All(r=>r.NewHeight==r.OldHeight),"divergent house/terrace level estimates never spread across aggregate footprint");
                Check(enriched6.Receipts.Any(r=>r.HeightOrigin.StartsWith("Relative DSM",StringComparison.Ordinal))&&enriched6.Receipts.Where(r=>r.HeightOrigin.StartsWith("Relative DSM",StringComparison.Ordinal)).All(r=>r.NewHeight==r.OldHeight),"relative DSM-DTM estimates remain primary and explicitly nonsurveyed");
                var physical3=RegionalPhysicalFallback.Prepare(admission3,enriched3,new M.Policy());var physical6=RegionalPhysicalFallback.Prepare(admission6,enriched6,new M.Policy());
                Check(physical3.Features.Count==1774&&physical3.Proxies.Count==32&&physical3.InputPhysicalConflicts==41&&physical3.RemainingPhysicalConflicts==0,"actual3kmphysical candidate resolves all audited conflicts with whole-footprint proxies");
                Check(physical6.Features.Count==4759&&physical6.Proxies.Count==66&&physical6.InputPhysicalConflicts==88&&physical6.RemainingPhysicalConflicts==0,"actual6kmphysical footprint candidate has zero overlapping admitted bodies");
                Check(physical3.ResolvedOriginalSources.Count==2443&&physical6.ResolvedOriginalSources.Count==6735,"every source keeps explicit final physical representative provenance");
                Check(physical6.Features.Any(f=>f.Kind=="water_tower"&&f.Height==10),"known height/gamekind survive physical conflict fallback");
                var by=physical6.Features.ToDictionary(f=>f.SourceKey,StringComparer.Ordinal);int important=0;double minimumCoverage=1;
                foreach(var source in admission6.OriginalRecords.Where(f=>f.Source==M.Origin.Regional&&f.Important))
                {
                    var target=by[physical6.ResolvedOriginalSources[source.SourceKey]];Check(PublicKind(target.Kind),"important source retains important/public canonical representative");double fraction=1;
                    if(source.SourceKey!=target.SourceKey){var pair=M.AuditPhysicalFootprints(new[]{source,target},new M.Policy());fraction=pair.Count==0?0:pair[0].A==source.SourceKey?pair[0].FractionA:pair[0].FractionB;}
                    minimumCoverage=Math.Min(minimumCoverage,fraction);important++;
                }
                Check(important==70&&minimumCoverage>=.98,"all70important outlines retain at least98percent physical coverage, no source clipping");
                var world3=PhysicalWorld(original,physical3);Check(Other(world3)==Other(original),"final3kmcandidate preserves every road/water/terrain/rail/route and other non-building value");
                // Use the ACTUAL current Unity model, JSON serializer, renderer guard
                // and peer codec. No WorldBuilder/finalization alters this fixture.
                var typed=JsonUtility.FromJson<GeoRegion>(world3.ToString(Formatting.None));string canonical=WorldSnapshotCodec.CanonicalJson(typed);string epoch=WorldSnapshotCodec.Epoch(typed);var decoded=WorldSnapshotCodec.Decode(canonical,WorldSnapshotCodec.NetworkId(typed),epoch);
                Check(decoded.buildings.Length==1774&&WorldSnapshotCodec.CanonicalJson(decoded)==canonical&&WorldSnapshotCodec.Epoch(decoded)==epoch,"current real peer codec accepts1774building3kmcandidate with exact canonical/epoch roundtrip");
                var render=BuildingRenderInput.Create(typed);Check(render.SourceCount==1774&&render.Renderable.Length==1774&&render.SkippedCount==0,"actual current renderer admits every3kmcandidate source below current guard");
                Check(WorldSnapshotCodec.CanonicalJson(typed)==canonical,"actual renderer interpretation leaves canonical source geometry unchanged");
                // A building-array-only guard fixture, NOT a playable6kmworld.
                var array6=JsonUtility.FromJson<BuildingArray>(new JObject{{"buildings",new JArray(physical6.Features.Select(RegionalMergeWorldCandidate.ToJson))}}.ToString(Formatting.None));var guardOnly=new GeoRegion{buildings=array6.buildings};var denied=BuildingRenderInput.Create(guardOnly);
                Check(denied.Renderable.Length==0&&denied.SkippedCount==4759&&denied.Entries.Any(e=>(e.Diagnostics&BuildingRenderInput.Flags.UnsupportedBudget)!=0),"actual unchanged renderer explicitly refuses4759building fixture: no stealth8kguard edit");
                var tooMany=JsonUtility.FromJson<GeoRegion>(canonical);tooMany.buildings=array6.buildings;string invalid=WorldSnapshotCodec.CanonicalJson(tooMany);bool refused=false;try{WorldSnapshotCodec.Decode(invalid,WorldSnapshotCodec.NetworkId(tooMany),WorldSnapshotCodec.Epoch(tooMany));}catch(InvalidDataException){refused=true;}
                Check(refused,"actual unchanged peer codec refuses oversize building array;6kmactivation remains blocked");
                Check(RuntimeFiles.All(f=>beforeAssets[f]==DataPackageRules.HashFile(Path.Combine(sourceFolder,f))),"all actual Regional source Assets remain byte-identical through verification");
                Check(DataPackageRules.HashFile(Path.Combine(library,"coverage-index.json"))==IndexPin,"immutable collection source index remains unchanged");
                Check(DataPackageRules.HashFile(Path.Combine(evidence,"native5-original-canonical.json"))==DataPackageRules.Hash(baselineBytes)&&DataPackageRules.HashFile(Path.Combine(evidence,"country-6km-footprints-only.json"))==DataPackageRules.Hash(raw6Bytes)&&DataPackageRules.HashFile(Path.Combine(evidence,"all-country-building-source-tags.json"))==DataPackageRules.Hash(tagsBytes)&&DataPackageRules.HashFile(Path.Combine(evidence,"height-source-diagnosis.json"))==DataPackageRules.Hash(heightBytes),"all pinned evidence inputs remain unmodified");
                Check(Directory.GetFiles(sourceFolder,"*.dll").Length==0,"no external Newtonsoft or binary plugin copied into runtime Assets");
                string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds/Verification/RegionalRuntime",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(output);
                var receipt=new JObject{{"format","RoadRushRegionalUnityEditorVerification1"},{"checks",checks},{"unityVersion",Application.unityVersion},{"library",library},{"evidence",evidence},{"indexSHA256",IndexPin},{"runtimeActivationAllowed",false},{"nativePlayerExecuted",false},{"dbAccess",false},{"assetsMutatedByTest",false},{"threeKmFeatures",physical3.Features.Count},{"sixKmFeatures",physical6.Features.Count},{"sixKmCanonicalWorld",false},{"minimumImportantCoverage",minimumCoverage},{"originalRecords3km",2443},{"originalRecords6km",6735},{"canonical3kmBytes",Encoding.UTF8.GetByteCount(canonical)},{"canonical3kmEpoch",epoch},{"renderer3kmRenderable",render.Renderable.Length},{"current6kmRendererAndCodecBlocked",true},{"elapsedMs",watch.ElapsedMilliseconds},{"runtimeSourceHashes",JObject.FromObject(beforeAssets)},{"limitations","Actual Editor data/codec/renderer-plan verification only.6kmfixture hasY0footprints, not a prepared world. Native meshes/physics/performance/LAN and source policy approval remain pending."}};
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString(Formatting.Indented));File.WriteAllText(Path.Combine(output,"three-km-current-codec-candidate.json"),canonical);Debug.Log("REGIONAL UNITY COMPLETE "+checks+" checks: "+output);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        [Serializable] sealed class BuildingArray {public GeoBuilding[] buildings=Array.Empty<GeoBuilding>();}
    }
}
