using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    public sealed class UpdaterRequestProbe : MonoBehaviour
    {public void Begin(CountryUpdateService service,CountryPack pack){StartCoroutine(service.BeforePrepare(pack));}}
    [InitializeOnLoad] public static class UpdaterVerification
    {
        const string Key="RRCountryUpdater";static string root;static GameSession session;static int phase,frames,checks;static double deadline;static bool blocked=true;static readonly ManualResetEventSlim release=new ManualResetEventSlim(false);static UpdaterRequestProbe requester;static CountryPack pack;static GeoRegion active;static Vector3 vertex;
        static UpdaterVerification()
        {
            if(!SessionState.GetBool(Key,false))return;root=SessionState.GetString(Key+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");CountryUpdateService.OverrideFolder=Path.Combine(root,"Updates");CountryUpdateService.AllowVerification=true;CountryUpdateService.FetchOverride=FakeFetch;CountryUpdateService.BusyOverride=()=>blocked;deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");string previous=Directory.GetDirectories("Builds/Verification","replication-*").OrderByDescending(x=>x).First();string root=Path.GetFullPath("Builds/Verification/updater-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(Path.Combine(root,"Country"));
            foreach(string name in new[]{"pack.json","map.sqlite"})File.Copy(Path.Combine(previous,name),Path.Combine(root,"Country",name));
            var metadata=JObject.Parse(File.ReadAllText(Path.Combine(root,"Country/pack.json")));if((string)metadata["version"]!="13")throw new Exception("Updater fixture requires completed replication13 test");
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(new PlayerProfile{region="dee-valley",quality=0,volume=0,rumble=0,livingScenery=false}));
            using(var file=File.Create(Path.Combine(root,"source.osc.gz")))using(var gzip=new GZipStream(file,CompressionMode.Compress))using(var writer=new StreamWriter(gzip,Encoding.UTF8))writer.Write("<osmChange version='0.6'><modify><node id='3' lat='52.0004' lon='-2.9999'/></modify></osmChange>");
            SessionState.SetString(Key+"Root",root);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static Task FakeFetch(string url,string target,int maximum,CancellationToken token)=>Task.Run(()=>
        {
            if(url.EndsWith(".osc.gz")){while(!release.Wait(10))token.ThrowIfCancellationRequested();token.ThrowIfCancellationRequested();File.Copy(Path.Combine(root,"source.osc.gz"),target,true);}
            else{token.ThrowIfCancellationRequested();File.WriteAllText(target,"sequenceNumber=14\ntimestamp=2026-11-05T00\\:00\\:00Z\n");}
        },token);
        static void Check(bool value,string text){if(!value)throw new Exception("UPDATER FAIL: "+text);checks++;Debug.Log("UPDATER PASS: "+text);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Update service timed out phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates)return;session.Updates.Automatic=false;pack=new CountryPack(Path.Combine(root,"Country"));active=session.World.Route.Region;vertex=session.World.GeographicWorld.GetComponentInChildren<MeshFilter>().sharedMesh.vertices[0];
                    requester=new GameObject("Disposable update requester").AddComponent<UpdaterRequestProbe>();requester.Begin(session.Updates,pack);phase=1;return;
                }
                if(phase==1)
                {
                    if(++frames<40)return;Check(session.Updates.SourceRequests==0,"background source requests yield while foreground network work is busy");blocked=false;phase=2;return;
                }
                if(phase==2)
                {
                    if(session.Updates.SourceRequests<3)return;UnityEngine.Object.Destroy(requester.gameObject);blocked=true;phase=3;return;
                }
                if(phase==3)
                {
                    if(session.Updates.Busy)return;Check(new CountryPack(pack.Folder).Version=="13","interrupted download leaves installed country unchanged");Check(session.Updates.SourceRequests==3,"interrupted request does not repeatedly hit the provider");
                    blocked=false;release.Set();requester=new GameObject("Resumed update requester").AddComponent<UpdaterRequestProbe>();requester.Begin(session.Updates,new CountryPack(pack.Folder));phase=4;return;
                }
                if(phase==4)
                {
                    if(new CountryPack(pack.Folder).Version!="14"||session.Updates.Busy)return;
                    Check(session.Updates.SourceRequests==5,"resumed job reuses its daily state and downloads one validated diff");Check(ReferenceEquals(active,session.World.Route.Region),"country-master update never replaces the active driving region");Check(vertex==session.World.GeographicWorld.GetComponentInChildren<MeshFilter>().sharedMesh.vertices[0],"visible terrain vertices remain pinned during country update");
                    Check(UnityEngine.Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==1,"update completion does not reload the driving scene");Check(Directory.GetFiles(CountryUpdateService.OverrideFolder,"*.partial",SearchOption.AllDirectories).Length==0,"interrupted transfers leave no partial activation file");
                    File.WriteAllText("Builds/Verification/country-updater.txt","PASS "+checks+" runtime priority/cancellation/recovery/pinning checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);release.Set();CountryUpdateService.FetchOverride=null;CountryUpdateService.BusyOverride=null;CountryUpdateService.AllowVerification=false;EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
