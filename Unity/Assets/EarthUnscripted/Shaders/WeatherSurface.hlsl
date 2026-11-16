#ifndef EARTHUNSCRIPTED_WEATHER_SURFACE
#define EARTHUNSCRIPTED_WEATHER_SURFACE
TEXTURE2D_ARRAY(_EarthUnscriptedTunnelMask);SAMPLER(sampler_EarthUnscriptedTunnelMask);
float4 _EarthUnscriptedTunnelMaskRect;float _EarthUnscriptedTunnelMaskEnabled,_EarthUnscriptedTunnelExtremeCold;
float3 EarthUnscriptedWeatherExposure(float3 position)
{
 float3 factors=1;
 float2 uv=(position.xz-_EarthUnscriptedTunnelMaskRect.xy)/max(_EarthUnscriptedTunnelMaskRect.zw,1);
 if(_EarthUnscriptedTunnelMaskEnabled>.5&&all(uv>=0)&&all(uv<1))
 {
  [unroll]for(int i=0;i<4;i++)
  {
   float4 shelter=SAMPLE_TEXTURE2D_ARRAY(_EarthUnscriptedTunnelMask,sampler_EarthUnscriptedTunnelMask,uv,i);
   if(shelter.y>shelter.x+.5&&position.y>=shelter.x-.7&&position.y<=shelter.y+.1)
    factors=min(factors,float3(shelter.z,shelter.w,max(shelter.w,_EarthUnscriptedTunnelExtremeCold*.12)));
  }
 }
 return factors;
}
float4 _EarthUnscriptedWeather; // wet, ice, frost, snow
void EarthUnscriptedWeather(inout float3 colour,inout float smoothness,float3 normal,float3 position)
{
 float up=saturate((normal.y-.18)*1.22);
 float grain=frac(sin(dot(floor(position.xz*11),float2(127.1,311.7)))*43758.5453);
 float patch=saturate(.7+.3*sin(position.x*.61+sin(position.z*.47)));
 float3 exposure=EarthUnscriptedWeatherExposure(position);
 float wet=_EarthUnscriptedWeather.x*up*exposure.x;
 colour*=1-wet*.30;smoothness=lerp(smoothness,.84,wet*.82);
 float ice=_EarthUnscriptedWeather.y*up*patch*exposure.x;
 colour=lerp(colour,colour*.7+float3(.08,.105,.12),ice*.55);smoothness=lerp(smoothness,.96,ice);
 float snow=saturate(_EarthUnscriptedWeather.w*exposure.y*up*(.85+grain*.3));
 float frost=_EarthUnscriptedWeather.z*exposure.z*up*(.2+grain*.45);
 colour=lerp(colour,float3(.75,.82,.87),frost);
 colour=lerp(colour,float3(.89,.92,.95),snow);
 smoothness=lerp(smoothness,.22,snow);
}
#endif
