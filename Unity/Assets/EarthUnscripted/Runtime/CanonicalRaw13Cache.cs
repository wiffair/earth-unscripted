using System;using System.IO;using System.Linq;using System.Threading;
namespace EarthUnscripted
{
    // New raw-only family. A live reviewed capability, never disk metadata,
    // selects source trust/canonical mode. No boot/prepared/network consumer.
    public sealed class CanonicalRaw13Cache
    {
        public enum Step {StageCreated,BodyFlushed,EvidenceFlushed,CommitFlushed,BeforePublish,Published}
        [Serializable]sealed class Evidence
        {public string family=ReviewedCanonicalRaw13Input.Family,compatibility=ReviewedCanonicalRaw13Input.Compatibility,phase="verified-raw-source-unadmitted",canonicalMode="ieee-exact-decimal-v1",conversionRecipe="original-ways-grid-binary32-canonical13-v2",inputSha256=ReviewedCanonicalRaw13Input.ExpectedInputSHA256,sourceSha256=ReviewedCanonicalRaw13Input.ExpectedSourceSHA256;}
        [Serializable]sealed class CommitRecord
        {public string format="EarthUnscriptedCanonicalRaw13Cache1",key,documentSha256=ReviewedCanonicalRaw13Input.ExpectedBodySHA256,evidenceSha256;public int version=1;}
        readonly string family;readonly object sync=new object();
        static readonly string[] Names={"commit.json","source-evidence.json","world.json"};
        public string FamilyDirectory=>family;
        public static byte[] EvidenceBytes()=>CanonicalGeography13.WriteBytes(new Evidence());
        public static string CacheKey=>World13DocumentCodec.Hash(CanonicalGeography13.WriteBytes(new CommitRecord{key="",evidenceSha256=World13DocumentCodec.Hash(EvidenceBytes())}));
        static byte[] CommitBytes()=>CanonicalGeography13.WriteBytes(new CommitRecord{key=CacheKey,evidenceSha256=World13DocumentCodec.Hash(EvidenceBytes())});
        public CanonicalRaw13Cache(string baseDirectory)
        {if(string.IsNullOrWhiteSpace(baseDirectory)||baseDirectory.StartsWith("\\\\",StringComparison.Ordinal))throw new ArgumentException("Local cache root required");family=Path.Combine(Path.GetFullPath(baseDirectory),ReviewedCanonicalRaw13Input.Family);}
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException("Canonical raw13 cache: "+why);}
        static void Check(CancellationToken token,Func<bool> current)
        {token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("Canonical raw source request/pins are stale");}
        static void NoLinks(string path)
        {for(var item=new DirectoryInfo(path);item!=null;item=item.Parent){try{Need((File.GetAttributes(item.FullName)&FileAttributes.ReparsePoint)==0,"linked path");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}}
        static void ExactFiles(string directory)
        {
            var seen=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);int count=0;
            foreach(string entry in Directory.EnumerateFileSystemEntries(directory))
            {Need(++count<=3,"excess bundle entries");string name=Path.GetFileName(entry);Need(Names.Contains(name)&&seen.Add(name)&&File.Exists(entry)&&!Directory.Exists(entry)&&(File.GetAttributes(entry)&FileAttributes.ReparsePoint)==0,"foreign/missing/linked bundle entry");}
            Need(count==3,"incomplete bundle");
        }
        static byte[] ReadBytes(string path,int maximum,CancellationToken token,Func<bool> current)
        {
            Check(token,current);Need(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"missing/linked file");
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {Need(file.Length>0&&file.Length<=maximum,"file byte budget");var bytes=new byte[(int)file.Length];int at=0;while(at<bytes.Length){Check(token,current);int n=file.Read(bytes,at,Math.Min(65536,bytes.Length-at));Need(n>0,"truncated file");at+=n;}Need(file.ReadByte()==-1,"file grew");return bytes;}
        }
        static void WriteBytes(string path,byte[] bytes,CancellationToken token,Func<bool> current)
        {using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){for(int at=0;at<bytes.Length;at+=65536){Check(token,current);file.Write(bytes,at,Math.Min(65536,bytes.Length-at));}Check(token,current);file.Flush(true);}}
        static void Live(ReviewedCanonicalRaw13Input input,CancellationToken token)
        {if(input==null)throw new ArgumentNullException(nameof(input));input.CopyDocumentBytes(token);}
        string Entry=>Path.Combine(family,CacheKey);
        public CanonicalRaw13Document Load(ReviewedCanonicalRaw13Input input,CancellationToken token=default,Func<bool> stillCurrent=null)
        {
            Check(token,stillCurrent);Live(input,token);NoLinks(family);NoLinks(Entry);Need(!File.Exists(Entry),"entry is a file");if(!Directory.Exists(Entry))return null;
            NoLinks(Entry);ExactFiles(Entry);
            // Expected metadata is produced from code-pinned live input, not cache claims.
            Need(ReadBytes(Path.Combine(Entry,"commit.json"),16384,token,stillCurrent).SequenceEqual(CommitBytes()),"commit differs from selected source/mode");
            Need(ReadBytes(Path.Combine(Entry,"source-evidence.json"),16384,token,stillCurrent).SequenceEqual(EvidenceBytes()),"evidence differs");
            var bytes=ReadBytes(Path.Combine(Entry,"world.json"),World13DocumentCodec.MaximumBytes,token,stillCurrent);
            Check(token,stillCurrent);var document=CanonicalRaw13Codec.Read(input,bytes,token);Check(token,stillCurrent);return document;
        }
        public CanonicalRaw13Document Commit(ReviewedCanonicalRaw13Input input,CanonicalRaw13Document document,CancellationToken token=default,Func<bool> stillCurrent=null,Action<Step> observe=null)
        {
            lock(sync)
            {
                Check(token,stillCurrent);Live(input,token);if(document==null)throw new ArgumentNullException(nameof(document));byte[] bytes=document.Write(token);
                // Validate exact input/owner binding before making any directory.
                CanonicalRaw13Codec.Read(input,bytes,token);Check(token,stillCurrent);NoLinks(family);NoLinks(Entry);Need(!File.Exists(Entry),"entry is a file");
                if(Directory.Exists(Entry))return Load(input,token,stillCurrent);
                Directory.CreateDirectory(family);NoLinks(family);string stage=Path.Combine(family,".stage-"+Guid.NewGuid().ToString("N"));bool owns=false,published=false;
                try
                {
                    Need(!Directory.Exists(stage)&&!File.Exists(stage),"stage collision");Directory.CreateDirectory(stage);owns=true;observe?.Invoke(Step.StageCreated);Check(token,stillCurrent);
                    WriteBytes(Path.Combine(stage,"world.json"),bytes,token,stillCurrent);observe?.Invoke(Step.BodyFlushed);Check(token,stillCurrent);
                    WriteBytes(Path.Combine(stage,"source-evidence.json"),EvidenceBytes(),token,stillCurrent);observe?.Invoke(Step.EvidenceFlushed);Check(token,stillCurrent);
                    WriteBytes(Path.Combine(stage,"commit.json"),CommitBytes(),token,stillCurrent);observe?.Invoke(Step.CommitFlushed);Check(token,stillCurrent);ExactFiles(stage);
                    // Actual staged readback before publication; hashes alone do not admit a body.
                    Need(ReadBytes(Path.Combine(stage,"commit.json"),16384,token,stillCurrent).SequenceEqual(CommitBytes()),"staged commit altered");
                    Need(ReadBytes(Path.Combine(stage,"source-evidence.json"),16384,token,stillCurrent).SequenceEqual(EvidenceBytes()),"staged evidence altered");
                    CanonicalRaw13Codec.Read(input,ReadBytes(Path.Combine(stage,"world.json"),World13DocumentCodec.MaximumBytes,token,stillCurrent),token);
                    observe?.Invoke(Step.BeforePublish);Check(token,stillCurrent);Live(input,token);NoLinks(family);NoLinks(stage);ExactFiles(stage);Check(token,stillCurrent);
                    Need(ReadBytes(Path.Combine(stage,"commit.json"),16384,token,stillCurrent).SequenceEqual(CommitBytes()),"final staged commit altered");
                    Need(ReadBytes(Path.Combine(stage,"source-evidence.json"),16384,token,stillCurrent).SequenceEqual(EvidenceBytes()),"final staged evidence altered");
                    CanonicalRaw13Codec.Read(input,ReadBytes(Path.Combine(stage,"world.json"),World13DocumentCodec.MaximumBytes,token,stillCurrent),token);Check(token,stillCurrent);
                    try{Directory.Move(stage,Entry);published=true;}catch(IOException){if(!Directory.Exists(Entry))throw;return Load(input,token,stillCurrent);}
                    observe?.Invoke(Step.Published);Check(token,stillCurrent);return Load(input,token,stillCurrent);
                }
                finally
                {
                    // Never recursively erase arbitrary added files or another writer's stage.
                    // On unexpected/linked contents retain the private orphan for inspection.
                    if(owns&&!published&&Directory.Exists(stage))
                    {NoLinks(stage);foreach(string name in Names){string path=Path.Combine(stage,name);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0)File.Delete(path);}if(!Directory.EnumerateFileSystemEntries(stage).Any())Directory.Delete(stage);}
                }
            }
        }
    }
}
