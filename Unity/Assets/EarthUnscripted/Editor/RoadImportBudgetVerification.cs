using System;
using System.Linq;
using Stopwatch=System.Diagnostics.Stopwatch;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 public static class RoadImportBudgetVerification
 {
  public static void Batch()
  {
   try
   {
    var place=new WorldPlace{name="Road budget fixture",latitude=50,longitude=0,country_code="GB",size=6000};
    foreach(int segments in new[]{4499,4500,4501,9999,10000,10001})
    {
     var geometry=new JArray();var nodes=new JArray();
     for(int i=0;i<=segments;i++){nodes.Add(i+1);geometry.Add(new JObject{{"lat",50},{"lon",(-2500+i*.5)/(111319.4908*Math.Cos(50*Math.PI/180))}});}
     string source=new JObject{{"elements",new JArray(new JObject{{"type","way"},{"id",1},{"tags",new JObject{{"highway","residential"}}},{"nodes",nodes},{"geometry",geometry}})}}.ToString(Newtonsoft.Json.Formatting.None);
     var watch=Stopwatch.StartNew();bool rejected=false;
     try{var region=GlobalRegions.Convert(place,source,Enumerable.Repeat(20f,289).ToArray(),17,6000);if(region.roads.Length!=segments)throw new Exception("Road count changed: "+region.roads.Length);}
     catch(Exception e){if(segments<=10000||!e.Message.Contains("too many road segments"))throw;rejected=true;}
     if(rejected!=(segments>10000))throw new Exception("Road budget boundary mismatch");
     Debug.Log("ROAD BUDGET PASS segments="+segments+" rejected="+rejected+" milliseconds="+watch.Elapsed.TotalMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));
    }
    EditorApplication.Exit(0);
   }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
  }
 }
}
