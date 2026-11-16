using System;using System.IO;using System.Threading;using System.Text;
namespace EarthUnscripted
{
    // Read-only cold restart. This class cannot commit, mint producer capability,
    // select an identity from disk or touch original source artifacts/provider DB.
    public sealed class ReviewedRaw13CacheReader
    {
        public enum Step{BeforeRead,CommitVerified,EvidenceVerified,BodyVerified,Decoded}
        const int MaximumReceiptBytes=16384;
        readonly string family;public string FamilyDirectory=>family;
        public ReviewedRaw13CacheReader(string baseDirectory)
        {if(string.IsNullOrEmpty(baseDirectory)||baseDirectory.StartsWith("\\\\",StringComparison.Ordinal))throw new ArgumentException("Local cache directory required");family=Path.Combine(Path.GetFullPath(baseDirectory),"Raw13-v1");}
        static void Require(bool value,string why){if(!value)throw new InvalidDataException("Reviewed raw13 restart: "+why);}
        static void Check(CancellationToken token,Func<bool> current)
        {token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("Reviewed raw13 restart request/selection is stale");}
        static void NoLinks(string path)
        {for(var item=new DirectoryInfo(path);item!=null;item=item.Parent)if((Directory.Exists(item.FullName)||File.Exists(item.FullName))&&(File.GetAttributes(item.FullName)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Reviewed raw13 restart: linked path refused");}
        static byte[] ReadExact(string path,int expected,int maximum,string sha,CancellationToken token,bool strictUtf8=false)
        {
            Require(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"missing/linked cache file");
            using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {Require(f.Length==expected&&expected>0&&expected<=maximum,"cache file length differs from reviewed catalog");var bytes=new byte[expected];int at=0;while(at<bytes.Length){token.ThrowIfCancellationRequested();int n=f.Read(bytes,at,Math.Min(65536,bytes.Length-at));Require(n>0,"truncated cache file");at+=n;}Require(f.ReadByte()==-1,"cache file grew");token.ThrowIfCancellationRequested();if(strictUtf8){try{new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException e){throw new InvalidDataException("Reviewed raw13 restart: malformed receipt UTF8",e);}}Require(World13DocumentCodec.Hash(bytes)==sha,"cache bytes differ from reviewed catalog");return bytes;}
        }
        public VerifiedRawWorld13Document Load(ReviewedRaw13CacheIdentity identity,CancellationToken token,Func<bool> stillCurrent=null,Action<Step> observe=null)
        {
            Check(token,stillCurrent);Require(identity!=null,"catalog-selected identity required");observe?.Invoke(Step.BeforeRead);Check(token,stillCurrent);
            string directory=Path.Combine(family,identity.CacheKey);NoLinks(directory);if(!Directory.Exists(directory))return null;
            var names=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);int entries=0;
            foreach(string path in Directory.EnumerateFileSystemEntries(directory))
            {
                Check(token,stillCurrent);Require(++entries<=3,"foreign cache contents");string name=Path.GetFileName(path);
                Require((name=="commit.json"||name=="source-evidence.json"||name=="world.json")&&names.Add(name),"foreign cache contents");
                Require((File.GetAttributes(path)&(FileAttributes.Directory|FileAttributes.ReparsePoint))==0,"linked/directory cache entry");
            }
            Require(entries==3,"incomplete cache contents");
            // Header is opaque checksum-verified data, never a source of expected pins.
            ReadExact(Path.Combine(directory,"commit.json"),identity.CommitBytes,MaximumReceiptBytes,identity.CommitSha256,token,true);observe?.Invoke(Step.CommitVerified);Check(token,stillCurrent);
            ReadExact(Path.Combine(directory,"source-evidence.json"),identity.EvidenceBytes,MaximumReceiptBytes,identity.EvidenceSha256,token,true);observe?.Invoke(Step.EvidenceVerified);Check(token,stillCurrent);
            byte[] body=ReadExact(Path.Combine(directory,"world.json"),identity.DocumentBytes,World13DocumentCodec.MaximumBytes,identity.DocumentSha256,token);observe?.Invoke(Step.BodyVerified);Check(token,stillCurrent);
            var result=World13DocumentCodec.ReadVerifiedRaw(body,identity.ReadContext());observe?.Invoke(Step.Decoded);Check(token,stillCurrent);return result;
        }
    }
}
