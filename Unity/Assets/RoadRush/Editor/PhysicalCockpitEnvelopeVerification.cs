using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class PhysicalCockpitEnvelopeVerification
    {
        const float NearClip=.06f;const int MaximumTriangles=250000;
        sealed class Face{public Vector3 a,b,c;public string mesh,material;public int triangle;public bool transparent;public float alpha,cull;}
        [Serializable] public sealed class Hit{public bool found,transparent;public string mesh,material;public int triangle;public float distance,alpha,cull;public Vector3 point,normal;}
        [Serializable] public sealed class Eye{public Vector3 position;public Hit roof,floor,left,right,forward,backward,forwardTransparent,backwardTransparent;public bool withinVerticalEnvelope,roofNearClipClear,floorNearClipClear,preferred;}
        [Serializable] public sealed class Report{public string status,interpretation;public float requiredNearClip;public int triangles,unreadableMeshes;public Vector3 boundsMin,boundsMax;public Eye[] eyes;}
        static bool Intersect(Vector3 origin,Vector3 direction,Face face,out float distance)
        {distance=0;var e1=face.b-face.a;var e2=face.c-face.a;var p=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,p);if(Mathf.Abs(determinant)<.000001f)return false;float inv=1/determinant;var t=origin-face.a;float u=Vector3.Dot(t,p)*inv;if(u<0||u>1)return false;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)*inv;if(v<0||u+v>1)return false;distance=Vector3.Dot(e2,q)*inv;return distance>.00001f&&distance<3;}
        public static void Run()
        {
            GameObject root=null;string previous=PlayerProfile.StorageOverride;int checks=0;var report=new Report{requiredNearClip=NearClip};
            void Check(bool ok,string why){if(!ok)throw new Exception("COCKPIT ENVELOPE FAIL "+why);checks++;Debug.Log("COCKPIT ENVELOPE PASS "+why);}
            try
            {
                PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/cockpit-envelope-profile.json");root=new GameObject("Isolated physical source cockpit envelope");var visual=root.AddComponent<CarVisual>();visual.Build(Color.blue,true,0);var initialProductionEye=visual.CockpitEye;Check(visual.ImportedAppearance,"actual imported source geometry used");var faces=new List<Face>();Bounds bounds=default;bool hasBounds=false;
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy||!filter.sharedMesh)continue;var mesh=filter.sharedMesh;if(!mesh.isReadable){report.unreadableMeshes++;continue;}var vertices=mesh.vertices;var materials=renderer.sharedMaterials;
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        var m=sub<materials.Length?materials[sub]:null;bool transparent=m&&(m.renderQueue>=3000||m.HasProperty("_Surface")&&m.GetFloat("_Surface")>.5f||m.GetTag("RenderType",false)=="Transparent");float alpha=m&&m.HasProperty("_BaseColor")?m.GetColor("_BaseColor").a:m&&m.HasProperty("_Color")?m.GetColor("_Color").a:1;float cull=m&&m.HasProperty("_Cull")?m.GetFloat("_Cull"):2;var indices=mesh.GetTriangles(sub);
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            if(faces.Count>=MaximumTriangles)throw new InvalidDataException("Physical enclosure triangle budget");var face=new Face{a=root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i]])),b=root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i+1]])),c=root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i+2]])),mesh=mesh.name,material=m?m.name:"",triangle=i/3,transparent=transparent,alpha=alpha,cull=cull};faces.Add(face);
                            foreach(var p in new[]{face.a,face.b,face.c}){if(!hasBounds){bounds=new Bounds(p,Vector3.zero);hasBounds=true;}else bounds.Encapsulate(p);}
                        }
                    }
                }
                report.triangles=faces.Count;report.boundsMin=bounds.min;report.boundsMax=bounds.max;Check(faces.Count>0&&report.unreadableMeshes==0,"all active source triangles readable within bounded scan");
                Hit Probe(Vector3 eye,Vector3 direction,bool onlyTransparent=false)
                {
                    var hit=new Hit{distance=3};foreach(var f in faces){if(onlyTransparent&&!f.transparent)continue;if(Intersect(eye,direction,f,out float distance)&&distance<hit.distance)hit=new Hit{found=true,distance=distance,point=eye+direction*distance,normal=Vector3.Cross(f.b-f.a,f.c-f.a).normalized,mesh=f.mesh,material=f.material,triangle=f.triangle,transparent=f.transparent,alpha=f.alpha,cull=f.cull};}return hit;
                }
                var eyes=new List<Eye>();foreach(var position in new[]{new Vector3(0,.95f,.43f),new Vector3(0,1.01f,.43f),new Vector3(0,1.02f,.43f),new Vector3(0,1.02f,.38f),new Vector3(0,1.03f,.43f),new Vector3(0,1.10f,.43f),new Vector3(0,.95f,.65f),new Vector3(0,1.03f,.65f),new Vector3(0,1.10f,.65f)})
                {
                    var eye=new Eye{position=position,roof=Probe(position,Vector3.up),floor=Probe(position,Vector3.down),left=Probe(position,Vector3.left),right=Probe(position,Vector3.right),forward=Probe(position,Vector3.forward),backward=Probe(position,Vector3.back),forwardTransparent=Probe(position,Vector3.forward,true),backwardTransparent=Probe(position,Vector3.back,true),preferred=position==new Vector3(0,1.02f,.38f)};eye.withinVerticalEnvelope=eye.roof.found&&eye.floor.found;eye.roofNearClipClear=eye.roof.found&&eye.roof.distance>=NearClip;eye.floorNearClipClear=eye.floor.found&&eye.floor.distance>=NearClip;eyes.Add(eye);Debug.Log("PHYSICAL COCKPIT "+JsonUtility.ToJson(eye));
                }
                report.eyes=eyes.ToArray();var preferred=eyes.Single(e=>e.preferred);Check(preferred.withinVerticalEnvelope&&preferred.roofNearClipClear&&preferred.floorNearClipClear,"selected1.02/.38 lies between actual roof/floor with at least.06m vertical clipping margin");
                var current=eyes.Single(e=>e.position==new Vector3(0,.95f,.43f));Check(current.withinVerticalEnvelope,"current source eye has real above/below enclosure references");Check(visual.CockpitEye==initialProductionEye,"diagnostic never changes production eye property");
                report.status="PASS_PHYSICAL_ENVELOPE_ONLY";report.interpretation="Up/down/side physical source-triangle probes include transparent glass and BOTH triangle sides, ignoring material culling only for this enclosure diagnostic. These do not replace unchanged visible-cull horizon/upward/road probes or PNG review. Three-metre no-hit cap is explicitly not a roof distance; found=false means unknown/open, not clear physical roof. NearClip.06 is a rendering margin, not ergonomic certification.";
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/cockpit-physical-envelope.json",JsonUtility.ToJson(report,true));File.WriteAllText("Builds/Verification/cockpit-physical-envelope.txt","PASS "+checks+" physical source enclosure checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){report.status="FAIL";report.interpretation=e.ToString();Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/cockpit-physical-envelope.json",JsonUtility.ToJson(report,true));Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}}
        }
    }
}
