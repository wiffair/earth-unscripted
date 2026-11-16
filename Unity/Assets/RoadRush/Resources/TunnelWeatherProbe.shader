Shader "Hidden/RoadRush/TunnelWeatherProbe"
{
 SubShader { Pass {
  ZTest Always ZWrite Off Cull Off
  HLSLPROGRAM
  #pragma target 3.5
  #pragma vertex Vert
  #pragma fragment Frag
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Assets/RoadRush/Shaders/WeatherSurface.hlsl"
  float4 _ProbePosition;float _ProbeSurface;
  float4 Vert(uint id:SV_VertexID):SV_POSITION{return GetFullScreenTriangleVertexPosition(id);}
  float4 Frag():SV_Target{if(_ProbeSurface<.5)return float4(RoadRushWeatherExposure(_ProbePosition.xyz),1);float3 colour=.5;float shine=.22;RoadRushWeather(colour,shine,float3(0,1,0),_ProbePosition.xyz);return float4(colour,shine);}
  ENDHLSL
 } }
}
