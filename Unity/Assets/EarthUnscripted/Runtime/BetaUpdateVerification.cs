using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using EarthUnscripted.Updates;
using EarthUnscripted.Radio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    // Explicit isolated native proof; never alters the owner's radio/profile.
    public sealed class BetaUpdateVerification : MonoBehaviour
    {
        readonly JArray checks=new JArray();
        void Check(bool okay,string label){checks.Add(new JObject{{"name",label},{"passed",okay}});if(!okay)throw new InvalidOperationException(label);}
        IEnumerator Start()
        {
            Application.runInBackground=true;double deadline=Time.realtimeSinceStartupAsDouble+180;
            GameSession session=null;CarRadio radio=null;
            while(Time.realtimeSinceStartupAsDouble<deadline)
            {session=FindAnyObjectByType<GameSession>();radio=session?session.GetComponent<CarRadio>():null;if(session&&session.BootReady&&radio&&radio.Ready)break;yield return null;}
            try{Check(session&&session.BootReady&&radio&&radio.Ready,"game and radio ready");}
            catch(Exception error){Finish(false,error.Message);yield break;}
            // Keep verification inaudible, but let the decoder clock advance.
            AudioListener.volume=0;AudioListener.pause=false;
            var fetch=Task.Run(()=>UpdateTransport.Feed("",CancellationToken.None));
            while(!fetch.IsCompleted)yield return null;
            try {
                Check(!fetch.IsFaulted&&!fetch.IsCanceled,"actual player retrieves the live signed feed");
                var prior=CultureInfo.CurrentCulture;
                try { foreach(string culture in new[]{"en-GB","en-US","de-DE","fr-FR","ja-JP","zh-CN","ar-SA"}) {
                    CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
                    var catalogue=UpdateProtocol.ReadSigned(fetch.Result.Item1,0);
                    Check(catalogue["issuedUtc"].Type==JTokenType.String,"signed UTC dates remain invariant strings under "+culture);
                } } finally {CultureInfo.CurrentCulture=prior;}
            } catch(Exception error){Finish(false,error.ToString());yield break;}
            session.SetMenu(false);radio.Settings.introChance=0;radio.Settings.jingleEvery=0;
            string station=radio.Catalog.ActiveStations.Select(s=>(string)s["id"]).First();radio.ChangeStation(station);
            while(Time.realtimeSinceStartupAsDouble<deadline&&(!radio.HasCurrentSegment||radio.SegmentType!="song"))yield return null;
            string nextSong="";int previousCount=0;
            try
            {
                Check(radio.HasCurrentSegment&&radio.SegmentType=="song","playing song before live update");
                var clip=radio.Output.clip;float seconds=radio.Output.time;string current=radio.Settings.song;
                var catalogue=JsonConvert.DeserializeObject<RadioCatalog>(JsonConvert.SerializeObject(radio.Catalog));
                nextSong=catalogue.songs.Keys.First(id=>id!=current);
                catalogue.playlists[station]["entries"]=new JArray(new JObject{{"song_id",nextSong},{"weight",1},{"enabled",true}});
                var added=(JObject)catalogue.stations[station].DeepClone();added["id"]="update-proof-station";added["name"]="Update Proof";
                var presenter=(JObject)catalogue.presenters[(string)added["presenter_id"]].DeepClone();presenter["id"]="update-proof-presenter";presenter["station_id"]="update-proof-station";catalogue.presenters.Add("update-proof-presenter",presenter);added["presenter_id"]="update-proof-presenter";catalogue.stations.Add("update-proof-station",added);
                var playlist=(JObject)catalogue.playlists[station].DeepClone();playlist["station_id"]="update-proof-station";catalogue.playlists.Add("update-proof-station",playlist);
                previousCount=radio.Played.Count;radio.AcceptUpdatedCatalog(catalogue);
                Check(radio.Output.clip==clip&&Mathf.Abs(radio.Output.time-seconds)<.05f&&radio.HasCurrentSegment,"live catalogue preserves audio clip, time and generation");
                Check(radio.Catalog.ActiveStations.Any(s=>(string)s["id"]=="update-proof-station"),"new station is available during current playback");
                // End the current source deterministically, as the decoder does at EOF.
                // Seeking MP3s near EOF is backend-dependent and is not the behaviour under test.
                radio.Output.Stop();
            }
            catch(Exception error){Finish(false,error.Message);yield break;}
            double nextDeadline=Time.realtimeSinceStartupAsDouble+30;
            while(Time.realtimeSinceStartupAsDouble<nextDeadline&&!radio.Played.Skip(previousCount).Any(s=>s.type=="song"&&s.song==nextSong))yield return null;
            try
            {
                Check(radio.Played.Skip(previousCount).Any(s=>s.type=="song"&&s.song==nextSong),"next song follows the updated playlist (menu="+session.Menu+", listenerPaused="+AudioListener.pause+", radio="+radio.NowPlaying+", errors="+string.Join(";",radio.MediaErrors)+")");
                session.SetMenu(true);var ui=FindAnyObjectByType<DrivingUI>();ui.ShowAppUpdates();
                session.AppUpdates.SetAutomatic(true);Check(session.AppUpdates.Settings.automaticUpdates,"automatic update setting can be enabled");session.AppUpdates.SetAutomatic(false);
                Check(File.ReadAllText(Path.Combine(SinglePlayerBeta.DataRoot,"Updates/settings.json")).Contains("automaticUpdates"),"update setting persisted separately from career");
            }
            catch(Exception error){Finish(false,error.Message);yield break;}
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"updates-menu.png"));yield return null;Finish(true,"");
        }
        void Finish(bool passed,string error)
        {File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"update-result.json"),new JObject{{"passed",passed},{"checks",checks},{"error",error},{"profile",SinglePlayerBeta.DataRoot}}.ToString());Application.Quit(passed?0:2);}
    }
}
