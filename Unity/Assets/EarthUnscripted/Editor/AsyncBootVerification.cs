using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    // OUTSIDE ASSETS candidate: actual play-mode boot, no synchronous test bypass.
    [InitializeOnLoad] public static class AsyncBootVerification
    {
        const string Key="EUAsyncBoot";const string SchemaId="download-async-boot-width";
        static string root,realPath,realHash,rawEpoch,schemaEpoch,cacheHash,cancelHash;
        static GameSession session,previous;static int index,phase,checks,errors,earlyFrames,lastFrame=-1,cancelFrame;
        static bool sawEarly;static Mesh cancelledMesh;static double deadline,lastHeartbeat,maxHeartbeat;
        static float customX,customZ;static int timingFrame=-1;static double timingPrevious;
        static AsyncBootVerification()
        {
            if(!SessionState.GetBool(Key,false))return;
            root=SessionState.GetString(Key+"Root","");realPath=SessionState.GetString(Key+"Real","");realHash=SessionState.GetString(Key+"Hash","");rawEpoch=SessionState.GetString(Key+"Raw","");schemaEpoch=SessionState.GetString(Key+"Schema","");cacheHash=SessionState.GetString(Key+"Cache","");customX=SessionState.GetFloat(Key+"X",0);customZ=SessionState.GetFloat(Key+"Z",0);
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");BootTelemetry.EnableIsolated();timingPrevious=BootTelemetry.Now;deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Tick;Application.logMessageReceived+=Log;
        }
        static string Hash(string path)=>File.Exists(path)?DataPackageRules.HashFile(path):"missing";
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");
            root=Path.GetFullPath("Builds/Verification/async-boot-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);realPath=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=Hash(realPath);
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
            var source=GeoRegion.Load("dee-valley");var data=WorldDataFinalizer.PrepareOwned(source,null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);rawEpoch=WorldSnapshotCodec.Epoch(data.Region);
            Vector3 normal=data.Route.At(35);var p=source.roads.Where(r=>!r.bridge&&!r.tunnel&&r.points.Length>1).Select(r=>source.RoadPoint(r,r.length*.5f,out _)).Where(q=>q.x>source.minX+128&&q.x<source.minX+source.width-128&&q.z>source.minZ+128&&q.z<source.minZ+source.depth-128).OrderByDescending(q=>(q-normal).sqrMagnitude).First();if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(normal.x,normal.z))<640)throw new Exception("Fixture lacks a genuinely distant custom road");customX=p.x;customZ=p.z;
            var explicitWorld=GeoRegion.Load("dee-valley");WorldDataFinalizer.PrepareOwned(explicitWorld,null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
            explicitWorld.id=SchemaId;explicitWorld.networkMapId="dee-valley";explicitWorld.roadWidthSchema=1;
            float Length(GeoRoad road){double total=0;for(int i=1;i<road.points.Length;i++){double x=(double)road.points[i].x-road.points[i-1].x,y=(double)road.points[i].y-road.points[i-1].y,z=(double)road.points[i].z-road.points[i-1].z;total+=Math.Sqrt(x*x+y*y+z*z);}return(float)total;}
            explicitWorld.roadWidthProfiles=explicitWorld.roads.Where(r=>!r.tunnel).Select(r=>new RoadWidthProfileDto{roadId=r.id,distances=new[]{0f,Length(r)},widths=new[]{r.width,r.width},derivatives=new[]{0f,0f}}).ToArray();
            var finalized=WorldDataFinalizer.PrepareOwned(explicitWorld,null,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);schemaEpoch=WorldSnapshotCodec.Epoch(finalized.Region);RegionCache.Save(finalized.Region,"Owned asynchronous boot verification fixture; constant-width descriptors, not an admission test.");cacheHash=Hash(RegionCache.PathFor(SchemaId));
            var profile=new PlayerProfile{credits=4321,quality=0,volume=.23f,rumble=0,livingScenery=false,detailedScenery=false,weatherMode=0,clockMode=0,fixedHour=14,wheelStrength=.31f};
            profile.garage.Active.engine=2;profile.garage.Active.damage=11;profile.garage.cars.Add(new OwnedCar{id="async-spare",model=2,paint=3,engine=1});File.WriteAllText(PlayerProfile.StoragePath,JsonUtility.ToJson(profile));
            SessionState.SetString(Key+"Root",root);SessionState.SetString(Key+"Real",realPath);SessionState.SetString(Key+"Hash",realHash);SessionState.SetString(Key+"Raw",rawEpoch);SessionState.SetString(Key+"Schema",schemaEpoch);SessionState.SetString(Key+"Cache",cacheHash);SessionState.SetFloat(Key+"X",customX);SessionState.SetFloat(Key+"Z",customZ);SessionState.SetBool(Key,true);GameSession.AutoStartCircuit=false;EditorApplication.EnterPlaymode();
        }
        static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors++;}
        static void Check(bool value,string why){if(!value)throw new Exception("ASYNC BOOT FAIL: "+why);checks++;Debug.Log("ASYNC BOOT PASS: "+why);}
        static void ResetObservation(){sawEarly=false;earlyFrames=0;lastFrame=-1;lastHeartbeat=0;}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Async boot timeout index "+index+" phase "+phase);
                if(!EditorApplication.isPlaying)return;
                if(timingFrame!=Time.frameCount){if(timingFrame>=0)BootTelemetry.Heartbeat(timingPrevious,timingFrame);else BootTelemetry.FirstFrame(timingPrevious);timingPrevious=BootTelemetry.Now;timingFrame=Time.frameCount;}
                var found=UnityEngine.Object.FindAnyObjectByType<GameSession>();
                if(phase==10)
                {
                    if(Time.frameCount<=cancelFrame+2)return;
                    Check(session.BootFailed&&!session.BootReady&&!session.Car&&Time.timeScale==0,"cancelled initial coverage never exposes a driveable car");
                    Check(!cancelledMesh,"cancel disposes the allocated hidden chunk mesh");Check(!session.World&&UnityEngine.Object.FindObjectsByType<GeoWorld>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0&&session.BootCleanupStatus=="","cancel removes incomplete world and completed chunks without cleanup warnings");Check(Hash(PlayerProfile.StoragePath)==cancelHash,"cancelled boot does not save a partial career");
                    previous=session;LocationLoading.Retry();phase=11;ResetObservation();return;
                }
                if(!found||ReferenceEquals(found,previous))return;session=found;
                if(session.BootFailed)throw new Exception("Actual boot failed immediately: "+session.BootStatus);
                if(!session.BootReady)
                {
                    if(session.Car)throw new Exception("Car exposed before coverage");if(!sawEarly)Check(true,"car remains absent before required coverage");sawEarly=true;
                    if(lastFrame!=Time.frameCount){double now=EditorApplication.timeSinceStartup;if(lastHeartbeat!=0)maxHeartbeat=Math.Max(maxHeartbeat,now-lastHeartbeat);lastHeartbeat=now;lastFrame=Time.frameCount;earlyFrames++;}
                    if(index==3&&phase!=11&&session.World&&session.World.GeographicWorld)
                    {
                        var hidden=session.World.GeographicWorld.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.name=="Terrain"&&!f.gameObject.activeInHierarchy);
                        if(hidden){cancelledMesh=hidden.sharedMesh;cancelHash=Hash(PlayerProfile.StoragePath);cancelFrame=Time.frameCount;session.CancelBoot();phase=10;}
                    }
                    return;
                }
                if(LocationLoading.Busy)return;
                Check(sawEarly&&earlyFrames>=2,"actual loading yields multiple rendered game frames before car creation case "+index);
                Check(session.Car&&session.World&&session.Input&&session.Conditions&&session.Updates&&session.Menu&&Time.timeScale==0,"ready publishes a complete paused gameplay session case "+index);
                Check(session.Profile.credits==4321&&session.Economy.Credits==4321&&session.Profile.garage.cars.Count==2&&session.Profile.garage.Active.engine==2&&Mathf.Abs(session.Car.Damage-11)<.01f,"credits/garage/upgrades/damage survive asynchronous boot case "+index);
                Check(session.Profile.quality==0&&Mathf.Abs(session.Profile.volume-.23f)<.0001f&&Mathf.Abs(session.Profile.wheelStrength-.31f)<.0001f,"graphics/audio/controller preferences retained case "+index);
                Check(session.Updates.SourceRequests==0&&session.Conditions.WeatherRequests==0,"isolated boot makes zero source/weather requests case "+index);
                var region=session.World.Route.Region;
                if(index==0)Check(region==null,"default procedural world still boots through the real asynchronous entry");
                else
                {
                    Check(WorldEpoch.Matches(WorldSnapshotCodec.Epoch(region),index==2?schemaEpoch:rawEpoch),"exact finalized canonical epoch survives scene attachment case "+index);
                    var centre=session.Car.Body.position;int cx=Mathf.FloorToInt(centre.x/320),cz=Mathf.FloorToInt(centre.z/320),tested=0;
                    for(int z=cz-3;z<=cz+3;z++)for(int x=cx-3;x<=cx+3;x++){if(x*320>=region.minX+region.width||(x+1)*320<=region.minX||z*320>=region.minZ+region.depth||(z+1)*320<=region.minZ)continue;Check(session.World.GeographicWorld.HasSurface(new Vector3(x*320+160,0,z*320+160)),"initial required cell physically published "+x+","+z);tested++;}Check(tested>0,"nonempty initial required coverage");
                    if(index==1){var road=region.Nearest(new Vector3(customX,0,customZ),out var point,out _);point=region.DrivingLanePoint(road,point,Vector3.forward,out _);Check(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(centre.x,centre.z))<.1f,"distant custom start uses the prepared cell window");}
                    if(index==2)Check(region.roadWidthSchema==1&&region.PreparedRoadWidths!=null,"authoritative schema1 cache attaches without missing controls");
                }
                Check(Hash(RegionCache.PathFor(SchemaId))==cacheHash&&Hash(realPath)==realHash,"source fixture and real career unchanged case "+index);
                if(index==3){Check(phase==11,"cancelled scene successfully retries through LocationLoading");Check(errors==0,"no runtime errors during boot/cancel/retry");File.WriteAllText(Path.Combine(root,"result.txt"),"PASS "+checks+" actual async-boot checks\nmaximum observed loading heartbeat gap seconds="+maxHeartbeat.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\nDiagnostic only; no heartbeat performance threshold asserted.\n");Finish(0);return;}
                previous=session;index++;ResetObservation();session.Profile.customStart=index==1;session.Profile.startRegion="dee-valley";session.Profile.startX=customX;session.Profile.startZ=customZ;
                Check(session.SelectRegion(index==2?SchemaId:"dee-valley"),"real location selection saves and reloads next isolated case");phase=0;
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){try{File.WriteAllText(Path.Combine(root,"boot-timing.json"),JsonUtility.ToJson(BootTelemetry.Capture(),true));}catch(Exception e){Debug.LogWarning("Timing output failed: "+e.Message);code=1;}if(Hash(realPath)!=realHash){Debug.LogError("ASYNC BOOT protected real career changed");code=1;}SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
