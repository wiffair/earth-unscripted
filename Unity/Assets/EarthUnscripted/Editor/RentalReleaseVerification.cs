using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EarthUnscripted.Core;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    /// <summary>Generated private profiles and fresh owned numeric preparation only.
    /// Never creates a scene, actor, collider, rental, reward or real-profile write.</summary>
    public static class RentalReleaseVerification
    {
        // Editor is a separate assembly. Query the existing internal numeric API
        // explicitly without widening production visibility for a verification helper.
        static readonly System.Reflection.MethodInfo HeightMethod=typeof(GeoRegion).GetMethod("RoadSurfaceHeightForRoad",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        static float SurfaceHeight(GeoRegion region,GeoRoad road,float x,float z)=>(float)HeightMethod.Invoke(region,new object[]{road,x,z});
        const string OutputFlag="-earthUnscriptedRentalReleaseChecksOutput";
        public static void Run()
        {
            int exit=1;
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,OutputFlag);
                if(at<0||at+1>=args.Length||args.Count(a=>a==OutputFlag)!=1)
                    throw new ArgumentException("One explicit fresh private output argument is required");
                exit=Verify(args[at+1]);
            }
            catch(Exception error){Debug.LogException(error);}
            finally{EditorApplication.Exit(exit);}
        }
        static string PrivateOutput(string requested)
        {
            if(string.IsNullOrEmpty(requested)||!Path.IsPathRooted(requested))throw new ArgumentException("Absolute private output required");
            string full=Path.GetFullPath(requested);
            if(full!=requested||Directory.Exists(full)||File.Exists(full))throw new IOException("Fresh canonical output required");
#if UNITY_EDITOR_WIN
            string prefix=Path.Combine(Path.GetTempPath(),"rr020-rental-release-checks-");
            var comparison=StringComparison.OrdinalIgnoreCase;
#else
            string prefix="/private/tmp/rr020-rental-release-checks-";
            var comparison=StringComparison.Ordinal;
#endif
            if(!full.StartsWith(prefix,comparison)||full.Substring(prefix.Length).Length<1
                ||full.Substring(prefix.Length).Length>96||full.Substring(prefix.Length).Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))
                throw new ArgumentException("Output must be a fresh rr020-rental-release-checks-<suffix> temp child");
            for(string item=full;item!=null;item=Path.GetDirectoryName(item))
            {
                try{if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked output ancestor refused");}
                catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
            }
            return full;
        }
        static string Sha(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static void Fresh(string path,string text)
        {using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var b=new UTF8Encoding(false,true).GetBytes(text);file.Write(b,0,b.Length);file.Flush(true);}}
        static JObject Json(PlayerProfile profile)=>JObject.Parse(JsonUtility.ToJson(profile));
        // Compare all serialized fields, not a hand-picked mirror of individual settings.
        static string LegacyView(JObject value,bool slotMetadata=false)
        {
            var copy=(JObject)value.DeepClone();copy.Remove("version");copy.Remove("modelLayoutBests");
            if(slotMetadata){copy.Remove("careerName");copy.Remove("careerSavedUtc");}
            return copy.ToString(Formatting.None);
        }
        static PlayerProfile Populated()
        {
            var p=new PlayerProfile{version=6,credits=123456,region="silverstone",regionTimeZone="Europe/London",
                trackMode=2,raceLaps=3,raceDifficulty=2,volume=.37f,rumble=.55f,wheelStrength=.4f,
                wheelEnabled=true,wheelForceEnabled=false,invertForce=true,steerPath="fixture-steering",gasPath="fixture-gas",brakePath="fixture-brake",wheelName="Generated fixture wheel",
                steerLeft=-.9f,steerCentre=.05f,steerRight=.95f,gasRest=.8f,gasFull=-.8f,brakeRest=.7f,brakeFull=-.7f,
                clockMode=1,weatherMode=2,fixedHour=17.25f,clockOffset=.75f,dayMinutes=33,quality=1,clarity=2,
                livingScenery=false,detailedScenery=true,customStart=false,startRegion="silverstone",startX=12.5f,startZ=-44.25f,
                bestLap=95.75f,careerName="Generated schema6 fixture",careerSavedUtc="2026-11-08T00:00:00Z"};
            var road=p.garage.Active;road.id="fixture-owned-road";road.engine=2;road.tyres=1;road.damage=14.25f;
            road.dents.Add(new SavedDent{x=.3f,y=.8f,z=-.7f,nx=1,ny=0,nz=0,severity=9.5f});
            var race=new OwnedCar{id="fixture-owned-single-seat",model=3,engine=2,gearing=1,tyres=2,brakes=1,suspension=3,differential=2,
                paint=4,wheels=2,bodyKit=1,finish=2,damage=7.5f};
            race.raceSetup.finalDrive=1.05f;race.raceSetup.brakeBias=.58f;race.raceSetup.spring=1.1f;race.raceSetup.differential=.6f;
            race.raceSetup.frontWing=.7f;race.raceSetup.rearWing=.8f;race.raceSetup.compound=RaceCompound.Hard;
            race.raceTyres.compound=RaceCompound.Hard;race.raceTyres.temperatureC=83.75f;race.raceTyres.wear=.21f;
            race.dents.Add(new SavedDent{x=-.25f,y=.6f,z=.9f,nx=0,ny=0,nz=1,severity=12});
            p.garage.cars.Add(race);p.garage.activeId=race.id;
            p.records.Add(new TrialRecord{key="legacy-model-setup-weather-rules2",track="Generated legacy track",car="Apex Single Seat",conditions="fixture-wet-night",utc="2026-11-07T00:00:00Z",lap=95.75f,peak=57.5f,sectors=new[]{30.25f,31.5f,34f}});
            p.rewardedEvents.Add("fixture-legacy-paid-1");p.rewardedEvents.Add("fixture-legacy-paid-2");return p;
        }
        static string LegacyText()
        {var value=Json(Populated());value.Remove("modelLayoutBests");return value.ToString(Formatting.Indented);}
        static bool SameFloat(float a,float b)=>BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b);
        static bool SamePoint(Vector3 a,Vector3 b)=>SameFloat(a.x,b.x)&&SameFloat(a.y,b.y)&&SameFloat(a.z,b.z);
        static void SameDrivingInputs(PreparedWorldData a,PreparedWorldData b)
        {
            void Need(bool ok){if(!ok)throw new InvalidDataException("Supposed cosmetic fixture changed actual driving arrays/query values");}
            Need(a.Route.Points.Length==b.Route.Points.Length);float arc=0;
            for(int i=0;i<a.Route.Points.Length;i++)
            {
                Need(SamePoint(a.Route.Points[i],b.Route.Points[i]));if(i>0)arc+=Vector3.Distance(a.Route.Points[i-1],a.Route.Points[i]);
                Need(SameFloat(a.Route.Width(arc),b.Route.Width(arc)));
                foreach(float side in new[]{-.5f,0,.5f})Need(SamePoint(a.Route.At(arc,side*a.Route.Width(arc)),b.Route.At(arc,side*b.Route.Width(arc))));
            }
            var ar=a.Region.roads.Where(r=>r.kind=="raceway").ToArray();var br=b.Region.roads.Where(r=>r.kind=="raceway").ToArray();Need(ar.Length==br.Length);
            for(int r=0;r<ar.Length;r++)
            {
                Need(ar[r].points.Length==br[r].points.Length&&ar[r].bridge==br[r].bridge&&ar[r].tunnel==br[r].tunnel&&ar[r].layer==br[r].layer);arc=0;
                for(int i=0;i<ar[r].points.Length;i++)
                {
                    Need(SamePoint(ar[r].points[i],br[r].points[i]));if(i>0)arc+=Vector3.Distance(ar[r].points[i-1],ar[r].points[i]);
                    Need(SameFloat(a.Region.EffectiveWidth(ar[r],arc),b.Region.EffectiveWidth(br[r],arc)));
                    var p=ar[r].points[i];Need(SameFloat(SurfaceHeight(a.Region,ar[r],p.x,p.z),SurfaceHeight(b.Region,br[r],p.x,p.z)));
                }
            }
            Need((a.RoadSurfaces==null)==(b.RoadSurfaces==null));
            if(a.RoadSurfaces!=null)
            {
                Need(a.RoadSurfaces.Parts.Count==b.RoadSurfaces.Parts.Count);
                foreach(var pair in a.RoadSurfaces.Parts)
                {
                    Need(b.RoadSurfaces.Parts.ContainsKey(pair.Key));var left=pair.Value.Surface;var right=b.RoadSurfaces.Parts[pair.Key].Surface;
                    Need(left.Vertices.Length==right.Vertices.Length&&left.Triangles.SequenceEqual(right.Triangles));
                    for(int i=0;i<left.Vertices.Length;i++)Need(SamePoint(left.Vertices[i],right.Vertices[i]));
                }
            }
        }
        public static int Verify(string requested)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||Application.isPlaying)
                throw new InvalidOperationException("Data-only verification requires Edit mode with no player session");
            string output=PrivateOutput(requested);Directory.CreateDirectory(output);
            string previous=PlayerProfile.StorageOverride;var checks=new JArray();var identities=new JObject();string failure=null;
            void Check(bool pass,string name){if(!pass)throw new InvalidDataException(name);checks.Add(name);File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS "+name+"\n");}
            try
            {
                // Every production profile/career call is inside this private nonempty override.
                PlayerProfile.StorageOverride=Path.Combine(output,"generated-profile.json");string text=LegacyText();byte[] original=new UTF8Encoding(false,true).GetBytes(text);
                Fresh(PlayerProfile.StoragePath,text);var raw=JObject.Parse(text);var loaded=PlayerProfile.Load();
                Check(!loaded.LoadBlocked&&loaded.version==7,"Actual populated schema6 Load migrates to schema7");
                Check(LegacyView(Json(loaded))==LegacyView(raw),"ALL serialized legacy fields preserved on migration (records/car/condition/setup/tyres/settings/credits)");
                Check(loaded.records.Count==1&&loaded.rewardedEvents.Count==2&&loaded.modelLayoutBests.Count==0,"Legacy records/events nonempty; new model/layout records initially empty");
                string backup=PlayerProfile.StoragePath+".pre-lap-economy-v7.bak";
                Check(File.ReadAllBytes(backup).SequenceEqual(original)&&File.ReadAllBytes(PlayerProfile.StoragePath).SequenceEqual(original),"Migration backup byte-exact; Load leaves source file unchanged");
                var proposed=new ModelLayoutLapBest(new ModelLayoutLapKey(3,"physical-circuit1-generated-fixture"),91.25);
                loaded.modelLayoutBests.Add(ModelLayoutLapRecord.From(proposed));Check(loaded.Save(),"Actual migrated profile Save succeeds");
                byte[] schema7=File.ReadAllBytes(PlayerProfile.StoragePath);var roundtrip=PlayerProfile.Load();
                Check(!roundtrip.LoadBlocked&&LegacyView(Json(roundtrip))==LegacyView(raw),"Actual schema7 roundtrip retains every legacy field");
                Check(roundtrip.modelLayoutBests.Count==1&&roundtrip.modelLayoutBests[0].Read().Key.Matches(proposed.Key)&&roundtrip.modelLayoutBests[0].Read().Seconds==proposed.Seconds,"New model/layout PB roundtrips independently");
                Check(File.ReadAllBytes(backup).SequenceEqual(original),"First exact schema6 backup immutable after Save and reload");
                Check(CareerStorage.Store(0,"Generated populated slot",roundtrip),"Actual CareerStorage.Store accepts migrated populated career");
                var slot=CareerStorage.Read(0);Check(slot!=null&&!slot.LoadBlocked&&LegacyView(Json(slot),true)==LegacyView(raw,true)&&slot.modelLayoutBests.Count==1&&slot.modelLayoutBests[0].key==proposed.Key.Identity&&slot.modelLayoutBests[0].seconds==91.25,"Actual named slot retains legacy and independent model PB (intentional slot name/date excepted)");
                Check(File.ReadAllBytes(PlayerProfile.StoragePath).SequenceEqual(schema7),"Named slot store/read does not replace active private profile bytes");
                Fresh(CareerStorage.PathFor(1),text);var oldSlot=CareerStorage.Read(1);
                Check(oldSlot!=null&&!oldSlot.LoadBlocked&&oldSlot.version==7&&oldSlot.modelLayoutBests.Count==0&&LegacyView(Json(oldSlot))==LegacyView(raw),"Actual CareerStorage.Read migrates populated schema6 slot without inferred model PB");
                Check(File.ReadAllBytes(CareerStorage.PathFor(1)+".pre-lap-economy-v7.bak").SequenceEqual(original),"Named slot schema6 backup exactly retained");
                Check(CareerStorage.Store(1,"Generated migrated slot",oldSlot)&&File.ReadAllBytes(CareerStorage.PathFor(1)+".pre-lap-economy-v7.bak").SequenceEqual(original),"Named slot migration Save preserves exact prior backup");
                var future=(JObject)raw.DeepClone();future["version"]=8;future["futureSentinel"]="retain unknown future bytes";future["garage"]["cars"][0]["model"]=77;
                string futureText=future.ToString(Formatting.Indented);PlayerProfile.StorageOverride=Path.Combine(output,"generated-future8.json");Fresh(PlayerProfile.StoragePath,futureText);
                var refused=PlayerProfile.Load();Check(refused.LoadBlocked&&refused.version==8&&refused.garage.cars[0].model==77,"Future8 refused before normalization/downgrade");
                Check(!refused.Save()&&File.ReadAllText(PlayerProfile.StoragePath)==futureText&&!File.Exists(PlayerProfile.StoragePath+".pre-lap-economy-v7.bak"),"Future8 save refused; original bytes and unknown fields retained");
                // Resource capture on main, then EXACT same numeric decoder/finalizer on a worker.
                using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3)))
                {
                    string Capture(string id,string expected)
                    {var asset=Resources.Load<TextAsset>("Geo/"+id);if(!asset)throw new InvalidDataException("Actual bundled resource missing: "+id);var source=asset.text;Check(Sha(new UTF8Encoding(false,true).GetBytes(source))==expected,"Actual "+id+" source resource pin");return source;}
                    string silverstone=Capture("silverstone","324d892164145e7a1a45f73175fb19e3858320129936c7fbb5e25b6f91675924");
                    string spa=Capture("spa","bf6edf1c4d25f9d171b28e43cf9a1788c07b74b422fe439142c9722e77c79c70");
                    PreparedWorldData Prepare(string source,Action<GeoRegion> edit=null)
                    {return Task.Run(()=>{var region=WorldSnapshotCodec.DecodeBootSource(source,out bool authoritative);if(authoritative||!region.circuit)throw new InvalidDataException("Expected actual raw circuit source");edit?.Invoke(region);return WorldDataFinalizer.PrepareOwned(region,null,cancel.Token,RoadWidthWorldPreparation.PrepareSurfacesOwned);},cancel.Token).GetAwaiter().GetResult();}
                    var baseline=Prepare(silverstone);string identity=PhysicalCircuitIdentity.Compute(baseline,cancel.Token);identities["silverstone"]=identity;
                    var renamed=Prepare(silverstone,region=>{region.id="fixture-event-rule-alias";region.networkMapId="fixture-event-rule-alias";region.name="Generated alternate event display name";foreach(var road in region.roads)road.name="Display-only name";region.attribution="Generated diagnostic metadata only";});
                    SameDrivingInputs(baseline,renamed);Check(PhysicalCircuitIdentity.Compute(renamed,cancel.Token)==identity,"Names/owned IDs/event label aliases preserve identity after equal actual driving inputs");
                    var decorative=Prepare(silverstone,region=>{if(region.buildings.Length==0)throw new InvalidDataException("Expected existing actual building fixture");region.buildings[0].height+=1;region.buildings[0].roof=region.buildings[0].roof=="flat"?"gabled":"flat";foreach(var tree in region.trees??Array.Empty<GeoTree>())tree.height+=.25f;foreach(var water in region.waters)water.name="Decorative name only";});
                    SameDrivingInputs(baseline,decorative);Check(PhysicalCircuitIdentity.Compute(decorative,cancel.Token)==identity,"Building height/roof and scenery-only edits do not fork identical driving inputs");
                    void Changed(string name,Action<GeoRegion> edit){var changed=Prepare(silverstone,edit);string changedIdentity=PhysicalCircuitIdentity.Compute(changed,cancel.Token);identities[name]=changedIdentity;Check(changedIdentity!=identity,"Actual finalized "+name+" changes physical identity");}
                    Changed("route",region=>{region.route[10].x+=.5f;});
                    Changed("raceway-height",region=>{var road=region.roads.First(r=>r.kind=="raceway");road.points[road.points.Length/2].y+=1;});
                    Changed("raceway-width",region=>{region.roads.First(r=>r.kind=="raceway").width+=1;});
                    var other=Prepare(spa);identities["spa"]=PhysicalCircuitIdentity.Compute(other,cancel.Token);Check((string)identities["spa"]!=identity,"Actual independently prepared Spa differs from Silverstone");
                }
                identities["legacyFixtureSha256"]=Sha(original);identities["retainedSchema6BackupSha256"]=Sha(File.ReadAllBytes(backup));
            }
            catch(Exception error){failure=error.ToString();File.AppendAllText(Path.Combine(output,"checks.txt"),"FAIL "+failure+"\n");}
            finally{PlayerProfile.StorageOverride=previous;}
            var report=new JObject{{"status",failure==null?"PASS":"FAIL"},{"checks",checks},{"identities",identities},{"error",failure},{"profileOverrideRestored",PlayerProfile.StorageOverride==previous},{"scope","generated private profiles + real numeric source preparation; no GameSession/scene/physics/rental/native playability"}};
            Fresh(Path.Combine(output,"receipt.json"),report.ToString(Formatting.Indented));return failure==null?0:1;
        }
    }
}
