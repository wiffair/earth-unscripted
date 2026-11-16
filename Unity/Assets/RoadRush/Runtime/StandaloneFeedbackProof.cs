using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace RoadRush
{
    public sealed class StandaloneFeedbackProof : MonoBehaviour
    {
        string prefix;int phase,frames,checks,errors;GameSession previous,current;float began,deadline;readonly List<float> times=new List<float>();bool captured;
        void Start()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushSwitchProof");prefix=args[at+1];DontDestroyOnLoad(gameObject);Application.runInBackground=true;Screen.SetResolution(2560,1600,FullScreenMode.Windowed);deadline=Time.realtimeSinceStartup+180;Application.logMessageReceived+=Log;
        }
        // Bound diagnostic IO to128 actual observations. The service remains the
        // production PitService transaction; this observer never repairs it.
        int pitRows, pitCredits;float nextPitObservation;bool pitServiceObserved;
        void ObservePit(bool force=false)
        {
            if(!current||!current.Pits||!current.Car||pitRows>=128)return;
            if(!force&&Time.time<nextPitObservation)return;
            nextPitObservation=Time.time+.25f;pitRows++;
            var body=current.Car.Body.position;
            File.AppendAllText(prefix+"-pit-observations.tsv",FormattableString.Invariant(
                $"{Time.frameCount}\t{Time.time:F6}\t{current.Pits.AtBay}\t{current.Pits.Servicing}\t{current.Pits.Remaining:F6}\t{current.Car.AbsoluteSpeed:F6}\t{current.Car.Body.linearVelocity.y:F6}\t{current.Car.Damage:F6}\t{current.Car.StartLocked}\t{current.Car.Grounded}\t{body.x:F6}\t{body.y:F6}\t{body.z:F6}\t{current.Menu}\n"));
            pitServiceObserved|=current.Pits.Servicing;
        }
        void Log(string text,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error)errors++;}
        void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Debug.Log("STANDALONE PASS: "+text);}
        bool Ready(string id)
        {
            var found=FindAnyObjectByType<GameSession>();if(!found||!found.BootReady||ReferenceEquals(found,previous)||LocationLoading.Busy)return false;current=found;Check((current.World.Route.Region?.id??"")==id,"loaded "+id);Check(FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==1,"one active session");current.Input.enabled=false;current.Input.Steer=current.Input.Throttle=current.Input.Brake=0;current.Input.Handbrake=1;return true;
        }
        void Update()
        {
            try
            {
                if(Time.realtimeSinceStartup>deadline)throw new Exception("switch proof timeout phase "+phase);
                if(phase==0)
                {
                    if(!Ready(""))return;current.GetComponent<DrivingUI>().ShowSettings();phase=1;frames=0;
                }
                if(phase==1)
                {
                    if(++frames==40)ScreenCapture.CaptureScreenshot(prefix+"-settings.png");if(frames<45)return;previous=current;current.SelectRegion("dee-valley");phase=2;return;
                }
                if(phase==2)
                {
                    if(!Ready("dee-valley"))return;var region=current.World.Route.Region;var bridge=Array.Find(region.roads,r=>r.bridge&&r.name=="Llangollen Bridge")??Array.Find(region.roads,r=>r.bridge);var point=region.RoadPoint(bridge,bridge.length*.5f,out var forward);current.Car.Recover(point,Quaternion.LookRotation(forward));current.World.GeographicWorld.Refresh(point,true);current.Car.StartLocked=true;current.SetMenu(false);current.View.Mode=3;frames=0;phase=3;return;
                }
                if(phase==3)
                {
                    frames++;
                    if(frames==40)
                    {
                        current.View.enabled=false;var bridge=Array.Find(current.World.Route.Region.roads,r=>r.bridge&&r.name=="Llangollen Bridge");var anchor=current.World.Route.Region.RoadPoint(bridge,bridge.length*.25f,out var forward);var right=Vector3.Cross(Vector3.up,forward).normalized;
                        current.View.transform.SetPositionAndRotation(anchor+right*18-Vector3.up*2.2f,Quaternion.LookRotation(anchor-Vector3.up*.5f-(anchor+right*18-Vector3.up*2.2f),Vector3.up));
                    }
                    if(frames==90)ScreenCapture.CaptureScreenshot(prefix+"-bridge-side.png");
                    if(frames==95)current.View.enabled=true;
                    if(frames>180)times.Add(Time.unscaledDeltaTime);if(frames==300)ScreenCapture.CaptureScreenshot(prefix+"-river-bridge.png");if(frames<305)return;current.SetMenu(true);current.GetComponent<DrivingUI>().ShowWorldMap();frames=0;phase=4;return;
                }
                if(phase==4)
                {
                    if(++frames==25)ScreenCapture.CaptureScreenshot(prefix+"-world.png");if(frames<30)return;Check(current.SaveCareer(0,"Exploration"),"manual career saved");current.GetComponent<DrivingUI>().ShowCareers();frames=0;phase=5;return;
                }
                if(phase==5)
                {
                    if(++frames==25)ScreenCapture.CaptureScreenshot(prefix+"-career.png");if(frames<30)return;previous=current;current.SelectCircuit("silverstone",2,1,1);phase=6;return;
                }
                if(phase==6)
                {
                    if(!Ready("silverstone"))return;Check(current.Racing.Entrants==4,"race opponents recreated after switch");current.Car.ReplicaDamage(30);current.Car.Visual.Dent(current.Car.Body.position+current.Car.transform.forward*2,20);// Recover deliberately adds0.7m of air and zero velocity. Starting
                    // this stationary-service test there can start service before
                    // the fall, then abort above1m/s and leave visited latched.
                    // Fit against actual pad colliders through the same production
                    // supported-rest initializer used by boot/garage, or refuse.
                    Check(current.Car.TryInitializeSupportedRest(current.Pits.Bay,current.World.Route.Rotation(42),out var pitRestReason,out _),"actual pit supported rest: "+pitRestReason);
                    pitCredits=current.Economy.Credits;pitRows=0;pitServiceObserved=false;
                    File.WriteAllText(prefix+"-pit-observations.tsv","frame\ttime\tatBay\tservicing\tremaining\tspeed3D\tverticalSpeed\tdamage\tstartLocked\tgrounded\tx\ty\tz\tmenu\n");
                    current.SetMenu(false);current.View.Mode=4;began=Time.time;captured=false;ObservePit(true);phase=7;return;
                }
                if(phase==7)
                {
                    ObservePit();if(!captured&&Time.time-began>3){ScreenCapture.CaptureScreenshot(prefix+"-pit-service.png");captured=true;}
                    if(Time.time-began<9)return;ObservePit(true);Check(pitServiceObserved,"actual pit service began");Check(current.Car.Damage==0&&!current.Car.StartLocked,"free pit service completes in standalone player");Check(current.Economy.Credits==pitCredits,"pit repair remains free");current.SetMenu(true);previous=current;current.SelectCircuit("spa",0,1,1);phase=8;return;
                }
                if(phase==8)
                {
                    if(!Ready("spa"))return;current.SetMenu(false);current.Car.StartLocked=true;current.View.Mode=0;frames=0;phase=9;return;
                }
                if(phase==9)
                {
                    if(++frames==60)ScreenCapture.CaptureScreenshot(prefix+"-spa.png");if(frames<65)return;current.View.Mode=2;frames=0;phase=10;return;
                }
                if(phase==10)
                {
                    if(++frames==25)ScreenCapture.CaptureScreenshot(prefix+"-cockpit.png");if(frames<30)return;Check(errors==0,"no runtime or shader errors");times.Sort();float median=times.Count>0?times[times.Count/2]*1000:0,p95=times.Count>0?times[(int)(times.Count*.95f)]*1000:0;
                    File.WriteAllText(prefix+"-result.txt",$"PASS {checks} checks\n2560x1600 windowed capture\nstationaryBridgeMedianMs={median:F2}\nstationaryBridgeP95Ms={p95:F2}\nNot an extended driving benchmark\n");Application.Quit();enabled=false;
                }
            }
            catch(Exception e){if(phase==7)try{ObservePit(true);}catch(Exception pitLogError){Debug.LogException(pitLogError);}Debug.LogException(e);File.WriteAllText(prefix+"-result.txt","FAIL "+e);Application.Quit(1);enabled=false;}
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
