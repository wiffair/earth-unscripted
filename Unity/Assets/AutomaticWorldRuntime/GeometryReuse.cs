using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace RoadRush.NewbridgePrototype
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
            var identity = new StringBuilder("finished-mesh-v1|").Append(RoadRush.GeometryCacheVersion.Identity).Append('|').Append(Application.unityVersion).Append('|').Append(key.x).Append('|').Append(key.y).Append('|').Append(coastalPatch == null ? "pre-coast" : finishedCoastHash);
            var owner = new Vector2Int(Mathf.FloorToInt((key.x + .5f) * ChunkSize / AreaSizeMetres), Mathf.FloorToInt((key.y + .5f) * ChunkSize / AreaSizeMetres));
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                var area = owner + new Vector2Int(x, z);
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
            root.SetActive(true);chunks.Add(key,root);report.reusedGeometryChunks++;
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
