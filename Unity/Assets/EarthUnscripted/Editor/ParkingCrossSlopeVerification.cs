using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    // Isolated actual physics, no force formula substitution. Not a native hill proof.
    public static class ParkingCrossSlopeVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const float Dt=.01f,MaximumInitialRollback=.15f,MaximumCreep=.05f,MaximumSpeed=.04f,MaximumAngularNorm=.04f,MaximumOrientationChange=.5f;
        static readonly MethodInfo Awake=typeof(VehicleController).GetMethod("Awake",Hidden),Tick=typeof(VehicleController).GetMethod("FixedUpdate",Hidden);
        [Serializable]sealed class Sample{public float seconds,speed,angularNorm;public Vector3 position,rotationEuler;public bool applied,holding;public int contacts;}
        [Serializable]sealed class Case
        {
            public string name,error;public float pitch,roll,yaw,steer,actualSteer,compressionSpread,actualPlaneIncline,initialRollback,tenSecondCreep,finalSpeed,maxHoldingAngularNorm,maxHoldingSpeed,orientationDeltaDegrees;
            public bool asymmetricCg,warped,pass,freshUnlatched,noAnchoring,finalApplied,finalHolding;public int holdingSamples,minimumHoldingContacts=4;
            public List<Sample> samples=new List<Sample>();
        }
        [Serializable]sealed class Report
        {public string status,scope="Actual isolated physics fresh no-pedal cross-slope fixtures; no native-world or LAN acceptance";public float maximumInitialRollback=MaximumInitialRollback,maximumCreep=MaximumCreep,maximumFinalSpeed=MaximumSpeed,maximumHoldingAngularNorm=MaximumAngularNorm,maximumOrientationChange=MaximumOrientationChange;public List<Case> cases=new List<Case>();}
        sealed class Fixture:IDisposable
        {
            public GameObject Floor,Root;public VehicleController Car;
            public Fixture(float pitch,float roll,float yaw,float steer,bool asymmetricCg,bool warped)
            {
                try
                {
                    var surface=Quaternion.Euler(pitch,0,roll);var heading=Quaternion.Euler(0,yaw,0);
                    Floor=new GameObject("Owned cross-slope support");
                    if(!warped){Floor.transform.rotation=surface;Floor.transform.position=-Floor.transform.up*.5f;Floor.AddComponent<BoxCollider>().size=new Vector3(100,1,100);}
                    else for(int i=0;i<4;i++)
                    {var anchor=heading*VehicleGeometry.Anchor(0,i);float h=-(surface*Vector3.up).x*anchor.x/(surface*Vector3.up).y-(surface*Vector3.up).z*anchor.z/(surface*Vector3.up).y+(i==3?.03f:0);var pad=new GameObject("Independent tilted wheel pad");pad.transform.SetParent(Floor.transform,false);pad.transform.rotation=surface;pad.transform.position=new Vector3(anchor.x,h,anchor.z)-(surface*Vector3.up)*.05f;pad.AddComponent<BoxCollider>().size=new Vector3(.9f,.1f,.9f);}
                    Root=new GameObject("Owned cross-slope player");Root.layer=8;Root.transform.SetPositionAndRotation(Vector3.up*.7f,heading);
                    var box=Root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
                    var visual=Root.AddComponent<CarVisual>();visual.Build(Color.white,false);Car=Root.AddComponent<VehicleController>();Awake.Invoke(Car,null);Car.Visual=visual;Car.NetworkDriven=true;
                    // Do not force a latch through supported-rest initialization.
                    Car.ParkingAutomaticEnabled=false;Car.ApplyTuning(new Core.OwnedCar());Car.Body.interpolation=RigidbodyInterpolation.None;if(asymmetricCg)Car.Body.centerOfMass=new Vector3(.16f,.62f,.24f);Car.NetworkSteer=steer/30f;Physics.SyncTransforms();
                    if(!Car.TryInitializeSupportedRest(Vector3.zero,heading,out var reason,out _))throw new InvalidOperationException("Rest initialization: "+reason);
                    Car.ParkingAutomaticEnabled=true;
                }
                catch{Dispose();throw;}
            }
            public void Step(){Tick.Invoke(Car,null);Physics.Simulate(Dt);Physics.SyncTransforms();}
            public void Dispose(){if(Root){Geometry.ReleaseMeshes(Root);UnityEngine.Object.DestroyImmediate(Root);}if(Floor)UnityEngine.Object.DestroyImmediate(Floor);}
        }
        static Sample Capture(VehicleController car,float seconds)
        {
            int contacts=0;foreach(bool v in car.WheelGrounded)if(v)contacts++;
            return new Sample{seconds=seconds,position=car.Body.position,rotationEuler=car.Body.rotation.eulerAngles,speed=car.Body.linearVelocity.magnitude,angularNorm=car.Body.angularVelocity.magnitude,applied=car.ParkingBrakeApplied,holding=car.ParkingBrakeHolding,contacts=contacts};
        }
        static Case RunCase(string name,float pitch,float roll,float yaw=0,float steer=0,bool asymmetricCg=false,bool warped=false)
        {
            var result=new Case{name=name,pitch=pitch,roll=roll,yaw=yaw,steer=steer,asymmetricCg=asymmetricCg,warped=warped};
            try
            {
                using(var f=new Fixture(pitch,roll,yaw,steer,asymmetricCg,warped))
                {
                    var car=f.Car;var initial=car.Body.position;var constraints=car.Body.constraints;bool kinematic=car.Body.isKinematic;result.freshUnlatched=!car.ParkingBrakeApplied;
                    result.actualPlaneIncline=Vector3.Angle(Vector3.up,Quaternion.Euler(pitch,0,roll)*Vector3.up);
                    for(int i=1;i<=200;i++){f.Step();result.samples.Add(Capture(car,i*Dt));}
                    result.initialRollback=Vector3.Distance(initial,car.Body.position);var settled=car.Body.position;var orientation=car.Body.rotation;
                    for(int i=1;i<=1000;i++)
                    {
                        f.Step();var sample=Capture(car,2+i*Dt);result.samples.Add(sample);result.maxHoldingAngularNorm=Mathf.Max(result.maxHoldingAngularNorm,sample.angularNorm);result.maxHoldingSpeed=Mathf.Max(result.maxHoldingSpeed,sample.speed);result.minimumHoldingContacts=Mathf.Min(result.minimumHoldingContacts,sample.contacts);if(sample.holding)result.holdingSamples++;
                    }
                    result.tenSecondCreep=Vector3.Distance(settled,car.Body.position);result.finalSpeed=car.Body.linearVelocity.magnitude;result.orientationDeltaDegrees=Quaternion.Angle(orientation,car.Body.rotation);result.finalApplied=car.ParkingBrakeApplied;result.finalHolding=car.ParkingBrakeHolding;
                    var compression=(float[])typeof(VehicleController).GetField("compression",Hidden).GetValue(car);result.compressionSpread=Mathf.Max(compression)-Mathf.Min(compression);result.actualSteer=car.Steering;
                    result.noAnchoring=car.Body.constraints==constraints&&car.Body.isKinematic==kinematic&&!kinematic;
                    result.pass=result.freshUnlatched&&result.finalApplied&&result.finalHolding&&result.initialRollback<MaximumInitialRollback&&result.tenSecondCreep<MaximumCreep&&result.finalSpeed<MaximumSpeed&&result.maxHoldingAngularNorm<MaximumAngularNorm&&result.minimumHoldingContacts==4&&result.noAnchoring&&result.orientationDeltaDegrees<MaximumOrientationChange&&Mathf.Abs(result.actualSteer-steer)<.1f&&(!(asymmetricCg||warped)||result.compressionSpread>.001f);
                    if(!result.pass)result.error="Strict physical gate failed; inspect retained full trajectory, no waiver";
                }
            }
            catch(Exception e){result.pass=false;result.error=e.ToString();}
            return result;
        }
        public static void Run()
        {
            int exit=1;var mode=Physics.simulationMode;float step=Time.fixedDeltaTime,scale=Time.timeScale;var report=new Report();
            string output=Path.GetFullPath("Builds/Verification/ParkingCrossSlope/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            try
            {
                if(Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>()||mode!=SimulationMode.FixedUpdate)throw new InvalidOperationException("Requires isolated Editor with automatic entry physics and no real session");
                Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=Dt;Time.timeScale=1;
                report.cases.Add(RunCase("pitch8-control",8,0));report.cases.Add(RunCase("fresh-roll8",0,8));report.cases.Add(RunCase("fresh-pitch8-roll6",8,6));
                report.cases.Add(RunCase("fresh-roll-minus8",0,-8));report.cases.Add(RunCase("fresh-pitch8-roll-minus6",8,-6));
                report.cases.Add(RunCase("compound-yaw37",8,6,37));report.cases.Add(RunCase("cross-steered-left",0,8,0,-25));report.cases.Add(RunCase("compound-steered-right",8,6,37,25));
                report.cases.Add(RunCase("asymmetric-CG-compound",8,6,37,0,true));report.cases.Add(RunCase("warped-crossfall",0,8,0,0,false,true));
                bool pass=true;foreach(var c in report.cases)pass&=c.pass;report.status=pass?"PASS":"FAIL";exit=pass?0:1;
            }
            catch(Exception e){report.status="FAIL";File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}
            finally{Physics.simulationMode=mode;Time.fixedDeltaTime=step;Time.timeScale=scale;File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(report,true));}
            foreach(var c in report.cases)Debug.Log("PARKING CROSS-SLOPE "+c.name+" "+c.pass+" initial="+c.initialRollback+" creep="+c.tenSecondCreep+" angular="+c.maxHoldingAngularNorm+" yaw/rotation="+c.orientationDeltaDegrees);
            Debug.Log("PARKING CROSS-SLOPE "+report.status+" "+output);EditorApplication.Exit(exit);
        }
    }
}
