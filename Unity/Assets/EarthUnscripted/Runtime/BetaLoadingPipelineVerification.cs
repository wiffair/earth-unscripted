using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace EarthUnscripted
{
 public sealed class BetaLoadingPipelineVerification:MonoBehaviour
 {
  IEnumerator Start()
  {
   if(SinglePlayerBeta.VerificationOutput==null)throw new InvalidOperationException("Disposable profile required");
   GameSession session=null;double deadline=Time.realtimeSinceStartupAsDouble+30;
   while(!(session=FindFirstObjectByType<GameSession>())&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
   if(!session)throw new Exception("No loading session");
   // Give the clear-only camera time to adopt the HD frame settings.
   yield return new WaitForSecondsRealtime(3);
   var camera=session.GetComponentInChildren<SessionBootClearingCamera>()?.GetComponentInChildren<Camera>();
   var hd=camera?camera.GetComponent<HDAdditionalCameraData>():null;
   var result=new JObject{["mode"]=GameGraphics.Name,["width"]=Screen.width,["height"]=Screen.height,["loadingInterval"]=OnDemandRendering.renderFrameInterval,["loadingUpdateTarget"]=Application.targetFrameRate,["clearOnly"]=camera&&camera.cullingMask==0,["hdMinimal"]=!GameGraphics.HighDefinition||(hd&&hd.antialiasing==HDAdditionalCameraData.AntialiasingMode.None&&hd.volumeLayerMask.value==0&&!hd.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.SSGI)&&!hd.renderingPathCustomFrameSettings.IsEnabled(FrameSettingsField.Postprocess))};
   int before=session.BootClearingCameraRenderCount;double began=Time.realtimeSinceStartupAsDouble;
   ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"loading.png"));
   yield return new WaitForSecondsRealtime(3);
   double renders=(session.BootClearingCameraRenderCount-before)/(Time.realtimeSinceStartupAsDouble-began);result["loadingRenderFps"]=renders;
   session.CancelBoot();yield return new WaitForSecondsRealtime(2);
   result["cancelled"]=session.BootFailed;result["failureUpdateTarget"]=Application.targetFrameRate;result["failureInterval"]=OnDemandRendering.renderFrameInterval;
   ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"cancelled.png"));yield return new WaitForSecondsRealtime(1);
   result["passed"]=(int)result["loadingInterval"]==4&&(int)result["loadingUpdateTarget"]==120&&(bool)result["clearOnly"]&&(bool)result["hdMinimal"]&&renders>0&&renders<40&&session.BootFailed&&Application.targetFrameRate==30;
   File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"loading-proof.json"),result.ToString());Application.Quit((bool)result["passed"]?0:1);
  }
 }
}
