using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // The exact driving footprint precedes its broad scenery margin. Every
        // existing broad, branch and planned window remains in the same plan.
        Dictionary<Vector2Int,float> GeometryPriorities(Vector3 position,Vector3 plannedHeading)
        {
            var priorities=new Dictionary<Vector2Int,float>();
            void AddWindow(Vector3 point,float radius,float along,bool driving=false)
            {
                int cells=Mathf.CeilToInt(radius/ChunkSize);
                var centre=ChunkKey(point);
                for(int dz=-cells;dz<=cells;dz++)for(int dx=-cells;dx<=cells;dx++)
                {
                    var key=centre+new Vector2Int(dx,dz);
                    float west=key.x*ChunkSize,south=key.y*ChunkSize;
                    if(AreaAt(west+ChunkSize*.5f,south+ChunkSize*.5f)==null)continue;
                    float lateral=new Vector2(west+ChunkSize*.5f-point.x,south+ChunkSize*.5f-point.z).magnitude;
                    if(lateral>radius+ChunkSize*.5f)continue;
                    float priority=driving?DrivingCorridorPriority(along,lateral):along*.55f+lateral*1.5f;
                    if(!priorities.TryGetValue(key,out float previous)||priority<previous)priorities[key]=priority;
                }
            }
            void AddDrivingFootprint(Vector3 point,float along)
            {
                const float margin=4;
                int west=Mathf.CeilToInt((point.x-margin)/ChunkSize)-1,east=Mathf.FloorToInt((point.x+margin)/ChunkSize);
                int south=Mathf.CeilToInt((point.z-margin)/ChunkSize)-1,north=Mathf.FloorToInt((point.z+margin)/ChunkSize);
                for(int cz=south;cz<=north;cz++)for(int cx=west;cx<=east;cx++)
                {
                    float x=cx*ChunkSize,z=cz*ChunkSize;
                    if(AreaAt(x+ChunkSize*.5f,z+ChunkSize*.5f)==null)continue;
                    float dx=Mathf.Max(0,Mathf.Max(x-point.x,point.x-(x+ChunkSize)));
                    float dz=Mathf.Max(0,Mathf.Max(z-point.z,point.z-(z+ChunkSize)));
                    if(dx*dx+dz*dz>margin*margin)continue;
                    var key=new Vector2Int(cx,cz);float priority=-2000+along*.4f;
                    if(!priorities.TryGetValue(key,out float previous)||priority<previous)priorities[key]=priority;
                }
            }
            AddWindow(position,StreamRadius,0);
            // Establish the human driver's stopping corridor before spending
            // time on side scenery, even while the road forecast is updating.
            if(!fly)
            {
                float horizon=EarthUnscripted.AutomaticWorldHost.PreparationHorizon(embedded?embeddedSpeed:car?car.GetComponent<Rigidbody>().linearVelocity.magnitude:0,RecentPreparationSeconds);
                if(embedded&&immediateDrivingCorridor.Length>1)
                {
                    var corridor=immediateDrivingCorridor;
                    // Use actual metres along the road. The previous tiny
                    // index weight let far, well-centred cells outrank missing
                    // support near the car.
                    for(int i=0;i<corridor.Length;i+=2)AddWindow(corridor[i],48,i*8,true);
                    for(int i=0;i<Mathf.Min(corridor.Length,256);i++)AddDrivingFootprint(corridor[i],i*8);
                }
                else for(float ahead=0;ahead<=horizon;ahead+=32)AddWindow(position+plannedHeading*ahead,48,ahead,true);
            }
            float progress=ClosestStation(position,out _);
            if(!fly)
                foreach(var corridor in branchWindows)
                    AddWindow(corridor.point,96f,corridor.distanceMetres);
            // Always keep the immediate stopping corridor first. Then build
            // the active route before other branches and background scenery.
            if(!fly&&plannedRoute!=null&&plannedRoute.Active)
            {
                float at=plannedRoute.Progress(position);
                for(float ahead=0;ahead<=1600&&at+ahead<=plannedRoute.Length;ahead+=32)
                    AddWindow(plannedRoute.At(at+ahead),48,(-150+ahead*.02f)/.55f);
            }
            return priorities;
        }
    }
}
