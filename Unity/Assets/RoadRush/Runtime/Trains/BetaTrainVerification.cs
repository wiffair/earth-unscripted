using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Opt-in real-world train traversal with a disposable beta profile.</summary>
    public sealed class BetaTrainVerification : MonoBehaviour
    {
        readonly List<string> errors=new List<string>(),checks=new List<string>();
        RailTrainSystem observedTrain;readonly JArray motion=new JArray();
        string Output=>SinglePlayerBeta.VerificationOutput;
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool condition,string message)
        {File.WriteAllText(Path.Combine(Output,"train-progress.json"),new JObject{["check"]=message,["passed"]=condition}.ToString());if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
        IEnumerator Start()
        {
            string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0)
            {
                bool more=false;object current=null;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception error){failure=error.ToString();}
                if(failure!=null)break;if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(Output,"result.json"),new JObject{["passed"]=passed,["failure"]=failure,["errors"]=new JArray(errors),["checks"]=new JArray(checks),["version"]=Application.version,["motion"]=motion,["trainStatus"]=observedTrain?observedTrain.Status:"disposed",["scope"]="Actual standalone UK loaded-track two-car simulated service; not a complete railway dispatch, station or live timetable system."}.ToString());Application.Quit(passed?0:1);
        }
        IEnumerator Exercise()
        {
            GameSession session=null;double deadline=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Train startup");yield return null;}
            var trains=session.GetComponent<RailTrainSystem>();observedTrain=trains;Check(trains!=null,"Beta session owns the train service");
            session.Profile.trainServiceMode=0;session.Profile.simulatedTrains=true;session.Profile.volume=0;session.Car.StartLocked=true;session.SetMenu(false);
            deadline=Time.realtimeSinceStartupAsDouble+90;
            while(!trains.Running)
            {
                File.WriteAllText(Path.Combine(Output,"train-progress.json"),new JObject{["status"]=trains.Status}.ToString());
                if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("No train: "+trains.Status);yield return null;
            }
            Check(!string.IsNullOrEmpty(trains.SourceWay),"Train retains actual prepared source-way identity");
            Check(trains.Visual.Bodies.Count==2&&trains.Visual.MeshCount>=15,"Two carriages reuse owned component and stock body LOD meshes");
            foreach(var body in trains.Visual.Bodies)Check(body.isKinematic&&body.GetComponent<BoxCollider>()!=null,"Train carriage has a physical kinematic collision body");
            double began=trains.TravelledMetres;float finish=Time.time+16;float peakSpeed=0,nextSample=0;
            while(Time.time<finish)
            {
                peakSpeed=Mathf.Max(peakSpeed,trains.SpeedMps);
                if(Time.time>=nextSample){nextSample=Time.time+1;motion.Add(new JObject{["head"]=trains.HeadMetres,["travelled"]=trains.TravelledMetres,["speed"]=trains.SpeedMps,["length"]=trains.SectionLengthMetres,["status"]=trains.Status,["way"]=trains.SourceWay});}
                yield return null;
            }
            Check(trains.TravelledMetres-began>15&&peakSpeed>1,"Train travels along real prepared track");
            var body0=trains.Visual.Bodies[0];session.View.enabled=false;
            var camera=Camera.main;camera.transform.position=body0.position-body0.transform.forward*25+Vector3.up*12+body0.transform.right*9;camera.transform.LookAt(body0.position+body0.transform.forward*8+Vector3.up*1.5f);
            var ui=FindFirstObjectByType<DrivingUI>();ui.enabled=false;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(Output,"train-cardiff-running.png"));yield return null;yield return new WaitForEndOfFrame();
            camera.transform.position=body0.position+Vector3.up*30;camera.transform.LookAt(body0.position,body0.transform.forward);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(Output,"train-cardiff-overhead.png"));yield return null;yield return new WaitForEndOfFrame();
            session.SetMenu(true);double paused=trains.HeadMetres;yield return new WaitForSecondsRealtime(.5f);Check(trains.HeadMetres==paused,"Pause menu stops train simulation");
            session.Profile.simulatedTrains=false;yield return null;Check(!trains.Running,"Train setting removes its active scenery/collisions");
            session.Profile.simulatedTrains=true;session.SetMenu(false);yield return new WaitForSecondsRealtime(.2f);Check(trains.Running,"Re-enabling the service restores the bounded train");
            session.SetMenu(true);ui.enabled=true;
            // A stationary real car on the supported track must receive an impact
            // event and controller motor pulse when the kinematic train reaches it.
            session.Car.StartLocked=true;session.Profile.rumble=1;
            session.SetMenu(false);float movingDeadline=Time.realtimeSinceStartup+35;
            while((trains.SpeedMps<3||!trains.TryPointAhead(30,out _,out _))&&Time.realtimeSinceStartup<movingDeadline)yield return null;
            Check(trains.SpeedMps>=3&&trains.TryPointAhead(30,out _,out _),"Collision check starts during supported travel, outside the planned end dwell");
            Check(trains.TryPointAhead(15,out var impactPoint,out var impactRotation),"Collision fixture uses supported track ahead");
            session.Car.Recover(impactPoint,impactRotation);
            int impactRevision=session.Car.HapticImpactRevision;float peak=0,peakCarSpeed=0,peakTrainSpeed=0;
            float impactDeadline=Time.realtimeSinceStartup+12;session.SetMenu(false);
            while((session.Car.HapticImpactRevision==impactRevision||!(session.Car.LastImpactObject??"").StartsWith("Carriage",StringComparison.Ordinal))&&Time.realtimeSinceStartup<impactDeadline)
            {yield return new WaitForEndOfFrame();peak=Mathf.Max(peak,session.Feedback.LastMotors.High);peakCarSpeed=Mathf.Max(peakCarSpeed,session.Car.AbsoluteSpeed);foreach(var body in trains.Visual.Bodies)peakTrainSpeed=Mathf.Max(peakTrainSpeed,body.linearVelocity.magnitude);}
            bool carriageContact=(session.Car.LastImpactObject??"").StartsWith("Carriage",StringComparison.Ordinal);
            for(int frame=0;frame<4;frame++){yield return new WaitForEndOfFrame();peak=Mathf.Max(peak,session.Feedback.LastMotors.High);peakCarSpeed=Mathf.Max(peakCarSpeed,session.Car.AbsoluteSpeed);foreach(var body in trains.Visual.Bodies)peakTrainSpeed=Mathf.Max(peakTrainSpeed,body.linearVelocity.magnitude);}
            Check(session.Car.HapticImpactRevision!=impactRevision&&session.Car.HapticImpactSpeed>.6f&&carriageContact,"Actual train/car contact reaches impact feedback");
            Check(peak>.15f,"Actual collision generates a strong controller motor command");
            Check(peakCarSpeed<60&&peakTrainSpeed<45,"Train/car collision stays within physical speed bounds");
            session.SetMenu(true);var trainRoot=trains.Visual.Root;Destroy(trains);
            yield return null;yield return null;Check(!trainRoot,"Train runtime disposes its actual scene objects");
            Check(errors.Count==0,"No native train rendering/physics exceptions");
        }
    }
}
