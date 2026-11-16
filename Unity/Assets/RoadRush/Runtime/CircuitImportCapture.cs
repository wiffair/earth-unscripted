using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RoadRush.Core;
using RoadRush.Packs;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Opt-in isolated native observer. Normal UI handlers import/start the layout;
    /// a real RaceDriver supplies controls for one lap. No pose writes occur during that lap.
    /// Pit placement and a deliberately missing reference are separate labelled fault tests.</summary>
    public sealed class CircuitImportCapture : MonoBehaviour
    {
        string prefix, archive, home, buildKey, token, hiddenReference, referencePath;
        GameSession current, previous; PackCircuitContext context; RaceDriver driver;
        int phase, frames, creditsBefore, creditsAfterLap, recoveries, traceRows;
        double deadline; float lapBegan, nextTrace, pitBegan; bool detailCaptured, creditsCaptured, hireCapture, eventCapture, saveFailureArmed, saveFailureVerified, pitObserved, finished, drivingCaptured, pitCaptured;
        float lapSeconds, distance; Vector3 lastPosition;
        StreamWriter trace;
        readonly JArray checks = new JArray();
        string BuildKey(OwnedCar c) => string.Join("|", new object[] { c.id, c.model, c.paint, c.wheels, c.bodyKit, c.finish,
            c.engine, c.gearing, c.tyres, c.brakes, c.suspension, c.differential, c.raceSetup?.Identity() });
        void Check(bool value, string name)
        { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        internal void Initialize(GameSession session, string output, string source)
        {
            if (string.IsNullOrEmpty(PlayerProfile.StorageOverride) || Path.GetFullPath(PlayerProfile.StoragePath) != Path.GetFullPath(output + "-profile.json"))
                throw new InvalidOperationException("Circuit capture requires its exact isolated profile");
            eventCapture=Environment.GetCommandLineArgs().Contains("-roadRushCaptureCircuitEvent");hireCapture=Environment.GetCommandLineArgs().Contains("-roadRushCaptureHire");
            current = session; prefix = output; archive = source; home = session.Profile.region;
            buildKey = BuildKey(session.Profile.garage.Active); creditsBefore = session.Economy.Credits;
            DontDestroyOnLoad(gameObject); Application.runInBackground = true; deadline = Time.realtimeSinceStartupAsDouble + 180;
            Prepare(current); current.GetComponent<DrivingUI>().ShowImportedPacks();
        }
        void Prepare(GameSession session)
        {
            Check(session.Conditions.WeatherRequests == 0 && (!session.Updates || session.Updates.SourceRequests == 0), "No provider fetch in isolated scene " + phase);
            if (session.Updates) { session.Updates.Enabled = false; session.Updates.Automatic = false; }
            session.Conditions.CancelLocalFetch(); session.SetMenu(true); session.Car.StartLocked = true;
            session.Input.enabled = false; session.Input.Throttle = session.Input.Steer = 0; session.Input.Brake = session.Input.Handbrake = 1;
            foreach (var police in FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None)) police.gameObject.SetActive(false);
            foreach (var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None)) traffic.gameObject.SetActive(false);
        }
        bool Ready(string id)
        {
            var found = FindAnyObjectByType<GameSession>();
            if (!found || found == previous || !found.BootReady || LocationLoading.Busy) return false;
            Check(found.Profile.region == id, "Expected saved source token/region " + id);
            current = found; Prepare(current); return true;
        }
        void Capture(string name) { ScreenCapture.CaptureScreenshot(prefix + "-circuit-" + name + ".png"); frames = 0; }
        void Trace()
        {
            if (trace == null || Time.time < nextTrace || traceRows >= 1024) return;
            nextTrace = Time.time + 1; traceRows++;
            var lap = current.Racing.Lap;
            trace.WriteLine(new JObject { ["time"] = Time.time, ["position"] = new JArray(current.Car.Body.position.x, current.Car.Body.position.y, current.Car.Body.position.z),
                ["speed"] = current.Car.Speed, ["damage"] = current.Car.Damage, ["completedLaps"] = lap?.CompletedLaps ?? 0,
                ["valid"] = lap?.Valid ?? false, ["reason"] = lap?.Reason, ["recoveries"] = recoveries }.ToString(Newtonsoft.Json.Formatting.None)); trace.Flush();
        }
        // Direct financial transaction fixtures are separate from the later actual
        // driven lap. Restore the isolated starting career before that drive.
        void EconomyTransactionFixtures()
        {
            var profile=current.Profile;var records=profile.records;var bests=profile.modelLayoutBests;var receipts=profile.rewardedEvents;
            int savedCredits=profile.credits,credits=current.Economy.Credits;var car=profile.garage.Active.Clone();
            var setup=new TrialRecord{key="qa-setup",track="QA only",car="QA only",conditions="QA only",lap=100,peak=30};
            string tmp=PlayerProfile.StoragePath+".tmp";
            try
            {
                Check(current.CommitCircuitLap("qa-economy-first",car,100,true,setup,out var first)&&first.GrossReward==32&&current.Economy.Credits==credits+32,"Atomic owned first model/course PB pays one percent");
                car.engine=3;setup=setup.Clone();setup.key="qa-other-weather-upgrades";
                Check(current.CommitCircuitLap("qa-economy-clean",car,110,true,setup,out var clean)&&clean.GrossReward==8&&!clean.NewPersonalBest,"Changing setup/weather does not reset model/course PB reward");
                Check(current.CommitCircuitLap("qa-economy-invalid",car,50,false,null,out var invalid)&&invalid.GrossReward==0&&profile.modelLayoutBests[0].seconds==100,"Invalid faster lap earns nothing and cannot replace model/course PB");
                int before=current.Economy.Credits;Check(!current.CommitCircuitLap("qa-economy-first",car,90,true,null,out _)&&current.Economy.Credits==before,"Duplicate completed-lap receipt cannot pay twice");
                var oldRecords=profile.records;var oldBests=profile.modelLayoutBests;var oldReceipts=profile.rewardedEvents;int oldProfileCredits=profile.credits;
                Directory.CreateDirectory(tmp);
                Check(!current.CommitCircuitLap("qa-economy-save-failure",car,90,true,null,out _)&&current.Economy.Credits==before&&profile.credits==oldProfileCredits
                    &&ReferenceEquals(profile.records,oldRecords)&&ReferenceEquals(profile.modelLayoutBests,oldBests)&&ReferenceEquals(profile.rewardedEvents,oldReceipts),"Failed actual profile save rolls back credits, both record lists and receipt");
                Directory.Delete(tmp);
                Check(current.CommitCircuitLap("qa-economy-save-failure",car,90,true,null,out var retry)&&retry.GrossReward==32&&current.Economy.Credits==before+32,"Same failed transaction can commit once after save repair");
                var reloaded=PlayerProfile.Load();Check(!reloaded.LoadBlocked&&reloaded.version==7&&reloaded.modelLayoutBests.Single().seconds==90,"Schema7 reload retains separate model/course PB");
            }
            finally
            {
                if(Directory.Exists(tmp))Directory.Delete(tmp);profile.records=records;profile.modelLayoutBests=bests;profile.rewardedEvents=receipts;profile.credits=savedCredits;
                current.Economy.AddCredits(credits-current.Economy.Credits);Check(profile.Save(),"Financial fixtures restored isolated starting career before actual lap");
            }
        }
        void RentalTransactionFixtures()
        {
            var profile=current.Profile;var records=profile.records;var bests=profile.modelLayoutBests;var receipts=profile.rewardedEvents;
            int balance=current.Economy.Credits,profileCredits=profile.credits;string owned=JsonUtility.ToJson(profile.garage);string tmp=PlayerProfile.StoragePath+".tmp";
            try
            {
                Check(current.RentCircuitCar(3)&&current.IsRenting&&current.DrivenCar.model==3&&profile.garage.Active.model==0,"Normal hire handler creates single-seat lease separate from owned coupe");
                current.Car.ReplicaDamage(25);current.Car.Visual.Dent(current.Car.Body.position+current.Car.transform.forward,20);current.SyncActiveCondition();
                Check(current.TrySave()&&JsonUtility.ToJson(profile.garage)==owned&&JsonUtility.ToJson(PlayerProfile.Load().garage)==owned,"Hired damage/dents save cannot contaminate owned garage");
                var setup=current.DrivenCar.raceSetup.Clone();setup.compound=RaceCompound.Wet;
                Check(current.FitHiredRaceSetup(setup)&&current.Car.RaceSetup.compound==RaceCompound.Wet&&JsonUtility.ToJson(profile.garage)==owned,"Hired single-seat setup/tyres remain separate from owned garage");
                current.Network.Host();Check(!current.Network.Active&&current.Network.Status.Contains("hired"),"Hired car refuses LAN before network setup");
                Check(current.BeginRentalAttempt("qa-hire-bad")&&current.Economy.Credits==balance-140,"Actual hire attempt saves half-percent debit before driving");
                Check(current.BeginRentalAttempt("qa-hire-bad")&&current.Economy.Credits==balance-140,"Repeated same attempt cannot debit twice");
                Check(current.CommitCircuitLap("qa-hired-invalid",current.DrivenCar.Clone(),100,false,null,out var invalid,"qa-hire-bad")&&invalid.GrossReward==0&&current.Economy.Credits==balance-140,"Bad hired lap retains only its half-percent fee");
                int before=current.Economy.Credits;var oldEvents=profile.rewardedEvents;int oldCredits=profile.credits;Directory.CreateDirectory(tmp);
                Check(!current.BeginRentalAttempt("qa-hire-retry")&&!current.RentalAttemptPaid&&current.Economy.Credits==before&&profile.credits==oldCredits&&ReferenceEquals(profile.rewardedEvents,oldEvents),"Failed hire-fee save cannot spend credits or unlock paid attempt");
                Directory.Delete(tmp);
                Check(current.BeginRentalAttempt("qa-hire-retry")&&current.CommitCircuitLap("qa-hired-pb",current.DrivenCar.Clone(),90,true,null,out var pb,"qa-hire-retry")&&pb.GrossReward==280&&current.Economy.Credits==balance,"Hired clean PB pays one percent gross after saved half-percent fee");
                Check(!current.CommitCircuitLap("qa-hired-reused-fee",current.DrivenCar.Clone(),80,true,null,out _,"qa-hire-retry"),"Settled hire fee cannot authorize another reward");
                Check(current.BeginRentalAttempt("qa-hire-clean")&&current.CommitCircuitLap("qa-hired-clean",current.DrivenCar.Clone(),100,true,null,out var clean,"qa-hire-clean")&&clean.GrossReward==70&&current.Economy.Credits==balance-70,"Ordinary clean hired lap has quarter-percent net cost");
                current.Car.Body.linearVelocity=current.Car.transform.forward*10;
                typeof(CircuitSession).GetProperty("Finished").SetValue(current.Racing,true);
                Check(current.ReturnCircuitHire()&&!current.IsRenting&&JsonUtility.ToJson(profile.garage)==owned,"Explicit finished-race return restores owned car while paused without forcing another paid attempt");
                int saved=current.Economy.Credits;current.Economy.AddCredits(-saved);profile.credits=0;Check(profile.Save(),"Insufficient-funds fixture saved privately");
                Check(!current.RentCircuitCar(3)&&!current.IsRenting&&JsonUtility.ToJson(profile.garage)==owned,"Insufficient credits refuse hire without garage mutation");
                current.Economy.AddCredits(saved);profile.credits=saved;
            }
            finally
            {
                if(Directory.Exists(tmp))Directory.Delete(tmp);if(current.IsRenting)current.ReturnCircuitHire();
                profile.records=records;profile.modelLayoutBests=bests;profile.rewardedEvents=receipts;profile.credits=profileCredits;
                current.Economy.AddCredits(balance-current.Economy.Credits);Check(profile.Save(),"Rental fixtures restored isolated starting career");
            }
        }
        void Update()
        {
            if (finished || string.IsNullOrEmpty(prefix)) return;
            try
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Circuit import phase " + phase);
                if (phase == 0)
                {
                    var menu = current.GetComponent<DrivingUI>().ImportedPacks; if (menu.Busy) return;
                    menu.ImportArchive(archive); phase = 1; return;
                }
                if (phase == 1)
                {
                    var menu = current.GetComponent<DrivingUI>().ImportedPacks; if (menu.Busy) return;
                    Check(menu.RevisionCount == 1 && menu.OpenCircuitRevision(0), "Normal library opened imported venue"); phase = 2; return;
                }
                if (phase == 2)
                {
                    var menu = current.GetComponent<DrivingUI>().ImportedCircuits; if (menu.Busy || menu.SelectedCircuit == null) return;
                    Check(menu.LayoutCount == (eventCapture?2:1) && menu.SelectedLayout == "gp", "GP selected through normal layout chooser");
                    if(eventCapture){Check(menu.EventCount==2,"Event catalogue retains unsupported future sibling");menu.SelectEvent("road-trial");phase=20;return;}
                    context = menu.SelectedCircuit; token = context.Token; Capture("chooser"); phase = 3; return;
                }
                if(phase==20)
                {
                    var menu=current.GetComponent<DrivingUI>().ImportedCircuits;if(menu.Busy)return;
                    Check(menu.SelectedEvent=="road-trial"&&menu.SelectedCircuit?.Event!=null,"Normal chooser selected verified separate event and rule files");
                    context=menu.SelectedCircuit;token=context.Token;Capture("chooser");phase=3;return;
                }
                if (phase == 3)
                {
                    if (++frames < 6) return; previous = current;
                    Check(current.GetComponent<DrivingUI>().ImportedCircuits.StartSession(0, 1, 0), "Normal imported-session handler saved and switched");
                    phase = 4; deadline = Time.realtimeSinceStartupAsDouble + 180; return;
                }
                if (phase == 4)
                {
                    if (!Ready(token)) return;
                    Check(current.ImportedCircuit != null && current.ImportedCircuit.ManifestSha256 == context.ManifestSha256 && current.World.IsCircuit, "Normal boot bound exact imported circuit context");
                    Check(current.World.Route.Region.id == token && Mathf.Abs(current.World.Route.Region.roads[0].width - 18.9f) < .0001f, "Owned source ID isolated and raw GP width prepared exactly once");
                    Check(BuildKey(current.Profile.garage.Active) == buildKey && current.Economy.Credits == creditsBefore, "Car/upgrades and credits preserved on imported entry");
                    current.Network.Host(); Check(!current.Network.Active, "Imported host refused before shared layout/rules validation");
                    current.Network.Join("127.0.0.1"); Check(!current.Network.Active, "Imported join refused before shared layout/rules validation");
                    if(eventCapture)
                    {
                        Check(current.ImportedCircuit.Event?.Event.Id=="road-trial"&&current.Profile.trackMode==1&&current.Profile.raceLaps==3&&current.Profile.raceDifficulty==2,"Normal boot restored pinned event and persisted preset values");
                        Check(current.RacingRules.Id=="pack-road-rules"&&current.RacingRules.TrialCountdown==0,"Event boot consumes separate pack rule file");
                        Check(!current.SetRacingRulesForNextStart(RacingRuleSet.LegacyDefaults),"Pinned event refuses mid-session rule replacement");
                        current.Racing.Begin(0);
                        Check(current.Racing.Mode==1&&current.Racing.Countdown==0&&!current.Car.StartLocked,"Pinned event overrides requested practice and immediately starts its trial");
                    }
                    else
                    {
                    var normalRules=current.RacingRules;
                    Check(normalRules.BehaviourSha256==RacingRuleSet.LegacyDefaults.BehaviourSha256,"Bundled data rule resource matches prior behavior");
                    var zeroJson=new JObject{["format"]="RoadRushRacingRules",["schema"]=1,["id"]="qa-zero",["name"]="Isolated zero countdown",["revision"]=1,["parent"]=normalRules.Id,
                        ["settings"]=new JObject{["raceCountdown"]=0,["trialCountdown"]=0,["collisionInvalidates"]=false,["recoveryInvalidates"]=false,["recoveryPenalty"]=11}};
                    var zeroRules=RacingRuleSetJson.Parse(System.Text.Encoding.UTF8.GetBytes(zeroJson.ToString()),normalRules);
                    Check(current.SetRacingRulesForNextStart(zeroRules),"Resolved custom rules selected while paused");
                    current.Racing.Begin(2);
                    Check(current.Racing.Entrants==4&&current.Racing.Countdown==0&&!current.Car.StartLocked
                        &&FindObjectsByType<RaceDriver>(FindObjectsSortMode.None).All(d=>!d.Car.StartLocked),"Zero countdown immediately unlocks actual race fleet");
                    current.Racing.Begin(1);Check(current.Racing.Countdown==0&&!current.Car.StartLocked,"Zero countdown immediately unlocks actual trial");
                    current.Racing.Lap.Reset(current.World.Route.Length-.1f);current.Racing.Lap.Tick(.01f,.1f,true,20,true);current.Racing.Incident();
                    Check(current.Racing.Lap.Valid,"Collision rule is consumed by actual circuit session");
                    current.Racing.Recovery();Check(current.Racing.Lap.Valid&&current.Racing.Lap.Penalties==11,"Recovery rule is consumed by actual circuit session");
                    current.Racing.Cancel();var singleRules=RacingRuleSetJson.Parse(System.Text.Encoding.UTF8.GetBytes(Resources.Load<TextAsset>("RacingRules/single-seat").text),normalRules);
                    Check(current.SetRacingRulesForNextStart(singleRules),"Single-seat child rule parsed and selected");current.Racing.Begin(1);
                    Check(!current.Racing.Running&&current.Racing.Entrants==0,"Ineligible road car refused without replacement or purchase");
                    Check(current.SetRacingRulesForNextStart(normalRules),"Default data rules restored before real lap");current.Racing.Begin(0);
                    }
                    EconomyTransactionFixtures();
                    if(hireCapture)
                    {
                        RentalTransactionFixtures();Check(current.RentCircuitCar(0),"Hire stock coupe for actual driven lap");
                        current.GetComponent<DrivingUI>().ShowCircuitHire();
                    }
                    if(hireCapture){Capture("hire-menu");phase=21;return;}
                    current.SetMenu(false); current.Car.StartLocked = true; Capture("loaded"); phase = 5; return;
                }
                if(phase==21)
                {if(++frames<6)return;current.SetMenu(false);current.Car.StartLocked=true;Capture("loaded");phase=5;return;}
                if (phase == 5)
                {
                    if (++frames < 6) return;
                    current.Racing.Begin(1); current.Car.NetworkDriven = true;
                    driver = current.Car.gameObject.AddComponent<RaceDriver>(); driver.Car = current.Car; driver.Route = current.World.Route;
                    driver.Session = current; driver.Fleet = new List<VehicleController> { current.Car }; driver.Difficulty = 0; driver.Index = 0;
                    driver.Recovered += progress => { recoveries++; current.Racing.Recovery(); };
                    lapBegan = Time.time; nextTrace = Time.time; lastPosition = current.Car.Body.position;
                    trace = new StreamWriter(prefix + "-circuit-lap.jsonl", false); Time.captureFramerate = 30;
                    phase = 6; deadline = Time.realtimeSinceStartupAsDouble + 420; return;
                }
                if (phase == 6)
                {
                    if(!detailCaptured&&Time.time-lapBegan>35){current.Profile.detailedHud=true;Capture("detailed-hud");detailCaptured=true;}
                    if(detailCaptured&&Time.time-lapBegan>36)current.Profile.detailedHud=false;
                    distance += Vector3.Distance(lastPosition, current.Car.Body.position); lastPosition = current.Car.Body.position; Trace();
                    if (current.Racing.Lap.CompletedLaps < 1)
                    {
                        if(!saveFailureArmed&&current.Racing.Lap.Armed&&current.Racing.Lap.NextGate==0)
                        {Directory.CreateDirectory(PlayerProfile.StoragePath+".tmp");saveFailureArmed=true;}
                        if (Time.time - lapBegan > 650) throw new TimeoutException("No complete imported GP lap in650simulated seconds");
                        if (!drivingCaptured && Time.time - lapBegan > 30) { Capture("driving"); drivingCaptured = true; } return;
                    }
                    if(!saveFailureVerified)
                    {
                        Check(saveFailureArmed&&current.Racing.HasPendingLap&&current.Menu&&current.Economy.Credits==creditsBefore-(hireCapture?16:0)&&current.Profile.modelLayoutBests.Count==0,"Actual driven finish save failure pauses with no credits/PB applied");
                        current.ResumeDrive();Check(current.Menu&&current.Racing.HasPendingLap&&current.Economy.Credits==creditsBefore-(hireCapture?16:0),"Resume refuses while actual settlement cannot save");
                        string retainedRegion=current.Profile.region;
                        Check(!current.SelectRegion(home)&&current.Profile.region==retainedRegion&&!LocationLoading.Busy,"Failed pending result prevents location transition");
                        Check(!current.SaveCareer(0,"Rejected pending fixture"),"Failed pending result prevents named-career snapshot");
                        Check(!current.LoadCareer(0)&&!LocationLoading.Busy,"Failed pending result prevents career replacement");
                        Directory.Delete(PlayerProfile.StoragePath+".tmp");current.ResumeDrive();
                        Check(!current.Racing.HasPendingLap&&!current.Menu,"Resume retries actual pending settlement successfully");saveFailureVerified=true;
                    }
                    Trace(); Check(current.Racing.Lap.LastValid && recoveries == 0 && current.Car.Damage == 0, "Actual full imported lap valid with no recovery/damage");
                    Check(distance > current.World.Route.Length * .97f, "Observed travel covers the complete GP distance");
                    lapSeconds = current.Racing.Lap.LastTime; creditsAfterLap = current.Economy.Credits;
                    Check(current.Profile.records.Any(r => r.key.Contains(token)), "Imported layout has a separate persisted PB identity");
                    Check(creditsAfterLap == creditsBefore + RaceLapEconomy.QuoteForModel(current.DrivenCar.model,hireCapture,true,true).NetCredits, hireCapture?"Actual hired PB nets half-percent after saved attempt fee":"Owned new model/course PB receives one percent of new base price");
                    Check(current.Profile.modelLayoutBests.Count==1,"Dedicated model/course PB saved separately from setup records");
                    driver.enabled = false; current.Car.StartLocked = true; current.Car.NetworkThrottle = current.Car.NetworkSteer = 0; current.Car.NetworkBrake = current.Car.NetworkHandbrake = 1;
                    trace.Dispose(); trace = null; Capture("lap-finished"); phase = 7; return;
                }
                if (phase == 7)
                {
                    if (++frames < 6) return;
                    if(hireCapture)
                    {
                        Check(current.Economy.Credits==creditsAfterLap,"Finish-line braking cooldown does not buy another hire attempt");
                        var oldReceipts=current.Profile.rewardedEvents;int oldProfileCredits=current.Profile.credits;
                        current.Car.Body.position+=current.Car.transform.forward*100;
                        typeof(CircuitSession).GetMethod("FixedUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(current.Racing,null);
                        Check(current.Economy.Credits==creditsAfterLap-16,"Actual next-attempt callback saves fee before driving beyond cooldown");
                        current.Profile.rewardedEvents=oldReceipts;current.Profile.credits=oldProfileCredits;current.Economy.AddCredits(16);
                        Check(current.Profile.Save(),"Continuation-fee position fixture restores isolated post-lap balance");
                    }
                    current.Racing.Cancel();
                    current.Car.ReplicaDamage(30); current.Car.Visual.Dent(current.Car.Body.position + current.Car.transform.forward * 2, 20);
                    Check(current.Car.TryInitializeSupportedRest(current.Pits.Bay, current.World.Route.Rotation(42), out var reason, out _), "Actual pit supported-rest fit: " + reason);
                    current.Car.StartLocked = false; current.SetMenu(false); current.View.Mode = 4; pitBegan = Time.time; phase = 8; return;
                }
                if (phase == 8)
                {
                    pitObserved |= current.Pits.Servicing;
                    if (Time.time - pitBegan > 2 && !pitCaptured) { Capture("pit-service"); pitCaptured = true; }
                    if (Time.time - pitBegan < 10) return;
                    Check(pitObserved && current.Car.Damage == 0 && !current.Pits.Servicing && current.Economy.Credits == creditsAfterLap, "Normal imported pit service repairs for free");
                    current.SetMenu(true); current.Car.StartLocked = true; Capture("pit-complete"); phase = 9; return;
                }
                if (phase == 9)
                {
                    if (++frames < 6) return; if(hireCapture)Check(current.ReturnCircuitHire()&&!current.IsRenting,"Actual hire returned after full lap and pit stop"); previous = current; Check(current.SelectRegion(home), "Normal return to previous road area");
                    phase = 10; deadline = Time.realtimeSinceStartupAsDouble + 180; return;
                }
                if (phase == 10)
                {
                    if (!Ready(home)) return;
                    Check(current.ImportedCircuit == null && BuildKey(current.Profile.garage.Active) == buildKey && current.Economy.Credits == creditsAfterLap, "Return retains car and earned imported result");
                    referencePath = Path.Combine(RegionalPackActivation.LibraryRoot(), "circuit-launches", token + ".json"); hiddenReference = referencePath + ".qa-hidden";
                    File.Move(referencePath, hiddenReference); previous = current;
                    Check(current.SelectImportedCircuit(context, 0, 1, 0), "Fault fixture removes source after preflight and requests normal saved launch");
                    phase = 11; deadline = Time.realtimeSinceStartupAsDouble + 180; return;
                }
                if (phase == 11)
                {
                    var failed = FindAnyObjectByType<GameSession>(); if (!failed || failed == previous || !failed.BootFailed) return;
                    current = failed;
                    Check(!current.BootReady && current.Profile.region == token && current.CanReturnFromMissingImportedCircuit, "Missing reference is explicit boot failure, without silent career fallback");
                    Capture("missing-source"); phase = 12; return;
                }
                if (phase == 12)
                {
                    if (++frames < 6) return; File.Move(hiddenReference, referencePath); hiddenReference = null; previous = current;
                    Check(current.ReturnToCountyKeepingCareer(), "Explicit error-screen county recovery saves existing career");
                    phase = 13; deadline = Time.realtimeSinceStartupAsDouble + 180; return;
                }
                if (phase == 13)
                {
                    if (!Ready("")) return;
                    Check(BuildKey(current.Profile.garage.Active) == buildKey && current.Economy.Credits == creditsAfterLap
                        && current.Profile.records.Any(r => r.key.Contains(token)), "Explicit missing-source recovery preserves car, upgrades, credits and PB");
                    Capture("county-recovered"); phase = 14; return;
                }
                if (phase == 14)
                {
                    if (++frames < 6) return; if(!creditsCaptured){current.GetComponent<DrivingUI>().ShowDataCredits();Capture("data-credits");creditsCaptured=true;return;} finished = true; Time.captureFramerate = 0;
                    File.WriteAllText(prefix + "-circuit-result.json", new JObject { ["status"] = "PASS_NATIVE_IMPORTED_CIRCUIT_LAP_PIT_AND_RECOVERY",
                        ["checks"] = checks, ["lapSeconds"] = lapSeconds, ["distanceMetres"] = distance, ["recoveriesDuringLap"] = recoveries,
                        ["creditsBefore"] = creditsBefore, ["creditsAfterPB"] = creditsAfterLap, ["sourceManifest"] = context.ManifestSha256,
                        ["launchToken"] = token, ["ruleScope"] = eventCapture?"Pinned separate pack event/rule files, catalogue lap rewards/hire fees, isolated rented condition and actual failed-save retry":"Base JSON rules/custom actual component cases and owned model/course lap rewards",
                        ["hiredCarLap"] = hireCapture,["compactHudDefault"] = !current.Profile.detailedHud,["creditsMenuCaptured"] = creditsCaptured,
                        ["automatedControlDriver"] = true, ["physicalInputHardwareVerified"] = false }.ToString()); Application.Quit();
                }
            }
            catch (Exception error)
            {
                finished = true; Time.captureFramerate = 0; trace?.Dispose(); trace = null;
                if (hiddenReference != null && File.Exists(hiddenReference) && !File.Exists(referencePath)) File.Move(hiddenReference, referencePath);
                File.WriteAllText(prefix + "-circuit-result.json", new JObject { ["status"] = "FAIL", ["phase"] = phase, ["error"] = error.ToString(),
                    ["checks"] = checks, ["distanceMetres"] = distance, ["recoveriesDuringLap"] = recoveries }.ToString());
                Debug.LogError("Imported circuit verification: " + error); Application.Quit(1);
            }
        }
        void OnDestroy() { trace?.Dispose(); }
    }
}
