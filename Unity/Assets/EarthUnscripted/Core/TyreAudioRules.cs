using System;
namespace EarthUnscripted.Core
{
 public enum TyreSoundKind { Road,Slick,Wet,AllTerrain,Offroad }
 public enum TyreSoundSurface { Asphalt,CleanAsphalt,RoughAsphalt,RaceAsphalt,Concrete,Gravel,Grass,Mud,Snow,Ice }
 public struct TyreSoundParameters
 {
  public float Rolling,Spray,Grain,Tread,TreadHz,CutoffHz;
 }
 public static class TyreAudioRules
 {
  static float Bound(float x,float min,float max,float fallback=0)=>float.IsNaN(x)||float.IsInfinity(x)?fallback:Math.Max(min,Math.Min(max,x));
  static bool Has(string s,string part)=>s!=null&&s.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0;
  public static TyreSoundSurface Surface(string contact,string mapped,string condition,bool race,bool paved)
  {
   string source=string.IsNullOrEmpty(mapped)?contact:mapped;
   if(Has(source,"concrete"))return TyreSoundSurface.Concrete;
   if(Has(source,"gravel")||Has(source,"ballast")||Has(source,"shoulder")||Has(source,"unpaved")||Has(source,"cobblestone")||Has(source,"paving_stones"))return TyreSoundSurface.Gravel;
   if(Has(source,"mud"))return TyreSoundSurface.Mud;
   if(Has(source,"grass")||Has(source,"terrain")||Has(source,"dirt")||Has(source,"earth"))return TyreSoundSurface.Grass;
   if(Has(condition,"bad")||Has(source,"rough"))return TyreSoundSurface.RoughAsphalt;
   if(Has(condition,"excellent")||Has(source,"new asphalt"))return TyreSoundSurface.CleanAsphalt;
   if(race&&(paved||Has(source,"track")||Has(source,"asphalt")))return TyreSoundSurface.RaceAsphalt;
   return paved||Has(source,"asphalt")||Has(source,"footpath")||Has(source,"road")?TyreSoundSurface.Asphalt:TyreSoundSurface.Grass;
  }
  public static TyreSoundKind Kind(string declared,bool race,RaceCompound compound,float radius,float offroadGrip)
  {
   switch(declared){case "road":return TyreSoundKind.Road;case "slick":return TyreSoundKind.Slick;case "wet":return TyreSoundKind.Wet;case "all-terrain":return TyreSoundKind.AllTerrain;case "offroad":return TyreSoundKind.Offroad;}
   if(race)return compound==RaceCompound.Wet||compound==RaceCompound.Intermediate?TyreSoundKind.Wet:TyreSoundKind.Slick;
   return radius>=.5f&&offroadGrip>=.8f?TyreSoundKind.Offroad:TyreSoundKind.Road;
  }
  public static TyreSoundParameters Evaluate(float speed,float wet,float snow,float ice,float depth,float radius,float width,TyreSoundKind tyre,TyreSoundSurface surface,bool grounded)
  {
   speed=Bound(Math.Abs(speed),0,120);wet=Bound(wet,0,1);snow=Bound(snow,0,1);ice=Bound(ice,0,1);depth=Bound(depth,0,2);radius=Bound(radius,.2f,1,.36f);width=Bound(width,.1f,1,.225f);
   var p=new TyreSoundParameters{CutoffHz=1400};if(!grounded||speed<.15f)return p;
   if(snow>.08f)surface=TyreSoundSurface.Snow;else if(ice>.5f)surface=TyreSoundSurface.Ice;
   float level=Math.Min(1,speed/22),size=(float)Math.Sqrt(width/.225f),roll=.006f,grain=0,cutoff=1400;
   switch(surface)
   {
    case TyreSoundSurface.CleanAsphalt:roll=.0035f;cutoff=900;break;
    case TyreSoundSurface.RoughAsphalt:roll=.009f;grain=.0022f;cutoff=1800;break;
    case TyreSoundSurface.RaceAsphalt:roll=.004f;cutoff=1100;break;
    case TyreSoundSurface.Concrete:roll=.008f;grain=.0012f;cutoff=1200;break;
    case TyreSoundSurface.Gravel:roll=.011f;grain=.005f;cutoff=850;break;
    case TyreSoundSurface.Grass:roll=.007f;cutoff=400;break;
    case TyreSoundSurface.Mud:roll=.007f;grain=.001f;cutoff=350;break;
    case TyreSoundSurface.Snow:roll=.005f;cutoff=650;break;
    case TyreSoundSurface.Ice:roll=.003f;cutoff=1600;break;
   }
   bool hard=surface==TyreSoundSurface.Asphalt||surface==TyreSoundSurface.CleanAsphalt||surface==TyreSoundSurface.RoughAsphalt||surface==TyreSoundSurface.RaceAsphalt||surface==TyreSoundSurface.Concrete;
   float tread=tyre==TyreSoundKind.Offroad?1:tyre==TyreSoundKind.AllTerrain?.5f:tyre==TyreSoundKind.Wet?.2f:tyre==TyreSoundKind.Slick?0:.08f;
   float treadBlocks=tyre==TyreSoundKind.Offroad?24:tyre==TyreSoundKind.AllTerrain?38:60;
   float water=snow>.08f||ice>.5f?0:Math.Max(wet,Math.Min(1,depth*12));
   p.Rolling=Bound(roll*size*level*(1+(hard?tread*.35f:0)),0,.018f);
   p.Spray=Bound(.004f*size*water*level*(hard?1:.35f)*(tyre==TyreSoundKind.Wet?.8f:1),0,.007f);
   p.Grain=Bound(grain*size*level,0,.007f);p.Tread=hard?.0015f*tread*level:0;
   p.TreadHz=Bound(speed/(2*(float)Math.PI*radius)*treadBlocks,0,2000);p.CutoffHz=cutoff;return p;
  }
 }
 public sealed class TyreNoiseSynth
 {
  float rolling,sprayLow,sprayBase,grain;double phase;
  public float Sample(float noise,float rollingGain,float sprayGain,float grainGain,float treadGain,float treadHz,float cutoffStep,int rate)
  {
   if(rate<=0)return 0;
   rolling+=(noise-rolling)*cutoffStep;sprayLow+=(noise-sprayLow)*.32f;sprayBase+=(sprayLow-sprayBase)*.035f;
   if(Math.Abs(noise)>.996f)grain=noise;grain*=.975f;
   phase+=treadHz/rate;phase-=Math.Floor(phase);float block=(float)(1-4*Math.Abs(phase-.5));
   return rolling*rollingGain+(sprayLow-sprayBase)*sprayGain+grain*grainGain+block*treadGain;
  }
 }
}
