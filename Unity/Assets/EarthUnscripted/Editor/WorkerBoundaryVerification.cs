using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthUnscripted.Editor
{
    public static class WorkerBoundaryVerification
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Debug.Log("PASS "+name);}
        static void Reject(Action action,string name){try{action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception(name);}
        public static void Run()
        {
            try
            {
                const int port=28001;
                Reject(()=>WorkerHttp.Register(port,"short"),"reject malformed capability");
                WorkerHttp.Register(port,new string('a',64));
                using(var request=WorkerHttp.Authorize(UnityWebRequest.Get("http://127.0.0.1:28001/health")))
                    Check(request.GetRequestHeader("X-Earth-Session")==new string('a',64)&&request.redirectLimit==0,"authorize owned loopback without redirects");
                WorkerHttp.Register(port,new string('b',64));
                using(var request=WorkerHttp.Authorize(UnityWebRequest.Get("http://127.0.0.1:28001/job/test")))
                    Check(request.GetRequestHeader("X-Earth-Session")==new string('b',64),"recovery replaces capability for later requests");
                Reject(()=>WorkerHttp.Authorize(UnityWebRequest.Get("https://example.invalid:28001/health")),"never disclose capability to remote host");
                Reject(()=>WorkerHttp.Authorize(UnityWebRequest.Get("http://127.0.0.1:28002/health")),"reject unowned port");
                WorkerHttp.Forget(port);
                Reject(()=>WorkerHttp.Authorize(UnityWebRequest.Get("http://127.0.0.1:28001/health")),"retired session is forgotten");
                Debug.Log("Worker client boundary: "+checks+" checks passed");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
