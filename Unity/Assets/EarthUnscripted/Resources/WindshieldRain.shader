Shader "EarthUnscripted/WindshieldRain" {
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Transparent"}
Pass {Tags {"LightMode"="UniversalForward"} Blend SrcAlpha OneMinusSrcAlpha ZTest LEqual ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A{float3 positionOS:POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
            struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
            V Vert(A i){V o;o.positionCS=TransformWorldToHClip(TransformObjectToWorld(i.positionOS));o.uv=i.uv;o.colour=i.colour;return o;}
            float4 Lens(V i)
            {
                float2 p=(i.uv-.5)*2;float r=length(p);clip(1-r);
                float edge=smoothstep(.68,.88,r)*(1-smoothstep(.88,1,r));
                float crescent=edge*saturate(dot(normalize(p+float2(.0001,0)),normalize(float2(-.5,.8))));
                float highlight=pow(saturate(1-length((p-float2(-.25,.35))*6.5)),2);
                float shadow=pow(saturate(1-length((p-float2(.18,-.25))*2.5)),2);
                float body=pow(saturate(1-r*r),2);
                float alpha=i.colour.a*(crescent*.075+highlight*.65+shadow*.13+body*.025);
                float3 rgb=lerp(i.colour.rgb*.26,float3(.9,.96,1),saturate(highlight+crescent*.35));return float4(rgb,alpha);
            }
float4 Frag(V i):SV_Target{return Lens(i);}
ENDHLSL }}
SubShader {Tags {"RenderPipeline"="HDRenderPipeline" "Queue"="Overlay" "RenderType"="Transparent"}
Pass {Tags {"LightMode"="ForwardOnly"} Blend SrcAlpha OneMinusSrcAlpha ZTest LEqual ZWrite Off Cull Off
HLSLPROGRAM
#pragma target 4.5
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
float _EarthUnscriptedParticleRadiance;

            struct A{float3 positionOS:POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
            struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
            V Vert(A i){V o;o.positionCS=TransformWorldToHClip(TransformObjectToWorld(i.positionOS));o.uv=i.uv;o.colour=i.colour;return o;}
            float4 Lens(V i)
            {
                float2 p=(i.uv-.5)*2;float r=length(p);clip(1-r);
                float edge=smoothstep(.68,.88,r)*(1-smoothstep(.88,1,r));
                float crescent=edge*saturate(dot(normalize(p+float2(.0001,0)),normalize(float2(-.5,.8))));
                float highlight=pow(saturate(1-length((p-float2(-.25,.35))*6.5)),2);
                float shadow=pow(saturate(1-length((p-float2(.18,-.25))*2.5)),2);
                float body=pow(saturate(1-r*r),2);
                float alpha=i.colour.a*(crescent*.075+highlight*.65+shadow*.13+body*.025);
                float3 rgb=lerp(i.colour.rgb*.26,float3(.9,.96,1),saturate(highlight+crescent*.35));return float4(rgb,alpha);
            }
float4 Frag(V i):SV_Target{float4 c=Lens(i);c.rgb*=GetCurrentExposureMultiplier()*_EarthUnscriptedParticleRadiance;return c;}
ENDHLSL }}}
