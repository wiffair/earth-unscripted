using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace EarthUnscripted.Core
{
    [Serializable] public sealed class DataFileManifest
    {
        public int format=1;public string id="",kind="",pack="",fromVersion="",toVersion="",sha256="",source="",licence="",signer="",signature="",timestamp="";public long bytes;public bool hasBounds;public double west,south,east,north;
    }
    public static class DataPackageRules
    {
        public const int MaximumFileBytes=32*1024*1024,MaximumHeaderBytes=16384,ChunkBytes=32768,MaximumOsmSourceBytes=256*1024*1024;
        public static bool SafeId(string id)=>id!=null&&Regex.IsMatch(id,@"^[A-Za-z0-9_-]{1,96}$");
        public static bool Valid(DataFileManifest m)
        {
            if(m==null||(m.format!=1&&m.format!=2)||!SafeId(m.id)||!SafeId(m.pack)||m.bytes<1||m.bytes>(m.kind=="osm-source"?MaximumOsmSourceBytes:MaximumFileBytes)||!Regex.IsMatch(m.sha256??"",@"^[0-9a-fA-F]{64}$"))return false;
            if(m.format==1&&!string.IsNullOrEmpty(m.timestamp))return false;
            if(m.format==2&&((m.timestamp??"").Length>80||!Regex.IsMatch(m.timestamp??"",@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")||!DateTimeOffset.TryParse(m.timestamp,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _)))return false;
            if(m.kind!="map-diff"&&m.kind!="region-snapshot"&&m.kind!="terrain-tile"&&m.kind!="osm-source")return false;
            if(m.kind=="osm-source"&&(m.format!=2||m.fromVersion!="raw-osm-pbf-v1"||m.toVersion!=m.sha256||m.id!="osm-"+m.sha256||m.licence!="ODbL-1.0"||!Regex.IsMatch(m.sha256,@"^[a-f0-9]{64}$")||!OsmSourceUrl(m.source)))return false;
            if((m.fromVersion??"").Length>80||(m.toVersion??"").Length>80||(m.source??"").Length>2048||(m.licence??"").Length>512)return false;
            if(m.hasBounds&&(double.IsNaN(m.west)||double.IsNaN(m.south)||double.IsNaN(m.east)||double.IsNaN(m.north)||m.west< -180||m.east>180||m.south< -90||m.north>90||m.west>m.east||m.south>m.north))return false;return true;
        }
        public static bool OsmSourceUrl(string value)
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out var url)||url.Scheme!="https"||url.Port!=443||!string.IsNullOrEmpty(url.UserInfo)||!string.IsNullOrEmpty(url.Query)||!string.IsNullOrEmpty(url.Fragment))return false;
            if(url.Host=="download.geofabrik.de")return Regex.IsMatch(url.AbsolutePath,@"^/[A-Za-z0-9/_-]+\.osm\.pbf$");
            return url.Host=="download.bbbike.org"&&Regex.IsMatch(url.AbsolutePath,@"^/osm/bbbike/([A-Za-z0-9_-]{1,80})/\1\.osm\.pbf$");
        }
        static string B(string value)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(value??""));
        public static byte[] Canonical(DataFileManifest m)
        {
            string value=string.Join("|",new[]{"EarthUnscriptedData"+m.format,B(m.id),B(m.kind),B(m.pack),B(m.fromVersion),B(m.toVersion),m.bytes.ToString(CultureInfo.InvariantCulture),B(m.sha256),B(m.source),B(m.licence),m.hasBounds?"1":"0",m.west.ToString("R",CultureInfo.InvariantCulture),m.south.ToString("R",CultureInfo.InvariantCulture),m.east.ToString("R",CultureInfo.InvariantCulture),m.north.ToString("R",CultureInfo.InvariantCulture),B(m.signer)});
            if(m.format==2)value+="|"+B(m.timestamp);return Encoding.UTF8.GetBytes(value);
        }
        public static string Hash(Stream stream){using(var sha=SHA256.Create())return Hex(sha.ComputeHash(stream));}
        public static string Hash(byte[] data){using(var sha=SHA256.Create())return Hex(sha.ComputeHash(data));}
        public static string HashFile(string path){using(var stream=File.OpenRead(path))return Hash(stream);}
        static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        public static bool FileMatches(string path,DataFileManifest manifest)
        {try{return Valid(manifest)&&new FileInfo(path).Length==manifest.bytes&&string.Equals(HashFile(path),manifest.sha256,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
        public static string Fingerprint(string publicKey)=>Hash(Encoding.ASCII.GetBytes(publicKey??""));
        public static bool VerifySignature(DataFileManifest manifest,string trustedFingerprint)
        {
            try
            {
                if(!Valid(manifest)||(manifest.signer??"").Length>2048||(manifest.signature??"").Length>1024||Fingerprint(manifest.signer)!=trustedFingerprint)return false;
                var parts=manifest.signer.Split('.');if(parts.Length!=2)return false;var modulus=Convert.FromBase64String(parts[0]);var exponent=Convert.FromBase64String(parts[1]);if(modulus.Length<256||modulus.Length>512||exponent.Length<1||exponent.Length>8)return false;
                using(var rsa=RSA.Create()){rsa.ImportParameters(new RSAParameters{Modulus=modulus,Exponent=exponent});return rsa.VerifyData(Canonical(manifest),Convert.FromBase64String(manifest.signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);}
            }
            catch{return false;}
        }
        public static string Extension(string kind)=>kind=="osm-source"?".osm.pbf":kind=="map-diff"?".osc.gz":kind=="terrain-tile"?".euh":kind=="region-snapshot"?".json":throw new ArgumentException("Unsupported map data type");
        public static string PathFor(string root,DataFileManifest manifest)
        {if(!Valid(manifest))throw new ArgumentException("Invalid data manifest");return Path.Combine(root,manifest.id+Extension(manifest.kind));}
        public static bool NetworkMayRun(bool mapBusy,bool weatherBusy,bool loading,float rttMilliseconds,int queuedGameplayBytes)=>!mapBusy&&!weatherBusy&&!loading&&rttMilliseconds<100&&queuedGameplayBytes<65536;
        public static bool MayActivate(DataFileManifest update,bool activeArea,bool lanSession,string pinnedVersion,double visibleWest,double visibleSouth,double visibleEast,double visibleNorth)
        {
            if(!Valid(update))return false;if(lanSession&&update.toVersion!=pinnedVersion)return false;
            if(!activeArea)return true;if(!update.hasBounds)return false;
            return update.east<visibleWest||update.west>visibleEast||update.north<visibleSouth||update.south>visibleNorth;
        }
        public static bool NextSequence(string installed,string from,string to)=>long.TryParse(installed,out long current)&&long.TryParse(from,out long previous)&&long.TryParse(to,out long next)&&current==previous&&next==current+1;
    }
    public sealed class DataSigningKey : IDisposable
    {
        readonly RSA rsa;public readonly string PublicKey;public string Fingerprint=>DataPackageRules.Fingerprint(PublicKey);
        public DataSigningKey()
        {
            // Unity Mono's default CSP can retain its original1024-bit key when
            // KeySize is assigned after construction. Construct the required size.
            rsa=new RSACryptoServiceProvider(2048){PersistKeyInCsp=false};var p=rsa.ExportParameters(false);
            if(p.Modulus.Length<256){rsa.Dispose();throw new CryptographicException("Host signing key is smaller than2048bits");}
            PublicKey=Convert.ToBase64String(p.Modulus)+"."+Convert.ToBase64String(p.Exponent);
        }
        public void Sign(DataFileManifest manifest){if(!DataPackageRules.Valid(manifest))throw new ArgumentException("Invalid manifest");manifest.signer=PublicKey;manifest.signature=Convert.ToBase64String(rsa.SignData(DataPackageRules.Canonical(manifest),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));}
        public void Dispose()=>rsa.Dispose();
    }
}
