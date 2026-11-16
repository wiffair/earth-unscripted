using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using UnityEngine;using RoadRush.Core;
namespace RoadRush
{
 // Opt-in proof only: reads actual actors/decoded packets, never changes simulation.
 public sealed class LanVehicleMetricEvidence
 {
  [Serializable]public sealed class Actor
  {public string role;public int model;public float bodyWidth,mirrorWidth,length,colliderWidth,colliderLength,colliderCentreZ,track,wheelbase;public float[] radii,widths;public bool replica,kinematic;}
  [Serializable]public sealed class Sample
  {public float stamp;public float[] host,guest;}
  [Serializable]public sealed class Receipt
  {public string recipe,role,limitations;public int protocol=11,samples,failures,sent,received,interpolationChecks,overflow,repeatedFrameCalls;public float maxInterpolationResidual,wheelRange;public Actor[] actors;public List<Sample> packets=new List<Sample>();}
  public readonly Receipt Data=new Receipt();
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;const int Limit=2048;
  static readonly FieldInfo Latest=typeof(LanSession).GetField("latest",Private),NextSend=typeof(LanSession).GetField("nextSend",Private);
  float lastNextSend,lastStamp=-1;int lastObservedFrame=-1;float[] previousHost,previousGuest;Vector3 previousHostPosition,previousGuestPosition;bool initialized;
  readonly float[] low=Enumerable.Repeat(float.PositiveInfinity,8).ToArray(),high=Enumerable.Repeat(float.NegativeInfinity,8).ToArray();
  static float[] Wheels(VehicleController car)=>car.Visual.Wheels.Select(w=>w.localPosition.y).ToArray();
  static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
  static bool Under(Transform t,Transform owner)=>t==owner||t.IsChildOf(owner);
  static Actor Measure(VehicleController car,string role)
  {
   var visual=car.Visual;var m=VehicleDimensions.ForModel(visual.Model);var body=new List<Vector3>();var all=new List<Vector3>();
   foreach(var f in visual.GetComponentsInChildren<MeshFilter>(true))
   {
    if(!f.sharedMesh||!f.gameObject.activeInHierarchy)continue;bool excluded=visual.Wheels.Any(w=>Under(f.transform,w));for(var t=f.transform;t&&t!=visual.transform;t=t.parent)excluded|=t.name.IndexOf("Mirror",StringComparison.OrdinalIgnoreCase)>=0||t.name.StartsWith("Fixed brake caliper",StringComparison.Ordinal);
    foreach(var p in f.sharedMesh.vertices){var v=visual.transform.InverseTransformPoint(f.transform.TransformPoint(p));all.Add(v);if(!excluded)body.Add(v);}
   }
   if(body.Count==0||all.Count==0)throw new InvalidOperationException("No actual car geometry");var box=car.GetComponent<BoxCollider>();
   var radii=new float[4];var widths=new float[4];for(int i=0;i<4;i++){var w=visual.Wheels[i];var points=w.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.gameObject.activeInHierarchy).SelectMany(f=>f.sharedMesh.vertices.Select(p=>w.InverseTransformPoint(f.transform.TransformPoint(p)))).ToArray();radii[i]=points.Max(v=>new Vector2(v.y,v.z).magnitude);widths[i]=points.Max(v=>v.x)-points.Min(v=>v.x);}
   var a=new Actor{role=role,model=visual.Model,replica=car.Replica,kinematic=car.Body.isKinematic,bodyWidth=body.Max(v=>v.x)-body.Min(v=>v.x),mirrorWidth=all.Max(v=>v.x)-all.Min(v=>v.x),length=body.Max(v=>v.z)-body.Min(v=>v.z),colliderWidth=box.size.x,colliderLength=box.size.z,colliderCentreZ=box.center.z,track=visual.Wheels[2].localPosition.x-visual.Wheels[0].localPosition.x,wheelbase=visual.Wheels[1].localPosition.z-visual.Wheels[0].localPosition.z,radii=radii,widths=widths};
   // Dented panels may shrink; mirror span/track/tyres/collider are measured independently.
   if(visual.transform.localScale!=Vector3.one||Mathf.Abs(a.colliderWidth-m.ColliderWidth)>.001f||Mathf.Abs(a.colliderLength-m.ColliderLength)>.001f||Mathf.Abs(a.colliderCentreZ-m.ColliderCentreZ)>.001f||Mathf.Abs(a.track-m.Track)>.001f||Mathf.Abs(a.wheelbase-m.Wheelbase)>.001f||Mathf.Abs(a.mirrorWidth-m.MirrorSpan)>.035f||a.bodyWidth>m.BodyWidth+.04f||a.bodyWidth<m.BodyWidth-.3f||radii.Any(r=>Mathf.Abs(r-m.Radius)>.006f)||widths.Any(w=>w>m.TyreWidth+.016f))throw new InvalidOperationException("Actual actor metric mismatch "+role+": "+JsonUtility.ToJson(a));
   return a;
  }
  static float[] FieldWheels(object packet,string name){var state=packet.GetType().GetField(name).GetValue(packet);return state==null?null:(float[])state.GetType().GetField("wheelY").GetValue(state);}
  static Vector3 FieldPosition(object packet,string name){var state=packet.GetType().GetField(name).GetValue(packet);return (Vector3)state.GetType().GetField("position").GetValue(state);}
  void Keep(float stamp,float[] host,float[] guest)
  {if(Data.packets.Count>=Limit){Data.overflow++;return;}if(host==null||guest==null||host.Length!=4||guest.Length!=4||host.Concat(guest).Any(y=>!Finite(y)||y<.14f||y>.96f)){Data.failures++;return;}Data.packets.Add(new Sample{stamp=stamp,host=(float[])host.Clone(),guest=(float[])guest.Clone()});for(int i=0;i<8;i++){float y=i<4?host[i]:guest[i-4];low[i]=Mathf.Min(low[i],y);high[i]=Mathf.Max(high[i],y);}}
  void CheckInterpolation(VehicleController car,float[] before,Vector3 beforePosition,float[] target,Vector3 position)
  {if(before==null||target==null)return;float blend=Vector3.Distance(beforePosition,position)>15?1:1-Mathf.Exp(-Time.unscaledDeltaTime*22);var after=Wheels(car);for(int i=0;i<4;i++){float residual=Mathf.Abs(after[i]-Mathf.Lerp(before[i],target[i],blend));Data.maxInterpolationResidual=Mathf.Max(Data.maxInterpolationResidual,residual);if(residual>.00015f)Data.failures++;Data.interpolationChecks++;}}
  public void Observe(GameSession session)
  {
   if(!session.Network.Joined||!session.Network.Other||session.Network.RttSamples<10)return;
   // Finish may request the final sample from the same driver Update. The
   // replica applies once per frame, so its interpolation must be checked once.
   if(lastObservedFrame==Time.frameCount){Data.repeatedFrameCalls++;return;}lastObservedFrame=Time.frameCount;
   try
   {
    bool host=session.Network.Hosting;var hostCar=host?session.Car:session.Network.Other;var guestCar=host?session.Network.Other:session.Car;
    if(!initialized){Data.role=host?"host":"client";Data.recipe=VehicleDimensions.Recipe;Data.actors=new[]{Measure(hostCar,"host-car"),Measure(guestCar,"guest-car")};if(hostCar.Replica==host||guestCar.Replica==host||hostCar.Body.isKinematic==host||guestCar.Body.isKinematic==host)Data.failures++;initialized=true;lastNextSend=(float)NextSend.GetValue(session.Network);}
    Data.samples++;
    if(host){float next=(float)NextSend.GetValue(session.Network);if(next!=lastNextSend){Keep(Time.unscaledTime,Wheels(hostCar),Wheels(guestCar));Data.sent++;lastNextSend=next;}}
    else
    {
     var packet=Latest.GetValue(session.Network);if(packet==null){Data.failures++;return;}var t=packet.GetType();if((int)t.GetField("protocol").GetValue(packet)!=11||(string)t.GetField("vehicleRecipe").GetValue(packet)!=VehicleDimensions.Recipe)Data.failures++;
     float stamp=(float)t.GetField("hostStamp").GetValue(packet);var h=FieldWheels(packet,"host");var g=FieldWheels(packet,"guest");
     if(lastStamp>=0){CheckInterpolation(hostCar,previousHost,previousHostPosition,h,FieldPosition(packet,"host"));CheckInterpolation(guestCar,previousGuest,previousGuestPosition,g,FieldPosition(packet,"guest"));}
     if(stamp!=lastStamp){Keep(stamp,h,g);Data.received++;lastStamp=stamp;}
     previousHost=Wheels(hostCar);previousGuest=Wheels(guestCar);previousHostPosition=hostCar.Body.position;previousGuestPosition=guestCar.Body.position;
    }
   }catch(Exception e){if(Data.failures<4)Debug.LogError("LAN vehicle proof: "+e);Data.failures++;}
  }
  public bool Finish(GameSession session,string path)
  {
   Observe(session);Data.wheelRange=Enumerable.Range(0,8).Where(i=>Finite(low[i])&&Finite(high[i])).Select(i=>high[i]-low[i]).DefaultIfEmpty(0).Max();
   Data.limitations="Opt-in read-only actual actor and packet observations. Host authority simulates both cars; client is kinematic. Compare common hostStamp packet wheel arrays between BOTH receipts for transport equality. Per-client-frame interpolation is checked independently against previous actual wheel pose. Body width allows existing dent shrinkage; full roundness/style/free-body/native terrain acceptance is separate. No independent guest physics or Windows claim.";
   // The client intentionally finishes first. Stop() clears Hosting on the
   // surviving host; classify this receipt by its observed joined role.
   bool pass=initialized&&Data.samples>100&&Data.failures==0&&Data.overflow==0&&Data.wheelRange>.001f&&(Data.role=="host"?Data.sent>100:Data.role=="client"&&Data.received>100&&Data.interpolationChecks>800);File.WriteAllText(path,JsonUtility.ToJson(Data,true));return pass;
  }
 }
}
