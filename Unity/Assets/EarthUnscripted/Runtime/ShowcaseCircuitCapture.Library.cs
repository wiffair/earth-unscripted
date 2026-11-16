// Explicit opt-in Player observer. The existing capture entry point requires
// isolated storage, forces silence and bounds the owned test lifecycle.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class ShowcaseCircuitCapture
    {
        IEnumerator RunLibraryCheckFixture()
        {
            Check(AudioListener.volume==0&&AudioListener.pause,"Silent before library fixture");
            Prepare(session);Check(session.TrySave(),"Isolated starting profile saved");
            byte[] saved=File.ReadAllBytes(PlayerProfile.StoragePath);
            string originalRegion=session.Profile.region;int originalCredits=session.Economy.Credits;
            var originalSession=session;var ui=session.GetComponent<DrivingUI>();ui.ShowImportedPacks();
            var library=ui.ImportedPacks;
            while(library.Busy)yield return null;
            Check(library.RevisionCount==0,"Fresh isolated library starts empty");
            var messages=new JArray();
            string[] args=Environment.GetCommandLineArgs();
            string legacy=args[Array.IndexOf(args,"-libraryLegacyPack")+1];
            foreach(var item in new[]{new[]{archive,"silverstone-builder-private-draft","9/9 elements readable","current"},
                                      new[]{legacy,"silverstone-showcase","4/4 elements readable","legacy"}})
            {
                library.ImportArchive(item[0]);while(library.Busy)yield return null;
                var rows=(IReadOnlyList<WorldPackInventoryItem>)typeof(WorldPackMenu).GetField("rows",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(library);
                int index=rows.ToList().FindIndex(row=>row.Manifest?.Id==item[1]);
                Check(index>=0,"Normal import retains "+item[3]+" revision");
                library.CheckContent(index);while(library.Busy)yield return null;
                Check(library.Status.Contains(item[2]),"Normal library CHECK reports complete "+item[3]+" supported subset");
                Check(library.Status.Contains("1/1 driving layouts readable"),"Normal library CHECK recognises "+item[3]+" layout");
                Check(library.Status.Contains("No scene activation or native-validation claim"),"Readability stays separate from gameplay certification");
                messages.Add(library.Status);
                // Leave the normal library UI/state unchanged until the async
                // screenshot has completed; no fixture teleport or scene swap.
                string image=prefix+"-library-"+item[3]+".png";
                yield return new WaitForSecondsRealtime(.3f);yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(image);
                float began=Time.realtimeSinceStartup;
                while(!File.Exists(image)||new FileInfo(image).Length==0)
                {Check(Time.realtimeSinceStartup-began<10,"Library capture completes within timeout");yield return null;}
            }
            Check(session==originalSession&&session.BootReady&&!session.BootFailed,"Inspection retains original working world");
            Check(session.Profile.region==originalRegion&&session.Economy.Credits==originalCredits,"Inspection retains credits and destination");
            Check(File.ReadAllBytes(PlayerProfile.StoragePath).SequenceEqual(saved),"Inspection leaves isolated career bytes unchanged");
            session.ResumeDrive();yield return null;
            Check(!session.Menu&&session.BootReady,"Normal resume exits library to original world");
            Check(AudioListener.volume==0&&AudioListener.pause,"Library test remains silent");
            File.WriteAllText(prefix+"-showcase-result.json",new JObject{{"status","PASS_LIBRARY_PLAYER_HANDLERS"},{"checks",new JArray(checks)},{"messages",messages},{"scope","Actual Player normal import/check/resume handlers and rendered library; isolated saves. No physical button input, circuit driving, publisher authentication or Windows hardware claim."}}.ToString());
            Application.Quit();
        }
    }
}
