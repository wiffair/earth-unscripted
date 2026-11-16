using System;
using System.Threading;
using System.Threading.Tasks;
namespace EarthUnscripted
{
    // A single result transfers exclusive region ownership. Caller must not retain
    // or mutate the input GeoRegion after handing it to Run.
    public sealed class PreparedWorldData
    {
        public readonly GeoRegion Region;
        public readonly RoutePath Route;
        public readonly FinalizedRoadSurfacePlan RoadSurfaces;
        public readonly DefaultTerrainPlan DefaultTerrain;
        public readonly float GarageDistance;
        public readonly UnityEngine.Vector3 GarageSite;
        internal PreparedWorldData(GeoRegion region,RoutePath route,float distance,UnityEngine.Vector3 site,FinalizedRoadSurfacePlan surfaces=null,DefaultTerrainPlan defaultTerrain=null)
        {Region=region;Route=route;GarageDistance=distance;GarageSite=site;RoadSurfaces=surfaces;DefaultTerrain=defaultTerrain;}
    }
    public sealed class OwnedWorldPreparation : IDisposable
    {
        // Cancellation storms never start simultaneous full-region preparations.
        static readonly SemaphoreSlim Serial=new SemaphoreSlim(1,1);
        readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        readonly Task<PreparedWorldData> task;
        int claimed,disposed;
        public bool Completed=>task.IsCompleted;
        public bool Cancelled=>task.IsCanceled;
        public Exception Error=>task.IsFaulted?task.Exception.GetBaseException():null;
        public OwnedWorldPreparation(Func<CancellationToken,PreparedWorldData> exclusiveWork)
        {
            if(exclusiveWork==null)throw new ArgumentNullException(nameof(exclusiveWork));
            var token=cancellation.Token;
            task=Task.Run(async()=>
            {
                await Serial.WaitAsync(token).ConfigureAwait(false);
                try{token.ThrowIfCancellationRequested();var result=exclusiveWork(token);token.ThrowIfCancellationRequested();return result;}
                finally{Serial.Release();}
            },token);
            // Observe faults even if the scene/coroutine has gone away. No logging
            // or engine calls on worker completion; abandoned result becomes GC owned.
            task.ContinueWith(t=>{var ignored=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
        public bool TryTake(out PreparedWorldData result)
        {
            result=null;if(!Completed||Cancelled||Error!=null||cancellation.IsCancellationRequested)return false;
            if(Interlocked.CompareExchange(ref claimed,1,0)!=0)return false;
            result=task.GetAwaiter().GetResult();return true;
        }
        public void Cancel(){if(Volatile.Read(ref disposed)!=0)return;try{cancellation.Cancel();}catch(ObjectDisposedException){}}
        public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;cancellation.Cancel();/* CTS disposed only after outstanding worker has stopped. */task.ContinueWith(_=>cancellation.Dispose(),TaskScheduler.Default);}
    }
}
