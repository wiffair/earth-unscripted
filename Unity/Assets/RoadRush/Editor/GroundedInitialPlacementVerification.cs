using System;using System.IO;using System.Linq;using System.Reflection;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;using RoadRush.Core;
namespace RoadRush.Editor
{
 public static class GroundedInitialPlacementVerification
 {
  static int checks;static JArray cases=new JArray();const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static void Check(bool test,string why){if(!test)throw new InvalidOperationException("GROUND REST FAIL "+why);checks++;Debug.Log("GROUND REST PASS "+why);}
  static void Release(GameObject root){if(!root)return;foreach(var visual in root.GetComponentsInChildren<CarVisual>(true)){var adapter=typeof(CarVisual).GetField("imported",Private).GetValue(visual)as ImportedCarAppearance;adapter?.ReleaseMaterials();}Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}
  public static void Run()
  {
   string output=Path.GetFullPath("Builds/Verification/GroundedInitialPlacement/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);var mode=Physics.simulationMode;float step=Time.fixedDeltaTime,scale=Time.timeScale;checks=0;cases=new JArray();int exitCode=0;
   try
   {
    Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"isolated Editor fixture opens no career/session");Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;Time.timeScale=0;
    Case(0,0,Quaternion.identity,"flat");Case(0,0,Quaternion.Euler(8,0,0),"pitch8");Case(1,0,Quaternion.Euler(8,0,6),"pitch8roll6");Case(2,0,Quaternion.Euler(0,0,6),"roll6");Case(0,2,Quaternion.Euler(5,0,0),"suspension2");Case(1,0,Quaternion.Euler(8,0,6),"translated-yaw37",new Vector3(112.25f,7,-83.75f),37);Case(0,0,Quaternion.identity,"warped3cm");Case(0,0,Quaternion.identity,"overhead1p5");Case(0,0,Quaternion.identity,"missing");Case(0,0,Quaternion.Euler(0,0,35),"steep-fallback");
    File.WriteAllText(Path.Combine(output,"receipt.json"),new JObject{{"checks",checks},{"cases",cases},{"scope","actual isolated4-ray placement/suspension first step and ordinary Recover; not native free-body/streaming/cancellation acceptance"}}.ToString());Debug.Log("GROUND REST PASS "+checks+" "+output);
   }catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);exitCode=1;}finally{Physics.simulationMode=mode;Time.fixedDeltaTime=step;Time.timeScale=scale;}
   EditorApplication.Exit(exitCode);
  }
  static void Case(int model,int suspension,Quaternion floorRotation,string name,Vector3? hintOverride=null,float yaw=0)
  {
   var hint=hintOverride??Vector3.zero;var sourceHeading=Quaternion.Euler(0,yaw,0);GameObject support=null,root=null;try
   {
    support=new GameObject("Owned support "+name);
    if(name!="missing")
    {
     if(name=="warped3cm")for(int i=0;i<4;i++){var a=VehicleGeometry.Anchor(model,i);var pad=new GameObject("Independent support"+i);pad.transform.SetParent(support.transform,false);pad.transform.position=new Vector3(a.x,(i==3?.03f:0)-.05f,a.z);pad.AddComponent<BoxCollider>().size=new Vector3(.8f,.1f,.8f);}
     else{var floor=new GameObject("Road support");floor.transform.SetParent(support.transform,false);floor.transform.rotation=floorRotation;floor.transform.position=hint-(floorRotation*Vector3.up)*.5f;floor.AddComponent<BoxCollider>().size=new Vector3(100,1,100);}
     if(name=="overhead1p5"){var ceiling=new GameObject("Actual overhead deck");ceiling.transform.SetParent(support.transform,false);ceiling.transform.position=new Vector3(0,1.6f,0);ceiling.AddComponent<BoxCollider>().size=new Vector3(10,.2f,10);}
    }
    root=new GameObject("Owned paused player "+name);root.layer=8;root.transform.SetPositionAndRotation(hint+Vector3.up*.7f,sourceHeading);var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);var visual=root.AddComponent<CarVisual>();visual.Build(Color.white,true,model);var car=root.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",Private).Invoke(car,null);car.Visual=visual;car.NetworkDriven=true;car.NetworkHandbrake=1;
    var owned=new Core.OwnedCar{model=model,suspension=suspension,damage=7};owned.dents.Add(new SavedDent{x=.8f,y=.6f,z=0,nx=-1,severity=4});car.ApplyTuning(owned);car.Body.interpolation=RigidbodyInterpolation.None;Physics.SyncTransforms();
    var beforePosition=car.Body.position;var beforeRotation=car.Body.rotation;float mass=car.Body.mass,spring=car.SpringFactor,damper=car.DamperFactor;var cg=car.Body.centerOfMass;float centreY=box.center.y,sizeY=box.size.y;var dent=owned.dents[0];float dentX=dent.x;
    bool placed=car.TryInitializeSupportedRest(hint,sourceHeading,out string reason,out bool missing);
    if(name=="missing"||name=="steep-fallback")
    {Check(!placed&&missing==(name=="missing"),"actual missing support distinguished from conservative fit limit "+name);Check(car.Body.position==beforePosition&&car.Body.rotation==beforeRotation&&((float[])typeof(VehicleController).GetField("compression",Private).GetValue(car)).All(v=>v==0),"failed candidate retains exact safe loading pose and suspension "+name);cases.Add(new JObject{{"name",name},{"placed",false},{"supportMissing",missing},{"reason",reason}});return;}
    Check(placed&&!missing,"actual supported paused placement "+name+": "+reason);
    float laneError=Vector2.Distance(new Vector2(car.Body.position.x,car.Body.position.z),new Vector2(hint.x,hint.z));var expectedForward=sourceHeading*Vector3.forward;var actualForward=Vector3.ProjectOnPlane(car.transform.forward,Vector3.up).normalized;float headingCross=Vector3.Cross(actualForward,expectedForward).magnitude;Check(laneError<.001f,"chosen lane/root XZ retained "+name);Check(headingCross<.00001f&&Vector3.Dot(actualForward,expectedForward)>.99999f,"chosen horizontal heading retained "+name);Check(car.Grounded&&car.WheelGrounded.All(v=>v),"four initialized contacts before time advances "+name);
    Check(car.Body.mass==mass&&car.Body.centerOfMass==cg&&car.SpringFactor==spring&&car.DamperFactor==damper&&box.center.y==centreY&&box.size.y==sizeY,"mass/CG/tuning/role vertical collider unchanged "+name);Check(owned.dents.Count==1&&ReferenceEquals(dent,owned.dents[0])&&dent.x==dentX&&car.Damage==7,"career dent records/damage unchanged "+name);
    if(name=="flat"||name=="overhead1p5")Check(Mathf.Abs(car.Body.position.y-car.Metrics.RestRootHeight(mass,spring))<.0005f,"flat calibrated rest height ignores overhead deck "+name);
    var values=(float[])typeof(VehicleController).GetField("compression",Private).GetValue(car);var up=car.transform.up;var initialCompression=(float[])values.Clone();
    if(name=="warped3cm")Check(initialCompression.Max()-initialCompression.Min()>.005f,"warped road retains four independent spring travels");
    if(name=="translated-yaw37")Check(initialCompression.Max()-initialCompression.Min()<.001f,"true planar compound grade has consistent normal-axis spring travel");
    for(int i=0;i<4;i++)
    {
     Check(Physics.Raycast(car.transform.TransformPoint(VehicleGeometry.Anchor(model,i)),-up,out var h,.78f,~(1<<8),QueryTriggerInteraction.Ignore),"final actual wheel ray "+name+"/"+i);
     Check(Mathf.Abs(values[i]-(.78f-h.distance))<.00003f,"private suspension history matches actual ray travel "+name+"/"+i);Check(Mathf.Abs(Vector3.Dot(visual.Wheels[i].position-h.point,up)-.36f)<.00004f,"actual wheel centre/radius contact "+name+"/"+i);
     var vertices=visual.Wheels[i].GetComponentsInChildren<MeshFilter>().SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)));float gap=vertices.Min(v=>Vector3.Dot(v-h.point,up));Check(Mathf.Abs(gap)<.007f,"actual tyre mesh near support plane "+name+"/"+i);
    }
    Time.timeScale=1;typeof(VehicleController).GetMethod("FixedUpdate",Private).Invoke(car,null);Check(car.SuspensionImpact<.006f,"first FixedUpdate has no artificial damper-compression kick "+name);var posed=car.Body.position;Physics.Simulate(.01f);Check(car.Body.linearVelocity.magnitude<.12f&&Vector3.Distance(car.Body.position,posed)<.002f,"actual first free physics step has no launch impulse "+name);Time.timeScale=0;
    cases.Add(new JObject{{"name",name},{"model",model},{"rootY",posed.y},{"laneError",laneError},{"headingCross",headingCross},{"initialCompression",new JArray(initialCompression)},{"up",new JArray(up.x,up.y,up.z)},{"firstSuspensionImpact",car.SuspensionImpact},{"firstSpeed",car.Body.linearVelocity.magnitude}});
    car.Recover(new Vector3(10,2,10),Quaternion.identity);Check(Vector3.Distance(car.Body.position,new Vector3(10,2.7f,10))<.00001f&&values.All(v=>v==0),"ordinary Recover retains original .7m drop/reset semantics "+name);
   }finally{Release(root);if(support)UnityEngine.Object.DestroyImmediate(support);Time.timeScale=0;}
  }
 }
}
