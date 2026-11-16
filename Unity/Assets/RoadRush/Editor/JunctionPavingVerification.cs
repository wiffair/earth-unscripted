using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;
using Newtonsoft.Json.Linq;
using RoadRush.NewbridgePrototype;
namespace RoadRush.Editor
{
 public static class JunctionPavingVerification
 {
  static NewbridgeWorld.Feature Road(string id,Vector3 a,Vector3 b,string n,string m,bool round=false,bool bridge=false)=>new NewbridgeWorld.Feature{id=id,sourceId=id,category="road",kind="primary",width=6,travelWidth=6,oneway=true,roundabout=round,bridge=bridge,points=new[]{a,b},sourceNodeIds=new[]{"osm:"+n,"osm:"+m},level="0"};
  static bool Covers(IEnumerable<NewbridgeWorld.PavingPatch> patches,double x,double z,bool island)=>patches.Any(p=>p.island==island&&Clipper.PointInPolygon(new PointD(x,z),p.outline,3)!=PointInPolygonResult.IsOutside);
  static void Check(bool ok,string why){if(!ok)throw new InvalidDataException("Junction paving: "+why);}
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
   var root=Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath));var actualPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),"Library/Application Support/Blackwood Games Beta/Road Rush Beta/AutomaticWorldCache/Prepared/main-1fe99b1a587d977d8448/6955091714c9d1f4/-1_0.json");
   var actual=JsonUtility.FromJson<NewbridgeWorld.Map>(File.ReadAllText(actualPath));var watch=System.Diagnostics.Stopwatch.StartNew();patches=NewbridgeWorld.PlanJunctionPaving(actual);Check(Covers(patches,-255.39694,111.71028,true),"actual Castle Street12m2 refuge paved");Check(Covers(patches,-237.17961,112.45531,true),"actual Castle Street7.6m2 refuge paved");
   Directory.CreateDirectory(Path.Combine(root,"Feedback28"));File.WriteAllText(Path.Combine(root,"Feedback28/paving-checks.json"),new JObject{["passed"]=true,["cases"]=9,["actualMapPlanningSeconds"]=watch.Elapsed.TotalSeconds,["actualAsphaltTriangles"]=patches.Count(p=>!p.island),["actualIslandTriangles"]=patches.Count(p=>p.island)}.ToString());
  }
 }
}
