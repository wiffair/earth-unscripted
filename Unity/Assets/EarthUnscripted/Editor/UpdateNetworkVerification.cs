using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EarthUnscripted.Updates;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor {
 public static class UpdateNetworkVerification {
  public static void Run() {
   try {
    var response=Task.Run(()=>UpdateTransport.Feed("",CancellationToken.None)).GetAwaiter().GetResult();
    Newtonsoft.Json.Linq.JObject data=null;
    var previous=System.Globalization.CultureInfo.CurrentCulture;
    try {foreach(string culture in new[]{"en-GB","en-US","de-DE","fr-FR","ja-JP","zh-CN","ar-SA"}) {
      System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo(culture);
      data=UpdateProtocol.ReadSigned(response.Item1,0);
      if(data["issuedUtc"].Type!=Newtonsoft.Json.Linq.JTokenType.String)throw new Exception("UTC date coerced under "+culture);
      Debug.Log("UNITY SIGNED FEED CULTURE PASS "+culture);
    }} finally {System.Globalization.CultureInfo.CurrentCulture=previous;}
    Debug.Log("UNITY LIVE UPDATE VERIFIED "+data["game"]["version"]+" sequence "+data["sequence"]);
    EditorApplication.Exit(0);
   } catch(Exception error) { Debug.LogException(error);EditorApplication.Exit(1); }
  }
 }
}
