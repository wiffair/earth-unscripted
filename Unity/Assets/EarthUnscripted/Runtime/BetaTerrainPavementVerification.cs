using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted
{
    // Explicit nonce-isolated Kessock landing proof. Geographic coordinates are
    // regression anchors only; the production clearance contains no site rules.
    public sealed class BetaTerrainPavementVerification : MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),errors=new List<string>();readonly JObject evidence=new JObject();
        string Output=>SinglePlayerBeta.VerificationOutput;
        readonly struct RoadTriangle
        {
            public readonly Vector3 a,b,c;
            public RoadTriangle(Vector3 p,Vector3 q,Vector3 r){a=p;b=q;c=r;}
            public bool Sample(float x,float z,out float y)
            {
                y=0;float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(denominator)<.000001f)return false;
                float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/denominator,v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/denominator;
                if(u<-.000001f||v<-.000001f||u+v>1.000001f)return false;y=a.y*u+b.y*v+c.y*(1-u-v);return true;
            }
        }
        void Awake(){if(string.IsNullOrEmpty(Output))throw new InvalidOperationException("Isolated pavement proof required");Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        IEnumerator Start()
        {
            string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0){bool more=false;object next=null;try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception error){failure=error.ToString();}if(failure!=null)break;if(!more){stack.Pop();continue;}if(next is IEnumerator nested){stack.Push(nested);continue;}yield return next;}
            evidence["passed"]=failure==null&&errors.Count==0;evidence["failure"]=failure;evidence["checks"]=new JArray(checks);evidence["errors"]=new JArray(errors);evidence["graphics"]=GameGraphics.Name;
            if(failure!=null)yield return Capture("failure-view");File.WriteAllText(Path.Combine(Output,"result.json"),evidence.ToString());Application.Quit((bool)evidence["passed"]?0:1);
        }
        IEnumerator Capture(string name){yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}finally{Destroy(image);}}
        IEnumerator Exercise()
        {
            GameSession session=null;double until=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady){if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Landing proof boot");yield return null;}
            var host=session.World.AutomaticWorld;Check(host&&host.Ready,"Normal streamed world is active");Check(Math.Abs(host.Region.latitude-57.47908)<.01,"Inverness frame selected for the Kessock regression");
            session.Profile.performanceStats=true;session.Profile.weatherMode=0;session.Profile.fixedHour=14;session.ResumeDrive();
            while(session.Menu||session.PreparingStart)yield return null;session.Car.StartLocked=true;session.Input.Throttle=0;session.Input.Brake=1;
            var world=host.Renderer;var anchor=new Vector3(-727.17084f,0,2520.6887f);until=Time.realtimeSinceStartupAsDouble+120;
            while(!world.CoastalBedsReady||!world.CoastalSurfaceReady||!world.HasGeometryAt(anchor)){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Supported landing and coastal beds did not publish");yield return null;}
            yield return new WaitForSecondsRealtime(1);
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;var roots=(Dictionary<Vector2Int,GameObject>)typeof(NewbridgeWorld).GetField("chunks",hidden).GetValue(world);
            var triangles=new List<RoadTriangle>();var grid=new Dictionary<Vector2Int,List<int>>();int terrainMeshPairs=0,meshMismatches=0;
            foreach(var pair in roots)
            {
                if(!pair.Value||!pair.Value.activeInHierarchy)continue;var terrain=pair.Value.transform.Find("Terrain");if(terrain)
                {var f=terrain.GetComponent<MeshFilter>();var collider=terrain.GetComponent<MeshCollider>();if(f&&f.sharedMesh&&collider){terrainMeshPairs++;if(collider.sharedMesh!=f.sharedMesh)meshMismatches++;}}
                var child=pair.Value.transform.Find("Road asphalt");if(!child)continue;var filter=child.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;
                var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;var matrix=child.localToWorldMatrix;
                for(int i=0;i+2<indices.Length;i+=3)
                {
                    var a=matrix.MultiplyPoint3x4(vertices[indices[i]]);var b=matrix.MultiplyPoint3x4(vertices[indices[i+1]]);var c=matrix.MultiplyPoint3x4(vertices[indices[i+2]]);
                    if(Mathf.Abs(Vector3.Cross(b-a,c-a).y)<.000001f)continue;
                    float minX=Mathf.Min(a.x,Mathf.Min(b.x,c.x)),maxX=Mathf.Max(a.x,Mathf.Max(b.x,c.x)),minZ=Mathf.Min(a.z,Mathf.Min(b.z,c.z)),maxZ=Mathf.Max(a.z,Mathf.Max(b.z,c.z));
                    if(minX>anchor.x+75||maxX<anchor.x-75||minZ>anchor.z+75||maxZ<anchor.z-75)continue;
                    int index=triangles.Count;triangles.Add(new RoadTriangle(a,b,c));
                    for(int z=Mathf.FloorToInt(minZ/4);z<=Mathf.FloorToInt(maxZ/4);z++)for(int x=Mathf.FloorToInt(minX/4);x<=Mathf.FloorToInt(maxX/4);x++)
                    {var key=new Vector2Int(x,z);if(!grid.TryGetValue(key,out var rows))grid[key]=rows=new List<int>();rows.Add(index);}
                }
            }
            bool HighestRoad(float x,float z,out float y)
            {
                y=float.NegativeInfinity;if(!grid.TryGetValue(new Vector2Int(Mathf.FloorToInt(x/4),Mathf.FloorToInt(z/4)),out var rows))return false;
                foreach(int index in rows)if(triangles[index].Sample(x,z,out float h))y=Mathf.Max(y,h);return !float.IsNegativeInfinity(y);
            }
            var failures=new JArray();int inspected=0,unpublished=0;float maximumExcess=float.NegativeInfinity;
            foreach(var road in triangles)foreach(var weights in new[]{new Vector3(1/3f,1/3f,1/3f),new Vector3(.6f,.2f,.2f),new Vector3(.2f,.6f,.2f),new Vector3(.2f,.2f,.6f)})
            {
                var point=road.a*weights.x+road.b*weights.y+road.c*weights.z;if(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(anchor.x,anchor.z))>70||!HighestRoad(point.x,point.z,out float pavement))continue;
                if(!world.TryPublishedTerrainHeight(point.x,point.z,out float grass)){unpublished++;continue;}inspected++;float excess=grass-pavement;maximumExcess=Mathf.Max(maximumExcess,excess);
                if(excess>.005f&&failures.Count<40)failures.Add(new JObject{["x"]=point.x,["z"]=point.z,["terrainY"]=grass,["asphaltY"]=pavement,["excess"]=excess});
            }
            bool pointPaved=HighestRoad(anchor.x,anchor.z,out float deck),pointSupported=world.TryPublishedTerrainHeight(anchor.x,anchor.z,out float terrainY);
            evidence["samples"]=inspected;evidence["unpublished"]=unpublished;evidence["maximumGrassExcess"]=maximumExcess;evidence["failures"]=failures;evidence["terrainMeshPairs"]=terrainMeshPairs;evidence["meshMismatches"]=meshMismatches;
            evidence["clearedTerrainTriangles"]=world.ClearedTerrainPavementTriangles;evidence["maximumClearanceWorkerMs"]=world.MaximumTerrainPavementWorkerMilliseconds;evidence["finishedCacheHits"]=world.FinishedCacheHits;
            evidence["landingProbe"]=new JObject{["x"]=anchor.x,["z"]=anchor.z,["paved"]=pointPaved,["supported"]=pointSupported,["asphaltY"]=deck,["terrainY"]=terrainY};
            session.View.enabled=false;var camera=session.View.GetComponent<Camera>();var site=new Vector3(anchor.x,pointPaved?deck:session.Car.Body.position.y,anchor.z);
            camera.transform.SetPositionAndRotation(site+new Vector3(-11,4,9),Quaternion.LookRotation(new Vector3(11,-3.7f,-9),Vector3.up));for(int i=0;i<12;i++)yield return null;yield return Capture("landing-overview");
            camera.transform.SetPositionAndRotation(site+new Vector3(5,1.8f,-5),Quaternion.LookRotation(new Vector3(-5,-1.5f,5),Vector3.up));for(int i=0;i<12;i++)yield return null;yield return Capture("landing-road-edge");
            Check(terrainMeshPairs>0&&meshMismatches==0,"Published terrain visuals and cooked colliders use the same emitted mesh");
            Check(inspected>=200,"Actual emitted asphalt triangles sampled across the landing");Check(failures.Count==0,"No visible terrain protrudes above the highest actual pavement surface at the landing");
            Check(pointPaved&&pointSupported&&terrainY<=deck-.08f,"Original intrusive landing triangle now remains below its actual asphalt footprint");
            evidence["scope"]="Normal streamed landing; actual asphalt triangle planes and matching published terrain mesh/collider, highest overlapping pavement to exclude legitimate upper levels. Lower-crossing/tunnel eligibility is independently verified in Editor fixtures.";
        }
    }
}
