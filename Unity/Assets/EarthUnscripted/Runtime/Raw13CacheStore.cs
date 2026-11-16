using System;using System.IO;using System.Linq;using System.Text;using System.Threading;
namespace EarthUnscripted
{
    // Separate verified RAW input family. No legacy paths, prepared snapshots or scene hooks.
    public sealed class Raw13CacheStore
    {
        public enum Step{StageCreated,BodyFlushed,EvidenceFlushed,CommitFlushed,BeforePublish,Published}
        [Serializable]sealed class CommitRecord
        {public string format="EarthUnscriptedRaw13InputCache1",key,compatibility,documentSha256,evidenceSha256,sourceDigest,phase="verified-raw-source";public int version=1;}
        readonly string family;readonly object sync=new object();
        public string FamilyDirectory=>family;
        public Raw13CacheStore(string baseDirectory)
        {if(string.IsNullOrEmpty(baseDirectory)||baseDirectory.StartsWith("\\\\",StringComparison.Ordinal))throw new ArgumentException("Local cache directory required");family=Path.Combine(Path.GetFullPath(baseDirectory),"Raw13-v1");}
        static void Require(bool ok,string why){if(!ok)throw new InvalidDataException("Raw13 cache: "+why);}
        static void Check(CancellationToken token,Func<bool> current)
        {token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("Raw13 source pins/request generation is stale");}
        static void NoLinks(string path)
        {
            var item=new DirectoryInfo(path);while(item!=null){if((Directory.Exists(item.FullName)||File.Exists(item.FullName))&&(File.GetAttributes(item.FullName)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Raw13 cache: linked path refused");item=item.Parent;}
        }
        string Entry(VerifiedRaw13Input expected)
        {Require(expected!=null&&expected.CacheKey.Length==64,"trusted source capability required");return Path.Combine(family,expected.CacheKey);}
        static byte[] CommitBytes(VerifiedRaw13Input expected)=>Encoding.UTF8.GetBytes(WorldFieldJson.Write(new CommitRecord{key=expected.CacheKey,compatibility=expected.Compatibility,documentSha256=expected.DocumentSha256,evidenceSha256=expected.EvidenceSha256,sourceDigest=expected.SourceDigest}));
        static byte[] ReadBounded(string path,int maximum)
        {
            Require(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"missing/linked bundle file");
            using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {Require(f.Length>0&&f.Length<=maximum,"bundle file size");var bytes=new byte[(int)f.Length];int at=0;while(at<bytes.Length){int n=f.Read(bytes,at,bytes.Length-at);Require(n>0,"truncated bundle file");at+=n;}Require(f.ReadByte()==-1,"bundle grew while reading");return bytes;}
        }
        static void WriteNew(string path,byte[] data)
        {using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(data,0,data.Length);f.Flush(true);}}
        static void CompleteFiles(string directory)
        {Require(Directory.GetDirectories(directory).Length==0&&Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(s=>s,StringComparer.Ordinal).SequenceEqual(new[]{"commit.json","source-evidence.json","world.json"}),"incomplete/foreign bundle contents");}
        public VerifiedRawWorld13Document Load(VerifiedRaw13Input expected,CancellationToken token,Func<bool> stillCurrent=null)
        {
            Check(token,stillCurrent);Require(expected!=null,"trusted source capability required");expected.RevalidateSourcePins(token);string directory=Entry(expected);NoLinks(family);if(!Directory.Exists(directory))return null;NoLinks(directory);CompleteFiles(directory);
            // Compare against independently trusted expected bytes, NOT self-declared disk hashes/flags.
            Require(ReadBounded(Path.Combine(directory,"commit.json"),16384).SequenceEqual(CommitBytes(expected)),"commit/source pins differ");
            Require(ReadBounded(Path.Combine(directory,"source-evidence.json"),16384).SequenceEqual(expected.CopyEvidence()),"source evidence differs");
            byte[] body=ReadBounded(Path.Combine(directory,"world.json"),World13DocumentCodec.MaximumBytes);Require(World13DocumentCodec.Hash(body)==expected.DocumentSha256,"body corrupted or stale");Check(token,stillCurrent);
            var result=World13DocumentCodec.ReadVerifiedRaw(body,expected.Context());Check(token,stillCurrent);return result;
        }
        public VerifiedRawWorld13Document Commit(VerifiedRaw13Input input,CancellationToken token,Func<bool> stillCurrent=null,Action<Step> observe=null)
        {
            lock(sync)
            {
                Check(token,stillCurrent);Require(input!=null,"trusted source capability required");input.RevalidateSourcePins(token);NoLinks(family);string target=Entry(input);if(Directory.Exists(target))return Load(input,token,stillCurrent);
                Directory.CreateDirectory(family);string stage=Path.Combine(family,".stage-"+input.CacheKey+"-"+Guid.NewGuid().ToString("N"));bool own=false,published=false;
                try
                {
                    Require(!Directory.Exists(stage)&&!File.Exists(stage),"stage name collision");Directory.CreateDirectory(stage);own=true;observe?.Invoke(Step.StageCreated);Check(token,stillCurrent);
                    WriteNew(Path.Combine(stage,"world.json"),input.CopyDocument());observe?.Invoke(Step.BodyFlushed);Check(token,stillCurrent);
                    WriteNew(Path.Combine(stage,"source-evidence.json"),input.CopyEvidence());observe?.Invoke(Step.EvidenceFlushed);Check(token,stillCurrent);
                    WriteNew(Path.Combine(stage,"commit.json"),CommitBytes(input));observe?.Invoke(Step.CommitFlushed);Check(token,stillCurrent);CompleteFiles(stage);
                    observe?.Invoke(Step.BeforePublish);Check(token,stillCurrent);Require(input!=null,"trusted source capability required");input.RevalidateSourcePins(token);NoLinks(family);NoLinks(stage);Check(token,stillCurrent);
                    // Same-parent rename is the publication point. No partial folder
                    // is accepted; power-loss durability is not claimed by this API.
                    try{Directory.Move(stage,target);published=true;}
                    catch(IOException){if(!Directory.Exists(target))throw;return Load(input,token,stillCurrent);}
                    observe?.Invoke(Step.Published);Check(token,stillCurrent);return Load(input,token,stillCurrent);
                }
                finally
                {
                    // Remove only this invocation's unpublished GUID stage. Never
                    // delete/overwrite existing entries, sources or another writer.
                    if(own&&!published&&Directory.Exists(stage)){NoLinks(stage);Directory.Delete(stage,true);}
                }
            }
        }
    }
}
