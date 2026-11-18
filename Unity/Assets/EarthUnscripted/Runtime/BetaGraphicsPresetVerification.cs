using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;
namespace EarthUnscripted
{
    // Explicit nonce-scoped acceptance only; never installed in ordinary play.
    public sealed class BetaGraphicsPresetVerification:MonoBehaviour
    {
        readonly List<string> errors=new List<string>();readonly JArray rows=new JArray();
        void Awake(){if(SinglePlayerBeta.VerificationOutput==null)throw new InvalidOperationException("Isolated quality proof required");Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string text,string stack,LogType kind){if(kind==LogType.Error||kind==LogType.Exception)errors.Add(text);}
        static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        IEnumerator Start()
        {
            var work=Exercise();Exception failure=null;
            while(true){object current=null;bool more=false;try{more=work.MoveNext();if(more)current=work.Current;}catch(Exception e){failure=e;}if(failure!=null||!more)break;yield return current;}
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),new JObject{{"passed",passed},{"version",ProductIdentity.Version},{"graphics",GameGraphics.Name},{"failure",failure?.ToString()},{"errors",new JArray(errors)},{"presets",rows}}.ToString());Application.Quit(passed?0:1);
        }
        IEnumerator Exercise()
        {
            GameSession session=null;double deadline=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady){if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Quality boot");yield return null;}
            session.Car.StartLocked=true;session.Car.Body.isKinematic=true;session.Profile.clarity=0;session.Conditions.NetworkConditions(14,0,0,.2f,0);session.SetMenu(false);
            int displayWidth=Screen.width,displayHeight=Screen.height;
            var camera=session.View.GetComponent<Camera>();var scenery=FindFirstObjectByType<AutomaticScenery>();
            int fullWidth=0,fullTrees=0;
            foreach(int q in new[]{3,2,1,0})
            {
                string before=WorldSnapshotCodec.Epoch(session.World.Route.Region);session.ApplyQuality(q);
                Check(WorldSnapshotCodec.Epoch(session.World.Route.Region)==before,"Applying a graphics preset must not change source geometry");
                double until=Time.realtimeSinceStartupAsDouble+3;while(Time.realtimeSinceStartupAsDouble<until)yield return new WaitForEndOfFrame();
                int width,height;
                if(GameGraphics.HighDefinition){var hd=HDCamera.GetOrCreate(camera);width=hd.actualWidth;height=hd.actualHeight;}
                else{var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;width=Mathf.RoundToInt(camera.pixelWidth*pipeline.renderScale);height=Mathf.RoundToInt(camera.pixelHeight*pipeline.renderScale);}
                int trees=scenery?scenery.VisibleTrees:0;
                if(q==3){fullWidth=width;fullTrees=trees;}
                Check(Screen.width==displayWidth&&Screen.height==displayHeight&&camera.pixelWidth==displayWidth,"Graphics preset must preserve native display/UI resolution");
                Check(Mathf.Abs(width/(float)fullWidth-GraphicsQualityPreset.SceneScale(q,0))<.015f,"Actual rendered scene scale must follow the preset");
                if(q<2)Check(trees<=fullTrees,"Reduced scenery must not draw more trees than Ultra");
                rows.Add(new JObject{{"name",GraphicsQualityPreset.Names[q]},{"displayWidth",displayWidth},{"displayHeight",displayHeight},{"sceneWidth",width},{"sceneHeight",height},{"visibleTrees",trees},{"detailedTrees",scenery?scenery.DetailedTrees:0},{"targetFps",Application.targetFrameRate},{"shadowDistance",QualitySettings.shadowDistance}});
                var image=ScreenCapture.CaptureScreenshotAsTexture();try{
                    var pixels=image.GetPixels(Mathf.RoundToInt(image.width*.66f),Mathf.RoundToInt(image.height*.32f),Mathf.RoundToInt(image.width*.06f),Mathf.RoundToInt(image.height*.10f));float luma=0;foreach(var pixel in pixels)luma+=pixel.r*.2126f+pixel.g*.7152f+pixel.b*.0722f;luma/=pixels.Length;
                    ((JObject)rows[rows.Count-1])["roadRoiLuma"]=luma;
                    Check(luma<.78f,"Bridge asphalt must retain its colour on lower presets");
                    File.WriteAllBytes(Path.Combine(SinglePlayerBeta.VerificationOutput,"quality-"+q+".png"),image.EncodeToPNG());}finally{Destroy(image);}
            }
            session.SetMenu(true);yield return null;Check(Application.targetFrameRate==30,"Paused menus use the lighter30FPS budget");
            session.Profile.clarity=2;session.ApplyQuality(3);yield return new WaitForSecondsRealtime(2);yield return new WaitForEndOfFrame();
            if(GameGraphics.HighDefinition)Check(Mathf.Abs(HDCamera.GetOrCreate(camera).actualWidth-displayWidth)<=2,"Legacy detail values must not override the HD preset");
            Check(session.Profile.quality==3,"Detail selection must retain the chosen Ultra preset");
        }
    }
}
