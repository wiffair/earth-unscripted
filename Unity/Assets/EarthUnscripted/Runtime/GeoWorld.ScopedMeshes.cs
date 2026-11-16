using System;using System.Collections;using System.Threading;using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class GeoWorld
    {
        ScopedWorldMeshPreview verifiedScopedPreview;IEnumerator scopedPreviewWork;int scopedPreviewLifetime,scopedPreviewWorldGeneration;
        public PreparedWorldMeshPlan ScopedMeshPlan=>verifiedScopedPreview&&scopedPreviewWorldGeneration==generation&&verifiedScopedPreview.Ready?verifiedScopedPreview.Plan:null;
        // Explicit isolated-proof adapter; a32m source subset must never silently
        // impersonate the normal49-chunk prepared world or run its feature builders.
        public IEnumerator BuildVerifiedScopedPreview(PreparedWorldMeshPlan plan,Material sharedRoad,Material sharedTerrain,CancellationToken token=default,Func<bool> stillCurrent=null)
        {
            if(string.IsNullOrEmpty(PlayerProfile.StorageOverride)||Region!=null||loaded.Count!=0||Observer||SecondaryObserver||RaceObservers.Count!=0||verifiedScopedPreview)throw new InvalidOperationException("Fresh isolated GeoWorld required for scoped packet proof");
            if(plan==null||plan.FullWorldAdmitted)throw new InvalidOperationException("Explicit bounded source-subset proof required");
            bootPreparing=true;int request=++scopedPreviewLifetime,worldGeneration=generation;
            var root=new GameObject("Verified scoped world fixture");root.transform.SetParent(transform,false);var preview=root.AddComponent<ScopedWorldMeshPreview>();verifiedScopedPreview=preview;scopedPreviewWorldGeneration=worldGeneration;
            bool Current()=>request==scopedPreviewLifetime&&worldGeneration==generation&&ReferenceEquals(verifiedScopedPreview,preview)&&(stillCurrent==null||stillCurrent());
            var work=preview.Build(plan,sharedRoad,sharedTerrain,token,Current);scopedPreviewWork=work;
            try{while(true){token.ThrowIfCancellationRequested();if(!Current())throw new OperationCanceledException("Scoped world preview superseded");if(!work.MoveNext())break;yield return work.Current;}}
            finally
            {
                if(ReferenceEquals(scopedPreviewWork,work))scopedPreviewWork=null;
                (work as IDisposable)?.Dispose();
                // An old suspended iterator must not clear a later world/preview.
                if(ReferenceEquals(verifiedScopedPreview,preview)&&(!preview||!preview.Ready)){verifiedScopedPreview=null;if(preview)preview.Invalidate();if(root)Release(root);}
            }
            // Keep automatic49-chunk streaming held: this is still an isolated
            // scoped preview. The caller can drive a fixture car only in its scope.
        }
        void ResetScopedPreview()
        {
            // Revoke query/identity access BEFORE disposing or deferred destruction.
            var preview=verifiedScopedPreview;verifiedScopedPreview=null;++scopedPreviewLifetime;
            var work=scopedPreviewWork;scopedPreviewWork=null;
            try{(work as IDisposable)?.Dispose();}
            finally{if(preview){preview.Invalidate();Release(preview.gameObject);}}
        }
        bool TryScopedMeshGround(float x,float z,out float y)
        {var plan=ScopedMeshPlan;y=0;if(plan==null)return false;y=(float)plan.RequireGround(x,z);return true;}
        bool ScopedMeshHasSurface(Vector3 position)
        {var plan=ScopedMeshPlan;return plan!=null&&plan.TryGround(position.x,position.z,out _);}
    }
}
