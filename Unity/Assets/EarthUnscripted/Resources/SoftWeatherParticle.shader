Shader "EarthUnscripted/SoftWeatherParticle"
{
    Properties { _BaseColor("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float fog:TEXCOORD1; float distance:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(world);o.color=v.color*_BaseColor;o.uv=v.uv;
                o.fog=ComputeFogFactor(o.positionCS.z);o.distance=distance(world,_WorldSpaceCameraPos);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                float envelope=saturate(1-dot(p,p));
                half alpha=i.color.a*envelope*envelope*smoothstep(.35,1.2,i.distance);
                clip(alpha-.001);
                return half4(MixFog(i.color.rgb,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
