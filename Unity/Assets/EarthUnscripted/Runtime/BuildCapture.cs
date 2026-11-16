using System.Collections;
using System.IO;
using UnityEngine;

namespace EarthUnscripted
{
    // Standalone visual verification, enabled only by an explicit command-line flag.
    public sealed class BuildCapture : MonoBehaviour
    {
        [System.Serializable] sealed class VideoRoadRouteReceipt
        {
            public string regionId, firstRoadId;
            public bool customStartUsed;
            public Vector3 requestedOrigin, actualRouteStart;
            public Vector3[] sourceRoutePoints;
            public string qualification = "Planned genuine source-connected automated route, not completed traversal";
        }
        [System.Serializable] sealed class VideoDrivingObservation
        {
            public string kind, nearestRoadId, nearestRoadName, routeRoadId, routeRoadName, forwardHitName, forwardHitEntity;
            // Source ownership is distinct from the legacy 2D-nearest labels above.
            public string actualSourceRoadId, roadOwnershipDiagnostic;
            public string sessionRoadId;
            public float sessionSpeedLimit, supportHeightY;
            public bool sessionOnRoad;
            public int videoFrame, frame, forwardTriangle;
            public float gameTime, fixedTime, progress, routeLateral, routeSeparation, speed, speed3D, damage;
            public float throttle, brake, steer, handbrake, collisionSeverity;
            public Vector3 body, velocity, routePoint, collisionPoint, forwardHitPoint;
            public Quaternion rotation;
            public bool[] controllerWheelGrounded;
            public Vector3[] controllerContactPoints;
            public string[] observationRayNames = new string[4];
            public Vector3[] observationRayPoints = new Vector3[4];
            public bool forwardRayHit;
        }

        // QA-only observations: no forces, actor pose writes or support waivers.
        // Controller contacts are retained separately from these fresh query rays.
        // One row/second plus bounded collision/recovery events survives failure.
        void WriteDrivingObservation(StreamWriter writer,GameSession session,RoutePath route,string kind,
            int videoFrame,ref int rowCount,ref bool truncated,float severity=0,Vector3 collisionPoint=default)
        {
            if(rowCount>=256){truncated=true;return;}
            var car=session.Car;var body=car.Body;var region=session.World.Route.Region;
            float progress=route.Closest(body.position,out float lateral,out float separation);
            var routePoint=route.At(progress,route.LaneOffset(progress));
            var nearest=region?.Nearest(body.position,out _,out _);
            var routeRoad=region?.Nearest(routePoint,out _,out _);
            // Record the same physical support reference used by gameplay, without
            // changing contacts or asking the guided route which road we occupy.
            // Replica wheel visuals are networked; suspension contacts are not refreshed.
            // Skip those stale arrays and use the existing loaded-rest fallback below.
            float supportTotal=0;int supportCount=0;
            for(int wheel=0;!car.Replica&&wheel<car.WheelGrounded.Length;wheel++)
                if(car.WheelGrounded[wheel]&&!float.IsNaN(car.ContactPoints[wheel].y)&&!float.IsInfinity(car.ContactPoints[wheel].y))
                {supportTotal+=car.ContactPoints[wheel].y;supportCount++;}
            float supportY=supportCount>0?supportTotal/supportCount:body.position.y-car.Metrics.RestRootHeight(body.mass,car.SpringFactor);
            var value=new VideoDrivingObservation{kind=kind,videoFrame=videoFrame,frame=Time.frameCount,
                gameTime=Time.time,fixedTime=Time.fixedTime,progress=progress,routeLateral=lateral,routeSeparation=separation,
                body=body.position,rotation=body.rotation,velocity=body.linearVelocity,speed=car.Speed,speed3D=car.AbsoluteSpeed,
                damage=car.Damage,throttle=car.NetworkThrottle,brake=car.NetworkBrake,steer=car.NetworkSteer,handbrake=car.NetworkHandbrake,
                routePoint=routePoint,nearestRoadId=nearest?.id,nearestRoadName=nearest?.name,routeRoadId=routeRoad?.id,routeRoadName=routeRoad?.name,
                actualSourceRoadId=route.SourceRoadIdAt(progress),roadOwnershipDiagnostic=route.RoadOwnershipDiagnostic,
                sessionRoadId=session.CurrentRoad?.id,sessionSpeedLimit=session.Limit,sessionOnRoad=session.OnRoad,supportHeightY=supportY,
                controllerWheelGrounded=(bool[])car.WheelGrounded.Clone(),controllerContactPoints=(Vector3[])car.ContactPoints.Clone(),
                collisionSeverity=severity,collisionPoint=collisionPoint};
            for(int wheel=0;wheel<4;wheel++)
            {
                var anchor=VehicleGeometry.Anchor(car.Metrics.Model,wheel);
                if(Physics.Raycast(car.transform.TransformPoint(anchor),-car.transform.up,out var hit,car.Metrics.SuspensionRayLength,~(1<<8),QueryTriggerInteraction.Ignore))
                {value.observationRayNames[wheel]=hit.collider.name;value.observationRayPoints[wheel]=hit.point;}
            }
            value.forwardRayHit=Physics.Raycast(body.position,car.transform.forward,out var obstruction,12,~(1<<8),QueryTriggerInteraction.Ignore);
            if(value.forwardRayHit){value.forwardHitName=obstruction.collider.name;value.forwardHitEntity=obstruction.collider.GetEntityId().ToString();value.forwardHitPoint=obstruction.point;value.forwardTriangle=obstruction.triangleIndex;}
            writer.WriteLine(JsonUtility.ToJson(value));writer.Flush();rowCount++;
        }

        string capturePrefix;GameSession captureSession;bool failed,isolated;double preparationSeconds;
        IEnumerator Start(){yield return Guard(Capture());}
        IEnumerator Guard(IEnumerator job)
        {
            try
            {
                while(!failed)
                {
                    bool more=false;object next=null;System.Exception error=null;
                    try{more=job.MoveNext();if(more)next=job.Current;}catch(System.Exception e){error=e;}
                    if(error!=null){Fail(error.ToString());yield break;}if(!more)yield break;
                    if(next is IEnumerator nested)yield return Guard(nested);else yield return next;
                }
            }
            finally{(job as System.IDisposable)?.Dispose();}
        }
        void Fail(string reason)
        {
            if(failed)return;failed=true;Time.captureFramerate=0;
            if(captureSession&&isolated){captureSession.CancelPreparingStart();captureSession.SetMenu(true);}
            Debug.LogError("Standalone capture failed: "+reason);
            if(!string.IsNullOrEmpty(capturePrefix))
                try{File.WriteAllText(capturePrefix+"-result.txt","FAIL standalone capture: "+reason+"\n");File.WriteAllText(capturePrefix+"-video-result.txt","FAIL standalone capture: "+reason+"\n");}catch(System.Exception e){Debug.LogWarning("Could not write capture failure: "+e.Message);}
            Application.Quit(1);
        }
        void IsolateCapture(GameSession session,string prefix)
        {
            // Only the explicit command-line capture's own profile may disable
            // provider work or replace a live-weather choice with a clear fixture.
            if(string.IsNullOrEmpty(PlayerProfile.StorageOverride)||Path.GetFullPath(PlayerProfile.StoragePath)!=Path.GetFullPath(prefix+"-profile.json"))
                throw new System.InvalidOperationException("Capture requires its exact isolated -earthUnscriptedCapture profile");
            isolated=true;
            if(session.Updates){session.Updates.Enabled=false;session.Updates.Automatic=false;}
            if(session.Weather==5)session.Weather=0;
            session.Conditions.CancelLocalFetch();
            if(session.Conditions.WeatherRequests!=0||(session.Updates&&session.Updates.SourceRequests!=0))throw new System.InvalidOperationException("Provider request started before isolated capture setup");
        }
        IEnumerator ResumePreparedDrive(GameSession session,Vector3 position,Quaternion rotation)
        {
            bool previouslyLocked=session.Car.StartLocked;session.Car.StartLocked=true;
            // Leave the showroom while still paused, so its observer and hidden
            // player mesh are restored before preparing the actual driving start.
            if(session.View.Preview){session.GetComponent<DrivingUI>().ShowSettings();for(int i=0;i<2;i++)yield return new WaitForEndOfFrame();}
            session.Car.Recover(position,rotation);session.Input.enabled=false;session.Input.Throttle=session.Input.Brake=session.Input.Steer=session.Input.Handbrake=0;
            if(session.World.GeographicWorld)session.World.GeographicWorld.Observer=session.Car.transform;
            double began=Time.realtimeSinceStartupAsDouble;session.ResumeDrive();
            while(session.PreparingStart&&Time.realtimeSinceStartupAsDouble-began<90)yield return new WaitForEndOfFrame();
            preparationSeconds=Time.realtimeSinceStartupAsDouble-began;
            if(session.PreparingStart||session.Menu||session.World.GeographicWorld&&!session.World.IsCircuit&&!session.World.GeographicWorld.DriveFrontierReady)
                throw new System.InvalidOperationException("Drive preparation did not complete: "+session.PreparingStartStatus);
            if(session.View.Preview||session.Car.Visual.PresentationHidden)throw new System.InvalidOperationException("Showroom did not restore the driving view");
            if(session.Conditions.WeatherRequests!=0||(session.Updates&&session.Updates.SourceRequests!=0))throw new System.InvalidOperationException("Capture attempted an online provider request");
            session.Car.StartLocked=previouslyLocked||(session.World.IsCircuit&&session.Racing.Running&&session.Racing.Countdown>0);
        }
        IEnumerator Capture()
        {
            var args=System.Environment.GetCommandLineArgs();string prefix=null;
            for(int i=0;i<args.Length-1;i++)if(args[i]=="-earthUnscriptedCapture")prefix=args[i+1];
            bool town=System.Array.IndexOf(args,"-earthUnscriptedTown")>=0;bool garage=System.Array.IndexOf(args,"-earthUnscriptedGarage")>=0;
            var session=GetComponent<GameSession>();
            if(town)session.Car.Recover(session.World.Route.At(session.World.Route.Length*.43f,-2.7f),session.World.Route.Rotation(session.World.Route.Length*.43f));
            if(prefix==null){Destroy(this);yield break;}
            capturePrefix=prefix;captureSession=session;IsolateCapture(session,prefix);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix)));
            int eyeAt=System.Array.IndexOf(args,"-earthUnscriptedCockpitEye");if(eyeAt>=0&&eyeAt+1<args.Length)
            {
                var values=args[eyeAt+1].Split(',');float x=0,y=0,z=0;var culture=System.Globalization.CultureInfo.InvariantCulture;
                if(values.Length==3&&float.TryParse(values[0],System.Globalization.NumberStyles.Float,culture,out x)&&float.TryParse(values[1],System.Globalization.NumberStyles.Float,culture,out y)&&float.TryParse(values[2],System.Globalization.NumberStyles.Float,culture,out z)&&Mathf.Abs(x)<=.6f&&y>=.7f&&y<=1.4f&&z>=-.5f&&z<=.85f)session.View.DevelopmentCockpitEye=new Vector3(x,y,z);
            }
            Application.runInBackground=true;
            if(System.Array.IndexOf(args,"-earthUnscriptedVideo")>=0){yield return RecordVideo(session,prefix);yield break;}
            if(System.Array.IndexOf(args,"-earthUnscriptedCaptureStartupRecovery")>=0)
            {
                if(!FindAnyObjectByType<StartupLocationRecoveryCapture>())new GameObject("Isolated startup recovery acceptance").AddComponent<StartupLocationRecoveryCapture>().Initialize(session,prefix);
                yield break;
            }
            bool hiRes=System.Array.IndexOf(args,"-earthUnscriptedCaptureHiRes")>=0;Screen.SetResolution(hiRes?2560:1440,hiRes?1600:900,FullScreenMode.Windowed);
            int showcaseAt=System.Array.IndexOf(args,"-earthUnscriptedShowcasePack");
            if(showcaseAt>=0){if(showcaseAt+1>=args.Length)throw new System.ArgumentException("Showcase archive required");if(!FindAnyObjectByType<ShowcaseCircuitCapture>())new GameObject("Isolated showcase capture").AddComponent<ShowcaseCircuitCapture>().Initialize(session,prefix,args[showcaseAt+1]);Destroy(this);yield break;}
            int importedCircuitAt=System.Array.IndexOf(args,"-earthUnscriptedCaptureCircuitPack");
            if(importedCircuitAt>=0)
            {
                if(importedCircuitAt+1>=args.Length)throw new System.ArgumentException("Circuit pack archive required");
                if(!FindAnyObjectByType<CircuitImportCapture>())new GameObject("Isolated imported circuit capture").AddComponent<CircuitImportCapture>().Initialize(session,prefix,args[importedCircuitAt+1]);
                Destroy(this);yield break;
            }
            int packSwitchAt=System.Array.IndexOf(args,"-earthUnscriptedCapturePackSwitch");
            if(packSwitchAt>=0)
            {
                if(packSwitchAt+1>=args.Length)throw new System.ArgumentException("Pack switch archive required");
                if(!FindAnyObjectByType<RegionalPackSwitchCapture>())new GameObject("Isolated regional pack switch capture").AddComponent<RegionalPackSwitchCapture>().Initialize(session,prefix,args[packSwitchAt+1]);
                Destroy(this);yield break;
            }
            int selectionAt=System.Array.IndexOf(args,"-earthUnscriptedCapturePackSelection");
            if(selectionAt>=0)
            {
                if(selectionAt+1>=args.Length)throw new System.ArgumentException("Pack selection archive required");
                yield return CapturePackSelection(session,prefix,args[selectionAt+1],System.Array.IndexOf(args,"-earthUnscriptedSelectionReload")>=0);yield break;
            }
            int movingAt=System.Array.IndexOf(args,"-earthUnscriptedCaptureMovingPacks");
            if(movingAt>=0)
            {
                if(movingAt+1>=args.Length)throw new System.ArgumentException("Moving pack archive required");
                yield return CaptureMovingPack(session,prefix,args[movingAt+1]);yield break;
            }
            int buildingAt=System.Array.IndexOf(args,"-earthUnscriptedCaptureBuildingWindow");
            if(buildingAt>=0)
            {
                if(buildingAt+1>=args.Length)throw new System.ArgumentException("Building-window archive required");
                yield return CaptureBuildingWindow(session,prefix,args[buildingAt+1]);yield break;
            }
            int packAt=System.Array.IndexOf(args,"-earthUnscriptedCapturePacks");
            if(packAt>=0)
            {
                if(packAt+1>=args.Length)throw new System.ArgumentException("Pack capture archive required");
                yield return CapturePackLibrary(session,prefix,args[packAt+1]);yield break;
            }
            if(System.Array.IndexOf(args,"-earthUnscriptedCaptureWorld")>=0)session.GetComponent<DrivingUI>().ShowWorldMap();
            if(System.Array.IndexOf(args,"-earthUnscriptedCaptureCareer")>=0)session.GetComponent<DrivingUI>().ShowCareers();
            if(System.Array.IndexOf(args,"-earthUnscriptedCaptureSettings")>=0)session.GetComponent<DrivingUI>().ShowSettings();
            if(garage){session.TowToGarage();session.GetComponent<DrivingUI>().ShowGarage();}
            for(int i=0;i<(garage?90:20);i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-menu.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            float location=session.World.Route.Region!=null&&town?35:session.World.Route.Length*(town?.43f:.66f);
            yield return ResumePreparedDrive(session,session.World.Route.At(location,session.World.Route.LaneOffset(location)),session.World.Route.Rotation(location));
            if(failed)yield break;
            session.View.Mode=0;
            for(int i=0;i<90;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-drive.png");
            session.View.Mode=2;
            for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-cockpit.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            if(session.Conditions.WeatherRequests!=0||(session.Updates&&session.Updates.SourceRequests!=0))throw new System.InvalidOperationException("Capture attempted an online provider request");
            File.WriteAllText(prefix+"-result.txt","Standalone capture completed; scene, UI and cameras rendered.\n"+session.Conditions.WeatherStatus+"\n"+session.Conditions.ClockStatus+"\nChunks="+(session.World.GeographicWorld?session.World.GeographicWorld.LoadedChunks:0)+"\nDrivePreparationSeconds="+preparationSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"\nProviderRequests=0");
            Application.Quit();
        }
        IEnumerator CapturePackSelection(GameSession session,string prefix,string archive,bool reload)
        {
            var activation=session.GetComponent<RegionalPackActivation>();var world=session.World.GeographicWorld;var region=world.Region;
            if(!activation||!world||region.circuit||session.Network.Active)throw new System.InvalidOperationException("Offline normal pack activation required");
            var canonicalBuildings=region.buildings;var canonicalRoads=region.roads;
            session.Car.StartLocked=true;session.Input.enabled=false;session.Input.Throttle=session.Input.Steer=0;session.Input.Brake=session.Input.Handbrake=1;
            foreach(var police in FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None))police.gameObject.SetActive(false);
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            var roads=new System.Collections.Generic.List<MeshCollider>();foreach(var c in world.GetComponentsInChildren<MeshCollider>())if(c.name.StartsWith("Mapped road")||c.name.StartsWith("Finalized mapped asphalt"))roads.Add(c);
            if(roads.Count==0)throw new System.InvalidOperationException("Road collider witnesses missing");
            var ui=session.GetComponent<DrivingUI>();double deadline=Time.realtimeSinceStartupAsDouble+120;
            while(activation.PreferenceBusy&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(activation.PreferenceBusy)throw new System.TimeoutException("Initial selection read");
            ui.ShowImportedPacks();var menu=ui.ImportedPacks;
            while(menu.Busy&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(menu.Busy)throw new System.TimeoutException("Pack inventory read");
            if(!reload)
            {
                if(activation.Selected.Enabled)throw new System.InvalidOperationException("Fresh test inherited a selection");
                menu.ImportArchive(archive);
                while(menu.Busy&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
                if(menu.Busy||menu.RevisionCount!=1)throw new System.InvalidOperationException("Normal import failed: "+menu.Status);
                menu.PreviewRevision(0);
            }
            else if(!activation.Selected.Enabled)throw new System.InvalidOperationException("Saved preview was not reloaded");
            deadline=Time.realtimeSinceStartupAsDouble+120;
            while((activation.PreferenceBusy||!activation.Controller||activation.Controller.Busy||world.BuildingWindowIdentity==null)&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(activation.PreferenceBusy||!activation.Controller||activation.Controller.Busy||world.BuildingWindowIdentity==null)throw new System.InvalidOperationException("Normal preview failed: "+activation.Status);
            string preview=world.BuildingWindowIdentity;
            if(!activation.Selected.AllowCandidatePreview)throw new System.InvalidOperationException("Preview opt-in missing");
            session.Network.Host();if(session.Network.Active)throw new System.InvalidOperationException("Unshared preview hosted LAN");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-selection-enabled.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            string root=RegionalPackActivation.LibraryRoot(),setting=Path.Combine(root,"selection.json");byte[] before=File.ReadAllBytes(setting);
            using(var held=new FileStream(Path.Combine(root,".selection.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                menu.DisablePreview();deadline=Time.realtimeSinceStartupAsDouble+20;
                while(activation.PreferenceBusy&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
                if(activation.PreferenceBusy||!activation.Selected.Enabled||world.BuildingWindowIdentity!=preview||!System.Linq.Enumerable.SequenceEqual(before,File.ReadAllBytes(setting)))throw new System.InvalidOperationException("Failed preference write changed scenery/choice");
            }
            menu.DisablePreview();session.Network.Join("127.0.0.1");if(session.Network.Active)throw new System.InvalidOperationException("Pending restoration joined LAN");
            deadline=Time.realtimeSinceStartupAsDouble+120;
            int restorationStagingFrames=0;
            while((activation.PreferenceBusy||world.BuildingWindowBusy||world.BuildingWindowIdentity!=null)&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                if(world.BuildingWindowBusy)
                {
                    restorationStagingFrames++;
                    if(world.BuildingWindowIdentity!=preview)throw new System.InvalidOperationException("Preview identity cleared before restoration commit");
                }
                yield return new WaitForEndOfFrame();
            }
            if(restorationStagingFrames==0)throw new System.InvalidOperationException("No staged restoration observed");
            if(activation.PreferenceBusy||world.BuildingWindowBusy||world.BuildingWindowIdentity!=null||activation.Selected.Enabled||Packs.WorldPackSelection.Load(root).Enabled)throw new System.InvalidOperationException("Default scenery restoration failed: "+activation.Status);
            foreach(var c in roads)if(!c||!c.enabled||!c.gameObject.activeInHierarchy)throw new System.InvalidOperationException("Scenery replacement retired a road collider");
            if(!object.ReferenceEquals(canonicalBuildings,region.buildings)||!object.ReferenceEquals(canonicalRoads,region.roads))throw new System.InvalidOperationException("Normal preview mutated canonical arrays");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-selection-disabled.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            // First process leaves an enabled preference for an actual second launch.
            if(!reload)
            {
                menu.PreviewRevision(0);deadline=Time.realtimeSinceStartupAsDouble+120;
                while((activation.PreferenceBusy||!activation.Controller||activation.Controller.Busy||world.BuildingWindowIdentity==null)&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
                if(activation.PreferenceBusy||activation.Controller.Busy||world.BuildingWindowIdentity==null||!Packs.WorldPackSelection.Load(root).Enabled)throw new System.InvalidOperationException("Reselection before restart failed");
            }
            var report=new Newtonsoft.Json.Linq.JObject{{"status","PASS_NORMAL_OFFLINE_PACK_SELECTION"},{"actualSavedSelectionReload",reload},{"normalUIHandlers",true},{"failedWriteRetainedChoiceAndScene",true},{"baselineRestoredBeforeIdentityCleared",true},{"restorationStagingFrames",restorationStagingFrames},{"roadColliderWitnessesUnchanged",roads.Count},{"canonicalArraysUnchanged",true},{"unsharedPreviewLANRefused",true},{"explicitPreviewOptIn",true},{"automatedQAProfile",true},{"hardwareInputVerified",false},{"worldSwitchVerified",false},{"damage",session.Car.Damage}};
            File.WriteAllText(prefix+"-selection-result.json",report.ToString(Newtonsoft.Json.Formatting.Indented));File.WriteAllText(prefix+"-result.txt","PASS normal offline selection handlers, failure retention and baseline restoration; automated isolated QA; location switching pending.\n");Application.Quit();
        }
        IEnumerator CaptureMovingPack(GameSession session,string prefix,string archive)
        {
            var world=session.World.GeographicWorld;var region=session.World.Route.Region;
            if(!world||region==null||region.circuit||session.Network.Active)throw new System.InvalidOperationException("Offline road scenery world required");
            foreach(var police in FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None))police.gameObject.SetActive(false);
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            var originalBuildings=region.buildings;var originalRoads=region.roads;
            var store=new Packs.WorldPackStore(Path.Combine(Path.GetDirectoryName(prefix),"MovingPacks"));
            var installation=System.Threading.Tasks.Task.Run(()=>store.Install(archive));
            while(!installation.IsCompleted)yield return new WaitForEndOfFrame();
            if(installation.IsFaulted)throw installation.Exception.GetBaseException();
            double preparationBegan=Time.realtimeSinceStartupAsDouble;
            var controller=session.gameObject.AddComponent<RegionalPackWindowController>();controller.OpenPreview(session,store,installation.Result);
            double deadline=Time.realtimeSinceStartupAsDouble+120;
            while((controller.Busy||controller.SubmittedWindows<1||world.BuildingWindowBusy)&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(controller.SubmittedWindows<1||controller.Busy||world.BuildingWindowBusy)throw new System.InvalidOperationException("Moving scenery did not prepare: "+controller.Status);
            double initialScenerySeconds=Time.realtimeSinceStartupAsDouble-preparationBegan;
            float initialMaximumSlice=world.MaximumBuildingWindowSliceMilliseconds;world.ResetBuildingWindowSliceMetrics();
            var route=RoadDemoRoute.Build(region,session.Car.transform.position);
            yield return ResumePreparedDrive(session,route.At(0,route.LaneOffset(0)),route.Rotation(0));
            session.Input.enabled=false;session.Car.NetworkDriven=true;
            var driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Route=route;driver.Session=session;driver.Index=0;driver.Difficulty=0;driver.RoadLane=true;
            driver.Fleet=new System.Collections.Generic.List<VehicleController>{session.Car};
            int recoveries=0;System.Action<float> recovered=progress=>recoveries++;driver.Recovered+=recovered;
            int traceRows=0;string tracePath=prefix+"-moving-trace.jsonl";
            System.Action<string> observe=kind=>{
                if(traceRows++>=256)return;var car=session.Car;var position=car.transform.position;var velocity=car.Body.linearVelocity;
                var row=new Newtonsoft.Json.Linq.JObject{{"kind",kind},{"realtime",Time.realtimeSinceStartupAsDouble},{"position",new Newtonsoft.Json.Linq.JArray(position.x,position.y,position.z)},
                    {"velocity",new Newtonsoft.Json.Linq.JArray(velocity.x,velocity.y,velocity.z)},{"damage",car.Damage},{"reverse",car.Reverse},{"startLocked",car.StartLocked},
                    {"impactObject",car.LastImpactObject},{"worldBusy",world.BuildingWindowBusy},{"readyChunks",world.PreparedBuildingWindowChunks},{"residentChunks",world.LoadedChunks},
                    {"queries",controller.QueriesCompleted},{"submitted",controller.SubmittedWindows},{"parts",controller.LastRegionalParts},{"bytes",controller.LastSelectedBytes},{"status",controller.Status}};
                File.AppendAllText(tracePath,row.ToString(Newtonsoft.Json.Formatting.None)+"\n");
            };
            System.Action<float,Vector3> crashed=(severity,point)=>observe("collision");session.Car.Crashed+=crashed;
            int cellChanges=0;var previousCell=new Vector2Int(Mathf.FloorToInt(session.Car.transform.position.x/320),Mathf.FloorToInt(session.Car.transform.position.z/320));
            Vector3 previousPosition=session.Car.transform.position;double distance=0;
            session.View.Mode=0;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;Time.captureFramerate=30;
            try
            {
                for(int frame=0;frame<1350;frame++)
                {
                    yield return new WaitForEndOfFrame();var position=session.Car.transform.position;distance+=Vector3.Distance(previousPosition,position);previousPosition=position;
                    var cell=new Vector2Int(Mathf.FloorToInt(position.x/320),Mathf.FloorToInt(position.z/320));if(cell!=previousCell){cellChanges++;previousCell=cell;}
                    if(frame%30==0)observe("drive");
                    if(frame==150)ScreenCapture.CaptureScreenshot(prefix+"-moving-start.png");
                    if(frame==675)ScreenCapture.CaptureScreenshot(prefix+"-moving-mid.png");
                    if(frame==1200)ScreenCapture.CaptureScreenshot(prefix+"-moving-late.png");
                }
            }
            finally
            {
                observe("drive-end");Time.captureFramerate=0;driver.Recovered-=recovered;driver.enabled=false;
                // A held brake alone requests reverse after stopping. Use the same
                // service/handbrake start lock as a paused start, preserving physics.
                session.Car.StartLocked=true;session.Car.NetworkThrottle=session.Car.NetworkSteer=0;session.Car.NetworkBrake=session.Car.NetworkHandbrake=1;
            }
            double waitBegan=Time.realtimeSinceStartupAsDouble,nextObservation=waitBegan;deadline=waitBegan+60;
            while((controller.Busy||world.BuildingWindowBusy)&&Time.realtimeSinceStartupAsDouble<deadline)
            {if(Time.realtimeSinceStartupAsDouble>=nextObservation){observe("waiting");nextObservation=Time.realtimeSinceStartupAsDouble+1;}yield return new WaitForEndOfFrame();}
            double finalSceneryWaitSeconds=Time.realtimeSinceStartupAsDouble-waitBegan;observe("wait-end");session.Car.Crashed-=crashed;
            if(controller.Busy||world.BuildingWindowBusy||controller.QueriesCompleted<2||cellChanges<1||distance<100||recoveries!=0||session.Car.Damage>.01f)
                throw new System.InvalidOperationException("Moving scenery incomplete: "+controller.Status+" distance="+distance+" queries="+controller.QueriesCompleted+" cells="+cellChanges+" recoveries="+recoveries+" damage="+session.Car.Damage);
            if(!ReferenceEquals(originalBuildings,region.buildings)||!ReferenceEquals(originalRoads,region.roads))throw new System.InvalidOperationException("Moving scenery changed canonical geography");
            session.Network.Host();if(session.Network.Active)throw new System.InvalidOperationException("Unshared preview incorrectly hosted LAN");
            session.Network.Join("127.0.0.1");if(session.Network.Active)throw new System.InvalidOperationException("Unshared preview incorrectly joined LAN");
            int queries=controller.QueriesCompleted,submitted=controller.SubmittedWindows,unchanged=controller.UnchangedWindows;long selectedBytes=controller.MaximumSelectedBytes;int selectedParts=controller.MaximumRegionalParts;
            controller.StopPreview();
            for(int i=0;i<8;i++){controller.OpenPreview(session,store,installation.Result);controller.StopPreview();}
            deadline=Time.realtimeSinceStartupAsDouble+30;
            while(controller.PendingWorkerCount>0&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(controller.PendingWorkerCount!=0||controller.MaximumConcurrentWorkers>1)throw new System.InvalidOperationException("Reopened regional workers exceeded their serial budget");
            for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
            if(controller.Active||world.BuildingWindowBusy)throw new System.InvalidOperationException("Stopped controller left a scenery operation active");
            var report=new Newtonsoft.Json.Linq.JObject{{"status","PASS_NATIVE_MOVING_REGIONAL_WINDOWS"},{"simulatedSeconds",45},{"distanceMetres",distance},{"observerCellChanges",cellChanges},
                {"queriesCompleted",queries},{"submittedWindows",submitted},{"unchangedWindows",unchanged},{"maximumSelectedSourceBytes",selectedBytes},{"maximumSelectedParts",selectedParts},{"totalInstalledPackBytes",installation.Result.Manifest.InstalledBytes},{"maximumBuildingSliceMs",world.MaximumBuildingWindowSliceMilliseconds},{"initialMaximumSliceMs",initialMaximumSlice},{"initialScenerySeconds",initialScenerySeconds},{"finalSceneryWaitSeconds",finalSceneryWaitSeconds},{"recoveries",recoveries},{"damage",session.Car.Damage},
                {"canonicalArraysUnchanged",true},{"LANrefusedUnsharedPreview",true},{"stopDuringOpeningRetainedScenery",true},{"maximumConcurrentWorkers",controller.MaximumConcurrentWorkers},{"retiredWorkersDrained",controller.PendingWorkerCount==0},{"full10miCoverage",false},{"QApreviewOnly",true}};
            File.WriteAllText(prefix+"-moving-result.json",report.ToString(Newtonsoft.Json.Formatting.Indented));File.WriteAllText(prefix+"-result.txt","PASS native moving pack windows; automated driver; QA-only; full10mi/public/LANactivation pending.\n");
            Application.Quit();
        }
        [System.Serializable] sealed class BuildingWindowPayload { public GeoBuilding[] buildings; }
        IEnumerator CaptureBuildingWindow(GameSession session,string prefix,string archive)
        {
            var world=session.World.GeographicWorld;var region=session.World.Route.Region;
            if(!world||region==null||region.circuit||session.Network.Active)throw new System.InvalidOperationException("Offline geographic world required");
            var originalBuildings=region.buildings;var originalRoads=region.roads;
            // Keep this scenery/fault exercise independent of traffic/pursuit outcomes.
            foreach(var police in FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None))police.gameObject.SetActive(false);
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            string source=JsonUtility.ToJson(region);string storePath=Path.Combine(Path.GetDirectoryName(prefix),"WindowPacks");
            var task=System.Threading.Tasks.Task.Run(()=>{
                var store=new Packs.WorldPackStore(storePath);var installed=store.Install(archive);
                var readers=new Packs.PackEngineSupport("0.20.0",new System.Collections.Generic.Dictionary<string,int>{{"regional-index",1},{"regional-buildings",1},{"provenance",1}});
                var reader=Packs.WorldPackRegionalReader.OpenVerified(store,installed,readers);
                var selected=reader.Query(reader.VerificationWindow,new RegionalDraft.RegionalCollection.Limits());
                return Packs.WorldPackBuildingWindow.Prepare(Newtonsoft.Json.Linq.JObject.Parse(source),selected,installed.Manifest.ManifestSha256);
            });
            while(!task.IsCompleted)yield return new WaitForEndOfFrame();
            if(task.IsFaulted)throw task.Exception.GetBaseException();var plan=task.Result;
            int baselineWalls=0;foreach(var filter in world.GetComponentsInChildren<MeshFilter>())if(filter.name.StartsWith("Building ")&&filter.name!="Building scenery")baselineWalls++;
            var roadBodies=new System.Collections.Generic.List<MeshCollider>();
            foreach(var collider in world.GetComponentsInChildren<MeshCollider>())if(collider.name.StartsWith("Mapped road")||collider.name.StartsWith("Finalized mapped asphalt"))roadBodies.Add(collider);
            if(roadBodies.Count==0)throw new System.InvalidOperationException("No road collider witnesses");
            var payload=new Newtonsoft.Json.Linq.JObject{{"buildings",plan.CopyRenderBuildings()}};
            var buildings=JsonUtility.FromJson<BuildingWindowPayload>(payload.ToString(Newtonsoft.Json.Formatting.None)).buildings;
            string previousIdentity=world.BuildingWindowIdentity;
            world.QueueBuildingWindowPreview(region,buildings,plan.Identity);
            yield return new WaitForEndOfFrame();
            if(!world.BuildingWindowBusy)throw new System.InvalidOperationException("Partial-cancellation case completed too early");
            world.CancelBuildingWindowPreview();
            for(int i=0;i<2;i++)yield return new WaitForEndOfFrame();
            if(world.BuildingWindowBusy||world.BuildingWindowIdentity!=previousIdentity)throw new System.InvalidOperationException("Cancelled window changed published identity");
            foreach(var child in world.GetComponentsInChildren<Transform>(true))if(child.name=="Pending building scenery")throw new System.InvalidOperationException("Cancelled building child remained allocated");
            world.QueueBuildingWindowPreview(region,buildings,plan.Identity);
            double deadline=Time.realtimeSinceStartupAsDouble+120;
            while(world.BuildingWindowBusy&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(world.BuildingWindowBusy||world.BuildingWindowIdentity!=plan.Identity)throw new System.InvalidOperationException(world.BuildingWindowStatus);
            if(!ReferenceEquals(originalBuildings,region.buildings)||!ReferenceEquals(originalRoads,region.roads))throw new System.InvalidOperationException("Canonical world arrays changed");
            foreach(var road in roadBodies)if(!road||!road.enabled||!road.gameObject.activeInHierarchy)throw new System.InvalidOperationException("Road collider replaced or disabled during scenery swap");
            int emittedWalls=0;foreach(var filter in world.GetComponentsInChildren<MeshFilter>())if(filter.name.StartsWith("Building ")&&filter.name!="Building scenery")emittedWalls++;
            if(emittedWalls==0)throw new System.InvalidOperationException("No building walls emitted");
            var retainedChildren=new System.Collections.Generic.List<GameObject>();
            foreach(var child in world.GetComponentsInChildren<Transform>())if(child.name=="Building scenery")retainedChildren.Add(child.gameObject);
            var retainedSites=new System.Collections.Generic.List<string>(world.BuildingSites.Keys);
            world.QueueBuildingWindowPreview(region,buildings,plan.Identity);deadline=Time.realtimeSinceStartupAsDouble+90;
            while(world.BuildingWindowBusy&&Time.realtimeSinceStartupAsDouble<deadline)yield return new WaitForEndOfFrame();
            if(world.BuildingWindowBusy||world.LastBuildingWindowRebuiltChunks!=0||world.LastBuildingWindowReusedChunks==0)
                throw new System.InvalidOperationException("Identical building window rebuilt unchanged scenery");
            foreach(var child in retainedChildren)if(!child||!child.activeInHierarchy)throw new System.InvalidOperationException("Identical window lost a resident building root");
            foreach(var siteId in retainedSites)if(!world.BuildingSites.ContainsKey(siteId))throw new System.InvalidOperationException("Identical window lost derived building-site diagnostics");
            bool budgetRefused=false;var excessive=new GeoBuilding[2501];for(int i=0;i<excessive.Length;i++)excessive[i]=buildings[0];
            try{world.QueueBuildingWindowPreview(region,excessive,plan.Identity);}catch(System.IO.InvalidDataException){budgetRefused=true;}
            if(!budgetRefused||world.BuildingWindowBusy||world.BuildingWindowIdentity!=plan.Identity)throw new System.InvalidOperationException("Budget refusal changed active scenery");
            for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-building-window-menu.png");
            yield return ResumePreparedDrive(session,session.Car.transform.position,session.Car.transform.rotation);
            session.View.Mode=0;for(int i=0;i<120;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-building-window-drive.png");
            session.View.Mode=2;for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-building-window-cockpit.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            var receipt=new Newtonsoft.Json.Linq.JObject{{"status","PASS_NATIVE_BUILDING_WINDOW_SWAP"},{"identity",plan.Identity},
                {"baselineParts",plan.BaselineParts},{"regionalParts",plan.RegionalParts},{"admittedParts",plan.RenderBuildingCount},
                {"baselineWallMeshes",baselineWalls},{"emittedWallMeshes",emittedWalls},{"retainedRoadColliders",roadBodies.Count},
                {"canonicalArraysUnchanged",true},{"partialCancellationCleanedUp",true},{"identicalWindowReusedChunks",world.LastBuildingWindowReusedChunks},{"identicalWindowRebuiltChunks",world.LastBuildingWindowRebuiltChunks},{"overBudgetRefusedKeepingCurrentScenery",true},{"sourceAuthoringBlocked",plan.SourceAuthoringBlocked},{"QApreviewOnly",true},
                {"remainingOverlapReviewCases",plan.Admission.LocalReviewCases.Count},{"fullMovingWindowAcceptance",false}};
            File.WriteAllText(prefix+"-building-window-result.json",receipt.ToString(Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(prefix+"-result.txt","PASS native building-window swap; QA preview only; full moving-window and LAN admission pending.\n");Application.Quit();
        }
        IEnumerator CapturePackLibrary(GameSession session,string prefix,string archive)
        {
            if(Application.companyName!="EarthUnscripted Verification"||Application.identifier!="com.earthunscripted.qa.loadingcamera")
                throw new System.InvalidOperationException("Pack capture requires separate QA identity");
            var ui=session.GetComponent<DrivingUI>();ui.ShowImportedPacks();var menu=ui.ImportedPacks;
            double deadline=Time.realtimeSinceStartupAsDouble+60;
            while(menu.Busy&&Time.realtimeSinceStartupAsDouble<deadline){menu.Poll();yield return new WaitForEndOfFrame();}
            if(menu.Busy||menu.RevisionCount!=0)throw new System.InvalidOperationException("Fresh pack library required: "+menu.Status);
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-packs-empty.png");
            menu.ImportArchive(archive);deadline=Time.realtimeSinceStartupAsDouble+90;
            while(menu.Busy&&Time.realtimeSinceStartupAsDouble<deadline){menu.Poll();yield return new WaitForEndOfFrame();}
            if(menu.Busy||menu.RevisionCount!=1)throw new System.InvalidOperationException("Pack import did not complete: "+menu.Status);
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-packs-installed.png");
            menu.CheckContent(0);deadline=Time.realtimeSinceStartupAsDouble+90;
            while(menu.Busy&&Time.realtimeSinceStartupAsDouble<deadline){menu.Poll();yield return new WaitForEndOfFrame();}
            if(menu.Busy||!menu.Status.Contains("318 whole building parts"))throw new System.InvalidOperationException("Pantside reference audit failed: "+menu.Status);
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-packs-checked.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            if(session.Conditions.WeatherRequests!=0||(session.Updates&&session.Updates.SourceRequests!=0))throw new System.InvalidOperationException("Pack capture requested provider data");
            File.WriteAllText(prefix+"-result.txt","PASS native pack library import/check workflow; isolated QA career; "+menu.Status+"\nNo pack scene activation or regional rendering acceptance.\n");
            Application.Quit();
        }
        IEnumerator RecordVideo(GameSession session,string prefix)
        {
            // Explicit development capture uses the isolated -earthUnscriptedCapture profile.
            var args=System.Environment.GetCommandLineArgs();int seconds=30;
            int durationAt=System.Array.IndexOf(args,"-earthUnscriptedVideoSeconds");
            if(durationAt>=0&&(durationAt+1>=args.Length||!int.TryParse(args[durationAt+1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out seconds)||seconds<10||seconds>120))
                throw new System.ArgumentException("Video duration must be a whole number from 10 to 120 seconds");
            int totalFrames=seconds*30,cockpitFrame=totalFrames*2/3;
            bool roadVideo=System.Array.IndexOf(args,"-earthUnscriptedRoadVideo")>=0;
            if(roadVideo)foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            Screen.SetResolution(1920,1200,FullScreenMode.Windowed);
            for(int i=0;i<60;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-main-menu.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            session.GetComponent<DrivingUI>().ShowSettings();
            for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-settings.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            session.GetComponent<DrivingUI>().ShowWorldMap();
            for(int i=0;i<10;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-world-map.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            session.GetComponent<DrivingUI>().ShowGarage();
            for(int i=0;i<60;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-garage.png");
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            // Honour the existing normal map-start coordinates for this private
            // diagnostic. The default remains the original zero origin. This is
            // a route request: RoadDemoRoute still owns directed source topology.
            var region=session.World.Route.Region;
            bool customRoadStart=roadVideo&&session.Profile.customStart&&region!=null&&session.Profile.startRegion==region.id;
            var origin=customRoadStart?new Vector3(session.Profile.startX,0,session.Profile.startZ):Vector3.zero;
            var drivingRoute=roadVideo?RoadDemoRoute.Build(region,origin):session.World.Route;
            if(roadVideo)
            {
                var first=drivingRoute.At(0);var firstRoad=region.Nearest(first,out _,out _);
                var receipt=new VideoRoadRouteReceipt{regionId=region.id,firstRoadId=firstRoad?.id,customStartUsed=customRoadStart,
                    requestedOrigin=origin,actualRouteStart=first,sourceRoutePoints=(Vector3[])drivingRoute.Points.Clone()};
                using(var stream=new FileStream(prefix+"-road-route.json",FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                using(var writer=new StreamWriter(stream))writer.Write(JsonUtility.ToJson(receipt,true));
            }
            float start=roadVideo?0:200;
            yield return ResumePreparedDrive(session,drivingRoute.At(start,roadVideo?drivingRoute.LaneOffset(start):0),drivingRoute.Rotation(start));
            if(failed)yield break;
            session.Input.enabled=false;session.Car.NetworkDriven=true;
            var driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Route=drivingRoute;driver.Session=session;driver.Index=0;driver.Difficulty=0;driver.RoadLane=roadVideo;
            driver.Fleet=new System.Collections.Generic.List<VehicleController>(FindObjectsByType<VehicleController>(FindObjectsSortMode.None));
            session.View.Mode=0;Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            for(int i=0;i<60;i++)yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(prefix+"-frames");Time.captureFramerate=30;
            int traceRows=0,traceFrame=0,collisionEvents=0,recoveryEvents=0;bool traceTruncated=false;
            using(var stream=new FileStream(prefix+"-driving-trace.jsonl",FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var trace=new StreamWriter(stream))
            {
                System.Action<float,Vector3> crashed=(severity,point)=>
                {collisionEvents++;WriteDrivingObservation(trace,session,drivingRoute,"collision",traceFrame,ref traceRows,ref traceTruncated,severity,point);};
                System.Action<float> recovered=progress=>
                {recoveryEvents++;WriteDrivingObservation(trace,session,drivingRoute,"recovery-after-reset",traceFrame,ref traceRows,ref traceTruncated);};
                session.Car.Crashed+=crashed;driver.Recovered+=recovered;
                try
                {
                    for(int frame=0;frame<totalFrames;frame++)
                    {
                        traceFrame=frame;
                        if(frame==cockpitFrame)session.View.Mode=2;
                        yield return new WaitForEndOfFrame();
                        if(frame%30==0)WriteDrivingObservation(trace,session,drivingRoute,"one-second-sample",frame,ref traceRows,ref traceTruncated);
                        ScreenCapture.CaptureScreenshot(Path.Combine(prefix+"-frames",frame.ToString("D5")+".png"));
                    }
                    traceFrame=totalFrames;
                    WriteDrivingObservation(trace,session,drivingRoute,"final",traceFrame,ref traceRows,ref traceTruncated);
                }
                finally
                {
                    if(session.Car)session.Car.Crashed-=crashed;
                    if(driver)driver.Recovered-=recovered;
                }
            }
            Time.captureFramerate=0;
            for(int i=0;i<5;i++)yield return new WaitForEndOfFrame();
            File.Copy(Path.Combine(prefix+"-frames",(totalFrames*4/15).ToString("D5")+".png"),prefix+"-driving.png",true);File.Copy(Path.Combine(prefix+"-frames",(totalFrames*13/15).ToString("D5")+".png"),prefix+"-inside.png",true);
            if(session.Conditions.WeatherRequests!=0||(session.Updates&&session.Updates.SourceRequests!=0))throw new System.InvalidOperationException("Video capture attempted an online provider request");
            File.WriteAllText(prefix+"-video-result.txt",totalFrames+" gameplay frames at30fps ="+seconds+"seconds;1920x1080; automated driver; first two thirds chase then cockpit; isolated career.\nDrivePreparationSeconds="+preparationSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"\nProviderRequests=0\nDrivingTraceRows="+traceRows+"\nDrivingTraceTruncated="+traceTruncated+"\nCollisionEvents="+collisionEvents+"\nRecoveryEvents="+recoveryEvents+"\nTrace is diagnostic, not completed traversal acceptance.\n");
            Application.Quit();
        }
    }
}
