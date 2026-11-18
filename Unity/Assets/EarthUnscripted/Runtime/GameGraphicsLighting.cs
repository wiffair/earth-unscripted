using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EarthUnscripted
{
    [DefaultExecutionOrder(19000)]
    public sealed class GameGraphicsLighting:MonoBehaviour
    {
        Volume volume;GlobalIllumination bounce;Exposure exposure;ScreenSpaceAmbientOcclusion ambient;HDShadowSettings shadowSettings;int lastQuality=-1,lastClarity=-1;double nextLights;GameSession session;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if(!GameGraphics.HighDefinition)return;
            var root=new GameObject("HD graphics services");DontDestroyOnLoad(root);root.AddComponent<GameGraphicsLighting>();
        }
        void Awake()
        {
            volume=gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=50;
            volume.profile=Instantiate(Resources.Load<VolumeProfile>("Graphics/HDSkyProfile"));
            volume.profile.Add<VisualEnvironment>(true).skyType.Override(SkySettings.GetUniqueID<EarthUnscriptedSky>());
            var sky=volume.profile.components.Find(c=>c is EarthUnscriptedSky) as EarthUnscriptedSky;// The sky hash tracks daylight/cloud/direction. Realtime's scaled
            // clock leaves a daytime light probe active when time changes in a
            // paused menu, then night exposure makes trees glow white.
            sky.updateMode.Override(EnvironmentUpdateMode.OnChanged);
            // Pausing stops HDRP's progressive adaptation clock. A daylight-
            // aware exposure also gives a valid first/menu frame immediately.
            exposure=volume.profile.Add<Exposure>(true);exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(14);
            volume.profile.Add<UnityEngine.Rendering.HighDefinition.Tonemapping>(true).mode.Override(UnityEngine.Rendering.HighDefinition.TonemappingMode.ACES);
            bounce=volume.profile.Add<GlobalIllumination>(true);bounce.enable.Override(true);bounce.tracing.Override(RayCastingMode.RayMarching);
            // Reconstruct diffuse bounce from half-resolution rays. The main
            // scene, UI and display stay at the requested resolution.
            bounce.fullResolutionSS.Override(false);
            ambient=volume.profile.Add<ScreenSpaceAmbientOcclusion>(true);ambient.intensity.Override(.35f);
            shadowSettings=volume.profile.Add<HDShadowSettings>(true);
        }
        void Update()
        {
            if(!session)session=FindFirstObjectByType<GameSession>();
            int level=session?.Profile==null?2:GraphicsQualityPreset.Clamp(session.Profile.quality);
            int clarity=0; // HDRP scales down reliably; extra supersampling is Standard-only.
            bounce.enable.Override(true);bounce.quality.Override(level<2?0:1);ambient.intensity.Override(level==0?0:level==1?.2f:level==2?.3f:.35f);
            shadowSettings.maxShadowDistance.Override(GraphicsQualityPreset.ShadowRange(level));
            if(level!=lastQuality||clarity!=lastClarity)
            {
                var asset=GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
                if(asset)
                {
                    var settings=asset.currentPlatformRenderPipelineSettings;var resolution=settings.dynamicResolutionSettings;
                    resolution.enabled=true;resolution.forceResolution=true;
                    resolution.forcedPercentage=GraphicsQualityPreset.SceneScale(level,clarity)*100;
                    resolution.minPercentage=70;resolution.maxPercentage=150;resolution.dynResType=DynamicResolutionType.Software;
                    resolution.advancedUpscalerNames=new System.Collections.Generic.List<string>();
                    resolution.upsampleFilter=DynamicResUpscaleFilter.ContrastAdaptiveSharpen;
                    settings.dynamicResolutionSettings=resolution;asset.currentPlatformRenderPipelineSettings=settings;
                }
                lastQuality=level;lastClarity=clarity;
            }
            if(Time.realtimeSinceStartupAsDouble<nextLights)return;nextLights=Time.realtimeSinceStartupAsDouble+.5;
            foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var data=camera.GetComponent<HDAdditionalCameraData>()??camera.gameObject.AddComponent<HDAdditionalCameraData>();
                data.allowDynamicResolution=camera.cameraType==CameraType.Game;camera.allowDynamicResolution=data.allowDynamicResolution;
                data.volumeLayerMask=~0;if(camera==Camera.main)data.clearColorMode=HDAdditionalCameraData.ClearColorMode.Sky;data.antialiasing=HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                data.customRenderingSettings=true;data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.SSGI,true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.SSGI]=true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ExposureControl,true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ExposureControl]=true;
            }
            foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if(!light.GetComponent<HDAdditionalLightData>())light.gameObject.AddComponent<HDAdditionalLightData>();
        }
        void LateUpdate()
        {
            if(!session||session.World==null)return;
            var sun=session.World.Sun;var moon=session.World.Moon;
            bool moonDominant=moon&&(!sun||moon.intensity>sun.intensity);
            bool shadows=session.Profile==null||session.Profile.quality>0;
            if(sun)sun.shadows=shadows&&!moonDominant?LightShadows.Soft:LightShadows.None;
            if(moon)moon.shadows=shadows&&moonDominant?LightShadows.Soft:LightShadows.None;
            float daylight=session.Conditions?session.Conditions.Daylight:1;
            exposure.fixedExposure.Override(Mathf.Lerp(2,14,Mathf.Pow(daylight,.7f)));
        }
        void OnDestroy(){if(volume&&volume.profile)Destroy(volume.profile);}
    }
}
