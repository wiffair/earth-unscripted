using UnityEngine;
namespace EarthUnscripted
{
    public sealed class PoliceDriver : MonoBehaviour
    {
        public enum Duty { Parked,Pursuit,Disengaging }
        public GameSession Session;public TrafficCar Marker;public VehicleController Car {get;private set;}
        public Duty State {get;private set;}=Duty.Parked;
        public bool Pursuing=>State==Duty.Pursuit;
        public float CooldownUntil;
        bool streamingFrozen;float bornAt;
        float nudgeAt,nudgeUntil,departedAt,sirenGain,occlusionAt,occlusion=1;
        RoadNavigator navigator;System.Collections.Generic.List<Vector3> navigation;int waypoint;float replanAt;
        AudioSource siren;bool sirenStarted;static AudioClip sirenClip;Renderer beacon;MaterialPropertyBlock lightProperties;
        public void Configure(GameSession session,TrafficCar marker)
        {
            Session=session;Marker=marker;bornAt=Time.time;if(marker.Route.Region!=null)navigator=new RoadNavigator(marker.Route.Region);Marker.ManagedPolice=this;gameObject.layer=8;
            Car=gameObject.AddComponent<VehicleController>();Car.NetworkDriven=true;Car.Visual=Marker.Visual;Car.Body.isKinematic=false;
            // Patrols use exactly the garage's highest upgrade level for their
            // own model. ApplyTuning also supplies model mass and suspension.
            var patrolBuild = new Core.OwnedCar { model = Marker.Visual.Model };
            for (int part = 0; part < Core.GarageRules.Parts.Length; part++)
                patrolBuild.SetLevel(part, 3);
            Car.ApplyTuning(patrolBuild);
            Car.Body.ResetInertiaTensor();
            Car.NetworkHandbrake=1;Car.NetworkBrake=1;
            siren=gameObject.AddComponent<AudioSource>();siren.playOnAwake=false;siren.loop=true;siren.spatialBlend=1;siren.minDistance=10;siren.maxDistance=240;siren.rolloffMode=AudioRolloffMode.Logarithmic;siren.dopplerLevel=.35f;siren.velocityUpdateMode=AudioVelocityUpdateMode.Fixed;
            if(!sirenClip)
            {
                const int rate=24000,seconds=4;var data=new float[rate*seconds];double phase=0;
                for(int i=0;i<data.Length;i++){double t=(double)i/rate;phase+=(650+210*System.Math.Sin(t*System.Math.PI*2))/rate;data[i]=(float)(System.Math.Sin(phase*System.Math.PI*2)*.5+System.Math.Sin(phase*System.Math.PI*4)*.12);}
                sirenClip=AudioClip.Create("Positional patrol wail",data.Length,1,rate,false);sirenClip.SetData(data,0);
            }
            siren.clip=sirenClip;siren.volume=0;StartSirenWhenReady();lightProperties=new MaterialPropertyBlock();beacon=transform.Find("Police lightbar")?.GetComponent<Renderer>();
        }
        void StartSirenWhenReady(){if(sirenStarted||!Session||!Session.BootReady)return;siren.Play();sirenStarted=true;}
        public void BeginPursuit(){State=Duty.Pursuit;Car.NetworkHandbrake=0;Car.NetworkBrake=0;nudgeAt=Time.time+6;}
        public void Disengage(){if(State!=Duty.Pursuit)return;State=Duty.Disengaging;departedAt=Time.time;CooldownUntil=Time.time+90;nudgeUntil=0;}
        public bool CanObserveSpeeding()
        {
            if(State!=Duty.Parked||Time.time<CooldownUntil||Session.Network.Active||!Session.OnRoad)return false;
            var delta=Session.Car.Body.position-Car.Body.position;float distance=delta.magnitude;
            if(distance>70||distance<5||Session.Car.AbsoluteSpeed*Core.DrivingMath.MphPerMetreSecond<=Session.Limit+6)return false;
            // A real nearby observer with line of sight replaces the old global timer.
            var eye=Car.Body.position+Vector3.up*1.45f;var target=Session.Car.Body.position+Vector3.up*.8f;
            return !Physics.Linecast(eye,target,~(1<<8),QueryTriggerInteraction.Ignore);
        }
        void Update()
        {
            if(!Session||!Session.BootReady||!Car)return;StartSirenWhenReady();
            bool loaded=!Session.World.GeographicWorld||Session.World.GeographicWorld.HasSurface(Car.Body.position)||(State==Duty.Parked&&Time.time-bornAt<2);
            if(!loaded&&!streamingFrozen){Car.enabled=false;Car.Body.isKinematic=true;streamingFrozen=true;}
            else if(loaded&&streamingFrozen){Car.Body.isKinematic=false;Car.enabled=true;streamingFrozen=false;}
            var surfaceWeather=Session.Conditions.ApplySurface(Car);Car.Visual.WiperRain=Session.Conditions.RainAt(Car.Body.position);Car.Visual.SetAutomaticLights(Session.Conditions.Daylight<.55f||Session.Conditions.Rain>.2f);
            float progress=Marker.Route.Closest(Car.Body.position,out _,out float roadDistance);Marker.Distance=progress;Marker.Speed=Car.AbsoluteSpeed;
            bool road=roadDistance<Marker.Route.Width(progress)*.5f;
            if(Marker.Route.Region!=null)road=Marker.Route.Region.IsOnRoad(Car.Body.position);
            Car.Grip = (road ? 1 : Car.OffroadGripFactor)
                * WeatherSurfaceRules.Grip(surfaceWeather,Car.Metrics.Model==3) * Car.TyreFactor;
            if(Session.Network.Active){Car.NetworkThrottle=0;Car.NetworkBrake=Car.NetworkHandbrake=1;return;}
            if(Time.timeScale>0)
            {
                if(State==Duty.Parked){Car.NetworkThrottle=Car.NetworkSteer=0;Car.NetworkBrake=Car.NetworkHandbrake=1;if(CanObserveSpeeding())Session.ReportSpeeding(this);}
                else Drive();
            }
            float targetGain=Pursuing&&Time.timeScale>0?Session.Profile.volume*.55f:0;sirenGain=Mathf.MoveTowards(sirenGain,targetGain,Time.unscaledDeltaTime*.22f);
            if(Time.time>=occlusionAt){occlusionAt=Time.time+.2f;occlusion=Camera.main&&Physics.Linecast(Car.Body.position+Vector3.up,Camera.main.transform.position,~(1<<8),QueryTriggerInteraction.Ignore)?.45f:1;}
            siren.volume=sirenGain*occlusion;
            if(beacon){bool flash=Pursuing&&Mathf.Repeat(Time.time*4,1)<.55f;lightProperties.SetColor("_BaseColor",flash?new Color(.1f,.3f,1):new Color(.06f,.09f,.16f));lightProperties.SetColor("_EmissionColor",flash?Color.blue*5:Color.black);beacon.SetPropertyBlock(lightProperties);}
        }
        /// <summary>Chase target in metres/second, bounded by the actual patrol
        /// model's upgraded gearing. The physical motor/drag can limit it sooner.</summary>
        public static float DesiredPursuitSpeed(float playerSpeed,float separation,bool nudge,float vehicleLimit)
        {
            // Preserve a braking gap behind a stationary car. The moving nudge
            // needs enough closing speed and time to reach the rear quarter.
            if(playerSpeed<2)return Mathf.Min(8,Mathf.Sqrt(Mathf.Max(0,(separation-8)*10)));
            return nudge?Mathf.Min(vehicleLimit,playerSpeed+5):Mathf.Min(vehicleLimit,playerSpeed+Mathf.Sqrt(Mathf.Max(0,(separation-6)*9)));
        }
        void Drive()
        {
            var player=Session.Car;bool activeNudge=false;Vector3 target;float separation=Vector3.Distance(Car.Body.position,player.Body.position);float desired;
            if(State==Duty.Disengaging)
            {
                // Continue visibly at a reduced speed, then pull onto the verge.
                if(Marker.Route.Region!=null)
                {
                    var region=Marker.Route.Region;var road=region.Nearest(Car.Body.position,out var nearest,out _);
                    if(road!=null){float along=region.RoadProgress(road,nearest);region.RoadPoint(road,along,out var forward);float sign=Vector3.Dot(transform.forward,forward)>=0?1:-1;target=region.RoadPoint(road,along+sign*15,out forward)+Vector3.Cross(Vector3.up,forward).normalized*region.drivingSide*(region.EffectiveWidth(road,Mathf.Clamp(along+sign*15,0,road.length))*.5f+2.5f);}
                    else target=Car.Body.position+transform.forward*15;
                }
                else
                {
                float d=Marker.Route.Closest(Car.Body.position,out _,out _);float direction=Vector3.Dot(transform.forward,Marker.Route.Forward(d))>=0?1:-1;
                target=Marker.Route.At(d+direction*20,Marker.Route.DrivingSide*(Marker.Route.Width(d)*.5f+2.4f));
                }
                desired=Time.time-departedAt>8?3:9;
                if(Time.time-departedAt>15){State=Duty.Parked;Car.NetworkThrottle=0;Car.NetworkBrake=Car.NetworkHandbrake=1;return;}
            }
            else
            {
                bool aligned=Vector3.Dot(transform.forward,player.transform.forward)>.8f;
                if(Time.time>nudgeAt&&separation<18&&player.Speed>9&&aligned){nudgeUntil=Time.time+6f;nudgeAt=Time.time+12;Session.Notify("Patrol lining up a rear-quarter nudge");}
                if(player.AbsoluteSpeed<9)nudgeUntil=0;
                bool nudge=Time.time<nudgeUntil;activeNudge=nudge;
                target=nudge?player.Body.position+player.Body.linearVelocity*.35f-player.transform.forward*3.6f+player.transform.right*.8f:player.Body.position+player.Body.linearVelocity*.25f;
                if(player.AbsoluteSpeed<2)target=player.Body.position-player.transform.forward*7.5f;
                if(!nudge&&separation>25&&Session.OnRoad&&Marker.Route.Region==null)
                {
                    float self=Marker.Route.Closest(Car.Body.position,out _,out _),goal=Marker.Route.Closest(player.Body.position,out float lateral,out _);
                    float direction=Mathf.Sign(Core.DrivingMath.SignedRouteDelta(self,goal,Marker.Route.Length));
                    target=Marker.Route.At(self+direction*Mathf.Clamp(Car.AbsoluteSpeed*.8f+10,12,30),lateral);
                }
                if(!nudge&&separation>25&&Session.OnRoad&&navigator!=null)
                {
                    if(Time.time>replanAt){replanAt=Time.time+1.3f;navigation=navigator.Find(Car.Body.position,player.Body.position);waypoint=0;}
                    if(navigation!=null&&navigation.Count>0){while(waypoint<navigation.Count-1&&Vector3.Distance(Car.Body.position,navigation[waypoint])<Mathf.Max(7,Car.AbsoluteSpeed*.4f))waypoint++;target=navigation[waypoint];}
                }
                desired=DesiredPursuitSpeed(player.AbsoluteSpeed,separation,nudge,Car.SpeedLimit);
            }
            var toward=target-Car.Body.position;var local=transform.InverseTransformDirection(toward);
            bool reverse=local.z< -2&&toward.magnitude<25;
            float angle=Mathf.Atan2(local.x,reverse?-local.z:local.z)*Mathf.Rad2Deg;
            var heading=Quaternion.AngleAxis(Mathf.Clamp(angle,-85,85),Vector3.up)*transform.forward;
            float best=float.NegativeInfinity;float selected=angle;
            if(!reverse&&desired>1)
            {
                foreach(float offset in new[]{0f,-30f,30f,-60f,60f,-85f,85f})
                {
                    var forward=Quaternion.AngleAxis(offset,Vector3.up)*heading;float length=Mathf.Clamp(Car.AbsoluteSpeed*.9f+10,10,80);var point=Car.Body.position+forward*length;
                    bool blocked=Physics.SphereCast(Car.Body.position+Vector3.up*.85f,.75f,forward,out var hit,length,~(1<<8),QueryTriggerInteraction.Ignore)&&hit.normal.y<.55f;
                    float slope=Mathf.Abs(Marker.Route.TerrainHeight(point.x,point.z)-Car.Body.position.y)/length;
                    bool water=Marker.Route.Region!=null&&Marker.Route.Region.IsWater(point.x,point.z);
                    if(water){var bridge=Marker.Route.Region.Nearest(point,out _,out float bridgeDistance);if(bridge!=null&&bridge.bridge&&bridgeDistance<bridge.width*.5f+.5f)water=false;}
                    float score=Vector3.Dot(forward,toward.normalized)*3-(blocked?12:0)-(water?12:0)-Mathf.Max(0,slope-.4f)*20-Mathf.Abs(offset)*.002f;
                    if(score>best){best=score;selected=Vector3.SignedAngle(transform.forward,forward,Vector3.up);}
                }
                if(best< -4)desired=0;
                desired*=Mathf.Lerp(1,.3f,Mathf.Clamp01(Mathf.Abs(selected)/70));
                if(activeNudge&&best>=-4&&Mathf.Abs(selected)<20)desired=Mathf.Max(desired,Mathf.Min(Car.SpeedLimit,player.AbsoluteSpeed+4));
            }
            Car.NetworkSteer=Mathf.Clamp(selected/Mathf.Lerp(27,12,Mathf.Clamp01(Car.AbsoluteSpeed/35)),-1,1);
            if(reverse)
            {Car.NetworkSteer=Mathf.Clamp(angle/25,-1,1);Car.NetworkBrake=Car.Speed> -4?.55f:0;Car.NetworkThrottle=Car.Speed< -4.5f?.3f:0;Car.NetworkHandbrake=0;}
            else
            {
                // Full available acceleration when catching up; steering, obstacles
                // and the stationary stand-off still reduce the target above.
                Car.NetworkThrottle=Car.Speed<desired?1f:0;Car.NetworkBrake=Car.Speed>desired+.5f?Mathf.Clamp01((Car.Speed-desired)*.4f):0;
                Car.NetworkHandbrake=desired<.5f&&Car.AbsoluteSpeed<1?1:0;if(Car.NetworkHandbrake>0)Car.NetworkBrake=1;
            }
        }
    }
}
