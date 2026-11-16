using System;
using System.Collections.Generic;
using System.Linq;
using RoadRush.Core;

namespace RoadRush
{
    public sealed partial class GameSession
    {
        // A single save transaction commits credits, setup record, model/course best
        // and idempotency receipt. Hired attempts already paid their fee before
        // driving; this transaction adds their gross reward only. A pack cannot supply cash.
        public bool CommitCircuitLap(string receipt,OwnedCar completedCar,double seconds,bool clean,
            TrialRecord setupRecord,out RaceLapCredits quote,string prepaidHireAttempt=null)
        {
            quote=default;
            if(!BootReady||!World.IsCircuit||Network.Active||completedCar==null||string.IsNullOrEmpty(receipt)
                ||string.IsNullOrEmpty(PhysicalLayoutIdentity)||Profile.rewardedEvents.Contains(receipt))return false;
            if(IsRenting&&(!HasPaidRentalAttempt(prepaidHireAttempt)||completedCar.id!=DrivenCar.id||completedCar.model!=DrivenCar.model))return false;
            if(!IsRenting&&prepaidHireAttempt!=null)return false;
            var key=new ModelLayoutLapKey(completedCar.model,PhysicalLayoutIdentity);
            var oldBest=Profile.modelLayoutBests.SingleOrDefault(b=>b.key==key.Identity);
            var assessment=RaceLapEconomy.AssessLap(key,seconds,clean,IsRenting,oldBest?.Read());quote=assessment.Credits;
            if(oldBest==null&&assessment.ShouldUpdateBest&&Profile.modelLayoutBests.Count>=1000)
            {Notify("Model/course record storage is full · no result or credits changed");return false;}
            SyncActiveCondition();
            var oldRecords=Profile.records;var oldBests=Profile.modelLayoutBests;var oldReceipts=Profile.rewardedEvents;int oldCredits=Profile.credits;
            var records=new List<TrialRecord>(oldRecords);
            if(clean&&setupRecord!=null){records.RemoveAll(r=>r.key==setupRecord.key);records.Add(setupRecord);if(records.Count>1000)records.RemoveAt(0);}
            var bests=new List<ModelLayoutLapRecord>(oldBests);
            if(assessment.ShouldUpdateBest){bests.RemoveAll(b=>b.key==key.Identity);bests.Add(ModelLayoutLapRecord.From(assessment.ProposedBest));}
            var receipts=new List<string>(oldReceipts){receipt};if(receipts.Count>256)receipts.RemoveAt(0);
            int updated=(int)Math.Max(0L,Math.Min(10000000L,(long)Economy.Credits+quote.GrossReward));
            Profile.records=records;Profile.modelLayoutBests=bests;Profile.rewardedEvents=receipts;Profile.credits=updated;
            if(!Profile.Save())
            {
                Profile.records=oldRecords;Profile.modelLayoutBests=oldBests;Profile.rewardedEvents=oldReceipts;Profile.credits=oldCredits;
                Notify("Lap result not saved · credits and records unchanged · resume to retry");return false;
            }
            Economy.AddCredits(updated-Economy.Credits);if(IsRenting)EndRentalAttempt();return true;
        }
    }
}
