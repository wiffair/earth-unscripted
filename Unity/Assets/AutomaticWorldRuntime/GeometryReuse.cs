using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // Numeric disk-cache entries do not contain Unity meshes or cooked
        // colliders. Retain a bounded recent set so a return trip can activate
        // the exact supported geometry instead of rebuilding it.
        FinishedGeometryStore finishedStore;
        readonly Dictionary<Map, string> finishedSourceHashes = new Dictionary<Map, string>();
        CoastRing[] finishedCoastIdentity;
        string finishedCoastHash;
        public long FinishedCacheHits => finishedStore?.Hits ?? 0;
        public long FinishedCacheBytesRead => finishedStore?.BytesRead ?? 0;
        public long FinishedCacheBytesWritten => finishedStore?.BytesWritten ?? 0;
        public int FinishedCachePendingWrites => finishedStore?.Pending ?? 0;
        string FinishedChunkKey(Vector2Int key)
        {
            if (finishedStore == null) return null;
            if (coastalPatch != null && !ReferenceEquals(finishedCoastIdentity, coastalPatch.shore))
            {
                finishedCoastIdentity = coastalPatch.shore;
                finishedCoastHash = FinishedGeometryStore.Digest(JsonUtility.ToJson(coastalPatch));
            }
            var owner = new Vector2Int(Mathf.FloorToInt((key.x + .5f) * ChunkSize / AreaSizeMetres), Mathf.FloorToInt((key.y + .5f) * ChunkSize / AreaSizeMetres));
            if(!areas.TryGetValue(owner,out var owningMap)||!spatialByMap.TryGetValue(owningMap,out var spatial))return null;
            float margin=spatial.geometryQueryMargin;
            if(float.IsNaN(margin)||float.IsInfinity(margin)||margin<0)return null;
            float minX=key.x*ChunkSize-margin,minZ=key.y*ChunkSize-margin,maxX=(key.x+1)*ChunkSize+margin,maxZ=(key.y+1)*ChunkSize+margin;
            // Cached shells may finish after roads. Include every footprint
            // assigned to this chunk now, even if its visuals are deferred;
            // large buildings can query terrain well beyond their centre cell.
            if(spatial.buildings.TryGetValue(key,out var houses))foreach(var house in houses)
                foreach(var point in house.points)
                {
                    if(float.IsNaN(point.x)||float.IsInfinity(point.x)||float.IsNaN(point.z)||float.IsInfinity(point.z))return null;
                    minX=Mathf.Min(minX,point.x-margin);maxX=Mathf.Max(maxX,point.x+margin);
                    minZ=Mathf.Min(minZ,point.z-margin);maxZ=Mathf.Max(maxZ,point.z+margin);
                }
            if(float.IsInfinity(minX)||float.IsInfinity(minZ)||float.IsInfinity(maxX)||float.IsInfinity(maxZ))return null;
            if((double)minX/AreaSizeMetres<=int.MinValue||(double)minZ/AreaSizeMetres<=int.MinValue||(double)maxX/AreaSizeMetres>=int.MaxValue||(double)maxZ/AreaSizeMetres>=int.MaxValue)return null;
            int firstX=Mathf.FloorToInt(minX/AreaSizeMetres),lastX=Mathf.FloorToInt(maxX/AreaSizeMetres);
            int firstZ=Mathf.FloorToInt(minZ/AreaSizeMetres),lastZ=Mathf.FloorToInt(maxZ/AreaSizeMetres);
            // Extreme imported footprints remain renderable but cannot create
            // unbounded cache-key scans. Declining a cache entry is safe.
            if((long)lastX-firstX+1>64||(long)lastZ-firstZ+1>64||((long)lastX-firstX+1)*((long)lastZ-firstZ+1)>64)return null;
            var identity = new StringBuilder("finished-mesh-local-dependencies-v2|").Append(EarthUnscripted.GeometryCacheVersion.Identity).Append('|').Append(Application.unityVersion).Append('|').Append(key.x).Append('|').Append(key.y).Append('|').Append(coastalPatch == null ? "pre-coast" : finishedCoastHash);
            // Include present/absent state and exact source hashes only where
            // this mesh can query them. An unrelated adjacent core changing
            // residency must not invalidate every interior chunk in this core.
            for(int z=firstZ;z<=lastZ;z++)for(int x=firstX;x<=lastX;x++)
            {
                var area = new Vector2Int(x,z);
                identity.Append('|').Append(area.x).Append(',').Append(area.y).Append(':');
                if (areas.TryGetValue(area, out var source))
                {
                    if (!finishedSourceHashes.TryGetValue(source, out var hash)) return null;
                    identity.Append(hash);
                }
                else identity.Append("absent");
            }
            return FinishedGeometryStore.Digest(identity.ToString());
        }
        const long WarmGeometryBudget = 128L * 1024 * 1024;
        const int MaximumWarmChunks = 256;
        sealed class WarmChunk
        {
            public GameObject root;
            public long bytes;
            public LinkedListNode<Vector2Int> age;
        }
        readonly Dictionary<Vector2Int,WarmChunk> warmChunks = new Dictionary<Vector2Int,WarmChunk>();
        readonly Dictionary<Vector2Int,long> geometryBytes = new Dictionary<Vector2Int,long>();
        readonly LinkedList<Vector2Int> warmAge = new LinkedList<Vector2Int>();
        long warmBytes;
        int geometryRevision;

        long EstimateGeometryBytes(GameObject root)
        {
            // Include a conservative second mesh allocation for collision,
            // plus object overhead. This is an accounting bound, not a claim
            // that the engine's entire allocation is exactly measurable here.
            long total = 65536;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if(filter.sharedMesh) total += 2 * Profiler.GetRuntimeMemorySizeLong(filter.sharedMesh);
            return total;
        }

        void RetainGeometry(Vector2Int key, GameObject root)
        {
            root.SetActive(false);
            if(!geometryBytes.TryGetValue(key,out long bytes)) bytes=EstimateGeometryBytes(root);
            warmChunks.Add(key,new WarmChunk {root=root,bytes=bytes,age=warmAge.AddLast(key)});
            warmBytes+=bytes;
            while(warmBytes>WarmGeometryBudget || warmChunks.Count>MaximumWarmChunks)
                RemoveWarmGeometry(warmAge.First.Value,true);
            report.warmGeometryBytes=warmBytes;
            report.warmGeometryChunks=warmChunks.Count;
        }

        GameObject RemoveWarmGeometry(Vector2Int key,bool destroy)
        {
            var item=warmChunks[key];
            warmChunks.Remove(key);warmAge.Remove(item.age);warmBytes-=item.bytes;
            if(destroy){DestroyChunk(item.root);geometryBytes.Remove(key);report.evictedWarmChunks++;}
            return item.root;
        }

        bool RestoreGeometry(Vector2Int key)
        {
            if(!warmChunks.ContainsKey(key))return false;
            var root=RemoveWarmGeometry(key,false);
            if(!root){geometryBytes.Remove(key);return false;}
            root.SetActive(true);chunks.Add(key,root);sceneryGroundRevision++;fineTerrainVisibilityRevision++;report.reusedGeometryChunks++;
            report.warmGeometryBytes=warmBytes;report.warmGeometryChunks=warmChunks.Count;
            return true;
        }

        void PurgeWarmArea(Vector2Int area)
        {
            var remove=new List<Vector2Int>();
            foreach(var key in warmChunks.Keys)
                if(Mathf.FloorToInt((key.x+.5f)*ChunkSize/AreaSizeMetres)==area.x &&
                    Mathf.FloorToInt((key.y+.5f)*ChunkSize/AreaSizeMetres)==area.y)remove.Add(key);
            foreach(var key in remove)RemoveWarmGeometry(key,true);
            report.warmGeometryBytes=warmBytes;report.warmGeometryChunks=warmChunks.Count;
        }
    }
}
