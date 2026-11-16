using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EarthUnscripted
{
    public sealed class SharedGraphicsVerification:MonoBehaviour
    {
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        GameSession session;Gamepad pad;
        string Output=>SinglePlayerBeta.VerificationOutput;
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);}
        void Check(bool value,string name)
        {File.WriteAllText(Path.Combine(Output,"progress.json"),new JObject{["check"]=name,["passed"]=value}.ToString());if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        IEnumerator Start()
        {
            Exception failure=null;var work=new Stack<IEnumerator>();work.Push(Exercise());
            while(work.Count>0)
            {
                bool more=false;object item=null;
                try{more=work.Peek().MoveNext();if(more)item=work.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null)break;if(!more){work.Pop();continue;}
                if(item is IEnumerator nested){work.Push(nested);continue;}yield return item;
            }
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(Output,"result.json"),new JObject{["passed"]=passed,["failure"]=failure?.ToString(),["errors"]=new JArray(errors),["checks"]=new JArray(checks),["version"]=Application.version,["graphics"]=GameGraphics.Name,["actualPipeline"]=RenderPipelineManager.currentPipeline?.GetType().FullName,["persistentDataPath"]=Application.persistentDataPath,["betaDataPath"]=SinglePlayerBeta.DataRoot,["geometryIdentity"]=GeometryCacheVersion.Identity}.ToString());
            Application.Quit(passed?0:1);
        }
        IEnumerator Capture(string name)
        {
            yield return null;yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var pixels=image.GetPixels32();int pink=pixels.Count(p=>p.r>220&&p.b>220&&p.g<60);
                Check(pink<pixels.Length*.005,name+" has no missing-shader magenta");
                if(name=="after-driving")
                {
                    int scenePixels=0,white=0;
                    for(int y=0;y<image.height*2/3;y++)for(int x=image.width/2;x<image.width;x++)
                    {var p=pixels[y*image.width+x];scenePixels++;if(p.r>245&&p.g>245&&p.b>245)white++;}
                    Check(white<scenePixels*.4,"Driving scenery is not overexposed white");
                }
                File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            }
            finally{Destroy(image);}
        }
        IEnumerator Exercise()
        {
            Check(Output!=null,"Explicit isolated graphics proof");
            double end=Time.realtimeSinceStartupAsDouble+600;
            while(!(session=FindFirstObjectByType<GameSession>())||!session.BootReady)
            {if(session&&session.BootFailed)throw new IOException(session.BootStatus);if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Graphics proof startup");yield return null;}
            session.SetMenu(true);
            yield return new WaitForSecondsRealtime(3);
            Check((RenderPipelineManager.currentPipeline is HDRenderPipeline)==GameGraphics.HighDefinition,"Actual renderer matches the selected build");
            Check(!Application.persistentDataPath.ToLowerInvariant().Contains("hddemo"),"Playable beta uses no old demo save directory");
            if(GameGraphics.HighDefinition)
            {
                Check(EarthUnscriptedSkyRenderer.ScreenDraws>0&&EarthUnscriptedSkyRenderer.BakeDraws>0,"Astronomical sky is drawn and supplies HDRP ambient light");
                var data=HDCamera.GetOrCreate(Camera.main);var value=data.volumeStack.GetComponent<Exposure>();
                File.WriteAllText(Path.Combine(Output,"hd-lighting.json"),new JObject{["exposureFrame"]=data.frameSettings.IsEnabled(FrameSettingsField.ExposureControl),["exposureMode"]=value.mode.value.ToString(),["exposureEV"]=value.fixedExposure.value,["sunLux"]=session.World.Sun.intensity,["skyDraws"]=EarthUnscriptedSkyRenderer.ScreenDraws,["skyBakes"]=EarthUnscriptedSkyRenderer.BakeDraws,["skySource"]=EarthUnscriptedSkyRenderer.SourceShader,["skyType"]=data.volumeStack.GetComponent<VisualEnvironment>().skyType.value,["cameraClear"]=Camera.main.GetComponent<HDAdditionalCameraData>().clearColorMode.ToString(),["sky"]=AstronomicalSky.CurrentMaterial?.shader.name,["skyDaylight"]=AstronomicalSky.CurrentMaterial?AstronomicalSky.CurrentMaterial.GetFloat("_Daylight"):0}.ToString());
                Check(FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l=>l.type==LightType.Directional&&l.shadows!=LightShadows.None)<=1,"Only the dominant celestial light casts HDRP directional shadows");
            }
            string marker=Path.Combine(SinglePlayerBeta.DataRoot,"shared-progress-proof.json");
            if(File.Exists(marker))
            {
                var saved=JObject.Parse(File.ReadAllText(marker));
                Check(session.Profile.garage.activeId==(string)saved["activeId"]&&session.Economy.Credits==(int)saved["credits"],"Other graphics build resumes the exact owned car and credits");
            }
            yield return Radio.RadioNativeVerification.Resume(session,Check);
            yield return Capture("game-menu");
            // Exercise real human-input driving with the same physics code.
            pad=InputSystem.AddDevice<Gamepad>("SharedGraphicsProof");pad.MakeCurrent();
            session.ResumeDrive();
            while(session.PreparingStart){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Driving preparation");yield return null;}
            var car=session.Car;car.StartLocked=false;
            var start=car.Body.position;session.Input.enabled=true;
            InputSystem.QueueStateEvent(pad,new GamepadState{rightTrigger=.3f});
            for(double until=Time.realtimeSinceStartupAsDouble+5;Time.realtimeSinceStartupAsDouble<until;)yield return null;
            InputSystem.QueueStateEvent(pad,new GamepadState());session.SetMenu(true);
            var travelled=car.Body.position-start;travelled.y=0;
            Check(!car.NetworkDriven&&travelled.magnitude>1&&Mathf.Abs(car.Body.position.y-start.y)<15,"Human-input car drives on real generated road in this renderer");
            car.Body.linearVelocity=car.Body.angularVelocity=Vector3.zero;
            yield return Capture("after-driving");
            var scenery=FindFirstObjectByType<AutomaticScenery>();
            var materials=new JArray();foreach(var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if(!material||!material.name.StartsWith("Owned tree views"))continue;
                materials.Add(new JObject{["name"]=material.name,["shader"]=material.shader.name,["supported"]=material.shader.isSupported,["mode"]=material.HasProperty("_EarthUnscriptedMode")?material.GetFloat("_EarthUnscriptedMode"):-1,["cutoff"]=material.GetFloat("_Cutoff"),["viewsBound"]=material.GetTexture("_Views")!=null,["instancing"]=material.enableInstancing});
            }
            File.WriteAllText(Path.Combine(Output,"scenery.json"),new JObject{["availableTrees"]=scenery?scenery.AvailableTrees:0,["visibleTrees"]=scenery?scenery.VisibleTrees:0,["detailedTrees"]=scenery?scenery.DetailedTrees:0,["materials"]=materials}.ToString());
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta35Proof")>=0)
            {var extra=gameObject.AddComponent<Beta35Verification>();yield return extra.Exercise(session,pad,Check);Destroy(extra);}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta36Proof")>=0)
            {var extra=gameObject.AddComponent<Beta36Verification>();yield return extra.Exercise(session,pad,Check);Destroy(extra);}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta36ServicesProof")>=0)
                yield return Beta36ServiceVerification.Exercise(session,Check);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta36PublishedProof")>=0)
                yield return Beta36PublishedVerification.Exercise(session,Check);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta43Proof")>=0)yield return Beta43NativeVerification.Exercise(session,pad,Check);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta42Proof")>=0)yield return Beta42NativeVerification.Exercise(session,pad,Check);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaRadioProof")>=0)yield return Radio.RadioNativeVerification.Exercise(session,pad,Check);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta37Proof")>=0)
                yield return Beta37Verification.Exercise(session,Check);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta35WaterProof")>=0)
                yield return Beta35WaterVerification.Exercise(session,Check);
            // Retiring a chunk must never destroy the sphere used by pedestrians
            // or the shared meshes used by standard traffic-sign clones.
            var personRoot=new GameObject("Pedestrian lifetime proof");
            var living=personRoot.AddComponent<LivingScenery>();living.enabled=false;
            var actor=typeof(LivingScenery).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(living,new object[]{0,0});
            var person=(Transform)actor.GetType().GetField("Root").GetValue(actor);
            var head=person.Find("Head").GetComponent<MeshFilter>();var sphere=head.sharedMesh;
            Check(sphere&&sphere.vertexCount>20,"Actual pedestrian model contains a head mesh");
            var chunk=new GameObject("Disposable streamed chunk");
            Geometry.Primitive(PrimitiveType.Sphere,"Shared sphere in old chunk",chunk.transform,Vector3.zero,Vector3.one,Geometry.Material("Lifetime proof",Color.gray));
            var generated=Instantiate(sphere);var own=new GameObject("Owned generated mesh",typeof(MeshFilter),typeof(GeneratedMeshOwner));own.transform.SetParent(chunk.transform);own.GetComponent<MeshFilter>().sharedMesh=generated;own.GetComponent<GeneratedMeshOwner>().Mesh=generated;
            var renderer=FindFirstObjectByType<NewbridgePrototype.NewbridgeWorld>();
            typeof(NewbridgePrototype.NewbridgeWorld).GetMethod("DestroyChunk",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(renderer,new object[]{chunk});
            yield return null;yield return null;
            Check(sphere&&head.sharedMesh==sphere&&sphere.vertexCount>20,"Pedestrian head survives streamed-chunk retirement");
            Check(!generated,"Owned generated mesh is released on chunk retirement");
            // Photograph the real model, close enough to verify head and face.
            session.Input.enabled=false;session.View.enabled=false;
            foreach(var ui in FindObjectsByType<DrivingUI>(FindObjectsSortMode.None))ui.enabled=false;
            foreach(var indicator in FindObjectsByType<BetaIndicator>(FindObjectsSortMode.None))indicator.enabled=false;
            foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))camera.enabled=false;
            foreach(var t in personRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            person.position=Vector3.zero;person.rotation=Quaternion.identity;
            var cameraObject=new GameObject("Pedestrian portrait camera");var portrait=cameraObject.AddComponent<Camera>();portrait.cullingMask=1<<30;
            portrait.transform.position=new Vector3(0,1.5f,3.1f);portrait.transform.LookAt(new Vector3(0,1.2f,0));portrait.fieldOfView=38;
            portrait.clearFlags=CameraClearFlags.SolidColor;portrait.backgroundColor=new Color(.08f,.12f,.16f);
            if(GameGraphics.HighDefinition)
            {
                var data=cameraObject.AddComponent<HDAdditionalCameraData>();data.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;data.backgroundColorHDR=portrait.backgroundColor;data.volumeLayerMask=~0;
                var exposure=new GameObject("Fixed portrait exposure").AddComponent<Volume>();exposure.isGlobal=true;exposure.priority=200;exposure.profile=ScriptableObject.CreateInstance<VolumeProfile>();
                var value=exposure.profile.Add<Exposure>(true);value.mode.Override(ExposureMode.Fixed);value.fixedExposure.Override(12);
            }
            yield return new WaitForSecondsRealtime(2);yield return Capture("pedestrian-head");
            Check(head.GetComponent<Renderer>().isVisible,"Surviving pedestrian head is visible to the rendered camera");
            File.WriteAllText(marker,new JObject{["activeId"]=session.Profile.garage.activeId,["credits"]=session.Economy.Credits}.ToString());
            Check(errors.Count==0,"No native graphics/gameplay exceptions");
        }
    }
}
