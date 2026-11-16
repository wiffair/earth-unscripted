using System;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using RoadRush.Core;
namespace RoadRush.Editor
{
    public static class RailwaySnapshotVerification
    {
        public static void Run()
        {
            int checks=0;string prior=PlayerProfile.StorageOverride;
            void Check(bool value,string why){if(!value)throw new Exception("RAIL SNAPSHOT FAIL "+why);checks++;Debug.Log("RAIL SNAPSHOT PASS "+why);}
            try
            {
                PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/rail-snapshot-profile.json");
                var sample=GeoRegion.Load("dee-valley");var a=sample.roads[0].points[0];var b=a+new Vector3(12,1,10);
                sample.railways=new[]{new GeoRailway{id="rail-12",start="101",end="102",kind="rail",name="Fixture",operatorName="Original",electrified="no",points=new[]{a,b},nodeIds=new[]{"101","102"},tracks=2,width=8,layer=1,bridge=true,estimatedProfile=true}};sample.railwayProfilesPrepared=true;sample.roads[0].layer=-1;sample.roads[0].maxVehicleHeight=4.6f;
                string json=WorldSnapshotCodec.CanonicalJson(sample),epoch=WorldSnapshotCodec.Epoch(sample),map=WorldSnapshotCodec.NetworkId(sample);
                // Exercise first-use metadata publication concurrently without calling Unity APIs on workers.
                var shape=typeof(WorldSnapshotCodec).GetMethod("CheckShape",BindingFlags.NonPublic|BindingFlags.Static);
                Parallel.For(0,64,i=>shape.Invoke(null,new object[]{json}));Check(true,"parallel cold typed-schema reads publish complete immutable metadata");
                var imported=WorldSnapshotCodec.Decode(json,map,epoch);
                Check(WorldSnapshotCodec.Compatibility.EndsWith("geometry12"),"geometry compatibility bumped");
                Check(WorldSnapshotCodec.CanonicalJson(imported)==json&&WorldSnapshotCodec.Epoch(imported)==epoch,"complete prepared railway round trip retains exact canonical bytes and epoch");
                Check(imported.railways[0].points[1]==b&&imported.railways[0].nodeIds[1]=="102"&&imported.railways[0].bridge&&imported.railways[0].layer==1&&imported.roads[0].maxVehicleHeight==4.6f,"host profile, nodes and road clearance remain authoritative");
                void Refuse(Action<JObject> mutate,string why,bool shape=false)
                {
                    var obj=JObject.Parse(json);mutate(obj);string altered=obj.ToString(Newtonsoft.Json.Formatting.None);
                    string digest=shape?epoch:WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,map,JsonUtility.ToJson(JsonUtility.FromJson<GeoRegion>(altered)));
                    bool rejected=false;try{WorldSnapshotCodec.Decode(altered,map,digest);}catch(InvalidDataException){rejected=true;}catch(Newtonsoft.Json.JsonException){rejected=true;}Check(rejected,why);
                }
                Refuse(o=>o["railwayProfilesPrepared"]=false,"unprepared source profiles refused even with matching content digest");
                Refuse(o=>o["railways"][0]["id"]="","empty rail ID");
                Refuse(o=>((JArray)o["railways"]).Add(o["railways"][0].DeepClone()),"duplicate rail IDs");
                Refuse(o=>o["railways"][0]["kind"]="motorway","unknown lifecycle");
                Refuse(o=>o["railways"][0]["gauge"]=.1,"invalid gauge");
                Refuse(o=>o["railways"][0]["width"]=101,"unbounded width");
                Refuse(o=>o["railways"][0]["width"]=5,"track envelope exceeds declared physical width");
                Refuse(o=>o["railways"][0]["tracks"]=17,"unbounded track count");
                Refuse(o=>o["railways"][0]["layer"]=21,"invalid layer");
                Refuse(o=>o["railways"][0]["tunnel"]=true,"contradictory bridge/tunnel");
                Refuse(o=>o["railways"][0]["nodeIds"]=new JArray("101"),"unaligned node IDs");
                Refuse(o=>o["railways"][0]["start"]="999","endpoint/node mismatch");
                Refuse(o=>o["railways"][0]["nodeIds"][1]="101","adjacent identical node IDs");
                Refuse(o=>o["railways"][0]["points"][1]=o["railways"][0]["points"][0].DeepClone(),"degenerate rail segment");
                Refuse(o=>o["railways"][0]["points"][1]["x"]=99999,"outlying rail coordinates");
                Refuse(o=>o["roads"][0]["maxVehicleHeight"]=-1,"invalid road vehicle clearance");
                Refuse(o=>o["roads"][0]["layer"]=-21,"invalid road layer");
                Refuse(o=>o["railways"][0]["unknown"]=true,"unknown nested rail field",true);
                Refuse(o=>o["roads"][0]["unknown"]=true,"unknown nested road field",true);
                Refuse(o=>o["railways"][0]["tracks"]="2","string integer coercion refused",true);
                Refuse(o=>o["railways"][0]["tracks"]=2.5,"fractional integer refused",true);
                Refuse(o=>o["railwayProfilesPrepared"]="true","string boolean coercion refused",true);
                Refuse(o=>((JObject)o["railways"][0]).Remove("estimatedProfile"),"missing explicit railway field",true);
                Refuse(o=>o["railways"][0]["points"][0]["y"]=null,"null vector component",true);
                Refuse(o=>{var values=(JArray)o["railways"];while(values.Count<=RailwayMetrics.MaximumWays)values.Add(values[0].DeepClone());},"rail way allocation budget",true);
                Refuse(o=>{var values=(JArray)o["railways"][0]["points"];while(values.Count<=RailwayMetrics.MaximumPointsPerWay)values.Add(values[0].DeepClone());},"per-way allocation budget",true);
                Refuse(o=>{var rails=(JArray)o["railways"];var rail=(JObject)rails[0];var p=(JArray)rail["points"];while(p.Count<4096)p.Add(p[0].DeepClone());for(int i=1;i<17;i++)rails.Add(rail.DeepClone());},"aggregate rail point allocation budget",true);
                string duplicate=json.Replace("\"tracks\":2", "\"tracks\":2,\"tracks\":2");bool duplicateRefused=false;try{WorldSnapshotCodec.Decode(duplicate,map,epoch);}catch(InvalidDataException){duplicateRefused=true;}Check(duplicate!=json&&duplicateRefused,"duplicate nested rail property refused before Unity parsing");
                var changed=JObject.Parse(json);changed["railways"][0]["points"][1]["y"]=(float)b.y+.125f;
                bool tampered=false;try{WorldSnapshotCodec.Decode(changed.ToString(),map,epoch);}catch(InvalidDataException){tampered=true;}Check(tampered,"profile alteration cannot retain signed epoch");
                var empty=GeoRegion.Load("spa");empty.railways=Array.Empty<GeoRailway>();empty.railwayProfilesPrepared=false;string old=WorldSnapshotCodec.CanonicalJson(empty);var oldDecoded=WorldSnapshotCodec.Decode(old,empty.id,WorldSnapshotCodec.Epoch(empty));Check(oldDecoded.railways.Length==0&&!oldDecoded.railwayProfilesPrepared,"empty legacy profile flag remains supported");
                sample.railwayProfilesPrepared=false;Check(WorldSnapshotCodec.CanonicalJson(sample).Contains("railwayProfilesPrepared"),"raw cache canonical identity remains available before host preparation");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/railway-snapshot.txt","PASS "+checks+" railway typed schema/budget/epoch checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=prior;}
        }
    }
}
