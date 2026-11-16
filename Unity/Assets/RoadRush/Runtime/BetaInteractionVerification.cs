using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // Explicit nonce-scoped acceptance only. Normal launches never attach it.
    public sealed class BetaInteractionVerification : MonoBehaviour
    {
        GameSession session;DrivingUI ui;readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        void Awake(){Application.logMessageReceived+=Error;}
        void Error(string text,string stack,LogType kind){if((kind==LogType.Error||kind==LogType.Exception)&&errors.Count<20)errors.Add(text);}
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        void Progress(string stage,Vector3? destination=null)
        {
            var d=new JObject{{"stage",stage},{"screenWidth",Screen.width},{"screenHeight",Screen.height},{"uiScale",Mathf.Min(Screen.width/1440f,Screen.height/900f)}};
            if(destination.HasValue&&ui){var nav=ui.Navigation;var p=destination.Value;float scale=1330/nav.ViewSpan;d["targetX"]=720+(p.x-nav.ViewCentre.x)*scale;d["targetY"]=435-(p.z-nav.ViewCentre.y)*scale;d["destination"]=JObject.FromObject(new{x=p.x,y=p.y,z=p.z});}
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"progress.json"),d.ToString());
        }
        IEnumerator Wait(Func<bool> done,string stage,double timeout=300,Vector3? destination=null)
        {
            double start=Time.realtimeSinceStartupAsDouble,next=0;
            while(!done()){if(Time.realtimeSinceStartupAsDouble>next){Progress(stage,destination);next=Time.realtimeSinceStartupAsDouble+.5;}if(Time.realtimeSinceStartupAsDouble-start>timeout)throw new TimeoutException(stage);yield return null;}
        }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0){object next=null;bool more=false;Exception failure=null;try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception e){failure=e;}if(failure!=null){Finish(false,failure.ToString());yield break;}if(!more){stack.Pop();continue;}if(next is IEnumerator child){stack.Push(child);continue;}yield return next;}
        }
        IEnumerator Exercise()
        {
            yield return Wait(()=> (session=FindAnyObjectByType<GameSession>())&&session.BootReady,"initial-world",600);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-roadRushBetaGeometrySmoke")>=0)
            {
                yield return BetaSmokeVerification.CheckGeometryReuse(session);
                Check(session.World.AutomaticWorld.Renderer.ReusedGeometryChunks>0,"Native mesh/collider reuse and both retention bounds pass");
            }
            ui=FindAnyObjectByType<DrivingUI>();Check(ui,"Normal driving UI exists");
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            session.ResumeDrive();yield return Wait(()=>!session.Menu&&!session.PreparingStart,"enter-drive",330);
            session.Car.Body.isKinematic=true;session.Input.enabled=false;
            var region=session.World.Route.Region;var graph=new RoadNavigationGraph(region.roads);Vector3 target=Vector3.zero;bool found=false;
            foreach(var road in region.roads.Where(r=>r.NativeSourceWay=="66391366").Concat(region.roads).Take(100))
            {
                var endpoint=road.points[road.points.Length-1];if(Vector3.Distance(endpoint,session.Car.Body.position)<200||Vector3.Distance(endpoint,session.Car.Body.position)>1500)continue;
                try{var route=graph.Plan(session.Car.Body.position,endpoint);if(route.Length>250){target=endpoint;found=true;break;}}catch(InvalidOperationException){}
            }
            Check(found,"A connected destination exists in the actual streamed road graph");
            yield return Wait(()=>ui.Navigation.IsOpen,"click-minimap",300,target);
            Check(session.Menu&&Time.timeScale==0,"Clicking the driving minimap pauses the game and opens navigation");
            var before=ui.Navigation.ViewCentre;float beforeSpan=ui.Navigation.ViewSpan;
            yield return Wait(()=>Vector2.Distance(before,ui.Navigation.ViewCentre)>5&&Mathf.Abs(beforeSpan-ui.Navigation.ViewSpan)>1,"pan-and-zoom-map",300,target);
            Check(true,"Expanded map pans and zooms through actual UI input");
            yield return Wait(()=>ui.Navigation.Preview!=null,"select-destination",300,target);
            Check(ui.Navigation.Preview.Length>0&&Time.timeScale==0,"Selecting a destination plots a road route while remaining paused");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"navigation-preview.png"));yield return new WaitForEndOfFrame();
            yield return Wait(()=>!ui.Navigation.IsOpen&&ui.Navigation.Current!=null&&!session.Menu,"start-route-and-drive",300,target);
            Check(Time.timeScale>0,"Accepting the route resumes the driving game");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"navigation-guidance.png"));yield return new WaitForEndOfFrame();
            yield return Wait(()=>ui.Navigation.IsOpen,"reopen-to-cancel",300,target);
            yield return Wait(()=>ui.Navigation.Current==null,"cancel-route",300,target);
            yield return Wait(()=>!ui.Navigation.IsOpen&&!session.Menu,"cancel-return-to-drive",300,target);
            Check(true,"Cancel route clears guidance; Cancel / Return leaves the map and resumes");
            // Drive over the exact captured failure apron with ordinary physics.
            var start=new Vector3(1523,51.65f,1746);var end=new Vector3(1513.6f,51.1f,1742.5f);var heading=end-start;heading.y=0;var rotation=Quaternion.LookRotation(heading);
            var host=session.World.AutomaticWorld;host.BeginRelocation(start);
            try{yield return Wait(()=>host.RelocationReady,"load-hole-approach",300);}finally{host.EndRelocation();}
            session.Car.Body.isKinematic=false;session.Car.Recover(start,rotation);Check(session.Car.TryInitializeSupportedRest(start,rotation,out var reason,out _),"Hole approach has initial wheel support: "+reason);
            var initial=session.Car.Body.position;double began=Time.realtimeSinceStartupAsDouble;float minimum=initial.y;
            while(Vector3.ProjectOnPlane(session.Car.Body.position-initial,Vector3.up).magnitude<9.4f)
            {
                if(Time.realtimeSinceStartupAsDouble-began>=30)throw new Exception("Physical hole approach stalled");minimum=Mathf.Min(minimum,session.Car.Body.position.y);if(session.Car.Body.position.y<=47)throw new Exception("Car fell through the captured portal gap");
                session.Input.Steer=0;session.Input.Throttle=session.Car.AbsoluteSpeed<2.4f?.25f:0;session.Input.Brake=session.Car.AbsoluteSpeed>2.9f?.3f:0;yield return null;
            }
            session.Input.Throttle=0;session.Input.Brake=1;double stop=Time.realtimeSinceStartupAsDouble;while(Time.realtimeSinceStartupAsDouble-stop<2){minimum=Mathf.Min(minimum,session.Car.Body.position.y);yield return null;}
            Check(initial.y-minimum<2,"Normal vehicle crosses the recorded gap without the underworld drop");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"hole-drive.png"));yield return new WaitForEndOfFrame();
            Check(errors.Count==0,"No runtime errors during collision, map and hole acceptance");Finish(true,"");
        }
        void Finish(bool passed,string error)
        {
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),new JObject{{"passed",passed},{"error",error},{"version",Application.version},{"checks",new JArray(checks)},{"runtimeErrors",new JArray(errors)},{"dataRoot",SinglePlayerBeta.DataRoot},{"screenWidth",Screen.width},{"screenHeight",Screen.height},{"uiScale",Mathf.Min(Screen.width/1440f,Screen.height/900f)}}.ToString());Application.Quit(passed?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Error;}
    }
}
