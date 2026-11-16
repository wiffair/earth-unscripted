using System;
using System.Collections.Generic;
using System.Linq;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        // Lease state belongs to this circuit session, never the serialized garage.
        // Quitting/reloading returns the owned car; paid attempts are not refunded.
        OwnedCar hiredCar;string hiredAttempt;
        public OwnedCar DrivenCar=>hiredCar??Profile?.garage?.Active;
        public bool IsRenting=>hiredCar!=null;
        public bool RentalAttemptPaid=>IsRenting&&hiredAttempt!=null;
        bool CanChangeCircuitCar()
        {
            if(!BootReady||!World.IsCircuit||!Menu||Network.Active||PreparingStart||LocationLoading.Busy
                ||(Car.AbsoluteSpeed>=.7f&&!(Racing&&Racing.Finished))||(Pits&&Pits.Servicing))
            {Notify("Pause while stopped at the circuit, outside pit service, to change hire cars");return false;}
            return !Racing||Racing.RetryPendingLap();
        }
        public bool RentCircuitCar(int model)
        {
            if(!CanChangeCircuitCar()||model<0||model>=ImportedVehicleCatalogue.BuiltInCount)return false;
            var rules=ImportedCircuit?.Event?.Rules??RacingRules;
            if(!rules.AllowedVehicleClasses.Contains(model==3?"single-seat":"road")){Notify("This event does not allow that car class");return false;}
            int fee=RaceLapEconomy.QuoteForModel(model,true,false,false).HireFee;
            if(Economy.Credits<fee){Notify("Hire needs "+fee+" credits for each lap attempt");return false;}
            if(!TrySave()){Notify("Hire not changed · career could not be saved");return false;}
            var previous=DrivenCar.Clone();
            var candidate=new OwnedCar{id="hire-"+Guid.NewGuid().ToString("N"),model=model,paint=DrivenCar.paint};
            if(!ApplyCircuitCar(candidate,out var reason)){ApplyCircuitCar(previous,out _);Notify("Hire car could not be placed: "+reason);return false;}
            Racing?.Cancel();hiredCar=candidate;hiredAttempt=null;Car.StartLocked=true;
            Notify("Hired "+GarageRules.Catalogue[model].Name+" · "+fee+" credits per lap attempt · resume to start");return true;
        }
        public bool ReturnCircuitHire()
        {
            if(!IsRenting)return true;if(!CanChangeCircuitCar())return false;
            if(!TrySave()){Notify("Hire not returned · career could not be saved");return false;}
            var previous=DrivenCar.Clone();
            if(!ApplyCircuitCar(Profile.garage.Active,out var reason)){ApplyCircuitCar(previous,out _);Notify("Owned car could not be placed: "+reason);return false;}
            Racing?.Cancel();hiredCar=null;hiredAttempt=null;Car.StartLocked=false;
            Notify("Owned car restored · hire damage and tuning remain separate");return true;
        }
        bool ApplyCircuitCar(OwnedCar car,out string reason)
        {
            Car.Visual.Rebuild(car);Car.ApplyTuning(car);Car.Body.linearVelocity=Vector3.zero;Car.Body.angularVelocity=Vector3.zero;
            Car.Recover(World.Route.At(-8),World.Route.Rotation(-8));
            return Car.TryInitializeGarageRest(out reason);
        }
        // Called before an attempt may run. Save the debit first; a failed save
        // leaves both live credits and the old receipt list unchanged.
        internal bool BeginRentalAttempt(string attempt)
        {
            if(!IsRenting)return true;if(hiredAttempt==attempt)return true;
            if(string.IsNullOrEmpty(attempt)||Network.Active)return false;
            string receipt="hire-fee-"+attempt;int fee=RaceLapEconomy.QuoteForModel(DrivenCar.model,true,false,false).HireFee;
            if(Economy.Credits<fee){Notify("Not enough credits for the next hired lap · return the car or choose a cheaper model");return false;}
            if(Profile.rewardedEvents.Contains(receipt))return false;
            var old=Profile.rewardedEvents;int oldCredits=Profile.credits;
            var receipts=new List<string>(old){receipt};if(receipts.Count>256)receipts.RemoveAt(0);
            Profile.rewardedEvents=receipts;Profile.credits=Economy.Credits-fee;
            if(!Profile.Save()){Profile.rewardedEvents=old;Profile.credits=oldCredits;Notify("Hire fee could not be saved · attempt remains paused");return false;}
            Economy.Spend(fee);hiredAttempt=attempt;return true;
        }
        internal bool HasPaidRentalAttempt(string attempt)=>IsRenting&&attempt!=null&&hiredAttempt==attempt&&Profile.rewardedEvents.Contains("hire-fee-"+attempt);
        internal void EndRentalAttempt(){hiredAttempt=null;}
        internal bool FitHiredRaceSetup(RaceSetup setup)
        {
            if(!IsRenting||DrivenCar.model!=3||setup==null)return false;
            var next=setup.Clone();next.Validate();Car.ReplaceRaceSetup(next);Car.Repair();SyncActiveCondition();return true;
        }
    }
}
