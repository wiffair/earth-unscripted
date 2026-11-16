using System;
using System.Collections.Generic;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted
{
    public sealed class CircuitSession : MonoBehaviour
    {
        sealed class Racer {public string Name;public VehicleController Car;public RaceDriver Driver;public LapTracker Lap;public float Progress,Finish=-1;public bool PitProgressEngaged;}
        public GameSession Session;public bool Running {get;private set;}public bool Finished {get;private set;}public int Mode {get;private set;}
        public bool Competitive=>Running&&Mode!=0;public float Countdown {get;private set;}public float Clock=>(float)clock;
        public LapTracker Lap=>racers.Count>0?racers[0].Lap:null;public int Position {get;private set;}=1;public int Entrants=>racers.Count;
        public string ModeName=>new[]{"PRACTICE","TIME TRIAL","RACE","SPEED TRIAL"}[Mathf.Clamp(Mode,0,3)];
        public string Result {get;private set;}="";public string ConditionsLabel {get;private set;}="";public string[] Classification {get;private set;}=Array.Empty<string>();
        sealed class PendingLap
        {public string Receipt,Text,PrepaidHireAttempt;public OwnedCar Car;public double Seconds;public bool Clean;public TrialRecord Record;}
        PendingLap pendingLap;bool lapHadIncident,rentalFeeDue;string hireAttemptId;int hireAttemptNumber;Vector3 rentalFeeOrigin;
        public bool HasPendingLap=>pendingLap!=null;
        RacingRuleSet activeRules=RacingRuleSet.LegacyDefaults;
        const float RentalCooldownMaximumMetres=50, RentalCooldownCourseFraction=.05f;
        static readonly string[] RacerNames={"","ROWAN","ALEX","JULES"};
        readonly List<Racer> racers=new List<Racer>();double clock;string eventId,startConditions,startLayout;OwnedCar lapSetup;bool awarded;int laps;
        void Start(){if(Session.World.IsCircuit&&GameSession.AutoStartCircuit){GameSession.AutoStartCircuit=false;Session.SetMenu(false);Begin(Session.Profile.trackMode);}}
        public void Begin(int mode)
        {
            if(Session.DrivenCar.model>=ImportedVehicleCatalogue.BuiltInCount){Session.Notify("Imported cars currently support free drive; select a built-in car for circuit timing.");return;}
            if(pendingLap!=null&&!RetryPendingLap())return;
            if(!Session.World.IsCircuit)return;if(Session.Network.Active){Session.Notify("LAN currently supports free practice; shared races are a later stage");return;}
            var preset=Session.ImportedCircuit?.Event;if(preset!=null)mode=preset.Mode;
            var chosenRules=preset?.Rules??Session.RacingRules;string vehicleClass=Session.DrivenCar.model==3?"single-seat":"road";
            if(!System.Linq.Enumerable.Contains(chosenRules.AllowedVehicleClasses,vehicleClass)){Session.Notify("This racing ruleset does not allow the active car class");return;}
            Cancel();activeRules=chosenRules;Mode=Mathf.Clamp(mode,0,3);laps=preset?.Laps??Session.Profile.raceLaps;clock=0;Countdown=Mode==2?activeRules.RaceCountdown:activeRules.TrialCountdown;Running=true;Finished=false;awarded=false;Result="";Classification=Array.Empty<string>();eventId=Guid.NewGuid().ToString("N");
            hireAttemptNumber=1;hireAttemptId=Session.IsRenting?eventId+"-attempt-1":null;rentalFeeDue=false;
            if(!Session.BeginRentalAttempt(hireAttemptId)){Running=false;Session.Car.StartLocked=true;Session.SetMenu(true);return;}
            Session.SyncActiveCondition();var route=Session.World.Route;float lane=Mode==2?-2.7f:0;Session.Car.Recover(route.At(-8,lane),route.Rotation(-8));
            var player=new Racer{Name="YOU",Car=Session.Car,Lap=new LapTracker(route.Length,activeRules)};player.Lap.Reset(route.Closest(player.Car.Body.position,out _,out _));racers.Add(player);
            if(Mode==2)for(int i=1;i<=activeRules.RaceOpponents;i++)
            {
                using var populationFactory = Session.BeginActualVehicleFactory(this, "race/" + i, VehiclePopulation.ActualVehicleRosterDomain.Role.RaceOpponent);
                var owned=Session.DrivenCar.Clone();owned.damage=0;owned.dents.Clear();owned.paint=(owned.paint+i+1)%8;owned.wheels=i%3;owned.bodyKit=i%3;
                var g=new GameObject("Circuit opponent "+i);g.layer=8;var box=g.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
                var visual=g.AddComponent<CarVisual>();visual.Build(CarColours.Paint[owned.paint],true,owned.model,owned.wheels,owned.bodyKit,owned.finish);VehicleGeometry.ApplyCollider(box,owned.model);var car=g.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;car.ApplyTuning(owned);
                car.PowerFactor*=1-Session.Car.Damage*.005f;float d=-8-i/2*8;float side=i%2==0?-2.7f:2.7f;car.Recover(route.At(d,side),route.Rotation(d));
                var ai=g.AddComponent<RaceDriver>();ai.Car=car;ai.Route=route;ai.Session=Session;ai.Index=i;ai.Difficulty=preset?.Difficulty??Session.Profile.raceDifficulty;ai.Lane=side;g.AddComponent<TyreEffects>().Car=car;
                var racer=new Racer{Name=i<RacerNames.Length?RacerNames[i]:"DRIVER "+i,Car=car,Driver=ai,Lap=new LapTracker(route.Length,activeRules)};racer.Lap.Reset(route.Closest(car.Body.position,out _,out _));ai.Recovered+=progress=>racer.Lap.Recover(MapPitProgress(racer,progress));racers.Add(racer);
                populationFactory.Commit();
            }
            var fleet=new List<VehicleController>();foreach(var r in racers)fleet.Add(r.Car);foreach(var r in racers){r.Car.StartLocked=Countdown>0;if(r.Driver)r.Driver.Fleet=fleet;}
            if(Session.World.GeographicWorld){Session.World.GeographicWorld.RaceObservers.Clear();foreach(var r in racers)if(r.Driver)Session.World.GeographicWorld.RaceObservers.Add(r.Car.transform);}
            Session.Notify(Mode==2?"Grid ready · three lights to go":"Cross the start line to begin a timed lap");
        }
        public void Cancel()
        {
            Session?.EndRentalAttempt();hireAttemptId=null;rentalFeeDue=false;
            foreach(var r in racers){if(r.Car)r.Car.StartLocked=false;if(r.Driver&&r.Car){Session.RetireActualVehicle(this, r.Car.gameObject);r.Car.gameObject.SetActive(false);Destroy(r.Car.gameObject);}}
            racers.Clear();if(Session&&Session.World&&Session.World.GeographicWorld)Session.World.GeographicWorld.RaceObservers.Clear();Running=Finished=false;
        }
        void FixedUpdate()
        {
            if(!Running||Session.Menu||Session.Network.Active)return;float dt=Time.fixedDeltaTime;
            // A completed flying lap starts the next tracker immediately. Allow a
            // short finish-line cooldown to stop/return or enter the nearby pit without buying another
            // attempt; save its fee before any further lap driving can continue.
            if(Session.IsRenting&&rentalFeeDue&&Vector3.Distance(Session.Car.Body.position,rentalFeeOrigin)>Mathf.Min(RentalCooldownMaximumMetres,Session.World.Route.Length*RentalCooldownCourseFraction))
            {
                string next=eventId+"-attempt-"+(hireAttemptNumber+1);
                if(!Session.BeginRentalAttempt(next))
                {Session.Car.Body.linearVelocity=Vector3.zero;Session.Car.Body.angularVelocity=Vector3.zero;Session.Car.StartLocked=true;Session.SetMenu(true);return;}
                hireAttemptNumber++;hireAttemptId=next;rentalFeeDue=false;Session.Car.StartLocked=false;
            }
            if(Countdown>0){Countdown=Mathf.Max(0,Countdown-dt);if(Countdown==0){foreach(var r in racers)r.Car.StartLocked=false;Session.Notify("GO");}return;}
            clock+=dt;var route=Session.World.Route;
            for(int i=0;i<racers.Count;i++)
            {
                var r=racers[i];r.Progress=route.Closest(r.Car.Body.position,out _,out float separation);if(r.Finish>=0)continue;
                // Each racer owns its bypass state. Main-route nearest projection can
                // jump hundreds of metres where a pit lane passes another corner.
                bool legalPit=Mode==2&&activeRules.RacePitLaneAllowed&&Session.Pits&&Session.Pits.OnLane(r.Car.Body.position)&&r.Car.AbsoluteSpeed<activeRules.RacePitLaneSpeed;
                bool onTrack=separation<route.Width(r.Progress)*.5f+.85f||legalPit;
                r.Progress=MapPitProgress(r,r.Progress);float horizontal=new Vector2(r.Car.Body.linearVelocity.x,r.Car.Body.linearVelocity.z).magnitude;float speed=r.Car.Speed>0?horizontal:-horizontal;
                var lapEvent=r.Lap.Tick(dt,r.Progress,onTrack,speed,r.Car.Grounded);
                if(i==0&&lapEvent==LapEvent.Started)CaptureSetup();
                if(i==0&&lapEvent==LapEvent.Aborted&&Session.IsRenting)
                {Session.EndRentalAttempt();hireAttemptId=null;rentalFeeDue=true;rentalFeeOrigin=Session.Car.Body.position;}
                if(lapEvent==LapEvent.Finished)
                {
                    if(Mode==2&&r.Lap.CompletedLaps>=laps){r.Finish=Clock-dt*(1-r.Lap.CrossingFraction)+r.Lap.Penalties;if(r.Driver)r.Driver.Cooldown=true;}
                    if(i==0)
                    {
                        if(!SaveLap()){Session.SetMenu(true);return;}CaptureSetup();
                        if(Session.IsRenting){rentalFeeDue=!(Mode==2&&r.Lap.CompletedLaps>=laps);rentalFeeOrigin=Session.Car.Body.position;hireAttemptId=null;}
                    }
                }
            }
            Position=1;float playerProgress=ProgressOf(racers[0]);for(int i=1;i<racers.Count;i++)if(ProgressOf(racers[i])>playerProgress)Position++;
            if(Mode==2&&racers[0].Finish>=0)
            {
                bool all=true;foreach(var r in racers)if(r.Finish<0)all=false;
                if(all||Clock>racers[0].Finish+activeRules.RaceFinishWait)FinishRace();
            }
        }
        // Recovery and regular ticks must use the same lap coordinate. Resetting
        // to nearest-main progress inside a pit would falsely abort the next tick.
        float MapPitProgress(Racer racer,float mainProgress)
        {
            if(Mode==2&&activeRules.RacePitLaneAllowed&&Session.Pits)
            {if(Session.Pits.TryLapProgress(racer.Car.Body.position,mainProgress,ref racer.PitProgressEngaged,out float mapped))return mapped;}
            else racer.PitProgressEngaged=false;
            return mainProgress;
        }
        float ProgressOf(Racer r)=>r.Lap.CompletedLaps*Session.World.Route.Length+r.Progress-(r.Lap.Armed?0:Session.World.Route.Length);
        string ConditionKey()
        {
            int wet=Session.Conditions.Wetness>.65f?2:Session.Conditions.Wetness>.2f?1:0;string light=Session.Conditions.Daylight<.4f?"night":"day";
            return "weather"+Session.Weather+"-wet"+wet+"-"+light;
        }
        void CaptureSetup(){lapHadIncident=false;Session.SyncActiveCondition();lapSetup=Session.DrivenCar.Clone();startConditions=ConditionKey();startLayout=Session.World.Route.Region.id+"|layout-epoch-"+WorldSnapshotCodec.Epoch(Session.World.Route.Region);if(activeRules.BehaviourSha256!=RacingRuleSet.LegacyDefaults.BehaviourSha256)startLayout+="|racing-behaviour-"+activeRules.BehaviourSha256;ConditionsLabel=startConditions;}
        bool SaveLap()
        {
            if(Mode==0){if(Session.IsRenting)Session.EndRentalAttempt();return true;}var lap=Lap;bool clean=lap.LastValid&&!lapHadIncident;
            string conditions=startConditions==ConditionKey()?startConditions:"variable-"+Session.Weather;ConditionsLabel=conditions;var owned=lapSetup??Session.DrivenCar;
            string key=TrialRules.SetupKey(startLayout??Session.World.Route.Region.id,Mode,owned,conditions);var best=Session.Profile.records.Find(r=>r.key==key);bool speed=Mode==3;
            string result=$"{Format(lap.LastTime)} · peak {lap.LastPeak*DrivingMath.MphPerMetreSecond:F1} mph";
            TrialRecord record=null;
            if(clean&&TrialRules.Better(best,lap.LastTime,lap.LastPeak,speed))
                record=new TrialRecord{key=key,track=Session.World.Route.Region.name,car=GarageRules.Catalogue[owned.model].Name,conditions=conditions+(owned.model==3?"|"+owned.raceSetup.Identity()+"|"+owned.raceTyres.ConditionKey():""),utc=DateTimeOffset.UtcNow.ToString("O"),lap=lap.LastTime,peak=lap.LastPeak,sectors=(float[])lap.LastSectors.Clone()};
            pendingLap=new PendingLap{Receipt=eventId+"-lap-"+lap.CompletedLaps,Text=result,Car=owned.Clone(),Seconds=lap.LastTime,Clean=clean,Record=record,PrepaidHireAttempt=hireAttemptId};
            return RetryPendingLap();
        }
        public bool RetryPendingLap()
        {
            if(pendingLap==null)return true;var pending=pendingLap;
            if(!Session.CommitCircuitLap(pending.Receipt,pending.Car,pending.Seconds,pending.Clean,pending.Record,out var credits,pending.PrepaidHireAttempt))return false;
            pendingLap=null;Result=pending.Text;CaptureSetup();
            if(Session.IsRenting){hireAttemptId=null;rentalFeeDue=!(Mode==2&&Lap.CompletedLaps>=laps);rentalFeeOrigin=Session.Car.Body.position;}
            Session.Notify(!pending.Clean?"Lap not clean · no lap reward":(credits.NewPersonalBest?"CAR / COURSE PERSONAL BEST · ":"CLEAN LAP · ")+pending.Text+" · +"+credits.GrossReward+" credits"+(credits.Rented?" · hire fee "+credits.HireFee+" already paid":""));
            return true;
        }
        void FinishRace()
        {
            if(awarded)return;awarded=true;Finished=true;Running=false;var order=new List<Racer>(racers);order.Sort((a,b)=>(a.Finish>=0?a.Finish:float.MaxValue).CompareTo(b.Finish>=0?b.Finish:float.MaxValue));
            Classification=new string[order.Count];int rank=0;for(int i=0;i<order.Count;i++){var r=order[i];Classification[i]=$"{i+1}. {r.Name}   {(r.Finish<0?"DNF":Format(r.Finish))}";if(r==racers[0])rank=i+1;if(r.Driver)r.Driver.Cooldown=true;}
            Position=rank;Result=$"FINISHED {rank}/{racers.Count} · {Format(racers[0].Finish)} · lap rewards already settled";
            Session.Notify(Result);Session.SetMenu(true);
        }
        public void Recovery(){if(Running&&Lap!=null){if(Lap.Armed)lapHadIncident=true;Lap.Recover(MapPitProgress(racers[0],Session.World.Route.Closest(Session.Car.Body.position,out _,out _)));Session.Notify("Recovery · "+(activeRules.RecoveryInvalidates?"lap invalid · ":"")+"race penalty +"+activeRules.RecoveryPenalty.ToString("0.#")+"s");}}
        public void Incident(){if(Running&&Lap!=null){if(Lap.Armed)lapHadIncident=true;if(activeRules.CollisionInvalidates)Lap.Invalidate("Collision");}}
        public static string Format(float seconds){int value=Mathf.Max(0,Mathf.RoundToInt(seconds*100));return (value/6000)+":"+((value/100)%60).ToString("00")+"."+(value%100).ToString("00");}
        void OnDestroy(){Cancel();}
    }
}
