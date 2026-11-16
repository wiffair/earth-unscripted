namespace RoadRush
{
    public sealed partial class GeoWorld
    {
        // Read-only ownership stamps; not permission to bind provider provenance.
        public int SurfaceGeneration=>generation;
        internal FinalizedRoadSurfacePlan FacadePreparedGroundOwner=>preparedRoadSurfaces;
    }
}
