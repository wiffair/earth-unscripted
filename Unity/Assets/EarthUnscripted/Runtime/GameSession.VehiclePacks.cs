using System;
using EarthUnscripted.Core;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        public bool PaintActiveCar(int colour,int pattern,int finish)
        {
            if(!SinglePlayerBeta.Enabled||!BootReady||!Menu||World.IsCircuit||Network.Active||IsRenting||Wanted>0||Run.Active||Car.AbsoluteSpeed>.5f)
            {Notify("Stop in free drive and finish any challenge or pursuit before painting.");return false;}
            SyncActiveCondition();var previous=Profile.garage;var active=previous.Active;
            var transaction=GarageRules.Style(previous,Economy.Credits,colour,active.wheels,active.bodyKit,finish,pattern);
            if(transaction==null){Notify("Choose a change you can afford.");return false;}
            int previousCredits=Profile.credits;Profile.garage=transaction.Garage;Profile.credits=transaction.Credits;
            if(!Profile.Save()){Profile.garage=previous;Profile.credits=previousCredits;Notify("Paint job not applied: saving failed.");return false;}
            Economy.Spend(Economy.Credits-transaction.Credits);
            Car.Visual.Rebuild(Profile.garage.Active);Car.ApplyTuning(Profile.garage.Active);
            Notify(VehiclePaintJobs.Names[pattern]+" paint job applied");return true;
        }
        /// <summary>Beta free-drive owned-car switch used by both menus. Identity
        /// is the saved car ID, so two owned examples of one model stay distinct.
        /// Buying/fitting parts retains the separate Harbor Auto transaction.</summary>
        public bool SelectOwnedVehicle(string ownedId)
        {
            var owned=Profile?.garage?.cars.Find(car=>car.id==ownedId);
            if(owned==null||!ImportedVehicleCatalogue.Available(owned))
            {Notify("That owned car is unavailable.");return false;}
            return SelectPackVehicle(owned.model,ownedId);
        }

        /// <summary>Explicit beta-only car selection at a stationary free-drive
        /// boundary. Uses the existing save transaction; never modifies other cars,
        /// credits, active challenges or an in-progress pursuit. A failed save
        /// restores the prior garage. Pack selection retains the current location.</summary>
        public string VehicleSwapUnavailableReason
        {
            get
            {
                if(Wanted>0)return "You can't swap cars while a police pursuit is active. Resume driving and end the pursuit first; its countdown pauses in menus.";
                if(!SinglePlayerBeta.Enabled)return "Car swapping here is available in the single-player beta.";
                if(!BootReady)return "Wait for the world to finish loading before changing cars.";
                if(Network.Active)return "Car swapping is unavailable during a multiplayer session.";
                if(World.IsCircuit)return "Return to free drive before changing cars.";
                if(IsRenting)return "Return your hired car before changing cars.";
                if(Run.Active)return "End the active challenge before changing cars.";
                if(!Menu)return "Pause free drive before changing cars.";
                return "";
            }
        }

        public bool SelectPackVehicle(int model,string ownedId=null)
        {
            string unavailableReason=VehicleSwapUnavailableReason;
            if(!string.IsNullOrEmpty(unavailableReason))
            { Notify(unavailableReason); return false; }
            var entry = model >= ImportedVehicleCatalogue.BuiltInCount ? ImportedVehicleCatalogue.Get(model) : null;
            if (entry != null && VehiclePackLibrary.ForModel(model) == null) return false;
            var specification = GarageRules.Catalogue[model];
            SyncActiveCondition();
            var previous = Profile.garage;
            var next = previous.Clone();
            var owned = next.cars.Find(c => ownedId != null ? c.id == ownedId && c.model == model
                : entry == null ? string.IsNullOrEmpty(c.packVehicleId) && c.model == model : c.packVehicleId == entry.Settings.Id);
            if(ownedId!=null&&owned==null){Notify("That owned car is unavailable.");return false;}
            if (owned == null)
            {
                if (entry == null) { Notify("This built-in car is not owned."); return false; }
                if (next.cars.Count >= 8) { Notify("Your garage is full (8 cars)."); return false; }
                if (entry.Settings.Price > Economy.Credits) { Notify("Not enough credits for this car."); return false; }
                owned = new OwnedCar { id = Guid.NewGuid().ToString("N"), model = model, packVehicleId = entry.Settings.Id, paint = 1 };
                next.cars.Add(owned);
            }
            bool purchase = !previous.cars.Exists(c => c.id == owned.id);
            int charge = purchase ? entry.Settings.Price : 0;
            int oldCredits = Profile.credits;
            var oldPosition = Car.Body.position;
            var oldRotation = Car.Body.rotation;
            var oldVelocity = Car.Body.linearVelocity;
            var oldAngularVelocity = Car.Body.angularVelocity;
            var oldOwned = previous.Active;
            var supportHint = new Vector3(oldPosition.x, VehicleRoadSupportHeight(), oldPosition.z);
            Car.Visual.Rebuild(owned); Car.ApplyTuning(owned);
            bool fits = Car.TryInitializeSupportedRest(supportHint, oldRotation, out _, out _)
                && ClearStartBox(Car, supportHint, Car.Body.rotation);
            if (!fits && World.AutomaticWorld) fits = PlaceClearAutomaticStart(Car);
            if (!fits)
            {
                RestoreCarBeforeSwap(oldOwned, oldPosition, oldRotation, oldVelocity, oldAngularVelocity);
                Notify("Not enough clear supported space for that car here. Move to a wider road and try again.");
                return false;
            }
            next.activeId = owned.id;
            Profile.garage = next; Profile.credits = Economy.Credits - charge;
            if (!Profile.Save())
            {
                Profile.garage = previous; Profile.credits = oldCredits;
                RestoreCarBeforeSwap(oldOwned, oldPosition, oldRotation, oldVelocity, oldAngularVelocity);
                Notify("Could not save; car selection unchanged."); return false;
            }
            if (charge > 0) Economy.Spend(charge);
            Drifts.Cancel();
            Notify(specification.Name + " selected" + (entry == null ? "" : " · pack " + entry.Settings.Version));
            return true;
        }
        // A rejected placement/save restores the old vehicle and its paused motion.
        void RestoreCarBeforeSwap(OwnedCar owned, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            Car.Visual.Rebuild(owned); Car.ApplyTuning(owned);
            Car.Body.position = position; Car.Body.rotation = rotation;
            Car.Body.linearVelocity = velocity; Car.Body.angularVelocity = angularVelocity;
            Physics.SyncTransforms();
        }
    }
}
