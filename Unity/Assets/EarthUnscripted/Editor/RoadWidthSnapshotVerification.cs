using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    // Wire-format acceptance only. Driving/terrain mesh acceptance remains separate.
    public static class RoadWidthSnapshotVerification
    {
        public static void Run()
        {
            int checks=0;string previous=PlayerProfile.StorageOverride;
            void Check(bool valid,string why){if(!valid)throw new Exception("WIDTH SNAPSHOT FAIL "+why);checks++;Debug.Log("WIDTH SNAPSHOT PASS "+why);}
            try
            {
                PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/width-snapshot-profile.json");
                var world=GeoRegion.Load("dee-valley");RailwayProfileSolver.Prepare(world);string roadsBefore=JsonUtility.ToJson(new RoadList{roads=world.roads});
                Check(world.roadWidthSchema==0&&(world.roadWidthProfiles==null||world.roadWidthProfiles.Length==0),"legacy source remains explicitly unprofiled");
                string legacy=WorldSnapshotCodec.CanonicalJson(world);var old=WorldSnapshotCodec.Decode(legacy,WorldSnapshotCodec.NetworkId(world),WorldSnapshotCodec.Epoch(world));
                Check(old.roadWidthSchema==0,"legacy payload still decodes without profile invention");
                world.roadWidthSchema=1;world.roadWidthProfiles=world.roads.Where(r=>!r.tunnel).Select(r=>new RoadWidthProfileDto{roadId=r.id,distances=new[]{0f,r.length},widths=new[]{r.width,r.width},derivatives=new[]{0f,0f}}).ToArray();
                var first=world.roadWidthProfiles[0];first.widths[0]=Mathf.Max(2,first.widths[0]-.3f);first.derivatives[0]=.001f;first.bevelBoundary=true;
                var field=new EffectiveRoadWidthField(world.roadWidthProfiles,world.roads);string canonical=WorldSnapshotCodec.CanonicalJson(world),map=WorldSnapshotCodec.NetworkId(world),epoch=WorldSnapshotCodec.Epoch(world);
                var imported=WorldSnapshotCodec.Decode(canonical,map,epoch);Check(WorldSnapshotCodec.Compatibility.EndsWith("geometry12-clearance1"),"legacy width schema with clearance recipe separates incompatible derived geometry");
                Check(WorldSnapshotCodec.CanonicalJson(imported)==canonical&&WorldSnapshotCodec.Epoch(imported)==epoch,"host/guest canonical controls and epoch round-trip exactly");
                string oldRecipeEpoch=WorldEpoch.Compute("EarthUnscripted/"+Application.version+"/geometry12",map,canonical);
                bool oldRecipeRefused=false;
                try{WorldSnapshotCodec.Decode(canonical,map,oldRecipeEpoch);}catch(InvalidDataException){oldRecipeRefused=true;}
                Check(oldRecipeRefused,"same raw geography with stale derived-height recipe is rejected");
                var guest=new EffectiveRoadWidthField(imported.roadWidthProfiles,imported.roads);var sourceRoad=world.roads.Single(r=>r.id==first.roadId);var guestRoad=imported.roads.Single(r=>r.id==first.roadId);
                for(int i=0;i<=20;i++)Check(field.At(sourceRoad,sourceRoad.length*i/20.0)==guest.At(guestRoad,guestRoad.length*i/20.0),"guest evaluates exact published float controls at sample "+i);
                Check(guest.UseBevelBoundary(guestRoad)&&imported.roadWidthProfiles[0].derivatives[0]==.001f,"bevel and supplied derivatives are authoritative; no guest slope solve");
                first.widths[0]+=1;Check(field.At(sourceRoad,0)!=first.widths[0],"runtime field owns immutable control copies");first.widths[0]-=1;
                var copy=field.CopySnapshot();copy.Single(p=>p.roadId==sourceRoad.id).widths[0]+=1;Check(field.At(sourceRoad,0)==guest.At(guestRoad,0),"snapshot copies cannot mutate resident field");
                Check(JsonUtility.ToJson(new RoadList{roads=world.roads})==roadsBefore,"profile construction leaves source roads and source widths unchanged");
                void Refuse(Action<JObject> mutate,string why,bool shape=false)
                {
                    var obj=JObject.Parse(canonical);mutate(obj);string altered=obj.ToString(Formatting.None);string alteredEpoch=shape?epoch:WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,map,JsonUtility.ToJson(JsonUtility.FromJson<GeoRegion>(altered)));bool refused=false;
                    try{WorldSnapshotCodec.Decode(altered,map,alteredEpoch);}catch(InvalidDataException){refused=true;}catch(JsonException){refused=true;}Check(refused,why);
                    Check(WorldSnapshotCodec.CanonicalJson(imported)==canonical,"rejected candidate leaves independent reference object unchanged: "+why);
                }
                Refuse(o=>o["roadWidthSchema"]=2,"unknown profile schema");
                Refuse(o=>o["roadWidthSchema"]=0,"profile controls cannot masquerade as legacy");
                Refuse(o=>o["roadWidthProfiles"]=new JArray(),"prepared schema requires complete road coverage");
                Refuse(o=>((JArray)o["roadWidthProfiles"]).RemoveAt(0),"missing road profile");
                Refuse(o=>o["roadWidthProfiles"][0]["roadId"]=o["roadWidthProfiles"][1]["roadId"].DeepClone(),"duplicate profile identity");
                Refuse(o=>o["roadWidthProfiles"][0]["roadId"]="unknown-road","unknown road profile");
                Refuse(o=>o["roadWidthProfiles"][0]["distances"][1]=0,"non-increasing profile stations");
                Refuse(o=>o["roadWidthProfiles"][0]["distances"][0]=1,"profile source domain must begin at zero");
                Refuse(o=>o["roadWidthProfiles"][0]["widths"][0]=81,"unbounded effective width");
                Refuse(o=>o["roadWidthProfiles"][0]["derivatives"][0]=9,"unbounded derivative");
                Refuse(o=>o["roadWidthProfiles"][0]["derivatives"]=new JArray(0),"unaligned control arrays");
                Refuse(o=>{var p=o["roadWidthProfiles"][0];p["widths"]=new JArray(80,80);p["derivatives"]=new JArray(1,-1);},"analytic interior cubic overshoot");
                Refuse(o=>o["roads"][0]["id"]=null,"explicit profiles require valid canonical road IDs");
                Refuse(o=>o["roadWidthProfiles"][0]["bevelBoundary"]="true","no boolean coercion",true);
                Refuse(o=>((JObject)o["roadWidthProfiles"][0]).Remove("bevelBoundary"),"missing explicit boundary semantics",true);
                Refuse(o=>o["roadWidthProfiles"][0]["widths"]=new JArray(Enumerable.Repeat(8f,513)),"station allocation bound",true);
                // Direct typed-pass probes distinguish preallocation rejection from later semantic alignment rejection.
                var typedPass=typeof(WorldSnapshotCodec).GetMethod("CheckShape",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                foreach(string control in new[]{"distances","widths","derivatives"})
                {
                    var oversized=JObject.Parse(canonical);for(int i=0;i<164;i++)oversized["roadWidthProfiles"][i][control]=new JArray(Enumerable.Repeat(8f,512));bool bounded=false;
                    try{typedPass.Invoke(null,new object[]{oversized.ToString(Formatting.None)});}catch(System.Reflection.TargetInvocationException e){bounded=e.InnerException is InvalidDataException&&e.InnerException.Message.Contains("width total control budget");}
                    Check(bounded,control+" aggregate allocation is refused by the typed streaming pass before DTO allocation");
                }
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/road-width-snapshot.txt","PASS "+checks+" width protocol/immutability/tamper checks. No surface activation/native claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;}
        }
        [Serializable]sealed class RoadList{public GeoRoad[] roads;}
    }
}
