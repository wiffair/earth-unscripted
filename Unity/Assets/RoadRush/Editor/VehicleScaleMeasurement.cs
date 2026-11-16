using System;using System.Collections.Generic;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;
namespace RoadRush.Editor
{
    public static class VehicleScaleMeasurement
    {
        public static void Run()
        {
            GameObject root=null;
            try
            {
                var output=new JArray();
                for(int model=0;model<3;model++)
                {
                    root=new GameObject("Isolated vehicle measurement");var visual=root.AddComponent<CarVisual>();visual.Build(Color.white,true,model,0,0,0);
                    var parts=new JArray();var points=new List<Vector3>();var body=new List<Vector3>();
                    foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer=filter.GetComponent<Renderer>();if(!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy||!filter.sharedMesh)continue;
                        string path=filter.name;for(var parent=filter.transform.parent;parent&&parent!=root.transform;parent=parent.parent)path=parent.name+"/"+path;
                        var vertices=filter.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();if(vertices.Length==0)continue;
                        points.AddRange(vertices);if(path.IndexOf("mirror",StringComparison.OrdinalIgnoreCase)<0)body.AddRange(vertices);
                        parts.Add(new JObject{{"path",path},{"bounds",BoundsOf(vertices)}});
                    }
                    var spec=RoadRush.Core.GarageRules.Catalogue[model];
                    var measuredBox=root.AddComponent<BoxCollider>();measuredBox.center=new Vector3(0,.86f,0);measuredBox.size=new Vector3(1.95f,.85f,4.15f);VehicleGeometry.ApplyCollider(measuredBox,model);
                    Vector3 measuredSize=measuredBox.size,measuredCentre=measuredBox.center;UnityEngine.Object.DestroyImmediate(measuredBox);
                    float measuredTrack=Mathf.Abs(visual.Wheels[2].localPosition.x-visual.Wheels[0].localPosition.x),measuredWheelbase=Mathf.Abs(visual.Wheels[1].localPosition.z-visual.Wheels[0].localPosition.z);
                    output.Add(new JObject{{"model",model},{"name",spec.Name},{"allVisibleMeshVertices",BoundsOf(points)},{"excludingNamedMirrors",BoundsOf(body)},{"collisionBox",new JArray(measuredSize.x,measuredSize.y,measuredSize.z)},{"collisionCentre",new JArray(measuredCentre.x,measuredCentre.y,measuredCentre.z)},{"wheelTrack",measuredTrack},{"wheelbase",measuredWheelbase},{"parts",parts}});
                    if(model==0)output.Last["settledPhysics"]=Settle(root,visual);
                    Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);root=null;
                }
                string folder="Builds/Verification/VehicleScale";Directory.CreateDirectory(folder);string pathOut=Path.Combine(folder,DateTime.UtcNow.Ticks+".json");File.WriteAllText(pathOut,output.ToString());Debug.Log("VEHICLE SCALE MEASURED "+Path.GetFullPath(pathOut));EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{if(root)UnityEngine.Object.DestroyImmediate(root);}
        }
        static JObject BoundsOf(IEnumerable<Vector3> vertices)
        {
            var p=vertices.ToArray();if(p.Length==0)return new JObject();var bounds=new Bounds(p[0],Vector3.zero);foreach(var v in p)bounds.Encapsulate(v);
            return new JObject{{"width",bounds.size.x},{"height",bounds.size.y},{"length",bounds.size.z},{"min",new JArray(bounds.min.x,bounds.min.y,bounds.min.z)},{"max",new JArray(bounds.max.x,bounds.max.y,bounds.max.z)}};
        }
        static JObject Settle(GameObject root,CarVisual visual)
        {
            var previousMode=Physics.simulationMode;float previousScale=Time.timeScale,previousFixed=Time.fixedDeltaTime;GameObject floor=null;
            try
            {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;Time.fixedDeltaTime=.01f;
                floor=new GameObject("Owned measurement floor");floor.transform.position=new Vector3(0,-.5f,0);floor.AddComponent<BoxCollider>().size=new Vector3(100,1,100);
                root.layer=8;root.transform.position=new Vector3(0,.7f,0);var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.86f,0);box.size=new Vector3(1.95f,.85f,4.15f);
                var car=root.AddComponent<VehicleController>();
                typeof(VehicleController).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(car,null);
                car.Visual=visual;car.NetworkDriven=true;car.NetworkBrake=0;car.NetworkHandbrake=1;car.ApplyTuning(new RoadRush.Core.OwnedCar{model=0});
                car.Body.interpolation=RigidbodyInterpolation.None;
                car.Body.constraints=RigidbodyConstraints.FreezeRotation|RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ;
                var step=typeof(VehicleController).GetMethod("FixedUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Physics.SyncTransforms();
                for(int i=0;i<1000;i++){step.Invoke(car,null);Physics.Simulate(.01f);Physics.SyncTransforms();}
                if(car.WheelGrounded.Count(x=>x)!=4||car.Body.linearVelocity.magnitude>.01f||root.transform.position.y<-.2f||root.transform.position.y>.5f)throw new Exception("Flat-floor measurement did not settle; no height acceptance");
                var world=new List<Vector3>();foreach(var f in root.GetComponentsInChildren<MeshFilter>())
                {var renderer=f.GetComponent<Renderer>();if(renderer&&renderer.enabled&&f.sharedMesh)foreach(var p in f.sharedMesh.vertices)world.Add(f.transform.TransformPoint(p));}
                return new JObject{{"seconds",10},{"bodyRootY",root.transform.position.y},{"velocity",car.Body.linearVelocity.magnitude},{"groundedWheels",car.WheelGrounded.Count(x=>x)},{"visibleWorldBounds",BoundsOf(world)},{"wheelCentresY",new JArray(visual.Wheels.Select(w=>w.position.y))},{"note","Isolated flat floor, actual suspension FixedUpdate and Physics.Simulate, stock tuning and handbrake, no career load. Body constrained level/XZ and interpolation disabled for manual Editor simulation; not native free-body road-driving acceptance."}};
            }
            finally{Physics.simulationMode=previousMode;Time.timeScale=previousScale;Time.fixedDeltaTime=previousFixed;if(floor)UnityEngine.Object.DestroyImmediate(floor);}
        }
    }
}
