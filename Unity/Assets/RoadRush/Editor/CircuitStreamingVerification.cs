using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RoadRush.Packs;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class CircuitStreamingVerification
    {
        public static void Run()
        {
            GameObject fixture=null,cameraRoot=null;
            var checks=new List<string>();
            void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);checks.Add(why);}
            try
            {
                var bounds=new Bounds(new Vector3(1500,900,0),new Vector3(3000,10,30));
                Check(CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{Vector3.zero},false),"Long structure loads by its nearby end despite remote centre/elevation");
                bounds=new Bounds(new Vector3(2200,0,0),Vector3.one*10);
                Check(!CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{Vector3.zero},false)&&CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{Vector3.zero,new Vector3(2200,0,0)},false),"Remote secondary/race observer retains its own nearby detail");
                bounds=new Bounds(Vector3.zero,Vector3.zero);
                Check(!CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{new Vector3(900,0,0)},false)&&CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{new Vector3(900,0,0)},true),"Hysteresis avoids rebuilding detail in the800–1100m band");
                Check(CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{new Vector3(800,0,0)},false)&&CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{new Vector3(1100,0,0)},true)&&!CircuitShowcaseScenery.ShouldHoldDetail(bounds,new[]{new Vector3(1101,0,0)},true),"Admission and retention boundaries are continuous and finite");
                Check(!CircuitShowcaseScenery.ShouldHoldDetail(bounds,Array.Empty<Vector3>(),false),"Missing observer does not request new detail");
                var args=Environment.GetCommandLineArgs();
                string Arg(string key){int at=Array.IndexOf(args,key);if(at<0)throw new ArgumentException(key);return args[at+1];}
                string input=Arg("-streamingInput"),output=Arg("-streamingOutput");
                if(!output.StartsWith("/private/tmp/rr-circuit-streaming-",StringComparison.Ordinal)||File.Exists(output))throw new IOException("Fresh private proof output required");
                string raw=File.ReadAllText(Path.Combine(input,"layout.json"));
                var region=JsonUtility.FromJson<GeoRegion>(raw);
                var plan=CircuitSceneryPlan.Parse(File.ReadAllText(Path.Combine(input,"scenery.json")),region,WorldPackPolicy.Hash(System.Text.Encoding.UTF8.GetBytes(raw)));
                fixture=new GameObject("Private circuit streaming fixture");fixture.AddComponent<WorldBuilder>();
                var art=CircuitShowcaseScenery.Install(fixture.transform,plan,true);
                var route=new RoutePath(region);var stages=new Stack<IEnumerator>();stages.Push(art.PrepareInitialDetail(route.At(35),()=>false,_=>{}));int yields=0;
                try
                {
                    while(stages.Count>0)
                    {
                        var stage=stages.Peek();
                        if(!stage.MoveNext()){(stage as IDisposable)?.Dispose();stages.Pop();continue;}
                        if(stage.Current is IEnumerator nested)stages.Push(nested);else yields++;
                    }
                }
                finally{while(stages.Count>0)(stages.Pop() as IDisposable)?.Dispose();}
                Check(art.InitialDetailReady&&yields>0,"Real initial mesh/collision preparation yields before admission");
                Check(art.ResidentDetailCells>0&&art.ResidentDetailCells<art.DetailCellCount,"Real pack admits a subset of detailed cells");
                int collisionCount=art.ResidentCollisionCount,initial=art.ResidentDetailCells;
                Check(collisionCount==plan.Parts.Count(p=>p.Collision),"All verified static collision is present before actors");
                cameraRoot=new GameObject("Private streaming observer");var camera=cameraRoot.AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(100000,100,100000);
                art.SendMessage("Update");
                Check(art.ResidentDetailCells==initial-1&&art.DetailCellsRetired==1,"One reconciliation retires only one distant detail cell");
                Check(art.ResidentCollisionCount==collisionCount,"Retiring visual cells does not remove any collision");
                var owned=((List<UnityEngine.Object>)typeof(CircuitShowcaseScenery).GetField("owned",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(art)).ToArray();
                UnityEngine.Object.DestroyImmediate(fixture);fixture=null;
                Check(owned.All(item=>!item),"World destruction releases owned coarse/collision meshes, materials and textures");
                var cancelledRoot=new GameObject("Private cancellation fixture");cancelledRoot.AddComponent<WorldBuilder>();
                try
                {
                    var cancelledArt=CircuitShowcaseScenery.Install(cancelledRoot.transform,plan,true);bool cancel=false;
                    var preparation=cancelledArt.PrepareInitialDetail(route.At(35),()=>cancel,_=>{});
                    int moves=0;
                    try
                    {
                        while(preparation.MoveNext())
                        {
                            if(++moves>=100000)throw new InvalidOperationException("Cancellation preparation did not terminate");
                            if(cancelledArt.ResidentCollisionCount>0)cancel=true;
                            if(preparation.Current is IEnumerator)throw new InvalidOperationException("Cancellation should precede detail admission");
                        }
                    }
                    finally{(preparation as IDisposable)?.Dispose();}
                    Check(cancel&&!cancelledArt.InitialDetailReady,"Cancellation after real collision allocation cannot publish Ready");
                    var cancelledOwned=((List<UnityEngine.Object>)typeof(CircuitShowcaseScenery).GetField("owned",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cancelledArt)).ToArray();
                    UnityEngine.Object.DestroyImmediate(cancelledRoot);
                    Check(cancelledOwned.All(item=>!item),"Cancelled partial preparation releases all allocated assets");
                }
                finally{if(cancelledRoot)UnityEngine.Object.DestroyImmediate(cancelledRoot);}
                File.WriteAllText(output,new JObject{["status"]="PASS_STREAMING_POLICY_AND_REAL_MESH_LIFETIME",["checks"]=new JArray(checks),["initialDetailCells"]=initial,["collisionCount"]=collisionCount,["yieldBoundaries"]=yields}.ToString());
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally{if(fixture)UnityEngine.Object.DestroyImmediate(fixture);if(cameraRoot)UnityEngine.Object.DestroyImmediate(cameraRoot);}
        }
    }
}
