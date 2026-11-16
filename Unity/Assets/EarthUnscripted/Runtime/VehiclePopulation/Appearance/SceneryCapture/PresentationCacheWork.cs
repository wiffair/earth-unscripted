namespace EarthUnscripted.FunnyAnimals.ActualRegistryConsumer
{
    /// <summary>
    /// Observed operation counts for ONE helper candidate call. Counts are not native
    /// time/GC acceptance and exclude opaque work inside DeformationExtent/font/native
    /// getters. Owner controls actual per-frame budgets and dirty/publication authority.
    /// </summary>
    internal sealed class PresentationCacheWork
    {
        internal int NodesVisited;
        internal int RenderersVisited;
        internal int UniqueMeshMetadataReads;
        internal int VertexBufferReads;
        internal int IndexBufferReads;
        internal int VerticesHashed;
        internal long IndicesHashed;
        internal int DeformationExtentGetterCalls;
        internal int TemplateLookups;
    }
}
