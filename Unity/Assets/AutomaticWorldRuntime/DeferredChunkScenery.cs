using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        sealed class ChunkSceneryWork
        {
            public Vector2Int Key;
            public GameObject Root;
            public Map Source;
            public List<Building> Buildings;
            public string FinishedKey;
            public FinishedGeometryStore.Part[] Parts;
        }
        readonly Dictionary<Vector2Int,ChunkSceneryWork> chunkScenery=new Dictionary<Vector2Int,ChunkSceneryWork>();
        public int PendingSceneryChunks {get{int count=0;foreach(var work in chunkScenery.Values)if(work.Root&&work.Root.activeInHierarchy)count++;return count;}}
        void ForgetChunkScenery(GameObject root)
        {
            Vector2Int? found=null;foreach(var pair in chunkScenery)if(pair.Value.Root==root){found=pair.Key;break;}
            if(found.HasValue)chunkScenery.Remove(found.Value);
        }
        void QueueChunkScenery(Vector2Int key,GameObject root,Map source,List<Building> buildings,string finishedKey,FinishedGeometryStore.Part[] parts)
        {
            chunkScenery[key]=new ChunkSceneryWork{Key=key,Root=root,Source=source,Buildings=buildings,FinishedKey=finishedKey,Parts=parts};
        }
        // Called serially by the geometry scheduler after transport coverage.
        // Never run alongside BuildChunk: both intentionally share workingMap.
        IEnumerator CompleteNextScenery()
        {
            foreach(var key in chunkScenery.Where(pair=>!pair.Value.Root).Select(pair=>pair.Key).ToArray())chunkScenery.Remove(key);
            var work=chunkScenery.Values.Where(item=>item.Root&&item.Root.activeInHierarchy&&chunks.TryGetValue(item.Key,out var active)&&active==item.Root)
                .OrderBy(item=>new Vector2((item.Key.x+.5f)*ChunkSize-observer.x,(item.Key.y+.5f)*ChunkSize-observer.z).sqrMagnitude).FirstOrDefault();
            if(work==null)yield break;
            var previous=workingMap;workingMap=work.Source;
            GameObject scenery=null;bool published=false;
            try
            {
                var walls=new Batch("Mapped building massing",wall,true);var roofs=new Batch("Mapped roofs",roof,true);
                foreach(var house in work.Buildings)
                {
                    if(!work.Root)yield break;
                    yield return PrepareBuilding(house,walls,roofs);
                }
                if(!work.Root)yield break;
                scenery=new GameObject("Completed chunk buildings");scenery.SetActive(false);scenery.transform.SetParent(work.Root.transform,false);
                var timing=new ChunkTiming();
                foreach(var batch in new[]{walls,roofs})
                {
                    var watch=Stopwatch.StartNew();batch.Publish(scenery.transform,timing);RecordGenerationSlice("Deferred building mesh publication",watch.Elapsed.TotalMilliseconds);yield return null;
                }
                foreach(var batch in new[]{walls,roofs})
                {
                    while(batch.Baking!=null&&!batch.Baking.IsCompleted)yield return null;
                    batch.FinishCollider();yield return null;
                }
                if(!work.Root)yield break;
                scenery.SetActive(true);published=true;
                geometryBytes[work.Key]=EstimateGeometryBytes(work.Root);
                // A supported road-only chunk is reusable in memory, but is
                // NEVER persisted under the complete finished-geometry schema.
                if(work.FinishedKey!=null&&work.Parts!=null)
                {
                    work.Parts[11]=walls.Capture();work.Parts[12]=roofs.Capture();
                    finishedStore.Write(work.FinishedKey,work.Parts);
                }
                chunkScenery.Remove(work.Key);
            }
            finally
            {
                workingMap=previous;
                if(!published&&scenery)DestroyChunk(scenery);
            }
        }
    }
}
