using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Radio;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class Beta10RadioVerification
    {
        public static void RunCLI(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException("Beta10 radio: "+message);}
            float[] Tone(float amplitude,float dc=0){var data=new float[48000*2];for(int i=0;i<data.Length;i++)data[i]=dc+amplitude*(float)Math.Sin(i/2*.07);return data;}
            var quiet=Tone(.015f);var loud=Tone(.95f);var jingle=Tone(.95f,.05f);
            var a=RadioAudioPolicy.Normalize(quiet,2,48000,true);var b=RadioAudioPolicy.Normalize(loud,2,48000,true);var c=RadioAudioPolicy.Normalize(jingle,2,48000,false);
            Check(a.gain<=4&&a.outputPeak<=.65f,"quiet recordings use a bounded lift");
            Check(b.gatedRms<=.141f&&b.outputPeak<=.651f,"loud songs meet level and peak targets");
            Check(c.gatedRms<b.gatedRms&&c.outputPeak<b.outputPeak,"hot jingle is quieter than equally loud music");
            Check(Math.Abs(jingle.Average(v=>(double)v))<.0001,"DC offset is removed");
            var silent=new float[2048];RadioAudioPolicy.Normalize(silent,2,48000,false);Check(silent.All(v=>v==0),"silence stays silent");
            bool rejected=false;try{RadioAudioPolicy.Normalize(new[]{float.NaN},1,48000,true);}catch(ArgumentException){rejected=true;}Check(rejected,"nonfinite decoded audio rejected");
            Check(RadioAudioPolicy.NaturalEnd(48000*8,48000*10,48000,false,false,12.4,12),"DSP deadline recognizes EOF after a two-second render gap");
            Check(!RadioAudioPolicy.NaturalEnd(48000*4,48000*10,48000,false,false,5,11),"interrupted mid-song source remains recoverable");
            Check(!RadioAudioPolicy.NaturalEnd(48000*4,48000*10,48000,false,true,20,11),"device reset never masquerades as EOF");
            Check(!RadioAudioPolicy.NaturalEnd(48000*4,48000*10,48000,false,false,20,26),"rebased post-menu deadline excludes paused time");
            var catalog=new RadioCatalog();var song=new JObject{{"id","song"},{"duration_seconds",180}};
            catalog.intros["i"]=new JObject{{"id","i"},{"station_id","s"},{"song_id","song"},{"_approved",true},{"duration_seconds",8}};
            catalog.jingles["j"]=new JObject{{"id","j"},{"station_id","s"},{"_approved",true},{"duration_seconds",10}};
            var random=new System.Random(10);int jingles=0,intros=0;var intervals=new System.Collections.Generic.HashSet<int>();int since=0;var joinTypes=new System.Collections.Generic.HashSet<string>();
            for(int i=0;i<2000;i++)
            {
                var plan=RadioPlan.Broadcast(catalog,"s",song,random);Check(plan.Last().type=="song"&&plan.All(p=>p.station=="s"),"random links retain station/song identity");
                since++;if(plan.Any(p=>p.type=="jingle")){jingles++;intervals.Add(since);since=0;}if(plan.Any(p=>p.type=="intro"))intros++;
                var join=RadioPlan.JoinBroadcast(catalog,"s",song,random);joinTypes.Add(join[0].type);Check(join[0].startFraction>=0&&join[0].startFraction<1,"random joins stay inside decoded segment");
            }
            Check(jingles>400&&jingles<900&&intros>900&&intros<1600&&intervals.Count>5,"links occur randomly rather than every N songs");
            Check(joinTypes.SetEquals(new[]{"song","intro","jingle"}),"tuning can join song, intro or jingle");
            Debug.Log("BETA10_RADIO_POLICY_PASS "+checks);
        }
    }
}
