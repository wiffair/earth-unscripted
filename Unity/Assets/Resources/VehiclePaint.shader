Shader "EarthUnscripted/VehiclePaint"
{
 Properties { _BaseColor("Paint",Color)=(.8,.08,.02,1) _AccentColor("Accent",Color)=(.94,.94,.90,1) _Smoothness("Clearcoat smoothness",Range(0,1))=.75 _Metallic("Metallic",Range(0,1))=.5 _PaintJob("Paint job",Float)=0 _Dirt("Mud and dust",Range(0,1))=0 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   float4 _BaseColor,_AccentColor;float _Smoothness,_Metallic,_PaintJob,_Dirt;
   struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;};struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 local:TEXCOORD1;float3 normal:TEXCOORD2;float fog:TEXCOORD3;};
   V Vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.local=a.positionOS.xyz;o.normal=TransformObjectToWorldNormal(a.normalOS);o.positionCS=TransformWorldToHClip(o.world);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   float Hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
   half4 Frag(V i):SV_Target
   {
    float3 n=normalize(i.normal),view=normalize(_WorldSpaceCameraPos-i.world);float pattern=0;
    if(_PaintJob>.5&&_PaintJob<1.5)pattern=step(.10,abs(i.local.x))*step(abs(i.local.x),.24);
    if(_PaintJob>1.5&&_PaintJob<2.5)pattern=step(.5,frac(i.local.z*.85+i.local.y*.9))*step(.42,abs(n.x));
    if(_PaintJob>2.5&&_PaintJob<3.5)pattern=step(.5+.16*sin(i.local.z*6),i.local.y);
    if(_PaintJob>3.5)pattern=step(.73,Hash(floor(i.local*7)))*step(.35,abs(n.x));
    float3 paint=lerp(_BaseColor.rgb,_AccentColor.rgb,pattern);
    float flakes=Hash(floor(i.local*240));paint*=.97+.035*flakes*_Metallic;
    float dirt=saturate(_Dirt*(1.25-saturate(i.local.y/1.5))*(.65+Hash(floor(i.local*18))*.7));paint=lerp(paint,float3(.18,.105,.045),dirt);
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));float diffuse=saturate(dot(n,sun.direction))*sun.shadowAttenuation;
    float roughness=max(.04,1-_Smoothness*(1-dirt*.8));float3 halfVector=normalize(sun.direction+view);
    float specular=pow(saturate(dot(n,halfVector)),lerp(180,8,roughness));
    float fresnel=.04+.7*pow(1-saturate(dot(n,view)),5);
    float3 colour=paint*(SampleSH(n)+sun.color*diffuse*.8)+sun.color*specular*sun.shadowAttenuation*(.3+_Metallic*.8);
    colour+=SampleSH(reflect(-view,n))*fresnel*_Smoothness*(1-dirt);
    return half4(MixFog(colour,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
