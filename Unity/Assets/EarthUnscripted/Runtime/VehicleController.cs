using System;
using UnityEngine;

namespace EarthUnscripted
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed partial class VehicleController : MonoBehaviour
    {
        public DriverInput Input;
        public bool NetworkDriven; public float NetworkSteer,NetworkThrottle,NetworkBrake,NetworkHandbrake;
        public Vector3 ReplicaVelocity;
        public bool Replica,StartLocked;
        // Owned only by automatic world coverage; independent of race/menu locks.
        public bool StreamingBrake;public float StreamingSpeedLimit=float.PositiveInfinity;public int ReplicaRevision=-1;
        public CarVisual Visual;
        public Rigidbody Body { get; private set; }
        public float Damage { get; private set; }
        public float Speed=>Vector3.Dot(Replica?ReplicaVelocity:Body.linearVelocity,transform.forward);
        public float AbsoluteSpeed=>(Replica?ReplicaVelocity:Body.linearVelocity).magnitude;
        public float Grip=1;
        int model;
        Core.ImportedVehicleSettings importedSettings;
        public float OffroadGripFactor => importedSettings?.OffroadGrip ?? .53f;
        public Core.VehicleMetric Metrics=>Core.VehicleDimensions.ForModel(model);public float WheelbaseScale=>Metrics.Wheelbase/2.72f;
        public Vector3 DiagnosticQueuedForce,DiagnosticQueuedTorque,DiagnosticBeforeVelocity;
        public float PowerFactor=1,SpeedLimit=65,TyreFactor=1,BrakeFactor=1,SpringFactor=1,DamperFactor=1,DifferentialFactor=1;
        public Core.RaceSetup RaceSetup {get;private set;}=new Core.RaceSetup();public Core.RaceTyreState RaceTyres {get;private set;}=new Core.RaceTyreState();
        public bool RoadSurface=true;public float SurfaceWetness,SurfaceSnow,SurfaceIce,SurfaceFrost;
        public readonly bool[] WheelGrounded=new bool[4];
        public readonly string[] ContactSurfaceNames=new string[4];
        public readonly Vector3[] ContactPoints=new Vector3[4],ContactNormals=new Vector3[4];
        // Mean current tyre support identifies the physical road level. Replica
        // contacts are stale; airborne/replica fallback uses loaded suspension.
        public float RoadSupportHeight
        {
            get
            {
                float total=0;int count=0;
                for(int wheel=0;!Replica&&wheel<WheelGrounded.Length;wheel++)
                {
                    if(!WheelGrounded[wheel])continue;
                    float height=ContactPoints[wheel].y;
                    if(float.IsNaN(height)||float.IsInfinity(height))continue;
                    total+=height;count++;
                }
                return count>0?total/count:Body.position.y-Metrics.RestRootHeight(Body.mass,SpringFactor);
            }
        }
        public readonly float[] WheelSlip=new float[4];
        public float RearPowerSlip=>(WheelSlip[0]+WheelSlip[2])*.5f;
        public float Slip { get; private set; }
        public float SuspensionImpact { get; private set; }
        // Peak-hold avoids losing a short physics bump between display frames (m/s).
        public float HapticBumpSpeed { get; private set; }
        public int HapticImpactRevision { get; private set; }
        public float HapticImpactSpeed { get; private set; }
        float lastHapticImpact = -10;
        void RecordHapticImpact(float normalSpeed)
        {
            if (normalSpeed < .6f || Time.time - lastHapticImpact < .10f) return;
            lastHapticImpact = Time.time; HapticImpactSpeed = normalSpeed;
            unchecked { HapticImpactRevision++; }
        }
        public float Steering { get; private set; }
        public bool Grounded { get; private set; }
        public bool ReplicaReversing;public bool Braking {get;private set;}
        public bool Reverse=>Replica?ReplicaReversing:pedals.Reverse;
        public event Action<float,Vector3> Crashed;
        public bool LastImpactWasPolice {get;private set;}
        // Actual collider label for crash/recovery diagnostics; no effect on response.
        public string LastImpactObject {get;private set;}

        // Player/authoritative LAN guest only. Ordinary AI defaults off.
        public bool ParkingAutomaticEnabled,NetworkParkingBrakeRequest;
        public int NetworkParkingBrakeCommand;
        public bool ParkingBrakeApplied {get;private set;}
        public bool ParkingBrakeHolding {get;private set;}
        public int ParkingBrakeCommandAck {get;private set;}
        float parkingStillTime;bool parkingAutoSuppressed;
        bool ParkingSupport()
        {
            if(Body.linearVelocity.magnitude>.35f||Body.angularVelocity.magnitude>.3f)return false;
            for(int i=0;i<4;i++)if(!Physics.Raycast(transform.TransformPoint(VehicleGeometry.Anchor(model,i)),-transform.up,out var hit,Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore)||hit.rigidbody||Vector3.Dot(hit.normal,Vector3.up)<.8660254f)return false;
            return true;
        }
        void UpdateParking(float throttle,float brake,float dt,bool supported)
        {
            ParkingBrakeHolding=false;if(!ParkingAutomaticEnabled){ParkingBrakeApplied=false;return;}
            int command=NetworkDriven?NetworkParkingBrakeCommand:Input?Input.ParkingBrakeCommand:0;
            bool requested=NetworkDriven?NetworkParkingBrakeRequest:Input&&Input.ParkingBrakeRequest;
            if(command>ParkingBrakeCommandAck){ParkingBrakeCommandAck=command;ParkingBrakeApplied=requested;parkingAutoSuppressed=!requested;parkingStillTime=0;}
            // Parking is deliberately NOT an AutomaticPedals handbrake input.
            // Existing forward/reverse intent must be able to release the latch.
            if(Mathf.Abs(pedals.Power)>.05f){ParkingBrakeApplied=false;parkingAutoSuppressed=false;parkingStillTime=0;return;}
            if(Body.linearVelocity.magnitude>.5f)parkingAutoSuppressed=false;
            bool stationary=supported&&Body.linearVelocity.magnitude<.35f&&Body.angularVelocity.magnitude<.15f&&throttle<.05f;
            parkingStillTime=stationary?parkingStillTime+dt:0;
            if(!parkingAutoSuppressed&&parkingStillTime>=.1f)ParkingBrakeApplied=true;
        }
        static Vector3 ParkingRequiredForce(Vector3 velocity,Vector3 normal,float normalForce)
        {
            // All-wheel stationary service hold: each contact compensates only
            // its ACTUAL normal-load share, not an equal/whole rear mass share.
            // With an unclamped static contact, normal + tangential reaction is
            // vertical, avoiding a yaw couple on crossfall or unequal CG loads.
            float normalGravity=-Vector3.Dot(Physics.gravity,normal);
            if(normalGravity<.1f||normalForce<=0)return Vector3.zero;
            return -(Vector3.ProjectOnPlane(Physics.gravity,normal)+Vector3.ProjectOnPlane(velocity,normal)*12)*(normalForce/normalGravity);
        }
        public void InitializeNetworkParking(bool applied,int command,bool pendingRequest=false)
        {if(Replica||command<0)return;ParkingBrakeApplied=applied;ParkingBrakeCommandAck=NetworkParkingBrakeCommand=command;NetworkParkingBrakeRequest=applied;parkingStillTime=0;parkingAutoSuppressed=pendingRequest&&!applied;}
        public void ReplicaParking(bool applied,bool holding,int acknowledgement)
        {if(!Replica)return;ParkingBrakeApplied=applied;ParkingBrakeHolding=holding;ParkingBrakeCommandAck=acknowledgement;}

        readonly float[] compression=new float[4],spin=new float[4];
        readonly Core.AutomaticPedals pedals=new Core.AutomaticPedals();
        float incidentTime=-10;
        void Awake()
        {
            Body=GetComponent<Rigidbody>();Body.mass=1350;Body.centerOfMass=new Vector3(0,.48f,.02f);Body.interpolation=RigidbodyInterpolation.Interpolate;Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            Body.linearDamping=.025f;Body.angularDamping=.65f;
            VehiclePopulation.VehiclePopulationHooks.ObserveAwake(this);var initialBox=GetComponent<BoxCollider>();if(initialBox){VehicleGeometry.ApplyCollider(initialBox,0);Body.ResetInertiaTensor();}
        }
        void FixedUpdate()
        {
            using var timing=WorkTelemetry.Measure("frame.vehicle-fixed",null,8);
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            if(Replica||(!Input&&!NetworkDriven)||Time.timeScale==0)return;
            float throttle=NetworkDriven?NetworkThrottle:Input.Throttle,brakeInput=NetworkDriven?NetworkBrake:Input.Brake,steerInput=NetworkDriven?NetworkSteer:Input.Steer,handbrakeInput=NetworkDriven?NetworkHandbrake:Input.Handbrake;
            if(StartLocked){throttle=steerInput=0;brakeInput=handbrakeInput=1;}
            else if(StreamingBrake)
            {
                // Coverage is a speed envelope, not a full-brake toggle. Keep
                // steering and the driver's stronger brake request intact.
                float requested=float.IsPositiveInfinity(StreamingSpeedLimit)?1:Mathf.Clamp01((AbsoluteSpeed-StreamingSpeedLimit)*.16f);
                throttle=0;brakeInput=Mathf.Max(brakeInput,requested);
                if(StreamingSpeedLimit<.1f&&AbsoluteSpeed<.1f)handbrakeInput=1;
            }
            else if(!float.IsPositiveInfinity(StreamingSpeedLimit))
                throttle*=Mathf.Clamp01((StreamingSpeedLimit-AbsoluteSpeed)/3f);
            float dt=Time.fixedDeltaTime;float speed=Speed;if(model==3){RaceTyres.Tick(Mathf.Min(dt,1),AbsoluteSpeed,Mathf.Abs(Slip)*.1f+RearPowerSlip,SurfaceWetness,20);Visual?.SetRaceCompound(RaceTyres.compound);}
            pedals.Tick(throttle,brakeInput,speed,dt,handbrakeInput>.1f);
            bool parkingSupported=ParkingAutomaticEnabled&&ParkingSupport();UpdateParking(throttle,brakeInput,dt,parkingSupported);bool allParkingForces=ParkingBrakeApplied&&parkingSupported;
            float target=steerInput*Mathf.Lerp(30,10,Mathf.Clamp01(Mathf.Abs(speed)/48));
            Steering=Mathf.MoveTowards(Steering,target,(!NetworkDriven&&Input.Profile.wheelEnabled?240:90)*dt);
            BeginContactStep();
            int contacts=0;Slip=0;SuspensionImpact=0;HapticBumpSpeed=Mathf.MoveTowards(HapticBumpSpeed,0,dt*18);
            for(int i=0;i<4;i++)
            {
                var anchor=VehicleGeometry.Anchor(model,i);
                var origin=transform.TransformPoint(anchor);float wheelY=Metrics.FullyExtendedWheelLocalY;
                bool front=i%2==1;WheelGrounded[i]=false;
                var turn=Quaternion.AngleAxis(front?Steering:0,transform.up);var forward=turn*transform.forward;var right=turn*transform.right;
                if(TryWheelContact(origin,i,out var hit,out float contactDistance))
                {
                    WheelGrounded[i]=true;ContactSurfaceNames[i]=hit.collider.name;ContactPoints[i]=hit.point;ContactNormals[i]=hit.normal;
                    contacts++;float travel=Mathf.Clamp(Metrics.SuspensionRayLength-contactDistance,0,Metrics.SuspensionTravel);compression[i]=travel;
                    var groundVelocity=hit.rigidbody?hit.rigidbody.GetPointVelocity(hit.point):Vector3.zero;
                    var suspensionAxis=SuspensionForceAxis(hit.normal);
                    float compressionSpeed=-Vector3.Dot(Body.GetPointVelocity(origin)-groundVelocity,suspensionAxis);
                    float normalForce=Mathf.Clamp(travel*Metrics.SpringRate*SpringFactor+compressionSpeed*Metrics.DamperRate*DamperFactor,0,20000);
                    Body.AddForceAtPosition(suspensionAxis*normalForce,ParkingBrakeApplied&&parkingSupported?hit.point:origin);
                    var velocity=Body.GetPointVelocity(hit.point);float lateral=Vector3.Dot(velocity,right),longitudinal=Vector3.Dot(velocity,forward);
                    float handbrake=front?0:handbrakeInput;float friction=Grip*(model==3?RaceTyres.Grip(SurfaceWetness):1)*(1-Damage*.002f)*(handbrake>0?.5f:1);
                    float tyreLimit=normalForce*1.05f*friction;
                    float lateralForce=-lateral*Body.mass/4*7;
                    // Built-ins retain their existing rear-wheel force. Imported AWD
                    // splits the same total axle force between front and rear.
                    float axleShare = importedSettings == null ? (front ? 0 : 1) : front ? importedSettings.FrontDriveFraction : 1 - importedSettings.FrontDriveFraction;
                    float drive=axleShare*pedals.Power*4300*PowerFactor*(model==3?RaceSetup.finalDrive:1)*(1-Damage*.005f)*Mathf.Clamp01(1-Mathf.Abs(speed)/(Reverse?9:SpeedLimit/(model==3?RaceSetup.finalDrive:1)));
                    float demand=axleShare<=0?0:Core.TyreRules.PowerSlip(drive,normalForce,friction)*DifferentialFactor;
                    WheelSlip[i]=Mathf.MoveTowards(WheelSlip[i],demand,dt*(demand>WheelSlip[i]?4:2));
                    // Powered rear tyres lose lateral reserve when spinning. Lifting
                    // the throttle restores it gradually, allowing countersteer recovery.
                    float lateralLimit=tyreLimit*(axleShare<=0?1:Mathf.Lerp(1,.4f,Mathf.Clamp01(WheelSlip[i])));
                    lateralForce=Mathf.Clamp(lateralForce,-lateralLimit,lateralLimit);
                    float brake=(pedals.Brake*8500*BrakeFactor*(model==3?(front?RaceSetup.brakeBias:1-RaceSetup.brakeBias)*2:1)+Mathf.Max(handbrake,!front&&ParkingBrakeApplied?1:0)*13000);
                    float brakeForce=-Mathf.Sign(longitudinal)*Mathf.Min(brake,Mathf.Abs(longitudinal)*Body.mass/4/dt);
                    var tyreForce=Vector2.ClampMagnitude(new Vector2(lateralForce,drive+brakeForce),tyreLimit);
                    if(ParkingBrakeApplied&&parkingSupported)
                    {var needed=ParkingRequiredForce(velocity,hit.normal,normalForce);Body.AddForceAtPosition(Vector3.ClampMagnitude(needed,Mathf.Max(0,tyreLimit)),hit.point);allParkingForces&=needed.magnitude<=tyreLimit+.01f;}
                    else Body.AddForceAtPosition(right*tyreForce.x+forward*tyreForce.y,hit.point);
                    Slip+=Mathf.Abs(lateral)*.25f;SuspensionImpact=Mathf.Max(SuspensionImpact,Mathf.Abs(compressionSpeed));HapticBumpSpeed=Mathf.Max(HapticBumpSpeed,Mathf.Abs(compressionSpeed));
                    wheelY=Metrics.SuspensionAnchorY-contactDistance+Metrics.Radius;
                }
                else {compression[i]=0;WheelSlip[i]=Mathf.MoveTowards(WheelSlip[i],0,dt);}
                if(Visual&&Visual.Wheels!=null)
                {
                    spin[i]+=(speed+(front?0:Mathf.Sign(pedals.Power)*WheelSlip[i]*12))*dt/Metrics.Radius*Mathf.Rad2Deg;var w=Visual.Wheels[i];w.localPosition=new Vector3(anchor.x,wheelY,anchor.z);w.localRotation=Quaternion.Euler(0,front?Steering:0,0)*Quaternion.Euler(spin[i],0,0);Visual.FollowWheel(i,front?Steering:0);VehicleGeometry.FollowRaceWheelCollider(transform,model,i,wheelY);obstacleWheels?.Follow(i,wheelY,Steering);
                }
            }
            if(importedSettings!=null)obstacleWheels?.ApplyObstacleTraction(this,pedals.Power,importedSettings.FrontDriveFraction);
            ParkingBrakeHolding=allParkingForces&&contacts==4&&Body.linearVelocity.magnitude<.08f;
            Grounded=contacts>=2;FinishContactStep(contacts,dt);
            if(WaterDepth>0)Body.AddForce(-Body.linearVelocity*Body.mass*Mathf.Min(3,WaterDepth*1.6f));
            var v=Body.linearVelocity;Body.AddForce(-v*v.magnitude*(model==3?.43f+.22f*(RaceSetup.frontWing+RaceSetup.rearWing):.43f));
            if(Grounded){if(model==3){Body.AddForceAtPosition(-transform.up*speed*speed*(.8f+RaceSetup.frontWing),transform.TransformPoint(new Vector3(0,.25f,1.75f)));Body.AddForceAtPosition(-transform.up*speed*speed*(1+RaceSetup.rearWing),transform.TransformPoint(new Vector3(0,.25f,-1.75f)));}else if(importedSettings==null)Body.AddForce(-transform.up*speed*speed*1.1f);}
            if(SinglePlayerBeta.VerificationOutput!=null){DiagnosticQueuedForce=Body.GetAccumulatedForce();DiagnosticQueuedTorque=Body.GetAccumulatedTorque();DiagnosticBeforeVelocity=Body.linearVelocity;}
            Braking=pedals.Brake>.1f||handbrakeInput>.1f||ParkingBrakeApplied;
            if(Visual){Visual.Brake(Braking);Visual.SetSteering(steerInput);}
            if(transform.position.y<-30)Crashed?.Invoke(1,transform.position);
        }
        // Supported static placement for boot or an explicit stationary garage boundary.
        // Ordinary Recover/reset intentionally retains its drop.
        // Trial raycasts are read-only; no pose/suspension state changes on failure.
        public bool TryInitializeSupportedRest(Vector3 supportHint,Quaternion heading,out string reason,out bool supportMissing)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            reason="";supportMissing=false;
            bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
            bool FiniteVector(Vector3 p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z);
            if(!Body||Replica||!FiniteVector(supportHint)||!Finite(heading.x)||!Finite(heading.y)||!Finite(heading.z)||!Finite(heading.w)||heading.x*heading.x+heading.y*heading.y+heading.z*heading.z+heading.w*heading.w<.5f){reason="Invalid initial vehicle pose";return false;}
            var m=Metrics;float flatTravel=Metrics.SuspensionRayLength-Metrics.SuspensionAnchorY-m.RestRootHeight(Body.mass,SpringFactor);
            var flatForward=Vector3.ProjectOnPlane(heading*Vector3.forward,Vector3.up);if(flatForward.sqrMagnitude<.01f){reason="Invalid initial heading";return false;}flatForward.Normalize();
            var flatRotation=Quaternion.LookRotation(flatForward,Vector3.up);var hit=new RaycastHit[4];var anchors=new Vector3[4];for(int i=0;i<4;i++)anchors[i]=VehicleGeometry.Anchor(model,i);
            Physics.SyncTransforms();
            for(int i=0;i<4;i++)
            {
                var offset=flatRotation*new Vector3(anchors[i].x,0,anchors[i].z);var from=supportHint+offset+Vector3.up*1.0f;
                if(!Physics.Raycast(from,Vector3.down,out hit[i],2.25f,~(1<<8),QueryTriggerInteraction.Ignore)||Mathf.Abs(hit[i].point.y-supportHint.y)>1.25f){supportMissing=true;reason="Starting wheel footprint has no nearby support";return false;}
            }
            bool Fit(out Vector3 centre,out Vector3 normal,out Quaternion rotation)
            {
                centre=Vector3.zero;foreach(var h in hit)centre+=h.point;centre*=.25f;
                var right=(hit[2].point+hit[3].point-hit[0].point-hit[1].point)*.5f;var forward=(hit[1].point+hit[3].point-hit[0].point-hit[2].point)*.5f;normal=Vector3.Cross(forward,right).normalized;rotation=Quaternion.identity;
                if(!FiniteVector(centre)||normal.sqrMagnitude<.9f||Vector3.Dot(normal,Vector3.up)<.8660254f)return false;
                foreach(var h in hit)if(!FiniteVector(h.point)||Vector3.Dot(h.normal,normal)<.96f||Mathf.Abs(Vector3.Dot(h.point-centre,normal))>.12f)return false;
                // Preserve the original horizontal heading while adding support grade.
                forward=new Vector3(flatForward.x,-(normal.x*flatForward.x+normal.z*flatForward.z)/normal.y,flatForward.z);if(forward.sqrMagnitude<.1f)return false;rotation=Quaternion.LookRotation(forward.normalized,normal);return true;
            }
            Vector3 RestPosition(Vector3 planePoint,Vector3 normal)
            {
                // Pin the chosen lane/root XZ; solve only its elevation. Relative
                // coordinates avoid large world-coordinate cancellation.
                float height=Metrics.SuspensionRayLength-Metrics.SuspensionAnchorY-flatTravel*Vector3.Dot(normal,Vector3.up);
                float y=planePoint.y-(normal.x*(supportHint.x-planePoint.x)+normal.z*(supportHint.z-planePoint.z))/normal.y+height/normal.y;
                return new Vector3(supportHint.x,y,supportHint.z);
            }
            if(!Fit(out var centre,out var up,out var rotation)){reason="Starting support is too steep or uneven";return false;}
            var position=RestPosition(centre,up);
            // Refit on actual suspension-axis hits. No engine step or world mutation.
            for(int iteration=0;iteration<3;iteration++)
            {
                for(int i=0;i<4;i++)if(!Physics.Raycast(position+rotation*anchors[i],-(rotation*Vector3.up),out hit[i],Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore)){reason="Starting suspension ray has no support";return false;}
                if(!Fit(out centre,out up,out rotation)){reason="Starting suspension support is too steep or uneven";return false;}
                position=RestPosition(centre,up);
            }
            if(!FiniteVector(position)||Vector3.ProjectOnPlane(position-supportHint,Vector3.up).magnitude>.5f||Mathf.Abs(position.y-supportHint.y)>1.5f){reason="Starting rest correction exceeds its safe bounds";return false;}
            var travel=new float[4];var axis=rotation*Vector3.up;float mean=0;
            for(int i=0;i<4;i++)
            {
                if(!Physics.Raycast(position+rotation*anchors[i],-axis,out hit[i],Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore)||Vector3.Dot(hit[i].normal,axis)<.96f){reason="Final starting wheel support is missing";return false;}
                travel[i]=Metrics.SuspensionRayLength-hit[i].distance;
                if(travel[i]<.005f||travel[i]>Metrics.SuspensionTravel-.005f){reason="Starting suspension is outside usable travel";return false;}mean+=travel[i]*.25f;
            }
            if(Mathf.Abs(mean-flatTravel*Vector3.Dot(axis,Vector3.up))>.003f){reason="Starting rest pose did not converge";return false;}
            // Commit once, before the existing boot actor freeze and any yield.
            // Successful supported-rest fits (including garage fits) are real
            // pose discontinuities; failed fits above have not written body pose.
            VehiclePopulation.VehiclePopulationHooks.PoseWillReset(this);
            Body.position=position;Body.rotation=rotation;transform.SetPositionAndRotation(position,rotation);Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;
            for(int i=0;i<4;i++)
            {
                compression[i]=travel[i];WheelGrounded[i]=true;ContactPoints[i]=hit[i].point;ContactNormals[i]=hit[i].normal;WheelSlip[i]=0;
                if(Visual&&Visual.Wheels!=null&&Visual.Wheels.Length==4){Visual.Wheels[i].localPosition=new Vector3(anchors[i].x,Metrics.SuspensionAnchorY-hit[i].distance+m.Radius,anchors[i].z);Visual.FollowWheel(i,0);VehicleGeometry.FollowRaceWheelCollider(transform,model,i,Visual.Wheels[i].localPosition.y);}
            }
            Grounded=true;Slip=SuspensionImpact=HapticBumpSpeed=0;HapticImpactSpeed=0;if(ParkingAutomaticEnabled)ParkingBrakeApplied=true;Physics.SyncTransforms();return true;
        }

        // Call only after a successful stationary garage transaction has rebuilt
        // visuals, applied tuning and zeroed body velocities. No road-height edits,
        // horizontal teleport or changes to ordinary driving/Recover are involved.
        public bool TryInitializeGarageRest(out string reason)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            reason="";if(!Body||Replica||Body.isKinematic||AbsoluteSpeed>1){reason="Garage rest fitting requires a stationary authoritative car";return false;}
            var position=Body.position;var rotation=Body.rotation;
            Array.Clear(compression,0,compression.Length);Array.Clear(spin,0,spin.Length);Array.Clear(WheelSlip,0,WheelSlip.Length);Array.Clear(WheelGrounded,0,WheelGrounded.Length);Array.Clear(ContactPoints,0,ContactPoints.Length);Array.Clear(ContactNormals,0,ContactNormals.Length);
            pedals.Reset();Steering=Slip=SuspensionImpact=HapticBumpSpeed=0;HapticImpactSpeed=0;Grounded=false;
            if(Visual){Visual.SetSteering(0);if(Visual.Wheels!=null&&Visual.Wheels.Length==4)for(int i=0;i<4;i++){var anchor=VehicleGeometry.Anchor(model,i);Visual.Wheels[i].localPosition=new Vector3(anchor.x,Metrics.FullyExtendedWheelLocalY,anchor.z);Visual.Wheels[i].localRotation=Quaternion.identity;Visual.FollowWheel(i,0);VehicleGeometry.FollowRaceWheelCollider(transform,model,i,Metrics.FullyExtendedWheelLocalY);}}
            // Solver refuses missing/ambiguous/steep support without changing pose.
            // Cleared histories remain safe if the fit is refused.
            return TryInitializeSupportedRest(position,rotation,out reason,out _);
        }

        public void Recover(Vector3 position,Quaternion rotation)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            VehiclePopulation.VehiclePopulationHooks.PoseWillReset(this);
            Body.position=position+Vector3.up*.7f;Body.rotation=rotation;transform.SetPositionAndRotation(Body.position,rotation);Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;
            Array.Clear(compression,0,compression.Length);pedals.Reset();Body.WakeUp();
        }
        public void ApplyTuning(Core.OwnedCar owned)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);

            VehiclePopulation.VehiclePopulationHooks.ShapeWillChange(this);
            model=owned.model;importedSettings=model>=Core.ImportedVehicleCatalogue.BuiltInCount?Core.ImportedVehicleCatalogue.Get(model).Settings:null;suspensionLevel=owned.suspension;savedDifferential=1-owned.differential*.1f;var spec=Core.GarageRules.Catalogue[owned.model];Body.mass=spec.Mass;PowerFactor=Core.GarageRules.Power(owned);SpeedLimit=Core.GarageRules.Speed(owned);TyreFactor=Core.GarageRules.Grip(owned);BrakeFactor=Core.GarageRules.Braking(owned);SpringFactor=1+owned.suspension*.08f;DamperFactor=1+owned.suspension*.1f;DifferentialFactor=1-owned.differential*.1f;
            if(model==3){RaceSetup=(owned.raceSetup??new Core.RaceSetup()).Clone();RaceSetup.Validate();RaceTyres=(owned.raceTyres??new Core.RaceTyreState()).Clone();RaceTyres.Validate();SpringFactor*=RaceSetup.spring;DamperFactor*=Mathf.Sqrt(RaceSetup.spring);DifferentialFactor*=1.2f-.5f*RaceSetup.differential;}
            Body.centerOfMass=importedSettings!=null?new Vector3(0,importedSettings.CentreOfMassY,0):model==3?new Vector3(0,.24f,-.12f):new Vector3(0,.48f,.02f);
            var box=GetComponent<BoxCollider>();if(box){if(model<Core.ImportedVehicleCatalogue.BuiltInCount&&model!=3){var centre=box.center;centre.y=Core.VehicleMetric.ColliderCentreY;box.center=centre;var size=box.size;size.y=Core.VehicleMetric.ColliderHeight;box.size=size;}VehicleGeometry.ApplyCollider(box,model);}VehicleGeometry.EnsureRaceWheelColliders(transform,model);obstacleWheels=VehicleObstacleWheels.Build(this,importedSettings);Body.ResetInertiaTensor();
            pedals.Reset();Damage=Mathf.Clamp(owned.damage,0,100);Visual.RestoreDents(owned.dents);Visual.ApplyPaintJob(owned);if(model==3)Visual.SetRaceCompound(RaceTyres.compound);
        }
        public void ReplicaRaceCondition(Core.RaceSetup setup,Core.RaceTyreState tyres){if(model!=3||setup==null||tyres==null)return;RaceSetup=setup.Clone();RaceSetup.Validate();RaceTyres=tyres.Clone();RaceTyres.Validate();Visual?.SetRaceCompound(RaceTyres.compound);}
        public void CaptureRaceCondition(Core.OwnedCar owned){if(model!=3)return;owned.raceSetup=RaceSetup.Clone();owned.raceTyres=RaceTyres.Clone();}
        public void ReplaceRaceSetup(Core.RaceSetup setup){if(model!=3||setup==null)return;RaceSetup=setup.Clone();RaceSetup.Validate();RaceTyres.Replace(RaceSetup.compound,20);SpringFactor=(1+GetSuspensionLevel()*.08f)*RaceSetup.spring;DamperFactor=(1+GetSuspensionLevel()*.1f)*Mathf.Sqrt(RaceSetup.spring);DifferentialFactor=savedDifferential* (1.2f-.5f*RaceSetup.differential);Visual?.SetRaceCompound(RaceTyres.compound);}
        int suspensionLevel;float savedDifferential=1;int GetSuspensionLevel()=>suspensionLevel;
        public void ReplicaDamage(float damage){Damage=Mathf.Clamp(damage,0,100);}
        public void Repair(){Damage=0;Visual.Repair();}
    }
}
