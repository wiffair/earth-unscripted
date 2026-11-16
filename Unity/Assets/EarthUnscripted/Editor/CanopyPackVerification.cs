using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    public static class CanopyPackVerification
    {
        static int checks;static string root;
        static void Check(bool ok,string text){if(!ok)throw new Exception("CANOPY FAIL: "+text);checks++;Debug.Log("CANOPY PASS: "+text);}
        static void Reject(Action action,string text){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,text);}
        static GeoRegion Fixture()
        {
            var region=new GeoRegion{id="canopy-fixture",name="Canopy fixture",preparedWidths=true,minX=-500,minZ=-500,width=1000,depth=1000,nx=2,nz=2,heights=new float[4],landNx=2,landNz=2,landCover=new int[4],roads=new[]{new GeoRoad{id="lane",start="a",end="b",width=2,limit=30,points=new[]{new Vector3(-400,0,25),new Vector3(400,0,25)}},new GeoRoad{id="wide",start="c",end="d",width=40,limit=50,points=new[]{new Vector3(-400,0,40),new Vector3(400,0,40)}}},waters=new[]{new GeoWater{width=12,points=new[]{new Vector3(-400,0,-100),new Vector3(400,0,-100)}}},areas=Array.Empty<GeoArea>(),buildings=new[]{new GeoBuilding{id="house",height=6,points=new[]{new Vector3(90,0,90),new Vector3(110,0,90),new Vector3(110,0,110),new Vector3(90,0,110),new Vector3(90,0,90)}}},route=new[]{new Vector3(-300,0,25),new Vector3(300,0,25)}};region.Prepare();return region;
        }
        static GeoTree Tree(float x,float z)=>new GeoTree{id="fixture-tree",x=x,z=z,height=10,crownRadius=2,seed=DetailCanopyPacks.Seed("fixture-tree")};
        public static void Run()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();int sourceAt=Array.IndexOf(args,"-earthUnscriptedCanopyPack");string selectedSource=sourceAt>=0&&sourceAt+1<args.Length?Path.GetFullPath(args[sourceAt+1]):null;string outputName=selectedSource==null?"CanopyPack":"CanopyPack-"+DataPackageRules.Hash(System.Text.Encoding.UTF8.GetBytes(selectedSource)).Substring(0,8);
                root=Path.GetFullPath("Builds/Verification/"+outputName);Directory.CreateDirectory(root);PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
                var fixture=Fixture();var clear=new DetailCanopyPacks.Clearance(fixture);
                Check(clear.Allows(Tree(-100,150)),"clear land accepts an entire bounded crown");Check(!clear.Allows(Tree(-100,18)),"all nearby widths exclude wide parallel carriageway even when narrow lane is nearer");Check(!clear.Allows(Tree(100,100))&&!clear.Allows(Tree(113,100)),"building interior and crown-margin overlap are excluded");Check(!clear.Allows(Tree(-100,-100)),"river and crown footprint are excluded");Check(!clear.Allows(Tree(-300,12)),"route spawn margin is excluded");
                fixture.ServicePad=new Vector3(200,0,200);fixture.HasServicePad=true;clear=new DetailCanopyPacks.Clearance(fixture);Check(!clear.Allows(Tree(230,200)),"prepared service forecourt is excluded");
                var polygon=new GeoCanopyCoverage{minX=-10,minZ=-10,maxX=10,maxZ=10,boundary=new[]{new Vector3(0,0,-10),new Vector3(10,0,0),new Vector3(0,0,10),new Vector3(-10,0,0),new Vector3(0,0,-10)}};
                Check(DetailCanopyPacks.Contains(polygon,0,0,2)&&!DetailCanopyPacks.Contains(polygon,9,9),"projected tile boundary prevents fallback suppression in unsurveyed bounding-box corners");
                string original=selectedSource??"/private/tmp/EarthUnscriptedDetailCompiled/PantsideCanopyReviewed";var pack=new DetailCanopyPack(original);Check(pack.Identity.Length==64,"reviewed relative-height manifest and exact payload validate");
                Reject(()=>new DetailCanopyPack("/private/tmp/EarthUnscriptedDetailCompiled/PantsideCanopy"),"unpromoted research prototype cannot activate");
                string bad=Path.Combine(root,"bad-pack");Directory.CreateDirectory(bad);foreach(string name in new[]{"canopy-pack.json","canopy.json"})File.Copy(Path.Combine(original,name),Path.Combine(bad,name),true);File.AppendAllText(Path.Combine(bad,"canopy.json")," ");Reject(()=>new DetailCanopyPack(bad),"changed payload bytes are rejected before scenery mutation");
                File.Copy(Path.Combine(original,"canopy.json"),Path.Combine(bad,"canopy.json"),true);var meta=JObject.Parse(File.ReadAllText(Path.Combine(bad,"canopy-pack.json")));meta["offlineAllowed"]=false;File.WriteAllText(Path.Combine(bad,"canopy-pack.json"),meta.ToString());Reject(()=>new DetailCanopyPack(bad),"missing offline reuse permission rejects optional pack");
                var payload=JObject.Parse(File.ReadAllText(Path.Combine(original,"canopy.json")));payload["estimatedCanopyCandidates"][0]["height"]=float.NaN;File.WriteAllText(Path.Combine(bad,"canopy.json"),payload.ToString());meta["offlineAllowed"]=true;meta["bytes"]=new FileInfo(Path.Combine(bad,"canopy.json")).Length;meta["sha256"]=DataPackageRules.HashFile(Path.Combine(bad,"canopy.json"));File.WriteAllText(Path.Combine(bad,"canopy-pack.json"),meta.ToString());Reject(()=>new DetailCanopyPack(bad),"hash-consistent invalid relative height is rejected");
                string source=File.Exists("Builds/Verification/Pantside/region-id.txt")?Path.Combine("Builds/Verification/Pantside/Regions",File.ReadAllText("Builds/Verification/Pantside/region-id.txt")+".json"):Directory.GetFiles("Builds/Verification/Pantside/Regions","download-*.json").First();
                var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(source));region.trees=Array.Empty<GeoTree>();region.canopyCoverage=Array.Empty<GeoCanopyCoverage>();region.Prepare();DetailCanopyPacks.PrepareDrivingWidths(region);new DetailBuildingPack("/private/tmp/EarthUnscriptedDetailCompiled/PantsideBuildings").Apply(region);
                Check(RegionCache.Safe(DetailCanopyPacks.CacheId("download-"+new string('a',80),new[]{pack})),"country/building/canopy cache identity remains within safe filename budget");string bareEpoch=WorldSnapshotCodec.Epoch(region);int added=pack.Apply(region);Check(added>0&&added<=3486,"real Pantside candidates activate after final prepared-road and merged-building clearance");
                Check(pack.Apply(region)==0&&region.trees.Length==added,"reapplying identical immutable tile is idempotent");
                foreach(string name in new[]{"canopy-pack.json","canopy.json"})File.Copy(Path.Combine(original,name),Path.Combine(bad,name),true);meta=JObject.Parse(File.ReadAllText(Path.Combine(bad,"canopy-pack.json")));meta["attribution"]="Changed tile revision with valid source provenance";File.WriteAllText(Path.Combine(bad,"canopy-pack.json"),meta.ToString());string pinned=JsonUtility.ToJson(region);Check(!DetailCanopyPacks.ApplySafely(new DetailCanopyPack(bad),region)&&JsonUtility.ToJson(region)==pinned,"different revision of already pinned tile cannot mutate or duplicate active coverage");
                Check(region.canopyCoverage.Length==1&&region.canopyCoverage[0].candidateCount==3486&&region.canopyCoverage[0].acceptedCount==added,"source count and accepted/rejection audit persist with coverage");
                Check(region.canopyCoverage[0].densityRejectedCount>0,"dense source stands use explicitly audited bounded density selection");DetailCanopyPacks.ValidateCounts(region.trees);Check(true,"accepted canopy respects region and256-per-chunk limits");
                var clearance=new DetailCanopyPacks.Clearance(region);Check(region.trees.All(clearance.Allows),"all accepted real-world crowns clear current roads buildings water and spawn");
                Check(DetailCanopyPacks.CacheId(region.id,new[]{pack})!=region.id&&bareEpoch!=WorldSnapshotCodec.Epoch(region),"optional canopy changes cache identity and canonical world epoch");
                string canonical=WorldSnapshotCodec.CanonicalJson(region),epoch=WorldSnapshotCodec.Epoch(region),map=WorldSnapshotCodec.NetworkId(region);DetailCanopyPacks.OverrideRoot=Path.Combine(root,"no-local-source");var guest=WorldSnapshotCodec.Decode(canonical,map,epoch);
                Check(guest.trees.Length==added&&JsonUtility.ToJson(new TreeList{trees=guest.trees})==JsonUtility.ToJson(new TreeList{trees=region.trees})&&WorldSnapshotCodec.Epoch(guest)==epoch,"guest reconstructs identical canonical trees and epoch with no local source pack");
                DetailCanopyPacks.FinalizeForWorld(guest);Check(WorldSnapshotCodec.Epoch(guest)==epoch,"guest finalization never independently changes host tree selection");
                string snapshot=Path.Combine(root,"peer-world.json");File.WriteAllText(snapshot,canonical);var manifest=new DataFileManifest{id="world-"+epoch,kind="region-snapshot",pack="earthunscripted-worlds",fromVersion=map,toVersion=epoch,source="Canopy verification host",licence="ODbL-1.0; Copernicus DEM; OGL-3.0 where attributed",bytes=new FileInfo(snapshot).Length,sha256=DataPackageRules.HashFile(snapshot)};
                using(var signer=new DataSigningKey()){signer.Sign(manifest);var imported=WorldSnapshotCodec.Import(snapshot,manifest,signer.Fingerprint);Check(imported.trees.Length==added&&WorldSnapshotCodec.Epoch(imported)==epoch,"signed peer snapshot import preserves canopy and its epoch");}
                var mutated=JObject.Parse(canonical);mutated["trees"][0]["x"]=region.roads[0].points[0].x;mutated["trees"][0]["z"]=region.roads[0].points[0].z;string invalid=mutated.ToString(Newtonsoft.Json.Formatting.None);Reject(()=>WorldSnapshotCodec.Decode(invalid,map,epoch),"peer cannot move accepted tree onto a road or bypass epoch validation");
                region.ServicePad=new Vector3(region.trees[0].x,0,region.trees[0].z);region.HasServicePad=true;int before=region.trees.Length;DetailCanopyPacks.FinalizeForWorld(region);Check(region.trees.Length<before&&region.canopyCoverage.Sum(c=>c.acceptedCount)==region.trees.Length,"late host service planning filters conflicting trees and updates audit before publication");
                File.WriteAllText(Path.Combine(root,"coverage-audit.json"),JsonUtility.ToJson(region.canopyCoverage[0],true));File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS {checks} canopy pack/clearance/cache/epoch/signed snapshot checks\naccepted={added}\n"+canonical.Length+" canonical characters\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{DetailCanopyPacks.OverrideRoot=null;}
        }
        [Serializable] sealed class TreeList{public GeoTree[] trees;}
    }
}
