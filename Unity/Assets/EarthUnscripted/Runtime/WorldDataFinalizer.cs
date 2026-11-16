// Shared numeric world finalization with the existing garage placement policy.
using System;using System.Collections.Generic;using System.Threading;using UnityEngine;
namespace EarthUnscripted
{
    public static class WorldDataFinalizer
    {
        sealed class PlanningState
        {
            internal float garageDistance=35;internal Vector3 garageSite;
            internal RoutePath Route;
            internal PlanningState(GeoRegion region){Route=region==null?new RoutePath():new RoutePath(region);}
        internal void PlanGeographicGarage(GeoRegion region)
        {
            if(region.CircuitScenery!=null){garageSite=region.CircuitScenery.Bay;garageDistance=Route.Closest(garageSite,out _,out _);region.ServicePad=garageSite;region.HasServicePad=true;return;}
            var prepared=System.Array.Find(region.roads,r=>r.id=="fictional-service-approach");
            if(prepared!=null)
            {
                garageSite=prepared.points[prepared.points.Length-1];garageDistance=Route.Closest(prepared.points[0],out _,out _);
                region.ServicePad=garageSite;region.HasServicePad=true;return;
            }
            for(float d=100;d<Route.Length;d+=45)
            {
                foreach(float offset in new[]{-45f,45f,-65f,65f})
                {
                    var p=Route.At(d,offset);var road=region.Nearest(p,out var q,out float separation);
                    if(road==null||separation<32||region.IsWater(p.x,p.z)||region.RailwayIntersectsCircle(p,36)||Mathf.Abs(region.RawHeight(p.x,p.z)-q.y)>4)continue;
                    bool occupied=false;
                    foreach(var b in region.buildings){if(GeoRegion.Contains(b.points,new Vector2(p.x,p.z))){occupied=true;break;}foreach(var v in b.points)if(Vector2.Distance(new Vector2(v.x,v.z),new Vector2(p.x,p.z))<36){occupied=true;break;}if(occupied)break;}
                    if(occupied)continue;
                    p.y=q.y;garageDistance=d;garageSite=p;region.ServicePad=p;region.HasServicePad=true;AddServiceApproach(region,d,p);return;
                }
            }
            garageDistance=Route.Length*.6f;garageSite=Route.At(garageDistance,-55);region.ServicePad=garageSite;region.HasServicePad=true;AddServiceApproach(region,garageDistance,garageSite);
        }
        void AddServiceApproach(GeoRegion region,float distance,Vector3 site)
        {
            var entry=Route.At(distance);var roads=new List<GeoRoad>(region.roads);roads.Add(new GeoRoad{id="fictional-service-approach",start="service-entry",end="service-forecourt",name="HARBOR AUTO approach",kind="service",width=5,limit=15,points=new[]{entry,site}});region.roads=roads.ToArray();using(BootTelemetry.Measure("worker.finalizer.index"))region.Prepare();
        }
        }
        // admitWidths must be a worker-safe, bounded implementation. It must not
        // capture World, Profile, Resources, Application or active GeoRegion.
        // Null is explicitly the legacy-schema path, not silent width fallback.
        public static PreparedWorldData AttachAuthoritative(GeoRegion region,CancellationToken token,Func<GeoRegion,CancellationToken,FinalizedRoadSurfacePlan> prepareSurfaces=null)
        {
            if(region==null)throw new ArgumentNullException(nameof(region));token.ThrowIfCancellationRequested();
            if(region.railways!=null&&region.railways.Length>0&&!region.railwayProfilesPrepared)throw new System.IO.InvalidDataException("Authoritative rails not finalized");
            using(BootTelemetry.Measure("worker.finalizer.index"))region.Prepare();var state=new PlanningState(region);
            if(region.CircuitRoadSurfaces!=null&&region.CircuitScenery!=null)
            {
                // This imported course already declares its real service lane.
                // Reconstruct render state without inventing garage/pit roads.
                state.garageSite=region.CircuitScenery.Bay;
                state.garageDistance=state.Route.Closest(state.garageSite,out _,out _);
                region.ServicePad=state.garageSite;region.HasServicePad=true;
                var authoredSurfaces=Surfaces(region,prepareSurfaces,token);
                return new PreparedWorldData(region,state.Route,state.garageDistance,state.garageSite,authoredSurfaces);
            }
            var approach=Array.Find(region.roads,r=>r.id=="fictional-service-approach");
            if(approach==null||approach.points==null||approach.points.Length<2)throw new System.IO.InvalidDataException("Authoritative world lacks prepared garage approach");
            // Reconstruct only nonserialized render state. No roads, rail heights,
            // profile controls, canopy or canonical fields change here.
            state.garageSite=approach.points[approach.points.Length-1];state.garageDistance=state.Route.Closest(approach.points[0],out _,out _);
            region.ServicePad=state.garageSite;region.HasServicePad=true;token.ThrowIfCancellationRequested();
            if(region.circuit&&!Array.Exists(region.roads,r=>r.id=="fictional-pit-lane"))throw new System.IO.InvalidDataException("Authoritative circuit lacks prepared pit lane");
            FinalizedRoadSurfacePlan surfaces;using(BootTelemetry.Measure("worker.finalizer.surfaces"))surfaces=Surfaces(region,prepareSurfaces,token);
            return new PreparedWorldData(region,state.Route,state.garageDistance,state.garageSite,surfaces);
        }
        public static PreparedWorldData PrepareOwned(GeoRegion region,Action<GeoRegion,CancellationToken> admitWidths,CancellationToken token,Func<GeoRegion,CancellationToken,FinalizedRoadSurfacePlan> prepareSurfaces=null)
        {
            token.ThrowIfCancellationRequested();
            var state=new PlanningState(region);FinalizedRoadSurfacePlan surfaces=null;
            if(region!=null)
            {
                // GeoRegion must be a fresh decode/conversion, never SnapshotCopy.
                using(BootTelemetry.Measure("worker.finalizer.index"))region.Prepare();token.ThrowIfCancellationRequested();
                using(BootTelemetry.Measure("worker.finalizer.garage"))state.PlanGeographicGarage(region);token.ThrowIfCancellationRequested();
                using(BootTelemetry.Measure("worker.finalizer.pit"))PitService.Prepare(region,state.Route);token.ThrowIfCancellationRequested();
                using(BootTelemetry.Measure("worker.finalizer.rail"))RailwayProfileSolver.Prepare(region);token.ThrowIfCancellationRequested();
                using(BootTelemetry.Measure("worker.finalizer.width"))admitWidths?.Invoke(region,token);token.ThrowIfCancellationRequested();
                using(BootTelemetry.Measure("worker.finalizer.index"))region.Prepare();token.ThrowIfCancellationRequested();
                // Final Prepare reconstructed field identity: surfaces must bind NOW.
                using(BootTelemetry.Measure("worker.finalizer.surfaces"))surfaces=Surfaces(region,prepareSurfaces,token);
                using(BootTelemetry.Measure("worker.finalizer.canopy"))DetailCanopyPacks.FinalizeForWorld(region,surfaces);token.ThrowIfCancellationRequested();
            }
            DefaultTerrainPlan terrain=null;
            if(region==null)using(BootTelemetry.Measure("worker.finalizer.defaultterrain"))terrain=DefaultTerrainPlan.Prepare(state.Route,token);
            return new PreparedWorldData(region,state.Route,state.garageDistance,state.garageSite,surfaces,terrain);
        }
        static FinalizedRoadSurfacePlan Surfaces(GeoRegion region,Func<GeoRegion,CancellationToken,FinalizedRoadSurfacePlan> build,CancellationToken token)
        {
            if(region.roadWidthSchema==0)return null;
            if(build==null)throw new InvalidOperationException("Explicit width field requires prepared surfaces");
            token.ThrowIfCancellationRequested();var result=build(region,token);token.ThrowIfCancellationRequested();
            if(result==null||result.Owner!=region||result.WidthField!=region.PreparedRoadWidths)throw new System.IO.InvalidDataException("Prepared surface/region/field identity mismatch");
            return result;
        }
    }
}
