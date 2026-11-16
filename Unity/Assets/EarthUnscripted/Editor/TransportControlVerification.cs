using System;
using System.IO;
using EarthUnscripted.NewbridgePrototype;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class TransportControlVerification
 {
  public static void Run()
  {
   int checks=0;
   void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
   var a=new NewbridgeWorld.Control{dx=1,dz=0};var opposite=new NewbridgeWorld.Control{dx=-1,dz=0};var cross=new NewbridgeWorld.Control{dx=0,dz=1};
   foreach(string country in new[]{"GB","US","CH"})for(int t=0;t<1200;t++)
   {
    float seconds=t*.1f;int own=TrafficControlPhase.State(a,seconds,country);
    Check(own==TrafficControlPhase.State(opposite,seconds,country),"Opposing approaches disagree");
    Check(!(own==2&&TrafficControlPhase.State(cross,seconds,country)==2),"Conflicting simultaneous greens");
   }
   Check(TrafficControlPhase.State(a,58,"GB")==3,"UK red+amber missing");
   Check(TrafficControlPhase.State(a,58,"US")==0,"US must not use UK red+amber");
   var root=new GameObject("Isolated control fixture");
   try
   {
    foreach(string kind in new[]{"stop","give_way","no_entry","oneway","speed","traffic_signals","crossing"})
    {var c=new NewbridgeWorld.Control{id=kind,kind=kind,dx=1,value="30 mph",crossing="zebra",direction=1};TrafficControlVisual.Create(root.transform,c,7,"GB",-1);}
    Check(root.transform.childCount==7,"Missing control visuals");
    foreach(string country in new[]{"GB","US","CA","CH"})foreach(string kind in new[]{"stop","give_way","no_entry","oneway","speed"})
    {
     var fixture=new GameObject("Rear post fixture");
     try
     {
      TrafficControlVisual.Create(fixture.transform,new NewbridgeWorld.Control{id="post",kind=kind,dx=1,value="30"},7,country,-1);
      var structure=fixture.transform.GetChild(0).Find("Sign structure");var post=structure.Find("Post");
      Check(post.localPosition.z+post.localScale.z*.5f<-.029f,"Post must stay behind every sign face/back");
      var support=structure.GetComponents<BoxCollider>()[0];Check(Mathf.Abs(support.center.z-post.localPosition.z)<.001f,"Post collision follows rear support");
      var clamp=structure.Find("Rear mounting clamp");Check(clamp.localPosition.z+clamp.localScale.z*.5f<0,"Clamp must not protrude through sign front");
     }
     finally{UnityEngine.Object.DestroyImmediate(fixture);}
    }

    var first=StandardTrafficSigns.Place(root.transform,"speed","30 mph","GB");var second=StandardTrafficSigns.Place(root.transform,"speed","30 mph","GB");
    var meshA=first.transform.Find("Front legend").GetComponent<MeshFilter>().sharedMesh;var meshB=second.transform.Find("Front legend").GetComponent<MeshFilter>().sharedMesh;
    Check(meshA==meshB,"Repeated signs must share the master legend geometry");Check(meshA.bounds.size.x<=.491f&&meshA.bounds.size.y<=.331f,"Speed legend exceeds the standard face");
    foreach(string value in new[]{"5","20","70","100","120"})
    {var sign=StandardTrafficSigns.Place(root.transform,"speed",value,"GB");var bounds=sign.transform.Find("Front legend").GetComponent<MeshFilter>().sharedMesh.bounds;Check(bounds.size.x<=.491f&&bounds.size.y<=.331f&&bounds.size.y>.2f,"One/two/three digit speed sizing");}
    UnityEngine.Object.DestroyImmediate(first);Check(meshB&&meshB.vertexCount>0,"Unloading one clone destroyed a shared master");

    foreach(var filter in root.GetComponentsInChildren<MeshFilter>())Check(filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0,"Empty sign mesh");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
   // Exercise the actual AI control handler: rolling through a stop must
   // not count as stopping; a complete stop releases after the dwell.
   var carObject=new GameObject("Isolated traffic decision");var playerObject=new GameObject("Isolated player");
   try
   {
    var car=carObject.AddComponent<TrafficCar>();var player=playerObject.AddComponent<VehicleController>();var body=playerObject.GetComponent<Rigidbody>();
    typeof(VehicleController).GetProperty("Body").SetValue(player,body);body.position=Vector3.one*1000;car.Player=player;
    var c=new NewbridgeWorld.Control{id="stop-fixture",kind="stop",z=50,dz=1,direction=1};
    var road=new GeoRoad{id="fixture",length=100,points=new[]{Vector3.zero,Vector3.forward*100},NativeControls=new[]{c}};
    var region=new GeoRegion{circuit=true,roads=new[]{road},route=road.points,countryCode="GB"};car.Route=new RoutePath(region);
    var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
    typeof(TrafficCar).GetField("networkRoad",flags).SetValue(car,road);typeof(TrafficCar).GetField("networkDistance",flags).SetValue(car,46.5f);
    var method=typeof(TrafficCar).GetMethod("ControlSpeed",flags);float Decide(float dt)=>(float)method.Invoke(car,new object[]{12f,dt});
    car.Speed=3;Check(Decide(2)==0,"Rolling stop incorrectly released");
    car.Speed=0;Check(Decide(1.1f)==0,"Stop released before complete dwell");
    Check(Decide(.2f)>0,"Empty junction failed to release after full stop");
    typeof(TrafficCar).GetMethod("ResetControlStops",flags).Invoke(car,null);Check(Decide(.1f)==0,"Returning to a stop sign must require a fresh stop");
   }
   finally{UnityEngine.Object.DestroyImmediate(carObject);UnityEngine.Object.DestroyImmediate(playerObject);}
            var before=new NewbridgeWorld.Feature{category="road",sourceId="a",width=7,speedLimitMps=13.4112f,points=new[]{new Vector3(0,0,-50),Vector3.zero}};
            var after=new NewbridgeWorld.Feature{category="road",sourceId="b",width=7,speedLimitMps=13.4112f,points=new[]{Vector3.zero,new Vector3(0,0,50)},controls=new[]{new NewbridgeWorld.Control{id="b:b:speed:1:1:speed",kind="speed",value="30",direction=1,dz=1}}};
            var continuous=new TrafficControlLayout(new NewbridgeWorld.Map{features=new[]{before,after}},-1);
            Check(continuous.Items.Count==0,"Same speed across an OSM way split creates no extra sign");
            after.speedLimitMps=8.9408f;after.controls[0].value="20";
            var transition=new TrafficControlLayout(new NewbridgeWorld.Map{features=new[]{before,after}},-1);
            Check(transition.Items.Count==1&&transition.ClearOfRoads(transition.Items[0].position),"Speed change remains and sign clears carriageway");
            after.controls=new[]{new NewbridgeWorld.Control{id="b:b:oneway:1:oneway",kind="oneway",direction=1,dz=1,z=12},new NewbridgeWorld.Control{id="b:b:entry:-1:no_entry",kind="no_entry",direction=-1,dz=-1,z=50}};after.oneway=true;
            Check(new TrafficControlLayout(new NewbridgeWorld.Map{features=new[]{before,after}},-1).Items.Count==0,"Continuous one-way segments are not new entrances");
            after.controls=new[]{new NewbridgeWorld.Control{id="mapped-1",kind="stop",direction=1,dz=1,z=20},new NewbridgeWorld.Control{id="mapped-2",kind="stop",direction=1,dz=1,z=21}};
            var parallel=new NewbridgeWorld.Feature{category="road",width=7,points=new[]{new Vector3(-7,0,-50),new Vector3(-7,0,70)}};
            var safe=new TrafficControlLayout(new NewbridgeWorld.Map{features=new[]{after,parallel}},-1);
            Check(safe.Items.Count==1,"Coincident same-facing control duplicates collapse");
            Check(safe.ClearOfRoads(safe.Items[0].position)&&safe.Items[0].position.x<-11,"Post clears neighbouring carriageway too");
            string realFixture="Assets/EarthUnscripted/Editor/Fixtures/CardiffSignsFixture.json";
            if(System.IO.File.Exists(realFixture))
            {
                var actual=JsonUtility.FromJson<NewbridgeWorld.Map>(System.IO.File.ReadAllText(realFixture));var watch=System.Diagnostics.Stopwatch.StartNew();
                var actualLayout=new TrafficControlLayout(actual,-1);watch.Stop();int count=0;foreach(var f in actual.features)count+=f.controls?.Length??0;
                foreach(var p in actualLayout.Items)if(p.control.kind!="crossing")Check(actualLayout.ClearOfRoads(p.position),"Real Cardiff sign clears all same-level carriageways");
                Check(actualLayout.Items.Count<count,"Real Cardiff clutter reduced");
                Debug.Log("CARDIFF_SIGN_LAYOUT original="+count+" visible="+actualLayout.Items.Count+" milliseconds="+watch.Elapsed.TotalMilliseconds);
            }
   Debug.Log("TRANSPORT CONTROL PASS "+checks);
  }
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
 }
}
