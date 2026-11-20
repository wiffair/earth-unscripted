using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EarthUnscripted.Radio;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class Beta42PolicyVerification
    {
        public static void Run()
        {
            int checks=0;void Need(bool value,string message){checks++;if(!value)throw new IOException("Beta42: "+message);}
            TunnelWeatherIndex.Source S(float x0,float y0,float x1,float y1,bool tunnel)=>new TunnelWeatherIndex.Source{a=new Vector3(x0,y0,0),b=new Vector3(x1,y1,0),width=8,clearance=5,tunnel=tunnel,kind="road"};
            var source=new List<TunnelWeatherIndex.Source>{S(-220,0,-200,0,false),S(200,0,220,0,false)};
            for(int x=-200;x<200;x+=10)source.Add(S(x,0,x+10,0,true));
            var index=new TunnelWeatherIndex(source);Need(index.PortalCount==2,"only real open/tunnel connections are portals");
            var middle=index.Probe(new Vector3(0,.5f,0));var mouth=index.Probe(new Vector3(-198,.5f,0));
            Need(middle.Covered&&middle.Wet<.01f&&middle.Snow<.001f,"long flat interior stays predominantly dry and snow-free");
            Need(mouth.Covered&&mouth.Wet>.8f&&mouth.Snow>.3f,"portal splash/dragged snow retained");
            Need(index.Probe(new Vector3(-165,.5f,0)).Wet>middle.Wet,"tyre-carried water decays with distance");
            Need(!index.Probe(new Vector3(0,15,0)).Covered,"road above tunnel is not sheltered");
            Need(!index.Probe(new Vector3(0,.5f,15)).Covered,"adjacent outdoor road remains exposed");
            Need(WeatherSurfaceRules.RoadBaseSmoothness(true,1)==.22f,"spatial shader road base never gets a global wet gloss");
            var outside=new WeatherSurfaceState(1,.9f,.8f,1,1);var dry=WeatherSurfaceRules.Sheltered(outside,middle,-3);var wet=WeatherSurfaceRules.Sheltered(outside,mouth,-3);
            Need(dry.Wet<.01f&&dry.Ice<.01f&&dry.Frost<.01f&&dry.Snow<.01f,"sheltered wet/ice/frost/snow use local moisture");
            Need(wet.Wet>.8f&&wet.Ice>.7f,"wet portal can freeze");
            Need(WeatherSurfaceRules.Sheltered(outside,middle,-25).Frost>.08f,"extreme cold allows limited interior frost");
            Need(WeatherSurfaceRules.Sheltered(outside,default,-3).Wet==1,"outdoor conditions preserved");
            Need(WeatherSurfaceRules.Grip(dry)>WeatherSurfaceRules.Grip(wet),"dry tunnel has more grip than wet/icy portal");
            Need(WeatherSurfaceRules.Grip(new WeatherSurfaceState(0,0,0,0,0))==1,"ordinary dry handling unchanged");
            Need(Mathf.Abs(WeatherSurfaceRules.Grip(new WeatherSurfaceState(1,0,0,0,0))-.62f)<.0001f,"existing rain grip preserved outdoors");
            var downhill=new List<TunnelWeatherIndex.Source>{S(-220,12,-200,12,false),S(200,0,220,0,false)};
            for(int x=-200;x<200;x+=10){float Y(float v)=>v<0?-v*.06f:0;downhill.Add(S(x,Y(x),x+10,Y(x+10),true));}
            var slope=new TunnelWeatherIndex(downhill);Need(slope.Probe(new Vector3(-70,4.7f,0)).Wet>index.Probe(new Vector3(-70,.5f,0)).Wet+.07f,"downhill runoff reaches beyond splash zone");
            var hill=new List<TunnelWeatherIndex.Source>{S(-220,0,-200,0,false)};for(int x=-200;x<200;x+=10)hill.Add(S(x,(x+200)*.05f,x+10,(x+210)*.05f,true));
            Need(new TunnelWeatherIndex(hill).Probe(new Vector3(0,10.5f,0)).Wet<.02f,"water does not run uphill through bore");
            var clipped=new TunnelWeatherIndex(source.Where(s=>s.tunnel));Need(clipped.PortalCount==0&&clipped.Probe(new Vector3(-198,.5f,0)).Wet==0,"tile/way endpoints alone do not invent portals");
            var dense=new List<TunnelWeatherIndex.Source>{S(-220,0,-200,0,false),S(200,0,220,0,false)};for(float x=-200;x<200;x+=.25f)dense.Add(S(x,0,x+.25f,0,true));
            Need(Mathf.Abs(new TunnelWeatherIndex(dense).Probe(new Vector3(-150,.5f,0)).Wet-index.Probe(new Vector3(-150,.5f,0)).Wet)<.001f,"water penetration is independent of dense road resampling");
            Need(!index.Intersects(new Vector2(10000,10000),2048),"far-away/no-tunnel windows require no atlas allocation");
            var raster=index.Raster(new Vector2(-256,-256),512,128,1);int cell=64*128+64;
            Need(raster.Any(layer=>layer[cell].g>layer[cell].r&&layer[cell].b<.02f),"shader atlas includes dry floor/roof height band");
            var stacked=new List<TunnelWeatherIndex.Source>(source);stacked.AddRange(source.Select(s=>{s.a+=Vector3.up*20;s.b+=Vector3.up*20;return s;}));var layers=new TunnelWeatherIndex(stacked).Raster(new Vector2(-256,-256),512,128,1);
            Need(layers.Count(layer=>layer[cell].g>layer[cell].r)==2,"stacked tunnels keep independent height bands");
            var layerArrays=raster.ToArray();var fresh=index.Raster(new Vector2(-256,-256),512,128,1);
            index.Raster(new Vector2(10000,10000),512,128,1,raster);
            Need(raster.All(layer=>layer.All(pixel=>pixel==Color.clear)),"reused weather storage clears data from the previous window");
            var restored=index.Raster(new Vector2(-256,-256),512,128,1,raster);
            Need(ReferenceEquals(restored,raster)&&Enumerable.Range(0,4).All(i=>ReferenceEquals(layerArrays[i],restored[i])),"weather refresh reuses all four exact backing arrays");
            Need(Enumerable.Range(0,4).All(i=>fresh[i].SequenceEqual(restored[i])),"reused weather raster exactly matches fresh floor/roof/moisture layers");
            var catalog=new RadioCatalog();var song=new Newtonsoft.Json.Linq.JObject{{"id","tune-fixture"},{"duration_seconds",200}};catalog.songs["tune-fixture"]=song;
            catalog.intros["intro-fixture"]=new Newtonsoft.Json.Linq.JObject{{"id","intro-fixture"},{"station_id","station-fixture"},{"song_id","tune-fixture"},{"duration_seconds",8},{"_approved",true}};
            catalog.jingles["jingle-fixture"]=new Newtonsoft.Json.Linq.JObject{{"id","jingle-fixture"},{"station_id","station-fixture"},{"duration_seconds",10},{"_approved",true}};
            var kinds=new HashSet<string>();double mean=0;var random=new System.Random(42);
            for(int i=0;i<1000;i++)
            {
                var sequence=RadioPlan.Tune(catalog,"station-fixture",song,random,1,1);var first=sequence[0];kinds.Add(first.type);mean+=first.startFraction;
                Need(first.startFraction>=0&&first.startFraction<1,"tune offset stays within segment");
                Need(sequence.Last().type=="song"&&sequence.All(s=>s.station=="station-fixture"),"tuning preserves following song and station identity");
            }
            Need(kinds.SetEquals(new[]{"song","intro","jingle"}),"tuning can join music, speech or station ident");
            Need(mean/1000>.4&&mean/1000<.6,"tuning covers segment rather than replaying its beginning");
            Need(CarRadio.MixGain>1&&CarRadio.MixGain<1.6f,"radio boost is modest and independent of engine level");
            Debug.Log("BETA42_POLICY_PASS "+checks+" checks");
        }
    }
}
