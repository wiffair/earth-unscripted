using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public static class Beta37Verification
    {
        public static IEnumerator Exercise(GameSession session,Action<bool,string> check)
        {
            check(SinglePlayerBeta.VerificationOutput!=null,"Provider acceptance uses isolated beta progress");
            var host=session.World.AutomaticWorld;
            string folder=(string)typeof(AutomaticWorldHost).GetField("sessionFolder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
            var plan=JObject.Parse(File.ReadAllText(Path.Combine(folder,"Worker/world-data-selection.json")));
            check((int)plan["schema"]==1&&(string)plan["selectedTerrain"]=="terrain.gb","Actual packaged worker selects British terrain through the shared registry");
            check((string)plan["catalogueVersion"]==WorldProviderRegistry.Version,"Player and worker agree on provider catalogue version");
            var ui=UnityEngine.Object.FindFirstObjectByType<DrivingUI>();var tab=typeof(DrivingUI).GetField("tab",BindingFlags.Instance|BindingFlags.NonPublic);int old=(int)tab.GetValue(ui);
            session.SetMenu(true);tab.SetValue(ui,27);yield return null;yield return new WaitForEndOfFrame();
            check(ui.MenuTab==27,"Data source status is drawn by the real menu");
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            try{File.WriteAllBytes(Path.Combine(SinglePlayerBeta.VerificationOutput,"world-data-sources.png"),image.EncodeToPNG());}
            finally{UnityEngine.Object.Destroy(image);tab.SetValue(ui,old);}
        }
    }
}
