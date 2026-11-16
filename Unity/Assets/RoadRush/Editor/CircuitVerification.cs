using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    [InitializeOnLoad] public static class CircuitVerification
    {
        const string Key="RoadRushCircuitVerification";static GameSession session;static RaceDriver driver;static int stage,checks;static double wallStart;static float traceAt;static string track;static int mode;
        static CircuitVerification()
        {
            if(!SessionState.GetBool(Key,false))return;track=SessionState.GetString(Key+"Track","silverstone");mode=SessionState.GetInt(Key+"Mode",1);PlayerProfile.StorageOverride=SessionState.GetString(Key+"Path","");
            wallStart=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
        }
        public static void RunSilverstoneTrial()=>Run("silverstone",1);
        public static void RunSpaTrial()=>Run("spa",1);
        public static void RunSilverstoneRace()=>Run("silverstone",2);
        public static void RunSpaRace()=>Run("spa",2);
        public static void RunSilverstoneSpeed()=>Run("silverstone",3);
        static void Run(string region,int selectedMode)
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/RoadRush/Scenes/Blackwood.unity");Directory.CreateDirectory("Builds/Verification");
            string path=Path.GetFullPath("Builds/Verification/"+region+"-mode"+selectedMode+"-"+DateTime.UtcNow.Ticks+".json");
            var profile=new PlayerProfile{region=region,trackMode=selectedMode,raceLaps=1,raceDifficulty=1,quality=0,volume=0,rumble=0};File.WriteAllText(path,JsonUtility.ToJson(profile));
            SessionState.SetString(Key+"Track",region);SessionState.SetInt(Key+"Mode",selectedMode);SessionState.SetString(Key+"Path",path);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string message){if(!value)throw new Exception("CIRCUIT FAIL: "+message);checks++;Debug.Log("CIRCUIT PASS: "+message);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-wallStart>600)throw new Exception("Circuit verification exceeded ten minutes wall time");if(!EditorApplication.isPlaying)return;
                if(stage==0)
                {
                    session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session))return;Check(session.World.IsCircuit&&session.World.Route.Region.id==track,"requested offline circuit loaded");
                    Check(Mathf.Abs(session.World.Route.Length-session.World.Route.Region.referenceLength)/session.World.Route.Region.referenceLength<.002f,"drivable lap length agrees with GP reference within0.2%");
                    session.Input.VerificationInput=true;Application.runInBackground=true;session.SetMenu(false);session.Racing.Begin(mode);
                    session.Car.NetworkDriven=true;driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Route=session.World.Route;driver.Session=session;driver.Index=0;driver.Difficulty=1;
                    driver.Fleet=UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None).ToList();driver.Recovered+=progress=>session.Racing.Lap.Recover(progress);
                    Time.timeScale=5;stage=1;
                }
                if(session.Racing.Clock>traceAt)
                {
                    traceAt=session.Racing.Clock+20;session.World.Route.Closest(session.Car.Body.position,out _,out float separation);
                    Debug.Log($"CIRCUIT TRACE {track} mode{mode}: time={session.Racing.Clock:F1} progress={session.Progress:F1} speed={session.Car.Speed:F1} separation={separation:F2} nextGate={session.Racing.Lap.NextGate} armed={session.Racing.Lap.Armed} valid={session.Racing.Lap.Valid} reason={session.Racing.Lap.Reason}");
                }
                if(session.Racing.Clock>700)throw new Exception("No complete lap/race after700simulation seconds");
                if(mode==2)
                {
                    if(session.Racing.Lap.CompletedLaps>=1)driver.Cooldown=true;if(!session.Racing.Finished)return;
                    Check(session.Racing.Classification.Length==4&&session.Racing.Lap.CompletedLaps>=1,"four-car physical race reaches a classified finish");
                    Check(session.Racing.Classification.All(s=>!s.Contains("DNF")),"all AI opponents complete the full circuit");
                    string id=session.Profile.rewardedEvents.Last(s=>s.EndsWith("-race"));int credits=session.Economy.Credits;
                    Check(!session.CommitAchievement(id,null,999)&&session.Economy.Credits==credits,"race completion reward cannot be paid twice");
                }
                else
                {
                    if(session.Racing.Lap.CompletedLaps<1)return;
                    Check(session.Racing.Lap.LastValid,"full physically driven lap is valid");
                    var record=session.Profile.records.Find(r=>r.key.StartsWith(track+"|"+mode+"|"));
                    Check(record!=null&&record.lap>60&&record.peak>15,"trial mode saves a timed lap and measured peak speed");
                    Check(Mathf.Abs(record.sectors.Sum()-record.lap)<.02f,"saved sectors match the complete lap time");
                    Check(PlayerProfile.Load().records.Exists(r=>r.key==record.key),"circuit/setup/condition record survives reload");
                }
                session.SetMenu(true);Directory.CreateDirectory("Builds/Verification");
                File.WriteAllText("Builds/Verification/circuit-"+track+"-mode"+mode+".txt",$"PASS {checks} checks\nlap={session.Racing.Lap.LastTime}\npeak={session.Racing.Lap.LastPeak}\nclock={session.Racing.Clock}\n"+string.Join("\n",session.Racing.Classification));Finish(0);
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
