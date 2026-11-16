using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;
namespace EarthUnscripted
{
    // Explicit isolated packet viewer, not the normal world renderer or codec.
    public sealed class RoadGeometryNativeProof : MonoBehaviour
    {
        const string Flag="-earthUnscriptedRoadGeometryProof",Format="EarthUnscriptedRoadProfileRenderFixture1";
        static string prefix,folder,manifestHash,realCareer,realHash;static int errors;static readonly List<string> messages=new List<string>();
        [Serializable] public sealed class ProbeResult{public string id,category;public bool roadHit,bedHit;public float expectedTop,actualTop,expectedBedTop,actualBedTop,roadDelta,bedDelta;public Vector3 point;}
        [Serializable] public sealed class TerrainResult{public string id;public Vector3 point;public bool hit;public float expected,actual,delta;}
        [Serializable] public sealed class Report
        {public string status,error,version,manifestSHA256,fixtureFolder,scope;public int schemaVersion,packets,vertices,triangles,probes,roadMissing,bedMissing,rawRayMismatches,runtimeErrors,wheelContacts,width,height,terrainProbes,terrainMissing,terrainRayMismatches,underRoadCoverFailures,underRoadGapFailures,seamPairs,seamRayFailures,upstreamSteepFaces;public float maxRoadDelta,maxBedDelta,carDamage,carSpeed,carRoll,maxTerrainDelta,maxSeamPointDelta,upstreamMaxGrade,upstreamBroadGrade,upstreamMaxCutFill;public bool realCareerUnchanged,fixtureUnchanged,noGameSession,uploadedVerticesExact,externalWheelSupport,terrainAccepted;public Vector3 carPosition;public float[] wheelRoadGaps;public string[] captures,firstErrors;public ProbeResult[] results;public TerrainResult[] terrainResults;}
        sealed class MeshRecord{public string role;public MeshCollider collider;}
        readonly Report report=new Report();readonly List<MeshRecord> colliders=new List<MeshRecord>();readonly List<ProbeResult> results=new List<ProbeResult>();readonly List<string> captures=new List<string>();readonly Dictionary<string,string> hashes=new Dictionary<string,string>();
        readonly List<TerrainResult> terrainResults=new List<TerrainResult>();JObject probes;Camera camera;VehicleController car;Vector3 middle,forward,right;float rowWidth;bool done;double deadline;
        static string Arg(string[] args,string name){int at=Array.IndexOf(args,name);if(at<0||at+1>=args.Length)throw new ArgumentException("Missing "+name);return args[at+1];}
        static void Log(string text,string stack,LogType type){if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;errors++;if(messages.Count<16)messages.Add(text);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)] static void Prepare()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,Flag)<0)return;
            Application.runInBackground=true;Application.logMessageReceived+=Log;
            PlayerProfile.StorageOverride=Path.Combine(Application.temporaryCachePath,"invalid-road-geometry-proof-"+Guid.NewGuid().ToString("N")+".json");
            try
            {
                prefix=Path.GetFullPath(Arg(args,Flag));folder=Path.GetFullPath(Arg(args,"-earthUnscriptedRoadGeometryFixture"));manifestHash=Arg(args,"-earthUnscriptedRoadGeometryManifestSha").ToLowerInvariant();
                if(File.Exists(prefix+".json")||File.Exists(prefix+"-profile.json"))throw new IOException("Fresh geometry proof prefix required");
                PlayerProfile.StorageOverride=prefix+"-profile.json";realCareer=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json");realHash=File.Exists(realCareer)?Core.DataPackageRules.HashFile(realCareer):null;
                Directory.CreateDirectory(Path.GetDirectoryName(prefix));
                if(manifestHash.Length!=64||Core.DataPackageRules.HashFile(Path.Combine(folder,"manifest.json"))!=manifestHash)throw new IOException("Pinned fixture manifest differs");
            }
            catch(Exception e){if(prefix!=null)File.WriteAllText(prefix+".json",JsonUtility.ToJson(new Report{status="FAIL",error=e.ToString()},true));Application.Quit(1);}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Launch(){if(prefix!=null)new GameObject("Isolated road geometry proof").AddComponent<RoadGeometryNativeProof>();}
        void OnEnable(){deadline=Time.realtimeSinceStartupAsDouble+150;}
        void Update(){if(!done&&Time.realtimeSinceStartupAsDouble>deadline)Finish("Geometry proof deadline exceeded");}
        IEnumerator Start()
        {IEnumerator run=Run();while(true){bool more;object value;try{more=run.MoveNext();value=more?run.Current:null;}catch(Exception e){Finish(e.ToString());yield break;}if(!more)yield break;yield return value;}}
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static Vector3 V3(JToken token)
        {if(token is not JObject o||o.Count!=3)throw new InvalidDataException("Expected bounded XYZ vector");var p=new Vector3((float)o["x"],(float)o["y"],(float)o["z"]);if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)||p.sqrMagnitude>1e10f)throw new InvalidDataException("Invalid fixture vector");return p;}
        static Vector2 V2(JToken token){if(token is not JObject o||o.Count!=2)throw new InvalidDataException("Expected UV vector");var p=new Vector2((float)o["x"],(float)o["y"]);if(!Finite(p.x)||!Finite(p.y))throw new InvalidDataException("Nonfinite UV");return p;}
        JObject Read(string name)
        {
            var file=Path.Combine(folder,name);var info=new FileInfo(file);if(!info.Exists||info.Length>32*1024*1024)throw new InvalidDataException("Fixture file byte limit");
            using var reader=new JsonTextReader(new StringReader(File.ReadAllText(file))){MaxDepth=40,FloatParseHandling=FloatParseHandling.Double};return JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
        }
        static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        bool ValidFormat(JObject value)=>(int?)value["version"]==report.schemaVersion&&(string)value["format"]=="EarthUnscriptedRoadProfileRenderFixture"+report.schemaVersion;
        void ValidateManifest()
        {
            var manifest=Read("manifest.json");report.schemaVersion=(int)manifest["version"];Require((report.schemaVersion==1||report.schemaVersion==2)&&ValidFormat(manifest),"Unsupported geometry fixture");
            if(report.schemaVersion==2){var receipt=manifest["terrainReceipt"];Require(receipt!=null,"Terrain recipe evidence required");report.upstreamSteepFaces=(int)receipt["gradeOver1p2FaceCount"];report.upstreamMaxGrade=(float)receipt["maxTerrainGrade"];report.upstreamBroadGrade=(float)receipt["maxTerrainGradeAbove10cmAltitude"];report.upstreamMaxCutFill=(float)receipt["maxAlterationFromOriginalDEM"];report.terrainAccepted=false;}
            var files=manifest["files"] as JArray;Require(files!=null&&files.Count==4,"Exactly four pinned fixture files required");
            var expected=new HashSet<string>(new[]{"meshes.json","probes.json","controls.json","terrain-source.json"},StringComparer.Ordinal);
            foreach(var entry in files)
            {string name=(string)entry["path"],hash=(string)entry["sha256"];Require(expected.Remove(name)&&hash!=null&&hash.Length==64,"Unexpected or duplicate fixture file");var file=Path.Combine(folder,name);Require(new FileInfo(file).Length==(long)entry["bytes"]&&Core.DataPackageRules.HashFile(file)==hash,"Fixture file pin mismatch "+name);hashes.Add(name,hash);}
            Require(expected.Count==0,"Missing fixture file");hashes.Add("manifest.json",manifestHash);
        }
        void BuildMeshes()
        {
            var data=Read("meshes.json");Require(ValidFormat(data),"Mesh packet format");var packets=data["packets"] as JArray;Require(packets!=null&&packets.Count>0&&packets.Count<=256,"Mesh packet count");
            var names=new HashSet<string>(StringComparer.Ordinal);int vertexCount=0,indexCount=0;bool exact=true;
            foreach(var packet in packets)
            {
                string id=(string)packet["id"],role=(string)packet["materialRole"];Require(!string.IsNullOrEmpty(id)&&id.Length<=256&&names.Add(id),"Packet identity");Require(role=="asphalt"||role=="paint"||role=="roadbed"||report.schemaVersion==2&&(role=="terrain"||role=="verge"),"Material role");
                var pv=packet["vertices"] as JArray;var pt=packet["triangles"] as JArray;var pu=packet["uv"] as JArray;
                Require(pv!=null&&pv.Count>=3&&pv.Count<=65535&&pt!=null&&pt.Count>0&&pt.Count%3==0&&pu!=null&&pu.Count==pv.Count,"Mesh dimensions");Require((vertexCount+=pv.Count)<=250000&&(indexCount+=pt.Count)<=1500000,"Aggregate mesh budget");
                var vertices=pv.Select(V3).ToArray();var uv=pu.Select(V2).ToArray();var triangles=pt.Select(t=>{Require(t.Type==JTokenType.Integer,"Integer mesh index");long n=(long)t;Require(n>=0&&n<vertices.Length,"Mesh index bounds");return(int)n;}).ToArray();
                bool collision=(bool)packet["collision"];Require(role!="paint"||!collision,"Paint must not add a collider");
                var material=role=="asphalt"?Geometry.Surface("Road asphalt",new Color(.13f,.145f,.16f),.22f,1):role=="paint"?Geometry.Material("Profile proof paint",new Color(.94f,.94f,.82f)):role=="terrain"||role=="verge"?Geometry.Surface("Grass terrain",new Color(.32f,.40f,.22f),.22f,1):Geometry.Surface("Profile proof roadbed",new Color(.32f,.29f,.22f),.25f,1);
                var mesh=Geometry.Mesh(id,transform,vertices,triangles,uv,material,collision);var uploaded=mesh.GetComponent<MeshFilter>().sharedMesh.vertices;
                for(int i=0;i<vertices.Length;i++)exact&=Same(uploaded[i],vertices[i]);
                if(collision)colliders.Add(new MeshRecord{role=role,collider=mesh.GetComponent<MeshCollider>()});
                report.packets++;report.vertices+=vertices.Length;report.triangles+=triangles.Length/3;
            }
            report.uploadedVerticesExact=exact;Require(exact,"Uploaded source vertices changed");Physics.SyncTransforms();
        }
        static bool Same(Vector3 a,Vector3 b)=>BitConverter.SingleToInt32Bits(a.x)==BitConverter.SingleToInt32Bits(b.x)&&BitConverter.SingleToInt32Bits(a.y)==BitConverter.SingleToInt32Bits(b.y)&&BitConverter.SingleToInt32Bits(a.z)==BitConverter.SingleToInt32Bits(b.z);
        bool Top(string role,Vector3 p,out float y)
        {y=float.NegativeInfinity;bool found=false;foreach(var record in colliders)if((record.role==role||role=="natural"&&(record.role=="terrain"||record.role=="verge")||role=="earth"&&(record.role=="terrain"||record.role=="verge"||record.role=="roadbed"))&&record.collider.Raycast(new Ray(new Vector3(p.x,p.y+10,p.z),Vector3.down),out var hit,30)){found=true;y=Mathf.Max(y,hit.point.y);}return found;}
        void CheckProbes()
        {
            probes=Read("probes.json");Require(ValidFormat(probes),"Probe format");var items=probes["probes"] as JArray;Require(items!=null&&items.Count>0&&items.Count<=100000,"Probe count");
            foreach(var item in items)
            {
                if((bool?)item["hasTerrain"]==true)
                {
                    Require(report.schemaVersion==2,"Terrain probes require schema2");var point=V3(item["point"]);float expected=(float)item["expectedTerrainTop"];Require(Finite(expected),"Finite terrain expected height");var terrainResult=new TerrainResult{id=(string)item["id"],point=point,expected=expected};terrainResult.hit=Top("natural",new Vector3(point.x,expected,point.z),out float terrainY);terrainResult.actual=terrainResult.hit?terrainY:0;terrainResult.delta=terrainResult.hit?Mathf.Abs(terrainY-expected):0;
                    if(!terrainResult.hit)report.terrainMissing++;else if(terrainResult.delta>.0002f)report.terrainRayMismatches++;report.maxTerrainDelta=Mathf.Max(report.maxTerrainDelta,terrainResult.delta);terrainResults.Add(terrainResult);report.terrainProbes++;continue;
                }
                var p=V3(item["point"]);float top=(float)item["expectedTop"],bed=(float)item["expectedBedTop"];Require(Finite(top)&&Finite(bed),"Probe heights");
                var query=new Vector3(p.x,top,p.z); // Contract point supplies XZ; expectedTop supplies the vertical ray reference.
                var result=new ProbeResult{id=(string)item["id"],category=(string)item["category"],point=p,expectedTop=top,expectedBedTop=bed};result.roadHit=Top("asphalt",query,out float roadY);result.bedHit=Top("roadbed",query,out float bedY);result.actualTop=result.roadHit?roadY:0;result.actualBedTop=result.bedHit?bedY:0;
                if(!result.roadHit)report.roadMissing++;else{result.roadDelta=Mathf.Abs(roadY-top);report.maxRoadDelta=Mathf.Max(report.maxRoadDelta,result.roadDelta);}
                if(!result.bedHit)report.bedMissing++;else{result.bedDelta=Mathf.Abs(bedY-bed);report.maxBedDelta=Mathf.Max(report.maxBedDelta,result.bedDelta);}
                if(result.roadHit&&result.roadDelta>.0002f||result.bedHit&&result.bedDelta>.0002f)report.rawRayMismatches++;
                if(report.schemaVersion==2){if(!Top("earth",query,out float earth)||earth<top-1.2f)report.underRoadGapFailures++;else if(earth>top+.04f)report.underRoadCoverFailures++;}
                results.Add(result);
            }
            report.probes=items.Count;
            if(report.schemaVersion==2)
            {
                var seams=probes["seamProbes"] as JArray;Require(seams!=null&&seams.Count>0&&seams.Count<=100000&&report.terrainProbes>0,"Schema2 requires native terrain and seam probes");
                foreach(var seam in seams)
                {var inside=V3(seam["pointOnAsphalt"]);var outside=V3(seam["pointOutside"]);float top=(float)seam["expectedAsphaltTop"],terrain=(float)seam["expectedTerrainTop"];Require(Finite(top)&&Finite(terrain),"Finite seam heights");bool road=Top("asphalt",new Vector3(inside.x,top,inside.z),out float roadY),grass=Top("natural",new Vector3(outside.x,terrain,outside.z),out float grassY);float delta=road&&grass?Mathf.Max(Mathf.Abs(roadY-top),Mathf.Abs(grassY-terrain)):0;if(!road||!grass||delta>.0002f)report.seamRayFailures++;report.maxSeamPointDelta=Mathf.Max(report.maxSeamPointDelta,delta);report.seamPairs++;}
            }
        }
        void SetupCarAndCamera()
        {
            var sections=probes["crossSections"] as JArray;Require(sections!=null&&sections.Count>0&&sections.Count<=256,"Cross sections");
            var section=sections.FirstOrDefault(s=>(string)s["id"]=="hall-default-start")??sections[0];var row=((JArray)section["points"]).Select(V3).ToArray();Require(row.Length>=3&&row.Length<=256,"Cross-section row");
            middle=row[row.Length/2];right=(row[row.Length-1]-row[0]);right.y=0;rowWidth=right.magnitude;Require(rowWidth>2&&rowWidth<30,"Cross-section width");right.Normalize();forward=Vector3.Cross(right,Vector3.up).normalized;
            var p=middle-right*rowWidth*.24f;Require(Top("asphalt",p,out float y),"Car anchor has real uploaded asphalt");p.y=y+.7f;
            var owner=new GameObject("Calibrated Brook on prototype road");owner.layer=8;owner.transform.SetParent(transform,false);owner.transform.SetPositionAndRotation(p,Quaternion.LookRotation(forward,Vector3.up));
            var box=owner.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
            var visual=owner.AddComponent<CarVisual>();visual.Build(new Color(.05f,.24f,.55f),true,0);car=owner.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;car.NetworkHandbrake=1;car.ApplyTuning(new Core.OwnedCar{model=0});
            foreach(var child in owner.GetComponentsInChildren<Transform>(true))child.gameObject.layer=8;
            foreach(var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))c.enabled=false;
            var go=new GameObject("Geometry proof camera");go.transform.SetParent(transform,false);camera=go.AddComponent<Camera>();camera.nearClipPlane=.06f;camera.farClipPlane=500;camera.fieldOfView=58;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.45f,.58f,.7f);
            var light=new GameObject("Geometry proof daylight").AddComponent<Light>();light.transform.SetParent(transform,false);light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(48,-25,0);
            RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.45f,.52f,.62f);RenderSettings.ambientEquatorColor=new Color(.3f,.33f,.36f);RenderSettings.ambientGroundColor=new Color(.18f,.16f,.14f);
        }
        IEnumerator Capture(string name,Vector3 position,Vector3 target)
        {camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up));for(int i=0;i<4;i++)yield return new WaitForEndOfFrame();string path=prefix+"-"+name+".png";var image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(path,image.EncodeToPNG());captures.Add(path);}finally{Destroy(image);}}
        IEnumerator Run()
        {
            Require(!FindAnyObjectByType<GameSession>(),"Normal gameplay must not start in geometry proof");Require(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Native rendering device required");
            ValidateManifest();Screen.SetResolution(2560,1600,FullScreenMode.Windowed);QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Time.timeScale=1;BuildMeshes();CheckProbes();SetupCarAndCamera();
            camera.transform.SetPositionAndRotation(middle-forward*18+Vector3.up*12,Quaternion.LookRotation(forward*18-Vector3.up*12,Vector3.up));
            double began=Time.realtimeSinceStartupAsDouble;while(Time.realtimeSinceStartupAsDouble-began<6)yield return null;
            Require(Screen.width==2560&&Screen.height==1600,"Native geometry capture resolution");
            report.wheelContacts=car.WheelGrounded.Count(v=>v);report.carDamage=car.Damage;report.carSpeed=car.Body.linearVelocity.magnitude;report.carRoll=Mathf.Abs(Mathf.DeltaAngle(car.transform.eulerAngles.z,0));
            report.carPosition=car.Body.position;report.externalWheelSupport=true;report.wheelRoadGaps=new float[4];
            for(int i=0;i<4;i++)
            {
                var origin=car.transform.TransformPoint(VehicleGeometry.Anchor(0,i));
                report.externalWheelSupport&=Physics.Raycast(origin,-car.transform.up,out var support,.78f,~(1<<8),QueryTriggerInteraction.Ignore)&&!support.collider.transform.IsChildOf(car.transform);
                var wheel=car.Visual.Wheels[i];var points=wheel.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh&&f.sharedMesh.isReadable).SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v))).ToArray();Require(points.Length>0,"Actual readable wheel geometry");var lowest=points.OrderBy(v=>v.y).First();Require(Top("asphalt",lowest,out float surface),"Actual tyre lowest point has asphalt support");report.wheelRoadGaps[i]=lowest.y-surface;
            }
            car.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;car.Body.isKinematic=true;
            yield return Capture("overview",middle-forward*27-right*25+Vector3.up*25,middle+forward*7);
            yield return Capture("road-eye",middle-forward*14+right*rowWidth*.25f+Vector3.up*1.6f,middle+forward*16+Vector3.up*.4f);
            yield return Capture("crossfall",middle-forward*4+right*11+Vector3.up*2.4f,middle+Vector3.up*.4f);
            Finish();
        }
        void Finish(string error=null)
        {
            if(done)return;done=true;report.error=error;report.version=Application.version;report.fixtureFolder=folder;report.manifestSHA256=manifestHash;report.realCareerUnchanged=(File.Exists(realCareer)?Core.DataPackageRules.HashFile(realCareer):null)==realHash;report.fixtureUnchanged=hashes.Count==5&&hashes.All(p=>File.Exists(Path.Combine(folder,p.Key))&&Core.DataPackageRules.HashFile(Path.Combine(folder,p.Key))==p.Value);report.noGameSession=!FindAnyObjectByType<GameSession>();report.runtimeErrors=errors;report.firstErrors=messages.ToArray();report.results=results.ToArray();report.terrainResults=terrainResults.ToArray();report.captures=captures.ToArray();report.width=Screen.width;report.height=Screen.height;
            report.scope="Two-road exported authority subgraph rendered as supplied packets, with actual uploaded mesh/collider ray diagnostics and calibrated car static contact. Roadbed is asphalt translated-0.55m only. Natural terrain/verge conformance, full world geometry13 integration, paint, provider updates, Windows, driving/performance and final artwork acceptance remain separate. Raw ray mismatches are retained, never filtered or silently waived.";
            bool pass=error==null&&errors==0&&report.realCareerUnchanged&&report.fixtureUnchanged&&report.noGameSession&&report.uploadedVerticesExact&&report.roadMissing==0&&report.bedMissing==0&&report.rawRayMismatches==0&&report.terrainMissing==0&&report.terrainRayMismatches==0&&report.underRoadCoverFailures==0&&report.underRoadGapFailures==0&&report.seamRayFailures==0&&report.wheelContacts==4&&report.externalWheelSupport&&report.wheelRoadGaps!=null&&report.wheelRoadGaps.All(g=>Mathf.Abs(g)<.02f)&&report.carDamage<.001f&&report.carSpeed<.05f&&report.carRoll<5&&captures.Count==3;
            report.status=pass?(report.schemaVersion==2?"PASS_DIAGNOSTIC_ONLY":"PASS"):"FAIL";
            if(report.schemaVersion==2)report.scope="Natural terrain/viewer DIAGNOSTIC ONLY: retain upstream positive-area steep-face and cut/fill issues. Native raw probe/seam/cover gates are independent. terrainAccepted remains false; no full-world geometry13, final terrain smoothness, retaining-feature or playable release acceptance.";
            try{File.WriteAllText(prefix+".json",JsonUtility.ToJson(report,true));}finally{Application.Quit(pass?0:1);}
        }
    }
}
