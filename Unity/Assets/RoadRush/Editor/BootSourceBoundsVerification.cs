using System;using System.IO;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;
namespace RoadRush.Editor
{
    public static class BootSourceBoundsVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("BOOT SOURCE FAIL "+why);checks++;Debug.Log("BOOT SOURCE PASS "+why);}
            try
            {
                string first=null;
                foreach(string id in new[]{"dee-valley","silverstone","spa"})
                {
                    string text=Resources.Load<TextAsset>("Geo/"+id).text;if(first==null)first=text;
                    var region=WorldSnapshotCodec.DecodeBootSource(text,out bool authoritative);
                    Check(!authoritative&&region.preparedWidths,"raw "+id+" validates and normalizes once");
                    Check(region.Nearest(region.roads[0].points[0],out _,out _)==null,"raw "+id+" reaches no spatial Prepare during bounds preflight");
                    var repeated=WorldSnapshotCodec.DecodeBootSource(JsonUtility.ToJson(region),out _);Check(repeated.roads[0].width==region.roads[0].width,"prepared raw "+id+" is never inflated twice");
                }
                void Refuse(Action<JObject> mutate,string label)
                {var obj=JObject.Parse(first);mutate(obj);bool rejected=false;try{WorldSnapshotCodec.DecodeBootSource(obj.ToString(Newtonsoft.Json.Formatting.None),out _);}catch(InvalidDataException){rejected=true;}catch(Newtonsoft.Json.JsonException){rejected=true;}Check(rejected,label);}
                Refuse(o=>o["roads"][0]["points"][0]["x"]=999999,"outlying geometry rejected before spatial index work");
                Refuse(o=>o["nx"]=513,"inconsistent terrain grid rejected before terrain sampling");
                Refuse(o=>o["roads"][0]["points"][0]["x"]="0","numeric coercion rejected by typed source parser");
                Refuse(o=>{o["id"]="download-peer-unprepared";o["preparedWidths"]=false;},"unprepared peer phase rejected instead of normalized");
                Refuse(o=>{o["roadWidthSchema"]=1;o["preparedWidths"]=false;},"unprepared explicit profile phase rejected instead of normalized");
                Refuse(o=>o["unexpected-root"]=true,"unknown source fields rejected before decoding");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/boot-source-bounds.txt","PASS "+checks+" actual Unity source-phase/typed/bounds checks"+Environment.NewLine);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
