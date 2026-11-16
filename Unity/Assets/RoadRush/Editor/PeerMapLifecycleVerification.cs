using System;
using System.IO;
using System.Linq;
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
    public static class PeerMapLifecycleVerification
    {
        static string root,folder,oldHostPath,epoch,previousProfile;
        static LanMapShare host,restarted;static TcpClient client;static Task<PeerFileResult> request;static Exception clientError;
        static int phase,checks,headerSeen;static double deadline,blockedSince,revokedAt;static bool finished;
        public static void Run()
        {
            previousProfile=PlayerProfile.StorageOverride;root=Path.GetFullPath("Builds/Verification/peer-lifecycle-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
            PlayerProfile.StorageOverride=Path.Combine(root,"profile.json");folder=PlayerProfile.StorageOverride+".peerdata";
            epoch=WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,"blackwood",LanMapShare.Seed);
            host=new LanMapShare();host.Budget(false);host.Host(LanMapShare.Seed,"blackwood",epoch);
            deadline=EditorApplication.timeSinceStartup+30;EditorApplication.update+=Tick;
        }
        static void Check(bool value,string text)
        {if(!value)throw new Exception("PEER LIFECYCLE FAIL: "+text);checks++;Debug.Log("PEER LIFECYCLE PASS: "+text);}
        static void StartClient(LanMapShare owner)
        {
            var manifest=owner.Manifest;string token=owner.Token,fingerprint=DataPackageRules.Fingerprint(manifest.signer),destination=Path.Combine(root,"Received");
            Interlocked.Exchange(ref headerSeen,0);clientError=null;
            request=Task.Run(()=>
            {
                try
                {
                    using(var socket=new TcpClient())
                    {
                        client=socket;socket.ReceiveTimeout=socket.SendTimeout=5000;socket.Connect(IPAddress.Loopback,LanMapShare.Port);
                        return PeerFileProtocol.Receive(socket.GetStream(),token,manifest.id,fingerprint,destination,json=>{var decoded=JsonConvert.DeserializeObject<DataFileManifest>(json);Interlocked.Exchange(ref headerSeen,1);return decoded;},null,CancellationToken.None);
                    }
                }
                catch(Exception e){clientError=e;return null;}
                finally{client=null;}
            });
        }
        static bool HasEmptyPartial()=>Directory.Exists(Path.Combine(root,"Received"))&&Directory.GetFiles(Path.Combine(root,"Received"),"*.partial").Any(p=>new FileInfo(p).Length==0);
        static void Tick()
        {
            try
            {
                if(finished)return;if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Peer lifecycle timed out in phase "+phase);
                if(phase==0)
                {
                    if(host.Error!=null)throw new Exception(host.Error);if(!host.HostReady)return;
                    Check(host.Manifest.fromVersion=="blackwood"&&host.Manifest.toVersion==epoch,"isolated host prepared the signed known seed");
                    oldHostPath=Directory.GetFiles(folder,"host-*.json").Single();host.BindPeer("127.0.0.1");StartClient(host);phase=1;return;
                }
                if(phase==1)
                {
                    if(request.IsCompleted)throw new Exception("Budget-paused transfer completed or failed before revocation: "+clientError);
                    if(Volatile.Read(ref headerSeen)==0||!HasEmptyPartial())return;
                    blockedSince=EditorApplication.timeSinceStartup;phase=2;return;
                }
                if(phase==2)
                {
                    if(EditorApplication.timeSinceStartup-blockedSince<.2)return;
                    Check(!request.IsCompleted&&host.BytesSent==0,"authenticated transfer remains blocked by its bandwidth budget");
                    Check(!File.Exists(DataPackageRules.PathFor(Path.Combine(root,"Received"),host.Manifest)),"blocked transfer has not published a usable file");
                    revokedAt=EditorApplication.timeSinceStartup;host.BindPeer(null);deadline=revokedAt+3;phase=3;return;
                }
                if(phase==3)
                {
                    if(!request.IsCompleted)return;
                    Check(request.Result==null&&clientError!=null,"revoking the gameplay binding interrupts its authenticated data socket");
                    Check(EditorApplication.timeSinceStartup-revokedAt<3,"revocation completes promptly while the bandwidth gate remains paused");
                    Check(!File.Exists(DataPackageRules.PathFor(Path.Combine(root,"Received"),host.Manifest))&&HasEmptyPartial(),"revocation preserves only the resumable partial and never activates it");
                    // Immediately recreate a host using the same cache folder and
                    // epoch; no waiting for the previous serving worker to unwind.
                    host.Dispose();restarted=new LanMapShare();restarted.Host(LanMapShare.Seed,"blackwood",epoch);deadline=EditorApplication.timeSinceStartup+30;phase=4;return;
                }
                if(phase==4)
                {
                    if(restarted.Error!=null)throw new Exception(restarted.Error);if(!restarted.HostReady)return;
                    string nextPath=Directory.GetFiles(folder,"host-*.json").Single(p=>p!=oldHostPath);
                    Check(nextPath!=oldHostPath&&DataPackageRules.HashFile(nextPath)==restarted.Manifest.sha256,"immediate same-epoch rehost uses a different immutable source file");
                    Check(restarted.Manifest.toVersion==epoch,"rapid rehost retains the same canonical world epoch");
                    restarted.BindPeer("127.0.0.1");StartClient(restarted);deadline=EditorApplication.timeSinceStartup+10;phase=5;return;
                }
                if(phase==5)
                {
                    if(!request.IsCompleted)return;if(clientError!=null)throw new Exception("Rehost receive failed",clientError);
                    if(restarted.BytesSent!=restarted.Manifest.bytes)return;
                    Check(request.Result!=null&&DataPackageRules.FileMatches(request.Result.path,restarted.Manifest),"restarted host successfully serves a complete signed/hash-valid file");
                    Check(restarted.BytesSent==restarted.Manifest.bytes,"restarted listener records exactly the expected payload bytes");
                    restarted.Dispose();deadline=EditorApplication.timeSinceStartup+5;phase=6;return;
                }
                if(phase==6)
                {
                    if(Directory.GetFiles(folder,"host-*.json").Length!=0)return;
                    Check(true,"both immutable host sources are removed after serving workers unwind");
                    File.WriteAllText("Builds/Verification/peer-map-lifecycle.txt","PASS "+checks+" Unity Mono binding-revocation/paused-transfer/rapid-rehost/source-cleanup checks\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code)
        {
            if(finished)return;finished=true;EditorApplication.update-=Tick;client?.Close();host?.Dispose();restarted?.Dispose();PlayerProfile.StorageOverride=previousProfile;EditorApplication.Exit(code);
        }
    }
}
