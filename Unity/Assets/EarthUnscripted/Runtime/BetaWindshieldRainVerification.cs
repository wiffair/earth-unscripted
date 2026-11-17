using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed class BetaWindshieldRainVerification : MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),errors=new List<string>();readonly JObject evidence=new JObject();GameSession session;
        string Output=>SinglePlayerBeta.VerificationOutput;
        void Awake(){if(string.IsNullOrEmpty(Output))throw new InvalidOperationException("Isolated windshield proof required");Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        IEnumerator Start()
        {
            string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0){bool more=false;object next=null;try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception error){failure=error.ToString();}if(failure!=null)break;if(!more){stack.Pop();continue;}if(next is IEnumerator nested){stack.Push(nested);continue;}yield return next;}
            evidence["passed"]=failure==null&&errors.Count==0;evidence["failure"]=failure;evidence["checks"]=new JArray(checks);evidence["errors"]=new JArray(errors);evidence["graphics"]=GameGraphics.Name;
            yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(Output,failure==null?"final-view.png":"failure-view.png"),image.EncodeToPNG());}finally{Destroy(image);}
            File.WriteAllText(Path.Combine(Output,"result.json"),evidence.ToString());Application.Quit((bool)evidence["passed"]?0:1);
        }
        IEnumerator Capture(string name){yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}finally{Destroy(image);}}
        IEnumerator Exercise()
        {
            double until=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady){if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Rain proof boot");yield return null;}
            var models=new List<int>{0};var camper=Core.ImportedVehicleCatalogue.Find("earthunscripted.vw-splittie");if(camper!=null)models.Add(camper.Model);
            var results=new JArray();evidence["vehicles"]=results;session.Economy.AddCredits(1000000);
            foreach(int model in models)
            {
                session.SetMenu(true);Check(session.SelectPackVehicle(model),"Select test vehicle "+model);session.ResumeDrive();
                while(session.Menu||session.PreparingStart)yield return null;session.Car.StartLocked=true;session.Input.Throttle=0;session.Input.Brake=1;
                session.Conditions.NetworkConditions(14,.85f,1,.9f,0);session.View.Mode=2;session.View.Preview=null;
                var rain=session.View.GetComponent<WindshieldRain>();Check(rain,"Cockpit camera owns its isolated rain lens pass");rain.BindNow();Check(rain.HasGlass,"Actual vehicle has identifiable front glass");
                float reach=0;bool capturedTop=false;int cleared=rain.ClearedDrops;until=Time.realtimeSinceStartupAsDouble+9;
                while(Time.realtimeSinceStartupAsDouble<until)
                {
                    for(int i=0;i<session.Car.Visual.Wipers.Length;i++)if(WindshieldWipers.BladeSegment(session.Car.transform,rain.GlassSurface,session.Car.Visual.Wipers[i],out var a,out var b))reach=Mathf.Max(reach,a.y,b.y);
                    if(!capturedTop&&reach>=rain.GlassSurface.Height*.94f){yield return Capture("cockpit-rain-blade-top-"+model);capturedTop=true;}
                    yield return null;
                }
                var row=new JObject{["model"]=model,["glassWidth"]=rain.GlassSurface.Width,["glassHeight"]=rain.GlassSurface.Height,["bladeReach"]=reach,["emitted"]=rain.EmittedDrops,["cleared"]=rain.ClearedDrops,["remaining"]=rain.DropCount,
                    ["origin"]=new JArray(rain.GlassSurface.origin.x,rain.GlassSurface.origin.y,rain.GlassSurface.origin.z),["up"]=new JArray(rain.GlassSurface.up.x,rain.GlassSurface.up.y,rain.GlassSurface.up.z)};
                var blades=new JArray();foreach(var w in session.Car.Visual.Wipers){var blade=w.Find("Wiper blade");var travel=w.GetComponent<WindshieldWiperTravel>();blades.Add(new JObject{["mount"]=new JArray(w.parent.localPosition.x,w.parent.localPosition.y,w.parent.localPosition.z),["radius"]=blade.localPosition.y+blade.localScale.y*.5f,["park"]=travel?travel.ParkAngle:0,["end"]=travel?travel.EndAngle:0});}row["blades"]=blades;results.Add(row);
                yield return Capture("cockpit-rain-"+model);
                Check(rain.EmittedDrops>20&&rain.ClearedDrops>cleared,"Rain impacts arrive and actual moving blades clear them");
                Check(reach>=rain.GlassSurface.Height*.9f,"Real blade geometry sweeps the full glass height");
                Check(rain.RenderingGlass,"Droplet lenses render through the actual cockpit camera");
                int before=rain.EmittedDrops,count=rain.DropCount;session.SetMenu(true);yield return new WaitForSecondsRealtime(.4f);Check(rain.EmittedDrops==before&&rain.DropCount==count,"Menu pause preserves windshield water state");session.ResumeDrive();
                session.View.Mode=0;yield return null;yield return null;Check(!rain.RenderingGlass,"External driving camera has no windscreen overlay");yield return Capture("external-rain-"+model);

            }
            yield return CamperCockpitNativeVerification.Exercise(session,Check);
            var open=Core.ImportedVehicleCatalogue.Find("earthunscripted.hekla-formula-offroad");
            if(open!=null){session.SetMenu(true);Check(session.SelectPackVehicle(open.Model),"Select open-frame Hekla");session.View.GetComponent<WindshieldRain>().BindNow();Check(!session.View.GetComponent<WindshieldRain>().HasGlass,"Open Hekla has no invented windscreen, wipers or glass overlay");}
            evidence["scope"]="Normal Standard/HD cockpit draw, physical blade reach and clear counts; screenshots require visual review. Droplet lenses use lightweight highlights, without scene-colour refraction.";
        }
    }
}
