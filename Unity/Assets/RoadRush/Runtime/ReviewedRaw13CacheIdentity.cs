using System;using System.IO;
namespace RoadRush
{
    // Read authority comes from an independently reviewed application catalog,
    // never a cache header, caller-supplied hash or deserialized identity object.
    public sealed class ReviewedRaw13CacheIdentity
    {
        public string CatalogId{get;}public string Compatibility{get;}public string CacheKey{get;}public string DocumentSha256{get;}public string EvidenceSha256{get;}public string SourceDigest{get;}public string CommitSha256{get;}
        public int DocumentBytes{get;}public int EvidenceBytes{get;}public int CommitBytes{get;}
        public string Scope=>"Reviewed frozen Pantside3km verified RAW input only; no prepared-world admission or current provider/database signature";
        ReviewedRaw13CacheIdentity(string id,string compatibility,string key,string document,string evidence,string source,string commit,int documentBytes,int evidenceBytes,int commitBytes)
        {CatalogId=id;Compatibility=compatibility;CacheKey=key;DocumentSha256=document;EvidenceSha256=evidence;SourceDigest=source;CommitSha256=commit;DocumentBytes=documentBytes;EvidenceBytes=evidenceBytes;CommitBytes=commitBytes;}
        public static ReviewedRaw13CacheIdentity FromReviewedCatalog(string id,string expectedCompatibility)
        {
            const string compatibility="RoadRush/0.19.0/geometry13";
            if(id!="pantside-3km-frozen-source-1074-v1"||expectedCompatibility!=compatibility)throw new InvalidDataException("Reviewed raw13 catalog entry/compatibility not available");
            // Exact actual Unity PASS43 output, reviewed independently of the cache.
            // Catalog updates require a code review; no disk receipt adds entries.
            return new ReviewedRaw13CacheIdentity(id,compatibility,
                "fb4bb364b93cb508a742fc647d60245dedf988cf0cea193b192a6ab33d35fa05",
                "d6ed4c8c979b38f023c7fcc9675fe736f12c3d511e83f848e4a573e6548454d6",
                "06be2feb896944d33de5d92c84e06a7cd5be7ac7b42aee1487d7aff1b06fe343",
                "6c46339f3b03141caff71417448980759d96caf1e3547d92f4a4d093cd4d6f42",
                "1ba0d156c42857e2b3f6719b05c1ad27f6b3a98f377ea6e392adb08c453a5619",1749615,1558,447);
        }
        internal World13ReadContext ReadContext()=>World13ReadContext.FromVerifiedRawReceipt(World13ReadContext.RawOrigin.VerifiedFreshProvider,Compatibility,DocumentSha256,EvidenceSha256);
    }
}
