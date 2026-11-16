using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace RoadRush.Editor
{
    public static class PostProcessSetup
    {
        public const string ProfilePath="Assets/RoadRush/Resources/Atmosphere/RoadRushAtmosphere.asset";
        public const string PackageDataPath="Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
        public static void Ensure()
        {
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/RoadRush/Settings/RoadRushRenderer.asset");
            if(!renderer)throw new InvalidDataException("Earth Unscripted renderer asset is missing.");
            var data=AssetDatabase.LoadAssetAtPath<PostProcessData>(PackageDataPath);
            if(!data)throw new InvalidDataException("Installed URP PostProcessData asset is missing.");
            if(renderer.postProcessData!=data){renderer.postProcessData=data;renderer.SetDirty();EditorUtility.SetDirty(renderer);}
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            if(!AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath))
            {
                var profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="RoadRushAtmosphere";AssetDatabase.CreateAsset(profile,ProfilePath);
                var tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
                var bloom=profile.Add<Bloom>();bloom.intensity.Override(.2f);bloom.threshold.Override(1.2f);
                var vignette=profile.Add<Vignette>();vignette.intensity.Override(.16f);
                foreach(var component in profile.components){component.name=component.GetType().Name;AssetDatabase.AddObjectToAsset(component,profile);EditorUtility.SetDirty(component);}
                EditorUtility.SetDirty(profile);
            }
        }
    }
}
