using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    /// <summary>Actual host forecast and physical asphalt support at competing road levels.</summary>
    public static class RoadCoverageLevelVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static void Check(bool valid,string name){if(!valid)throw new Exception("ROAD COVERAGE LEVEL FAIL "+name);checks++;}
        static GeoRoad Road(string id,float height,float width,bool across)=>new GeoRoad
        {id=id,start=id+"-a",end=id+"-b",kind="primary",width=width,bridge=height>0,layer=height>0?1:0,points=across?new[]{new Vector3(-512,height,0),new Vector3(512,height,0)}:new[]{new Vector3(0,height,-512),new Vector3(0,height,512)}};
        sealed class Fixture:IDisposable
        {
            public readonly GameObject Root;public readonly Native Renderer;public readonly GeoRegion Region;public readonly VehicleController Car;public readonly AutomaticWorldHost Host;public readonly BoxCollider Asphalt;
            public readonly float Support;
            public Fixture(bool upper,bool flat=false)
            {
                Root=new GameObject("Road coverage level verification");
                try
                {
                Renderer=Root.AddComponent<Native>();Renderer.enabled=false;
                typeof(Native).GetField("ready",Hidden).SetValue(Renderer,true);
                var chunk=new GameObject("Published owned geometry");chunk.transform.SetParent(Root.transform,false);
                var chunks=(Dictionary<Vector2Int,GameObject>)typeof(Native).GetField("chunks",Hidden).GetValue(Renderer);
                for(int z=-1;z<=7;z++)for(int x=-1;x<=0;x++)chunks.Add(new Vector2Int(x,z),chunk);
                Support=upper?8:0;
                var asphalt=new GameObject("Road asphalt");asphalt.transform.SetParent(chunk.transform,false);asphalt.transform.position=new Vector3(0,Support-.1f,200);
                Asphalt=asphalt.AddComponent<BoxCollider>();Asphalt.size=new Vector3(8,.2f,1100);
                var lower=Road("lower",0,upper?16:8,false);var bridge=Road("upper",8,upper?8:16,!upper);
                Region=new GeoRegion{roads=flat?new[]{lower}:new[]{lower,bridge},waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),minX=-600,minZ=-600,width=1200,depth=1200,nx=2,nz=2,heights=new[]{0f,0f,0f,0f}};
                typeof(GeoRegion).GetField("NativeWorld",Hidden).SetValue(Region,Renderer);Region.RebuildRenderQueryIndexesOnly(true);
                var carRoot=new GameObject("Actual player car");carRoot.transform.SetParent(Root.transform,false);carRoot.transform.position=new Vector3(0,Support+.6f,0);
                Car=carRoot.AddComponent<VehicleController>();
                if(!Car.Body)typeof(VehicleController).GetMethod("Awake",Hidden).Invoke(Car,null);
                Car.enabled=false;Car.Body.useGravity=false;Car.Body.linearVelocity=Vector3.forward*20;
                Car.WheelGrounded[0]=Car.WheelGrounded[1]=true;Car.ContactPoints[0]=Car.ContactPoints[1]=new Vector3(0,Support,0);
                var hostRoot=new GameObject("Actual coverage host");hostRoot.transform.SetParent(Root.transform,false);Host=hostRoot.AddComponent<AutomaticWorldHost>();Host.enabled=false;
                typeof(AutomaticWorldHost).GetProperty("Renderer").SetValue(Host,Renderer);typeof(AutomaticWorldHost).GetProperty("Region").SetValue(Host,Region);Host.Player=Car;
                // A garage preview may borrow the observer. The player's support
                // guard must still forecast from the physical player pose.
                var preview=new GameObject("Temporary garage observer");preview.transform.SetParent(Root.transform,false);preview.transform.position=new Vector3(500,100,500);Host.Observer=preview.transform;
                Physics.SyncTransforms();
                }
                catch{UnityEngine.Object.DestroyImmediate(Root);Physics.SyncTransforms();throw;}
            }
            public List<Vector3> Update()
            {
                typeof(AutomaticWorldHost).GetField("nextCoverageAt",Hidden).SetValue(Host,float.NegativeInfinity);
                typeof(AutomaticWorldHost).GetMethod("UpdateDrivingCoverage",Hidden).Invoke(Host,null);
                return (List<Vector3>)typeof(AutomaticWorldHost).GetField("drivingCorridor",Hidden).GetValue(Host);
            }
            public void Dispose(){UnityEngine.Object.DestroyImmediate(Root);Physics.SyncTransforms();}
        }
        static void Forecast(bool upper,bool planned,bool flat=false)
        {
            using(var fixture=new Fixture(upper,flat))
            {
                if(planned)fixture.Renderer.SetPlannedRoute(new[]{new Vector3(0,fixture.Support,0),new Vector3(0,fixture.Support,480)});
                var corridor=fixture.Update();string label=(flat?"flat":upper?"upper":"lower")+(planned?" planned":" unplanned");
                Check(corridor.Count>=40,label+" host forecasts full speed-dependent horizon");
                foreach(var point in corridor)
                {
                    Check(Mathf.Abs(point.y-fixture.Support)<.001f,label+" actual emitted forecast stays on the driven/intended level");
                    Check(fixture.Renderer.HasDrivingSurfaceAt(point),label+" unchanged strict native guard accepts every actual forecast point");
                }
                Check(!fixture.Car.StreamingBrake&&float.IsPositiveInfinity(fixture.Car.StreamingSpeedLimit),label+" ready actual deck does not spuriously brake");
                bool roadRequired=(bool)typeof(Native).GetField("immediateCorridorNeedsRoad",Hidden).GetValue(fixture.Renderer);
                Check(roadRequired,label+" keeps asphalt-specific safety requirement");
                fixture.Asphalt.enabled=false;Physics.SyncTransforms();fixture.Update();
                Check(fixture.Car.StreamingBrake&&fixture.Car.StreamingSpeedLimit==0,label+" missing actual asphalt still forces stop despite geometry-ready chunk");
                Check((bool)typeof(Native).GetField("immediateCorridorNeedsRoad",Hidden).GetValue(fixture.Renderer),label+" missing asphalt is never reclassified as geometry-only off-road");
            }
        }
        static void ShortPlannedTail()
        {
            using(var fixture=new Fixture(false,true))
            {
                fixture.Asphalt.transform.position=new Vector3(0,-.1f,0);fixture.Asphalt.size=new Vector3(8,.2f,64.02f);Physics.SyncTransforms();
                fixture.Renderer.SetPlannedRoute(new[]{Vector3.zero,new Vector3(0,0,32)});
                var corridor=fixture.Update();
                Check(corridor.Count>=40,"short planned tail retains full forecast horizon");
                for(int i=0;i<corridor.Count;i++)
                {
                    Check(Mathf.Abs(corridor[i].z-i*8)<.001f,"planned endpoint continues monotonically rather than replaying earlier support");
                    Check(Mathf.Abs(corridor[i].x-corridor[0].x)<.001f,"fallback retains actual planned lane offset");
                }
                Check(fixture.Renderer.HasDrivingSurfaceAt(corridor[4])&&!fixture.Renderer.HasDrivingSurfaceAt(corridor[5]),"real physical support ends at32m, next forecast step unsupported");
                Check(fixture.Car.StreamingBrake&&Mathf.Abs(fixture.Car.StreamingSpeedLimit-AutomaticWorldHost.CoverageSpeed(32))<.0001f,"short physical support yields true32m safety speed, not repeated64m inflation");
            }
        }
        static void PlannedLeftTurn(bool oneWay)
        {
            using(var fixture=new Fixture(false,true))
            {
                var turn=new GeoRoad{id="future-left",start="left-a",end="left-b",kind="primary",width=8,oneWay=oneWay?-1:0,points=new[]{new Vector3(-512,0,32),new Vector3(512,0,32)}};
                fixture.Region.roads=new[]{fixture.Region.roads[0],turn};fixture.Region.RebuildRenderQueryIndexesOnly(true);
                fixture.Renderer.SetPlannedRoute(new[]{Vector3.zero,new Vector3(0,0,32),new Vector3(-480,0,32)});
                var corridor=fixture.Update();
                Check(fixture.Renderer.TryPlannedDrivingPoint(fixture.Car.Body.position,64,out var planned),"turned planned route supplies future point");
                var projection=fixture.Region.ProjectRoadForVehicle(planned,planned.y);
                var expected=fixture.Region.DrivingLanePoint(turn,projection.Source.Point,Vector3.left,out var intendedForward);
                Check(projection.Road==turn,"future lane fixture selects named turned road");
                Check(Vector3.Dot(intendedForward,Vector3.left)>.99f,"named road permits planned leftward travel including reverse one-way");
                Check((corridor[8]-expected).sqrMagnitude<.000001f&&Mathf.Abs(corridor[8].z-30.08f)<.001f,"actual host follows future planned tangent on correct side after90-degree left turn");
            }
        }
        static void Support()
        {
            using(var fixture=new Fixture(false,true))
            {
                var car=fixture.Car;Array.Clear(car.WheelGrounded,0,car.WheelGrounded.Length);
                car.WheelGrounded[0]=car.WheelGrounded[1]=car.WheelGrounded[2]=true;
                car.ContactPoints[0]=new Vector3(0,4,0);car.ContactPoints[1]=new Vector3(0,6,0);car.ContactPoints[2]=new Vector3(0,float.NaN,0);car.ContactPoints[3]=new Vector3(0,1000,0);
                Check(car.RoadSupportHeight==5,"only grounded finite tyre contacts enter mean");
                car.ContactPoints[2]=new Vector3(0,float.PositiveInfinity,0);Check(car.RoadSupportHeight==5,"infinite contact rejected");
                car.Body.mass=2100;car.SpringFactor=1.24f;car.Body.position=new Vector3(0,20,0);
                float expected=20-car.Metrics.RestRootHeight(2100,1.24f);Array.Clear(car.WheelGrounded,0,car.WheelGrounded.Length);
                Check(car.RoadSupportHeight==expected,"airborne fallback uses actual mass and upgraded spring rest-root geometry");
                car.WheelGrounded[0]=true;car.ContactPoints[0]=new Vector3(0,-200,0);car.Replica=true;
                Check(car.RoadSupportHeight==expected,"replica ignores stale suspension contact arrays");
                car.Replica=false;car.ContactPoints[0]=new Vector3(0,float.NaN,0);Check(car.RoadSupportHeight==expected,"all-invalid grounded contacts use exact existing fallback");
                // No plausible road height must not weaken the guard: planar
                // road classification continues to require real asphalt.
                fixture.Asphalt.enabled=false;car.Body.position=new Vector3(0,4,0);car.ContactPoints[0]=new Vector3(0,4,0);Physics.SyncTransforms();fixture.Update();
                Check(car.StreamingBrake&&car.StreamingSpeedLimit==0,"implausible support height plus missing asphalt still brakes");
                Check((bool)typeof(Native).GetField("immediateCorridorNeedsRoad",Hidden).GetValue(fixture.Renderer),"implausible height retains strict asphalt classification");
            }
        }
        public static void Run()
        {checks=0;Forecast(true,false);Forecast(false,false);Forecast(true,true);Forecast(false,true);Forecast(false,false,true);Forecast(false,true,true);ShortPlannedTail();PlannedLeftTurn(false);PlannedLeftTurn(true);Support();Debug.Log("ROAD COVERAGE LEVEL PASS "+checks+"; actual host forecast, physical asphalt, planned/unplanned stacked decks");}
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
