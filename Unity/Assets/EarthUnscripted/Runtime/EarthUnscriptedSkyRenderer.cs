using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace EarthUnscripted
{
    public sealed class EarthUnscriptedSkyRenderer:SkyRenderer
    {
        public static int ScreenDraws,BakeDraws;public static string SourceShader;
        Material material;readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        public override void Build(){material=CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("Graphics/HDCelestialSky"));}
        public override void Cleanup(){CoreUtils.Destroy(material);}
        public override void RenderSky(BuiltinSkyParameters context,bool cubemap,bool sunDisk)
        {
            if(cubemap)BakeDraws++;else ScreenDraws++;
            var source=AstronomicalSky.CurrentMaterial;SourceShader=source?source.shader.name:"missing";
            if(source&&source.shader.name.Contains("Astronomical"))
            {
                foreach(string name in new[]{"_Daylight","_Clouds","_Pollution","_MoonPhase","_Latitude","_Sidereal","_UsePanorama"})material.SetFloat(name,source.GetFloat(name));
                material.SetVector("_SunDirection",source.GetVector("_SunDirection"));material.SetVector("_MoonDirection",source.GetVector("_MoonDirection"));
                material.SetTexture("_MainTex",source.GetTexture("_MainTex"));
            }
            properties.SetMatrix("_PixelCoordToViewDirWS",context.pixelCoordToViewDirMatrix);
            CoreUtils.DrawFullScreen(context.commandBuffer,material,properties,cubemap?0:1);
        }
    }
}
