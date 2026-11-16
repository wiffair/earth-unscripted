using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace RoadRush.NewbridgePrototype
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
        Task<PreparedBuilding> QueueBuilding(BuildingWork work)=>Task.Run(()=>
        {
            buildingWorkers.Wait();
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
            using var timing=RoadRush.WorkTelemetry.Measure("building.publish",null,8);
            var watch=Stopwatch.StartNew();walls.Append(prepared.walls);roofs.Append(prepared.roofs);
            report.buildingJobs++;report.maximumBuildingWorkerMilliseconds=Math.Max(report.maximumBuildingWorkerMilliseconds,prepared.milliseconds);
            report.trimmedBuildings+=prepared.stats.trimmed;report.failedBuildingRoofTriangulations+=prepared.stats.failedRoofs;
            report.maximumBuildingRoadOverlap=Math.Max(report.maximumBuildingRoadOverlap,prepared.stats.overlap);
            RecordGenerationSlice("Building publication",watch.Elapsed.TotalMilliseconds);
        }
        IEnumerator PrepareBuilding(Building house,Batch walls,Batch roofs)
        {
            BuildingWork work;
            var capture=Stopwatch.StartNew();
            using(RoadRush.WorkTelemetry.Measure("building.capture",house.id,8))work=CaptureBuilding(house);
            RecordGenerationSlice("Building snapshot",capture.Elapsed.TotalMilliseconds);
            if(work==null)yield break;
            var preparing=QueueBuilding(work);
            while(!preparing.IsCompleted)yield return null;
            ApplyBuilding(preparing.GetAwaiter().GetResult(),walls,roofs);
        }
    }
}
