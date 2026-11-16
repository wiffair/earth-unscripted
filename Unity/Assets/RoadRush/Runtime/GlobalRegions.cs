using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using RoadRush.Core;
namespace RoadRush
{
    [Serializable] public sealed class WorldPlace {public string name,country,country_code,timezone;public double latitude,longitude;public int drivingSide;public int size=3000;}
    [Serializable] sealed class PlaceSearch {public WorldPlace[] results;}
    [Serializable] sealed class ElevationResult {public float[] elevation;}
    public static class RegionCache
    {
        public static string OverrideFolder;public static string Folder=>OverrideFolder??(string.IsNullOrEmpty(PlayerProfile.StorageOverride)?Path.Combine(RoadRush.SinglePlayerBeta.ProductRoot,"Regions"):PlayerProfile.StorageOverride+".regions");
        public static bool Safe(string id)=>id!=null&&Regex.IsMatch(id,@"^download-[a-zA-Z0-9_-]{1,80}$");
        public static string PeerFolder=>Path.Combine(Folder,"PeerSnapshots-v1");
        public static string PathFor(string id)=>Safe(id)?Path.Combine(id.StartsWith("download-peer-",StringComparison.Ordinal)?PeerFolder:Folder,id+".json"):throw new ArgumentException("Invalid region identifier");
        public static string SourcePath(string id)=>Path.Combine(Path.GetDirectoryName(PathFor(id)),id+"-osm-source.txt");
        public static bool Exists(string id)=>Safe(id)&&File.Exists(PathFor(id));
        public static string[] All()=>new[]{Folder,PeerFolder}.Where(Directory.Exists).SelectMany(folder=>Directory.GetFiles(folder,"download-*.json")).Select(Path.GetFileNameWithoutExtension).Where(Safe).Distinct().OrderBy(x=>x).ToArray();
        static string CacheHeader(TextReader source){var buffer=new char[8192];int count=source.ReadBlock(buffer,0,buffer.Length);return new string(buffer,0,count);}
        public static string Label(string id)
        {
            try
            {
                using(var source=File.OpenText(PathFor(id)))using(var reader=new JsonTextReader(new StringReader(CacheHeader(source))){MaxDepth=16})
                {
                    int tokens=0;while(tokens++<30&&reader.Read())if(reader.TokenType==JsonToken.PropertyName&&(string)reader.Value=="name")
                    {reader.Read();string name=reader.Value as string;if(!string.IsNullOrWhiteSpace(name)){if(name.Length>18){int n=char.IsHighSurrogate(name[17])?17:18;name=name.Substring(0,n)+"…";}return name;}break;}
                }
            }
            catch{}return "Saved area";
        }
        static string Digest(string text,int bytes)
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)),0,bytes).Replace("-","").ToLowerInvariant();
        }
        public static string Id(WorldPlace p)
        {
            int size=Mathf.Clamp(p.size<=0?3000:p.size,1000,6000);
            string identity=p.latitude.ToString("R",CultureInfo.InvariantCulture)+"|"+p.longitude.ToString("R",CultureInfo.InvariantCulture)+"|"+size+"|"+(p.country_code??"").ToUpperInvariant()+"|"+p.timezone+"|"+p.drivingSide;
            return "download-v4-"+Digest(identity,12)+"-"+size;
        }
        public static string PackId(WorldPlace place,CountryPack pack)=>Id(place)+"-c"+Digest(pack.Id+"|"+(string)pack.Metadata["replication"]?["osmosis_replication_base_url"]+"|"+pack.RailwayIdentity,8)+"-p"+pack.Version;

        public static void Save(GeoRegion region,string osm)
        {
            Directory.CreateDirectory(Folder);
            string path=PathFor(region.id),temp=path+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(temp,JsonUtility.ToJson(region));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
            File.WriteAllText(SourcePath(region.id),osm);
            RegionCacheMaintenance.Trim(region.id);
            File.WriteAllText(Path.Combine(Folder,"DATA_NOTICES.txt"),"Map data © OpenStreetMap contributors, ODbL: https://www.openstreetmap.org/copyright\nElevation: Copernicus DEM GLO-90 via Open-Meteo, sampled from source DEM into a local game grid. Modified for gameplay. GeoNames place names via Open-Meteo. See the game's DATA_SOURCES.md for full notices.\n");
        }
    }
    public sealed class GlobalRegions : MonoBehaviour
    {
        public WorldPlace[] Results=Array.Empty<WorldPlace>();public GeoRegion Ready;public GeoRegion MapPreview;public string Status="Search for a town or enter coordinates. Downloads are optional and cached.";public bool Busy {get;private set;}
        UnityWebRequest active;System.Diagnostics.Process systemRequest;readonly List<string> temporaryFiles=new List<string>();static float lastDownload=-1000,lastSearch=-1000;
        public string OverpassEndpoint="https://overpass.atownsend.org.uk/api/interpreter";
        void Awake(){try{string path=Path.Combine(RegionCache.Folder,"provider.txt");if(File.Exists(path)){string url=File.ReadAllText(path).Trim();if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https")OverpassEndpoint=url;}}catch{}}
        public void Search(string name){if(!Busy&&name.Trim().Length>=2&&Time.unscaledTime-lastSearch>2)StartCoroutine(Find(name.Trim()));}
        IEnumerator Find(string name)
        {
            Busy=true;lastSearch=Time.unscaledTime;Status="Searching places…";string data=null,error=null;
            yield return Get("https://geocoding-api.open-meteo.com/v1/search?name="+UnityWebRequest.EscapeURL(name)+"&count=8&language=en&format=json",null,250000,(s,e)=>{data=s;error=e;});
            if(error==null)try{Results=JsonUtility.FromJson<PlaceSearch>(data)?.results??Array.Empty<WorldPlace>();Status=Results.Length>0?"Choose a place below.":"No place found. Try a nearby town or coordinates.";}catch{Status="Could not read the location response.";}
            else Status="Place search is unavailable. Please try again, or choose a saved area.";Busy=false;
        }
        public void Download(WorldPlace place)
        {
            if(Busy)return;Ready=null;if(place==null||double.IsNaN(place.latitude)||double.IsNaN(place.longitude)||Math.Abs(place.latitude)>80||Math.Abs(place.longitude)>179.98){Status="Use a latitude between -80 and 80 and longitude between -179.98 and179.98.";return;}
            var installed=CountryPacks.Find(place);if(installed!=null){PrepareOffline(place,installed);return;}
            string id=RegionCache.Id(place);if(RegionCache.Exists(id)){try{Ready=GeoRegion.Load(id);Status="Cached area ready · choose a start point.";}catch(Exception e){Status=e.Message;}return;}
            if(Time.unscaledTime-lastDownload<60){Status="Please wait a minute between new area downloads.";return;}StartCoroutine(Fetch(place));
        }
        public void PrepareOffline(WorldPlace place,CountryPack pack){if(!Busy&&pack.Covers(place))StartCoroutine(FromPack(place,pack));}
        IEnumerator FromPack(WorldPlace place,CountryPack pack)
        {
            Busy=true;Status="Checking cached start-area data…";yield return null;
            if(CountryUpdateService.Instance&&CountryUpdateService.Instance.Enabled)
            {
                yield return CountryUpdateService.Instance.BeforePrepare(pack);
                try{pack=new CountryPack(pack.Folder);}catch(Exception e){Status="Using the previous cache · "+e.Message;}
            }
            Status="Preparing "+place.name+" from offline "+pack.Name+" pack…";
            var details=GetComponent<GameSession>()?.Profile.detailedScenery==true?DetailBuildingPacks.Find(place):Array.Empty<DetailBuildingPack>();
            var canopy=GetComponent<GameSession>()?.Profile.detailedScenery==true?DetailCanopyPacks.Find(place):Array.Empty<DetailCanopyPack>();
            string id=DetailCanopyPacks.CacheId(DetailBuildingPacks.CacheId(RegionCache.PackId(place,pack),details),canopy);
            if(RegionCache.Exists(id)){try{Ready=GeoRegion.Load(id);Status="Offline "+pack.Name+" ready · version "+pack.Version;}catch(Exception e){Status=e.Message;}Busy=false;yield break;}
            var work=System.Threading.Tasks.Task.Run(()=>
            {
                var source=pack.Extract(place);var grid=pack.Grid(place,65);var result=Convert(place,source,grid,65,Mathf.Clamp(place.size<=0?3000:place.size,1000,6000));foreach(var detail in details)detail.ApplySafely(result);if(canopy.Length>0)DetailCanopyPacks.PrepareDrivingWidths(result);foreach(var trees in canopy)DetailCanopyPacks.ApplySafely(trees,result);return Tuple.Create(result,source);
            });
            while(!work.IsCompleted)yield return null;
            try
            {
                if(work.IsFaulted)throw work.Exception.InnerException;var region=work.Result.Item1;region.id=id;region.datasetVersion=pack.Name+"/"+pack.Version;region.datasetSource=(string)pack.Metadata["replication"]?["osmosis_replication_base_url"];RegionCache.Save(region,work.Result.Item2);Ready=GeoRegion.Load(id);
                Status="Offline "+pack.Name+" ready · version "+pack.Version+" · select a road start";
            }
            catch(Exception e){Status="Country pack could not prepare this area: "+e.Message;}Busy=false;
        }
        public static string RailwayQuery(string box)=>string.Join("",new[]{"railway","disused:railway","abandoned:railway","razed:railway","demolished:railway","construction:railway","proposed:railway"}.Select(key=>"way[\""+key+"\"]("+box+");"));
        public void SelectPlace(WorldPlace place)
        {
            if(Busy)return;Ready=null;MapPreview=null;
            if(place==null||double.IsNaN(place.latitude)||double.IsNaN(place.longitude)||Math.Abs(place.latitude)>80||Math.Abs(place.longitude)>179.98){Status="Choose valid coordinates before loading the map.";return;}
            StartCoroutine(PreviewPlace(place));
        }
        IEnumerator PreviewPlace(WorldPlace place)
        {
            Busy=true;Status="Loading the map for "+place.name+"…";
            string id=RegionCache.Id(place),raw=null,error=null;
            if(RegionCache.Exists(id))
            {
                try{Ready=GeoRegion.Load(id);MapPreview=Ready;Status="Saved area ready. Click a road, then Drive.";}
                catch{Status="The saved map could not be opened. Try preparing this area again.";}
                Busy=false;yield break;
            }
            yield return MapSource(place,(text,failure)=>{raw=text;error=failure;});
            if(error==null)
            {
                try{MapPreview=Preview(place,raw);Status="Map loaded. Prepare this area to add road heights and scenery.";}
                catch(Exception e){Debug.Log("Map preview could not be read: "+e);Status="The map could not be read. Try a smaller area or a nearby place.";}
            }
            else Status=error;
            Busy=false;
        }
        public static GeoRegion Preview(WorldPlace place,string source)
        {
            var elements=JObject.Parse(source)["elements"] as JArray;
            if(elements==null||elements.Count>30000)throw new IOException("Invalid preview response");
            float size=Mathf.Clamp(place.size<=0?3000:place.size,1000,6000);
            var roads=new List<GeoRoad>();var water=new List<GeoWater>();
            double cos=Math.Cos(place.latitude*Math.PI/180);
            foreach(var element in elements)
            {
                var tags=element["tags"];var geometry=element["geometry"] as JArray;
                if(tags==null||geometry==null||geometry.Count<2)continue;
                bool isRoad=tags["highway"]!=null,isWater=tags["waterway"]!=null;if(!isRoad&&!isWater)continue;
                var run=new List<Vector3>();
                void Flush(){if(run.Count>1){if(isRoad)roads.Add(new GeoRoad{points=run.ToArray(),tunnel=(string)tags["tunnel"]=="yes"});else water.Add(new GeoWater{points=run.ToArray()});}run.Clear();}
                foreach(var point in geometry)
                {
                    if(!(point is JObject)||point["lat"]==null||point["lon"]==null){Flush();continue;}
                    run.Add(new Vector3((float)(((double)point["lon"]-place.longitude)*111319.4908*cos),0,(float)(((double)point["lat"]-place.latitude)*111319.4908)));
                }
                Flush();
            }
            if(roads.Count==0)throw new IOException("No roads in preview");
            return new GeoRegion{id=RegionCache.Id(place),name=place.name,latitude=place.latitude,longitude=place.longitude,countryCode=place.country_code,drivingSide=place.drivingSide==0?CountryRules.Side(place.country_code):place.drivingSide,minX=-size/2,minZ=-size/2,width=size,depth=size,roads=roads.ToArray(),waters=water.ToArray()};
        }
        IEnumerator MapSource(WorldPlace place,Action<string,string> done)
        {
            string F(double value)=>value.ToString("F6",CultureInfo.InvariantCulture);
            double lat=place.latitude,lon=place.longitude,cos=Math.Cos(lat*Math.PI/180);float size=Mathf.Clamp(place.size<=0?3000:place.size,1000,6000);
            double south=lat-size*.5/111319.4908,north=lat+size*.5/111319.4908,west=lon-size*.5/(111319.4908*cos),east=lon+size*.5/(111319.4908*cos);
            string box=F(south)+","+F(west)+","+F(north)+","+F(east);
            string query="[out:json][timeout:40][maxsize:16000000];(way[highway~\"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|service|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link)$\"][access!~\"^(no|private)$\"][motor_vehicle!=no]("+box+");way[waterway~\"^(river|stream|canal)$\"]("+box+");way[building]("+box+");way[landuse]("+box+");way[natural=wood]("+box+");"+RailwayQuery(box)+");out body geom("+box+");";
            string path=RegionCache.SourcePath(RegionCache.Id(place)),raw=null,error=null;
            bool Valid(string text){try{var data=JObject.Parse(text);return (data["elements"] as JArray)?.Count>0&&string.IsNullOrEmpty((string)data["remark"]);}catch{return false;}}
            try{if(File.Exists(path)&&new FileInfo(path).Length<8000000)raw=File.ReadAllText(path);}catch{}
            if(Valid(raw)){done(raw,null);yield break;}
            // Try both existing public mirrors automatically. A failed selection
            // never publishes Ready, so it cannot retain a different drive start.
            string alternate=OverpassEndpoint.Contains("private.coffee")?"https://overpass-api.de/api/interpreter":"https://overpass.private.coffee/api/interpreter";
            int attempt=0;
            foreach(string endpoint in new[]{"https://overpass.atownsend.org.uk/api/interpreter",OverpassEndpoint,alternate}.Distinct())
            {
                Status=(attempt++==0?"Loading roads for ":"The first map server did not respond. Trying the backup for ")+place.name+"…";
                yield return Get(endpoint+"?data="+UnityWebRequest.EscapeURL(query),null,8000000,(text,failure)=>{raw=text;error=failure;},20);
                if(error!=null)Debug.Log("Map download retry: "+new Uri(endpoint).Host+" · "+error);
                if(error==null&&Valid(raw))
                {
                    try{Directory.CreateDirectory(RegionCache.Folder);File.WriteAllText(path,raw);}catch{}
                    done(raw,null);yield break;
                }
            }
            done(null,"Could not load the selected area from the map servers. Your current drive has not changed. Try again, choose 1 km, or open a saved area.");
        }
        IEnumerator Fetch(WorldPlace place)
        {
            Busy=true;lastDownload=Time.unscaledTime;Ready=null;string error=null,raw=null;
            const int n=17;float size=Mathf.Clamp(place.size<=0?3000:place.size,1000,6000);double lat=place.latitude,lon=place.longitude,cos=Math.Cos(lat*Math.PI/180);
            string F(double value)=>value.ToString("F6",CultureInfo.InvariantCulture);
            yield return MapSource(place,(text,failure)=>{raw=text;error=failure;});
            if(error!=null){Status=error;Busy=false;yield break;}
            var heights=new List<float>();
            for(int start=0;start<n*n;start+=100)
            {
                Status=$"Sampling Copernicus terrain · {start}/{n*n} points";var lats=new List<string>();var lons=new List<string>();
                for(int j=start;j<Math.Min(n*n,start+100);j++){int x=j%n,z=j/n;lats.Add(F(lat+(-size*.5+z*size/(n-1))/111319.4908));lons.Add(F(lon+(-size*.5+x*size/(n-1))/(111319.4908*cos)));}
                string text=null;yield return Get("https://api.open-meteo.com/v1/elevation?latitude="+string.Join(",",lats)+"&longitude="+string.Join(",",lons),null,100000,(s,e)=>{text=s;error=e;});if(error!=null){Status="Terrain heights could not be downloaded. Try preparing this area again; saved areas are still available.";Busy=false;yield break;}
                float[] values=null;try{values=JsonUtility.FromJson<ElevationResult>(text).elevation;}catch{}if(values==null||values.Length!=lats.Count||values.Any(v=>float.IsNaN(v)||float.IsInfinity(v)||v< -500||v>9000)){Status="Invalid terrain response · no map replaced";Busy=false;yield break;}heights.AddRange(values);yield return new WaitForSecondsRealtime(1);
            }
            Status="Building connected roads, terrain and scenery…";yield return null;
            try{var region=Convert(place,raw,heights.ToArray(),n,size);RegionCache.Save(region,raw);Ready=GeoRegion.Load(region.id);Status=$"{region.roads.Length} road segments · cached offline · select a road and Drive";}catch(Exception e){Status="Area could not be prepared: "+e.Message;}
            Busy=false;
        }
        IEnumerator Get(string url,string post,int max,Action<string,string> done,int timeout=50)
        {
            using(var req=new UnityWebRequest(url,post==null?"GET":"POST"))
            {
                var handler=new LimitedDownload(max);req.downloadHandler=handler;req.timeout=timeout;req.SetRequestHeader("User-Agent","RoadRush-PersonalPrototype/0.8 (manual cached geographic import)");
                if(post!=null){req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("data="+UnityWebRequest.EscapeURL(post)));req.SetRequestHeader("Content-Type","application/x-www-form-urlencoded");}
                active=req;yield return req.SendWebRequest();
                if(req.result==UnityWebRequest.Result.ConnectionError&&req.responseCode==0)
                {Status="Retrying the secure download…";yield return SystemGet(url,post,max,done,timeout);}
                else done(req.result==UnityWebRequest.Result.Success?handler.Text:null,req.result==UnityWebRequest.Result.Success?null:"Download unavailable at "+new Uri(url).Host+" ("+req.responseCode+"). Retry later or use a cached map.");active=null;
            }
        }
        IEnumerator SystemGet(string url,string post,int max,Action<string,string> done,int timeout)
        {
            string output=Path.GetTempFileName(),form=Path.GetTempFileName();temporaryFiles.Add(output);temporaryFiles.Add(form);
            string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
            string program=Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"curl.exe"):"/usr/bin/curl";
            string args="--silent --show-error --fail --location --proto =https --proto-redir =https --connect-timeout 10 --max-time "+timeout+" --max-filesize "+max+" --user-agent RoadRush-PersonalPrototype/0.8 --output "+Quote(output);
            if(post!=null){File.WriteAllText(form,"data="+Uri.EscapeDataString(post));args+=" --data-binary "+Quote("@"+form);}
            args+=" "+Quote(new Uri(url).AbsoluteUri);string error=null;
            try{systemRequest=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true});}catch(Exception e){error=e.Message;}
            if(error==null)
            {
                float deadline=Time.realtimeSinceStartup+timeout+5;
                while(!systemRequest.HasExited)
                {
                    if(Time.realtimeSinceStartup>deadline||new FileInfo(output).Length>max){systemRequest.Kill();error="Request timeout or size limit";break;}
                    yield return null;
                }
                systemRequest.WaitForExit();if(systemRequest.ExitCode!=0&&error==null)error=systemRequest.StandardError.ReadToEnd();systemRequest.Dispose();systemRequest=null;
            }
            string text=null;try{if(error==null&&new FileInfo(output).Length<=max)text=File.ReadAllText(output);else error=error??"Response too large";}catch(Exception e){error=e.Message;}
            foreach(string path in new[]{output,form}){try{File.Delete(path);}catch{}temporaryFiles.Remove(path);}
            done(text,error==null?null:"System HTTPS download unavailable at "+new Uri(url).Host+" · "+error);
        }
        sealed class LimitedDownload : DownloadHandlerScript
        {
            readonly MemoryStream stream=new MemoryStream();readonly int maximum;public LimitedDownload(int max):base(new byte[32768]){maximum=max;}protected override bool ReceiveData(byte[] data,int length){if(stream.Length+length>maximum)return false;stream.Write(data,0,length);return true;}public string Text=>Encoding.UTF8.GetString(stream.ToArray());protected override byte[] GetData()=>stream.ToArray();
        }
        public static GeoRegion Convert(WorldPlace place,string source,float[] heights,int n,float size)
        {
            using var reader=new JsonTextReader(new StringReader(source)){MaxDepth=40};var json=JObject.Load(reader);var elements=(JArray)json["elements"];if(elements==null||elements.Count>30000)throw new Exception("Area is too dense; choose a smaller town.");
            float datum=Mathf.Floor(heights.Min())-10;var region=new GeoRegion{id=RegionCache.Id(place),name=place.name+" · real roads",latitude=place.latitude,longitude=place.longitude,countryCode=place.country_code??"",drivingSide=place.drivingSide!=0?place.drivingSide:CountryRules.Side(place.country_code),timeZone=string.IsNullOrEmpty(place.timezone)?"UTC":place.timezone,datum=datum,minX=-size*.5f,minZ=-size*.5f,width=size,depth=size,nx=n,nz=n,heights=heights.Select(h=>h-datum).ToArray(),landNx=65,landNz=65,landCover=new int[65*65],attribution="© OpenStreetMap contributors · Copernicus DEM GLO-90 / Open-Meteo · GeoNames"};
            double cos=Math.Cos(place.latitude*Math.PI/180);var roads=new List<GeoRoad>();var railways=new List<GeoRailway>();int railwayPoints=0;var waters=new List<GeoWater>();var buildings=new List<GeoBuilding>();var areas=new List<GeoArea>();var nodePoints=new Dictionary<string,Vector3>();
            float Number(string value,float fallback){if(value==null)return fallback;var match=Regex.Match(value,@"[-+]?\d+(\.\d+)?");return match.Success&&float.TryParse(match.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float v)?v:fallback;}
            foreach(var element in elements)
            {
                var tags=element["tags"] as JObject;var geometry=element["geometry"] as JArray;if(tags==null||geometry==null||geometry.Count<2)continue;var points=new List<Vector3>();bool clipped=false;
                foreach(var point in geometry){if(!(point is JObject)||point["lat"]==null||point["lon"]==null){points.Add(new Vector3(float.NaN,0,0));clipped=true;continue;}float x=(float)(((double)point["lon"]-place.longitude)*111319.4908*cos),z=(float)(((double)point["lat"]-place.latitude)*111319.4908);if(Mathf.Abs(x)>size*.5f-5||Mathf.Abs(z)>size*.5f-5)clipped=true;points.Add(new Vector3(x,region.RawHeight(x,z),z));}
                if(points.Count<2)continue;string id=element["id"].ToString(),highway=(string)tags["highway"];
                RailwayImport.Append(railways,region,tags,id,element["nodes"] as JArray,points,size*.5f-5,ref railwayPoints);
                if(highway!=null)
                {
                    if(roads.Count>4500)throw new Exception("Too many roads in the area.");var nodes=element["nodes"] as JArray;if(nodes==null||nodes.Count!=points.Count)continue;
                    int one=(string)tags["oneway"]=="-1"?-1:(string)tags["oneway"]=="yes"||(string)tags["junction"]=="roundabout"?1:0;bool bridge=tags["bridge"]!=null&&(string)tags["bridge"]!="no",tunnel=tags["tunnel"]!=null&&(string)tags["tunnel"]!="no";
                    float lanes=Number((string)tags["lanes"],2),width=Number((string)tags["width"],Mathf.Clamp(lanes*3.1f,4,20));string limit=(string)tags["maxspeed"];float speed=Number(limit,CountryRules.UsesMiles(place.country_code)?30:50);if(!string.IsNullOrEmpty(limit)&&Regex.IsMatch(limit,@"\d")&&!limit.Contains("mph")||string.IsNullOrEmpty(limit)&&!CountryRules.UsesMiles(place.country_code))speed/=1.609344f;
                    for(int i=0;i<points.Count-1;i++)
                    {
                        if(float.IsNaN(points[i].x)||float.IsNaN(points[i+1].x)||Mathf.Abs(points[i].x)>size*.5f-5||Mathf.Abs(points[i].z)>size*.5f-5||Mathf.Abs(points[i+1].x)>size*.5f-5||Mathf.Abs(points[i+1].z)>size*.5f-5||Vector3.Distance(points[i],points[i+1])<.1f)continue;
                        string a=nodes[i].ToString(),b=nodes[i+1].ToString();nodePoints[a]=points[i];nodePoints[b]=points[i+1];roads.Add(new GeoRoad{id=id+"-"+i,start=a,end=b,name=(string)tags["name"]??highway,kind=highway,width=width,limit=Mathf.Clamp(Mathf.RoundToInt(speed),10,85),oneWay=one,bridge=bridge,tunnel=tunnel,layer=RailwayTags.Layer(tags),maxVehicleHeight=RailwayTags.Metres((string)tags["maxheight"],0),points=new[]{points[i],points[i+1]}});
                    }
                }
                else if(tags["waterway"]!=null&&waters.Count<250)
                {
                    var run=new List<Vector3>();float previousHeight=float.MaxValue;
                    void Flush(){if(run.Count>=2)waters.Add(new GeoWater{name=(string)tags["name"]??"Waterway",width=Mathf.Clamp(Number((string)tags["width"],(string)tags["waterway"]=="river"?14:3),2,100),points=run.ToArray()});run.Clear();previousHeight=float.MaxValue;}
                    foreach(var value in points){if(float.IsNaN(value.x)){Flush();continue;}var p=value;p.y=Mathf.Min(previousHeight,p.y);previousHeight=p.y;run.Add(p);}Flush();
                }
                else if(tags["building"]!=null){if(!clipped&&buildings.Count<650&&points.Count>=4){float height=Number((string)tags["height"],Number((string)tags["building:levels"],2)*3);buildings.Add(new GeoBuilding{id=id,kind=(string)tags["building"],roof=(string)tags["roof:shape"]??"gabled",height=Mathf.Clamp(height,2.5f,80),measuredHeight=tags["height"]!=null||tags["building:levels"]!=null,points=points.ToArray()});}}
                else if((tags["landuse"]!=null||tags["natural"]!=null)&&points.Count>=4&&points.Count<=4096&&areas.Count<400&&!points.Any(p=>float.IsNaN(p.x)))
                {var boundary=ClipArea(points,size*.5f-5);if(boundary.Length>=4)areas.Add(new GeoArea{kind=(string)tags["landuse"]??(string)tags["natural"]??"grass",points=boundary});}
            }
            if(roads.Count<8)throw new Exception("Not enough drivable roads here; try a nearby settlement.");
            // Share node heights and cap un-surveyed gradients without breaking junctions.
            for(int pass=0;pass<30;pass++){bool changed=false;foreach(var r in roads){var a=nodePoints[r.start];var b=nodePoints[r.end];float max=Mathf.Max(.1f,new Vector2(a.x-b.x,a.z-b.z).magnitude*.16f);if(a.y>b.y+max){a.y=b.y+max;nodePoints[r.start]=a;changed=true;}if(b.y>a.y+max){b.y=a.y+max;nodePoints[r.end]=b;changed=true;}}if(!changed)break;}
            foreach(var r in roads){r.points[0]=nodePoints[r.start];r.points[1]=nodePoints[r.end];}
            region.railwaySourceIdentity=(string)json["roadRushRailwaySource"]?["identity"];region.railwaySourceComplete=(bool?)json["roadRushRailwaySource"]?["complete"]==true;
            region.roads=roads.ToArray();region.railways=railways.ToArray();region.railwayProfilesPrepared=false;region.waters=waters.ToArray();region.buildings=buildings.ToArray();region.areas=areas.ToArray();
            foreach(var area in areas)for(int z=0;z<65;z++)for(int x=0;x<65;x++){var point=new Vector2(-size*.5f+x*size/64,-size*.5f+z*size/64);if(GeoRegion.Contains(area.points,point))region.landCover[z*65+x]=area.kind=="forest"||area.kind=="wood"?2:area.kind=="residential"||area.kind=="industrial"||area.kind=="commercial"?3:1;}
            region.Prepare();region.route=PreviewLoop(region,nodePoints);return region;
        }
        static Vector3[] ClipArea(List<Vector3> polygon,float half)
        {
            // Preserve land cover which crosses a prepared area's boundary instead
            // of dropping entire forests/residential districts at the cache edge.
            var output=new List<Vector3>(polygon);if(output.Count>1&&(output[0]-output[output.Count-1]).sqrMagnitude<.01f)output.RemoveAt(output.Count-1);
            foreach(int edge in new[]{0,1,2,3})
            {
                var input=output;output=new List<Vector3>();if(input.Count==0)break;
                float Distance(Vector3 p)=>edge==0?p.x+half:edge==1?half-p.x:edge==2?p.z+half:half-p.z;
                var previous=input[input.Count-1];float before=Distance(previous);
                foreach(var current in input)
                {
                    float after=Distance(current);if((before>=0)!=(after>=0))output.Add(Vector3.Lerp(previous,current,before/(before-after)));
                    if(after>=0)output.Add(current);previous=current;before=after;
                }
            }
            if(output.Count<3)return Array.Empty<Vector3>();output.Add(output[0]);return output.ToArray();
        }
        static Vector3[] PreviewLoop(GeoRegion region,Dictionary<string,Vector3> nodes)
        {
            var graph=new Dictionary<string,List<string>>();foreach(var r in region.roads){if(r.tunnel)continue;if(!graph.ContainsKey(r.start))graph[r.start]=new List<string>();if(!graph.ContainsKey(r.end))graph[r.end]=new List<string>();graph[r.start].Add(r.end);graph[r.end].Add(r.start);}
            string root=graph.Keys.OrderBy(k=>nodes[k].sqrMagnitude).First();var parent=new Dictionary<string,string>{{root,null}};var queue=new Queue<string>();queue.Enqueue(root);List<string> best=null;float bestScore=float.MaxValue;
            while(queue.Count>0)
            {
                string a=queue.Dequeue();foreach(string b in graph[a])
                {
                    if(!parent.ContainsKey(b)){parent[b]=a;queue.Enqueue(b);continue;}if(parent[a]==b||parent[b]==a)continue;
                    var left=new List<string>();for(string p=a;p!=null;p=parent[p])left.Add(p);var right=new List<string>();for(string p=b;p!=null;p=parent[p])right.Add(p);int common=left.FindIndex(right.Contains);if(common<0)continue;string join=left[common];var cycle=left.Take(common+1).Reverse().ToList();cycle.AddRange(right.TakeWhile(k=>k!=join));cycle.Add(join);float length=0;for(int i=1;i<cycle.Count;i++)length+=Vector3.Distance(nodes[cycle[i-1]],nodes[cycle[i]]);if(length<350)continue;float score=Mathf.Abs(length-2000)+nodes[join].magnitude;if(score<bestScore){best=cycle;bestScore=score;}
                }
            }
            if(best!=null)return best.Select(k=>nodes[k]).ToArray();
            var route=new List<Vector3>();string current=root,previous=null;for(int i=0;i<200;i++){route.Add(nodes[current]);string next=graph[current].FirstOrDefault(k=>k!=previous&&!route.Contains(nodes[k]));if(next==null)break;previous=current;current=next;}if(route.Count<2)throw new Exception("No usable driving route.");for(int i=route.Count-2;i>=0;i--)route.Add(route[i]);return route.ToArray();
        }
        void OnDestroy(){if(active!=null)active.Abort();try{if(systemRequest!=null&&!systemRequest.HasExited)systemRequest.Kill();systemRequest?.Dispose();}catch{}foreach(var path in temporaryFiles)try{File.Delete(path);}catch{}}
    }
}
