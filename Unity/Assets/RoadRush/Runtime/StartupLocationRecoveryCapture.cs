using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Explicit isolated native acceptance only. Never created in ordinary play.</summary>
    public sealed class StartupLocationRecoveryCapture : MonoBehaviour
    {
        string prefix;GameSession session;
        public void Initialize(GameSession owner,string path)
        {
            if(string.IsNullOrEmpty(PlayerProfile.StorageOverride)||Path.GetFullPath(PlayerProfile.StorageOverride)!=Path.GetFullPath(path+"-profile.json"))throw new InvalidOperationException("Isolated profile required");
            session=owner;prefix=path;DontDestroyOnLoad(gameObject);StartCoroutine(Guard());
        }
        IEnumerator Guard()
        {
            var routine=Verify();
            while(true)
            {
                object next=null;bool more=false;Exception error=null;
                try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){error=e;}
                if(error!=null){File.WriteAllText(prefix+"-startup-result.json","{\"status\":\"FAIL\",\"error\":"+Newtonsoft.Json.JsonConvert.SerializeObject(error.ToString())+"}");Application.Quit(1);yield break;}
                if(!more)yield break;yield return next;
            }
        }
        IEnumerator Verify()
        {
            int credits=session.Profile.credits;string garage=JsonUtility.ToJson(session.Profile.garage);
            session.GetComponent<DrivingUI>().ShowSettings();for(int i=0;i<8;i++)yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(prefix+"-startup-settings.png");for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();
            var broken=session.World.Route.Region.SnapshotCopy();broken.id="download-invalid-location-proof";broken.roadWidthSchema=0;broken.networkMapId="";
            broken.waters=new[]{new GeoWater{name="invalid height fixture",width=8,points=new[]{Vector3.zero,new Vector3(0,25000,10)}}};
            string cache=prefix+"-profile.json.regions";Directory.CreateDirectory(cache);File.WriteAllText(Path.Combine(cache,broken.id+".json"),JsonUtility.ToJson(broken));
            session.Profile.region=broken.id;if(!session.Profile.Save())throw new IOException("Could not save isolated invalid location");
            LocationLoading.Open("Invalid location acceptance fixture");double deadline=Time.realtimeSinceStartupAsDouble+120;
            do{yield return null;session=FindAnyObjectByType<GameSession>();}while((!session||!session.BootFailed)&&Time.realtimeSinceStartupAsDouble<deadline);
            if(!session||!session.BootFailed||!session.CanReturnFromFailedLocation)throw new InvalidOperationException("Failed ordinary map has no safe recovery");
            for(int i=0;i<8;i++)yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-startup-error-recovery.png");for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();
            // Same handler as the visible recovery button; no profile-file editing fallback.
            if(!session.ReturnToCountyKeepingCareer())throw new InvalidOperationException("Safe recovery save refused");
            deadline=Time.realtimeSinceStartupAsDouble+120;
            do{yield return null;session=FindAnyObjectByType<GameSession>();}while((!session||!session.BootReady||LocationLoading.Busy)&&Time.realtimeSinceStartupAsDouble<deadline);
            if(!session||!session.BootReady||LocationLoading.Busy||!session.Menu||session.Profile.region!=""||session.Profile.credits!=credits||JsonUtility.ToJson(session.Profile.garage)!=garage)throw new InvalidOperationException("Safe recovery lost career or did not return to menu");
            for(int i=0;i<8;i++)yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(prefix+"-startup-recovered-menu.png");for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();
            File.WriteAllText(prefix+"-startup-result.json","{\"status\":\"PASS_NATIVE_FAILED_MAP_RECOVERY\",\"credits\":"+credits+",\"garagePreserved\":true,\"returnedToMenu\":true,\"normalRecoveryHandler\":true}");Application.Quit();
        }
    }
}
