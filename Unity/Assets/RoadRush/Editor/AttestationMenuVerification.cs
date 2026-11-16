using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RoadRush.Packs;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    /// <summary>Actual Unity menu handlers and isolated I/O; no scene, publisher trust or production identity.</summary>
    public static class AttestationMenuVerification
    {
        static readonly List<string> checks = new List<string>();
        static void Check(bool value, string message)
        { if (!value) throw new InvalidDataException(message); checks.Add(message); }
        static void Wait(WorldPackMenu menu)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(menu.Busy && clock.Elapsed.TotalSeconds<45) { menu.Poll(); Thread.Sleep(10); }
            Check(!menu.Busy,"menu worker finishes");
        }
        static IReadOnlyList<PackAttestationItem> Records(WorldPackMenu menu)
            => (IReadOnlyList<PackAttestationItem>)typeof(WorldPackMenu).GetField("certificates",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(menu);
        static byte[] Signed(RSA rsa,string manifest)
        {
            var key=rsa.ExportParameters(false);
            var json=new JObject {
                ["format"]="RoadRushPackValidation1",["manifestSha256"]=manifest,["subsetSha256"]=new string('b',64),
                ["engineVersion"]="0.22.1",["gameBuildSha256"]=new string('c',64),["platform"]="macos-arm64",
                ["reportSha256"]=new string('d',64),["issuedUtc"]="2026-11-10T04:00:00Z",
                ["publicKey"]=Convert.ToBase64String(key.Modulus)+"."+Convert.ToBase64String(key.Exponent),
                ["signature"]="pending",["checks"]=new JArray("manifest","managed") };
            var record=WorldPackValidation.Parse(Encoding.UTF8.GetBytes(json.ToString(Formatting.None)));
            json["signature"]=Convert.ToBase64String(rsa.SignData(record.CanonicalBytes(),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
            return Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        }
        public static void Run()
        {
            int code=1; string output=null; WorldPackMenu menu=null;
            AudioListener.volume=0; AudioListener.pause=true;
            try
            {
                var args=Environment.GetCommandLineArgs(); Func<string,string> arg=name=>args[Array.IndexOf(args,name)+1];
                output=arg("-attestationResult"); string root=Path.Combine(Path.GetDirectoryName(output),"isolated-library");
                Check(!Directory.Exists(root),"fresh isolated library");
                menu=new WorldPackMenu();
                typeof(WorldPackMenu).GetField("root",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(menu,root);
                typeof(WorldPackMenu).GetField("engineVersion",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(menu,"0.22.1");
                menu.Open(null); Wait(menu); Check(menu.RevisionCount==0,"empty library ready");
                menu.ImportArchive(arg("-attestationPack")); Wait(menu); Check(menu.RevisionCount==1,"normal archive import retained");
                menu.ShowCertificates(0); Wait(menu); Check(Records(menu).Count==0,"unsigned revision inspected");
                var store=new WorldPackStore(root); var installed=store.Discover().Single();
                string certificate=Path.Combine(Path.GetDirectoryName(output),"ephemeral.rrvalidation");
                using(var rsa=new RSACryptoServiceProvider(2048) { PersistKeyInCsp=false })
                {
                    Check(rsa.ExportParameters(false).Modulus.Length==256,"ephemeral signer uses 2048-bit key");
                    File.WriteAllBytes(certificate,Signed(rsa,installed.Manifest.ManifestSha256));
                    menu.ImportCertificate(certificate); Wait(menu);
                    Check(Records(menu).Count==1 && Records(menu)[0].Record!=null,"normal menu certificate import and inspection: "+menu.Status);
                    Check(menu.Status.Contains("does not grant signer trust"),"menu explicitly withholds trust");
                    var claim=Records(menu)[0].Record;
                    Check(claim.Evaluate(installed.Manifest.ManifestSha256,new string('b',64),"0.22.1",new string('c',64),"macos-arm64",new System.Collections.Generic.HashSet<string>())==PackValidationState.UntrustedIssuer,"signature import confers no trust");
                    menu.ImportCertificate(certificate); Wait(menu); Check(Records(menu).Count==1,"menu import remains idempotent");
                    File.WriteAllBytes(certificate,Signed(rsa,new string('a',64)));
                    menu.ImportCertificate(certificate); Wait(menu); Check(menu.Status.StartsWith("Pack operation failed:"),"wrong-pack error presented");
                    Check(Records(menu).Count==1,"failed import retains previous inspected record");
                    File.WriteAllText(certificate,"{malformed");menu.ImportCertificate(certificate);Wait(menu);
                    Check(menu.Status.StartsWith("Pack operation failed:"),"malformed error presented");
                    menu.ShowCertificates(0);Wait(menu); Check(Records(menu).Count==1,"refresh recovers after failure");
                    menu.CheckContent(0);Wait(menu);Check(menu.Status.Contains("elements readable"),"ordinary content check retained");
                }
                Check(!File.Exists(Path.Combine(root,"selection.json")),"certificate handlers never select a revision");
                store.VerifyInstalled(installed.Directory,installed.Manifest.ManifestSha256);
                Check(true,"immutable installed pack still verifies");
                Check(AudioListener.volume==0 && AudioListener.pause,"verification remains silent");
                File.WriteAllText(output,new JObject { ["status"]="PASS_UNITY_MENU_HANDLERS",["checks"]=checks.Count,["passed"]=new JArray(checks),
                    ["scope"]="Unity compile/normal async menu handlers with isolated library and ephemeral signing key. No rendered UI, production trust, current-build attestation, native driving or Windows execution claim." }.ToString(Formatting.Indented));
                code=0;
            }
            catch(Exception e)
            { Debug.LogException(e); if(output!=null) File.WriteAllText(output,new JObject { ["status"]="FAIL",["error"]=e.ToString(),["passed"]=new JArray(checks) }.ToString()); }
            finally { menu?.Dispose(); EditorApplication.Exit(code); }
        }
    }
}
