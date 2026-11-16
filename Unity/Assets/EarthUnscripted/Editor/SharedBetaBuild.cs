using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EarthUnscripted.Editor
{
    // Both players are built from this exact project/assembly. There is no HD
    // copy of driving, menu, traffic, navigation, save or world-generation code.
    public static class SharedBetaBuild
    {
        const string Version=ProductIdentity.Version;
        static readonly string Root="Assets/EarthUnscripted/Graphics";
        public static void SetupOnly(){try{Setup();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void BuildPair()=>Build(false);
        public static void BuildWindowsPair()=>Build(true);
        public static void BuildStandard()=>Build(false,0);
        public static void BuildHD()=>Build(false,1);
        public static void BuildWindowsStandard()=>Build(true,0);
        public static void BuildWindowsHD()=>Build(true,1);
        static void Setup()
        {
            AssetDatabase.ImportAsset("Assets/Resources/EarthUnscripted/Icon.png",ImportAssetOptions.ForceUpdate);
            Directory.CreateDirectory(Root);
            var hd=AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(Root+"/HDPipeline.asset")??ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            hd.name="Earth Unscripted HD";
            var options=hd.currentPlatformRenderPipelineSettings;
            options.supportDecals=false;options.supportVolumetrics=false;options.supportSSGI=true;options.supportSSR=true;options.supportSSAO=true;
            options.supportRayTracing=false;options.supportedLitShaderMode=RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly;
            // Streamed worlds have no prebaked adaptive probe volumes. Use the
            // live sky probe and SSGI instead of allocating an empty APV system.
            options.lightProbeSystem=RenderPipelineSettings.LightProbeSystem.LegacyLightProbes;
            hd.currentPlatformRenderPipelineSettings=options;
            if(!AssetDatabase.Contains(hd))AssetDatabase.CreateAsset(hd,Root+"/HDPipeline.asset");
            var global=typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
            global.GetMethod("Ensure",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{true});
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Graphics/HDSharedLit.shader");
            if(!shader)throw new IOException("Shared HD shader missing");
            // Retain runtime-generated opaque/cutout/transparent keyword variants.
            for(int i=0;i<4;i++)
            {
                string path=$"Assets/Resources/Graphics/HDVariant{i}.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
                material.SetFloat("_AlphaCutoffEnable",i==1||i==3?1:0);
                material.SetFloat("_AlphaCutoff",0);material.SetFloat("_DoubleSidedEnable",i==1||i==3?1:0);
                material.SetFloat("_SurfaceType",i==2?1:0);material.SetFloat("_EarthUnscriptedMode",i==3?5:0);
                material.enableInstancing=true;GameGraphics.Validate(material);EditorUtility.SetDirty(material);
            }
            string skyPath="Assets/Resources/Graphics/HDSkyProfile.asset";
            var skyProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(skyPath);
            if(!skyProfile){skyProfile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(skyProfile,skyPath);}
            if(!skyProfile.TryGet<EarthUnscriptedSky>(out _))
            {var sky=skyProfile.Add<EarthUnscriptedSky>(true);AssetDatabase.AddObjectToAsset(sky,skyProfile);EditorUtility.SetDirty(skyProfile);}
            AssetDatabase.SaveAssets();
        }
        static void Build(bool windows,int selected=-1)
        {
            try
            {
                Setup();
                SharedGraphicsPolicyVerification.Run();Beta36PolicyVerification.Run();Beta37PolicyVerification.Run();Beta38PolicyVerification.Run();
                ControllerFeedbackVerification.Run();Beta32PolicyVerification.Run();RadioPackVerification.Run();Beta42PolicyVerification.Run();Beta43PolicyVerification.Run();BrandPresentationVerification.Run();
                GroundPathSupportVerification.Run();BuildingRoadClearanceVerification.Run();TunnelClearanceVerification.Run();JunctionPavingVerification.Run();TransportControlVerification.Run();
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-earthUnscriptedSharedOutput");
                if(index<0||index+1>=args.Length)throw new ArgumentException("Fresh shared-beta output root required");
                string output=Path.GetFullPath(args[index+1]);if(Directory.Exists(output))throw new IOException("Fresh output root required");Directory.CreateDirectory(output);
                PlayerSettings.companyName=ProductIdentity.Company;PlayerSettings.productName=ProductIdentity.BetaName;
                PlayerSettings.bundleVersion=Version;PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;
                // Trees/facades are generated at runtime, not present in a build
                // scene. Their instancing variants must not be stripped as unused.
                var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
                graphics.FindProperty("m_InstancingStripping").intValue=2;graphics.ApplyModifiedPropertiesWithoutUndo();
                var icon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/EarthUnscripted/Icon.png");
                var sizes=PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone,IconKind.Any);var icons=new Texture2D[sizes.Length];for(int i=0;i<icons.Length;i++)icons[i]=icon;
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone,icons,IconKind.Any);
                // Native startup art is visible before the engine/first scene
                // can render the runtime loading screen (including shader warmup).
                var logo=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/EarthUnscripted/Icon.png");
                if(!logo)throw new IOException("Startup logo sprite missing");
                PlayerSettings.SplashScreen.show=true;PlayerSettings.SplashScreen.showUnityLogo=false;
                PlayerSettings.SplashScreen.backgroundColor=InterfaceTheme.Navy;
                PlayerSettings.SplashScreen.logos=new[]{PlayerSettings.SplashScreenLogo.Create(2,logo)};
                if(!windows)UnityEditor.OSXStandalone.UserBuildSettings.architecture=UnityEditor.Build.OSArchitecture.ARM64;
                foreach(bool hd in new[]{false,true})
                {
                    if(selected>=0&&hd!=(selected==1))continue;
                    var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(hd?Root+"/HDPipeline.asset":"Assets/EarthUnscripted/Settings/EarthUnscriptedPipeline.asset");
                    GraphicsSettings.defaultRenderPipeline=pipeline;
                    QualitySettings.renderPipeline=pipeline;
                    var quality=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
                    var list=quality.FindProperty("m_QualitySettings");
                    for(int q=0;q<list.arraySize;q++)list.GetArrayElementAtIndex(q).FindPropertyRelative("customRenderPipeline").objectReferenceValue=pipeline;
                    quality.ApplyModifiedPropertiesWithoutUndo();
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,hd?SinglePlayerBeta.HDIdentifier:SinglePlayerBeta.Identifier);
                    AssetDatabase.SaveAssets();
                    string label=hd?"Earth Unscripted Beta HD":"Earth Unscripted Beta";
                    string path=Path.Combine(output,label+(windows?"/"+label+".exe":".app"));
                    var inactive=hd?GraphicsSettings.GetSettingsForRenderPipeline<UnityEngine.Rendering.Universal.UniversalRenderPipeline>():GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
                    if(hd)GraphicsSettings.UnregisterRenderPipelineSettings<UnityEngine.Rendering.Universal.UniversalRenderPipeline>();
                    else GraphicsSettings.UnregisterRenderPipelineSettings<HDRenderPipeline>();
                    const string atmosphere="Assets/EarthUnscripted/Resources/Atmosphere/EarthUnscriptedAtmosphere.asset";
                    string parkedAtmosphere=Root+"/StandardAtmosphere.asset";bool parked=false;
                    try
                    {
                        if(hd&&File.Exists(atmosphere))
                        {string moveError=AssetDatabase.MoveAsset(atmosphere,parkedAtmosphere);if(!string.IsNullOrEmpty(moveError))throw new IOException(moveError);parked=true;}
                        AssetDatabase.SaveAssets();
                        var report=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes,path,windows?BuildTarget.StandaloneWindows64:BuildTarget.StandaloneOSX,Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedGraphicsTraceBuild")>=0?BuildOptions.Development:BuildOptions.None);
                        if(report.summary.result!=BuildResult.Succeeded)throw new IOException(label+" build failed");
                    }
                    finally
                    {
                        if(parked){string restoreError=AssetDatabase.MoveAsset(parkedAtmosphere,atmosphere);if(!string.IsNullOrEmpty(restoreError))throw new IOException(restoreError);}
                        if(inactive){if(hd)GraphicsSettings.RegisterRenderPipelineSettings<UnityEngine.Rendering.Universal.UniversalRenderPipeline>(inactive);else GraphicsSettings.RegisterRenderPipelineSettings<HDRenderPipeline>(inactive);}
                        AssetDatabase.SaveAssets();
                    }
                    string destination=windows?Path.Combine(Path.GetDirectoryName(path),"Helpers/AutomaticWorld"):Path.Combine(path,"Contents/Resources/AutomaticWorld");
                    string worker=Path.GetFullPath(windows?"AutomaticWorldWindowsWorker":"AutomaticWorldWorker");
                    foreach(string source in Directory.GetFiles(worker,"*",SearchOption.AllDirectories))
                    {string target=Path.Combine(destination,source.Substring(worker.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,false);}
                    if(!windows)
                    {
                        string infoPath=Path.Combine(path,"Contents/Info.plist");string info=File.ReadAllText(infoPath);
                        info=new Regex(@"(<key>CFBundleVersion</key>\s*<string>)[^<]*(</string>)").Replace(info,"${1}2${2}");
                        File.WriteAllText(infoPath,info);
                    }
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
