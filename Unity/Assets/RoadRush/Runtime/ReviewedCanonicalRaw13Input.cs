using System;
using System.IO;
using System.Threading;
namespace RoadRush
{
    // Only the fixed, independently reviewed original-source snapshot can mint
    // this initial capability. Copied payloads/receipts never select trust/mode.
    public sealed class ReviewedCanonicalRaw13Input
    {
        public const string Family="Raw13Canonical-v1";
        public const string Compatibility="RoadRush/0.19.0/geometry13";
        public const string ExpectedBodySHA256="fd3b6201584eac1e3303b1392c21896a2a0c76774149f389cc3ace17022803cb";
        public const string ExpectedInputSHA256="1bc00de68ac69dc657578dfba0bc58b90cb6282a537cf188626d7e1b5acec491";
        public const string ExpectedSourceSHA256="f16323b89e4818e6ee57e6847a1798e742b47195a311fb3facd3a8682c8ed62d";
        const string Evidence="cfc71bf6f051e6e7e725f195c010b4ef8b4fed0765de119bd34c4f1cf1efe113";
        readonly Raw13SourceCapture source;
        readonly byte[] body;
        readonly World13ReadContext context;
        ReviewedCanonicalRaw13Input(Raw13SourceCapture source,byte[] body)
        {this.source=source;this.body=(byte[])body.Clone();context=World13ReadContext.FromVerifiedRawReceipt(World13ReadContext.RawOrigin.VerifiedFreshProvider,Compatibility,ExpectedBodySHA256,Evidence);}
        public string Scope=>"Fixed reviewed dated original3km source; raw inspection ownership only, not current-provider/prepared/world/cache/network admission";
        public string CanonicalMode=>"ieee-exact-decimal-v1";
        public string ConversionRecipe=>"original-ways-grid-binary32-canonical13-v2";
        public static ReviewedCanonicalRaw13Input CaptureReviewedSnapshot(string folder,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            var source=Raw13SourceCapture.CaptureReviewedPantside3km(folder,cancellation);
            if(source.InputDigest!=ExpectedInputSHA256)throw new InvalidDataException("Reviewed canonical source identity differs");
            var result=OriginalWayRaw13Converter.ConvertReviewed(source,Compatibility,cancellation);
            byte[] bytes=result.CopyDocumentBytes();
            if(result.Report.recipe!="original-ways-grid-binary32-canonical13-v2"||result.Report.canonicalMode!="ieee-exact-decimal-v1"||result.Report.sourceDigest!=ExpectedSourceSHA256||World13DocumentCodec.Hash(bytes)!=ExpectedBodySHA256)throw new InvalidDataException("Reviewed canonical conversion differs from independent vectors");
            cancellation.ThrowIfCancellationRequested();return new ReviewedCanonicalRaw13Input(source,bytes);
        }
        internal World13ReadContext CheckedContext(CancellationToken cancellation)
        {cancellation.ThrowIfCancellationRequested();if(source.CheckedPayload().InputDigest!=ExpectedInputSHA256)throw new InvalidDataException("Reviewed canonical source changed");cancellation.ThrowIfCancellationRequested();return context;}
        public byte[] CopyDocumentBytes(CancellationToken cancellation=default)
        {CheckedContext(cancellation);return(byte[])body.Clone();}
    }
}
