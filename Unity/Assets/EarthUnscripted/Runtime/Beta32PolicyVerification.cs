#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    public static class Beta32PolicyVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
            Check(!UKRepeaterSigns.Policy("GB-WLS",20,"yes",false,false,false,"").Enabled,"No Welsh20 repeaters on lit roads");
            Check(UKRepeaterSigns.Policy("GB-WLS",30,"yes",false,false,false,"").interval==250,"Welsh30 exception repeaters");
            foreach(string country in new[]{"GB-ENG","GB-SCT","GB-NIR"})Check(!UKRepeaterSigns.Policy(country,30,"yes",false,false,false,"").Enabled,"No lit30 default repeaters "+country);
            Check(UKRepeaterSigns.Policy("GB-ENG",30,"no",false,false,false,"").first==200,"Unlit30 first repeater");
            Check(!UKRepeaterSigns.Policy("GB-WLS",20,"unknown",false,false,false,"").Enabled,"Unknown lighting cannot enable prohibited default repeaters");
            Check(!UKRepeaterSigns.Policy("GB-ENG",70,"yes",true,true,true,"").Enabled,"No motorway national-limit repeaters");
            Check(!UKRepeaterSigns.Policy("GB-ENG",60,"no",true,false,false,"").Enabled,"No unlit national-limit repeaters");
            Check(UKRepeaterSigns.Policy("GB-ENG",60,"yes",true,false,false,"").interval==500,"Lit national single carriageway spacing");
            Check(UKRepeaterSigns.Policy("GB-ENG",70,"yes",true,false,true,"").interval==600,"Lit national dual carriageway spacing");
            foreach(int speed in new[]{20,40})Check(!UKRepeaterSigns.Policy("GB-ENG",speed,"yes",false,false,false,"GB:zone"+speed).Enabled,"No ordinary upright repeaters in signed zone");
            Check(!UKRepeaterSigns.Policy("US",40,"yes",false,false,false,"").Enabled,"UK policy not applied outside UK");
            var pieces=Enumerable.Range(0,10).Select(i=>new NewbridgeWorld.Feature{id="piece"+i,sourceId="piece"+i,category="road",kind="primary",width=7,speedLimitMps=17.8816f,oneway=true,signLighting="yes",signLimitForward="40 mph",signContexts=new[]{new SignContextSpan{start=0,end=100,region="GB-ENG"}},points=new[]{new Vector3(0,0,i*100),new Vector3(0,0,(i+1)*100)}}).ToArray();
            var signs=UKRepeaterSigns.Build(pieces);Check(signs.Count==3,"Fragmented1000m road gets three repeaters, not one per way");
            float[] positions=signs.Select(p=>p.control.z).OrderBy(x=>x).ToArray();Check(Mathf.Abs(positions[0]-250)<.01f&&Mathf.Abs(positions[1]-600)<.01f&&Mathf.Abs(positions[2]-950)<.01f,"Distance measured through source-way joins");
            Check(signs[0].control.mountSide!=signs[1].control.mountSide&&signs[0].control.diameter==.3f,"Repeaters alternate side with300mm face");
            var beforeLit=new NewbridgeWorld.Feature{id="unlit",category="road",kind="primary",width=7,speedLimitMps=17.8816f,signLighting="no",signLimitForward="40 mph",oneway=true,signContexts=new[]{new SignContextSpan{start=0,end=100,region="GB-ENG"}},points=new[]{new Vector3(0,0,-100),Vector3.zero}};
            var litStart=new NewbridgeWorld.Feature{id="lit",category="road",kind="primary",width=7,speedLimitMps=17.8816f,signLighting="yes",signLimitForward="40 mph",oneway=true,signContexts=new[]{new SignContextSpan{start=0,end=100,region="GB-ENG"}},points=new[]{Vector3.zero,new Vector3(0,0,100)}};
            Check(UKRepeaterSigns.Build(new[]{beforeLit,litStart}).Any(i=>i.road==litStart&&Mathf.Abs(i.control.z)<.01f),"Repeater at start of carriageway lighting");
            var route=new RoutePreparation(new[]{Vector3.zero,new Vector3(0,0,300),new Vector3(-2000,0,300)});
            Check(route.NextMissing(Vector3.zero,3000,512,(x,z)=>x==0&&z==0,out var target,out float ahead)&&target.x<0&&target.z==300,"Fetch priority follows route around bend, not straight ahead");
            Check(!new RoutePreparation(null).NextMissing(Vector3.zero,3000,512,(x,z)=>false,out _,out _),"Cancel removes route data priority");
            Check(RoutePreparation.GeometryPriority(1000,0)<0&&RoutePreparation.GeometryPriority(0,0)>-220,"Route geometry precedes side scenery but follows stopping corridor");
            var track=new[]{new Vector3(4,0,-10),new Vector3(4,0,10)};
            Check(!RailwayBarrierClearance.Clear(Vector3.zero,new Vector3(20,0,0),track,4.2f,5.6f),"Rail barrier crossing away from midpoint rejected");
            Check(RailwayBarrierClearance.Clear(new Vector3(0,7,0),new Vector3(20,7,0),track,4.2f,5.6f),"Proper overhead bridge barrier retained");
            Check(RailwayBarrierClearance.Clear(new Vector3(9,0,-10),new Vector3(9,0,10),track,4.2f,5.6f),"Safe outer bridge barrier retained");
            Debug.Log("BETA32_POLICY_PASS "+checks);
        }
    }
}
#endif
