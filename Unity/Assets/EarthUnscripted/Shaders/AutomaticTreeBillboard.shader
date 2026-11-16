Shader "EarthUnscripted/Automatic Tree Views"
{
    Properties
    {
        _Views("Eight views per variant",2DArray)=""{}
        _Variant("Variant",Float)=0
        _Cutoff("Cutout",Range(0,1))=.3
        _Brightness("Brightness",Range(0,3))=1.3
        [PerRendererData] _TreeFade("Tree visibility",Vector)=(1,0,0,0)
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
        Cull Off
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AutomaticTreeFade.hlsl"
            TEXTURE2D_ARRAY(_Views);SAMPLER(sampler_Views);
            CBUFFER_START(UnityPerMaterial)
            float _Variant,_Cutoff,_Brightness;
            CBUFFER_END
            struct Attributes{float3 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings{float4 positionCS:SV_POSITION;float4 uv:TEXCOORD0;half fog:TEXCOORD1;float distanceMetres:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID};
            Varyings Vert(Attributes input)
            {
                Varyings output;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);
                float3 centre=TransformObjectToWorld(float3(0,0,0));
                float3 camera=GetCameraPositionWS();float3 direction=camera-centre;direction.y=0;direction=normalize(direction);
                float3 right=normalize(cross(float3(0,1,0),direction));
                float scale=length(TransformObjectToWorldDir(float3(0,1,0),false));
                float3 position=centre+(right*input.positionOS.x+float3(0,input.positionOS.y,0))*scale;
                float3 local=TransformWorldToObject(camera);
                float angle=atan2(local.x,local.z);
                float view=angle*(8./6.2831853)+8.;
                output.positionCS=TransformWorldToHClip(position);output.uv=float4(input.uv,_Variant*8+fmod(floor(view),8.),frac(view));
                output.distanceMetres=distance(camera,centre);
                output.fog=ComputeFogFactor(output.positionCS.z);return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                AutomaticTreeClip(input.positionCS.xy);
                half4 colour=SAMPLE_TEXTURE2D_ARRAY(_Views,sampler_Views,input.uv.xy,input.uv.z);
                float nextView=_Variant*8+fmod(input.uv.z-_Variant*8+1,8.);
                half4 nextColour=SAMPLE_TEXTURE2D_ARRAY(_Views,sampler_Views,input.uv.xy,nextView);
                colour=lerp(colour,nextColour,input.uv.w);
                clip(colour.a-_Cutoff);colour.rgb=MixFog(colour.rgb*_Brightness,input.fog);return half4(colour.rgb,1);
            }
            ENDHLSL
        }
    }
}
