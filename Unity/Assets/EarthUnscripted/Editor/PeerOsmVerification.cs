using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted.Editor
{
    public static class PeerOsmVerification
    {
        static DataFileManifest Header(long bytes,string sha)=>new DataFileManifest{format=2,id="osm-"+sha,kind="osm-source",pack="osm-source-proof",fromVersion="raw-osm-pbf-v1",toVersion=sha,bytes=bytes,sha256=sha,source="https://download.geofabrik.de/test-latest.osm.pbf",licence="ODbL-1.0",timestamp=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",System.Globalization.CultureInfo.InvariantCulture)};
        public static void Run()
        {
            int checks=0;void Need(bool value,string text){checks++;if(!value)throw new Exception("PEER OSM "+text);}
            Need(DataPackageRules.MaximumFileBytes==32*1024*1024,"legacy world/terrain/diff cap is unchanged");
            var item=Header(33*1024*1024,new string('a',64));Need(DataPackageRules.Valid(item),"a bounded full source can exceed the legacy snapshot cap");
            item.bytes=DataPackageRules.MaximumOsmSourceBytes+1L;Need(!DataPackageRules.Valid(item),"full source cap remains enforced");item.bytes=33*1024*1024;
            item.kind="region-snapshot";Need(!DataPackageRules.Valid(item),"a source extension cannot enlarge a world snapshot");item.kind="osm-source";
            foreach(string url in new[]{"http://download.geofabrik.de/test.osm.pbf","https://127.0.0.1/test.osm.pbf","https://download.geofabrik.de.evil.test/test.osm.pbf","https://download.bbbike.org/osm/bbbike/A/B.osm.pbf","https://download.geofabrik.de/test.osm.pbf?token=x"})Need(!DataPackageRules.OsmSourceUrl(url),"untrusted source URL is rejected");
            Need(DataPackageRules.OsmSourceUrl("https://download.bbbike.org/osm/bbbike/Sydney/Sydney.osm.pbf"),"official matching city source is admitted");
            using(var key=new DataSigningKey())
            {
                key.Sign(item);Need(DataPackageRules.VerifySignature(item,key.Fingerprint),"source header is bound to the connected host");item.bytes--;Need(!DataPackageRules.VerifySignature(item,key.Fingerprint),"changed source size invalidates signature");
            }
            string temp=Path.Combine(Path.GetTempPath(),"EarthPeerReceipt-"+Guid.NewGuid().ToString("N"));
            try
            {
                string folder=Path.Combine(temp,"Incoming");Directory.CreateDirectory(folder);var data=new byte[]{1,2,3};var cached=Header(data.Length,DataPackageRules.Hash(data));string path=Path.Combine(folder,cached.sha256+".osm.pbf");File.WriteAllBytes(path,data);
                Need(!PeerOsmSources.Cached(temp,cached),"an uncommitted payload is never a cache hit");
                var receipt=new JObject{["url"]=cached.source,["bytes"]=cached.bytes,["sha256"]=cached.sha256,["origin"]="authenticated-gameplay-peer",["licence"]="ODbL-1.0",["fetchedUTC"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d};
                File.WriteAllText(path+".receipt.json",receipt.ToString());Need(PeerOsmSources.Cached(temp,cached),"a committed fractional-second receipt is admitted");
                receipt["fetchedUTC"]=DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeSeconds();File.WriteAllText(path+".receipt.json",receipt.ToString());Need(!PeerOsmSources.Cached(temp,cached),"stale source receipts trigger refresh");
            }
            finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
            string recipe=WorldSnapshotCodec.Compatibility;Need(recipe.Length<=128&&recipe.Contains("/world-"),"prepared peer compatibility includes a bounded combined recipe identity");
            string different=recipe.Substring(0,recipe.Length-1)+(recipe.EndsWith("a")?"b":"a");Need(WorldEpoch.Compute(recipe,"proof","same map")!=WorldEpoch.Compute(different,"proof","same map"),"different generation recipes cannot share an active world epoch");
            Debug.Log("PEER_OSM_POLICY_PASS "+checks);
        }
        static string Argument(string name)
        {var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);if(at<0||at+1>=args.Length)throw new ArgumentException("Missing "+name);return Path.GetFullPath(args[at+1]);}
        public static void Batch()
        {
            try
            {
                Run();string fixture=Argument("-earthPeerOsmFixture"),root=Argument("-earthPeerOsmOutput");Directory.CreateDirectory(root);
                string host=Path.Combine(root,"Host","OpenStreetMap"),offers=Path.Combine(root,"Offers"),guest=Path.Combine(root,"Guest","OpenStreetMap"),received=Path.Combine(root,"Received");Directory.CreateDirectory(host);
                string source=Path.Combine(host,"regional-proof.osm.pbf");File.Copy(fixture,source,true);long bytes=new FileInfo(source).Length;if(bytes<=DataPackageRules.MaximumFileBytes)throw new Exception("Real bulk fixture must exercise >32MiB");
                string sha=DataPackageRules.HashFile(source);File.WriteAllText(source+".receipt.json",new JObject{["url"]="https://download.geofabrik.de/test-latest.osm.pbf",["bytes"]=bytes,["sha256"]=sha,["fetchedUTC"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.ToString(Formatting.None));
                using(var key=new DataSigningKey())using(var cancellation=new CancellationTokenSource())
                {
                    var catalogue=PeerOsmSources.Catalogue(host,offers,key,cancellation.Token);if(catalogue.Length!=1)throw new Exception("Verified public source was not catalogued");var item=catalogue[0];
                    using(var initial=new PeerDataReceiver(received,item.manifest,key.Fingerprint))
                    using(var input=File.OpenRead(item.path))
                    {var first=new byte[DataPackageRules.ChunkBytes];int n=input.Read(first,0,first.Length);initial.Append(0,first,0,n);}
                    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(1);int port=((IPEndPoint)listener.LocalEndpoint).Port;string token=new string('d',64);PeerFileResult result=null;
                    try
                    {
                        var server=Task.Run(()=>
                        {
                            using(var client=listener.AcceptTcpClient())
                            {client.ReceiveTimeout=client.SendTimeout=15000;return PeerFileProtocol.Serve(client.GetStream(),token,id=>id==item.manifest.id?item:null,m=>JsonConvert.SerializeObject(m),null,cancellation.Token);}
                        });
                        using(var client=new TcpClient())
                        {
                            client.ReceiveTimeout=client.SendTimeout=15000;client.Connect(IPAddress.Loopback,port);
                            result=PeerFileProtocol.Receive(client.GetStream(),token,item.manifest.id,key.Fingerprint,received,json=>JsonConvert.DeserializeObject<DataFileManifest>(json),null,cancellation.Token);
                        }
                        if(!server.Wait(30000)||server.Result!=bytes-DataPackageRules.ChunkBytes||result.resumed!=DataPackageRules.ChunkBytes)throw new Exception("Actual TCP source transfer/resume counts disagree");
                    }
                    finally{cancellation.Cancel();listener.Stop();}
                    string staged=PeerOsmSources.Stage(result,guest,key.Fingerprint);if(!PeerOsmSources.Cached(guest,item.manifest)||DataPackageRules.HashFile(staged)!=sha)throw new Exception("Peer source staging changed content");
                    File.WriteAllText(Path.Combine(root,"result.json"),new JObject{["passed"]=true,["bytes"]=bytes,["transferred"]=result.transferred,["resumed"]=result.resumed,["sha256"]=sha,["cacheRoot"]=guest,["source"]=item.manifest.source,["limitations"]="Authenticated loopback TCP and staged source validation; two-machine throughput and single-player peer discovery are not exercised."}.ToString(Formatting.Indented));
                }
                Debug.Log("PEER_OSM_TCP_PASS");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
