Shader "RoadRush/GeoTerrain"
{
 Properties { _CircuitTurf("Maintained circuit turf",Range(0,1))=0  _BaseMap("Land cover",2D)="white"{} _Grass("Grass",2D)="white"{} _Rock("Rock",2D)="gray"{} _GrassNormal("Grass normal",2D)="bump"{} _RockNormal("Rock normal",2D)="bump"{} }
 SubShader
 {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
   #include "WeatherSurface.hlsl"
   float _CircuitTurf;
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_Grass);SAMPLER(sampler_Grass);TEXTURE2D(_Rock);SAMPLER(sampler_Rock);TEXTURE2D(_GrassNormal);SAMPLER(sampler_GrassNormal);TEXTURE2D(_RockNormal);SAMPLER(sampler_RockNormal);
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;};
   Varyings Vert(Attributes v){Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   half4 Frag(Varyings i):SV_Target
   {
    half3 land=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;half3 normal=normalize(i.normalWS);float2 grassUV=i.positionWS.xz*.17;float2 rockUV=abs(normal.x)>.5?i.positionWS.zy*.14:i.positionWS.xy*.14;
    half3 grass=SAMPLE_TEXTURE2D(_Grass,sampler_Grass,grassUV).rgb;half3 rock=SAMPLE_TEXTURE2D(_Rock,sampler_Rock,rockUV).rgb;half slope=saturate((.86-normal.y)*3.7);half forest=saturate((.27-land.r)*8);half urban=saturate((land.r-.35)*7);
    half3 albedo=lerp(grass,rock,slope);albedo*=lerp(half3(.88,1,.82),half3(.64,.72,.56),forest);albedo=lerp(albedo,rock*.85,urban*.45);albedo*=.88+.12*sin(i.positionWS.x*.013+i.positionWS.z*.017);
    // Optional maintained infield turf. Default0 preserves normal-world art.
    half detail=dot(grass,half3(.2126,.7152,.0722));
    half mowing=lerp(.95,1.05,step(0,sin((i.positionWS.x+i.positionWS.z*.17)*.22)));
    half3 turf=half3(.065,.13,.04)*(.72+detail*1.5)*mowing;
    albedo=lerp(albedo,lerp(turf,rock,slope),_CircuitTurf);
    half3 bump=UnpackNormal(SAMPLE_TEXTURE2D(_GrassNormal,sampler_GrassNormal,grassUV));normal=normalize(normal+half3(bump.x,0,bump.y)*.18*(1-slope));
    float3 weatherColour=albedo;float weatherSmoothness=.05;RoadRushWeather(weatherColour,weatherSmoothness,normal,i.positionWS);albedo=weatherColour;
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));half3 illumination=SampleSH(normal)+sun.color*saturate(dot(normal,sun.direction))*sun.shadowAttenuation;
    #if defined(_ADDITIONAL_LIGHTS)
    uint count=GetAdditionalLightsCount();for(uint index=0;index<count;index++){Light light=GetAdditionalLight(index,i.positionWS);illumination+=light.color*saturate(dot(normal,light.direction))*light.distanceAttenuation*light.shadowAttenuation;}
    #endif
    return half4(MixFog(albedo*max(illumination,.06),i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
