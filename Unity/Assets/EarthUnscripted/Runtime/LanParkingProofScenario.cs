using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace EarthUnscripted
{
    // Opt-in test coordinator ONLY. Files coordinate test phases, never parking state.
    // Request/ack/state still use DriverInput -> LanSession protocol11 -> actual host physics.
    public sealed class LanParkingProofScenario
    {
        [Serializable]public sealed class Command {public string token;public int phase;}
        [Serializable]public sealed class CarState
        {public Vector3 position;public float speed,signedSpeed,damage;public bool replica,kinematic,automatic,applied,holding,reverse;public int ack,contacts;}
        [Serializable]public sealed class Sample
        {public int frame,phase,command;public double realtime,simulation;public bool joined,bridgeReady,requested;public float throttle,brake,handbrake;public CarState host,guest;}
        [Serializable]public sealed class Receipt
        {public string role,status="RUNNING",error;public int phase=-1;public bool completed;public List<Sample> samples=new List<Sample>();}
        readonly string prefix,shared,token,role;readonly Receipt data;double next,phaseAt;int phase=-1;bool queued;
        float submittedThrottle,submittedBrake,submittedHandbrake;
        public bool Completed=>data.completed;public bool Passed=>data.completed&&data.error==null;
        public LanParkingProofScenario(string localPrefix,string sharedPrefix,string role,string token)
        {
            prefix=Path.GetFullPath(localPrefix);shared=Path.GetFullPath(sharedPrefix);this.role=role;this.token=token;
            if((role!="host"&&role!="client")||prefix!=shared+"-"+role||string.IsNullOrEmpty(token)||token.Length!=32||Path.GetFullPath(PlayerProfile.StoragePath)!=prefix+"-profile.json")throw new InvalidOperationException("Parking proof must own exact isolated role profile/prefix");
            data=new Receipt{role=role};
        }
        static CarState Capture(VehicleController c)
        {
            if(!c||!c.Body)throw new InvalidOperationException("Missing LAN car");int contacts=0;if(!c.Replica)foreach(bool hit in c.WheelGrounded)if(hit)contacts++;
            return new CarState{position=c.Body.position,speed=c.AbsoluteSpeed,signedSpeed=c.Speed,damage=c.Damage,replica=c.Replica,kinematic=c.Body.isKinematic,automatic=c.ParkingAutomaticEnabled,applied=c.ParkingBrakeApplied,holding=c.ParkingBrakeHolding,reverse=c.Reverse,ack=c.ParkingBrakeCommandAck,contacts=c.Replica?-1:contacts};
        }
        void Write()
        {
            string destination=prefix+"-parking.json",stage=destination+".writing";File.WriteAllText(stage,JsonUtility.ToJson(data));
            // Single writer per role; reader retries an absent file during this bounded rename.
            if(File.Exists(destination))File.Delete(destination);File.Move(stage,destination);
        }
        public void Tick(GameSession session,bool bridgeReady,ref float steer,ref float throttle,ref float brake,ref float handbrake)
        {
            if(data.error!=null)throw new InvalidOperationException(data.error);
            try
            {
                // Client finishes first as in existing LAN metric proof. Host must
                // still finish its retained evidence after peer teardown.
                if(phase==8&&Time.realtimeSinceStartupAsDouble-phaseAt>=(role=="host"?4:1))
                {data.completed=true;data.status="SCENARIO_COMPLETE_REQUIRES_EXTERNAL_VALIDATION";Write();return;}
                if(!session.Network.Joined||!session.Network.Other)return;
                if(Time.realtimeSinceStartupAsDouble>=next)
                {
                    next=Time.realtimeSinceStartupAsDouble+.1;
                    var command=JsonUtility.FromJson<Command>(File.ReadAllText(shared+"-parking-command.json"));
                    if(command==null||command.token!=token||command.phase<phase||command.phase>phase+1||command.phase>8)throw new InvalidOperationException("Invalid/nonsequential owned parking phase");
                    if(command.phase!=phase){phase=command.phase;data.phase=phase;phaseAt=Time.realtimeSinceStartupAsDouble;queued=false;}
                    var host=role=="host"?session.Car:session.Network.Other;var guest=role=="host"?session.Network.Other:session.Car;
                    if(data.samples.Count>=10000)throw new InvalidOperationException("Parking sample cap exceeded");
                    data.samples.Add(new Sample{frame=Time.frameCount,phase=phase,realtime=Time.realtimeSinceStartupAsDouble,simulation=Time.timeAsDouble,joined=session.Network.Joined,bridgeReady=bridgeReady,command=session.Input.ParkingBrakeCommand,requested=session.Input.ParkingBrakeRequest,host=Capture(host),guest=Capture(guest),throttle=submittedThrottle,brake=submittedBrake,handbrake=submittedHandbrake});
                    if(phase==8&&Time.realtimeSinceStartupAsDouble-phaseAt>=(role=="host"?4:1)){data.completed=true;data.status="SCENARIO_COMPLETE_REQUIRES_EXTERNAL_VALIDATION";}
                    Write();
                }
                if(phase<1){submittedThrottle=throttle;submittedBrake=brake;submittedHandbrake=handbrake;return;}
                if(!queued&&(phase==3||phase==4)&&role=="client")
                {
                    bool required=phase==4;
                    if(session.Car.ParkingBrakeApplied==required)throw new InvalidOperationException("Manual transition must begin in opposite actual state");
                    session.Input.QueueParkingToggle(session.Car.ParkingBrakeApplied,session.Car.ParkingBrakeCommandAck);queued=true;
                }
                steer=0;throttle=brake=handbrake=0;
                if(phase==1||phase==6){handbrake=1;brake=session.Car.AbsoluteSpeed>.3f?.8f:0;}
                if(phase==5)
                {
                    // Functional release, not a mixed-car acceleration race. Both
                    // use finite production pedals to approach a declared2.5m/s.
                    throttle=Mathf.Clamp((2.5f-session.Car.Speed)*.22f,0,.25f);
                    brake=session.Car.Speed>3f?.25f:0;
                }
                if(phase==7)brake=.8f; // Actual brake-to-reverse delay; no Reverse/state injection.
                submittedThrottle=throttle;submittedBrake=brake;submittedHandbrake=handbrake;
            }
            catch(Exception e){data.error=e.ToString();data.status="FAIL";Write();throw;}
        }
    }
}
