using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Clipper2Lib;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class LocalFeedbackVerification
 {
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
  public static void Run()
  {
   int count=0;void Check(bool value,string text){if(!value)throw new Exception(text);count++;}
   string before=PlayerProfile.StorageOverride;var folder=Path.GetFullPath("Builds/Verification/local-feedback-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(folder);PlayerProfile.StorageOverride=Path.Combine(folder,"profile.json");
   try{
    var p=new PlayerProfile();string json=JsonUtility.ToJson(p).Replace("\"engineVolume\":0.35,","");File.WriteAllText(PlayerProfile.StorageOverride,json);p=PlayerProfile.Load();Check(Mathf.Abs(p.engineVolume-.35f)<.001f,"Old saves receive quieter engine default");p.engineVolume=0;Check(p.Save(),"Engine setting saves");p=PlayerProfile.Load();Check(p.engineVolume==0&&Mathf.Abs(p.volume-.65f)<.001f,"Engine mute persists independently of master");
    p.credits=1200;p.QueueAutosave();p.credits=4321;Check(p.Save(),"Manual save supersedes queued save");Thread.Sleep(100);Check(PlayerProfile.Load().credits==4321,"Queued autosave cannot overwrite newer manual progress");
   }finally{PlayerProfile.StorageOverride=before;}
   foreach(var item in new[]{(1280,720),(1920,1080),(2560,1440),(3456,2160)})Check(DisplayResolutionMenu.Valid(item.Item1,item.Item2,3456,2160),"Supported display dimensions");Check(!DisplayResolutionMenu.Valid(0,720,3456,2160)&&!DisplayResolutionMenu.Valid(9999,9999,3456,2160),"Invalid custom size rejected");
   NewbridgeWorld.Feature Road(string id,Vector3 b,string end)=>new NewbridgeWorld.Feature{id=id,sourceId=id,category="road",kind="primary",width=6,travelWidth=6,level="0",points=new[]{Vector3.zero,b},sourceNodeIds=new[]{"osm:join","osm:"+end}};
   var a=Road("east",new Vector3(30,0,0),"east");var b=Road("north",new Vector3(0,0,30),"north");var map=new NewbridgeWorld.Map{features=new[]{a,b}};
   bool Covered(System.Collections.Generic.List<NewbridgeWorld.PavingPatch> patches,double x,double z)=>patches.Any(p=>!p.island&&Clipper.PointInPolygon(new PointD(x,z),p.outline,3)!=PointInPolygonResult.IsOutside);
   var patches=NewbridgeWorld.PlanJunctionPaving(map);Check(Covered(patches,4,4),"Rounded corner adds usable tarmac beyond straight road ribbons");Check(!Covered(patches,6,6),"Corner radius remains bounded");
   var sample=patches.First(p=>!p.island);sample.Contains(0,0);long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)sample.Contains(i*.001f,i*.0007f);Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Repeated paving containment does not allocate");
   bool identical=true;for(int i=0;i<500;i++){float x=(i%23)*.317f-3,z=(i%19)*.281f-2;if(sample.Contains(x,z)!=(Clipper.PointInPolygon(new PointD(x,z),sample.outline,3)!=PointInPolygonResult.IsOutside))identical=false;}Check(identical,"Cached integer containment preserves Clipper millimetre precision");
   map.buildings=new[]{new NewbridgeWorld.Building{points=new[]{new Vector3(3.5f,0,3.5f),new Vector3(6,0,3.5f),new Vector3(6,0,6),new Vector3(3.5f,0,6)}}};Check(!Covered(NewbridgeWorld.PlanJunctionPaving(map),4,4),"Buildings block junction expansion");JunctionPavingVerification.Run();
   a.points=new[]{new Vector3(-8,.1f,0),new Vector3(8,.1f,0)};a.width=8;var mask=NewbridgeWorld.RasterRoadWaterMask(new[]{a},new Vector4(-16,-16,32,32),128);int centre=(64*128+64)*2;Check(mask[centre+1]==1&&Mathf.Abs(mask[centre]-.02f)<.001f,"Water clearance follows low road deck");Check(mask[0+1]==0,"Water remains outside road footprint");Check(.18f>mask[centre]&&-.18f<mask[centre],"Wave crest reaches exclusion threshold; trough remains below deck");
   var root=new GameObject("Numeric sign lifecycle");try{var sign=StandardTrafficSigns.Place(root.transform,"speed","30","GB");var renderer=sign.transform.Find("Front legend").GetComponent<MeshRenderer>();Check(renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount>50,"Speed digits are vector geometry");Check(renderer.sharedMaterial.shader.name!="EarthUnscripted/SignLegend","Speed numerals do not sample a font atlas");UnityEngine.Object.DestroyImmediate(sign);var other=StandardTrafficSigns.Place(root.transform,"speed","30","GB");Check(other.transform.Find("Front legend").GetComponent<MeshFilter>().sharedMesh,"Shared master survives clone unload");}finally{UnityEngine.Object.DestroyImmediate(root);}
   Debug.Log("LOCAL FEEDBACK PASS "+count);
  }
 }
}
