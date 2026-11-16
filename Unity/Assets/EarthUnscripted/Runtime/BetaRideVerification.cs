using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using EarthUnscripted.Core;
namespace EarthUnscripted
{
    // Explicit disposable-profile proof; normal sessions never create this.
    public sealed class BetaRideVerification:MonoBehaviour
    {
        readonly JArray cases=new JArray();readonly List<string> errors=new List<string>();
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text+"\n"+stack);}
        static void Require(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
        IEnumerator Start()
        {
            Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0)
            {
                object current=null;bool more=false;try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null)break;if(!more){stack.Pop();continue;}
                if(current is IEnumerator child)stack.Push(child);else yield return current;
            }
            bool passed=failure==null&&errors.Count==0;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),new JObject{["passed"]=passed,["failure"]=failure?.ToString(),["errors"]=new JArray(errors),["version"]=Application.version,["cases"]=cases}.ToString());Application.Quit(passed?0:1);
        }
        IEnumerator SignFaces()
        {
            var root=new GameObject("Isolated standard sign render");root.transform.position=Vector3.up*2000;
            var sign=EarthUnscripted.NewbridgePrototype.StandardTrafficSigns.Place(root.transform,"speed","30","GB");
            var cameraObject=new GameObject("Isolated sign camera");var cam=cameraObject.AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=.55f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.cyan;cam.nearClipPlane=.1f;cam.farClipPlane=10;
            var target=new RenderTexture(256,256,24);cam.targetTexture=target;
            var counts=new JObject();
            foreach(bool front in new[]{true,false})
            {
                cameraObject.transform.position=new Vector3(0,2002.2f,front?4:-4);cameraObject.transform.LookAt(new Vector3(0,2002.2f,0));
                yield return new WaitForEndOfFrame();
                var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(256,256,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();RenderTexture.active=prior;
                int red=0,black=0,grey=0;foreach(var pixel in image.GetPixels())
                {if(pixel.r>.7f&&pixel.g<.2f&&pixel.b<.2f)red++;if(pixel.r<.1f&&pixel.g<.1f&&pixel.b<.1f)black++;if(pixel.r>.2f&&pixel.r<.4f&&Mathf.Abs(pixel.r-pixel.g)<.05f)grey++;}
                counts[front?"front":"back"]=new JObject{["red"]=red,["black"]=black,["grey"]=grey};
                File.WriteAllBytes(Path.Combine(SinglePlayerBeta.VerificationOutput,front?"sign-front.png":"sign-back.png"),image.EncodeToPNG());Destroy(image);
                Require(front?red>100&&black>100:grey>1000&&red==0&&black==0,front?"Front lettering/plate not rendered":"Legend or face visible through sign back");
            }
            cases.Add(new JObject{["standardSignPixels"]=counts});cam.targetTexture=null;Destroy(target);Destroy(cameraObject);Destroy(root);yield return null;
        }
        void VerifyVergeContacts(VehicleController car)
        {
            var fixture=new GameObject("One terrain mesh with a raised verge");
            var mesh=new Mesh{vertices=new[]{new Vector3(40,1000,-5),new Vector3(40,1000,5),new Vector3(50,1000,5),new Vector3(50,1000,-5),new Vector3(50,1000.7f,-5),new Vector3(50,1000.7f,5),new Vector3(60,1000.7f,5),new Vector3(60,1000.7f,-5)},triangles=new[]{0,1,2,0,2,3,3,2,5,3,5,4,4,5,6,4,6,7}};mesh.RecalculateNormals();fixture.AddComponent<MeshCollider>().sharedMesh=mesh;Physics.SyncTransforms();
            int recovered=0;var hits=new RaycastHit[24];var m=car.Metrics;float radius=m.Radius+Physics.defaultContactOffset*3;
            var method=typeof(VehicleController).GetMethod("TryWheelContact",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            for(float offset=.05f;offset<m.Radius*1.3f;offset+=.025f)
            {
                var origin=new Vector3(50-offset,1000+m.SuspensionRayLength*.85f,0);
                int count=Physics.SphereCastNonAlloc(origin+Vector3.up*m.Radius,radius,Vector3.down,hits,Mathf.Max(.01f,m.SuspensionRayLength-radius+m.Radius),~0,QueryTriggerInteraction.Ignore);
                bool oldValid=false;for(int i=0;i<count;i++)if(Vector3.Dot(hits[i].normal,Vector3.up)>=.08f&&Mathf.Abs(hits[i].point.x-origin.x)<=m.TyreWidth*.5f+.12f)oldValid=true;
                if(oldValid||!Physics.Raycast(origin,Vector3.down,out var narrow,m.SuspensionRayLength)||narrow.collider.gameObject!=fixture)continue;
                object[] args={origin,0,default(RaycastHit),0f};Require((bool)method.Invoke(car,args),"Verge swallowed real wheel support");recovered++;
            }
            Require(recovered>0,"Fixture failed to reproduce the old broad-query contact loss");cases.Add(new JObject{["vergeSupportRecoveredCases"]=recovered});Destroy(fixture);Destroy(mesh);
        }
        static float Height(float z)=>z<25||z>105?0:.2f*(1-Mathf.Cos((z-25)*Mathf.PI*2/80));
        IEnumerator Exercise()
        {
            Require(SinglePlayerBeta.VerificationOutput!=null,"Isolated ride proof only");
            Time.timeScale=1;Time.fixedDeltaTime=1/120f;Application.targetFrameRate=120;
            if(!Application.isBatchMode)yield return SignFaces();
            var ground=new GameObject("Measured ride seam/junction fixture");var mesh=new Mesh();var vertices=new List<Vector3>();var indices=new List<int>();
            // Separate rows reproduce actual road mesh seams. Continuous,
            // class-bounded approach/crest transitions surround a raised mouth.
            for(int z=-20;z<180;z+=2)
            {
                int i=vertices.Count;vertices.Add(new Vector3(-8,1000+Height(z),z));vertices.Add(new Vector3(-8,1000+Height(z+2),z+2));vertices.Add(new Vector3(8,1000+Height(z+2),z+2));vertices.Add(new Vector3(8,1000+Height(z),z));indices.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            }
            mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();var floor=ground.AddComponent<MeshCollider>();floor.sharedMesh=mesh;
            var hekla=ImportedVehicleCatalogue.Find("earthunscripted.hekla-formula-offroad");Require(hekla!=null,"Actual Hekla pack loaded");
            foreach(int model in new[]{0,hekla.Model})
            {
                var root=new GameObject("Actual suspension test car "+model);root.layer=8;root.transform.position=new Vector3(0,1001,0);var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
                var visual=root.AddComponent<CarVisual>();visual.Build(Color.red,true,model);var car=root.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=false;car.ApplyTuning(new OwnedCar{model=model,packVehicleId=model==hekla.Model?hekla.Settings.Id:null});
                if(model==hekla.Model)VerifyVergeContacts(car);
                Require(car.TryInitializeSupportedRest(new Vector3(0,1000,0),Quaternion.identity,out var why,out _),"Real suspension rest fit: "+why);
                for(int i=0;i<180;i++)yield return new WaitForFixedUpdate();
                Require(car.WheelGrounded.All(v=>v),"Four contacts after settling");float restY=car.Body.position.y;
                // One missing contact frame must not turn zeroed compression
                // history into an upward damper impulse on reacquisition.
                floor.enabled=false;yield return new WaitForFixedUpdate();floor.enabled=true;
                float reacquireVelocity=0,reacquireRise=0;
                for(int i=0;i<120;i++){yield return new WaitForFixedUpdate();reacquireVelocity=Mathf.Max(reacquireVelocity,Mathf.Abs(car.Body.linearVelocity.y));reacquireRise=Mathf.Max(reacquireRise,car.Body.position.y-restY);}
                Require(reacquireVelocity<.30f&&reacquireRise<.035f,"No artificial kick after a one-frame wheel-contact loss");
                Require(car.TryInitializeSupportedRest(new Vector3(0,1000,0),Quaternion.identity,out why,out _),"Drive rest reset: "+why);
                float peakVerticalAcceleration=0,peakDeviation=0;int unsupported=0;var samples=new JArray();float previousYVelocity=0;
                for(int i=0;i<960;i++)
                {
                    var velocity=car.Body.linearVelocity;velocity.z=13.4112f;velocity.x=0;car.Body.linearVelocity=velocity;
                    yield return new WaitForFixedUpdate();var p=car.Body.position;float vy=car.Body.linearVelocity.y;
                    if(i>30)peakVerticalAcceleration=Mathf.Max(peakVerticalAcceleration,Mathf.Abs(vy-previousYVelocity)/Time.fixedDeltaTime);
                    peakDeviation=Mathf.Max(peakDeviation,Mathf.Abs(p.y-Height(p.z)-restY));if(!car.Grounded)unsupported++;
                    if(i%12==0)samples.Add(new JObject{["z"]=p.z,["bodyY"]=p.y,["verticalVelocity"]=vy,["contacts"]=car.WheelGrounded.Count(v=>v),["surfaces"]=new JArray(car.ContactSurfaceNames)});previousYVelocity=vy;
                }
                cases.Add(new JObject{["model"]=model,["recontactPeakVerticalSpeed"]=reacquireVelocity,["recontactRise"]=reacquireRise,["peakVerticalAcceleration"]=peakVerticalAcceleration,["peakBodyDeviation"]=peakDeviation,["unsupportedFrames"]=unsupported,["samples"]=samples});
                Require(unsupported==0&&peakDeviation<.15f&&peakVerticalAcceleration<8,"Supported, stable 30 mph drive across seams and smooth junction transitions");
                if(model==hekla.Model)
                {
                    Require(car.TryInitializeSupportedRest(new Vector3(0,1000,140),Quaternion.identity,out why,out _),"Sign impact starting support");
                    var signs=new GameObject("Yielding post crash fixture");
                    var control=new EarthUnscripted.NewbridgePrototype.NewbridgeWorld.Control{id="crash-post",kind="speed",value="30",x=.65f,y=1000,z=150,dz=1};
                    EarthUnscripted.NewbridgePrototype.TrafficControlVisual.Create(signs.transform,control,0,"GB",-1,null,p=>1000);
                    var post=signs.GetComponentInChildren<RoadSignImpact>();float beforeDamage=car.Damage;car.NetworkThrottle=.6f;car.Body.linearVelocity=Vector3.forward*10;
                    for(int tick=0;tick<240;tick++)yield return new WaitForFixedUpdate();
                    cases.Add(new JObject{["signBent"]=post.Bent,["signDamage"]=car.Damage-beforeDamage,["afterSignZ"]=car.Body.position.z});
                    Require(post.Bent&&car.Damage>beforeDamage&&car.Body.position.z>151,"Post must yield, cause minor damage and allow onward movement");
                    Destroy(signs);
                }
                Destroy(root);yield return null;
            }
        }
    }
}
