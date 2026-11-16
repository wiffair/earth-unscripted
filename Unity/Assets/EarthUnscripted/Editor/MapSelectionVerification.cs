using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor {
public static class MapSelectionVerification {
 public static void Run(){try{var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-earthMapFixtures");if(index<0)throw new ArgumentException("Map fixture directory required");string folder=args[index+1];int checks=0;foreach(var p in new[]{new WorldPlace{name="New York",latitude=40.7128,longitude=-74.006,country_code="US",size=1000},new WorldPlace{name="Berlin",latitude=52.52,longitude=13.405,country_code="DE",size=1000},new WorldPlace{name="Sydney",latitude=-33.8688,longitude=151.2093,country_code="AU",size=1000}}){string source=File.ReadAllText(Path.Combine(folder,p.country_code+"-preview.json"));var preview=GlobalRegions.Preview(p,source);var region=GlobalRegions.Convert(p,source,Enumerable.Repeat(20f,289).ToArray(),17,1000);if(preview.roads.Length==0||region.roads.Length<8||region.countryCode!=p.country_code||Math.Abs(region.longitude-p.longitude)>1e-6)throw new Exception("Selected location lost: "+p.name);var providers=GlobalRegions.MapProviders(p,"https://overpass.atownsend.org.uk/api/interpreter");if(providers.Any(x=>x.Contains("atownsend")))throw new Exception("UK-only provider leaked internationally");Debug.Log("MAP FIXTURE PASS "+p.name+" roads="+region.roads.Length+" first="+providers[0]);checks++;}TransportControlVerification.Run();Debug.Log("MAP SELECTION PASS "+checks);EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}}
