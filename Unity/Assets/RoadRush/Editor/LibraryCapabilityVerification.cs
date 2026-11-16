using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RoadRush.Packs;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>Data-only check of actual library and loader capabilities; no game scene or real pack library.</summary>
    public static class LibraryCapabilityVerification
    {
        static int checks;
        static void Check(bool condition, string reason)
        { if (!condition) throw new InvalidDataException(reason); checks++; }

        public static void Run()
        {
            int code=1;string output=null;
            AudioListener.volume=0;AudioListener.pause=true;
            try
            {
                string[] args=Environment.GetCommandLineArgs();
                Func<string,string> argument=name=>args[Array.IndexOf(args,name)+1];
                output=argument("-libraryCapabilityResult");
                var library=(PackEngineSupport)typeof(WorldPackMenu).GetMethod("Readers",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{"0.22.0"});
                var circuitType=typeof(WorldPackMenu).Assembly.GetType("RoadRush.PackCircuitSources",true);
                var circuit=(PackEngineSupport)circuitType.GetMethod("Readers",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{"0.22.0"});
                foreach(var pair in circuit.Features)
                    Check(library.Features.TryGetValue(pair.Key,out int schema)&&schema==pair.Value,"Library differs from implemented circuit reader: "+pair.Key);
                Check(library.Features["circuit-event"]==2&&library.Features["circuit-pit-complexes"]==1,"Event-selected pit inspection capability missing");
                Check(library.Features["regional-index"]==1&&library.Features["regional-buildings"]==1,"Regional readers lost");
                var store=new WorldPackStore(Path.Combine(Path.GetDirectoryName(output),"isolated-library"));
                var current=store.Install(argument("-libraryCurrentPack"));
                var librarySubset=WorldPackCompatibility.Resolve(current.Manifest,library);
                var circuitSubset=WorldPackCompatibility.Resolve(current.Manifest,circuit);
                Check(librarySubset.CanActivate&&librarySubset.SubsetIdentity==circuitSubset.SubsetIdentity,"Library and loader resolve different current pack subsets");
                var reader=WorldPackCircuitReader.OpenVerified(store,current,library);
                Check(reader.Events.Count==2&&reader.Events.All(e=>e.Readable),"Library check incorrectly skips selected-pit events");
                foreach(string id in new[]{"wing","national"})
                    Check(reader.ReadEvent("gp-"+id+"-service").PitComplexId==id,"Library cannot inspect "+id+" pit event");
                var legacy=store.Install(argument("-libraryLegacyPack"));
                var legacyReader=WorldPackCircuitReader.OpenVerified(store,legacy,library);
                Check(legacyReader.Layouts.Any(l=>l.Readable),"Frozen legacy circuit no longer readable");
                Check(WorldPackCompatibility.Resolve(legacy.Manifest,library).SubsetIdentity==WorldPackCompatibility.Resolve(legacy.Manifest,circuit).SubsetIdentity,"Legacy resolution differs between menu and loader");
                Check(AudioListener.volume==0&&AudioListener.pause,"Verification must stay silent");
                File.WriteAllText(output,new JObject{{"status","PASS_LIBRARY_CAPABILITIES_DATA_ONLY"},{"checks",checks},{"currentManifest",current.Manifest.ManifestSha256},{"currentSubset",librarySubset.SubsetIdentity},{"legacyManifest",legacy.Manifest.ManifestSha256},{"scope","Actual Unity library/loader capability resolution and verified isolated pack reads. No Player UI, scene activation, native-driving certificate, Windows hardware or real-career claim."}}.ToString());
                code=0;
            }
            catch(Exception error)
            { Debug.LogException(error);if(output!=null)File.WriteAllText(output,new JObject{{"status","FAIL"},{"error",error.ToString()}}.ToString()); }
            finally { EditorApplication.Exit(code); }
        }
    }
}
