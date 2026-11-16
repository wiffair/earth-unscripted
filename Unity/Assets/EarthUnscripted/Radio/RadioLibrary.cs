using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EarthUnscripted.Radio
{
    // Plain-data importer: no Unity objects, scene changes or executable pack content.
    public sealed class RadioCatalog
    {
        public int schema = 1;
        public Dictionary<string,JObject> songs=new Dictionary<string,JObject>(), stations=new Dictionary<string,JObject>(),
            playlists=new Dictionary<string,JObject>(), intros=new Dictionary<string,JObject>(), jingles=new Dictionary<string,JObject>(),
            presenters=new Dictionary<string,JObject>(), drops=new Dictionary<string,JObject>(), packs=new Dictionary<string,JObject>();
        public IEnumerable<JObject> ActiveStations => stations.Values.Where(s=>(string)s["status"]=="active" &&
            playlists.TryGetValue((string)s["id"],out var p) && (bool?)p["enabled"]!=false &&
            ((JArray)p["entries"]).Any(e=>(bool?)e["enabled"]!=false && (double?)e["weight"]>0 && songs.ContainsKey((string)e["song_id"]))).OrderBy(s=>(string)s["name"]);
    }
    public sealed class RadioLibrary
    {
        public readonly string Root;
        static readonly object gate=new object();
        static readonly Regex Identifier=new Regex("^[a-z0-9][a-z0-9._-]{0,95}$",RegexOptions.CultureInvariant);
        const long MaxArchive=512L*1024*1024,MaxMedia=64L*1024*1024;
        public RadioLibrary(string root){Root=Path.GetFullPath(root);}
        public static string Digest(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static string DigestFile(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        public static string SafePath(string path)
        {
            if(string.IsNullOrEmpty(path)||path.Length>240||path.IndexOf('\\')>=0||path.IndexOf(':')>=0||Path.IsPathRooted(path)||path.Any(char.IsControl)||path.Split('/').Any(p=>p==".."||p=="."||p.Length==0))
                throw new InvalidDataException("Unsafe pack path: "+path);
            return path;
        }
        static string Id(JToken value,string context)
        {string id=(string)value;if(id==null||!Identifier.IsMatch(id))throw new InvalidDataException(context+": invalid ID");return id;}
        static JObject Json(byte[] bytes,string context)
        {
            if(bytes.Length>2*1024*1024)throw new InvalidDataException(context+": metadata exceeds2MiB");
            try{return JObject.Parse(Encoding.UTF8.GetString(bytes),new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});}
            catch(JsonException e){throw new InvalidDataException(context+": "+e.Message,e);}
        }
        public RadioCatalog Load()
        {
            if(!Directory.Exists(Root))return new RadioCatalog();
            Exception failure=null;
            foreach(string name in new[]{"current.json","previous.json"})
            {
                string pointer=Path.Combine(Root,name);if(!File.Exists(pointer))continue;
                try
                {
                    var row=JObject.Parse(File.ReadAllText(pointer));string file=SafePath((string)row["file"]);
                    if(file.Contains("/"))throw new InvalidDataException("Invalid catalog pointer");
                    byte[] bytes=File.ReadAllBytes(Path.Combine(Root,file));
                    if(Digest(bytes)!=(string)row["sha256"])throw new InvalidDataException("Radio catalog checksum mismatch");
                    var catalog=JsonConvert.DeserializeObject<RadioCatalog>(Encoding.UTF8.GetString(bytes));
                    if(catalog==null||catalog.schema!=1)throw new InvalidDataException("Unsupported radio library schema");
                    return catalog;
                }
                catch(Exception e) when(e is IOException||e is JsonException||e is ArgumentException){failure=e;}
            }
            if(failure!=null)throw new InvalidDataException("Radio library could not be read; previous catalog also unavailable",failure);
            return new RadioCatalog();
        }
        public string MediaPath(JObject record)
        {string key=(string)record["_media"];if(key==null||!Regex.IsMatch(key,"^[a-f0-9]{64}\\.(mp3|wav|ogg)$"))throw new InvalidDataException("Invalid stored radio media reference");return Path.Combine(Root,"Media",key);}
        public bool MediaValid(JObject record)
        {try{string p=MediaPath(record);return File.Exists(p)&&DigestFile(p)==Path.GetFileNameWithoutExtension(p);}catch(IOException){return false;}}
        static string Format(byte[] b,string path)
        {
            string ext=Path.GetExtension(path).ToLowerInvariant();
            bool valid=ext==".wav"?b.Length>=44&&Encoding.ASCII.GetString(b,0,4)=="RIFF"&&Encoding.ASCII.GetString(b,8,4)=="WAVE":
                ext==".ogg"?b.Length>=4&&Encoding.ASCII.GetString(b,0,4)=="OggS":
                ext==".mp3"&&b.Length>=3&&((b[0]=='I'&&b[1]=='D'&&b[2]=='3')||(b[0]==255&&(b[1]&224)==224));
            if(!valid)throw new InvalidDataException(path+": unsupported/corrupt audio header");return ext;
        }
        static JObject Public(JObject row){var copy=(JObject)row.DeepClone();foreach(var p in copy.Properties().Where(p=>p.Name.StartsWith("_",StringComparison.Ordinal)).ToArray())p.Remove();return copy;}
        public RadioCatalog Import(string archive,ISet<string> approvedHashes=null)
        {
            lock(gate)
            {
                var result=Load();string stage=Path.Combine(Root,".stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
                var newlyAdded=new List<string>();string catalogFile=null;bool committed=false;
                try
                {
                    using(var zip=ZipFile.OpenRead(archive))
                    {
                        if(zip.Entries.Count>4096||zip.Entries.Sum(e=>e.Length)>MaxArchive)throw new InvalidDataException("Radio pack exceeds512MiB/4096-entry budget");
                        var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
                        foreach(var entry in zip.Entries)
                        {
                            string path=entry.FullName.TrimEnd('/');SafePath(path);
                            if(((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException(path+": symbolic links are not allowed");
                            if(entry.FullName.EndsWith("/",StringComparison.Ordinal))continue;
                            if(entries.ContainsKey(path))throw new InvalidDataException(path+": duplicate archive entry");entries.Add(path,entry);
                        }
                        byte[] Read(string path,long limit)
                        {
                            SafePath(path);if(!entries.TryGetValue(path,out var entry))throw new InvalidDataException(path+": missing file");
                            if(entry.Length>limit)throw new InvalidDataException(path+": size limit exceeded");
                            using(var input=entry.Open())using(var output=new MemoryStream()){var buffer=new byte[81920];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit||output.Length+count>entry.Length)throw new InvalidDataException(path+": expanded size limit exceeded");output.Write(buffer,0,count);}if(output.Length!=entry.Length)throw new InvalidDataException(path+": invalid expanded size");return output.ToArray();}
                        }
                        byte[] manifestBytes=Read("manifest.json",2*1024*1024);var manifest=Json(manifestBytes,"manifest.json");
                        if((string)manifest["format"]!="EarthUnscriptedRadioPack"||(int?)manifest["format_version"]!=1)throw new InvalidDataException("manifest.json: unsupported radio pack format/version");
                        string archiveHash=DigestFile(archive);
                        string pack=Id(manifest["pack_id"],"manifest.pack_id"),version=(string)manifest["pack_version"],manifestHash=Digest(manifestBytes);
                        if(!Regex.IsMatch(version??"",@"^\d+\.\d+\.\d+$")||!Version.TryParse(version,out var revision)||revision.Build<0)throw new InvalidDataException("manifest.pack_version: use major.minor.patch");
                        if(result.packs.TryGetValue(pack,out var previous))
                        {
                            int order=revision.CompareTo(Version.Parse((string)previous["version"]));
                            if(order<0)throw new InvalidDataException("Older radio pack revision rejected");
                            if(order==0){if((string)previous["manifest"]!=manifestHash||(string)previous["archive"]!=archiveHash)throw new InvalidDataException("Conflicting content for installed pack version");return result;}
                        }
                        var integrity=new Dictionary<string,JObject>(StringComparer.Ordinal);
                        foreach(var token in manifest["file_integrity"] as JArray??throw new InvalidDataException("manifest.file_integrity: array required"))
                        {
                            var row=token as JObject??throw new InvalidDataException("manifest.file_integrity: object required");string path=SafePath((string)row["path"]);
                            if(integrity.ContainsKey(path)||!Regex.IsMatch((string)row["sha256"]??"","^[a-f0-9]{64}$"))throw new InvalidDataException(path+": duplicate/invalid checksum");
                            byte[] bytes=Read(path,MaxMedia);if(bytes.LongLength!=(long?)row["bytes"]||Digest(bytes)!=(string)row["sha256"])throw new InvalidDataException(path+": checksum/size mismatch");integrity[path]=row;
                        }
                        var permittedOwners=new HashSet<string>();
                        foreach(var declared in manifest["supersedes"] as JArray??new JArray())
                        {string owner=Id(declared["pack_id"],"manifest.supersedes.pack_id");if(!result.packs.TryGetValue(owner,out var receipt)||(string)receipt["version"]!=(string)declared["pack_version"])throw new InvalidDataException("manifest.supersedes: installed owner/version does not match: "+owner);permittedOwners.Add(owner);}
                        var imported=new Dictionary<string,HashSet<string>>();
                        void Records(string field,string array,Dictionary<string,JObject> target,bool singular=false)
                        {
                            var listed=manifest[field] as JArray;if(listed==null)return;var seen=new HashSet<string>();imported[field]=seen;
                            foreach(var fileToken in listed)
                            {
                                string file=SafePath((string)fileToken);var document=Json(Read(file,2*1024*1024),file);
                                if(document["schema_version"]!=null&&(int?)document["schema_version"]!=1)throw new InvalidDataException(file+": unsupported schema_version");
                                IEnumerable<JToken> records=singular?new JToken[]{document}:document[array] as JArray??throw new InvalidDataException(file+".$."+array+": array required");
                                foreach(var token in records)
                                {
                                    var row=token as JObject??throw new InvalidDataException(file+": object required");string id=Id(row[singular&&field=="playlists"?"station_id":"id"],file);
                                    if(!seen.Add(id))throw new InvalidDataException(file+": duplicate ID "+id);
                                    if(row.Properties().Any(p=>p.Name.StartsWith("_",StringComparison.Ordinal)))throw new InvalidDataException(file+": reserved internal metadata field");
                                    var copy=(JObject)row.DeepClone();
                                    target.TryGetValue(id,out var old);string owner=pack;
                                    if(old!=null&&(string)old["_pack"]!=pack)
                                    {
                                        bool identical=JToken.DeepEquals(Public(old),Public(copy));string previousOwner=(string)old["_pack"];
                                        if(!identical&&!permittedOwners.Contains(previousOwner))throw new InvalidDataException(file+": conflicting ID owned by another pack: "+id+"; explicit supersedes owner/version required");
                                        if(identical)owner=previousOwner;
                                    }
                                    copy["_pack"]=owner;copy["_file"]=file;
                                    string audio=(string)(copy["audio"]??copy["file"]);
                                    if(audio!=null)
                                    {
                                        SafePath(audio);if(!integrity.TryGetValue(audio,out var verified))throw new InvalidDataException(file+": audio missing from file_integrity: "+audio);
                                        byte[] bytes=Read(audio,MaxMedia);string hash=(string)verified["sha256"],key=hash+Format(bytes,audio);copy["_media"]=key;
                                        copy["_approved"]=(approvedHashes?.Contains(hash)??false)||((bool?)old?["_approved"]==true&&(string)old["_media"]==key)||(string)copy["review_status"]=="approved";
                                        string staged=Path.Combine(stage,key);if(!File.Exists(staged))File.WriteAllBytes(staged,bytes);
                                    }
                                    target[id]=copy;
                                }
                            }
                        }
                        Records("song_catalogues","songs",result.songs);Records("station_definitions",null,result.stations,true);Records("playlists",null,result.playlists,true);
                        Records("presenters","presenters",result.presenters);Records("intros","intros",result.intros);Records("jingles","jingles",result.jingles);Records("drops","drops",result.drops);
                        Validate(result);
                        Directory.CreateDirectory(Path.Combine(Root,"Media"));
                        foreach(string file in Directory.GetFiles(stage))
                        {
                            string target=Path.Combine(Root,"Media",Path.GetFileName(file));
                            if(File.Exists(target)){if(DigestFile(target)!=Path.GetFileNameWithoutExtension(file))throw new InvalidDataException("Existing content-addressed audio is corrupt: "+Path.GetFileName(file));}
                            else{File.Move(file,target);newlyAdded.Add(target);}
                        }
                        result.packs[pack]=new JObject{{"version",version},{"manifest",manifestHash},{"archive",archiveHash},{"imported_utc",DateTime.UtcNow.ToString("O")}};
                        catalogFile=Path.Combine(Root,"catalog-"+Guid.NewGuid().ToString("N")+".json");byte[] catalog=Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));File.WriteAllBytes(catalogFile,catalog);
                        string pointer=Path.Combine(Root,"current.json"),pending=Path.Combine(stage,"pointer.json");
                        File.WriteAllText(pending,new JObject{{"file",Path.GetFileName(catalogFile)},{"sha256",Digest(catalog)}}.ToString(Formatting.None));
                        if(File.Exists(pointer))File.Replace(pending,pointer,Path.Combine(Root,"previous.json"));else File.Move(pending,pointer);
                        committed=true;return result;
                    }
                }
                finally
                {
                    if(!committed){foreach(string p in newlyAdded)File.Delete(p);if(catalogFile!=null)File.Delete(catalogFile);}
                    if(Directory.Exists(stage))Directory.Delete(stage,true);
                }
            }
        }
        public static void Validate(RadioCatalog c)
        {
            void Boolean(JObject row,string key,string context){if(row[key]!=null&&row[key].Type!=JTokenType.Boolean)throw new InvalidDataException(context+"."+key+": boolean required");}
            foreach(var table in new[]{c.songs,c.stations,c.playlists,c.intros,c.jingles,c.drops})foreach(var pair in table)Boolean(pair.Value,"enabled",pair.Key);
            foreach(var pair in c.songs)if(string.IsNullOrWhiteSpace((string)pair.Value["title"])||string.IsNullOrWhiteSpace((string)pair.Value["artist"])||pair.Value["_media"]==null)throw new InvalidDataException("songs."+pair.Key+": title, artist and audio required");
            foreach(var pair in c.stations)
            {
                var s=pair.Value;double frequency=(double?)s["frequency_mhz"]??0;
                if(string.IsNullOrWhiteSpace((string)s["name"])||double.IsNaN(frequency)||double.IsInfinity(frequency)||frequency<=0)throw new InvalidDataException("stations."+pair.Key+": name/frequency required");
                if(!c.playlists.TryGetValue(pair.Key,out var playlist)||SafePath((string)s["playlist"])!=(string)playlist["_file"])throw new InvalidDataException("stations."+pair.Key+": missing/mismatched playlist");
                string presenter=(string)s["presenter_id"];if(presenter!=null&&(!c.presenters.TryGetValue(presenter,out var p)||(string)p["station_id"]!=pair.Key))throw new InvalidDataException("stations."+pair.Key+": invalid presenter");
            }
            foreach(var pair in c.playlists)
            {
                if(!c.stations.ContainsKey(pair.Key))throw new InvalidDataException("playlists."+pair.Key+": unknown station");var seen=new HashSet<string>();
                if(pair.Value["rules"] is JObject rules){Boolean(rules,"shuffle",pair.Key+".rules");if(rules["avoid_last_n_songs"]!=null&&(rules["avoid_last_n_songs"].Type!=JTokenType.Integer||(int)rules["avoid_last_n_songs"]<0||(int)rules["avoid_last_n_songs"]>50))throw new InvalidDataException(pair.Key+".rules.avoid_last_n_songs: integer0–50 required");}
                foreach(var row in pair.Value["entries"] as JArray??throw new InvalidDataException("playlists."+pair.Key+": entries required"))
                {if(!(row is JObject entry))throw new InvalidDataException(pair.Key+": playlist entry must be an object");Boolean(entry,"enabled",pair.Key+".entries");string id=Id(row["song_id"],"playlist.song_id");double weight=(double?)row["weight"]??1;if(!seen.Add(id)||!c.songs.ContainsKey(id)||double.IsNaN(weight)||double.IsInfinity(weight)||weight<0||weight>1000000)throw new InvalidDataException("playlists."+pair.Key+": duplicate/missing song or invalid weight: "+id);if(row["weight"]==null)row["weight"]=1;}
            }
            foreach(var pair in c.presenters)if(!c.stations.ContainsKey((string)pair.Value["station_id"]??""))throw new InvalidDataException("presenters."+pair.Key+": unknown station");
            foreach(var table in new[]{c.intros,c.jingles,c.drops})foreach(var pair in table)
            {
                var r=pair.Value;string station=Id(r["station_id"],pair.Key);
                if(!c.stations.ContainsKey(station)||r["_media"]==null)throw new InvalidDataException(pair.Key+": unknown station or missing audio");
                if(table==c.intros&&(!c.songs.ContainsKey((string)r["song_id"]??"")||!c.presenters.TryGetValue((string)r["presenter_id"]??"",out var p)||(string)p["station_id"]!=station))throw new InvalidDataException(pair.Key+": invalid composite song/station/presenter reference");
            }
        }
    }
}
