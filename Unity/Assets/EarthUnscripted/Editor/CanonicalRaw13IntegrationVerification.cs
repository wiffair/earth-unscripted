using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class CanonicalRaw13IntegrationVerification
    {
        static string FileHash(string path)=>File.Exists(path)?World13DocumentCodec.Hash(File.ReadAllBytes(path)):"missing";
        static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        public static void Run()
        {
            int exit=1;string output="/private/tmp/rr019-canonical-integration-unity-"+DateTime.UtcNow.Ticks;Directory.CreateDirectory(output);
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=FileHash(career);var report=new JObject{{"status","RUNNING"},{"scope","Isolated canonical raw/legacy cache contracts only; no prepared, boot, peer or world admission"},{"normalActivation",false}};
            try
            {
                Require(!Application.isPlaying&&UnityEngine.Object.FindAnyObjectByType<GameSession>()==null,"Isolated data contract checks only");
                var numbers=CanonicalGeography13Checks.Execute(Path.Combine(output,"canonical-numbers"));Require(numbers.status=="PASS"&&numbers.checks==1267,"Canonical number/field contract count changed");report["canonicalNumberChecks"]=numbers.checks;
                var source=OriginalRaw13ConversionChecks.Execute(Path.Combine(output,"canonical-source"));Require(source.status=="PASS"&&source.checks==49,"Canonical source identity vectors failed");report["canonicalSourceChecks"]=source.checks;
                var owner=CanonicalRaw13OwnerChecks.Execute(Path.Combine(output,"canonical-owner"));Require(owner.status=="PASS"&&owner.checks==27,"Canonical owner contract count changed");report["canonicalOwnerChecks"]=owner.checks;
                string fixedOutput=Path.Combine(output,"legacy-fixed-cache");var legacy=Raw13CacheChecks.Execute(fixedOutput,"EarthUnscripted/"+Application.version+"/geometry13","/private/tmp/rr019-integration-drafts/ActualPantside","/private/tmp/rr018-fresh-layout");
                Require(legacy.status=="PASS"&&legacy.checks==43,"Existing fixed raw cache regression failed");Require(legacy.documentSha=="d6ed4c8c979b38f023c7fcc9675fe736f12c3d511e83f848e4a573e6548454d6","Existing fixed raw document bytes changed");report["legacyFixedCacheChecks"]=legacy.checks;
                string bundle=Path.Combine(fixedOutput,"Normal","Raw13-v1",legacy.cacheKey);var restart=Raw13RestartChecks.Execute(Path.Combine(output,"legacy-cold-restart"),bundle);Require(restart.status=="PASS"&&restart.checks==45,"Existing cold-restart contract regression failed");report["legacyColdRestartChecks"]=restart.checks;
                Require(FileHash(career)==before,"Protected real career changed");report["careerUnchanged"]=true;report["checks"]=numbers.checks+source.checks+owner.checks+legacy.checks+restart.checks;report["status"]="PASS";exit=0;
            }
            catch(Exception e){report["status"]="FAIL";report["error"]=e.ToString();Debug.LogException(e);}
            finally{File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString());Debug.Log("CANONICAL RAW INTEGRATION "+(exit==0?"PASS":"FAIL")+" "+output);EditorApplication.Exit(exit);}
        }
    }
}
