using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace RoadRush
{
    [Serializable,VolumeComponentMenu("Sky/Earth Unscripted sky"),SupportedOnRenderPipeline(typeof(HDRenderPipelineAsset)),SkyUniqueID(38744)]
    public sealed class RoadRushSky:SkySettings
    {
        public override Type GetSkyRendererType()=>typeof(RoadRushSkyRenderer);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash=base.GetHashCode();var source=AstronomicalSky.CurrentMaterial;
                if(source&&source.shader.name.Contains("Astronomical"))
                {hash=hash*23+Mathf.RoundToInt(source.GetFloat("_Daylight")*100);hash=hash*23+Mathf.RoundToInt(source.GetFloat("_Clouds")*100);var direction=source.GetVector("_SunDirection");hash=hash*23+Mathf.RoundToInt(direction.x*1000);hash=hash*23+Mathf.RoundToInt(direction.y*1000);hash=hash*23+Mathf.RoundToInt(direction.z*1000);}
                return hash;
            }
        }
    }
}
