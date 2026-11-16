using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Newtonsoft.Json.Linq;
namespace EarthUnscripted.Editor
{
    // Actual Editor mesh/plan/lifecycle checks. This is not a native pixel/performance proof.
    public static class BuildingFacadeVerification
    {
        static int checks;static string root;static Mesh[] shared;static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool ok,string text){if(!ok)throw new Exception("BUILDING FACADE FAIL: "+text);checks++;Debug.Log("BUILDING FACADE PASS: "+text);}
        static GeoBuilding House(bool reverse=false,string kind="building",float height=6)
        {
            var p=new[]{new Vector3(40,2,18),new Vector3(60,2,18),new Vector3(60,2,26),new Vector3(40,2,26)};
            if(reverse)Array.Reverse(p);return new GeoBuilding{id="fixture-house",kind=kind,height=height,roof="gabled",points=p.Concat(new[]{p[0]}).ToArray()};
        }
        static GeoRegion Fixture(GeoBuilding b)
        {
            var r=new GeoRegion{id="facade-fixture",name="Facade fixture",preparedWidths=true,minX=-320,minZ=-320,width=640,depth=640,nx=2,nz=2,heights=new[]{2f,2f,2f,2f},landNx=2,landNz=2,landCover=new int[4],roads=new[]{new GeoRoad{id="front-road",start="a",end="b",kind="residential",width=8,points=new[]{new Vector3(-300,2,0),new Vector3(300,2,0)}}},buildings=new[]{b},waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),route=new[]{new Vector3(0,2,0),new Vector3(100,2,0)}};r.Prepare();return r;
        }
        static BuildingFacadePlan.Opening[] Plan(GeoBuilding b,Func<Vector3,bool> exterior=null,Func<float,float,float> surface=null)
            =>BuildingFacadePlan.Create(Fixture(b),b,2,exterior??(p=>true),surface??((x,z)=>2)).ToArray();
        static string PlanKey(IEnumerable<BuildingFacadePlan.Opening> openings)=>string.Join("|",openings.Select(o=>o.Id+":"+o.Position.ToString("R")+":"+o.Rotation.ToString("R")+":"+o.Width.ToString("R")+":"+o.Height.ToString("R")+":"+o.Door+":"+o.Palette));
        static string Features(GameObject g)=>string.Join("|",g.GetComponentsInChildren<BuildingFacadeChunk>(true).SelectMany(c=>c.Features).Select(o=>o.Id+":"+o.Position.ToString("R")+":"+o.Rotation.ToString("R")+":"+o.Width.ToString("R")+":"+o.Height.ToString("R")+":"+o.IsDoor+":"+o.Palette));
        static object Invoke(GeoWorld world,string name,params object[] args)=>typeof(GeoWorld).GetMethod(name,Private).Invoke(world,args);
        static Dictionary<Vector2Int,GameObject> Chunks(GeoWorld world)=>(Dictionary<Vector2Int,GameObject>)typeof(GeoWorld).GetField("loaded",Private).GetValue(world);
        static void PlanChecks()
        {
            var house=House();var first=Plan(house);Check(first.Length>0&&first.Any(o=>o.Door)&&first.Any(o=>!o.Door),"ordinary frontage produces metric windows and entrances");
            Check(PlanKey(first)==PlanKey(Plan(house)),"same source ID and geometry produce bit-stable ordered descriptors");
            Check(first.Select(o=>o.Id).Distinct().Count()==first.Length,"opening IDs are unique within the source footprint");
            var reverse=Plan(House(true));Check(first.Count(o=>o.Door)==reverse.Count(o=>o.Door)&&first.Count(o=>!o.Door)==reverse.Count(o=>!o.Door),"clockwise and counterclockwise loops preserve opening counts");
            bool outward=true;foreach(var b in new[]{House(),House(true)})foreach(var o in Plan(b))outward&=!GeoRegion.Contains(b.points,new Vector2((o.Position+o.Rotation*Vector3.forward*.1f).x,(o.Position+o.Rotation*Vector3.forward*.1f).z));
            Check(outward,"both footprint winding directions orient every front plane out of the building");
            Check(first.Where(o=>o.Door).All(o=>o.Width==.92f&&o.Height==2.08f)&&first.Where(o=>!o.Door).All(o=>o.Width<=1.12f&&o.Height>=.65f&&o.Height<=1.25f),"doors and windows use bounded metre-scale dimensions");
            Check(first.All(o=>o.Position.y+o.Height*.5f<=8-.18f+.0001f)&&first.Where(o=>!o.Door).All(o=>o.Position.y-o.Height*.5f>=2.16f),"openings clear the physical ground and remain below the wall top");
            Check(first.Where(o=>o.Door).All(o=>Vector3.Dot(o.Rotation*Vector3.forward,Vector3.back)>.99f),"entrances face the nearby road rather than rear or side facades");
            var party=Plan(house,p=>p.x<=60);Check(party.Length<first.Length&&party.All(o=>o.Position.x<59.99f),"an attached neighbor excludes decorations on the shared party wall");
            Check(Plan(house,p=>false).Length==0,"road or occupied-space exclusion rejects every affected opening");
            Check(Plan(house,null,(x,z)=>20).Length==0,"terrain above the wall rejects buried windows and doors");
            var slope=Plan(house,null,(x,z)=>2+(x-40)*.6f);Check(!slope.Any(o=>o.Door),"doorways spanning steep lateral ground are rejected rather than floating over a slope");
            var uphill=Plan(house,null,(x,z)=>2+Mathf.Max(0,18-z)*3);Check(uphill.All(o=>!o.Door||o.Position.y-o.Height*.5f>=2+Mathf.Max(0,18-(o.Position+o.Rotation*Vector3.forward*.08f).z)*3+.009f),"outward-rising ground cannot bury a door frame");
            var important=Plan(House(false,"important_building"));Check(important.Count(o=>o.Door)==1&&first.Count(o=>o.Door)>1,"important building has one frontage entrance instead of repeated terrace doors");
            foreach(float height in new[]{2.8f,4.3f,4.69f,6f,11.86f})Check(Plan(House(false,"building",height)).All(o=>o.Position.y+o.Height*.5f<=2+height-.18f+.0001f),"height "+height+" preserves eaves clearance without forced two-storey art");
            var large=House(false,"important_building",14);large.points=new[]{new Vector3(0,2,18),new Vector3(200,2,18),new Vector3(200,2,78),new Vector3(0,2,78),new Vector3(0,2,18)};Check(Plan(large).Length==BuildingFacadePlan.MaximumOpeningsPerBuilding,"large complex facade obeys the explicit192-opening bound");
        }
        static BuildingFacadeChunk RendererChecks()
        {
            var g=new GameObject("Facade renderer fixture");var component=g.AddComponent<BuildingFacadeChunk>();typeof(BuildingFacadeChunk).GetMethod("OnEnable",Private).Invoke(component,null);g.SetActive(false);
            Check(component.AddWindow("test-window",Vector3.zero,Quaternion.identity,1.12f,1.25f)&&component.AddDoor("test-door",Vector3.right*2,Quaternion.identity,.92f,2.08f,3),"renderer accepts plan-sized door/window features");
            Check(!component.AddDoor("bad-palette",Vector3.zero,Quaternion.identity,1,2,4)&&!component.AddWindow("bad-number",Vector3.zero,Quaternion.identity,float.NaN,1),"renderer rejects invalid palette and nonfinite dimensions");
            Check(g.transform.childCount==0&&g.GetComponentsInChildren<Collider>(true).Length==0&&g.GetComponentsInChildren<MeshFilter>(true).Length==0&&g.GetComponentsInChildren<Renderer>(true).Length==0,"feature additions create no individual objects, renderers, meshes or colliders");
            shared=typeof(BuildingFacadeChunk).GetFields(BindingFlags.Static|BindingFlags.NonPublic).Where(f=>f.FieldType==typeof(Mesh)).Select(f=>(Mesh)f.GetValue(null)).ToArray();Check(shared.Length==6&&shared.All(m=>m),"all six original facade meshes are shared application resources");
            foreach(var m in shared)
            {
                var v=m.vertices;var n=m.normals;var t=m.triangles;bool valid=v.Length==n.Length&&v.Length>0;
                foreach(var p in v)valid&=Mathf.Abs(p.x)<=.50001f&&Mathf.Abs(p.y)<=.50001f&&p.z>=-.00001f&&p.z<=.08001f&&m.bounds.SqrDistance(p)<.000001f;
                for(int i=0;i<t.Length;i+=3){var normal=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).normalized;valid&=normal.sqrMagnitude>.9f&&Vector3.Dot(normal,n[t[i]])>.99f;}
                Check(valid,m.name+" has bounded finite geometry and outward face normals consistent with triangle winding");
            }
            var nearWindow=shared.Single(m=>m.name=="Shared recessed sash window");var farWindow=shared.Single(m=>m.name=="Shared flat sash window");var nearDoor=shared.Single(m=>m.name=="Shared recessed door frame");var farDoor=shared.Single(m=>m.name=="Shared flat door frame");
            Check(nearWindow.bounds.min.x==farWindow.bounds.min.x&&nearWindow.bounds.max.x==farWindow.bounds.max.x&&nearWindow.bounds.min.y==farWindow.bounds.min.y&&nearWindow.bounds.max.y==farWindow.bounds.max.y&&nearDoor.bounds.size.x==farDoor.bounds.size.x&&nearDoor.bounds.size.y==farDoor.bounds.size.y,"near/far profiles preserve exact facade silhouettes while dropping less than0.1m depth detail");
            Check(BuildingAppearanceMaterials.Frame==BuildingAppearanceMaterials.Frame&&BuildingAppearanceMaterials.Glass==BuildingAppearanceMaterials.Glass&&Enumerable.Range(0,4).All(i=>BuildingAppearanceMaterials.Door(i)==BuildingAppearanceMaterials.Door(i)&&BuildingAppearanceMaterials.Door(i).enableInstancing),"frame glass and four door palettes reuse cached instancing-capable materials");
            g.transform.SetPositionAndRotation(new Vector3(17,9,-13),Quaternion.Euler(0,37,0));g.transform.localScale=new Vector3(2,.7f,1.3f);typeof(BuildingFacadeChunk).GetMethod("RebuildMatrices",Private).Invoke(component,null);
            var matrices=(Matrix4x4[])typeof(BuildingFacadeChunk).GetField("matrices",Private).GetValue(component);var bounds=(Bounds[])typeof(BuildingFacadeChunk).GetField("bounds",Private).GetValue(component);bool contains=true;
            for(int i=0;i<component.Features.Count;i++)foreach(var mesh in shared)foreach(var v in mesh.vertices)contains&=bounds[i].SqrDistance(matrices[i].MultiplyPoint3x4(v))<.000001f;Check(contains,"actual world bounds contain every shared mesh vertex under rotated nonuniform root and feature scales");
            var prior=BuildingFacadeChunk.Statistics;typeof(BuildingFacadeChunk).GetMethod("LateUpdate",Private).Invoke(component,null);var after=BuildingFacadeChunk.Statistics;Check(prior.submittedWindows==after.submittedWindows&&prior.submittedDoors==after.submittedDoors&&prior.drawSubmissions==after.drawSubmissions,"inactive staged component cannot submit drawing even when LateUpdate is invoked explicitly");
            for(int i=component.FeatureCount;i<BuildingFacadeChunk.Capacity;i++)CheckAdd(component.AddWindow("capacity-"+i,Vector3.zero,Quaternion.identity,1,1));Check(component.FeatureCount==4096&&!component.AddWindow("overflow",Vector3.zero,Quaternion.identity,1,1)&&component.OverflowCount==1,"4096 features are bounded and the next addition is explicitly rejected and counted");
            return component;
        }
        static void CheckAdd(bool ok){if(!ok)throw new Exception("Unexpected early facade capacity rejection");}
        static void LifecycleChecks()
        {
            var world=new GameObject("Facade lifecycle world").AddComponent<GeoWorld>();world.Initialize(Fixture(House()),Geometry.Material("Facade verification asphalt",Color.gray));world.gameObject.SetActive(false);var key=Vector2Int.zero;
            var first=Chunks(world)[key];string descriptor=Features(first);Check(descriptor.Length>0,"actual GeoWorld construction publishes instanced openings on the fixture shell");var shell=first.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name.StartsWith("Building fixture-house",StringComparison.Ordinal));var owned=shell.sharedMesh;var v=owned.vertices;var indices=owned.triangles;
            Invoke(world,"Retire",key,"facade-verification");Check(!first&&!owned&&shared.All(m=>m),"normal chunk retirement destroys shell ownership but retains every shared facade mesh");
            object stage=Invoke(world,"StartStage",key,"facade-verification");var hidden=(Transform)stage.GetType().GetField("root").GetValue(stage);int steps=0;while((bool)Invoke(world,"AdvanceStage",stage)){if(++steps>100000)throw new Exception("stage never completed");}
            var completed=Chunks(world)[key];var rebuilt=completed.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name.StartsWith("Building fixture-house",StringComparison.Ordinal));Check(descriptor==Features(completed)&&v.SequenceEqual(rebuilt.sharedMesh.vertices)&&indices.SequenceEqual(rebuilt.sharedMesh.triangles),"fully drained staged generation reproduces exact descriptors and collision geometry");
            Invoke(world,"Retire",key,"facade-verification");stage=Invoke(world,"StartStage",key,"facade-cancel-verification");typeof(GeoWorld).GetField("stage",Private).SetValue(world,stage);hidden=(Transform)stage.GetType().GetField("root").GetValue(stage);steps=0;
            while(!hidden.GetComponent<BuildingFacadeChunk>()||hidden.GetComponent<BuildingFacadeChunk>().FeatureCount==0){CheckAdd((bool)Invoke(world,"AdvanceStage",stage));if(++steps>100000)throw new Exception("no hidden facade before completion");}
            var cancelled=hidden.GetComponent<BuildingFacadeChunk>();var stagedOwned=hidden.GetComponentsInChildren<MeshFilter>(true).First().sharedMesh;Check(!hidden.gameObject.activeInHierarchy&&cancelled.FeatureCount>0,"unfinished chunk contains actual feature instances while its complete root remains inactive");Invoke(world,"CancelStage","facade-verification");Check(!hidden&&!cancelled&&!stagedOwned&&shared.All(m=>m),"cancelling partially prepared facade destroys instances and owned terrain without deleting shared meshes");UnityEngine.Object.DestroyImmediate(world.gameObject);
        }
        static void SiteAnalysis(GeoWorld world,GeoRegion region)
        {
            var rows=new JArray();var roofIDs=new JArray();var reliefIDs=new JArray();var complexIDs=new JArray();int visited=0;
            foreach(var building in region.buildings.OrderBy(b=>b.id,StringComparer.Ordinal))
            {
                bool rendererVisited=world.BuildingSites.TryGetValue(building.id,out var site);if(rendererVisited)visited++;
                // Analyse the full prepared region, including currently unloaded sites;
                // reuse exact renderer results wherever they already exist.
                if(!rendererVisited)site=BuildingSitePlan.Create(region,building,world.MeshSurfaceHeight);
                if(site.RoofBurialRisk)roofIDs.Add(building.id);if(site.HighRelief)reliefIDs.Add(building.id);if(site.ComplexFootprint)complexIDs.Add(building.id);
                rows.Add(new JObject{{"id",building.id},{"kind",building.kind??""},{"height",building.height},{"rendererVisited",rendererVisited},{"roadOverlapRejected",region.FootprintOverlapsRoad(building.points)},
                    {"levelFloorY",site.LevelFloorY},{"groundMedianY",site.GroundMedianY},{"sampledBoundaryMinY",site.BoundaryMinY},{"sampledBoundaryMaxY",site.BoundaryMaxY},{"sampleCount",site.SampleCount},
                    {"highRelief",site.HighRelief},{"roofBurialRisk",site.RoofBurialRisk},{"complexFootprint",site.ComplexFootprint},{"centreOutsideFootprint",site.CentreOutsideFootprint},{"conservativeFallback",site.ConservativeFallback},
                    {"maxExposedFoundation",site.MaxExposedFoundation},{"maxUphillCut",site.MaxUphillCut},{"hasNearbyRoad",site.HasNearbyRoad},{"nearestRoadIsBridge",site.NearestRoadIsBridge},
                    {"roadGroundDelta",site.RoadGroundDelta},{"roadToFloorDelta",site.RoadToFloorDelta},{"centreX",site.Centre.x},{"centreZ",site.Centre.z}});
            }
            var report=new JObject{{"scope","Full prepared Pantside region; unloaded sites sampled with the same actual triangulated surface callback. This is diagnostic analysis, not zero-risk acceptance."},
                {"loadedChunks",world.LoadedChunks},{"analysedSites",rows.Count},{"rendererVisitedSites",visited},{"highReliefCount",reliefIDs.Count},{"roofBurialRiskCount",roofIDs.Count},{"complexFootprintCount",complexIDs.Count},
                {"roofBurialRiskIDs",roofIDs},{"highReliefIDs",reliefIDs},{"complexFootprintIDs",complexIDs},{"sites",rows}};
            var renderedIDs=new HashSet<string>(world.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.StartsWith("Building ",StringComparison.Ordinal)).Select(c=>{string name=c.name.Substring(9);int suffix=name.LastIndexOf(" (",StringComparison.Ordinal);return suffix>=0?name.Substring(0,suffix):name;}));
            Check(renderedIDs.All(id=>world.BuildingSites.ContainsKey(id)),"every rendered original or derived wall has its actual horizontal site diagnostics");
            var actual=new JArray();var renderedRisk=new JArray();var derivedRisk=new JArray();int derivedCount=0;
            foreach(var pair in world.BuildingSites.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                bool derived=pair.Key.Contains("/site-"),rendered=renderedIDs.Contains(pair.Key);if(!derived&&!rendered)continue;var site=pair.Value;
                if(derived)derivedCount++;if(site.RoofBurialRisk&&rendered)renderedRisk.Add(pair.Key);if(site.RoofBurialRisk&&derived)derivedRisk.Add(pair.Key);
                int cut=pair.Key.IndexOf("/site-",StringComparison.Ordinal);actual.Add(new JObject{{"id",pair.Key},{"sourceId",cut>=0?pair.Key.Substring(0,cut):pair.Key},{"derived",derived},{"secondaryContour",pair.Key.Contains("/contour-")},{"rendered",rendered},
                    {"levelFloorY",site.LevelFloorY},{"groundMedianY",site.GroundMedianY},{"sampledBoundaryMinY",site.BoundaryMinY},{"sampledBoundaryMaxY",site.BoundaryMaxY},{"highRelief",site.HighRelief},{"roofBurialRisk",site.RoofBurialRisk},{"complexFootprint",site.ComplexFootprint},{"conservativeFallback",site.ConservativeFallback},{"roadToFloorDelta",site.RoadToFloorDelta}});
            }
            var access=world.GetComponentsInChildren<BuildingAccessChunk>(true);report["renderedSiteCount"]=renderedIDs.Count;report["derivedPartCount"]=derivedCount;report["derivedPartRoofBurialRiskCount"]=derivedRisk.Count;report["derivedPartRoofBurialRiskIDs"]=derivedRisk;report["renderedRoofBurialRiskCount"]=renderedRisk.Count;report["renderedRoofBurialRiskIDs"]=renderedRisk;report["actualRenderedOrDerivedSites"]=actual;report["accessChunks"]=access.Length;report["accessFlights"]=access.Sum(c=>c.FlightCount);report["accessOverflow"]=access.Sum(c=>c.OverflowCount);
            Check(access.All(c=>c.OverflowCount==0&&c.Sealed),"actual Pantside access meshes commit once without capacity overflow");
            File.WriteAllText(Path.Combine(root,"site-analysis.json"),report.ToString());
        }
        static void PantsideChecks()
        {
            string json=File.ReadAllText("Builds/Verification/CanopyPack-cb8d2bf7/peer-world.json");var region=JsonUtility.FromJson<GeoRegion>(json);new DetailBuildingPack("/private/tmp/EarthUnscriptedDetailCompiled/PantsideBuildingsV2").Apply(region);region.Prepare();Check(region.buildings.Count(b=>b.kind=="important_building")==7&&region.trees.Length>0,"actual Pantside world includes classified V2 source parts and accepted canopy");string snapshot=WorldSnapshotCodec.CanonicalJson(region);
            var native=JsonUtility.FromJson<GeoRegion>(snapshot);native.id="download-pantside-facade-v2";native.networkMapId="";File.WriteAllText(Path.Combine(root,"native-world.json"),JsonUtility.ToJson(native));
            var world=new GameObject("Actual Pantside facade verification").AddComponent<GeoWorld>();world.EntryAudit.Enabled=true;world.Initialize(region,Geometry.Material("Pantside facade verification asphalt",Color.gray));world.gameObject.SetActive(false);
            var chunks=world.GetComponentsInChildren<BuildingFacadeChunk>(true);var features=chunks.SelectMany(c=>c.Features).ToArray();Check(chunks.Length>0&&features.Any(f=>f.IsDoor)&&features.Any(f=>!f.IsDoor),"actual Pantside chunks contain original instanced entrances and windows");Check(chunks.All(c=>c.FeatureCount<=4096&&c.OverflowCount==0)&&features.Select(f=>f.Id).Distinct().Count()==features.Length,"actual Pantside generation stays within explicit per-chunk capacity with no silently rejected or duplicate features");Check(snapshot==WorldSnapshotCodec.CanonicalJson(region),"facade rendering leaves authoritative footprint height canopy and network snapshot data unchanged");
            string auditText=world.EntryAudit.ExportJson();File.WriteAllText(Path.Combine(root,"entry-audit.json"),auditText);var audit=JObject.Parse(auditText);var auditRows=(JArray)audit["rows"];
            Check((bool)audit["enabled"]&&(int)audit["pending"]==0&&(int)audit["dropped"]==0&&(int)audit["duplicateTerminals"]==0&&auditRows.All(r=>!string.IsNullOrEmpty((string)r["terminal"])),"opt-in production entry funnel records exactly one terminal per bounded candidate without dropped or unfinished rows");
            Check((int)audit["accepted"]==features.Count(f=>f.IsDoor)&&auditRows.Count(r=>(bool)r["planned"])==(int)audit["planned"],"actual accepted doors and planned entry funnel counts balance without changing geometry");
            SiteAnalysis(world,region);
            int walls=world.GetComponentsInChildren<MeshCollider>(true).Count(c=>c.name.StartsWith("Building ",StringComparison.Ordinal));File.WriteAllText(Path.Combine(root,"pantside-counts.txt"),$"loadedChunks={world.LoadedChunks}\nwallMeshes={walls}\nfacadeChunks={chunks.Length}\nwindows={features.Count(f=>!f.IsDoor)}\ndoors={features.Count(f=>f.IsDoor)}\nsourceImportant=7\ncanonicalTrees={region.trees.Length}\n");Check(walls>0&&world.GetComponentsInChildren<DetailedWoodlandChunk>(true).Length>0,"actual classified building shells and detailed woodland coexist");UnityEngine.Object.DestroyImmediate(world.gameObject);Check(shared.All(m=>m),"complete Pantside world disposal retains shared facade meshes");
        }
        public static void Run()
        {
            GameObject renderer=null;string realPath=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");string realHash=File.Exists(realPath)?EarthUnscripted.Core.DataPackageRules.HashFile(realPath):"missing";
            try
            {
                ProjectSetup.Configure();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);root=Path.GetFullPath("Builds/Verification/BuildingFacade");Directory.CreateDirectory(root);PlayerProfile.StorageOverride=Path.Combine(root,"isolated-profile.json");RegionCache.OverrideFolder=Path.Combine(root,"Regions");
                PlanChecks();renderer=RendererChecks().gameObject;LifecycleChecks();PantsideChecks();UnityEngine.Object.DestroyImmediate(renderer);renderer=null;Check(shared.All(m=>m),"disposing standalone instance buffers never destroys shared geometry");
                Check((File.Exists(realPath)?EarthUnscripted.Core.DataPackageRules.HashFile(realPath):"missing")==realHash,"isolated Editor fixture leaves the real career bytes unchanged");
                File.WriteAllText(Path.Combine(root,"result.txt"),$"PASS {checks} actual Editor facade plan metric clearance capacity meshes normals bounds materials staged collision and ownership checks\nNo native pixel, GPU submission or performance acceptance claim.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{if(renderer)UnityEngine.Object.DestroyImmediate(renderer);}
        }
    }
}
