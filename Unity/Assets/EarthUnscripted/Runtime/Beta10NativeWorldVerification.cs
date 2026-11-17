using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    // Explicit nonce-isolated normal-player proof; does not alter production data.
    public sealed class Beta10NativeWorldVerification : MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),errors=new List<string>(),captures=new List<string>();
        readonly JObject evidence=new JObject();GameSession session;
        string Output=>SinglePlayerBeta.VerificationOutput;
        void Awake(){if(string.IsNullOrEmpty(Output))throw new InvalidOperationException("Isolated beta proof required");Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        IEnumerator Start()
        {
            string failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0)
            {
                object current=null;bool more=false;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception e){failure=e.ToString();}
                if(failure!=null)break;if(!more){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(failure!=null)
            {
                yield return new WaitForEndOfFrame();var failureView=ScreenCapture.CaptureScreenshotAsTexture();
                try{File.WriteAllBytes(Path.Combine(Output,"failure-view.png"),failureView.EncodeToPNG());captures.Add("failure-view.png");}finally{Destroy(failureView);}
            }
            evidence["passed"]=failure==null&&errors.Count==0;evidence["failure"]=failure;evidence["checks"]=new JArray(checks);evidence["errors"]=new JArray(errors);evidence["captures"]=new JArray(captures);
            evidence["version"]=Application.version;evidence["graphics"]=GameGraphics.Name;evidence["dataRoot"]=SinglePlayerBeta.DataRoot;
            File.WriteAllText(Path.Combine(Output,"result.json"),evidence.ToString());Application.Quit((bool)evidence["passed"]?0:1);
        }
        void Progress(string stage)
        {
            var world=session?.World?.AutomaticWorld?.Renderer;
            File.WriteAllText(Path.Combine(Output,"progress.json"),new JObject{["stage"]=stage,["boot"]=session?session.BootStatus:"waiting-session",["farReady"]=world&&world.DistantTerrainReady,["residentFallback"]=world&&world.DistantTerrainUsingResidentFallback,["farFailures"]=world?world.DistantTerrainFailedRequests:0,["farBuildPhase"]=world?world.DistantTerrainBuildPhase:"none",["farBuildRow"]=world?world.DistantTerrainBuildRow:0,["farAborts"]=world?world.DistantTerrainAbortedBuilds:0,["coastPhase"]=world?world.CoastalVisualPreviewPhase:"none",["coastChunks"]=world?world.CoastalVisualChunksProcessed:0,["coastChunkTotal"]=world?world.CoastalVisualChunksTotal:0}.ToString());
        }
        JArray ProjectedLochTerrain(Camera camera,NewbridgePrototype.NewbridgeWorld world)
        {
            // Diagnose the isolated silhouette needle from actual published
            // meshes; this isolated proof never runs during ordinary driving.
            var nearest=new List<(float score,JObject point)>();
            void Inspect(GameObject root,bool fine)
            {
                if(!root||!root.activeInHierarchy)return;var filter=root.GetComponent<MeshFilter>();var renderer=root.GetComponent<MeshRenderer>();if(!filter||!filter.sharedMesh||!renderer||!renderer.enabled)return;
                var vertices=filter.sharedMesh.vertices;var matrix=root.transform.localToWorldMatrix;
                for(int i=0;i<vertices.Length;i+=fine?8:1)
                {
                    var point=matrix.MultiplyPoint3x4(vertices[i]);var viewport=camera.WorldToViewportPoint(point);if(viewport.z<=0||point.y<20)continue;
                    float score=(new Vector2(viewport.x,viewport.y)-new Vector2(.3f,.55f)).sqrMagnitude;if(score>.015f)continue;
                    var row=new JObject{["x"]=point.x,["y"]=point.y,["z"]=point.z,["screenX"]=viewport.x,["screenY"]=viewport.y,["fine"]=fine};
                    int insert=nearest.FindIndex(item=>score<item.score);if(insert<0)insert=nearest.Count;nearest.Insert(insert,(score,row));if(nearest.Count>24)nearest.RemoveAt(nearest.Count-1);
                }
            }
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(NewbridgePrototype.NewbridgeWorld);
            Inspect((GameObject)type.GetField("farRoot",flags).GetValue(world),false);
            foreach(DictionaryEntry entry in (IDictionary)type.GetField("chunks",flags).GetValue(world)){var chunk=(GameObject)entry.Value;if(chunk){var terrain=chunk.transform.Find("Terrain");if(terrain)Inspect(terrain.gameObject,true);}}
            return new JArray(nearest.Select(item=>item.point));
        }
        IEnumerator Exercise()
        {
            double until=Time.realtimeSinceStartupAsDouble+600,nextProgress=0;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {
                if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Fort William ordinary boot");
                if(Time.realtimeSinceStartupAsDouble>=nextProgress){Progress("normal-boot");nextProgress=Time.realtimeSinceStartupAsDouble+2;}yield return null;
            }
            var host=session.World.AutomaticWorld;Check(host&&host.Ready,"Ordinary selected area activates its owned automatic world");
            var region=host.Region;evidence["region"]=region.id;evidence["latitude"]=region.latitude;evidence["longitude"]=region.longitude;
            Check(Math.Abs(region.latitude-56.81648)<.01&&Math.Abs(region.longitude+5.11208)<.01,"Normal player uses the supplied Fort William region seed");
            session.Profile.performanceStats=true;session.Profile.weatherMode=0;session.Profile.fixedHour=14;session.ResumeDrive();
            until=Time.realtimeSinceStartupAsDouble+300;
            while(session.Menu||session.PreparingStart){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Fort William drive entry");yield return null;}
            session.Car.StartLocked=true;var renderer=host.Renderer;until=Time.realtimeSinceStartupAsDouble+90;nextProgress=0;
            var coastalTransitions=new JArray();double nextCoastInspection=0;
            while(!renderer.DistantTerrainReady||renderer.DistantTerrainUsingResidentFallback||!renderer.CoastalSurfaceReady||!renderer.DistantTerrainCoastReady||!renderer.CoastalBedsReady)
            {
                if(renderer.CoastalSurfaceReady&&Time.realtimeSinceStartupAsDouble>=nextCoastInspection)
                {
                    nextCoastInspection=Time.realtimeSinceStartupAsDouble+1;var coastState=renderer.InspectVisibleCoastalTerrain(session.Car.Body.position,2000);
                    var row=JObject.Parse(JsonUtility.ToJson(coastState));row["bedsReady"]=renderer.CoastalBedsReady;coastalTransitions.Add(row);
                    evidence["coastalTransitionChecks"]=coastalTransitions;
                    if(coastState.incorrectAboveSea>0)
                    {
                        File.WriteAllText(Path.Combine(Output,"coastal-transition-failure.json"),row.ToString());yield return new WaitForEndOfFrame();
                        var failedView=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(Output,"coastal-transition-failure.png"),failedView.EncodeToPNG());}finally{Destroy(failedView);}
                    }
                    Check(coastState.incorrectAboveSea==0,"No visible old fine-terrain sea cliffs while coastal beds rebuild");
                }
                if(Time.realtimeSinceStartupAsDouble>=nextProgress){Progress("distant-terrain-request");nextProgress=Time.realtimeSinceStartupAsDouble+2;}
                if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Actual distant terrain did not replace resident fallback within90seconds");yield return null;
            }
            evidence["coastalTransitionChecks"]=coastalTransitions;
            var settledCoast=renderer.InspectVisibleCoastalTerrain(session.Car.Body.position,2000);evidence["visibleFineSeaTerrain"]=JObject.Parse(JsonUtility.ToJson(settledCoast));
            Check(settledCoast.incorrectAboveSea==0,"Actual visible fine terrain stays below mapped sea except supported road earthworks");
            Check(renderer.DistantTerrainReady&&!renderer.DistantTerrainUsingResidentFallback,"Actual cached/downloaded distant terrain publishes, rather than fallback only");
            var root=(GameObject)typeof(NewbridgePrototype.NewbridgeWorld).GetField("farRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
            Check(root&&root.activeInHierarchy,"Distant terrain render root is active");var mesh=root.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh&&mesh.vertexCount>0&&mesh.triangles.Length>0,"Distant terrain has real visible geometry");
            float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
            foreach(var vertex in mesh.vertices){var point=root.transform.TransformPoint(vertex);minimum=Mathf.Min(minimum,point.y);maximum=Mathf.Max(maximum,point.y);}
            evidence["terrainMinimumY"]=minimum;evidence["terrainMaximumY"]=maximum;evidence["terrainVertices"]=mesh.vertexCount;evidence["visibleQuads"]=renderer.DistantTerrainVisibleQuads;evidence["farFailures"]=renderer.DistantTerrainFailedRequests;evidence["usingFallback"]=renderer.DistantTerrainUsingResidentFallback;
            Check(maximum>500,"Fort William distant mesh contains mountain relief above500metres");
            // These exact loch coordinates exposed 50–70m coarse-data humps.
            // Check the real published mesh, not just a seabed helper fixture.
            var seaSamples=new JArray();var vertices=mesh.vertices;
            foreach(var location in new[]{new Vector3(-768,0,0),new Vector3(-768,0,256),new Vector3(-512,0,256)})
            {
                Check(renderer.IsMappedSeaAt(location),"Regression point is inside mapped Loch Linnhe water");
                float distance=float.PositiveInfinity,height=float.PositiveInfinity;
                foreach(var vertex in vertices){var point=root.transform.TransformPoint(vertex);float gap=new Vector2(point.x-location.x,point.z-location.z).sqrMagnitude;if(gap<distance){distance=gap;height=point.y;}}
                seaSamples.Add(new JObject{["x"]=location.x,["z"]=location.z,["distance"]=Mathf.Sqrt(distance),["meshY"]=height,["seaY"]=renderer.MappedSeaLevel});
                Check(distance<1&&height<renderer.MappedSeaLevel-.07f,"Actual distant loch floor stays below the water surface");
            }
            evidence["lochMeshSamples"]=seaSamples;
            var camera=session.View.GetComponent<Camera>();session.View.enabled=false;var origin=session.Car.Body.position+Vector3.up*5;
            var directions=new[]{new Vector3(-1,.05f,-.5f),new Vector3(1,.08f,-.3f),Vector3.forward,Vector3.right};
            var names=new[]{"fort-william-loch","fort-william-mountains","fort-william-north","fort-william-east"};
            var seaTerrain=new JArray();int seaChecked=0,seaWalls=0;
            var carPosition=session.Car.Body.position;
            for(float z=carPosition.z-1024;z<=carPosition.z+1536;z+=64)for(float x=carPosition.x-1536;x<=carPosition.x+768;x+=64)
            {
                var location=new Vector3(x,0,z);if(!renderer.IsInteriorMappedSea(location)||!renderer.TryPublishedTerrainHeight(x,z,out float height))continue;
                seaChecked++;
                if(height>renderer.MappedSeaLevel+.5f&&!renderer.HasDrivingSurfaceAt(new Vector3(x,height+.2f,z)))
                {seaWalls++;if(seaTerrain.Count<60)seaTerrain.Add(new JObject{["x"]=x,["z"]=z,["publishedY"]=height,["seaY"]=renderer.MappedSeaLevel});}
            }
            evidence["coastalBedsReady"]=renderer.CoastalBedsReady;evidence["publishedSeaSamples"]=seaChecked;evidence["aboveSeaTerrainWalls"]=seaWalls;evidence["seaTerrainFailures"]=seaTerrain;
            Check(seaChecked>=100&&seaWalls==0,"Actual published terrain across the owner's loch view has no false grass plateaus above mapped sea");
            var oldNeedle=new Vector3(-1088,0,-1132);bool needleSupported=renderer.TryPublishedTerrainHeight(oldNeedle.x,oldNeedle.z,out float needleY);
            evidence["knownCoastalNeedle"]=new JObject{["x"]=oldNeedle.x,["z"]=oldNeedle.z,["publishedY"]=needleY,["seaY"]=renderer.MappedSeaLevel,["supported"]=needleSupported};
            Check(needleSupported&&needleY>=renderer.MappedSeaLevel-.5f&&needleY<renderer.MappedSeaLevel+3,"Actual near-shore coarse-data needle is replaced by the continuous coastal road bank");
            int treeSamples=0;var treeGrounding=new JArray();var scenery=FindFirstObjectByType<AutomaticScenery>();
            Check(scenery,"Automatic tree scenery exists");
            until=Time.realtimeSinceStartupAsDouble+120;
            while(!scenery.TreeGroundingReady){if(Time.realtimeSinceStartupAsDouble>until)throw new TimeoutException("Published tree grounding did not finish");yield return null;}
            for(int i=0;i<directions.Length;i++)
            {
                camera.transform.SetPositionAndRotation(origin,Quaternion.LookRotation(directions[i],Vector3.up));for(int frame=0;frame<12;frame++)yield return null;yield return new WaitForEndOfFrame();
                if(i==0)evidence["lochNeedleTerrainSamples"]=ProjectedLochTerrain(camera,renderer);
                var grounding=scenery.InspectPublishedTreeGrounding();treeSamples+=grounding.samples;treeGrounding.Add(JObject.Parse(JsonUtility.ToJson(grounding)));evidence["treeGrounding"]=treeGrounding;evidence["treeSamples"]=treeSamples;
                Check(grounding.unsupported==0&&grounding.maximumBaseGap<=.15f,"Submitted tree bases match actually published terrain");
                var image=ScreenCapture.CaptureScreenshotAsTexture();try{string name=names[i]+".png";File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());captures.Add(name);}finally{Destroy(image);}
            }
            Check(treeSamples>=12,"Actual rendered tree bases sampled across the Fort William views");evidence["treeGrounding"]=treeGrounding;evidence["treeSamples"]=treeSamples;
            evidence["scope"]="Normal isolated Fort William player boot and actual distant terrain mesh; screenshots require visual review. This does not prove all road/water geometry.";
        }
    }
}
