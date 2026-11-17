using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
 public sealed partial class NewbridgeWorld
 {
  int roadWaterSourceRevision,roadWaterRevision=-1;Texture2D roadWaterMask;Vector4 roadWaterRect;Vector3 roadWaterCentre=Vector3.positiveInfinity;
  void ApplyRoadWaterMask(Material material){if(!material||!roadWaterMask)return;material.SetTexture("_RoadWaterMask",roadWaterMask);material.SetVector("_RoadWaterRect",roadWaterRect);material.SetFloat("_RoadWaterEnabled",1);}
  public static float[] RasterRoadWaterMask(Feature[] roads,Vector4 bounds,int size)
  {
   var values=new float[size*size*2];float step=bounds.z/size,padding=step*.72f;
   foreach(var f in roads)for(int i=1;i<f.points.Length;i++)
   {
    var a=f.points[i-1];var b=f.points[i];var delta=new Vector2(b.x-a.x,b.z-a.z);float squared=delta.sqrMagnitude;if(squared<.0001f)continue;
    var side=new Vector2(delta.y,-delta.x).normalized*f.roadCentreOffset;a.x+=side.x;a.z+=side.y;b.x+=side.x;b.z+=side.y;
    float half=f.width*.5f+padding;
    int x0=Mathf.Max(0,Mathf.FloorToInt((Mathf.Min(a.x,b.x)-half-bounds.x)/step)),x1=Mathf.Min(size-1,Mathf.CeilToInt((Mathf.Max(a.x,b.x)+half-bounds.x)/step));
    int z0=Mathf.Max(0,Mathf.FloorToInt((Mathf.Min(a.z,b.z)-half-bounds.y)/step)),z1=Mathf.Min(size-1,Mathf.CeilToInt((Mathf.Max(a.z,b.z)+half-bounds.y)/step));
    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
    {
     var p=new Vector2(bounds.x+(x+.5f)*step-a.x,bounds.y+(z+.5f)*step-a.z);float t=Mathf.Clamp01(Vector2.Dot(p,delta)/squared);if((p-delta*t).sqrMagnitude>half*half)continue;
     int at=(z*size+x)*2;float height=Mathf.Lerp(a.y,b.y,t)-.08f;if(values[at+1]<.5f||height<values[at])values[at]=height;values[at+1]=1;
    }
   }
   return values;
  }
  // Keep road clearance independent of network coastline fetches and seabed
  // rebuilding. An optional scenery provider must never hold this safety mask.
  IEnumerator RoadWaterClearanceLoop()
  {
   while(ready)
   {
    if(roadWaterRevision!=roadWaterSourceRevision||Vector3.ProjectOnPlane(observer-roadWaterCentre,Vector3.up).sqrMagnitude>128*128)
     yield return RefreshRoadWaterMask();
    yield return new WaitForSecondsRealtime(.5f);
   }
  }
  IEnumerator RefreshRoadWaterMask()
  {
   if(!SystemInfo.SupportsTextureFormat(TextureFormat.RGFloat))yield break;
   const int size=512;const float span=2048;var centre=observer;int revision=roadWaterSourceRevision;var rect=new Vector4(centre.x-span*.5f,centre.z-span*.5f,span,span);
   var roads=areas.Values.SelectMany(m=>m.features).Where(f=>f.category=="road"&&!f.tunnel).ToArray();var task=Task.Run(()=>RasterRoadWaterMask(roads,rect,size));while(!task.IsCompleted)yield return null;if(task.IsFaulted)yield break;
   // Junction fitting can lower the rendered deck relative to the raw way.
   // Match its actual height field, rather than masking only the source line.
   var values=task.Result;var slice=System.Diagnostics.Stopwatch.StartNew();float step=span/size;
   for(int z=0;z<size;z++)for(int x=0;x<size;x++)
   {
    int at=(z*size+x)*2;if(values[at+1]<.5f)continue;
    float east=rect.x+(x+.5f)*step,north=rect.y+(z+.5f)*step;
    // Fitted surfaces belong to resident cores. Retain the conservative raw
    // raster in planning-only halos; a stale workingMap is not their owner.
    if(AreaAt(east,north)==null)continue;
    var nearby=Nearby(east,north);
    if(nearby!=null)foreach(var segment in nearby)
    {
     var feature=segment.feature;if(feature.category!="road"||feature.tunnel)continue;
     float distance=Project(segment,east,north,out float raw);
     if(distance>feature.width*.5f+Mathf.Abs(feature.roadCentreOffset)+step*.72f||Mathf.Abs(raw-values[at]-.08f)>2.5f)continue;
     float surface=RoadSurfacePoint(feature,new Vector3(east,raw,north)).y-.08f;
     values[at]=Mathf.Min(values[at],surface);
    }
    if(slice.Elapsed.TotalMilliseconds>1.5){RecordGenerationSlice("Road water clearance",slice.Elapsed.TotalMilliseconds);yield return null;slice.Restart();}
   }
   var texture=new Texture2D(size,size,TextureFormat.RGFloat,false,true){name="Road clearance for animated water",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Point};texture.SetPixelData(values,0);texture.Apply(false,true);
   var old=roadWaterMask;roadWaterMask=texture;roadWaterRect=rect;roadWaterCentre=centre;roadWaterRevision=revision;foreach(var material in coastalMaterials)ApplyRoadWaterMask(material);ApplyRoadWaterMask(mappedWaterMaterial);if(old)Destroy(old);
  }
 }
}
