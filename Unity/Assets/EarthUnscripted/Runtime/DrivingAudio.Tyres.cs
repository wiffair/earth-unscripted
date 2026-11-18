using UnityEngine;
namespace EarthUnscripted
{
 public sealed partial class DrivingAudio
 {
  readonly Core.TyreNoiseSynth tyreNoise=new Core.TyreNoiseSynth();
  volatile float tyreSpray,tyreGrain,tyreTread,tyreTreadHz,tyreCutoffStep=.2f;
  float smoothTyreSpray,smoothTyreGrain,smoothTyreTread,smoothTyreHz,nextTyreAudio;
  Core.TyreSoundParameters tyres;
  void UpdateTyreAudio(bool active,float speed)
  {
   if(Time.unscaledTime>=nextTyreAudio)
   {
    nextTyreAudio=Time.unscaledTime+.1f;tyres=default;
    var metric=Car.Metrics;var spec=enginePack?.Settings;
    var kind=Core.TyreAudioRules.Kind(spec?.TyreSoundProfile,metric.Model==3,Car.RaceTyres.compound,metric.Radius,Car.OffroadGripFactor);
    var renderer=owner?.World?.AutomaticWorld?.Renderer;bool race=owner&&owner.World.IsCircuit;
    for(int i=0;i<4;i++)
    {
     string mapped=null,condition=null;
     if(Car.WheelGrounded[i]&&renderer)renderer.TryRoadSoundSurface(Car.ContactPoints[i],out mapped,out condition);
     var surface=Core.TyreAudioRules.Surface(Car.ContactSurfaceNames[i],mapped,condition,race,Car.RoadSurface);
     var p=Core.TyreAudioRules.Evaluate(speed,Car.SurfaceWetness,Car.SurfaceSnow,Car.SurfaceIce,Car.WaterDepth,metric.Radius,metric.TyreWidth,kind,surface,Car.WheelGrounded[i]);
     tyres.Rolling+=p.Rolling*.25f;tyres.Spray+=p.Spray*.25f;tyres.Grain+=p.Grain*.25f;tyres.Tread+=p.Tread*.25f;tyres.TreadHz+=p.TreadHz*.25f;tyres.CutoffHz+=p.CutoffHz*.25f;
    }
   }
   float gain=active?Profile.volume:0;road=tyres.Rolling*gain;tyreSpray=tyres.Spray*gain;tyreGrain=tyres.Grain*gain;tyreTread=tyres.Tread*gain;tyreTreadHz=tyres.TreadHz;
   tyreCutoffStep=1-Mathf.Exp(-2*Mathf.PI*tyres.CutoffHz/Mathf.Max(8000,sampleRate));
  }
 }
}
