using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class ImportedCarVisualVerification
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception("IMPORTED CAR FAIL "+message);checks++;Debug.Log("IMPORTED CAR PASS "+message);}
        static double Fingerprint(GameObject prefab)
        {
            double result=0;int index=1;
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))foreach(var vertex in filter.sharedMesh.vertices)
                result+=(vertex.x*.31+vertex.y*.17+vertex.z*.53)*(index++%7919+1);
            return result;
        }
        public static void Run()
        {
            GameObject instance=null;
            try
            {
                ProjectSetup.Configure();var asset=Resources.Load<GameObject>("Vehicles/CarConcept");Check(asset,"licensed appearance prefab is present");double fingerprint=Fingerprint(asset);var sourceMaterials=asset.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();var sourceColours=sourceMaterials.Select(m=>m.color).ToArray();
                instance=new GameObject("Isolated imported visual proof");instance.transform.SetPositionAndRotation(new Vector3(12,3,-4),Quaternion.Euler(10,73,-2));
                var visual=instance.AddComponent<CarVisual>();visual.Build(Color.blue,true);Check(visual.ImportedAppearance,"starter coupe uses the licensed appearance adapter");
                Check(instance.GetComponentsInChildren<Collider>().Length==0,"appearance adds no collider or physics component");
                Check(visual.Wheels.Length==4&&visual.Wheels.Distinct().Count()==4&&visual.Wheels.All(w=>w.parent==instance.transform),"four distinct wheel animation pivots remain directly owned by the vehicle");
                Check(Mathf.Abs(visual.Wheels[2].localPosition.x-visual.Wheels[0].localPosition.x-1.58f)<.001f&&Mathf.Abs(visual.Wheels[1].localPosition.z-visual.Wheels[0].localPosition.z-2.72f)<.001f&&visual.Wheels.All(w=>w.localPosition.y>.24f&&w.localPosition.y<.26f),"measured stock wheel pivots have1.58mtrack/original2.72m wheelbase and settled suspension datum");
                Check(visual.Wheels.All(w=>Vector3.Dot(w.GetChild(0).right,instance.transform.right)>.999f),"source steering pose is neutralised without reversing the wheel axles");
                Check(visual.SteeringWheel&&Math.Abs(visual.CockpitEye.x)<.01f&&visual.CockpitEye.y<1.15f,"camera and steering use the concept car's centre driver position");
                var directionRoot=new GameObject("Steering direction proof");ImportedCarAppearance direction;Check(ImportedCarAppearance.TryBuild(directionRoot.transform,Color.blue,0,0,0,out direction),"direction check builds the actual imported wheel adapter");
                var shaft=direction.SteeringWheel;var normal=shaft.TransformDirection(Vector3.up).normalized;var upper=Vector3.ProjectOnPlane(Vector3.up,normal).normalized;var landmark=shaft.InverseTransformDirection(upper);
                direction.Steering(-9);Check(shaft.TransformDirection(landmark).x>.1f,"small positive right input moves the upper rim right in the cockpit");direction.Steering(9);Check(shaft.TransformDirection(landmark).x<-.1f,"small negative left input moves the upper rim left in the cockpit");direction.ReleaseMaterials();Geometry.ReleaseMeshes(directionRoot);UnityEngine.Object.DestroyImmediate(directionRoot);

                Check(visual.Body&&Vector4.Distance(visual.Body.sharedMaterial.color,Color.blue)<.001f,"selected paint reaches the imported body material");
                var filters=instance.GetComponentsInChildren<GeneratedMeshOwner>().Where(o=>o.Mesh).ToArray();var originals=filters.Select(o=>o.Mesh.vertices).ToArray();var normals=filters.Select(o=>o.Mesh.normals).ToArray();
                var watch=System.Diagnostics.Stopwatch.StartNew();visual.Dent(instance.transform.TransformPoint(new Vector3(0,.7f,2.15f)),18,instance.transform.TransformDirection(Vector3.back));watch.Stop();
                Check(visual.DeformationExtent>.1f&&visual.DeformationExtent<.71f,"a front collision crumples the imported panels in vehicle space");
                Check(filters.Any(o=>o.Mesh.vertices.Where((p,i)=>p!=originals[Array.IndexOf(filters,o)][i]).Any()),"damage changes actual per-car mesh vertices");
                Check(Math.Abs(Fingerprint(asset)-fingerprint)<.000001&&sourceMaterials.Select((m,i)=>m.color==sourceColours[i]).All(x=>x),"crashing and repainting do not mutate shared source mesh/material assets");
                Check(filters.All(o=>{var ns=o.Mesh.normals;var ts=o.Mesh.tangents;return ts.Length==0||ns.Select((n,i)=>Mathf.Abs(Vector3.Dot(n,new Vector3(ts[i].x,ts[i].y,ts[i].z)))<.001f).All(x=>x);}),"deformed normal-map tangents remain orthogonal to new panel normals");
                for(int i=0;i<12;i++)visual.Dent(instance.transform.TransformPoint(new Vector3(0,.7f,2.15f)),30,instance.transform.TransformDirection(Vector3.back));
                Check(visual.DeformationExtent<=.701f,"repeated imported-panel impacts stay bounded");
                visual.Repair();Check(visual.DeformationExtent==0&&filters.Select((o,i)=>o.Mesh.vertices.SequenceEqual(originals[i])&&o.Mesh.normals.SequenceEqual(normals[i])).All(x=>x),"repair restores exact original geometry and authored normals");
                visual.SetAutomaticLights(true);Check(visual.Headlights.Length==2&&visual.Headlights.All(l=>l&&l.enabled),"night lighting has two working physical headlamps");visual.SetAutomaticLights(false);Check(visual.Headlights.All(l=>!l.enabled),"daylight parks the headlamps");
                Check(visual.Wipers.Length==2&&visual.Wipers.All(w=>w.GetComponentsInChildren<MeshRenderer>().Length==2),"two lightweight animated wiper assemblies replace the static source mesh");
                visual.SetPresentationHidden(true);visual.Rebuild(new Core.OwnedCar{model=0,paint=3,wheels=1,bodyKit=1,finish=2});
                Check(visual.ImportedAppearance&&visual.PresentationHidden&&visual.GetComponentsInChildren<Renderer>().All(r=>!r.enabled),"garage rebuild preserves hidden-player presentation");visual.SetPresentationHidden(false);
                Check(visual.GetComponentsInChildren<Transform>().Count(t=>t.name=="Alternate spoke")==20&&visual.GetComponentsInChildren<Transform>().Any(t=>t.name=="Concept rear spoiler"),"wheel style and body-kit purchases remain visible");
                Check(visual.GetComponentsInChildren<Transform>(true).Where(t=>t.name.EndsWith("Rim",StringComparison.Ordinal)).All(t=>!t.gameObject.activeSelf),"alternate wheels hide the source rims instead of overlaying both styles");
                visual.Wheels[1].localRotation=Quaternion.Euler(90,15,0);visual.FollowWheel(1,15);var caliper=visual.GetComponentsInChildren<Transform>().First(t=>t.name=="Fixed brake caliper 1");Check(Quaternion.Angle(caliper.localRotation,Quaternion.Euler(0,15,0))<.01f&&caliper.localPosition==visual.Wheels[1].localPosition,"brake calipers follow suspension and steering without rolling with tyres");
                Check(Math.Abs(Fingerprint(asset)-fingerprint)<.000001,"rebuilding styling leaves shared source art intact");
                visual.Rebuild(new Core.OwnedCar{model=0,finish=1});var matte=visual.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m.name.Contains("Paint ")).Distinct().ToArray();Check(matte.Length>=2&&matte.All(m=>m.GetFloat("_Smoothness")<.2f&&(!m.HasProperty("_ClearCoatMask")||m.GetFloat("_ClearCoatMask")==0)),"matte finish removes the glossy clear coat as well as lowering base smoothness");
                visual.Rebuild(new Core.OwnedCar{model=1});Check(!visual.ImportedAppearance&&visual.Wheels.Length==4,"other owned models retain their existing distinct appearance");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/imported-car-visual.txt",$"PASS {checks} imported mesh / physics boundary / paint / wheel / damage / repair / style checks\nfirstDentMilliseconds={watch.Elapsed.TotalMilliseconds:F2}\nActual lighting/camera/gameplay rendering still requires native verification.\n");
                UnityEngine.Object.DestroyImmediate(instance);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);if(instance)UnityEngine.Object.DestroyImmediate(instance);EditorApplication.Exit(1);}
        }
    }
}
