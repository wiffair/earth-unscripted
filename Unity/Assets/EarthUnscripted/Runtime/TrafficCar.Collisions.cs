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
            body.linearVelocity=transform.forward*Speed;body.angularDamping=.8f;body.ResetInertiaTensor();body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        }
        bool TickCollisionDynamics(float dt)
        {
            if(Police||ManagedPolice)return false;
            var delta=body.position-Player.Body.position;
            float gap=delta.magnitude;
            if(!collisionDynamics)
            {
                float closing=Vector3.Dot(Player.Body.linearVelocity-transform.forward*Speed,delta.normalized);
                if(gap>12+Mathf.Max(0,closing)*.15f||Mathf.Abs(delta.y)>3)return false;
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
                    var groundVelocity=hit.rigidbody?hit.rigidbody.GetPointVelocity(hit.point):Vector3.zero;
                    float compressionSpeed=-Vector3.Dot(body.GetPointVelocity(origin)-groundVelocity,hit.normal);
                    float force=Mathf.Clamp(compression*Metrics.SpringRate+compressionSpeed*Metrics.DamperRate,0,20000);
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
            if(gap>40)
            {
                // Dynamics has advanced the body independently of the route cursor.
                // Reusing that old cursor made the next MovePosition sweep tens of
                // metres through the player at thousands of metres per second.
                float nearest=Route.Closest(body.position,out _,out _);
                var target=Rest(Route.At(nearest,Lane));
                var segment=target-body.position;float t=segment.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector3.Dot(Player.Body.position-body.position,segment)/segment.sqrMagnitude);
                if(Vector3.Distance(target,Player.Body.position)>40&&Vector3.Distance(body.position+segment*t,Player.Body.position)>25)
                {
                    VehiclePopulation.VehiclePopulationHooks.PoseWillReset(this);
                    body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
                    body.isKinematic=true;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                    Distance=nearest;Speed=0;body.position=target;body.rotation=Quaternion.LookRotation(Route.Forward(nearest)*Direction,Vector3.up);
                    collisionDynamics=false;networkRoad=null;slowUntil=Time.time+1;
                }
            }
            return true;
        }
    }
}
