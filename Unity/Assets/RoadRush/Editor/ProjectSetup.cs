using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadRush.Editor
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        // Single manually maintained release version; Configure derives the
        // Unity project/build metadata from this value on every build.
        public const string GameVersion = ProductIdentity.Version;
        static ProjectSetup(){EditorApplication.delayCall+=Configure;}
        [MenuItem("Earth Unscripted/Configure project")]
        public static void Configure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            AssetDatabase.Refresh();
            foreach(var asset in AssetDatabase.FindAssets("",new[]{"Assets/Plugins"}))
            {
                var assetPath=AssetDatabase.GUIDToAssetPath(asset);
                if(!(AssetImporter.GetAtPath(assetPath) is PluginImporter plugin))continue;
                bool mac=assetPath.EndsWith(".dylib"),windows=assetPath.EndsWith("RoadRushHaptics.dll")||assetPath.EndsWith("sqlite3.dll");
                if(!mac&&!windows)continue;
                plugin.SetCompatibleWithAnyPlatform(false);
                plugin.SetCompatibleWithEditor(mac?Application.platform==RuntimePlatform.OSXEditor:Application.platform==RuntimePlatform.WindowsEditor);
                plugin.SetEditorData("OS",mac?"OSX":"Windows");plugin.SetEditorData("CPU",mac?"ARM64":"x86_64");
                plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX,mac);
                plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64,windows);
                plugin.SetPlatformData(BuildTarget.StandaloneOSX,"CPU","ARM64");plugin.SetPlatformData(BuildTarget.StandaloneWindows64,"CPU","x86_64");
                plugin.SaveAndReimport();
            }
            Directory.CreateDirectory("Assets/RoadRush/Settings");Directory.CreateDirectory("Assets/RoadRush/Scenes");
            Directory.CreateDirectory("Assets/RoadRush/Resources");
            const string libraryPath="Assets/RoadRush/Resources/SurfaceLibrary.asset";
            var library=AssetDatabase.LoadAssetAtPath<SurfaceLibrary>(libraryPath);
            if(!library)
            {
                library=ScriptableObject.CreateInstance<SurfaceLibrary>();
                var shader=Shader.Find("Universal Render Pipeline/Lit");
                library.Surface=new Material(shader){name="Runtime surface",enableInstancing=true};
                library.Emissive=new Material(shader){name="Runtime emissive",enableInstancing=true};
                library.Emissive.EnableKeyword("_EMISSION");library.Emissive.SetColor("_EmissionColor",Color.white);
                library.Sky=Shader.Find("Skybox/Procedural");library.Rain=Shader.Find("Universal Render Pipeline/Particles/Unlit");
                library.SignFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                AssetDatabase.CreateAsset(library,libraryPath);AssetDatabase.AddObjectToAsset(library.Surface,library);AssetDatabase.AddObjectToAsset(library.Emissive,library);
            }
            library.WorldText=Shader.Find("RoadRush/WorldText");library.Terrain=Shader.Find("RoadRush/GeoTerrain");
            foreach(var id in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/RoadRush/Resources/Textures"}))
            {var texturePath=AssetDatabase.GUIDToAssetPath(id);if(!(AssetImporter.GetAtPath(texturePath) is TextureImporter importer))continue;bool normal=texturePath.Contains("Normal");if(importer.maxTextureSize==2048&&importer.anisoLevel==16&&(!normal||importer.textureType==TextureImporterType.NormalMap))continue;importer.maxTextureSize=2048;importer.anisoLevel=16;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.wrapMode=TextureWrapMode.Repeat;if(normal)importer.textureType=TextureImporterType.NormalMap;importer.SaveAndReimport();}EditorUtility.SetDirty(library);
            const string path="Assets/RoadRush/Settings/RoadRushPipeline.asset";
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if(!pipeline)
            {
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/RoadRush/Settings/RoadRushRenderer.asset");
                pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.msaaSampleCount=4;pipeline.shadowDistance=220;pipeline.supportsHDR=true;
                AssetDatabase.CreateAsset(pipeline,path);
            }
            var pipelineSettings=new SerializedObject(pipeline);pipelineSettings.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue=true;pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            PostProcessSetup.Ensure();
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.enableFrameTimingStats=true;
            PlayerSettings.companyName="Blackwood Games Beta";PlayerSettings.productName=ProductIdentity.BetaName;PlayerSettings.bundleVersion=GameVersion;PlayerSettings.runInBackground=false;
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,SinglePlayerBeta.Identifier);
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var handler=settings.FindProperty("activeInputHandler");if(handler!=null&&handler.intValue!=1){handler.intValue=1;settings.ApplyModifiedPropertiesWithoutUndo();}
            const string scene="Assets/RoadRush/Scenes/Blackwood.unity";
            if(!File.Exists(scene)){var s=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.SaveScene(s,scene);}
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scene,true)};AssetDatabase.SaveAssets();
        }
        [MenuItem("Earth Unscripted/Build Mac")]
        public static void BuildMac(){UnityEditor.OSXStandalone.UserBuildSettings.architecture=UnityEditor.Build.OSArchitecture.ARM64;Build(BuildTarget.StandaloneOSX,"Builds/Mac/Earth Unscripted Beta.app");}
        [MenuItem("Earth Unscripted/Build Windows")]
        public static void BuildWindows(){Build(BuildTarget.StandaloneWindows64,"Builds/Windows/Earth Unscripted Beta.exe");}
        [MenuItem("Earth Unscripted/Build Mac and Windows")]
        public static void BuildAll(){BuildMac();BuildWindows();}
        static void Build(BuildTarget target,string output)
        {
            Configure();Directory.CreateDirectory(Path.GetDirectoryName(output));
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,target))throw new System.InvalidOperationException("Install "+target+" build support in Unity Hub first.");
            var result=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes,output,target,BuildOptions.None);
            if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("Build failed: "+result.summary.result);
            if(target==BuildTarget.StandaloneOSX&&Application.platform==RuntimePlatform.OSXEditor)MacLocalSigning.SignAndVerify(output);
            Debug.Log("Earth Unscripted build ready: "+output);
        }
    }
}
