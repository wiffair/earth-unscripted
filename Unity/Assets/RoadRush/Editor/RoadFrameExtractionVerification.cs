using System;using System.IO;using System.Linq;using System.Collections;using System.Reflection;using System.Security.Cryptography;using UnityEditor;using UnityEngine;using RoadRush.Editor.VerificationSupport;
namespace RoadRush.Editor
{
 public static class RoadFrameExtractionVerification
 {
  static int checks;
  static void Check(bool v,string why){if(!v)throw new InvalidOperationException(why);checks++;Debug.Log("ROAD FRAME EXTRACTION PASS "+why);}
  static void Value(BinaryWriter w,object value)
  {
   w.Write(value!=null);if(value==null)return;var type=value.GetType();if(type==typeof(string)){w.Write((string)value);return;}if(type==typeof(float)){w.Write((float)value);return;}if(type==typeof(double)){w.Write((double)value);return;}if(type==typeof(int)){w.Write((int)value);return;}if(type==typeof(bool)){w.Write((bool)value);return;}
   if(value is IDictionary dict){var keys=dict.Keys.Cast<string>().OrderBy(k=>k,StringComparer.Ordinal).ToArray();w.Write(keys.Length);foreach(var k in keys){w.Write(k);Value(w,dict[k]);}return;}
   if(value is IEnumerable list){var items=list.Cast<object>().ToArray();w.Write(items.Length);foreach(var item in items)Value(w,item);return;}
   var fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>!f.IsNotSerialized).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();w.Write(fields.Length);foreach(var f in fields){w.Write(f.Name);Value(w,f.GetValue(value));}
  }
  static string Digest(object plan,bool framesOnly)
  {
   using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){if(framesOnly){foreach(var n in new[]{"Frames","Continuations","Widths","Junctions","Chains","Cycles"}){writer.Write(n);Value(writer,plan.GetType().GetField(n).GetValue(plan));}}else Value(writer,plan);writer.Flush();using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","");}
  }
  static GeoRoad Road(string id,string start,string end,params Vector3[] points)=>new GeoRoad{id=id,start=start,end=end,width=7,points=points,kind="residential"};
  static void Case(string name,GeoRoad[] roads,Func<GeoRoad,double,float> width)
  {
   string before=Digest(roads,false);var old=Legacy18RoadMarkingPlan.Build(roads,width);var current=RoadMarkingPlan.Build(roads,width);var frames=RoadMarkingPlan.BuildFrames(roads,width);
   Check(Digest(old,false)==Digest(current,false),"complete old/new paint and frames are bit-identical "+name);Check(Digest(old,true)==Digest(frames,true),"frame-only geometry/continuations match frozen18 "+name);Check(frames.Strokes.Count==0&&frames.PaintedLength==0,"frame-only path emits no paint "+name);Check(Digest(roads,false)==before,"source data unchanged "+name);
  }
  public static void Run()
  {
   int exit=1;checks=0;try
   {
    Check(!Application.isPlaying,"isolated Editor geometry, no game session");
    foreach(var path in new[]{"Builds/Verification/derived-world-639295264169154970/dee-valley-built-world.json","/private/tmp/rr017-bridge-native5-canonical.json"}){var roads=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(path)).roads;Case(Path.GetFileName(path),roads,null);Case(Path.GetFileName(path)+" variable widths",roads,(r,d)=>r.width+.2f*Mathf.Sin((float)d/20));}
    var junction=new[]{Road("a","west","c",new Vector3(-30,0,0),Vector3.zero),Road("b","c","east",Vector3.zero,new Vector3(30,3,0)),Road("t","c","north",Vector3.zero,new Vector3(0,1,30)),Road("y","north","fork",new Vector3(0,1,30),new Vector3(15,3,50))};Case("T/Y/grade",junction,null);Case("short-tag-boundary",new[]{Road("short","a","b",Vector3.zero,new Vector3(2.105f,.1f,0)),Road("long","b","c",new Vector3(2.105f,.1f,0),new Vector3(35,3,5))},null);
    var cycle=new[]{Road("loop","a","a",Vector3.zero,new Vector3(25,0,0),new Vector3(25,2,25),new Vector3(0,1,25),Vector3.zero)};Case("closed sharp corners",cycle,null);
    var narrow=Road("narrow","a","b",Vector3.zero,new Vector3(25,1,0));narrow.width=1.25f;string narrowBefore=Digest(narrow,false);
    var narrowFrames=RoadMarkingPlan.BuildFrames(new[]{narrow},(r,d)=>3f);
    Check(narrowFrames.Frames.ContainsKey(narrow.id)&&narrowFrames.Strokes.Count==0,"frame-only reconstruction accepts narrow raw width with explicit effective width");
    Check(Digest(narrow,false)==narrowBefore,"narrow raw source is not widened by frame reconstruction");
    bool legacyRejected=false;try{RoadMarkingPlan.Build(new[]{narrow});}catch(InvalidDataException){legacyRejected=true;}Check(legacyRejected,"ordinary cosmetic marking input range unchanged");
    narrow.width=.1f;Check(RoadMarkingPlan.BuildFrames(new[]{narrow}).Frames.Count==1,"raw frame minimum boundary matches geography13");
    narrow.width=.099f;bool tooNarrowRejected=false;try{RoadMarkingPlan.BuildFrames(new[]{narrow});}catch(InvalidDataException){tooNarrowRejected=true;}Check(tooNarrowRejected,"out-of-contract raw frame width rejected");
    Directory.CreateDirectory("Builds/Verification/RoadFrameExtraction");File.WriteAllText("Builds/Verification/RoadFrameExtraction/receipt.txt","PASS "+checks+" actual Unity frozen18 full-paint and frame-only bit equivalence; no source mutation. Not new terrain or world13 admission.\n");exit=0;
   }catch(Exception e){Debug.LogException(e);}EditorApplication.Exit(exit);
  }
 }
}
