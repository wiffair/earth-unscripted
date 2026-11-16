using System;using System.Collections;using System.IO;using System.Threading;using UnityEngine;
namespace RoadRush
{
    // Isolated preview only. Does not alter GeoWorld's49-chunk coverage promise or
    // make a partial terrain proof usable by GameSession/normal boot.
    public sealed class ScopedWorldMeshPreview:MonoBehaviour
    {
        public PreparedWorldMeshPlan Plan{get;private set;}public bool Ready{get;private set;}
        public IEnumerator Build(PreparedWorldMeshPlan plan,Material sharedRoad,Material sharedTerrain,CancellationToken cancellation=default,Func<bool> stillCurrent=null)
        {
            if(Plan!=null||Ready||plan==null)throw new InvalidOperationException("Fresh scoped preview owner required");
            if(transform.position!=Vector3.zero||transform.rotation!=Quaternion.identity||transform.lossyScale!=Vector3.one)throw new InvalidDataException("Identity preview root required");
            GameObject stage=null;bool handed=false;
            try
            {
                stage=new GameObject("Scoped final mesh stage");stage.SetActive(false);stage.transform.SetParent(transform,false);
                foreach(var step in WorldMeshUpload.BuildHidden(stage.transform,plan,role=>role==PreparedWorldMeshPlan.Role.GroundRoad?sharedRoad:sharedTerrain,cancellation,stillCurrent))yield return step;
                cancellation.ThrowIfCancellationRequested();if(stillCurrent!=null&&!stillCurrent())throw new OperationCanceledException("Scoped preview stale");
                Plan=plan;stage.SetActive(true);Ready=true;handed=true;
            }
            finally{if(!handed&&stage){Geometry.ReleaseMeshes(stage);if(Application.isPlaying)Destroy(stage);else DestroyImmediate(stage);}}
        }
        public float RequireGround(float x,float z){if(!Ready)throw new InvalidOperationException("Scoped mesh not ready");return(float)Plan.RequireGround(x,z);}
        internal void Invalidate(){Ready=false;Plan=null;}
        void OnDestroy(){Invalidate();Geometry.ReleaseMeshes(gameObject);}
    }
}
