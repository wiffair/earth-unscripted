using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    public static class WorldSnapshotVerification
    {
        public static void Run()
        {
            int checks=0;string priorFolder=RegionCache.OverrideFolder,priorProfile=PlayerProfile.StorageOverride;
            void Check(bool value,string message){if(!value)throw new Exception("SNAPSHOT FAIL "+message);checks++;Debug.Log("SNAPSHOT PASS "+message);}
            try
            {
                string root=Path.GetFullPath("Builds/Verification/snapshot-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);RegionCache.OverrideFolder=Path.Combine(root,"Regions");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");
                using(var host=new DataSigningKey())
                {
                    foreach(string id in new[]{"dee-valley","silverstone","spa"})
                    {
                        var original=GeoRegion.Load(id);RailwayProfileSolver.Prepare(original);string json=WorldSnapshotCodec.CanonicalJson(original),epoch=WorldSnapshotCodec.Epoch(original);string payload=Path.Combine(root,id+".json");File.WriteAllText(payload,json,new UTF8Encoding(false));
                        var manifest=new DataFileManifest{id="snapshot-"+id,kind="region-snapshot",pack="earthunscripted-worlds",fromVersion=id,toVersion=epoch,source="Connected host",licence="ODbL-1.0",bytes=new FileInfo(payload).Length,sha256=DataPackageRules.HashFile(payload)};host.Sign(manifest);
                        var imported=WorldSnapshotCodec.Import(payload,manifest,host.Fingerprint);Check(imported.id!=id&&RegionCache.Safe(imported.id)&&WorldSnapshotCodec.NetworkId(imported)==id,"safe local alias retains host identity for "+id);
                        Check(!File.Exists(Path.Combine(RegionCache.Folder,imported.id+".json"))&&File.Exists(RegionCache.SourcePath(imported.id)),"prepared snapshots stay outside older-release raw caches for "+id);
                        var loaded=GeoRegion.Load(imported.id);Check(Math.Abs(loaded.roads[0].width-original.roads[0].width)<.00001f,"prepared road widths are not inflated again for "+id);
                        Check(WorldEpoch.Matches(WorldSnapshotCodec.Epoch(loaded),epoch),"cached reload retains the complete host epoch for "+id);
                        Check(original.id==id&&string.IsNullOrEmpty(original.networkMapId),"snapshot serialization never mutates host identity for "+id);
                    }
                    var sample=GeoRegion.Load("dee-valley");RailwayProfileSolver.Prepare(sample);string valid=WorldSnapshotCodec.CanonicalJson(sample),digest=WorldSnapshotCodec.Epoch(sample);
                    void Refused(string json,string why)
                    {
                        bool rejected=false;try{WorldSnapshotCodec.Decode(json,"dee-valley",digest);}catch(InvalidDataException){rejected=true;}catch(Newtonsoft.Json.JsonException){rejected=true;}Check(rejected,why);
                    }
                    var broken=JObject.Parse(valid);broken["nx"]=513;Refused(broken.ToString(),"wrong terrain dimensions are rejected before Prepare");
                    broken=JObject.Parse(valid);broken["preparedWidths"]=false;Refused(broken.ToString(),"raw widths cannot masquerade as a prepared world");
                    broken=JObject.Parse(valid);broken["roads"][0]["start"]=null;Refused(broken.ToString(),"null junction identifiers cannot reach spatial indexing");
                    broken=JObject.Parse(valid);broken["drivingSide"]=0;Refused(broken.ToString(),"invalid driving-side metadata is rejected");
                    Refused(valid.Replace("\"latitude\":", "\"latitude\":1,\"latitude\":"),"duplicate root metadata is rejected");
                    Refused("{\"heights\":[NaN]}","non-finite JSON values are rejected before allocation");
                    broken=JObject.Parse(valid);broken["roads"][0]["points"][0]["x"]=10000000;Refused(broken.ToString(),"outlying geometry cannot create an unbounded spatial index");
                    broken=JObject.Parse(valid);broken["heights"][0]=(double)broken["heights"][0]+1;Refused(broken.ToString(),"altered terrain cannot retain the host's signed epoch");
                    bool wrongKey=false;using(var other=new DataSigningKey())
                    {
                        string payload=Path.Combine(root,"dee-valley.json");var manifest=new DataFileManifest{id="host",kind="region-snapshot",pack="world",fromVersion="dee-valley",toVersion=digest,source="Host",licence="ODbL-1.0",bytes=new FileInfo(payload).Length,sha256=DataPackageRules.HashFile(payload)};host.Sign(manifest);
                        try{WorldSnapshotCodec.Import(payload,manifest,other.Fingerprint);}catch(InvalidDataException){wrongKey=true;}
                    }
                    Check(wrongKey,"an unrelated signing key cannot import a map into the cache");
                }
                File.WriteAllText("Builds/Verification/world-snapshot.txt","PASS "+checks+" signed snapshot/alias/width/validation checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{RegionCache.OverrideFolder=priorFolder;PlayerProfile.StorageOverride=priorProfile;}
        }
    }
}
