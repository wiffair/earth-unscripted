// Shared scenery/vehicle semantics over Unity HDRP17.6's native Lit lighting,
// shadow, depth and G-buffer passes. Source assets remain unchanged.
#define GetSurfaceAndBuiltinData EUDefaultSurface
#define ApplyVertexModification EUDefaultVertex
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData
#undef ApplyVertexModification
TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
TEXTURE2D(_AlphaMap);SAMPLER(sampler_AlphaMap);
TEXTURE2D(_Grass);SAMPLER(sampler_Grass);
TEXTURE2D(_Rock);SAMPLER(sampler_Rock);
TEXTURE2D(_GrassNormal);SAMPLER(sampler_GrassNormal);
TEXTURE2D(_RockNormal);SAMPLER(sampler_RockNormal);
TEXTURE2D(_CoastMask);SAMPLER(sampler_CoastMask);
TEXTURE2D(_WaterMask);SAMPLER(sampler_WaterMask);
TEXTURE2D_ARRAY(_Views);SAMPLER(sampler_Views);
#include "Assets/EarthUnscripted/Shaders/WeatherSurface.hlsl"
#include "Assets/EarthUnscripted/Shaders/TyreTrackSurface.hlsl"
#include "Assets/EarthUnscripted/Shaders/AutomaticTreeFade.hlsl"
float EUHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float3 EUBeach(float2 p,float mode)
{
    float grain=EUHash(floor(p*55)),ridge=sin(p.x*32+sin(p.y*1.8)*1.7)*.5+.5;
    float3 sand=float3(.69,.61,.43)*(.88+grain*.18+ridge*.025);
    if(mode<1.5)return sand;
    float2 grid=p*18,cell=floor(grid),q=frac(grid);float nearest=10,seed=0;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {float2 k=float2(x,y);float h=EUHash(cell+k);float d=length(q-k-float2(h,EUHash(cell+k+17)));if(d<nearest){nearest=d;seed=h;}}
    float3 stone=lerp(float3(.27,.25,.22),float3(.65,.59,.49),seed)*(.7+.3*smoothstep(.03,.18,nearest));
    return mode<2.5?stone:lerp(sand,stone,.45);
}
#if !defined(SHADER_STAGE_RAY_TRACING)
void ApplyVertexModification(AttributesMesh input,float3 normalWS,inout float3 positionRWS,float3 timeParameters)
{
    EUDefaultVertex(input,normalWS,positionRWS,timeParameters);
    if(_EarthUnscriptedMode>4.5&&_EarthUnscriptedMode<5.5)
    {
        float3 centre=TransformObjectToWorld(float3(0,0,0));
        float3 direction=GetCurrentViewPosition()-centre;direction.y=0;
        direction=normalize(direction+float3(0,0,.00001));
        float3 right=normalize(cross(float3(0,1,0),direction));
        float scale=length(TransformObjectToWorldDir(float3(0,1,0),false));
        positionRWS=centre+(right*input.positionOS.x+float3(0,input.positionOS.y,0))*scale;
    }
    if(_EarthUnscriptedMode>5.5&&_EarthUnscriptedMode<6.5&&(_Mode<.5||_Mode>3.5))
    {
        float3 world=GetAbsolutePositionWS(positionRWS);float t=_Time.y;
        positionRWS.y+=_WaveHeight*(.65*sin(world.x*.12+world.z*.06+t*1.1)+.35*sin(world.z*.24-world.x*.08+t*1.6));
    }
}
void GetSurfaceAndBuiltinData(FragInputs input,float3 V,inout PositionInputs posInput,out SurfaceData surfaceData,out BuiltinData builtinData)
{
    EUDefaultSurface(input,V,posInput,surfaceData,builtinData);
    float3 world=GetAbsolutePositionWS(input.positionRWS);
    float3 normal=normalize(input.tangentToWorld[2]);
    float2 uv=input.texCoord0.xy*_BaseMap_ST.xy+_BaseMap_ST.zw;
    float4 sample=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv);
    float3 colour=sample.rgb*_BaseColor.rgb;
    builtinData.opacity=sample.a*_BaseColor.a;
    if(_EarthUnscriptedMode>.5&&_EarthUnscriptedMode<1.5)
    {
        float3 axis=abs(normal),tangent;
        if(axis.y>.65){uv=float2(world.x,-world.z*sign(normal.y));tangent=float3(1,0,0);}
        else if(axis.x>axis.z){uv=float2(-world.z*sign(normal.x),world.y);tangent=float3(0,0,-sign(normal.x));}
        else{uv=float2(world.x*sign(normal.z),world.y);tangent=float3(sign(normal.z),0,0);}
        uv/=max(_TileSize.xy,.1);
        tangent=normalize(tangent-normal*dot(tangent,normal));
        float3 detail=UnpackNormalMapRGorAG(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_NormalStrength);
        normal=normalize(tangent*detail.x+cross(normal,tangent)*detail.y+normal*detail.z);
        colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_BaseColor.rgb;
        float2 wuv=(world.xz-_WaterRect.xy)/max(_WaterRect.zw,1);
        if(_WaterMaskEnabled>.5&&all(wuv>=0)&&all(wuv<=1))
        {float2 w=SAMPLE_TEXTURE2D(_WaterMask,sampler_WaterMask,wuv).rg;if(w.y>.5&&world.y<w.x/max(.001,w.y)-.02)colour=lerp(float3(.24,.22,.17),float3(.38,.34,.25),EUHash(floor(world.xz*18)));}
        float2 cuv=(world.xz-_CoastRect.xy)/max(_CoastRect.zw,1);
        if(_CoastEnabled>.5&&all(cuv>=0)&&all(cuv<=1))
        {float3 mask=SAMPLE_TEXTURE2D(_CoastMask,sampler_CoastMask,cuv).rgb;float cover=saturate(mask.r+mask.g+mask.b);
         if(cover>.001){float3 beach=(EUBeach(world.xz,1)*mask.r+EUBeach(world.xz,2)*mask.g+EUBeach(world.xz,3)*mask.b)/cover;colour=lerp(colour,beach,cover);}}
    }
    else if(_EarthUnscriptedMode>1.5&&_EarthUnscriptedMode<2.5)
    {
        float3 land=sample.rgb,grass=SAMPLE_TEXTURE2D(_Grass,sampler_Grass,world.xz*.17).rgb;
        float2 ruv=abs(normal.x)>.5?world.zy*.14:world.xy*.14;
        float3 rock=SAMPLE_TEXTURE2D(_Rock,sampler_Rock,ruv).rgb;
        float slope=saturate((.86-normal.y)*3.7),forest=saturate((.27-land.r)*8),urban=saturate((land.r-.35)*7);
        colour=lerp(grass,rock,slope)*lerp(float3(.88,1,.82),float3(.64,.72,.56),forest);
        colour=lerp(colour,rock*.85,urban*.45)*(.88+.12*sin(world.x*.013+world.z*.017));
        float detail=dot(grass,float3(.2126,.7152,.0722));float mowing=lerp(.95,1.05,step(0,sin((world.x+world.z*.17)*.22)));
        colour=lerp(colour,lerp(float3(.065,.13,.04)*(.72+detail*1.5)*mowing,rock,slope),_CircuitTurf);
    }
    else if(_EarthUnscriptedMode>2.5&&_EarthUnscriptedMode<3.5)
    {
        float3 local=TransformWorldToObject(input.positionRWS);float pattern=0;
        if(_PaintJob>.5&&_PaintJob<1.5)pattern=step(.10,abs(local.x))*step(abs(local.x),.24);
        if(_PaintJob>1.5&&_PaintJob<2.5)pattern=step(.5,frac(local.z*.85+local.y*.9))*step(.42,abs(normal.x));
        if(_PaintJob>2.5&&_PaintJob<3.5)pattern=step(.5+.16*sin(local.z*6),local.y);
        if(_PaintJob>3.5)pattern=step(.73,EUHash(floor(local.xz*7)))*step(.35,abs(normal.x));
        float dirt=saturate(_Dirt*(1.25-saturate(local.y/1.5))*(.65+EUHash(floor(local.xz*18))*.7));
        colour=lerp(lerp(_BaseColor.rgb,_AccentColor.rgb,pattern),float3(.18,.105,.045),dirt);
        surfaceData.perceptualSmoothness=_Smoothness*(1-dirt*.8);
    }
    else if(_EarthUnscriptedMode>3.5&&_EarthUnscriptedMode<4.5)
    {
        float alpha=_EarthUnscriptedBroadleaf>.5?sample.a:min(sample.a,SAMPLE_TEXTURE2D(_AlphaMap,sampler_AlphaMap,uv).r);
        clip(alpha-_Cutoff);AutomaticTreeClip(posInput.positionSS.xy);
    }
    else if(_EarthUnscriptedMode>4.5&&_EarthUnscriptedMode<5.5)
    {
        AutomaticTreeClip(posInput.positionSS.xy);
        float3 local=TransformWorldToObject(GetCurrentViewPosition());
        float view=atan2(local.x,local.z)*(8./6.2831853)+8.;
        float index=_Variant*8+fmod(floor(view),8.);
        float4 a=SAMPLE_TEXTURE2D_ARRAY(_Views,sampler_Views,input.texCoord0.xy,index);
        float4 b=SAMPLE_TEXTURE2D_ARRAY(_Views,sampler_Views,input.texCoord0.xy,_Variant*8+fmod(floor(view)+1,8.));
        sample=lerp(a,b,frac(view));clip(sample.a-_Cutoff);
        colour=sample.rgb*_Brightness;normal=normalize(V);
    }
    else if(_EarthUnscriptedMode>5.5&&_EarthUnscriptedMode<6.5)
    {
        if(_Mode>3.5){clip(sin(world.x*2.7+world.z*3.1+_Time.y*2)+EUHash(floor(world.xz*9))-.35);colour=float3(.76,.84,.81);}
        else if(_Mode<.5)
        {float x=world.x,z=world.z,t=_Time.y;normal=normalize(float3(-_WaveHeight*(.078*cos(x*.12+z*.06+t*1.1)-.028*cos(z*.24-x*.08+t*1.6)),1,-_WaveHeight*(.039*cos(x*.12+z*.06+t*1.1)+.084*cos(z*.24-x*.08+t*1.6))));colour=float3(.025,.16,.21);surfaceData.perceptualSmoothness=.92;}
        else colour=EUBeach(world.xz,_Mode);
    }
    else if(_EarthUnscriptedMode>6.5&&_EarthUnscriptedMode<7.5)
    {
        clip(sample.a-.35);colour=_EarthUnscriptedWorldText>.5?input.color.rgb:_BaseColor.rgb;builtinData.opacity=1;
    }
    if(_EarthUnscriptedMode>7.5&&_EarthUnscriptedMode<8.5)
    {
        float alpha,shine;EarthUnscriptedTrack(input.texCoord0.xy,input.texCoord1.xy,input.color,colour,alpha,shine);
        if(input.color.a>.5)alpha*=saturate(_EarthUnscriptedWeather.w*3);
        clip(alpha-.002);builtinData.opacity=alpha;surfaceData.perceptualSmoothness=shine;
    }
    if(_EarthUnscriptedMode<2.5||(_EarthUnscriptedMode>3.5&&_EarthUnscriptedMode<4.5))EarthUnscriptedWeather(colour,surfaceData.perceptualSmoothness,normal,world);
    surfaceData.baseColor=max(colour,0);surfaceData.normalWS=normal;surfaceData.geomNormalWS=normal;
}
#else
#define GetSurfaceAndBuiltinData EUDefaultSurface
#endif
