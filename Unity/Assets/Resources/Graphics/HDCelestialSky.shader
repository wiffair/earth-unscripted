Shader "EarthUnscriptedHD/Celestial Sky" {
 Properties {
 _MainTex("Day panorama",2D)="gray"{}
 _Daylight("Daylight",Float)=1 _Clouds("Clouds",Float)=0 _Pollution("Pollution",Float)=0
 _MoonPhase("Moon phase",Float)=0 _Latitude("Latitude",Float)=0 _Sidereal("Sidereal",Float)=0 _UsePanorama("Panorama",Float)=0
 _SunDirection("Sun",Vector)=(0,1,0,0) _MoonDirection("Moon",Vector)=(0,-1,0,0)
 }
 HLSLINCLUDE
 #pragma target 4.5
 #pragma vertex Vert
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
 #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
 #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyUtils.hlsl"
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
 float3 _SunDirection,_MoonDirection;float _Daylight,_Clouds,_Pollution,_MoonPhase,_Latitude,_Sidereal,_UsePanorama;
 struct A {uint vertexID:SV_VertexID;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 positionCS:SV_POSITION;UNITY_VERTEX_OUTPUT_STEREO};
 V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=GetFullScreenTriangleVertexPosition(i.vertexID,UNITY_RAW_FAR_CLIP_VALUE);return o;}
   float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453123);}
   float4 SkyColour(V i)
   {
    float3 direction=normalize(-GetSkyViewDirWS(i.positionCS.xy));float elevation=direction.y;
    float2 uv=float2(.5+atan2(direction.x,direction.z)/(2*PI),.5+asin(clamp(direction.y,-1,1))/PI);
    float3 photograph=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv).rgb;
    float3 daySky=lerp(float3(.43,.64,.83),float3(.055,.20,.46),saturate(elevation));
    daySky=lerp(daySky,photograph*.85,_UsePanorama*.7);
    float3 colour=lerp(float3(.001,.002,.006),daySky,_Daylight);
    float horizon=pow(1-saturate(abs(elevation)),5);
    colour+=float3(.035,.025,.018)*_Pollution*horizon*(1-_Daylight);
    float sunDot=dot(direction,_SunDirection);
    float solarDisc=smoothstep(cos(.31*PI/180),cos(.255*PI/180),sunDot);
    float solarHalo=pow(saturate(sunDot),600);
    colour+=float3(1,.84,.55)*(solarDisc*8+solarHalo*.18)*(1-_Clouds*.94)*smoothstep(-.015,.005,_SunDirection.y);
    float moonDot=dot(direction,_MoonDirection);
    // Half a degree diameter, with a small visibility exaggeration for a display.
    float lunarRadius=.32*PI/180;
    if(moonDot>cos(lunarRadius)&&elevation>-.01)
    {
     float3 right=normalize(cross(float3(0,1,0),_MoonDirection));float3 up=cross(_MoonDirection,right);
     float2 disc=float2(dot(direction,right),dot(direction,up))/sin(lunarRadius);
     float radial=dot(disc,disc);
     float3 normal=right*disc.x+up*disc.y-_MoonDirection*sqrt(saturate(1-radial));
     float light=saturate(dot(normal,_SunDirection));
     float craters=.72+.13*sin(disc.x*29+sin(disc.y*17)*3)+.08*sin(disc.y*37+disc.x*21);
     float edge=1-smoothstep(.93,1,radial);
     float3 moon=float3(.78,.79,.73)*craters*(.012+light*.8)*(1-_Clouds);
     // Daylight atmosphere stays in front of the unlit lunar face.
     // Replacing it with black made the daytime moon look like a hole.
     colour=lerp(colour,moon+colour*saturate(_Daylight*4),edge);
    }
    float meridian=-direction.z*sin(_Latitude)+direction.y*cos(_Latitude);
    float equatorialY=-direction.x;
    float3 celestial=float3(meridian*cos(_Sidereal)-equatorialY*sin(_Sidereal),meridian*sin(_Sidereal)+equatorialY*cos(_Sidereal),direction.z*cos(_Latitude)+direction.y*sin(_Latitude));
    float2 starUV=float2(atan2(celestial.y,celestial.x)/(2*PI)+.5,asin(clamp(celestial.z,-1,1))/PI+.5)*float2(2048,1024);
    float2 cell=floor(starUV),offset=frac(starUV)-float2(Hash(cell+9),Hash(cell+23));
    float seed=Hash(cell);float starPeak=exp(-dot(offset,offset)*90)*step(.9987,seed);
    float visibility=(1-_Daylight)*(1-_Daylight)*pow(1-_Clouds,4)*lerp(1,.08,_Pollution)*lerp(1,.45,_MoonPhase*saturate(_MoonDirection.y));
    float horizonFade=smoothstep(0,.16,elevation);
    if(moonDot>cos(lunarRadius))visibility=0;
    colour+=starPeak*float3(.82,.87,1)*visibility*horizonFade*(.8+1.8*Hash(cell+31));
    // Keep the sun in float precision until exposure; half overflow produced
    // a black disc. Bound probe radiance to finite half-float storage as well.
    return float4(min(max(0,colour)*lerp(1,10000,_Daylight),60000),1);
   }

 float4 Bake(V i):SV_Target{return SkyColour(i);}
 float4 Screen(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return float4(SkyColour(i).rgb*GetCurrentExposureMultiplier(),1);}
 ENDHLSL
 SubShader {Tags {"RenderPipeline"="HDRenderPipeline"}
 Pass { ZWrite Off ZTest Always Blend Off Cull Off HLSLPROGRAM
 #pragma fragment Bake
 ENDHLSL }
 Pass { ZWrite Off ZTest LEqual Blend Off Cull Off HLSLPROGRAM
 #pragma fragment Screen
 ENDHLSL }
 } }
