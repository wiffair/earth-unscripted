using System;
using System.IO;
namespace EarthUnscripted
{
    // FUTURE13 candidate only. Never populated from the document being decoded.
    // Receipt signature/source verification precedes construction; hash shape alone
    // does not establish provider authenticity. This is NOT a wire/schema validator.
    public sealed class World13ReadContext
    {
        public enum Phase { VerifiedRawSource=1,CommittedPreparedWorld=2,AuthenticatedPeerWorld=3 }
        public enum RawOrigin { Unspecified=0,VerifiedFreshProvider=1,VerifiedLegacyReconstruction=2,ExplicitAuthoredSource=3 }
        public Phase ExpectedPhase {get;}
        public RawOrigin Origin {get;}
        public string ExpectedCompatibility {get;}
        public string ExpectedLayoutRecipe {get;}
        public string ExpectedDocumentSha256 {get;}
        public string VerifiedEvidenceSha256 {get;}
        public string ExpectedHostFingerprint {get;}
        public bool ExpectedAuthoritative=>ExpectedPhase!=Phase.VerifiedRawSource;
        World13ReadContext(Phase phase,RawOrigin origin,string compatibility,string recipe,string documentSha,string evidenceSha,string host)
        {
            if(string.IsNullOrEmpty(compatibility)||compatibility.Length>160||!compatibility.StartsWith("EarthUnscripted/",StringComparison.Ordinal)||!compatibility.EndsWith("/geometry13",StringComparison.Ordinal))Fail("explicit geometry13 compatibility required");
            CheckRecipe(recipe);ExpectedPhase=phase;Origin=origin;ExpectedCompatibility=compatibility;ExpectedLayoutRecipe=recipe;
            ExpectedDocumentSha256=Hash(documentSha);VerifiedEvidenceSha256=Hash(evidenceSha);ExpectedHostFingerprint=host==null?null:Hash(host);
        }
        public static World13ReadContext FromVerifiedRawReceipt(RawOrigin origin,string compatibility,string documentSha,string verifiedOriginalReceiptSha)
        {
            string recipe;
            switch(origin)
            {
                case RawOrigin.VerifiedFreshProvider:recipe="preserved-ways-v1";break;
                case RawOrigin.VerifiedLegacyReconstruction:recipe="verified-legacy-cuts-v1";break;
                case RawOrigin.ExplicitAuthoredSource:recipe="authored-v1";break;
                default:throw new InvalidDataException("World13 context: raw source origin was not verified");
            }
            return new World13ReadContext(Phase.VerifiedRawSource,origin,compatibility,recipe,documentSha,verifiedOriginalReceiptSha,null);
        }
        public static World13ReadContext FromVerifiedBundleReceipt(string compatibility,string expectedRecipe,string documentSha,string verifiedCommitSha)
            =>new World13ReadContext(Phase.CommittedPreparedWorld,RawOrigin.Unspecified,compatibility,expectedRecipe,documentSha,verifiedCommitSha,null);
        public static World13ReadContext FromAuthenticatedPeerOffer(string compatibility,string expectedRecipe,string documentSha,string verifiedSignedOfferSha,string pinnedHostFingerprint)
            =>new World13ReadContext(Phase.AuthenticatedPeerWorld,RawOrigin.Unspecified,compatibility,expectedRecipe,documentSha,verifiedSignedOfferSha,Hash(pinnedHostFingerprint));
        public void RequireDocumentHash(string computedSha)
        {if(!string.Equals(ExpectedDocumentSha256,Hash(computedSha),StringComparison.Ordinal))Fail("document bytes differ from captured verified input");}
        // Call AFTER streaming preallocation+typed13 semantic validation. finalized
        // must be computed by that strict validator, never copied from one payload bool.
        public void RequireValidatedPayload(string actualCompatibility,string actualRecipe,bool finalized)
        {
            if(actualCompatibility!=ExpectedCompatibility)Fail("compatibility differs from captured context");
            if(actualRecipe!=ExpectedLayoutRecipe)Fail("layout recipe differs from verified origin/receipt");
            if(finalized!=ExpectedAuthoritative)Fail("raw/prepared authority phase mismatch; source reprepare required");
        }
        static void CheckRecipe(string recipe)
        {if(recipe!="preserved-ways-v1"&&recipe!="verified-legacy-cuts-v1"&&recipe!="authored-v1")Fail("unknown exact layout recipe");}
        static string Hash(string value)
        {
            if(value==null||value.Length!=64)throw new InvalidDataException("World13 context: missing SHA256 pin");
            foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))throw new InvalidDataException("World13 context: invalid SHA256 pin");
            return value.ToLowerInvariant();
        }
        static void Fail(string why){throw new InvalidDataException("World13 context: "+why);}
    }
}
