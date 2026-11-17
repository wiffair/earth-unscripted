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
    public static class Beta42NativeVerification
    {
        public static IEnumerator Exercise(GameSession session,Gamepad pad,Action<bool,string> check)
        {
            var radio=session.GetComponent<Radio.CarRadio>();double end=Time.realtimeSinceStartupAsDouble+60;
            while(!radio||!radio.Ready){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Radio42 readiness");yield return null;}
            session.SetMenu(true);string station=(string)radio.Catalog.ActiveStations.First()["id"];int initial=radio.Played.Count;
            radio.ChangeStation(station);
            while(radio.Played.Count==initial||!radio.Output.clip){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Random tune media loading");yield return null;}
            yield return null;float pausedAt=radio.Output.time;yield return new WaitForSecondsRealtime(.35f);
            check(!radio.Output.isPlaying&&Mathf.Abs(radio.Output.time-pausedAt)<.03f,"Opening/tuning in menu keeps programme paused at its exact position");
            var tuned=radio.Played.Last();check(tuned.startFraction>0&&tuned.startFraction<1&&pausedAt>0,"Tuning actually seeks into its decoded broadcast segment");
            float master=session.Profile.volume;radio.Settings.volume=.5f;yield return null;
            check(Mathf.Abs(radio.Output.volume-Mathf.Clamp01(.5f*master*Radio.CarRadio.MixGain))<.001f&&session.Profile.volume==master,"Radio boost changes radio mix without reducing engine/master setting");
            session.Car.StartLocked=true;InputSystem.QueueStateEvent(pad,new GamepadState());session.SetMenu(false);yield return new WaitForSecondsRealtime(.4f);
            check(radio.Output.isPlaying||radio.Played.Count>initial+1,"Closing menu resumes radio playback");
            session.SetMenu(true);yield return null;yield return null;pausedAt=radio.Output.time;int count=radio.Played.Count;
            yield return new WaitForSecondsRealtime(.4f);check(!radio.Output.isPlaying&&Mathf.Abs(radio.Output.time-pausedAt)<.03f&&radio.Played.Count==count,"Pause menu freezes radio time and segment sequence");
            var preview=radio.ReviewClips.First();radio.Preview(preview);end=Time.realtimeSinceStartupAsDouble+20;
            while(!radio.Output.isPlaying){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Explicit clip preview");yield return null;}
            check(session.Menu&&radio.Output.isPlaying,"Deliberate review preview remains audible while normal programme is paused");radio.StopPreview();yield return null;yield return null;
            session.SetMenu(false);var keyboard=InputSystem.AddDevice<Keyboard>("Beta42 radio shortcuts");keyboard.MakeCurrent();
            float volume=radio.Settings.volume;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F9));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            check(radio.Settings.volume<volume&&session.Profile.volume==master,"F9 adjusts driving-screen radio volume independently");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F7));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;check(radio.Settings.station=="","Driving-screen radio power shortcut turns station off");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F7));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;check(radio.Settings.station==station,"Radio on restores last selected station");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F8));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;check(!string.IsNullOrEmpty(radio.Settings.station),"Tune selects an available station rather than switching it off");
            InputSystem.RemoveDevice(keyboard);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"radio-driving-controls.png"));yield return new WaitForEndOfFrame();
            session.SetMenu(true);ProbeWeather(check);
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta42.json"),new JObject{{"passed",true},{"randomJoinType",tuned.type},{"randomJoinFraction",tuned.startFraction},{"menuPausedAtSeconds",pausedAt},{"radioMixGain",Radio.CarRadio.MixGain},{"masterVolumeUnchanged",session.Profile.volume==master},{"weatherAtlasPublishes",session.Conditions.Shelter?.PublishedAtlases??0}}.ToString());
        }
        static void ProbeWeather(Action<bool,string> check)
        {
            TunnelWeatherIndex.Source S(float a,float b,bool tunnel,float y=0)=>new TunnelWeatherIndex.Source{a=new Vector3(a,y,0),b=new Vector3(b,y,0),width=8,clearance=5,tunnel=tunnel,kind="road"};
            var sources=new List<TunnelWeatherIndex.Source>{S(-220,-200,false),S(200,220,false)};for(int x=-200;x<200;x+=10)sources.Add(S(x,x+10,true));
            var index=new TunnelWeatherIndex(sources);var layers=index.Raster(new Vector2(-256,-256),512,128,0);
            var texture=new Texture2DArray(128,128,4,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};for(int i=0;i<4;i++)texture.SetPixels(layers[i],i);texture.Apply(false,true);
            var oldTexture=Shader.GetGlobalTexture("_EarthUnscriptedTunnelMask");var oldRect=Shader.GetGlobalVector("_EarthUnscriptedTunnelMaskRect");var oldWeather=Shader.GetGlobalVector("_EarthUnscriptedWeather");float oldEnabled=Shader.GetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled"),oldCold=Shader.GetGlobalFloat("_EarthUnscriptedTunnelExtremeCold");
            var shader=Resources.Load<Shader>("TunnelWeatherProbe");check(shader&&shader.isSupported,"Native tunnel weather exposure shader is supported");var material=new Material(shader);var target=new RenderTexture(8,8,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();var read=new Texture2D(8,8,TextureFormat.RGBAFloat,false,true);var oldTarget=RenderTexture.active;
            try
            {
                Shader.SetGlobalTexture("_EarthUnscriptedTunnelMask",texture);Shader.SetGlobalVector("_EarthUnscriptedTunnelMaskRect",new Vector4(-256,-256,512,512));Shader.SetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled",1);Shader.SetGlobalFloat("_EarthUnscriptedTunnelExtremeCold",0);
                Color Draw(Vector3 point){RenderTexture.active=target;GL.Clear(false,true,Color.magenta);material.SetVector("_ProbePosition",point);material.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,3);read.ReadPixels(new Rect(0,0,8,8),0,0);read.Apply();return read.GetPixel(4,4);}
                var inside=Draw(new Vector3(2,.5f,2));var portal=Draw(new Vector3(-198,.5f,2));var above=Draw(new Vector3(2,15,2));
                check(inside.r<.02f&&inside.g<.01f&&inside.b<.01f,"GPU weather mask shelters deep interior from wetness/snow/frost");
                check(portal.r>.75f&&portal.g>.25f,"GPU retains portal splash and winter ingress");
                check(above.r>.99f&&above.g>.99f,"GPU height band keeps overlying road exposed");
                material.SetFloat("_ProbeSurface",1);Shader.SetGlobalVector("_EarthUnscriptedWeather",new Vector4(1,0,0,0));
                var drySurface=Draw(new Vector3(2,.5f,2));var wetSurface=Draw(new Vector3(2,15,2));
                check(drySurface.a<.24f&&wetSurface.a>.7f&&drySurface.r>wetSurface.r,"Actual weather shading keeps tunnel pavement matte/dry while outdoor road has wet sheen");
                material.SetFloat("_ProbeSurface",0);
                Shader.SetGlobalFloat("_EarthUnscriptedTunnelExtremeCold",1);check(Draw(new Vector3(2,.5f,2)).b>.1f,"GPU extreme-cold exception agrees with wheel-weather rules");
            }
            finally
            {
                RenderTexture.active=oldTarget;Shader.SetGlobalVector("_EarthUnscriptedWeather",oldWeather);Shader.SetGlobalTexture("_EarthUnscriptedTunnelMask",oldTexture);Shader.SetGlobalVector("_EarthUnscriptedTunnelMaskRect",oldRect);Shader.SetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled",oldEnabled);Shader.SetGlobalFloat("_EarthUnscriptedTunnelExtremeCold",oldCold);
                UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(material);target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(read);
            }
        }
    }
}
