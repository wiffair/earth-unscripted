using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class HeadlightContractVerification
    {
        public static void Run()
        {
            int checks=0;var roots=new List<GameObject>();string prior=PlayerProfile.StorageOverride;
            void Check(bool ok,string why){if(!ok)throw new Exception("HEADLIGHT CONTRACT FAIL "+why);checks++;Debug.Log("HEADLIGHT CONTRACT PASS "+why);}
            try
            {
                PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/headlight-contract-profile.json");
                Check(HeadlightTuning.Intensity==2880&&HeadlightTuning.Range==105&&HeadlightTuning.SpotAngle==58&&HeadlightTuning.InnerSpotAngle==38,"shared empirically tuned engine intensity preserves original beam range and cones");
                for(int model=0;model<Core.GarageRules.Catalogue.Length;model++)
                {
                    var root=new GameObject("Isolated headlight model "+model);roots.Add(root);var visual=root.AddComponent<CarVisual>();visual.Build(Color.blue,true,model);
                    var vertices=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh&&f.gameObject.activeInHierarchy).SelectMany(f=>f.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
                    var visibleBounds=new Bounds(vertices[0],Vector3.zero);foreach(var vertex in vertices)visibleBounds.Encapsulate(vertex);
                    Check(model!=0||visual.ImportedAppearance,"starter test uses actual imported appearance");Check(visual.Headlights.Length==2&&visual.Headlights.All(l=>l)&&root.GetComponentsInChildren<Light>(true).Length==2,"exactly two real headlights, no extra light cost model "+model);
                    Check(visual.Headlights[0].transform.localPosition.x<0&&visual.Headlights[1].transform.localPosition.x>0,"paired emitters remain on opposite vehicle sides model "+model);
                    foreach(var light in visual.Headlights)
                    {
                        Check(light.type==LightType.Spot&&light.intensity==HeadlightTuning.Intensity&&light.range==HeadlightTuning.Range&&light.spotAngle==HeadlightTuning.SpotAngle&&light.innerSpotAngle==HeadlightTuning.InnerSpotAngle,"actual builder uses shared beam contract model "+model);
                        Check(light.shadows==LightShadows.Soft&&light.color==new Color(1,.94f,.84f)&&Quaternion.Angle(light.transform.localRotation,Quaternion.Euler(4,0,0))<.001f,"original soft shadows colour and pitch preserved model "+model);
                        var p=light.transform.localPosition;Check(Mathf.Abs(p.x)<Core.VehicleDimensions.ForModel(model).BodyWidth*.5f&&p.y>0&&p.y<visibleBounds.max.y&&Mathf.Abs(p.z-visibleBounds.max.z)<.08f,"emitters fit the calibrated body and remain within8cm of its measured nose model "+model);
                        var before=light.transform.localPosition;var rotation=light.transform.localRotation;HeadlightTuning.Configure(light);Check(light.transform.localPosition==before&&light.transform.localRotation==rotation,"Configure is idempotent and never moves vehicle geometry model "+model);
                    }
                    visual.SetAutomaticLights(true);Check(visual.Headlights.All(l=>l.enabled&&l.intensity==2880),"automatic on-state retains configured intensity model "+model);
                    visual.SetPresentationHidden(true);Check(visual.Headlights.All(l=>!l.enabled),"garage presentation hide suppresses beams model "+model);visual.SetPresentationHidden(false);Check(visual.Headlights.All(l=>l.enabled),"presentation restoration retains requested automatic on-state model "+model);
                    visual.SetAutomaticLights(false);Check(visual.Headlights.All(l=>!l.enabled&&l.intensity==2880),"day off-state leaves shared intensity intact model "+model);
                    visual.SetAutomaticLights(true);Check(visual.Headlights.All(l=>l.enabled),"second automatic activation remains valid model "+model);
                }
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/headlight-contract.txt","PASS "+checks+" actual imported/procedural light/geometry/switch contract checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=prior;foreach(var root in roots)if(root){Geometry.ReleaseMeshes(root);UnityEngine.Object.DestroyImmediate(root);}}
        }
    }
}
