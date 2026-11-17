using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class TrafficScarcityVerification
 {
  const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  static int checks;
  static void Check(bool pass,string why){if(!pass)throw new Exception("TRAFFIC SCARCITY "+why);checks++;}
  static void Set(object owner,string name,object value)=>owner.GetType().GetField("<"+name+">k__BackingField",Hidden).SetValue(owner,value);
  static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Hidden).Invoke(owner,args);
  static void UnexpectedDeferredInvocation<T>(T site){throw new Exception("Deferred factory must not run during ownership invalidation");}
  static GeoRoad Road(string id,float x)=>new GeoRoad{id=id,start=id+"a",end=id+"b",kind="residential",width=6.8f,NativeTravelWidth=6.8f,oneWay=1,limit=30,points=new[]{new Vector3(x,1000,-312),new Vector3(x,1000,312)}};
  public static int Run()
  {
   checks=0;var owned=new List<GameObject>();var random=UnityEngine.Random.state;GameSession session=null;
   try
   {
    var sr=new GameObject("Inactive isolated scarcity session");sr.SetActive(false);owned.Add(sr);session=sr.AddComponent<GameSession>();
    var wr=new GameObject("Inactive isolated world owner");wr.SetActive(false);owned.Add(wr);var world=wr.AddComponent<WorldBuilder>();var host=wr.AddComponent<AutomaticWorldHost>();
    var rr=new GameObject("Actual supported road renderer");owned.Add(rr);var renderer=rr.AddComponent<NewbridgeWorld>();typeof(NewbridgeWorld).GetField("ready",Hidden).SetValue(renderer,true);
    var pavement=new GameObject("Road asphalt");pavement.transform.SetParent(rr.transform);var collider=pavement.AddComponent<BoxCollider>();collider.center=new Vector3(0,999.5f,0);collider.size=new Vector3(400,1,700);
    var chunks=(Dictionary<Vector2Int,GameObject>)typeof(NewbridgeWorld).GetField("chunks",Hidden).GetValue(renderer);for(int x=-4;x<=4;x++)for(int z=-6;z<=6;z++)chunks[new Vector2Int(x,z)]=rr;
    var road=Road("first",150);var region=new GeoRegion{drivingSide=-1,roads=new[]{road},route=road.points,waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),minX=-500,minZ=-500,width=1000,depth=1000,nx=2,nz=2,heights=new[]{1000f,1000f,1000f,1000f}};
    region.RebuildRenderQueryIndexesOnly(true);typeof(GeoRegion).GetField("NativeWorld",Hidden).SetValue(region,renderer);Set(world,"Route",new RoutePath(region));Set(world,"AutomaticWorld",host);Set(host,"Renderer",renderer);Set(host,"Region",region);Set(session,"World",world);
    var pr=new GameObject("Isolated physical player");owned.Add(pr);pr.layer=8;pr.AddComponent<Rigidbody>();var player=pr.AddComponent<VehicleController>();if(!Application.isPlaying)typeof(VehicleController).GetMethod("Awake",Hidden).Invoke(player,null);player.Body.isKinematic=true;player.Body.position=new Vector3(0,1001,0);Set(session,"Car",player);Physics.SyncTransforms();
    var sites=(IList)Call(session,"AutomaticTrafficSites",player,27);Check(sites.Count==25,"real supported candidate search returns25 without aborting27slot request");
    var positions=new List<Vector3>();foreach(var item in sites){var point=(Vector3)item.GetType().GetField("Point").GetValue(item);Check(Vector3.Distance(point,player.Body.position)>=90,"candidate excludes player start");Check(positions.All(previous=>Vector3.Distance(previous,point)>=22),"candidate separation retained");positions.Add(point);}
    collider.enabled=false;Check(((IList)Call(session,"AutomaticTrafficSites",player,27)).Count==0,"zero supported sites returns empty rather than aborting world");
    var allocate=typeof(GameSession).GetMethod("InitialAutomaticTrafficCount",BindingFlags.Static|BindingFlags.NonPublic);
    foreach(var test in new[]{(sites:0,traffic:0,police:0),(sites:1,traffic:0,police:1),(sites:2,traffic:1,police:1),(sites:3,traffic:2,police:1),(sites:25,traffic:24,police:1),(sites:27,traffic:24,police:1),(sites:30,traffic:24,police:1)})
    {int traffic=(int)allocate.Invoke(null,new object[]{test.sites});int police=Math.Min(1,Math.Max(0,test.sites-traffic));Check(traffic==test.traffic&&police==test.police,"production site allocation preserves patrol priority at "+test.sites);Check(traffic+police<=test.sites,"allocation never invents supported space");}
    var queue=typeof(GameSession).GetField("deferredAutomaticActors",Hidden).GetValue(session);var enqueue=queue.GetType().GetMethod("Enqueue");var callback=enqueue.GetParameters()[0].ParameterType;var siteType=callback.GetGenericArguments()[0];var noOp=typeof(TrafficScarcityVerification).GetMethod("UnexpectedDeferredInvocation",BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(siteType).CreateDelegate(callback);
    int Pending()=> (int)queue.GetType().GetProperty("Count").GetValue(queue);
    void AddPending(){enqueue.Invoke(queue,new object[]{noOp});typeof(GameSession).GetField("automaticTrafficOwnerWorld",Hidden).SetValue(session,world);typeof(GameSession).GetField("automaticTrafficOwnerPlayer",Hidden).SetValue(session,player);}
    AddPending();int cohort=(int)typeof(GameSession).GetField("actualTrafficCohort",Hidden).GetValue(session);var repeat=((IEnumerable<int>)Call(session,"SpawnTrafficStages",player,null)).GetEnumerator();Check(!repeat.MoveNext()&&Pending()==1&&(int)typeof(GameSession).GetField("actualTrafficCohort",Hidden).GetValue(session)==cohort,"same-owner repeat preserves deferred cohort rather than replacing slots");repeat.Dispose();
    Set(session,"Car",null);Call(session,"RefillAutomaticTraffic");Check(Pending()==0,"car replacement invalidates stale closures without invoking a factory");Set(session,"Car",player);
    AddPending();Set(session,"World",null);Call(session,"RefillAutomaticTraffic");Check(Pending()==0,"world replacement invalidates stale closures without invoking a factory");Set(session,"World",world);
    AddPending();Call(session,"CleanupIncompleteBootScene");Check(Pending()==0,"failed boot cleanup releases deferred closures even without owned roots");
    AddPending();Call(session,"RevokeActualVehicleRoster");Check(Pending()==0,"roster revocation releases deferred closures even before roster creation");
    Debug.Log("TRAFFIC SCARCITY EDIT GEOMETRY/POLICY/OWNERSHIP PASS "+checks+"; real factory/refill remains a separate nonce-isolated native acceptance requirement");return checks;
   }
   finally{if(session){Call(session,"RevokeActualVehicleRoster");Call(session,"CleanupIncompleteBootScene");}UnityEngine.Random.state=random;for(int i=owned.Count-1;i>=0;i--)if(owned[i])UnityEngine.Object.DestroyImmediate(owned[i]);}
  }
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
 }
}
