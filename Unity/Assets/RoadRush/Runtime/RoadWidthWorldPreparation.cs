using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
namespace RoadRush
{
    // Worker-only preparation on a freshly decoded, exclusively owned region.
    // This does not activate a world, create meshes, or mutate a visible scene.
    public static class RoadWidthWorldPreparation
    {
        public sealed class Report
        {
            public string status,diagnostic;public int roads,profiles,outwardChains,inwardChains,blockedChains;public long checkpoints,elapsedMilliseconds;
            public RoadWidthAdmission.Result admission;
        }
        public static Report PrepareOwned(GeoRegion region,CancellationToken token)
        {
            if(region==null)throw new ArgumentNullException(nameof(region));token.ThrowIfCancellationRequested();
            var timer=Stopwatch.StartNew();var report=new Report{roads=region.roads?.Length??0};
            if(region.roadWidthSchema==1)
            {RoadWidthProfileDto.Validate(region.roadWidthProfiles,region.roads);report.status="existing-authoritative-controls";report.profiles=region.roadWidthProfiles.Length;return report;}
            if(region.roadWidthSchema!=0||(region.roadWidthProfiles?.Length??0)!=0)throw new InvalidDataException("Unsupported width source schema");
            if(region.circuit){report.status="circuit-structure-preserved";return report;}
            if(!region.preparedWidths)throw new InvalidDataException("Base driving widths must be prepared before final width admission");
            if((region.railways?.Length??0)>0&&!region.railwayProfilesPrepared)throw new InvalidDataException("Railway clearance must be prepared before road admission");
            // These bounds/identity checks are explicit eligibility checks. An unsupported
            // source must be reported by the caller; never manufacture topology IDs.
            var topology=RoadMarkingPlan.Build(region.roads);token.ThrowIfCancellationRequested();
            var proposal=RoadWidthTaperPlan.Build(topology);token.ThrowIfCancellationRequested();
            var previousPavement=new RoadBankExteriorClip.SpatialIndex(LegacyRoadPavementFootprints.Build(region));token.ThrowIfCancellationRequested();
            var obstacles=new RoadWidthAdmission.ObstacleIndex(RoadWidthAdmission.RegionObstacles(region));token.ThrowIfCancellationRequested();
            RoadWidthAdmission.Result admitted=null;
            foreach(var checkpoint in ConstrainedRoadWidthAdmission.ResolveSteps(proposal,topology,previousPavement,obstacles,region.RoadSurfaceHeight,r=>admitted=r))
            {token.ThrowIfCancellationRequested();report.checkpoints++;}
            token.ThrowIfCancellationRequested();if(admitted==null)throw new InvalidDataException("Road admission did not complete");
            // Validation and immutable-copy creation happen before canonical publication.
            RoadWidthProfileDto.Validate(admitted.Profiles,region.roads);
            if(!LegacyRoadWidthPreparation.TryInstall(region,1,admitted.Profiles,out string diagnostic))throw new InvalidDataException("Final width installation refused: "+diagnostic);
            report.status=admitted.BlockedChains==0?"prepared-geometric-admission":"prepared-with-unresolved-chains";
            report.profiles=admitted.Profiles.Length;report.admission=admitted;report.blockedChains=admitted.BlockedChains;
            report.outwardChains=admitted.Decisions.Count(d=>d.Status=="admitted-geometric-clearance");report.inwardChains=admitted.Decisions.Count(d=>d.Status=="admitted-inward-geometric-clearance");report.elapsedMilliseconds=timer.ElapsedMilliseconds;
            report.diagnostic="Building, water and conservative railway footprint admission only. Terrain support, final bank/mesh grounding and native acceptance remain separate gates.";return report;
        }
        public static FinalizedRoadSurfacePlan PrepareSurfacesOwned(GeoRegion region,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();if(region.roadWidthSchema==0)return null;
            if(region.roadWidthSchema!=1||region.PreparedRoadWidths==null)throw new InvalidDataException("Surface preparation requires finalized controls");
            var topology=RoadMarkingPlan.Build(region.roads,(road,distance)=>region.EffectiveWidth(road,(float)distance));FinalizedRoadSurfacePlan result=null;
            foreach(var step in FinalizedRoadSurfacePlan.PrepareSteps(region,topology,region.roadWidthProfiles,region.RoadSurfaceHeight,p=>result=p))token.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();if(result==null)throw new InvalidDataException("Surface preparation did not complete");return result;
        }
    }
}
