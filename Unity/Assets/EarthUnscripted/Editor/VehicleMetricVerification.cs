using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
 public static class VehicleMetricVerification
 {
  static int checks;static JArray cases;
  static void Check(bool value,string why){if(!value)throw new InvalidOperationException("FAIL "+why);checks++;Debug.Log("VEHICLE METRIC PASS "+why);}
  static bool Under(Transform t,Transform root)=>root&&(t==root||t.IsChildOf(root));
  static IEnumerable<Vector3> Vertices(CarVisual visual,bool bodyOnly=false)
  {foreach(var filter in visual.GetComponentsInChildren<MeshFilter>(true)){if(!filter.gameObject.activeInHierarchy||!filter.sharedMesh)continue;if(bodyOnly){if(visual.Wheels.Any(w=>Under(filter.transform,w)))continue;bool ignore=false;for(var t=filter.transform;t&&t!=visual.transform;t=t.parent)if(t.name.IndexOf("Mirror",StringComparison.OrdinalIgnoreCase)>=0||t.name.StartsWith("Fixed brake caliper",StringComparison.Ordinal))ignore=true;if(ignore)continue;}foreach(var v in filter.sharedMesh.vertices)yield return visual.transform.InverseTransformPoint(filter.transform.TransformPoint(v));}}
  static Bounds BoundsOf(IEnumerable<Vector3> points){var array=points.ToArray();if(array.Length==0)throw new InvalidOperationException("Missing geometry");var b=new Bounds(array[0],Vector3.zero);foreach(var p in array)b.Encapsulate(p);return b;}
  static string AssetHash(GameObject asset)
  {using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))foreach(var mesh in asset.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Where(m=>m).Distinct().OrderBy(m=>m.name,StringComparer.Ordinal)){writer.Write(mesh.name);foreach(var v in mesh.vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}}using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();}
  static void Release(GameObject root)
  {if(!root)return;foreach(var visual in root.GetComponentsInChildren<CarVisual>(true)){var field=typeof(CarVisual).GetField("imported",BindingFlags.NonPublic|BindingFlags.Instance);(field?.GetValue(visual) as ImportedCarAppearance)?.ReleaseMaterials();}Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}
  public static void Run()
  {
   checks=0;cases=new JArray();string output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds/Verification/VehicleMetrics",DateTime.UtcNow.Ticks.ToString());Directory.CreateDirectory(output);
   try
   {
    Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"isolated Editor data/physics fixture with no active career/session");
    var source=Resources.Load<GameObject>("Vehicles/CarConcept");Check(source,"licensed source prefab available");string before=AssetHash(source);
    for(int model=0;model<GarageRules.Catalogue.Length;model++)foreach(bool cockpit in model==0?new[]{true,false}:new[]{true})for(int style=0;style<3;style++)Measure(model,cockpit,style,style);
    Check(AssetHash(source)==before,"all source prefab mesh vertices survive builds/styles/dents unchanged");
    VerticalRole(.86f,.85f);VerticalRole(.85f,.9f);WireContract();RejectIncompatiblePackets();Settled();
    File.WriteAllText(Path.Combine(output,"receipt.json"),new JObject{{"checks",checks},{"cases",cases},{"scope","Actual Editor mesh/collider/isolated constrained suspension checks only; native free-body driving/cockpit pixel/LAN acceptance remain required"},{"careerReadOrWritten",false},{"sourcePrefabSHA256",before}}.ToString());Debug.Log("VEHICLE METRICS PASS "+checks+" "+output);EditorApplication.Exit(0);
   }
   catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
  }
  static void Measure(int model,bool cockpit,int style,int kit)
  {
   GameObject root=null;try
   {
    root=new GameObject("Owned metric case");var visual=root.AddComponent<CarVisual>();visual.Build(Color.white,cockpit,model,style,kit);var m=VehicleDimensions.ForModel(model);var body=BoundsOf(Vertices(visual,true));var all=BoundsOf(Vertices(visual));
    Check(root.transform.localScale==Vector3.one,"physical owner identity scale model"+model+" cockpit"+cockpit+" style"+style);
    Check(Mathf.Abs(body.size.x-m.BodyWidth)<.03f,"actual shell/trim width model"+model+" style"+style);
    Check(Mathf.Abs(all.size.x-m.MirrorSpan)<.025f,"actual full mirror span model"+model+" style"+style);
    Check(Mathf.Abs(body.size.z-m.BodyLength)<.035f,"actual original body length model"+model+" style"+style);
    Check(Mathf.Abs(body.max.y-m.RoofLocalY)<.015f,"actual preserved or explicit proxy roof model"+model+" cockpit"+cockpit);
    Check(Mathf.Abs(visual.Wheels[2].localPosition.x-visual.Wheels[0].localPosition.x-m.Track)<.0001f&&Mathf.Abs(visual.Wheels[1].localPosition.z-visual.Wheels[0].localPosition.z-m.Wheelbase)<.0001f,"actual four-wheel track/original wheelbase model"+model);
    for(int i=0;i<4;i++)
    {
     var wheel=visual.Wheels[i];var vertices=wheel.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.gameObject.activeInHierarchy&&f.sharedMesh).SelectMany(f=>f.sharedMesh.vertices.Select(v=>wheel.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();var radial=vertices.Max(v=>new Vector2(v.y,v.z).magnitude);var width=vertices.Max(v=>v.x)-vertices.Min(v=>v.x);var bins=new float[16];foreach(var v in vertices){int bin=Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(v.z,v.y)+Mathf.PI)/(2*Mathf.PI)*16),0,15);bins[bin]=Mathf.Max(bins[bin],new Vector2(v.y,v.z).magnitude);}Check(Mathf.Abs(radial-m.Radius)<.005f&&bins.All(v=>v>m.Radius-.01f)&&width<m.TyreWidth+.016f,"actual wheel radial/roundness/axial bounds model"+model+" wheel"+i+" style"+style);
    }
    if(visual.Wipers!=null)
    {
     var glass=visual.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.gameObject.activeInHierarchy&&(f.name=="Windscreen"||f.name=="BodyWindshield"));if(glass){var bounds=BoundsOf(glass.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(glass.transform.TransformPoint(v))));foreach(float angle in new[]{-76f,0f,30f})foreach(var w in visual.Wipers){w.localRotation=Quaternion.Euler(0,0,angle);var b=BoundsOf(w.GetComponentsInChildren<MeshFilter>().SelectMany(f=>f.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(f.transform.TransformPoint(v)))));Check(b.min.x>=bounds.min.x+.005f&&b.max.x<=bounds.max.x-.005f,"wiper lateral sweep stays within narrowed glass model"+model);}}
    }
    var saved=new SavedDent{x=1.0f,y=.65f,z=0,nx=-1,severity=8};var dents=new List<SavedDent>{saved};visual.RestoreDents(dents);Check(saved.x==1&&saved.y==.65f&&dents.Count==1&&ReferenceEquals(visual.RecordedDents[0],saved),"legacy dent replay preserves caller record model"+model);visual.Repair();Check(visual.RecordedDents.Count==0,"repair clears generated deformation records model"+model);
    cases.Add(new JObject{{"model",model},{"cockpit",cockpit},{"wheelStyle",style},{"kit",kit},{"bodyWidth",body.size.x},{"mirrors",all.size.x},{"length",body.size.z},{"roofLocalY",body.max.y}});
   }finally{Release(root);}
  }
  static void VerticalRole(float centreY,float height)
  {var root=new GameObject("Owned role collider");try{typeof(GameSession).GetMethod("AddBootCollider",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root,centreY<.86f});var box=root.GetComponent<BoxCollider>();Check(box.center.y==centreY&&box.size.y==height&&Mathf.Abs(box.size.x-1.8f)<.0001f&&Mathf.Abs(box.center.z-.1483656f)<.0001f,"role vertical collider preserved centre"+centreY+" height"+height);}finally{UnityEngine.Object.DestroyImmediate(root);}}
  static void WireContract()
  {
   var packet=typeof(LanSession).GetNestedType("Packet",BindingFlags.NonPublic);var value=Activator.CreateInstance(packet,true);packet.GetField("type").SetValue(value,"hello");var encode=typeof(LanSession).GetMethod("Encode",BindingFlags.NonPublic|BindingFlags.Static);string text=(string)encode.Invoke(null,new[]{value});Check((int)JObject.Parse(text)["protocol"]==11&&(string)JObject.Parse(text)["vehicleRecipe"]==VehicleDimensions.Recipe,"actual outbound packet pins metric vehicle recipe");var missing=JsonUtility.FromJson("{\"protocol\":11,\"type\":\"hello\"}",packet);Check(packet.GetField("vehicleRecipe").GetValue(missing)==null,"absent incoming recipe cannot inherit outbound initializer and bypass mismatch guard");
   var state=typeof(LanSession).GetNestedType("State",BindingFlags.NonPublic);var pose=Activator.CreateInstance(state,true);state.GetField("rotation").SetValue(pose,Quaternion.identity);state.GetField("dents").SetValue(pose,new List<SavedDent>());state.GetField("wheelY").SetValue(pose,new[]{.247f,.247f,.247f,.247f});var valid=typeof(LanSession).GetMethod("Valid",BindingFlags.NonPublic|BindingFlags.Static);Check((bool)valid.Invoke(null,new[]{pose}),"actual LAN car state accepts four bounded wheel suspension poses");state.GetField("wheelY").SetValue(pose,new[]{float.NaN,.247f,.247f,.247f});Check(!(bool)valid.Invoke(null,new[]{pose}),"actual LAN car state refuses NaN wheel pose before application");state.GetField("wheelY").SetValue(pose,new[]{.247f});Check(!(bool)valid.Invoke(null,new[]{pose}),"actual LAN car state refuses truncated wheel pose array");
  }
  static void RejectIncompatiblePackets()
  {
   // Unconnected local socket: no bind/connect/send/receive, no network traffic.
   // Exercise actual Receive before dispatch, not a mirrored protocol predicate.
   bool background=Application.runInBackground;var type=typeof(LanSession);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var receive=type.GetMethod("Receive",flags);var peer=type.GetField("peer",flags);var pending=type.GetField("pendingHello",flags);var notice=type.GetField("disconnectNotice",flags);
   foreach(bool hosting in new[]{true,false})foreach(var bad in new[]{new JObject{{"protocol",9},{"vehicleRecipe",VehicleDimensions.Recipe}},new JObject{{"protocol",10},{"vehicleRecipe",VehicleDimensions.Recipe}},new JObject{{"protocol",11}},new JObject{{"protocol",11},{"vehicleRecipe","old-width-recipe"}},new JObject{{"vehicleRecipe",VehicleDimensions.Recipe}}})
   {
    GameObject root=null;LanWire wire=null;try
    {
     root=new GameObject("Owned incompatible peer fixture");var session=root.AddComponent<LanSession>();type.GetProperty("Hosting").SetValue(session,hosting);bad["type"]=hosting?"hello":"welcome";
     wire=new LanWire(new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Tcp));peer.SetValue(session,wire);
     int visuals=UnityEngine.Object.FindObjectsByType<CarVisual>(FindObjectsInactive.Include).Length;
     receive.Invoke(session,new object[]{bad.ToString(Newtonsoft.Json.Formatting.None)});
     Check(!wire.Open&&!session.Joined&&!session.Other&&pending.GetValue(session)==null&&(string)notice.GetValue(session)=="Incompatible game version"&&UnityEngine.Object.FindObjectsByType<CarVisual>(FindObjectsInactive.Include).Length==visuals,"actual incompatible "+bad["type"]+" rejected before sync/actor creation: "+bad.ToString(Newtonsoft.Json.Formatting.None));
    }finally{wire?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);Application.runInBackground=background;}
   }
  }
  static void Settled()
  {
   var mode=Physics.simulationMode;float scale=Time.timeScale,fixedStep=Time.fixedDeltaTime;GameObject floor=null,root=null;
   try
   {
    Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;floor=new GameObject("Owned metric floor");floor.transform.position=new Vector3(0,-.5f,0);floor.AddComponent<BoxCollider>().size=new Vector3(100,1,100);root=new GameObject("Owned metric physics");root.layer=8;root.transform.position=new Vector3(0,.7f,0);var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);var visual=root.AddComponent<CarVisual>();visual.Build(Color.white,true);var car=root.AddComponent<VehicleController>();typeof(VehicleController).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(car,null);car.NetworkDriven=true;car.NetworkHandbrake=1;car.Visual=visual;car.ApplyTuning(new OwnedCar());car.Body.constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ|RigidbodyConstraints.FreezeRotation;car.Body.interpolation=RigidbodyInterpolation.None;Physics.SyncTransforms();var fixedUpdate=typeof(VehicleController).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);for(int i=0;i<1000;i++){fixedUpdate.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();}var local=BoundsOf(Vertices(visual));float roof=local.max.y+root.transform.position.y,bottom=local.min.y+root.transform.position.y;
    Check(car.WheelGrounded.All(x=>x)&&car.Body.linearVelocity.magnitude<.005f,"actual10s suspension settles with four contacts and no brake-reverse input");Check(Mathf.Abs(root.transform.position.y-.112872f)<.004f&&roof>1.24f&&roof<1.28f,"actual settled stock roof/rootY baseline preserved");Check(bottom>=-.008f&&bottom<.015f,"normalized visible tyres contact floor instead of source22mm penetration");Check(car.Visual.Wheels.All(w=>Mathf.Abs(w.position.y-.36f)<.004f),"actual tyre centres match.36physics radius");cases.Add(new JObject{{"settledRootY",root.transform.position.y},{"roofAboveGround",roof},{"tireMinimum",bottom},{"contacts",car.WheelGrounded.Count(x=>x)}});
   }finally{Release(root);if(floor)UnityEngine.Object.DestroyImmediate(floor);Physics.simulationMode=mode;Time.timeScale=scale;Time.fixedDeltaTime=fixedStep;}
  }
 }
}
