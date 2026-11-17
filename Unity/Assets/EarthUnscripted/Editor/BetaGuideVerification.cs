using System;
using System.Collections.Generic;
using System.Reflection;
using EarthUnscripted.NewbridgePrototype;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BetaGuideVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
            RoutePath Guide(Vector3[] points)=>new RoutePath(new GeoRegion{circuit=true,route=points});
            // Exact Kessock acquisition shape: a raw saved point, a lateral
            // four-metre connector, then the genuine NW bridge centre line.
            var route=Guide(new[]{new Vector3(-250,16.79f,2176),new Vector3(-252.9673f,16.79f,2173.321f),new Vector3(-254.5032f,16.86f,2174.602f),new Vector3(-256.039f,16.93f,2175.883f),new Vector3(-300,19,2212)});
            float start=BetaSmokeVerification.SupportedGuideStart(route,0);
            Check(start>route.DistanceAtPoint(1),"Initial guide alignment must pass the lateral acquisition connector");
            Check(Vector3.Dot(route.PlanarForward(start),new Vector3(-.768f,0,.64f).normalized)>.98f,"Kessock QA aligns with NW bridge, not SW connector");
            var ordinary=Guide(new[]{Vector3.zero,new Vector3(0,0,25),new Vector3(25,0,25)});
            Check(BetaSmokeVerification.SupportedGuideStart(ordinary,0)==0,"A real road approach/junction must not be skipped");
            var loop=Guide(new[]{Vector3.zero,new Vector3(0,0,100),new Vector3(10,0,100),new Vector3(10,0,0),Vector3.zero});
            float station=BetaSmokeVerification.ClosestNear(loop,new Vector3(9,0,50),50,25);
            Check(Mathf.Abs(station-50)<.01f,"A spatially closer return leg must not jump the physical route station");
            var root=new GameObject("Published mesh audit fixture");
            try
            {
                var world=root.AddComponent<NewbridgeWorld>();
                var published=new NewbridgeWorld.Feature{id="owned",sourceId="owned",category="road",width=4,bridge=true,points=new[]{new Vector3(-20,0,2),new Vector3(-20,0,22)}};
                var halo=new NewbridgeWorld.Feature{id="unpublished",sourceId="unpublished",category="road",width=4,bridge=true,points=new[]{new Vector3(20,0,2),new Vector3(20,0,22)}};
                var map=new NewbridgeWorld.Map{features=new[]{published,halo},buildings=Array.Empty<NewbridgeWorld.Building>(),minX=-64,minZ=0,maxX=64,maxZ=64,coreMinX=-64,coreMinZ=0,coreMaxX=64,coreMaxZ=64,nx=2,nz=2,gridStep=64,heights=new float[4]};
                const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(NewbridgeWorld).GetField("workingMap",hidden).SetValue(world,map);
                typeof(NewbridgeWorld).GetMethod("IndexMap",hidden).Invoke(world,new object[]{map});
                var chunk=new GameObject("Active published chunk");chunk.transform.SetParent(root.transform,false);
                var road=new GameObject("Road asphalt");road.transform.SetParent(chunk.transform,false);var physical=road.AddComponent<BoxCollider>();physical.center=new Vector3(-20,-.05f,12);physical.size=new Vector3(8,.1f,30);
                var chunks=(Dictionary<Vector2Int,GameObject>)typeof(NewbridgeWorld).GetField("chunks",hidden).GetValue(world);chunks[new Vector2Int(-1,0)]=chunk;
                Physics.SyncTransforms();var evidence=world.InspectJunctionGeometry(new Vector3(0,0,12),50);
                Check(evidence.roadColliderSamples==3&&evidence.unpublishedRoadSamples==3,"Published road samples and resident unpublished halo samples have distinct counts");
                Check(evidence.failures.Count==0,"Unpublished numeric halo is not reported as a physical collider hole");
                physical.enabled=false;Physics.SyncTransforms();evidence=world.InspectJunctionGeometry(new Vector3(0,0,12),50);
                Check(evidence.failures.Count==3,"Real missing support inside an advertised published chunk still fails");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("BETA GUIDE / PUBLISHED GEOMETRY PASS "+checks);
        }
    }
}
