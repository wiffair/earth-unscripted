using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace EarthUnscripted.Editor
{
    public static class PostProcessConfigurationVerification
    {
        static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception("POSTPROCESS FAIL: "+message);checks++;Debug.Log("POSTPROCESS PASS: "+message);}
        public static void Run()
        {
            GameObject world=null,cameraObject=null;
            try
            {
                PostProcessSetup.Ensure();AssetDatabase.SaveAssets();
                var pipeline=GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/EarthUnscripted/Settings/EarthUnscriptedRenderer.asset");var data=AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessSetup.PackageDataPath);
                Check(pipeline&&pipeline.supportsHDR&&renderer&&renderer.postProcessData==data&&data,"active HDR pipeline renderer binds official installed PostProcessData");
                var serialized=new SerializedObject(pipeline);int index=serialized.FindProperty("m_DefaultRendererIndex").intValue;Check(serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue==renderer,"actual default renderer data is the corrected Earth Unscripted asset");
                Check(data.shaders!=null&&data.shaders.uberPostPS&&data.shaders.uberPostPS.isSupported&&data.shaders.lutBuilderLdrPS&&data.shaders.lutBuilderHdrPS&&data.shaders.bloomPS,"official postprocess resource shaders are bound and Uber is GPU-supported");
                var dependencies=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(renderer),true);Check(dependencies.Contains(PostProcessSetup.PackageDataPath),"postprocess resource asset is a real serialized build dependency");
                var source=Resources.Load<VolumeProfile>(WorldAtmosphere.ResourcePath);Check(source&&AssetDatabase.GetAssetPath(source)==PostProcessSetup.ProfilePath&&source.components.Count==3,"native Resources profile contains only original three effects");
                Check(source.TryGet<Tonemapping>(out var tone)&&tone.mode.overrideState&&tone.mode.value==TonemappingMode.ACES,"original ACES mode retained");
                Check(source.TryGet<Bloom>(out var bloom)&&bloom.intensity.overrideState&&Mathf.Approximately(bloom.intensity.value,.2f)&&Mathf.Approximately(bloom.threshold.value,1.2f),"original bloom intensity and threshold retained");
                Check(source.TryGet<Vignette>(out var vignette)&&Mathf.Approximately(vignette.intensity.value,.16f)&&!source.Has<ColorAdjustments>(),"original vignette retained; no new exposure or contrast grading");
                string sourceBefore=EditorJsonUtility.ToJson(source);var originals=source.components.ToArray();var componentBefore=originals.Select(x=>EditorJsonUtility.ToJson(x)).ToArray();
                world=new GameObject("Owned atmosphere fixture");var owner=WorldAtmosphere.Install(world.transform);var owned=owner.OwnedProfile;var copies=owned.components.ToArray();
                Check(owner.Volume&&owner.Volume.transform.parent==world.transform&&owner.Volume.isGlobal&&owner.Volume.weight==1&&owner.Volume.priority==0,"one world-parented global volume with original blend settings");
                Check(owner==WorldAtmosphere.Install(world.transform)&&world.GetComponentsInChildren<Volume>(true).Length==1,"repeat install does not duplicate world volumes");
                Check(owned!=source&&copies.Length==originals.Length+1&&owned.Has<ColorAdjustments>()&&copies.All(x=>!originals.Contains(x))&&owner.Volume.profile==owned,"profile and authored components deep-cloned with one owned exposure component");
                owned.TryGet<ColorAdjustments>(out var adaptation);
                owner.SetDaylight(0);Check(Mathf.Approximately(adaptation.postExposure.value,2),"owned night adaptation applies2EV without resource mutation");
                owner.SetDaylight(1);Check(Mathf.Approximately(adaptation.postExposure.value,0),"full daylight returns to original exposure");
                owned.TryGet<Tonemapping>(out var mutableTone);mutableTone.mode.Override(TonemappingMode.None);Check(tone.mode.value==TonemappingMode.ACES,"owned edits cannot mutate the Resources profile");
                cameraObject=new GameObject("Renderer binding fixture");var camera=cameraObject.AddComponent<Camera>();var extra=cameraObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=true;
                var actual=extra.scriptableRenderer;var property=typeof(UniversalRenderer).GetProperty("postProcessEnabled",BindingFlags.Instance|BindingFlags.NonPublic);Check(actual is UniversalRenderer&&property!=null&&(bool)property.GetValue(actual),"actual camera UniversalRenderer reports postProcessEnabled true");
                UnityEngine.Object.DestroyImmediate(world);world=null;Check(!owner&&!owned&&copies.All(x=>!x),"world destruction releases only the owned volume/profile/components");
                Check(source&&originals.All(x=>x)&&EditorJsonUtility.ToJson(source)==sourceBefore&&originals.Select(x=>EditorJsonUtility.ToJson(x)).SequenceEqual(componentBefore),"shared authored profile and components survive unchanged");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/postprocess-configuration.txt","PASS "+checks+" postprocess resource/active-renderer/profile ownership checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{if(world)UnityEngine.Object.DestroyImmediate(world);if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
