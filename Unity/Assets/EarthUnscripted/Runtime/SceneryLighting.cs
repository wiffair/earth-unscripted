using UnityEngine;
namespace EarthUnscripted
{
    public static class SceneryLighting
    {
        // Match astronomical sky radiance before HDRP exposure. A daylight
        // constant at night would turn a photographed tree into an emitter.
        public static float TreeBrightness(float daylight)=>Mathf.Lerp(.0025f,1.3f,Mathf.Clamp01(daylight));
        public static float TreeRadiance(float daylight)=>Mathf.Lerp(.05f,10000,Mathf.Pow(Mathf.Clamp01(daylight),1.3f));
    }
}
