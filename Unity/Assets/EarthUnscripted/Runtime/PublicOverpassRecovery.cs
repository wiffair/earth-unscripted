using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Run only after shipped providers fail. New URLs never enter UnityWebRequest/curl.
    public static class PublicOverpassRecovery
    {
        public static Task<string> Fetch(string query,string[] attempted,double[] bounds,string cache,CancellationToken cancellation)
        {
            bool windows=Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor;
            string helpers=Path.GetFullPath(Path.Combine(Application.dataPath,Application.platform==RuntimePlatform.OSXPlayer?"Resources/AutomaticWorld":"../Helpers/AutomaticWorld"));
#if UNITY_EDITOR
            helpers=Path.GetFullPath(Path.Combine(Application.dataPath,windows?"../AutomaticWorldWindowsWorker":"../AutomaticWorldWorker"));
#endif
            string program=Path.Combine(helpers,windows?"Python/python.exe":"EarthUnscriptedWorldWorker");
            var workTask=Task.Run(()=>
            {
                string work=Path.Combine(Path.GetTempPath(),"EarthUnscriptedPublicMap-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
                string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
                try
                {
                    // Same packaged-worker file boundary used by AutomaticWorldHost.
                    var pins=JObject.Parse(File.ReadAllText(Path.Combine(helpers,"package-files.json")));
                    foreach(var pin in pins.Properties())
                    {
                        cancellation.ThrowIfCancellationRequested();string file=Path.GetFullPath(Path.Combine(helpers,pin.Name));
                        if(!file.StartsWith(helpers+Path.DirectorySeparatorChar,windows?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||!File.Exists(file))throw new InvalidDataException("Public map helper file check failed");
                        using(var stream=File.OpenRead(file))using(var sha=System.Security.Cryptography.SHA256.Create())
                            if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=(string)pin.Value)throw new InvalidDataException("Public map helper digest differs");
                    }
                    string request=Path.Combine(work,"request.json");
                    File.WriteAllText(request,new JObject{{"query",query},{"attempted",new JArray(attempted)},{"bounds",new JArray(bounds)},{"cache",Path.Combine(cache,"PublicOverpassDirectory")}}.ToString());
                    string prefix=windows?"-X utf8 "+Quote(Path.Combine(helpers,"_internal/worker_entry.py"))+" ":"";
                    RunWorker(program,prefix+"--public-map-fallback --request "+Quote(request),cancellation,TimeSpan.FromSeconds(30));
                    string output=Path.Combine(work,"map.json");if(!File.Exists(output)||new FileInfo(output).Length>16000000)throw new InvalidDataException("Public map response byte cap");
                    return File.ReadAllText(output);
                }
                finally{try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
            },cancellation);
            // Observe even when scene destruction abandons the yielding owner.
            workTask.ContinueWith(task=>{var observed=task.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            return workTask;
        }
        internal static void RunWorker(string program,string arguments,CancellationToken cancellation,TimeSpan deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            using(var process=Process.Start(new ProcessStartInfo(program,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}))
            {
                var error=process.StandardError.ReadToEndAsync();var began=Stopwatch.StartNew();
                try
                {
                    while(!process.WaitForExit(50)){cancellation.ThrowIfCancellationRequested();if(began.Elapsed>deadline)throw new TimeoutException("Public map recovery deadline");}
                    cancellation.ThrowIfCancellationRequested();if(!error.Wait(1000))throw new TimeoutException("Public map stderr drain deadline");error.GetAwaiter().GetResult();
                    if(process.ExitCode!=0)throw new IOException("Public map recovery unavailable");
                }
                finally
                {
                    try{if(!process.HasExited){process.Kill();process.WaitForExit(1000);}}catch(InvalidOperationException){}
                    error.ContinueWith(task=>{var observed=task.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                }
            }
        }
    }
}
