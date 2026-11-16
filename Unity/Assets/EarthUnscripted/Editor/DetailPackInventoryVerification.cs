using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class DetailPackInventoryVerification
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception("DETAIL INVENTORY FAIL: "+text);checks++;Debug.Log("DETAIL INVENTORY PASS: "+text);}
        static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
        static readonly WorldPlace Place=new WorldPlace{latitude=51.671156,longitude=-3.131748,country_code="GB",size=3000};
        static string BuildingSource=>"/private/tmp/EarthUnscriptedDetailCompiled/PantsideBuildingsV2";
        static string CanopySource=>"/private/tmp/EarthUnscriptedDetailCompiled/PantsideCanopyReviewedV1";
        static string Copy(string root,string name,bool canopy=false,Action<JObject> edit=null)
        {
            string dest=Path.Combine(root,name),stem=canopy?"canopy":"buildings",source=canopy?CanopySource:BuildingSource;Directory.CreateDirectory(dest);
            File.Copy(Path.Combine(source,stem+".json"),Path.Combine(dest,stem+".json"));string header=Path.Combine(dest,stem+"-pack.json");File.Copy(Path.Combine(source,stem+"-pack.json"),header);
            if(edit!=null){var meta=JObject.Parse(File.ReadAllText(header));edit(meta);File.WriteAllText(header,meta.ToString());}return dest;
        }
        static DetailInventorySnapshot Scan(string root,WorldPlace place=null,bool requested=true,CancellationToken token=default)=>DetailPackInventory.Scan(new[]{new DetailInventoryRoot("GB",root)},place??Place,requested,token);
        static void MatchesActualFind(string root,DetailInventorySnapshot result)
        {
            string before=DetailBuildingPacks.OverrideRoot,canopyBefore=DetailCanopyPacks.OverrideRoot;
            try
            {
                DetailBuildingPacks.OverrideRoot=root;DetailCanopyPacks.OverrideRoot=root;
                var actual=DetailBuildingPacks.Find(Place).Select(p=>p.Identity).Concat(DetailCanopyPacks.Find(Place).Select(p=>p.Identity)).OrderBy(x=>x).ToArray();
                var shown=result.Rows.Where(r=>r.SelectedForNextPreparation).Select(r=>r.Identity).OrderBy(x=>x).ToArray();
                Check(actual.SequenceEqual(shown),"inventory next-preparation identities match actual Find");
            }
            finally{DetailBuildingPacks.OverrideRoot=before;DetailCanopyPacks.OverrideRoot=canopyBefore;}
        }
        public static int RunScenarios(string root)
        {
            checks=0;Directory.CreateDirectory(root);var originals=new[]{Path.Combine(BuildingSource,"buildings-pack.json"),Path.Combine(BuildingSource,"buildings.json"),Path.Combine(CanopySource,"canopy-pack.json"),Path.Combine(CanopySource,"canopy.json")}.ToDictionary(p=>p,Hash);
            string basic=Path.Combine(root,"basic");Copy(basic,"houses");Copy(basic,"trees",true);var result=Scan(basic);
            Check(result.SelectionComplete&&result.Rows.Count==2&&result.Rows.All(r=>r.SelectedForNextPreparation),"actual reviewed buildings and canopy are eligible in Pantside");MatchesActualFind(basic,result);
            Check(result.Rows.All(r=>r.KnownRuntimeBytes>0&&r.Licence=="OGL-3.0"&&r.SourceURLs.Count>0&&r.ArchiveBytes==null&&r.RemoteDownloadURL==null&&r.ActivePreparedRevision==null),"truthful measured bytes/licence/source links without invented download or active identity");
            Check(result.Rows.Single(r=>r.Kind=="Buildings").ValidationScope.Contains("during area preparation"),"building payload check does not claim full geometry acceptance");
            Check(Scan(basic,requested:false).Rows.All(r=>!r.SelectedForNextPreparation&&r.State==DetailInventoryState.SelectedRevision),"disabled preference preserves inventory and defers application");
            var unknown=DetailPackInventory.Scan(new[]{new DetailInventoryRoot("GB",basic)},null,true,CancellationToken.None);Check(unknown.Rows.All(r=>r.Coverage==DetailInventoryCoverage.Unknown&&!r.SelectedForNextPreparation),"no selected area means unknown coverage and no promised activation");
            string duplicate=Path.Combine(root,"duplicates");Copy(duplicate,"b");Copy(duplicate,"a");result=Scan(duplicate);Check(result.Rows.Count(r=>r.State==DetailInventoryState.Duplicate)==1&&result.Rows.Single(r=>r.SelectedForNextPreparation).Folder.EndsWith(Path.DirectorySeparatorChar+"a"),"duplicate identity deterministically represented once");MatchesActualFind(duplicate,result);
            var reversed=DetailPackInventory.Scan(new[]{new DetailInventoryRoot("GB",duplicate),new DetailInventoryRoot("GB",basic)},Place,true,CancellationToken.None);var forward=DetailPackInventory.Scan(new[]{new DetailInventoryRoot("GB",basic),new DetailInventoryRoot("GB",duplicate)},Place,true,CancellationToken.None);Check(reversed.Rows.Select(r=>r.Folder+"|"+r.State).SequenceEqual(forward.Rows.Select(r=>r.Folder+"|"+r.State)),"root enumeration order cannot change duplicate or selected status");
            string legacy=Path.Combine(root,"legacy");Copy(legacy,"current");Copy(legacy,"old",edit:m=>{m.Remove("revision");m.Remove("revisionSeries");});Copy(legacy,"trees",true);result=Scan(legacy);Check(result.Rows.Count(r=>r.Kind=="Buildings"&&r.State==DetailInventoryState.Conflict)==2&&result.Rows.Single(r=>r.Kind=="Canopy").SelectedForNextPreparation,"legacy/versioned conflict fails closed for buildings while independent canopy remains available");MatchesActualFind(legacy,result);
            string conflict=Path.Combine(root,"conflict");Copy(conflict,"one");Copy(conflict,"two",edit:m=>m["attribution"]=(string)m["attribution"]+" conflicting notice");result=Scan(conflict);Check(result.Rows.All(r=>r.State==DetailInventoryState.Conflict&&!r.SelectedForNextPreparation),"different manifests declaring identical revision fail closed");MatchesActualFind(conflict,result);
            string series=Path.Combine(root,"series");Copy(series,"one");Copy(series,"two",edit:m=>{m["revision"]=3;m["revisionSeries"]="unrelated";});result=Scan(series);Check(result.Rows.All(r=>r.State==DetailInventoryState.Conflict&&r.Reason.Contains("series")),"unrelated revision series are not ordered");MatchesActualFind(series,result);
            string outside=Path.Combine(root,"outside");Copy(outside,"old");Copy(outside,"new",edit:m=>{m["revision"]=3;m["bounds"]=new JArray(1,1,2,2);});result=Scan(outside);Check(result.Rows.Single(r=>r.Revision==3).State==DetailInventoryState.SelectedRevision&&result.Rows.Single(r=>r.Revision==3).Coverage==DetailInventoryCoverage.OutsideRequestedArea&&result.Rows.All(r=>!r.SelectedForNextPreparation),"choose latest revision before coverage; older in-area version does not reappear");MatchesActualFind(outside,result);
            string corrupt=Path.Combine(root,"corrupt");Copy(corrupt,"working");string bad=Copy(corrupt,"new",edit:m=>m["revision"]=3);File.WriteAllText(Path.Combine(bad,"buildings.json"),"{}");result=Scan(corrupt);Check(result.Rows.Count(r=>r.State==DetailInventoryState.Invalid||r.State==DetailInventoryState.Unreadable)==1&&result.Rows.Single(r=>r.SelectedForNextPreparation).Folder.EndsWith("working"),"bad newer payload remains visible as rejected while existing valid selection is preserved");MatchesActualFind(corrupt,result);
            string limit=Path.Combine(root,"limit");for(int i=0;i<65;i++)Copy(limit,"b"+i.ToString("D2"));Copy(limit,"canopy",true);result=Scan(limit);Check(result.Rows.Count(r=>r.State==DetailInventoryState.RootDiscoveryLimit)==65&&result.Rows.Single(r=>r.Kind=="Canopy").SelectedForNextPreparation,"65building manifests refuse partial building catalogue but canopy policy remains independent");MatchesActualFind(limit,result);
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();result=Scan(basic,token:cancel.Token);Check(result.Cancelled&&!result.SelectionComplete&&result.Rows.All(r=>!r.SelectedForNextPreparation),"cancelled scan cannot publish a partial selected catalogue");}
            result=DetailPackInventory.Scan(Enumerable.Range(0,17).Select(i=>new DetailInventoryRoot("GB",basic)),Place,true,CancellationToken.None);Check(!result.SelectionComplete&&result.Rows.Count==0&&result.Issues.Any(),"root budget refuses partial results");
            string budget=Path.Combine(root,"bytes");for(int i=0;i<5;i++)
            {
                string folder=Copy(budget,"b"+i,edit:m=>m["id"]="byte-fixture-"+i);string payload=Path.Combine(folder,"buildings.json");File.AppendAllText(payload,new string(' ',4000000-(int)new FileInfo(payload).Length));var meta=JObject.Parse(File.ReadAllText(Path.Combine(folder,"buildings-pack.json")));meta["bytes"]=4000000;meta["sha256"]=Hash(payload);File.WriteAllText(Path.Combine(folder,"buildings-pack.json"),meta.ToString());
            }
            result=Scan(budget);Check(!result.SelectionComplete&&result.ValidatedPayloadBytes<=DetailPackInventory.MaximumValidatedPayloadBytes&&result.Rows.All(r=>!r.SelectedForNextPreparation)&&result.Rows.Any(r=>r.State==DetailInventoryState.Deferred),"global validation-byte budget cannot report a misleading partial selected revision");
            string oversized=Path.Combine(root,"oversized"),oversizedFolder=Copy(oversized,"bad");File.WriteAllText(Path.Combine(oversizedFolder,"buildings-pack.json"),new string('x',DetailPackInventory.MaximumManifestBytes+1));result=Scan(oversized);Check(result.Rows.Single().State==DetailInventoryState.Invalid&&result.Rows.Single().PayloadBytes==null,"oversized metadata is rejected before payload inspection");
            string capped=Path.Combine(root,"tile-cap");for(int i=0;i<9;i++)Copy(capped,"b"+i,edit:m=>m["id"]="tile-"+i);result=Scan(capped);Check(result.Rows.Count(r=>r.State==DetailInventoryState.SelectedRevision)==9&&result.Rows.Count(r=>r.SelectedForNextPreparation)==8,"all selected revisions visible while existing final eight-tile cap limits next preparation");MatchesActualFind(capped,result);
            string display=Path.Combine(root,"display-only");string displayFolder=Copy(display,"houses",edit:m=>{m["version"]=new JObject{{"opaque",true}};m["sourceURLs"]=new JArray(new JObject{{"notALink",true}},"javascript:bad","https://www.ordnancesurvey.co.uk/products/os-open-map-local");});string displayHeader=Path.Combine(displayFolder,"buildings-pack.json");File.WriteAllText(displayHeader,File.ReadAllText(displayHeader),new System.Text.UTF8Encoding(true));result=Scan(display);Check(result.Rows.Single().SelectedForNextPreparation&&result.Rows.Single().Version==""&&result.Rows.Single().SourceURLs.Count==1,"BOM and ignored display metadata cannot disagree with actual validator acceptance");MatchesActualFind(display,result);
            string wrongRights=Path.Combine(root,"rights");Copy(wrongRights,"bad",edit:m=>m["peerRedistributionAllowed"]=false);result=Scan(wrongRights);Check(result.Rows.Single().State==DetailInventoryState.Invalid&&!result.Rows.Single().SelectedForNextPreparation,"manifest cannot bypass existing offline/peer permission requirements");
            string countrySearch=Path.Combine(root,"country-search"),country=Path.Combine(countrySearch,"UK");Directory.CreateDirectory(country);Copy(Path.Combine(country,"Detail"),"houses");
            File.WriteAllText(Path.Combine(country,"pack.json"),new JObject{{"country","GB"},{"mapReady",true},{"terrainReady",true}}.ToString());string database=Path.Combine(country,"map.sqlite");File.WriteAllText(database,"Not SQLite; inventory must not read this file");
            using(var locked=new FileStream(database,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            using(var job=new DetailInventoryJob(new DetailInventoryDiscoveryRequest(new[]{countrySearch},null),Place,true))
            {
                result=job.Completion.GetAwaiter().GetResult();Check(result.SelectionComplete&&result.CountryRoots.Count==1&&result.CountryRoots[0].CountryFolder==country&&result.Rows.Count==1,"worker discovers metadata-only country root while database is locked and unreadable as SQLite");
            }
            File.Delete(database);using(var job=new DetailInventoryJob(new DetailInventoryDiscoveryRequest(new[]{countrySearch},null),Place,true)){result=job.Completion.GetAwaiter().GetResult();Check(result.Rows.Single().State==DetailInventoryState.Verified&&!result.Rows.Single().CountryDataAvailable&&!result.Rows.Single().SelectedForNextPreparation,"missing base database preserves pack inventory without promising next preparation");}
            string config=Path.Combine(root,"world-packs.txt");File.WriteAllText(config,new string('x',DetailInventoryDiscovery.MaximumConfigBytes+1));using(var job=new DetailInventoryJob(new DetailInventoryDiscoveryRequest(Array.Empty<string>(),config),Place,true)){result=job.Completion.GetAwaiter().GetResult();Check(!result.SelectionComplete&&result.CountryRoots.Count==0&&result.Issues.Any(),"oversized root config produces explicit incomplete discovery");}
            File.WriteAllText(config,countrySearch+Environment.NewLine);using(var job=new DetailInventoryJob(new DetailInventoryDiscoveryRequest(Array.Empty<string>(),config),Place,true)){result=job.Completion.GetAwaiter().GetResult();Check(result.CountryRoots.Count==1&&result.Rows.Count==1,"worker reads bounded configured country paths without main-thread I/O");}
            using(var job=new DetailInventoryJob(new[]{new DetailInventoryRoot("GB",basic)},Place,true)){var completed=job.Completion.GetAwaiter().GetResult();Check(completed.SelectionComplete&&completed.Rows.Count==2,"asynchronous job completes using captured plain-data inputs");}
            Check(originals.All(p=>Hash(p.Key)==p.Value),"reviewed input archives/manifests/payloads remain byte-identical");return checks;
        }
        public static void Run()
        {
            try{string root=Path.GetFullPath("Builds/Verification/DetailInventory-"+DateTime.UtcNow.Ticks);int count=RunScenarios(root);Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/detail-pack-inventory.txt","PASS "+count+" inventory filesystem/selection/budget/persistence checks\n"+root+"\n");EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
