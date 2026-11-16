using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    // Explicit QA identity and disposable profile; exercises the normal session,
    // actor transaction, actual VehicleController and ordinary driving camera.
    public sealed class AutomaticWorldNativeQa : MonoBehaviour
    {
        static string output, initialError;
        static readonly List<string> errors = new List<string>();
        GameSession activeSession;
        float drivenMetres, airborneTime;
        JObject relocation;
        [Serializable] sealed class PerformanceFrame
        {
            public float wallMilliseconds;public bool focused;
            public double cpuMilliseconds,gpuMilliseconds,cpuMainThreadMilliseconds,cpuRenderThreadMilliseconds,presentWaitMilliseconds;public ulong engineTimestamp;
        }
        readonly List<PerformanceFrame> performance=new List<PerformanceFrame>();
        readonly FrameTiming[] engineTimings=new FrameTiming[1];
        ulong lastEngineTimestamp;
        bool benchmark;
        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Prepare()
        {
            if (Application.identifier != "com.earthunscripted.qa.automaticworld") return;
            // A QA build without an explicit output/input refuses to launch a
            // career. The production application never enters this branch.
            GameSession.SuppressAutomaticLaunch = true;
            try
            {
                output = Argument("-automaticWorldQA");
                string regionPath = Argument("-automaticWorldRegion");
                if (output == null || regionPath == null || Directory.Exists(output)) throw new IOException("Fresh QA output and normal-map fixture required");
                Directory.CreateDirectory(output);
                PlayerProfile.StorageOverride = Path.Combine(output, "profile.json");
                RegionCache.OverrideFolder = Path.Combine(output, "Regions");
                Directory.CreateDirectory(RegionCache.OverrideFolder);
                string text = File.ReadAllText(regionPath);
                var region = JsonUtility.FromJson<GeoRegion>(text);
                if (!AutomaticWorldHost.Eligible(region) || !RegionCache.Safe(region.id)) throw new IOException("QA fixture must select an ordinary live area");
                File.WriteAllText(RegionCache.PathFor(region.id), text);
                var launchArgs=Environment.GetCommandLineArgs();int qualityAt=Array.IndexOf(launchArgs,"-automaticWorldQuality");
                int qaQuality=qualityAt>=0 && qualityAt+1<launchArgs.Length ? int.Parse(launchArgs[qualityAt+1]) : 2;
                if(qaQuality<0 || qaQuality>2)throw new IOException("QA quality must be 0, 1 or 2");
                var profile = new PlayerProfile { region = region.id, regionTimeZone = region.timeZone,
                    quality = qaQuality, volume = 0, rumble = 0, credits = 4321, livingScenery = true,
                    weatherMode = 0, clockMode = 0, fixedHour = 14,
                    garage = new GarageState { activeId = "automatic-world-qa", cars = new List<OwnedCar> {
                        new OwnedCar { id = "automatic-world-qa", model = 0, paint = 3 } } } };
                File.WriteAllText(PlayerProfile.StorageOverride, JsonUtility.ToJson(profile, true));
                BootTelemetry.EnableIsolated(); Application.runInBackground = true;
                Application.logMessageReceived += (message, stack, type) =>
                { if ((type == LogType.Exception || type == LogType.Error) && errors.Count < 100) errors.Add(message + "\n" + stack); };
                GameSession.SuppressAutomaticLaunch = false;
            }
            catch (Exception error) { initialError = error.ToString(); Debug.LogException(error); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        { if (Application.identifier == "com.earthunscripted.qa.automaticworld") new GameObject("Normal automatic world QA").AddComponent<AutomaticWorldNativeQa>(); }
        IEnumerator Start()
        {
            var job = Exercise();
            while (true)
            {
                bool more = false; object current = null; Exception error = null;
                try { more = job.MoveNext(); if (more) current = job.Current; } catch (Exception exception) { error = exception; }
                if (error != null)
                {
                    if(output!=null)ScreenCapture.CaptureScreenshot(Path.Combine(output,"normal-failure.png"));
                    yield return new WaitForEndOfFrame();
                    Finish(false, error.ToString(), activeSession, drivenMetres, airborneTime); yield break;
                }
                if (!more) yield break; yield return current;
            }
        }
        IEnumerator Exercise()
        {
            if (initialError != null) throw new IOException(initialError);
            double began = Time.realtimeSinceStartupAsDouble;
            GameSession session;
            while (!(session = FindAnyObjectByType<GameSession>()) || !session.BootReady)
            {
                if (session && session.BootFailed) throw new IOException(session.BootStatus);
                if (Time.realtimeSinceStartupAsDouble - began > 600) throw new TimeoutException("Normal session startup");
                File.WriteAllText(Path.Combine(output, "progress.json"), new JObject {
                    ["stage"] = "boot", ["status"] = session ? session.BootStatus : "Waiting for session",
                    ["seconds"] = Time.realtimeSinceStartupAsDouble - began }.ToString());
                yield return null;
            }
            if (!session.World.AutomaticWorld || !session.World.Route.Region.AutomaticWorld ||
                !session.World.GeographicWorld.HasSurface(session.Car.Body.position)) throw new IOException("Normal session did not adopt the automatic world");
            activeSession = session;
            benchmark=Array.IndexOf(Environment.GetCommandLineArgs(),"-automaticWorldBenchmark")>=0;
            session.SetMenu(false);
            session.Car.NetworkDriven = true;
            float start = session.World.Route.Closest(session.Car.Body.position, out _, out _);
            float targetEnd = Mathf.Min(session.World.Route.Length - 20, start + 700);
            double driveBegan = Time.realtimeSinceStartupAsDouble, lastProgressAt = driveBegan;
            float best = start, airborne = 0;
            bool interruptWorker = Array.IndexOf(Environment.GetCommandLineArgs(), "-automaticWorldKillWorker") >= 0, interrupted = false;
            var frames = new List<float>();
            double nextProgressWrite=0;
            while (true)
            {
                float progress = session.World.Route.Closest(session.Car.Body.position, out _, out _);
                drivenMetres = progress - start;
                if (interruptWorker && !interrupted && drivenMetres > 100)
                { session.World.AutomaticWorld.InterruptWorkerForQa(); interrupted = true; }
                if (progress > best + 1) { best = progress; lastProgressAt = Time.realtimeSinceStartupAsDouble; }
                if (progress >= targetEnd) break;
                if (session.Car.Damage > 1) throw new IOException("Normal car impact: " + session.Car.Damage);
                if (Time.realtimeSinceStartupAsDouble - driveBegan > 300 ||
                    (!session.Car.StreamingBrake && Time.realtimeSinceStartupAsDouble - lastProgressAt > 30)) throw new TimeoutException("Normal physical drive stopped progressing");
                var aim = session.World.Route.At(progress + Mathf.Clamp(12 + session.Car.AbsoluteSpeed * .75f, 12, 40), session.World.Route.LaneOffset(progress));
                var local = session.Car.transform.InverseTransformPoint(aim);
                float angle = Mathf.Atan2(2 * session.Car.Metrics.Wheelbase * local.x, Mathf.Max(1, local.x * local.x + local.z * local.z)) * Mathf.Rad2Deg;
                session.Car.NetworkSteer = Mathf.Clamp(angle / Mathf.Lerp(30, 10, Mathf.Clamp01(session.Car.AbsoluteSpeed / 48)), -1, 1);
                float targetSpeed = Mathf.Min(11, session.Limit / 2.2369363f);
                for (float ahead = 0; ahead <= 120; ahead += 10)
                {
                    var a = session.World.Route.At(progress + ahead);
                    var b = session.World.Route.At(progress + ahead + 10);
                    var c = session.World.Route.At(progress + ahead + 20);
                    var before = Vector3.ProjectOnPlane(b - a, Vector3.up);
                    var after = Vector3.ProjectOnPlane(c - b, Vector3.up);
                    if (before.sqrMagnitude < 1 || after.sqrMagnitude < 1) continue;
                    float curve = Vector3.Angle(before, after) * Mathf.Deg2Rad / 10;
                    if (curve > .0001f) targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2.2f / curve + 6 * Mathf.Max(0, ahead - 12)));
                }
                foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
                {
                    float trafficProgress=session.World.Route.Closest(traffic.PhysicsPosition,out float trafficLateral,out _);
                    float along=trafficProgress-progress;
                    float laneGap=Mathf.Abs(trafficLateral-session.World.Route.LaneOffset(trafficProgress));
                    float heightGap=Mathf.Abs(traffic.PhysicsPosition.y-session.World.Route.At(trafficProgress).y-.7f);
                    if(along>0 && along<45 && laneGap<1.9f && heightGap<1.5f)
                        targetSpeed=Mathf.Min(targetSpeed,Mathf.Max(0,(along-8)/1.8f));
                }
                session.Car.NetworkThrottle = session.Car.AbsoluteSpeed < targetSpeed ? .5f : 0;
                session.Car.NetworkBrake = session.Car.AbsoluteSpeed > targetSpeed + 1 ? .3f : 0;
                if (!session.Car.Grounded && Time.realtimeSinceStartupAsDouble - driveBegan > 2) airborne += Time.unscaledDeltaTime;
                airborneTime = airborne;
                frames.Add(Time.unscaledDeltaTime * 1000);
                if(benchmark)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    if(Time.realtimeSinceStartupAsDouble-driveBegan>5)
                    {
                        var sample=new PerformanceFrame{wallMilliseconds=Time.unscaledDeltaTime*1000,focused=Application.isFocused};
                        if(FrameTimingManager.GetLatestTimings(1,engineTimings)>0 && engineTimings[0].frameStartTimestamp!=lastEngineTimestamp)
                        {
                            var timing=engineTimings[0];lastEngineTimestamp=timing.frameStartTimestamp;
                            sample.engineTimestamp=lastEngineTimestamp;sample.cpuMilliseconds=timing.cpuFrameTime;sample.gpuMilliseconds=timing.gpuFrameTime;
                            sample.cpuMainThreadMilliseconds=timing.cpuMainThreadFrameTime;sample.cpuRenderThreadMilliseconds=timing.cpuRenderThreadFrameTime;
                            sample.presentWaitMilliseconds=timing.cpuMainThreadPresentWaitTime;
                        }
                        performance.Add(sample);
                    }
                }
                if(Time.realtimeSinceStartupAsDouble>=nextProgressWrite)
                {
                    nextProgressWrite=Time.realtimeSinceStartupAsDouble+.5;
                    File.WriteAllText(Path.Combine(output, "progress.json"), new JObject {
                    ["stage"] = "drive", ["metres"] = progress - start, ["speed"] = session.Car.AbsoluteSpeed,
                    ["damage"] = session.Car.Damage, ["holdingForData"] = session.Car.StreamingBrake }.ToString());
                }
                yield return null;
            }
            session.Car.NetworkThrottle = 0; session.Car.NetworkBrake = 1;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-automaticWorldRelocationCheck")>=0)
            {
                // Advance explicitly so the outer owned QA runner catches an
                // assertion from this nested iterator and writes its result.
                var checks=CheckRelocation(session);
                while(checks.MoveNext())yield return checks.Current;
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "normal-driving.png"));
            yield return new WaitForEndOfFrame();
            var geometry=JObject.Parse(session.World.AutomaticWorld.Renderer.GeometryDiagnosticsJson());
            bool staticClear=true;
            foreach(string key in new[]{"buriedRailColliderSamples","buriedPathColliderSamples","buriedRoadColliderSamples",
                "blockedRoadCorridorSamples","blockedBridgeCorridorSamples","obstructedTunnelSamples","obstructedTunnelTerrainSamples"})
                if((int?)geometry[key]>0)staticClear=false;
            int focused=0;foreach(var sample in performance)if(sample.focused)focused++;
            bool benchmarkValid=!benchmark || performance.Count>=300 && focused>=performance.Count*.95f;
            Finish(errors.Count == 0 && airborne < .1f && staticClear && benchmarkValid && (!interruptWorker || session.World.AutomaticWorld.WorkerRestarts == 1), "", session, best - start, airborne, frames);
        }
        static void Finish(bool passed, string error, GameSession session, float distance, float airborne, List<float> frames = null)
        {
            if (output != null && Directory.Exists(output))
            {
                var report = new JObject { ["passed"] = passed, ["scope"] = "Actual main GameSession and normal car/camera; isolated QA profile",
                    ["error"] = error, ["runtimeErrors"] = JArray.FromObject(errors), ["distanceMetres"] = distance,
                    ["airborneSeconds"] = airborne, ["damage"] = session ? session.Car.Damage : -1,
                    ["lastImpactObject"] = session ? session.Car.LastImpactObject : "",
                    ["workerRestarts"] = session && session.World.AutomaticWorld ? session.World.AutomaticWorld.WorkerRestarts : 0,
                    ["nativeGeometry"] = session && session.World.AutomaticWorld ? JObject.Parse(session.World.AutomaticWorld.Renderer.GeometryDiagnosticsJson()) : null,
                    ["traffic"] = TrafficSnapshot(),
                    ["scenery"] = ScenerySnapshot(),
                    ["relocation"] = FindFirstObjectByType<AutomaticWorldNativeQa>()?.relocation,
                    ["performance"] = FindFirstObjectByType<AutomaticWorldNativeQa>()?.PerformanceJson(),
                    ["position"] = session ? JToken.FromObject(new { session.Car.Body.position.x, session.Car.Body.position.y, session.Car.Body.position.z }) : null,
                    ["automaticBackend"] = session && session.World && session.World.AutomaticWorld,
                    ["framesMilliseconds"] = JArray.FromObject(frames ?? new List<float>()) };
                File.WriteAllText(Path.Combine(output, "result.json"), report.ToString());
            }
            Application.Quit(passed ? 0 : 3);
        }
        static JArray TrafficSnapshot()
        {
            var rows=new JArray();
            foreach(var car in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
            {
                var p=car.PhysicsPosition;var f=car.transform.forward;var road=car.RoadForDiagnostics;
                rows.Add(new JObject{["name"]=car.name,["x"]=p.x,["y"]=p.y,["z"]=p.z,
                    ["forwardX"]=f.x,["forwardZ"]=f.z,["speed"]=car.Speed,["direction"]=car.Direction,
                    ["waitingForContinuation"]=car.StoppedForDiagnostics,["sourceWay"]=road?.NativeSourceWay,
                    ["roadStart"]=road?.start,["roadEnd"]=road?.end});
            }
            return rows;
        }
        static JToken ScenerySnapshot()
        {
            var scenery=FindFirstObjectByType<AutomaticScenery>();
            var buildings=FindFirstObjectByType<AutomaticBuildingDetails>();
            return scenery?JToken.FromObject(new {scenery.AvailableTrees,scenery.VisibleTrees,scenery.DetailedTrees,scenery.Quality,
                VisibleBuildingOpenings=buildings?buildings.VisibleOpenings:0,
                scenery.DrawDistanceMetres,scenery.AddedWhileDriving,
                MinimumArrivalDistanceMetres=float.IsPositiveInfinity(scenery.MinimumArrivalDistanceMetres)?(float?)null:scenery.MinimumArrivalDistanceMetres}):null;
        }
        JToken PerformanceJson()
        {
            if(!benchmark)return null;
            var pipeline=QualitySettings.renderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if(!pipeline)pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            return new JObject{["scope"]="Normal driving; first5seconds/screenshots excluded; geometry diagnostic census disabled; focused fraction required>=95%. Wall frame intervals are not presented FPS. Nonzero distinct engine timestamps identify CPU/GPU samples; zero timing values are unavailable.",
                ["width"]=Screen.width,["height"]=Screen.height,["quality"]=activeSession.Profile.quality,
                ["graphicsDevice"]=SystemInfo.graphicsDeviceName,["graphicsAPI"]=SystemInfo.graphicsDeviceType.ToString(),
                ["targetFrameRate"]=Application.targetFrameRate,["vSync"]=QualitySettings.vSyncCount,
                ["renderScale"]=pipeline?pipeline.renderScale:1,["msaaSamples"]=pipeline?pipeline.msaaSampleCount:1,
                ["shadowDistanceMetres"]=pipeline?pipeline.shadowDistance:QualitySettings.shadowDistance,
                ["renderInterval"]=UnityEngine.Rendering.OnDemandRendering.renderFrameInterval,
                ["displayRefreshHz"]=Screen.currentResolution.refreshRateRatio.value,
                ["geometryDiagnosticsEnabled"]=false,["frames"]=JArray.FromObject(performance)};
        }
        IEnumerator CheckRelocation(GameSession session)
        {
            double began=Time.realtimeSinceStartupAsDouble;
            while(session.Car.AbsoluteSpeed>.2f && Time.realtimeSinceStartupAsDouble-began<12)yield return null;
            var before=session.Car.Body.position;
            float damage=session.Car.Damage;
            int credits=session.Economy.Credits;
            session.Recover();
            while(session.PreparingStart && Time.realtimeSinceStartupAsDouble-began<330)yield return null;
            if(session.PreparingStart || session.Car.Body.isKinematic || Vector3.Distance(before,session.Car.Body.position)>10)
                throw new IOException("Native supported-level recovery failed");
            yield return new WaitForSecondsRealtime(.5f);
            var cancelPosition=session.Car.Body.position;
            bool geometryBefore=session.World.AutomaticWorld.Renderer.HasGeometryAt(session.World.Garage.position);
            session.World.AutomaticWorld.DelayNextRelocationForQa(5);
            session.TowToGarage();session.CancelPreparingStart();
            yield return null;yield return null;
            if(session.PreparingStart || session.Car.Body.isKinematic || Vector3.Distance(cancelPosition,session.Car.Body.position)>.2f)
                throw new IOException("Cancelled native tow changed the car or left it frozen");
            int retired=session.World.AutomaticWorld.Renderer.RetireGeometryForQa(session.World.Garage.position);
            bool absentBeforeTow=!session.World.AutomaticWorld.Renderer.HasGeometryAt(session.World.Garage.position);
            session.TowToGarage();began=Time.realtimeSinceStartupAsDouble;
            while(session.PreparingStart && Time.realtimeSinceStartupAsDouble-began<330)yield return null;
            if(session.PreparingStart || session.Car.Body.isKinematic ||
                !session.World.AutomaticWorld.Renderer.HasGeometryAt(session.World.Garage.position))
                throw new IOException("Native tow did not prepare its target geometry");
            session.SetMenu(false);yield return new WaitForSecondsRealtime(1);
            float separation=Vector3.Distance(session.Car.Body.position,session.World.Garage.position);
            if(separation>3 || session.Car.Damage!=damage || session.Economy.Credits!=credits || !session.Car.Grounded)
                throw new IOException("Native tow did not retain a supported, undamaged car and its credits");
            relocation=new JObject{["passed"]=true,["supportedLevelRecovery"]=true,["cancellationRestoredBody"]=true,
                ["serviceGeometryResidentBefore"]=geometryBefore,["serviceSeparationMetres"]=separation,
                ["cancelPendingReadinessFixtureSeconds"]=5,["explicitlyRetiredChunks"]=retired,["serviceGeometryAbsentBeforeTow"]=absentBeforeTow,
                ["towSeconds"]=Time.realtimeSinceStartupAsDouble-began,["damageRetained"]=true,["creditsRetained"]=true};
        }
    }
}
