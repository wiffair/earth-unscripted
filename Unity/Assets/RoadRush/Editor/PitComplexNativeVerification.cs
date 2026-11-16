using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RoadRush.Packs;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    /// <summary>Actual Unity data readers; no scene, production save or Player input.</summary>
    public static class PitComplexNativeVerification
    {
        public static void Run()
        {
            int code=1;var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-pitComplexResult");string output=at>=0&&at+1<args.Length?args[at+1]:null;
            try
            {
                if(output==null)throw new ArgumentException("Explicit result required");
                AudioListener.volume=0;AudioListener.pause=true;var checks=new JArray();
                void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
                var roads=Enumerable.Range(0,3).Select(i=>new GeoRoad{id=new[]{"wing","national","main"}[i],kind=i==2?"raceway":"service",width=i==1?4:9,points=new[]{new Vector3(i*20,0,0),new Vector3(i*20,0,30),new Vector3(i*20,0,60)}}).ToArray();
                var region=new GeoRegion{id="pit-native-synthetic",circuit=true,latitude=0,longitude=0,datum=0,minX=-20,minZ=-20,width=100,depth=100,roads=roads};string hash=new string('a',64);
                var meshes=new JArray();
                for(int i=0;i<3;i++)meshes.Add(new JObject{{"id",roads[i].id},{"roadIds",new JArray(roads[i].id)},{"vertices",new JArray(new JArray(i*20-2,0,0),new JArray(i*20+8,0,0),new JArray(i*20-2,0,60),new JArray(i*20+8,0,60))},{"triangles",new JArray(0,2,1,1,2,3)}});
                var pavement=new JObject{{"format","RoadRushCircuitRoadSurfaces1"},{"layoutSha256",hash},{"units","metres"},{"axes","east-up-north"},{"verticalReference","synthetic zero"},{"latitude",0},{"longitude",0},{"datum",0},{"meshes",meshes}};
                var surfaces=CircuitRoadSurfacePlan.Parse(pavement.ToString(),region,hash);
                JObject Row(string id,int x)=>new JObject{{"id",id},{"name",id+" service"},{"pitRoadId",id},{"bay",new JArray(x,0,30)},{"bayForward",new JArray(0,0,1)}};
                var value=new JObject{{"format","RoadRushCircuitPitComplexes"},{"schema",1},{"layoutSha256",hash},{"defaultId","wing"},{"complexes",new JArray(Row("wing",0),Row("national",20))}};
                CircuitPitComplexPlan Parse(JObject v)=>CircuitPitComplexPlan.Parse(v.ToString(),region,hash,surfaces);
                void Refuse(JObject v,string name){bool refused=false;try{Parse(v);}catch(InvalidDataException){refused=true;}Check(refused,name);}
                var plan=Parse(value);Check(plan.Complexes.Count==2&&plan.Select(null).Id=="wing","Explicit default with two independent pit complexes");
                Check(plan.Select("national").Bay.x==20&&plan.Select("national").PitRoadId=="national","National4m navigation hint uses actual asymmetric working apron");
                var art=new JObject{{"format","RoadRushCircuitScenery1"},{"layoutSha256",hash},{"worldId",region.id},{"latitude",0},{"longitude",0},{"pitRoadId","wing"},{"bay",new JArray(0,0,30)},{"bayForward",new JArray(0,0,1)},{"parts",new JArray(new JObject{{"name","synthetic"},{"material","concrete"},{"lod",-1},{"collision",false},{"position",new JArray(50,0,30)},{"size",new JArray(1,1,1)},{"yaw",0}})}};
                var original=CircuitSceneryPlan.Parse(art.ToString(),region,hash);var selected=(CircuitSceneryPlan)typeof(CircuitSceneryPlan).GetMethod("WithServiceAnchor",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(original,new object[]{plan.Select("national")});
                Check(selected.PitRoadId=="national"&&selected.Bay.x==20&&selected.PitComplexId=="national","Owned service binding follows National selection");
                Check(original.PitRoadId=="wing"&&original.Bay.x==0&&ReferenceEquals(original.Parts,selected.Parts),"Selection preserves original plan and shares immutable art only");
                var bad=(JObject)value.DeepClone();bad["layoutSha256"]=new string('b',64);Refuse(bad,"Wrong layout hash refused");
                bad=(JObject)value.DeepClone();bad["schema"]=2;Refuse(bad,"Unknown pit schema refused");
                bad=(JObject)value.DeepClone();bad["defaultId"]="absent";Refuse(bad,"Missing default refused");
                bad=(JObject)value.DeepClone();bad["complexes"][1]["id"]="wing";Refuse(bad,"Duplicate complex refused");
                bad=(JObject)value.DeepClone();bad["complexes"][1]["pitRoadId"]="absent";Refuse(bad,"Missing service road refused");
                bad=(JObject)value.DeepClone();bad["complexes"][1]["bay"][1]=.006;Refuse(bad,"6mm anchor height error refused");
                bad=(JObject)value.DeepClone();bad["complexes"][1]["bay"][1]=.004;Check(Parse(bad).Select("national").Bay.y>.0039f,"4mm anchor tolerance retained");
                bad=(JObject)value.DeepClone();bad["complexes"][1]["bayForward"]=new JArray(0,0,-1);Refuse(bad,"Service facing against lane flow refused");
                bad=(JObject)value.DeepClone();bad["complexes"][1]["bay"][2]=1;Refuse(bad,"Service area extending off pavement refused");
                bad=(JObject)value.DeepClone();bad["extra"]=true;Refuse(bad,"Unexpected root shape refused");
                bool absent=false;try{plan.Select("absent");}catch(InvalidDataException){absent=true;}Check(absent,"Unknown event selection refused");
                int packAt=Array.IndexOf(args,"-pitComplexPack");
                if(packAt>=0&&packAt+1<args.Length)
                {
                    var store=new WorldPackStore(Path.Combine(Path.GetDirectoryName(output),"private-import"));var installed=store.Install(args[packAt+1]);
                    var supported=new System.Collections.Generic.Dictionary<string,int>{{"circuit-catalogue",1},{"circuit-layout",1},{"circuit-scenery",1},{"circuit-road-surfaces",1},{"circuit-pit-complexes",1},{"circuit-event",2},{"racing-rules",1},{"provenance",1}};
                    var reader=WorldPackCircuitReader.OpenVerified(store,installed,new PackEngineSupport("0.22.0",supported));
                    var layout=reader.Layout("gp");var raw=JsonUtility.FromJson<GeoRegion>(reader.ReadRawLayoutJson("gp",default));
                    var physical=reader.ReadRoadSurfaces("gp",raw,default);var originalArt=reader.ReadScenery("gp",raw,default);
                    Check(reader.Events.Count==2&&reader.Events.All(e=>e.Readable),"Real pack exposes both supported service events");
                    foreach(string identity in new[]{"wing","national"})
                    {
                        var settings=reader.ReadEvent("gp-"+identity+"-service");
                        Check(settings.PitComplexId==identity&&settings.Event.LayoutId=="gp","Real event selects "+identity+" independently of GP layout/rules");
                        var service=reader.ResolvePitComplex("gp",raw,originalArt,physical,settings.PitComplexId,default);
                        Check(service.PitComplexId==identity&&service.PitRoadId.Contains(identity=="wing"?"international":"national"),"Real owned service resolves "+identity);
                    }
                    Check(originalArt.PitComplexId==null&&originalArt.PitRoadId.Contains("international"),"Real original scenery is not mutated by event selection");
                    supported.Remove("circuit-pit-complexes");supported["circuit-event"]=1;
                    var older=WorldPackCircuitReader.OpenVerified(store,installed,new PackEngineSupport("0.22.0",supported));
                    Check(older.Layout("gp").Readable&&older.Events.All(e=>!e.Readable),"Older capabilities keep GP while skipping schema2pit events");
                    bool refused=false;try{older.ReadEvent("gp-national-service");}catch(InvalidDataException){refused=true;}
                    Check(refused,"Unsupported National event cannot silently fall back to Wing");
                }
                Check(AudioListener.volume==0&&AudioListener.pause,"Verification stays muted");
                Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,new JObject{{"status","PASS_DATA_READERS_ONLY"},{"checks",checks},{"scope","Synthetic native Unity data admission/owned anchor resolution. No Player service, event store, scenery accuracy or Windows hardware acceptance."}}.ToString());code=0;
            }
            catch(Exception error){Debug.LogException(error);if(output!=null)File.WriteAllText(output,new JObject{{"status","FAIL"},{"error",error.ToString()}}.ToString());}
            finally{EditorApplication.Exit(code);}
        }
    }
}
