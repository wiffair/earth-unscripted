using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EarthUnscripted.Core;
using EarthUnscripted.Radio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted.Updates
{
    public sealed class ApplicationUpdateSettings
    {
        public bool automaticUpdates;
        public long highestSequence;
        public string etag="";
    }
    public sealed class ApplicationUpdateService : MonoBehaviour
    {
        public ApplicationUpdateSettings Settings {get;private set;}=new ApplicationUpdateSettings();
        public JObject Catalogue {get;private set;}
        public string Status {get;private set;}="Checking for updates…";
        public string DownloadStatus=>downloadStatus;
        public bool Busy=>checking!=null||downloading!=null;
        public bool Downloading=>downloading!=null;
        public bool ReadyToInstall {get;private set;}
        public bool InstallAtClose {get;private set;}
        public bool PromptPending {get;private set;}
        public bool ExitWaiting {get;private set;}
        public string AvailableVersion=>Catalogue?["game"]?["version"]?.ToString()??"";
        public bool HasGameUpdate=>Catalogue!=null&&UpdateProtocol.CompareVersion(AvailableVersion,ProductIdentity.Version)>0;
        public IReadOnlyList<JObject> PackUpdates=>availablePacks;
        readonly List<JObject> availablePacks=new List<JObject>();
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        Task<Tuple<JObject,string>> checking;
        Task<DownloadResult> downloading;
        sealed class DownloadResult {public bool Game;public RadioCatalog Radio;public int Cars;}
        string root,settingsPath,envelopePath,stagedEnvelopePath,archivePath,targetPath;
        bool automaticallyScheduled;
        volatile string downloadStatus="";
        GameSession session;
        bool initialized;
        bool installNow,allowQuit,shutdown,initialCheck=true;
        double nextCheck;
        RadioCatalog pendingRadio;
        public string Target=>Application.platform==RuntimePlatform.OSXPlayer||Application.platform==RuntimePlatform.OSXEditor?"macOS-arm64":"Windows-x64";
        public void Initialize(GameSession owner)
        {
            session=owner;if(initialized)return;initialized=true;DontDestroyOnLoad(gameObject);root=Path.Combine(SinglePlayerBeta.DataRoot,"Updates");Directory.CreateDirectory(root);
            settingsPath=Path.Combine(root,"settings.json");envelopePath=Path.Combine(root,"catalogue.signed.json");
            try{if(File.Exists(settingsPath))Settings=JsonConvert.DeserializeObject<ApplicationUpdateSettings>(File.ReadAllText(settingsPath))??new ApplicationUpdateSettings();}catch(Exception e){Status="Update settings reset: "+e.Message;}
            targetPath=FindInstallationRoot();Application.wantsToQuit+=WantsToQuit;
            string result=Path.Combine(root,"install-result.json");
            if(File.Exists(result))try{var row=JObject.Parse(File.ReadAllText(result));if((bool?)row["ok"]==false)Status="Previous update: "+(string)row["message"];}catch(Exception){}
            if(SinglePlayerBeta.VerificationOutput==null)CheckNow();else Status="Automatic update checks disabled for isolated verification.";
        }
        string FindInstallationRoot()
        {
            if(Application.isEditor)return "";
            var directory=new DirectoryInfo(Application.dataPath);
            if(Target=="macOS-arm64")
            {while(directory!=null){if(File.Exists(Path.Combine(directory.FullName,"Contents/Resources/InstallManifest.json")))return directory.FullName;directory=directory.Parent;}}
            else
            {while(directory!=null){if(File.Exists(Path.Combine(directory.FullName,"InstallManifest.json")))return directory.FullName;directory=directory.Parent;}}
            return "";
        }
        public void SaveSettings(){UpdateProtocol.AtomicText(settingsPath,JsonConvert.SerializeObject(Settings));}
        public void SetAutomatic(bool enabled){Settings.automaticUpdates=enabled;if(!enabled&&automaticallyScheduled){InstallAtClose=false;automaticallyScheduled=false;}SaveSettings();if(enabled&&ReadyToInstall){automaticallyScheduled=true;QueueForClose();}else if(enabled&&Catalogue!=null&&!Busy)DownloadUpdates(false,true);}
        public void CheckNow()
        {
            if(Busy||shutdown||ReadyToInstall)return;Status="Checking signed game and pack releases…";
            long minimum=Settings.highestSequence;string etag=Settings.etag;
            checking=Task.Run(async()=>
            {
                var response=await UpdateTransport.Feed(File.Exists(envelopePath)?etag:"",lifetime.Token).ConfigureAwait(false);
                byte[] bytes=response.Item1??File.ReadAllBytes(envelopePath);
                var catalogue=UpdateProtocol.ReadSigned(bytes,minimum);
                if(response.Item1!=null)UpdateProtocol.AtomicText(envelopePath,System.Text.Encoding.UTF8.GetString(bytes));
                return Tuple.Create(catalogue,response.Item2??"");
            });
        }
        void Update()
        {
            if(!session)return;
            if(checking!=null&&checking.IsCompleted)
            {
                var task=checking;checking=null;nextCheck=Time.realtimeSinceStartupAsDouble+900;
                if(task.IsFaulted||task.IsCanceled) {
                    Status="Could not check updates. Your installed game is ready to use. Retry in Settings → Updates.";
                    try {File.WriteAllText(Path.Combine(root,"last-check-error.txt"),task.Exception?.GetBaseException().ToString()??"Update check cancelled.");}catch(IOException){}
                }
                else
                {
                    Catalogue=task.Result.Item1;Settings.etag=task.Result.Item2;Settings.highestSequence=(long)Catalogue["sequence"];SaveSettings();RefreshPackChoices();
                    Status=HasGameUpdate?"Game "+AvailableVersion+" is available.":availablePacks.Count>0?availablePacks.Count+" pack update(s) available.":"The game and managed packs are up to date.";
                    if(initialCheck&&(HasGameUpdate||availablePacks.Count>0))PromptPending=true;
                    if(Settings.automaticUpdates&&(HasGameUpdate||availablePacks.Count>0))DownloadUpdates(false,true);
                }
                initialCheck=false;
            }
            if(downloading!=null&&downloading.IsCompleted)
            {
                var task=downloading;downloading=null;
                if(task.IsFaulted||task.IsCanceled){Status="Update download not completed: "+(task.Exception?.GetBaseException().Message??"cancelled")+". The current game is unchanged.";installNow=false;ExitWaiting=false;}
                else
                {
                    ReadyToInstall=task.Result.Game;pendingRadio=task.Result.Radio;
                    Status=(ReadyToInstall?"Game update downloaded and verified. ":"")+(task.Result.Cars>0?"Car pack updates are ready after restart. ":"")+(pendingRadio!=null?"Radio update ready; the playing song will continue.":"");
                    RefreshPackChoices();if(installNow&&ReadyToInstall){session.Save();Application.Quit();}
                    else if(ExitWaiting){allowQuit=true;session.Save();Application.Quit();}
                }
            }
            if(pendingRadio!=null)
            {
                var radio=session.GetComponent<CarRadio>();
                if(radio&&radio.Ready&&!radio.Importing){radio.AcceptUpdatedCatalog(pendingRadio);pendingRadio=null;Status="Radio stations and playlists updated. Current playback was kept."+(ReadyToInstall?" Game update ready for close.":"");}
            }
            if(!Busy&&!ReadyToInstall&&!shutdown&&SinglePlayerBeta.VerificationOutput==null&&Time.realtimeSinceStartupAsDouble>=nextCheck&&nextCheck>0)CheckNow();
        }
        void RefreshPackChoices()
        {
            availablePacks.Clear();if(Catalogue==null)return;
            RadioCatalog radio;
            try{radio=new RadioLibrary(Path.Combine(SinglePlayerBeta.DataRoot,"RadioLibrary")).Load();}
            catch(Exception){radio=new RadioCatalog();}
            var includedRadio=new Dictionary<string,string>();
            string included=Path.Combine(Application.streamingAssetsPath,"RadioPacks");
            if(Directory.Exists(included))foreach(string file in Directory.GetFiles(included,"*.euradio"))try
            {
                using(var zip=ZipFile.OpenRead(file)){var entry=zip.GetEntry("manifest.json");if(entry==null||entry.Length>UpdateProtocol.MaximumMetadata)continue;using(var input=entry.Open())using(var reader=new StreamReader(input)){var data=JObject.Parse(reader.ReadToEnd());includedRadio[(string)data["pack_id"]]=(string)data["pack_version"];}}
            }catch(Exception){}
            foreach(var pack in Catalogue["packs"].OfType<JObject>())
            {
                if(UpdateProtocol.CompareVersion((string)pack["minimumGameVersion"],ProductIdentity.Version)>0)continue;
                string id=(string)pack["id"],version=null;
                if((string)pack["kind"]=="radio")
                {if(radio.packs.TryGetValue(id,out var installed))version=(string)installed["version"];else includedRadio.TryGetValue(id,out version);}
                else
                {var installed=ImportedVehicleCatalogue.Find(id);version=installed?.Settings.Version;string file=Path.Combine(VehiclePackLibrary.Folder,id+".eucar");if(File.Exists(file))try{version=VehiclePackFormat.Read(file).Settings.Version;}catch(Exception){} }
                if(version==null?(bool?)pack["autoAdd"]==true:UpdateProtocol.CompareVersion((string)pack["version"],version)>0)availablePacks.Add(pack);
            }
        }
        public void DismissPrompt(){PromptPending=false;}
        public void DownloadUpdates(bool now,bool automatic=false)
        {
            if(Busy||Catalogue==null)return;PromptPending=false;automaticallyScheduled=automatic;installNow=now;InstallAtClose=HasGameUpdate;downloadStatus="Preparing verified downloads…";
            var game=(JObject)Catalogue["game"];var packs=availablePacks.Select(p=>(JObject)p.DeepClone()).ToArray();
            bool fetchGame=HasGameUpdate;string target=Target,builtin=Path.Combine(Application.streamingAssetsPath,"RadioPacks"),vehicleFolder=VehiclePackLibrary.Folder;
            var radio=session.GetComponent<CarRadio>();var approved=radio!=null?new HashSet<string>(radio.Settings.approved):new HashSet<string>();
            string inbox=Path.Combine(SinglePlayerBeta.DataRoot,"RadioLibrary/Imports");
            string customInbox=radio?.Settings.importDirectory;
            var library=new RadioLibrary(Path.Combine(SinglePlayerBeta.DataRoot,"RadioLibrary"));
            stagedEnvelopePath=Path.Combine(root,"staged-envelope.signed.json");if(fetchGame)File.Copy(envelopePath,stagedEnvelopePath,true);
            downloading=Task.Run(async()=>
            {
                var result=new DownloadResult();Directory.CreateDirectory(inbox);
                foreach(var pack in packs)
                {
                    string id=(string)pack["id"],kind=(string)pack["kind"];var artifact=(JObject)pack["artifact"];
                    string archive=Path.Combine(root,(string)artifact["sha256"]+(kind=="radio"?".euradio":".eucar"));
                    if(kind=="radio"&&pack["deltaRecipe"] is JObject delta)
                    {
                        var sources=new List<string>();foreach(string folder in new[]{builtin,inbox,customInbox}.Where(f=>!string.IsNullOrEmpty(f)&&Directory.Exists(f)))sources.AddRange(Directory.GetFiles(folder,"*.euradio"));
                        var transfer=new RadioDelta();await transfer.Reconstruct(artifact,delta,archive,Path.Combine(root,"RadioChunks"),sources,message=>downloadStatus=message,lifetime.Token).ConfigureAwait(false);
                    }
                    else await UpdateTransport.Download(artifact,archive,(done,total)=>downloadStatus=kind+" pack: "+(done/1024/1024)+" / "+(total/1024/1024)+" MB",lifetime.Token).ConfigureAwait(false);
                    ValidatePackIdentity(archive,pack);
                    if(kind=="radio")
                    {
                        var approvals=new HashSet<string>(approved);foreach(var hash in pack["approvedAudioSha256"] as JArray??new JArray())approvals.Add((string)hash);
                        // Existing importer validates every content hash and atomically commits its pointer.
                        result.Radio=library.Import(archive,approvals);
                        string installed=Path.Combine(inbox,id+".euradio"),temporary=installed+".partial";File.Copy(archive,temporary,true);
                        if(File.Exists(installed))File.Replace(temporary,installed,installed+".previous");else File.Move(temporary,installed);
                    }
                    else {var installed=VehiclePackLibrary.Import(archive,vehicleFolder);UpdateProtocol.Require(installed.Settings.Id==id&&installed.Settings.Version==(string)pack["version"],"Downloaded car identity does not match its update.");result.Cars++;}
                }
                if(fetchGame)
                {
                    UpdateProtocol.Require(!string.IsNullOrEmpty(targetPath),"This copy is not a complete installed package. Download the complete game from the website first.");
                    var artifact=((JArray)game["downloads"]).OfType<JObject>().Single(a=>(string)a["target"]==target);
                    long required=(long)artifact["bytes"]+(long)artifact["unpackedBytes"]+256L*1024*1024;
                    var drive=new DriveInfo(Path.GetPathRoot(targetPath));UpdateProtocol.Require(drive.AvailableFreeSpace>=required,"There is not enough free space to stage this update and keep the installed game.");
                    archivePath=Path.Combine(root,(string)artifact["sha256"]+".zip");
                    await UpdateTransport.Download(artifact,archivePath,(done,total)=>downloadStatus="Game: "+(done/1024/1024)+" / "+(total/1024/1024)+" MB",lifetime.Token).ConfigureAwait(false);
                    UpdateProtocol.CheckArchive(archivePath,(long)artifact["unpackedBytes"],target=="macOS-arm64");result.Game=true;
                }
                downloadStatus="Downloads verified.";return result;
            });
        }
        static void ValidatePackIdentity(string archive,JObject pack)
        {
            using(var zip=ZipFile.OpenRead(archive))
            {
                var entry=zip.GetEntry("manifest.json");UpdateProtocol.Require(entry!=null&&entry.Length<=UpdateProtocol.MaximumMetadata,"Pack manifest is missing/too large.");
                using(var input=entry.Open())using(var output=new MemoryStream())
                {var buffer=new byte[8192];int count;while((count=input.Read(buffer,0,buffer.Length))>0){UpdateProtocol.Require(output.Length+count<=UpdateProtocol.MaximumMetadata,"Pack manifest expanded beyond its limit.");output.Write(buffer,0,count);}var data=UpdateProtocol.Json(output.ToArray());bool radio=(string)pack["kind"]=="radio";UpdateProtocol.Require((string)data[radio?"pack_id":"id"]==(string)pack["id"]&&(string)data[radio?"pack_version":"version"]==(string)pack["version"],"Pack identity does not match the signed release.");}
            }
        }
        public void QueueForClose(){if(ReadyToInstall){InstallAtClose=true;Status="The verified game update will install when you close the game.";}}
        public void InstallNow(){if(!ReadyToInstall)return;InstallAtClose=true;installNow=true;session.Save();Application.Quit();}
        public void QuitWithoutUpdate(){InstallAtClose=false;ExitWaiting=false;allowQuit=true;session.Save();Application.Quit();}
        bool WantsToQuit()
        {
            if(allowQuit&&!ReadyToInstall)return true;
            if(InstallAtClose&&Downloading){ExitWaiting=true;if(session)session.SetMenu(true);PromptPending=true;Status="The update is still downloading. Wait here, or quit without installing.";return false;}
            if(ReadyToInstall&&InstallAtClose)
            {
                try{StartInstaller();return true;}
                catch(Exception error){Status="Could not start installation: "+error.Message;if(session)session.SetMenu(true);PromptPending=true;InstallAtClose=false;return false;}
            }
            return true;
        }
        void StartInstaller()
        {
            UpdateProtocol.ReadSigned(File.ReadAllBytes(stagedEnvelopePath),Settings.highestSequence);
            bool mac=Target=="macOS-arm64";string name="EarthUnscriptedUpdateInstaller"+(mac?"":".exe");
            string bundled=Path.Combine(targetPath,mac?"Contents/Helpers/"+name:name);
            UpdateProtocol.Require(File.Exists(bundled),"Update installer is missing. Use the website download.");
            string helpers=Path.Combine(root,"Installer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(helpers);string helper=Path.Combine(helpers,name);File.Copy(bundled,helper);
            if(mac){using(var mode=Process.Start(new ProcessStartInfo("/bin/chmod","700 "+Quote(helper)){UseShellExecute=false,CreateNoWindow=true}))mode.WaitForExit();}
            using(var process=Process.GetCurrentProcess())
            {
                var job=new JObject{{"targetPath",targetPath},{"archivePath",archivePath},{"envelopePath",stagedEnvelopePath},{"minimumSequence",Settings.highestSequence},{"parentPid",process.Id},{"parentStartTicks",process.StartTime.ToUniversalTime().Ticks},{"reopen",true}};
                string path=Path.Combine(root,"install-job.json");UpdateProtocol.AtomicText(path,job.ToString());
                Process.Start(new ProcessStartInfo(helper,"--apply "+Quote(path)){UseShellExecute=false,CreateNoWindow=true});
            }
        }
        static string Quote(string value)
        {
            var output=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){output.Append('\\',slashes*2+1);output.Append(c);}else{output.Append('\\',slashes);output.Append(c);}slashes=0;}
            output.Append('\\',slashes*2);return output.Append('"').ToString();
        }
        void OnDestroy(){shutdown=true;Application.wantsToQuit-=WantsToQuit;lifetime.Cancel();}
    }
}
