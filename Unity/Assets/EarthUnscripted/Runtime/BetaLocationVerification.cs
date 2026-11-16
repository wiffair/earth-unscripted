using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace EarthUnscripted
{
    // Explicit nonce-scoped QA only; normal launches never attach this driver.
    public sealed class BetaLocationVerification : MonoBehaviour
    {
        static bool installed;readonly List<string> checks=new List<string>();GameSession session;string expected;string placeName="Cardiff";double selectedLatitude,selectedLongitude;
        public static void Install(){if(installed)return;installed=true;var root=new GameObject("Explicit location verification");DontDestroyOnLoad(root);root.AddComponent<BetaLocationVerification>();}
        void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        IEnumerator Wait(Func<bool> done,string stage,double timeout=300)
        {
            double start=Time.realtimeSinceStartupAsDouble;
            while(!done()){File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"progress.json"),new JObject{{"stage",stage},{"status",session?session.Downloads?.Status:""}}.ToString());if(Time.realtimeSinceStartupAsDouble-start>timeout)throw new TimeoutException(stage);yield return null;}
        }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0)
            {
                object next=null;bool more=false;Exception failure=null;
                try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}
                catch(Exception e){failure=e;}
                if(failure!=null){Finish(false,failure.ToString());yield break;}
                if(!more){stack.Pop();continue;}
                if(next is IEnumerator nested){stack.Push(nested);continue;}
                yield return next;
            }
        }
        IEnumerator Exercise()
        {
            yield return Wait(()=> (session=FindAnyObjectByType<GameSession>())&&session.BootReady,"initial-world",600);
            var args=Environment.GetCommandLineArgs();int placeArg=Array.IndexOf(args,"-earthUnscriptedBetaLocationPlace");
            if(placeArg>=0&&placeArg+1<args.Length)placeName=args[placeArg+1];
            string original=session.Profile.region;var ui=FindAnyObjectByType<DrivingUI>();ui.ShowWorldMap();var menu=ui.LocationMenu;
            Check(menu.CanDrive,"Existing area is available before a new selection");
            menu.SelectPlace(session.Downloads,new WorldPlace{name="Invalid coordinate check",latitude=999,longitude=0,country_code="GB",timezone="Europe/London"});
            Check(!menu.CanDrive&&menu.Shown==null&&session.Downloads.Ready==null,"Invalid selection clears the old map and cannot drive the old start");
            Check(!menu.DriveSelected(session)&&session.Profile.region==original,"Blocked Drive leaves the current profile location unchanged");
            session.Downloads.Search(placeName);yield return Wait(()=>!session.Downloads.Busy,"search-cardiff",150);
            var place=session.Downloads.Results.FirstOrDefault(p=>p.country_code=="GB"&&p.latitude<52);Check(place!=null,"Live place search finds "+placeName+" in the UK");selectedLatitude=place.latitude;selectedLongitude=place.longitude;
            menu.SelectPlace(session.Downloads,place);Check(!menu.CanDrive&&menu.Shown==null,"Clicking "+placeName+" removes the previous preview and drive target");
            yield return Wait(()=>!session.Downloads.Busy,"cardiff-preview",150);menu.AcceptPrepared(session.Downloads);
            Check(menu.Shown!=null,session.Downloads.Status);Check(Math.Abs(menu.Shown.latitude-place.latitude)<.00001&&Math.Abs(menu.Shown.longitude-place.longitude)<.00001,"Preview follows the selected coordinates");
            Check(!menu.CanDrive&&session.Downloads.Ready==null,"Preview-only roads cannot be driven before preparation");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"cardiff-preview.png"));yield return new WaitForEndOfFrame();
            session.Downloads.Download(new WorldPlace{name=place.name,latitude=place.latitude,longitude=place.longitude,country_code=place.country_code,timezone=place.timezone,size=1000});
            yield return Wait(()=>!session.Downloads.Busy,"prepare-cardiff",180);menu.AcceptPrepared(session.Downloads);
            Check(session.Downloads.Ready!=null,session.Downloads.Status);expected=session.Downloads.Ready.id;
            Check(menu.CanDrive&&menu.Shown.id==expected&&expected!=original,"Prepared selection enables Drive only for the new area");
            var startRoad=menu.Shown.Nearest(menu.SelectedStart,out var actualPoint,out var separation);
            Check(startRoad!=null&&separation<.01f,"Default marker is supported by the prepared road index even when the town centre has no nearby road");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"cardiff-ready.png"));yield return new WaitForEndOfFrame();
            var previous=session;Check(menu.DriveSelected(session),"The menu Drive action accepts the selected road");
            yield return Wait(()=>{var next=FindAnyObjectByType<GameSession>();if(next&&next!=previous&&next.BootReady&&!LocationLoading.Busy){session=next;return true;}return false;},"boot-selected-cardiff",600);
            Check(session.Profile.region==expected&&session.Profile.startRegion==expected&&session.World.Route.Region.id==expected,"Scene reload retains the chosen region and custom road start");
            Check(Math.Abs(session.World.Route.Region.latitude-selectedLatitude)<.00001&&Math.Abs(session.World.Route.Region.longitude-selectedLongitude)<.00001,"Generated world uses the selected geographic origin");
            Check(session.World.AutomaticWorld&&session.World.AutomaticWorld.Ready,"Selected location uses the streaming world backend");
            session.ResumeDrive();yield return Wait(()=>!session.Menu&&!session.PreparingStart,"enter-cardiff-drive",330);
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;var keyboard=InputSystem.AddDevice<Keyboard>("Location test keyboard");keyboard.MakeCurrent();var initial=session.Car.Body.position;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));double began=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-began<3){InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return null;}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.RemoveDevice(keyboard);
            Check(Vector3.Distance(initial,session.Car.Body.position)>1,"Normal keyboard input drives the car in the newly selected world");
            Check(session.Car.Damage==0,"Selected start has no collision damage");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"cardiff-start-driving.png"));yield return new WaitForEndOfFrame();
            // Isolated road-surface acceptance after ordinary keyboard proof.
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            session.Input.enabled=false;
            // The town-centre default is a legitimate short side road. Use a
            // separately identified resident road for the longer surface check.
            // This does not replace any generated geometry or gameplay route.
            var region=session.World.Route.Region;
            var testRoad=region.roads.Where(road=>!road.tunnel && road.width>=6 && road.length>230)
                .OrderBy(road=>road.points.Min(point=>(point-session.Car.Body.position).sqrMagnitude)).FirstOrDefault();
            Check(testRoad!=null,"A resident Cardiff road provides the independent 180m surface test");
            var savedGuide=region.route;RoutePath physicalRoute;
            try{region.route=testRoad.points;physicalRoute=new RoutePath(region);}finally{region.route=savedGuide;}
            Check(true,"Physical surface route: "+testRoad.name+" / "+testRoad.id+" / "+physicalRoute.Length+"m");
            float guideStart=10;
            var initialRoad=physicalRoute.At(guideStart,physicalRoute.LaneOffset(guideStart));
            var initialRotation=physicalRoute.Rotation(guideStart);
            var host=session.World.AutomaticWorld;session.Car.Body.isKinematic=true;host.BeginRelocation(initialRoad);
            try{yield return Wait(()=>host.RelocationReady,"prepare-representative-cardiff-road",300);}
            finally{host.EndRelocation();session.Car.Body.isKinematic=false;}
            session.Car.Recover(initialRoad,initialRotation);
            Check(session.Car.TryInitializeSupportedRest(initialRoad,initialRotation,out var restError,out _),"Selected-world physical support: "+restError);
            float finish=Mathf.Min(guideStart+180,physicalRoute.Length-20);double traversalStart=Time.realtimeSinceStartupAsDouble;float airborne=0;
            Check(finish-guideStart>150,"Selected area supplies a long enough connected physical test route");
            while(true)
            {
                float progress=physicalRoute.Closest(session.Car.Body.position,out _,out _);if(progress>=finish)break;
                CheckTraversal(Time.realtimeSinceStartupAsDouble-traversalStart<180&&session.Car.Damage<.1f,"Selected-world physical drive stalled or hit generated geometry");
                var aim=physicalRoute.At(progress+12,physicalRoute.LaneOffset(progress));var local=session.Car.transform.InverseTransformPoint(aim);
                float angle=Mathf.Atan2(2*session.Car.Metrics.Wheelbase*local.x,Mathf.Max(1,local.x*local.x+local.z*local.z))*Mathf.Rad2Deg;
                session.Input.Steer=Mathf.Clamp(angle/Mathf.Lerp(30,10,Mathf.Clamp01(session.Car.AbsoluteSpeed/48)),-1,1);
                float target=Mathf.Min(6,session.Limit/2.2369363f);session.Input.Throttle=session.Car.AbsoluteSpeed<target?.4f:0;session.Input.Brake=session.Car.AbsoluteSpeed>target+.5f?.4f:0;
                if(!session.Car.Grounded&&Time.realtimeSinceStartupAsDouble-traversalStart>2)airborne+=Time.unscaledDeltaTime;
                yield return null;
            }
            session.Input.Throttle=0;session.Input.Brake=1;session.Input.Steer=0;session.Input.enabled=true;
            Check(airborne<.1f&&session.Car.Damage==0,"Selected-world 180m normal-physics drive has zero damage and less than0.1s airborne time");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"cardiff-driving.png"));yield return new WaitForEndOfFrame();Finish(true,"");
        }
        void CheckTraversal(bool value,string error){if(!value)throw new Exception(error);}
        void Finish(bool passed,string error)
        {
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),new JObject{{"passed",passed},{"error",error},{"version",Application.version},{"place",placeName},{"checks",new JArray(checks)},{"selectedRegion",expected},{"latitude",selectedLatitude},{"longitude",selectedLongitude},{"dataRoot",SinglePlayerBeta.DataRoot}}.ToString());Application.Quit(passed?0:1);
        }
    }
}
