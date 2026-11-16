Shader "RoadRush/Automatic Photographic Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap("Photograph",2D)="white"{}
        [MainColor] _BaseColor("Colour",Color)=(1,1,1,1)
        _AlphaMap("Cutout mask",2D)="white"{}
        [Normal] _BumpMap("Normal",2D)="bump"{}
        _Cutoff("Cutout",Range(0,1))=.35
        _Smoothness("Smoothness",Range(0,1))=.1
        [PerRendererData] _TreeFade("Tree visibility",Vector)=(1,1,1,0)
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
        Cull Off
        Pass
        {
            Name "ForwardLit" Tags{"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #include "AutomaticLighting.hlsl"
            #include "AutomaticTreeFade.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AlphaMap);SAMPLER(sampler_AlphaMap);
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;float _Cutoff,_Smoothness;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID};
            Varyings Vert(Attributes input)
            {
                Varyings output;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS=half4(TransformObjectToWorldDir(input.tangentOS.xyz),input.tangentOS.w*GetOddNegativeScale());
                output.uv=input.uv;output.fog=ComputeFogFactor(output.positionCS.z);return output;
            }
            half4 Frag(Varyings input,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                AutomaticTreeClip(input.positionCS.xy);
                half4 colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
                clip(min(colour.a,SAMPLE_TEXTURE2D(_AlphaMap,sampler_AlphaMap,input.uv).r)-_Cutoff);
                float3 n=normalize(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                float3 t=normalize(input.tangentWS.xyz),b=cross(n,t)*input.tangentWS.w;
                half3 detail=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,input.uv));
                n=normalize(t*detail.x+b*detail.y+n*detail.z);
                return AutomaticLit(colour.rgb,n,input.positionWS,input.positionCS,_Smoothness,input.fog);
            }
            ENDHLSL
        }
    }
}
