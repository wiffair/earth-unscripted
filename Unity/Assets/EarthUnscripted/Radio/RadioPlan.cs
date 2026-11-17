using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted.Radio
{
    public sealed class RadioSegment
    {
        public string type,station,song,id;public JObject record;public float startFraction;
    }
    public static class RadioPlan
    {
        public static bool Eligible(JObject row)=>(bool?)row["enabled"]!=false&&(bool?)row["_approved"]==true;
        public static JObject Song(RadioCatalog catalog,string station,IList<string> recent,Random random,ISet<string> failed=null)
        {
            if(!catalog.playlists.TryGetValue(station,out var playlist)||(bool?)playlist["enabled"]==false)return null;
            var entries=((JArray)playlist["entries"]).Where(e=>(bool?)e["enabled"]!=false&&(double?)e["weight"]>0&&catalog.songs.TryGetValue((string)e["song_id"],out var song)&&(bool?)song["enabled"]!=false&&!(failed?.Contains((string)song["_media"])??false)).ToArray();
            if(entries.Length==0)return null;
            int avoid=Math.Min(Math.Max(0,(int?)playlist["rules"]?["avoid_last_n_songs"]??3),entries.Length-1);
            JToken[] choices;
            do{var blocked=new HashSet<string>(recent.Skip(Math.Max(0,recent.Count-avoid)));choices=entries.Where(e=>!blocked.Contains((string)e["song_id"])).ToArray();if(choices.Length>0)break;avoid--;}while(avoid>=0);
            if((bool?)playlist["rules"]?["shuffle"]==false)
            {
                string last=recent.LastOrDefault();int at=Array.FindIndex(entries,e=>(string)e["song_id"]==last);
                for(int i=1;i<=entries.Length;i++){var next=entries[(at+i+entries.Length)%entries.Length];if(choices.Contains(next))return catalog.songs[(string)next["song_id"]];}
            }
            double value=random.NextDouble()*choices.Sum(e=>(double)e["weight"]);
            foreach(var entry in choices){value-=(double)entry["weight"];if(value<=0)return catalog.songs[(string)entry["song_id"]];}
            return catalog.songs[(string)choices.Last()["song_id"]];
        }
        // Broadcast links are chance-based, rather than an every-N-songs counter.
        // Keep Sequence/Tune below for deterministic pack-policy fixtures.
        public static List<RadioSegment> Broadcast(RadioCatalog catalog,string station,JObject song,Random random,ISet<string> failed=null)
        {
            var result=new List<RadioSegment>();string songId=(string)song["id"];
            void Pick(string type,IEnumerable<JObject> records)
            {
                var choices=records.Where(r=>Eligible(r)&&!(failed?.Contains((string)r["_media"])??false)).ToArray();
                if(choices.Length>0){var row=choices[random.Next(choices.Length)];result.Add(new RadioSegment{type=type,station=station,song=songId,id=(string)row["id"],record=row});}
            }
            if(random.NextDouble()<.32)Pick("jingle",catalog.jingles.Values.Where(r=>(string)r["station_id"]==station));
            else if(random.NextDouble()<.18)Pick("drop",catalog.drops.Values.Where(r=>(string)r["station_id"]==station));
            if(random.NextDouble()<.65)Pick("intro",catalog.intros.Values.Where(r=>(string)r["station_id"]==station&&(string)r["song_id"]==songId));
            result.Add(new RadioSegment{type="song",station=station,song=songId,id=songId,record=song});return result;
        }
        public static List<RadioSegment> JoinBroadcast(RadioCatalog catalog,string station,JObject song,Random random,ISet<string> failed=null)
        {
            var sequence=Broadcast(catalog,station,song,random,failed);
            double Duration(RadioSegment s){double value=(double?)s.record["duration_seconds"]??(s.type=="song"?180:8);return double.IsNaN(value)||double.IsInfinity(value)?8:Math.Max(.2,value);}
            double point=random.NextDouble()*sequence.Sum(Duration);
            while(sequence.Count>1&&point>=Duration(sequence[0])){point-=Duration(sequence[0]);sequence.RemoveAt(0);}
            sequence[0].startFraction=(float)Math.Min(.995,point/Duration(sequence[0]));return sequence;
        }
        public static List<RadioSegment> Tune(RadioCatalog catalog,string station,JObject song,Random random,float introChance,int jingleEvery,ISet<string> failed=null)
        {
            int count=jingleEvery>0&&random.Next(jingleEvery)==0?jingleEvery:0;
            var sequence=Sequence(catalog,station,song,random,introChance,count,jingleEvery,failed);
            double Duration(RadioSegment s){double d=(double?)s.record["duration_seconds"]??(s.type=="song"?180:8);return double.IsNaN(d)||double.IsInfinity(d)?8:Math.Max(.2,d);}
            double point=random.NextDouble()*sequence.Sum(Duration);
            while(sequence.Count>1&&point>=Duration(sequence[0])){point-=Duration(sequence[0]);sequence.RemoveAt(0);}
            sequence[0].startFraction=(float)Math.Min(.995,point/Duration(sequence[0]));return sequence;
        }
        public static List<RadioSegment> Sequence(RadioCatalog catalog,string station,JObject song,Random random,float introChance,int completed,int jingleEvery,ISet<string> failed=null)
        {
            var result=new List<RadioSegment>();string songId=(string)song["id"];
            JObject Pick(IEnumerable<JObject> records){var a=records.Where(r=>Eligible(r)&&!(failed?.Contains((string)r["_media"])??false)).ToArray();return a.Length==0?null:a[random.Next(a.Length)];}
            void Add(string type,JObject row){if(row!=null)result.Add(new RadioSegment{type=type,station=station,song=songId,id=(string)row["id"],record=row});}
            if(jingleEvery>0&&completed>0&&completed%jingleEvery==0)Add("jingle",Pick(catalog.jingles.Values.Where(r=>(string)r["station_id"]==station)));
            // Generic station-only drops remain independent of per-song speech.
            if(completed>0&&completed%5==0)Add("drop",Pick(catalog.drops.Values.Where(r=>(string)r["station_id"]==station)));
            if(random.NextDouble()<introChance)Add("intro",Pick(catalog.intros.Values.Where(r=>(string)r["station_id"]==station&&(string)r["song_id"]==songId)));
            Add("song",song);return result;
        }
    }
}
