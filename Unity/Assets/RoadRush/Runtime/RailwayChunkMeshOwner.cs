using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    // Runs in edit-mode verification as well as native play. The list contains ONLY
    // meshes uploaded by RailwayChunkGeometry, never primitive or shared assets.
    [ExecuteAlways,DisallowMultipleComponent]
    public sealed class RailwayChunkMeshOwner : MonoBehaviour
    {
        public int ApproachEarthBodies,ApproachRetainingBodies,ApproachExcluded,ApproachInvalidGround;
        readonly List<GeneratedMeshOwner> owners=new List<GeneratedMeshOwner>();
        public void Register(GameObject generated)
        {
            var owner=generated?generated.GetComponent<GeneratedMeshOwner>():null;
            if(owner&&owner.Mesh)owners.Add(owner);
        }
        public void Release()
        {
            // GeoWorld may already have called Geometry.ReleaseMeshes. That clears
            // each per-mesh owner, making this an idempotent cancellation fallback.
            foreach(var owner in owners)if(owner&&owner.Mesh)owner.Release();
            owners.Clear();
        }
        void OnDestroy(){Release();}
    }
}
