using System;
using EarthUnscripted.Core;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class TyreAudioVerification
 {
  public static void Run()
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception("TYRE AUDIO "+why);}
   TyreSoundParameters At(TyreSoundSurface surface,float wet=0,float speed=31,TyreSoundKind kind=TyreSoundKind.Road,float radius=.36f,float width=.225f,bool grounded=true)=>TyreAudioRules.Evaluate(speed,wet,0,0,0,radius,width,kind,surface,grounded);
   var road=At(TyreSoundSurface.Asphalt);Check(At(TyreSoundSurface.RaceAsphalt).Rolling<road.Rolling,"race tarmac quieter than ordinary road");Check(At(TyreSoundSurface.CleanAsphalt).Rolling<road.Rolling&&At(TyreSoundSurface.RoughAsphalt).Rolling>road.Rolling,"mapped surface condition changes sound without guessed age");
   Check(At(TyreSoundSurface.Concrete).CutoffHz!=road.CutoffHz,"concrete has distinct timbre");Check(At(TyreSoundSurface.Gravel).Grain>0&&road.Grain==0,"gravel adds texture rather than asphalt hiss");Check(At(TyreSoundSurface.Grass).CutoffHz<road.CutoffHz,"grass is soft and low-frequency");
   Check(At(TyreSoundSurface.Asphalt,1).Spray>0&&road.Spray==0,"local wetness controls spray; dry tunnel has none");Check(At(TyreSoundSurface.Asphalt,0,100).Rolling==road.Rolling,"high-speed rolling gain levels off");
   Check(At(TyreSoundSurface.Asphalt,0,0).Rolling==0&&At(TyreSoundSurface.Asphalt,1,31,grounded:false).Spray==0,"stationary and airborne wheels are silent");
   Check(At(TyreSoundSurface.Asphalt,0,31,TyreSoundKind.Offroad).Tread>At(TyreSoundSurface.Asphalt,0,31,TyreSoundKind.Slick).Tread,"offroad blocks differ from slicks");Check(At(TyreSoundSurface.Asphalt,0,31,radius:.7f).TreadHz<road.TreadHz&&At(TyreSoundSurface.Asphalt,0,31,width:.45f).Rolling>road.Rolling,"radius changes repetition and width changes contact sound");
   Check(TyreAudioRules.Surface("Road asphalt","concrete:plates",null,false,true)==TyreSoundSurface.Concrete,"mapped concrete overrides common merged mesh name");Check(TyreAudioRules.Surface("Road asphalt","asphalt","very_bad",false,true)==TyreSoundSurface.RoughAsphalt,"OSM rough condition used");Check(TyreAudioRules.Surface("Road asphalt",null,null,false,true)==TyreSoundSurface.Asphalt,"missing metadata defaults honestly to ordinary road");
   foreach(TyreSoundSurface surface in Enum.GetValues(typeof(TyreSoundSurface)))foreach(TyreSoundKind tyre in Enum.GetValues(typeof(TyreSoundKind)))foreach(float speed in new[]{0f,1f,31f,100f,float.NaN})foreach(float wet in new[]{0f,1f})
   {
    var p=TyreAudioRules.Evaluate(speed,wet,.1f,.2f,.03f,.7f,.8f,tyre,surface,true);
    Check(!float.IsNaN(p.Rolling+p.Spray+p.Grain+p.Tread+p.TreadHz+p.CutoffHz)&&p.Rolling+p.Spray+p.Grain+p.Tread<.05f,"all surface/tyre/weather combinations finite and quiet");
   }
   var synth=new TyreNoiseSynth();uint state=12;double before=0,after=0;float peak=0;
   for(int i=0;i<44100;i++){state^=state<<13;state^=state>>17;state^=state<<5;float n=state/(float)uint.MaxValue*2-1;float s=synth.Sample(n,road.Rolling,0,0,0,0,.18f,44100);before+=n*n;after+=s*s;peak=Math.Max(peak,Math.Abs(s));}
   Check(after<before*.00001&&peak<.01f,"smooth-road generator is filtered and subtle rather than full-band static");Check(synth.Sample(1,0,0,0,0,0,.2f,44100)==0,"zero gains silence rolling/spray/grain/tread together");
   Debug.Log("TYRE AUDIO PASS "+checks);
  }
 }
}
