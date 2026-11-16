using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Explicit isolated-profile visual acceptance; never runs in normal play.</summary>
    public sealed class BetaBrandVerification : MonoBehaviour
    {
        readonly List<string> errors=new List<string>();
        readonly List<string> captures=new List<string>();
        GameSession session;DrivingUI ui;bool loading;
        string Output=>SinglePlayerBeta.VerificationOutput;
        void Awake(){Application.logMessageReceived+=Log;Application.runInBackground=true;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text+"\n"+stack);}
        IEnumerator Start()
        {
            var work=Exercise();string failure=null;
            while(true)
            {
                bool more=false;object current=null;
                try{more=work.MoveNext();if(more)current=work.Current;}catch(Exception e){failure=e.ToString();}
                if(failure!=null||!more)break;
                yield return current;
            }
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(Output,"result.json"),new JObject{["passed"]=passed,["failure"]=failure,["errors"]=new JArray(errors),["captures"]=new JArray(captures),["version"]=Application.version,["graphics"]=GameGraphics.Name,["betaDataPath"]=SinglePlayerBeta.DataRoot,["scope"]="Actual native menu/HUD/loading rendering in isolated beta profile; screenshots require visual review."}.ToString());
            Application.Quit(passed?0:1);
        }
        void Capture(string name)
        {ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));captures.Add(name+".png");}
        IEnumerator Exercise()
        {
            if(string.IsNullOrEmpty(Output))throw new InvalidOperationException("Explicit isolated profile required");
            double deadline=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Presentation boot");yield return null;}
            ui=FindFirstObjectByType<DrivingUI>();session.SetMenu(true);
            foreach(var size in new[]{new Vector2Int(1440,900),new Vector2Int(1280,720),new Vector2Int(1920,900)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.35f);yield return new WaitForEndOfFrame();Capture("home-"+size.x+"x"+size.y);yield return new WaitForEndOfFrame();
            }
            Screen.SetResolution(1440,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.35f);
            var tab=typeof(DrivingUI).GetField("tab",BindingFlags.NonPublic|BindingFlags.Instance);
            session.Profile.detailedHud=true;session.Profile.livingScenery=true;session.Profile.performanceStats=true;
            foreach(int page in new[]{17,18,19,20,21,22,23,24,25,26,27,28,29,30})
            {
                tab.SetValue(ui,page);yield return null;yield return new WaitForEndOfFrame();Capture("menu-"+page);yield return new WaitForEndOfFrame();
            }
            ui.ShowGarage();yield return new WaitForSecondsRealtime(1.25f);yield return new WaitForEndOfFrame();Capture("garage");yield return new WaitForEndOfFrame();
            tab.SetValue(ui,0);yield return null;
            loading=true;yield return new WaitForEndOfFrame();Capture("loading-layout");yield return new WaitForEndOfFrame();loading=false;
            session.Profile.detailedHud=false;session.Profile.performanceStats=false;
            session.ResumeDrive();
            while(session.Menu||session.PreparingStart){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Presentation drive readiness");yield return null;}
            yield return new WaitForSecondsRealtime(1);yield return new WaitForEndOfFrame();Capture("driving-hud");yield return new WaitForEndOfFrame();
            ui.ShowNavigationMap();yield return new WaitForSecondsRealtime(1);yield return new WaitForEndOfFrame();Capture("navigation-map");yield return new WaitForEndOfFrame();
        }
        void OnGUI()
        {if(!loading)return;GUI.depth=-1800;BrandScreen.DrawLoading("Preparing your journey","Newbridge / Bryn Meadows","Building starting roads and scenery…",false);}
    }
}
