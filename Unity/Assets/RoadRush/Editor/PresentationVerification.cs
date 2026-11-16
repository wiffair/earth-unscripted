using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class PresentationVerification
    {
        public static void Run()
        {
            try
            {
                ProjectSetup.Configure();int count=0;void Check(bool condition,string name){if(!condition)throw new Exception("PRESENTATION FAIL: "+name);count++;Debug.Log("PRESENTATION PASS: "+name);}
                var car=new GameObject("Impact verification").AddComponent<CarVisual>();car.Build(Color.blue,true);
                car.Dent(new Vector3(0,.8f,2.15f),18,Vector3.back);float front=car.DeformationExtent;Check(front>.12f&&front<.71f,"front impact makes bounded local crumple");
                car.Repair();Check(car.DeformationExtent<.0001f,"repair restores undeformed panel mesh");
                car.Dent(new Vector3(-1,.8f,0),14,Vector3.right);Check(car.DeformationExtent>.1f,"side impact makes a distinct dent");
                for(int i=0;i<30;i++)car.Dent(new Vector3(-1,.8f,0),30,Vector3.right);Check(car.DeformationExtent<=.701f,"repeated impacts remain bounded");
                car.Repair();car.SetAutomaticLights(true);Check(car.Headlights[0].enabled&&car.Headlights[0].intensity>100,"night headlights have usable light output");car.SetAutomaticLights(false);Check(!car.Headlights[0].enabled,"daylight turns headlights off");
                Check(car.Wipers.Length==2,"cockpit has two rain-sensitive wiper assemblies");
                var board=new GameObject("Sign fit test").transform;var text=Geometry.FittedLabel("50\nMPH",board,Vector3.zero,Quaternion.identity,1.24f,1.05f,Color.black);
                var bounds=text.GetComponent<Renderer>().localBounds;var scale=text.transform.localScale;
                Check(bounds.size.x*scale.x<=1.241f&&bounds.size.y*scale.y<=1.051f,"speed-sign text fits its board with margins");
                var region=GeoRegion.Load();var navigator=new RoadNavigator(region);var route=navigator.Find(region.route[10],region.route[region.route.Length/2]);
                Check(route.Count>5,"police navigation finds a connected real-road route");
                string oldOverride=PlayerProfile.StorageOverride;string migration=Path.GetFullPath("Builds/Verification/migration-"+DateTime.UtcNow.Ticks+".json");Directory.CreateDirectory(Path.GetDirectoryName(migration));
                try
                {
                    PlayerProfile.StorageOverride=migration;File.WriteAllText(migration,"{\"version\":1,\"credits\":739,\"quality\":1,\"bestLap\":123.45,\"volume\":0.42,\"steerPath\":\"test-wheel-axis\",\"steerLeft\":0.8,\"steerRight\":-0.9,\"wheelForceEnabled\":true}");
                    var migrated=PlayerProfile.Load();Check(migrated.credits==739&&migrated.quality==1&&Mathf.Abs(migrated.bestLap-123.45f)<.01f,"old career keeps credits, graphics and lap record");
                    Check(migrated.steerPath=="test-wheel-axis"&&Mathf.Abs(migrated.steerLeft-.8f)<.001f&&!migrated.wheelForceEnabled,"old wheel calibration survives while force stays deliberately disarmed");
                    Check(migrated.garage.cars.Count==1&&migrated.fixedHour==14&&migrated.Save(),"old career gains a starter garage and valid clock defaults");
                    var again=PlayerProfile.Load();Check(again.credits==739&&again.garage.activeId==migrated.garage.activeId&&File.Exists(migration+".bak"),"migrated car identity persists with a previous-save backup");
                }
                finally{PlayerProfile.StorageOverride=oldOverride;}
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/presentation.txt","PASS "+count+" checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
