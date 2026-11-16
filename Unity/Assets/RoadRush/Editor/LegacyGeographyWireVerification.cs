using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
 public static class LegacyGeographyWireVerification
 {
  const string Folder="Builds/Verification/World13GeographyBaseline18";
  [Serializable]sealed class Entry{public string source,sourceSHA256,canonical,canonicalSHA256,id,epoch;}
  [Serializable]sealed class Receipt{public string compatibility;public Entry[] entries;}
  static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
  static string RealCareer()=>Path.Combine(Application.persistentDataPath,"road-rush-save.json");
  static string CareerHash()=>File.Exists(RealCareer())?Hash(File.ReadAllText(RealCareer())):"missing";
  static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);Debug.Log("LEGACY GEOGRAPHY PASS "+why);}
  public static void Capture()
  {
   int exit=1;string career=CareerHash();try
   {
    Require(!Application.isPlaying,"Editor-only capture, no game session opened");Require(!Directory.Exists(Folder),"fresh immutable legacy baseline");Directory.CreateDirectory(Folder);
    string[] sources={Path.GetFullPath("Builds/Verification/derived-world-639295264169154970/dee-valley-built-world.json"),"/private/tmp/rr017-bridge-native5-canonical.json"};
    var receipt=new Receipt{compatibility=WorldSnapshotCodec.Compatibility,entries=new Entry[sources.Length]};
    for(int i=0;i<sources.Length;i++){string raw=File.ReadAllText(sources[i]);var region=JsonUtility.FromJson<GeoRegion>(raw);string canonical=WorldSnapshotCodec.CanonicalJson(region),name=Path.GetFullPath(Path.Combine(Folder,"canonical-"+i+".json"));File.WriteAllText(name,canonical);receipt.entries[i]=new Entry{source=sources[i],sourceSHA256=Hash(raw),canonical=name,canonicalSHA256=Hash(canonical),id=WorldSnapshotCodec.NetworkId(region),epoch=Core.WorldEpoch.Compute(receipt.compatibility,WorldSnapshotCodec.NetworkId(region),canonical)};}
    File.WriteAllText(Path.Combine(Folder,"baseline.json"),JsonUtility.ToJson(receipt,true));Require(CareerHash()==career,"real career unchanged");exit=0;
   }catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);
  }
  public static void VerifyCurrent()
  {
   int exit=1;string career=CareerHash();try
   {
    Require(!Application.isPlaying,"Editor-only comparison, no game session opened");var receipt=JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(Folder,"baseline.json")));
    foreach(var e in receipt.entries)
    {
     string raw=File.ReadAllText(e.source);Require(Hash(raw)==e.sourceSHA256,"source bytes unchanged "+e.id);var region=JsonUtility.FromJson<GeoRegion>(raw);
     string canonical=WorldSnapshotCodec.CanonicalJson(region);Require(Hash(canonical)==e.canonicalSHA256&&canonical==File.ReadAllText(e.canonical),"legacy12 geographic payload remains byte-identical "+e.id);
     Require(Core.WorldEpoch.Compute(receipt.compatibility,WorldSnapshotCodec.NetworkId(region),canonical)==e.epoch,"legacy18 epoch remains identical under captured compatibility "+e.id);
     var nodes=typeof(GeoRoad).GetField("sourceNodeIds");var recipe=typeof(GeoRegion).GetField("roadLayoutRecipe");Require(nodes!=null&&nodes.IsNotSerialized&&recipe!=null&&recipe.IsNotSerialized,"future metadata is explicitly nonserialized");nodes.SetValue(region.roads[0],Enumerable.Repeat("unadmitted-test-only",region.roads[0].points.Length).ToArray());recipe.SetValue(region,"UNADMITTED-TEST-ONLY");
     Require(WorldSnapshotCodec.CanonicalJson(region)==canonical,"experimental shadow metadata cannot change legacy12 wire bytes "+e.id);
    }
    Require(CareerHash()==career,"real career unchanged");File.WriteAllText(Path.Combine(Folder,"comparison.txt"),"PASS actual Unity legacy12 source/payload/captured18epoch/nonserialized-shadow checks; no geometry13 admission or bare-geography13 persistence claim.\n");exit=0;
   }catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);
  }
 }
}
