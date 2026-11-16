using System;
using System.IO;
using System.Text;

namespace RoadRush.SceneryCandidate
{
    // Separate from provider-specific SourceBinding. This is a fictional cosmetic lease on an
    // existing ROOT-OWNED runtime body, NOT a provider identity or observed building semantics.
    public sealed class OwnedFacadeBinding
    {
        public string RuntimeBodyHandle;
        public GeoBuilding[] OriginalSources;
        public string[] RawSourceSha256,RawFootprintSha256;
        public string RenderGeometrySha256,SourceGenerationSha256,FrameGenerationSha256,GroundGenerationSha256;
    }
    public interface IOwnedFacadeRegistry
    {
        // True only for CURRENT exact owned references and generations. False: original procedural
        // fallback. Registry/identity/generation issuance is primary/source-factory owned.
        // v1 caller refuses sectionGeometry != entry.Geometry; no split-section inference.
        bool TryGetOwnedFacadeBinding(GeoWorld world,BuildingRenderInput.Entry entry,GeoBuilding sectionGeometry,out OwnedFacadeBinding binding);
    }
    public static class OwnedFacadeHash
    {
        // Full actual GeoBuilding content includes null/presence, id/kind/roof/height/measuredHeight,
        // directed raw XYZ count/bits. Local id is content here; NEVER canonical/runtime identity.
        public static string RawSource(GeoBuilding original)=>ReviewedFacadeConsumer.OriginalSourceCommit(original);
        public static string RenderGeometry(GeoBuilding acceptedGeometry)=>ReviewedFacadeConsumer.GeometryCommit(acceptedGeometry);
        public static string RawFootprint(GeoBuilding original)
        {
            if(original?.points==null||original.points.Length<3||original.points.Length>4096)throw new InvalidDataException("Raw footprint budget");
            using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms,Encoding.UTF8,true))
            {
                w.Write(Encoding.ASCII.GetBytes("RoadRushOwnedFootprint1\0"));w.Write(original.points.Length);
                foreach(var p in original.points)
                {
                    if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.z)||float.IsInfinity(p.z))throw new InvalidDataException("Nonfinite footprint");
                    w.Write(p.x);w.Write(p.z); // exact F32LE order/closure/signed-zero; no normalized copy
                }
                w.Flush();return ReviewedFacadeConsumer.Sha(ms.ToArray());
            }
        }
    }
}
