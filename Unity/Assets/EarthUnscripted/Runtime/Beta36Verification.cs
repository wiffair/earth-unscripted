using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using EarthUnscripted.Candidates.Articulated;
using EarthUnscripted.Candidates.Compatibility;
using UnityEngine.Rendering.HighDefinition;
namespace EarthUnscripted
{
    // Only constructed by an explicit isolated-profile command-line proof.
    public sealed class Beta36Verification:MonoBehaviour
    {
        string caption;string Output=>SinglePlayerBeta.VerificationOutput;
        void OnGUI(){if(!string.IsNullOrEmpty(caption))GUI.Label(new Rect(22,20,1300,44),caption,new GUIStyle(GUI.skin.label){fontSize=22,normal={textColor=Color.white}});}
        IEnumerator Photo(string name,Action<Texture2D> inspect=null)
        {
            yield return null;yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();
            try{inspect?.Invoke(image);File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}finally{Destroy(image);}
        }
        static int Pink(Texture2D image)=>image.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<60);
        public IEnumerator Exercise(GameSession session,Gamepad pad,Action<bool,string> check)
        {
            check(SinglePlayerBeta.DataRoot.Contains("/private/tmp/"),"Beta36 fixture uses isolated progress");
            var profile=session.Profile;var car=session.Car;var ui=FindFirstObjectByType<DrivingUI>();var indicators=FindObjectsByType<BetaIndicator>(FindObjectsSortMode.None);
            int weather=profile.weatherMode;bool trains=profile.simulatedTrains;profile.simulatedTrains=false;profile.surfaceTracks=true;
            var trackSystem=car.GetComponent<TyreEffects>()?SurfaceTrackSystem.Ensure(session):null;
            check(trackSystem&&trackSystem.Mesh,"Actual tyre effects own one bounded weather-track mesh");
            var point=car.Body.position;var rotation=car.Body.rotation;var camera=Camera.main;var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;float savedFov=camera.fieldOfView;int savedMask=camera.cullingMask;
            var particles=FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None);var particleStates=particles.Select(p=>p.enabled).ToArray();
            foreach(int mode in new[]{6,1})
            {
                session.SetMenu(false);session.View.enabled=true;car.Body.isKinematic=false;car.enabled=true;car.StartLocked=false;
                car.Recover(point,rotation);session.Weather=mode;yield return null;yield return null;
                var tyreIdentity=car.GetComponent<TyreEffects>().GetEntityId();int before=trackSystem.SegmentsFor(tyreIdentity);
                InputSystem.QueueStateEvent(pad,new GamepadState{rightTrigger=.4f});
                for(double end=Time.realtimeSinceStartupAsDouble+4.5;Time.realtimeSinceStartupAsDouble<end;)yield return null;
                InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;session.SetMenu(true);car.Body.linearVelocity=car.Body.angularVelocity=Vector3.zero;car.Body.isKinematic=true;
                check(trackSystem.SegmentsFor(tyreIdentity)>before+8,(mode==6?"Snow":"Wet")+" tracks are emitted by the actual grounded driving car");
                session.View.enabled=false;ui.enabled=false;foreach(var indicator in indicators)indicator.enabled=false;foreach(var particle in particles)if(particle)particle.enabled=false;
                bool conditionsEnabled=session.Conditions.enabled;session.Conditions.enabled=false;
                Vector3 focus=Vector3.Lerp(point,car.Body.position,.58f);camera.transform.position=focus+Vector3.up*17-rotation*Vector3.forward*3;camera.transform.LookAt(focus,rotation*Vector3.forward);camera.fieldOfView=48;
                Color32[] baseline=null;int changed=0;long difference=0;
                profile.surfaceTracks=false;yield return null;yield return Photo("beta36-"+mode+"-without-tracks",image=>baseline=image.GetPixels32());
                profile.surfaceTracks=true;yield return null;yield return new WaitForSecondsRealtime(.5f);
                yield return Photo("beta36-"+mode+"-tracks",image=>{
                    var pixels=image.GetPixels32();for(int y=image.height/6;y<image.height*5/6;y++)for(int x=image.width/4;x<image.width*3/4;x++)
                    {int index=y*image.width+x;var a=baseline[index];var b=pixels[index];int delta=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);if(delta>12)changed++;difference+=delta;}
                    check(Pink(image)<pixels.Length*.005,"Weather tyre tracks have no missing-shader magenta");
                });
                check(changed>40&&difference>3000,(mode==6?"Snow":"Wet")+" tracks visibly alter the real rendered road surface");
                File.WriteAllText(Path.Combine(Output,"track-difference-"+mode+".json"),new JObject{["changedRoadPixels"]=changed,["totalDifference"]=difference,["activeSegments"]=trackSystem.ActiveSegments,["snowTotal"]=trackSystem.SnowSegments,["wetTotal"]=trackSystem.WetSegments}.ToString());
                // Isolate the actual trail mesh against black. This cannot pass
                // because clouds, temporal lighting or road shadows changed.
                var isolatedRoot=new GameObject("Weather track visibility camera");var isolated=isolatedRoot.AddComponent<Camera>();isolated.cullingMask=1<<29;
                isolated.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);isolated.fieldOfView=camera.fieldOfView;isolated.clearFlags=CameraClearFlags.SolidColor;isolated.backgroundColor=Color.black;
                if(GameGraphics.HighDefinition){var hd=isolatedRoot.AddComponent<HDAdditionalCameraData>();hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=Color.black;hd.volumeLayerMask=~0;hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;}
                int oldLayer=trackSystem.Renderer.gameObject.layer;trackSystem.Renderer.gameObject.layer=29;camera.enabled=false;
                yield return new WaitForSecondsRealtime(.4f);int visible=0;
                yield return Photo("beta36-"+mode+"-track-only",image=>visible=image.GetPixels32().Count(pixel=>pixel.r+pixel.g+pixel.b>18));
                check(visible>100,(mode==6?"Snow":"Wet")+" trail pixels actually render in isolation from terrain and lighting changes");
                camera.enabled=true;trackSystem.Renderer.gameObject.layer=oldLayer;Destroy(isolatedRoot);session.Conditions.enabled=conditionsEnabled;
                ui.enabled=true;foreach(var indicator in indicators)indicator.enabled=true;for(int i=0;i<particles.Length;i++)if(particles[i])particles[i].enabled=particleStates[i];
            }
            check(trackSystem.ActiveSegments<=SurfaceTrackSystem.Capacity&&trackSystem.Mesh.vertexCount==SurfaceTrackSystem.Capacity*4,"All weather trails share a fixed-size mesh pool");
            profile.surfaceTracks=true;check(profile.Save()&&PlayerProfile.Load().surfaceTracks,"Weather-track setting persists through the real profile serializer");
            session.Weather=0;yield return null;yield return null;session.View.enabled=false;ui.enabled=false;foreach(var indicator in indicators)indicator.enabled=false;foreach(var particle in particles)if(particle)particle.enabled=false;
            car.enabled=false;car.Body.isKinematic=true;session.SetMenu(false);session.Conditions.NetworkConditions(14,0,0,0,0);camera.cullingMask=1<<30;
            var gallery=new GameObject("Owned train-stock acceptance gallery");var groundMaterial=Geometry.Material("Train proof ballast",new Color(.20f,.21f,.21f),0,.1f);var railMaterial=Geometry.Material("Train proof rails",new Color(.35f,.36f,.37f),.5f,.55f);
            var origin=new Vector3(6000,100,6000);
            var platform=GameObject.CreatePrimitive(PrimitiveType.Cube);Destroy(platform.GetComponent<Collider>());platform.transform.SetParent(gallery.transform);platform.transform.position=origin+new Vector3(0,-.2f,155);platform.transform.localScale=new Vector3(80,.4f,300);platform.GetComponent<Renderer>().sharedMaterial=groundMaterial;platform.layer=30;
            var visuals=new List<RailTrainVisual>();var rows=new JArray();
            for(int index=0;index<TrainStock.All.Length;index++)
            {
                var stock=TrainStock.All[index];float x=(index-2)*14;var path=new RailRoute(new[]{new Vec3(origin.x+x,origin.y,origin.z),new Vec3(origin.x+x,origin.y,origin.z+320)});
                foreach(float side in new[]{-.7175f,.7175f})
                {var rail=GameObject.CreatePrimitive(PrimitiveType.Cube);Destroy(rail.GetComponent<Collider>());rail.transform.SetParent(gallery.transform);rail.transform.position=origin+new Vector3(x+side,.025f,160);rail.transform.localScale=new Vector3(.075f,.05f,300);rail.GetComponent<Renderer>().sharedMaterial=railMaterial;rail.layer=30;}
                var visual=new RailTrainVisual(gallery.transform,stock);visuals.Add(visual);foreach(var child in visual.Root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;var pose=stock.Pose(path,260);visual.SetPose(pose,true);visual.Draw(0,0,1,true);yield return new WaitForFixedUpdate();
                check(visual.Bodies.Count==stock.Vehicles.Length&&visual.Bodies.All(b=>b.isKinematic),stock.Name+" uses the complete stock-specific physical formation");
                check(visual.FixedTick()&&visual.MaximumCommandedBodySpeed<.1f,stock.Name+" activates without an origin-to-track physics sweep");
                visual.Hide();var next=stock.Pose(path,265);visual.SetPose(next,true);visual.Draw(0,0,1,true);yield return new WaitForFixedUpdate();
                check(visual.FixedTick()&&visual.MaximumCommandedBodySpeed<.1f,stock.Name+" re-enters safely after retirement");
                Vector3 front=visual.Bodies[0].position;camera.transform.position=front+new Vector3(21,9,26);camera.transform.LookAt(front+Vector3.up*1.6f);camera.fieldOfView=54;caption=stock.Name+" · original approximation · "+stock.Vehicles.Length+" vehicles";
                yield return new WaitForSecondsRealtime(.7f);yield return Photo("beta36-stock-"+stock.Id,image=>check(Pink(image)<image.width*image.height*.005,stock.Name+" renders in "+GameGraphics.Name+" without shader errors"));
                rows.Add(new JObject{["stock"]=stock.Id,["vehicles"]=visual.Bodies.Count,["length"]=stock.Length,["meshes"]=visual.MeshCount,["maxSpawnBodySpeed"]=visual.MaximumCommandedBodySpeed});
            }
            check(visuals.Select(v=>v.OwnedMaterials[0]).Distinct().Count()==visuals.Count,"Each train owns independent paint materials");
            for(int i=0;i<visuals.Count;i++)check(Vector4.Distance(visuals[i].OwnedMaterials[0].GetColor("_BaseColor"),TrainStock.All[i].Body)<.01f,"Creating other formations preserves "+TrainStock.All[i].Name+" paint");
            caption="Train fleet 1.0.0 · regional, Civity, intercity and container freight";camera.transform.position=origin+new Vector3(110,145,390);camera.transform.LookAt(origin+new Vector3(0,0,205));camera.fieldOfView=48;
            yield return new WaitForSecondsRealtime(.6f);yield return Photo("beta36-stock-fleet");File.WriteAllText(Path.Combine(Output,"beta36-stock-metrics.json"),rows.ToString());
            var roots=visuals.Select(v=>v.Root).ToArray();foreach(var visual in visuals)visual.Dispose();Destroy(gallery);Destroy(groundMaterial);Destroy(railMaterial);yield return null;yield return null;
            check(roots.All(r=>!r),"All train gallery roots and owned stock meshes retire together");caption=null;
            session.Conditions.NetworkOverride=false;camera.cullingMask=savedMask;profile.weatherMode=weather;profile.simulatedTrains=trains;profile.surfaceTracks=true;car.Body.isKinematic=false;car.enabled=true;car.Recover(point,rotation);
            session.View.enabled=true;camera.transform.SetPositionAndRotation(savedPosition,savedRotation);camera.fieldOfView=savedFov;ui.enabled=true;foreach(var indicator in indicators)indicator.enabled=true;for(int i=0;i<particles.Length;i++)if(particles[i])particles[i].enabled=particleStates[i];session.SetMenu(true);
        }
    }
}
