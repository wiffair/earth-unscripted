using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Text;using System.Threading;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class Raw13RestartChecks
    {
        [Serializable]public sealed class Report{public string status,scope="Catalog-selected reviewed RAW13 restart only; no original artifact/provider/database access, commit authority, prepared admission or normal hooks";public int checks;public string cacheKey,documentSha,evidenceSha;public List<string> passed=new List<string>();}
        const string Catalog="pantside-3km-frozen-source-1074-v1",Compatibility="EarthUnscripted/0.19.0/geometry13";
        static void Check(Report r,bool value,string why){if(!value)throw new InvalidOperationException("RAW13 RESTART FAIL "+why);r.checks++;r.passed.Add(why);}
        static void Reject(Report r,Action action,string why)
        {try{action();}catch(Exception e)when(e is InvalidDataException||e is OperationCanceledException){Check(r,true,why);return;}throw new InvalidOperationException("RAW13 RESTART accepted "+why);}
        static string Entry(string root,ReviewedRaw13CacheIdentity id)=>Path.Combine(root,"Raw13-v1",id.CacheKey);
        static void CopyBundle(string source,string root,ReviewedRaw13CacheIdentity id)
        {string target=Entry(root,id);Directory.CreateDirectory(target);foreach(string name in new[]{"world.json","source-evidence.json","commit.json"})File.Copy(Path.Combine(source,name),Path.Combine(target,name),false);}
        static string[] BundlePins(string directory)=>new[]{"commit.json","source-evidence.json","world.json"}.Select(n=>n+":"+World13DocumentCodec.Hash(File.ReadAllBytes(Path.Combine(directory,n)))).ToArray();
        public static Report Execute(string output,string reviewedBundle)
        {
            output=Path.GetFullPath(output);if(!output.StartsWith("/private/tmp/",StringComparison.Ordinal)||Directory.Exists(output))throw new InvalidOperationException("Fresh isolated /private/tmp output required");Directory.CreateDirectory(output);var r=new Report();
            try
            {
                var id=ReviewedRaw13CacheIdentity.FromReviewedCatalog(Catalog,Compatibility);r.cacheKey=id.CacheKey;r.documentSha=id.DocumentSha256;r.evidenceSha=id.EvidenceSha256;
                // Test input is the already-reviewed actual Unity cache bundle, not
                // original provider/converter files. Its exact bytes are catalog checked.
                string[] before=BundlePins(reviewedBundle);var baseline=new ReviewedRaw13CacheReader(Path.GetDirectoryName(Path.GetDirectoryName(reviewedBundle)));
                Check(r,baseline.Load(id,CancellationToken.None)!=null,"exact reviewed Unity bundle validates independently of its disk header claims");
                Reject(r,()=>ReviewedRaw13CacheIdentity.FromReviewedCatalog("unreviewed",Compatibility),"unknown catalog ID rejected");
                Reject(r,()=>ReviewedRaw13CacheIdentity.FromReviewedCatalog(Catalog,"EarthUnscripted/0.18.0/geometry13"),"stale compatibility selection rejected");
                Reject(r,()=>ReviewedRaw13CacheIdentity.FromReviewedCatalog(Catalog,"EarthUnscripted/0.19.0/geometry12"),"legacy geometry12 cannot select raw13 identity");
                Check(r,typeof(ReviewedRaw13CacheIdentity).GetConstructors().Length==0&&typeof(ReviewedRaw13CacheIdentity).GetProperties().All(p=>!p.CanWrite),"identity is immutable and has no public constructor");
                Check(r,!typeof(ReviewedRaw13CacheReader).GetMethods().Any(m=>m.Name=="Commit")&&!typeof(Raw13CacheStore).GetMethods().Any(m=>m.Name=="Commit"&&m.GetParameters().Any(p=>p.ParameterType==typeof(ReviewedRaw13CacheIdentity))),"read-only identity grants no producer/commit authority");
                string miss=Path.Combine(output,"Missing");Check(r,new ReviewedRaw13CacheReader(miss).Load(id,CancellationToken.None)==null&&!Directory.Exists(miss),"cold cache miss writes no directory");
                string cold=Path.Combine(output,"ColdRestart");CopyBundle(reviewedBundle,cold,id);string[] cacheBefore=BundlePins(Entry(cold,id));
                Check(r,Directory.GetFileSystemEntries(cold).Select(Path.GetFileName).SequenceEqual(new[]{"Raw13-v1"}),"restart fixture contains cache only, no original source artifacts");
                var reader=new ReviewedRaw13CacheReader(cold);var wrapper=reader.Load(id,CancellationToken.None);var doc=wrapper.TakeOwnedDocument();
                Check(r,doc.geography.roads.Length==799&&doc.geography.buildings.Length==1793,"cold raw restart preserves complete reviewed3km geography");
                Check(r,doc.roadAuthority.schema==0&&!doc.geography.preparedWidths&&!doc.geography.railwayProfilesPrepared&&doc.geography.PreparedRoadWidths==null,"restart remains raw/unprepared with no field reconstruction");
                Check(r,RoadSourceIdentity.Compute(doc.geography.roads)==id.SourceDigest&&doc.geography.roads.All(x=>x.sourceNodeIds.Length==x.points.Length),"catalog-bound original source2 and node arrays preserved");
                var second=reader.Load(id,CancellationToken.None).TakeOwnedDocument();float y=second.geography.roads[0].points[0].y;doc.geography.roads[0].points[0].y+=1;
                Check(r,second.geography.roads[0].points[0].y==y&&!ReferenceEquals(doc.geography.roads,second.geography.roads),"fresh restart ownership cannot alias another result");
                try{wrapper.TakeOwnedDocument();throw new Exception("second take accepted");}catch(InvalidOperationException){Check(r,true,"restart transfers document ownership only once");}
                Check(r,cacheBefore.SequenceEqual(BundlePins(Entry(cold,id))),"owned document mutation does not write cache bytes");
                foreach(var step in new[]{ReviewedRaw13CacheReader.Step.BeforeRead,ReviewedRaw13CacheReader.Step.CommitVerified,ReviewedRaw13CacheReader.Step.EvidenceVerified,ReviewedRaw13CacheReader.Step.BodyVerified,ReviewedRaw13CacheReader.Step.Decoded})
                {
                    using(var cancel=new CancellationTokenSource())Reject(r,()=>reader.Load(id,cancel.Token,null,s=>{if(s==step)cancel.Cancel();}),"cancel returns no owned result at "+step);
                    bool current=true;Reject(r,()=>reader.Load(id,CancellationToken.None,()=>current,s=>{if(s==step)current=false;}),"stale generation returns no owned result at "+step);
                }
                using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject(r,()=>reader.Load(id,cancel.Token),"pre-cancelled cold load rejected");}
                Check(r,reader.Load(id,CancellationToken.None)!=null&&cacheBefore.SequenceEqual(BundlePins(Entry(cold,id))),"cancel/stale requests preserve complete cache and retry succeeds");
                foreach(string name in new[]{"world.json","source-evidence.json","commit.json"})
                {
                    string corrupt=Path.Combine(output,"Corrupt-"+name);CopyBundle(reviewedBundle,corrupt,id);string path=Path.Combine(Entry(corrupt,id),name);byte[] bytes=File.ReadAllBytes(path);bytes[bytes.Length/2]^=1;File.WriteAllBytes(path,bytes);
                    Reject(r,()=>new ReviewedRaw13CacheReader(corrupt).Load(id,CancellationToken.None),"exact catalog checksum rejects corrupted "+name);
                    Check(r,File.ReadAllBytes(path).SequenceEqual(bytes),"corruption retained without repair/write "+name);
                }
                string forged=Path.Combine(output,"ForgedRehash");CopyBundle(reviewedBundle,forged,id);string directory=Entry(forged,id);var payload=JObject.Parse(File.ReadAllText(Path.Combine(directory,"world.json")));payload["roadAuthority"]["schema"]=1;byte[] swapped=Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));File.WriteAllBytes(Path.Combine(directory,"world.json"),swapped);var header=JObject.Parse(File.ReadAllText(Path.Combine(directory,"commit.json")));header["documentSha256"]=World13DocumentCodec.Hash(swapped);header["phase"]="verified-raw-source";File.WriteAllText(Path.Combine(directory,"commit.json"),header.ToString(Newtonsoft.Json.Formatting.None));
                Reject(r,()=>new ReviewedRaw13CacheReader(forged).Load(id,CancellationToken.None),"rehashed self-declared header/prepared body cannot bootstrap trust");
                string incomplete=Path.Combine(output,"Incomplete");CopyBundle(reviewedBundle,incomplete,id);File.Delete(Path.Combine(Entry(incomplete,id),"source-evidence.json"));Reject(r,()=>new ReviewedRaw13CacheReader(incomplete).Load(id,CancellationToken.None),"incomplete cache rejected without reconstruction");
                string foreign=Path.Combine(output,"Foreign");CopyBundle(reviewedBundle,foreign,id);File.WriteAllText(Path.Combine(Entry(foreign,id),"identity.json"),"{}");Reject(r,()=>new ReviewedRaw13CacheReader(foreign).Load(id,CancellationToken.None),"disk identity extension cannot add trust");
                string nested=Path.Combine(output,"NestedDirectory");CopyBundle(reviewedBundle,nested,id);Directory.CreateDirectory(Path.Combine(Entry(nested,id),"nested"));Reject(r,()=>new ReviewedRaw13CacheReader(nested).Load(id,CancellationToken.None),"nested cache directories rejected by bounded enumeration");
                foreach(int delta in new[]{-1,1})
                {
                    string sized=Path.Combine(output,"BodySize"+delta);CopyBundle(reviewedBundle,sized,id);string path=Path.Combine(Entry(sized,id),"world.json");using(var stream=new FileStream(path,FileMode.Open,FileAccess.Write))stream.SetLength(id.DocumentBytes+delta);Reject(r,()=>new ReviewedRaw13CacheReader(sized).Load(id,CancellationToken.None),"catalog exact body size rejects delta "+delta);
                }
                foreach(string name in new[]{"commit.json","source-evidence.json"})
                {
                    string malformed=Path.Combine(output,"MalformedUTF8-"+name);CopyBundle(reviewedBundle,malformed,id);string path=Path.Combine(Entry(malformed,id),name);byte[] bytes=File.ReadAllBytes(path);bytes[0]=255;File.WriteAllBytes(path,bytes);Reject(r,()=>new ReviewedRaw13CacheReader(malformed).Load(id,CancellationToken.None),"malformed UTF8 receipt rejected "+name);
                    string oversized=Path.Combine(output,"OversizedReceipt-"+name);CopyBundle(reviewedBundle,oversized,id);path=Path.Combine(Entry(oversized,id),name);using(var stream=new FileStream(path,FileMode.Open,FileAccess.Write))stream.SetLength(16385);Reject(r,()=>new ReviewedRaw13CacheReader(oversized).Load(id,CancellationToken.None),"oversized receipt rejected before allocation "+name);
                }
                string excess=Path.Combine(output,"ExcessEntries");CopyBundle(reviewedBundle,excess,id);for(int i=0;i<64;i++)File.WriteAllText(Path.Combine(Entry(excess,id),"foreign-"+i),"{}");Reject(r,()=>new ReviewedRaw13CacheReader(excess).Load(id,CancellationToken.None),"excess cache entries rejected by bounded enumeration");
                string wrong=Path.Combine(output,"WrongDirectory");CopyBundle(reviewedBundle,wrong,id);Directory.Move(Entry(wrong,id),Path.Combine(wrong,"Raw13-v1",new string('0',64)));Check(r,new ReviewedRaw13CacheReader(wrong).Load(id,CancellationToken.None)==null,"reader does not discover/select arbitrary cache keys");
                Check(r,before.SequenceEqual(BundlePins(reviewedBundle)),"reviewed original cache bundle unchanged");r.status="PASS";
            }
            catch(Exception e){r.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}
            finally{File.WriteAllText(Path.Combine(output,"receipt.json"),WorldFieldJson.Write(r,true));}return r;
        }
    }
    public static class Raw13RestartVerification
    {
        public static void Run()
        {
            int exit=1;string output="/private/tmp/rr019-raw-restart-unity-"+DateTime.UtcNow.Ticks;
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=File.Exists(career)?EarthUnscripted.Core.DataPackageRules.HashFile(career):null;
            try
            {
                if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new InvalidOperationException("Isolated raw restart only");
                var r=Raw13RestartChecks.Execute(output,"/private/tmp/EarthUnscriptedProject/Builds/Verification/Raw13Cache/639295801893349970/Normal/Raw13-v1/fb4bb364b93cb508a742fc647d60245dedf988cf0cea193b192a6ab33d35fa05");
                if((File.Exists(career)?EarthUnscripted.Core.DataPackageRules.HashFile(career):null)!=before)throw new InvalidOperationException("Protected real career changed");Debug.Log("RAW13 RESTART PASS "+r.checks+" "+output);exit=0;
            }
            catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);
        }
    }
}
