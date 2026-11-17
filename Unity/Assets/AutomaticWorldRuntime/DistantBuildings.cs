using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        readonly Dictionary<Vector2Int,GameObject> distantBuildings=new Dictionary<Vector2Int,GameObject>();
        readonly Dictionary<Vector2Int,Map> distantBuildingSources=new Dictionary<Vector2Int,Map>();
        void UpdateDistantBuildingVisibility()
        {foreach(var pair in distantBuildings){if(!pair.Value)continue;bool visible=!(chunks.TryGetValue(pair.Key,out var full)&&full&&full.activeInHierarchy);if(pair.Value.activeSelf!=visible)pair.Value.SetActive(visible);}}
        public int DistantBuildingChunks=>distantBuildings.Count;
        public int VisibleDistantBuildingChunks=>distantBuildings.Values.Count(root=>root&&root.activeInHierarchy);
        IEnumerator DistantBuildingLoop()
        {
            // Full driving chunks stay small for collision safety and smooth
            // streaming. Cheap visual building massing has its own wider range.
            while(true)
            {
                if(!ready){yield return null;continue;}
                float radius=1600;var centre=observer;var candidates=new Dictionary<Vector2Int,Map>();
                var scanning=System.Diagnostics.Stopwatch.StartNew();int scanned=0;
                foreach(var entry in spatialByMap.ToArray())foreach(var key in entry.Value.buildings.Keys)
                {
                    var point=new Vector3((key.x+.5f)*ChunkSize,centre.y,(key.y+.5f)*ChunkSize);
                    if((point-centre).sqrMagnitude<=radius*radius)
                    {
                        var owner=AreaAt(point.x,point.z);
                        if(owner==entry.Key)candidates[key]=owner;
                    }
                    // Distant scenery must not scan every resident planning
                    // halo in one frame while the foreground car keeps moving.
                    // The count limit also bounds work when timer resolution
                    // or individual lookups are unusually cheap.
                    if(++scanned>=128||scanning.Elapsed.TotalMilliseconds>1.5)
                    {RecordGenerationSlice("Distant building candidate scan",scanning.Elapsed.TotalMilliseconds);yield return null;scanning.Restart();scanned=0;}
                }
                // The local candidate set is immutable until this task finishes.
                // Only numeric cell keys are sorted; no Unity objects are read.
                var ordering=System.Threading.Tasks.Task.Run(()=>candidates.OrderBy(p=>
                {
                    double dx=(p.Key.x+.5)*ChunkSize-centre.x,dz=(p.Key.y+.5)*ChunkSize-centre.z;
                    return dx*dx+dz*dz;
                }).ToArray());
                while(!ordering.IsCompleted)yield return null;
                foreach(var entry in ordering.GetAwaiter().GetResult())
                {
                    if(!spatialByMap.ContainsKey(entry.Value))continue;
                    if(distantBuildings.TryGetValue(entry.Key,out var old)&&distantBuildingSources[entry.Key]!=entry.Value)
                    {ownedRoots.Remove(old);DestroyChunk(old);distantBuildings.Remove(entry.Key);distantBuildingSources.Remove(entry.Key);}
                    if(!distantBuildings.ContainsKey(entry.Key))
                    {
                        var root=new GameObject("Distant mapped buildings "+entry.Key);root.SetActive(false);root.transform.SetParent(transform,false);ownedRoots.Add(root);
                        var sides=new Batch("Distant building walls",wall,false);var tops=new Batch("Distant building roofs",roof,false);
                        if(!spatialByMap.TryGetValue(entry.Value,out var spatial)){ownedRoots.Remove(root);Destroy(root);continue;}
                        var houses=spatial.buildings[entry.Key].ToArray();
                        var slice=System.Diagnostics.Stopwatch.StartNew();
                        foreach(var house in houses)
                        {
                            // Ground and clipping read workingMap synchronously.
                            // Restore it before any yield to preserve full-chunk ownership.
                            if(!spatialByMap.ContainsKey(entry.Value))break;
                            var previous=workingMap;BuildingWork work;
                            try{using var captureTiming=EarthUnscripted.WorkTelemetry.Measure("building.capture",house.id,8);workingMap=entry.Value;work=CaptureBuilding(house);}
                            finally{workingMap=previous;}
                            if(work!=null)
                            {
                                var preparing=QueueBuilding(work);
                                while(!preparing.IsCompleted)yield return null;
                                if(!spatialByMap.ContainsKey(entry.Value))break;
                                ApplyBuilding(preparing.GetAwaiter().GetResult(),sides,tops);
                            }
                            if(slice.Elapsed.TotalMilliseconds>1.5){yield return null;slice.Restart();}
                        }
                        if(!spatialByMap.ContainsKey(entry.Value)){ownedRoots.Remove(root);DestroyChunk(root);continue;}
                        var timing=new ChunkTiming();var publishing=System.Diagnostics.Stopwatch.StartNew();
                        using(EarthUnscripted.WorkTelemetry.Measure("distant-building.walls",null,4))sides.Publish(root.transform,timing);
                        RecordGenerationSlice("Distant building walls",publishing.Elapsed.TotalMilliseconds);yield return null;publishing.Restart();
                        using(EarthUnscripted.WorkTelemetry.Measure("distant-building.roofs",null,4))tops.Publish(root.transform,timing);
                        RecordGenerationSlice("Distant building roofs",publishing.Elapsed.TotalMilliseconds);
                        distantBuildings[entry.Key]=root;distantBuildingSources[entry.Key]=entry.Value;
                    }
                    bool full=chunks.TryGetValue(entry.Key,out var active)&&active&&active.activeInHierarchy;
                    distantBuildings[entry.Key].SetActive(!full);
                    yield return null;
                }
                scanning.Restart();scanned=0;
                foreach(var entry in distantBuildings.ToArray())
                {
                    var point=new Vector3((entry.Key.x+.5f)*ChunkSize,centre.y,(entry.Key.y+.5f)*ChunkSize);
                    if((point-centre).sqrMagnitude>(radius+350)*(radius+350)||AreaAt(point.x,point.z)==null)
                    {ownedRoots.Remove(entry.Value);DestroyChunk(entry.Value);distantBuildings.Remove(entry.Key);distantBuildingSources.Remove(entry.Key);}
                    else entry.Value.SetActive(!(chunks.TryGetValue(entry.Key,out var full)&&full&&full.activeInHierarchy));
                    if(++scanned>=128||scanning.Elapsed.TotalMilliseconds>1.5)
                    {RecordGenerationSlice("Distant building retirement",scanning.Elapsed.TotalMilliseconds);yield return null;scanning.Restart();scanned=0;}
                }
                yield return new WaitForSecondsRealtime(.5f);
            }
        }
    }
}
