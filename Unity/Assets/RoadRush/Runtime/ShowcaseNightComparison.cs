using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Opt-in causal capture only. Compare headlamp and asphalt responses
    /// at the same real pose, weather, exposure and camera; never save real settings.</summary>
    public sealed partial class ShowcaseCircuitCapture
    {
        float NightExposure()=>UnityEngine.Rendering.VolumeManager.instance.stack.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>()?.postExposure.value??0;
        static Color ScaledAmbient(Color value,float factor)=>new Color(value.r*factor,value.g*factor,value.b*factor,value.a);
        IEnumerator CompareNightLighting()
        {
            const float StationMetres=140;
            var route=session.World.Route;var car=session.Car;
            car.Recover(route.At(StationMetres),route.Rotation(StationMetres));
            car.StartLocked=true;car.NetworkDriven=true;car.NetworkBrake=car.NetworkHandbrake=1;car.NetworkThrottle=0;
            session.World.GeographicWorld.Refresh(car.Body.position,true);
            float settleBy=Time.unscaledTime+8;int stableTicks=0;
            while(stableTicks<20)
            {
                if(Time.unscaledTime>settleBy)throw new InvalidOperationException("Comparison placement did not settle");
                stableTicks=car.WheelGrounded.All(g=>g)?stableTicks+1:0;
                yield return new WaitForFixedUpdate();
            }
            var bodyPosition=car.Body.position;var bodyRotation=car.Body.rotation;
            var lamps=car.Visual.Headlights;
            var intensities=lamps.Select(l=>l.intensity).ToArray();
            var shadows=lamps.Select(l=>l.shadows).ToArray();
            var lampPositions=lamps.Select(l=>l.transform.localPosition).ToArray();
            var asphalt=session.World.Asphalt;float smoothness=asphalt.GetFloat("_Smoothness");
            var conditions=session.Conditions;bool environmentEnabled=conditions.enabled;
            float hour=conditions.Hour,rain=conditions.Rain,wetness=conditions.Wetness;
            var ambient=RenderSettings.ambientSkyColor;
            var ambientEquator=RenderSettings.ambientEquatorColor;var ambientGround=RenderSettings.ambientGroundColor;
            float exposure=NightExposure(),fog=RenderSettings.fogDensity,reflection=RenderSettings.reflectionIntensity;
            var exposureObject=new GameObject("Temporary diagnostic exposure");
            var exposureVolume=exposureObject.AddComponent<UnityEngine.Rendering.Volume>();
            var exposureProfile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            exposureProfile.hideFlags=HideFlags.DontSave;exposureVolume.isGlobal=true;exposureVolume.priority=10000;exposureVolume.sharedProfile=exposureProfile;
            var exposureAdjustment=exposureProfile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
            float originalTimeScale=Time.timeScale;bool wasKinematic=car.Body.isKinematic;
            var camera=session.View.GetComponent<Camera>();
            var records=new JArray();
            // These are diagnostic comparisons, not production settings or a
            // calibrated vehicle-photometry claim. Every changed value is restored.
            var variants=new[]{
                ("production",1f,smoothness,false,0f),
                ("no-beam",0f,smoothness,false,0f),
                ("intensity-2x",2f,smoothness,false,0f),
                ("intensity-4x",4f,smoothness,false,0f),
                ("no-headlamp-shadow",1f,smoothness,true,0f),
                ("lamp-forward-035m",1f,smoothness,false,.35f),
                ("wet-roughness-055",1f,.55f,false,0f),
                ("wet-roughness-065",1f,.65f,false,0f),
                ("ambient-4x",1f,smoothness,false,0f),
                ("exposure-plus-1",1f,smoothness,false,0f),
                ("exposure-plus-2",1f,smoothness,false,0f),
                ("balanced-control",2f,smoothness,false,0f)
            };
            conditions.enabled=false;car.Body.isKinematic=true;Time.timeScale=0;
            session.GetComponent<DrivingUI>().enabled=true;
            try
            {
                foreach(int mode in new[]{0,2})
                {
                    session.View.Mode=mode;session.View.enabled=true;
                    yield return new WaitForSecondsRealtime(1);
                    session.View.enabled=false;camera.fieldOfView=mode==2?74:65;
                    var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
                    foreach(var variant in variants)
                    {
                        for(int i=0;i<lamps.Length;i++)
                        {
                            lamps[i].intensity=intensities[i]*variant.Item2;
                            lamps[i].shadows=variant.Item4?LightShadows.None:shadows[i];
                            lamps[i].transform.localPosition=lampPositions[i]+Vector3.forward*variant.Item5;
                        }
                        asphalt.SetFloat("_Smoothness",variant.Item3);
                        float ambientFactor=variant.Item1=="ambient-4x"?4:variant.Item1=="balanced-control"?2:1;
                        float exposureOffset=variant.Item1=="exposure-plus-2"?2:variant.Item1=="exposure-plus-1"||variant.Item1=="balanced-control"?1:0;
                        RenderSettings.ambientSkyColor=ScaledAmbient(ambient,ambientFactor);
                        RenderSettings.ambientEquatorColor=ScaledAmbient(ambientEquator,ambientFactor);
                        RenderSettings.ambientGroundColor=ScaledAmbient(ambientGround,ambientFactor);
                        exposureAdjustment.postExposure.Override(exposure+exposureOffset);
                        yield return new WaitForEndOfFrame();
                        Check(Vector3.Distance(bodyPosition,car.Body.position)<.001f&&Quaternion.Angle(bodyRotation,car.Body.rotation)<.001f,"Comparison car pose remains fixed");
                        Check(Vector3.Distance(cameraPosition,camera.transform.position)<.001f&&Quaternion.Angle(cameraRotation,camera.transform.rotation)<.001f,"Comparison camera remains fixed");
                        Check(hour==conditions.Hour&&rain==conditions.Rain&&wetness==conditions.Wetness&&ScaledAmbient(ambient,ambientFactor)==RenderSettings.ambientSkyColor,"Comparison weather/time fixed; ambient matches declared control");
                        Check(Mathf.Abs(exposure+exposureOffset-NightExposure())<.0001f&&fog==RenderSettings.fogDensity&&reflection==RenderSettings.reflectionIntensity,"Comparison exposure matches declared control; fog/reflection fixed");
                        var texture=ScreenCapture.CaptureScreenshotAsTexture();
                        try
                        {
                            string view=mode==2?"cockpit":"chase";
                            string file=prefix+"-night-"+view+"-"+variant.Item1+".png";
                            File.WriteAllBytes(file,texture.EncodeToPNG());
                            var patches=new JArray();
                            foreach(float ahead in new[]{10f,20f,30f,40f,60f})foreach(float lateral in new[]{-2f,0f,2f})
                            {
                                var world=route.At(StationMetres+ahead,lateral);
                                world.y=route.Region.RoadSurfaceHeight(world.x,world.z)+.03f;
                                var pixel=camera.WorldToScreenPoint(world);
                                if(pixel.z<=0||pixel.x<texture.width*.18f||pixel.x>texture.width*.82f||pixel.y<texture.height*.35f||pixel.y>texture.height*.7f)continue;
                                var ray=camera.ScreenPointToRay(pixel);float expected=Vector3.Distance(camera.transform.position,world);
                                if(!Physics.Raycast(ray,out var hit,expected+1,~(1<<8),QueryTriggerInteraction.Ignore)||Mathf.Abs(hit.distance-expected)>1)continue;
                                var values=texture.GetPixels(Mathf.RoundToInt(pixel.x)-5,Mathf.RoundToInt(pixel.y)-5,11,11);
                                var luma=values.Select(c=>c.r*.2126f+c.g*.7152f+c.b*.0722f).OrderBy(v=>v).ToArray();
                                patches.Add(new JObject{["aheadMetres"]=ahead,["lateralMetres"]=lateral,["pixelX"]=pixel.x,["pixelY"]=pixel.y,["receiver"]=hit.collider.name,["mean"]=luma.Average(),["median"]=luma[luma.Length/2],["p95"]=luma[(int)((luma.Length-1)*.95f)],["saturatedFraction"]=values.Count(c=>Mathf.Max(c.r,Mathf.Max(c.g,c.b))>=.98f)/(float)values.Length});
                            }
                            Check(patches.Count>=6,"Comparison samples at least six visible physical road patches");
                            records.Add(new JObject{["view"]=view,["variant"]=variant.Item1,["png"]=file,["screenWidth"]=Screen.width,["screenHeight"]=Screen.height,["textureWidth"]=texture.width,["textureHeight"]=texture.height,["lampIntensityMultiplier"]=variant.Item2,["smoothness"]=variant.Item3,["headlampShadowsDisabled"]=variant.Item4,["lampForwardOffsetMetres"]=variant.Item5,["ambientMultiplier"]=ambientFactor,["postExposure"]=NightExposure(),["patches"]=patches});
                        }
                        finally{Destroy(texture);}
                    }
                }
                File.WriteAllText(prefix+"-night-comparison.json",new JObject{["status"]="CAPTURED_CONTROLS_NOT_VISUAL_ACCEPTANCE",["version"]=Application.version,["stationMetres"]=StationMetres,["hour"]=hour,["rain"]=rain,["wetness"]=wetness,["postExposure"]=exposure,["fogDensity"]=fog,["reflectionIntensity"]=reflection,["lampIntensities"]=new JArray(intensities),["records"]=records,["limits"]="Fixed-pose diagnostic. Display RGB luma is not lux or HDR photometry. Screenshots and physical-patch samples require human review; no driving, production-lighting fix, Windows or hardware acceptance is claimed."}.ToString());
                File.WriteAllText(prefix+"-showcase-result.json",new JObject{["status"]="PASS_CONTROLLED_NIGHT_CAPTURE_ONLY",["checks"]=new JArray(checks.Distinct()),["variants"]=records.Count}.ToString());
            }
            finally
            {
                for(int i=0;i<lamps.Length;i++){lamps[i].intensity=intensities[i];lamps[i].shadows=shadows[i];lamps[i].transform.localPosition=lampPositions[i];}
                asphalt.SetFloat("_Smoothness",smoothness);conditions.enabled=environmentEnabled;
                RenderSettings.ambientSkyColor=ambient;RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;
                exposureVolume.sharedProfile=null;exposureProfile.components.Clear();Destroy(exposureAdjustment);Destroy(exposureProfile);Destroy(exposureObject);
                car.Body.isKinematic=wasKinematic;Time.timeScale=originalTimeScale;
            }
            yield return new WaitForSecondsRealtime(.5f);Application.Quit();
        }
    }
}
