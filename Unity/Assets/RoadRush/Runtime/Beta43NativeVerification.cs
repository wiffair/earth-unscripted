using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RoadRush
{
    public static class Beta43NativeVerification
    {
        static IEnumerator State(Gamepad pad,GamepadState state)
        {InputSystem.QueueStateEvent(pad,state);yield return null;yield return null;}
        static IEnumerator Ready(Radio.CarRadio radio)
        {double until=Time.realtimeSinceStartupAsDouble+30;while(!radio.Ready||!radio.HasCurrentSegment){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Radio43 decoded segment readiness");yield return null;}}
        public static IEnumerator Exercise(GameSession session,Gamepad pad,Action<bool,string> check)
        {
            var radio=session.GetComponent<Radio.CarRadio>();session.SetMenu(true);string station=(string)radio.Catalog.ActiveStations.First()["id"];
            for(int attempt=0;attempt<20;attempt++)
            {radio.ChangeStation(station);yield return Ready(radio);if(radio.SegmentType=="song"&&radio.Output.clip.length-radio.Output.time>15)break;}
            check(radio.SegmentType=="song"&&radio.Output.clip.length-radio.Output.time>15,"Pause fixture joins a real song away from its natural end");session.Car.StartLocked=true;
            var clip=radio.Output.clip;int count=radio.Played.Count;float position=radio.Output.time;
            var pcm=new float[Math.Min(4096,clip.samples)*clip.channels];
            check(clip.GetData(pcm,Math.Min(clip.samples-pcm.Length/clip.channels,(int)(position*clip.frequency)))&&pcm.All(v=>!float.IsNaN(v)&&!float.IsInfinity(v))&&pcm.Any(v=>Mathf.Abs(v)>.00001f),"Actual randomly joined radio clip is fully decoded finite non-silent PCM");
            for(int i=0;i<12;i++)
            {
                session.SetMenu(false);yield return new WaitForSecondsRealtime(.08f);session.SetMenu(true);
                float paused=radio.Output.time;yield return null;yield return new WaitForSecondsRealtime(.04f);
                check(radio.Output.clip==clip&&radio.Played.Count==count&&Mathf.Abs(radio.Output.time-paused)<.02f,"Pause/resume cycle "+(i+1)+" retains exact decoded clip and segment position");
            }
            check(radio.Output.time>position+.3f,"Resumed programme moves forward rather than restarting");
            float pausedTime=radio.Output.time;session.SetMenu(false);session.SetMenu(true);yield return null;session.SetMenu(false);yield return null;session.SetMenu(true);yield return null;
            check(radio.Output.clip==clip&&radio.Played.Count==count&&radio.Output.time>=pausedTime-.03f,"Immediate same-frame and next-frame menu transitions never retune");
            session.SetMenu(false);float master=session.Profile.volume,volume=radio.Settings.volume;radio.Settings.volume=.5f;
            var up=new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.DpadUp);yield return State(pad,up);
            check(radio.Settings.volume>.5f&&!session.Input.RepairPressed,"LB plus D-pad up changes volume without roadside repair");
            yield return State(pad,new GamepadState());var down=new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.DpadDown);yield return State(pad,down);
            check(!session.Input.ParkingBrakePressed,"Radio volume down does not toggle parking brake");
            float adjusted=radio.Settings.volume;yield return State(pad,new GamepadState().WithButton(GamepadButton.DpadDown));
            check(!session.Input.ParkingBrakePressed&&radio.Settings.volume==adjusted,"Releasing radio modifier while D-pad is held does not leak a parking command");yield return State(pad,new GamepadState());
            var power=new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.North);yield return State(pad,power);
            check(radio.Settings.station==""&&!session.Input.LookBack,"LB plus Y powers off without moving the camera");yield return State(pad,new GamepadState());yield return State(pad,power);
            check(radio.Settings.station==station,"Controller powers on the last selected station");yield return State(pad,new GamepadState());yield return Ready(radio);
            // A second in-memory station exercises real next/previous selection;
            // it is never published to the library or saved as user content.
            const string alternate="qa-radio43-second";
            var row=(JObject)radio.Catalog.stations[station].DeepClone();row["id"]=alternate;row["name"]="QA SECOND STATION";
            radio.Catalog.stations[alternate]=row;radio.Catalog.playlists[alternate]=(JObject)radio.Catalog.playlists[station].DeepClone();
            yield return State(pad,new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.DpadRight));
            check(radio.Settings.station==alternate&&!session.Menu,"Controller tunes another station without opening any menu");yield return State(pad,new GamepadState());
            yield return State(pad,new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.DpadLeft));check(radio.Settings.station==station,"Controller tunes previous station");yield return State(pad,new GamepadState());
            radio.Catalog.stations.Remove(alternate);radio.Catalog.playlists.Remove(alternate);yield return Ready(radio);
            session.SetMenu(true);float menuVolume=radio.Settings.volume;yield return State(pad,up);check(radio.Settings.volume==menuVolume,"Driving radio shortcut is inactive in pause menus");yield return State(pad,new GamepadState());
            session.SetMenu(false);yield return State(pad,new GamepadState{leftStick=new Vector2(.3f,0),rightTrigger=.4f}.WithButton(GamepadButton.LeftShoulder));
            check(session.Input.Steer>.1f&&session.Input.Throttle>.3f,"Steering and throttle remain usable while holding radio modifier");yield return State(pad,new GamepadState());

            var safety=Camera.main.GetComponent<AudioMixSafety>();check(safety,"Main listener has final-mix peak protection");
            long before=safety.ProcessedFrames;var test=new GameObject("Isolated mix overload proof");AudioClip tone=null;
            try
            {
                int rate=AudioSettings.outputSampleRate;tone=AudioClip.Create("Mix overload fixture",rate,1,rate,false);var samples=new float[rate];for(int i=0;i<rate;i++)samples[i]=.85f*Mathf.Sin(2*Mathf.PI*440*i/rate);tone.SetData(samples,0);
                double start=AudioSettings.dspTime+.15;for(int i=0;i<2;i++){var source=test.AddComponent<AudioSource>();source.clip=tone;source.volume=1;source.spatialBlend=0;source.loop=true;source.PlayScheduled(start);}
                yield return new WaitForSecondsRealtime(1.2f);
                check(safety.ProcessedFrames>before&&safety.InputPeak>1.05f,"Actual listener receives an over-unity combined mix in native player");
                check(safety.OutputPeak<=Core.AudioPeakLimiter.Ceiling+.00001f&&safety.LimitedFrames>0,"Actual output mix stays below clipping with linked gain control");
            }
            finally{UnityEngine.Object.Destroy(test);if(tone)UnityEngine.Object.Destroy(tone);}
            radio.Settings.volume=volume;check(session.Profile.volume==master,"Radio operations preserve master/engine preferences");
            session.SetMenu(true);yield return new WaitForSecondsRealtime(.5f);radio.Save();yield return new WaitForSecondsRealtime(.5f);
            UnityEngine.Object.Destroy(radio);yield return null;radio=session.gameObject.AddComponent<Radio.CarRadio>();radio.Initialize(session);yield return Ready(radio);
            var joined=radio.Played.Last();check(radio.Settings.station==station&&joined.startFraction>0&&radio.Output.time>0,"Fresh radio instance restores station but joins broadcast at a random position");
            yield return State(pad,new GamepadState().WithButton(GamepadButton.LeftShoulder));session.SetMenu(false);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"radio-retro-controller.png"));yield return new WaitForEndOfFrame();yield return State(pad,new GamepadState());session.SetMenu(true);
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta43.json"),new JObject{{"passed",true},{"pauseResumeCycles",12},{"startupJoinFraction",joined.startFraction},{"mixInputPeak",safety.InputPeak},{"mixOutputPeak",safety.OutputPeak},{"limitedFrames",safety.LimitedFrames},{"controllerHud",true},{"masterVolumeUnchanged",session.Profile.volume==master}}.ToString());
        }
    }
}
