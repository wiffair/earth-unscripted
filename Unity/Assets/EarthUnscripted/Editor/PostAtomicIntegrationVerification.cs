using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    // Explicit batch-only verification against isolated generated careers/cache.
    public static class PostAtomicIntegrationVerification
    {
        public static void Run()
        {
            int exit=1;
            string output="/private/tmp/rr019-post-atomic-unity-"+DateTime.UtcNow.Ticks;
            string previous=PlayerProfile.StorageOverride;
            var receipt=new JObject{{"status","FAIL"},{"installedReleaseChanged",false},{"normalWorld13Activation",false}};
            try
            {
                if(!Application.isBatchMode||Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())
                    throw new InvalidOperationException("Isolated batch edit-mode verification required");
                Directory.CreateDirectory(output);
                PlayerProfile.StorageOverride=Path.Combine(output,"isolated-profile.json");
                int migration=ProfileMigrationSafetyVerification.Verify(Path.Combine(output,"migration"));
                if(migration!=0)throw new InvalidOperationException("Actual profile migration/save guards failed");
                string cacheOutput="/private/tmp/rr019-canonical-cache-proof-post-atomic-"+Path.GetFileName(output);
                receipt["canonicalCacheOutput"]=cacheOutput;
                var cache=CanonicalRaw13CacheChecks.Execute(cacheOutput);
                if(cache.status!="PASS"||cache.checks!=80)throw new InvalidOperationException("Canonical raw cache verification failed");
                if(PlayerProfile.StorageOverride!=Path.Combine(output,"isolated-profile.json"))
                    throw new InvalidOperationException("Profile override changed during verification");
                receipt["status"]="PASS";
                receipt["canonicalCacheChecks"]=cache.checks;
                receipt["migration"]=JObject.Parse(File.ReadAllText(Path.Combine(output,"migration","receipt.json")));
                receipt["careerSchema"]=PlayerProfile.CurrentVersion;
                receipt["lanProtocol"]=LanSession.Protocol;
                exit=0;
            }
            catch(Exception e){receipt["error"]=e.ToString();Debug.LogException(e);}
            finally
            {
                PlayerProfile.StorageOverride=previous;
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());
                Debug.Log("POST ATOMIC "+(exit==0?"PASS ":"FAIL ")+output);
            }
            EditorApplication.Exit(exit);
        }
    }
}
