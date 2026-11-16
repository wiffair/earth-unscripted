using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    /// <summary>Real container/boot-path checks before the separate native visual
    /// and moving-vehicle acceptance. All writes stay in explicit private QA paths.</summary>
    public static class ShowcaseVerification
    {
        public static void RunAndBuild()
        {
            var args=Environment.GetCommandLineArgs();
            string Arg(string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new ArgumentException(key);return Path.GetFullPath(args[i+1]);}
            string archive=Arg("-showcaseArchive"),output=Arg("-showcaseOutput");
            if(!output.StartsWith("/private/tmp/rr-showcase-",StringComparison.Ordinal)||Directory.Exists(output))throw new IOException("Fresh private output only");
            Directory.CreateDirectory(output);var checks=new List<string>();
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks.Add(message);}
            ProjectSetup.Configure();
            var authored=Resources.Load<UnityEngine.Rendering.VolumeProfile>(WorldAtmosphere.ResourcePath);
            string AuthoredState()=>UnityEditor.EditorJsonUtility.ToJson(authored)+string.Join("\n",authored.components.Select(c=>UnityEditor.EditorJsonUtility.ToJson(c)));
            string authoredBefore=AuthoredState();
            var fixture=new GameObject("Private night adaptation fixture");
            try
            {
                var atmosphere=WorldAtmosphere.Install(fixture.transform);
                Check(!ReferenceEquals(authored,atmosphere.OwnedProfile)&&!authored.components.Any(c=>atmosphere.OwnedProfile.components.Any(o=>ReferenceEquals(c,o))),"Night adaptation owns all cloned volume components");
                atmosphere.OwnedProfile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var colours);
                float baseline=colours.postExposure.value;
                atmosphere.SetDaylight(0);Check(Mathf.Abs(colours.postExposure.value-baseline-2)<.0001f,"Full night adds bounded2EV display adaptation");
                atmosphere.SetDaylight(.5f);Check(Mathf.Abs(colours.postExposure.value-baseline-1)<.0001f,"Twilight smoothly fades exposure adaptation");
                atmosphere.SetDaylight(1);Check(colours.postExposure.value==baseline,"Full daylight preserves authored exposure");
                atmosphere.SetDaylight(0);atmosphere.SetDaylight(0);Check(Mathf.Abs(colours.postExposure.value-baseline-2)<.0001f,"Repeated night updates do not accumulate exposure");
                atmosphere.SetDaylight(-1);Check(Mathf.Abs(colours.postExposure.value-baseline-2)<.0001f&&WorldAtmosphere.AdaptationStops(2)==0,"Out-of-range daylight cannot exceed adaptation bounds");
            }
            finally{UnityEngine.Object.DestroyImmediate(fixture);}
            Check(authoredBefore==AuthoredState(),"Night adaptation never edits authored Resources profile");

            var store=new WorldPackStore(Path.Combine(output,"store"));var installed=store.Install(archive);
            var engine=new PackEngineSupport("0.21.0",new Dictionary<string,int>{{"circuit-catalogue",1},{"circuit-layout",1},{"provenance",1},{"circuit-scenery",1}});
            var reader=WorldPackCircuitReader.OpenVerified(store,installed,engine);
            var layout=reader.Layouts.Single();var reference=WorldPackCircuitReference.For(installed.Manifest,layout);reference.Store(Path.Combine(output,"store"),CancellationToken.None);
            string sourceJson=reader.ReadRawLayoutJson(layout.Id);
            string layoutHash=WorldPackPolicy.Hash(System.Text.Encoding.UTF8.GetBytes(sourceJson));
            var region=WorldSnapshotCodec.DecodeBootSource(sourceJson,out _);region.CircuitScenery=reader.ReadScenery(layout.Id,region,CancellationToken.None);
            Check(region.circuit&&region.CircuitScenery!=null&&region.CircuitScenery.Parts.Count>1000,"Verified data reader binds source-specific scenery; normal binder awaits native test");
            var stand=region.CircuitScenery.Parts.First(p=>p.Name.Contains("stepped foundation")&&p.Collision);
            var standCentre=stand.Vertices.Aggregate(Vector3.zero,(sum,p)=>sum+p)/stand.Vertices.Length;
            Check(region.CircuitScenery.BlocksVegetation(standCentre.x,standCentre.z),"Physical grandstand excludes procedural tree crowns");
            Check(!region.CircuitScenery.BlocksVegetation(5999,5999),"Structure exclusion does not suppress remote landscape vegetation");
            Check(region.preparedWidths&&region.roads[0].width==16,"Explicit16m main width is not inflated twice");
            var prepared=WorldDataFinalizer.PrepareOwned(region,null,CancellationToken.None);
            Check(region.roads.Length>=2&&region.roads.Length<=3&&region.roads.All(r=>new[]{"silverstone-showcase-main","silverstone-mapped-international-pit","silverstone-mapped-national-pit"}.Contains(r.id)),"Only mapped main circuit and named real pit lanes are prepared");
            var pit=region.roads.Single(r=>r.id==region.CircuitScenery.PitRoadId);var route=new RoutePath(region);
            foreach(var p in new[]{pit.points[0],pit.points[pit.points.Length-1]})
            {
                float d=route.Closest(p,out _,out float separation);var road=route.At(d);
                Check(separation<.005f&&Mathf.Abs(p.y-road.y)<.005f,"Mapped pit/main shared endpoint within5mm");
            }
            float maxChange=0,previous=0;
            for(int i=1;i<region.route.Length;i++)
            {
                var v=region.route[i]-region.route[i-1];float grade=v.y/new Vector2(v.x,v.z).magnitude;
                if(i>1)maxChange=Mathf.Max(maxChange,Mathf.Abs(grade-previous));previous=grade;
            }
            Check(maxChange<.01f,"Conditioned profile has no abrupt per4m grade jump");
            Check(!WorldPackCompatibility.Resolve(installed.Manifest,new PackEngineSupport("0.20.1",new Dictionary<string,int>{{"circuit-catalogue",1},{"circuit-layout",1},{"provenance",1}})).CanActivate,"Older engine refuses required physical-environment capability safely");
            string raw=reader.ReadRawLayoutJson(layout.Id);var original=JsonUtility.FromJson<GeoRegion>(raw);
            string scenery=File.ReadAllText(Path.Combine(installed.Directory,"scenery.json"));
            bool rejected=false;var bad=JObject.Parse(scenery);bad["layoutSha256"]=new string('0',64);
            try{CircuitSceneryPlan.Parse(bad.ToString(),original,layoutHash);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Wrong layout hash cannot borrow scenery");
            bad=JObject.Parse(scenery);bad["parts"][0]["material"]="untrusted-shader";rejected=false;
            try{CircuitSceneryPlan.Parse(bad.ToString(),original,layoutHash);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"External shader/material names refused");
            bad=JObject.Parse(scenery);bad["bay"]=new JArray(5000,0,5000);rejected=false;
            try{CircuitSceneryPlan.Parse(bad.ToString(),original,layoutHash);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Remote service bay refused");
            // This tiny source mesh has twelve triangle corners. Two visual
            // LODs and one collider must allocate36 vertices, not just three.
            var minimal=JObject.Parse(scenery);
            var corners=new JArray(0,1,2,0,1,2,0,1,2,0,1,2);
            var triangle=new JObject { ["name"]="Allocation fixture",["material"]="concrete",["lod"]=-1,["collision"]=true,
                ["vertices"]=new JArray(new JArray(0,0,0),new JArray(1,0,0),new JArray(0,0,1)),["triangles"]=corners };
            minimal["parts"]=new JArray(triangle);
            var small=CircuitSceneryPlan.Parse(minimal.ToString(Newtonsoft.Json.Formatting.None),original,layoutHash);
            Check(small.AllocatedMeshVertices==36,"Expanded mesh budget includes both visual LODs and collider");
            var repeated=new JArray(Enumerable.Range(0,180000).Select(i=>i%3));
            triangle["triangles"]=repeated;
            // Six colliders stay well below the existing source-point/part/file
            // limits, but expand to3.24million vertices across all three copies.
            minimal["parts"]=new JArray(Enumerable.Range(0,6).Select(_=>triangle.DeepClone()));rejected=false;
            try{CircuitSceneryPlan.Parse(minimal.ToString(Newtonsoft.Json.Formatting.None),original,layoutHash);}
            catch(InvalidDataException error){rejected=error.Message.Contains("allocated mesh vertex budget")&&CircuitSceneryPlan.IsCapacityFailure(error);}
            Check(rejected,"Tiny repeated source mesh cannot bypass expanded allocation cap");
            triangle["triangles"]=corners;minimal["parts"]=new JArray(triangle);
            string smallJson=minimal.ToString(Newtonsoft.Json.Formatting.None);
            string excessiveTokens=smallJson.Substring(0,smallJson.Length-1)+",\"unused\":["+string.Join(",",Enumerable.Repeat("0",3000001))+"]}";rejected=false;
            try{CircuitSceneryPlan.Parse(excessiveTokens,original,layoutHash);}
            catch(InvalidDataException error){rejected=error.Message.Contains("JSON token budget")&&CircuitSceneryPlan.IsCapacityFailure(error);}
            Check(rejected,"Small JSON values cannot bypass parsed-token admission budget");
            rejected=false;
            try{CircuitSceneryPlan.Parse(new string(' ',CircuitSceneryPlan.MaximumBytes+1),original,layoutHash);}
            catch(InvalidDataException error){rejected=error.Message.Contains("byte budget")&&CircuitSceneryPlan.IsCapacityFailure(error);}
            Check(rejected,"Direct scenery parser enforces its byte limit");
            File.WriteAllText(Path.Combine(output,"verification.json"),new JObject{["status"]="PASS_CONTAINER_AND_PREPARATION_ONLY",["checks"]=new JArray(checks),["parts"]=region.CircuitScenery.Parts.Count,["allocatedMeshVertices"]=region.CircuitScenery.AllocatedMeshVertices,["maxGradeChange"]=maxChange,["nativeVisualVerified"]=false}.ToString());
            LoadingCameraQaBuild.Build();
        }
    }
}
