using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Isolated opt-in native acceptance fixture, not a gameplay mode or live-train feed.
    public sealed class RailwayNativeProof : MonoBehaviour
    {
        const string Flag="-earthUnscriptedRailProof";
        static string prefix,source,sourceHash,realSave,realHash;static int earlyCount;
        static readonly List<string> early=new List<string>();
        readonly List<string> checks=new List<string>(),errors=new List<string>();readonly HashSet<string> checkedOnce=new HashSet<string>();
        GameSession session;RailwayProofContacts contactProbe;RaceDriver driver;RoutePath corridor;GeoRegion region;
        bool done,driving;double deadline;int errorCount,recoveries,criticalFailures,criticalSamples;float travelled,previousSpeed;int discontinuities;Vector3 previous;
        readonly List<Crossing> crossings=new List<Crossing>();readonly List<CapturePose> capturePoses=new List<CapturePose>();
        [Serializable] public sealed class CapturePose {public string name;public Vector3 position;public Quaternion rotation;public float fov;public int width,height;}
        [Serializable] public sealed class Crossing
        {public string railId,roadId;public Vector3 position;public float railHead,roadY,postedHeight,requiredClearance,minimumMeasuredClearance=1000,routeDistance;public int laneSamples,underCarSamples;public float minimumCollisionEnvelopeGap=1000,minimumVisibleRoofGap=1000;public string undersideCollider;public int undersideTriangle;public bool estimatedProfile;}
        [Serializable] sealed class Report
        {public string status,error,version,sourceHash,mapId,epoch,limitations;public bool detailedScenery,realCareerUnchanged,sourceUnchanged,careerPreserved;public float travelled,damage,cleanMetres;public int earnedDriveCredits,finalCredits;public int recoveries,discontinuities,criticalSamples,criticalFailures,runtimeErrors,weatherRequests,updateRequests,railways,railMeshes,railColliders,barrierMeshes,width,height;public string[] checks,firstErrors,contacts;public Crossing[] crossings;public CapturePose[] cameras;}
        readonly Report report=new Report{status="FAIL"};
        static string Arg(string[] args,string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new ArgumentException("Missing "+key);return args[i+1];}
        static void Early(string text,string stack,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;earlyCount++;if(early.Count<16)early.Add(text);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Prepare()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,Flag)<0)return;
            Application.logMessageReceived+=Early;Application.runInBackground=true;
            prefix=Path.GetFullPath(Arg(args,Flag));source=Path.GetFullPath(Arg(args,"-earthUnscriptedRailWorld"));Directory.CreateDirectory(Path.GetDirectoryName(prefix));
            if(File.Exists(prefix+"-profile.json")||Directory.Exists(prefix+"-profile.json.regions"))throw new InvalidOperationException("Fresh rail proof prefix required");
            sourceHash=Core.DataPackageRules.HashFile(source);realSave=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=File.Exists(realSave)?Core.DataPackageRules.HashFile(realSave):null;
            PlayerProfile.StorageOverride=prefix+"-profile.json";Directory.CreateDirectory(RegionCache.Folder);
            var world=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(source));world.id="download-rail-native-proof";world.networkMapId="";File.WriteAllText(RegionCache.PathFor(world.id),JsonUtility.ToJson(world));
            var garage=new Core.GarageState{activeId="rail-owned",cars=new List<Core.OwnedCar>{new Core.OwnedCar{id="rail-owned",model=0,engine=1,tyres=1,paint=4,wheels=2,damage=0},new Core.OwnedCar{id="rail-spare",model=2,paint=3,damage=17}}};
            var profile=new PlayerProfile{region=world.id,garage=garage,credits=4321,detailedScenery=false,livingScenery=false,quality=1,clarity=1,weatherMode=0,clockMode=0,fixedHour=14,volume=.23f,rumble=.31f,wheelName="Rail proof wheel",steerPath="/Gamepad/leftStick/x",steerLeft=-.81f,steerCentre=.04f,steerRight=.84f};
            if(!profile.Save())throw new IOException("Cannot initialize isolated rail career");
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch(){if(prefix!=null){var go=new GameObject("Isolated physical railway underpass proof");DontDestroyOnLoad(go);go.AddComponent<RailwayNativeProof>();}}
        void OnEnable(){Application.logMessageReceived-=Early;errorCount=earlyCount;errors.AddRange(early);Application.logMessageReceived+=Log;deadline=Time.realtimeSinceStartupAsDouble+360;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string text,string stack,LogType kind){if(kind!=LogType.Error&&kind!=LogType.Exception&&kind!=LogType.Assert)return;errorCount++;if(errors.Count<16)errors.Add(text.Substring(0,Math.Min(1600,text.Length)));}
        void Update(){if(!done&&Time.realtimeSinceStartupAsDouble>deadline)Finish("360-second real-time watchdog expired");}
        void Check(bool yes,string why){if(!yes)throw new InvalidOperationException(why);if(checkedOnce.Add(why)&&checks.Count<256)checks.Add(why);}
        IEnumerator Start()
        {var run=Run();while(!done){bool moved=false;object current=null;Exception failure=null;try{moved=run.MoveNext();if(moved)current=run.Current;}catch(Exception e){failure=e;}if(failure!=null){Finish(failure.ToString());yield break;}if(!moved)yield break;yield return current;}}
        static bool Intersection(Vector3 a,Vector3 b,Vector3 c,Vector3 d,out float s,out float t)
        {var u=new Vector2(b.x-a.x,b.z-a.z);var v=new Vector2(d.x-c.x,d.z-c.z);var w=new Vector2(c.x-a.x,c.z-a.z);float cross=u.x*v.y-u.y*v.x;s=t=0;if(Mathf.Abs(cross)<.00001f)return false;s=(w.x*v.y-w.y*v.x)/cross;t=(w.x*u.y-w.y*u.x)/cross;return s>=0&&s<=1&&t>=0&&t<=1;}
        void FindCrossings()
        {
            var roads=region.roads.Where(r=>r.id!=null&&r.id.StartsWith("1109998597-",StringComparison.Ordinal)).ToArray();
            foreach(string wanted in new[]{"railway-256150001","railway-1108948870"})
            {
                Crossing found=null;
                foreach(var rail in region.railways.Where(r=>r.id.StartsWith(wanted,StringComparison.Ordinal)))for(int i=1;i<rail.points.Length;i++)foreach(var road in roads)for(int j=1;j<road.points.Length;j++)
                {
                    if(!Intersection(rail.points[i-1],rail.points[i],road.points[j-1],road.points[j],out float s,out float t))continue;
                    var p=Vector3.Lerp(road.points[j-1],road.points[j],t);p.y=region.RoadSurfaceHeight(p.x,p.z);float head=Vector3.Lerp(rail.points[i-1],rail.points[i],s).y;
                    found=new Crossing{railId=rail.id,roadId=road.id,position=p,roadY=p.y,railHead=head,postedHeight=road.maxVehicleHeight,requiredClearance=road.maxVehicleHeight>0?Mathf.Max(3,road.maxVehicleHeight+.25f):RailwayMetrics.MinimumRoadClearance,estimatedProfile=rail.estimatedProfile};break;
                }
                Check(found!=null,"actual Bridge Street crossing present for "+wanted);Check(found.estimatedProfile&&Mathf.Abs(found.postedHeight-4.0386f)<.002f,"posted 13ft3 restriction remains separate from estimated bridge profile");Check(crossings.All(c=>Vector2.Distance(new Vector2(c.position.x,c.position.z),new Vector2(found.position.x,found.position.z))>.25f),"two source ways have distinct independently testable crossing positions");crossings.Add(found);
            }
        }
        RoutePath Corridor()
        {
            var seed=region.roads.Single(r=>r.id==crossings[0].roadId);var list=seed.points.ToList();var used=new HashSet<GeoRoad>{seed};
            void Extend(bool front)
            {
                float added=0;for(int iteration=0;iteration<256&&added<140;iteration++)
                {
                    var at=front?list[0]:list[list.Count-1];var away=(front?list[0]-list[1]:list[list.Count-1]-list[list.Count-2]).normalized;
                    GeoRoad next=null;bool reverse=false;float score=float.NegativeInfinity;
                    foreach(var road in region.roads)
                    {
                        if(used.Contains(road)||road.bridge||road.tunnel||road.points.Length<2)continue;
                        bool rev=Vector3.Distance(road.points[road.points.Length-1],at)<.15f;if(!rev&&Vector3.Distance(road.points[0],at)>=.15f)continue;
                        var direction=(rev?road.points[road.points.Length-2]-at:road.points[1]-at).normalized;float straight=Vector3.Dot(away,direction);if(straight<-.15f)continue;
                        float value=straight+(road.name==seed.name ? .8f : 0);if(value>score){score=value;next=road;reverse=rev;}
                    }
                    if(next==null)break;used.Add(next);var ordered=(Vector3[])next.points.Clone();if(reverse)Array.Reverse(ordered);for(int i=1;i<ordered.Length;i++){added+=Vector3.Distance(ordered[i-1],ordered[i]);if(front)list.Insert(0,ordered[i]);else list.Add(ordered[i]);}
                }
            }
            Extend(true);Extend(false);var clone=region.SnapshotCopy();clone.route=list.ToArray();var whole=new RoutePath(clone);float centre=whole.Closest(crossings[0].position,out _,out _);float second=whole.Closest(crossings[1].position,out _,out _);float low=Mathf.Max(0,Mathf.Min(centre,second)-105),high=Mathf.Min(whole.Length-.01f,Mathf.Max(centre,second)+120);
            Check(high-low>180&&Mathf.Min(centre,second)-low>75&&high-Mathf.Max(centre,second)>75,"connected straight-preferred road corridor brackets both bridges with physical approach and departure");
            var cropped=new List<Vector3>{whole.At(low)};for(int i=1;i<whole.Points.Length-1;i++){float d=whole.DistanceAtPoint(i);if(d>low&&d<high)cropped.Add(whole.Points[i]);}cropped.Add(whole.At(high));clone.route=cropped.ToArray();var route=new RoutePath(clone);
            foreach(var crossing in crossings){crossing.routeDistance=route.Closest(crossing.position,out _,out float separation);Check(separation<.2f,"proof route physically intersects "+crossing.railId);}return route;
        }
        MeshCollider Underside(Vector3 from,out RaycastHit hit)
        {
            hit=default;MeshCollider best=null;float distance=float.PositiveInfinity;
            foreach(var collider in session.World.GetComponentsInChildren<MeshCollider>())if(collider.name.StartsWith("Railway closed bridge deck",StringComparison.Ordinal)&&collider.Raycast(new Ray(from,Vector3.up),out var candidate,30)&&candidate.distance<distance){best=collider;hit=candidate;distance=candidate.distance;}
            return best;
        }
        void InspectGeometry()
        {
            Physics.SyncTransforms();var meshes=session.World.GetComponentsInChildren<MeshFilter>();report.railMeshes=meshes.Count(m=>m.name.StartsWith("Railway ",StringComparison.Ordinal));report.barrierMeshes=meshes.Count(m=>m.name.StartsWith("Railway bridge barriers",StringComparison.Ordinal));report.railColliders=session.World.GetComponentsInChildren<MeshCollider>().Count(c=>c.name.StartsWith("Railway ",StringComparison.Ordinal));
            Check(report.railMeshes>0&&report.railColliders>0&&report.barrierMeshes>0,"standard world has physical closed railway decks and barriers without optional scenery");
            foreach(var crossing in crossings)
            {
                var road=region.roads.Single(r=>r.id==crossing.roadId);var forward=(road.points.Last()-road.points[0]).normalized;var side=Vector3.Cross(Vector3.up,forward).normalized;
                for(int sample=0,sampleCount=Mathf.CeilToInt((road.width-.3f)/.4f);sample<=sampleCount;sample++)
                {
                    float lateral=Mathf.Lerp(-road.width*.5f+.15f,road.width*.5f-.15f,sample/(float)sampleCount);
                    var p=crossing.position+side*lateral;p.y=region.RoadSurfaceHeight(p.x,p.z);Check(Underside(p+Vector3.up*.08f,out var under),"upward collider ray reaches actual bridge underside across carriageway");var owner=Underside(p+Vector3.up*.08f,out under);Check(owner.Raycast(new Ray(under.point+Vector3.up*3,Vector3.down),out var deckTop,5)&&deckTop.point.y-under.point.y>.70f,"closed physical deck has positive depth and an outward-facing top");crossing.undersideCollider=owner.name;crossing.undersideTriangle=under.triangleIndex;float clearance=under.point.y-p.y;crossing.minimumMeasuredClearance=Mathf.Min(crossing.minimumMeasuredClearance,clearance);crossing.laneSamples++;
                    Check(clearance+.02f>=crossing.requiredClearance&&Mathf.Abs(under.point.y-(crossing.railHead-RailwayMetrics.UnderDeckDrop))<.12f,"measured closed underside matches prepared railhead and posted-clearance policy");
                    var sweepCentre=p+Vector3.up*1.05f;var sweepHalf=new Vector3(.21f,.94f,.10f);var sweepRotation=Quaternion.LookRotation(new Vector3(forward.x,0,forward.z),Vector3.up);
                    foreach(var obstacle in Physics.OverlapBox(sweepCentre,sweepHalf,sweepRotation,~0,QueryTriggerInteraction.Ignore))if(obstacle is MeshCollider&&obstacle.name.StartsWith("Railway ",StringComparison.Ordinal))Check(false,"railway obstacle overlaps complete low-to-roof carriageway envelope");
                    foreach(var obstacle in Physics.BoxCastAll(sweepCentre-new Vector3(forward.x,0,forward.z).normalized*4,sweepHalf,new Vector3(forward.x,0,forward.z).normalized,sweepRotation,8,~0,QueryTriggerInteraction.Ignore))if(obstacle.collider is MeshCollider&&obstacle.collider.name.StartsWith("Railway ",StringComparison.Ordinal))Check(false,"railway obstacle blocks full-height swept carriageway cell");
                    var origin=p-forward*4+Vector3.up*1.2f;foreach(var c in session.World.GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Railway closed bridge deck",StringComparison.Ordinal)))Check(!c.Raycast(new Ray(origin,forward),out _,8),"no bridge support collider occupies underpass swept carriageway");
                }
            }
        }
        void FixedUpdate()
        {
            if(!driving||done||!session||Time.timeScale==0)return;var position=session.Car.Body.position;float movement=Vector3.Distance(position,previous);if(movement>Mathf.Max(previousSpeed,session.Car.AbsoluteSpeed)*Time.fixedDeltaTime+1)discontinuities++;travelled+=movement;previous=position;previousSpeed=session.Car.AbsoluteSpeed;
            for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){criticalSamples++;if(!session.World.GeographicWorld.HasSurface(position+new Vector3(x*320,0,z*320)))criticalFailures++;}
            foreach(var crossing in crossings)if(Vector2.Distance(new Vector2(position.x,position.z),new Vector2(crossing.position.x,crossing.position.z))<5&&Underside(position+Vector3.up*.3f,out var underside)){crossing.underCarSamples++;float roof=session.Car.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger).Select(c=>c.bounds.max.y).DefaultIfEmpty(position.y+1.5f).Max();crossing.minimumCollisionEnvelopeGap=Mathf.Min(crossing.minimumCollisionEnvelopeGap,underside.point.y-roof);float visibleRoof=session.Car.Visual.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).Select(r=>r.bounds.max.y).DefaultIfEmpty(float.PositiveInfinity).Max();crossing.minimumVisibleRoofGap=Mathf.Min(crossing.minimumVisibleRoofGap,underside.point.y-visibleRoof);}
        }
        void Pose(string name){var c=session.View.GetComponent<Camera>();capturePoses.Add(new CapturePose{name=name,position=c.transform.position,rotation=c.transform.rotation,fov=c.fieldOfView,width=Screen.width,height=Screen.height});}
        IEnumerator Capture(string name,int mode)
        {session.View.Mode=mode;yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();Pose(name);ScreenCapture.CaptureScreenshot(prefix+"-"+name+".png");yield return new WaitForEndOfFrame();}
        IEnumerator Run()
        {
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady||!session.Car||!session.Conditions)yield return null;
            report.version=Application.version;report.sourceHash=sourceHash;region=session.World.Route.Region;Check(region!=null&&region.railwayProfilesPrepared&&!session.Profile.detailedScenery,"ordinary world finalizes railway profiles with optional detail disabled");report.detailedScenery=session.Profile.detailedScenery;report.railways=region.railways.Length;report.mapId=WorldSnapshotCodec.NetworkId(region);report.epoch=WorldSnapshotCodec.Epoch(region);File.WriteAllText(prefix+"-canonical.json",WorldSnapshotCodec.CanonicalJson(region));
            foreach(var traffic in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))traffic.gameObject.SetActive(false);foreach(var police in FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None))police.enabled=false;session.Racing.Cancel();
            FindCrossings();corridor=Corridor();var corridorWorld=region.SnapshotCopy();corridorWorld.route=corridor.Points;File.WriteAllText(prefix+"-corridor-world.json",WorldSnapshotCodec.CanonicalJson(corridorWorld));// Open proof corridor has no physical wrap seam. Start inside it so the
            // centred tangent does not look backwards from its artificial endpoint.
            session.Car.Recover(corridor.At(3,corridor.LaneOffset(3)),corridor.Rotation(3));session.World.GeographicWorld.Refresh(session.Car.Body.position,true);yield return session.World.GeographicWorld.PrepareDriveFrontier(session.Car.Body.position,null);Check(session.World.GeographicWorld.DriveFrontierReady,"asynchronous drive frontier ready before start");
            // Ensure actual crossings are resident for full-width collider inspection, without moving the car through them.
            session.World.GeographicWorld.Refresh(crossings[0].position,true);InspectGeometry();session.World.GeographicWorld.Refresh(session.Car.Body.position,true);
            contactProbe=session.Car.gameObject.AddComponent<RailwayProofContacts>();driver=session.Car.gameObject.AddComponent<RaceDriver>();driver.Car=session.Car;driver.Session=session;driver.Route=corridor;driver.Fleet=new List<VehicleController>{session.Car};driver.Difficulty=0;driver.RoadLane=true;driver.Recovered+=_=>recoveries++;session.Car.NetworkDriven=true;previous=session.Car.Body.position;session.SetMenu(false);driving=true;
            bool chase=false,cockpit=false;float passed=crossings.Max(c=>c.routeDistance);double end=Time.realtimeSinceStartupAsDouble+100;
            while(Time.realtimeSinceStartupAsDouble<end)
            {
                float d=corridor.Closest(session.Car.Body.position,out _,out float separation);Check(separation<8,"actual car remains within connected proof road corridor");
                if(!chase&&d>crossings.Min(c=>c.routeDistance)-30){chase=true;yield return Capture("chase",0);}
                if(!cockpit&&d>crossings.Min(c=>c.routeDistance)-15){cockpit=true;yield return Capture("cockpit",2);}
                if(d>passed+65&&travelled>150)break;Check(session.Car.Damage<.001f&&recoveries==0&&discontinuities==0,"physical underpass traversal has no damage or recovery teleport");yield return new WaitForEndOfFrame();
            }
            driving=false;driver.enabled=false;session.SetMenu(true);Check(travelled>150&&corridor.Closest(session.Car.Body.position,out _,out _)>passed+65&&crossings.All(c=>c.underCarSamples>0&&c.minimumCollisionEnvelopeGap>0&&c.minimumVisibleRoofGap>0),"car physically travelled through both actual deck colliders and beyond them");Check(criticalSamples>0&&criticalFailures==0&&recoveries==0&&discontinuities==0&&session.Car.Damage<.001f,"physical drive has zero missing critical surfaces damage or recoveries");
            session.SetMenu(false);Time.timeScale=0;var camera=session.View.GetComponent<Camera>();session.View.enabled=false;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;camera.transform.position=crossings[0].position+new Vector3(-16,7,-20);camera.transform.LookAt(crossings[0].position+Vector3.up*2);yield return new WaitForEndOfFrame();Pose("oblique-inspection");ScreenCapture.CaptureScreenshot(prefix+"-oblique-inspection.png");yield return new WaitForEndOfFrame();camera.transform.SetPositionAndRotation(oldPosition,oldRotation);session.View.enabled=true;
            Check(report.epoch==WorldSnapshotCodec.Epoch(region),"canonical authoritative railway world remains unchanged throughout physical proof");var p=session.Profile;report.cleanMetres=session.Economy.CleanMetres;report.earnedDriveCredits=30*Mathf.FloorToInt(report.cleanMetres/100);report.finalCredits=p.credits;Check(session.Drifts.Total==0&&p.credits==4321+report.earnedDriveCredits&&p.credits==session.Economy.Credits,"isolated credits equal starting balance plus exact clean-driving rewards");report.careerPreserved=p.credits==4321+report.earnedDriveCredits&&p.garage.cars.Count==2&&p.garage.Active.id=="rail-owned"&&p.garage.Active.engine==1&&p.garage.Active.tyres==1&&p.garage.Active.paint==4&&p.garage.Active.wheels==2&&p.garage.cars.Single(c=>c.id=="rail-spare").damage==17&&p.quality==1&&p.clarity==1&&p.steerPath=="/Gamepad/leftStick/x"&&Mathf.Abs(p.volume-.23f)<.001f&&Mathf.Abs(p.rumble-.31f)<.001f;Check(report.careerPreserved,"isolated career credits cars upgrades damage graphics and controls preserved");Check(session.Conditions.WeatherRequests==0&&session.Updates.SourceRequests==0,"no weather or country-update provider requests");Check(errorCount==0,"no native runtime shader or assertion errors");Finish();
        }
        void Finish(string error=null)
        {
            if(done)return;done=true;driving=false;report.status=error==null&&errorCount==0?"PASS":"FAIL";report.error=error;report.runtimeErrors=errorCount;report.firstErrors=errors.ToArray();report.checks=checks.Take(4096).ToArray();report.crossings=crossings.ToArray();report.cameras=capturePoses.ToArray();report.contacts=contactProbe?contactProbe.Contacts.ToArray():Array.Empty<string>();report.travelled=travelled;report.recoveries=recoveries;report.discontinuities=discontinuities;report.criticalSamples=criticalSamples;report.criticalFailures=criticalFailures;report.width=Screen.width;report.height=Screen.height;
            report.realCareerUnchanged=(File.Exists(realSave)?Core.DataPackageRules.HashFile(realSave):null)==realHash;report.sourceUnchanged=File.Exists(source)&&Core.DataPackageRules.HashFile(source)==sourceHash;if(session){report.damage=session.Car.Damage;report.weatherRequests=session.Conditions.WeatherRequests;report.updateRequests=session.Updates.SourceRequests;}if(!report.realCareerUnchanged||!report.sourceUnchanged)report.status="FAIL";
            report.limitations="Isolated native physical default-car passage on two source railway bridges only; bounds sampled in .4m-wide contiguous overlapping full-height sweep cells, plus actual physical car passage; no live trains, surveyed soffit claim, national database byte-identity or general bridge clearance acceptance. Proof reads an already prepared local JSON and never opens a country database. Current provider counters cover weather/country updates. Actual chase/cockpit/oblique PNGs require human visual inspection. Initial single Recover places car on approach before measured driving; any assist recovery during traversal fails.";
            try{File.WriteAllText(prefix+".json",JsonUtility.ToJson(report,true));}finally{Application.Quit(report.status=="PASS"?0:1);}
        }
    }
    public sealed class RailwayProofContacts : MonoBehaviour
    {
        public readonly List<string> Contacts=new List<string>();
        void OnCollisionEnter(Collision collision){if(Contacts.Count<32)Contacts.Add(Time.frameCount+"|"+collision.collider.name+"|"+collision.relativeVelocity.magnitude.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
    }

}
