using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        sealed class BuildingWorkStats {public int trimmed,failedRoofs;public double overlap;}
        sealed class BuildingWork
        {public Building house;public List<Vector3> points;public float foundation,roofHeight;public SpatialData spatial;public bool audit;}
        sealed class PreparedBuilding
        {public Batch walls,roofs;public BuildingWorkStats stats;public double milliseconds;}
        public int CompletedBuildingJobs=>report.buildingJobs;
        public double MaximumBuildingWorkerMilliseconds=>report.maximumBuildingWorkerMilliseconds;
        static readonly SemaphoreSlim buildingWorkers=new SemaphoreSlim(2,2);
        Task<PreparedBuilding> QueueBuilding(BuildingWork work)=>Task.Run(async ()=>
        {
            await buildingWorkers.WaitAsync().ConfigureAwait(false);
            try
            {
                var watch=Stopwatch.StartNew();
                var result=new PreparedBuilding{walls=new Batch("Prepared building walls",null,false),roofs=new Batch("Prepared building roofs",null,false),stats=new BuildingWorkStats()};
                // Only immutable numeric source/index data and plain vertex/index
                // lists enter this worker. No renderer, scene, mesh or material API.
                CompileBuilding(work,result.walls,result.roofs,result.stats);
                result.milliseconds=watch.Elapsed.TotalMilliseconds;return result;
            }
            finally{buildingWorkers.Release();}
        });
        void ApplyBuilding(PreparedBuilding prepared,Batch walls,Batch roofs)
        {
            using var timing=EarthUnscripted.WorkTelemetry.Measure("building.publish",null,8);
            var watch=Stopwatch.StartNew();walls.Append(prepared.walls);roofs.Append(prepared.roofs);
            report.buildingJobs++;report.maximumBuildingWorkerMilliseconds=Math.Max(report.maximumBuildingWorkerMilliseconds,prepared.milliseconds);
            report.trimmedBuildings+=prepared.stats.trimmed;report.failedBuildingRoofTriangulations+=prepared.stats.failedRoofs;
            report.maximumBuildingRoadOverlap=Math.Max(report.maximumBuildingRoadOverlap,prepared.stats.overlap);
            RecordGenerationSlice("Building publication",watch.Elapsed.TotalMilliseconds);
        }
        // Bounded look-ahead keeps the two numeric workers occupied without
        // one render-frame round trip for every individual building. Capture
        // and append still run on the main thread, in original source order.
        const int BuildingPipelineCapacity=8;
        const double BuildingMainSliceMilliseconds=2;
        IEnumerator PrepareBuildingBatch(IList<Building> houses,int start,int count,Batch walls,Batch roofs,Map source=null,Func<bool> valid=null)
        {
            count=Math.Min(BuildingPipelineCapacity,Math.Min(count,houses.Count-start));
            var pending=new Task<PreparedBuilding>[count];var watch=Stopwatch.StartNew();
            var captureSource=source??workingMap;
            for(int i=0;i<count;i++)
            {
                if(valid!=null&&!valid())yield break;
                var previous=workingMap;BuildingWork work;var capture=Stopwatch.StartNew();
                try{workingMap=captureSource;using(EarthUnscripted.WorkTelemetry.Measure("building.capture",houses[start+i].id,8))work=CaptureBuilding(houses[start+i]);}
                finally{workingMap=previous;}
                RecordGenerationSlice("Building snapshot",capture.Elapsed.TotalMilliseconds);
                if(work!=null){pending[i]=QueueBuilding(work);pending[i].ContinueWith(f=>{var observed=f.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
                if(i+1<count&&watch.Elapsed.TotalMilliseconds>=BuildingMainSliceMilliseconds){yield return null;watch.Restart();}
            }
            watch.Restart();
            for(int i=0;i<count;i++)
            {
                if(pending[i]!=null)
                {
                    while(!pending[i].IsCompleted){yield return null;watch.Restart();}
                    if(valid!=null&&!valid())yield break;
                    ApplyBuilding(pending[i].GetAwaiter().GetResult(),walls,roofs);
                }
                if(i+1<count&&watch.Elapsed.TotalMilliseconds>=BuildingMainSliceMilliseconds){yield return null;watch.Restart();}
            }
        }
        IEnumerator PrepareBuildingRange(IList<Building> houses,Batch walls,Batch roofs,Map source=null,Func<bool> valid=null)
        {
            for(int i=0;i<houses.Count;i+=BuildingPipelineCapacity)
            {
                if(valid!=null&&!valid())yield break;
                yield return PrepareBuildingBatch(houses,i,BuildingPipelineCapacity,walls,roofs,source,valid);
            }
        }
        IEnumerator PrepareBuilding(Building house,Batch walls,Batch roofs)
        {
            BuildingWork work;
            var capture=Stopwatch.StartNew();
            using(EarthUnscripted.WorkTelemetry.Measure("building.capture",house.id,8))work=CaptureBuilding(house);
            RecordGenerationSlice("Building snapshot",capture.Elapsed.TotalMilliseconds);
            if(work==null)yield break;
            var preparing=QueueBuilding(work);
            while(!preparing.IsCompleted)yield return null;
            ApplyBuilding(preparing.GetAwaiter().GetResult(),walls,roofs);
        }
    }
}
