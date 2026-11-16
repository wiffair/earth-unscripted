#ifndef EARTHUNSCRIPTED_AUTOMATIC_LIGHTING
#define EARTHUNSCRIPTED_AUTOMATIC_LIGHTING
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "WeatherSurface.hlsl"
half4 AutomaticLitRaw(half3 albedo,half3 normalWS,float3 positionWS,float4 positionCS,half smoothness,half fog)
{
    SurfaceData surface=(SurfaceData)0;
    surface.albedo=albedo;surface.alpha=1;surface.occlusion=1;
    surface.specular=half3(.04,.04,.04);surface.smoothness=smoothness;
    surface.normalTS=half3(0,0,1);
    InputData input=(InputData)0;
    input.positionWS=positionWS;input.positionCS=positionCS;
    input.normalWS=normalize(normalWS);input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(positionWS);
    input.shadowCoord=TransformWorldToShadowCoord(positionWS);
    input.bakedGI=SampleSH(input.normalWS);
    input.vertexLighting=VertexLighting(positionWS,input.normalWS);
    input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(positionCS);
    input.shadowMask=half4(1,1,1,1);
    half4 colour=UniversalFragmentPBR(input,surface);
    colour.rgb=MixFog(colour.rgb,fog);return colour;
}
half4 AutomaticLit(half3 albedo,half3 normalWS,float3 positionWS,float4 positionCS,half smoothness,half fog)
{
    float3 colour=albedo;float shine=smoothness;EarthUnscriptedWeather(colour,shine,normalWS,positionWS);
    return AutomaticLitRaw(colour,normalWS,positionWS,positionCS,shine,fog);
}
#endif
