using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Security.Cryptography;
namespace EarthUnscripted.Radio
{
    // Pure managed worker work; no Unity objects/APIs and no audio-thread callbacks.
    public static class RadioPcmPreparation
    {
        public const string Revision="nlayer82f7a92e-normalize1";
        public const int BlockFrames=8192,HeaderBytes=80;
        const long InputLimit=64L*1024*1024,CacheLimit=512L*1024*1024;
        static readonly byte[] Magic=Encoding.ASCII.GetBytes("EU-RPCM1");
        static readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public sealed class Prepared
        {
            public readonly string Path;public readonly int Frames,Channels,Frequency;public readonly RadioAudioPolicy.Level Level;
            internal readonly byte[] ContentDigest;
            internal Prepared(string path,int frames,int channels,int frequency,RadioAudioPolicy.Level level,byte[] digest)
            {Path=path;Frames=frames;Channels=channels;Frequency=frequency;Level=level;ContentDigest=(byte[])digest.Clone();}
        }
        sealed class GuardedStream:Stream
        {
            readonly Stream source;readonly CancellationToken token;readonly Stopwatch clock;long readBytes;
            public GuardedStream(Stream value,CancellationToken cancellation,Stopwatch deadline){source=value;token=cancellation;clock=deadline;}
            void Check(){token.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>60)throw new TimeoutException("Radio decode deadline exceeded");}
            public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
            public override long Length{get{Check();return source.Length;}}
            public override long Position{get{Check();return source.Position;}set{Check();if(value<0||value>source.Length)throw new IOException("Radio seek outside source");source.Position=value;}}
            public override int Read(byte[] b,int o,int c){Check();if(c<0||c>1024*1024)throw new IOException("Radio decoder read exceeds bound");int n=source.Read(b,o,c);readBytes+=n;if(readBytes>InputLimit*8)throw new IOException("Radio decoder read budget exceeded");return n;}
            public override int ReadByte(){Check();int value=source.ReadByte();if(value>=0&&++readBytes>InputLimit*8)throw new IOException("Radio decoder read budget exceeded");return value;}
            public override long Seek(long offset,SeekOrigin origin){long value=origin==SeekOrigin.Begin?offset:origin==SeekOrigin.Current?checked(Position+offset):checked(Length+offset);Position=value;return value;}
            public override void Flush(){Check();}public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        }
        static void Check(CancellationToken token,Stopwatch clock){token.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>60)throw new TimeoutException("Radio preparation deadline exceeded");}
        static void OrdinaryFile(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Radio cache/source alias refused");}
        static byte[] Digest(Stream stream,long length,CancellationToken token,Stopwatch clock)
        {
            using(var sha=SHA256.Create())
            {
                var bytes=new byte[65536];long left=length;while(left>0){Check(token,clock);int n=stream.Read(bytes,0,(int)Math.Min(bytes.Length,left));if(n==0)throw new EndOfStreamException();sha.TransformBlock(bytes,0,n,null,0);left-=n;}sha.TransformFinalBlock(Array.Empty<byte>(),0,0);return sha.Hash;
            }
        }
        static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        static byte[] Identity(string hash)
        {if(hash==null||hash.Length!=64||hash.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new ArgumentException("Invalid radio source digest");var bytes=new byte[32];for(int i=0;i<bytes.Length;i++)bytes[i]=Convert.ToByte(hash.Substring(i*2,2),16);return bytes;}
        static void ReadExact(Stream stream,byte[] bytes,int count,CancellationToken token,Stopwatch clock)
        {int offset=0;while(offset<count){Check(token,clock);int n=stream.Read(bytes,offset,count-offset);if(n==0)throw new EndOfStreamException();offset+=n;}}
        static Prepared ReadValid(string path,byte[] identity,bool music,CancellationToken token,Stopwatch clock)
        {
            if(!File.Exists(path))return null;OrdinaryFile(path);
            try
            {
                using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var r=new BinaryReader(f,Encoding.UTF8,true))
                {
                    if(f.Length<HeaderBytes+32||f.Length>HeaderBytes+32+(long)RadioAudioPolicy.BoundedNormalization.MaximumSamples*4)throw new InvalidDataException("Radio PCM cache size");
                    if(!r.ReadBytes(8).SequenceEqual(Magic)||!r.ReadBytes(32).SequenceEqual(identity))throw new InvalidDataException("Radio PCM cache identity");
                    int channels=r.ReadInt32(),frequency=r.ReadInt32(),frames=r.ReadInt32(),kind=r.ReadInt32();
                    var bounded=new RadioAudioPolicy.BoundedNormalization(frames,channels,frequency);
                    if(kind!=(music?1:0)||frames/(double)frequency>1200)throw new InvalidDataException("Radio PCM cache dimensions");
                    var level=new RadioAudioPolicy.Level{inputPeak=r.ReadSingle(),outputPeak=r.ReadSingle(),gain=r.ReadSingle(),gatedRms=r.ReadSingle()};
                    if(new[]{level.inputPeak,level.outputPeak,level.gain,level.gatedRms}.Any(v=>float.IsNaN(v)||float.IsInfinity(v)||v<0)||level.gain>4||level.outputPeak>(music?.65001f:.55001f))throw new InvalidDataException("Radio PCM cache level");
                    long payload=r.ReadInt64();if(payload!=(long)frames*channels*4||f.Length!=HeaderBytes+payload+32)throw new InvalidDataException("Radio PCM cache length");
                    f.Position=0;var digest=Digest(f,HeaderBytes+payload,token,clock);if(!r.ReadBytes(32).SequenceEqual(digest))throw new InvalidDataException("Radio PCM cache checksum");
                    return new Prepared(path,frames,channels,frequency,level,digest);
                }
            }
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException||e is InvalidOperationException){return null;}
        }
        // Call only on an ordinary worker, with the validated library media digest.
        public static Prepared Prepare(string source,string sourceHash,string cacheRoot,bool music,CancellationToken token)
        {
            source=System.IO.Path.GetFullPath(source);cacheRoot=System.IO.Path.GetFullPath(cacheRoot);byte[] identity=Identity(sourceHash);var clock=Stopwatch.StartNew();
            if(!gate.Wait(TimeSpan.FromSeconds(60),token))throw new TimeoutException("Radio preparation queue timed out");
            string temporary=null;
            try
            {
                Check(token,clock);Directory.CreateDirectory(cacheRoot);OrdinaryFile(cacheRoot);OrdinaryFile(source);
                string final=System.IO.Path.Combine(cacheRoot,sourceHash+"-"+Revision+(music?"-song":"-speech")+".pcm");
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    if(input.Length<3||input.Length>InputLimit)throw new InvalidDataException("Radio MP3 input size");
                    if(!Digest(input,input.Length,token,clock).SequenceEqual(identity))throw new InvalidDataException("Radio source checksum mismatch");input.Position=0;
                    var cached=ReadValid(final,identity,music,token,clock);if(cached!=null){File.SetLastWriteTimeUtc(final,DateTime.UtcNow);Prune(cacheRoot,final,new FileInfo(final).Length);return cached;}
                    temporary=System.IO.Path.Combine(cacheRoot,".pending-"+Guid.NewGuid().ToString("N"));
                    using(var guarded=new GuardedStream(input,token,clock))using(var decoder=new NLayer.MpegFile(guarded))
                    {
                        int channels=decoder.Channels,frequency=decoder.SampleRate;long advertised=decoder.Length;
                        if(channels<1||channels>8||frequency<8000||frequency>192000||advertised<=0||advertised%((long)channels*4)!=0||advertised/4>RadioAudioPolicy.BoundedNormalization.MaximumSamples)throw new InvalidDataException("Radio MP3 dimensions");
                        int frames=(int)(advertised/(channels*4));if(frames/(double)frequency>1200)throw new InvalidDataException("Radio MP3 duration exceeds20minutes");
                        var normalization=new RadioAudioPolicy.BoundedNormalization(frames,channels,frequency);
                        var buffer=new float[BlockFrames*channels];var bytes=new byte[buffer.Length*4];
                        RadioAudioPolicy.Level level;byte[] contentDigest;
                        using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                        {
                            output.Write(new byte[HeaderBytes],0,HeaderBytes);int total=0;
                            while(true)
                            {
                                Check(token,clock);int count=decoder.ReadSamples(buffer,0,buffer.Length);if(count==0)break;
                                if(count<0||count>buffer.Length||count%channels!=0||count>frames*channels-total||decoder.Channels!=channels||decoder.SampleRate!=frequency)throw new InvalidDataException("Radio MP3 decoded length/format changed");
                                normalization.AccumulateDc(buffer,count);Buffer.BlockCopy(buffer,0,bytes,0,count*4);output.Write(bytes,0,count*4);total+=count;
                            }
                            if(total!=frames*channels)throw new InvalidDataException("Radio MP3 decoded length mismatch");normalization.FinishDc();
                            for(int pass=0;pass<2;pass++)
                            {
                                output.Position=HeaderBytes;
                                for(int offset=0;offset<total;offset+=buffer.Length)
                                {
                                    int count=Math.Min(buffer.Length,total-offset);long at=output.Position;ReadExact(output,bytes,count*4,token,clock);Buffer.BlockCopy(bytes,0,buffer,0,count*4);
                                    if(pass==0)normalization.Measure(buffer,count);
                                    else{normalization.Apply(buffer,count);Buffer.BlockCopy(buffer,0,bytes,0,count*4);output.Position=at;output.Write(bytes,0,count*4);}
                                }
                                if(pass==0)normalization.FinishMeasurements(music);else normalization.FinishApply();
                            }
                            // Level is deterministic; computed once at the completed energy pass.
                            level=LastLevel(normalization);
                            output.Position=0;using(var w=new BinaryWriter(output,Encoding.UTF8,true))
                            {w.Write(Magic);w.Write(identity);w.Write(channels);w.Write(frequency);w.Write(frames);w.Write(music?1:0);w.Write(level.inputPeak);w.Write(level.outputPeak);w.Write(level.gain);w.Write(level.gatedRms);w.Write((long)total*4);w.Flush();}
                            output.Position=0;var digest=Digest(output,HeaderBytes+(long)total*4,token,clock);output.Write(digest,0,digest.Length);output.Flush(true);contentDigest=digest;
                        }
                        Check(token,clock);Prune(cacheRoot,final,new FileInfo(temporary).Length);
                        if(File.Exists(final))File.Delete(final);File.Move(temporary,final);temporary=null;
                        return new Prepared(final,frames,channels,frequency,level,contentDigest);
                    }
                }
            }
            finally{if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}gate.Release();}
        }
        static RadioAudioPolicy.Level LastLevel(RadioAudioPolicy.BoundedNormalization value)=>value.Result;
        static void Prune(string root,string keep,long incoming)
        {
            var files=Directory.EnumerateFiles(root,"*.pcm").Take(129).Select(p=>new FileInfo(p)).ToArray();if(files.Length>128)throw new IOException("Radio PCM cache directory exceeds bound");
            long total=files.Where(f=>f.FullName!=keep).Sum(f=>f.Length)+incoming;int count=files.Count(f=>f.FullName!=keep)+1;
            foreach(var f in files.OrderBy(f=>f.LastWriteTimeUtc))
            {if(total<=CacheLimit&&count<=128)break;if(f.FullName==keep)continue;OrdinaryFile(f.FullName);long length=f.Length;f.Delete();total-=length;count--;}
            if(total>CacheLimit||count>128)throw new IOException("Radio PCM cache budget unavailable");
        }
        // Four reusable blocks. Producer owns file IO; main thread owns SetData.
        public sealed class Upload:IDisposable
        {
            public sealed class Chunk{public float[] Samples;public int Frame;}
            readonly ConcurrentQueue<Chunk> ready=new ConcurrentQueue<Chunk>();readonly ConcurrentQueue<float[]> free=new ConcurrentQueue<float[]>();
            readonly SemaphoreSlim available=new SemaphoreSlim(4,4);readonly CancellationTokenSource cancel=new CancellationTokenSource();
            readonly Task worker;int disposed;public Task Completion=>worker;
            public Upload(Prepared prepared)
            {
                if(prepared==null)throw new ArgumentNullException(nameof(prepared));
                for(int i=0;i<4;i++)free.Enqueue(new float[BlockFrames*prepared.Channels]);
                worker=Task.Run(()=>
                {
                    var clock=Stopwatch.StartNew();try
                    {
                        OrdinaryFile(prepared.Path);using(var file=new FileStream(prepared.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                        {
                            long payload=(long)prepared.Frames*prepared.Channels*4;
                            if(file.Length!=HeaderBytes+payload+32||!Digest(file,HeaderBytes+payload,cancel.Token,clock).SequenceEqual(prepared.ContentDigest))throw new InvalidDataException("Radio prepared file changed before upload");
                            file.Position=HeaderBytes;var bytes=new byte[BlockFrames*prepared.Channels*4];
                            for(int frame=0;frame<prepared.Frames;frame+=BlockFrames)
                            {
                                Check(cancel.Token,clock);if(!available.Wait(TimeSpan.FromSeconds(Math.Max(.001,60-clock.Elapsed.TotalSeconds)),cancel.Token))throw new TimeoutException("Radio upload consumer deadline exceeded");if(!free.TryDequeue(out var samples))throw new InvalidOperationException("Radio upload block ownership");
                                int count=Math.Min(BlockFrames,prepared.Frames-frame)*prepared.Channels;ReadExact(file,bytes,count*4,cancel.Token,clock);
                                if(samples.Length!=count)samples=new float[count];Buffer.BlockCopy(bytes,0,samples,0,count*4);ready.Enqueue(new Chunk{Samples=samples,Frame=frame});
                            }
                        }
                    }
                    finally{available.Dispose();}
                });
                worker.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);
            }
            public bool Empty=>ready.IsEmpty;
            public bool TryTake(out Chunk chunk)=>ready.TryDequeue(out chunk);
            public void Release(Chunk chunk)
            {
                if(worker.IsCompleted)return;free.Enqueue(chunk.Samples);try{available.Release();}catch(ObjectDisposedException){}
            }
            public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;cancel.Cancel();worker.ContinueWith(t=>cancel.Dispose(),TaskContinuationOptions.ExecuteSynchronously);}
        }
    }
}
