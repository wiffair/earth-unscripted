using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // One optional ticket, never a second numeric executor or concurrent
        // Unity admission. The server protects delivered files before replying.
        sealed class AheadPreparation { public Vector2Int tile;public string id; }
        AheadPreparation aheadPreparation;
        // Small immutable ready-file receipts, not decoded worlds or meshes.
        // The worker protects these files just as it protects resident replies.
        const int MaximumPreparedReplies=64;
        readonly Dictionary<Vector2Int,JobReply> preparedReplies=new Dictionary<Vector2Int,JobReply>();
        public int PreparedPromotionOverlaps {get;private set;}
        void RememberPreparedReply(Vector2Int tile,JobReply reply)
        {
            if(reply?.state!="ready")return;
            if(preparedReplies.Count>=MaximumPreparedReplies&&!preparedReplies.ContainsKey(tile))
            {var oldest=default(Vector2Int);foreach(var key in preparedReplies.Keys){oldest=key;break;}preparedReplies.Remove(oldest);}
            preparedReplies[tile]=reply;
        }
        JobReply TakePreparedReply(Vector2Int tile)
        {
            if(!preparedReplies.TryGetValue(tile,out var reply))return null;
            preparedReplies.Remove(tile);reply.cached=true;reply.generationSeconds=0;return reply;
        }
        bool ParkForPreparedPromotion(Candidate preferred,string identity)
        {
            if(preferred==null||aheadPreparation!=null||string.IsNullOrEmpty(identity)||
               preferred.tileX==pendingArea.x&&preferred.tileZ==pendingArea.y||!NeedsResidentSource(preferred)||
               !preparedReplies.ContainsKey(new Vector2Int(preferred.tileX,preferred.tileZ)))return false;
            aheadPreparation=new AheadPreparation{tile=pendingArea,id=identity};
            PreparedPromotionOverlaps++;return true;
        }
        public int AheadPreparationStarted {get;private set;}
        public int AheadPreparationReused {get;private set;}
        public int AheadPreparationDiscarded {get;private set;}
        public double LatestAreaReadMilliseconds {get;private set;}
        public double LatestAreaIndexMilliseconds {get;private set;}
        public double MaximumAreaIndexMilliseconds {get;private set;}
        public double LatestAreaPublicationWaitMilliseconds {get;private set;}

        Candidate AheadDataCandidate()=>DrivingDataCandidateExcluding(pendingArea)??PlannedDataCandidateExcluding(pendingArea);
        bool CanPrepareAhead(Candidate next)
        {
            if(!ready||!pumpRunning||fly||aheadPreparation!=null||next==null)return false;
            if(DistanceToSourceArea(next.tileX,next.tileZ)<=DataLookAheadMetres)return true;
            // The next planned core may already be selected for the disk cache
            // beyond residency. Start that same bounded serial job while Unity
            // indexes this core too, rather than leave the worker idle. Real
            // driving/turn requests still preempt the single optional ticket.
            return plannedRoute!=null&&plannedRoute.Active&&
                Vector3.ProjectOnPlane(plannedRoute.At(plannedRoute.Progress(observer))-observer,Vector3.up).sqrMagnitude<=25*25&&
                plannedRoute.AreaAhead(observer,PlannedSourcePlanningMetres,AreaSizeMetres,next.tileX,next.tileZ,25,out _);
        }

        Candidate ContinueAheadPreparation(Candidate preferred)
        {
            if(preferred!=null&&NeedsResidentSource(preferred)&&preparedReplies.ContainsKey(new Vector2Int(preferred.tileX,preferred.tileZ)))return preferred;
            var ahead=aheadPreparation;
            if(ahead==null||areas.ContainsKey(ahead.tile)||ShouldYieldSourceCore(ahead.tile,preferred))return preferred;
            return new Candidate{tileX=ahead.tile.x,tileZ=ahead.tile.y,
                x=(ahead.tile.x+.5f)*AreaSizeMetres,z=(ahead.tile.y+.5f)*AreaSizeMetres,
                distanceMetres=DistanceToSourceArea(ahead.tile.x,ahead.tile.y)};
        }

        IEnumerator BeginAheadPreparation()
        {
            var next=AheadDataCandidate();
            if(!CanPrepareAhead(next))yield break;
            var request=ObserverRequest();CenterSourceRequest(request,next.tileX,next.tileZ);
            // A full ready reply includes its source-bound sign sidecar and
            // protected file. It can later serve either resident or cache-only
            // acquisition without starting duplicate preparation.
            request.coverageOnly=true;request.cacheOnly=false;
            using(var submit=Post("/request",request))
            {
                yield return submit.SendWebRequest();
                if(submit.result!=UnityWebRequest.Result.Success)yield break;
                JobReply reply=null;
                try{reply=JsonUtility.FromJson<JobReply>(submit.downloadHandler.text);}
                catch(ArgumentException){} // Optional overlap must not fail the ready core.
                if(reply?.state!="pending"||string.IsNullOrEmpty(reply.id))yield break;
                aheadPreparation=new AheadPreparation{tile=new Vector2Int(next.tileX,next.tileZ),id=reply.id};
                AheadPreparationStarted++;
            }
        }
    }
}
