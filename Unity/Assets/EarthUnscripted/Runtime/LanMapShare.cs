using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted
{
    public sealed class LanMapShare : IDisposable
    {
        public const int Port=28592;
        public const string Seed="blackwood-seed128-v1";
        readonly string folder;readonly CancellationTokenSource cancel=new CancellationTokenSource();
        TcpListener listener;TcpClient connection;Task<PeerFileSource> prepare;Task server;Task<GeoRegion> receive;
        readonly object catalogueLock=new object();IDisposable preparingPins,cataloguePins;DataSigningKey signingKey;Task<PeerFileSource[]> catalogueJob;volatile PeerFileSource[] catalogue=Array.Empty<PeerFileSource>();Task<string> updateReceive;
        public bool ReceivingUpdate=>updateReceive!=null&&!updateReceive.IsCompleted;
        public DataFileManifest[] UpdateOffers
        {
            get
            {
                PublishCatalogue();return catalogue.Select(s=>s.manifest).ToArray();
            }
        }
        Task<PeerFileSource[]> sourceCatalogueJob;volatile PeerFileSource[] sourceCatalogue=Array.Empty<PeerFileSource>();
        public DataFileManifest[] SourceOffers {get{PublishSources();return sourceCatalogue.Select(s=>s.manifest).ToArray();}}
        readonly object bindingLock=new object();int bindingGeneration;string hostPath;
        volatile string permittedAddress,capability;volatile bool permitted=true;long received,total,sent,actualReceived;
        public bool HostReady=>prepare?.Status==TaskStatus.RanToCompletion;
        public DataFileManifest Manifest=>HostReady?prepare.Result.manifest:null;
        public string Token=>capability;
        public bool Received=>receive?.Status==TaskStatus.RanToCompletion;
        public bool Receiving=>receive!=null&&!receive.IsCompleted;
        public GeoRegion Region=>Received?receive.Result:null;
        public long BytesSent=>Interlocked.Read(ref sent);
        public long BytesReceived=>Interlocked.Read(ref actualReceived);
        public string Progress=>$"Receiving host map · {Interlocked.Read(ref received)/1048576f:F1} / {Interlocked.Read(ref total)/1048576f:F1} MB";
        public string Error=>prepare?.Exception?.GetBaseException().Message??receive?.Exception?.GetBaseException().Message??server?.Exception?.GetBaseException().Message;
        public LanMapShare()
        {
            folder=string.IsNullOrEmpty(PlayerProfile.StorageOverride)?Path.Combine(EarthUnscripted.SinglePlayerBeta.ProductRoot,"PeerData"):PlayerProfile.StorageOverride+".peerdata";
        }
        static Task<T> Worker<T>(Func<T> job)=>Task.Factory.StartNew(()=>{Thread.CurrentThread.Priority=System.Threading.ThreadPriority.BelowNormal;return job();},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        public void Host(string json,string map,string epoch)
        {
            hostPath=Path.Combine(folder,"host-"+Guid.NewGuid().ToString("N")+".json");
            prepare=Worker(()=>
            {
                cancel.Token.ThrowIfCancellationRequested();Directory.CreateDirectory(folder);string path=hostPath;File.WriteAllText(path,json,new UTF8Encoding(false));
                var manifest=new DataFileManifest{id="world-"+epoch,kind="region-snapshot",pack="earthunscripted-worlds",fromVersion=map,toVersion=epoch,source="Connected Earth Unscripted host",licence=map=="blackwood"?"Original Earth Unscripted scenery":"ODbL-1.0; Copernicus DEM; OGL-3.0 where attributed",bytes=new FileInfo(path).Length,sha256=DataPackageRules.HashFile(path)};
                signingKey=new DataSigningKey();signingKey.Sign(manifest);
                cancel.Token.ThrowIfCancellationRequested();listener=new TcpListener(IPAddress.Any,Port);listener.Start(1);var source=new PeerFileSource{manifest=manifest,path=path};
                server=Worker(()=>{Serve(source);return true;});return source;
            });
        }
        public void BindPeer(string address)
        {
            var bytes=new byte[32];using(var rng=System.Security.Cryptography.RandomNumberGenerator.Create())rng.GetBytes(bytes);TcpClient revoke;
            lock(bindingLock){capability=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();permittedAddress=address;Interlocked.Increment(ref bindingGeneration);revoke=connection;connection=null;}
            revoke?.Close();
        }
        public void Budget(bool allowed)=>permitted=allowed;
        void Wait(CancellationToken token,int generation,bool bulk=false)
        {
            while(!permitted){token.ThrowIfCancellationRequested();if(generation!=Volatile.Read(ref bindingGeneration))throw new OperationCanceledException("Gameplay peer changed");Thread.Sleep(20);}
            token.ThrowIfCancellationRequested();if(generation!=Volatile.Read(ref bindingGeneration))throw new OperationCanceledException("Gameplay peer changed");Thread.Sleep(bulk?8:64);
        }
        void Serve(PeerFileSource source)
        {
            while(!cancel.IsCancellationRequested)
            {
                TcpClient client;try{client=listener.AcceptTcpClient();}catch(SocketException){if(cancel.IsCancellationRequested)return;throw;}catch(ObjectDisposedException){return;}
                using(client)
                {
                    int generation;string expected,allowed,address=((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    lock(bindingLock){generation=bindingGeneration;expected=capability;allowed=permittedAddress;connection=client;}
                    if(allowed==null||address!=allowed||generation!=Volatile.Read(ref bindingGeneration)){lock(bindingLock){if(connection==client)connection=null;}continue;}
                    client.ReceiveTimeout=client.SendTimeout=15000;
                    IDisposable servingPin=null;bool bulk=false;
                    try{long count=PeerFileProtocol.Serve(client.GetStream(),expected,id=>
                    {
                        if(id==source.manifest.id)return source;
                        lock(catalogueLock){var item=catalogue.Concat(sourceCatalogue).FirstOrDefault(s=>s.manifest.id==id);if(item!=null){servingPin=PeerCountryUpdates.Pin(item.path);bulk=item.manifest.kind=="osm-source";}return item;}
                    },m=>JsonConvert.SerializeObject(m),c=>Wait(c,generation,bulk),cancel.Token);Interlocked.Add(ref sent,count);}
                    catch(Exception e){if(!cancel.IsCancellationRequested)Debug.LogWarning("Peer map transfer deferred: "+e.Message);}
                    finally{servingPin?.Dispose();lock(bindingLock){if(connection==client)connection=null;}}
                }
            }
        }
        void PublishCatalogue()
        {
            if(catalogueJob==null||!catalogueJob.IsCompleted)return;
            lock(catalogueLock)
            {
                if(catalogueJob.Status==TaskStatus.RanToCompletion){catalogue=catalogueJob.Result;cataloguePins?.Dispose();cataloguePins=preparingPins;preparingPins=null;}
                else{preparingPins?.Dispose();preparingPins=null;}
                catalogueJob=null;
            }
        }
        public bool RefreshUpdates(string updateFolder,string[] packFolders,PeerUpdateInterest[] interests=null)
        {
            if(!HostReady||cancel.IsCancellationRequested)return false;
            PublishCatalogue();if(catalogueJob!=null)return false;
            var pendingPaths=(interests??Array.Empty<PeerUpdateInterest>()).Where(PeerUpdateRules.ValidInterest).SelectMany(i=>Enumerable.Range(1,PeerUpdateRules.MaximumOffers).Select(n=>Path.Combine(updateFolder,i.pack,i.pack+"-"+(long.Parse(i.version,System.Globalization.CultureInfo.InvariantCulture)+n)+".osc.gz"))).ToArray();
            preparingPins=PeerCountryUpdates.Pin(pendingPaths);
            catalogueJob=Worker(()=>PeerCountryUpdates.Catalogue(updateFolder,packFolders,signingKey,cancel.Token,interests));return true;
        }
        void PublishSources()
        {
            if(sourceCatalogueJob==null||!sourceCatalogueJob.IsCompleted)return;
            lock(catalogueLock){if(sourceCatalogueJob.Status==TaskStatus.RanToCompletion)sourceCatalogue=sourceCatalogueJob.Result;sourceCatalogueJob=null;}
        }
        public bool RefreshSources(string cacheRoot)
        {
            if(!HostReady||cancel.IsCancellationRequested)return false;PublishSources();if(sourceCatalogueJob!=null)return false;
            string[] protectedPaths=sourceCatalogue.Select(s=>s.path).ToArray();
            sourceCatalogueJob=Worker(()=>PeerOsmSources.Catalogue(cacheRoot,Path.Combine(folder,"SourceOffers"),signingKey,cancel.Token,protectedPaths));return true;
        }
        public void ReceiveSource(string address,string token,DataFileManifest offered,string fingerprint,string cacheRoot)
        {
            if(updateReceive!=null)throw new InvalidOperationException("A data transfer is already pending");
            var expected=JsonConvert.DeserializeObject<DataFileManifest>(JsonConvert.SerializeObject(offered));
            updateReceive=Worker(()=>
            {
                if(PeerOsmSources.Cached(cacheRoot,expected))return expected.id;
                using(var client=new TcpClient())
                {
                    connection=client;client.ReceiveTimeout=120000;client.SendTimeout=15000;var attempt=client.BeginConnect(address,Port,null,null);
                    while(!attempt.IsCompleted){cancel.Token.ThrowIfCancellationRequested();Thread.Sleep(10);}client.EndConnect(attempt);
                    var result=PeerFileProtocol.Receive(client.GetStream(),token,expected.id,fingerprint,Path.Combine(folder,"OsmReceived"),json=>JsonConvert.DeserializeObject<DataFileManifest>(json),null,cancel.Token);
                    if(result.manifest.sha256!=expected.sha256||result.manifest.signature!=expected.signature)throw new InvalidDataException("Host changed its offered OSM source");
                    Interlocked.Add(ref actualReceived,result.transferred);PeerOsmSources.Stage(result,cacheRoot,fingerprint);return expected.id;
                }
            });
        }
        public void ReceiveUpdate(string address,string token,DataFileManifest offered,string fingerprint,string countryFolder,string updateFolder)
        {
            if(updateReceive!=null)throw new InvalidOperationException("An update transfer is already pending");
            // Copy caller-owned packet data before a worker uses it.
            var expected=JsonConvert.DeserializeObject<DataFileManifest>(JsonConvert.SerializeObject(offered));
            updateReceive=Worker(()=>
            {
                using(var client=new TcpClient())
                {
                    string receivedFolder=Path.Combine(folder,"Updates"),staged=DataPackageRules.PathFor(Path.Combine(updateFolder,expected.pack),expected);
                    Directory.CreateDirectory(receivedFolder);string cached=DataPackageRules.PathFor(receivedFolder,expected);
                    if(DataPackageRules.FileMatches(staged,expected)&&!DataPackageRules.FileMatches(cached,expected))File.Copy(staged,cached,true);
                    connection=client;client.ReceiveTimeout=120000;client.SendTimeout=15000;var attempt=client.BeginConnect(address,Port,null,null);
                    while(!attempt.IsCompleted){cancel.Token.ThrowIfCancellationRequested();Thread.Sleep(10);}client.EndConnect(attempt);
                    var result=PeerFileProtocol.Receive(client.GetStream(),token,expected.id,fingerprint,receivedFolder,json=>JsonConvert.DeserializeObject<DataFileManifest>(json),null,cancel.Token);
                    if(result.manifest.sha256!=expected.sha256||result.manifest.signature!=expected.signature)throw new InvalidDataException("Host changed its offered daily update");
                    Interlocked.Add(ref actualReceived,result.transferred);string path=PeerCountryUpdates.Stage(result,countryFolder,updateFolder,fingerprint);
                    // The staged cache owns the completed content; the receiver's
                    // duplicate is disposable and need not double disk usage.
                    try{File.Delete(result.path);}catch(IOException){}return expected.id;
                }
            });
        }
        public bool CompleteUpdate(out string id,out string error)
        {
            id=error=null;if(updateReceive==null||!updateReceive.IsCompleted)return false;
            if(updateReceive.Status==TaskStatus.RanToCompletion)id=updateReceive.Result;else error=updateReceive.Exception?.GetBaseException().Message??"Transfer cancelled";
            updateReceive=null;return true;
        }
        public void Receive(string address,string token,DataFileManifest offered,string fingerprint)
        {
            if(receive!=null)throw new InvalidOperationException("A map transfer is already pending");
            string map=offered.fromVersion,epoch=offered.toVersion,id=offered.id,compatibility=WorldSnapshotCodec.Compatibility;Interlocked.Exchange(ref total,offered.bytes);
            receive=Worker(()=>
            {
                using(var client=new TcpClient())
                {
                    connection=client;client.ReceiveTimeout=120000;client.SendTimeout=15000;var attempt=client.BeginConnect(address,Port,null,null);
                    while(!attempt.IsCompleted){cancel.Token.ThrowIfCancellationRequested();Thread.Sleep(10);}client.EndConnect(attempt);
                    var result=PeerFileProtocol.Receive(client.GetStream(),token,id,fingerprint,Path.Combine(folder,"Received"),json=>JsonConvert.DeserializeObject<DataFileManifest>(json),(bytes,size)=>Interlocked.Exchange(ref received,bytes),cancel.Token);
                    if(result.manifest.fromVersion!=map||!WorldEpoch.Matches(result.manifest.toVersion,epoch))throw new InvalidDataException("Host changed the offered map epoch");
                    Interlocked.Exchange(ref actualReceived,result.transferred);string json=File.ReadAllText(result.path,new UTF8Encoding(false,true));Interlocked.Exchange(ref received,result.manifest.bytes);
                    if(map=="blackwood")
                    {if(json!=Seed||!WorldEpoch.Matches(WorldEpoch.Compute(compatibility,map,json),epoch))throw new InvalidDataException("Unknown generated world seed");return null;}
                    return WorldSnapshotCodec.Decode(json,map,epoch,compatibility);
                }
            });
        }
        public void Dispose()
        {
            cancel.Cancel();listener?.Stop();connection?.Close();
            // A preparation worker may be between cancellation check and bind.
            prepare?.ContinueWith(async _=>
            {
                if(cancel.IsCancellationRequested)listener?.Stop();
                if(server!=null)try{await server;}catch{}
                if(catalogueJob!=null)try{await catalogueJob;}catch{}
                if(sourceCatalogueJob!=null)try{await sourceCatalogueJob;}catch{}
                preparingPins?.Dispose();cataloguePins?.Dispose();
                if(signingKey!=null){lock(signingKey)signingKey.Dispose();}
                if(hostPath!=null)try{File.Delete(hostPath);}catch{}
            });
        }
    }
}
