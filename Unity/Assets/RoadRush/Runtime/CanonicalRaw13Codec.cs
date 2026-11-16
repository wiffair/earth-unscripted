using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace RoadRush
{
    // Additive exact-byte raw family. The private legacy wrapper is used solely
    // for its existing invariant checks, never as this family's byte exporter.
    public static class CanonicalRaw13Codec
    {
        public static CanonicalRaw13Document Read(ReviewedCanonicalRaw13Input input,byte[] bytes,CancellationToken cancellation=default)
        {
            if(input==null)throw new ArgumentNullException(nameof(input));
            var context=input.CheckedContext(cancellation);
            if(bytes==null||bytes.Length>World13DocumentCodec.MaximumBytes)throw new InvalidDataException("Bounded canonical raw input required");
            var owned=(byte[])bytes.Clone();context.RequireDocumentHash(World13DocumentCodec.Hash(owned));cancellation.ThrowIfCancellationRequested();
            var legacy=World13DocumentCodec.ReadVerifiedRaw(owned,context);
            byte[] exact=CanonicalGeography13.WriteBytes(legacy.Document);
            if(!exact.SequenceEqual(owned))throw new InvalidDataException("Raw body is not the selected exact canonical representation");
            cancellation.ThrowIfCancellationRequested();return new CanonicalRaw13Document(input,legacy);
        }
    }
    public sealed class CanonicalRaw13Document
    {
        readonly ReviewedCanonicalRaw13Input input;
        readonly VerifiedRawWorld13Document legacy;
        internal CanonicalRaw13Document(ReviewedCanonicalRaw13Input input,VerifiedRawWorld13Document legacy){this.input=input;this.legacy=legacy;}
        public string Family=>ReviewedCanonicalRaw13Input.Family;
        public bool PreparedAdmitted=>false;
        byte[] CheckedBytes(CancellationToken cancellation)
        {
            input.CheckedContext(cancellation);
            if(legacy.Document==null)throw new InvalidOperationException("Canonical raw ownership already transferred");
            // Retain source/raw-phase/serialized AND NonSerialized guards. These
            // local legacy bytes are discarded, not cached or returned to callers.
            World13DocumentCodec.WriteVerifiedRaw(legacy);
            byte[] bytes=CanonicalGeography13.WriteBytes(legacy.Document);
            if(World13DocumentCodec.Hash(bytes)!=ReviewedCanonicalRaw13Input.ExpectedBodySHA256)throw new InvalidDataException("Canonical raw fields mutated");
            cancellation.ThrowIfCancellationRequested();return bytes;
        }
        public byte[] Write(CancellationToken cancellation=default)
        {lock(legacy.Sync)return CheckedBytes(cancellation);}
        public World13Document TakeOwnedDocument(CancellationToken cancellation=default)
        {lock(legacy.Sync){CheckedBytes(cancellation);return legacy.TakeOwnedDocument();}}
    }
}
