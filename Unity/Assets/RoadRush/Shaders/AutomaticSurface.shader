Shader "RoadRush/Automatic Photographic Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Photograph",2D)="white"{}
        [MainColor] _BaseColor("Colour",Color)=(1,1,1,1)
        [Normal] _BumpMap("Surface normal",2D)="bump"{}
        _TileSize("Metres per tile",Vector)=(2,2,0,0)
        _Smoothness("Smoothness",Range(0,1))=.15
        _NormalStrength("Normal strength",Range(0,2))=1
        _CoastMask("Mapped beaches",2D)="black"{}
        _CoastRect("Beach world bounds",Vector)=(0,0,1,1)
        _CoastEnabled("Coastal ground",Float)=0
        _WaterMask("Water surface heights",2D)="black"{}
        _WaterRect("Water mask bounds",Vector)=(0,0,1,1)
        _WaterMaskEnabled("Water substrate",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
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
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            TEXTURE2D(_CoastMask);SAMPLER(sampler_CoastMask);TEXTURE2D(_WaterMask);SAMPLER(sampler_WaterMask);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor,_TileSize,_CoastRect,_WaterRect;
            float _Smoothness,_NormalStrength,_CoastEnabled,_WaterMaskEnabled;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID};
            Varyings Vert(Attributes input)
            {
                Varyings output;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.fog=ComputeFogFactor(output.positionCS.z);return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 normal=normalize(input.normalWS),axis=abs(normal),tangent;
                float2 uv;
                if(axis.y>.65){uv=float2(input.positionWS.x,-input.positionWS.z*sign(normal.y));tangent=float3(1,0,0);}
                else if(axis.x>axis.z){uv=float2(-input.positionWS.z*sign(normal.x),input.positionWS.y);tangent=float3(0,0,-sign(normal.x));}
                else{uv=float2(input.positionWS.x*sign(normal.z),input.positionWS.y);tangent=float3(sign(normal.z),0,0);}
                uv/=max(_TileSize.xy,.1);
                tangent=normalize(tangent-normal*dot(tangent,normal));
                float3 bitangent=cross(normal,tangent);
                half3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_NormalStrength);
                normal=normalize(tangent*detail.x+bitangent*detail.y+normal*detail.z);
                half3 colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_BaseColor.rgb;
                float2 waterUV=(input.positionWS.xz-_WaterRect.xy)/max(_WaterRect.zw,1);
                float2 water=float2(0,0);if(_WaterMaskEnabled>.5&&all(waterUV>=0)&&all(waterUV<=1))water=SAMPLE_TEXTURE2D(_WaterMask,sampler_WaterMask,waterUV).rg;
                if(water.y>.5&&input.positionWS.y<water.x/max(.001,water.y)-.02)colour=lerp(float3(.24,.22,.17),float3(.38,.34,.25),frac(sin(dot(floor(input.positionWS.xz*18),float2(12.7,31.1)))*43758.5));
                float2 coastUV=(input.positionWS.xz-_CoastRect.xy)/max(_CoastRect.zw,1);
                if(_CoastEnabled>.5&&all(coastUV>=0)&&all(coastUV<=1))
                {
                    float3 mask=SAMPLE_TEXTURE2D(_CoastMask,sampler_CoastMask,coastUV).rgb;
                    float coverage=saturate(mask.r+mask.g+mask.b);
                    if(coverage>.001)
                    {
                        float2 p=input.positionWS.xz;
                        float grain=frac(sin(dot(floor(p*55),float2(127.1,311.7)))*43758.5453);
                        float3 sand=float3(.69,.61,.43)*(.88+grain*.18+.025*sin(p.x*32+sin(p.y*1.8)*1.7));
                        float2 grid=p*18,cell=floor(grid),q=frac(grid);float nearest=10,seed=0;
                        for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                        {float2 k=float2(x,y);float h=frac(sin(dot(cell+k,float2(127.1,311.7)))*43758.5453);float h2=frac(sin(dot(cell+k+17,float2(127.1,311.7)))*43758.5453);float d=length(q-k-float2(h,h2));if(d<nearest){nearest=d;seed=h;}}
                        float3 stone=lerp(float3(.27,.25,.22),float3(.65,.59,.49),seed)*(.7+.3*smoothstep(.03,.18,nearest));
                        float3 beach=(sand*mask.r+stone*mask.g+lerp(sand,stone,.45)*mask.b)/max(coverage,.001);
                        colour=lerp(colour,beach,coverage);normal=normalize(lerp(normal,input.normalWS,coverage));
                    }
                }
                return AutomaticLit(colour,normal,input.positionWS,input.positionCS,_Smoothness,input.fog);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
