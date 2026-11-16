using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted.Editor
{
    // Geometry regression for the actual visible opaque car meshes. This does
    // not replace native image inspection or hide cabin parts to pass a test.
    public static class ImportedCockpitClearanceVerification
    {
        [Serializable] sealed class Probe {public float yaw,elevation,distance=-1;public string blocker="",material="";}
        [Serializable] sealed class Result {public Vector3 oldEye,actualEye;public Probe[] oldProbes,actualProbes;public int opaqueMeshes,opaqueTriangles;}
        sealed class Surface {public Vector3[] vertices;public int[] triangles;public Bounds bounds;public CullMode cull;public string path,material;}
        static int checks;
        static void Check(bool value,string text){if(!value)throw new Exception("COCKPIT CLEARANCE FAIL: "+text);checks++;Debug.Log("COCKPIT CLEARANCE PASS: "+text);}
        static string Path(Transform t,Transform root)
        {var names=new List<string>();while(t){names.Add(t.name);if(t==root)break;t=t.parent;}names.Reverse();return string.Join("/",names);}
        static List<Surface> Surfaces(Transform root)
        {
            var surfaces=new List<Surface>();foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy||!filter.sharedMesh)continue;
                var mesh=filter.sharedMesh;var materials=renderer.sharedMaterials;var matrix=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;var vertices=mesh.vertices.Select(p=>matrix.MultiplyPoint3x4(p)).ToArray();
                var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var point in vertices)bounds.Encapsulate(point);
                for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
                {
                    var material=materials[Mathf.Min(submesh,materials.Length-1)];if(!material)continue;
                    if(material.renderQueue>=(int)RenderQueue.Transparent||material.HasProperty("_Surface")&&material.GetFloat("_Surface")>.5f)continue;
                    surfaces.Add(new Surface{vertices=vertices,triangles=mesh.GetTriangles(submesh),bounds=bounds,cull=material.HasProperty("_Cull")?(CullMode)(int)material.GetFloat("_Cull"):CullMode.Back,path=Path(filter.transform,root),material=material.name});
                }
            }
            return surfaces;
        }
        static bool Intersection(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,CullMode cull,out float distance)
        {
            distance=0;var e1=b-a;var e2=c-a;var p=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,p);
            if(Mathf.Abs(determinant)<.0000001f||cull==CullMode.Back&&determinant<=0||cull==CullMode.Front&&determinant>=0)return false;
            float inverse=1/determinant;var t=origin-a;float u=Vector3.Dot(t,p)*inverse;if(u<0||u>1)return false;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)*inverse;if(v<0||u+v>1)return false;distance=Vector3.Dot(e2,q)*inverse;return distance>=.06f&&distance<=2.5f;
        }
        static Probe Cast(List<Surface> surfaces,Vector3 eye,float yaw,float elevation)
        {
            float y=yaw*Mathf.Deg2Rad,e=elevation*Mathf.Deg2Rad;var direction=new Vector3(Mathf.Sin(y)*Mathf.Cos(e),Mathf.Sin(e),Mathf.Cos(y)*Mathf.Cos(e));var ray=new Ray(eye,direction);var result=new Probe{yaw=yaw,elevation=elevation};float closest=2.5f;
            foreach(var surface in surfaces)
            {
                if(!surface.bounds.IntersectRay(ray,out float entry)||entry>closest)continue;var indices=surface.triangles;
                for(int i=0;i<indices.Length;i+=3)
                    if(Intersection(eye,direction,surface.vertices[indices[i]],surface.vertices[indices[i+1]],surface.vertices[indices[i+2]],surface.cull,out float distance)&&distance<closest)
                    {closest=distance;result.distance=distance;result.blocker=surface.path;result.material=surface.material;}
            }
            return result;
        }
        static Probe[] Grid(List<Surface> surfaces,Vector3 eye)=>new[]{-10f,0,10}.SelectMany(yaw=>new[]{0f,10,20,30}.Select(elevation=>Cast(surfaces,eye,yaw,elevation))).ToArray();
        public static void Run()
        {
            GameObject root=null;
            try
            {
                ProjectSetup.Configure();root=new GameObject("Cockpit geometry regression");var visual=root.AddComponent<CarVisual>();visual.Build(Color.blue,true);Check(visual.ImportedAppearance,"regression uses the actual runtime imported appearance adapter");
                var surfaces=Surfaces(root.transform);Check(surfaces.Count>30&&surfaces.Any(s=>s.path.Contains("InteriorPillar")),"opaque cabin and pillar meshes remain present and visible");
                var report=new Result{oldEye=new Vector3(0,1.015f,.1f),actualEye=visual.CockpitEye,opaqueMeshes=surfaces.Count,opaqueTriangles=surfaces.Sum(s=>s.triangles.Length/3)};report.oldProbes=Grid(surfaces,report.oldEye);report.actualProbes=Grid(surfaces,report.actualEye);
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/imported-cockpit-clearance.json",JsonUtility.ToJson(report,true));
                var oldCentral=report.oldProbes.Single(p=>p.yaw==0&&p.elevation==20);Check(oldCentral.distance>.06f&&oldCentral.distance<.5f,"previous eye reproduces the near opaque upper-windscreen obstruction");
                Debug.Log("Previous-eye blocker: "+oldCentral.blocker+" · "+oldCentral.material+" · "+oldCentral.distance.ToString("F3")+" m");
                foreach(var probe in report.actualProbes)Check(probe.distance<0,"actual eye clears yaw "+probe.yaw+" / elevation "+probe.elevation+" degrees"+(probe.distance>=0?"; blocked by "+probe.blocker+" at "+probe.distance:""));
                Check(report.actualEye.y>.8f&&report.actualEye.y<1.1f&&report.actualEye.z>0&&report.actualEye.z<.8f,"corrected eye remains inside the cabin below the roof and behind the steering assembly");
                File.WriteAllText("Builds/Verification/imported-cockpit-clearance.txt",$"PASS {checks} actual opaque-mesh cockpit clearance checks\noldEye={report.oldEye}\nactualEye={report.actualEye}\noldCentralBlocker={oldCentral.blocker}\noldCentralDistance={oldCentral.distance:F3}m\nopaqueMeshes={report.opaqueMeshes}\nopaqueTriangles={report.opaqueTriangles}\nNative captures remain required for complete visual acceptance.\n");
                UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);if(root)UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(1);}
        }
    }
}
