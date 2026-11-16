using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class RailwayLanDigestVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool b,string why){if(!b)throw new Exception("LAN RAIL DIGEST FAIL "+why);checks++;Debug.Log("LAN RAIL DIGEST PASS "+why);}
            try
            {
                var region=GeoRegion.Load("dee-valley");region.railways=new[]{new GeoRailway{id="railway-fixture",start="1",end="2",kind="rail",name=null,operatorName=null,electrified=null,bridge=true,layer=1,points=new[]{Vector3.zero,new Vector3(20,4,0)},nodeIds=new[]{"1","2"}}};region.railwayProfilesPrepared=true;
                string baseline=RailwayLanProofEvidence.RailDigest(region),json=JsonUtility.ToJson(region);var roundtrip=JsonUtility.FromJson<GeoRegion>(json);Check(baseline==RailwayLanProofEvidence.RailDigest(roundtrip),"Unity nullable-string serialization preserves canonical rail digest");
                foreach(string field in new[]{"id","start","end","kind","name","operatorName","electrified"}){var clone=JObject.Parse(json);clone["railways"][0][field]="altered";Check(RailwayLanProofEvidence.RailDigest(JsonUtility.FromJson<GeoRegion>(clone.ToString()))!=baseline,"rail digest binds "+field);}
                foreach(string field in new[]{"width","gauge","tracks","layer"}){var clone=JObject.Parse(json);clone["railways"][0][field]=(double)clone["railways"][0][field]+1;Check(RailwayLanProofEvidence.RailDigest(JsonUtility.FromJson<GeoRegion>(clone.ToString()))!=baseline,"rail digest binds "+field);}
                foreach(string field in new[]{"bridge","tunnel","estimatedProfile"}){var clone=JObject.Parse(json);clone["railways"][0][field]=!(bool)clone["railways"][0][field];Check(RailwayLanProofEvidence.RailDigest(JsonUtility.FromJson<GeoRegion>(clone.ToString()))!=baseline,"rail digest binds "+field);}
                var nodes=JsonUtility.FromJson<GeoRegion>(json);nodes.railways[0].nodeIds[1]="99";Check(RailwayLanProofEvidence.RailDigest(nodes)!=baseline,"rail digest binds ordered source node identities");var profile=JsonUtility.FromJson<GeoRegion>(json);profile.railways[0].points[1].y+=.01f;Check(RailwayLanProofEvidence.RailDigest(profile)!=baseline,"rail digest binds exact authoritative elevation profile");
                var flags=JsonUtility.FromJson<GeoRegion>(json);flags.railwayProfilesPrepared=false;Check(RailwayLanProofEvidence.RailDigest(flags)!=baseline,"rail digest binds prepared flag");
                string road=RailwayLanProofEvidence.RoadDigest(region.roads);region.roads[0].maxVehicleHeight=4.0386f;Check(road!=RailwayLanProofEvidence.RoadDigest(region.roads),"road digest binds actual imperial height restriction metadata");road=RailwayLanProofEvidence.RoadDigest(region.roads);region.roads[0].layer++;Check(road!=RailwayLanProofEvidence.RoadDigest(region.roads),"road digest binds layer separation");road=RailwayLanProofEvidence.RoadDigest(region.roads);region.roads[0].points[0].y+=.02f;Check(road!=RailwayLanProofEvidence.RoadDigest(region.roads),"road digest binds physical underpass profile");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/railway-lan-digest.txt","PASS "+checks+" rail/profile/source/road digest checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
