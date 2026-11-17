using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        readonly struct AutomaticTrafficSite
        {
            public readonly Vector3 Point, Forward;
            public AutomaticTrafficSite(Vector3 point, Vector3 forward) { Point = point; Forward = forward; }
        }


        // Candidate locations follow the current player, independent of the
        // original guided route. Reuse the streamed region's spatial road index.
        internal bool TryRecycleTraffic(TrafficCar traffic)
        {
            if (!World.AutomaticWorld || !Car || !traffic) return false;
            var region=World.Route.Region;
            for(int attempt=0;attempt<24;attempt++)
            {
                float angle=UnityEngine.Random.value*Mathf.PI*2;
                float radius=UnityEngine.Random.Range(135f,215f);
                var query=Car.Body.position+new Vector3(Mathf.Sin(angle)*radius,0,Mathf.Cos(angle)*radius);
                var road=region.Nearest(query,out var centre,out float separation);
                if(road==null||road.width<3||road.kind=="track"||road.tunnel||separation>45)continue;
                int direction=road.oneWay==0?(UnityEngine.Random.value<.5f?-1:1):road.oneWay;
                region.RoadPoint(road,region.RoadProgress(road,centre),out var tangent);
                var point=region.DrivingLanePoint(road,centre,tangent*direction,out var forward);
                float gap=Vector3.Distance(point,Car.Body.position);
                if(gap<110||gap>240||PopulationVisible(point,4))continue;
                if(!World.AutomaticWorld.Renderer.HasDrivingSurfaceAt(point)||!World.AutomaticWorld.Renderer.HasDrivingSurfaceAt(point+forward*8)||!World.AutomaticWorld.Renderer.HasDrivingSurfaceAt(point-forward*8))continue;
                bool occupied=false;
                if(traffic.Fleet!=null)foreach(var other in traffic.Fleet)if(other&&other!=traffic&&Vector3.Distance(other.PhysicsPosition,point)<18){occupied=true;break;}
                if(occupied||Physics.CheckBox(point+Vector3.up*1.3f,new Vector3(1.2f,.4f,2.8f),Quaternion.LookRotation(forward),~0,QueryTriggerInteraction.Ignore))continue;
                traffic.RecycleAt(point,forward);return true;
            }
            return false;
        }
        readonly Queue<System.Action<AutomaticTrafficSite>> deferredAutomaticActors=new Queue<System.Action<AutomaticTrafficSite>>();
        TrafficCar[] automaticTrafficFleet;WorldBuilder automaticTrafficOwnerWorld;VehicleController automaticTrafficOwnerPlayer;float nextAutomaticActorRefill;
        void ResetDeferredAutomaticTraffic(){deferredAutomaticActors.Clear();automaticTrafficFleet=null;automaticTrafficOwnerWorld=null;automaticTrafficOwnerPlayer=null;nextAutomaticActorRefill=0;ResetPatrolPopulation();}
        void RefillAutomaticTraffic()
        {
            if(deferredAutomaticActors.Count==0)return;
            if(!World||!Car||!ReferenceEquals(World,automaticTrafficOwnerWorld)||!ReferenceEquals(Car,automaticTrafficOwnerPlayer)){ResetDeferredAutomaticTraffic();return;}
            if(!World.AutomaticWorld||Time.unscaledTime<nextAutomaticActorRefill)return;
            nextAutomaticActorRefill=Time.unscaledTime+1;
            if(!TryDeferredAutomaticTrafficSite(out var site))return;
            // Dispatch each slot at most once. Scarcity above retains the queue;
            // a construction fault must not repeatedly create partial actors.
            var create=deferredAutomaticActors.Dequeue();
            try{create(site);}catch(System.Exception error){Debug.LogWarning("Deferred traffic actor disabled after construction failed: "+error);}
        }
        bool TryDeferredAutomaticTrafficSite(out AutomaticTrafficSite site)
        {
            site=default;var region=World.Route.Region;
            for(int attempt=0;attempt<24;attempt++)
            {
                float angle=UnityEngine.Random.value*Mathf.PI*2,radius=UnityEngine.Random.Range(135f,215f);
                var road=region.Nearest(Car.Body.position+new Vector3(Mathf.Sin(angle)*radius,0,Mathf.Cos(angle)*radius),out var centre,out float separation);
                if(road==null||road.width<3||road.kind=="track"||road.tunnel||separation>45)continue;
                int direction=road.oneWay==0?(UnityEngine.Random.value<.5f?-1:1):road.oneWay;
                region.RoadPoint(road,region.RoadProgress(road,centre),out var tangent);var point=region.DrivingLanePoint(road,centre,tangent*direction,out var forward);
                float gap=Vector3.Distance(point,Car.Body.position);if(gap<110||gap>240||PopulationVisible(point,4))continue;
                var renderer=World.AutomaticWorld.Renderer;
                if(!renderer.HasDrivingSurfaceAt(point)||!renderer.HasDrivingSurfaceAt(point+forward*8)||!renderer.HasDrivingSurfaceAt(point-forward*8))continue;
                bool occupied=false;
                if(automaticTrafficFleet!=null)foreach(var other in automaticTrafficFleet)if(other&&Vector3.Distance(other.PhysicsPosition,point)<22){occupied=true;break;}
                foreach(var patrol in patrols)if(patrol&&patrol.Car&&Vector3.Distance(patrol.Car.Body.position,point)<22){occupied=true;break;}
                if(occupied||Physics.CheckBox(point+Vector3.up*1.3f,new Vector3(1.2f,.4f,2.8f),Quaternion.LookRotation(forward),~0,QueryTriggerInteraction.Ignore))continue;
                site=new AutomaticTrafficSite(point,forward);return true;
            }
            return false;
        }
        readonly RaycastHit[] populationSightHits=new RaycastHit[16];
        internal bool PopulationVisible(Vector3 point,float height=2)
        {
            var camera=View?View.GetComponent<Camera>():null;
            if(!camera)return false;
            // Include an expanded viewport so objects cannot pop in at its edge.
            var foot=camera.WorldToViewportPoint(point);
            var top=camera.WorldToViewportPoint(point+Vector3.up*height);
            bool inView=foot.z>0&&foot.z<camera.farClipPlane&&Mathf.Max(foot.x,top.x)>-.12f&&Mathf.Min(foot.x,top.x)<1.12f&&Mathf.Max(foot.y,top.y)>-.12f&&Mathf.Min(foot.y,top.y)<1.12f;
            if(!inView)return false;
            if(World.AutomaticWorld)
            {
                var delta=point+Vector3.up*height-camera.transform.position;float distance=delta.magnitude;
                int count=Physics.RaycastNonAlloc(camera.transform.position,delta.normalized,populationSightHits,Mathf.Max(0,distance-2),~0,QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++)if(populationSightHits[i].collider.transform.IsChildOf(World.AutomaticWorld.Renderer.transform))return false;
            }
            return true;
        }

        static int InitialAutomaticTrafficCount(int supportedSites)=>InitialSourceTrafficCount(supportedSites,VehiclePopulation.ActualVehicleRosterDomain.RoadPoliceSlots);
        static int InitialSourceTrafficCount(int supportedSites,int sourcePoliceSlots)=>Mathf.Min(VehiclePopulation.ActualVehicleRosterDomain.RoadTrafficSlots,Mathf.Max(0,supportedSites-sourcePoliceSlots));

        List<AutomaticTrafficSite> AutomaticTrafficSites(VehicleController player, int count)
        {
            if (!World.AutomaticWorld) return null;
            var region = World.Route.Region;
            var candidates = new List<AutomaticTrafficSite>();
            foreach (var road in region.roads.OrderBy(road => road.id))
            {
                if (road.width < 3 || road.kind == "track") continue;
                for (float distance = 12; distance < road.length - 12; distance += 24)
                {
                    var centre = region.RoadPoint(road, distance, out var sourceForward);
                    foreach (int direction in road.oneWay == 0 ? new[] { 1, -1 } : new[] { road.oneWay })
                    {
                        var point = region.DrivingLanePoint(road, centre, sourceForward * direction, out var forward);
                        float gap = Vector3.Distance(player.Body.position, point);
                        if (gap < 90 || gap > 850) continue;
                        if (!World.AutomaticWorld.Renderer.HasDrivingSurfaceAt(point) ||
                            !World.AutomaticWorld.Renderer.HasGeometryAt(point + forward * 3) ||
                            !World.AutomaticWorld.Renderer.HasGeometryAt(point - forward * 3)) continue;
                        if(Physics.CheckBox(point+Vector3.up*1.3f,new Vector3(1.15f,.35f,2.7f),Quaternion.LookRotation(forward,Vector3.up),~(1<<8),QueryTriggerInteraction.Ignore))continue;
                        candidates.Add(new AutomaticTrafficSite(point, forward));
                    }
                }
            }
            var selected = new List<AutomaticTrafficSite>();
            var random = new System.Random(128);
            foreach (var site in candidates.OrderBy(site => random.Next()))
            {
                if (selected.Any(previous => Vector3.Distance(previous.Point, site.Point) < 22)) continue;
                selected.Add(site);
                if (selected.Count == count) return selected;
            }
            // Scarce supported space limits population, never a valid player world.
            // Unfilled factory slots are retried off-camera as roads become ready.
            return selected;
        }
    }
}
