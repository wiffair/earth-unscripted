using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
namespace EarthUnscripted
{
    /// <summary>Explicit local export. No career, credentials, source downloads or pack assets are copied.</summary>
    public sealed class BetaDiagnostics : MonoBehaviour
    {
        [Serializable] sealed class Snapshot
        {
            public string version,platform,graphics,bootStatus,area,utc;
            public bool bootReady,bootFailed;public float x,y,z,speedMph;
            public float frameMilliseconds;public int pendingRoadChunks,pendingSceneryChunks;public string[] recentLog;
        }
        static readonly object gate=new object();static readonly Queue<string> lines=new Queue<string>();
        static Task<string> export;static string status="Export a local report to share with an issue. It includes your current location, without career files or pack assets.";
        public static string Status {get{Poll();return status;}}
        public static bool Busy=>export!=null;
        void OnEnable(){Application.logMessageReceivedThreaded+=Receive;}
        void OnDisable(){Application.logMessageReceivedThreaded-=Receive;}
        static void Receive(string message,string stack,LogType type)
        {lock(gate){string text=DateTime.UtcNow.ToString("O")+" "+type+" "+message+((type==LogType.Exception||type==LogType.Error)?"\n"+stack:"");lines.Enqueue(text.Length>4096?text.Substring(0,4096):text);while(lines.Count>128)lines.Dequeue();}}
        internal static string Redact(string value)
        {
            if(string.IsNullOrEmpty(value))return value??"";
            value=Regex.Replace(value,@"(?i)authorization\s*[:=]\s*[^\r\n]+","Authorization=[redacted]");
            value=Regex.Replace(value,@"(?i)(authorization|api[_-]?key|access[_-]?token|password|secret)\s*[:=]\s*[^\s,;]+","$1=[redacted]");
            value=Regex.Replace(value,@"https?://[^\s""<>]+",m=>{if(Uri.TryCreate(m.Value,UriKind.Absolute,out var uri))return uri.Scheme+"://"+uri.Host+uri.AbsolutePath;return "[URL]";});
            value=Regex.Replace(value,@"(?i)(/Users/|/home/|(?<![A-Za-z])[A-Z]:[\\/])[^\r\n""<>]+","[local path]");
            return value;
        }
        static void Poll()
        {if(export==null||!export.IsCompleted)return;var task=export;export=null;status=task.IsFaulted?"Report could not be exported. Try again.":"Report saved: "+Path.GetFileName(task.Result);}
        public static void Begin(GameSession session)
        {
            Poll();if(export!=null)return;
            var p=session&&session.Car?session.Car.transform.position:Vector3.zero;var renderer=session?.World?.AutomaticWorld?.Renderer;
            var report=new Snapshot{version=ProductIdentity.Version,platform=Application.platform.ToString(),graphics=SystemInfo.graphicsDeviceName,utc=DateTime.UtcNow.ToString("O"),bootReady=session&&session.BootReady,bootFailed=session&&session.BootFailed,bootStatus=Redact(session?session.BootStatus:"No session"),area=session?.World?.Route?.Region?.name??"Not loaded",x=p.x,y=p.y,z=p.z,speedMph=session&&session.Car?session.Car.AbsoluteSpeed*Core.DrivingMath.MphPerMetreSecond:0,frameMilliseconds=Time.unscaledDeltaTime*1000,pendingRoadChunks=renderer?renderer.PendingGeometryChunks:0,pendingSceneryChunks=renderer?renderer.PendingSceneryChunks:0};
            lock(gate){report.recentLog=Array.ConvertAll(lines.ToArray(),Redact);}
            var telemetry=FindFirstObjectByType<BetaDrivingTelemetry>();string timing=telemetry?Redact(telemetry.ExportSnapshot()):null;
            string json=JsonUtility.ToJson(report,true),folder=Path.Combine(SinglePlayerBeta.DataRoot??SinglePlayerBeta.ProductRoot,"Diagnostics");
            export=Task.Run(()=>
            {
                Directory.CreateDirectory(folder);string destination=Path.Combine(folder,"Earth-Unscripted-report-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".zip");
                using(var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
                {using(var writer=new StreamWriter(zip.CreateEntry("diagnostics.json").Open(),new UTF8Encoding(false)))writer.Write(json);if(timing!=null)using(var writer=new StreamWriter(zip.CreateEntry("driving-timing.json").Open(),new UTF8Encoding(false)))writer.Write(timing);}
                return destination;
            });status="Exporting report…";
        }
        public static void DrawFailedActions(GameSession session)
        {
            if(ControllerMenuGUI.Button(new Rect(790,690,330,42),"Export diagnostic report"))Begin(session);
            if(ControllerMenuGUI.Button(new Rect(790,744,330,42),"Open reports folder"))OpenFolder();
            GUI.Label(new Rect(790,798,530,37),Status,new GUIStyle(GUI.skin.label){wordWrap=true,fontSize=13});
        }
        public static void OpenFolder(){string folder=Path.Combine(SinglePlayerBeta.DataRoot??SinglePlayerBeta.ProductRoot,"Diagnostics");try{Directory.CreateDirectory(folder);Application.OpenURL(new Uri(folder).AbsoluteUri);}catch(IOException){status="Could not open the report folder.";}}
    }
}
