using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
namespace RoadRush
{
    public static class ConstrainedRoadWidthAdmission
    {
        // Immutable managed input required. Staged caller publishes ONLY completed
        // result and discards all partial work on cancellation/generation change.
        public static IEnumerable<int> ResolveSteps(RoadWidthTaperPlan outward,RoadMarkingPlan topology,RoadBankExteriorClip.SpatialIndex oldPavement,RoadWidthAdmission.ObstacleIndex obstacles,Func<float,float,float> surface,Action<RoadWidthAdmission.Result> finish)
        {
            RoadWidthAdmission.Result initial=null;foreach(var step in RoadWidthAdmission.EvaluateSteps(outward,topology,oldPavement,obstacles,surface,r=>initial=r))yield return step;
            var chosen=initial.Decisions.ToDictionary(d=>d.ChainId,StringComparer.Ordinal);var outwardConflicts=initial.Decisions.ToDictionary(d=>d.ChainId,d=>d.Conflicts,StringComparer.Ordinal);
            foreach(float inset in new[]{0f,.02f})
            {
                var blocked=new HashSet<string>(chosen.Values.Where(d=>d.Status.StartsWith("blocked")).Select(d=>d.ChainId),StringComparer.Ordinal);if(blocked.Count==0)break;
                var inward=RoadWidthTaperPlan.Build(topology,true,inset);var subset=new RoadWidthTaperPlan();foreach(var chain in inward.Chains.Where(c=>blocked.Contains(c.Id))){subset.Chains.Add(chain);foreach(var part in chain.Parts)subset.Roads.Add(part.Road.id,inward.Roads[part.Road.id]);}
                RoadWidthAdmission.Result fallback=null;foreach(var step in RoadWidthAdmission.EvaluateSteps(subset,topology,oldPavement,obstacles,surface,r=>fallback=r))yield return step;
                foreach(var decision in fallback.Decisions){decision.RejectedOutwardConflicts=outwardConflicts[decision.ChainId];decision.InwardInsetMetres=inset;chosen[decision.ChainId]=decision;}yield return 0;
            }
            var final=chosen.Values.OrderBy(d=>d.ChainId,StringComparer.Ordinal).ToArray();finish(new RoadWidthAdmission.Result{Decisions=final,Profiles=final.SelectMany(d=>d.Profiles).OrderBy(p=>p.roadId,StringComparer.Ordinal).ToArray(),BlockedChains=final.Count(d=>d.Status.StartsWith("blocked")),AdmittedChains=final.Count(d=>d.Status.StartsWith("admitted"))});
        }
        public static Task<RoadWidthAdmission.Result> ResolveWorker(RoadWidthTaperPlan outward,RoadMarkingPlan topology,RoadBankExteriorClip.SpatialIndex oldPavement,RoadWidthAdmission.ObstacleIndex obstacles,Func<float,float,float> surface,CancellationToken token)
        {return Task.Run(()=>{RoadWidthAdmission.Result result=null;foreach(var step in ResolveSteps(outward,topology,oldPavement,obstacles,surface,r=>result=r))token.ThrowIfCancellationRequested();token.ThrowIfCancellationRequested();return result;},token);}
    }
}
