Shader "EarthUnscripted/CoastalSurface"
{
 Properties { _Mode("Surface:sea,sand,pebbles,mixed",Float)=0 _WaveHeight("Wave height",Float)=.18 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10"}
  Pass
  {
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   float _Mode,_WaveHeight;
   struct A {float4 positionOS:POSITION;};
   struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float fog:TEXCOORD1;};
   V Vert(A input)
   {
    V o;float3 p=TransformObjectToWorld(input.positionOS.xyz);
    if(_Mode<.5||_Mode>3.5)p.y+=_WaveHeight*(.65*sin(p.x*.12+p.z*.06+_Time.y*1.1)+.35*sin(p.z*.24-p.x*.08+_Time.y*1.6));
    o.world=p;o.positionCS=TransformWorldToHClip(p);o.fog=ComputeFogFactor(o.positionCS.z);return o;
   }
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   half4 Frag(V i):SV_Target
   {
    float3 colour;
    if(_Mode>3.5){clip(sin(i.world.x*2.7+i.world.z*3.1+_Time.y*2)+Hash(floor(i.world.xz*9))-.35);return half4(MixFog(float3(.76,.84,.81),i.fog),1);}
    if(_Mode<.5)
    {
     float x=i.world.x,z=i.world.z,t=_Time.y;
     float3 n=normalize(float3(-_WaveHeight*(.078*cos(x*.12+z*.06+t*1.1)-.028*cos(z*.24-x*.08+t*1.6)),1,-_WaveHeight*(.039*cos(x*.12+z*.06+t*1.1)+.084*cos(z*.24-x*.08+t*1.6))));
     float3 view=normalize(_WorldSpaceCameraPos-i.world);float fresnel=pow(1-saturate(dot(n,view)),4);
     colour=lerp(float3(.025,.16,.21),float3(.40,.58,.67),fresnel);
     Light sun=GetMainLight();float glint=pow(saturate(dot(reflect(-sun.direction,n),view)),160);
     colour+=sun.color*glint*.75;colour+=.018*sin(x*.08+z*.06+t*.3);
    }
    else
    {
     float grain=Hash(floor(i.world.xz*55));float ridge=sin(i.world.x*32+sin(i.world.z*1.8)*1.7)*.5+.5;
     colour=float3(.69,.61,.43)*(.88+grain*.18+ridge*.025);
     if(_Mode>1.5)
     {
      float2 grid=i.world.xz*18,cell=floor(grid),q=frac(grid);float nearest=10,seed=0;
      for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){float2 k=float2(x,y);float h=Hash(cell+k);float2 centre=k+float2(h,Hash(cell+k+17));float d=length(q-centre);if(d<nearest){nearest=d;seed=h;}}
      float3 stone=lerp(float3(.27,.25,.22),float3(.65,.59,.49),seed);stone*=.7+.3*smoothstep(.03,.18,nearest);
      colour=_Mode<2.5?stone:lerp(colour,stone,.45);
     }
     colour*=.8+.2*saturate(GetMainLight().direction.y);
    }
    return half4(MixFog(colour,i.fog),1);
   }
   ENDHLSL
  }
 }
}
