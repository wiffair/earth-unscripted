using System.Linq;
using System;
using System.Globalization;
using UnityEngine;
namespace EarthUnscripted
{
    public sealed class WorldMapMenu : IDisposable
    {
        string packSummary="",query="Llangollen",latitude="52.969",longitude="-3.171",country="GB",zone="Europe/London",name="Custom location",selectionKey="";
        string pendingSavedId="";
        bool opened;int side,areaChoice=0;Texture2D preview;GeoRegion shown;Vector3 start;bool prepared;
        string[] cached,cachedLabels;Vector2 scroll;Vector2 controllerCursor = new Vector2(.5f,.5f);
        GUIStyle label,small,title,button,toolbar;
        NavigationAtlas atlas;
        public void Dispose(){ClearMap();atlas?.Dispose();atlas=null;}
        internal bool MatchesSelection(GeoRegion region)=>region!=null&&double.TryParse(latitude,NumberStyles.Float,CultureInfo.InvariantCulture,out var lat)&&double.TryParse(longitude,NumberStyles.Float,CultureInfo.InvariantCulture,out var lon)&&Math.Abs(region.latitude-lat)<.00001&&Math.Abs(region.longitude-lon)<.00001&&string.Equals(region.countryCode??"",country??"",StringComparison.OrdinalIgnoreCase);
        public GeoRegion Shown=>shown;
        public Vector3 SelectedStart=>start;
        public bool CanDrive=>prepared&&shown!=null;
        void Styles(){if(label!=null)return;label=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true};small=new GUIStyle(label){fontSize=13};title=new GUIStyle(label){fontSize=24,fontStyle=FontStyle.Bold};button=new GUIStyle(GUI.skin.button){fontSize=16};toolbar=new GUIStyle(button){fontSize=14,padding=new RectOffset(6,6,5,5)};}
        void Text(float x,float y,float w,float h,string text,GUIStyle style)=>GUI.Label(new Rect(x,y,w,h),text,style);
        void ClearMap(){if(preview)UnityEngine.Object.Destroy(preview);preview=null;shown=null;prepared=false;}
        void Choose(GeoRegion region,bool ready)
        {
            ClearMap();shown=region;prepared=ready;preview=RegionMapTexture.Create(region);
            if(ready)prepared=TryRoadStart(region,Vector3.zero,out start);
        }
        public static bool TryRoadStart(GeoRegion region,Vector3 requested,out Vector3 point)
        {
            point=requested;float best=float.PositiveInfinity;bool found=false;
            foreach(var road in region.roads)
            {
                if(road.tunnel||road.points==null)continue;
                for(int i=1;i<road.points.Length;i++)
                {
                    var a=road.points[i-1];var delta=road.points[i]-a;float squared=delta.x*delta.x+delta.z*delta.z;
                    if(squared<.000001f)continue;
                    float t=Mathf.Clamp01(((requested.x-a.x)*delta.x+(requested.z-a.z)*delta.z)/squared);
                    var candidate=a+delta*t;float distance=(new Vector2(candidate.x-requested.x,candidate.z-requested.z)).sqrMagnitude;
                    if(distance>=best)continue;best=distance;point=candidate;found=true;
                }
            }
            return found;
        }
        WorldPlace Place()=>new WorldPlace{name=name,latitude=double.Parse(latitude,CultureInfo.InvariantCulture),longitude=double.Parse(longitude,CultureInfo.InvariantCulture),country_code=country.ToUpperInvariant(),timezone=zone,size=new[]{1000,3000,6000}[areaChoice],drivingSide=side==0?0:side==1?-1:1};
        string Key()=>latitude+"|"+longitude+"|"+country+"|"+zone+"|"+areaChoice+"|"+side;
        void RefreshCache(){cached=RegionCache.All();cachedLabels=cached.Select(RegionCache.Label).ToArray();}
        public void SelectPlace(GlobalRegions service,WorldPlace place)
        {
            if(service.Busy)return;
            if(place==null){ClearMap();service.SelectPlace(null);return;}
            query=place.name;name=place.name;latitude=place.latitude.ToString("F6",CultureInfo.InvariantCulture);longitude=place.longitude.ToString("F6",CultureInfo.InvariantCulture);country=place.country_code;zone=place.timezone;side=0;
            selectionKey=Key();ClearMap();
            if(place==null||double.IsNaN(place.latitude)||double.IsNaN(place.longitude)||Math.Abs(place.latitude)>80||Math.Abs(place.longitude)>179.98)
            {service.SelectPlace(place);return;}
            var selected=Place();float span=selected.size;
            shown=new GeoRegion{id=RegionCache.Id(selected),name=selected.name,latitude=selected.latitude,longitude=selected.longitude,countryCode=selected.country_code,timeZone=selected.timezone,minX=-span*.5f,minZ=-span*.5f,width=span,depth=span,roads=Array.Empty<GeoRoad>(),waters=Array.Empty<GeoWater>()};
            service.SelectPlace(selected);
        }
        public void AcceptPrepared(GlobalRegions service)
        {
            if(!string.IsNullOrEmpty(pendingSavedId)&&service.Ready?.id==pendingSavedId)
            {
                var region=service.Ready;pendingSavedId="";name=region.name;
                latitude=region.latitude.ToString("F6",CultureInfo.InvariantCulture);longitude=region.longitude.ToString("F6",CultureInfo.InvariantCulture);
                country=region.countryCode;zone=region.timeZone;selectionKey=Key();Choose(region,true);RefreshCache();
            }
            if(MatchesSelection(service.Ready)&&!ReferenceEquals(service.Ready,shown)){Choose(service.Ready,true);RefreshCache();}
            else if(MatchesSelection(service.MapPreview)&&!ReferenceEquals(service.MapPreview,shown)&&!prepared)Choose(service.MapPreview,false);
        }
        public bool DriveSelected(GameSession session)=>CanDrive&&!session.Downloads.Busy&&session.TryStartAt(shown.id,start);
        public void Open(GameSession session)
        {
            packSummary=string.Join(", ",CountryPacks.Installed().GroupBy(p=>p.Name).Select(g=>g.First()).Select(p=>p.Name+(p.Ready?" installed":" preparing")));
            RegionCacheMaintenance.Trim(session.World.Route.Region?.id);RefreshCache();
            if(opened)return;opened=true;
            var region=session.World.Route.Region;
            if(region!=null&&!session.World.IsCircuit)
            {
                name=region.name;latitude=region.latitude.ToString("F6",CultureInfo.InvariantCulture);longitude=region.longitude.ToString("F6",CultureInfo.InvariantCulture);country=region.countryCode;zone=region.timeZone;selectionKey=Key();Choose(region,true);
            }
        }
        public void Draw(GameSession session,Action back)
        {
            Styles();var service=session.Downloads;bool enabled=GUI.enabled;
            GUI.Box(new Rect(35,240,1370,620),GUIContent.none);Text(65,260,1200,35,"CHOOSE WHERE TO DRIVE",title);
            var inputEvent=Event.current;
            bool submitSearch=inputEvent.type==EventType.KeyDown&&(inputEvent.keyCode==KeyCode.Return||inputEvent.keyCode==KeyCode.KeypadEnter)&&GUI.GetNameOfFocusedControl()=="place-search-query";
            GUI.SetNextControlName("place-search-query");
            query=ControllerMenuGUI.TextField(new Rect(65,315,470,38),query,100);
            if(submitSearch){if(enabled&&!service.Busy)service.Search(query);inputEvent.Use();}
            GUI.enabled=enabled&&!service.Busy;
            if(ControllerMenuGUI.Button(new Rect(550,315,155,38),"SEARCH",button))service.Search(query);
            var results=service.Results;
            for(int i=0;i<Math.Min(5,results.Length);i++)
            {
                var p=results[i];if(ControllerMenuGUI.Button(new Rect(65,370+i*39,640,34),p.name+" · "+p.country,button))SelectPlace(service,p);
            }
            Text(65,580,620,26,"Or enter latitude, longitude, country and time zone",small);
            latitude=ControllerMenuGUI.TextField(new Rect(65,615,150,34),latitude,14);longitude=ControllerMenuGUI.TextField(new Rect(225,615,150,34),longitude,14);country=ControllerMenuGUI.TextField(new Rect(385,615,75,34),country,2);zone=ControllerMenuGUI.TextField(new Rect(470,615,235,34),zone,60);
            areaChoice=ControllerMenuGUI.Toolbar(new Rect(65,665,310,33),areaChoice,new[]{"1 KM","3 KM","6 KM"},toolbar);side=ControllerMenuGUI.Toolbar(new Rect(385,665,320,33),side,new[]{"AUTO SIDE","LEFT","RIGHT"},toolbar);
            if(selectionKey!=Key())
            {
                selectionKey=Key();ClearMap();service.Ready=null;service.MapPreview=null;
                service.Status="Area settings changed. Prepare this area to update the map and choose a road.";
            }
            if(ControllerMenuGUI.Button(new Rect(65,715,640,42),"PREPARE THIS AREA",button))
            {
                prepared=false;service.Ready=null;
                if(double.TryParse(latitude,NumberStyles.Float,CultureInfo.InvariantCulture,out _)&&double.TryParse(longitude,NumberStyles.Float,CultureInfo.InvariantCulture,out _))service.Download(Place());
                else service.Status="Enter valid latitude and longitude values, for example 51.480 and -3.180.";
            }
            GUI.enabled=enabled;Text(65,770,640,52,service.Status,small);
            if(ControllerMenuGUI.Button(new Rect(65,825,210,30),"BACK",button))back();
            Text(290,825,420,30,RegionCacheMaintenance.Status,small);
            AcceptPrepared(service);
            Rect map=new Rect(820,320,440,350);GUI.Box(map,GUIContent.none);
            if(shown!=null)
            {
                if(atlas==null)atlas=new NavigationAtlas();atlas.Update();
                GUI.BeginGroup(map);atlas.Window(shown,new Vector2(shown.minX+shown.width*.5f,shown.minZ+shown.depth*.5f),shown.width,new Rect(0,0,map.width,map.height));atlas.Draw();GUI.EndGroup();
            }
            if(preview)GUI.DrawTexture(map,preview,ScaleMode.StretchToFill);
            else Text(845,430,390,160,name+"\n\n"+(service.Busy?"Loading this area's map…":"Prepare this area to show its roads."),label);
            bool selectFromPad = ControllerMenuGUI.Map(map, "Choose a starting road", out var moveCursor, out _);
            controllerCursor = Vector2.Min(Vector2.one, Vector2.Max(Vector2.zero, controllerCursor + new Vector2(moveCursor.x, -moveCursor.y) * .55f));
            if (ControllerMenuGUI.Active?.Focused?.Kind == ControllerMenuNavigation.ControlKind.Map)
            {
                var cross = map.position + Vector2.Scale(controllerCursor, map.size);
                GUI.Label(new Rect(cross.x - 10, cross.y - 15, 30, 30), "+");
            }
            if (selectFromPad && CanDrive && !service.Busy)
            {
                var requested = new Vector3(shown.minX + controllerCursor.x * shown.width, 0, shown.minZ + (1-controllerCursor.y) * shown.depth);
                if (TryRoadStart(shown, requested, out var roadStart)) start = roadStart;
            }
            if(shown!=null)
            {
                if(CanDrive&&!service.Busy&&Event.current.type==EventType.MouseDown&&map.Contains(Event.current.mousePosition))
                {
                    var uv=Event.current.mousePosition-map.position;var point=new Vector3(shown.minX+uv.x/map.width*shown.width,0,shown.minZ+(1-uv.y/map.height)*shown.depth);if(TryRoadStart(shown,point,out var snapped))start=snapped;Event.current.Use();
                }
                if(CanDrive)
                {
                    float x=map.x+(start.x-shown.minX)/shown.width*map.width,y=map.y+(1-(start.z-shown.minZ)/shown.depth)*map.height;GUI.color=Color.yellow;GUI.DrawTexture(new Rect(x-4,y-4,8,8),Texture2D.whiteTexture);GUI.color=Color.white;
                }
            }
            Text(795,675,550,48,(shown?.name??name)+"\n"+(CanDrive?"Ready. Click a road, or focus the map: stick moves +, A selects.":"Map preview only. Prepare this area before driving."),small);
            GUI.enabled=enabled&&CanDrive&&!service.Busy;
            if(ControllerMenuGUI.Button(new Rect(795,735,270,39),"DRIVE FROM SELECTED ROAD",button))DriveSelected(session);
            if(ControllerMenuGUI.Button(new Rect(1080,735,255,39),"RANDOM ROAD START",button))
            {
                var roads=shown.roads.Where(r=>!r.tunnel).ToArray();if(roads.Length>0){var road=roads[UnityEngine.Random.Range(0,roads.Length)];session.StartAt(shown.id,shown.RoadPoint(road,road.length*.5f,out _));}
            }
            GUI.enabled=enabled&&!service.Busy;
            if(cached==null)RefreshCache();
            if(cached.Length>0)
            {
                scroll=ControllerMenuGUI.BeginScrollView(new Rect(795,785,540,42),scroll,new Rect(0,0,Mathf.Max(540,cached.Length*180),32));
                for(int i=0;i<cached.Length;i++)if(ControllerMenuGUI.Button(new Rect(i*180,0,175,28),cachedLabels[i],button))
                {
                    pendingSavedId=cached[i];ClearMap();service.OpenSavedArea(pendingSavedId);
                }
                ControllerMenuGUI.EndScrollView();
            }
            GUI.enabled=enabled;
            Text(795,265,550,48,"Select a place → preview its map → prepare → choose a road.\nDownloads switch servers automatically. More roads load as you drive.",small);
            // Provider hostnames belong in diagnostics, not unexplained buttons.
            Text(795,830,550,28,"Roads: OpenStreetMap · terrain heights: Copernicus / Open-Meteo",small);
        }
    }
}
