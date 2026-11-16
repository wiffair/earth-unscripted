using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
namespace RoadRush
{
    // An authorised data adapter keeps provider credentials outside the game and
    // publishes the documented normalised snapshot. No account is auto-created.
    public sealed class TrainServiceFeed:MonoBehaviour
    {
        readonly PublicTrainData publicData=new PublicTrainData();
        GameSession session;UnityWebRequest request;Coroutine work;double nextPoll;string source="";int mode=-1;bool disposed;
        public TrainServiceSnapshot Snapshot{get;private set;}
        public string Status{get;private set;}="Local simulated trains · live connection not configured";
        internal static Func<double> VerificationClock;
        public static double Now=>SinglePlayerBeta.VerificationOutput!=null&&VerificationClock!=null?VerificationClock():DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;
        public string LocalPath=>Path.Combine(SinglePlayerBeta.DataRoot,"TrainData","services.json");
        public void Initialize(GameSession owner){session=owner;}
        void Fallback(string reason)
        {
            Snapshot=null;
            if(!session.Profile.trainTimetableFallback){Status=reason;return;}
            string country=session.World.Route.Region.countryCode??"";
            var region=session.World.Route.Region;var car=session.Car.Body.position;double lat=region.latitude+car.z/111319.4908,lon=region.longitude+car.x/(111319.4908*Math.Cos(region.latitude*Math.PI/180));
            if(!WorldProviderRegistry.Resolve(lon,lat,country,"rail.timetable",DateTimeOffset.FromUnixTimeSeconds((long)Now).UtcDateTime).Any(p=>p.Id=="rail.gb.published"&&p.Available)){Status=reason+" · no valid published timetable for this area/date";return;}
            Snapshot=PublishedTrainTimetable.Create(Now,out string status);
            if(Snapshot!=null&&!PublishedTrainTimetable.Paths.Values.Any(path=>{path.Project(path.DatasetPoint(session.World.Route.Region,session.Car.Body.position),out float gap,2000);return gap<=2000;}))
            {Snapshot=null;status="No published timetable for this area · choose Local Simulation";}
            Status=reason+" · "+status;
        }
        public static bool AllowedEndpoint(string text)
        {return Uri.TryCreate(text,UriKind.Absolute,out var uri)&&string.IsNullOrEmpty(uri.UserInfo)&&string.IsNullOrEmpty(uri.Fragment)&&(uri.Scheme=="https"||uri.Scheme=="http"&&uri.IsLoopback);}
        void Update()
        {
            using var timing=WorkTelemetry.Measure("frame.train-feed",null,8);
            if(disposed||!session||!session.BootReady)return;
            string selected=(session.Profile.trainFeedUrl??"").Trim();int selectedMode=session.Profile.trainServiceMode;
            if(selectedMode!=mode||selected!=source)
            {if(work!=null){StopCoroutine(work);work=null;}if(request!=null){request.Abort();request.Dispose();request=null;}source=selected;mode=selectedMode;Snapshot=null;nextPoll=0;}
            if(mode==0){Status="Local simulated trains · no live data requested";return;}
            if(work==null&&Time.realtimeSinceStartupAsDouble>=nextPoll)work=StartCoroutine(GuardedPoll());
        }
        IEnumerator Poll()
        {
            nextPoll=Time.realtimeSinceStartupAsDouble+15;
            try
            {
                string text;
                if(string.IsNullOrEmpty(source))
                {
                    if(!File.Exists(LocalPath))
                    {
                        var region=session.World.Route.Region;var car=session.Car.Body.position;double lat=region.latitude+car.z/111319.4908,lon=region.longitude+car.x/(111319.4908*Math.Cos(region.latitude*Math.PI/180));
                        bool available=WorldProviderRegistry.Resolve(lon,lat,region.countryCode,"rail.live").Any(p=>p.Available);
                        string endpoint=session.World.AutomaticWorld.PublicRailEndpoint;
                        if(!available||string.IsNullOrEmpty(endpoint)){Fallback("No account-free live source for this location");yield break;}
                        string url=endpoint+"?lon="+lon.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"&lat="+lat.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"&country="+Uri.EscapeDataString(region.countryCode??"");
                        request=UnityWebRequest.Get(url);request.timeout=3;request.redirectLimit=0;var pending=request.SendWebRequest();
                        while(!pending.isDone){if(request.downloadedBytes>2*1024*1024){request.Abort();throw new IOException("Public rail response limit");}yield return null;}
                        if(request.result!=UnityWebRequest.Result.Success){Fallback("Public source temporarily unavailable");yield break;}
                        text=request.downloadHandler.text;
                        Snapshot=publicData.Convert(text,Now,region.longitude,region.latitude,region.countryCode,session.World.AutomaticWorld.Renderer.ResidentAreas.SelectMany(a=>a.features??Array.Empty<RoadRush.NewbridgePrototype.NewbridgeWorld.Feature>()),session.Profile.trainTimetableFallback);
                        Status=(string)Newtonsoft.Json.Linq.JObject.Parse(text)["status"];
                        if(Snapshot==null||Snapshot.Services.Length==0)Status+=" · waiting for nearby reports on loaded, unambiguous track";
                        yield break;
                    }
                    if(new FileInfo(LocalPath).Length>2*1024*1024)throw new IOException("Rail file is too large");text=File.ReadAllText(LocalPath);nextPoll=Time.realtimeSinceStartupAsDouble+3;
                }
                else
                {
                    if(!AllowedEndpoint(source)){Fallback("Use a secure rail-data connection or the local data file");yield break;}
                    request=UnityWebRequest.Get(source);request.timeout=10;request.redirectLimit=0;var operation=request.SendWebRequest();
                    while(!operation.isDone){if(request.downloadedBytes>2*1024*1024){request.Abort();throw new IOException("Rail response is too large");}yield return null;}
                    if(request.result!=UnityWebRequest.Result.Success)throw new IOException("Rail data connection unavailable");text=request.downloadHandler.text;
                }
                Snapshot=TrainServiceSnapshot.Parse(text,Now);Status=(mode==2?"Reported positions where available; other positions estimated":"Timetable estimates")+" · "+Snapshot.Provider;
            }
            finally{request?.Dispose();request=null;work=null;}
        }
        IEnumerator GuardedPoll()
        {
            yield return null;var iterator=Poll();
            while(true)
            {
                bool more=false,failed=false;object current=null;
                try{using var timing=WorkTelemetry.Measure("frame.train-feed-poll",null,8);more=iterator.MoveNext();if(more)current=iterator.Current;}
                catch(Exception){Fallback("Rail connection unavailable");nextPoll=Time.realtimeSinceStartupAsDouble+15;work=null;failed=true;}
                if(failed||!more)yield break;yield return current;
            }
        }
        void OnDestroy(){disposed=true;if(request!=null)request.Abort();if(work!=null)StopCoroutine(work);request?.Dispose();}
    }
}
