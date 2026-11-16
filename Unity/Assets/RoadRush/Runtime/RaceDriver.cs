using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    public sealed class RaceDriver : MonoBehaviour
    {
        public VehicleController Car;public RoutePath Route;public GameSession Session;public List<VehicleController> Fleet;
        public event System.Action<float> Recovered;
        // Optional service/verification pace in metres/second. Zero retains the
        // existing race-speed policy; it does not change physical vehicle tuning.
        public float PaceLimitMetresPerSecond;
        public int Index,Difficulty=1;public float Lane;public bool Cooldown,RoadLane;float stuck;
        void FixedUpdate()
        {
            if(!Car||Car.StartLocked||Time.timeScale==0)return;
            float dt=Time.fixedDeltaTime;float d=Route.Closest(Car.Body.position,out float lateral,out float separation);float width=Route.Width(d);
            // Authored pavement, including pit aprons, determines surface grip.
            // Route width remains a navigation hint, not a replacement collider.
            var authored=Session.World.Route.Region?.CircuitRoadSurfaces;
            bool onRoad=authored!=null?authored.TryHeight(Car.Body.position.x,Car.Body.position.z,out _):separation<width*.5f+2;
            var surfaceWeather=Session.Conditions.ApplySurface(Car);Car.Grip=(onRoad?1:.65f)*WeatherSurfaceRules.Grip(surfaceWeather,Car.Metrics.Model==3)*Car.TyreFactor;Car.RoadSurface=onRoad;
            Car.Visual.WiperRain=Session.Conditions.RainAt(Car.Body.position);Car.Visual.SetAutomaticLights(Session.Conditions.Daylight<.55f||Session.Conditions.Rain>.2f);
            float speed=Car.AbsoluteSpeed,maxSpeed=Car.SpeedLimit*(Difficulty==0?.65f:Difficulty==2?.86f:.78f);float target=maxSpeed,curvature=0;
            foreach(float look in new[]{0f,12f,25f,45f,70f})
            {
                float k=Vector3.Angle(Route.Forward(d+look-6),Route.Forward(d+look+6))*Mathf.Deg2Rad/12;curvature=Mathf.Max(curvature,k);
                float corner=Mathf.Sqrt(9.81f*Mathf.Max(.3f,Car.Grip)/Mathf.Max(.001f,k))*(Difficulty==0?.72f:Difficulty==2?.93f:.82f);
                target=Mathf.Min(target,Mathf.Sqrt(corner*corner+2*7*Car.BrakeFactor*Mathf.Max(0,look-5)));
            }
            float raceFriction=0,raceAero=0,raceCurvature=0;
            if(Car.Metrics.Model==3)
            {
                raceFriction=Mathf.Max(.1f,Car.Grip*Car.RaceTyres.Grip(Car.SurfaceWetness)*(1-Car.Damage*.002f));
                raceAero=(1.8f+Car.RaceSetup.frontWing+Car.RaceSetup.rearWing)/Car.Body.mass;
                float deceleration=Mathf.Min(7*Car.BrakeFactor,raceFriction*9.81f*.7f);
                float preview=Core.RaceDrivingRules.PreviewMetres(speed,deceleration);target=Mathf.Min(target,Core.RaceDrivingRules.VisibleStoppingSpeed(deceleration));
                // Short source segments can have sharper curvature than a12m mean.
                // Bounded257samples cover actual stopping distance without changing route data.
                for(float look=0;look<=preview;look+=4)
                {
                    float candidateCurvature=Mathf.Max(Vector3.Angle(Route.PlanarForward(d+look-2),Route.PlanarForward(d+look+2))*Mathf.Deg2Rad/4,Vector3.Angle(Route.PlanarForward(d+look-6),Route.PlanarForward(d+look+6))*Mathf.Deg2Rad/12);
                    if(look<=12)raceCurvature=Mathf.Max(raceCurvature,candidateCurvature);
                    float corner=Core.RaceDrivingRules.CornerSpeed(candidateCurvature,raceFriction,raceAero,Difficulty==0?.72f:Difficulty==2?.93f:.82f);
                    target=Mathf.Min(target,Mathf.Sqrt(corner*corner+2*deceleration*Mathf.Max(0,look-5)));
                }
            }
            if(RoadLane)target=Mathf.Min(target,Route.Limit(d)/Core.DrivingMath.MphPerMetreSecond*.88f);
            float desiredLane=RoadLane?Route.LaneOffset(d):0,gap=200;VehicleController leader=null;
            if(Fleet!=null)foreach(var other in Fleet)
            {
                if(!other||other==Car)continue;var delta=other.Body.position-Car.Body.position;float ahead=Vector3.Dot(delta,transform.forward);float side=Vector3.Dot(delta,transform.right);
                if(ahead>0&&ahead<gap&&Mathf.Abs(side)<2.5f){gap=ahead;leader=other;}
                float otherD=Route.Closest(other.Body.position,out float otherLane,out _);float along=Mathf.Abs(Core.DrivingMath.SignedRouteDelta(d,otherD,Route.Length));
                var driver=other.GetComponent<RaceDriver>();if(driver&&driver.Index<Index&&along<15&&Mathf.Abs(otherLane)<4)desiredLane=Lane;
            }
            if(leader&&gap<26)
            {
                bool passing=curvature<.012f;
                if(passing)
                {
                    float choice=lateral<=0?2.7f:-2.7f;bool clear=true;
                    foreach(var other in Fleet)if(other&&other!=Car){float od=Route.Closest(other.Body.position,out float ol,out _);if(Mathf.Abs(Core.DrivingMath.SignedRouteDelta(d,od,Route.Length))<14&&Mathf.Abs(ol-choice)<2.3f)clear=false;}
                    if(clear)desiredLane=choice;else passing=false;
                }
                if(!passing||Mathf.Abs(Lane-lateral)<.5f&&Mathf.Abs(desiredLane-lateral)<1)target=Mathf.Min(target,Mathf.Max(0,leader.AbsoluteSpeed+(gap-7)*.7f));
            }
            if(PaceLimitMetresPerSecond>0)target=Mathf.Min(target,PaceLimitMetresPerSecond);
            if(Cooldown){desiredLane=Route.DrivingSide*(width*.5f+2);target=6;}
            Lane=Mathf.MoveTowards(Lane,Mathf.Clamp(desiredLane,-width*.5f-2,width*.5f+2),dt*1.4f);
            float lookahead=Mathf.Clamp(8+speed*.55f,8,27);var point=Route.At(d+lookahead,Lane);var local=transform.InverseTransformPoint(point);
            float steering=Mathf.Atan2(2*2.72f*Car.WheelbaseScale*local.x,Mathf.Max(1,local.x*local.x+local.z*local.z))*Mathf.Rad2Deg;
            float limit=Mathf.Lerp(30,10,Mathf.Clamp01(Mathf.Abs(Car.Speed)/48));Car.NetworkSteer=Mathf.Clamp(steering/limit,-1,1);
            Car.NetworkThrottle=Mathf.Clamp01((target-Car.Speed)*.18f)*(Mathf.Abs(Car.NetworkSteer)>.15f?.48f:.85f);
            if(Car.Metrics.Model==3)Car.NetworkThrottle=Mathf.Min(Car.NetworkThrottle,Core.RaceDrivingRules.RearThrottleBudget(speed,raceCurvature,raceFriction,raceAero,Car.Body.mass,Car.PowerFactor*Car.RaceSetup.finalDrive,Mathf.Abs(Car.Slip),Mathf.Max(0,Car.RearPowerSlip)));
            Car.NetworkBrake=Mathf.Clamp01((Car.Speed-target)*.35f);Car.NetworkHandbrake=target<.5f&&speed<1?1:0;
            if(speed<2.5f&&target>4)stuck+=dt;else stuck=Mathf.Max(0,stuck-dt*2);
            if(stuck>2&&stuck<4){Car.NetworkThrottle=0;Car.NetworkBrake=.6f;Car.NetworkHandbrake=0;Car.NetworkSteer=-Mathf.Sign(lateral)*.65f;}
            if(stuck>6||Vector3.Dot(transform.up,Vector3.up)<.2f)
            {
                // Retreat along the course; recovery cannot advance a race checkpoint.
                Car.Recover(Route.At(d-8,Lane),Route.Rotation(d-8));Car.NetworkThrottle=0;stuck=0;Recovered?.Invoke(Route.Closest(Car.Body.position,out _,out _));
            }
        }
    }
}
