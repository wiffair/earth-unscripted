namespace RoadRush.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Data-only seam intended INSIDE the actual publication source worker.
    /// The caller must supply its FULL private canonical GeoRegion clone, before
    /// publishing a SourceModel. A borrowed active World.Region is forbidden by
    /// contract. This seam cannot prove that provenance or issue a capability.
    /// Publication owner alone stores/revokes this immutable data with its model.
    /// No Unity object/native method is accessed; GeoRegion is plain source data.
    /// </summary>
    internal static class RasterHabitatSourceCopy800
    {
        internal static bool TryCopyFromPrivateCanonicalClone(GeoRegion privateCanonicalClone,
                                                              out RasterHabitat800 raster)
        {
            raster = null;
            return privateCanonicalClone != null && RasterHabitat800.TryCopy(
                privateCanonicalClone.minX, privateCanonicalClone.minZ,
                privateCanonicalClone.width, privateCanonicalClone.depth,
                privateCanonicalClone.landNx, privateCanonicalClone.landNz,
                privateCanonicalClone.landCover, out raster);
        }
    }
}
