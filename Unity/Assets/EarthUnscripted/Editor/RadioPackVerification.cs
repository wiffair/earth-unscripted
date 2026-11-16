using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Radio;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class RadioPackVerification
    {
        static int checks;
        static void Check(bool test,string message){checks++;if(!test)throw new IOException("Radio policy: "+message);}
        public static void RunCLI(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void Run()
        {
            checks=0;string root=Path.Combine(Path.GetTempPath(),"EarthUnscriptedRadio41-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string folder=Path.Combine(Application.streamingAssetsPath,"RadioPacks"),pack=Path.Combine(folder,"EarthUnscripted-IronAndAsh-2.0.0.euradio");
                var approved=new HashSet<string>(JArray.Parse(File.ReadAllText(Path.Combine(folder,"approved-audio.json"))).Values<string>());
                var library=new RadioLibrary(Path.Combine(root,"Library"));var c=library.Import(pack,approved);
                Check(c.ActiveStations.Count()==1&&c.stations.Count==1,"only delivered station active; planned concepts excluded");
                Check(c.songs.Count==6&&c.intros.Count==12&&c.jingles.Count==4&&c.playlists.Count==1&&c.presenters.Count==1,"actual pack logical counts");
                Check(c.intros.Values.All(RadioPlan.Eligible)&&c.jingles.Values.All(RadioPlan.Eligible),"all exact user-approved clips enabled");
                Check(Directory.GetFiles(Path.Combine(library.Root,"Media")).Length==22,"only22referenced audio files enter store; original batches excluded");
                string pointer=File.ReadAllText(Path.Combine(library.Root,"current.json"));var again=library.Import(pack,approved);
                Check(again.songs.Count==6&&File.ReadAllText(Path.Combine(library.Root,"current.json"))==pointer,"same revision import is idempotent");
                Check(Directory.GetFiles(Path.Combine(library.Root,"Media")).Length==22,"idempotent import has no duplicate media");
                string first=(string)c.songs.Values.First()["id"],original=(string)c.stations.Values.First()["id"];
                var station=new JObject{{"id","fixture-radio"},{"name","Fixture station"},{"frequency_mhz",99.1},{"status","active"},{"presenter_id","fixture-presenter"},{"playlist","playlists/fixture.json"}};
                var playlist=new JObject{{"station_id","fixture-radio"},{"enabled",true},{"entries",new JArray(new JObject{{"song_id",first},{"weight",1},{"enabled",true}})},{"rules",new JObject{{"shuffle",true},{"avoid_last_n_songs",3}}}};
                var presenter=new JObject{{"presenters",new JArray(new JObject{{"id","fixture-presenter"},{"station_id","fixture-radio"},{"voice","Test voice"}})}};
                var files=new Dictionary<string,byte[]>{{"stations/fixture.json",Bytes(station)},{"playlists/fixture.json",Bytes(playlist)},{"stations/fixture-presenter.json",Bytes(presenter)}};
                var manifest=Manifest("fixture-pack","1.0.0");manifest["station_definitions"]=new JArray("stations/fixture.json");manifest["playlists"]=new JArray("playlists/fixture.json");manifest["presenters"]=new JArray("stations/fixture-presenter.json");
                string synthetic=Pack(root,"synthetic.zip",manifest,files);c=library.Import(synthetic);
                Check(c.ActiveStations.Count()==2&&c.songs.Count==6,"new station reuses global songs without duplicate song records");
                Check(Directory.GetFiles(Path.Combine(library.Root,"Media")).Length==22,"shared song membership duplicates no media");
                var sequence=RadioPlan.Sequence(c,"fixture-radio",c.songs[first],new System.Random(4),1,3,3);
                Check(sequence.Count==1&&sequence[0].type=="song","new station never borrows another station's intro/jingle");
                byte[] voice=Wave(7);var intro=new JObject{{"intros",new JArray(new JObject{{"id","fixture-intro"},{"station_id","fixture-radio"},{"song_id",first},{"presenter_id","fixture-presenter"},{"audio","audio/fixture.wav"},{"enabled",true},{"review_status","approved"}})}};
                var jingle=new JObject{{"jingles",new JArray(new JObject{{"id","fixture-jingle"},{"station_id","fixture-radio"},{"file","audio/fixture.wav"},{"enabled",true},{"review_status","approved"}})}};
                var drop=new JObject{{"drops",new JArray(new JObject{{"id","fixture-drop"},{"station_id","fixture-radio"},{"audio","audio/fixture.wav"},{"type","station_link"},{"enabled",true},{"review_status","approved"}})}};
                files=new Dictionary<string,byte[]>{{"intros.json",Bytes(intro)},{"jingles.json",Bytes(jingle)},{"drops.json",Bytes(drop)},{"audio/fixture.wav",voice}};
                manifest=Manifest("fixture-pack","1.1.0");manifest["intros"]=new JArray("intros.json");manifest["jingles"]=new JArray("jingles.json");manifest["drops"]=new JArray("drops.json");Integrity(manifest,"audio/fixture.wav",voice);
                c=library.Import(Pack(root,"voice.zip",manifest,files));sequence=RadioPlan.Sequence(c,"fixture-radio",c.songs[first],new System.Random(4),1,15,3);
                Check(string.Join(",",sequence.Select(s=>s.type))=="jingle,drop,intro,song","jingle/drop/intro/song sequence uses distinct segment types");
                Check(sequence.Where(s=>s.type!="song").All(s=>(string)s.record["station_id"]=="fixture-radio"),"all speech/jingles match exact station");
                Check(RadioPlan.Sequence(c,original,c.songs[first],new System.Random(8),1,15,3).All(s=>s.id!="fixture-intro"),"synthetic intro never leaks into original station");
                Check(RadioPlan.Sequence(c,"fixture-radio",c.songs.Values.Last(),new System.Random(5),1,0,0).All(s=>s.type!="intro"),"intro composite includes song ID");
                Check(Directory.GetFiles(Path.Combine(library.Root,"Media")).Length==23,"three segment records share one audio blob");
                byte[] tune=Wave(19);var song=new JObject{{"songs",new JArray(new JObject{{"id","fixture-new-song"},{"artist","Fixture artist"},{"title","Fixture tune"},{"audio","audio/new.wav"}})}};
                var updated=(JObject)playlist.DeepClone();updated["entries"]=new JArray(new JObject{{"song_id","fixture-new-song"},{"enabled",true},{"weight",2}});
                files=new Dictionary<string,byte[]>{{"songs.json",Bytes(song)},{"playlists/fixture.json",Bytes(updated)},{"audio/new.wav",tune}};manifest=Manifest("fixture-pack","1.2.0");manifest["song_catalogues"]=new JArray("songs.json");manifest["playlists"]=new JArray("playlists/fixture.json");Integrity(manifest,"audio/new.wav",tune);
                string update=Pack(root,"update.zip",manifest,files);c=library.Import(update);
                Check(c.songs.Count==7&&RadioPlan.Song(c,"fixture-radio",new List<string>(),new System.Random(9))["id"].Value<string>()=="fixture-new-song","future song/playlist update requires no code");
                Check(((JArray)c.playlists[original]["entries"]).Any(e=>(string)e["song_id"]==first)&&c.songs.ContainsKey(first),"playlist removal does not delete shared songs");
                var history=new List<string>();var rng=new System.Random(11);for(int i=0;i<30;i++){var selected=RadioPlan.Song(c,original,history,rng);Check(!history.Skip(Math.Max(0,history.Count-3)).Contains((string)selected["id"]),"shuffle avoids last3songs");history.Add((string)selected["id"]);}
                Check((string)RadioPlan.Song(c,"fixture-radio",new List<string>{"fixture-new-song"},rng)["id"]=="fixture-new-song","single-song station remains playable");
                ((JArray)c.playlists["fixture-radio"]["entries"])[0]["enabled"]=false;Check(RadioPlan.Song(c,"fixture-radio",history,rng)==null,"disabled-only playlist handles0songs");
                c=library.Load();pointer=File.ReadAllText(Path.Combine(library.Root,"current.json"));int count=Directory.GetFiles(Path.Combine(library.Root,"Media")).Length;
                void Reject(string path,string reason){bool rejected=false;try{library.Import(path);}catch(Exception e) when(e is InvalidDataException||e is ArgumentException){rejected=true;}Check(rejected,reason);Check(File.ReadAllText(Path.Combine(library.Root,"current.json"))==pointer&&Directory.GetFiles(Path.Combine(library.Root,"Media")).Length==count,"failed import commits no partial metadata/media");}
                Reject(synthetic,"older revisions rejected");
                files["songs.json"]=Bytes(new JObject{{"songs",new JArray(new JObject{{"id","fixture-new-song"},{"artist","Changed"},{"title","Changed"},{"audio","audio/new.wav"}})}});
                Reject(Pack(root,"conflict.zip",manifest,files),"same revision with changed metadata rejected");
                var bad=Manifest("bad-pack","1.0.0");files=new Dictionary<string,byte[]>{{"../escape.wav",voice}};Reject(Pack(root,"traversal.zip",bad,files),"path traversal rejected even for unused archive entries");
                files=new Dictionary<string,byte[]>{{"audio/fixture.wav",voice}};Integrity(bad,"audio/fixture.wav",voice);bad["file_integrity"][0]["sha256"]=new string('0',64);Reject(Pack(root,"checksum.zip",bad,files),"checksum mismatch rejected");
                bad=Manifest("bad-reference","1.0.0");bad["intros"]=new JArray("intros.json");var wrong=(JObject)intro.DeepClone();wrong["intros"][0]["station_id"]="missing-station";files=new Dictionary<string,byte[]>{{"intros.json",Bytes(wrong)},{"audio/fixture.wav",voice}};Integrity(bad,"audio/fixture.wav",voice);Reject(Pack(root,"reference.zip",bad,files),"unknown station references rejected");
                files.Remove("audio/fixture.wav");Reject(Pack(root,"missing.zip",bad,files),"missing referenced audio rejected");
                bad=Manifest("bad-ids","1.0.0");bad["song_catalogues"]=new JArray("songs.json");files=new Dictionary<string,byte[]>{{"songs.json",Bytes(new JObject{{"songs",new JArray(new JObject{{"id","../../bad"}})}})}};Reject(Pack(root,"ids.zip",bad,files),"invalid metadata ID rejected");
                var independent=(JObject)updated.DeepClone();independent["entries"]=new JArray(new JObject{{"song_id",first},{"weight",1},{"enabled",true}},new JObject{{"song_id","fixture-new-song"},{"weight",2},{"enabled",true}});
                var independentManifest=Manifest("fixture-curation-update","1.0.0");independentManifest["playlists"]=new JArray("playlists/fixture.json");independentManifest["supersedes"]=new JArray(new JObject{{"pack_id","fixture-pack"},{"pack_version","0.0.0"}});
                files=new Dictionary<string,byte[]>{{"playlists/fixture.json",Bytes(independent)}};Reject(Pack(root,"stale-owner.zip",independentManifest,files),"independent update requires exact installed owner/version");
                independentManifest["supersedes"][0]["pack_version"]="1.2.0";c=library.Import(Pack(root,"independent.zip",independentManifest,files));
                Check(((JArray)c.playlists["fixture-radio"]["entries"]).Count==2&&c.songs.Count==7,"independent curation pack can explicitly supersede a playlist without duplicating songs");
                var held=(JObject)c.intros.Values.First().DeepClone();held["_approved"]=false;Check(!RadioPlan.Eligible(held),"unreviewed future clips held");held["_approved"]=true;held["enabled"]=false;Check(!RadioPlan.Eligible(held),"disabled clips remain held despite approval");
                string media=library.MediaPath(c.songs.Values.First());byte[] originalBytes=File.ReadAllBytes(media);File.WriteAllBytes(media,new byte[]{0,1,2});Check(!library.MediaValid(c.songs.Values.First()),"corrupt media detected before decoder");File.WriteAllBytes(media,originalBytes);
                File.WriteAllText(Path.Combine(library.Root,"current.json"),"broken");Check(library.Load().stations.Count==2,"catalog pointer corruption falls back to previous atomic revision");
                // The original fixture's transactional ownership cases above
                // remain independent of additional shipped station packs.
                var shipped=new RadioLibrary(Path.Combine(root,"Shipped"));
                foreach(var delivered in Directory.GetFiles(folder,"*.euradio").OrderBy(p=>p,StringComparer.Ordinal))shipped.Import(delivered,approved);
                var shippedCatalogue=shipped.Load();
                Check(shippedCatalogue.ActiveStations.Count()==2&&shippedCatalogue.songs.Count==12,"complete delivered station catalogue");
                Check(Directory.GetFiles(Path.Combine(shipped.Root,"Media")).Length==46,"complete delivered media deduplicated");
                Check(shippedCatalogue.intros.Values.All(RadioPlan.Eligible)&&shippedCatalogue.jingles.Values.All(RadioPlan.Eligible),"all delivered speech has exact approval");
                Debug.Log("RADIO_PACK_POLICY_PASS "+checks+" checks; shipped2stations/12songs");
                string output=Environment.GetEnvironmentVariable("EARTH_UNSCRIPTED_RADIO_POLICY_OUTPUT");if(!string.IsNullOrEmpty(output))File.WriteAllText(output,new JObject{{"passed",true},{"checks",checks},{"songs",6},{"stations",1},{"intros",12},{"jingles",4}}.ToString());
            }
            finally{Directory.Delete(root,true);}
        }
        static byte[] Bytes(JObject row)=>Encoding.UTF8.GetBytes(row.ToString());
        static JObject Manifest(string id,string version)=>new JObject{{"format","EarthUnscriptedRadioPack"},{"format_version",1},{"pack_id",id},{"pack_version",version},{"file_integrity",new JArray()}};
        static void Integrity(JObject manifest,string path,byte[] bytes)=>((JArray)manifest["file_integrity"]).Add(new JObject{{"path",path},{"sha256",RadioLibrary.Digest(bytes)},{"bytes",bytes.Length}});
        static string Pack(string root,string name,JObject manifest,Dictionary<string,byte[]> files)
        {string path=Path.Combine(root,name);using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){var all=new Dictionary<string,byte[]>(files){{"manifest.json",Bytes(manifest)}};foreach(var pair in all){var entry=zip.CreateEntry(pair.Key);using(var f=entry.Open())f.Write(pair.Value,0,pair.Value.Length);}}return path;}
        static byte[] Wave(int seed)
        {using(var m=new MemoryStream())using(var w=new BinaryWriter(m)){int count=800;w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(8000);w.Write(16000);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(count*2);for(int i=0;i<count;i++)w.Write((short)(Math.Sin(i*seed*.01)*1000));return m.ToArray();}}
    }
}
