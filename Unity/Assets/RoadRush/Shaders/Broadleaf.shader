Shader "RoadRush/Broadleaf"
{
 Properties
 {
  _BaseMap("Colour and opacity",2D)="white"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  _NormalMap("Leaf normal",2D)="bump"{}
  _RoughnessMap("Leaf roughness",2D)="white"{}
  _NormalStrength("Leaf vein strength",Range(0,1))=.45
  _Wetness("Wetness",Range(0,1))=0
  _Cull("Culling",Float)=0
  _Cutoff("Alpha threshold",Range(0,1))=.4
  _IsLeaf("Leaf material",Float)=1
  _WindStrength("Wind",Range(0,1))=.25
  _LodIndex("LOD",Float)=0
  _LodDistances("LOD distances",Vector)=(160,450,0,0)
  _TreeCameraPosition("Tree view camera",Vector)=(0,0,0,1)
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
  Cull [_Cull]
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
  TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);TEXTURE2D(_RoughnessMap);SAMPLER(sampler_RoughnessMap);
  CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST;half4 _BaseColor;float _Cutoff,_IsLeaf,_WindStrength,_LodIndex,_NormalStrength,_Wetness;float4 _LodDistances,_TreeCameraPosition;
  CBUFFER_END
  struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float3 pivot:TEXCOORD1;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
  struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;half shade:TEXCOORD4;float treeDistance:TEXCOORD5;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
  float3 Position(Attributes v)
  {
   float3 p=v.positionOS.xyz;
   if(_IsLeaf>.5)
   {
    float sx=length(TransformObjectToWorldDir(float3(1,0,0),false)),sy=length(TransformObjectToWorldDir(float3(0,1,0),false));
    p.y=clamp(v.pivot.y+(p.y-v.pivot.y)*sx/max(sy,.00001),0,1);
   }
   float3 origin=TransformObjectToWorld(float3(0,0,0));float phase=v.color.g*6.283185+dot(origin.xz,float2(.021,.013));
   float bend=.028*_WindStrength*v.color.r*p.y*p.y;
   p.x+=sin(_Time.y*1.35+phase)*bend;p.z+=cos(_Time.y*.97+phase*.8)*bend;
   return p;
  }
  float3 Normal(Attributes v)
  {
   if(_IsLeaf<.5)return TransformObjectToWorldNormal(v.normalOS);
   float3 x=normalize(TransformObjectToWorldDir(float3(1,0,0),false)),y=normalize(TransformObjectToWorldDir(float3(0,1,0),false)),z=normalize(TransformObjectToWorldDir(float3(0,0,1),false));
   return normalize(x*v.normalOS.x+y*v.normalOS.y+z*v.normalOS.z);
  }
  Varyings Vert(Attributes v)
  {
   UNITY_SETUP_INSTANCE_ID(v);Varyings o=(Varyings)0;UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
   o.positionWS=TransformObjectToWorld(Position(v));o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=Normal(v);o.uv=TRANSFORM_TEX(v.uv,_BaseMap);o.fog=ComputeFogFactor(o.positionCS.z);o.shade=v.color.b;o.treeDistance=distance(TransformObjectToWorld(float3(0,.5,0)),_TreeCameraPosition.xyz);return o;
  }
  void LodClip(Varyings i)
  {
   float noise=clamp(frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715)))),.001,.999);
   float nearFade=smoothstep(_LodDistances.x*.9,_LodDistances.x*1.1,i.treeDistance),farFade=smoothstep(_LodDistances.y*.9,_LodDistances.y*1.1,i.treeDistance);
   if(_LodIndex<.5)clip(noise-nearFade);
   else if(_LodIndex<1.5){clip(nearFade-noise);clip(noise-farFade);}
   else clip(farFade-noise);
  }
  half4 Albedo(Varyings i){LodClip(i);half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;clip(color.a-_Cutoff);return color;}
  ENDHLSL
  Pass
  {
   Name "Forward" Tags{"LightMode"="UniversalForward"} ZWrite On AlphaToMask On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   half4 Frag(Varyings i,bool facing:SV_IsFrontFace):SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(i);half4 base=Albedo(i);half3 n=normalize(i.normalWS)*(facing?1:-1);
    half roughness=.88;
    if(_IsLeaf>.5)
    {
     // Derivative tangent basis follows each photographed leaf card's actual UVs,
     // including the height/radius correction, without storing per-tree meshes.
     float3 dp1=ddx(i.positionWS),dp2=ddy(i.positionWS);float2 uv1=ddx(i.uv),uv2=ddy(i.uv);
     float3 a=cross(dp2,n),b=cross(n,dp1),t=a*uv1.x+b*uv2.x,bt=a*uv1.y+b*uv2.y;
     float inv=rsqrt(max(max(dot(t,t),dot(bt,bt)),.00000001));
     half3 scanned=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,i.uv));scanned.xy*=_NormalStrength;
     n=normalize(t*inv*scanned.x+bt*inv*scanned.y+n*scanned.z);
     roughness=SAMPLE_TEXTURE2D(_RoughnessMap,sampler_RoughnessMap,i.uv).r;
    }
    SurfaceData surface=(SurfaceData)0;surface.albedo=base.rgb*lerp(.82,1.12,i.shade)*lerp(1,.88,_Wetness);surface.metallic=0;surface.specular=half3(.04,.04,.04);surface.smoothness=saturate(1-roughness+_Wetness*.18);surface.normalTS=half3(0,0,1);surface.occlusion=1;surface.alpha=1;
    InputData lighting=(InputData)0;lighting.positionWS=i.positionWS;lighting.positionCS=i.positionCS;lighting.normalWS=n;lighting.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);lighting.shadowCoord=TransformWorldToShadowCoord(i.positionWS);lighting.fogCoord=i.fog;lighting.bakedGI=SampleSH(n);lighting.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);lighting.shadowMask=half4(1,1,1,1);
    half4 color=UniversalFragmentPBR(lighting,surface);Light sun=GetMainLight(lighting.shadowCoord);
    color.rgb+=surface.albedo*sun.color*(_IsLeaf*.16*saturate(dot(-n,sun.direction)))*sun.shadowAttenuation;
    color.rgb=MixFog(color.rgb,i.fog);color.a=base.a;return color;
   }
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex ShadowVert
   #pragma fragment ShadowFrag
   #pragma multi_compile_instancing
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   float3 _LightDirection,_LightPosition;
   Varyings ShadowVert(Attributes v)
   {
    UNITY_SETUP_INSTANCE_ID(v);Varyings o=Vert(v);float3 direction=_LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    direction=normalize(_LightPosition-o.positionWS);
    #endif
    o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction));
    #if UNITY_REVERSED_Z
    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
    #else
    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
    #endif
    return o;
   }
   half4 ShadowFrag(Varyings i):SV_Target{Albedo(i);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ZWrite On ColorMask R
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment DepthFrag
   #pragma multi_compile_instancing
   half4 DepthFrag(Varyings i):SV_Target{Albedo(i);return i.positionCS.z;}
   ENDHLSL
  }
 }
}
