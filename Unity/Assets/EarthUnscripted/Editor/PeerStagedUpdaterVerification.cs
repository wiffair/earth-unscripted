using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EarthUnscripted.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    // Verifies the normal updater consumes a formerly peer-staged payload without
    // another source download, preserving the live scene and signed cache header.
    [InitializeOnLoad] public static class PeerStagedUpdaterVerification
    {
        const string Key="EUPeerStagedUpdater";static string root;static GameSession session;static GeoRegion region;static Vector3 vertex;static UpdaterRequestProbe requester;static CountryPack pack;static int phase,checks;static double deadline;
        static PeerStagedUpdaterVerification()
        {
            if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");CountryUpdateService.OverrideFolder=Path.Combine(root,"Updates");CountryUpdateService.AllowVerification=true;CountryUpdateService.FetchOverride=Fetch;deadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");root=Path.GetFullPath("Builds/Verification/peer-staged-updater-"+DateTime.UtcNow.Ticks);string country=Path.Combine(root,"Country");Directory.CreateDirectory(country);
            string source=Directory.GetDirectories("Builds/Verification","replication-*").OrderByDescending(x=>x).First(p=>new CountryPack(p).Version=="13");foreach(string file in new[]{"pack.json","map.sqlite"})File.Copy(Path.Combine(source,file),Path.Combine(country,file));var pack=new CountryPack(country);
            string payload=Path.Combine(root,"source.osc.gz");using(var file=File.Create(payload))using(var gzip=new GZipStream(file,CompressionMode.Compress))using(var writer=new StreamWriter(gzip,Encoding.UTF8))writer.Write("<osmChange version='0.6'><modify><node id='3' lat='52.0004' lon='-2.9999'/></modify></osmChange>");
            var manifest=new DataFileManifest{format=2,id=pack.Id+"-14",kind="map-diff",pack=pack.Id,fromVersion="13",toVersion="14",timestamp="2026-11-05T00:00:00Z",bytes=new FileInfo(payload).Length,sha256=DataPackageRules.HashFile(payload),source=pack.ReplicationSource+"/000/000/014.osc.gz",licence="ODbL-1.0"};
            using(var key=new DataSigningKey()){key.Sign(manifest);PeerCountryUpdates.Stage(new PeerFileResult{manifest=manifest,path=payload},country,Path.Combine(root,"Updates"),key.Fingerprint);File.WriteAllText(Path.Combine(root,"fingerprint.txt"),key.Fingerprint);}
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{region="dee-valley",quality=0,volume=0,rumble=0,livingScenery=false}));SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static Task Fetch(string url,string destination,int maximum,CancellationToken token)
        {
            if(!url.EndsWith("/state.txt"))return Task.FromException(new Exception("Updater tried to redownload a peer-cached payload"));
            token.ThrowIfCancellationRequested();File.WriteAllText(destination,"sequenceNumber=14\ntimestamp=2026-11-05T00\\:00\\:00Z\n");return Task.CompletedTask;
        }
        static void Check(bool value,string text){if(!value)throw new Exception("PEER STAGED UPDATER FAIL: "+text);checks++;Debug.Log("PEER STAGED UPDATER PASS: "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Peer-staged updater timed out phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates)return;session.Updates.Automatic=false;region=session.World.Route.Region;vertex=session.World.GeographicWorld.GetComponentInChildren<MeshFilter>().sharedMesh.vertices[0];pack=new CountryPack(Path.Combine(root,"Country"));requester=new GameObject("Peer staged updater requester").AddComponent<UpdaterRequestProbe>();requester.Begin(session.Updates,pack);phase=1;return;
                }
                if(phase==1)
                {
                    if(session.Updates.Busy)return;
                    Check(new CountryPack(pack.Folder).Version=="14","normal updater applies a previously peer-staged daily diff");
                    Check(session.Updates.SourceRequests==1,"only latest state is checked; neither daily state nor map payload is redownloaded");
                    string directory=Path.Combine(CountryUpdateService.OverrideFolder,pack.Id),header=Path.Combine(directory,pack.Id+"-14.json");var manifest=JsonUtility.FromJson<DataFileManifest>(File.ReadAllText(header));
                    Check(DataPackageRules.VerifySignature(manifest,File.ReadAllText(Path.Combine(root,"fingerprint.txt"))),"activation preserves the original signed peer header");
                    Check(DataPackageRules.FileMatches(DataPackageRules.PathFor(directory,manifest),manifest),"cached payload remains complete and hash-valid after activation");
                    Check(ReferenceEquals(region,session.World.Route.Region)&&vertex==session.World.GeographicWorld.GetComponentInChildren<MeshFilter>().sharedMesh.vertices[0],"country maintenance leaves active world object and terrain vertices unchanged");
                    Check(UnityEngine.Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==1,"peer update activation never reloads the active drive");
                    File.WriteAllText("Builds/Verification/peer-staged-updater.txt","PASS "+checks+" peer-cache reuse / source request / signed header / active-world pinning checks\n"+root+"\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);CountryUpdateService.FetchOverride=null;CountryUpdateService.AllowVerification=false;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
