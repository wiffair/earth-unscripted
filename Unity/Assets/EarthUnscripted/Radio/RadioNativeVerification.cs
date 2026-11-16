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
            var radio=session.GetComponent<CarRadio>();double deadline=Time.realtimeSinceStartupAsDouble+90;
            while(!radio||!radio.Ready){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Radio import readiness");yield return null;}
            check(radio.Catalog.ActiveStations.Count()==1&&radio.Catalog.songs.Count==6,"Delivered radio pack discovers one active station and six global songs");
            check(radio.Catalog.intros.Count==12&&radio.Catalog.jingles.Count==4,"Delivered radio pack imports twelve intros and four jingles");
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
            string station=(string)radio.Catalog.ActiveStations.First()["id"];
            radio.Settings.introChance=1;radio.Settings.jingleEvery=1;radio.ChangeStation(station);radio.Settings.completedSongs=1;
            deadline=Time.realtimeSinceStartupAsDouble+70;
            while(!(radio.SegmentType=="song"&&radio.Output.isPlaying&&radio.Played.Count>=3))
            {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Actual jingle/intro/song playback: "+radio.Status);yield return null;}
            var played=radio.Played.TakeLast(3).ToArray();
            check(string.Join(",",played.Select(s=>s.type))=="jingle,intro,song","Actual radio source sequences jingle then exact song intro then music");
            check(played.All(s=>s.station==station)&&played[1].song==played[2].id,"Actual speech references the selected station and following song");
            check(radio.MediaErrors.Count==0&&radio.Output.spatialBlend==0&&!radio.Output.loop&&radio.Output.gameObject!=session.gameObject,"One non-looping radio output plays decoded media without errors");
            yield return new WaitForSecondsRealtime(2);
            radio.Save();string settings=Path.Combine(radio.Library.Root,"settings.json");deadline=Time.realtimeSinceStartupAsDouble+10;
            while(!File.Exists(settings)||(string)JObject.Parse(File.ReadAllText(settings))["station"]!=station)
            {if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Radio settings save");yield return null;}
            check(JObject.Parse(File.ReadAllText(settings))["history"] is JObject,"Radio station, resume and per-station history saved separately from career");
            var ui=UnityEngine.Object.FindFirstObjectByType<DrivingUI>();session.SetMenu(true);ui.ShowRadio();yield return null;yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(SinglePlayerBeta.VerificationOutput,"radio-menu.png"));yield return new WaitForEndOfFrame();
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"radio-audio.json"),new JObject{{"passed",true},{"decodedClips",clips},{"played",new JArray(played.Select(s=>new JObject{{"type",s.type},{"station",s.station},{"song",s.song},{"id",s.id}}))},{"settingsPath",settings},{"activeStations",radio.Catalog.ActiveStations.Count()},{"mediaErrors",new JArray(radio.MediaErrors)}}.ToString());
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
