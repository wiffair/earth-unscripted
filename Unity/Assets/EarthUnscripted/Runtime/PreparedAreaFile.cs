using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EarthUnscripted
{
    // The request owns this private file until ParseAsync queues its worker.
    // Disposing an abandoned coroutine cannot unlink a worker's active input.
    internal sealed class PreparedAreaFile:IDisposable
    {
        readonly string path;
        int ownership; //0 request,1 worker,2 released
        public string FilePath=>path;
        PreparedAreaFile(string path){this.path=path;}
        public static PreparedAreaFile Create(string directory)
        {
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".prepared-area");
            using(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
            return new PreparedAreaFile(path);
        }
        public Task<T> ParseAsync<T>(string sha256,int bytes,Func<string,T> parse,CancellationToken cancellation=default)
        {
            if(parse==null)throw new ArgumentNullException(nameof(parse));
            if(Interlocked.CompareExchange(ref ownership,1,0)!=0)throw new InvalidOperationException("Prepared area file ownership already transferred");
            try
            {
                // Do not pass cancellation to Task.Run: even a pre-cancelled
                // read must enter the delegate and execute its cleanup finally.
                return Task.Run(()=>
                {
                    try{return parse(VerifiedUtf8File.Read(path,sha256,bytes,cancellation));}
                    finally{try{File.Delete(path);}finally{Interlocked.Exchange(ref ownership,2);}}
                });
            }
            catch{Interlocked.CompareExchange(ref ownership,0,1);throw;}
        }
        public void Dispose()
        {
            if(Interlocked.CompareExchange(ref ownership,2,0)==0)File.Delete(path);
        }
    }

    internal static class VerifiedUtf8File
    {
        internal const int BufferBytes=16*1024;
        sealed class FillState
        {
            public FileStream stream;public byte[] bytes;public string sha256;public int length;public CancellationToken cancellation;
        }
        static void Verify(FileStream stream,IncrementalHash hash,long bytes,string expectedHash,int expectedBytes)
        {
            string digest=BitConverter.ToString(hash.GetHashAndReset()).Replace("-","").ToLowerInvariant();
            if(bytes!=expectedBytes||stream.Length!=expectedBytes||digest!=expectedHash)throw new InvalidDataException("Prepared area hash/size mismatch");
        }
        static int Count(Decoder decoder,byte[] bytes,int length,char[] scratch,bool flush)
        {
            int consumed=0,characters=0;bool complete;
            do
            {
                decoder.Convert(bytes,consumed,length-consumed,scratch,0,scratch.Length,flush,out int used,out int written,out complete);
                consumed+=used;characters=checked(characters+written);
                if(!complete&&used==0&&written==0)throw new InvalidDataException("Prepared UTF8 decoder made no progress");
            }while(!complete);
            if(consumed!=length)throw new InvalidDataException("Prepared UTF8 input was not fully consumed");
            return characters;
        }
        static void FillChunk(Decoder decoder,ReadOnlySpan<byte> input,Span<char> output,ref int position,bool flush)
        {
            int consumed=0;bool complete;
            do
            {
                decoder.Convert(input.Slice(consumed),output.Slice(position),flush,out int used,out int written,out complete);
                consumed+=used;position=checked(position+written);
                if(!complete&&used==0&&written==0)throw new InvalidDataException("Prepared UTF8 decoder made no progress");
            }while(!complete);
            if(consumed!=input.Length)throw new InvalidDataException("Prepared UTF8 input was not fully consumed");
        }
        static void Fill(Span<char> output,FillState state)
        {
            state.stream.Position=0;var decoder=Encoding.UTF8.GetDecoder();int position=0;long total=0;
            using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                int read;
                while(true)
                {
                    state.cancellation.ThrowIfCancellationRequested();read=state.stream.Read(state.bytes,0,state.bytes.Length);if(read==0)break;
                    total+=read;hash.AppendData(state.bytes,0,read);FillChunk(decoder,state.bytes.AsSpan(0,read),output,ref position,false);
                }
                FillChunk(decoder,ReadOnlySpan<byte>.Empty,output,ref position,true);
                if(position!=output.Length)throw new InvalidDataException("Prepared UTF8 character count changed");
                Verify(state.stream,hash,total,state.sha256,state.length);
            }
        }
        public static string Read(string path,string sha256,int bytes,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.SequentialScan))
            {
                if(bytes<0||stream.Length!=bytes)throw new InvalidDataException("Prepared area hash/size mismatch");
                var buffer=new byte[BufferBytes];var scratch=new char[BufferBytes];var decoder=Encoding.UTF8.GetDecoder();int characters=0;long total=0;
                using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    int read;
                    while(true)
                    {
                        cancellation.ThrowIfCancellationRequested();read=stream.Read(buffer,0,buffer.Length);if(read==0)break;
                        total+=read;hash.AppendData(buffer,0,read);characters=checked(characters+Count(decoder,buffer,read,scratch,false));
                    }
                    characters=checked(characters+Count(decoder,Array.Empty<byte>(),0,scratch,true));
                    Verify(stream,hash,total,sha256,bytes);
                }
                var state=new FillState{stream=stream,bytes=buffer,sha256=sha256,length=bytes,cancellation=cancellation};
                if(characters==0){Fill(Span<char>.Empty,state);return string.Empty;}
                return string.Create(characters,state,Fill);
            }
        }
    }
}
