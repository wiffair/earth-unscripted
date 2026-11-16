using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace RoadRush
{
 public sealed class BetaCoastVerification:MonoBehaviour
 {
  readonly List<string> errors=new List<string>();
  readonly List<string> policeSamples=new List<string>();
  float medianMs,p95Ms;int activeTraffic;
  int priorityChecks;bool stoppedPassed,movingContact,timerPassed;float stoppedGap=float.MaxValue,movingGap=float.MaxValue,maxPlayerY=float.NegativeInfinity;
  [Serializable] sealed class Report {public bool passed;public int beachPixels,activeMeshes;public string failure;public string[] errors;public int priorityChecks;public float medianFrameMilliseconds,p95FrameMilliseconds;public int activeTraffic;public bool stoppedPolicePassed,movingPoliceContact,timerPassed;public float stoppedMinimumGap,movingMinimumGap,maximumPlayerHeight;}
  void Awake(){if(SinglePlayerBeta.VerificationOutput==null)throw new InvalidOperationException("Isolated proof required");Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception)errors.Add(m+"\n"+s);};}
  IEnumerator Start()
  {
   var stack=new Stack<IEnumerator>();stack.Push(Exercise());
   while(stack.Count>0)
   {
    bool more=false;object value=null;string failure=null;
    try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception error){failure=error.ToString();}
    if(failure!=null){Finish(false,0,0,failure);yield break;}
    if(!more){stack.Pop();continue;}if(value is IEnumerator nested){stack.Push(nested);continue;}yield return value;
   }
  }
  IEnumerator Exercise()
  {
   double began=Time.realtimeSinceStartupAsDouble;GameSession session=null;
   while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
   {if(Time.realtimeSinceStartupAsDouble-began>600){Finish(false,0,0,"Boot timeout");yield break;}yield return null;}
   var world=session.World.AutomaticWorld.Renderer;
   while(!world.CoastalSurfaceReady)
   {if(Time.realtimeSinceStartupAsDouble-began>750){Finish(false,0,0,"Coastal surface did not activate");yield break;}yield return null;}
   var root=world.transform.Find("Mapped sea and beaches");int vertices=world.CoastalBeachPixels;
   int meshes=root?root.GetComponentsInChildren<MeshRenderer>().Length:0;
   session.Input.enabled=false;session.Car.StartLocked=true;session.SetMenu(true);session.View.enabled=false;
   var ui=FindFirstObjectByType<DrivingUI>();if(ui)ui.enabled=false;
   var camera=session.View.GetComponent<Camera>();var p=session.Car.Body.position;
   camera.transform.position=p+new Vector3(30,25,0);camera.transform.LookAt(p+new Vector3(-450,-15,0));
   for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
   ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"coast-west.png"));yield return new WaitForEndOfFrame();
   camera.transform.position=p+new Vector3(-30,25,0);camera.transform.LookAt(p+new Vector3(450,-15,0));
   for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
   ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"coast-east.png"));yield return new WaitForEndOfFrame();
   yield return Beta15WaterChecks.Exercise(session);
   CheckPriorities();yield return CheckTrafficPerformance(session);yield return CheckPolice(session);
   Finish(vertices>0&&meshes>=2&&world.CoastalGroundBound&&world.MappedBeachAt(-21111.664f,-32376.703f).g>.5f&&errors.Count==0&&priorityChecks==6&&stoppedPassed&&movingContact&&timerPassed&&medianMs<50&&p95Ms<100,vertices,meshes,null);
  }
  IEnumerator CheckTrafficPerformance(GameSession session)
  {
   session.View.enabled=true;session.Car.StartLocked=true;session.SetMenu(false);
   activeTraffic=FindObjectsByType<TrafficCar>(FindObjectsSortMode.None).Length;
   var samples=new List<float>();double start=Time.realtimeSinceStartupAsDouble;
   while(Time.realtimeSinceStartupAsDouble-start<9)
   {if(Time.realtimeSinceStartupAsDouble-start>1)samples.Add(Time.unscaledDeltaTime*1000);yield return null;}
   samples.Sort();medianMs=samples[samples.Count/2];p95Ms=samples[Mathf.Min(samples.Count-1,(int)(samples.Count*.95f))];
   session.SetMenu(true);
  }
  void CheckPriorities()
  {
   var minor=new GeoRoad{id="minor",kind="residential"};var major=new GeoRoad{id="major",kind="primary"};var circle=new GeoRoad{id="circle",kind="primary",roundabout=true};
   void Check(bool value){if(value)priorityChecks++;else errors.Add("Traffic priority scenario failed at "+priorityChecks);}
   Check(TrafficJunctionRules.MustYield(minor,major,3,2,20,0,true,false));
   Check(!TrafficJunctionRules.MustYield(major,minor,3,2,20,0,true,false));
   Check(TrafficJunctionRules.MustYield(major,circle,3,2,20,0,true,false));
   Check(!TrafficJunctionRules.MustYield(circle,major,3,2,20,0,true,true));
   Check(!TrafficJunctionRules.MustYield(minor,major,3,2,20,6,true,true));
   Check(!TrafficJunctionRules.MustYield(minor,major,3,12,60,0,true,true));
  }
  IEnumerator CheckPolice(GameSession session)
  {
   var patrols=FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None);if(patrols.Length==0){errors.Add("No physical patrol");yield break;}
   var patrol=patrols[0];foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))if(traffic!=patrol.Marker)traffic.gameObject.SetActive(false);
   var car=session.Car;var region=session.World.Route.Region;var road=region.Nearest(car.Body.position,out var point,out _);
   region.RoadPoint(road,region.RoadProgress(road,point),out var forward);if(forward.z<0)forward=-forward;
   point=region.DrivingLanePoint(road,point,forward,out forward);forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;var rotation=Quaternion.LookRotation(forward,Vector3.up);
   car.NetworkDriven=true;car.StartLocked=false;session.SetMenu(false);car.NetworkThrottle=0;car.NetworkBrake=car.NetworkHandbrake=1;
   car.TryInitializeSupportedRest(point,rotation,out _,out _);patrol.Car.TryInitializeSupportedRest(point-forward*16,rotation,out _,out _);
   patrol.enabled=true;patrol.BeginPursuit();int contacts=0;car.Crashed+=(v,p)=>{if(car.LastImpactWasPolice&&v>1){contacts++;movingContact=true;}};
   var contactProbe=car.gameObject.AddComponent<BetaPoliceContactProbe>();
   double start=Time.realtimeSinceStartupAsDouble;
   while(Time.realtimeSinceStartupAsDouble-start<6){stoppedGap=Mathf.Min(stoppedGap,Vector3.Distance(car.Body.position,patrol.Car.Body.position));yield return new WaitForFixedUpdate();}
   stoppedPassed=contacts==0&&stoppedGap>=5.5f;
   patrol.Disengage();patrol.enabled=false;car.NetworkBrake=car.NetworkHandbrake=0;
   car.TryInitializeSupportedRest(point,rotation,out _,out _);patrol.Car.TryInitializeSupportedRest(point-forward*12,rotation,out _,out _);
   car.Body.linearVelocity=forward*30;patrol.Car.Body.linearVelocity=forward*30;
   yield return new WaitForFixedUpdate();
   // Use the real observation/start path and verify its actual countdown.
   typeof(PoliceDriver).GetProperty("State").SetValue(patrol,PoliceDriver.Duty.Parked);patrol.CooldownUntil=0;
   patrol.enabled=true;session.ReportSpeeding(patrol);timerPassed=session.Wanted>59.9f&&session.Wanted<=60;
   if(!patrol.Pursuing)patrol.BeginPursuit();movingContact=false;contactProbe.PoliceContact=false;start=Time.realtimeSinceStartupAsDouble;
   while(Time.realtimeSinceStartupAsDouble-start<14&&!movingContact)
   {
    if(policeSamples.Count==0||Time.realtimeSinceStartupAsDouble-start>policeSamples.Count*.25)policeSamples.Add($"t={Time.realtimeSinceStartupAsDouble-start:F2} gap={Vector3.Distance(car.Body.position,patrol.Car.Body.position):F2} player={car.Speed:F2} police={patrol.Car.Speed:F2} steer={patrol.Car.NetworkSteer:F2} brake={patrol.Car.NetworkBrake:F2} nudgeUntil={typeof(PoliceDriver).GetField("nudgeUntil",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(patrol)} now={Time.time}");
    movingContact|=contactProbe.PoliceContact;
    movingGap=Mathf.Min(movingGap,Vector3.Distance(car.Body.position,patrol.Car.Body.position));maxPlayerY=Mathf.Max(maxPlayerY,car.Body.position.y);
    var v=car.Body.linearVelocity;car.Body.linearVelocity=forward*30+Vector3.up*v.y;car.Body.angularVelocity=Vector3.zero;
    yield return new WaitForFixedUpdate();
   }
   File.WriteAllLines(Path.Combine(SinglePlayerBeta.VerificationOutput,"police-physical-samples.txt"),policeSamples);
   var update=typeof(GameSession).GetMethod("UpdatePolice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   update.Invoke(session,new object[]{61f});timerPassed&=session.Wanted==0&&!patrol.Pursuing;
   session.SetMenu(true);
  }
  void Finish(bool passed,int vertices,int meshes,string failure)
  {File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),JsonUtility.ToJson(new Report{passed=passed,beachPixels=vertices,activeMeshes=meshes,failure=failure,errors=errors.ToArray(),priorityChecks=priorityChecks,medianFrameMilliseconds=medianMs,p95FrameMilliseconds=p95Ms,activeTraffic=activeTraffic,stoppedPolicePassed=stoppedPassed,movingPoliceContact=movingContact,timerPassed=timerPassed,stoppedMinimumGap=stoppedGap,movingMinimumGap=movingGap,maximumPlayerHeight=maxPlayerY},true));Application.Quit(passed?0:1);}
 }
 public sealed class BetaPoliceContactProbe:MonoBehaviour
 {
  public bool PoliceContact;
  void OnCollisionEnter(Collision collision){var other=collision.collider.GetComponentInParent<TrafficCar>();if(other&&other.Police)PoliceContact=true;}
 }
}
