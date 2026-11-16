using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using RoadRush.Core;
namespace RoadRush.Editor
{
    public static class PeerProtocolVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool value,string text){if(!value)throw new Exception("PEER WIRE FAIL "+text);checks++;Debug.Log("PEER WIRE PASS "+text);}
            try
            {
                string root=Path.GetFullPath("Builds/Verification/peer-wire-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);var bytes=new byte[90001];new System.Random(12).NextBytes(bytes);string file=Path.Combine(root,"source.bin");File.WriteAllBytes(file,bytes);
                using(var key=new DataSigningKey())
                {
                    var manifest=new DataFileManifest{id="fixture",kind="region-snapshot",pack="fixture",fromVersion="dee-valley",toVersion="epoch",bytes=bytes.Length,sha256=DataPackageRules.Hash(bytes),source="Connected host",licence="ODbL-1.0"};key.Sign(manifest);string token=new string('a',64);int budgetCalls=0;
                    for(int pass=0;pass<2;pass++)
                    {
                        var listener=new TcpListener(IPAddress.Loopback,0);
                        try
                        {
                            listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;long sent=-1;
                            var server=Task.Run(()=>{using(var peer=listener.AcceptTcpClient()){peer.ReceiveTimeout=peer.SendTimeout=5000;sent=PeerFileProtocol.Serve(peer.GetStream(),token,id=>new PeerFileSource{manifest=manifest,path=file},m=>JsonConvert.SerializeObject(m),c=>{c.ThrowIfCancellationRequested();Interlocked.Increment(ref budgetCalls);},CancellationToken.None);}});
                            PeerFileResult result;using(var client=new TcpClient()){client.ReceiveTimeout=client.SendTimeout=5000;client.Connect(IPAddress.Loopback,port);result=PeerFileProtocol.Receive(client.GetStream(),token,manifest.id,key.Fingerprint,Path.Combine(root,"received"),json=>JsonConvert.DeserializeObject<DataFileManifest>(json),null,CancellationToken.None);}
                            if(!server.Wait(10000))throw new Exception("Unity peer worker did not complete");
                            Check(DataPackageRules.FileMatches(result.path,manifest),"Unity Mono data socket publishes only signed/hash-matching bytes");
                            Check(sent==(pass==0?bytes.Length:0),pass==0?"dedicated Unity stream transfers the expected byte count":"second Unity connection reuses cached content with zero payload bytes");
                        }
                        finally{listener.Stop();}
                    }
                    Check(budgetCalls==3,"Unity worker calls its bandwidth gate once for each bounded chunk");
                }
                File.WriteAllText("Builds/Verification/peer-wire-runtime.txt","PASS "+checks+" Unity Mono socket/signature/cache/budget checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
