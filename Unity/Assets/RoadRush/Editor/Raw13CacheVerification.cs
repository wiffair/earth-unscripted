using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Text;using System.Threading;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;
namespace RoadRush.Editor
{
    public static class Raw13CacheChecks
    {
        [Serializable]public sealed class Report{public string status,scope="Verified frozen raw13 source/cache only; no prepared admission, original database/provider access or normal cache hook";public int checks;public string sourceDigest,cacheKey,documentSha;public bool originalSourceFilesUnchanged;public List<string> passed=new List<string>();}
        static void Check(Report r,bool ok,string message){if(!ok)throw new InvalidOperationException("RAW13 CACHE FAIL "+message);r.checks++;r.passed.Add(message);}
        static void Reject(Report r,Action run,string message)
        {try{run();}catch(Exception e)when(e is InvalidDataException||e is OperationCanceledException){Check(r,true,message);return;}throw new InvalidOperationException("RAW13 CACHE accepted "+message);}
        static string Entry(Raw13CacheStore store,VerifiedRaw13Input input)=>Path.Combine(store.FamilyDirectory,input.CacheKey);
        static void NoStage(Report r,Raw13CacheStore store,string message)=>Check(r,!Directory.Exists(store.FamilyDirectory)||Directory.GetDirectories(store.FamilyDirectory,".stage-*").Length==0,message);
        public static Report Execute(string output,string compatibility,string actualFolder,string layoutFolder)
        {
            Directory.CreateDirectory(output);var report=new Report();
            try
            {
                var input=VerifiedRaw13Input.VerifyFrozenPantside3km(actualFolder,layoutFolder,compatibility,CancellationToken.None);report.sourceDigest=input.SourceDigest;report.cacheKey=input.CacheKey;report.documentSha=input.DocumentSha256;
                var store=new Raw13CacheStore(Path.Combine(output,"Normal"));Check(report,store.Load(input,CancellationToken.None)==null,"fresh independent Raw13-v1 cache misses");var committed=store.Commit(input,CancellationToken.None);var doc=committed.TakeOwnedDocument();
                Check(report,doc.geography.roads.Length==799&&doc.geography.buildings.Length==1793,"real frozen3km799ways1793buildings retained");
                Check(report,!doc.geography.preparedWidths&&doc.geography.roadWidthSchema==0&&!doc.geography.railwayProfilesPrepared&&doc.roadAuthority.schema==0,"cache remains raw/unprepared");
                Check(report,doc.geography.PreparedRoadWidths==null&&doc.geography.roads.All(r=>r.sourceNodeIds.Length==r.points.Length),"no Prepare/field and complete original node mapping");
                Check(report,RoadSourceIdentity.Compute(doc.geography.roads)==input.SourceDigest,"independent source2 fingerprint survives cache");
                var again=store.Commit(input,CancellationToken.None);Check(report,again!=null&&Directory.GetDirectories(store.FamilyDirectory).Length==1,"idempotent same-source commit creates one complete entry");NoStage(report,store,"no unpublished stage after success");
                var first=store.Load(input,CancellationToken.None).TakeOwnedDocument();var second=store.Load(input,CancellationToken.None).TakeOwnedDocument();float y=second.geography.roads[0].points[0].y;first.geography.roads[0].points[0].y+=1;
                Check(report,second.geography.roads[0].points[0].y==y&&!ReferenceEquals(first.geography.roads,second.geography.roads),"independent load ownership; worker cannot alias another result");
                Check(report,World13DocumentCodec.Hash(File.ReadAllBytes(Path.Combine(Entry(store,input),"world.json")))==input.DocumentSha256,"owned mutation never writes cache/source");
                try{committed.TakeOwnedDocument();throw new Exception("ownership repeat accepted");}catch(InvalidOperationException){Check(report,true,"one-time loaded-document handoff");}
                foreach(var phase in new[]{Raw13CacheStore.Step.StageCreated,Raw13CacheStore.Step.BodyFlushed,Raw13CacheStore.Step.EvidenceFlushed,Raw13CacheStore.Step.CommitFlushed,Raw13CacheStore.Step.BeforePublish})
                {
                    var cancelled=new Raw13CacheStore(Path.Combine(output,"Cancel-"+phase));using(var cancel=new CancellationTokenSource())Reject(report,()=>cancelled.Commit(input,cancel.Token,null,s=>{if(s==phase)cancel.Cancel();}),"cancellation "+phase);
                    Check(report,!Directory.Exists(Entry(cancelled,input)),"no partial published entry "+phase);NoStage(report,cancelled,"only owned cancelled stage disposed "+phase);
                }
                var before=new Raw13CacheStore(Path.Combine(output,"AlreadyCancelled"));using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject(report,()=>before.Commit(input,cancel.Token),"pre-cancelled request creates no source result");}Check(report,!Directory.Exists(before.FamilyDirectory),"pre-cancelled request creates no family");
                var late=new Raw13CacheStore(Path.Combine(output,"LateCancel"));using(var cancel=new CancellationTokenSource())Reject(report,()=>late.Commit(input,cancel.Token,null,s=>{if(s==Raw13CacheStore.Step.Published)cancel.Cancel();}),"late cancellation returns no visible result");
                Check(report,late.Load(input,CancellationToken.None)!=null,"late committed cache is complete/valid, not partial");NoStage(report,late,"late cancel does not remove committed entry");
                bool current=true;var stale=new Raw13CacheStore(Path.Combine(output,"StaleGeneration"));Reject(report,()=>stale.Commit(input,CancellationToken.None,()=>current,s=>{if(s==Raw13CacheStore.Step.BodyFlushed)current=false;}),"stale request generation cannot publish");Check(report,!Directory.Exists(Entry(stale,input)),"stale request has no final entry");NoStage(report,stale,"stale request owns no leftover stage");
                var corrupt=new Raw13CacheStore(Path.Combine(output,"CorruptBody"));corrupt.Commit(input,CancellationToken.None);string body=Path.Combine(Entry(corrupt,input),"world.json");byte[] damaged=File.ReadAllBytes(body);damaged[100]^=1;File.WriteAllBytes(body,damaged);
                Reject(report,()=>corrupt.Load(input,CancellationToken.None),"body corruption fails trusted expectedSHA");Reject(report,()=>corrupt.Commit(input,CancellationToken.None),"existing corrupt cache never silently replaced");Check(report,File.ReadAllBytes(body).SequenceEqual(damaged),"corrupt evidence retained unchanged");
                var forged=new Raw13CacheStore(Path.Combine(output,"ForgedHeader"));forged.Commit(input,CancellationToken.None);File.WriteAllText(Path.Combine(Entry(forged,input),"commit.json"),"{\"verified\":true,\"phase\":\"prepared\"}");Reject(report,()=>forged.Load(input,CancellationToken.None),"disk verified/prepared flags cannot grant trust");
                var evidence=new Raw13CacheStore(Path.Combine(output,"ForgedEvidence"));evidence.Commit(input,CancellationToken.None);File.WriteAllText(Path.Combine(Entry(evidence,input),"source-evidence.json"),"{\"verified\":true}");Reject(report,()=>evidence.Load(input,CancellationToken.None),"source receipt is compared to external trusted capability");
                var changed=new Raw13CacheStore(Path.Combine(output,"RehashedPayload"));changed.Commit(input,CancellationToken.None);string directory=Entry(changed,input);var prepared=JObject.Parse(Encoding.UTF8.GetString(input.CopyDocument()));prepared["roadAuthority"]["schema"]=1;byte[] swapped=Encoding.UTF8.GetBytes(prepared.ToString(Newtonsoft.Json.Formatting.None));File.WriteAllBytes(Path.Combine(directory,"world.json"),swapped);var header=JObject.Parse(File.ReadAllText(Path.Combine(directory,"commit.json")));header["documentSha256"]=World13DocumentCodec.Hash(swapped);File.WriteAllText(Path.Combine(directory,"commit.json"),header.ToString(Newtonsoft.Json.Formatting.None));Reject(report,()=>changed.Load(input,CancellationToken.None),"rehashed self-declared prepared payload still cannot replace expected source");
                // Revalidate real pin changes using OWNED copies, never mutate the frozen originals.
                string copiedActual=Path.Combine(output,"CopiedSources/Actual"),copiedLayout=Path.Combine(output,"CopiedSources/Layout");Directory.CreateDirectory(copiedActual);Directory.CreateDirectory(copiedLayout);
                foreach(string name in new[]{"pantside-3000-source.json","pantside-3000-raw-converted.json","pantside-3000-full-regional-raw-review.json","pantside-3000-source-provenance-review.json","readonly-source-capture.json","pantside-3000-grid-f32.bin"})File.Copy(Path.Combine(actualFolder,name),Path.Combine(copiedActual,name));
                foreach(string name in new[]{"pantside-3000-preserved-ways-raw.json","pantside-3000-layout-receipt.json","preserve_ways.py"})File.Copy(Path.Combine(layoutFolder,name),Path.Combine(copiedLayout,name));
                var copied=VerifiedRaw13Input.VerifyFrozenPantside3km(copiedActual,copiedLayout,compatibility,CancellationToken.None);Check(report,copied.CacheKey==input.CacheKey,"source relocation does not change content cache identity");
                string source=Path.Combine(copiedActual,"pantside-3000-source.json");var bad=File.ReadAllBytes(source);bad[100]^=1;File.WriteAllBytes(source,bad);var pinned=new Raw13CacheStore(Path.Combine(output,"ChangedOriginalPins"));Reject(report,()=>pinned.Commit(copied,CancellationToken.None),"source changed after verification cannot publish");Check(report,!Directory.Exists(pinned.FamilyDirectory),"stale original pins create no cache");
                input.RevalidateSourcePins(CancellationToken.None);report.originalSourceFilesUnchanged=true;Check(report,true,"all frozen original source artifacts unchanged");report.status="PASS";
            }
            catch(Exception e){report.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}
            finally{File.WriteAllText(Path.Combine(output,"receipt.json"),WorldFieldJson.Write(report,true));}
            return report;
        }
    }
    public static class Raw13CacheVerification
    {
        public static void Run()
        {
            int exit=1;string output=Path.GetFullPath("Builds/Verification/Raw13Cache/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            string career=Path.Combine(Application.persistentDataPath,"road-rush-save.json"),before=File.Exists(career)?RoadRush.Core.DataPackageRules.HashFile(career):null;
            try
            {
                if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new InvalidOperationException("Isolated source/cache proof only");
                var report=Raw13CacheChecks.Execute(output,"RoadRush/"+Application.version+"/geometry13","/private/tmp/rr019-integration-drafts/ActualPantside","/private/tmp/rr018-fresh-layout");
                if((File.Exists(career)?RoadRush.Core.DataPackageRules.HashFile(career):null)!=before)throw new InvalidOperationException("Protected real career changed");Debug.Log("RAW13 CACHE PASS "+report.checks+" "+output);exit=0;
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());}
            EditorApplication.Exit(exit);
        }
    }
}
