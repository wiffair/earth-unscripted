using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        readonly System.Random patrolPopulationRandom=new System.Random();PoliceEncounterSchedule patrolEncounter;
        Vector3 lastPatrolObserver;float nextPatrolPlacement;int patrolPoseRevision;string patrolPoseHandle;
        int SourcePoliceSlots=>World&&World.ScopedSession800!=null?VehiclePopulation.ActualVehicleRosterDomain.ScopedRoadPoliceSlots:VehiclePopulation.ActualVehicleRosterDomain.RoadPoliceSlots;
        void ResetPatrolPopulation(){patrolEncounter=null;nextPatrolPlacement=0;patrolPoseHandle=null;}
        void RefreshAutomaticTrafficObservations()
        {
            if(World&&World.ScopedSession800!=null){if(automaticTrafficFleet!=null)foreach(var car in automaticTrafficFleet)if(car)car.Fleet=automaticTrafficFleet;return;}
            var observed=new List<TrafficCar>(28);
            void Add(TrafficCar car){if(car&&!observed.Contains(car))observed.Add(car);}
            if(actualVehicles!=null)foreach(var member in actualVehicles.Domain.OrderedMembers)if(member.Root is GameObject root&&root)Add(root.GetComponent<TrafficCar>());
            if(automaticTrafficFleet!=null)foreach(var car in automaticTrafficFleet)Add(car);
            foreach(var patrol in patrols)if(patrol)Add(patrol.Marker);
            var fleet=observed.ToArray();foreach(var car in fleet)car.Fleet=fleet;
        }
        void TickPatrolPopulation(float dt)
        {
            if(!World||!World.AutomaticWorld||World.ScopedSession800!=null||!Car||Menu||PreparingStart||dt<=0)return;
            if(!ReferenceEquals(World,automaticTrafficOwnerWorld)||!ReferenceEquals(Car,automaticTrafficOwnerPlayer)){ResetPatrolPopulation();return;}
            if(!View||!View.GetComponent<Camera>())return; // Unknown visibility never grants a recycle.
            var position=Car.Body.position;
            var member=actualVehicles?.Domain.Find(Car.gameObject);if(member==null){ResetPatrolPopulation();return;}
            if(patrolEncounter==null){patrolEncounter=new PoliceEncounterSchedule(patrolPopulationRandom.NextDouble());lastPatrolObserver=position;patrolPoseRevision=member.PoseRevision;patrolPoseHandle=member.RuntimeHandle;return;}
            if(patrolPoseRevision!=member.PoseRevision||patrolPoseHandle!=member.RuntimeHandle){lastPatrolObserver=position;patrolPoseRevision=member.PoseRevision;patrolPoseHandle=member.RuntimeHandle;return;}
            float step=Vector3.Distance(position,lastPatrolObserver);lastPatrolObserver=position;
            patrolEncounter.Observe(step,Car.Grounded&&OnRoad&&Car.AbsoluteSpeed>.5f&&step<Mathf.Max(8,Car.AbsoluteSpeed*dt*2+3));
            if(Wanted>0||!patrolEncounter.Due||Time.time<nextPatrolPlacement)return;
            nextPatrolPlacement=Time.time+2+(float)patrolPopulationRandom.NextDouble()*3;
            foreach(var patrol in patrols)
            {
                if(!patrol||!patrol.Car||!patrol.IdlePatrol||Vector3.Distance(position,patrol.Car.Body.position)<350||PopulationVisible(patrol.Car.Body.position,4))continue;
                bool parked=patrolPopulationRandom.NextDouble()<.4;
                if(TryPatrolPlacement(patrol,parked)||parked&&TryPatrolPlacement(patrol,false)){patrolEncounter.Reset(patrolPopulationRandom.NextDouble());break;}
            }
        }
        bool PatrolClear(PoliceDriver patrol,Vector3 point,Vector3 forward)
        {
            if(!View||!View.GetComponent<Camera>())return false;
            if(Vector3.Distance(point,Car.Body.position)<180||PopulationVisible(point,4))return false;
            if(patrol.Marker&&patrol.Marker.Fleet!=null)foreach(var other in patrol.Marker.Fleet)if(other&&other!=patrol.Marker&&Vector3.Distance(other.PhysicsPosition,point)<25)return false;
            foreach(var other in patrols)if(other&&other!=patrol&&Vector3.Distance(other.Car.Body.position,point)<300)return false;
            return !Physics.CheckBox(point+Vector3.up*1.25f,new Vector3(1.2f,.4f,2.8f),Quaternion.LookRotation(forward),~(1<<8),QueryTriggerInteraction.Ignore);
        }
        bool TryPatrolPlacement(PoliceDriver patrol,bool parked)
        {
            var region=World.Route.Region;var renderer=World.AutomaticWorld.Renderer;
            for(int attempt=0;attempt<16;attempt++)
            {
                float angle=(float)patrolPopulationRandom.NextDouble()*Mathf.PI*2,radius=220+(float)patrolPopulationRandom.NextDouble()*330;
                var query=Car.Body.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*radius;
                var road=region.Nearest(query,out var centre,out float separation);if(road==null||road.tunnel||road.bridge||road.kind=="track"||road.width<6||separation>45)continue;
                float at=region.RoadProgress(road,centre);if(Mathf.Min(at,road.length-at)<20)continue;
                int direction=road.oneWay!=0?road.oneWay:patrolPopulationRandom.NextDouble()<.5?-1:1;
                region.RoadPoint(road,at,out var tangent);var point=region.DrivingLanePoint(road,centre,tangent*direction,out var forward);
                if(!renderer.HasDrivingSurfaceAt(point)||!renderer.HasDrivingSurfaceAt(point+forward*10)||!renderer.HasDrivingSurfaceAt(point-forward*10))continue;
                if(parked)
                {
                    // A real supported shoulder outside the traffic corridor;
                    // no generated layby and no parked-in-lane fallback.
                    var shoulder=centre+Vector3.Cross(Vector3.up,forward).normalized*region.drivingSide*(region.EffectiveWidth(road,at)*.5f+2.4f);
                    if(region.IsWater(shoulder.x,shoulder.z)||!Physics.Raycast(shoulder+Vector3.up*8,Vector3.down,out var ground,20,~(1<<8),QueryTriggerInteraction.Ignore)||ground.normal.y<.94f)continue;
                    point=ground.point;var projection=region.ProjectRoad(point);if(projection.Road==null||projection.EdgeDistance<1.5f)continue;
                    if(Mathf.Abs(point.y-centre.y)>.5f)continue; // gentle physical merge, not a cliff/embankment
                }
                if(!PatrolClear(patrol,point,forward))continue;
                if(patrol.TryPlaceIdle(point,forward,parked,90+(float)patrolPopulationRandom.NextDouble()*210))return true;
            }
            return false;
        }
        internal bool CanMergePatrol(PoliceDriver patrol)
        {
            if(!World||World.Route?.Region==null||!Car||!patrol||!patrol.Car)return false;var region=World.Route.Region;
            var projection=region.ProjectRoadForVehicle(patrol.Car.Body.position,patrol.Car.RoadSupportHeight);if(projection.Road==null||!projection.HasPlausibleHeight)return false;
            var lane=region.DrivingLanePoint(projection.Road,projection.Source.Point,patrol.transform.forward,out _);
            if(Vector3.Distance(lane,Car.Body.position)<30)return false;
            if(patrol.Marker&&patrol.Marker.Fleet!=null)foreach(var other in patrol.Marker.Fleet)if(other&&other!=patrol.Marker&&Vector3.Distance(lane,other.PhysicsPosition)<30)return false;
            return !World.AutomaticWorld||World.AutomaticWorld.Renderer.HasDrivingSurfaceAt(lane);
        }
    }
}
