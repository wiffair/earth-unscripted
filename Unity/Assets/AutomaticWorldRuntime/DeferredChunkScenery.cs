using System;
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
            public Batch Walls,Roofs;public int NextBuilding,WallsIndex,RoofsIndex;
        }
        sealed class BuildingCollisionWork {public Vector2Int Key;public GameObject Root;public Batch[] Batches;public bool Started;}
        readonly List<BuildingCollisionWork> buildingCollisions=new List<BuildingCollisionWork>();
        bool sceneryPreparing;
        public int PendingBuildingCollisions=>buildingCollisions.Count;
        public bool HasBuildingVisualAt(Vector3 position)=>HasGeometryAt(position)&&!chunkScenery.ContainsKey(ChunkKey(position));
        readonly Dictionary<Vector2Int,ChunkSceneryWork> chunkScenery=new Dictionary<Vector2Int,ChunkSceneryWork>();
        public int PendingSceneryChunks {get{int count=0;foreach(var work in chunkScenery.Values)if(work.Root&&work.Root.activeInHierarchy)count++;return count;}}
        void ForgetChunkScenery(GameObject root)
        {
            Vector2Int? found=null;foreach(var pair in chunkScenery)if(pair.Value.Root==root){found=pair.Key;break;}
            if(found.HasValue)chunkScenery.Remove(found.Value);
            buildingCollisions.RemoveAll(item=>item.Root==root||!item.Root);
        }
        void QueueChunkScenery(Vector2Int key,GameObject root,Map source,List<Building> buildings,string finishedKey,FinishedGeometryStore.Part[] parts,int wallsIndex,int roofsIndex)
        {
            chunkScenery[key]=new ChunkSceneryWork{Key=key,Root=root,Source=source,Buildings=buildings,FinishedKey=finishedKey,Parts=parts,WallsIndex=wallsIndex,RoofsIndex=roofsIndex};
        }
        // A failed future support check is not necessarily an actionable mesh job
        // (for example a rail crossing or unloaded data beyond an off-road forecast).
        // Keep the vehicle support guard strict; let optional scenery advance once
        // the current geometry queue has drained instead of waiting forever.
        public static bool DeferredSceneryCanProgress(bool corridorReady,int pendingGeometry)
            =>corridorReady||pendingGeometry==0;
        const int SceneryBuildingsPerPass=BuildingPipelineCapacity;
        const double SceneryPassSeconds=.05;
        // Snapshot capture restores workingMap synchronously; numeric workers
        // hold immutable inputs. Roads can continue while these jobs await results.
        IEnumerator AdvanceScenery()
        {
            sceneryPreparing=true;
            try{yield return CompleteNextScenery();}
            finally{sceneryPreparing=false;}
        }
        void PumpBuildingCollisions()
        {
            buildingCollisions.RemoveAll(item=>!item.Root);
            int active=buildingCollisions.Count(item=>item.Started);
            foreach(var item in buildingCollisions.ToArray())
            {
                if(!item.Started)continue;
                if(item.Batches.Any(batch=>batch.Baking!=null&&!batch.Baking.IsCompleted))continue;
                foreach(var batch in item.Batches)batch.FinishCollider();
                buildingCollisions.Remove(item);active--;
            }
            // Stop-distance look-ahead, with a generous walking/off-road minimum.
            float speed=embedded?embeddedSpeed:car?car.Body.linearVelocity.magnitude:0;
            float radius=Mathf.Max(256,speed*speed/5+speed*4+96);
            foreach(var item in buildingCollisions.Where(item=>!item.Started&&item.Root.activeInHierarchy).OrderBy(item=>ChunkDistance(item.Key)))
            {
                if(active>=2)break;
                if(ChunkDistance(item.Key)>radius)continue;
                foreach(var batch in item.Batches)batch.StartColliderBake(true);
                item.Started=true;active++;
            }
        }
        float ChunkDistance(Vector2Int key)
        {
            float x=Mathf.Max(key.x*ChunkSize-observer.x,0,observer.x-(key.x+1)*ChunkSize);
            float z=Mathf.Max(key.y*ChunkSize-observer.z,0,observer.z-(key.y+1)*ChunkSize);
            return Mathf.Sqrt(x*x+z*z);
        }
        public static string TransportCacheKey(string completeKey)=>FinishedGeometryStore.Digest(completeKey+"|transport-only-v1");
        public static FinishedGeometryStore.Part[] TransportParts(FinishedGeometryStore.Part[] complete,int walls,int roofs)
        {
            var result=(FinishedGeometryStore.Part[])complete.Clone();
            foreach(int index in new[]{walls,roofs})result[index]=new FinishedGeometryStore.Part{vertices=Array.Empty<Vector3>(),normals=Array.Empty<Vector3>(),indices=Array.Empty<int>()};
            return result;
        }
        public static FinishedGeometryStore.Part CombineBuildingParts(FinishedGeometryStore.Part first,FinishedGeometryStore.Part last)
        {
            int offset=first.vertices.Length;
            return new FinishedGeometryStore.Part{vertices=first.vertices.Concat(last.vertices).ToArray(),normals=first.normals.Concat(last.normals).ToArray(),indices=first.indices.Concat(last.indices.Select(index=>index+offset)).ToArray()};
        }
        IEnumerator CompleteNextScenery()
        {
            foreach(var key in chunkScenery.Where(pair=>!pair.Value.Root).Select(pair=>pair.Key).ToArray())chunkScenery.Remove(key);
            var work=chunkScenery.Values.Where(item=>item.Root&&item.Root.activeInHierarchy&&chunks.TryGetValue(item.Key,out var active)&&active==item.Root)
                .OrderBy(item=>new Vector2((item.Key.x+.5f)*ChunkSize-observer.x,(item.Key.y+.5f)*ChunkSize-observer.z).sqrMagnitude).FirstOrDefault();
            if(work==null)yield break;

            GameObject scenery=null;bool published=false;
            try
            {
                var walls=work.Walls??(work.Walls=new Batch("Mapped building massing",wall,true));
                var roofs=work.Roofs??(work.Roofs=new Batch("Mapped roofs",roof,true));
                int completed=0;var quantum=Stopwatch.StartNew();
                while(work.NextBuilding<work.Buildings.Count)
                {
                    if(!work.Root)yield break;
                    int count=Math.Min(BuildingPipelineCapacity,work.Buildings.Count-work.NextBuilding);
                    yield return PrepareBuildingBatch(work.Buildings,work.NextBuilding,count,walls,roofs,work.Source,()=>work.Root);
                    work.NextBuilding+=count;completed+=count;
                    // Return between short batches so urgent roads and other
                    // scenery chunks receive their next scheduling turn.
                    if(work.NextBuilding<work.Buildings.Count&&(completed>=SceneryBuildingsPerPass||quantum.Elapsed.TotalSeconds>=SceneryPassSeconds))yield break;
                }
                if(!work.Root)yield break;
                scenery=new GameObject("Completed chunk buildings");scenery.SetActive(false);scenery.transform.SetParent(work.Root.transform,false);
                var timing=new ChunkTiming();
                foreach(var batch in new[]{walls,roofs})
                {
                    var watch=Stopwatch.StartNew();batch.PublishVisual(scenery.transform,timing);RecordGenerationSlice("Deferred building mesh publication",watch.Elapsed.TotalMilliseconds);yield return null;
                }
                if(!work.Root)yield break;
                scenery.SetActive(true);published=true;
                buildingCollisions.Add(new BuildingCollisionWork{Key=work.Key,Root=scenery,Batches=new[]{walls,roofs}});
                geometryBytes[work.Key]=EstimateGeometryBytes(work.Root);
                // A supported road-only chunk is reusable in memory, but is
                // NEVER persisted under the complete finished-geometry schema.
                if(work.FinishedKey!=null&&work.Parts!=null)
                {
                    work.Parts[work.WallsIndex]=CombineBuildingParts(work.Parts[work.WallsIndex],walls.Capture());
                    work.Parts[work.RoofsIndex]=CombineBuildingParts(work.Parts[work.RoofsIndex],roofs.Capture());
                    finishedStore.Write(work.FinishedKey,work.Parts);
                }
                chunkScenery.Remove(work.Key);
            }
            finally
            {
                if(!published&&scenery)DestroyChunk(scenery);
            }
        }
    }
}
