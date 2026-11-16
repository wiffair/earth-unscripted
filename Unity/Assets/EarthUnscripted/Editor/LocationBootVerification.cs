using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
 [InitializeOnLoad] public static class LocationBootVerification
 {
  const string Key="EULocationBoot"; static string root; static double deadline; static int frames; static GameSession session;
  static LocationBootVerification(){if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+420;EditorApplication.update+=Tick;}
  public static void Run(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-earthLocation");if(i<0)throw new ArgumentException("-earthLocation source.json required");string source=Path.GetFullPath(args[i+1]);var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(source));root=Path.GetFullPath("Builds/Verification/location-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(Path.Combine(root,"Regions"));File.Copy(source,Path.Combine(root,"Regions",region.id+".json"));File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{region=region.id,quality=0,volume=0,rumble=0,livingScenery=false,weatherMode=0,clockMode=0,fixedHour=14}));PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"com.earthunscripted.qa.location");SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");EditorApplication.EnterPlaymode();}
  static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException(session?session.BootStatus:"No session");if(!EditorApplication.isPlaying)return;if(!session)session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!session)return;if(session.BootFailed)throw new Exception(session.BootStatus);if(!session.BootReady)return;if(++frames<100)return;if(session.World.Route.Region==null)throw new Exception("Unexpected fallback world");Finish(true,"Ready: "+session.World.Route.Region.name);}catch(Exception e){Debug.LogException(e);Finish(false,e.ToString());}}
  static void Finish(bool ok,string message){File.WriteAllText(Path.Combine(root,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=ok,message}));Debug.Log("LOCATION RESULT "+root+" "+message);SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(ok?0:1);}
 }
}
