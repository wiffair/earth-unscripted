using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class PoliceDriver
    {
        float parkedUntil;readonly RaycastHit[] patrolObstacleHits=new RaycastHit[32];
        public bool IdlePatrol=>State==Duty.Parked||State==Duty.Cruising;
        public bool CanWitnessOffence(float maximumDistance)
        {
            if(!IdlePatrol||!Session||!Session.Car||!Car||!Session.Network||Time.time<CooldownUntil||Session.Network.Active)return false;
            float distance=Vector3.Distance(Session.Car.Body.position,Car.Body.position);if(distance<5||distance>maximumDistance)return false;
            return !Physics.Linecast(Car.Body.position+Vector3.up*1.45f,Session.Car.Body.position+Vector3.up*.8f,~(1<<8),QueryTriggerInteraction.Ignore);
        }
        internal bool TryPlaceIdle(Vector3 point,Vector3 forward,bool parked,float dwellSeconds)
        {
            if(!IdlePatrol)return false;
            // The caller proves offscreen placement and a physical safe site.
            // This method still requires real wheel support and records a pose reset.
            if(!Car.TryInitializeSupportedRest(point,Quaternion.LookRotation(forward),out _,out _))return false;
            if(streamingFrozen){Car.Body.isKinematic=false;Car.enabled=true;streamingFrozen=false;}
            Marker.ResetPhysicalPatrolPlan();Marker.Speed=0;State=parked?Duty.Parked:Duty.Cruising;parkedUntil=Time.time+dwellSeconds;CooldownUntil=Mathf.Max(CooldownUntil,Time.time+8);
            Car.NetworkThrottle=Car.NetworkSteer=0;Car.NetworkBrake=1;Car.NetworkHandbrake=parked?1:0;return true;
        }
        internal float PatrolObstacleSpeed(float desired,Vector3 target)
        {
            var toward=target-Car.Body.position;float length=toward.magnitude;if(length<=.1f)return desired;
            int count=Physics.SphereCastNonAlloc(Car.Body.position+Vector3.up*.85f,.75f,toward/length,patrolObstacleHits,Mathf.Min(length,Mathf.Max(8,Car.AbsoluteSpeed*1.5f)),~(1<<8),QueryTriggerInteraction.Ignore);
            if(count==patrolObstacleHits.Length)return 0;
            for(int i=0;i<count;i++){var hit=patrolObstacleHits[i];if(!hit.collider||hit.rigidbody==Car.Body||hit.transform.IsChildOf(transform))continue;if(hit.normal.y<.55f)desired=Mathf.Min(desired,Mathf.Sqrt(Mathf.Max(0,hit.distance-3)*5));}
            return desired;
        }
        void FixedUpdate()
        {
            if(!Session||!Session.BootReady||!Car||streamingFrozen||Time.timeScale<=0||Session.Network.Active)return;
            if(State==Duty.Parked&&(Marker.Route.Region==null||Session.World.ScopedSession800!=null))return; // Preserve the frozen reviewed source scene.
            if(State==Duty.Parked&&Time.time>=parkedUntil&&Session.CanMergePatrol(this)){State=Duty.Cruising;Marker.ResetPhysicalPatrolPlan();}
            if(State!=Duty.Cruising)return;
            if(!Marker.PlanPhysicalPatrol(Car,Time.fixedDeltaTime,out var target,out float desired)){Car.NetworkThrottle=0;Car.NetworkBrake=Car.NetworkHandbrake=1;return;}
            var local=transform.InverseTransformPoint(target);float square=Mathf.Max(1,local.x*local.x+local.z*local.z);
            if(local.z<=.5f){Car.NetworkThrottle=0;Car.NetworkBrake=Car.NetworkHandbrake=1;return;} // Do not accelerate against the legal lane after a spin.
            float curvature=2*local.x/square;desired=Mathf.Min(desired,Mathf.Sqrt(2.4f/Mathf.Max(.002f,Mathf.Abs(curvature))));
            desired=PatrolObstacleSpeed(desired,target);
            float angle=Mathf.Atan2(2*Car.Metrics.Wheelbase*local.x,square)*Mathf.Rad2Deg;
            Car.NetworkSteer=Mathf.Clamp(angle/Mathf.Lerp(27,12,Mathf.Clamp01(Car.AbsoluteSpeed/35)),-1,1);
            Car.NetworkThrottle=Car.AbsoluteSpeed<desired?Mathf.Clamp01((desired-Car.AbsoluteSpeed)*.2f):0;
            Car.NetworkBrake=Car.AbsoluteSpeed>desired+.2f?Mathf.Clamp01((Car.AbsoluteSpeed-desired)*.4f):0;
            Car.NetworkHandbrake=desired<.2f&&Car.AbsoluteSpeed<.3f?1:0;if(Car.NetworkHandbrake>0)Car.NetworkBrake=1;
        }
    }
}
