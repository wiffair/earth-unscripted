using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class RailwayProfileVerification
    {
        static GeoRegion Region(float roadY=10)
        {
            var r=new GeoRegion{id="rail-fixture",minX=-1000,minZ=-1000,width=2000,depth=2000,nx=2,nz=2,heights=new float[4],landNx=2,landNz=2,landCover=new int[4],waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>(),preparedWidths=true,roads=new[]{new GeoRoad{id="road",start="r1",end="r2",kind="residential",width=8,limit=30,points=new[]{new Vector3(0,roadY,-100),new Vector3(0,roadY,100)}}},route=new[]{new Vector3(0,roadY,-100),new Vector3(0,roadY,100)}};r.Prepare();return r;
        }
        static GeoRailway Rail(string id,string[] ids,params Vector3[] points)=>new GeoRailway{id=id,start=ids[0],end=ids[ids.Length-1],kind="rail",width=3.8f,points=points,nodeIds=ids};
        public static void Run()
        {
            int count=0;void Check(bool value,string why){if(!value)throw new Exception("RAIL PROFILE FAIL "+why);count++;Debug.Log("RAIL PROFILE PASS "+why);}
            try
            {
                var r=Region();var bridge=Rail("bridge",new[]{"a","b","c"},new Vector3(-100,.45f,0),new Vector3(0,.45f,0),new Vector3(100,.45f,0));bridge.bridge=true;bridge.layer=1;
                var approach=Rail("approach",new[]{"c","d","e"},new Vector3(100,.45f,0),new Vector3(300,.45f,0),new Vector3(700,.45f,0));r.railways=new[]{bridge,approach};var beforeRoad=JsonUtility.ToJson(r.roads[0]);var terrain=(float[])r.heights.Clone();var report=RailwayProfileSolver.Prepare(r);
                Check(report.BridgeConstraints>0&&report.LiftedNodes>0&&r.railwayProfilesPrepared,"bridge constraint applied and profile finalized");
                Check(bridge.points.All(p=>p.y-RailwayMetrics.UnderDeckDrop>=10+RailwayMetrics.MinimumRoadClearance),"entire estimated deck keeps minimum road clearance");
                Check(bridge.points[2]==approach.points[0],"shared-node bridge and ramp endpoint is exactly continuous");
                for(int i=1;i<approach.points.Length;i++)Check(Mathf.Abs(approach.points[i].y-approach.points[i-1].y)<=Vector2.Distance(new Vector2(approach.points[i].x,approach.points[i].z),new Vector2(approach.points[i-1].x,approach.points[i-1].z))*RailwayMetrics.AdditionalRampGrade+.001f,"bridge added lift bounded by ramp grade "+i);
                Check(beforeRoad==JsonUtility.ToJson(r.roads[0])&&terrain.SequenceEqual(r.heights),"solver never mutates driving roads or source terrain");string finalized=JsonUtility.ToJson(r);Check(RailwayProfileSolver.Prepare(r).AlreadyPrepared&&JsonUtility.ToJson(r)==finalized,"repeated profile preparation is exactly idempotent");
                var crossing=Region(2);crossing.railways=new[]{Rail("cross",new[]{"f","g","h"},new Vector3(-20,.45f,0),new Vector3(0,.45f,0),new Vector3(20,.45f,0))};RailwayProfileSolver.Prepare(crossing);Check(Mathf.Abs(crossing.railways[0].points[1].y-2.02f)<.0001f,"at-grade railhead matches actual road surface plus two centimetres");
                foreach(string kind in new[]{"construction","proposed","razed","demolished"}){var hidden=Region();var rail=Rail("hidden",new[]{"u","v"},new Vector3(-20,.45f,0),new Vector3(20,.45f,0));rail.kind=kind;rail.bridge=true;rail.layer=1;hidden.railways=new[]{rail};RailwayProfileSolver.Prepare(hidden);Check(!RailwayMetrics.Visible(kind)&&rail.points.All(p=>p.y==.45f),"lifecycle "+kind+" creates no visible bridge-lift constraint");}
                var limited=Region();var first=Rail("ok",new[]{"i","j"},new Vector3(-10,.45f,0),new Vector3(10,.45f,0));var invalid=Rail("bad",new[]{"k","l"},new Vector3(20,.45f,0),new Vector3(30,.45f,0));invalid.nodeIds=new[]{"k"};limited.railways=new[]{first,invalid};var originalPoints=first.points;bool refused=false;try{RailwayProfileSolver.Prepare(limited);}catch(InvalidDataException){refused=true;}Check(refused&&!limited.railwayProfilesPrepared&&ReferenceEquals(first.points,originalPoints),"invalid later railway cannot partially publish earlier profile");
                Check(Mathf.Abs(RailwayTags.Metres("13'3\"")-4.0386f)<.00001f&&Mathf.Abs(RailwayTags.Metres("13 ft 3 in")-4.0386f)<.00001f,"imperial vehicle-height restriction parsed as metres");
                var restricted=Region(10);restricted.roads[0].maxVehicleHeight=RailwayTags.Metres("13'3\"");var rb=Rail("restricted",new[]{"o","p"},new Vector3(-20,.45f,0),new Vector3(20,.45f,0));rb.bridge=true;rb.layer=1;restricted.railways=new[]{rb};RailwayProfileSolver.Prepare(restricted);Check(rb.points[0].y>10+4.0386f+RailwayMetrics.UnderDeckDrop&&rb.points[0].y<20,"maxheight is underpass restriction, never absolute deck elevation");
                var tags=JObject.Parse("{\"railway\":\"rail\",\"gauge\":\"1435\",\"tracks\":\"2\"}");var imported=new List<GeoRailway>();int total=0;RailwayImport.Append(imported,Region(),tags,"source",new JArray("100","101"),new List<Vector3>{new Vector3(-300,0,0),new Vector3(300,0,0)},100,ref total);
                Check(imported.Count==1&&Mathf.Abs(imported[0].points[0].x+100)<.001f&&Mathf.Abs(imported[0].points.Last().x-100)<.001f,"complete segment crossing window retained when both endpoints outside");
                Check(imported[0].nodeIds.Length==imported[0].points.Length&&imported[0].start==imported[0].nodeIds[0]&&imported[0].end==imported[0].nodeIds.Last(),"clipped/subdivided nodes align with canonical endpoints");
                Check(imported[0].points.Zip(imported[0].points.Skip(1),(a,b)=>Vector3.Distance(a,b)).All(d=>d<=2.501f)&&Mathf.Abs(imported[0].gauge-1.435f)<.0001f,"bounded profile samples and millimetre gauge conversion");
                int priorCount=imported.Count,priorTotal=total;refused=false;try{RailwayImport.Append(imported,Region(),tags,"overflow",new JArray("1","2","3","4","5"),new List<Vector3>{new Vector3(-20,0,0),new Vector3(20,0,0),new Vector3(float.NaN,0,0),new Vector3(-6000,0,0),new Vector3(6000,0,0)},6000,ref total);}catch(InvalidDataException){refused=true;}Check(refused&&imported.Count==priorCount&&total==priorTotal,"import failure after valid fragment is all-or-nothing");
                var elements=new JArray();for(int i=0;i<8;i++)elements.Add(new JObject{["id"]=i+1,["nodes"]=new JArray(i+100,i+101),["tags"]=new JObject{["highway"]="residential",["railway"]=i==0?"tram":null},["geometry"]=new JArray(new JObject{["lat"]=0,["lon"]=(i-4)*.0004},new JObject{["lat"]=0,["lon"]=(i-3)*.0004})});var dual=GlobalRegions.Convert(new WorldPlace{name="fixture",latitude=0,longitude=0,country_code="GB"},new JObject{["elements"]=elements}.ToString(),new float[4],2,1000);Check(dual.roads.Length==8&&dual.railways.Length==1&&dual.railways[0].kind=="tram","dual highway and tram way enters both source networks");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/railway-profile.txt","PASS "+count+" production solver/import/query checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
