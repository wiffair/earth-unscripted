using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using EarthUnscripted.Radio;
namespace EarthUnscripted.Editor
{
    public static class Beta22RadioPcmVerification
    {
        public static int Exercise()
        {
            int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception("Radio PCM preparation: "+message);}
            string root=Path.Combine(Path.GetTempPath(),"EarthUnscriptedRadioPcm-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
            try
            {
                var mpeg=new byte[417*8];for(int i=0;i<8;i++){mpeg[i*417]=255;mpeg[i*417+1]=251;mpeg[i*417+2]=144;}
                string input=Path.Combine(root,"fixture.mp3");File.WriteAllBytes(input,mpeg);string identity=Hash(input),cache=Path.Combine(root,"cache");
                var prepared=RadioPcmPreparation.Prepare(input,identity,cache,true,CancellationToken.None);
                Check(prepared.Channels==2&&prepared.Frequency==44100&&prepared.Frames==8*1152,"real decoder dimensions from8 silent MPEG frames");
                Check(prepared.Level.gain==1&&prepared.Level.outputPeak==0,"decoded silence remains silent");
                string original=Hash(prepared.Path);var hit=RadioPcmPreparation.Prepare(input,identity,cache,true,CancellationToken.None);Check(Hash(hit.Path)==original,"verified cache hit");
                foreach(int at in new[]{40,80})
                {using(var f=new FileStream(prepared.Path,FileMode.Open,FileAccess.Write)){f.Position=at;f.WriteByte(255);}prepared=RadioPcmPreparation.Prepare(input,identity,cache,true,CancellationToken.None);Check(Hash(prepared.Path)==original,"metadata/payload tampering causes regeneration");}
                int frames=0;using(var upload=new RadioPcmPreparation.Upload(prepared))
                {
                    var deadline=DateTime.UtcNow.AddSeconds(5);
                    while(frames<prepared.Frames)
                    {
                        while(upload.TryTake(out var chunk)){Check(chunk.Frame==frames&&chunk.Samples.All(v=>v==0),"bounded upload order/content");frames+=chunk.Samples.Length/prepared.Channels;upload.Release(chunk);}
                        if(upload.Completion.IsFaulted)throw upload.Completion.Exception;if(DateTime.UtcNow>deadline)throw new Exception("PCM upload deadline");Thread.Sleep(1);
                    }
                    upload.Completion.GetAwaiter().GetResult();Check(frames==prepared.Frames,"full and partial final upload blocks");
                }
                File.SetLastWriteTimeUtc(prepared.Path,new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc));RadioPcmPreparation.Prepare(input,identity,cache,true,CancellationToken.None);Check(File.GetLastWriteTimeUtc(prepared.Path)>DateTime.UtcNow.AddMinutes(-1),"cache hit refreshes LRU timestamp");
                for(int i=0;i<127;i++){string extra=Path.Combine(cache,"fixture-"+i+".pcm");File.WriteAllBytes(extra,new byte[]{1});File.SetLastWriteTimeUtc(extra,new DateTime(2000,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(i));}
                var speech=RadioPcmPreparation.Prepare(input,identity,cache,false,CancellationToken.None);Check(Directory.EnumerateFiles(cache,"*.pcm").Count()==128&&File.Exists(prepared.Path)&&File.Exists(speech.Path),"128-entry budget retains song and speech, evicts oldest extra");
                foreach(string extra in Directory.EnumerateFiles(cache,"fixture-*.pcm"))File.Delete(extra);
                string sparsePath=Path.Combine(cache,"budget.pcm");using(var sparse=File.Create(sparsePath))sparse.SetLength(512L*1024*1024);File.SetLastWriteTimeUtc(sparsePath,new DateTime(2000,1,1));RadioPcmPreparation.Prepare(input,identity,cache,true,CancellationToken.None);Check(!File.Exists(sparsePath)&&File.Exists(prepared.Path),"512MiB disk budget prunes oldest even on cache hit");
                var cancel=new CancellationTokenSource();cancel.Cancel();bool stopped=false;try{RadioPcmPreparation.Prepare(input,identity,cache,true,cancel.Token);}catch(OperationCanceledException){stopped=true;}finally{cancel.Dispose();}Check(stopped,"pre-cancelled worker exits");
                bool identityRejected=false;try{RadioPcmPreparation.Prepare(input,new string('0',64),cache,true,CancellationToken.None);}catch(InvalidDataException){identityRejected=true;}Check(identityRejected,"source checksum rejection");
                var uploadCancel=new RadioPcmPreparation.Upload(prepared);uploadCancel.Dispose();uploadCancel.Dispose();try{uploadCancel.Completion.GetAwaiter().GetResult();}catch(OperationCanceledException){}Check(uploadCancel.Completion.IsCompleted,"idempotent upload cancellation closes worker");
                System.Text.Encoding.ASCII.GetBytes("VBRI").CopyTo(mpeg,36);mpeg[54]=127;mpeg[55]=255;mpeg[58]=127;mpeg[59]=255;
                using(var hostile=new NLayer.MpegFile(new MemoryStream(mpeg)))Check(hostile.Length==8L*1152*2*4,"hostile VBRI table rejected before allocation");
                File.WriteAllBytes(input,new byte[1024]);bool invalid=false;try{RadioPcmPreparation.Prepare(input,Hash(input),cache,true,CancellationToken.None);}catch(InvalidDataException){invalid=true;}Check(invalid,"malformed decoder input rejected");
                Check(!Directory.EnumerateFiles(cache,".pending-*").Any(),"all failure/cancel temp files cleaned");
                return checks;
            }
            finally{Directory.Delete(root,true);}
        }
        public static void Run()=>Console.WriteLine("BETA22_RADIO_PCM_PASS "+Exercise());
    }
}
