using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using UnityEditor;using UnityEngine;
namespace RoadRush.Editor
{
    public static class CanonicalRaw13CacheChecks
    {
        [Serializable]public sealed class Report{public string status,scope="Raw13Canonical-v1 fixed reviewed input only; live capability required. No cold trust, prepared, boot, network, provider or national database access.",key,evidenceSHA,commitSHA,bodySHA;public int checks;public string[] passed;}
        #if UNITY_EDITOR_OSX || !UNITY_5_3_OR_NEWER
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)]static extern int symlink(string target,string link);
#endif
        static readonly System.Collections.Generic.List<string> Passed=new System.Collections.Generic.List<string>();
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("CANONICAL CACHE FAIL "+why);Passed.Add(why);}
        static void Reject(Action action,string why){try{action();}catch(Exception e)when(e is InvalidDataException||e is OperationCanceledException||e is InvalidOperationException||e is ArgumentException){Passed.Add(why);return;}throw new Exception("CANONICAL CACHE accepted "+why);}
        static CanonicalRaw13Document Fresh(ReviewedCanonicalRaw13Input input)=>CanonicalRaw13Codec.Read(input,input.CopyDocumentBytes());
        static string Entry(CanonicalRaw13Cache cache)=>Path.Combine(cache.FamilyDirectory,CanonicalRaw13Cache.CacheKey);
        static string Stage(CanonicalRaw13Cache cache)=>Directory.EnumerateDirectories(cache.FamilyDirectory,".stage-*").Single();
        static void CopyTree(string source,string target){Directory.CreateDirectory(target);foreach(string f in Directory.EnumerateFiles(source))File.Copy(f,Path.Combine(target,Path.GetFileName(f)));}
        public static Report Execute(string output,string sourceFolder="/private/tmp/rr019-integration-drafts/ActualPantside")
        {
            if(Directory.Exists(output)||!Path.GetFullPath(output).StartsWith("/private/tmp/rr019-canonical-cache-proof-",StringComparison.Ordinal))throw new InvalidOperationException("Fresh owned proof directory required");Directory.CreateDirectory(output);Passed.Clear();var r=new Report();
            try
            {
                var input=ReviewedCanonicalRaw13Input.CaptureReviewedSnapshot(sourceFolder);byte[] original=input.CopyDocumentBytes();string originalSha=World13DocumentCodec.Hash(original);
                var cache=new CanonicalRaw13Cache(Path.Combine(output,"good"));
                Check(cache.Load(input)==null&&!Directory.Exists(cache.FamilyDirectory),"missing read is nonmutating and returns no ownership");
                Reject(()=>cache.Commit(null,Fresh(input)),"hashes/flags/null cannot replace typed reviewed capability");Check(!Directory.Exists(cache.FamilyDirectory),"invalid capability creates no stage");
                var originalOwner=Fresh(input);var saved=cache.Commit(input,originalOwner);Check(saved!=null&&!saved.PreparedAdmitted&&saved.Write().SequenceEqual(original),"atomic commit/readback retains exact canonical raw bytes and unadmitted status");
                Check(originalOwner.Write().SequenceEqual(original),"commit borrows owner without transferring caller ownership");
                string directory=Entry(cache);Check(Directory.EnumerateFileSystemEntries(directory).Count()==3&&!Directory.EnumerateDirectories(cache.FamilyDirectory,".stage-*").Any(),"only3complete files published and own stage removed");
                r.key=CanonicalRaw13Cache.CacheKey;r.evidenceSHA=World13DocumentCodec.Hash(File.ReadAllBytes(Path.Combine(directory,"source-evidence.json")));r.commitSHA=World13DocumentCodec.Hash(File.ReadAllBytes(Path.Combine(directory,"commit.json")));r.bodySHA=originalSha;
                Check(File.ReadAllBytes(Path.Combine(directory,"source-evidence.json")).SequenceEqual(CanonicalRaw13Cache.EvidenceBytes()),"evidence binds code-selected compatibility mode recipe input and source");
                var first=cache.Load(input);var second=cache.Load(input);var taken=first.TakeOwnedDocument();Check(taken!=null&&second.Write().SequenceEqual(original),"each read transfers a fresh independent owned document");Reject(()=>first.Write(),"taken cache owner cannot be reused");
                Check(cache.Commit(input,Fresh(input)).Write().SequenceEqual(original),"existing valid entry reused without replacement");
                foreach(CanonicalRaw13Cache.Step step in Enum.GetValues(typeof(CanonicalRaw13Cache.Step)))
                {
                    var cancelledCache=new CanonicalRaw13Cache(Path.Combine(output,"cancel-"+step));using(var cancel=new CancellationTokenSource())Reject(()=>cancelledCache.Commit(input,Fresh(input),cancel.Token,null,p=>{if(p==step)cancel.Cancel();}),"cancelled transaction returns no owner at "+step);
                    Check(!Directory.EnumerateDirectories(cancelledCache.FamilyDirectory,".stage-*").Any(),"cancel leaves no owned unpublished stage at "+step);
                    Check(Directory.Exists(Entry(cancelledCache))==(step==CanonicalRaw13Cache.Step.Published),"publication point retained exactly at "+step);
                    if(step==CanonicalRaw13Cache.Step.Published)Check(cancelledCache.Load(input).Write().SequenceEqual(original),"postpublication cancellation preserves valid immutable bundle");
                    var staleCache=new CanonicalRaw13Cache(Path.Combine(output,"stale-"+step));bool current=true;Reject(()=>staleCache.Commit(input,Fresh(input),default,()=>current,p=>{if(p==step)current=false;}),"stale generation returns no owner at "+step);Check(!Directory.EnumerateDirectories(staleCache.FamilyDirectory,".stage-*").Any(),"stale cleanup removes own stage at "+step);
                }
                foreach(string name in new[]{"commit.json","source-evidence.json","world.json"})
                {
                    var bad=new CanonicalRaw13Cache(Path.Combine(output,"corrupt-"+name));CopyTree(directory,Entry(bad));string file=Path.Combine(Entry(bad),name);byte[] bytes=File.ReadAllBytes(file);bytes[bytes.Length/2]^=1;File.WriteAllBytes(file,bytes);string corruptSha=World13DocumentCodec.Hash(bytes);
                    Reject(()=>bad.Load(input),"altered "+name+" rejected against capability-selected identity");Reject(()=>bad.Commit(input,Fresh(input)),"existing corrupted "+name+" is not overwritten/repaired silently");Check(World13DocumentCodec.Hash(File.ReadAllBytes(file))==corruptSha,"corrupt evidence retained unchanged "+name);
                }
                foreach(string mode in new[]{"foreign","excess","nested","missing","oversize-evidence","oversize-commit","empty-body"})
                {
                    var bad=new CanonicalRaw13Cache(Path.Combine(output,"bad-"+mode));CopyTree(directory,Entry(bad));
                    if(mode=="foreign")File.Move(Path.Combine(Entry(bad),"commit.json"),Path.Combine(Entry(bad),"unknown.json"));
                    if(mode=="excess")for(int i=0;i<8;i++)File.WriteAllText(Path.Combine(Entry(bad),"extra"+i),"foreign");
                    if(mode=="nested")Directory.CreateDirectory(Path.Combine(Entry(bad),"nested"));
                    if(mode=="missing")File.Delete(Path.Combine(Entry(bad),"commit.json"));
                    if(mode=="oversize-evidence")File.WriteAllBytes(Path.Combine(Entry(bad),"source-evidence.json"),new byte[16385]);
                    if(mode=="oversize-commit")File.WriteAllBytes(Path.Combine(Entry(bad),"commit.json"),new byte[16385]);
                    if(mode=="empty-body")File.WriteAllBytes(Path.Combine(Entry(bad),"world.json"),Array.Empty<byte>());
                    Reject(()=>bad.Load(input),"bounded read refuses "+mode+" bundle");
                }
                var fileEntry=new CanonicalRaw13Cache(Path.Combine(output,"file-entry"));Directory.CreateDirectory(fileEntry.FamilyDirectory);File.WriteAllText(Entry(fileEntry),"foreign");Reject(()=>fileEntry.Load(input),"regular file cannot masquerade as bundle directory");Reject(()=>fileEntry.Commit(input,Fresh(input)),"foreign entry file is never overwritten");
#if UNITY_EDITOR_OSX || !UNITY_5_3_OR_NEWER
                foreach(bool dangling in new[]{false,true})
                {var linked=new CanonicalRaw13Cache(Path.Combine(output,"linked-"+dangling));Directory.CreateDirectory(linked.FamilyDirectory);string destination=dangling?Path.Combine(output,"absent-target"):directory;Check(symlink(destination,Entry(linked))==0,"owned symlink fixture created "+dangling);Reject(()=>linked.Load(input),"linked/dangling cache entry refused "+dangling);Reject(()=>linked.Commit(input,Fresh(input)),"linked/dangling entry not replaced "+dangling);}
                {var linked=new CanonicalRaw13Cache(Path.Combine(output,"linked-file"));CopyTree(directory,Entry(linked));string path=Path.Combine(Entry(linked),"world.json");File.Delete(path);Check(symlink(Path.Combine(directory,"world.json"),path)==0,"linked bundle file fixture created");Reject(()=>linked.Load(input),"linked body refused before read");}
#endif
                foreach(string name in new[]{"world.json","commit.json","source-evidence.json"})
                {
                    var bad=new CanonicalRaw13Cache(Path.Combine(output,"staged-"+name));Reject(()=>bad.Commit(input,Fresh(input),default,null,p=>{if(p==CanonicalRaw13Cache.Step.BeforePublish)File.WriteAllText(Path.Combine(Stage(bad),name),"corrupt");}),"final staged readback refuses changed "+name);Check(!Directory.Exists(Entry(bad)),"changed staged "+name+" never publishes");
                }
                var foreignStage=new CanonicalRaw13Cache(Path.Combine(output,"foreign-stage"));Reject(()=>foreignStage.Commit(input,Fresh(input),default,null,p=>{if(p==CanonicalRaw13Cache.Step.BeforePublish)File.WriteAllText(Path.Combine(Stage(foreignStage),"do-not-delete.txt"),"unowned");}),"foreign staged file blocks publication");Check(File.ReadAllText(Path.Combine(Stage(foreignStage),"do-not-delete.txt"))=="unowned","cleanup does not recursively erase unowned foreign file");
                var winner=new CanonicalRaw13Cache(Path.Combine(output,"winner"));var competitor=new CanonicalRaw13Cache(Path.Combine(output,"winner"));bool competed=false;
                winner.Commit(input,Fresh(input),default,null,p=>{if(p==CanonicalRaw13Cache.Step.BeforePublish){competitor.Commit(input,Fresh(input));competed=true;}});Check(competed&&winner.Load(input).Write().SequenceEqual(original)&&!Directory.EnumerateDirectories(winner.FamilyDirectory,".stage-*").Any(),"another writer wins rename race without overwrite or leaked loser stage");
                using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject(()=>cache.Load(input,cancel.Token),"cancelled read returns no owned document");}
                int calls=0;Reject(()=>cache.Load(input,default,()=>++calls<8),"stale request during bounded read returns no owned document");
                Check(cache.Load(input).Write().SequenceEqual(original)&&input.CopyDocumentBytes().SequenceEqual(original),"all adversarial reads leave source input and original valid bundle unchanged");
                Check(!Directory.Exists(Path.Combine(output,"good","Raw13-v1")),"old fixed Raw13-v1 family never opened/changed");
                r.status="PASS";
            }
            catch(Exception e){r.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());throw;}
            finally{r.checks=Passed.Count;r.passed=Passed.ToArray();File.WriteAllText(Path.Combine(output,"receipt.json"),CanonicalGeography13.Write(r,true));}return r;
        }
    }
    public static class CanonicalRaw13CacheVerification
    {public static void Run(){int exit=1;string output="/private/tmp/rr019-canonical-cache-proof-unity-"+DateTime.UtcNow.Ticks;try{if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())throw new InvalidOperationException("Isolated raw-only proof required");var r=CanonicalRaw13CacheChecks.Execute(output);Debug.Log("CANONICAL RAW CACHE PASS "+r.checks+" "+output);exit=0;}catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);}}
}
