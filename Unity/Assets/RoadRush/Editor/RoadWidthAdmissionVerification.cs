using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using RoadRush.Core;
namespace RoadRush.Editor
{
    public static class RoadWidthAdmissionVerification
    {
        public static void Run()
        {
            int checks=0;string previous=PlayerProfile.StorageOverride;
            void Check(bool good,string why){if(!good)throw new Exception("WIDTH ADMISSION FAIL "+why);checks++;Debug.Log("WIDTH ADMISSION PASS "+why);}
            try
            {
                var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"-roadRushWidthWorld");if(arg<0||arg+1>=args.Length)throw new InvalidDataException("Explicit prepared source fixture required");
                string input=Path.GetFullPath(args[arg+1]),sourceHash=DataPackageRules.HashFile(input),root=Path.GetFullPath("Builds/Verification/WidthAdmission/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(root);PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");
                var world=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(input));world.Prepare();string before=JsonUtility.ToJson(world);Check(world.roadWidthSchema==0&&world.preparedWidths&&world.railwayProfilesPrepared,"fixture is a prepared legacy-width source with finalized rails");
                // Background execution is intentional: no UnityEngine.Object or visible region is supplied.
                var report=Task.Run(()=>RoadWidthWorldPreparation.PrepareOwned(world,CancellationToken.None)).GetAwaiter().GetResult();
                Check(report.status=="prepared-geometric-admission"&&report.blockedChains==0,"actual fixture has no unresolved changing-width chains");
                Check(report.outwardChains==6&&report.inwardChains==2,"six outward and two inward alternatives survive actual Unity worker admission");
                Check(report.profiles==world.roads.Count(r=>!r.tunnel)&&world.PreparedRoadWidths!=null,"all visible source roads receive immutable canonical controls");
                var a=JObject.Parse(before);var b=JObject.Parse(JsonUtility.ToJson(world));foreach(string field in new[]{"roadWidthSchema","roadWidthProfiles"}){a.Remove(field);b.Remove(field);}Check(JToken.DeepEquals(a,b),"all non-profile canonical source values remain unchanged");
                string canonical=WorldSnapshotCodec.CanonicalJson(world),epoch=WorldSnapshotCodec.Epoch(world);var guest=WorldSnapshotCodec.Decode(canonical,WorldSnapshotCodec.NetworkId(world),epoch);
                var hostSurfaces=Task.Run(()=>RoadWidthWorldPreparation.PrepareSurfacesOwned(world,CancellationToken.None)).GetAwaiter().GetResult();var guestSurfaces=Task.Run(()=>RoadWidthWorldPreparation.PrepareSurfacesOwned(guest,CancellationToken.None)).GetAwaiter().GetResult();
                Check(hostSurfaces.Parts.Count>0&&hostSurfaces.Parts.Count==guestSurfaces.Parts.Count,"host and peer reconstruct matching finalized surface records without guest admission solve");
                Check(hostSurfaces.Parts.All(p=>{var x=p.Value.Surface;var y=guestSurfaces.Parts[p.Key].Surface;return x.Vertices.SequenceEqual(y.Vertices)&&x.Triangles.SequenceEqual(y.Triangles)&&x.Widths.SequenceEqual(y.Widths)&&x.Distances.SequenceEqual(y.Distances);}),"host and guest surface vertices, triangles and row controls match exactly");
                Check(DataPackageRules.HashFile(input)==sourceHash,"input world bytes unchanged");
                File.WriteAllText(Path.Combine(root,"candidate-world.json"),canonical);File.WriteAllText(Path.Combine(root,"admission.json"),JsonUtility.ToJson(report,true));
                File.WriteAllText(Path.Combine(root,"receipt.json"),new JObject{{"status","PASS_CPU_PREPARATION_ONLY"},{"checks",checks},{"source",input},{"sourceSHA256",sourceHash},{"sourceUnchanged",true},{"canonicalEpoch",epoch},{"profiles",report.profiles},{"outwardChains",report.outwardChains},{"inwardChains",report.inwardChains},{"changedSurfaces",hostSurfaces.Parts.Count},{"elapsedAdmissionMilliseconds",report.elapsedMilliseconds},{"checkpoints",report.checkpoints},{"limitations","Actual Unity worker math/data preparation and exact peer reconstruction; no collider, terrain-support, native visuals, gameplay or active-world transaction acceptance."}}.ToString());EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;}
        }
    }
}
