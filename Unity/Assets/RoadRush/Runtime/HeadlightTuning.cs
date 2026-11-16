using UnityEngine;
namespace RoadRush
{
    // Shared URP beam settings for imported and procedural player cars.
    // Intensity is the engine value, not a claim of calibrated vehicle photometry.
    public static class HeadlightTuning
    {
        public const float Intensity=2880,Range=105,SpotAngle=58,InnerSpotAngle=38;
        public static void Configure(Light light)
        {
            light.type=LightType.Spot;light.range=Range;
            light.spotAngle=SpotAngle;light.innerSpotAngle=InnerSpotAngle;
            light.intensity=Intensity;light.color=new Color(1,.94f,.84f);
            light.shadows=LightShadows.Soft;
        }
    }
}
