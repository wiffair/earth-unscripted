using System;
using System.IO;
using System.Text;
using System.Threading;
namespace EarthUnscripted.Core
{
    public sealed class PeerFileSource {public DataFileManifest manifest;public string path;}
    public sealed class PeerFileResult {public DataFileManifest manifest;public string path;public long transferred,resumed;}
    // One bounded file on a dedicated worker-owned stream. The caller binds the
    // socket to the gameplay peer, closes it on cancellation and sets IO timeouts.
    public static class PeerFileProtocol
    {
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        static void Read(Stream stream,byte[] bytes,int count,CancellationToken cancel)
        {int read=0;while(read<count){cancel.ThrowIfCancellationRequested();int n=stream.Read(bytes,read,count-read);if(n<=0)throw new EndOfStreamException("Peer data connection ended");read+=n;}}
        static void Number(Stream stream,long value,int size)
        {var bytes=new byte[size];for(int i=size-1;i>=0;i--){bytes[i]=(byte)value;value>>=8;}stream.Write(bytes,0,size);}
        static long Number(Stream stream,int size,CancellationToken cancel)
        {var bytes=new byte[size];Read(stream,bytes,size,cancel);long value=0;foreach(byte b in bytes)value=(value<<8)|b;return value;}
        static void Text(Stream stream,string text,int maximum)
        {var bytes=Utf8.GetBytes(text??"");if(bytes.Length<1||bytes.Length>maximum)throw new InvalidDataException("Peer header exceeds its budget");Number(stream,bytes.Length,4);stream.Write(bytes,0,bytes.Length);}
        static string Text(Stream stream,int maximum,CancellationToken cancel)
        {long length=Number(stream,4,cancel);if(length<1||length>maximum)throw new InvalidDataException("Peer header exceeds its budget");var bytes=new byte[(int)length];Read(stream,bytes,bytes.Length,cancel);return Utf8.GetString(bytes);}
        static bool TokenMatches(string expected,string actual)
        {
            if(expected==null||expected.Length<32||expected.Length>128||actual.Length!=expected.Length)return false;
            int difference=0;for(int i=0;i<expected.Length;i++)difference|=expected[i]^actual[i];return difference==0;
        }
        public static long Serve(Stream connection,string token,Func<string,PeerFileSource> catalogue,Func<DataFileManifest,string> encode,Action<CancellationToken> waitForBudget,CancellationToken cancel)
        {
            if(Text(connection,32,cancel)!="EarthUnscriptedPeer1"||!TokenMatches(token,Text(connection,128,cancel)))throw new InvalidDataException("Data connection is not bound to the gameplay session");
            string id=Text(connection,96,cancel);if(!DataPackageRules.SafeId(id))throw new InvalidDataException("Invalid catalogue identifier");
            var source=catalogue(id);if(source?.manifest==null||source.manifest.id!=id||!DataPackageRules.Valid(source.manifest))throw new InvalidDataException("File is not in the bounded peer catalogue");
            using(var file=File.OpenRead(source.path))
            {
                var manifest=source.manifest;
                if(file.Length!=manifest.bytes||!string.Equals(DataPackageRules.Hash(file),manifest.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Catalogue content changed");
                Text(connection,encode(manifest),DataPackageRules.MaximumHeaderBytes);connection.Flush();
                long offset=Number(connection,8,cancel);if(offset<0||offset>manifest.bytes)throw new InvalidDataException("Invalid peer resume offset");file.Position=offset;long sent=0;
                var buffer=new byte[DataPackageRules.ChunkBytes];
                while(file.Position<file.Length)
                {
                    cancel.ThrowIfCancellationRequested();waitForBudget?.Invoke(cancel);cancel.ThrowIfCancellationRequested();
                    int count=file.Read(buffer,0,(int)Math.Min(buffer.Length,file.Length-file.Position));if(count<=0)throw new EndOfStreamException("Catalogue file ended");
                    Number(connection,count,4);connection.Write(buffer,0,count);connection.Flush();sent+=count;
                }
                Number(connection,0,4);connection.Flush();if(Number(connection,4,cancel)!=1)throw new InvalidDataException("Peer did not validate the completed file");return sent;
            }
        }
        public static PeerFileResult Receive(Stream connection,string token,string requested,string trustedFingerprint,string folder,Func<string,DataFileManifest> decode,Action<long,long> progress,CancellationToken cancel)
        {
            if(!DataPackageRules.SafeId(requested))throw new InvalidDataException("Invalid catalogue identifier");
            Text(connection,"EarthUnscriptedPeer1",32);Text(connection,token,128);Text(connection,requested,96);connection.Flush();
            var manifest=decode(Text(connection,DataPackageRules.MaximumHeaderBytes,cancel));
            if(manifest?.id!=requested||!DataPackageRules.VerifySignature(manifest,trustedFingerprint))throw new InvalidDataException("Peer returned an untrusted file header");
            string destination=DataPackageRules.PathFor(folder,manifest);
            if(DataPackageRules.FileMatches(destination,manifest))
            {
                Number(connection,manifest.bytes,8);connection.Flush();if(Number(connection,4,cancel)!=0)throw new InvalidDataException("Unexpected cached-file payload");Number(connection,1,4);connection.Flush();return new PeerFileResult{manifest=manifest,path=destination,resumed=manifest.bytes};
            }
            using(var receiver=new PeerDataReceiver(folder,manifest,trustedFingerprint))
            {
                long resumed=receiver.Offset;Number(connection,resumed,8);connection.Flush();var buffer=new byte[DataPackageRules.ChunkBytes];
                while(true)
                {
                    long count=Number(connection,4,cancel);if(count==0)break;
                    if(count<1||count>buffer.Length||receiver.Offset+count>manifest.bytes)throw new InvalidDataException("Invalid peer payload frame");
                    Read(connection,buffer,(int)count,cancel);receiver.Append(receiver.Offset,buffer,0,(int)count);progress?.Invoke(receiver.Offset,manifest.bytes);
                }
                string path=receiver.Complete();Number(connection,1,4);connection.Flush();return new PeerFileResult{manifest=manifest,path=path,resumed=resumed,transferred=manifest.bytes-resumed};
            }
        }
    }
}
