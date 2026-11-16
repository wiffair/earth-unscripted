using System;
namespace EarthUnscripted
{
    [Serializable] public sealed class SourceRoad13Metadata
    {public string roadId;public string[] nodeIds;}
    [Serializable] public sealed class RoadAuthorityMetadata13
    {
        public int schema;
        public string layoutRecipe,sourceDigest;
        public SourceRoad13Metadata[] sourceRoads=Array.Empty<SourceRoad13Metadata>();
        public RoadElevationProfileDto[] elevationProfiles=Array.Empty<RoadElevationProfileDto>();
        public RoadJunctionPatchDto[] junctionPatches=Array.Empty<RoadJunctionPatchDto>();
        public RoadContinuationDto[] continuations=Array.Empty<RoadContinuationDto>();
        public RoadSyntheticAnchorDto[] syntheticAnchors=Array.Empty<RoadSyntheticAnchorDto>();
    }
    // Geography retains exactly the existing12 serialized contract. Future runtime
    // metadata fields are NonSerialized; only this envelope persists13 metadata.
    [Serializable] public sealed class World13Document
    {
        public string format="EarthUnscriptedWorld13Document";
        public int version=1;
        public string compatibility;
        public GeoRegion geography;
        public RoadAuthorityMetadata13 roadAuthority;
    }
    public sealed class VerifiedRawWorld13Document
    {
        internal World13Document Document;internal readonly World13ReadContext Context;internal readonly string SourceCanonicalSha;internal readonly object Sync=new object();
        internal VerifiedRawWorld13Document(World13Document document,World13ReadContext context,string canonicalSha){Document=document;Context=context;SourceCanonicalSha=canonicalSha;}
        public World13Document TakeOwnedDocument()
        {lock(Sync){if(Document==null)throw new InvalidOperationException("World13 raw ownership already transferred");var result=Document;Document=null;return result;}}
    }
    public sealed class UnadmittedWorld13Document
    {
        internal readonly World13Document Document;
        internal UnadmittedWorld13Document(World13Document document){Document=document;}
        public World13Document CopyForInspection()=>WorldFieldJson.Clone(Document);
        public bool Admitted=>false;
    }
}
