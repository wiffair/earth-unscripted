using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class GeographyVerification
    {
        static int count;
        static void Require(bool value,string name){if(!value)throw new Exception("GEO FAIL: "+name);count++;Debug.Log("GEO PASS: "+name);}
        public static void Run()
        {
            try
            {
                ProjectSetup.Configure();var r=GeoRegion.Load();Require(r!=null&&r.roads.Length>500&&r.buildings.Length>1000,"offline region and buildings load");
                int samples=0,bridges=0,clear=0;float worst=-100,maxGrade=0;
                foreach(var road in r.roads)
                {
                    if(road.bridge)bridges++;
                    for(int i=0;i<road.points.Length-1;i++)
                    {
                        var a=road.points[i];var b=road.points[i+1];float flat=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));if(flat>4)maxGrade=Mathf.Max(maxGrade,Mathf.Abs(b.y-a.y)/flat);
                        if(road.bridge||road.tunnel)continue;
                        for(int j=0;j<4;j++){var p=Vector3.Lerp(a,b,j/4f);float over=r.TerrainHeight(p.x,p.z)-r.RoadSurfaceHeight(p.x,p.z);worst=Mathf.Max(worst,over);if(over<-.1f)clear++;samples++;}
                    }
                }
                Debug.Log($"GEO METRICS: samples={samples} clear={clear} worstTerrainOverRoad={worst:F2} maxGrade={maxGrade:F2} bridges={bridges}");
                Require(clear>(int)(samples*.995f),"road-height conformance for at least 99.5% of source centreline samples");
                Require(maxGrade<.181f,"source road gradients capped for driveability");
                Require(r.countryCode=="GB"&&r.drivingSide==-1&&new RoutePath(r).LaneOffset(100)<0,"GB metadata chooses a left-side driving lane");
                var belgium=GeoRegion.Load("spa");Require(belgium.countryCode=="BE"&&belgium.drivingSide==1&&new RoutePath(belgium).LaneOffset(100)>0,"Belgian metadata chooses a right-side driving lane");
                var silverstone=GeoRegion.Load("silverstone");Require(silverstone.FootprintOverlapsRoad(Array.Find(silverstone.buildings,b=>b.id=="507976158").points),"whole footprint detects the mapped overhead structure crossing the circuit");
                var laneRoad=new GeoRoad{start="fixture-a",end="fixture-b",width=8,length=100,points=new[]{Vector3.zero,Vector3.forward*100}};
                GeoRegion FlatLaneRegion(int side)
                {var flat=new GeoRegion{drivingSide=side,nx=2,nz=2,width=100,depth=100,minX=-50,minZ=0,heights=new float[4],roads=new[]{laneRoad},waters=Array.Empty<GeoWater>()};flat.Prepare();return flat;}
                var leftRegion=FlatLaneRegion(-1);var rightRegion=FlatLaneRegion(1);
                Require(leftRegion.DrivingLanePoint(laneRoad,Vector3.forward*50,Vector3.forward,out _).x<0&&leftRegion.DrivingLanePoint(laneRoad,Vector3.forward*50,Vector3.back,out _).x>0,"left-side recovery respects both directions of travel");
                Require(rightRegion.DrivingLanePoint(laneRoad,Vector3.forward*50,Vector3.forward,out _).x>0&&rightRegion.DrivingLanePoint(laneRoad,Vector3.forward*50,Vector3.back,out _).x<0,"right-side recovery respects both directions of travel");
                laneRoad.oneWay=1;Require(rightRegion.DrivingLanePoint(laneRoad,Vector3.forward*50,Vector3.back,out var allowed).x>0&&allowed.z>0,"recovery respects one-way direction despite the incoming heading");
                Require(bridges>0,"mapped river crossings available");
                var route=new RoutePath(r);Require(route.Length>4000&&route.Length<7000,"connected demonstration loop has plausible length");
                var aPoly=new[]{new Vector3(0,0,0),new Vector3(5,0,0),new Vector3(5,0,5),new Vector3(2,0,2),new Vector3(0,0,5)};
                Require(GeoWorld.Triangulate(aPoly).Length==9,"concave footprint roof triangulates");
                var jan=new DateTimeOffset(2026,1,1,23,30,0,TimeSpan.Zero);var jul=new DateTimeOffset(2026,7,1,23,30,0,TimeSpan.Zero);
                Require(EnvironmentController.Clock(jan,"Europe/London",2).Day==2,"positive time offset crosses midnight");
                Require(EnvironmentController.Clock(jan,"Europe/London",-24).Day==31,"negative time offset crosses year boundary");
                Require(EnvironmentController.Clock(jul,"Europe/London",0).Hour==0&&EnvironmentController.Clock(jan,"Europe/London",0).Hour==23,"map clock uses daylight saving");
                var spring=new DateTimeOffset(2026,3,29,0,30,0,TimeSpan.Zero);Require(EnvironmentController.Clock(spring,"Europe/London",1).Hour==2,"time shift crosses the spring DST boundary correctly");
                Require(EnvironmentController.Clock(jan,"Europe/Brussels",0).Hour==0&&EnvironmentController.Clock(jul,"Europe/Brussels",0).Hour==1,"Belgian map clock uses winter and summer offsets");
                var world=new GameObject("Geographic verification").AddComponent<WorldBuilder>();world.Build("dee-valley");
                var sourceRegion=r;r=world.Route.Region;var roadAtGarage=sourceRegion.Nearest(world.Garage.position,out _,out float garageSeparation);Require(roadAtGarage!=null&&garageSeparation>30,"service garage forecourt clears the mapped road");
                var probe=GameObject.CreatePrimitive(PrimitiveType.Cylinder);var sharedPrimitive=probe.GetComponent<MeshFilter>().sharedMesh;
                int initial=world.GeographicWorld.LoadedChunks;Require(initial>0&&initial<=49,"initial chunk window bounded");
                world.GeographicWorld.Refresh(new Vector3(r.minX+200,100,r.minZ+200),true);
                Require(world.GeographicWorld.LoadedChunks<=81,"travelling releases distant chunks");
                Require(sharedPrimitive&&probe.GetComponent<MeshFilter>().sharedMesh==sharedPrimitive,"unloading terrain never destroys shared wheel/tree primitive meshes");
                var secondary=new GameObject("Second map observer");secondary.transform.position=new Vector3(r.minX+r.width-250,100,r.minZ+r.depth-250);world.GeographicWorld.SecondaryObserver=secondary.transform;
                world.GeographicWorld.Refresh(new Vector3(r.minX+200,100,r.minZ+200),true);Require(world.GeographicWorld.HasSurface(secondary.transform.position)&&world.GeographicWorld.LoadedChunks<=162,"two separated LAN drivers receive bounded terrain coverage");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/geography.txt",$"PASS {count} checks\nroadSamples={samples}\nclear={clear}\nworstTerrainOverRoad={worst}\nmaxSourceGrade={maxGrade}\nbridges={bridges}\n");
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
