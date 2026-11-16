using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RoadRush
{
    // Inspection and an explicit next-area preparation; installed files remain read-only.
    public sealed class SceneryPackMenu : IDisposable
    {
        DetailInventoryJob job;DetailInventorySnapshot snapshot;
        Task<Tuple<CountryPack,string>> countryWork;CancellationTokenSource countryCancel;
        WorldPlace place;GeoRegion prepared,previousReady;
        Vector2 scroll;bool pending;string status="";GUIStyle title,body,small,button,toggle;
        public DetailInventorySnapshot Snapshot=>snapshot;
        public string Status=>status;
        public bool Scanning=>job!=null&&!job.Completion.IsCompleted;
        public GeoRegion Prepared=>prepared;
        public bool Preparing=>pending;
        bool HasCountry=>place!=null&&snapshot!=null&&snapshot.CountryRoots.Any(r=>r.CountryMetadataReady&&r.DatabasePresent&&string.Equals(r.Country,place.country_code,StringComparison.OrdinalIgnoreCase));
        void Styles()
        {
            if(title!=null)return;
            title=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            body=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true,richText=false};body.normal.textColor=Color.white;
            small=new GUIStyle(body){fontSize=13};small.normal.textColor=new Color(.65f,.73f,.77f);
            button=new GUIStyle(GUI.skin.button){fontSize=16};toggle=new GUIStyle(GUI.skin.toggle){fontSize=17};toggle.normal.textColor=Color.white;
        }
        public void Open(GameSession session){CancelCountry();prepared=null;pending=false;Refresh(session);}
        public bool SetDetailRequested(GameSession session,bool requested)
        {
            if(pending)return false;
            bool previous=session.Profile.detailedScenery;if(previous==requested)return true;
            session.Profile.detailedScenery=requested;
            if(!session.TrySave()){session.Profile.detailedScenery=previous;status="Could not save the scenery choice; previous choice retained.";return false;}
            prepared=null;Refresh(session);return true;
        }
        public void Refresh(GameSession session)
        {
            if(pending)return;
            job?.Dispose();job=null;snapshot=null;
            var region=session.World.Route.Region;place=region!=null?StartAreaPreflight.Place(region):null;
            try
            {
                // Capture Unity-dependent paths only. Discovery, metadata and hashing run in the worker.
                job=new DetailInventoryJob(DetailInventoryDiscovery.CaptureSearchPaths(),place,session.Profile.detailedScenery);
                status="Checking installed scenery files…";
            }
            catch(Exception e){status="Scenery inspection unavailable: "+Clean(e.Message);}
        }
        public void Update(GameSession session)
        {
            if(job!=null&&job.Completion.IsCompleted)
            {
                if(job.Completion.IsFaulted)status="Scenery inspection unavailable: "+Clean(job.Completion.Exception.GetBaseException().Message);
                else if(job.Completion.IsCanceled)status="Scenery inspection cancelled.";
                else{snapshot=job.Completion.Result;status=snapshot.Cancelled?"Scenery inspection cancelled.":snapshot.SelectionComplete?"Installed file checks complete.":"Some files could not be checked; see the notices below.";}
                job.Dispose();job=null;
            }
            if(countryWork!=null&&countryWork.IsCompleted)
            {
                var work=countryWork;countryWork=null;countryCancel?.Dispose();countryCancel=null;
                if(work.IsCanceled){pending=false;status="Area preparation cancelled.";}
                else if(work.IsFaulted){pending=false;status="Country inspection failed: "+Clean(work.Exception.GetBaseException().Message);}
                else if(work.Result.Item1==null){pending=false;status=work.Result.Item2;}
                else if(session.Network.Active||session.World.IsCircuit||session.Downloads.Busy||LocationLoading.Busy)
                {pending=false;status="Return to a road area outside LAN to prepare scenery.";}
                else
                {
                    session.Downloads.PrepareOffline(place,work.Result.Item1);pending=session.Downloads.Busy;
                    status=pending?"Preparing this area from the installed country pack…":"This area could not be prepared.";
                }
            }
            if(pending&&countryWork==null&&!session.Downloads.Busy)
            {
                pending=false;
                if(session.Downloads.Ready!=null&&!ReferenceEquals(session.Downloads.Ready,previousReady))
                {prepared=session.Downloads.Ready;status="Area prepared. Choose Drive prepared area to enter it.";}
                else status=session.Downloads.Status;
            }
        }
        public bool PrepareCurrent(GameSession session)
        {
            if(pending||!HasCountry||session.World.IsCircuit||session.Network.Active||session.Downloads.Busy||session.PreparingStart||LocationLoading.Busy)return false;
            if(!session.TrySave()){status="Could not save your career; preparation has not started.";return false;}
            var roots=snapshot.CountryRoots.Where(r=>r.CountryMetadataReady&&r.DatabasePresent&&string.Equals(r.Country,place.country_code,StringComparison.OrdinalIgnoreCase)).ToArray();
            var area=new WorldPlace{name=place.name,latitude=place.latitude,longitude=place.longitude,country_code=place.country_code,timezone=place.timezone,drivingSide=place.drivingSide,size=place.size};
            CancelCountry();countryCancel=new CancellationTokenSource();var token=countryCancel.Token;
            prepared=null;previousReady=session.Downloads.Ready;pending=true;
            countryWork=Task.Run(()=>ResolveCountry(roots,area,token),token);status="Checking installed country coverage…";return true;
        }
        static Tuple<CountryPack,string> ResolveCountry(DetailInventoryRoot[] roots,WorldPlace area,CancellationToken token)
        {
            string lastError="";var candidates=new List<Tuple<CountryPack,bool,long>>();
            foreach(var root in roots)
            {
                token.ThrowIfCancellationRequested();
                try{var p=new CountryPack(root.CountryFolder);if(p.Ready)candidates.Add(Tuple.Create(p,(bool?)p.Metadata["completeNodes"]==true,long.TryParse(p.Version,out long v)?v:0));}
                catch(Exception e){lastError=Clean(e.Message);}
            }
            foreach(var candidate in candidates.OrderByDescending(p=>p.Item2).ThenByDescending(p=>p.Item3))
            {token.ThrowIfCancellationRequested();try{if(candidate.Item1.Covers(area))return Tuple.Create(candidate.Item1,"");}catch(Exception e){lastError=Clean(e.Message);}}
            return Tuple.Create<CountryPack,string>(null,string.IsNullOrEmpty(lastError)?"No ready installed country pack covers this area.":"The installed country pack could not prepare this area: "+lastError);
        }
        public bool DrivePrepared(GameSession session)
        {
            if(prepared==null||pending||session.Network.Active||session.Downloads.Busy||LocationLoading.Busy)return false;
            prepared.Nearest(session.Car.Body.position,out var start,out _);return session.TryStartAt(prepared.id,start);
        }
        public void Draw(GameSession session,Action back)
        {
            Styles();GUI.Box(new Rect(35,240,1370,620),GUIContent.none);
            Label(65,257,1100,34,"OPTIONAL SCENERY PACKS",title);
            Label(65,298,1270,36,"Installed buildings and woodland · file sizes, coverage and licences",body);
            bool beforeToggle=GUI.enabled;GUI.enabled=beforeToggle&&!pending;
            bool requested=InterfaceTheme.CheckBox(new Rect(65,340,810,28),session.Profile.detailedScenery,(session.Profile.detailedScenery?"ON · ":"OFF · ")+"Use installed high-detail buildings and woodland",toggle);GUI.enabled=beforeToggle;
            if(requested!=session.Profile.detailedScenery)SetDetailRequested(session,requested);
            Label(65,377,1260,43,"Apply this choice by preparing the area again. Your current drive keeps its prepared scenery.",small);
            bool old=GUI.enabled;GUI.enabled=old&&!Scanning&&!pending;
            if(ControllerMenuGUI.Button(new Rect(1110,336,230,34),"REFRESH FILE CHECKS",button))Refresh(session);GUI.enabled=old;
            int rows=snapshot?.Rows.Count??0,issues=snapshot?.Issues.Count??0;
            float height=Math.Max(318,rows*151+issues*43+55);
            scroll=ControllerMenuGUI.BeginScrollView(new Rect(65,425,1275,316),scroll,new Rect(0,0,1245,height));
            if(snapshot==null)Label(12,12,1200,70,status,body);
            else
            {
                if(rows==0)Label(12,12,1200,55,"No compiled building or woodland packs were found in the installed country folders.",body);
                int first=Math.Max(0,(int)(scroll.y/151)-1),last=Math.Min(rows,(int)((scroll.y+316)/151)+2);
                for(int i=first;i<last;i++)DrawRow(snapshot.Rows[i],i*151);
                float y=rows==0?70:rows*151;
                foreach(string issue in snapshot.Issues){if(y+43>=scroll.y&&y<=scroll.y+316)Label(12,y,1200,40,Clean(issue,350),small);y+=43;}
                Label(12,y,1200,42,"Sizes show compiled scenery data. Shared graphics come with the game; raw LiDAR is not required to drive.",small);
            }
            ControllerMenuGUI.EndScrollView();
            Label(65,752,1250,39,pending&&countryWork==null?session.Downloads.Status:status,small);
            if(ControllerMenuGUI.Button(new Rect(65,800,175,37),"BACK",button)){CancelCountry();pending=false;session.Save();back();}
            GUI.enabled=old&&!pending&&HasCountry&&!session.World.IsCircuit&&!session.Network.Active&&!session.Downloads.Busy&&!session.PreparingStart&&!LocationLoading.Busy;
            if(ControllerMenuGUI.Button(new Rect(265,800,345,37),"PREPARE CURRENT AREA",button))PrepareCurrent(session);
            GUI.enabled=old&&prepared!=null&&!pending&&!session.Network.Active&&!session.Downloads.Busy&&!LocationLoading.Busy;
            if(ControllerMenuGUI.Button(new Rect(635,800,345,37),"DRIVE PREPARED AREA",button))DrivePrepared(session);
            GUI.enabled=old;
            Label(1000,793,340,54,session.Network.Active?"Leave LAN to prepare another area.":session.World.IsCircuit?"Select a road area to prepare scenery.":!HasCountry?"An installed country pack is needed for preparation.":"Source revisions of an existing drive may be unavailable.",small);
        }
        void DrawRow(DetailInventoryRow row,float y)
        {
            GUI.Box(new Rect(0,y,1228,141),GUIContent.none);
            string name=string.IsNullOrEmpty(row.Id)?Path.GetFileName(row.Folder):row.Id;
            string revision=row.Revision.HasValue?"revision "+row.Revision.Value:"legacy revision";
            Label(12,y+7,1200,29,(row.Kind=="Canopy"?"Woodland":Clean(row.Kind))+" · "+Clean(name)+" · "+revision+" · "+Clean(row.Country),body);
            string selection=row.SelectedForNextPreparation?"Eligible for next preparation":row.State==DetailInventoryState.SelectedRevision?(row.DetailRequested?"Selected installed revision":"Detail disabled") :row.State.ToString();
            string coverage=row.Coverage==DetailInventoryCoverage.InsideRequestedArea?"covers this area":row.Coverage==DetailInventoryCoverage.OutsideRequestedArea?"outside this area":"coverage not checked";
            Label(12,y+39,1200,30,selection+" · "+coverage+" · game data "+Size(row.KnownRuntimeBytes)+" · "+Clean(row.Licence),small);
            Label(12,y+72,1200,30,Clean(row.Attribution,600),small);
            string reason=row.State==DetailInventoryState.SelectedRevision?"Compiled files verified; scenery is checked when the area is prepared.":row.Reason;
            Label(12,y+105,1200,30,Clean(reason,600),small);
        }
        static string Size(long? bytes)=>bytes.HasValue?(bytes.Value<1024?bytes.Value+" B":bytes.Value<1024*1024?(bytes.Value/1024d).ToString("F1")+" KiB":(bytes.Value/(1024d*1024)).ToString("F2")+" MiB"):"unknown";
        static string Clean(string text,int length=200)=>new string((text??"").Take(length).Select(c=>char.IsControl(c)?' ':c).ToArray());
        static void Label(float x,float y,float w,float h,string text,GUIStyle style)=>GUI.Label(new Rect(x,y,w,h),text,style);
        void CancelCountry(){countryCancel?.Cancel();countryCancel?.Dispose();countryCancel=null;countryWork=null;}
        public void Dispose(){CancelCountry();job?.Dispose();job=null;}
    }
}
