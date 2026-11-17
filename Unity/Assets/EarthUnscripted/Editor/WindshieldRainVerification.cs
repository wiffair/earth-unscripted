using System;
using System.Reflection;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class WindshieldRainVerification
    {
        public static void Batch(){try{Run();UnityEditor.EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
            var actualRoot=new GameObject("Actual Brook windscreen verification");
            try
            {
                var actual=actualRoot.AddComponent<CarVisual>();actual.Build(Color.white,true,0);Check(WindshieldWipers.TrySurface(actual,out var actualSurface),"Actual Brook front glass is identified without rubber gasket geometry");
                float actualReach=0;for(int step=0;step<=200;step++){WindshieldWipers.SetSweep(actual.Wipers,step/200f);for(int i=0;i<actual.Wipers.Length;i++)if(WindshieldWipers.BladeSegment(actualRoot.transform,actualSurface,actual.Wipers[i],out var a,out var b))actualReach=Mathf.Max(actualReach,a.y,b.y);}
                Debug.Log("ACTUAL BROOK FULL SWEEP width="+actualSurface.Width+" height="+actualSurface.Height+" reach="+actualReach);
                Check(actualReach>=actualSurface.Height*.94f,"Actual shallow Brook windscreen retains full-height blades instead of radius clamping");
                foreach(var w in actual.Wipers){var travel=w.GetComponent<WindshieldWiperTravel>();Check(travel&&Mathf.Abs(travel.EndAngle)<30,"Actual Brook adjusts mechanical end travel to preserve glass/pillar clearance");}
            }
            finally{Geometry.ReleaseMeshes(actualRoot);UnityEngine.Object.DestroyImmediate(actualRoot);}
            var root=new GameObject("Vertical camper glass sizing fixture");var cameraRoot=new GameObject("Rain simulation fixture");
            try
            {
                var visual=root.AddComponent<CarVisual>();var mesh=new Mesh{vertices=new[]{new Vector3(-.739f,1.4035f,2.034f),new Vector3(.739f,1.4035f,2.034f),new Vector3(.739f,1.8465f,2.034f),new Vector3(-.739f,1.8465f,2.034f)},triangles=new[]{0,1,2,0,2,3}};
                var glass=new GameObject("Windscreen");glass.transform.SetParent(root.transform,false);glass.AddComponent<MeshFilter>().sharedMesh=mesh;glass.AddComponent<GeneratedMeshOwner>().Mesh=mesh;
                Check(WindshieldWipers.TrySurface(visual,out var surface),"Actual vertical glass geometry produces its own surface frame");
                var trim=Geometry.Box("Thin matched windshield surround",root.transform,new Vector3(0,1.6f,2),new Vector3(2,1,.1f),Geometry.Material("Rain surround exclusion fixture",Color.white));
                Check(WindshieldWipers.TrySurface(visual,out var withTrim)&&Mathf.Abs(withTrim.Width-surface.Width)<.00001f&&Mathf.Abs(withTrim.Height-surface.Height)<.00001f,"Named metal windscreen surrounds never inflate the glass surface or receive rain");
                var tapered=new WindshieldWipers.Surface(Vector3.zero,Vector3.right*2,Vector3.up,.8f);
                Check(Mathf.Abs(tapered.Point(0,1).x-.6f)<.001f&&Mathf.Abs(tapered.Point(1,1).x-1.4f)<.001f,"Rain follows the narrower upper glass instead of painting the pillars");
                var panes=new WindshieldWipers.PaneMask(new WindshieldWipers.Surface(Vector3.zero,Vector3.right,Vector3.up),new[]{
                    new Vector3(0,0,0),new Vector3(.45f,0,0),new Vector3(.45f,1,0),new Vector3(0,0,0),new Vector3(.45f,1,0),new Vector3(0,1,0),
                    new Vector3(.55f,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(.55f,0,0),new Vector3(1,1,0),new Vector3(.55f,1,0)});
                Check(panes.Contains(new Vector2(.2f,.8f),.01f)&&panes.Contains(new Vector2(.8f,.8f),.01f),"Actual separate pane triangles retain rain on both sides");
                Check(!panes.Contains(new Vector2(.5f,.8f),.01f),"Split-window centre seal is never painted with raindrops");
                Check(!panes.Contains(new Vector2(.445f,.8f),.01f)&&!panes.Contains(new Vector2(.005f,.8f),.01f),"Droplet corners cannot cross pane seals or outside edges");
                var wipers=WindshieldWipers.Fit(visual,null);Check(wipers.Length==2,"Glazed imported cabin receives two physical wiper assemblies");
                float reach=0;
                for(int sample=0;sample<=100;sample++)
                {
                    WindshieldWipers.SetSweep(wipers,sample/100f);
                    for(int i=0;i<2;i++)
                    {
                        Check(WindshieldWipers.BladeSegment(root.transform,surface,wipers[i],out var a,out var b),"Actual rubber blade endpoints are available");
                        reach=Mathf.Max(reach,a.y,b.y);Check(a.x>=.005f&&b.x>=.005f&&a.x<=surface.Width-.005f&&b.x<=surface.Width-.005f,"Mirrored full sweep stays on the real glass");
                    }
                }
                Check(reach>surface.Height*.94f&&reach<surface.Height,"Blade reaches the top of the pane with a retained seal margin");
                var bladeMesh=wipers[0].Find("Wiper blade").GetComponent<MeshFilter>().sharedMesh;float baseHalf=0,tipHalf=0;
                foreach(var v in bladeMesh.vertices){if(v.y<-.49f)baseHalf=Mathf.Max(baseHalf,Mathf.Abs(v.x));if(v.y>.49f)tipHalf=Mathf.Max(tipHalf,Mathf.Abs(v.x));}
                Check(Mathf.Abs(bladeMesh.bounds.min.y+.5f)<.00001f&&Mathf.Abs(bladeMesh.bounds.max.y-.5f)<.00001f,"Thin blade retains the exact physical clearing endpoints");
                Check(wipers[0].Find("Wiper blade").localScale.x<=.0101f&&tipHalf<baseHalf*.4f,"Beveled rubber frame narrows from10mm to3.5mm at its near-eye tip");
                var rain=cameraRoot.AddComponent<WindshieldRain>();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                void Set(string name,object value)=>typeof(WindshieldRain).GetField(name,flags).SetValue(rain,value);
                Set("boundVisual",visual);Set("boundWipers",wipers);Set("surface",surface);Set("hasGlass",true);
                WindshieldWipers.SetSweep(wipers,.55f);rain.Advance(.001f,0,0);
                WindshieldWipers.SetSweep(wipers,.75f);WindshieldWipers.BladeSegment(root.transform,surface,wipers[0],out var lower,out var upper);
                Check(WindshieldWipers.Swept((lower+upper)*.5f,lower,upper,lower,upper,.014f),"Actual stationary rubber blade blocks a new impact beneath it");
                var target=Vector2.Lerp(lower,upper,.92f);Check(target.y>surface.Height*.85f,"Clearing test targets the upper glass, not only the lower half");
                rain.SeedVerificationDrop(new Vector2(target.x/surface.Width,target.y/surface.Height));rain.Advance(.016f,0,0);
                Check(rain.ClearedDrops==1&&rain.DropCount==0,"The real blade movement clears an upper-glass droplet");
                rain.SeedVerificationDrop(new Vector2(.98f,.98f));rain.Advance(.016f,0,0);Check(rain.DropCount==1,"Unswept upper-corner droplets remain on glass");
                int emitted=rain.EmittedDrops,count=rain.DropCount;rain.Advance(0,1,40);Check(rain.DropCount==count&&rain.EmittedDrops==emitted,"Paused time causes no accumulation or clearing");
                rain.Advance(.5f,1,0);Check(rain.EmittedDrops>emitted&&rain.DropCount>1,"Real precipitation adds droplets over time");
                emitted=rain.EmittedDrops;rain.Advance(.016f,0,0);Check(rain.EmittedDrops==emitted,"Dry/sheltered weather stops new impacts while retained droplets can remain");
                for(int i=0;i<30;i++)rain.Advance(.1f,1,40);Check(rain.DropCount<=180,"Rain geometry and state remain bounded during sustained precipitation");
                Check(!WindshieldWipers.Swept(new Vector2(10,10),Vector2.zero,Vector2.up,Vector2.right,Vector2.one,.02f),"Clearing never wipes unrelated glass areas");
                glass.SetActive(false);
                var paneVertices=new[]{new Vector3(0,0,0),new Vector3(.45f,0,0),new Vector3(.45f,1,0),new Vector3(0,0,0),new Vector3(.45f,1,0),new Vector3(0,1,0),new Vector3(.55f,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(.55f,0,0),new Vector3(1,1,0),new Vector3(.55f,1,0)};
                for(int i=0;i<paneVertices.Length;i++)paneVertices[i]=surface.Point(paneVertices[i].x,paneVertices[i].y);
                var paneIndices=new int[paneVertices.Length];for(int i=0;i<paneIndices.Length;i++)paneIndices[i]=i;
                var splitObject=new GameObject("Windscreen");splitObject.transform.SetParent(root.transform,false);var splitMesh=new Mesh{vertices=paneVertices,triangles=paneIndices};splitObject.AddComponent<MeshFilter>().sharedMesh=splitMesh;splitObject.AddComponent<GeneratedMeshOwner>().Mesh=splitMesh;
                Check(WindshieldWipers.TrySurface(visual,out var splitSurface,out var splitTriangles)&&WindshieldWipers.SplitPaneBounds(splitSurface,splitTriangles,out _,out _),"Real split-pane triangles establish separate mechanical glass bounds");
                wipers=WindshieldWipers.Fit(visual,wipers);var splitMask=new WindshieldWipers.PaneMask(splitSurface,splitTriangles);float splitReach=0;
                for(int sample=0;sample<=100;sample++)
                {
                    WindshieldWipers.SetSweep(wipers,sample/100f);for(int i=0;i<2;i++)
                    {WindshieldWipers.BladeSegment(root.transform,splitSurface,wipers[i],out var a,out var b);Check(splitMask.Contains(a,.003f)&&splitMask.Contains(b,.003f),"Physical blade endpoints stay within their own pane at every sweep phase");splitReach=Mathf.Max(splitReach,a.y,b.y);}
                }
                Check(splitReach>splitSurface.Height*.94f,"Per-pane travel retains full vertical reach instead of shortening blades");
                Debug.Log("WINDSHIELD RAIN PASS "+checks);
            }
            finally{UnityEngine.Object.DestroyImmediate(cameraRoot);UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
