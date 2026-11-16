using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
namespace RoadRush
{
    // Explicit isolated native UI workflow; no downloads, source installation or deletion.
    public sealed class SceneryPackNativeProof : MonoBehaviour
    {
        const string Flag="-roadRushSceneryProof";
        static string prefix,realSave,realHash,sourceRoot,sourceWorld;
        static string countryDatabase,countryDatabaseStamp,sourceStamp;
        static int earlyErrorCount;static readonly List<string> earlyErrors=new List<string>();
        static void EarlyLog(string message,string stack,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;earlyErrorCount++;if(earlyErrors.Count<16)earlyErrors.Add((message??"").Substring(0,Math.Min(message?.Length??0,1600)));}
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        int runtimeErrors;bool completed;double deadline;
        [Serializable] sealed class Report
        {
            public string status,error,version,limitations,profile,preparedId,preparedDigest,countryVersion,persistedCacheEpoch,runtimeEpoch,expectedFinalEpoch,preparedCacheHash;
            public int preparedRoadCount,runtimeRoadCount,runtimeTreeCount,expectedFinalRoadCount,expectedFinalTreeCount;
            public bool saveFailureRollback,prepareSaveFailureBlocked,preparedCandidateInvalidation;
            public bool runInBackground,realCareerUnchanged,countryDatabaseMetadataUnchanged,sourceFileMetadataUnchanged,currentWorldPinnedUntilExplicitDrive,careerPreserved;
            public int preparationFrames;
            public int width,height,frames,inventoryRows,buildings,importantBuildings,trees,physicalWalls,accessFlights,windows,doors,criticalSurfaceSamples,criticalSurfaceFailures,weatherRequests,updateRequests,runtimeErrors;
            public long countryDatabaseBytes,countryDatabaseWriteTicks;
            public string countryMetadataHash,countryReplicationSequence;public double preparationSeconds;public string[] checks,firstErrors;
        }
        Report report=new Report{status="FAIL"};
        static string Arg(string[] args,string key){int at=Array.IndexOf(args,key);return at>=0&&at+1<args.Length?args[at+1]:throw new ArgumentException("Missing proof argument "+key);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Isolate()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,Flag)<0)return;
            Application.logMessageReceived+=EarlyLog;Application.runInBackground=true;prefix=Path.GetFullPath(Arg(args,Flag));sourceWorld=Path.GetFullPath(Arg(args,"-roadRushSceneryWorld"));sourceRoot=Path.GetFullPath(Arg(args,"-roadRushSceneryPackRoot")).TrimEnd(Path.DirectorySeparatorChar);
            Directory.CreateDirectory(Path.GetDirectoryName(prefix));
            // Fail before replacing any proof outputs; reuse would invalidate a cold-preparation claim.
            if(File.Exists(prefix+"-profile.json")||Directory.Exists(prefix+"-profile.json.regions"))throw new InvalidOperationException("Use a fresh scenery-proof prefix");
            realSave=Path.Combine(Application.persistentDataPath,"road-rush-save.json");realHash=File.Exists(realSave)?Core.DataPackageRules.HashFile(realSave):null;
            PlayerProfile.StorageOverride=prefix+"-profile.json";Directory.CreateDirectory(RegionCache.Folder);
            File.WriteAllText(Path.Combine(RegionCache.Folder,"world-packs.txt"),sourceRoot+Environment.NewLine);
            var initial=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(sourceWorld));initial.id="download-scenery-proof-start";initial.networkMapId="";
            File.WriteAllText(RegionCache.PathFor(initial.id),JsonUtility.ToJson(initial));
            var garage=new Core.GarageState{activeId="scenery-owned",cars=new List<Core.OwnedCar>{new Core.OwnedCar{id="scenery-owned",model=1,engine=2,tyres=1,paint=4,wheels=2,damage=17},new Core.OwnedCar{id="scenery-spare",model=2,paint=3,engine=1}}};
            var profile=new PlayerProfile{region=initial.id,garage=garage,credits=4321,detailedScenery=true,livingScenery=false,quality=1,clarity=1,volume=.23f,rumble=.31f,wheelName="Scenery proof wheel",steerPath="/Gamepad/leftStick/x",steerLeft=-.81f,steerCentre=.04f,steerRight=.84f,weatherMode=0,clockMode=0,fixedHour=14};
            if(!profile.Save())throw new IOException("Could not initialize isolated scenery career");
            sourceStamp=SourceStamp();
        }
        static string SourceStamp()
        {
            // Bounded metadata-only stamp, not a claim of complete source byte identity.
            // Hashing a national SQLite database would needlessly read many GiB twice.
            var files=Directory.EnumerateFiles(sourceRoot,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith("-shm",StringComparison.Ordinal)&&!p.EndsWith("-wal",StringComparison.Ordinal)).Take(16385).ToArray();if(files.Length>16384)throw new InvalidOperationException("Source metadata stamp exceeds16384file bound");
            var lines=files.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>{var info=new FileInfo(p);return p+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;});
            return Core.DataPackageRules.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",lines)));
        }
        static string CountryStamp()
        {
            var info=new FileInfo(countryDatabase);var metadata=Path.Combine(info.DirectoryName,"pack.json");if(new FileInfo(metadata).Length>256000)throw new InvalidOperationException("Country metadata exceeds proof fingerprint bound");
            string sequence;using(var db=new NativeMapDatabase(countryDatabase))sequence=MapReplication.Sequence(db);
            return info.Length+"|"+info.LastWriteTimeUtc.Ticks+"|"+Core.DataPackageRules.HashFile(metadata)+"|"+sequence;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch(){if(!string.IsNullOrEmpty(prefix)){var go=new GameObject("Isolated scenery menu workflow proof");DontDestroyOnLoad(go);go.AddComponent<SceneryPackNativeProof>();}}
        void OnEnable(){Application.logMessageReceived-=EarlyLog;runtimeErrors=earlyErrorCount;errors.AddRange(earlyErrors);Application.runInBackground=true;deadline=Time.realtimeSinceStartupAsDouble+300;Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;runtimeErrors++;if(errors.Count<16)errors.Add((message??"").Substring(0,Math.Min(message?.Length??0,1600)));}
        void Update(){if(!completed&&Time.realtimeSinceStartupAsDouble>deadline)Finish("Real-time scenery workflow watchdog expired after300seconds");}
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
        void Finish(string failure=null)
        {
            if(completed)return;completed=true;report.error=failure;report.status=failure==null&&runtimeErrors==0?"PASS":"FAIL";report.runtimeErrors=runtimeErrors;report.firstErrors=errors.ToArray();report.checks=checks.ToArray();report.frames=Time.frameCount;report.width=Screen.width;report.height=Screen.height;report.runInBackground=Application.runInBackground;
            report.realCareerUnchanged=(File.Exists(realSave)?Core.DataPackageRules.HashFile(realSave):null)==realHash;
            report.countryDatabaseMetadataUnchanged=!string.IsNullOrEmpty(countryDatabase)&&CountryStamp()==countryDatabaseStamp;
            report.sourceFileMetadataUnchanged=SourceStamp()==sourceStamp;
            if(!report.realCareerUnchanged||!report.countryDatabaseMetadataUnchanged||!report.sourceFileMetadataUnchanged)report.status="FAIL";
            report.limitations="Native UI/installed-pack preparation workflow only. Screenshots include the actual IMGUI page and must be visually reviewed for readability. Request counters cover weather and country-update services; the preparation invokes the installed offline-only action and asserts its exact country/revision-derived cache identity. The saved source-preparation epoch/cache bytes are checked separately from the exact post-finalization live-world epoch; the expected clone runs the same production garage/pit/canopy finalization with no rendering, and no generated roads or other differences are ignored. No network hosting, source installation/deletion, Windows or performance acceptance claim. Country no-write evidence compares bounded pack metadata hashes and database/source-file length+mtime stamps plus query-only replication sequence; SQLite WAL/SHM auxiliary files are excluded from the metadata stamp because ordinary query-only readers can maintain those files; it does not hash or claim complete byte identity of a national database. The short idle drive validates road collision/coverage without adding driving damage to the isolated career.";
            try{File.WriteAllText(prefix+".json",JsonUtility.ToJson(report,true));}finally{Application.Quit(report.status=="PASS"?0:1);}
        }
        IEnumerator Start()
        {
            var run=Run();while(!completed){object current=null;bool moved=false;Exception failure=null;try{moved=run.MoveNext();if(moved)current=run.Current;}catch(Exception e){failure=e;}if(failure!=null){Finish(failure.ToString());yield break;}if(!moved)yield break;yield return current;}
        }
        void Career(PlayerProfile p)
        {
            Check(p.credits==4321&&p.garage.cars.Count==2&&p.garage.activeId=="scenery-owned","two-car4321-credit career preserved");var car=p.garage.Active;var spare=p.garage.cars.Single(c=>c.id=="scenery-spare");
            Check(car.model==1&&car.engine==2&&car.tyres==1&&car.paint==4&&car.wheels==2&&Math.Abs(car.damage-17)<.001f&&spare.model==2&&spare.engine==1&&spare.paint==3,"owned cars upgrades styling and pre-existing damage preserved");
            Check(p.quality==1&&p.clarity==1&&Math.Abs(p.volume-.23f)<.001f&&Math.Abs(p.rumble-.31f)<.001f&&p.wheelName=="Scenery proof wheel"&&p.steerPath=="/Gamepad/leftStick/x"&&Math.Abs(p.steerLeft+.81f)<.001f&&Math.Abs(p.steerCentre-.04f)<.001f&&Math.Abs(p.steerRight-.84f)<.001f,"graphics sound and controller calibration preserved");
        }
        void Inspect(SceneryPackMenu page,bool requested)
        {
            var snapshot=page.Snapshot;Check(snapshot!=null&&snapshot.SelectionComplete&&!snapshot.Cancelled,"actual menu worker completes without partial inventory selection");report.inventoryRows=snapshot.Rows.Count;
            var selected=snapshot.Rows.Where(r=>r.State==DetailInventoryState.SelectedRevision&&r.Coverage==DetailInventoryCoverage.InsideRequestedArea).ToArray();
            Check(selected.Any(r=>r.Kind=="Buildings"&&r.Revision==2)&&selected.Any(r=>r.Kind=="Canopy"&&r.Revision==1),"real reviewed local buildingV2 and canopyV1 cards cover Pantside");
            Check(selected.All(r=>r.DetailRequested==requested&&r.SelectedForNextPreparation==requested&&r.KnownRuntimeBytes>0&&!string.IsNullOrEmpty(r.Licence)&&!string.IsNullOrEmpty(r.Attribution)),"actual selected cards expose sizes licence attribution and correct next-preparation state");
        }
        IEnumerator Run()
        {
            GameSession session=null;double until=Time.realtimeSinceStartupAsDouble+45;
            while(!(session=FindAnyObjectByType<GameSession>())||!session.BootReady||!session.Car||!session.World.GeographicWorld){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Initial session timeout");yield return null;}
            report.version=Application.version;report.profile=PlayerProfile.StoragePath;
            Check(Path.GetFullPath(PlayerProfile.StoragePath)==prefix+"-profile.json"&&RegionCache.Folder==prefix+"-profile.json.regions","all career and region writes isolated before GameSessionAwake");
            Check(!session.Updates.Enabled,"background country updates disabled for isolated workflow");session.Input.enabled=false;session.SetMenu(true);Career(session.Profile);
            var pack=CountryPacks.Installed().First(p=>p.Country=="GB"&&p.Ready&&p.Covers(StartAreaPreflight.Place(session.World.Route.Region)));countryDatabase=Path.Combine(pack.Folder,"map.sqlite");countryDatabaseStamp=CountryStamp();var dbInfo=new FileInfo(countryDatabase);report.countryDatabaseBytes=dbInfo.Length;report.countryDatabaseWriteTicks=dbInfo.LastWriteTimeUtc.Ticks;report.countryMetadataHash=Core.DataPackageRules.HashFile(Path.Combine(pack.Folder,"pack.json"));using(var db=new NativeMapDatabase(countryDatabase))report.countryReplicationSequence=MapReplication.Sequence(db);report.countryVersion=pack.Name+"/"+pack.Version;
            Check(Path.GetFullPath(pack.Folder).StartsWith(sourceRoot+Path.DirectorySeparatorChar,StringComparison.Ordinal),"preparation source is the explicitly supplied installed UK pack");
            var original=session.World.Route.Region;string pinned=WorldSnapshotCodec.CanonicalJson(original);var ui=FindAnyObjectByType<DrivingUI>();Check(ui&&ui.enabled,"actual DrivingUI is enabled");ui.ShowSceneryPacks();var page=ui.SceneryPacks;
            until=Time.realtimeSinceStartupAsDouble+30;while(page.Scanning||page.Snapshot==null){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Initial inventory worker timeout");yield return null;}
            Inspect(page,true);File.WriteAllText(prefix+"-inventory-on.json",JsonConvert.SerializeObject(page.Snapshot,Formatting.Indented));yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-menu-on.png");yield return null;
            string isolatedPath=PlayerProfile.StorageOverride,profileBefore=Core.DataPackageRules.HashFile(isolatedPath),blockedParent=prefix+"-blocked-parent";File.WriteAllText(blockedParent,"Intentionally blocked isolated test save parent");var snapshotBefore=page.Snapshot;bool oldChoice=session.Profile.detailedScenery;
            try
            {
                PlayerProfile.StorageOverride=Path.Combine(blockedParent,"profile.json");
                Check(!page.SetDetailRequested(session,!oldChoice)&&session.Profile.detailedScenery==oldChoice&&ReferenceEquals(page.Snapshot,snapshotBefore),"real save failure rolls detail choice back without refreshing inventory");
                Check(!page.PrepareCurrent(session)&&!session.Downloads.Busy&&page.Prepared==null,"real save failure refuses preparation before any worker starts");report.prepareSaveFailureBlocked=true;
            }
            finally{PlayerProfile.StorageOverride=isolatedPath;}
            Check(Core.DataPackageRules.HashFile(isolatedPath)==profileBefore&&ReferenceEquals(session.World.Route.Region,original)&&WorldSnapshotCodec.CanonicalJson(original)==pinned,"save-failure injection leaves owned profile bytes and visible world unchanged");report.saveFailureRollback=true;
            Check(page.SetDetailRequested(session,false),"actual menu accepts detail-off choice");until=Time.realtimeSinceStartupAsDouble+30;while(page.Scanning||page.Snapshot==null){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Detail-off worker timeout");yield return null;}Inspect(page,false);Career(PlayerProfile.Load());Check(ReferenceEquals(session.World.Route.Region,original)&&WorldSnapshotCodec.CanonicalJson(original)==pinned,"detail-off choice leaves the visible prepared world unchanged");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-menu-off.png");yield return null;
            Check(page.SetDetailRequested(session,true),"actual menu accepts detail-on choice");until=Time.realtimeSinceStartupAsDouble+30;while(page.Scanning||page.Snapshot==null){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Detail-on worker timeout");yield return null;}Inspect(page,true);Career(PlayerProfile.Load());
            var place=StartAreaPreflight.Place(original);string expected=DetailCanopyPacks.CacheId(DetailBuildingPacks.CacheId(RegionCache.PackId(place,pack),DetailBuildingPacks.Find(place)),DetailCanopyPacks.Find(place));Check(!RegionCache.Exists(expected),"new area preparation has no pre-existing isolated cache hit");
            double began=Time.realtimeSinceStartupAsDouble;int preparationFrame=Time.frameCount;Check(page.PrepareCurrent(session),"actual menu starts installed-country offline preparation");until=began+120;
            while(page.Prepared==null){if(!ReferenceEquals(session.World.Route.Region,original)||!session.Menu)throw new InvalidOperationException("Visible world changed during preparation");if(!page.Preparing&&!session.Downloads.Busy){page.Update(session);if(page.Prepared!=null)break;throw new InvalidOperationException("Preparation failed: "+page.Status);}if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Offline area preparation timeout");yield return null;}
            report.preparationSeconds=Time.realtimeSinceStartupAsDouble-began;report.preparationFrames=Time.frameCount-preparationFrame;Check(report.preparationFrames>1,"offline preparation yields rendered updates while the menu stays usable");var prepared=page.Prepared;
            Check(prepared.id==expected&&prepared.datasetVersion==pack.Name+"/"+pack.Version,"prepared area has the exact installed country/revision and selected-detail cache identity");
            Check(ReferenceEquals(session.World.Route.Region,original)&&WorldSnapshotCodec.CanonicalJson(original)==pinned&&session.Menu,"completed preparation does not apply until explicit DrivePrepared");report.currentWorldPinnedUntilExplicitDrive=true;
            report.preparedId=prepared.id;report.preparedDigest=WorldSnapshotCodec.Epoch(prepared);report.preparedRoadCount=prepared.roads.Length;report.preparedCacheHash=Core.DataPackageRules.HashFile(RegionCache.PathFor(prepared.id));File.WriteAllText(prefix+"-prepared-canonical.json",WorldSnapshotCodec.CanonicalJson(prepared));report.buildings=prepared.buildings.Length;report.importantBuildings=prepared.buildings.Count(b=>b.kind=="important_building");report.trees=prepared.trees.Length;
            Check(report.buildings==945&&report.importantBuildings==7&&report.trees==2338,"prepared Pantside contains945canonical buildings7important sources2338accepted trees");Career(PlayerProfile.Load());yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-menu-prepared.png");yield return null;
            // A prepared snapshot belongs to the saved detail choice. Failed saves retain
            // it; a successful opposite choice invalidates only this candidate, never the drive.
            string savedCandidateProfile=Core.DataPackageRules.HashFile(isolatedPath);
            try{PlayerProfile.StorageOverride=Path.Combine(blockedParent,"profile.json");Check(!page.SetDetailRequested(session,false)&&ReferenceEquals(page.Prepared,prepared)&&session.Profile.detailedScenery,"failed detail save retains the completed prepared candidate");}
            finally{PlayerProfile.StorageOverride=isolatedPath;}
            Check(Core.DataPackageRules.HashFile(isolatedPath)==savedCandidateProfile,"failed candidate-choice save retains original isolated profile bytes");
            Check(page.SetDetailRequested(session,false)&&page.Prepared==null&&ReferenceEquals(session.World.Route.Region,original),"successfully saved opposite detail choice invalidates candidate while retaining the visible world");
            until=Time.realtimeSinceStartupAsDouble+30;while(page.Scanning||page.Snapshot==null){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Candidate invalidation inventory timeout");yield return null;}Inspect(page,false);
            Check(page.SetDetailRequested(session,true),"restore requested detail choice before explicit re-preparation");until=Time.realtimeSinceStartupAsDouble+30;while(page.Scanning||page.Snapshot==null){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Restored detail inventory timeout");yield return null;}Inspect(page,true);
            Check(page.PrepareCurrent(session),"explicit re-preparation accepts the existing matching isolated cache");until=Time.realtimeSinceStartupAsDouble+60;
            while(page.Prepared==null){if(!page.Preparing&&!session.Downloads.Busy){page.Update(session);if(page.Prepared!=null)break;throw new InvalidOperationException("Re-preparation failed: "+page.Status);}if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Re-preparation timeout");yield return null;}
            prepared=page.Prepared;Check(prepared.id==expected&&WorldSnapshotCodec.Epoch(prepared)==report.preparedDigest&&WorldSnapshotCodec.CanonicalJson(original)==pinned,"re-preparation recovers the exact complete candidate without applying it");report.preparedCandidateInvalidation=true;
            Check(Core.DataPackageRules.HashFile(RegionCache.PathFor(prepared.id))==report.preparedCacheHash,"re-preparation retains exact persisted cache bytes");
            var expectedFinal=GeoRegion.Load(prepared.id);var helperObject=new GameObject("Owned geometry-free expected world preparation");helperObject.SetActive(false);
            try{var helper=helperObject.AddComponent<WorldBuilder>();helper.PrepareRegionGeometry(expectedFinal);Check(helperObject.GetComponentsInChildren<MeshFilter>(true).Length==0&&helperObject.GetComponentsInChildren<Light>(true).Length==0&&helperObject.GetComponentsInChildren<Renderer>(true).Length==0,"production expected-world finalization creates no scene geometry lights or renderers");}
            finally{Destroy(helperObject);}
            string expectedFinalJson=WorldSnapshotCodec.CanonicalJson(expectedFinal);report.expectedFinalEpoch=WorldSnapshotCodec.Epoch(expectedFinal);report.expectedFinalRoadCount=expectedFinal.roads.Length;report.expectedFinalTreeCount=expectedFinal.trees.Length;File.WriteAllText(prefix+"-expected-final-canonical.json",expectedFinalJson);
            Check(page.DrivePrepared(session),"actual explicit DrivePrepared accepts the complete snapshot");var previous=session;until=Time.realtimeSinceStartupAsDouble+60;
            while(!(session=FindAnyObjectByType<GameSession>())||!session.BootReady||ReferenceEquals(session,previous)||LocationLoading.Busy){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Prepared area scene reload timeout");yield return null;}
            var persisted=GeoRegion.Load(prepared.id);report.persistedCacheEpoch=WorldSnapshotCodec.Epoch(persisted);report.runtimeEpoch=WorldSnapshotCodec.Epoch(session.World.Route.Region);report.runtimeRoadCount=session.World.Route.Region.roads.Length;report.runtimeTreeCount=session.World.Route.Region.trees.Length;
            File.WriteAllText(prefix+"-persisted-canonical.json",WorldSnapshotCodec.CanonicalJson(persisted));File.WriteAllText(prefix+"-runtime-canonical.json",WorldSnapshotCodec.CanonicalJson(session.World.Route.Region));
            Check(WorldSnapshotCodec.CanonicalJson(persisted)==WorldSnapshotCodec.CanonicalJson(prepared)&&report.persistedCacheEpoch==report.preparedDigest&&Core.DataPackageRules.HashFile(RegionCache.PathFor(prepared.id))==report.preparedCacheHash,"raw cache reload preserves the exact selected prepared snapshot and cache bytes");
            Check(session.World.Route.Region.id==prepared.id&&WorldSnapshotCodec.CanonicalJson(session.World.Route.Region)==expectedFinalJson&&report.runtimeEpoch==report.expectedFinalEpoch,"scene reload exactly matches the selected snapshot after the same production world finalization");Career(session.Profile);Check(session.Profile.detailedScenery,"detail-on choice survives explicit area reload");
            session.Input.enabled=false;foreach(var car in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))car.gameObject.SetActive(false);session.ResumeDrive();until=Time.realtimeSinceStartupAsDouble+95;while(session.PreparingStart){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Physical frontier readiness timeout");yield return null;}
            Check(!session.Menu&&session.World.GeographicWorld.DriveFrontierReady,"actual resume completes all physical frontier preparation before driving");
            var world=session.World.GeographicWorld;var position=session.Car.Body.position;int cx=Mathf.FloorToInt(position.x/320),cz=Mathf.FloorToInt(position.z/320);
            for(int z=cz-3;z<=cz+3;z++)for(int x=cx-3;x<=cx+3;x++){report.criticalSurfaceSamples++;if(!world.HasSurface(new Vector3(x*320+160,0,z*320+160)))report.criticalSurfaceFailures++;}
            Check(report.criticalSurfaceFailures==0,"all49required road/terrain chunk surfaces exist after actual resume");Check(Physics.RaycastAll(position+Vector3.up*3,Vector3.down,8).Any(hit=>hit.collider is MeshCollider&&hit.collider.transform.IsChildOf(world.transform)&&hit.normal.y>.7f&&(hit.collider.name=="Mapped road"||hit.collider.name=="Junction apron"||hit.collider.name=="Bridge deck")),"actual mapped road mesh collider supports the starting car, excluding its own body collider");
            report.physicalWalls=world.GetComponentsInChildren<MeshCollider>().Count(c=>c.name.StartsWith("Building ",StringComparison.Ordinal));var chunks=world.GetComponentsInChildren<BuildingFacadeChunk>();report.windows=chunks.Sum(c=>c.Features.Count(f=>!f.IsDoor));report.doors=chunks.Sum(c=>c.Features.Count(f=>f.IsDoor));report.accessFlights=world.GetComponentsInChildren<BuildingAccessChunk>().Sum(c=>c.FlightCount);Check(report.physicalWalls>0&&report.windows>0&&report.doors>0&&report.accessFlights>0,"prepared drive reconstructs physical buildings windows doors and supported access flights");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-drive.png");yield return null;session.SetMenu(true);session.Save();Career(PlayerProfile.Load());report.careerPreserved=true;
            report.weatherRequests=session.Conditions.WeatherRequests;report.updateRequests=session.Updates.SourceRequests;Check(report.weatherRequests==0&&report.updateRequests==0,"weather and country-update provider request counters remain zero");Check(runtimeErrors==0,"native workflow emitted no runtime shader or assertion errors");yield return null;Finish();
        }
    }
}
