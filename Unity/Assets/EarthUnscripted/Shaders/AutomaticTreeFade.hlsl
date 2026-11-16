#ifndef EARTHUNSCRIPTED_TREE_FADE
#define EARTHUNSCRIPTED_TREE_FADE
UNITY_INSTANCING_BUFFER_START(TreeVisibility)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TreeFade)
UNITY_INSTANCING_BUFFER_END(TreeVisibility)
// Complementary screen-door coverage keeps both representations in place
// through a 25 m LOD transition. New distant data fades in over five seconds;
// the outer 600 m also fades instead of exposing a hard draw-radius edge.
void AutomaticTreeClip(float2 pixel)
{
    float4 fade=UNITY_ACCESS_INSTANCED_PROP(TreeVisibility,_TreeFade);
    float noise=frac(52.9829189*frac(dot(floor(pixel),float2(.06711056,.00583715))));
    clip(fade.z>.5 ? fade.y-noise : noise-fade.y);
    float outerNoise=frac(52.9829189*frac(dot(floor(pixel)+float2(37,71),float2(.06711056,.00583715))));
    clip(fade.x-outerNoise);
}
#endif
