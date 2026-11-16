Shader "RoadRushHD/Particles" {
 Properties { [MainTexture] _MainTex("Opacity",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader { Tags {"RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass { Tags {"LightMode"="ForwardOnly"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
 #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);float4 _BaseColor;float _RoadRushParticleRadiance;
 struct A {float3 positionOS:POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
 struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
 V Vert(A i){V o;o.positionCS=TransformWorldToHClip(TransformObjectToWorld(i.positionOS));o.uv=i.uv;o.colour=i.colour*_BaseColor;return o;}
 float4 Frag(V i):SV_Target {float2 p=i.uv*2-1;float envelope=saturate(1-dot(p,p));float alpha=i.colour.a*SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a*envelope*envelope;clip(alpha-.001);return float4(i.colour.rgb*GetCurrentExposureMultiplier()*_RoadRushParticleRadiance,alpha);}
 ENDHLSL } } }
