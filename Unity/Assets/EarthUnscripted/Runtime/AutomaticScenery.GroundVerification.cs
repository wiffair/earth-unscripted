using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
 public sealed partial class AutomaticScenery
 {
  [Serializable] public sealed class TreeGroundEvidence
  {public int samples,unsupported;public float maximumBaseGap;public List<TreeGroundSample> points=new List<TreeGroundSample>();}
  [Serializable] public sealed class TreeGroundSample{public float x,z,baseY,groundY,gap;public bool supported;}
  public TreeGroundEvidence InspectPublishedTreeGrounding()
  {
   if(!captureGrounding)throw new InvalidOperationException("Explicit isolated tree grounding proof required");
   var evidence=new TreeGroundEvidence();
   foreach(var record in groundingProof)
   {
    float baseY=record.far.MultiplyPoint3x4(Vector3.zero).y;
    bool supported=host.Renderer.TryPublishedSceneryGround(record.source.x,record.source.z,out float ground);
    float gap=Mathf.Abs(baseY-ground);evidence.samples++;if(!supported)evidence.unsupported++;
    if(supported)evidence.maximumBaseGap=Mathf.Max(evidence.maximumBaseGap,gap);
    evidence.points.Add(new TreeGroundSample{x=record.source.x,z=record.source.z,baseY=baseY,groundY=ground,supported=supported,gap=gap});
   }
   return evidence;
  }
 }
}
