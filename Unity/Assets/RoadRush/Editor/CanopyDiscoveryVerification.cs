using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class CanopyDiscoveryVerification
    {
        static int checks;static string root;static readonly List<string> warnings=new List<string>();
        static void Check(bool ok,string text){if(!ok)throw new Exception("CANOPY DISCOVERY FAIL: "+text);checks++;Debug.Log("CANOPY DISCOVERY PASS: "+text);}
        static DetailCanopyPack Create(string folder,long? revision,string series="pantside-reviewed",string extra="")
        {
            Directory.CreateDirectory(folder);var meta=JObject.Parse(File.ReadAllText("/private/tmp/RoadRushDetailCompiled/PantsideCanopyReviewed/canopy-pack.json"));meta.Remove("revision");meta.Remove("revisionSeries");if(revision.HasValue){meta["revision"]=revision.Value;meta["revisionSeries"]=series;}meta["attribution"]=(string)meta["attribution"]+extra;
            File.WriteAllText(Path.Combine(folder,"canopy.json"),"{\"estimatedCanopyCandidates\":[]}");meta["candidateCount"]=0;meta["bytes"]=new FileInfo(Path.Combine(folder,"canopy.json")).Length;meta["sha256"]=RoadRush.Core.DataPackageRules.HashFile(Path.Combine(folder,"canopy.json"));File.WriteAllText(Path.Combine(folder,"canopy-pack.json"),meta.ToString());return new DetailCanopyPack(folder);
        }
        static void Warn(string text,string trace,LogType type){if(type==LogType.Warning)warnings.Add(text);}
        public static void Run()
        {
            try
            {
                root=Path.GetFullPath("Builds/Verification/CanopyDiscovery-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");Application.logMessageReceived+=Warn;
                var particle=AssetDatabase.LoadAssetAtPath<Shader>("Assets/RoadRush/Resources/SoftWeatherParticle.shader");Check(particle&&!ShaderUtil.ShaderHasError(particle)&&particle.renderQueue==3000,"soft weather particle shader imports without compiler errors and uses transparent render queue");
                string catalogue=Path.Combine(root,"catalogue");var old=Create(Path.Combine(catalogue,"zz-old"),1);var newer=Create(Path.Combine(catalogue,"aa-new"),2);var place=new WorldPlace{latitude=51.6707,longitude=-3.1365,country_code="GB",size=3000};DetailCanopyPacks.OverrideRoot=catalogue;
                var found=DetailCanopyPacks.Find(place);Check(found.Length==1&&found[0].Identity==newer.Identity,"explicit highest revision wins independently of folder or content-hash order");Check(DetailCanopyPacks.SelectRevisions(new[]{old,newer}).Single().Identity==DetailCanopyPacks.SelectRevisions(new[]{newer,old}).Single().Identity,"catalogue registration order cannot select an older revision");Check(DetailCanopyPacks.CacheId("download-original",new[]{old})!=DetailCanopyPacks.CacheId("download-original",new[]{newer}),"a selected newer immutable revision creates a new prepared cache identity");
                var duplicate=DetailCanopyPacks.SelectRevisions(new[]{newer,newer});Check(duplicate.Length==1,"identical immutable revision references deduplicate");
                var conflict=Create(Path.Combine(root,"conflict"),2,extra:" conflicting provenance");Check(DetailCanopyPacks.SelectRevisions(new[]{old,newer,conflict}).Length==0&&warnings.Last().Contains("same revision"),"different payload identities claiming the same revision fail closed clearly");
                var otherSeries=Create(Path.Combine(root,"other-series"),3,"other-series");Check(DetailCanopyPacks.SelectRevisions(new[]{old,otherSeries}).Length==0&&warnings.Last().Contains("different series"),"unrelated revision series cannot silently replace a tile");
                var legacy=Create(Path.Combine(root,"legacy"),null);var otherLegacy=Create(Path.Combine(root,"legacy-other"),null,extra:" another legacy record");Check(DetailCanopyPacks.SelectRevisions(new[]{legacy}).Single().Identity==legacy.Identity,"a single deployed legacy tile remains usable without inventing revision order");Check(DetailCanopyPacks.SelectRevisions(new[]{legacy,otherLegacy}).Length==0&&DetailCanopyPacks.SelectRevisions(new[]{legacy,newer}).Length==0,"ambiguous legacy and mixed catalogues fail closed instead of sorting arbitrary hashes");
                bool bad=false;try{Create(Path.Combine(root,"invalid"),0);}catch(InvalidDataException){bad=true;}Check(bad,"invalid explicit revision numbers are rejected before selection");
                var partial=JObject.Parse(File.ReadAllText(Path.Combine(root,"invalid","canopy-pack.json")));partial["revision"]=1;partial.Remove("revisionSeries");File.WriteAllText(Path.Combine(root,"invalid","canopy-pack.json"),partial.ToString());bad=false;try{new DetailCanopyPack(Path.Combine(root,"invalid"));}catch(InvalidDataException){bad=true;}Check(bad,"partial ordering metadata cannot masquerade as a trusted revision");
                string over=Path.Combine(root,"over-budget");for(int i=0;i<65;i++){string folder=Path.Combine(over,i.ToString("D3"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"canopy-pack.json"),"{}");}DetailCanopyPacks.OverrideRoot=over;Check(DetailCanopyPacks.Find(place).Length==0&&warnings.Last().Contains("partial revision catalogue"),"bounded discovery rejects an incomplete catalogue instead of hiding a later revision");
                for(int i=0;i<65;i++)Directory.CreateDirectory(Path.Combine(catalogue,"00-nonbuilding-"+i.ToString("D3")));string actualBuilding=Path.Combine(catalogue,"zz-building");Directory.CreateDirectory(actualBuilding);foreach(string name in new[]{"buildings-pack.json","buildings.json"})File.Copy(Path.Combine("/private/tmp/RoadRushDetailCompiled/PantsideBuildings",name),Path.Combine(actualBuilding,name),true);
                DetailBuildingPacks.OverrideRoot=catalogue;Check(DetailBuildingPacks.Find(place).Length==1,"nonbuilding directories cannot consume building-manifest discovery budget");Directory.Delete(actualBuilding,true);
                DetailBuildingPacks.OverrideRoot=catalogue;int warningCount=warnings.Count;Check(DetailBuildingPacks.Find(place).Length==0&&warnings.Count==warningCount,"building discovery skips canopy and unrelated folders without missing-manifest warnings");
                string json=File.ReadAllText("Builds/Verification/CanopyPack/peer-world.json");var region=JsonUtility.FromJson<GeoRegion>(json);region.Prepare();string pinned=WorldSnapshotCodec.CanonicalJson(region);Check(!DetailCanopyPacks.ApplySafely(newer,region)&&WorldSnapshotCodec.CanonicalJson(region)==pinned,"discovering a newer tile cannot mutate an already pinned canonical world");
                var first=new GeoTree{id="a",x=1,z=2,height=4,crownRadius=2,seed=3};var second=new GeoTree{id="b",x=-1,z=3,height=5,crownRadius=3,seed=4};string digest=LanVerificationDriver.CanopyDigest(new[]{first,second});Check(digest==LanVerificationDriver.CanopyDigest(new[]{second,first}),"LAN canopy digest is invariant to enumeration order");
                foreach(string field in new[]{"id","x","z","height","crownRadius","seed"})
                {
                    var changed=new GeoTree{id=first.id,x=first.x,z=first.z,height=first.height,crownRadius=first.crownRadius,seed=first.seed};var info=typeof(GeoTree).GetField(field);if(field=="id")info.SetValue(changed,"different");else if(field=="seed")info.SetValue(changed,5);else info.SetValue(changed,(float)info.GetValue(changed)+.25f);Check(digest!=LanVerificationDriver.CanopyDigest(new[]{changed,second}),"LAN digest binds canonical tree "+field);
                }
                File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS {checks} immutable canopy discovery revision/cache/pinning and LAN digest checks\n");File.WriteAllText("Builds/Verification/canopy-discovery.txt",$"PASS {checks} checks\n"+root+"\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{Application.logMessageReceived-=Warn;DetailCanopyPacks.OverrideRoot=null;DetailBuildingPacks.OverrideRoot=null;}
        }
    }
}
