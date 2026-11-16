#ifndef EARTHUNSCRIPTED_TYRE_TRACK_SURFACE
#define EARTHUNSCRIPTED_TYRE_TRACK_SURFACE
float _EarthUnscriptedTrackTime;
// UV length is physical metres, so tread spacing does not stretch with speed.
void EarthUnscriptedTrack(float2 uv,float2 lifetime,float4 encoded,out float3 colour,out float opacity,out float shine)
{
    // HDRP forwards UV1.xy for lightmaps, discarding zw. Encode kind and
    // intensity in vertex alpha instead, which both pipelines preserve.
    float snow=step(.5,encoded.a);float strength=snow>.5?(encoded.a-.5)*2:encoded.a/.49;
    float2 kind=float2(snow,saturate(strength));float4 tint=float4(encoded.rgb,.82);
    float age=max(0,_EarthUnscriptedTrackTime-lifetime.x);
    float fade=saturate((lifetime.y-age)/max(1,min(12,lifetime.y*.25)));
    float edge=smoothstep(0,.09,uv.x)*(1-smoothstep(.91,1,uv.x));
    float ribs=1-smoothstep(.025,.06,abs(frac(uv.x*4)-.5));
    float blocks=smoothstep(.3,.45,frac(uv.y*5+abs(uv.x-.5)*2.3));
    float tread=saturate(.35+blocks*.55-ribs*.18);
    colour=tint.rgb*(.82+tread*.18);
    if(kind.x>.5){colour=lerp(colour,float3(.7,.74,.78),pow(abs(uv.x-.5)*2,14)*.7);}
    opacity=tint.a*edge*fade*kind.y*(kind.x>.5?.6+.4*tread:tread);
    shine=kind.x>.5?.16:.58;
}
#endif
