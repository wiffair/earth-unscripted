using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace EarthUnscripted.Radio
{
    // Explicit isolated player proof only. Never instantiated during normal play.
    public static class RadioNativeVerification
    {
        public static IEnumerator Exercise(GameSession session,Gamepad pad,Action<bool,string> check)
        {
            check(SinglePlayerBeta.VerificationOutput!=null,"Radio proof has isolated beta data");
            bool originalMenu=session.Menu,originalLock=session.Car.StartLocked;
            try
            {
            var radio=session.GetComponent<CarRadio>();double deadline=Time.realtimeSinceStartupAsDouble+90;
            while(!radio||!radio.Ready){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Radio import readiness");yield return null;}
            check(radio.Catalog.ActiveStations.Count()>=1&&radio.Catalog.songs.Count>=6,"Delivered radio pack discovers delivered active stations and global songs");
            check(radio.Catalog.intros.Count>=12&&radio.Catalog.jingles.Count>=4,"Delivered radio pack imports delivered intros and jingles");
            check(radio.ReviewClips.All(RadioPlan.Eligible),"All exact user-approved radio clips are eligible");
            var clips=new JArray();
            foreach(var record in radio.Catalog.songs.Values.Concat(radio.Catalog.intros.Values).Concat(radio.Catalog.jingles.Values))
            {
                string path=radio.Library.MediaPath(record);AudioType type=path.EndsWith(".wav",StringComparison.Ordinal)?AudioType.WAV:AudioType.MPEG;
                using(var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,type))
                {
                    ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio=false;request.timeout=20;yield return request.SendWebRequest();
                    check(request.result==UnityWebRequest.Result.Success,"Native decoder opens "+(string)record["id"]);
                    var clip=DownloadHandlerAudioClip.GetContent(request);check(clip&&clip.length>.1f&&clip.channels>0,"Decoded clip has real audio frames: "+(string)record["id"]);
                    float peak=0;double energy=0;int samples=0,nonFinite=0,clipped=0;
                    try
                    {
                        int count=Math.Min(4096,clip.samples);var data=new float[count*clip.channels];
                        foreach(float fraction in new[]{.05f,.35f,.65f,.9f})
                        {
                            check(clip.GetData(data,Math.Min(clip.samples-count,(int)(clip.samples*fraction))),"Decoded PCM is readable: "+(string)record["id"]);
                            foreach(float value in data){if(float.IsNaN(value)||float.IsInfinity(value)){nonFinite++;continue;}peak=Mathf.Max(peak,Mathf.Abs(value));energy+=value*value;samples++;if(Mathf.Abs(value)>=.999f)clipped++;}
                        }
                        check(nonFinite==0&&peak>.0001f,"Decoded audio is finite and non-silent: "+(string)record["id"]);
                        clips.Add(new JObject{{"id",(string)record["id"]},{"seconds",clip.length},{"channels",clip.channels},{"sampleRate",clip.frequency},{"sampledPeak",peak},{"sampledRms",Math.Sqrt(energy/Math.Max(1,samples))},{"sampledClippedSamples",clipped},{"nonFiniteSamples",nonFinite}});
                    }
                    finally{UnityEngine.Object.Destroy(clip);}
                }
                yield return null;
            }
            session.Car.StartLocked=true;session.SetMenu(false);
            int initialPlayed=radio.Played.Count;
            string station=(string)radio.Catalog.ActiveStations.First()["id"];
            radio.ChangeStation(station);
            deadline=Time.realtimeSinceStartupAsDouble+70;
            while(!(radio.SegmentType=="song"&&radio.Output.isPlaying))
            {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Actual normalized song playback: "+radio.Status);yield return null;}
            // A random join can land in a song's last seconds. Retune that case
            // before continuity assertions so natural EOF cannot race the test.
            for(int attempt=0;radio.Output.clip.length-radio.Output.time<5;attempt++)
            {
                if(attempt>=10)throw new IOException("Radio continuity fixture could not join away from EOF");
                radio.ChangeStation(station);deadline=Time.realtimeSinceStartupAsDouble+70;
                while(!(radio.SegmentType=="song"&&radio.Output.isPlaying))
                {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Radio continuity fixture tune");yield return null;}
            }
            var played=radio.Played.Skip(initialPlayed).ToArray();
            check(played.Last().type=="song"&&played.All(s=>s.station==station),"Actual radio source plays random links followed by station music");
            check(radio.CurrentLevel!=null&&radio.CurrentLevel.outputPeak<=.651f&&radio.CurrentLevel.gatedRms<=.141f,"Playing PCM has measured mix headroom");
            var currentClip=radio.Output.clip;float position=radio.Output.time;int playedCount=radio.Played.Count,recoveries=radio.PlaybackRecoveries;
            radio.Output.Stop();yield return new WaitForSecondsRealtime(.3f);
            check(radio.Output.clip==currentClip&&radio.Output.isPlaying&&radio.Output.time>=position-.1f&&radio.Played.Count==playedCount&&radio.PlaybackRecoveries>recoveries,"A lost source resumes the same programme instead of skipping songs");
            check(radio.MediaErrors.Count==0&&radio.Output.spatialBlend==0&&!radio.Output.loop&&radio.Output.gameObject!=session.gameObject,"One non-looping radio output plays decoded media without errors");
            session.SetMenu(true);yield return null;yield return null;
            float pausedPosition=radio.Output.time;playedCount=radio.Played.Count;
            yield return new WaitForSecondsRealtime(.3f);
            check(radio.Output.clip==currentClip&&!radio.Output.isPlaying&&Mathf.Abs(radio.Output.time-pausedPosition)<.03f&&radio.Played.Count==playedCount,"Menu pause preserves the same clip and exact programme position");
            session.SetMenu(false);yield return new WaitForSecondsRealtime(.3f);
            check(radio.Output.clip==currentClip&&radio.Output.isPlaying&&radio.Output.time>pausedPosition+.1f&&radio.Played.Count==playedCount,"Menu resume continues the same clip instead of retuning");
            // Deliberately span EOF with a main-thread/render hitch while the
            // independent native audio clock continues playing the last samples.
            recoveries=radio.PlaybackRecoveries;playedCount=radio.Played.Count;
            radio.Output.timeSamples=Math.Max(1,radio.Output.clip.samples-radio.Output.clip.frequency/4);
            yield return null;yield return null;
            System.Threading.Thread.Sleep(600);yield return null;
            deadline=Time.realtimeSinceStartupAsDouble+15;
            while(radio.Played.Count==playedCount){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Radio natural EOF during render hitch");yield return null;}
            check(radio.PlaybackRecoveries==recoveries,"Natural EOF during a render hitch advances the broadcast instead of replaying the song tail");
            yield return new WaitForSecondsRealtime(2);
            radio.Save();string settings=Path.Combine(radio.Library.Root,"settings.json");deadline=Time.realtimeSinceStartupAsDouble+10;
            while(!File.Exists(settings)||(string)JObject.Parse(File.ReadAllText(settings))["station"]!=station)
            {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Radio settings save");yield return null;}
            check(JObject.Parse(File.ReadAllText(settings))["history"] is JObject,"Radio station, resume and per-station history saved separately from career");
            var ui=UnityEngine.Object.FindFirstObjectByType<DrivingUI>();session.SetMenu(true);ui.ShowRadio();yield return null;yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"radio-menu.png"));yield return new WaitForEndOfFrame();
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"radio-audio.json"),new JObject{{"passed",true},{"decodedClips",clips},{"played",new JArray(played.Select(s=>new JObject{{"type",s.type},{"station",s.station},{"song",s.song},{"id",s.id}}))},{"settingsPath",settings},{"activeStations",radio.Catalog.ActiveStations.Count()},{"mediaErrors",new JArray(radio.MediaErrors)}}.ToString());
            }
            finally{if(session){session.SetMenu(originalMenu);if(session.Car)session.Car.StartLocked=originalLock;}}
        }
        public static IEnumerator Resume(GameSession session,Action<bool,string> check)
        {
            var radio=session.GetComponent<CarRadio>();double deadline=Time.realtimeSinceStartupAsDouble+60;
            while(!radio||!radio.Ready){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Shared radio resume");yield return null;}
            string path=Path.Combine(radio.Library.Root,"settings.json");if(!File.Exists(path))yield break;
            var saved=JObject.Parse(File.ReadAllText(path));
            check(radio.Settings.station==(string)saved["station"],"Other graphics build resumes exact selected radio station");
        }
    }
}
