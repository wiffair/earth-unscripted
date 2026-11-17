using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.Editor
{
 [InitializeOnLoad] public static class AnimatedWaterVerification
 {
  const string Key="EUWaterRender";static int frames,phase;static Camera camera;static Material water;static RenderTexture target;static Texture2D mask;
  static AnimatedWaterVerification(){if(SessionState.GetBool(Key,false)){GameSession.SuppressAutomaticLaunch=true;EditorApplication.update+=Tick;}}
  public static void Standard(){GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/EarthUnscripted/Settings/EarthUnscriptedPipeline.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;Run();}
  public static void HD(){GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/EarthUnscripted/Graphics/HDPipeline.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;Run();}
  static void Run(){GameSession.SuppressAutomaticLaunch=true;UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
  static void Coverage(float height)
  {
   var values=new float[64*64*2];for(int z=0;z<64;z++)for(int x=24;x<40;x++){int i=(z*64+x)*2;values[i]=height;values[i+1]=1;}
   mask.SetPixelData(values,0);mask.Apply();water.SetTexture("_RoadWaterMask",mask);water.SetVector("_RoadWaterRect",new Vector4(-16,-16,32,32));water.SetFloat("_RoadWaterEnabled",1);
  }
  static void Tick()
  {
   try{
    if(!EditorApplication.isPlaying)return;
    if(!camera){
     var go=new GameObject("Water render camera");camera=go.AddComponent<Camera>();camera.transform.position=new Vector3(0,30,0);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.orthographic=true;camera.orthographicSize=16;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.magenta;if(GameGraphics.HighDefinition){var data=go.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>()??go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();data.clearColorMode=UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;data.backgroundColorHDR=Color.magenta;}target=new RenderTexture(256,256,24);camera.targetTexture=target;
     var vertices=new Vector3[33*33];var indices=new int[32*32*6];for(int z=0;z<=32;z++)for(int x=0;x<=32;x++)vertices[z*33+x]=new Vector3(x-16,0,z-16);for(int z=0;z<32;z++)for(int x=0;x<32;x++){int i=(z*32+x)*6,v=z*33+x;indices[i]=v;indices[i+1]=v+33;indices[i+2]=v+34;indices[i+3]=v;indices[i+4]=v+34;indices[i+5]=v+1;}
     var mesh=new Mesh{vertices=vertices,triangles=indices};mesh.RecalculateNormals();var sea=new GameObject("Animated test sea");sea.AddComponent<MeshFilter>().sharedMesh=mesh;water=GameGraphics.Create(Resources.Load<Shader>("CoastalSurface"));water.SetFloat("_Mode",0);sea.AddComponent<MeshRenderer>().sharedMaterial=water;mask=new Texture2D(64,64,TextureFormat.RGFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
     var volume=new GameObject("Fixed exposure").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=ScriptableObject.CreateInstance<VolumeProfile>();var e=volume.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.Exposure>();e.mode.Override(UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed);e.fixedExposure.Override(0);return;
    }
    if(++frames<15)return;frames=0;
    RenderTexture.active=target;var image=new Texture2D(256,256,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();var centre=image.GetPixel(128,128);var outside=image.GetPixel(32,128);bool Empty(Color c)=>c.r>.3f&&c.b>.3f&&c.g<Mathf.Min(c.r,c.b)*.3f;
    Directory.CreateDirectory("Builds/Verification/Water");File.WriteAllBytes("Builds/Verification/Water/"+GameGraphics.Name+"-"+phase+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
    if(Empty(outside))throw new Exception("Sea outside the road footprint disappeared");
    if(phase==0){if(Empty(centre))throw new Exception("Baseline sea not rendered");Coverage(-1);phase=1;return;}
    if(phase==1){if(!Empty(centre))throw new Exception("Wave envelope protrudes through road coverage");Coverage(3);phase=2;return;}
    if(Empty(centre))throw new Exception("Water beneath an elevated bridge was removed");
    Debug.Log("ANIMATED WATER RENDER PASS "+GameGraphics.Name+" baseline, full wave clearance, below-bridge water");SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(0);
   }catch(Exception e){SessionState.SetBool(Key,false);Debug.LogException(e);EditorApplication.Exit(1);}
  }
 }
}
