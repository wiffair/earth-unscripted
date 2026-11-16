Shader "EarthUnscripted/Surface Tracks"
{
 Properties { [MainColor] _BaseColor("Colour",Color)=(1,1,1,1) _Surface("Surface",Float)=1 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Offset -1,-1
  Pass
  {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile _ _FORWARD_PLUS
   #include "Assets/EarthUnscripted/Shaders/AutomaticLighting.hlsl"
   #include "Assets/EarthUnscripted/Shaders/TyreTrackSurface.hlsl"
   struct A {float3 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float4 lifetime:TEXCOORD1;float4 colour:COLOR;};
   struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float4 lifetime:TEXCOORD3;half4 colour:COLOR;half fog:TEXCOORD5;};
   V Vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);o.uv=i.uv;o.lifetime=i.lifetime;o.colour=i.colour;o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   half4 Frag(V i):SV_Target
   {
    float3 colour;float opacity,shine;EarthUnscriptedTrack(i.uv,i.lifetime.xy,i.colour,colour,opacity,shine);
    if(i.colour.a>.5)opacity*=saturate(_EarthUnscriptedWeather.w*3);
    clip(opacity-.002);half4 lit=AutomaticLitRaw(colour,normalize(i.normal),i.world,i.positionCS,shine,i.fog);return half4(lit.rgb,opacity);
   }
   ENDHLSL
  }
 }
}
