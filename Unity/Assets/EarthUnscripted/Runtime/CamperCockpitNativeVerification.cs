using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted
{
    // Called only by an explicit nonce-scoped beta acceptance driver.
    public static class CamperCockpitNativeVerification
    {
        sealed class Face {public Vector3 a,b,c;public string mesh;public int triangle;public bool glass,frontGlass;}
        sealed class Hit {public Face face;public float distance;}
        static bool Intersection(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,out float distance,float maximum=1.2f)
        {
            distance=0;var e1=b-a;var e2=c-a;var p=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,p);if(Mathf.Abs(determinant)<.000001f)return false;
            var t=origin-a;float u=Vector3.Dot(t,p)/determinant;if(u<0||u>1)return false;var q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)/determinant;if(v<0||u+v>1)return false;
            distance=Vector3.Dot(e2,q)/determinant;return distance>.03f&&distance<maximum;
        }
        public static IEnumerator Exercise(GameSession session,Action<bool,string> check)
        {
            check(SinglePlayerBeta.VerificationOutput!=null,"Camper visual proof uses a disposable beta profile");
            var entry=Core.ImportedVehicleCatalogue.Find(CamperCockpit.VehicleId);check(entry!=null,"Reviewed camper pack is available");
            var originalId=session.Profile.garage.activeId;bool menu=session.Menu,locked=session.Car.StartLocked,kinematic=session.Car.Body.isKinematic,controllerEnabled=session.Car.enabled;
            int mode=session.View.Mode;var eyeOverride=session.View.DevelopmentCockpitEye;
            bool network=session.Conditions.NetworkOverride;float hour=session.Conditions.Hour,rain=session.Conditions.Rain,wet=session.Conditions.Wetness,clouds=session.Conditions.Clouds,wind=session.Conditions.Wind,fog=session.Conditions.Fog;
            try
            {
                var owned=session.Profile.garage.cars.FirstOrDefault(c=>c.packVehicleId==CamperCockpit.VehicleId);
                if(owned==null){owned=new Core.OwnedCar{id="qa-camper-cockpit",model=entry.Model,packVehicleId=CamperCockpit.VehicleId};session.Profile.garage.cars.Add(owned);}
                session.SetMenu(true);check(session.SelectOwnedVehicle(owned.id),"Normal owned-car selection activates the camper");
                session.Car.StartLocked=true;session.Car.Body.isKinematic=true;
                var cabin=session.Car.Visual.GetComponentInChildren<CamperCockpit>();check(cabin,"Camper instantiates its model-specific interior");
                check(cabin.RemovedWindowCapTriangles==4,"Exactly four buried windscreen-cap triangles are omitted");
                var pack=VehiclePackLibrary.ForModel(entry.Model);check(CamperCockpit.Supports(pack),"Model-specific hollow-cabin adaptation is pinned to the reviewed camper archive");int sourceCaps=0,visibleCaps=0,deepOriginalReveals=0,opaqueQuarterPanels=0,leftFrontPosts=0,rightFrontPosts=0;
                foreach(var part in pack.Parts)for(int i=0;i<part.Triangles.Length;i+=3)if(CamperCockpit.WindowCap(part.Vertices[part.Triangles[i]],part.Vertices[part.Triangles[i+1]],part.Vertices[part.Triangles[i+2]]))sourceCaps++;
                foreach(var f in session.Car.Visual.GetComponentsInChildren<MeshFilter>())
                {
                    if(f.name!="Warm ivory body"||!f.sharedMesh)continue;var vertices=f.sharedMesh.vertices;var triangles=f.sharedMesh.triangles;
                    for(int i=0;i<triangles.Length;i+=3)if(CamperCockpit.WindowCap(vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]))visibleCaps++;
                    var actualBody=new VehiclePackPart{Name=f.name,Attachment="body",Vertices=vertices,Triangles=triangles,Normals=f.sharedMesh.normals,UV=f.sharedMesh.uv};
                    deepOriginalReveals+=CamperCockpit.DeepWindowRevealTriangles(actualBody);
                    opaqueQuarterPanels+=CamperCockpit.OpaqueQuarterlightTriangles(actualBody);
                    leftFrontPosts+=CamperCockpit.RetainedFrontPostTriangles(actualBody,-1);rightFrontPosts+=CamperCockpit.RetainedFrontPostTriangles(actualBody,1);
                }
                check(sourceCaps==4&&visibleCaps==0,"Reviewed pack data retains its original caps; instantiated mesh opens only the obstructing apertures");
                check(cabin.GetComponentsInChildren<Collider>().Length==0,"Interior detailing adds no physical obstructions");
                int remainingBakedWipers=CamperCockpit.CountBakedWiperComponents(session.Car.Visual.transform);
                session.Conditions.NetworkConditions(14,0,0,.15f,4,.00035f);session.SetMenu(false);session.View.DevelopmentCockpitEye=null;session.View.Mode=2;
                // Let the real camera's production FOV interpolation settle;
                // first-frame chase FOV would crop the passenger dashboard edge.
                double viewReady=Time.realtimeSinceStartupAsDouble+1;
                while(Time.realtimeSinceStartupAsDouble<viewReady)yield return new WaitForEndOfFrame();
                var eye=session.Car.Visual.CockpitEye;check(Vector3.Distance(session.View.transform.position,session.Car.transform.TransformPoint(eye))<.01f,"Actual driver camera uses the camper eye inside its window aperture");
                var steering=session.Car.Visual.SteeringWheel;
                var rim=steering.Find("Raked period steering rim");check(rim,"Camper has its period steering assembly");
                var shaft=rim.forward;var hub=rim.position;var neutral=rim.rotation;session.Car.enabled=false;
                foreach(float input in new[]{-1f,-.5f,0f,.5f,1f,0f})
                {
                    session.Car.Visual.SetSteering(input);
                    double settle=Time.realtimeSinceStartupAsDouble+.80;
                    while(Time.realtimeSinceStartupAsDouble<settle)yield return null;
                    if(input!=0)check(Quaternion.Angle(neutral,rim.rotation)>80,"Camper steering rim actually rotates under steering input "+input);
                    check(Vector3.Angle(shaft,rim.forward)<.01f&&Vector3.Distance(hub,rim.position)<.0001f,"Camper wheel turns about its fixed raked shaft at input "+input);
                }
                session.Car.enabled=controllerEnabled;
                var output=SinglePlayerBeta.VerificationOutput;
                // Preserve the real driving view even when a later geometric
                // assertion fails and finally restores the previous vehicle.
                var image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(output,"camper-driver-interior.png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}
                JObject Point(Vector3 v)=>new JObject{{"x",v.x},{"y",v.y},{"z",v.z}};
                var faces=new List<Face>();
                foreach(var f in session.Car.Visual.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer=f.GetComponent<MeshRenderer>();if(!renderer||!renderer.enabled||!f.sharedMesh)continue;
                    var vertices=f.sharedMesh.vertices;var triangles=f.sharedMesh.triangles;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        var a=f.transform.TransformPoint(vertices[triangles[i]]);var b=f.transform.TransformPoint(vertices[triangles[i+1]]);var c=f.transform.TransformPoint(vertices[triangles[i+2]]);
                        bool glass=f.name.IndexOf("glass",StringComparison.OrdinalIgnoreCase)>=0;
                        bool frontGlass=glass&&session.Car.transform.InverseTransformPoint(a).z>1.99f&&session.Car.transform.InverseTransformPoint(b).z>1.99f&&session.Car.transform.InverseTransformPoint(c).z>1.99f;
                        faces.Add(new Face{a=a,b=b,c=c,mesh=f.name,triangle=i/3,glass=glass,frontGlass=frontGlass});
                    }
                }
                foreach(var panel in new[]{new Vector3(-.813f,.99f,1.27f),new Vector3(.813f,.99f,1.27f),new Vector3(0,.951f,1.968f),new Vector3(0,1.9f,1.995f)})
                {
                    var origin=session.Car.transform.TransformPoint(eye);var target=cabin.transform.TransformPoint(panel);var direction=(target-origin).normalized;
                    bool covered=false;
                    foreach(var face in faces)if(!face.glass&&Vector3.Dot(Vector3.Cross(face.b-face.a,face.c-face.a),direction)<0&&Intersection(origin,direction,face.a,face.b,face.c,out _,Vector3.Distance(origin,target)+.1f)){covered=true;break;}
                    check(covered,"Driver-facing interior panel closes cabin at "+panel);
                }
                foreach(float x in new[]{-.79f,0,.79f})foreach(float z in new[]{.40f,.65f,1.27f,1.90f})
                {
                    var origin=cabin.transform.TransformPoint(new Vector3(x,.85f,z));var down=-cabin.transform.up;bool floor=false;
                    foreach(var face in faces)if(face.mesh=="Cabin vents and instrument recesses"&&Vector3.Dot(Vector3.Cross(face.b-face.a,face.c-face.a),down)<0&&Intersection(origin,down,face.a,face.b,face.c,out _,.25f)){floor=true;break;}
                    check(floor,"Continuous driver-facing camper floor at "+x+","+z);
                }
                var camera=session.View.GetComponent<Camera>();
                var gaugeViewport=camera.WorldToViewportPoint(cabin.Needle.position);
                bool gaugeVisible=gaugeViewport.z>0&&gaugeViewport.x>=0&&gaugeViewport.x<=1&&gaugeViewport.y>=0&&gaugeViewport.y<=1;
                int steeringCornersVisible=0;
                foreach(var wheelRenderer in session.Car.Visual.SteeringWheel.GetComponentsInChildren<MeshRenderer>())
                {
                    var bounds=wheelRenderer.bounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                        var view=camera.WorldToViewportPoint(point);if(view.z>0&&view.x>=0&&view.x<=1&&view.y>=0&&view.y<=1)steeringCornersVisible++;
                    }
                }
                var rayResults=new JArray();
                foreach(float yaw in new[]{-15f,0,15f})foreach(float pitch in new[]{-8f,0,8f})
                {
                    var origin=session.Car.transform.TransformPoint(eye);var direction=session.Car.transform.TransformDirection(Quaternion.Euler(-pitch,yaw,0)*Vector3.forward);
                    var hits=new List<Hit>();float glassDistance=float.PositiveInfinity;
                    foreach(var face in faces)
                    {
                        if(!Intersection(origin,direction,face.a,face.b,face.c,out float distance))continue;
                        hits.Add(new Hit{face=face,distance=distance});if(face.frontGlass)glassDistance=Mathf.Min(glassDistance,distance);
                    }
                    var ordered=hits.OrderBy(h=>h.distance).ToArray();
                    var obstruction=ordered.FirstOrDefault(h=>!h.face.glass&&h.distance<glassDistance-.001f);
                    JObject Describe(Hit hit)
                    {
                        var point=origin+direction*hit.distance;var face=hit.face;
                        return new JObject{{"mesh",face.mesh},{"triangle",face.triangle},{"triangleIndexOffset",face.triangle*3},{"distance",hit.distance},{"pointWorld",Point(point)},{"pointCarLocal",Point(session.Car.transform.InverseTransformPoint(point))},{"aCarLocal",Point(session.Car.transform.InverseTransformPoint(face.a))},{"bCarLocal",Point(session.Car.transform.InverseTransformPoint(face.b))},{"cCarLocal",Point(session.Car.transform.InverseTransformPoint(face.c))},{"glass",face.glass},{"frontGlass",face.frontGlass}};
                    }
                    bool hasGlass=!float.IsInfinity(glassDistance);
                    rayResults.Add(new JObject{{"yaw",yaw},{"pitch",pitch},{"glassIntersectionFound",hasGlass},{"glassDistance",hasGlass?(JToken)new JValue(glassDistance):JValue.CreateNull()},{"interiorClear",hasGlass&&obstruction==null},{"interiorObstruction",obstruction==null?JValue.CreateNull():(JToken)Describe(obstruction)},{"hitsTo1_2Metres",new JArray(ordered.Take(16).Select(Describe))}});
                }
                var instruments=new JArray();
                var probes=new[]{
                    Tuple.Create("speedometer-face",new Vector3(.455f,1.33f,1.653f)),
                    Tuple.Create("ivory-dashboard",new Vector3(.10f,1.29f,1.675f)),
                    Tuple.Create("vent-slots",new Vector3(-.20f,1.292f,1.674f)),
                    Tuple.Create("passenger-dashboard",new Vector3(-.40f,1.286f,1.670f)),
                    Tuple.Create("shelf-front-lip",new Vector3(0,1.12f,1.645f))};
                foreach(var probe in probes)
                {
                    var origin=session.Car.transform.TransformPoint(eye);var target=cabin.transform.TransformPoint(probe.Item2);var delta=target-origin;float limit=delta.magnitude-.002f;var direction=delta.normalized;
                    Hit obstruction=null;float nearest=limit;
                    foreach(var face in faces)
                    {
                        if(face.glass)continue;
                        if(Intersection(origin,direction,face.a,face.b,face.c,out float distance,limit)&&distance<nearest){obstruction=new Hit{face=face,distance=distance};nearest=distance;}
                    }
                    JToken detail=JValue.CreateNull();
                    if(obstruction!=null)
                    {
                        var face=obstruction.face;detail=new JObject{{"mesh",face.mesh},{"triangle",face.triangle},{"triangleIndexOffset",face.triangle*3},{"distance",obstruction.distance},{"pointCarLocal",Point(session.Car.transform.InverseTransformPoint(origin+direction*obstruction.distance))},{"aCarLocal",Point(session.Car.transform.InverseTransformPoint(face.a))},{"bCarLocal",Point(session.Car.transform.InverseTransformPoint(face.b))},{"cCarLocal",Point(session.Car.transform.InverseTransformPoint(face.c))}};
                    }
                    var viewport=camera.WorldToViewportPoint(target);bool onScreen=viewport.z>0&&viewport.x>=0&&viewport.x<=1&&viewport.y>=0&&viewport.y<=1;
                    instruments.Add(new JObject{{"label",probe.Item1},{"targetCarLocal",Point(probe.Item2)},{"viewport",Point(viewport)},{"onScreen",onScreen},{"unobstructed",obstruction==null},{"obstruction",detail}});
                }
                var frameBounds=new JArray();bool matchedFrames=cabin.WindshieldFrames!=null&&cabin.WindshieldFrames.Length==2;
                Bounds? firstFrame=null;
                if(matchedFrames)foreach(var frame in cabin.WindshieldFrames)
                {
                    var mesh=frame.GetComponentInChildren<MeshFilter>().sharedMesh;var bounds=mesh.bounds;
                    frameBounds.Add(new JObject{{"name",frame.name},{"centreCarLocal",Point(session.Car.transform.InverseTransformPoint(frame.position))},{"meshSize",Point(bounds.size)}});
                    matchedFrames&=bounds.size.z<=.013f&&bounds.size.x>=.70f&&bounds.size.x<=.714f&&bounds.size.y>=.45f&&bounds.size.y<=.464f;
                    if(firstFrame.HasValue)matchedFrames&=(bounds.size-firstFrame.Value.size).sqrMagnitude<.00000001f;else firstFrame=bounds;
                }
                var paneOpeningRays=new JArray();
                for(int side=-1;side<=1;side+=2)foreach(float dx in new[]{-.22f,0,.22f})foreach(float y in new[]{1.48f,1.625f,1.77f})
                {
                    var origin=session.Car.transform.TransformPoint(eye);var target=session.Car.transform.TransformPoint(new Vector3(side*.395f+dx,y,2.034f));var delta=target-origin;float distanceToPane=delta.magnitude;var direction=delta.normalized;
                    bool glassFound=false;Hit obstruction=null;float nearest=distanceToPane-.001f;
                    foreach(var face in faces)
                    {
                        if(!Intersection(origin,direction,face.a,face.b,face.c,out float distance,distanceToPane+.02f))continue;
                        if(face.frontGlass&&Mathf.Abs(distance-distanceToPane)<.005f)glassFound=true;
                        else if(!face.glass&&distance<nearest){nearest=distance;obstruction=new Hit{face=face,distance=distance};}
                    }
                    JToken blocked=JValue.CreateNull();if(obstruction!=null)blocked=new JObject{{"mesh",obstruction.face.mesh},{"triangleIndexOffset",obstruction.face.triangle*3},{"pointCarLocal",Point(session.Car.transform.InverseTransformPoint(origin+direction*obstruction.distance))},{"distance",obstruction.distance}};
                    paneOpeningRays.Add(new JObject{{"pane",side<0?"left":"right"},{"targetCarLocal",Point(session.Car.transform.InverseTransformPoint(target))},{"actualGlassFound",glassFound},{"clear",glassFound&&obstruction==null},{"obstruction",blocked}});
                }
                var quarters=new JArray();bool quarterMeshes=cabin.Quarterlights!=null&&cabin.Quarterlights.Length==2;
                if(quarterMeshes)foreach(var quarter in cabin.Quarterlights)
                {
                    var mesh=quarter.GetComponentInChildren<MeshFilter>().sharedMesh;var vertices=mesh.vertices;
                    var target=quarter.TransformPoint((vertices[0]+vertices[1]+vertices[2]+vertices[3])*.25f);var origin=session.Car.transform.TransformPoint(eye);var delta=target-origin;float distanceToPane=delta.magnitude;var direction=delta.normalized;
                    bool foundGlass=false;Hit obstruction=null;float nearest=distanceToPane-.002f;
                    foreach(var face in faces)
                    {
                        if(!Intersection(origin,direction,face.a,face.b,face.c,out float distance,distanceToPane+.02f))continue;
                        if(face.glass&&Mathf.Abs(distance-distanceToPane)<.005f)foundGlass=true;
                        else if(!face.glass&&distance<nearest){nearest=distance;obstruction=new Hit{face=face,distance=distance};}
                    }
                    var front=session.Car.transform.InverseTransformPoint(quarter.TransformPoint((vertices[2]+vertices[3])*.5f));int side=front.x<0?-1:1;
                    var windshieldEdge=new Vector3(side*.739f,front.y,2.034f);float postSpan=Vector3.Distance(front,windshieldEdge);
                    JToken blocked=JValue.CreateNull();if(obstruction!=null)blocked=new JObject{{"mesh",obstruction.face.mesh},{"triangleIndexOffset",obstruction.face.triangle*3},{"pointCarLocal",Point(session.Car.transform.InverseTransformPoint(origin+direction*obstruction.distance))}};
                    quarters.Add(new JObject{{"name",quarter.name},{"glassFound",foundGlass},{"clear",foundGlass&&obstruction==null},{"targetCarLocal",Point(session.Car.transform.InverseTransformPoint(target))},{"quarterlightFront",Point(front)},{"postSpanToWindshieldMetres",postSpan},{"structuralPostPresent",side<0?leftFrontPosts>0:rightFrontPosts>0},{"obstruction",blocked}});
                }
                var report=new JObject{{"passed",false},{"sourceArchiveHash",pack.ArchiveHash},{"productionEye",Point(eye)},{"verticalFovDegrees",camera.fieldOfView},{"cockpitLookDownDegrees",session.Car.Visual.CockpitPitchDegrees},{"gaugeViewport",Point(gaugeViewport)},{"gaugeOnScreen",gaugeVisible},{"steeringBoundsCornersOnScreen",steeringCornersVisible},{"sourceCapTriangles",sourceCaps},{"instantiatedCapTriangles",visibleCaps},{"removedCaps",cabin.RemovedWindowCapTriangles},{"hollowedInteriorTriangles",cabin.HollowedInteriorTriangles},{"trimmedWindowRevealTriangles",cabin.TrimmedWindowRevealTriangles},{"remainingOriginalDeepRevealTriangles",deepOriginalReveals},{"matchedThinFrames",matchedFrames},{"openedQuarterlightTriangles",cabin.OpenedQuarterlightTriangles},{"remainingOpaqueQuarterPanelTriangles",opaqueQuarterPanels},{"leftFrontPostTriangles",leftFrontPosts},{"rightFrontPostTriangles",rightFrontPosts},{"quarterlights",quarters},{"frameBounds",frameBounds},{"bothPaneOpeningRays",paneOpeningRays},{"instrumentProbes",instruments},{"removedBakedWiperComponents",cabin.RemovedBakedWiperComponents},{"remainingBakedWiperComponents",remainingBakedWipers},{"animatedWipers",session.Car.Visual.Wipers.Length},{"windowRays",rayResults},{"capturedBeforeAssertions",true},{"scope","Actual native camper rendering. Interior clearance ends at the real front-glass intersection; all nearer opaque faces obstruct it. Exterior wiper/seal hits beyond the glass are retained as detailed evidence, rather than incorrectly requiring the exterior to be empty. Original pack data and physical frames are retained."}};
                File.WriteAllText(Path.Combine(output,"camper-interior.json"),report.ToString());
                check(quarterMeshes&&cabin.OpenedQuarterlightTriangles>0&&opaqueQuarterPanels==0,"Both missing quarterlights replace the original opaque side panels with actual openings");
                foreach(JObject quarter in quarters)
                    check((bool)quarter["clear"]&&(bool)quarter["structuralPostPresent"]&&(float)quarter["postSpanToWindshieldMetres"]>=.08f&&(float)quarter["postSpanToWindshieldMetres"]<=.105f,"Quarterlight is open and retains an8–10cm structural front post: "+quarter["name"]);
                check(cabin.TrimmedWindowRevealTriangles>0&&deepOriginalReveals==0,"Original buried pane wells and central core contain no remaining deep reveal surfaces");
                check(matchedFrames,"Both actual interior frame meshes have matched width/height and at most13mmdepth");
                foreach(JObject paneRay in paneOpeningRays)check((bool)paneRay["clear"],"Both split panes remain open from the driving eye: "+paneRay["pane"]+" "+paneRay["targetCarLocal"]);
                check(cabin.HollowedInteriorTriangles>0,"Old solid coachwork deck and cabin bottom are opened only inside the bounded passenger compartment");
                foreach(JObject instrument in instruments)
                    check((bool)instrument["unobstructed"]&&(bool)instrument["onScreen"],"Actual camper instrument/panel is visible without an opaque intervening surface: "+instrument["label"]);
                check(gaugeVisible&&steeringCornersVisible>0,"Actual camper driver camera includes the speedometer and steering rim in its view");
                check(cabin.RemovedBakedWiperComponents==4&&remainingBakedWipers==0&&session.Car.Visual.Wipers.Length==2,"Four identified baked wiper components are replaced by two animated wipers, without duplicate static blades");
                foreach(JObject ray in rayResults)
                {
                    string blockingMesh=ray["interiorObstruction"] is JObject blocker?(string)blocker["mesh"]:"";
                    check((bool)ray["interiorClear"],"Camper interior-to-glass ray is clear at yaw"+ray["yaw"]+" pitch"+ray["pitch"]+" ("+blockingMesh+")");
                }
                // Wider central cabin view is diagnostic only; the production
                // right-hand driving eye is retained in the primary screenshot.
                var overviewEye=new Vector3(0,1.70f,.30f);var overviewWorld=session.Car.transform.TransformPoint(overviewEye);
                float roofClearance=float.PositiveInfinity,floorClearance=float.PositiveInfinity;
                foreach(var face in faces)
                {
                    if(Intersection(overviewWorld,session.Car.transform.up,face.a,face.b,face.c,out float up,2))roofClearance=Mathf.Min(roofClearance,up);
                    if(Intersection(overviewWorld,-session.Car.transform.up,face.a,face.b,face.c,out float down,2))floorClearance=Mathf.Min(floorClearance,down);
                }
                report["overviewEye"]=Point(overviewEye);report["overviewRoofClearance"]=float.IsInfinity(roofClearance)?JValue.CreateNull():(JToken)new JValue(roofClearance);report["overviewFloorClearance"]=float.IsInfinity(floorClearance)?JValue.CreateNull():(JToken)new JValue(floorClearance);
                File.WriteAllText(Path.Combine(output,"camper-interior.json"),report.ToString());
                check(!float.IsInfinity(roofClearance)&&!float.IsInfinity(floorClearance)&&roofClearance>.07f&&floorClearance>.07f,"Overview camera remains inside the physical roof/floor envelope with clear headroom");
                session.View.DevelopmentCockpitEye=overviewEye;
                for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();
                image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(output,"camper-cabin-overview.png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}
                report["passed"]=true;report["overviewCaptured"]=true;report["overviewEye"]=Point(new Vector3(0,1.70f,.30f));
                File.WriteAllText(Path.Combine(output,"camper-interior.json"),report.ToString());
            }
            finally
            {
                session.View.DevelopmentCockpitEye=eyeOverride;session.View.Mode=mode;
                session.SetMenu(true);session.SelectOwnedVehicle(originalId);
                session.Car.Body.isKinematic=kinematic;session.Car.enabled=controllerEnabled;session.Car.StartLocked=locked;session.SetMenu(menu);
                session.Conditions.NetworkConditions(hour,rain,wet,clouds,wind,fog);session.Conditions.NetworkOverride=network;
            }
        }
    }
}
