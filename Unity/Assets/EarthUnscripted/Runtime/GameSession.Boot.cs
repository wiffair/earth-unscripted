using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        public bool BootReady {get;private set;}
        public bool BootFailed {get;private set;}
        string bootStatus="Preparing your world…";
        AutomaticWorldHost bootAutomaticHost;
        public string BootStatus {get=>!BootFailed&&bootAutomaticHost&&!bootAutomaticHost.Ready?bootAutomaticHost.Status:bootStatus;private set=>bootStatus=value;}
        public string BootSourceSha {get;private set;}="";
        BootTelemetry.Trace bootTiming;
        OwnedWorldPreparation bootWork;int bootGeneration;double bootBegan;
        sealed class BootSource
        {
            public string Text,Path,Id,RulesText,PhysicalLayout;public long Length,Modified;public PackCircuitSources.Captured Circuit;public Core.RacingRuleSet Rules;
            public static BootSource Capture(string id)
            {
                var result=new BootSource{Id=id};var rules=Resources.Load<TextAsset>("RacingRules/base");result.RulesText=rules?rules.text:null;if(string.IsNullOrEmpty(id))return result;
                if(Packs.WorldPackCircuitReference.IsToken(id)){result.Circuit=PackCircuitSources.Capture(id);return result;}
                var asset=Resources.Load<TextAsset>("Geo/"+id);
                if(asset){result.Text=asset.text;return result;}
                result.Path=RegionCache.PathFor(id);var info=new FileInfo(result.Path);
                if(!info.Exists||info.Length>EarthUnscripted.Core.DataPackageRules.MaximumFileBytes)throw new InvalidDataException("Saved area is missing or exceeds its byte limit");
                result.Length=info.Length;result.Modified=info.LastWriteTimeUtc.Ticks;return result;
            }
            public bool Unchanged()
            {if(Path==null)return true;var info=new FileInfo(Path);return info.Exists&&info.Length==Length&&info.LastWriteTimeUtc.Ticks==Modified;}
            public string Read(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();if(Circuit!=null)return Circuit.Read(token);if(Path==null)return Text;
                if(!Unchanged())throw new IOException("Saved area changed during preparation");
                string value=File.ReadAllText(Path);if(!Unchanged())throw new IOException("Saved area changed while reading");return value;
            }
        }
        static string BootHash(string value)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value??""))).Replace("-","").ToLowerInvariant();}
        void BeginBoot(string regionId,string[] args)
        {
            if(Profile!=null&&Profile.LoadBlocked){FailBoot(new InvalidOperationException(Profile.LoadIssue));return;}
            RevokeActualVehicleRoster();ImportedCircuit=null;hasAutomaticSafeRoad=false;
            BootReady=false;BootFailed=false;Time.timeScale=0;bootBegan=Time.realtimeSinceStartupAsDouble;
            int request=++bootGeneration;
            BeginBootClearingCamera(request);
            try { if (TryBeginScopedSession800(args, request)) return; }
            catch (Exception error) { FailBoot(error); return; }
            BootSource source;bootTiming?.SelectRegion(regionId);
            try{using(BootTelemetry.Enter(bootTiming,"main.source.capture"))source=BootSource.Capture(regionId);}catch(Exception e){FailBoot(e);return;}
            string sourceHash="";
            double queued=bootTiming==null?0:BootTelemetry.Now;
            bootWork=new OwnedWorldPreparation(token=>
            {
                bootTiming?.Wall("worker.queue",queued);using var workerTiming=BootTelemetry.Enter(bootTiming,"worker.total");
                string text;using(BootTelemetry.Measure("worker.source.read"))text=source.Read(token);using(BootTelemetry.Measure("worker.source.hash"))sourceHash=BootHash(text);GeoRegion region=null;
                if(text!=null)
                {
                    if(System.Text.Encoding.UTF8.GetByteCount(text)>EarthUnscripted.Core.DataPackageRules.MaximumFileBytes)throw new InvalidDataException("World byte budget");
                    // Raw versus authoritative classification and bounded semantic
                    // preflight happen before normalization or spatial indexing.
                    using(BootTelemetry.Measure("worker.source.bounded-decode"))region=WorldSnapshotCodec.DecodeBootSource(text,out _);
                    source.Circuit?.BindOwned(region,token);
                    if(region.circuit){if(source.RulesText==null)throw new InvalidDataException("Racing rules are missing. Reinstall this game build.");source.Rules=source.Circuit?.Context?.Event?.Rules??Packs.RacingRuleSetJson.Parse(System.Text.Encoding.UTF8.GetBytes(source.RulesText));}
                }
                token.ThrowIfCancellationRequested();
                // Current bounded handoff supports raw bundled/cache source and
                // already-finalized schema1/signed-peer caches. Future bundle phase
                // and exact input pins replace this narrow phase discriminator.
                if(AutomaticWorldHost.Eligible(region))
                    return new PreparedWorldData(region,null,0,Vector3.zero);
                PreparedWorldData prepared;
                if(region!=null&&(region.roadWidthSchema==1||region.id.StartsWith("download-peer-",StringComparison.Ordinal)))
                    using(BootTelemetry.Measure("worker.finalizer.authoritative"))prepared=WorldDataFinalizer.AttachAuthoritative(region,token,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                else using(BootTelemetry.Measure("worker.finalizer.raw"))prepared=WorldDataFinalizer.PrepareOwned(region,null,token,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                using(BootTelemetry.Measure("worker.circuit.physical-identity"))source.PhysicalLayout=PhysicalCircuitIdentity.Compute(prepared,token);
                return prepared;
            });
            StartCoroutine(GuardBoot(Boot(request,regionId,args,source,()=>sourceHash)));
        }
        IEnumerator Boot(int request,string regionId,string[] args,BootSource source,Func<string> hash)
        {
            var graphicsWarmup=GetComponent<DrivingGraphicsWarmup>()??gameObject.AddComponent<DrivingGraphicsWarmup>();
            yield return graphicsWarmup.Prepare();
            var work=bootWork;
            while(!work.Completed)
            {if(request!=bootGeneration)yield break;if(Time.realtimeSinceStartupAsDouble-bootBegan>300)throw new TimeoutException("World preparation timed out");yield return null;}
            if(request!=bootGeneration)yield break;
            if(work.Error!=null)throw work.Error;
            if(!work.TryTake(out var data))throw new OperationCanceledException("World preparation cancelled");
            if(!source.Unchanged())throw new IOException("Saved area changed before activation");
            // Complete source-policy/input-pin and immutable bundle receipt guard
            // belongs here when the regional transaction is connected. No Ready,
            // profile save or scene request is published by this worker.
            PhysicalLayoutIdentity=source.PhysicalLayout;ImportedCircuit=source.Circuit?.Context;RacingRules=source.Rules??Core.RacingRuleSet.LegacyDefaults;BootSourceSha=hash();BootStatus="Creating your world…";yield return null;
            World=OwnBootRoot(new GameObject("Prepared driving world")).AddComponent<WorldBuilder>();
            if(AutomaticWorldHost.Eligible(data.Region))
            {
                var sourceRegion=data.Region;
                var points=sourceRegion.route;
                if(points==null||points.Length<2)throw new InvalidDataException("Live area has no starting road");
                Vector3 initial=points[0],forward=points[1]-points[0];
                if(Profile.customStart&&Profile.startRegion==regionId)
                {
                    initial=new Vector3(Profile.startX,0,Profile.startZ);
                    float best=20*20;var requestedPosition=initial;
                    foreach(var road in sourceRegion.roads)
                        for(int i=1;i<road.points.Length;i++)
                        {
                            var a=road.points[i-1];var delta=road.points[i]-a;float squared=delta.x*delta.x+delta.z*delta.z;
                            float t=Mathf.Clamp01(((requestedPosition.x-a.x)*delta.x+(requestedPosition.z-a.z)*delta.z)/Mathf.Max(.001f,squared));
                            var point=a+delta*t;float distance=(new Vector2(point.x-requestedPosition.x,point.z-requestedPosition.z)).sqrMagnitude;
                            // A resumed drive may be kilometres beyond its
                            // original small source seed. Only snap a nearby
                            // start; the live worker acquires a remote start's
                            // own roads in the same geographic coordinate frame.
                            if(distance>=best)continue;best=distance;initial=point;forward=delta;
                        }
                }
                var hostRoot=new GameObject("Owned automatic world");hostRoot.transform.SetParent(World.transform,false);
                var host=hostRoot.AddComponent<AutomaticWorldHost>();bootAutomaticHost=host;
                yield return host.Prepare(sourceRegion,initial,forward,()=>request!=bootGeneration);
                if(request!=bootGeneration)yield break;
                World.BuildAutomaticWorld(host);
                data=new PreparedWorldData(host.Region,World.Route,35,World.Garage.position);
            }
            else using(BootTelemetry.Enter(bootTiming,"main.buildprepared.total"))World.BuildPrepared(data,true);
            Vector3 start=data.Route.At(35,data.Route.LaneOffset(35));
            if(Profile.customStart&&Profile.startRegion==regionId&&data.Region!=null&&!data.Region.circuit)
            {var road=data.Region.Nearest(new Vector3(Profile.startX,0,Profile.startZ),out var point,out _);if(road!=null)start=data.Region.DrivingLanePoint(road,point,Vector3.forward,out _);}
            if(World.GeographicWorld)yield return World.GeographicWorld.PrepareInitialCoverage(start,()=>request!=bootGeneration,
                (done,total)=>BootStatus="Building starting roads "+done+" / "+total);
            if(World.CircuitArt)yield return World.CircuitArt.PrepareInitialDetail(start,()=>request!=bootGeneration,
                message=>BootStatus=message);
            if(request!=bootGeneration)yield break;
            if(!source.Unchanged())throw new IOException("Saved area changed during scene preparation");
            BootStatus="Preparing your car…";yield return null;
            yield return PrepareActorStages(regionId,args,request);
            if(request==bootGeneration&&!BootFailed&&!BootReady)throw new InvalidOperationException("Actor stages ended without a ready commit");
        }
        IEnumerator GuardBoot(IEnumerator job)
        {
            try
            {
                while(!BootFailed)
                {
                    bool more=false;object current=null;Exception error=null;
                    try{more=job.MoveNext();if(more)current=job.Current;}catch(Exception e){error=e;}
                    if(error!=null){FailBoot(error);yield break;}if(!more)yield break;
                    if(current is IEnumerator nested)yield return GuardBoot(nested);else yield return current;
                }
            }
            finally{(job as IDisposable)?.Dispose();}
        }
        void FailBoot(Exception error)
        {CancelBootWorker();BootReady=false;BootFailed=true;BootStatus=error.Message;Time.timeScale=0;CleanupIncompleteBootScene();bootTiming?.Complete("failed: "+error.GetType().Name);}
        void CancelBootWorker()
        {
            RevokeActualVehicleRoster();
            CancelScopedSession800();
            ++bootGeneration;
            KeepBootClearingCamera();
            bootWork?.Dispose();bootWork=null;
        }
        public void CancelBoot(){if(!BootReady)FailBoot(new OperationCanceledException("Preparation cancelled. Your saved career was retained."));}
        readonly ControllerMenuNavigation bootMenus = new ControllerMenuNavigation();
        internal ControllerMenuNavigation BootMenus => bootMenus;
        void OnGUI()
        {
            if(BootReady||LocationLoading.Busy)return;
            GUI.depth=-900;GUI.color=Color.white;
            BrandScreen.DrawLoading(BootFailed?"World not started":"Preparing your journey",World?World.Route?.Region?.name??"Your saved location":"Your saved location",BootStatus,BootFailed);
            using var navigationScope = ControllerMenuGUI.Begin(bootMenus, BootFailed ? "boot-error" : "boot-loading");
            var button=new Rect(98,690,300,42);
            if(!BootFailed){if(ControllerMenuGUI.Button(button,"Cancel preparation"))CancelBoot();}
            else{if(ControllerMenuGUI.Button(button,"Retry saved location"))LocationLoading.Retry();button.y+=55;if(CanReturnFromFailedLocation){if(ControllerMenuGUI.Button(button,"Open county · keep career"))ReturnToCountyKeepingCareer();button.y+=55;}if(ControllerMenuGUI.Button(button,"Quit"))Application.Quit();}
        }
    }
}
