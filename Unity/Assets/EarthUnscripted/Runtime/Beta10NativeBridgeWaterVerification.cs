using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted
{
 public sealed class Beta10NativeBridgeWaterVerification:MonoBehaviour
 {
  readonly List<string> errors=new List<string>(),checks=new List<string>();readonly JObject evidence=new JObject();
  string Output=>SinglePlayerBeta.VerificationOutput;
  const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
  static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Hidden).GetValue(owner);
  void Awake(){if(string.IsNullOrEmpty(Output))throw new InvalidOperationException("Isolated beta proof required");Application.logMessageReceived+=Log;}
  void OnDestroy(){Application.logMessageReceived-=Log;}
  void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
  void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
  IEnumerator Start()
  {
   string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
   while(stack.Count>0)
   {
    object current=null;bool more=false;try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception e){failure=e.ToString();}
    if(failure!=null)break;if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
   }
   evidence["passed"]=failure==null&&errors.Count==0;evidence["failure"]=failure;evidence["checks"]=new JArray(checks);evidence["errors"]=new JArray(errors);evidence["version"]=Application.version;evidence["graphics"]=GameGraphics.Name;
   File.WriteAllText(Path.Combine(Output,"result.json"),evidence.ToString());Application.Quit((bool)evidence["passed"]?0:1);
  }
  IEnumerator Exercise()
  {
   GameSession session=null;double until=Time.realtimeSinceStartupAsDouble+600;
   while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
   {
    if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Kessock normal boot");yield return null;
   }
   var host=session.World.AutomaticWorld;Check(host&&host.Ready,"Normal beta owns a ready streamed world");
   Check(Math.Abs(host.Region.latitude-57.47908)<.01&&Math.Abs(host.Region.longitude+4.22398)<.01,"Supplied seed is Inverness");
   session.Profile.performanceStats=true;session.Profile.weatherMode=0;session.Profile.fixedHour=14;session.ResumeDrive();
   until=Time.realtimeSinceStartupAsDouble+120;
   while(session.Menu||session.PreparingStart){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Kessock drive entry");yield return null;}
   session.Car.StartLocked=true;var world=host.Renderer;until=Time.realtimeSinceStartupAsDouble+120;
   while(!world.CoastalSurfaceReady||!Field<Texture2D>(world,"roadWaterMask"))
   {if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Current coastline and road clearance mask did not publish");yield return null;}
   // Allow the independent road mask to catch the latest completed geometry.
   yield return new WaitForSecondsRealtime(2);yield return new WaitForEndOfFrame();
   var mask=Field<Texture2D>(world,"roadWaterMask");var rect=Field<Vector4>(world,"roadWaterRect");
   var materials=Field<List<Material>>(world,"coastalMaterials").Where(m=>m).ToList();var inland=Field<Material>(world,"mappedWaterMaterial");if(inland)materials.Add(inland);
   Check(materials.Count>0,"Actual streamed water materials exist");
   foreach(var material in materials)Check(material.GetFloat("_RoadWaterEnabled")>.5f&&material.GetTexture("_RoadWaterMask")==mask,"Current clearance mask bound to "+material.name);
   var previous=RenderTexture.active;var target=RenderTexture.GetTemporary(mask.width,mask.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var readback=new Texture2D(mask.width,mask.height,TextureFormat.RGBAFloat,false,true);Color[] pixels;
   try{Graphics.Blit(mask,target);RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,mask.width,mask.height),0,0);readback.Apply();pixels=readback.GetPixels();}
   finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(readback);}
   var samples=new JArray();var seen=new HashSet<string>();int supported=0,covered=0,waveRisk=0,failures=0;float maxExcess=float.NegativeInfinity;Vector3 direction=Vector3.forward;
   var centre=session.Car.Body.position;evidence["carPosition"]=JObject.FromObject(new{x=centre.x,y=centre.y,z=centre.z});
   Check(Vector2.Distance(new Vector2(centre.x,centre.z),new Vector2(-27.2047f,1993.742f))<100,"Start is near the owner's Kessock regression location");
   foreach(var map in world.ResidentAreas)foreach(var road in map.features)
   {
    if(road.category!="road"||!road.bridge||road.points==null)continue;
    for(int i=1;i<road.points.Length;i+=4)
    {
     var point=(road.points[i-1]+road.points[i])*.5f;if(Vector3.ProjectOnPlane(point-centre,Vector3.up).magnitude>150||!world.HasGeometryAt(point))continue;
     string key=road.sourceId+":"+Mathf.RoundToInt(point.x)+":"+Mathf.RoundToInt(point.z);if(!seen.Add(key))continue;
     var forward=Vector3.ProjectOnPlane(road.points[i]-road.points[i-1],Vector3.up).normalized;var right=Vector3.Cross(Vector3.up,forward);direction=forward;
     foreach(float fraction in new[]{-.8f,0,.8f})
     {
      var position=point+right*(road.roadCentreOffset+road.width*.5f*fraction);float deck=float.NegativeInfinity;
      foreach(var hit in Physics.RaycastAll(position+Vector3.up*2,Vector3.down,5,~0,QueryTriggerInteraction.Ignore))
       if(hit.collider.name=="Road asphalt"&&hit.collider.transform.IsChildOf(world.transform))deck=Mathf.Max(deck,hit.point.y);
      if(float.IsNegativeInfinity(deck))continue;supported++;
      float u=(position.x-rect.x)/rect.z,v=(position.z-rect.y)/rect.w;
      if(u<0||u>1||v<0||v>1){failures++;continue;}
      var pixel=pixels[Mathf.Clamp((int)(v*mask.height),0,mask.height-1)*mask.width+Mathf.Clamp((int)(u*mask.width),0,mask.width-1)];bool masked=pixel.g>.5f;if(masked)covered++;else failures++;
      bool risk=world.MappedSeaLevel+.18f>=deck-.03f;
      if(risk){waveRisk++;float excess=pixel.r-(deck-.03f);maxExcess=Mathf.Max(maxExcess,excess);if(!masked||excess>.005f)failures++;}
      samples.Add(new JObject{["source"]=road.sourceId,["x"]=position.x,["z"]=position.z,["deckY"]=deck,["gpuMaskY"]=pixel.r,["covered"]=masked,["waveCanReachDeck"]=risk});
     }
    }
   }
   evidence["roadSamples"]=samples;evidence["supportedSamples"]=supported;evidence["coveredSamples"]=covered;evidence["waveRiskSamples"]=waveRisk;evidence["maskFailures"]=failures;evidence["seaLevel"]=world.MappedSeaLevel;evidence["materialCount"]=materials.Count;evidence["maxMaskExcess"]=float.IsNegativeInfinity(maxExcess)?0:maxExcess;
   var camera=session.View.GetComponent<Camera>();session.View.enabled=false;
   camera.transform.SetPositionAndRotation(centre+Vector3.up*3,Quaternion.LookRotation(direction+Vector3.down*.06f));
   for(int frame=0;frame<12;frame++)yield return null;
   yield return new WaitForEndOfFrame();var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
   try{File.WriteAllBytes(Path.Combine(Output,"kessock-real-bridge-water.png"),screenshot.EncodeToPNG());}finally{Destroy(screenshot);}
   Check(supported>=12,"Published bridge asphalt sampled at actual centre and carriageway edges");Check(covered==supported,"GPU clearance texture covers all physical bridge samples");Check(waveRisk>0,"Regression includes a real deck section that wave crests can reach");Check(failures==0,"GPU water cutoff stays beneath the physical road deck where crests can reach it");
   evidence["scope"]="Actual Inverness streamed bridge geometry, GPU clearance texture and live material bindings; screenshot requires visual review.";
  }
 }
}
