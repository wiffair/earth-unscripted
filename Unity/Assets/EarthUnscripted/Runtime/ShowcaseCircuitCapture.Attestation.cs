// Explicit opt-in observer; the shared capture entry point requires isolated storage and forces silence.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class ShowcaseCircuitCapture
    {
        IEnumerator AttestationCapture(string suffix)
        {
            string path=prefix+"-certificate-"+suffix+".png";
            yield return new WaitForSecondsRealtime(.3f);yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);float began=Time.realtimeSinceStartup;
            while(!File.Exists(path)||new FileInfo(path).Length==0)
            { if(Time.realtimeSinceStartup-began>10)throw new IOException("Certificate capture timeout");yield return null; }
        }
        IEnumerator RunAttestationMenuFixture()
        {
            Check(AudioListener.volume==0&&AudioListener.pause,"Certificate fixture begins silent");
            Prepare(session);Check(session.TrySave(),"Isolated career saved");
            byte[] saved=File.ReadAllBytes(PlayerProfile.StoragePath);
            int credits=session.Economy.Credits;string region=session.Profile.region;var original=session;
            var ui=session.GetComponent<DrivingUI>();ui.ShowImportedPacks();var library=ui.ImportedPacks;
            while(library.Busy)yield return null;
            Check(library.RevisionCount==0,"Fresh isolated certificate library");
            library.ImportArchive(archive);while(library.Busy)yield return null;
            Check(library.RevisionCount==1,"Normal pack import");
            library.ShowCertificates(0);while(library.Busy)yield return null;
            Check(library.Status.Contains("Certificate records checked"),"Unsigned revision inspection");
            yield return AttestationCapture("unsigned");
            var store=new WorldPackStore(RegionalPackActivation.LibraryRoot());var pack=store.Discover().Single();
            var buildSource=PackGameBuildSource.Capture();var build=buildSource.Verify();
            string subset=WorldPackCompatibility.Resolve(pack.Manifest,PackCircuitSources.Readers(Application.version)).SubsetIdentity;
            Check(library.Status.Contains("Current build fingerprint verified"),"Menu verifies actual packaged runtime fingerprint");
            string source=prefix+"-ephemeral.euvalidation", fullSource=prefix+"-synthetic-native-claim.euvalidation";
            using(var rsa=new RSACryptoServiceProvider(2048) { PersistKeyInCsp=false })
            {
                var key=rsa.ExportParameters(false);Check(key.Modulus.Length==256,"Ephemeral test RSA key is 2048 bits");
                var json=new JObject { ["format"]="EarthUnscriptedPackValidation1",["manifestSha256"]=pack.Manifest.ManifestSha256,
                    ["subsetSha256"]=subset,["engineVersion"]=Application.version,["gameBuildSha256"]=build.Sha256,
                    ["platform"]="macos-arm64",["reportSha256"]=new string('d',64),["issuedUtc"]="2026-11-10T04:00:00Z",
                    ["publicKey"]=Convert.ToBase64String(key.Modulus)+"."+Convert.ToBase64String(key.Exponent),
                    ["signature"]="pending",["checks"]=new JArray("manifest","managed") };
                var record=WorldPackValidation.Parse(Encoding.UTF8.GetBytes(json.ToString(Formatting.None)));
                json["signature"]=Convert.ToBase64String(rsa.SignData(record.CanonicalBytes(),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
                File.WriteAllText(source,json.ToString(Formatting.None));
                // A deliberately synthetic UI fixture, never an issued gameplay certificate.
                json["checks"]=new JArray("manifest","native-load","native-drive","native-render");
                json["reportSha256"]=new string('e',64);json["signature"]="pending";
                var full=WorldPackValidation.Parse(Encoding.UTF8.GetBytes(json.ToString(Formatting.None)));
                json["signature"]=Convert.ToBase64String(rsa.SignData(full.CanonicalBytes(),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
                File.WriteAllText(fullSource,json.ToString(Formatting.None));
            }
            library.ImportCertificate(source);while(library.Busy)yield return null;
            Check(library.Status.Contains("does not grant signer trust"),"Normal certificate import withholds signer trust");
            Check(new WorldPackAttestationStore(RegionalPackActivation.LibraryRoot()).Inspect(pack.Manifest.ManifestSha256).Count==1,"One certificate persisted outside pack");
            Check(library.CertificateState(0)==PackValidationState.UntrustedIssuer,"Exact build match alone never grants issuer trust");
            yield return AttestationCapture("imported");
            var claims=new WorldPackAttestationStore(RegionalPackActivation.LibraryRoot()).Inspect(pack.Manifest.ManifestSha256);
            string fingerprint=claims.Single().Record.IssuerFingerprint;var trust=new WorldPackIssuerTrust(RegionalPackActivation.LibraryRoot());
            Check(trust.Load().Count==0,"Certificate import never grants issuer trust");
            library.RequestIssuerTrust(0);Check(trust.Load().Count==0,"Opening trust review does not grant trust");
            yield return AttestationCapture("confirm");
            library.CancelIssuerTrust();Check(trust.Load().Count==0,"Cancelling review leaves signer untrusted");
            library.RequestIssuerTrust(0);library.ConfirmIssuerTrust();while(library.Busy)yield return null;
            Check(trust.Load().SetEquals(new[]{fingerprint}),"Explicit confirmation persists selected signer only");
            Check(library.Status.Contains("now trust"),"Trust confirmation reports local scope");
            Check(library.CertificateState(0)==PackValidationState.VerifiedPartialEvidence,"Trusted partial checks never become full native certification");
            yield return AttestationCapture("trusted");
            library.ImportCertificate(fullSource);while(library.Busy)yield return null;
            var storedClaims=new WorldPackAttestationStore(RegionalPackActivation.LibraryRoot()).Inspect(pack.Manifest.ManifestSha256).ToList();
            int fullIndex=storedClaims.FindIndex(c=>c.Record.Checks.Contains("native-drive"));
            int partialIndex=storedClaims.FindIndex(c=>!c.Record.Checks.Contains("native-drive"));
            Check(fullIndex>=0&&partialIndex>=0&&library.CertificateState(fullIndex)==PackValidationState.ValidatedForThisBuild,
                "Synthetic full-scope claim matches verified build subset and platform only after explicit signer trust");
            typeof(WorldPackMenu).GetField("certificateScroll",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(library,new Vector2(0,fullIndex*190));
            yield return AttestationCapture("synthetic-full-scope");
            // Damage only this dedicated QA app's detached descriptor, not a game payload or real installation.
            // Ordinary play must remain available even when a claimed build cannot be verified.
            byte[] descriptor=File.ReadAllBytes(buildSource.ManifestPath);
            try
            {
                File.WriteAllText(buildSource.ManifestPath,"{invalid");library.ShowCertificates(0);while(library.Busy)yield return null;
                Check(library.CertificateState(0)==null&&library.Status.Contains("unavailable or files differ"),"Bad build descriptor withholds exact-build claim");
                yield return AttestationCapture("build-unavailable");
            }
            finally {File.WriteAllBytes(buildSource.ManifestPath,descriptor);}
            library.ShowCertificates(0);while(library.Busy)yield return null;
            Check(library.CertificateState(partialIndex)==PackValidationState.VerifiedPartialEvidence&&library.CertificateState(fullIndex)==PackValidationState.ValidatedForThisBuild,"Restored descriptor recovers current-build partial report");
            byte[] validSource=File.ReadAllBytes(source);
            try
            {
                File.WriteAllText(source,"{malformed");library.ImportCertificate(source);while(library.Busy)yield return null;
                Check(library.CertificateState(fullIndex)==null,"Failed fresh import withholds previously accepted exact-build report");
                yield return AttestationCapture("failed-refresh");
            }
            finally {File.WriteAllBytes(source,validSource);}
            library.ShowCertificates(0);while(library.Busy)yield return null;
            Check(library.CertificateState(fullIndex)==PackValidationState.ValidatedForThisBuild,"Fresh successful check restores report after import failure");
            library.RevokeIssuerTrust(0);while(library.Busy)yield return null;
            Check(trust.Load().Count==0&&library.Status.Contains("revoked"),"Normal revoke removes local issuer trust");

            File.WriteAllText(source,"{malformed");library.ImportCertificate(source);while(library.Busy)yield return null;
            Check(library.Status.StartsWith("Pack operation failed:"),"Malformed import has recoverable error");
            yield return AttestationCapture("error");
            library.CloseCertificates();library.CheckContent(0);while(library.Busy)yield return null;
            Check(library.Status.Contains("elements readable"),"Back returns to working library check");
            yield return AttestationCapture("library");
            Check(session==original&&session.BootReady&&!session.BootFailed,"Original working scene retained");
            Check(session.Economy.Credits==credits&&session.Profile.region==region,"Career destination and credits retained");
            Check(File.ReadAllBytes(PlayerProfile.StoragePath).SequenceEqual(saved),"Isolated career bytes unchanged");
            session.ResumeDrive();yield return null;Check(!session.Menu&&session.BootReady,"Normal resume after certificate error");
            Check(AudioListener.volume==0&&AudioListener.pause,"Certificate fixture remains silent");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject { ["status"]="PASS_CERTIFICATE_MENU_PLAYER",
                ["checks"]=new JArray(checks),["scope"]="Actual Player normal library/certificate import, error/back/check/resume handlers and captures. Ephemeral synthetic claim and isolated explicit trust/revoke, no production trust, independent gameplay certification, physical button input, Windows or driving acceptance." }.ToString());
            Application.Quit();
        }
    }
}
