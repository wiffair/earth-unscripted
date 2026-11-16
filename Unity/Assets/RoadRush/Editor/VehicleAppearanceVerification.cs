using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace RoadRush.Editor
{
    public static class VehicleAppearanceVerification
    {
        public static void RepairAndRun()
        {
            try{VehicleAppearanceImporter.RepairGeneratedMaterialNames();Run();}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            GameObject instance=null;int checks=0;
            try
            {
                void Check(bool value,string text){if(!value)throw new Exception("VEHICLE PREFAB FAIL: "+text);checks++;Debug.Log("VEHICLE PREFAB PASS: "+text);}
                checks+=VehicleAppearanceImporter.VerifyBoundaries();checks+=VehicleAppearanceImporter.VerifyImportedPacking();
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(VehicleAppearanceImporter.PrefabPath);Check(prefab,"reviewed car prefab exists");instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);var transforms=instance.GetComponentsInChildren<Transform>(true);var renderers=instance.GetComponentsInChildren<MeshRenderer>(true);
                Check(renderers.Length==107,"all107 reviewed visible primitives are present");
                Check(instance.GetComponentsInChildren<MonoBehaviour>(true).Length==0&&instance.GetComponentsInChildren<Collider>(true).Length==0&&instance.GetComponentsInChildren<Rigidbody>(true).Length==0,"prefab contains appearance only, with no scripts or replacement physics");
                foreach(string removed in new[]{"Road Rush omitted branding surface 32","Road Rush omitted branding surface 23"})
                {var node=transforms.Single(t=>t.name==removed);Check(node.GetComponentsInChildren<MeshRenderer>(true).Length==0,"excluded mark mesh is absent from "+removed);}
                var wheels=new[]{"WheelFrontL","WheelFrontR","WheelRearL","WheelRearR"}.Select(name=>transforms.Single(t=>t.name==name)).ToArray();
                Check(wheels.Select(t=>t.position).Distinct().Count()==4&&wheels[0].position.x<0&&wheels[1].position.x>0&&wheels[0].position.z>wheels[2].position.z,"four named wheel pivots preserve handedness and front/rear placement");
                Check(transforms.Single(t=>t.name=="InteriorSteeringCylinder").childCount==5,"steering hierarchy remains independently animatable");
                int triangles=renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3),transparentTriangles=renderers.Where(r=>r.sharedMaterial.GetFloat("_Surface")>0).Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);int transparentRenderers=renderers.Count(r=>r.sharedMaterial.GetFloat("_Surface")>0);
                Check(triangles==213195,"prefab triangle count matches the reviewed derivative without decimation");
                Check(transparentRenderers>0&&renderers.Where(r=>r.sharedMaterial.GetFloat("_Surface")>0).All(r=>r.sharedMaterial.renderQueue==(int)RenderQueue.Transparent&&r.sharedMaterial.GetFloat("_ZWrite")==0&&r.shadowCastingMode==ShadowCastingMode.Off),"glass uses explicit transparent depth/shadow fallback");
                var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);Check(bounds.size.z>4&&bounds.size.z<5&&bounds.size.y>1&&bounds.size.y<1.5f,"actual imported world bounds remain metre-scaled");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/car-concept-asset-verification.txt",$"PASS {checks} source/accessor/material/texture/prefab checks\nrenderers={renderers.Length}\ntriangles={triangles}\ntransparentRenderers={transparentRenderers}\ntransparentTriangles={transparentTriangles}\nbounds={bounds}\n");
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{if(instance)UnityEngine.Object.DestroyImmediate(instance);}
        }
    }
}
