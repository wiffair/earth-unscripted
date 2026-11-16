using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    [InitializeOnLoad] public static class DriveFrontierVerification
    {
        const string Flag="EUDriveFrontierVerification";static string root;static int checks;static double deadline;static GameSession session;static GeoWorld world;static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
        static DriveFrontierVerification(){if(!SessionState.GetBool(Flag,false))return;root=SessionState.GetString(Flag+"Root","");PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;}
        public static void Run()
        {
            ProjectSetup.Configure();EditorSceneManager.OpenScene("Assets/EarthUnscripted/Scenes/EarthUnscripted.unity");root=Path.GetFullPath("Builds/Verification/DriveFrontier");Directory.CreateDirectory(root);var profile=new PlayerProfile{region="dee-valley",credits=4321,quality=0,volume=.23f,rumble=0,livingScenery=false};profile.garage.Active.engine=2;profile.garage.Active.damage=27;File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(profile));SessionState.SetString(Flag+"Root",root);SessionState.SetBool(Flag,true);EditorApplication.EnterPlaymode();
        }
        static void Check(bool ok,string text){if(!ok)throw new Exception("FRONTIER FAIL: "+text);checks++;Debug.Log("FRONTIER PASS: "+text);}
        static GeoRegion Fixture()
        {
            var r=new GeoRegion{id="drive-frontier-fixture",preparedWidths=true,name="Drive frontier fixture",minX=-2560,minZ=-2560,width=5120,depth=5120,nx=2,nz=2,heights=new[]{2f,2f,2f,2f},landNx=2,landNz=2,landCover=new int[4],roads=new[]{new GeoRoad{id="road",start="a",end="b",width=10,limit=30,points=new[]{new Vector3(-2000,0,160),new Vector3(2000,0,160)}}},waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>(),route=new[]{new Vector3(0,0,160),new Vector3(100,0,160)}};r.Prepare();return r;
        }
        static void Reset(Vector3 position)
        {
            session.SetMenu(true);world.Initialize(Fixture(),world.Asphalt);session.Car.Body.position=position+Vector3.up*4;session.Car.transform.SetPositionAndRotation(session.Car.Body.position,Quaternion.identity);Physics.SyncTransforms();world.Observer=session.Car.transform;world.Refresh(session.Car.Body.position,true);world.ClearStreamingTelemetry();
        }
        static bool NearWindow(Vector3 position)
        {int x=Mathf.FloorToInt(position.x/320),z=Mathf.FloorToInt(position.z/320);for(int zz=z-3;zz<=z+3;zz++)for(int xx=x-3;xx<=x+3;xx++)if(!world.HasSurface(new Vector3(xx*320+160,0,zz*320+160)))return false;return true;}
        static IEnumerator Cases()
        {
            session.Input.enabled=false;session.Updates.Automatic=session.Updates.Enabled=false;session.Car.Body.interpolation=RigidbodyInterpolation.None;session.Car.Body.isKinematic=true;world=session.World.GeographicWorld;
            Reset(new Vector3(160,0,160));var zero=world.PrepareDriveFrontier(session.Car.Body.position,null);Check(!zero.MoveNext()&&world.DriveFrontierReady,"middle-of-cell start has immediate zero-work frontier readiness");
            Reset(new Vector3(1,0,160));int frame=Time.frameCount,calls=0,lastReady=-1,totalSeen=0;bool monotonic=true;string canonical=WorldSnapshotCodec.CanonicalJson(world.Region);
            yield return world.PrepareDriveFrontier(session.Car.Body.position,(ready,total)=>{calls++;monotonic&=ready>=lastReady;lastReady=ready;totalSeen=total;});
            Check(world.DriveFrontierReady&&totalSeen==7&&calls>1&&Time.frameCount>frame,"near-west border preparation advances across actual rendered updates rather than a synchronous row fill");Check(monotonic&&lastReady==7,"visible progress monotonically reaches the complete7cell frontier");Check(session.Menu&&Time.timeScale==0,"preparation keeps the car and gameplay paused");Check(WorldSnapshotCodec.CanonicalJson(world.Region)==canonical,"startup scheduling does not change source geometry or canonical world identity");
            world.Refresh(new Vector3(-1,0,160));Check(NearWindow(new Vector3(-1,0,160))&&world.CaptureStreamingTelemetry().requiredMisses==0,"immediate westbound or reverse crossing has full required surfaces and zero urgent catchup");
            Reset(new Vector3(319,0,319));session.ResumeDrive();Check(session.PreparingStart&&session.Menu&&Time.timeScale==0,"actual first ResumeDrive exposes preparation before unpausing");while(session.PreparingStart)yield return null;
            Check(!session.Menu&&session.Started&&world.DriveFrontierReady,"actual first drive unpauses only after corner frontier readiness");session.SetMenu(true);world.Refresh(new Vector3(321,0,321));Check(NearWindow(new Vector3(321,0,321))&&world.CaptureStreamingTelemetry().requiredMisses==0,"immediate diagonal east/north crossing after real resume needs no urgent chunks");
            Check(session.Economy.Credits==4321&&session.Profile.garage.Active.engine==2&&Math.Abs(session.Car.Damage-27)<.001f&&Math.Abs(session.Profile.volume-.23f)<.001f,"new-start and resumed preparation preserve credits upgrades damage and settings in isolated career");
            Reset(new Vector3(1,0,1));session.ResumeDrive();Check(session.PreparingStart,"later ResumeDrive also prepares near-boundary roads");session.CancelPreparingStart();for(int i=0;i<3;i++)yield return null;Check(session.Menu&&Time.timeScale==0&&!session.PreparingStart&&!world.DriveFrontierReady,"cancel returns to usable paused menu and stale coroutine cannot unpause later");
            Reset(new Vector3(1,0,160));var timeout=typeof(GeoWorld).GetField("frontierTimeoutSeconds",BindingFlags.Instance|BindingFlags.NonPublic);timeout.SetValue(world,0d);session.ResumeDrive();while(session.PreparingStart)yield return null;timeout.SetValue(world,90d);Check(session.Menu&&Time.timeScale==0&&!world.DriveFrontierReady&&session.PreparingStartStatus.Contains("timed out"),"timeout retains menu and explicitly refuses unready driving");
            Reset(new Vector3(1,0,160));bool cancel=false;var cancelled=world.PrepareDriveFrontier(session.Car.Body.position,(ready,total)=>cancel=true,()=>cancel);Check(!cancelled.MoveNext()&&!world.DriveFrontierReady,"cancellation from progress callback cannot race into a ready result");
            Reset(new Vector3(1,0,160));var interrupted=world.PrepareDriveFrontier(session.Car.Body.position,null);Check(interrupted.MoveNext(),"near-border request starts genuine asynchronous work");world.Initialize(Fixture(),world.Asphalt);Check(!interrupted.MoveNext()&&!world.DriveFrontierReady,"location replacement invalidates old request and releases its priority");
            Reset(new Vector3(160,0,160));world.enabled=false;session.Car.transform.rotation=Quaternion.LookRotation(Vector3.right);world.Refresh(session.Car.Body.position);double until=Time.realtimeSinceStartupAsDouble+.15;while(Time.realtimeSinceStartupAsDouble<until)yield return null;session.Car.transform.rotation=Quaternion.LookRotation(Vector3.left);world.Refresh(session.Car.Body.position);
            var queue=(Queue<EarthUnscripted.Core.ChunkCell>)typeof(GeoWorld).GetField("pendingBuilds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);Check(queue.Count>0&&queue.Peek().X==-4,"material heading reversal reprioritises pending frontier inside the same origin cell");world.enabled=true;
            Check(session.Menu&&Time.timeScale==0,"all cancelled and diagnostic paths finish safely paused");File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS {checks} actual asynchronous startup/resume/frontier/cancel/timeout/career/heading checks\n");Finish(0);
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Frontier verification timed out");if(!EditorApplication.isPlaying)return;
                if(session==null){session=UnityEngine.Object.FindAnyObjectByType<GameSession>();if(!EditorBootReadiness.Ready(session)||!session.Updates){session=null;return;}work.Push(Cases());}
                while(work.Count>0){var item=work.Peek();if(!item.MoveNext()){(item as IDisposable)?.Dispose();work.Pop();continue;}if(item.Current is IEnumerator nested){work.Push(nested);continue;}break;}
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Flag,false);EditorApplication.update-=Tick;Time.timeScale=1;EditorApplication.Exit(code);}
    }
}
