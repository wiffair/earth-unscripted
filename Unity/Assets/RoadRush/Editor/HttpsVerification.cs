using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class HttpsVerification
    {
        static Task work;static CancellationTokenSource cancellation;static double deadline;static string path;static int phase;static ReplicationState state;
        public static void Run()
        {
            path=Path.GetFullPath("Builds/Verification/https-"+DateTime.UtcNow.Ticks+".txt");Directory.CreateDirectory(Path.GetDirectoryName(path));cancellation=new CancellationTokenSource();deadline=EditorApplication.timeSinceStartup+35;
            work=BackgroundHttps.Fetch("/usr/bin/curl","https://download.geofabrik.de/europe/united-kingdom-updates/state.txt",path,16384,cancellation.Token);EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline){cancellation.Cancel();if(!work.IsCompleted)return;throw new Exception("HTTPS proof timed out");}if(!work.IsCompleted)return;
                if(phase==0)
                {
                    if(work.IsFaulted)throw work.Exception.GetBaseException();if(work.IsCanceled)throw new Exception("HTTPS request cancelled");state=ReplicationState.Parse(File.ReadAllText(path));
                    Debug.Log("HTTPS PASS: actual verified-TLS system client retrieved the official UK replication state");
                    work=BackgroundHttps.Fetch("/usr/bin/curl","http://127.0.0.1/blocked-by-https-policy",path+"-blocked",16384,cancellation.Token);phase=1;return;
                }
                if(!work.IsFaulted||File.Exists(path+"-blocked")||File.Exists(path+"-blocked.partial"))throw new Exception("HTTPS-only policy or partial-file cleanup failed");
                Debug.Log("HTTPS PASS: non-HTTPS request rejected and partial file cleaned");File.WriteAllText("Builds/Verification/background-https.txt",$"PASS2actual-system-client checks\nUK source sequence={state.sequence}\ntimestamp={state.timestamp}\nNo country database modified.\n");Finish(0);
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){cancellation.Cancel();EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
