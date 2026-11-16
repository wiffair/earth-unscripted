using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RoadRush.Core;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Explicit disposable-profile Beta15 checks. Uses actual physics,
    /// the real pack, generated path colliders and rendered sky/paint materials.
    /// Never attached in ordinary play.</summary>
    public static class Beta15FeatureChecks
    {
        static readonly JObject results=new JObject();
        static void Check(bool condition,string message,List<string> checks)
        {
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta15-features.json"),results.ToString());
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"progress.json"),new JObject{["check"]=message,["passed"]=condition}.ToString());
            if(!condition)throw new InvalidOperationException(message);checks.Add(message);
        }
        static VehicleController CreateCar(int model,Vector3 ground,bool upgraded=false)
        {
            var owned=new OwnedCar{model=model,packVehicleId=ImportedVehicleCatalogue.IdForModel(model),paint=1};
            if(upgraded)owned.engine=owned.gearing=owned.tyres=owned.brakes=owned.suspension=owned.differential=3;
            var root=new GameObject("Beta15 physical car "+model);root.layer=8;
            root.transform.position=VehicleGeometry.RoadRestPosition(ground,model);
            root.AddComponent<Rigidbody>();var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.8f,.85f,4);
            var visual=root.AddComponent<CarVisual>();visual.Build(CarColours.Paint[owned.paint],true,model);
            var car=root.AddComponent<VehicleController>();car.Visual=visual;car.NetworkDriven=true;car.ParkingAutomaticEnabled=true;car.ApplyTuning(owned);
            return car;
        }
        static IEnumerator Step(int count){for(int index=0;index<count;index++)yield return new WaitForFixedUpdate();}
        public static IEnumerator Exercise(GameSession session,VehicleController oldCar,Camera camera,List<string> checks)
        {
            var output=SinglePlayerBeta.VerificationOutput;
            oldCar.gameObject.SetActive(false);
            var entry=ImportedVehicleCatalogue.Find("blackwood.hekla-formula-offroad");
            var fixedPack=ImportedVehicleCatalogue.Find("blackwood.vw-splittie");
            var fixedCar=new OwnedCar{id="fixed-livery-fixture",model=fixedPack.Model,packVehicleId=fixedPack.Settings.Id};
            var fixedGarage=new GarageState{activeId=fixedCar.id,cars=new List<OwnedCar>{fixedCar}};
            Check(!fixedPack.Settings.SupportsPaint&&GarageRules.Style(fixedGarage,1000000,2,0,0,2,1)==null,"Fixed-livery imported pack cannot charge for unsupported paint changes",checks);
            Check(entry.Settings.SupportsPaint,"Hekla supports the painted material options",checks);
            Check(entry.Settings.WheelObstacleContact&&entry.Settings.EngineLayout=="v8-crossplane"&&entry.Settings.SafeLandingSpeed>=10,"Revised pack supplies tyre contact, V8 and off-road landing settings",checks);
            Check(VehicleImpactRules.CollisionDamage(15,2200,1000,1.7f)<VehicleImpactRules.CollisionDamage(15,1000,2200,1),"Heavy reinforced car takes less damage in the same car-to-car impact",checks);
            Check(VehicleImpactRules.CollisionDamage(30,2200,float.PositiveInfinity,1.7f)>VehicleImpactRules.CollisionDamage(10,2200,float.PositiveInfinity,1.7f)*5,"High-speed fixed-object impact remains dangerous",checks);
            Check(VehicleImpactRules.LandingDamage(8,10,1.7f)==0&&VehicleImpactRules.LandingDamage(8,5.5f,1)>0,"Off-roader tolerates a landing that damages a road car",checks);
            Check(VehicleImpactRules.LandingDamage(16,10,1.7f)>10,"Severe off-road landing can still damage the car",checks);
            var summer=CelestialEphemeris.Calculate(new DateTimeOffset(2024,6,21,12,0,0,TimeSpan.Zero),51.5,0);
            var winter=CelestialEphemeris.Calculate(new DateTimeOffset(2024,12,21,12,0,0,TimeSpan.Zero),51.5,0);
            Check(summer.Sun.Up>.86&&winter.Sun.Up>.22&&winter.Sun.Up<.30,"London summer/winter noon solar heights are seasonally correct",checks);
            var eclipse=CelestialEphemeris.Calculate(new DateTimeOffset(2024,4,8,18,0,0,TimeSpan.Zero),30,-100);
            var full=CelestialEphemeris.Calculate(new DateTimeOffset(2024,3,25,7,0,0,TimeSpan.Zero),51.5,0);
            Check(eclipse.Illumination<.002&&full.Illumination>.995,"Known new/full moon dates produce correct illuminated fractions",checks);
            var morning=CelestialEphemeris.Calculate(new DateTimeOffset(2024,3,20,6,0,0,TimeSpan.Zero),0,0);
            Check(morning.Sun.East>.99,"Equinox morning sun rises in the east",checks);
            Check(Resources.Load<Shader>("AstronomicalSky")&&Resources.Load<Shader>("VehiclePaint"),"Sky and paint shaders are included",checks);
            var patrol=UnityEngine.Object.FindObjectsByType<PoliceDriver>(FindObjectsSortMode.None).FirstOrDefault();
            if(patrol)
            {
                var maximum=new OwnedCar{model=patrol.Car.Visual.Model,engine=3,gearing=3,tyres=3,brakes=3,suspension=3,differential=3};
                Check(Mathf.Abs(patrol.Car.PowerFactor-GarageRules.Power(maximum))<.001f&&Mathf.Abs(patrol.Car.SpeedLimit-GarageRules.Speed(maximum))<.001f,"Actual patrol receives the model's maximum engine/gearing upgrades",checks);
                Check(Mathf.Abs(patrol.Car.TyreFactor-GarageRules.Grip(maximum))<.001f&&Mathf.Abs(patrol.Car.BrakeFactor-GarageRules.Braking(maximum))<.001f,"Actual patrol receives maximum tyre and brake upgrades",checks);
                Check(PoliceDriver.DesiredPursuitSpeed(40,60,false,patrol.Car.SpeedLimit)>45,"Pursuit target no longer has the old 45 m/s cap",checks);
                results["patrolGearingLimitMph"]=patrol.Car.SpeedLimit*DrivingMath.MphPerMetreSecond;
            }
            yield return CheckPaths(session,checks);
            // This platform belongs only to the QA scene, above all real geometry.
            var extended=Geometry.Box("Beta15 long test platform",null,new Vector3(20,9999.5f,1400),new Vector3(160,1,3000),Geometry.Material("QA ground",new Color(.14f,.17f,.11f)),true);
            var car=CreateCar(entry.Model,new Vector3(0,10000,-9));
            yield return Step(100);
            var obstacle=Geometry.Box("QA bonnet-height obstacle",null,new Vector3(0,10000.45f,0),new Vector3(2,.9f,3),Geometry.Material("QA bonnet",Color.gray),true);
            car.NetworkThrottle=1;
            float maxHeight=car.Body.position.y;int step=0;
            while(step++<550&&car.Body.position.z<7)
            {maxHeight=Mathf.Max(maxHeight,car.Body.position.y);yield return new WaitForFixedUpdate();}
            results["obstacleTractionApplications"]=car.GetComponentInChildren<VehicleObstacleWheels>().TractionApplications;
            results["obstacleProgress"]=car.Body.position.z;results["obstacleRise"]=maxHeight-10000-entry.Metric.RestRootHeight(entry.Settings.Mass);
            Check(car.Body.position.z>4&&maxHeight>10000+entry.Metric.RestRootHeight(entry.Settings.Mass)+.30f,"Large physical tyres climb a bonnet-height obstacle instead of a full-width wall",checks);
            UnityEngine.Object.Destroy(obstacle);UnityEngine.Object.Destroy(car.gameObject);yield return null;
            // Separate car-to-car check: the target has actual dynamic mass and a
            // lower bonnet / higher cabin profile, not an infinitely heavy box.
            var hitter=CreateCar(entry.Model,new Vector3(0,10000,-8));
            var targetRoot=new GameObject("QA traffic target");targetRoot.layer=8;targetRoot.transform.position=VehicleGeometry.RoadRestPosition(new Vector3(0,10000,0),0);
            targetRoot.AddComponent<Rigidbody>();var targetBox=targetRoot.AddComponent<BoxCollider>();targetBox.center=new Vector3(0,.85f,0);targetBox.size=new Vector3(2,.9f,4.25f);VehicleGeometry.ApplyCollider(targetBox,0);
            var targetVisual=targetRoot.AddComponent<CarVisual>();targetVisual.Build(Color.gray,false,0);
            var traffic=targetRoot.AddComponent<TrafficCar>();traffic.Visual=targetVisual;traffic.Player=hitter;traffic.Route=session.World.Route;traffic.Speed=0;traffic.ActivateCollisionPhysics();
            yield return Step(100);hitter.NetworkThrottle=1;
            float targetPeak=0;
            for(int index=0;index<500;index++){targetPeak=Mathf.Max(targetPeak,traffic.GetComponent<Rigidbody>().linearVelocity.magnitude);yield return new WaitForFixedUpdate();}
            results["targetPeakSpeed"]=targetPeak;results["ownDamage"]=hitter.Damage;results["targetDamage"]=traffic.CollisionDamage;
            Check(targetPeak>1,"Traffic receives collision momentum instead of behaving as an immovable kinematic wall",checks);
            UnityEngine.Object.Destroy(hitter.gameObject);UnityEngine.Object.Destroy(targetRoot);yield return null;
            // Drop each vehicle from the same height; no direct damage setter.
            float roadDamage=0,offroadDamage=0;
            foreach(int model in new[]{0,entry.Model})
            {
                var dropped=CreateCar(model,new Vector3(25,10004,0));dropped.ParkingAutomaticEnabled=false;
                for(int index=0;index<250;index++)yield return new WaitForFixedUpdate();
                if(model==0)roadDamage=dropped.Damage;else offroadDamage=dropped.Damage;
                UnityEngine.Object.Destroy(dropped.gameObject);yield return null;
            }
            results["roadLandingDamage"]=roadDamage;results["offroadLandingDamage"]=offroadDamage;
            Check(roadDamage>offroadDamage&&offroadDamage<.1f,"Same physical jump is absorbed by off-road suspension but damages the road car",checks);
            // Trunk policy and native collider/visual break callback.
            var saplingRoot=new GameObject("QA sapling");saplingRoot.AddComponent<CapsuleCollider>();var sapling=saplingRoot.AddComponent<TreeImpact>();bool removed=false;sapling.Configure(.08f,()=>removed=true);
            float saplingScale=sapling.Strike(1500,15,Vector3.forward);
            var oakRoot=new GameObject("QA mature tree");oakRoot.AddComponent<CapsuleCollider>();var oak=oakRoot.AddComponent<TreeImpact>();oak.Configure(.5f,()=>{});float oakScale=oak.Strike(1500,15,Vector3.forward);
            Check(sapling.Broken&&removed&&!saplingRoot.GetComponent<Collider>().enabled,"Small tree breaks and removes its blocking collider and standing visual",checks);
            Check(!oak.Broken&&oakScale>saplingScale*3,"Mature tree remains solid and causes more damage",checks);
            UnityEngine.Object.Destroy(saplingRoot);UnityEngine.Object.Destroy(oakRoot);
            // Engine synthesis runs the same sample function used by the audio callback.
            WriteEnginePreview(Path.Combine(output,"hekla-v8.wav"));Check(new FileInfo(Path.Combine(output,"hekla-v8.wav")).Length>100000,"Original V8 firing/exhaust audio preview generated",checks);
            // Paint persistence/transactions are exercised against disposable data.
            session.enabled=true;session.SetMenu(true);session.Car.Body.linearVelocity=Vector3.zero;session.Car.Body.angularVelocity=Vector3.zero;
            int credits=session.Economy.Credits;
            Check(session.PaintActiveCar(2,1,2),"Paint job applies through a saved, priced transaction",checks);
            var saved=PlayerProfile.Load();Check(saved.garage.Active.paintJob==1&&saved.garage.Active.paint==2&&session.Economy.Credits<credits,"Paint pattern/colour persist and consume quoted game credits",checks);
            foreach(var renderer in session.Car.Visual.GetComponentsInChildren<Renderer>())foreach(var material in renderer.sharedMaterials)Check(material.shader.name!="Hidden/InternalErrorShader","No pink/error vehicle material",checks);
            var qaSun=GameObject.Find("Car-pack QA daylight");if(qaSun)UnityEngine.Object.Destroy(qaSun);
            session.Profile.clockMode=0;session.Profile.fixedHour=0;session.Profile.worldDate="2024-04-08";session.Weather=0;
            session.enabled=false;Time.timeScale=1;
            yield return new WaitForSecondsRealtime(1.2f);
            camera.transform.position=new Vector3(20,10002,0);camera.transform.rotation=Quaternion.LookRotation(new Vector3(0,.6f,1));
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"night-sky.png"));yield return new WaitForEndOfFrame();yield return null;
            Check(RenderSettings.skybox.shader.name=="RoadRush/Astronomical Sky","Actual sky uses astronomical sun/moon/stars shader",checks);
            session.Profile.fixedHour=14;yield return new WaitForSecondsRealtime(1.2f);
            var showroom=CreateCar(entry.Model,new Vector3(0,10000,-8));yield return Step(100);
            camera.transform.position=showroom.Body.position+new Vector3(4.8f,2.4f,4.8f);camera.transform.LookAt(showroom.Body.position+Vector3.up*.8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"hekla-1.1-front.png"));yield return new WaitForEndOfFrame();yield return null;
            File.WriteAllText(Path.Combine(output,"beta15-features.json"),results.ToString());
            UnityEngine.Object.Destroy(showroom.gameObject);UnityEngine.Object.Destroy(extended);
        }
        static IEnumerator CheckPaths(GameSession session,List<string> checks)
        {
            var world=session.World.AutomaticWorld.Renderer;int samples=0;float largest=0;int features=0;
            var paths=world.GetComponentsInChildren<MeshCollider>().Where(c=>c.name=="Footpaths").ToArray();
            var terrain=world.GetComponentsInChildren<MeshCollider>().Where(c=>c.name=="Terrain").ToArray();
            foreach(var map in world.ResidentAreas)foreach(var feature in map.features)
            {
                if(feature.category!="path"||feature.bridge||feature.tunnel||feature.layer!=0||feature.level!="0"&&!string.IsNullOrEmpty(feature.level))continue;
                bool counted=false;
                for(int index=1;index<feature.points.Length;index++)
                {
                    var point=Vector3.Lerp(feature.points[index-1],feature.points[index],.5f);if(!world.HasGeometryAt(point))continue;
                    var ray=new Ray(point+Vector3.up*30,Vector3.down);float pathY=float.NegativeInfinity,groundY=float.NegativeInfinity;
                    foreach(var collider in paths)if(collider.Raycast(ray,out var hit,80))pathY=Mathf.Max(pathY,hit.point.y);
                    foreach(var collider in terrain)if(collider.Raycast(ray,out var hit,80))groundY=Mathf.Max(groundY,hit.point.y);
                    if(float.IsInfinity(pathY)||float.IsInfinity(groundY))continue;
                    float gap=pathY-groundY;
                    largest=Mathf.Max(largest,gap);samples++;counted=true;
                    if(samples%64==0)yield return null;
                    if(samples>=512)break;
                }
                if(counted)features++;if(samples>=512)break;
            }
            results["groundedPathSamples"]=samples;results["groundedPathFeatures"]=features;results["maximumPathSupportGapMetres"]=largest;
            Check(samples>=10&&features>=3&&largest<=.36f,"Multiple generated ground-path features follow terrain with supported kerbs (global rule)",checks);
        }
        static void WriteEnginePreview(string path)
        {
            const int rate=24000,count=rate*4;var synth=new EngineSoundSynth();var data=new short[count];double sum=0;
            for(int index=0;index<count;index++){float t=index/(float)count;float sample=synth.Sample(900+t*5600,.7f,rate);if(float.IsNaN(sample)||Mathf.Abs(sample)>1)throw new InvalidDataException("Invalid V8 audio sample");sum+=sample*sample;data[index]=(short)(sample*22000);}
            if(Math.Sqrt(sum/count)<.01)throw new InvalidDataException("V8 engine is inaudible");
            using(var stream=new BinaryWriter(File.Create(path)))
            {stream.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));stream.Write(36+count*2);stream.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));stream.Write(16);stream.Write((short)1);stream.Write((short)1);stream.Write(rate);stream.Write(rate*2);stream.Write((short)2);stream.Write((short)16);stream.Write(System.Text.Encoding.ASCII.GetBytes("data"));stream.Write(count*2);foreach(short sample in data)stream.Write(sample);}
        }
    }
}
