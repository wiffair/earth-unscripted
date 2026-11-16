using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // Opt-in disposable-profile real-physics regression, never a normal game service.
    public sealed class BetaLaunchVerification:MonoBehaviour
    {
        readonly JArray cases=new JArray();readonly List<string> errors=new List<string>();
        GameSession s; bool stable=true;
        static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        void Awake(){Application.logMessageReceived+=Log;}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Log(string t,string stack,LogType k){if(k==LogType.Exception||k==LogType.Error)errors.Add(t+"\n"+stack);}
        IEnumerator Start()
        {
            Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Exercise());
            while(stack.Count>0){object current=null;bool more=false;try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null)break;if(!more){stack.Pop();continue;}if(current is IEnumerator child)stack.Push(child);else yield return current;}
            bool passed=failure==null&&errors.Count==0&&stable;
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"result.json"),new JObject{["passed"]=passed,["failure"]=failure?.ToString(),["errors"]=new JArray(errors),["version"]=Application.version,["cases"]=cases}.ToString());Application.Quit(passed?0:1);
        }
        IEnumerator Trial(string label,bool reverse,float seconds,float steering=0)
        {
            var c=s.Car;c.NetworkDriven=true;c.NetworkSteer=steering;c.NetworkThrottle=0;c.NetworkBrake=0;c.StartLocked=false;
            s.SetMenu(false);float start=Time.time;var initial=c.Body.position;var samples=new JArray();float peak=0,spin=0,maxContact=0;bool okay=true;
            while(Time.time-start<seconds)
            {
                c.NetworkThrottle=reverse?0:1;c.NetworkBrake=reverse?1:0;
                yield return new WaitForFixedUpdate();
                float speed=c.AbsoluteSpeed,angular=c.Body.angularVelocity.magnitude,reach=0;
                for(int i=0;i<4;i++)if(c.WheelGrounded[i])reach=Mathf.Max(reach,Vector3.Distance(c.ContactPoints[i],c.Body.position));
                peak=Mathf.Max(peak,speed);spin=Mathf.Max(spin,angular);maxContact=Mathf.Max(maxContact,reach);
                var row=new JObject{["t"]=Time.time-start,["position"]=V(c.Body.position),["velocity"]=V(c.Body.linearVelocity),["angular"]=V(c.Body.angularVelocity),["contacts"]=c.WheelGrounded.Count(x=>x),["reach"]=reach,["damage"]=c.Damage,["impact"]=c.LastImpactObject,["contactEvents"]=new JArray(c.DiagnosticContactEvents),["trains"]=new JArray((s.GetComponent<RailTrainSystem>()?.Visual?.Bodies??new Rigidbody[0]).Select(b=>(JToken)new JObject{["position"]=V(b.position),["velocity"]=V(b.linearVelocity),["angular"]=V(b.angularVelocity)})),["collider"]=c.DiagnosticCollider,["collisionImpulse"]=V(c.DiagnosticCollisionImpulse),["otherVelocity"]=V(c.DiagnosticOtherVelocity),["queuedForce"]=V(c.DiagnosticQueuedForce),["queuedTorque"]=V(c.DiagnosticQueuedTorque),["beforeVelocity"]=V(c.DiagnosticBeforeVelocity),["normals"]=new JArray(c.ContactNormals.Select(n=>(JToken)V(n))),["inertia"]=V(c.Body.inertiaTensor)};
                if(samples.Count>=15)samples.RemoveAt(0);samples.Add(row);
                if(float.IsNaN(speed)||speed>100||angular>100||Mathf.Abs(c.Body.position.y-initial.y)>20){okay=false;break;}
            }
            c.NetworkThrottle=c.NetworkBrake=0;s.SetMenu(true);
            var result=new JObject{["scenario"]=label,["stable"]=okay,["peakSpeed"]=peak,["peakSpin"]=spin,["maxContactReach"]=maxContact,["travelled"]=Vector3.Distance(initial,c.Body.position),["samples"]=samples};cases.Add(result);
            stable&=okay;File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"launch-progress.json"),cases.ToString());yield return null;
        }
        IEnumerator Obstacle(int model)
        {
            s.SetMenu(false);s.Car.gameObject.SetActive(false);
            var floor=Geometry.Box("Tyre climb regression floor",null,new Vector3(0,999.5f,0),new Vector3(30,1,80),Geometry.Material("QA floor",Color.gray),true);
            var obstacle=Geometry.Box("Tyre climb regression bonnet",null,new Vector3(0,1000.45f,0),new Vector3(2,.9f,3),Geometry.Material("QA bonnet",Color.gray),true);
            var root=new GameObject("Actual upgraded off-road climb car");root.layer=8;root.transform.position=VehicleGeometry.RoadRestPosition(new Vector3(0,1000,-9),model);
            root.AddComponent<Rigidbody>();var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.8f,.85f,4);
            var visual=root.AddComponent<CarVisual>();visual.Build(Color.red,true,model);
            var car=root.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=true;
            car.ApplyTuning(new Core.OwnedCar{model=model,packVehicleId=Core.ImportedVehicleCatalogue.IdForModel(model),engine=3,gearing=3,tyres=3,brakes=3,suspension=3,differential=3});
            for(int n=0;n<100;n++)yield return new WaitForFixedUpdate();float initial=car.Body.position.y,peak=0,top=initial;car.NetworkThrottle=1;
            for(int n=0;n<550&&car.Body.position.z<7;n++){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,car.AbsoluteSpeed);top=Mathf.Max(top,car.Body.position.y);if(peak>100)break;}
            bool okay=peak<100&&car.Body.position.z>4&&top>initial+.3f;stable&=okay;
            cases.Add(new JObject{["scenario"]="Upgraded Hekla physically climbs 0.9m bonnet",["stable"]=okay,["peakSpeed"]=peak,["progress"]=car.Body.position.z,["rise"]=top-initial});
            Destroy(root);Destroy(obstacle);Destroy(floor);yield return null;s.Car.gameObject.SetActive(true);s.SetMenu(true);
        }

        IEnumerator Exercise()
        {
            double limit=Time.realtimeSinceStartupAsDouble+600;
            while(!(s=FindFirstObjectByType<GameSession>())||!s.BootReady){if(s&&s.BootFailed)throw new IOException(s.BootStatus);if(Time.realtimeSinceStartupAsDouble>limit)throw new TimeoutException("Boot");yield return null;}
            s.Profile.volume=0;s.Profile.simulatedTrains=false;
            var pack=Core.ImportedVehicleCatalogue.Find("earthunscripted.hekla-formula-offroad");
            if(pack==null||!s.SelectPackVehicle(pack.Model))throw new Exception("Hekla selection: "+s.Notice);
            yield return null;
            yield return Trial("Hekla selected, forward",false,6);
            s.TowToGarage();while(s.PreparingStart)yield return null;
            yield return Trial("Hekla towed, reverse",true,4);
            s.TowToGarage();while(s.PreparingStart)yield return null;
            yield return Trial("Hekla towed, forward",false,6);
            s.TowToGarage();while(s.PreparingStart)yield return null;
            s.SetMenu(false);for(int n=0;n<90;n++)yield return new WaitForFixedUpdate();
            s.Recover();while(s.PreparingStart)yield return null;
            yield return Trial("Hekla recovered, forward",false,6);
            s.Profile.simulatedTrains=true;
            var owned=s.Profile.garage.Active;owned.engine=owned.gearing=owned.tyres=owned.brakes=owned.suspension=owned.differential=3;
            s.Car.ApplyTuning(owned);
            foreach(float angle in new[]{0f,90f,180f,270f,315f})
            {
                var point=new Vector3(-41.691f,24.3f,-452.816f);
                s.Car.Recover(point,Quaternion.Euler(0,angle,0));s.SetMenu(false);
                for(int n=0;n<120;n++)yield return new WaitForFixedUpdate();
                yield return Trial("Upgraded Hekla forward, trains on, yaw "+angle,false,4);
            }
            foreach(float steering in new[]{-.65f,.65f})
            {
                s.TowToGarage();while(s.PreparingStart)yield return null;
                yield return Trial("Upgraded Hekla forward and steering "+steering,false,4,steering);
                s.TowToGarage();while(s.PreparingStart)yield return null;
                yield return Trial("Upgraded Hekla reverse and steering "+steering,true,4,steering);
            }
            yield return Obstacle(pack.Model);

        }
    }
}
