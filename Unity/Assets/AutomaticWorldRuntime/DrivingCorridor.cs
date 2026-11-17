using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        Vector3[] immediateDrivingCorridor=System.Array.Empty<Vector3>();
        bool immediateCorridorNeedsRoad;int immediateCorridorRequiredPoints;
        float embeddedSpeed;Vector3 plannedDrivePosition=new Vector3(float.MaxValue,0,0);int plannedDriveRevision=-1;float plannedDriveProgress;
        readonly RaycastHit[] drivingSupportHits=new RaycastHit[24];
        public float RecentPreparationSeconds
        {
            get {float seconds=3;for(int i=Mathf.Max(0,report.chunks.Count-12);i<report.chunks.Count;i++)seconds=Mathf.Max(seconds,(float)report.chunks[i].queueToReadyMilliseconds*.001f);return Mathf.Min(seconds,20);}
        }
        public static float DrivingCorridorPriority(float metresAhead,float lateral)=>-1000+metresAhead*.4f+lateral*1.5f;
        public void SetDrivingCorridor(List<Vector3> points,float speed,bool needsRoad=true,bool unresolvedEnd=false)
        {
            embeddedSpeed=speed;immediateCorridorNeedsRoad=needsRoad;immediateDrivingCorridor=points.ToArray();immediateCorridorRequiredPoints=points.Count-(unresolvedEnd?1:0);
        }
        public bool TryPlannedDrivingPoint(Vector3 position,float ahead,out Vector3 point)
        {
            point=default;if(plannedRoute==null||!plannedRoute.Active)return false;
            if(plannedDrivePosition!=position||plannedDriveRevision!=plannedRouteRevision){plannedDrivePosition=position;plannedDriveRevision=plannedRouteRevision;plannedDriveProgress=plannedRoute.Progress(position);}
            float at=plannedDriveProgress;var nearest=plannedRoute.At(at);
            if(Vector3.ProjectOnPlane(nearest-position,Vector3.up).sqrMagnitude>25*25||at+ahead>plannedRoute.Length)return false;
            point=plannedRoute.At(at+ahead);return true;
        }
        public bool HasDrivingSurfaceAt(Vector3 position)
        {
            if(!HasGeometryAt(position))return false;
            int hits=Physics.RaycastNonAlloc(position+Vector3.up*2,Vector3.down,drivingSupportHits,4,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<hits;i++)
            {
                var hit=drivingSupportHits[i];var c=hit.collider;
                if(c && c.transform.IsChildOf(transform) && (c.name=="Road asphalt"||c.name=="Road shoulders") && hit.normal.y>.5f && Mathf.Abs(hit.point.y-position.y)<1.5f)return true;
            }
            return false;
        }
    }
}
