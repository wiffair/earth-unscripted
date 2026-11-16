using UnityEngine;
namespace EarthUnscripted
{
 public sealed partial class GeoWorld
 {
// Initial coverage runs before actors/physics are admitted. A larger loading-
// only slice avoids thousands of expensive rendered-frame yields while keeping
// UI/cancel opportunities bounded by the next cooperative step. Individual
// geometry operations are atomic, so this is not a frame-time/FPS guarantee.
// Normal in-play streaming continues to use its separate 4ms budget.
const float InitialBootCoverageSliceBudgetMilliseconds = 64;
bool bootPreparing,bootActorHold;
public void BeginBootActorPreparation(){bootActorHold=true;}
public void EndBootActorPreparation(){bootActorHold=false;}
public bool BootPreparationHeld=>bootPreparing||bootActorHold;BootTelemetry.Trace bootTiming;
public System.Collections.IEnumerator PrepareInitialCoverage(UnityEngine.Vector3 position,System.Func<bool> cancelled,System.Action<int,int> progress)
{
    if(IsAutomaticWorld){if(!automaticWorld.Renderer.HasGeometryAt(position))throw new System.IO.InvalidDataException("Native starting geometry is incomplete");progress?.Invoke(1,1);yield break;}
    if(!bootPreparing)throw new System.InvalidOperationException("Initial coverage not deferred");
    double wallStart=BootTelemetry.Now;bool complete=false;double started=Time.realtimeSinceStartupAsDouble;
    try
    {
        var centre=Key(position);var keys=new System.Collections.Generic.List<UnityEngine.Vector2Int>();
        for(int z=centre.y-3;z<=centre.y+3;z++)for(int x=centre.x-3;x<=centre.x+3;x++)
        {var key=new UnityEngine.Vector2Int(x,z);if(Intersects(key)&&!loaded.ContainsKey(key))keys.Add(key);}
        // No spawned observers/physics yet; this request has one captured start.
        int done=0;progress?.Invoke(done,keys.Count);
        foreach(var key in keys)
        {
            if(cancelled())yield break;if(Time.realtimeSinceStartupAsDouble-started>300)throw new System.TimeoutException("Starting road preparation timed out");stage=StartStage(key,"boot-required");
            while(stage!=null)
            {
                if(cancelled())yield break;if(Time.realtimeSinceStartupAsDouble-started>300)throw new System.TimeoutException("Starting road preparation timed out");
                long began=Timestamp();bool more;
                using(BootTelemetry.Enter(bootTiming,"main.coverage.slice"))
                do{var active=stage;var timing=BootTelemetry.Measure("main.coverage.advance");try{more=AdvanceStage(active);}finally{if(bootTiming!=null)timing.Context(active.key.x+","+active.key.y+"/"+active.phase);timing.Dispose();}}while(more&&Elapsed(began)<InitialBootCoverageSliceBudgetMilliseconds);
                if(!more)stage=null;
                yield return null; // repaint/input/cancel even on a completed cell
            }
            progress?.Invoke(++done,keys.Count);
        }
        // Reconcile only after all required cells physically exist. This preserves
        // normal Refresh/force return guarantees without recording startup misses.
        using(BootTelemetry.Enter(bootTiming,"main.coverage.reconcile"))Refresh(position,true);complete=true;
    }
    finally
    {
        if(!complete)CancelStage("boot-cancel");
        bootTiming?.Wall("coverage.total.wall",wallStart,complete?"complete":"cancelled");
        // Keep preparation guard enabled on failure/cancel. Owner must destroy the
        // not-adopted world root; do not expose a partially built drive.
        if(complete)bootPreparing=false;
    }
}

 }
}
