using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// One bounded, cancellable worker prepares nearby pack buildings as the observer
    /// changes 320m cells. Unity DTO decoding and scene publication stay on the main thread.
    /// Local previews require explicit selection. LAN sharing/admission remains separate.
    /// </summary>
    public sealed class RegionalPackWindowController : MonoBehaviour
    {
        const double MetresPerDegree=111319.4908;
        const int CellMetres=320,RadiusMetres=640;
        sealed class Opened
        {
            public WorldPackRegionalReader Reader;
            public JObject Baseline;
            public string ManifestHash,BaselineIdentity;
        }
        sealed class Prepared
        {
            public Vector2Int Cell;
            public string Identity,Payload,Status;
            public int RegionalParts;
            public bool IsBaseline;
            public long SelectedBytes;
        }
        [Serializable] sealed class Payload { public GeoBuilding[] buildings; }
        GameSession session;GeoWorld world;GeoRegion region;
        CancellationTokenSource lifetime;
        readonly SemaphoreSlim workerGate=new SemaphoreSlim(1,1);
        int worldGeneration,pendingWorkers,activeWorkers,maximumConcurrentWorkers;
        public int PendingWorkerCount=>Volatile.Read(ref pendingWorkers);
        public int MaximumConcurrentWorkers=>Volatile.Read(ref maximumConcurrentWorkers);
        async Task<T> RunWorker<T>(Func<T> operation,CancellationToken token)
        {
            Interlocked.Increment(ref pendingWorkers);
            try
            {
                // Reopened controllers share this gate with their cancelled retired
                // jobs. WaitAsync uses no blocked thread while the previous job drains.
                await workerGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    int count=Interlocked.Increment(ref activeWorkers);
                    Interlocked.Exchange(ref maximumConcurrentWorkers,Math.Max(Volatile.Read(ref maximumConcurrentWorkers),count));
                    try{return await Task.Run(operation,token).ConfigureAwait(false);}
                    finally{Interlocked.Decrement(ref activeWorkers);}
                }
                finally{workerGate.Release();}
            }
            finally{Interlocked.Decrement(ref pendingWorkers);}
        }
        Task<Opened> opening;Task<Prepared> work;Opened opened;
        bool active,requested;Vector2Int requestedCell;
        string submittedIdentity,submittedStatus;bool awaitingCommit,submittedBaseline;
        public bool Active=>active;
        public bool Busy=>opening!=null||work!=null||world&&world.BuildingWindowBusy;
        public string Status {get;private set;}="No regional pack selected";
        public int QueriesStarted {get;private set;}
        public int QueriesCompleted {get;private set;}
        public int DiscardedResults {get;private set;}
        public int SubmittedWindows {get;private set;}
        public int UnchangedWindows {get;private set;}
        public int LastRegionalParts {get;private set;}
        public long LastSelectedBytes {get;private set;}
        public long MaximumSelectedBytes {get;private set;}
        public int MaximumRegionalParts {get;private set;}

        public void OpenPreview(GameSession owner,WorldPackStore store,InstalledWorldPack installed)
        {
            if(Application.isEditor||Application.companyName!="EarthUnscripted Verification"||Application.identifier!="com.earthunscripted.qa.loadingcamera"
                ||string.IsNullOrEmpty(PlayerProfile.StorageOverride)||!owner||owner.Network.Active||!owner.World.GeographicWorld||owner.World.IsCircuit)
                throw new InvalidOperationException("Regional streaming preview requires isolated offline geographic QA");
            OpenSelected(owner,store,installed,true);
        }
        internal void OpenSelected(GameSession owner,WorldPackStore store,InstalledWorldPack installed,bool explicitPreview)
        {
            if(!explicitPreview)throw new InvalidOperationException("Unvalidated local pack needs explicit offline preview selection");
            if(!owner||!owner.BootReady||owner.Network.Active||!owner.World.GeographicWorld||owner.World.IsCircuit||owner.IsScopedReaderSession800)
                throw new InvalidOperationException("Regional scenery preview requires a ready offline geographic world");
            if(store==null||installed==null)throw new ArgumentNullException();
            StopPreview();session=owner;world=owner.World.GeographicWorld;region=world.Region;worldGeneration=world.SurfaceGeneration;
            string baseline=JsonUtility.ToJson(region),version=Application.version;
            lifetime=new CancellationTokenSource();var token=lifetime.Token;
            active=true;requested=false;QueriesStarted=QueriesCompleted=DiscardedResults=SubmittedWindows=UnchangedWindows=0;
            LastRegionalParts=0;LastSelectedBytes=0;MaximumSelectedBytes=0;MaximumRegionalParts=0;submittedIdentity=null;submittedStatus=null;awaitingCommit=false;Status="Checking regional pack…";
            opening=RunWorker(()=>{
                var engine=new PackEngineSupport(version,new Dictionary<string,int>{{"regional-index",1},{"regional-buildings",1},{"provenance",1}});
                return new Opened { Reader=WorldPackRegionalReader.OpenVerified(store,installed,engine,token),
                    Baseline=JObject.Parse(baseline),ManifestHash=installed.Manifest.ManifestSha256,
                    BaselineIdentity=WorldPackPolicy.Hash(Encoding.UTF8.GetBytes("RegionalBaseline1|"+installed.Manifest.ManifestSha256+"|"+baseline)) };
            },token);
        }
        bool OwnsWorld()=>session&&session.BootReady&&world&&ReferenceEquals(session.World.GeographicWorld,world)&&ReferenceEquals(world.Region,region)&&world.SurfaceGeneration==worldGeneration;
        static Vector2Int Cell(Vector3 position)=>new Vector2Int((int)Math.Floor(position.x/CellMetres),(int)Math.Floor(position.z/CellMetres));
        static Prepared Prepare(Opened source,Vector2Int cell,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var baseline=source.Baseline;var coverage=source.Reader.Coverage;
            double latitude=(double)baseline["latitude"],longitude=(double)baseline["longitude"],cosine=Math.Cos(latitude*Math.PI/180);
            if(Math.Abs(cosine)<.001)throw new InvalidDataException("Unsupported polar scenery projection");
            double x=cell.x*CellMetres+CellMetres*.5,z=cell.y*CellMetres+CellMetres*.5;
            double minX=(double)baseline["minX"],minZ=(double)baseline["minZ"],maxX=minX+(double)baseline["width"],maxZ=minZ+(double)baseline["depth"];
            double west=Math.Max(coverage.West,longitude+Math.Max(minX,x-RadiusMetres)/(MetresPerDegree*cosine));
            double east=Math.Min(coverage.East,longitude+Math.Min(maxX,x+RadiusMetres)/(MetresPerDegree*cosine));
            double south=Math.Max(coverage.South,latitude+Math.Max(minZ,z-RadiusMetres)/MetresPerDegree);
            double north=Math.Min(coverage.North,latitude+Math.Min(maxZ,z+RadiusMetres)/MetresPerDegree);
            if(west>=east||south>=north)
                return new Prepared { Cell=cell,IsBaseline=true,Identity=source.BaselineIdentity,Payload=new JObject{{"buildings",baseline["buildings"].DeepClone()}}.ToString(Formatting.None),Status="Outside pack coverage · base scenery retained" };
            var selected=source.Reader.Query(new PackBounds(west,south,east,north),new RegionalDraft.RegionalCollection.Limits(),token);
            var plan=WorldPackBuildingWindow.Prepare(baseline,selected,source.ManifestHash,token);
            return new Prepared { Cell=cell,Identity=plan.Identity,Payload=new JObject{{"buildings",plan.CopyRenderBuildings()}}.ToString(Formatting.None),
                RegionalParts=plan.RegionalParts,SelectedBytes=selected.SelectedBytes,Status="Nearby pack scenery ready" };
        }
        void Update()
        {
            if(!active)return;
            if(!OwnsWorld()||!session.Car||session.Network.Active){StopPreview();Status="World changed · regional preview stopped";return;}
            if(opening!=null)
            {
                if(!opening.IsCompleted)return;var done=opening;opening=null;
                if(done.IsCanceled){StopPreview();Status="Regional opening cancelled";return;}
                if(done.IsFaulted){string error=done.Exception.GetBaseException().Message;StopPreview();Status="Regional pack unavailable: "+error;return;}
                opened=done.Result;
            }
            if(awaitingCommit&&!world.BuildingWindowBusy)
            {Status=(submittedBaseline?world.BuildingWindowIdentity==null:world.BuildingWindowIdentity==submittedIdentity)?submittedStatus:world.BuildingWindowStatus;awaitingCommit=false;}
            var desired=Cell(session.Car.transform.position);
            if(work!=null&&work.IsCompleted)
            {
                var done=work;
                if(done.IsCanceled){work=null;Status="Area query cancelled";}
                else if(done.IsFaulted){work=null;Status="Area query failed; current scenery retained: "+done.Exception.GetBaseException().Message;}
                else if(done.Result.Cell!=desired){work=null;DiscardedResults++;}
                else
                {
                    var prepared=done.Result;
                    if(world.BuildingWindowBusy)return;
                    work=null;QueriesCompleted++;LastRegionalParts=prepared.RegionalParts;LastSelectedBytes=prepared.SelectedBytes;MaximumSelectedBytes=Math.Max(MaximumSelectedBytes,prepared.SelectedBytes);MaximumRegionalParts=Math.Max(MaximumRegionalParts,prepared.RegionalParts);
                    if(prepared.IsBaseline?world.BuildingWindowIdentity==null:world.BuildingWindowIdentity==prepared.Identity){UnchangedWindows++;Status=prepared.Status;}
                    else
                    {
                        try { var payload=JsonUtility.FromJson<Payload>(prepared.Payload);if(prepared.IsBaseline)world.QueueDefaultBuildings(session);else world.QueueBuildingWindow(session,region,payload.buildings,prepared.Identity);submittedBaseline=prepared.IsBaseline;submittedIdentity=world.PendingBuildingWindowIdentity;submittedStatus=prepared.Status;awaitingCommit=true;SubmittedWindows++;Status="Preparing nearby scenery…"; }
                        catch(Exception error){Status="Scenery unchanged: "+error.Message;}
                    }
                }
            }
            if(work==null&&(!requested||desired!=requestedCell))
            {
                requested=true;requestedCell=desired;QueriesStarted++;var snapshot=opened;var token=lifetime.Token;
                work=RunWorker(()=>Prepare(snapshot,desired,token),token);
            }
        }
        public void StopPreview()
        {
            active=false;
            if(world&&world.BuildingWindowBusy&&world.PendingBuildingWindowIdentity==submittedIdentity)world.CancelBuildingWindowPreview();
            var source=lifetime;lifetime=null;
            Task unfinished=(Task)opening??work;opening=null;work=null;opened=null;
            if(source!=null)
            {
                source.Cancel();
                if(unfinished==null)source.Dispose();
                else unfinished.ContinueWith(done=>{if(done.IsFaulted)_=done.Exception;source.Dispose();},TaskScheduler.Default);
            }
        }
        void OnDestroy()=>StopPreview();
    }
}
