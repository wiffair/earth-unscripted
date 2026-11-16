using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace EarthUnscripted
{
    // Explicit disposable-profile regression fixture. Production startup never
    // constructs it or changes the user's weather, camera or audio sources.
    public sealed class Beta35Verification:MonoBehaviour
    {
        Camera treeCamera;AutomaticSceneryResources art;Material[] nearMaterials;Material farMaterial;
        Mesh billboard;MaterialPropertyBlock nearFade,farFade;bool drawTrees;
        string Output=>SinglePlayerBeta.VerificationOutput;
        void LateUpdate()
        {
            if(!drawTrees)return;
            var model=art.treeModels[0];var matrix=Matrix4x4.TRS(new Vector3(-5,0,35),Quaternion.identity,Vector3.one*(12/model.height))*model.local;
            for(int part=0;part<model.mesh.subMeshCount;part++)
            {
                var material=nearMaterials[Mathf.Min(part,nearMaterials.Length-1)];
                Graphics.DrawMeshInstanced(model.mesh,part,material,new[]{matrix},1,nearFade,ShadowCastingMode.On,true,30,treeCamera);
            }
            Graphics.DrawMeshInstanced(billboard,0,farMaterial,new[]{Matrix4x4.TRS(new Vector3(5,0,35),Quaternion.identity,Vector3.one*12)},1,farFade,ShadowCastingMode.Off,false,30,treeCamera,LightProbeUsage.Off);
        }
        IEnumerator Capture(string name,Action<Texture2D> examine=null)
        {
            yield return null;yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();
            try{examine?.Invoke(image);File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}finally{Destroy(image);}
        }
        static double HalfLight(Texture2D image,bool left)
        {
            var pixels=image.GetPixels32();double sum=0;int count=0;
            for(int y=image.height/5;y<image.height*4/5;y++)for(int x=left?image.width/8:image.width/2;x<(left?image.width/2:image.width*7/8);x++)
            {var p=pixels[y*image.width+x];sum+=(p.r+p.g+p.b)/765.0;count++;}
            return sum/Math.Max(1,count);
        }
        public IEnumerator Exercise(GameSession session,Gamepad pad,Action<bool,string> check)
        {
            check(Output!=null&&SinglePlayerBeta.DataRoot.Contains("/private/tmp/"),"Beta35 checks use a disposable profile");
            var sceneryWorld=session.World.AutomaticWorld.Renderer;
            check(sceneryWorld.DistantBuildingChunks>0&&sceneryWorld.VisibleDistantBuildingChunks>0,"Mapped building massing is visible beyond the short driving-chunk radius");
            var view=session.View;var car=session.Car;bool controller=car.enabled;bool kinematic=car.Body.isKinematic;
            car.enabled=false;car.Body.linearVelocity=car.Body.angularVelocity=Vector3.zero;car.Body.isKinematic=true;
            session.SetMenu(false);view.Mode=0;float originalPitch=view.ChasePitch;
            InputSystem.QueueStateEvent(pad,new GamepadState{rightStick=new Vector2(0,.8f)});
            yield return new WaitForSecondsRealtime(.65f);float raised=view.ChasePitch;
            check(raised>originalPitch+8,"Right joystick raises the actual default chase camera");
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;yield return null;float released=view.ChasePitch;yield return new WaitForSecondsRealtime(.4f);
            check(Mathf.Abs(view.ChasePitch-released)<.01f,"Chase elevation stays after releasing the joystick");
            session.SetMenu(true);InputSystem.QueueStateEvent(pad,new GamepadState{rightStick=Vector2.up});yield return new WaitForSecondsRealtime(.3f);
            check(Mathf.Abs(view.ChasePitch-released)<.01f,"Menu joystick navigation cannot move the paused camera");
            InputSystem.QueueStateEvent(pad,new GamepadState());session.SetMenu(false);
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.RightStick));yield return null;yield return null;
            InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;check(Mathf.Abs(view.ChasePitch)<.01f,"Right-stick click restores default elevation");
            session.Profile.performanceStats=true;check(session.Profile.Save()&&PlayerProfile.Load().performanceStats,"Separate performance-bar choice persists in the real save code");
            yield return Capture("beta35-performance-bar");
            var conditions=session.Conditions;conditions.NetworkConditions(14,0,0,.2f,0);
            foreach(int mode in new[]{6,7,8})
            {
                session.Weather=mode;yield return null;yield return null;
                check(mode==6?conditions.Snow>.99f&&conditions.Snowfall>.7f:mode==7?conditions.Frost>.8f:conditions.Ice>.8f,"Actual environment applies "+new[]{"snow","frost","ice"}[mode-6]+" surface state");
                if(mode==6)
                {yield return new WaitForSecondsRealtime(1);var flakes=FindObjectsByType<ParticleSystem>().FirstOrDefault(p=>p.name=="Local falling snow");check(flakes&&flakes.isPlaying&&flakes.particleCount>0,"Required snow emits visible slow snowflake particles");}
                yield return Capture("beta35-weather-"+mode);
            }
            check(WeatherSurfaceRules.Live(-3,90,-4,1,.8f,.1f,73,0).Snow>.99f,"Live snow code and measured depth produce snow cover");
            check(WeatherSurfaceRules.Live(-2,90,-3,1,0,0,66,0).Ice>.5f,"Live freezing rain produces an icy surface");
            check(WeatherSurfaceRules.Live(-1,90,-2,0,0,0,0,0).Frost>.5f,"Cold humid conditions produce frost");
            check(WeatherSurfaceRules.Live(-5,30,-15,0,0,0,0,0).Frost==0,"Dry cold weather does not invent frost");
            check(WeatherSurfaceRules.Live(12,70,5,1,0,0,61,0).Wet>.99f,"Live rainfall wets the world surfaces");
            session.Weather=0;conditions.NetworkOverride=false;session.Profile.fixedHour=14;session.Profile.clockMode=0;
            // Exercise source interruption and priority under actual scene voice load.
            var engineRoot=new GameObject("Beta35 recorded engine proof");var engine=engineRoot.AddComponent<CamperRecordedEngine>();
            check(engine.Ready,"Recorded camper clips load in the actual player");
            var voices=engine.GetComponentsInChildren<AudioSource>();
            check(voices.Length==2&&voices.All(s=>s.priority==0&&s.loop&&s.spatialBlend==0),"Both camper loops have protected player priority and no distance cutoff");
            engine.Tick(true,2400,.8f,.65f);yield return new WaitForSecondsRealtime(.2f);voices[1].Stop();yield return null;engine.Tick(true,2400,.8f,.65f);yield return null;
            check(voices.All(s=>s.isPlaying),"Camper playback recovers after an actual stopped source");
            voices[1].enabled=false;yield return null;engine.Tick(true,2400,.8f,.65f);yield return null;
            check(voices.All(s=>s.enabled&&s.isPlaying),"Camper playback recovers after a suspended audio source");
            var probes=voices.Select(s=>s.gameObject.AddComponent<BetaRecordedAudioProbe>()).ToArray();
            foreach(var probe in probes)probe.ResetWindow();
            for(double until=Time.realtimeSinceStartupAsDouble+6.5;Time.realtimeSinceStartupAsDouble<until;)
            {engine.Tick(true,2400,.8f,.65f);yield return null;}
            double energy=0;long frames=0;
            for(int index=0;index<probes.Length;index++){double amount=probes[index].Energy(out long count);frames+=count;energy+=count>0?amount/count*voices[index].volume*voices[index].volume:0;}
            check(frames>0&&Math.Sqrt(energy)>.005&&voices.Any(s=>s.volume>.02f&&!s.isVirtual),"Actual recorded engine output stays audible beyond a complete loop after interruption");
            engine.Tick(false,2400,.8f,.65f);check(voices.All(s=>s.volume==0),"Camper still mutes immediately on pause");Destroy(engineRoot);
            // Draw the actual source tree mesh and photographic billboard side
            // by side. This catches the close-up alpha-channel/LOD regression.
            session.SetMenu(true);view.enabled=false;var main=view.GetComponent<Camera>();main.enabled=false;
            foreach(var ui in FindObjectsByType<DrivingUI>())ui.enabled=false;
            foreach(var indicator in FindObjectsByType<BetaIndicator>())indicator.enabled=false;
            art=Resources.Load<AutomaticSceneryResources>("AutomaticWorld/Scenery");
            nearMaterials=art.treeModels[0].materials.Select(GameGraphics.Create).ToArray();
            check(!GameGraphics.HighDefinition||nearMaterials.All(m=>m.renderQueue>=(int)RenderQueue.AlphaTest&&m.enableInstancing),"HD cutout trees retain the native prepass queue and instancing");
            farMaterial=GameGraphics.Create(art.billboardShader,material=>material.enableInstancing=true);farMaterial.SetTexture("_Views",art.treeViews);farMaterial.SetFloat("_Variant",0);
            nearFade=new MaterialPropertyBlock();nearFade.SetVectorArray("_TreeFade",new[]{new Vector4(1,1,1,0)});
            farFade=new MaterialPropertyBlock();farFade.SetVectorArray("_TreeFade",new[]{new Vector4(1,0,0,0)});
            billboard=new Mesh{name="Beta35 exact billboard proof"};billboard.vertices=new[]{new Vector3(-.27f,-.04f,0),new Vector3(.27f,-.04f,0),new Vector3(.27f,1.04f,0),new Vector3(-.27f,1.04f,0)};billboard.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};billboard.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};billboard.triangles=new[]{0,2,1,0,3,2};billboard.RecalculateBounds();
            var cameraObject=new GameObject("Beta35 controlled tree camera");treeCamera=cameraObject.AddComponent<Camera>();treeCamera.cullingMask=1<<30;treeCamera.clearFlags=CameraClearFlags.SolidColor;treeCamera.backgroundColor=Color.black;treeCamera.fieldOfView=40;treeCamera.transform.position=new Vector3(0,6,0);treeCamera.transform.LookAt(new Vector3(0,6,35));
            if(GameGraphics.HighDefinition){var hd=cameraObject.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=Color.black;hd.volumeLayerMask=~0;}
            drawTrees=true;session.Profile.fixedHour=12;farMaterial.SetFloat("_Brightness",SceneryLighting.TreeBrightness(1));if(GameGraphics.HighDefinition)farMaterial.SetFloat("_Radiance",SceneryLighting.TreeRadiance(1));
            yield return new WaitForSecondsRealtime(2);double nearDay=0,farDay=0;
            yield return Capture("beta35-trees-day",image=>{nearDay=HalfLight(image,true);farDay=HalfLight(image,false);});
            check(nearDay>.0005&&farDay>.0005,"Actual instanced close and distant tree representations both render");
            session.Profile.fixedHour=0;farMaterial.SetFloat("_Brightness",SceneryLighting.TreeBrightness(0));if(GameGraphics.HighDefinition)farMaterial.SetFloat("_Radiance",SceneryLighting.TreeRadiance(0));
            yield return new WaitForSecondsRealtime(2);double night=0,nearNight=0;
            yield return Capture("beta35-trees-night",image=>{night=HalfLight(image,false);nearNight=HalfLight(image,true);});
            File.WriteAllText(Path.Combine(Output,"tree-night-metrics.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{nearDay,farDay,nearNight,farNight=night}));
            check(night<farDay*.22&&nearNight<nearDay*.5,"Near and distant night trees remain dark through renderer exposure");
            drawTrees=false;session.Profile.fixedHour=12;treeCamera.cullingMask=0;treeCamera.clearFlags=CameraClearFlags.Skybox;
            if(GameGraphics.HighDefinition)treeCamera.GetComponent<HDAdditionalCameraData>().clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;
            yield return new WaitForSecondsRealtime(2);var sun=AstronomicalSky.Vector(conditions.CelestialPosition.Sun);treeCamera.transform.rotation=Quaternion.LookRotation(sun,Vector3.up);treeCamera.fieldOfView=8;
            yield return new WaitForSecondsRealtime(.5f);double centreBrightness=0;
            yield return Capture("beta35-sun",image=>{var colour=image.GetPixel(image.width/2,image.height/2);centreBrightness=(colour.r+colour.g+colour.b)/3;});
            check(sun.y>0&&centreBrightness>.1,"Rendered midday sun has finite visible brightness rather than a black overflow disc");
            var moon=AstronomicalSky.Vector(conditions.CelestialPosition.Moon);
            if(moon.y>.02f)
            {
                treeCamera.transform.rotation=Quaternion.LookRotation(moon,Vector3.up);yield return new WaitForSecondsRealtime(.4f);double moonBrightness=0;
                yield return Capture("beta35-daytime-moon",image=>{var c=image.GetPixel(image.width/2,image.height/2);moonBrightness=(c.r+c.g+c.b)/3;});
                check(moonBrightness>.06,"Daytime lunar face retains foreground sky rather than painting a black disc");
            }
            File.WriteAllText(Path.Combine(Output,"beta35-feature-metrics.json"),$"{{\"nearTreeDay\":{nearDay.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"farTreeDay\":{farDay.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"farTreeNight\":{night.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"sunBrightness\":{centreBrightness.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
            Destroy(cameraObject);foreach(var material in nearMaterials)Destroy(material);Destroy(farMaterial);Destroy(billboard);
            main.enabled=true;view.enabled=true;car.Body.isKinematic=kinematic;car.enabled=controller;
            foreach(var ui in FindObjectsByType<DrivingUI>())ui.enabled=true;
            session.Profile.fixedHour=14;conditions.NetworkOverride=false;session.Weather=0;session.SetMenu(true);
        }
    }
}
