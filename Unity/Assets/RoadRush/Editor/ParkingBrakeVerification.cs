using System;using System.IO;using System.Linq;using System.Reflection;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;using UnityEngine.InputSystem;
namespace RoadRush.Editor
{
 public static class ParkingBrakeVerification
 {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;static int checks;static JArray cases;
  static void Check(bool v,string why){if(!v)throw new InvalidOperationException("PARKING FAIL "+why);checks++;Debug.Log("PARKING PASS "+why);}
  sealed class Fixture:IDisposable
  {
   public GameObject floor,root;public VehicleController car;
   public Fixture(float slope=0,bool initial=false,bool enable=true)
   {
    floor=new GameObject("Owned parking support");floor.transform.rotation=Quaternion.Euler(slope,0,0);floor.transform.position=-floor.transform.up*.5f;floor.AddComponent<BoxCollider>().size=new Vector3(100,1,100);
    root=new GameObject("Owned parking car");root.layer=8;root.transform.position=Vector3.up*.7f;var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);var visual=root.AddComponent<CarVisual>();visual.Build(Color.white,false);car=root.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Private).Invoke(car,null);car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=initial&&enable;car.ApplyTuning(new Core.OwnedCar());car.Body.interpolation=RigidbodyInterpolation.None;Physics.SyncTransforms();Check(car.TryInitializeSupportedRest(Vector3.zero,Quaternion.identity,out var reason,out _),"actual supported test pose: "+reason);car.ParkingAutomaticEnabled=enable;
   }
   public void Advance(float seconds){for(int i=0;i<Mathf.RoundToInt(seconds/.01f);i++){typeof(VehicleController).GetMethod("FixedUpdate",Private).Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();}}
   public void Dispose(){if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}if(floor)UnityEngine.Object.DestroyImmediate(floor);}
  }
  public static void Run()
  {
   int exit=1;var mode=Physics.simulationMode;float step=Time.fixedDeltaTime,scale=Time.timeScale;string output=Path.GetFullPath("Builds/Verification/ParkingBrake/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);checks=0;cases=new JArray();
   try
   {
    Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"isolated test opens no real session/career");Check(mode==SimulationMode.FixedUpdate,"entry physics remains automatic mode0");Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;Time.timeScale=1;
    FreshStop(0);FreshStop(8);FreshStop(15);ReleaseForward();ReleaseReverse();Manual();ImpactAndSupport();Slippery();AiUnchanged();InputEdges();Wire();
    File.WriteAllText(Path.Combine(output,"receipt.json"),new JObject{{"checks",checks},{"cases",cases},{"scope","actual isolated physics/input and production JSON capture/apply; native two-process parking acceptance remains required"}}.ToString());exit=0;
   }catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}
   finally{Physics.simulationMode=mode;Time.fixedDeltaTime=step;Time.timeScale=scale;}
   // Exit AFTER restoration; Unity may terminate before unwinding a finally.
   Debug.Log("PARKING "+(exit==0?"PASS ":"FAIL ")+checks+" "+output);EditorApplication.Exit(exit);
  }
  static void FreshStop(float slope)
  {
   using(var f=new Fixture(slope))
   {Check(!f.car.ParkingBrakeApplied,"fresh unlatched supported stop, no pedals slope"+slope);var initial=f.car.Body.position;var constraints=f.car.Body.constraints;bool kine=f.car.Body.isKinematic;f.Advance(2);Check(f.car.ParkingBrakeApplied&&f.car.ParkingBrakeHolding,"auto engages from genuine no-pedal hill stop slope"+slope);float initialRoll=Vector3.Distance(initial,f.car.Body.position);var settled=f.car.Body.position;f.Advance(10);float creep=Vector3.Distance(settled,f.car.Body.position);Check(initialRoll<.15f&&creep<.05f&&f.car.Body.linearVelocity.magnitude<.04f,"measured initial rollback and ten-second creep bounded slope"+slope);Check(f.car.Body.constraints==constraints&&f.car.Body.isKinematic==kine&&!kine,"no constraints/kinematic anchoring slope"+slope);cases.Add(new JObject{{"slope",slope},{"initialRollback",initialRoll},{"tenSecondCreep",creep},{"speed",f.car.Body.linearVelocity.magnitude}});}
  }
  static void ReleaseForward(){using(var f=new Fixture(8,true)){f.Advance(.5f);f.car.NetworkThrottle=.5f;f.Advance(.2f);Check(!f.car.ParkingBrakeApplied&&Mathf.Abs(f.car.Speed)>.05f,"forward power releases parking automatically");}}
  static void ReleaseReverse(){using(var f=new Fixture(0,true)){f.Advance(.2f);f.car.NetworkBrake=1;f.Advance(.2f);Check(!f.car.Reverse&&f.car.ParkingBrakeApplied,"brake reverse retains original deliberate delay");f.Advance(.4f);Check(f.car.Reverse&&!f.car.ParkingBrakeApplied&&f.car.Speed<-.05f,"parking never blocks brake-to-reverse intentional power");}}
  static void Manual(){using(var f=new Fixture(0,true)){f.car.NetworkParkingBrakeRequest=false;f.car.NetworkParkingBrakeCommand=1;f.Advance(.5f);Check(!f.car.ParkingBrakeApplied&&f.car.ParkingBrakeCommandAck==1,"manual release remains released while stationary");f.car.NetworkParkingBrakeRequest=true;f.car.NetworkParkingBrakeCommand=2;f.Advance(.2f);Check(f.car.ParkingBrakeApplied&&f.car.ParkingBrakeHolding&&f.car.ParkingBrakeCommandAck==2,"manual re-engage processed once with acknowledgement");}}
  static void ImpactAndSupport(){using(var f=new Fixture(0,true)){f.Advance(.5f);var initial=f.car.Body.position;f.car.Body.AddForce(Vector3.forward*(f.car.Body.mass*3),ForceMode.Impulse);Physics.Simulate(.01f);f.Advance(.2f);Check(Vector3.Distance(initial,f.car.Body.position)>.25f&&f.car.Body.linearVelocity.magnitude>1&&!f.car.ParkingBrakeHolding,"external physical impulse overcomes finite parking friction");f.floor.SetActive(false);f.Advance(.2f);Check(!f.car.ParkingBrakeHolding&&f.car.Body.linearVelocity.y<-.5f,"missing support/airborne body is never anchored");}}
  static void Slippery(){using(var f=new Fixture(8,true)){f.car.Grip=.03f;var initial=f.car.Body.position;f.Advance(2);Check(Vector3.Distance(initial,f.car.Body.position)>.2f&&!f.car.ParkingBrakeHolding,"insufficient contact friction permits physical downhill slide");}}
  static void AiUnchanged(){using(var f=new Fixture(0,false,false)){f.car.NetworkParkingBrakeRequest=true;f.car.NetworkParkingBrakeCommand=1;f.Advance(.5f);Check(!f.car.ParkingBrakeApplied&&!f.car.ParkingBrakeHolding&&f.car.ParkingBrakeCommandAck==0,"ordinary NPC/patrol controllers remain opt-out");}}
  static void InputEdges()
  {
   GameObject root=null;Gamepad pad=null;Keyboard keyboard=null;try
   {
    root=new GameObject("Owned parking input");var input=root.AddComponent<DriverInput>();pad=InputSystem.AddDevice<Gamepad>();keyboard=InputSystem.AddDevice<Keyboard>();input.VerificationInput=true;input.VerificationPad=pad;input.VerificationKeyboard=keyboard;input.ParkingInputAllowed=true;var update=typeof(DriverInput).GetMethod("Update",Private);
    void Poll(){InputSystem.Update();update.Invoke(input,null);}Poll();InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.P));Poll();Check(input.ParkingBrakePressed,"keyboard P produces one parking edge");Poll();Check(!input.ParkingBrakePressed,"held P never repeats toggle");InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());Poll();
    input.ParkingInputAllowed=false;InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown));Poll();Check(!input.ParkingBrakePressed,"menu Dpad-down navigation does not toggle parking");input.ParkingInputAllowed=true;Poll();Check(!input.ParkingBrakePressed,"held menu button cannot leak through driving re-entry");InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());Poll();InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown));Poll();Check(input.ParkingBrakePressed,"released/repressed gamepad button toggles in driving");InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());Poll();
    var action=new InputAction("Owned calibration fixture");action.AddBinding("<Joystick>/stick/x");var rebind=action.PerformInteractiveRebinding();typeof(DriverInput).GetField("bindingAction",Private).SetValue(input,action);typeof(DriverInput).GetField("rebinding",Private).SetValue(input,rebind);InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.P));Poll();Check(!input.ParkingBrakePressed,"calibration suppresses parking edges");input.CancelBinding();Poll();Check(!input.ParkingBrakePressed,"held calibration button cannot leak after completion");
    input.QueueParkingToggle(true,0);Check(!input.ParkingBrakeRequest&&input.ParkingBrakeCommand==1,"manual command toggles applied state");input.QueueParkingToggle(true,0);Check(input.ParkingBrakeRequest&&input.ParkingBrakeCommand==2,"second unacknowledged tap toggles pending state correctly");
   }finally{if(root)UnityEngine.Object.DestroyImmediate(root);if(pad!=null)InputSystem.RemoveDevice(pad);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);}
  }
  static void Wire()
  {
   using(var host=new Fixture(0,true))using(var guest=new Fixture(0,false))
   {
    guest.root.SetActive(false);guest.floor.SetActive(false);host.car.InitializeNetworkParking(true,1234);host.Advance(.2f);Check(host.car.ParkingBrakeCommandAck==1234&&host.car.ParkingBrakeApplied,"joining player seeds existing high command baseline without replay");
    var capture=typeof(LanSession).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static);var state=capture.Invoke(null,new object[]{host.car});string json=JsonUtility.ToJson(state);var decoded=JsonUtility.FromJson(json,state.GetType());guest.car.Replica=true;guest.car.Body.isKinematic=true;var replicaSession=guest.root.AddComponent<LanSession>();typeof(LanSession).GetMethod("Apply",Private).Invoke(replicaSession,new[]{(object)guest.car,decoded});Check(guest.car.ParkingBrakeApplied==host.car.ParkingBrakeApplied&&guest.car.ParkingBrakeHolding==host.car.ParkingBrakeHolding&&guest.car.ParkingBrakeCommandAck==1234,"actual authoritative parking state survives production Capture/JSON/Apply");
    var packet=typeof(LanSession).GetNestedType("Packet",BindingFlags.NonPublic);var request=Activator.CreateInstance(packet,true);packet.GetField("type").SetValue(request,"input");packet.GetField("parkingBrake").SetValue(request,false);packet.GetField("parkingCommand").SetValue(request,1235);string wire=(string)typeof(LanSession).GetMethod("Encode",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{request});var parsed=JObject.Parse(wire);Check((int)parsed["protocol"]==11&&!(bool)parsed["parkingBrake"]&&(int)parsed["parkingCommand"]==1235,"protocol11 encodes independent parking request+revision");
    host.car.NetworkParkingBrakeRequest=false;host.car.NetworkParkingBrakeCommand=1235;host.Advance(.1f);Check(host.car.ParkingBrakeCommandAck==1235&&!host.car.ParkingBrakeApplied,"host applies next guest parking release once");
    host.car.InitializeNetworkParking(false,1236,true);host.Advance(.3f);Check(!host.car.ParkingBrakeApplied&&host.car.ParkingBrakeCommandAck==1236,"pending manual release survives hello seeding without automatic relatch");
   }
  }
 }
}
