using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
 public sealed partial class NewbridgeWorld
 {
  public bool TryRoadSoundSurface(Vector3 contact,out string surface,out string smoothness)
  {
   surface=smoothness=null;var nearby=Nearby(contact.x,contact.z);if(nearby==null)return false;
   float best=float.MaxValue;bool found=false;
   foreach(var segment in nearby)
   {
    if(segment.feature.category!="road"&&segment.feature.category!="path")continue;
    float distance=Project(segment,contact.x,contact.z,out float height);
    if(distance>segment.feature.width*.5f+.6f||Mathf.Abs(height-contact.y)>1.5f||distance>=best)continue;
    best=distance;surface=segment.feature.surface;smoothness=segment.feature.smoothness;found=true;
   }
   return found;
  }
 }
}
