using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using RoadRush.Core;
namespace RoadRush.Editor
{
    public static class PeerDataVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool value,string text){if(!value)throw new Exception("PEER DATA FAIL "+text);checks++;Debug.Log("PEER DATA PASS "+text);}
            try
            {
                string root=Path.GetFullPath("Builds/Verification/peer-data-"+DateTime.UtcNow.Ticks);byte[] bytes=Encoding.UTF8.GetBytes("terrain-élevé fixture");
                using(var host=new DataSigningKey())
                {
                    var manifest=new DataFileManifest{id="fixture",kind="region-snapshot",pack="fixture-country",toVersion="1",bytes=bytes.Length,sha256=DataPackageRules.Hash(bytes),source="Connected LAN host",licence="ODbL-1.0",hasBounds=true,west=-3.171,south=52.969,east=-3.17,north=52.97};host.Sign(manifest);
                    Debug.Log("PEER VALID "+DataPackageRules.Valid(manifest)+" modulusBytes="+Convert.FromBase64String(host.PublicKey.Split('.')[0]).Length+" direct="+DataPackageRules.VerifySignature(manifest,host.Fingerprint));
                    try
                    {
                        using(var verifier=System.Security.Cryptography.RSA.Create())
                        {
                            var parts=host.PublicKey.Split('.');verifier.ImportParameters(new System.Security.Cryptography.RSAParameters{Modulus=Convert.FromBase64String(parts[0]),Exponent=Convert.FromBase64String(parts[1])});
                            Debug.Log("PEER RAW VERIFY "+verifier.GetType().FullName+" "+verifier.VerifyData(DataPackageRules.Canonical(manifest),Convert.FromBase64String(manifest.signature),System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1));
                        }
                    }
                    catch(Exception debug){Debug.LogException(debug);}
                    var decoded=JsonUtility.FromJson<DataFileManifest>(JsonUtility.ToJson(manifest));
                    foreach(var field in typeof(DataFileManifest).GetFields())if(!Equals(field.GetValue(manifest),field.GetValue(decoded)))Debug.Log("PEER HEADER FIELD CHANGED "+field.Name+" before="+field.GetValue(manifest)+" after="+field.GetValue(decoded));
                    Check(DataPackageRules.VerifySignature(decoded,host.Fingerprint),"Unity Mono RSA signature survives the actual Unity JSON header roundtrip");
                    using(var receiver=new PeerDataReceiver(root,decoded,host.Fingerprint)){receiver.Append(0,bytes,0,8);}
                    using(var receiver=new PeerDataReceiver(root,decoded,host.Fingerprint))
                    {
                        Check(receiver.Offset==8,"Unity receiver resumes an interrupted signed transfer");receiver.Append(8,bytes,8,bytes.Length-8);string path=receiver.Complete();Check(DataPackageRules.HashFile(path)==manifest.sha256,"Unity receiver validates and atomically publishes a complete payload");
                    }
                    using(var receiver=new PeerDataReceiver(root,decoded,host.Fingerprint))
                    {
                        var bad=(byte[])bytes.Clone();bad[9]^=1;receiver.Append(0,bad,0,bad.Length);bool rejected=false;try{receiver.Complete();}catch(InvalidDataException){rejected=true;}
                        Check(rejected&&DataPackageRules.HashFile(DataPackageRules.PathFor(root,decoded))==manifest.sha256,"Unity corruption rejection preserves the previous usable file");
                    }
                    Check(Directory.GetFiles(root,"*.partial").Length==0,"Unity receiver removes corrupted partial content");
                    var dated=JsonUtility.FromJson<DataFileManifest>(JsonUtility.ToJson(manifest));dated.format=2;dated.timestamp="2026-11-05T00:00:00Z";host.Sign(dated);
                    var datedCopy=JsonUtility.FromJson<DataFileManifest>(JsonUtility.ToJson(dated));Check(DataPackageRules.VerifySignature(datedCopy,host.Fingerprint),"Unity JSON preserves the signed replication timestamp");
                    using(var receiver=new PeerDataReceiver(Path.Combine(root,"dated"),datedCopy,host.Fingerprint)){receiver.Append(0,bytes,0,bytes.Length);Check(DataPackageRules.FileMatches(receiver.Complete(),datedCopy),"Unity receiver retains all version2 signed header fields");}
                    decoded.toVersion="2";bool refused=false;try{using(var invalid=new PeerDataReceiver(root,decoded,host.Fingerprint)){} }catch(InvalidDataException){refused=true;}Check(refused,"Unity runtime rejects altered signed metadata before receiving content");
                }
                File.WriteAllText("Builds/Verification/peer-data-runtime.txt","PASS "+checks+" Unity Mono signature/header/resume/hash/atomic-publication checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
