using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace EarthUnscripted
{
    // Owned, bounded OS HTTPS client. No shell, credentials or TLS bypass.
    public static class BackgroundHttps
    {
        public static Task Fetch(string program,string url,string output,int maximum,CancellationToken cancellation)
        {
            return Task.Factory.StartNew(()=>
            {
                string partial=output+".partial";Directory.CreateDirectory(Path.GetDirectoryName(output));
                string Quote(string text)=>"\""+text.Replace("\"","\\\"")+"\"";
                var oldPriority=Thread.CurrentThread.Priority;
                try
                {
                    Thread.CurrentThread.Priority=ThreadPriority.BelowNormal;cancellation.ThrowIfCancellationRequested();
                    if(File.Exists(partial))File.Delete(partial);
                    var arguments="--silent --show-error --fail --location --proto =https --proto-redir =https --connect-timeout 5 --max-time 180 --limit-rate 128k --max-filesize "+maximum+" --user-agent EarthUnscripted-PersonalPrototype/0.9 --output "+Quote(partial)+" "+Quote(new Uri(url).AbsoluteUri);
                    using(var process=Process.Start(new ProcessStartInfo(program,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}))
                    {
                        try
                        {
                            while(!process.WaitForExit(100))
                            {
                                cancellation.ThrowIfCancellationRequested();
                                if(File.Exists(partial)&&new FileInfo(partial).Length>maximum)throw new InvalidDataException("Background response exceeded its byte budget");
                            }
                            cancellation.ThrowIfCancellationRequested();
                            if(process.ExitCode!=0)throw new IOException("HTTPS update unavailable: "+process.StandardError.ReadToEnd());
                            if(!File.Exists(partial)||new FileInfo(partial).Length>maximum)throw new InvalidDataException("Background response exceeds its byte budget");
                        }
                        finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(1000);}}catch(InvalidOperationException){} }
                    }
                    if(File.Exists(output))File.Delete(output);File.Move(partial,output);
                }
                finally{Thread.CurrentThread.Priority=oldPriority;if(File.Exists(partial))File.Delete(partial);}
            },cancellation,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        }
    }
}
