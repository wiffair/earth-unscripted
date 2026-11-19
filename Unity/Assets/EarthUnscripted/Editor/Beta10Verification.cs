using System;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class Beta10Verification
    {
        public static void Batch()
        {
            try
            {
                Run();EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            WorldCacheUsageVerification.Run();LoadingPipelineVerification.Run();VehicleProjectionCostVerification.Run();TreeVisibilityJobVerification.Run();BuildingPipelineVerification.Run();TyreAudioVerification.Run();ScenerySchedulingVerification.Run();Beta22RadioNormalizationVerification.Run();Beta22RadioPcmVerification.Run();FinishedGeometryCacheVerification.Run();PackControllerVerification.Run();Beta22MenuVerification.Run();MacWorkPriorityVerification.Run();DrivingCoverageEndVerification.Run();RailwayFurnitureVerification.Run();
            PolicePatrolVerification.Run();
            DeferredVehicleRosterVerification.Run();
            Debug.Log("CARDIFF CONNECTED ROUTE PASS "+BetaLocationRouteVerification.Exercise());ControlPlacementCacheVerification.Run();GeometryPriorityVerification.Run();DrivingDataPriorityVerification.Run();TrafficScarcityVerification.Run();RoadCoverageLevelVerification.Run();VehiclePackExtensionVerification.Run();PreparedAreaFileVerification.Run();GeoSegmentStorageVerification.Run();AutomaticBuildingReuseVerification.Run();NativeSegmentIndexVerification.Run();BetaTimingPolicyVerification.Run();ShaderFallbackVerification.Run();GraphicsWarmupLoadingVerification.Run();TerrainPavementClearanceVerification.Run();SurfaceQueryViewVerification.Run();WindshieldRainVerification.Run();Beta10RadioVerification.Run();Beta10MenuVerification.Run();ImportedLampStateVerification.Run();Beta10MinimapVerification.Run();
            CoastalStartupVerification.Run();CoastalIndexVerification.Run();PublishedSceneryGroundVerification.Run();BoundedQueryIndexVerification.Run();TerrainWaterRegressionVerification.Run();VehicleVergeVerification.Run();BetaGuideVerification.Run();BridgeCorridorVerification.Run();
            LocalFeedbackVerification.Run();TransportControlVerification.Run();
            TrafficCooperationVerification.Run();TrafficPassingVerification.Run();RoadQueryIndexSharingVerification.Run();AutomaticTopologyAllocationVerification.Run();StreamingPolicy();StreamingColliderCoverage();
            Debug.Log("BETA10 REGRESSION PASS");
        }
        public static void BuildPair()
        {
            try{Run();SharedBetaBuild.BuildPair();}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void BuildStandard()
        {
            try{Run();SharedBetaBuild.BuildStandard();}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        static void StreamingColliderCoverage()
        {
            var root=new GameObject("Road coverage fixture");var chunk=new GameObject("Published terrain without road");chunk.transform.SetParent(root.transform,false);
            var mesh=new Mesh{vertices=new[]{new Vector3(-10,0,-10),new Vector3(-10,0,10),new Vector3(10,0,10),new Vector3(10,0,-10)},triangles=new[]{0,1,2,0,2,3}};
            try
            {
                var world=root.AddComponent<NewbridgePrototype.NewbridgeWorld>();
                var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(NewbridgePrototype.NewbridgeWorld).GetField("ready",hidden).SetValue(world,true);
                var chunks=(System.Collections.Generic.Dictionary<Vector2Int,GameObject>)typeof(NewbridgePrototype.NewbridgeWorld).GetField("chunks",hidden).GetValue(world);chunks[Vector2Int.zero]=chunk;
                if(!world.HasGeometryAt(Vector3.zero)||world.HasDrivingSurfaceAt(Vector3.zero))throw new Exception("Terrain-only active chunk must not claim a driving surface");
                var road=new GameObject("Road asphalt");road.transform.SetParent(chunk.transform,false);var collider=road.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
                if(!world.HasDrivingSurfaceAt(Vector3.zero))throw new Exception("Baked road collider is recognized");
                collider.enabled=false;Physics.SyncTransforms();if(world.HasDrivingSurfaceAt(Vector3.zero))throw new Exception("Disabled road collider is unavailable");
                collider.enabled=true;road.transform.localPosition=Vector3.down*3;Physics.SyncTransforms();if(world.HasDrivingSurfaceAt(Vector3.zero))throw new Exception("Lower level road cannot stand in for missing upper deck");
                Debug.Log("BETA10 COLLIDER COVERAGE PASS 4");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
        }
        static void StreamingPolicy()
        {
            void Check(bool ok,string label){if(!ok)throw new Exception(label);}
            float previous=0;
            for(float speed=0;speed<=90;speed+=1)
            {
                float horizon=AutomaticWorldHost.PreparationHorizon(speed,5);
                Check(horizon>=previous,"Forecast horizon grows with speed");previous=horizon;
                Check(horizon>=speed*speed/5+speed*3||horizon>=1800,"Forecast covers comfortable stopping and request lead time");
            }
            Check(AutomaticWorldHost.PreparationHorizon(35,15)>AutomaticWorldHost.PreparationHorizon(35,3),"Slow preparation increases lookahead");
            Check(AutomaticWorldHost.CoverageSpeed(0)==0&&AutomaticWorldHost.CoverageSpeed(24)==0,"Missing support requires stop before edge");
            Check(AutomaticWorldHost.CoverageSpeed(224)>AutomaticWorldHost.CoverageSpeed(124),"Supported distance provides gradual speed envelope");
            Check(NewbridgePrototype.NewbridgeWorld.DrivingCorridorPriority(100,45)<NewbridgePrototype.NewbridgeWorld.DrivingCorridorPriority(400,0),"Nearby road support wins over a perfectly centred distant cell");
            Check(NewbridgePrototype.NewbridgeWorld.DrivingCorridorPriority(1800,45)<0,"Even the far driving corridor precedes optional side scenery");
            Debug.Log("BETA10 STREAMING POLICY PASS");
        }
    }
}
