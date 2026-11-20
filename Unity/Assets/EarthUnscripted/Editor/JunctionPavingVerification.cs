using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;
using Newtonsoft.Json.Linq;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 public static class JunctionPavingVerification
 {
  static NewbridgeWorld.Feature Road(string id,Vector3 a,Vector3 b,string n,string m,bool round=false,bool bridge=false)=>new NewbridgeWorld.Feature{id=id,sourceId=id,category="road",kind="primary",width=6,travelWidth=6,oneway=true,roundabout=round,bridge=bridge,points=new[]{a,b},sourceNodeIds=new[]{"osm:"+n,"osm:"+m},level="0"};
  static bool Covers(IEnumerable<NewbridgeWorld.PavingPatch> patches,double x,double z,bool island)=>patches.Any(p=>p.island==island&&Clipper.PointInPolygon(new PointD(x,z),p.outline,3)!=PointInPolygonResult.IsOutside);
  static void Check(bool ok,string why){if(!ok)throw new InvalidDataException("Junction paving: "+why);}
  public static void Batch()
  {try{Run();UnityEngine.Debug.Log("JUNCTION PAVING VERIFICATION PASS 15 cases");UnityEditor.EditorApplication.Exit(0);}catch(Exception error){UnityEngine.Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
  public static void Run()
  {
   var zero=Vector3.zero;var east=new Vector3(15,0,0);var north=new Vector3(0,0,15);
   var a=Road("a",zero,east,"join","east");var b=Road("b",zero,north,"join","north");
   var map=new NewbridgeWorld.Map{features=new[]{a,b}};var patches=NewbridgeWorld.PlanJunctionPaving(map);Check(Covers(patches,-1,-1,false),"shared-node corner filled");
   b.sourceNodeIds[0]="osm:independent";Check(NewbridgeWorld.PlanJunctionPaving(map).Count==0,"nearby disconnected roads stay separate");b.sourceNodeIds[0]="osm:join";b.bridge=true;Check(NewbridgeWorld.PlanJunctionPaving(map).Count==0,"bridge levels stay separate");
   NewbridgeWorld.Map Ring(float size,bool round)=>new NewbridgeWorld.Map{features=new[]{Road("n",zero,new Vector3(size,0,0),"a","b",round),Road("e",new Vector3(size,0,0),new Vector3(size,0,size),"b","c",round),Road("s",new Vector3(size,0,size),new Vector3(0,0,size),"c","d",round),Road("w",new Vector3(0,0,size),zero,"d","a",round)}};
   patches=NewbridgeWorld.PlanJunctionPaving(Ring(10,false));Check(Covers(patches,5,5,true)&&!Covers(patches,5,5,false),"small one-way island footprint preserved and paved");
   patches=NewbridgeWorld.PlanJunctionPaving(Ring(10,true));Check(!Covers(patches,5,5,true)&&!Covers(patches,5,5,false),"roundabout centre preserved");
   patches=NewbridgeWorld.PlanJunctionPaving(Ring(40,false));Check(!Covers(patches,20,20,true)&&!Covers(patches,20,20,false),"large landscaped island preserved");
   a=Road("offset",zero,east,"join","east");a.roadCentreOffset=2;b=Road("arm",zero,north,"join","north");map=new NewbridgeWorld.Map{features=new[]{a,b}};
   double beforeArea=NewbridgeWorld.PlanJunctionPaving(map).Sum(p=>Math.Abs(Clipper.Area(p.outline)));Array.Reverse(a.points);Array.Reverse(a.sourceNodeIds);a.roadCentreOffset=-2;
   double afterArea=NewbridgeWorld.PlanJunctionPaving(map).Sum(p=>Math.Abs(Clipper.Area(p.outline)));Check(Math.Abs(beforeArea-afterArea)<.01,"asymmetric shoulders preserved when source direction reverses");
   NewbridgeWorld.Map Corner()=>new NewbridgeWorld.Map{features=new[]{Road("east",zero,east,"corner","east-end"),Road("north",zero,north,"corner","north-end")}};
   NewbridgeWorld.Building Box(float west,float south,float eastX,float northZ)=>new NewbridgeWorld.Building{points=new[]{new Vector3(west,0,south),new Vector3(eastX,0,south),new Vector3(eastX,0,northZ),new Vector3(west,0,northZ)}};
   var bare=Corner();var barePaving=NewbridgeWorld.PlanJunctionPaving(bare);
   var blocked=Corner();blocked.buildings=new[]{Box(-1.5f,-1.5f,-.5f,-.5f)};
   Check(!Covers(NewbridgeWorld.PlanJunctionPaving(blocked),-1,-1,false),"junction fillet excludes a mapped building");
   var edge=Corner();edge.buildings=new[]{Box(-4,-1,-3.1f,1)};
   Check(Covers(barePaving,-2.9,-.05,false)&&!Covers(NewbridgeWorld.PlanJunctionPaving(edge),-2.9,-.05,false),"building outside infill bounds still excludes its full rounded safety offset");
   var huge=Corner();huge.buildings=new[]{Box(-1.5f,-1.5f,200,-.5f)};
   Check(!Covers(NewbridgeWorld.PlanJunctionPaving(huge),-1,-1,false),"distant-centred building retains the part of its footprint at the junction");
   var remote=Corner();remote.buildings=Enumerable.Range(0,1000).Select(i=>Box(1000+i*20,500+i*20,1010+i*20,510+i*20)).ToArray();
   var remotePaving=NewbridgeWorld.PlanJunctionPaving(remote);
   Check(remotePaving.Count==barePaving.Count&&Math.Abs(remotePaving.Sum(p=>Clipper.Area(p.outline))-barePaving.Sum(p=>Clipper.Area(p.outline)))<1e-9,"unrelated distant buildings do not change the junction footprint");
   var occupiedIsland=Ring(10,false);occupiedIsland.buildings=new[]{Box(4.5f,4.5f,5.5f,5.5f)};var islandPaving=NewbridgeWorld.PlanJunctionPaving(occupiedIsland);
   Check(!Covers(islandPaving,5,5,true)&&Covers(islandPaving,5,3.5,true),"building clips only its occupied part of a proper small traffic island");
   var translated=Corner();var shift=new Vector3(-10000,0,-20000);foreach(var road in translated.features)for(int i=0;i<road.points.Length;i++)road.points[i]+=shift;
   translated.buildings=new[]{Box(-10004,-20001,-10003.1f,-19999)};
   Check(!Covers(NewbridgeWorld.PlanJunctionPaving(translated),-10002.9,-20000.05,false),"negative world coordinates preserve building offset clipping");
   var root=Path.GetFullPath("Temp/EarthUnscriptedChecks");var actualPath="Assets/EarthUnscripted/Editor/Fixtures/JunctionRoads.json";
   var actual=JsonUtility.FromJson<NewbridgeWorld.Map>(File.ReadAllText(actualPath));var watch=System.Diagnostics.Stopwatch.StartNew();patches=NewbridgeWorld.PlanJunctionPaving(actual);Check(Covers(patches,-255.39694,111.71028,true),"actual Castle Street12m2 refuge paved");Check(Covers(patches,-237.17961,112.45531,true),"actual Castle Street7.6m2 refuge paved");
   Directory.CreateDirectory(Path.Combine(root,"Feedback28"));File.WriteAllText(Path.Combine(root,"Feedback28/paving-checks.json"),new JObject{["passed"]=true,["cases"]=15,["actualMapPlanningSeconds"]=watch.Elapsed.TotalSeconds,["actualAsphaltTriangles"]=patches.Count(p=>!p.island),["actualIslandTriangles"]=patches.Count(p=>p.island)}.ToString());
  }
 }
}
