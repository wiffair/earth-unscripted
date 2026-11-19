using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace EarthUnscripted
{
    /// <summary>
    /// Explicit command-line verification only. Normal double-click startup
    /// never creates this driver. Exercises keyboard input and the ordinary
    /// human-controlled vehicle with disposable beta-only save data.
    /// </summary>
    public sealed class BetaSmokeVerification : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly JArray driverHistory=new JArray();
        GameSession session;
        Keyboard keyboard;
        float drivenMetres, physicalDistance, speedIntegral;
        double drivingWall, drivingSimulation;
        float worstFrame;int slowFrames,bridgeChecks,unpublishedBridgeSamples;
        float guideStartAdjustment;
        int fastTrafficFrames,fastAheadTrafficFrames,fastOpposingFrames;float peakDrivingMph;
        float diagnosticProgress,diagnosticLookAhead,diagnosticTargetSpeed,diagnosticLane,stuckSeconds;
        Vector3 diagnosticAim,diagnosticAimLocal;
        float airborneSeconds;
        bool keyboardPassed;
        bool cameraPassed;
        bool careerPassed;
        bool lanRefused;
        bool geometryReusePassed;
        EarthUnscripted.NewbridgePrototype.NewbridgeWorld.JunctionGeometryEvidence junctionGeometry;

        void Awake()
        {
            if (!SinglePlayerBeta.Enabled || SinglePlayerBeta.VerificationOutput == null)
                throw new InvalidOperationException("Explicit isolated beta verification required");
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.logMessageReceived += CaptureError;
        }

        void CaptureError(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) && errors.Count < 30)
                errors.Add(message + "\n" + stack);
        }

        IEnumerator Start()
        {
            var work = Exercise();
            while (true)
            {
                bool more = false;
                object current = null;
                Exception failure = null;
                try { using(WorkTelemetry.Measure("qa.driver-step",null,8)){more = work.MoveNext(); if (more) current = work.Current;} }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "failure.png"));
                    yield return new WaitForEndOfFrame();
                    Finish(false, failure.ToString());
                    yield break;
                }
                if (!more) yield break;
                yield return current;
            }
        }

        IEnumerator Exercise()
        {
            double began = Time.realtimeSinceStartupAsDouble;
            while (!(session = FindFirstObjectByType<GameSession>()) || !session.BootReady)
            {
                if (SinglePlayerBeta.StartupError != null) throw new IOException(SinglePlayerBeta.StartupError);
                if (session && session.BootFailed) throw new IOException(session.BootStatus);
                Require(Time.realtimeSinceStartupAsDouble - began < 600, "Beta boot timed out");
                Progress("loading", session ? session.BootStatus : "Starting beta");
                yield return null;
            }
            Require(session.World.AutomaticWorld && session.World.AutomaticWorld.Ready, "Automatic world did not start");
            Require(!session.Car.NetworkDriven && session.Input.enabled, "Normal startup must use human input");
            Require(PlayerProfile.StoragePath.StartsWith(SinglePlayerBeta.DataRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Beta save escaped its own data folder");
            // Probe the actual generated crossing colliders while paused. This
            // is independent of the acceptance driver's route and steering.
            foreach(var road in session.World.Route.Region.roads)
            {
                if(!road.bridge)continue;
                for(int index=1;index<road.points.Length;index++)
                {
                    var point=road.points[index];
                    if(Vector3.Distance(point,session.Car.Body.position)>180)continue;
                    var direction=Vector3.ProjectOnPlane(point-road.points[index-1],Vector3.up).normalized;
                    var side=Vector3.Cross(Vector3.up,direction);
                    foreach(float offset in new[]{-.9f,0,.9f})
                    {
                        var probe=point+side*offset;
                        // This probe audits published physical meshes. Resident
                        // numeric halo roads outside published chunks are counted
                        // separately, rather than misreported as collider holes.
                        if(!session.World.AutomaticWorld.Renderer.HasGeometryAt(probe)){unpublishedBridgeSamples++;continue;}
                        bool supported=false;
                        foreach(var hit in Physics.RaycastAll(probe+Vector3.up*1.3f,Vector3.down,1.5f))
                        {
                            if(!hit.collider.transform.IsChildOf(session.World.AutomaticWorld.transform))continue;
                            if(Mathf.Abs(hit.point.y-probe.y)<.12f)supported=true;
                            Require(hit.point.y<probe.y+.3f,"Obstruction on bridge "+road.id+" at "+probe);
                        }
                        Require(supported,"Missing physical bridge support "+road.id+" at "+probe);
                        bridgeChecks++;
                    }
                }
            }
            junctionGeometry=session.World.AutomaticWorld.Renderer.InspectJunctionGeometry(session.Car.Body.position,120);
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"junction-geometry.json"),JsonUtility.ToJson(junctionGeometry,true));
            Require(junctionGeometry.roadColliderSamples>0,"No physical road samples");
            Require(junctionGeometry.failures.Count==0,"Junction geometry: "+string.Join("; ",junctionGeometry.failures));
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaGeometrySmoke")>=0)
            {yield return CheckGeometryReuse(session);geometryReusePassed=true;}
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "menu.png"));
            yield return new WaitForEndOfFrame();
            var interfaceView=FindFirstObjectByType<DrivingUI>();
            began=Time.realtimeSinceStartupAsDouble;
            while(interfaceView && !interfaceView.MinimapReady && Time.realtimeSinceStartupAsDouble-began<20)yield return null;
            session.ResumeDrive();
            began = Time.realtimeSinceStartupAsDouble;
            while (session.Menu || session.PreparingStart)
            {
                Require(Time.realtimeSinceStartupAsDouble - began < 330, "Free Drive did not enter normal play");
                yield return null;
            }

            keyboard = InputSystem.AddDevice<Keyboard>("Isolated beta keyboard verification");
            keyboard.MakeCurrent();
            var initial = session.Car.Body.position;
            var initialRotation=session.Car.Body.rotation;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            began = Time.realtimeSinceStartupAsDouble;
            bool throttleSeen = false;
            while (Time.realtimeSinceStartupAsDouble - began < 1.2)
            {
                throttleSeen |= session.Input.Throttle > .8f;
                yield return null;
            }
            keyboardPassed = throttleSeen && Vector3.Distance(initial, session.Car.Body.position) > .2f;
            Require(keyboardPassed, "W key did not move the normal human-controlled car");
            int camera = session.View.Mode;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C));
            yield return null; yield return null;
            cameraPassed = session.View.Mode != camera;
            Require(cameraPassed, "Camera key did not reach the normal camera");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            session.View.Mode = 0;
            InputSystem.RemoveDevice(keyboard); keyboard = null;
            session.Input.enabled = false;
            // The human spawn may face either legal direction. Align only this
            // scripted traversal with its guide after proving real keyboard input.
            bool international=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaInternational")>=0;
            float guideStart=ClosestNear(session.World.Route,initial,0,100);
            float alignedStart=SupportedGuideStart(session.World.Route,guideStart);
            guideStartAdjustment=alignedStart-guideStart;guideStart=alignedStart;
            initial=session.World.Route.At(guideStart,session.World.Route.LaneOffset(guideStart));
            initialRotation=session.World.Route.Rotation(guideStart);
            session.Car.Recover(initial,initialRotation);
            Require(session.Car.TryInitializeSupportedRest(initial,initialRotation,out var restError,out _),"Keyboard check rest reset: "+restError);

            float start = ClosestNear(session.World.Route,session.Car.Body.position,guideStart,30);
            float targetMetres=international?1200:180;var arguments=Environment.GetCommandLineArgs();int metresArgument=Array.IndexOf(arguments,"-earthUnscriptedBetaDriveMetres");if(metresArgument>=0&&metresArgument+1<arguments.Length&&float.TryParse(arguments[metresArgument+1],out float requestedMetres))targetMetres=Mathf.Clamp(requestedMetres,500,3000);
            float proofSpeed=6;int speedArgument=Array.IndexOf(arguments,"-earthUnscriptedBetaDriveSpeedMph");
            if(speedArgument>=0&&speedArgument+1<arguments.Length&&float.TryParse(arguments[speedArgument+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float requestedSpeed))proofSpeed=Mathf.Clamp(requestedSpeed,5,100)*.44704f;
            float end = Mathf.Min(start + targetMetres, session.World.Route.Length - 20);
            if(international)Require(session.World.Route.Region.countryCode=="NL"&&end-start>=480,"International traversal requires a complete Dutch driving route");
            if(international&&targetMetres>500)foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);
            if(international){var ui=FindFirstObjectByType<DrivingUI>();typeof(DrivingNavigation).GetProperty("Current").SetValue(ui.Navigation,new NavigationRoute(session.World.Route.Points));session.World.AutomaticWorld.Renderer.SetPlannedRoute(ui.Navigation.Current.points);}
            began = Time.realtimeSinceStartupAsDouble;
            double nextProgress = 0, previousWall=began, previousFixed=Time.fixedTimeAsDouble;
            double simulationBegan=previousFixed;
            var previousPosition=session.Car.Body.position;float previousProgress=start;
            while (true)
            {
                double now=Time.realtimeSinceStartupAsDouble, fixedNow=Time.fixedTimeAsDouble;
                physicalDistance+=Vector3.Distance(previousPosition,session.Car.Body.position);
                speedIntegral+=session.Car.Body.linearVelocity.magnitude*(float)(fixedNow-previousFixed);
                float frame=(float)((now-previousWall)*1000);worstFrame=Mathf.Max(worstFrame,frame);if(frame>100)slowFrames++;
                drivingWall=now-began;drivingSimulation=fixedNow-simulationBegan;
                previousPosition=session.Car.Body.position;previousFixed=fixedNow;previousWall=now;
                float progress = ClosestNear(session.World.Route,session.Car.Body.position,previousProgress,Mathf.Max(25,session.Car.AbsoluteSpeed*.2f+10));
                previousProgress=progress;
                drivenMetres = progress - start;
                if (international?physicalDistance>=targetMetres&&drivenMetres>=targetMetres-20:progress>=end) break;
                Require(Time.realtimeSinceStartupAsDouble - began < 420, "Human-input vehicle traversal stalled");
                Require(session.Car.Damage < .1f, "Beta vehicle hit generated geometry");
                float lookAhead=Mathf.Max(12,session.Car.AbsoluteSpeed*.8f+6);
                var aim = session.World.Route.At(progress + lookAhead, session.World.Route.LaneOffset(progress));
                var local = session.Car.transform.InverseTransformPoint(aim);
                diagnosticProgress=progress;diagnosticLookAhead=lookAhead;diagnosticLane=session.World.Route.LaneOffset(progress);diagnosticAim=aim;diagnosticAimLocal=local;
                float angle = Mathf.Atan2(2 * session.Car.Metrics.Wheelbase * local.x, Mathf.Max(1, local.x * local.x + local.z * local.z)) * Mathf.Rad2Deg;
                session.Input.Steer = Mathf.Clamp(angle / Mathf.Lerp(30, 10, Mathf.Clamp01(session.Car.AbsoluteSpeed / 48)), -1, 1);
                float targetSpeed = Mathf.Min(proofSpeed, session.Limit / 2.2369363f);
                foreach(float ahead in new[]{16f,32f,64f})
                {
                    float bend=Vector3.Angle(session.World.Route.Forward(progress+2),session.World.Route.Forward(progress+ahead))*Mathf.Deg2Rad;
                    if(bend>.01f)targetSpeed=Mathf.Min(targetSpeed,Mathf.Sqrt(2.8f*ahead/bend));
                }
                if(international&&Vector3.Angle(session.World.Route.Forward(progress+3),session.World.Route.Forward(progress+16))>30)targetSpeed=Mathf.Min(targetSpeed,3);
                int aheadCars=0,opposingCars=0;peakDrivingMph=Mathf.Max(peakDrivingMph,session.Car.AbsoluteSpeed*2.23694f);
                foreach (var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
                {
                    var relative = session.Car.transform.InverseTransformPoint(traffic.PhysicsPosition);
                    if(!traffic.Police&&!traffic.ManagedPolice&&relative.z>25&&relative.z<900){aheadCars++;if(Vector3.Dot(traffic.transform.forward,session.Car.transform.forward)<-.5f)opposingCars++;}
                    if (relative.z > 0 && relative.z < 25 && Mathf.Abs(relative.x) < 1.9f && Mathf.Abs(relative.y) < 2)
                        targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0, (relative.z - 8) / 1.8f));
                }
                if(session.Car.AbsoluteSpeed*2.23694f>=55){fastTrafficFrames++;if(aheadCars>0)fastAheadTrafficFrames++;if(opposingCars>0)fastOpposingFrames++;}
                diagnosticTargetSpeed=targetSpeed;
                session.Input.Throttle = session.Car.AbsoluteSpeed < targetSpeed ? .4f : 0;
                session.Input.Brake = session.Car.AbsoluteSpeed > targetSpeed + .5f ? .4f : 0;
                stuckSeconds=session.Input.Throttle>.2f&&session.Car.AbsoluteSpeed<.25f&&!session.Car.StreamingBrake&&!session.Car.ParkingBrakeApplied?stuckSeconds+Time.unscaledDeltaTime:0;
                if(stuckSeconds>20){File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"stalled-diagnostics.json"),DriverDiagnostic().ToString());throw new IOException("QA physical driver stalled under throttle; inspect stalled-diagnostics.json");}
                if (!session.Car.Grounded && Time.realtimeSinceStartupAsDouble - began > 2) airborneSeconds += Time.unscaledDeltaTime;
                if (Time.realtimeSinceStartupAsDouble >= nextProgress)
                { nextProgress = Time.realtimeSinceStartupAsDouble + .5; Progress("driving", drivenMetres.ToString("F1") + " metres"); }
                yield return null;
            }
            session.Input.Throttle = 0; session.Input.Brake = 1; session.Input.Steer = 0;
            session.Input.enabled = true;
            Require(!session.Car.NetworkDriven && airborneSeconds < .1f, "Normal vehicle support failed");
            session.Network.Host(); session.Network.Join("127.0.0.1");
            lanRefused = !session.Network.Active && !session.Network.Hosting;
            Require(lanRefused, "Single-player beta opened a LAN session");
            Require(session.TrySave(), "Beta autosave failed");
            careerPassed = session.SaveCareer(0, "Isolated beta verification") && CareerStorage.Read(0) != null;
            Require(careerPassed && CareerStorage.Folder.StartsWith(SinglePlayerBeta.DataRoot, StringComparison.Ordinal), "Beta career isolation failed");
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput, "driving.png"));
            yield return new WaitForEndOfFrame();
            Finish(errors.Count == 0, "");
        }

        internal static IEnumerator CheckGeometryReuse(GameSession session)
        {
            if(SinglePlayerBeta.VerificationOutput==null)throw new InvalidOperationException("Explicit isolated verification required");

                var renderer=session.World.AutomaticWorld.Renderer;
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                object Get(string n)=>renderer.GetType().GetField(n,flags).GetValue(renderer);
                object Call(string n,params object[] args)=>renderer.GetType().GetMethod(n,flags).Invoke(renderer,args);
                double began=Time.realtimeSinceStartupAsDouble;
                while((bool)Get("building")){Require(Time.realtimeSinceStartupAsDouble-began<60,"Geometry pass did not yield");yield return null;}
                var active=(System.Collections.IDictionary)Get("chunks");var p=session.Car.Body.position;var key=new Vector2Int(Mathf.FloorToInt(p.x/64),Mathf.FloorToInt(p.z/64));
                var root=(GameObject)active[key];Require(root,"Car geometry is absent");var mesh=root.GetComponentInChildren<MeshFilter>().sharedMesh;
                Call("RetainGeometry",key,root);active.Remove(key);Require(!root.activeSelf,"Warm collision remained active");
                Require((bool)Call("RestoreGeometry",key)&&root.activeSelf&&ReferenceEquals(active[key],root)&&ReferenceEquals(root.GetComponentInChildren<MeshFilter>().sharedMesh,mesh),"Native cached mesh/collider reuse failed");
                Physics.SyncTransforms();Require(Physics.Raycast(p+Vector3.up,Vector3.down,10,~(1<<8),QueryTriggerInteraction.Ignore),"Restored native chunk lost road support");
                var weights=(System.Collections.IDictionary)Get("geometryBytes");
                var over=new Vector2Int(900000,900000);var fake=new GameObject("Explicit QA over-budget warm entry");fake.transform.SetParent(renderer.transform,false);weights[over]=129L*1024*1024;
                Call("RetainGeometry",over,fake);Require(renderer.WarmGeometryChunks==0&&renderer.WarmGeometryEstimatedBytes==0,"Warm memory cap failed");
                for(int i=0;i<257;i++){var k=new Vector2Int(900000+i,900001);var g=new GameObject("Explicit QA bounded warm entry");g.transform.SetParent(renderer.transform,false);Call("RetainGeometry",k,g);}
                Require(renderer.WarmGeometryChunks==256&&renderer.WarmGeometryEstimatedBytes<=128L*1024*1024,"Warm chunk count cap failed");
                var warm=(System.Collections.IDictionary)Get("warmChunks");var keys=new List<Vector2Int>();foreach(Vector2Int k in warm.Keys)keys.Add(k);foreach(var k in keys)Call("RemoveWarmGeometry",k,true);
                bool reusePassed=renderer.ReusedGeometryChunks>0;
                File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"geometry-reuse.json"),renderer.GeometryDiagnosticsJson());
                Require(reusePassed,"No native geometry reuse was recorded");
                    }

        void Progress(string stage, string status)
        {
            JObject snapshot=stage=="driving"?DriverDiagnostic():null;
            if(snapshot!=null){snapshot["realtimeSeconds"]=Time.realtimeSinceStartupAsDouble;driverHistory.Add(snapshot);if(driverHistory.Count>64)driverHistory.RemoveAt(0);}
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "progress.json"),
                new JObject { ["stage"] = stage, ["status"] = status,["driver"]=snapshot }.ToString());
        }

        static JArray XYZ(Vector3 vector)=>new JArray(vector.x,vector.y,vector.z);
        JObject DriverDiagnostic()
        {
            if(!session||!session.Car)return new JObject();
            var car=session.Car;var contacts=new JArray();
            for(int wheel=0;wheel<4;wheel++)contacts.Add(new JObject{["grounded"]=car.WheelGrounded[wheel],["surface"]=car.ContactSurfaceNames[wheel],["point"]=XYZ(car.ContactPoints[wheel]),["normal"]=XYZ(car.ContactNormals[wheel]),["slip"]=car.WheelSlip[wheel]});
            var nearby=new JArray();foreach(var collider in Physics.OverlapSphere(car.Body.position,5,~(1<<8),QueryTriggerInteraction.Ignore))if(nearby.Count<30)nearby.Add(new JObject{["name"]=collider.name,["aabbClosest"]=XYZ(collider.bounds.ClosestPoint(car.Body.position))});
            return new JObject{["progress"]=diagnosticProgress,["routeLength"]=session.World.Route.Length,["routeForward"]=XYZ(session.World.Route.PlanarForward(diagnosticProgress)),["bodyPosition"]=XYZ(car.Body.position),["bodyEuler"]=XYZ(car.Body.rotation.eulerAngles),["bodyForward"]=XYZ(car.transform.forward),["velocity"]=XYZ(car.Body.linearVelocity),["angularVelocity"]=XYZ(car.Body.angularVelocity),["steerInput"]=session.Input.Steer,["wheelSteeringDegrees"]=car.Steering,["throttle"]=session.Input.Throttle,["brake"]=session.Input.Brake,["lookAhead"]=diagnosticLookAhead,["laneOffset"]=diagnosticLane,["aim"]=XYZ(diagnosticAim),["aimLocal"]=XYZ(diagnosticAimLocal),["targetMps"]=diagnosticTargetSpeed,["road"]=session.CurrentRoad?.id,["roadSource"]=session.CurrentRoad?.NativeSourceWay,["lastImpact"]=car.LastImpactObject,["collisionContactEvents"]=JArray.FromObject(car.DiagnosticContactEvents),["contacts"]=contacts,["nearbyColliders"]=nearby};
        }

        // The worker acquisition guide may prepend a short lateral connector
        // from an arbitrary saved pose to its first real road vertex. It is not
        // a source road and must not define the acceptance driver's heading.
        // Align once after the keyboard proof, on the following continuous road;
        // no pose, collision or station correction occurs during traversal.
        public static float SupportedGuideStart(RoutePath route,float nearest)
        {
            if(route.Points.Length<3)return nearest;
            float first=route.DistanceAtPoint(1),second=route.DistanceAtPoint(2)-first;
            var acquisition=Vector3.ProjectOnPlane(route.Points[1]-route.Points[0],Vector3.up);
            var road=Vector3.ProjectOnPlane(route.Points[2]-route.Points[1],Vector3.up);
            if(first<12&&second>.1f&&Vector3.Angle(acquisition,road)>45&&nearest<first+3)
                return Mathf.Min(route.Length-3,first+4);
            return nearest;
        }

        // Test-driver stationing is continuous across route loops. A nearest
        // point elsewhere on the guide cannot count as physical travel.
        public static float ClosestNear(RoutePath route,Vector3 point,float previous,float ahead)
        {
            float low=Mathf.Max(0,previous-8),high=Mathf.Min(route.Length,previous+ahead),best=float.PositiveInfinity,result=previous;
            for(int i=1;i<route.Points.Length;i++)
            {
                float a=route.DistanceAtPoint(i-1),b=route.DistanceAtPoint(i);if(b<low||a>high||b-a<.0001f)continue;
                var delta=route.Points[i]-route.Points[i-1];float f=Mathf.Clamp(Vector3.Dot(point-route.Points[i-1],delta)/Mathf.Max(.0001f,delta.sqrMagnitude),Mathf.Clamp01((low-a)/(b-a)),Mathf.Clamp01((high-a)/(b-a)));
                float gap=(point-(route.Points[i-1]+delta*f)).sqrMagnitude;if(gap<best){best=gap;result=Mathf.Lerp(a,b,f);}
            }
            return result;
        }

        static void Require(bool condition, string reason)
        { if (!condition) throw new IOException(reason); }

        void Finish(bool passed, string error)
        {
            if(passed&&Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaInternational")>=0)
                File.WriteAllText(Path.Combine(SinglePlayerBeta.DataRoot,"shared-progress-proof.json"),new JObject{["activeId"]=session.Profile.garage.activeId,["credits"]=session.Economy.Credits}.ToString());
            var art = FindFirstObjectByType<AutomaticScenery>();
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"traffic-census.json"),new JObject{{"peakMph",peakDrivingMph},{"framesAt55MphOrAbove",fastTrafficFrames},{"fastFramesWithTrafficAhead",fastAheadTrafficFrames},{"fastFramesWithOpposingTrafficAhead",fastOpposingFrames}}.ToString());
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput, "result.json"), new JObject
            {
                ["passed"] = passed, ["error"] = error, ["runtimeErrors"] = JArray.FromObject(errors),["driverDiagnostic"]=DriverDiagnostic(),["driverHistory"]=driverHistory,
                ["bundleIdentifier"] = Application.identifier, ["version"] = Application.version,
                ["normalHumanVehicle"] = session && session.Car && !session.Car.NetworkDriven,
                ["keyboardMovedCar"] = keyboardPassed, ["cameraKeyWorked"] = cameraPassed,
                ["nativeGeometryReuseAndBoundsPassed"] = geometryReusePassed,
                ["distanceMetres"] = drivenMetres,
                ["bridgeColliderChecks"]=bridgeChecks,["unpublishedBridgeSamples"]=unpublishedBridgeSamples,["initialGuideAlignmentMetres"]=guideStartAdjustment,["physicalDistanceMetres"]=physicalDistance,["speedIntegralMetres"]=speedIntegral,
                ["drivingWallSeconds"]=drivingWall,["drivingSimulationSeconds"]=drivingSimulation,
                ["worstFrameMilliseconds"]=worstFrame,["framesOver100ms"]=slowFrames, ["airborneSeconds"] = airborneSeconds,
                ["damage"] = session && session.Car ? session.Car.Damage : -1,
                ["lanRefused"] = lanRefused, ["careerSavedAndRead"] = careerPassed,
                ["profilePath"] = PlayerProfile.StoragePath, ["careerFolder"] = CareerStorage.Folder,
                ["dataRoot"] = SinglePlayerBeta.DataRoot, ["persistentDataPath"] = Application.persistentDataPath,
                ["treeRecords"] = art ? art.AvailableTrees : 0
            }.ToString());
            Application.Quit(passed ? 0 : 3);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= CaptureError;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        }
    }
}
