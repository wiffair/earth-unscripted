using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using RoadRush.Core;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class PeerCountryUpdateVerification
    {
        static string root,hostUpdates,guestUpdates,guestCountry,originalHash,priorProfile;static LanMapShare host,guest;static DataFileManifest offer,second;static string firstPath;static DataFileManifest firstHeader;static int phase,checks;static double deadline;
        static void Check(bool value,string text){if(!value)throw new Exception("PEER COUNTRY FAIL: "+text);checks++;Debug.Log("PEER COUNTRY PASS: "+text);}
        public static void Run()
        {
            try
            {
                priorProfile=PlayerProfile.StorageOverride;root=Path.GetFullPath("Builds/Verification/peer-country-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(root);
                string previous=Directory.GetDirectories("Builds/Verification","replication-*").OrderByDescending(x=>x).First();string hostCountry=Path.Combine(root,"HostCountry");guestCountry=Path.Combine(root,"GuestCountry");
                foreach(string country in new[]{hostCountry,guestCountry}){Directory.CreateDirectory(country);foreach(string file in new[]{"pack.json","map.sqlite"})File.Copy(Path.Combine(previous,file),Path.Combine(country,file));}
                var pack=new CountryPack(hostCountry);Check(pack.Version=="13","isolated host and guest use the replication13 fixture");
                originalHash=DataPackageRules.HashFile(Path.Combine(guestCountry,"map.sqlite"));hostUpdates=Path.Combine(root,"HostUpdates");guestUpdates=Path.Combine(root,"GuestUpdates");string directory=Path.Combine(hostUpdates,pack.Id);Directory.CreateDirectory(directory);string id=pack.Id+"-14",path=Path.Combine(directory,id+".osc.gz");
                using(var file=File.Create(path))using(var gzip=new GZipStream(file,CompressionMode.Compress))using(var writer=new StreamWriter(gzip,Encoding.UTF8))writer.Write("<osmChange version='0.6'><modify><node id='3' lat='52.0004' lon='-2.9999'/></modify></osmChange>");
                var manifest=new DataFileManifest{id=id,kind="map-diff",pack=pack.Id,fromVersion="13",toVersion="14",bytes=new FileInfo(path).Length,sha256=DataPackageRules.HashFile(path),source=pack.ReplicationSource+"/000/000/014.osc.gz",licence="ODbL-1.0"};
                File.WriteAllText(Path.Combine(directory,id+".json"),JsonConvert.SerializeObject(manifest));File.WriteAllText(Path.Combine(directory,id+".state.txt"),"sequenceNumber=14\ntimestamp=2026-11-05T00\\:00\\:00Z\n");
                string nextId=pack.Id+"-15",nextPath=Path.Combine(directory,nextId+".osc.gz");File.Copy(path,nextPath);
                var next=JsonConvert.DeserializeObject<DataFileManifest>(JsonConvert.SerializeObject(manifest));next.id=nextId;next.fromVersion="14";next.toVersion="15";next.source=pack.ReplicationSource+"/000/000/015.osc.gz";
                File.WriteAllText(Path.Combine(directory,nextId+".json"),JsonConvert.SerializeObject(next));File.WriteAllText(Path.Combine(directory,nextId+".state.txt"),"sequenceNumber=15\ntimestamp=2026-11-06T00\\:00\\:00Z\n");
                for(int i=0;i<300;i++)File.WriteAllText(Path.Combine(directory,"unrelated-"+i+".json"),"{}");
                PlayerProfile.StorageOverride=Path.Combine(root,"host-profile.json");host=new LanMapShare();host.Host(LanMapShare.Seed,"blackwood",WorldEpoch.Compute(WorldSnapshotCodec.Compatibility,"blackwood",LanMapShare.Seed));
                PlayerProfile.StorageOverride=Path.Combine(root,"guest-profile.json");guest=new LanMapShare();deadline=EditorApplication.timeSinceStartup+40;EditorApplication.update+=Tick;
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Peer country test timed out phase "+phase);
                if(phase==0)
                {
                    if(host.Error!=null)throw new Exception(host.Error);if(!host.HostReady)return;host.BindPeer("127.0.0.1");host.RefreshUpdates(hostUpdates,new[]{Path.Combine(root,"HostCountry")});phase=1;return;
                }
                if(phase==1)
                {
                    var offers=host.UpdateOffers;if(offers.Length==0)return;Check(offers.Length==2&&offers[0].toVersion=="14"&&offers[1].toVersion=="15","catalogue returns the guest contiguous14/15 window despite300 unrelated newer headers");offer=offers[0];second=offers[1];string fingerprint=DataPackageRules.Fingerprint(host.Manifest.signer);
                    Check(offer.format==2&&offer.timestamp=="2026-11-05T00:00:00Z","host catalogue authenticates the actual replication state timestamp");
                    Check(offer.signer==host.Manifest.signer&&DataPackageRules.VerifySignature(offer,fingerprint),"daily file uses the same signing identity as the joined map");
                    guest.ReceiveUpdate("127.0.0.1",host.Token,offer,fingerprint,guestCountry,guestUpdates);phase=2;return;
                }
                if(phase==2)
                {
                    if(!guest.CompleteUpdate(out string id,out string error))return;if(error!=null)throw new Exception(error);
                    string directory=Path.Combine(guestUpdates,offer.pack),path=DataPackageRules.PathFor(directory,offer);var staged=JsonConvert.DeserializeObject<DataFileManifest>(File.ReadAllText(Path.Combine(directory,id+".json")));var state=ReplicationState.Parse(File.ReadAllText(Path.Combine(directory,id+".state.txt")));
                    Check(id==offer.id&&DataPackageRules.FileMatches(path,staged),"actual separate socket delivers a signed, hash-valid daily diff into the guest staging cache");
                    Check(staged.signature==offer.signature&&state.sequence==14&&state.timestamp==offer.timestamp,"staged commit retains the signed date, sequence and host signature");
                    Check(new CountryPack(guestCountry).Version=="13"&&DataPackageRules.HashFile(Path.Combine(guestCountry,"map.sqlite"))==originalHash,"peer transfer leaves the guest country database unchanged before safe activation");
                    Check(guest.BytesReceived==offer.bytes,"guest records only actual daily payload bytes");
                    bool refused=false;try{MapReplication.Apply(new CountryPack(guestCountry),path,staged,"2026-11-06T00:00:00Z");}catch(InvalidDataException){refused=true;}
                    Check(refused&&new CountryPack(guestCountry).Version=="13","a changed unsigned sidecar date cannot override the signed timestamp");
                    firstPath=path;firstHeader=staged;host.RefreshUpdates(hostUpdates,new[]{Path.Combine(root,"HostCountry")},new[]{new PeerUpdateInterest{pack=offer.pack,version="14"}});phase=3;return;
                }
                if(phase==3)
                {
                    var offers=host.UpdateOffers;if(offers.Length!=1||offers[0].toVersion!="15")return;
                    Check(offers[0].fromVersion=="14","guest verified staging tip advances its offered window while country database remains13");
                    second=offers[0];guest.ReceiveUpdate("127.0.0.1",host.Token,second,DataPackageRules.Fingerprint(host.Manifest.signer),guestCountry,guestUpdates);phase=4;return;
                }
                if(phase==4)
                {
                    if(!guest.CompleteUpdate(out string id,out string error))return;if(error!=null)throw new Exception(error);
                    string path=DataPackageRules.PathFor(Path.Combine(guestUpdates,second.pack),second);
                    Check(id==second.id&&DataPackageRules.FileMatches(path,second),"next daily update15 is downloaded and staged without activating14");
                    Check(new CountryPack(guestCountry).Version=="13"&&DataPackageRules.HashFile(Path.Combine(guestCountry,"map.sqlite"))==originalHash,"both staged daily updates leave the country and active version pinned13");
                    bool refused=false;try{MapReplication.Apply(new CountryPack(guestCountry),path,second,second.timestamp);}catch(InvalidDataException){refused=true;}
                    Check(refused&&new CountryPack(guestCountry).Version=="13","a valid later diff cannot skip the missing activation predecessor");
                    MapReplication.Apply(new CountryPack(guestCountry),firstPath,firstHeader,firstHeader.timestamp);var result=MapReplication.Apply(new CountryPack(guestCountry),path,second,second.timestamp);
                    Check(result.version=="15"&&new CountryPack(guestCountry).Version=="15","both peer-staged payloads activate transactionally in exact14 then15 order when safe");
                    Check(!PeerUpdateRules.Matches(second,second.pack,"15",new CountryPack(guestCountry).ReplicationSource),"an already applied peer sequence is not requested again");
                    using(PeerCountryUpdates.Pin(firstPath))
                    {
                        int removed=PeerCountryUpdates.TrimApplied(guestUpdates,guestCountry,0);
                        Check(removed==1&&File.Exists(firstPath)&&!File.Exists(path),"applied-cache eviction protects a pinned catalogue or in-flight payload");
                    }
                    Check(PeerCountryUpdates.TrimApplied(guestUpdates,guestCountry,0)==1&&!File.Exists(firstPath),"released applied data is evicted without touching country databases");
                    string pendingPath=Path.Combine(root,"HostUpdates",firstHeader.pack,firstHeader.id+".osc.gz");var future=JsonConvert.DeserializeObject<DataFileManifest>(JsonConvert.SerializeObject(second));future.id=future.pack+"-16";future.fromVersion="15";future.toVersion="16";future.source=new CountryPack(guestCountry).ReplicationSource+"/000/000/016.osc.gz";
                    using(var key=new DataSigningKey()){key.Sign(future);PeerCountryUpdates.Stage(new PeerFileResult{manifest=future,path=pendingPath},guestCountry,guestUpdates,key.Fingerprint);}
                    Check(PeerCountryUpdates.TrimApplied(guestUpdates,guestCountry,0)==0&&File.Exists(DataPackageRules.PathFor(Path.Combine(guestUpdates,future.pack),future)),"quota cleanup retains unapplied chain data even when no disposable space remains");
                    File.WriteAllText("Builds/Verification/peer-country-updates.txt","PASS "+checks+" Unity Mono signed catalogue / real socket / staging / timestamp / transactional activation checks\n"+root+"\n");Finish(0);
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){EditorApplication.update-=Tick;host?.Dispose();guest?.Dispose();PlayerProfile.StorageOverride=priorProfile;EditorApplication.Exit(code);}
    }
}
