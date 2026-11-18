using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
 [InitializeOnLoad] public static class SignReadabilityVerification
 {
  const string Key="EUSignRender";static int frames;static RenderTexture rt;static Camera camera;static GameObject root;static Transform speed;
  static SignReadabilityVerification(){if(!SessionState.GetBool(Key,false))return;GameSession.SuppressAutomaticLaunch=true;EditorApplication.update+=Tick;}
  public static void RunStandard(){SetPipeline("Assets/EarthUnscripted/Settings/EarthUnscriptedPipeline.asset");Run();}
  public static void RunHD(){SetPipeline("Assets/EarthUnscripted/Graphics/HDPipeline.asset");Run();}
  static void SetPipeline(string path){var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;}
  static void Run(){GameSession.SuppressAutomaticLaunch=true;UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
  static void Tick()
  {
   try
   {
    if(!EditorApplication.isPlaying)return;
    if(!camera)
    {
     GameSession.SuppressAutomaticLaunch=true;root=new GameObject("Isolated sign lettering proof");
     var sign=StandardTrafficSigns.Place(root.transform,"speed","20","US");sign.transform.position=Vector3.zero;speed=sign.transform;
     var stop=StandardTrafficSigns.Place(root.transform,"stop","","US");stop.transform.position=new Vector3(1,0,0);
     var canadian=StandardTrafficSigns.Place(root.transform,"speed","50","CA");canadian.transform.position=new Vector3(2,0,0);
     camera=new GameObject("Proof camera").AddComponent<Camera>();camera.transform.position=new Vector3(1,0,4);camera.transform.rotation=Quaternion.Euler(0,180,0);camera.orthographic=true;camera.orthographicSize=.60f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.3f,.4f,.5f);
     if(GameGraphics.HighDefinition){var data=camera.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();data.clearColorMode=UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;data.backgroundColorHDR=camera.backgroundColor;data.volumeLayerMask=~0;data.antialiasing=UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;}

     rt=new RenderTexture(1500,600,24);camera.targetTexture=rt;
     var light=new GameObject("Proof light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=3;light.transform.rotation=Quaternion.Euler(0,180,0);
     var volume=new GameObject("Proof exposure").AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;volume.sharedProfile=ScriptableObject.CreateInstance<VolumeProfile>();var exposure=volume.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.Exposure>();exposure.mode.Override(UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed);exposure.fixedExposure.Override(12);
     return;
    }
    if(++frames==20)
    {
     var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");for(int size=12;size<100;size+=3)font.RequestCharactersInTexture("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",size,FontStyle.Normal);
    }
    if(frames<60)return;
    RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
    string output="Builds/Verification/SignWords";Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,GameGraphics.Name+".png"),image.EncodeToPNG());
    var pixels=image.GetPixels();var centre=camera.WorldToViewportPoint(speed.position+Vector3.up*.20f);int cx=Mathf.RoundToInt(centre.x*rt.width),cy=Mathf.RoundToInt(centre.y*rt.height);float maximum=0;int dark=0,total=0;
    int halfWidth=110,halfHeight=54;
    for(int y=cy-halfHeight;y<cy+halfHeight;y++)for(int x=cx-halfWidth;x<cx+halfWidth;x++)maximum=Mathf.Max(maximum,pixels[y*rt.width+x].grayscale);
    for(int y=cy-halfHeight;y<cy+halfHeight;y++)for(int x=cx-halfWidth;x<cx+halfWidth;x++){total++;if(pixels[y*rt.width+x].grayscale<maximum*.9f)dark++;}
    var atlas=(Texture2D)typeof(StandardTrafficSigns).GetField("letteringAtlas",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var dynamic=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf").material.mainTexture;
    string report=JsonUtility.ToJson(new Result{mode=GameGraphics.Name,headingDarkPixels=dark,headingPixels=total,panelMaximum=maximum,liveAtlas=atlas==dynamic,passed=dark>120&&maximum>.1f},true);File.WriteAllText(Path.Combine(output,GameGraphics.Name+".json"),report);Debug.Log("SIGN WORDS "+report);
    SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(dark>120&&maximum>.1f?0:1);
   }
   catch(Exception error){SessionState.SetBool(Key,false);Debug.LogException(error);EditorApplication.Exit(1);}
  }
  [Serializable] sealed class Result{public string mode;public int headingDarkPixels,headingPixels;public float panelMaximum;public bool liveAtlas,passed;}
 }
}
