using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class PlayFeedbackVerification
    {
        const string Key="EUPlayFeedback";static int phase,checks;static double deadline;static GameSession current;static GameSession previous;static float pitStarted;
        static PlayFeedbackVerification(){if(!SessionState.GetBool(Key,false))return;PlayerProfile.StorageOverride=SessionState.GetString(Key+"Path","");deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");Directory.CreateDirectory("Builds/Verification");string path=Path.GetFullPath("Builds/Verification/feedback-"+DateTime.UtcNow.Ticks+".json");File.WriteAllText(path,JsonUtility.ToJson(new PlayerProfile{quality=0,volume=0,rumble=0,credits=1200}));SessionState.SetString(Key+"Path",path);SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool value,string message){if(!value)throw new Exception("FEEDBACK FAIL: "+message);checks++;Debug.Log("FEEDBACK PASS: "+message);}
        static bool NewSession(){var s=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(s)||ReferenceEquals(s,previous)||LocationLoading.Busy)return false;current=s;Check(UnityEngine.Object.FindObjectsByType<GameSession>(FindObjectsSortMode.None).Length==1,"exactly one session after live transition");return true;}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Live transition verification timed out at phase "+phase);if(!EditorApplication.isPlaying)return;
                if(phase==0)
                {
                    current=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(current))return;Application.runInBackground=true;
                    current.Car.ReplicaDamage(48);current.Car.Visual.Dent(current.Car.Body.position+current.Car.transform.forward*2,20);Check(current.SaveCareer(0,"First driver"),"named career snapshot saves damage and funds");
                    current.Economy.AddCredits(100);Check(current.FreeRepair("test")&&current.Car.Damage==0&&current.Economy.Credits==1300,"free roadside repair restores the car without a charge");Check(current.SaveCareer(1,"Second driver"),"second career uses an independent slot");
                    var one=CareerStorage.Read(0);var two=CareerStorage.Read(1);Check(one.credits==1200&&one.garage.Active.damage==48&&two.credits==1300&&two.garage.Active.damage==0,"career slots retain independent balances and car condition");
                    previous=current;Check(current.LoadCareer(0),"saved career loads through live transition");phase=1;return;
                }
                if(phase==1)
                {
                    if(!NewSession())return;Check(current.Economy.Credits==1200&&current.Car.Damage==48,"loaded career restores saved balance and damage");previous=current;current.SelectCircuit("silverstone",1,1,1);phase=2;return;
                }
                if(phase==2)
                {
                    if(!NewSession())return;Check(current.World.IsCircuit&&current.World.Route.Region.id=="silverstone","county to Silverstone works without restarting the app");Check(current.World.Route.Width(600)>18,"circuit width expanded relative to the car");
                    current.SetMenu(false);current.Car.ReplicaDamage(35);current.Car.Recover(current.Pits.Bay,current.World.Route.Rotation(42));Time.timeScale=5;pitStarted=Time.time;phase=3;return;
                }
                if(phase==3)
                {
                    if(Time.time-pitStarted<9)return;Check(current.Car.Damage==0&&current.Economy.Credits==1200&&!current.Car.StartLocked,"physical pit visit completes free timed repair and releases brakes");current.SetMenu(true);previous=current;current.SelectRegion("dee-valley");phase=4;return;
                }
                if(phase==4)
                {
                    if(!NewSession())return;Check(current.World.Route.Region.id=="dee-valley"&&!current.World.IsCircuit,"circuit to real roads works in the same process");previous=current;current.SelectCircuit("spa",2,1,1);phase=5;return;
                }
                if(phase==5)
                {
                    if(!NewSession())return;Check(current.World.Route.Region.id=="spa"&&current.Racing.Entrants==4,"real roads to Spa builds the full race grid");current.SetMenu(true);previous=current;current.SelectRegion("");phase=6;return;
                }
                if(phase==6)
                {
                    if(!NewSession())return;Check(current.World.Route.Region==null&&current.Economy.Credits==1200,"returning to original county preserves funds");Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length==1,"live switches leave one active audio listener");Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/play-feedback.txt","PASS "+checks+" live-transition/career/free-repair/pit/width checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
