using System;
using System.IO;
namespace RoadRush.Core
{
    // Worker-thread receiver for one signed, bounded content file. Network and
    // activation policy belong to the caller; completing this never changes a world.
    public sealed class PeerDataReceiver : IDisposable
    {
        readonly DataFileManifest manifest;
        readonly string partial;
        FileStream stream;
        public string Destination {get;private set;}
        public long Offset=>stream?.Length??(Completed?manifest.bytes:0);
        public bool Completed {get;private set;}
        public PeerDataReceiver(string folder,DataFileManifest candidate,string trustedFingerprint)
        {
            if(candidate==null)throw new InvalidDataException("Peer data manifest is missing");
            // Copy every signed field so a caller cannot change a checked header
            // while the payload is arriving.
            manifest=new DataFileManifest{format=candidate.format,timestamp=candidate.timestamp,id=candidate.id,kind=candidate.kind,pack=candidate.pack,fromVersion=candidate.fromVersion,toVersion=candidate.toVersion,bytes=candidate.bytes,sha256=candidate.sha256,source=candidate.source,licence=candidate.licence,signer=candidate.signer,signature=candidate.signature,hasBounds=candidate.hasBounds,west=candidate.west,south=candidate.south,east=candidate.east,north=candidate.north};
            if(!DataPackageRules.VerifySignature(manifest,trustedFingerprint))throw new InvalidDataException("Peer data manifest is not signed by the connected host");
            Destination=DataPackageRules.PathFor(folder,manifest);Directory.CreateDirectory(folder);
            partial=Path.Combine(folder,manifest.sha256.ToLowerInvariant()+".partial");
            stream=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
            if(stream.Length>manifest.bytes){stream.Dispose();stream=null;File.Delete(partial);throw new InvalidDataException("Partial peer file exceeds its signed size");}stream.Position=stream.Length;
        }
        public void Append(long offset,byte[] bytes,int index,int count)
        {
            if(stream==null)throw new ObjectDisposedException(nameof(PeerDataReceiver));
            if(bytes==null||count<1||count>DataPackageRules.ChunkBytes||index<0||index>bytes.Length-count||offset!=stream.Position||offset+count>manifest.bytes)throw new InvalidDataException("Peer chunk offset or size is invalid");
            stream.Write(bytes,index,count);
        }
        public string Complete()
        {
            if(stream==null)throw new ObjectDisposedException(nameof(PeerDataReceiver));
            if(stream.Length!=manifest.bytes)throw new InvalidDataException("Peer file is incomplete");
            stream.Dispose();stream=null;
            if(!DataPackageRules.FileMatches(partial,manifest)){File.Delete(partial);throw new InvalidDataException("Peer file failed its signed content hash");}
            if(File.Exists(Destination))File.Replace(partial,Destination,Destination+".bak");else File.Move(partial,Destination);
            Completed=true;return Destination;
        }
        public void Dispose(){stream?.Dispose();stream=null;}
    }
}
