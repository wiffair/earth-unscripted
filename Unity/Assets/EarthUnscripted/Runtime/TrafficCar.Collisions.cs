using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class TrafficCar
    {
        bool collisionDynamics, struck;
        float dynamicsStarted;
        readonly float[] crashCompression=new float[4];
        public float CollisionMassKg=>Core.GarageRules.Catalogue[Visual?Visual.Model:0].Mass;
        public float CollisionDamage {get;private set;}

        void BuildTrafficContactProfile()
        {
            if(GetComponent<VehicleController>())return;
            var chassis=GetComponent<BoxCollider>();if(!chassis)return;
            // Road-car bonnet/deck are lower than the cabin. A single roof-height
            // box over the entire car behaved like a wall in front of big tyres.
            var centre=chassis.center;centre.y=.67f;chassis.center=centre;
            var size=chassis.size;size.y=.52f;chassis.size=size;
            var cabin=new GameObject("Traffic cabin collision");cabin.layer=gameObject.layer;cabin.transform.SetParent(transform,false);
            var box=cabin.AddComponent<BoxCollider>();box.center=new Vector3(0,1.035f,-.25f);box.size=new Vector3(Metrics.BodyWidth*.75f,.38f,1.65f);
            VehiclePopulation.ActualVehiclePhysicalLayout.RecordSupplemental(transform,box);
        }
        internal void ActivateCollisionPhysics()
        {
            if(collisionDynamics)return;
            collisionDynamics=true;struck=false;dynamicsStarted=Time.time;
            body.isKinematic=false;body.mass=CollisionMassKg;body.centerOfMass=new Vector3(0,.38f,0);
            body.linearVelocity=transform.forward*Speed;body.angularDamping=.8f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        }
        bool TickCollisionDynamics(float dt)
        {
            if(Police||ManagedPolice)return false;
            var delta=body.position-Player.Body.position;
            float gap=delta.magnitude;
            if(!collisionDynamics)
            {
                float closing=Vector3.Dot(Player.Body.linearVelocity-transform.forward*Speed,delta.normalized);
                if(gap>8+Mathf.Max(0,closing)*.15f||closing<1||Mathf.Abs(delta.y)>3)return false;
                ActivateCollisionPhysics();
            }
            Speed=Vector3.Dot(body.linearVelocity,transform.forward);
            for(int index=0;index<4;index++)
            {
                var anchor=VehicleGeometry.Anchor(Visual.Model,index);var origin=transform.TransformPoint(anchor);
                float previous=crashCompression[index];
                if(Physics.Raycast(origin,-transform.up,out var hit,Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore))
                {
                    float compression=Mathf.Clamp(Metrics.SuspensionRayLength-hit.distance,0,Metrics.SuspensionTravel);
                    crashCompression[index]=compression;
                    float force=Mathf.Clamp(compression*Metrics.SpringRate+(compression-previous)/dt*Metrics.DamperRate,0,20000);
                    body.AddForceAtPosition(hit.normal*force,origin);
                    var velocity=body.GetPointVelocity(hit.point);
                    body.AddForceAtPosition(-Vector3.Project(velocity,transform.right)*body.mass*1.5f,hit.point);
                    if(struck)body.AddForceAtPosition(-Vector3.Project(velocity,transform.forward)*body.mass*.35f,hit.point);
                    if(Visual?.Wheels!=null){var p=Visual.Wheels[index].localPosition;p.y=Metrics.SuspensionAnchorY-hit.distance+Metrics.Radius;Visual.Wheels[index].localPosition=p;}
                }
                else crashCompression[index]=0;
            }
            // Restore cheap route following only after separation. A collision
            // cannot be overwritten by the next kinematic MovePosition call.
            if((struck&&gap>40)||(!struck&&Time.time-dynamicsStarted>1.2f&&gap>9))
            {
                VehiclePopulation.VehiclePopulationHooks.PoseWillReset(this);
                body.isKinematic=true;collisionDynamics=false;networkRoad=null;slowUntil=Time.time+1;
            }
            return true;
        }
    }
}
