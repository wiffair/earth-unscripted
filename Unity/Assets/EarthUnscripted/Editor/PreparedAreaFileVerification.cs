using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using World=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
 public static class PreparedAreaFileVerification
 {
  static readonly BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
  static readonly Type FileType=typeof(GameSession).Assembly.GetType("EarthUnscripted.PreparedAreaFile"),Utf8Type=typeof(GameSession).Assembly.GetType("EarthUnscripted.VerifiedUtf8File");
  static int checks;static string root;
  static void Check(bool value,string message){checks++;if(!value)throw new Exception("PREPARED AREA FILE: "+message);}
  static string Hash(byte[] input){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
  static string HashFile(string path){using(var input=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
  static object Create()=>FileType.GetMethod("Create",Flags).Invoke(null,new object[]{root});
  static string PathOf(object file)=>(string)FileType.GetProperty("FilePath",Flags).GetValue(file);
  static Task<T> Parse<T>(object file,string hash,int bytes,Func<string,T> parser,CancellationToken cancellation=default)=>(Task<T>)FileType.GetMethod("ParseAsync",Flags).MakeGenericMethod(typeof(T)).Invoke(file,new object[]{hash,bytes,parser,cancellation});
  static string Read(string path,string hash,int bytes,CancellationToken cancellation=default)=>(string)Utf8Type.GetMethod("Read",Flags).Invoke(null,new object[]{path,hash,bytes,cancellation});
  static void Dispose(object file)=>((IDisposable)file).Dispose();
  static Type Failure(Action action){try{action();return null;}catch(Exception error){return error.GetBaseException().GetType();}}
  static void Text(byte[] bytes,string label)
  {
   string path=System.IO.Path.Combine(root,"utf8.bin");File.WriteAllBytes(path,bytes);string expected=Encoding.UTF8.GetString(bytes),actual=Read(path,Hash(bytes),bytes.Length);Check(actual==expected,label+" exact UTF8.GetString code units");
  }
  static void Utf8()
  {
   foreach(string text in new[]{"","ASCII\r\n map","\uFEFF{\"name\":\"BOM retained\"}","Newbridge, Cymru: ŵ ê; Reykjavík; 東京; 🌍🚐"})Text(Encoding.UTF8.GetBytes(text),"text "+text.Length);
   foreach(var invalid in new[]{new byte[]{0xE2},new byte[]{0xE2,0x82},new byte[]{0xF0,0x9F,0x8C},new byte[]{0xC0,0xAF},new byte[]{0xED,0xA0,0x80},new byte[]{0xFF,0x80,0xBF},new byte[]{0xF4,0x90,0x80,0x80}})Text(invalid,"invalid/truncated "+BitConverter.ToString(invalid));
   int chunk=(int)Utf8Type.GetField("BufferBytes",Flags).GetRawConstantValue();
   foreach(int offset in Enumerable.Range(-4,9))foreach(var suffix in new[]{new byte[]{0xE2,0x82,0xAC},new byte[]{0xF0,0x9F,0x8C,0x8D},new byte[]{0xED,0xA0,0x80},new byte[]{0xE2},new byte[]{0xC2}})
    Text(Enumerable.Repeat((byte)'a',chunk+offset).Concat(suffix).Concat(Enumerable.Repeat((byte)'b',chunk)).ToArray(),"chunk boundary "+offset);
   // Pending invalid prefix plus a full next chunk can produce chunk+1 chars.
   Text(Enumerable.Repeat((byte)'a',chunk-1).Concat(new byte[]{0xE2}).Concat(Enumerable.Repeat((byte)'b',chunk)).ToArray(),"pending prefix + full ASCII scratch overflow");
   Text(Enumerable.Range(0,256).Select(i=>(byte)i).ToArray(),"every byte value");var random=new System.Random(12409);for(int i=0;i<80;i++){var bytes=new byte[i*37+1];random.NextBytes(bytes);Text(bytes,"random replacement "+i);}
   byte[] valid=Encoding.UTF8.GetBytes("\uFEFF🌍abc\uFFFD");string path=System.IO.Path.Combine(root,"reject.bin");File.WriteAllBytes(path,valid);
   Check(Failure(()=>Read(path,new string('0',64),valid.Length))==typeof(InvalidDataException),"Wrong hash rejected before parse");
   Check(Failure(()=>Read(path,Hash(valid),valid.Length-1))==typeof(InvalidDataException),"Wrong size rejected");Check(Failure(()=>Read(path,Hash(valid),-1))==typeof(InvalidDataException),"Negative protocol size rejected");
   File.WriteAllBytes(path,valid.Take(valid.Length-1).ToArray());Check(Failure(()=>Read(path,Hash(valid),valid.Length))==typeof(InvalidDataException),"Truncated file rejected");File.Delete(path);
   Check(Failure(()=>Read(path,Hash(valid),valid.Length))==typeof(FileNotFoundException),"Missing input reports IO failure");
   var cancelled=new CancellationTokenSource();cancelled.Cancel();Check(Failure(()=>Read(path,Hash(valid),valid.Length,cancelled.Token))==typeof(OperationCanceledException),"Cancellation checked before IO");
  }
  static string FillTest<T>(int count,object state)
  {
   var action=(System.Buffers.SpanAction<char,T>)Delegate.CreateDelegate(typeof(System.Buffers.SpanAction<char,T>),Utf8Type.GetMethod("Fill",Flags));return string.Create(count,(T)state,action);
  }
  static void ChangedSecondPass()
  {
   byte[] original=Encoding.UTF8.GetBytes("{\"value\":1}"),changed=Encoding.UTF8.GetBytes("{\"value\":2}");string path=System.IO.Path.Combine(root,"changed-pass.json");File.WriteAllBytes(path,changed);
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
   {
    var type=Utf8Type.GetNestedType("FillState",Flags);object state=Activator.CreateInstance(type,true);
    type.GetField("stream",Flags).SetValue(state,stream);type.GetField("bytes",Flags).SetValue(state,new byte[16384]);type.GetField("sha256",Flags).SetValue(state,Hash(original));type.GetField("length",Flags).SetValue(state,original.Length);type.GetField("cancellation",Flags).SetValue(state,CancellationToken.None);
    var fill=typeof(PreparedAreaFileVerification).GetMethod("FillTest",Flags).MakeGenericMethod(type);
    Check(Failure(()=>fill.Invoke(null,new object[]{original.Length,state}))==typeof(InvalidDataException),"Second pass independently rejects same-size changed contents before parser");
   }
  }
  static void Ownership()
  {
   byte[] bytes=Encoding.UTF8.GetBytes("{\"value\":\"🌍\"}");string hash=Hash(bytes);
   object file=Create();string path=PathOf(file);Check(File.Exists(path),"Fresh GUID path reserved");Dispose(file);Dispose(file);Check(!File.Exists(path),"Pre-transfer disposal removes its own file idempotently");Check(Failure(()=>Parse(file,hash,bytes.Length,text=>text.Length))==typeof(InvalidOperationException),"Disposed file cannot transfer");
   using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
   {
    file=Create();path=PathOf(file);File.WriteAllBytes(path,bytes);var task=Parse(file,hash,bytes.Length,text=>{entered.Set();if(!release.Wait(5000))throw new TimeoutException();return text;});
    Check(entered.Wait(5000),"Worker owns input and entered parser");Dispose(file);Check(File.Exists(path),"Coroutine disposal cannot unlink a transferred worker input");Check(Failure(()=>Parse(file,hash,bytes.Length,text=>text.Length))==typeof(InvalidOperationException),"Transfer occurs only once");release.Set();Check(task.GetAwaiter().GetResult()==Encoding.UTF8.GetString(bytes)&&!File.Exists(path),"Worker success cleans file after exact decode");
   }
   using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var cancellationAfterTransfer=new CancellationTokenSource())
   {
    file=Create();path=PathOf(file);File.WriteAllBytes(path,bytes);var task=Parse(file,hash,bytes.Length,text=>{entered.Set();if(!release.Wait(5000))throw new TimeoutException();cancellationAfterTransfer.Token.ThrowIfCancellationRequested();return text.Length;},cancellationAfterTransfer.Token);
    Check(entered.Wait(5000),"Cancellation fixture reached transferred worker");Dispose(file);cancellationAfterTransfer.Cancel();Check(File.Exists(path),"Cancellation and coroutine disposal do not unlink active parser input");release.Set();Check(typeof(OperationCanceledException).IsAssignableFrom(Failure(()=>task.GetAwaiter().GetResult()))&&!File.Exists(path),"Post-transfer cancellation executes worker cleanup finally");
   }
   foreach(bool hashFailure in new[]{false,true})
   {
    file=Create();path=PathOf(file);File.WriteAllBytes(path,bytes);int parsed=0;var task=Parse<int>(file,hashFailure?new string('0',64):hash,bytes.Length,text=>{Interlocked.Increment(ref parsed);throw new IOException("Intentional parser failure");});Dispose(file);
    Check(Failure(()=>task.GetAwaiter().GetResult())==(hashFailure?typeof(InvalidDataException):typeof(IOException)),"Worker failure propagates");Check(!File.Exists(path)&&parsed==(hashFailure?0:1),"Verification precedes parser and failure finally unlinks");
   }
   var cancellation=new CancellationTokenSource();cancellation.Cancel();file=Create();path=PathOf(file);File.WriteAllBytes(path,bytes);var cancelled=Parse(file,hash,bytes.Length,text=>text.Length,cancellation.Token);Dispose(file);Check(typeof(OperationCanceledException).IsAssignableFrom(Failure(()=>cancelled.GetAwaiter().GetResult()))&&!File.Exists(path),"Pre-cancelled worker still enters deletion finally");
   file=Create();path=PathOf(file);File.Delete(path);var missing=Parse(file,hash,bytes.Length,text=>text.Length);Check(Failure(()=>missing.GetAwaiter().GetResult())==typeof(FileNotFoundException)&&!File.Exists(path),"Worker IO failure preserves cleanup");Dispose(file);
  }
  sealed class LocalServer:IDisposable
  {
   readonly TcpListener listener;readonly Task work;readonly ManualResetEventSlim release=new ManualResetEventSlim();readonly string source,token;readonly bool pause;
   public int Port {get;}
   public LocalServer(string source,string token,bool pause=false)
   {
    this.source=source;this.token=token;this.pause=pause;listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;
    work=Task.Run(()=>
    {
     try
     {
      using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream())
      {
       client.ReceiveTimeout=5000;client.SendTimeout=5000;var request=new StringBuilder();int next;
       while(request.Length<16384&&(next=stream.ReadByte())>=0){request.Append((char)next);if(request.ToString().EndsWith("\r\n\r\n",StringComparison.Ordinal))break;}
       if(!request.ToString().Contains("X-Earth-Session: "+token))throw new Exception("Capability header missing");
       byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+new FileInfo(source).Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);
       using(var input=File.OpenRead(source)){var buffer=new byte[16384];int read;bool first=true;while((read=input.Read(buffer,0,buffer.Length))>0){stream.Write(buffer,0,read);if(pause&&first){first=false;release.Wait(10000);}}}
      }
     }
     catch(Exception error)when(pause&&(error is IOException||error is SocketException||error is ObjectDisposedException)){}
    });
   }
   public void Finish(){release.Set();if(!work.Wait(15000))throw new TimeoutException("Local server did not complete");work.GetAwaiter().GetResult();}
   public void Dispose(){release.Set();listener.Stop();try{if(!work.Wait(15000))throw new TimeoutException();work.GetAwaiter().GetResult();}finally{release.Dispose();}}
  }
  static void Await(UnityWebRequestAsyncOperation operation)
  {var clock=System.Diagnostics.Stopwatch.StartNew();while(!operation.isDone){if(clock.ElapsedMilliseconds>15000)throw new TimeoutException("Local request did not complete");Thread.Sleep(1);}}
  static void Network(string source)
  {
   string token=new string('a',64);object file=Create();string path=PathOf(file);
   using(var server=new LocalServer(source,token))
   {
    WorkerHttp.Register(server.Port,token);
    try
    {
     using(var download=WorkerHttp.Authorize(new UnityWebRequest("http://127.0.0.1:"+server.Port+"/area","GET")))
     {
      download.downloadHandler=new DownloadHandlerFile(path){removeFileOnAbort=true};download.timeout=10;Check(download.redirectLimit==0,"Owned worker request still refuses redirects");Await(download.SendWebRequest());Check(download.result==UnityWebRequest.Result.Success,"Native file download succeeds with capability header");
     }
     server.Finish();Check(new FileInfo(path).Length==new FileInfo(source).Length&&HashFile(path)==HashFile(source),"Native downloaded file is byte-identical");
     var parsed=Parse(file,HashFile(source),checked((int)new FileInfo(source).Length),text=>text.Length);Dispose(file);Check(parsed.GetAwaiter().GetResult()==Encoding.UTF8.GetString(File.ReadAllBytes(source)).Length&&!File.Exists(path),"Closed native download passes worker decode and cleanup");
    }
    finally{WorkerHttp.Forget(server.Port);Dispose(file);}
   }
   string abortSource=System.IO.Path.Combine(root,"abort-source");using(var stream=File.Create(abortSource)){stream.SetLength(1024*1024);}
   file=Create();path=PathOf(file);
   using(var server=new LocalServer(abortSource,token,true))
   {
    WorkerHttp.Register(server.Port,token);
    try
    {
     using(var download=WorkerHttp.Authorize(new UnityWebRequest("http://127.0.0.1:"+server.Port+"/area","GET")))
     {
      download.downloadHandler=new DownloadHandlerFile(path){removeFileOnAbort=true};var operation=download.SendWebRequest();var clock=System.Diagnostics.Stopwatch.StartNew();while(download.downloadedBytes==0&&!operation.isDone){if(clock.ElapsedMilliseconds>10000)throw new TimeoutException();Thread.Sleep(1);}Check(download.downloadedBytes>0&&!operation.isDone,"Abort case has a real partial native file download");download.Abort();Await(operation);
     }
     Check(!File.Exists(path),"DownloadHandlerFile removes its aborted native file");Dispose(file);Check(!File.Exists(path),"Coroutine cleanup tolerates native abort deletion");server.Finish();
    }
    finally{WorkerHttp.Forget(server.Port);Dispose(file);}
   }
  }
  static void Real(string path)
  {
   byte[] bytes=File.ReadAllBytes(path);string expected=Encoding.UTF8.GetString(bytes);var expectedMap=JsonUtility.FromJson<World.Map>(expected);object file=Create();string downloaded=PathOf(file);File.Copy(path,downloaded,true);
   var task=Parse(file,Hash(bytes),bytes.Length,JsonUtility.FromJson<World.Map>);Dispose(file);var actual=task.GetAwaiter().GetResult();
   Check(Read(path,Hash(bytes),bytes.Length)==expected,"Explicit real fixture exact decoded code units");Check(actual.format==expectedMap.format&&actual.tileX==expectedMap.tileX&&actual.tileZ==expectedMap.tileZ&&actual.nx==expectedMap.nx&&actual.nz==expectedMap.nz&&actual.heights.SequenceEqual(expectedMap.heights),"Actual large fixture parsed map header/terrain equivalent");
   Check(JsonUtility.ToJson(actual)==JsonUtility.ToJson(expectedMap)&&!File.Exists(downloaded),"Actual large fixture every serialized map field equivalent and temp removed");Debug.Log("PREPARED AREA explicit fixture exercised bytes="+bytes.Length+" chars="+expected.Length+" features="+actual.features.Length);Network(path);
  }
  public static void Run()
  {
   checks=0;root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"EarthUnscriptedPreparedProof-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try
   {
    Utf8();ChangedSecondPass();Ownership();string small=System.IO.Path.Combine(root,"network.json");File.WriteAllText(small,"{\"name\":\"Cymru 🌍\"}",new UTF8Encoding(false));Network(small);
    var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-earthUnscriptedPreparedAreaFixture");if(at>=0){if(at+1>=args.Length)throw new ArgumentException("Explicit prepared fixture path required");Real(args[at+1]);}else Debug.Log("PREPARED AREA real fixture not requested");
    Check(Directory.GetFiles(root,"*.prepared-area").Length==0,"No owned download temp files remain");Debug.Log("PREPARED AREA FILE PASS "+checks+" Mono string.Create/Decoder equivalence, native local file download, verification, cancellation and ownership cleanup; no allocation-byte/timing claim");
   }
   finally{Directory.Delete(root,true);}
  }
  public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
 }
}
