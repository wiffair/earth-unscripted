using System;
using System.Threading.Tasks;
using UnityEngine;
namespace EarthUnscripted
{
    sealed class DrivingNavigation : IDisposable
    {
        public bool IsOpen{get;private set;}
        public NavigationRoute Current{get;private set;}
        internal NavigationRoute Preview=>preview;
        internal Vector2 ViewCentre=>centre;internal float ViewSpan=>span;
        NavigationRoute preview;Vector3? chosen,destination;Vector2 centre,lastMouse,dragStart;
        float span=4000;bool dragging,moved;string status="Click a road to choose a destination.";int revision;
        GUIStyle label,small,button;Texture2D texture;Vector2 imageCentre;float imageSpan;
        GeoRoad[] imageRoads;Task<Raster> painting;Color32[] pixels;
        sealed class Raster{public Color32[] pixels;public Vector2 centre;public float span;public GeoRoad[] roads;}
        sealed class Planned{public int revision;public NavigationRoute route;public string error;public bool driving;}
        Task<Planned> planning;bool requestPending,forDriving;Vector3 requestStart,requestEnd;GeoRoad[] requestRoads;
        RoadNavigationGraph graph;GeoRoad[] graphRoads;double retryAt;
        NavigationAtlas atlas;GeoRegion requestRegion;
        public void OpenMap(GameSession session)
        {
            if(session==null||!session.BootReady||!session.Car||session.World?.Route?.Region==null){session?.Notify("Choose a real-world location to plan a map route.");return;}
            if(atlas==null)atlas=new NavigationAtlas();
            IsOpen=true;session.SetMenu(true);var p=session.Car.Body.position;centre=new Vector2(p.x,p.z);span=4000;
            chosen=destination;preview=Current;status=Current==null?"Click a road to choose a destination.":"Current route · choose another destination or resume.";revision++;requestPending=false;
        }
        void CloseMap(GameSession session){IsOpen=false;revision++;requestPending=false;preview=null;chosen=null;session.SetMenu(false);}
        public void CancelRoute(){Current=null;destination=null;preview=null;chosen=null;revision++;requestPending=false;status="Route cancelled. Click a road to choose another destination.";}
        void Request(GameSession session,Vector3 target,bool driving)
        {revision++;requestPending=true;requestStart=session.Car.Body.position;requestEnd=target;requestRegion=session.World.Route.Region;requestRoads=requestRegion.roads;forDriving=driving;if(!driving){preview=null;status="Finding a route…";}}
        public void Update(GameSession session)
        {
            if(IsOpen&&(session?.World?.Route?.Region==null||!session.Car)){IsOpen=false;revision++;requestPending=false;preview=null;chosen=null;return;}
            if(IsOpen)atlas?.Update();
            if(IsOpen&&!session.Menu){IsOpen=false;revision++;requestPending=false;preview=null;chosen=null;}
            if(planning!=null&&planning.IsCompleted)
            {
                var finished=planning.IsFaulted?null:planning.Result;planning=null;
                if(finished!=null&&finished.revision==revision)
                {
                    if(finished.driving){if(finished.route!=null)Current=finished.route;}
                    else{preview=finished.route;status=finished.error??(preview!=null?"Route: "+Distance(preview.Length)+" · follow the highlighted road.":"No route found. Choose a nearby road.");if(preview!=null)FitRoute(preview);}
                }
            }
            if(requestPending&&planning==null)
            {
                requestPending=false;int token=revision;var start=requestStart;var target=requestEnd;var roads=requestRoads;bool driving=forDriving;
                var region=requestRegion;
                planning=Task.Run(async()=>{try{NavigationRoute route;try{if(graph==null||graphRoads!=roads){graph=new RoadNavigationGraph(roads);graphRoads=roads;}route=graph.Plan(start,target);}catch(InvalidOperationException){route=await atlas.Plan(region,start,target);}return new Planned{revision=token,route=route,driving=driving};}catch(Exception){return new Planned{revision=token,error="Route unavailable. Check your connection or try a nearby road.",driving=driving};}});
            }
            if(Current!=null&&!IsOpen&&!session.Menu&&destination.HasValue&&Time.realtimeSinceStartupAsDouble>retryAt)
            {
                Current.Closest(session.Car.Body.position,out float gap);
                if(gap>35&&planning==null){retryAt=Time.realtimeSinceStartupAsDouble+5;Request(session,destination.Value,true);}
            }
        }
        static string Distance(float metres)=>metres>=1000?(metres/1000).ToString("0.0")+" km":Mathf.RoundToInt(metres)+" m";
        void FitRoute(NavigationRoute route)
        {
            float west=float.MaxValue,east=float.MinValue,south=float.MaxValue,north=float.MinValue;
            foreach(var p in route.points){west=Mathf.Min(west,p.x);east=Mathf.Max(east,p.x);south=Mathf.Min(south,p.z);north=Mathf.Max(north,p.z);}
            centre=new Vector2((west+east)*.5f,(south+north)*.5f);span=Mathf.Clamp(Mathf.Max(east-west,(north-south)*1330/620)*1.25f,500,24000);
        }
        static void Line(Vector2 a,Vector2 b,Color colour,float width=3)
        {
            var delta=b-a;if(delta.sqrMagnitude<.01f)return;var matrix=GUI.matrix;var old=GUI.color;GUI.color=colour;
            // Rotate in logical UI space, then apply the caller's scale and
            // letterbox transform. RotateAroundPivot unclips the pivot and
            // mixes these spaces inside a scaled group on large displays.
            GUI.matrix=matrix*Matrix4x4.TRS(new Vector3(a.x,a.y,0),Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg),Vector3.one);
            GUI.DrawTexture(new Rect(0,-width*.5f,delta.magnitude,width),Texture2D.whiteTexture);GUI.matrix=matrix;GUI.color=old;
        }
        static void Marker(Vector2 p,Color colour,float size=8){var old=GUI.color;GUI.color=colour;GUI.DrawTexture(new Rect(p.x-size*.5f,p.y-size*.5f,size,size),Texture2D.whiteTexture);GUI.color=old;}
        static void Badge(Rect rect,string text,GUIStyle style)
        {var old=GUI.color;GUI.color=new Color(.02f,.04f,.06f,.95f);GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=Color.white;GUI.Label(new Rect(rect.x+5,rect.y+2,rect.width-10,rect.height-4),text,style);GUI.color=old;}
        static void MiniCar(Rect rect)
        {Marker(rect.center,Color.black,20);Marker(rect.center,Color.white,16);Marker(rect.center,new Color(.05f,.4f,1),12);Arrow(rect.center,0,Color.white);}
        static void Arrow(Vector2 centre,float degrees,Color colour)
        {
            float a=degrees*Mathf.Deg2Rad;var forward=new Vector2(Mathf.Sin(a),-Mathf.Cos(a));var side=new Vector2(-forward.y,forward.x);var tip=centre+forward*13;
            Line(centre-forward*10,tip,colour,3);Line(tip,tip-forward*8+side*7,colour,3);Line(tip,tip-forward*8-side*7,colour,3);
        }
        Vector2 Pixel(Vector3 point,Rect r)=>new Vector2(r.width*.5f+(point.x-centre.x)*r.width/span,r.height*.5f-(point.z-centre.y)*r.width/span);
        Vector3 World(Vector2 pixel,Rect r)=>new Vector3(centre.x+(pixel.x-r.center.x)*span/r.width,0,centre.y-(pixel.y-r.center.y)*span/r.width);
        static bool ClipSide(float direction,float distance,ref float low,ref float high)
        {if(Mathf.Abs(direction)<.000001f)return distance>=0;float t=distance/direction;if(direction<0){if(t>high)return false;low=Mathf.Max(low,t);}else{if(t<low)return false;high=Mathf.Min(high,t);}return true;}
        internal static bool ClipRouteSegment(ref Vector2 a,ref Vector2 b,Rect rect)
        {
            var delta=b-a;float low=0,high=1;
            if(!ClipSide(-delta.x,a.x-rect.xMin,ref low,ref high)||!ClipSide(delta.x,rect.xMax-a.x,ref low,ref high)||
                !ClipSide(-delta.y,a.y-rect.yMin,ref low,ref high)||!ClipSide(delta.y,rect.yMax-a.y,ref low,ref high))return false;
            b=a+delta*high;a+=delta*low;return true;
        }
        void RouteLines(NavigationRoute route,Func<Vector3,Vector2> project,Color colour,Rect viewport)
        {
            if(route==null)return;
            var clip=new Rect(viewport.x+5,viewport.y+5,viewport.width-10,viewport.height-10);
            for(int pass=0;pass<2;pass++)for(int i=1;i<route.points.Length;i++)
            {var a=project(route.points[i-1]);var b=project(route.points[i]);if(ClipRouteSegment(ref a,ref b,clip))Line(a,b,pass==0?Color.black:colour,pass==0?9:5);}
        }
        public void DrawMap(GameSession session)
        {
            if(!IsOpen||session?.World?.Route?.Region==null||!session.Car||atlas==null){IsOpen=false;return;}
            if(label==null){label=new GUIStyle(GUI.skin.label){fontSize=22,wordWrap=true};small=new GUIStyle(label){fontSize=15};button=new GUIStyle(GUI.skin.button){fontSize=17};}
            var incoming=GUI.color;GUI.color=Color.white;
            GUI.Box(new Rect(25,35,1390,830),GUIContent.none);GUI.Label(new Rect(55,48,800,38),"PLAN YOUR DRIVE",label);
            GUI.Label(new Rect(55,88,1300,28),"Mouse: drag / scroll / click · Controller: focus map, sticks pan, triggers zoom, A selects centre",small);
            var rect=new Rect(55,125,1330,620);var e=Event.current;
            bool selectFromPad = ControllerMenuGUI.Map(rect, "Choose destination on map", out var pan, out float zoom);
            centre += pan * (span * .55f);
            span = Mathf.Clamp(span * Mathf.Exp(zoom * 1.6f), 500, 24000);
            if (selectFromPad) { chosen = World(rect.center, rect); Request(session, chosen.Value, false); }

            if(rect.Contains(e.mousePosition))
            {
                if(e.type==EventType.ScrollWheel){var anchor=World(e.mousePosition,rect);span=Mathf.Clamp(span*Mathf.Pow(1.15f,e.delta.y),500,24000);var next=World(e.mousePosition,rect);centre+=new Vector2(anchor.x-next.x,anchor.z-next.z);e.Use();}
                if(e.type==EventType.MouseDown&&e.button==0){dragging=true;moved=false;dragStart=lastMouse=e.mousePosition;e.Use();}
            }
            if(dragging&&e.type==EventType.MouseDrag){var delta=e.mousePosition-lastMouse;centre+=new Vector2(-delta.x,delta.y)*span/rect.width;lastMouse=e.mousePosition;moved|=(e.mousePosition-dragStart).sqrMagnitude>25;e.Use();}
            if(dragging&&e.type==EventType.MouseUp&&e.button==0){dragging=false;if(!moved&&rect.Contains(e.mousePosition)){chosen=World(e.mousePosition,rect);Request(session,chosen.Value,false);}e.Use();}
            var region=session.World.Route.Region;
            if(painting!=null&&painting.IsCompleted)
            {
                if(!painting.IsFaulted){var result=painting.Result;if(!texture)texture=new Texture2D(768,768,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,name="Pannable navigation map"};texture.SetPixels32(result.pixels);texture.Apply(false);imageCentre=result.centre;imageSpan=result.span;imageRoads=result.roads;}
                painting=null;
            }
            if(painting==null&&(!texture||imageRoads!=region.roads||Mathf.Abs(imageSpan-span)>.1f||Vector2.Distance(imageCentre,centre)>span/768))
            {
                var roads=region.roads;var water=region.waters;var at=centre;float width=span;var buffer=pixels??(pixels=new Color32[768*768]);
                painting=Task.Run(()=>new Raster{pixels=Paint(roads,water,at,width,buffer),centre=at,span=width,roads=roads});
            }
            GUI.BeginGroup(rect);var local=new Rect(0,0,rect.width,rect.height);
            var old=GUI.color;GUI.color=new Color(.08f,.13f,.11f);GUI.DrawTexture(local,Texture2D.whiteTexture);GUI.color=old;
            if(texture){float w=span/imageSpan,h=w*rect.height/rect.width;var uv=.5f*Vector2.one+(centre-imageCentre)/imageSpan;var wanted=new Rect(uv.x-w*.5f,uv.y-h*.5f,w,h);float x0=Mathf.Max(0,wanted.xMin),x1=Mathf.Min(1,wanted.xMax),y0=Mathf.Max(0,wanted.yMin),y1=Mathf.Min(1,wanted.yMax);
                if(x1>x0&&y1>y0)GUI.DrawTextureWithTexCoords(new Rect((x0-wanted.xMin)/w*local.width,(wanted.yMax-y1)/h*local.height,(x1-x0)/w*local.width,(y1-y0)/h*local.height),texture,new Rect(x0,y0,x1-x0,y1-y0));}
            atlas.Window(region,centre,span,local);atlas.Draw();
            // Textures use a group; rotated vector strokes must not. Unity's
            // group clip offset rotates with a modified GUI matrix on scaled
            // displays. Draw in the parent space with explicit segment clips.
            GUI.EndGroup();
            if(e.type==EventType.Repaint)
            {
                Vector2 Project(Vector3 p)=>rect.position+Pixel(p,rect);
                RouteLines(Current,Project,new Color(.25f,.9f,.9f),rect);RouteLines(preview,Project,Color.yellow,rect);
                var actual=Project(session.Car.Body.position);var car=new Vector2(Mathf.Clamp(actual.x,rect.x+22,rect.xMax-22),Mathf.Clamp(actual.y,rect.y+22,rect.yMax-22));
                Marker(car,Color.black,36);Marker(car,Color.white,30);Marker(car,new Color(.05f,.4f,1),24);Arrow(car,session.Car.transform.eulerAngles.y,Color.white);
                Badge(new Rect(Mathf.Clamp(car.x+20,rect.x+2,rect.xMax-152),Mathf.Clamp(car.y-15,rect.y+2,rect.yMax-34),150,32),rect.Contains(actual)?"YOU":"YOU · OFF MAP",small);
                Vector3? endpoint=chosen.HasValue?(preview!=null?preview.points[preview.points.Length-1]:chosen.Value):destination;
                if(endpoint.HasValue){var end=Project(endpoint.Value);if(rect.Contains(end))Marker(new Vector2(Mathf.Clamp(end.x,rect.x+6,rect.xMax-6),Mathf.Clamp(end.y,rect.y+6,rect.yMax-6)),Color.yellow,11);}
            }
            if (ControllerMenuGUI.Active?.Focused?.Kind == ControllerMenuNavigation.ControlKind.Map)
            {
                var cursor = rect.center;
                Line(cursor-Vector2.right*13,cursor+Vector2.right*13,Color.black,5);
                Line(cursor-Vector2.up*13,cursor+Vector2.up*13,Color.black,5);
                Line(cursor-Vector2.right*11,cursor+Vector2.right*11,Color.white,2);
                Line(cursor-Vector2.up*11,cursor+Vector2.up*11,Color.white,2);
            }
            Badge(new Rect(rect.x+10,rect.yMax-28,610,26),"© OpenStreetMap contributors · openstreetmap.org/copyright",small);
            GUI.Label(new Rect(55,751,1280,32),status,small);
            bool enabled=GUI.enabled;GUI.enabled=enabled&&preview!=null&&!requestPending&&planning==null;
            if(ControllerMenuGUI.Button(new Rect(55,800,270,42),"START ROUTE AND DRIVE",button)){Current=preview;destination=preview.points[preview.points.Length-1];retryAt=Time.realtimeSinceStartupAsDouble+5;CloseMap(session);}
            GUI.enabled=enabled;
            if(ControllerMenuGUI.Button(new Rect(345,800,240,42),"CANCEL / RETURN",button))CloseMap(session);
            if(Current!=null&&ControllerMenuGUI.Button(new Rect(605,800,240,42),"CANCEL ROUTE",button))CancelRoute();
            if(ControllerMenuGUI.Button(new Rect(865,800,180,42),"CENTRE ON CAR",button)){var car=session.Car.Body.position;centre=new Vector2(car.x,car.z);}
            if((preview!=null||Current!=null)&&ControllerMenuGUI.Button(new Rect(1060,800,155,42),"SHOW ROUTE",button))FitRoute(preview??Current);
            GUI.Label(new Rect(1225,803,155,42),Distance(span)+" · N ↑",small);GUI.color=incoming;
        }
        public void DrawMini(Rect rect,GameSession session)
        {
            if(small==null){small=new GUIStyle(GUI.skin.label){font=InterfaceTheme.Font,fontSize=14,wordWrap=true};small.normal.textColor=InterfaceTheme.Paper;}
            if(Current==null){if(Event.current.type==EventType.Repaint)MiniCar(rect);return;}var p=session.Car.Body.position;float a=session.Car.transform.eulerAngles.y*Mathf.Deg2Rad;float sin=Mathf.Sin(a),cos=Mathf.Cos(a);
            Vector2 Project(Vector3 v){float dx=v.x-p.x,dz=v.z-p.z;return new Vector2(rect.center.x+(dx*cos-dz*sin)*rect.width/600,rect.center.y-(dx*sin+dz*cos)*rect.width/600);}
            if(Event.current.type==EventType.Repaint){RouteLines(Current,Project,InterfaceTheme.Gold,rect);var end=Project(Current.points[Current.points.Length-1]);if(rect.Contains(end))Marker(new Vector2(Mathf.Clamp(end.x,rect.x+6,rect.xMax-6),Mathf.Clamp(end.y,rect.y+6,rect.yMax-6)),Color.yellow);MiniCar(rect);}
            float progress=Current.Closest(p,out float separation);float remaining=Mathf.Max(0,Current.Length-progress);string text="Continue · "+Distance(remaining)+" remaining";float direction=0;
            var aim=Current.At(progress+25)-p;direction=Mathf.DeltaAngle(session.Car.transform.eulerAngles.y,Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg);float routeBearing=direction;
            foreach(var turn in Current.turns)if(turn.station>progress+5){text=(turn.angle>0?"Turn right":"Turn left")+" · "+Distance(turn.station-progress);direction=turn.angle>0?90:-90;break;}
            if(Mathf.Abs(routeBearing)>120){text="Turn around when safe";direction=180;}
            if(remaining<20&&separation<20){text="Destination reached";direction=0;}
            else if(separation>35)text="Rejoining route…";
            if(Event.current.type==EventType.Repaint){var old=GUI.color;GUI.color=new Color(.02f,.04f,.06f,.95f);InterfaceTheme.Card(new Rect(rect.xMax+18,rect.y+4,240,114),.94f);GUI.color=old;Arrow(new Vector2(rect.xMax+48,rect.y+36),direction,InterfaceTheme.Gold);}
            GUI.Label(new Rect(rect.xMax+35,rect.y+62,205,50),text,small??GUI.skin.label);
        }
        static Color32[] Paint(GeoRoad[] roads,GeoWater[] waters,Vector2 centre,float span,Color32[] pixels)
        {
            const int size=768;for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(22,35,31,255);
            void Segment(Vector3 a,Vector3 b,Color32 colour)
            {
                float ax=(a.x-centre.x)/span*size+size*.5f,ay=(a.z-centre.y)/span*size+size*.5f;
                float dx=(b.x-a.x)/span*size,dy=(b.z-a.z)/span*size,lo=0,hi=1;
                bool Clip(float dir,float dist){if(Mathf.Abs(dir)<.000001f)return dist>=0;float t=dist/dir;if(dir<0){if(t>hi)return false;lo=Mathf.Max(lo,t);}else{if(t<lo)return false;hi=Mathf.Min(hi,t);}return true;}
                if(!Clip(-dx,ax)||!Clip(dx,size-1-ax)||!Clip(-dy,ay)||!Clip(dy,size-1-ay))return;
                int steps=Mathf.CeilToInt(Mathf.Max(Mathf.Abs(dx),Mathf.Abs(dy))*(hi-lo));for(int i=0;i<=steps;i++){float t=Mathf.Lerp(lo,hi,steps==0?0:(float)i/steps);int x=Mathf.Clamp(Mathf.RoundToInt(ax+dx*t),0,size-1),y=Mathf.Clamp(Mathf.RoundToInt(ay+dy*t),0,size-1);pixels[y*size+x]=colour;}
            }
            foreach(var w in waters??Array.Empty<GeoWater>())for(int i=1;i<w.points.Length;i++)Segment(w.points[i-1],w.points[i],new Color32(70,150,190,255));
            foreach(var r in roads??Array.Empty<GeoRoad>())for(int i=1;i<r.points.Length;i++)Segment(r.points[i-1],r.points[i],new Color32(215,222,215,255));
            return pixels;
        }
        public void Dispose(){IsOpen=false;requestPending=false;revision++;atlas?.Dispose();if(texture)UnityEngine.Object.Destroy(texture);}
    }
}
