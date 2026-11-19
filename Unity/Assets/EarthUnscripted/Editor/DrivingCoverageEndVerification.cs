using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
 public static class DrivingCoverageEndVerification
 {
  const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;static int checks;
  static void Check(bool pass,string name){if(!pass)throw new InvalidDataException("COVERAGE END FAIL "+name);checks++;}
  sealed class Fixture:IDisposable
  {
   public readonly GameObject Root;public readonly Native Renderer;public readonly GeoRegion Region;public readonly AutomaticWorldHost Host;public readonly VehicleController Car;public readonly BoxCollider Asphalt,Ground;public readonly GeoRoad Road;
   public Fixture(string endpoint="osm:2")
   {
    Root=new GameObject("Actual dead-end coverage fixture");
    try
    {
     Renderer=Root.AddComponent<Native>();Renderer.enabled=false;typeof(Native).GetField("ready",Hidden).SetValue(Renderer,true);
     var chunk=new GameObject("Published fixture geometry");chunk.transform.SetParent(Root.transform,false);var chunks=(Dictionary<Vector2Int,GameObject>)typeof(Native).GetField("chunks",Hidden).GetValue(Renderer);for(int z=-12;z<=8;z++)for(int x=-8;x<=8;x++)chunks.Add(new Vector2Int(x,z),chunk);
     BoxCollider Box(string name,Vector3 centre,Vector3 size){var g=new GameObject(name);g.transform.SetParent(chunk.transform,false);g.transform.position=centre;VehicleObstacleWheels.MarkSuspensionSurface(g,name);var c=g.AddComponent<BoxCollider>();c.size=size;return c;}
     Asphalt=Box("Road asphalt",new Vector3(0,999.9f,-320),new Vector3(4,.2f,640));Ground=Box("Terrain",new Vector3(0,999.88f,-100),new Vector3(1000,.2f,1400));
     var areas=(Dictionary<Vector2Int,Native.Map>)typeof(Native).GetField("areas",Hidden).GetValue(Renderer);
     for(int z=-1;z<=0;z++)for(int x=-1;x<=0;x++)
     {
      var map=new Native.Map{minX=x*1536,minZ=z*1536,maxX=(x+1)*1536,maxZ=(z+1)*1536,coreMinX=x*1536,coreMinZ=z*1536,coreMaxX=(x+1)*1536,coreMaxZ=(z+1)*1536,tileX=x,tileZ=z,gridStep=1536,nx=2,nz=2,heights=new[]{1000f,1000f,1000f,1000f},features=Array.Empty<Native.Feature>(),buildings=Array.Empty<Native.Building>(),trees=Array.Empty<Native.TreeSeed>(),routes=Array.Empty<Native.DriveRoute>()};
      areas.Add(new Vector2Int(x,z),map);typeof(Native).GetMethod("IndexMap",Hidden).Invoke(Renderer,new object[]{map});
     }
     Road=new GeoRoad{id="native:fixture-service:0",NativeSourceWay="fixture-service",start="osm:1",end=endpoint,kind="service",width=4,NativeTravelWidth=4,limit=20,points=new[]{new Vector3(0,1000,-600),new Vector3(0,1000,0)}};
     Region=new GeoRegion{roads=new[]{Road},waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),route=Road.points,minX=-1536,minZ=-1536,width=3072,depth=3072,nx=2,nz=2,heights=new[]{1000f,1000f,1000f,1000f}};typeof(GeoRegion).GetField("NativeWorld",Hidden).SetValue(Region,Renderer);Region.RebuildRenderQueryIndexesOnly(true);
     var g=new GameObject("Real parked player at terminal road");g.transform.SetParent(Root.transform,false);g.layer=8;g.AddComponent<Rigidbody>();var box=g.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);var visual=g.AddComponent<CarVisual>();visual.Build(Color.blue);Car=g.AddComponent<VehicleController>();if(!Car.Body)typeof(VehicleController).GetMethod("Awake",Hidden).Invoke(Car,null);Car.Visual=visual;Car.NetworkDriven=true;Car.ParkingAutomaticEnabled=true;Car.enabled=false;Car.Body.interpolation=RigidbodyInterpolation.None;Car.ApplyTuning(new Core.OwnedCar{model=0});
     var hr=new GameObject("Actual coverage host");hr.transform.SetParent(Root.transform,false);Host=hr.AddComponent<AutomaticWorldHost>();Host.enabled=false;typeof(AutomaticWorldHost).GetProperty("Renderer").SetValue(Host,Renderer);typeof(AutomaticWorldHost).GetProperty("Region").SetValue(Host,Region);Host.Player=Car;
     Physics.SyncTransforms();Check(Car.TryInitializeSupportedRest(new Vector3(0,1000,-2.2f),Quaternion.identity,out var reason,out _),"real fixture initial rest: "+reason);
    }
    catch{UnityEngine.Object.DestroyImmediate(Root);throw;}
   }
   public void Update(){typeof(AutomaticWorldHost).GetField("nextCoverageAt",Hidden).SetValue(Host,float.NegativeInfinity);typeof(AutomaticWorldHost).GetMethod("UpdateDrivingCoverage",Hidden).Invoke(Host,null);}
   public bool[] Requirements=>(bool[])typeof(Native).GetField("immediateCorridorRoadRequired",Hidden).GetValue(Renderer);
   public void Dispose(){Geometry.ReleaseMeshes(Root);UnityEngine.Object.DestroyImmediate(Root);Physics.SyncTransforms();}
  }
  static void Physical(bool reverse)
  {
   using(var f=new Fixture(reverse?"boundary:unresolved":"osm:2"))
   {
    f.Update();if(reverse)Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0,"unresolved forward source frontier initially holds parked car");
    for(int tick=0;tick<30;tick++){f.Update();typeof(VehicleController).GetMethod("FixedUpdate",Hidden).Invoke(f.Car,null);Physics.Simulate(.01f);Physics.SyncTransforms();}
    Check(f.Car.ParkingBrakeApplied,"actual automatic parking latch engaged");var start=f.Car.Body.position;float low=start.y;
    f.Car.NetworkThrottle=reverse?0:.6f;f.Car.NetworkBrake=reverse?.8f:0;
    for(int tick=0;tick<450;tick++){f.Update();typeof(VehicleController).GetMethod("FixedUpdate",Hidden).Invoke(f.Car,null);Physics.Simulate(.01f);Physics.SyncTransforms();low=Mathf.Min(low,f.Car.Body.position.y);}
    Check(reverse?f.Car.Body.position.z<start.z-3:f.Car.Body.position.z>1,"actual car physically "+(reverse?"reverses away from guarded frontier":"leaves real asphalt end onto prepared ground"));
    Check(!f.Car.ParkingBrakeApplied&&!f.Car.StreamingBrake,"supported requested direction releases actual parking/coverage hold");Check(low>999.7f&&f.Car.Damage<.1f,"real terrain supports the complete drive without falling/damage");
   }
  }
  static void DifferentLevelGroundForecast(string kind)
  {
   using(var f=new Fixture())
   {
    bool bridge=kind=="bridge",tunnel=kind=="tunnel";float height=tunnel?992:1008;
    var crossing=new GeoRoad{id="separate-"+kind,start="osm:other-a",end="osm:other-b",kind="primary",width=8,bridge=bridge,tunnel=tunnel,layer=bridge?1:tunnel?-1:0,points=new[]{new Vector3(-100,height,160),new Vector3(100,height,160)}};f.Region.roads=new[]{f.Road,crossing};f.Region.RebuildRenderQueryIndexesOnly(true);
    var deck=new GameObject("Road asphalt");deck.transform.SetParent(f.Root.transform,false);deck.transform.position=new Vector3(0,height-.1f,160);VehicleObstacleWheels.MarkSuspensionSurface(deck,deck.name);var collider=deck.AddComponent<BoxCollider>();collider.size=new Vector3(200,.2f,8);Physics.SyncTransforms();
    var probe=new Vector3(0,1000,160);var projection=f.Region.ProjectRoadForVehicle(probe,1000);Check(projection.Road==crossing&&!projection.HasPlausibleHeight,kind+" fixture really selects a distinct-height source owner beyond the ended road's query cell");
    Check(f.Renderer.HasPreparedGroundAt(probe)&&!f.Renderer.HasDrivingSurfaceAt(probe)&&f.Renderer.HasDrivingSurfaceAt(new Vector3(0,height,160)),kind+" physical ground and deck both exist at separate heights");
    f.Update();if(bridge||tunnel){Check(!f.Requirements[20]&&float.IsPositiveInfinity(f.Car.StreamingSpeedLimit),kind+" future ground continuation does not demand unrelated-level asphalt");}else{Check(f.Requirements[20]&&!float.IsPositiveInfinity(f.Car.StreamingSpeedLimit),"ordinary height mismatch never weakens strict road re-entry");}
    if(bridge||tunnel)
    {
     collider.enabled=false;f.Car.Body.position=new Vector3(0,height+.2f,160);Physics.SyncTransforms();f.Update();Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0&&f.Requirements[0],kind+" initial source-road missing asphalt remains strictly guarded");
    }
   }
  }
  public static int Run()
  {
   checks=0;var oldMode=Physics.simulationMode;var oldScale=Time.timeScale;var oldFixed=Time.fixedDeltaTime;
   try
   {
    Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;
    Check(AutomaticWorldHost.CoverageTravelHeading(Vector3.zero,Vector3.forward,0,1,false).z<-.99f,"stationary brake intent forecasts reverse before gear transition");Check(AutomaticWorldHost.CoverageTravelHeading(Vector3.forward,Vector3.forward,0,1,false).z>.99f,"moving braking coverage follows actual momentum");Check(AutomaticWorldHost.CoverageTravelHeading(Vector3.back,Vector3.forward,1,0,true).z<-.99f,"moving reverse momentum stays guarded even with forward intent");
    using(var f=new Fixture())
    {
     f.Update();Check(!f.Car.StreamingBrake&&float.IsPositiveInfinity(f.Car.StreamingSpeedLimit),"prepared native ground endpoint is not missing asphalt");Check(f.Requirements[0]&&f.Requirements.Skip(1).Any(required=>!required),"mixed corridor retains strict road then physical ground support requirements");
     f.Ground.enabled=false;Physics.SyncTransforms();f.Update();Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0,"geometry-ready root without actual ground still stops");f.Ground.enabled=true;f.Asphalt.enabled=false;Physics.SyncTransforms();f.Update();Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0,"missing asphalt at current continuing source footprint cannot use terrain fallback");
    }
    using(var f=new Fixture("boundary:clip")){f.Update();Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0,"clipped source endpoint remains unresolved despite published ground");}
    using(var f=new Fixture()){f.Road.bridge=true;f.Update();Check(f.Car.StreamingBrake,"real deck endpoint never grants a terrain exit");}
    using(var f=new Fixture()){var areas=(Dictionary<Vector2Int,Native.Map>)typeof(Native).GetField("areas",Hidden).GetValue(f.Renderer);areas.Remove(new Vector2Int(-1,0));f.Update();Check(f.Car.StreamingBrake,"native node beside missing source ownership remains unresolved");}
    using(var f=new Fixture())
    {
     var crossing=new GeoRoad{id="continuing-crossing",start="osm:c1",end="osm:c2",kind="service",width=6,points=new[]{new Vector3(-100,1000,64),new Vector3(100,1000,64)}};f.Region.roads=new[]{f.Road,crossing};f.Region.RebuildRenderQueryIndexesOnly(true);f.Update();Check(!float.IsPositiveInfinity(f.Car.StreamingSpeedLimit),"new mapped road requires its own missing asphalt, even after legitimate ground exit");Check(f.Requirements[8],"mapped road re-entry restores per-point strict asphalt authority");
    }
    using(var f=new Fixture()){f.Car.Body.position=new Vector3(50,1000.2f,-2.2f);f.Car.ContactPoints[0]=f.Car.ContactPoints[1]=new Vector3(50,999.98f,-2.2f);f.Car.WheelGrounded[0]=f.Car.WheelGrounded[1]=true;f.Update();Check(!f.Car.StreamingBrake,"actual supported offroad travel remains drivable");f.Ground.enabled=false;Physics.SyncTransforms();f.Update();Check(f.Car.StreamingBrake,"offroad geometry registry alone cannot hide missing physical terrain");}
    using(var f=new Fixture())
    {
     f.Car.Body.position=new Vector3(0,1000.2f,3);f.Car.NetworkBrake=.8f;f.Update();Check(!f.Requirements[0],"past the true road end uses actual current ground footprint rather than clamped asphalt behind");Check(f.Requirements.Skip(1).Any(required=>required),"reverse forecast re-enters strict asphalt after real ground");f.Ground.enabled=false;Physics.SyncTransforms();f.Update();Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0,"unsupported actual footprint past road end cannot borrow asphalt behind it");
    }
    using(var f=new Fixture())
    {
     var branch=new GeoRoad{id="legal-junction-exit",start="osm:2",end="osm:3",kind="service",width=4,oneWay=1,points=new[]{new Vector3(0,1000,0),new Vector3(100,1000,0)}};f.Region.roads=new[]{f.Road,branch};f.Region.RebuildRenderQueryIndexesOnly(true);
     Check(!(bool)typeof(AutomaticWorldHost).GetMethod("KnownPreparedRoadEnd",Hidden).Invoke(f.Host,new object[]{f.Road,1}),"mapped legal junction continuation is never labelled a native terminal");
     f.Car.Body.position=new Vector3(0,1000.2f,3);f.Update();Check(f.Car.StreamingBrake&&f.Car.StreamingSpeedLimit==0,"slightly beyond section endpoint at junction cannot bypass missing continuation asphalt as terrain");
    }
    DifferentLevelGroundForecast("bridge");DifferentLevelGroundForecast("tunnel");DifferentLevelGroundForecast("ordinary");
    Physical(false);Physical(true);
    var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"-earthUnscriptedCoverageFixture");if(arg>=0)
    {
     var map=JsonUtility.FromJson<Native.Map>(File.ReadAllText(args[arg+1]));var feature=map.features.Single(f=>f.category=="road"&&f.sourceId=="173493017");Check(feature.sourceNodeIds.Last()=="osm:1842941152","actual supplied Inverness source has real terminal OSM node");Check(Vector3.Distance(feature.points.Last(),new Vector3(996.1672f,feature.points.Last().y,413.4612f))<3,"actual reported car location lies immediately before native road end");Check(map.features.Count(f=>f.category=="road"&&f.sourceNodeIds!=null&&f.sourceNodeIds.Contains("osm:1842941152"))==1,"actual source terminal has no other road continuation");Debug.Log("COVERAGE END actual explicit prepared fixture exercised");
    }
    Debug.Log("COVERAGE END PASS "+checks);return checks;
   }
   finally{Physics.simulationMode=oldMode;Time.timeScale=oldScale;Time.fixedDeltaTime=oldFixed;}
  }
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
 }
}
