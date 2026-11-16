using UnityEngine;

namespace EarthUnscripted
{
    public sealed class CarVisual : MonoBehaviour
    {
        public int Model {get;private set;}
        public Transform[] Wheels { get; private set; }
        public Transform SteeringWheel { get; private set; }
        public Renderer Body { get; private set; }
        public Light[] Headlights { get; private set; }
        ImportedCarAppearance imported;SingleSeatCandidate singleSeat;VehiclePackAppearance packAppearance;
        Material customPaint;
        public bool ImportedAppearance=>imported!=null;
        public string DashboardReadout=>singleSeat!=null?singleSeat.DashboardReadout:imported?.DashboardReadout??"";
        public Vector3 CockpitEye=>packAppearance?packAppearance.CockpitEye:singleSeat!=null?transform.InverseTransformPoint(singleSeat.transform.TransformPoint(singleSeat.CockpitEye)):imported!=null?imported.CockpitEye:VehicleAppearanceMetrics.ProceduralPoint(Model,new Vector3(.43f,1.36f,-.34f));
        float? frontCameraExtent;
        /// <summary>Frontmost mesh extent in vehicle-local metres, cached once per
        /// built appearance. Includes badges and bumpers but excludes particle bounds.
        /// Nose-mounted cameras must sit outside this surface for every car pack.</summary>
        public float FrontCameraExtent
        {
            get
            {
                if (frontCameraExtent.HasValue) return frontCameraExtent.Value;
                float front = Core.VehicleDimensions.ForModel(Model).BodyLength * .5f;
                foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh) continue;
                    var bounds = filter.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1,
                                (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        front = Mathf.Max(front, transform.InverseTransformPoint(filter.transform.TransformPoint(point)).z);
                    }
                }
                frontCameraExtent = front;
                return front;
            }
        }
        Renderer[] brakeLights;
        public bool PresentationHidden {get;private set;}
        /// <summary>
        /// Hide/show every renderer owned by this physical car, including currently
        /// inactive tyre variants. Garage compound changes may activate those objects
        /// later: excluding inactive children lets wet grooves leak outside the preview.
        /// The showroom has a separate CarVisual and is not a child of this car.
        /// Main-thread presentation policy only; no physics, saved condition or material
        /// ownership changes. Existing policy sets renderer enabled state on restore.
        /// </summary>
        public void SetPresentationHidden(bool hidden)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            PresentationHidden = hidden;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !hidden;
            if (Headlights != null)
                SetAutomaticLights(nightLights);
        }
        public Transform[] Wipers {get;private set;}
        public float WiperRain;float wiperPhase,wheelAngle,wheelTarget;
        float replicaSpin;
        public void AnimateReplica(float steer,float speed){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
replicaSpin+=speed*Time.unscaledDeltaTime/Core.VehicleDimensions.ForModel(Model).Radius*Mathf.Rad2Deg;for(int i=0;i<Wheels.Length;i++){Wheels[i].localRotation=Quaternion.Euler(0,i%2==1?steer:0,0)*Quaternion.Euler(replicaSpin,0,0);FollowWheel(i,i%2==1?steer:0);}}
        public void FollowWheel(int index,float steer){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
if(singleSeat!=null)singleSeat.FollowWheel(index,Wheels[index],steer);if(imported!=null)imported.FollowWheel(index,steer,Wheels[index].localPosition);}
        public void SetSteering(float input){wheelTarget=-Mathf.Clamp(input,-1,1)*450;}
        Material brakeOn, brakeOff,tailOn;bool nightLights,braking;
        Vector3[] originalVertices;
        Mesh bodyMesh;
        readonly System.Collections.Generic.Dictionary<Transform,Vector3> attachments=new System.Collections.Generic.Dictionary<Transform,Vector3>();
        public void Build(Color colour, bool cockpit = false,int model=0,int wheelStyle=0,int bodyKit=0,int finish=0)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);

            frontCameraExtent = null;
            Model=Mathf.Clamp(model,0,Core.GarageRules.Catalogue.Length-1);model=Model;var metric=Core.VehicleDimensions.ForModel(model);
            var carPack = VehiclePackLibrary.ForModel(model);
            if (carPack != null)
            {
                packAppearance = VehiclePackAppearance.Create(transform, carPack, colour, finish);
                Wheels = packAppearance.Wheels; SteeringWheel = packAppearance.Steering;
                Body = packAppearance.Body; Headlights = packAppearance.Headlights; Wipers = new Transform[0];
                brakeLights = null; bodyMesh = null; originalVertices = null;
                if (PresentationHidden) SetPresentationHidden(true);
                return;
            }
            if(model==3)
            {
                singleSeat=SingleSeatCandidate.Create(transform);singleSeat.transform.localPosition=Vector3.down*metric.RestRootHeight(Core.GarageRules.Catalogue[model].Mass);singleSeat.Paint(colour,finish);singleSeat.Style(wheelStyle,bodyKit);Body=singleSeat.BodyRenderer;Wheels=new Transform[4];for(int i=0;i<4;i++){Wheels[i]=new GameObject("Physical wheel pose"+i).transform;Wheels[i].SetParent(transform,false);var a=metric.Anchor(i);Wheels[i].localPosition=new Vector3(a.x,metric.RestWheelLocalY(Core.GarageRules.Catalogue[model].Mass),a.z);FollowWheel(i,0);}Headlights=new Light[0];Wipers=new Transform[0];brakeLights=null;bodyMesh=null;originalVertices=null;if(PresentationHidden)SetPresentationHidden(true);return;
            }
            if(model==0&&cockpit&&ImportedCarAppearance.TryBuild(transform,colour,wheelStyle,bodyKit,finish,out imported))
            {Wheels=imported.Wheels;Wipers=imported.Wipers;SteeringWheel=imported.SteeringWheel;Body=imported.Body;Headlights=imported.Headlights;SetAutomaticLights(nightLights);if(PresentationHidden)SetPresentationHidden(true);return;}
            var spec=Core.GarageRules.Catalogue[Mathf.Clamp(model,0,2)];
            var paint = Geometry.Material("Paint " + colour+" F"+finish, colour, finish==1?.05f:.62f, finish==1?.18f:finish==2?.9f:.62f);
            var dark = Geometry.Material("Carbon", new Color(.025f, .033f, .043f), .2f, .4f);
            var rubber = Geometry.Material("Tyre", new Color(.025f, .026f, .028f), 0, .12f);
            var alloy = Geometry.Material("Alloy", new Color(.55f, .61f, .67f), .85f, .7f);
            var glass = Geometry.Material("Smoked glass", new Color(.055f, .12f, .16f), .6f, .94f);
            // Longitudinal body rings make a continuous tapered sports-car shell.
            float[] z = { -2.18f, -1.85f, -1.2f, -.35f, .55f, 1.4f, 2.15f };
            float[] widths = { .87f, 1.01f, 1.04f, 1.02f, 1.03f, 1f, .86f };
            float[] tops = { .78f, 1.01f, 1.05f, 1.02f, 1.03f, .97f, .74f };
            const int rings=49;var vertices = new Vector3[rings * 8]; var uv = new Vector2[vertices.Length];
            for (int r = 0; r < rings; r++)
            {
                float u=r/(float)(rings-1)*(z.Length-1);int k=Mathf.Min((int)u,z.Length-2);float t=u-k;
                float Smooth(float[] values){float a=values[Mathf.Max(0,k-1)],b=values[k],c=values[k+1],d=values[Mathf.Min(values.Length-1,k+2)];return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);}
                float w=Smooth(widths),h=Smooth(tops),longitudinal=Mathf.Lerp(z[k],z[k+1],t);
                Vector2[] ring = { new Vector2(-w*.84f,.43f), new Vector2(-w,.58f), new Vector2(-w,.82f*h), new Vector2(-w*.78f,h), new Vector2(w*.78f,h),new Vector2(w,.82f*h),new Vector2(w,.58f),new Vector2(w*.84f,.43f) };
                for (int j = 0; j < 8; j++) { float y=ring[j].y;float targetZ=VehicleAppearanceMetrics.ProceduralPoint(model,new Vector3(0,0,longitudinal)).z;float wheelDistance=model==0?Mathf.Min(Mathf.Abs(targetZ-metric.Wheelbase*.5f),Mathf.Abs(targetZ+metric.Wheelbase*.5f)):Mathf.Min(Mathf.Abs(longitudinal-1.36f),Mathf.Abs(longitudinal+1.36f));float arch=model==0?metric.Radius+.10f:.465f;if(wheelDistance<arch&&j!=3&&j!=4){float cut=model==0?(metric.RestWheelLocalY(spec.Mass)+Mathf.Sqrt(arch*arch-wheelDistance*wheelDistance))/(metric.RoofLocalY/1.585f):.39f+Mathf.Sqrt(arch*arch-wheelDistance*wheelDistance);y=Mathf.Max(y,cut);}
                    vertices[r*8+j] = new Vector3(ring[j].x, y, longitudinal); uv[r*8+j] = new Vector2(j/7f,r/(float)(rings-1)); }
            }
            var triangles = new System.Collections.Generic.List<int>();
            for (int r = 0; r < rings - 1; r++) for (int j = 0; j < 8; j++)
            { int a=r*8+j,b=r*8+(j+1)%8,c=a+8,d=b+8; triangles.AddRange(new[]{a,c,b,b,c,d}); }
            for (int j=1;j<7;j++) { triangles.AddRange(new[]{0,j,j+1,(rings-1)*8,(rings-1)*8+j+1,(rings-1)*8+j}); }
            var shell = Geometry.Mesh("Sculpted body", transform, vertices, triangles.ToArray(), uv, paint, false);
            Body = shell.GetComponent<Renderer>(); bodyMesh = shell.GetComponent<MeshFilter>().sharedMesh; PanelDeformation.Subdivide(bodyMesh,2);originalVertices = bodyMesh.vertices;
            // Sloped cabin; its front is open for an unobstructed cockpit view.
            var roofVertices=new Vector3[25];var roofTriangles=new System.Collections.Generic.List<int>();
            for(int r=0;r<5;r++)for(int j=0;j<5;j++){float x=(j/4f-.5f)*1.48f,zRoof=Mathf.Lerp(-1.1f,.1f,r/4f);roofVertices[r*5+j]=new Vector3(x,1.51f+Mathf.Sin(r/4f*Mathf.PI)*.035f+(1-Mathf.Abs(x)/.74f)*.04f,zRoof);if(r<4&&j<4){int a=r*5+j;roofTriangles.AddRange(new[]{a,a+5,a+1,a+1,a+5,a+6});}}
            Geometry.Mesh("Curved roof",transform,roofVertices,roofTriangles.ToArray(),new Vector2[25],paint,false);
            Geometry.Mesh("Rear window",transform,new[]{new Vector3(-.94f,1.04f,-1.9f),new Vector3(.94f,1.04f,-1.9f),new Vector3(.74f,1.51f,-1.1f),new Vector3(-.74f,1.51f,-1.1f)},new[]{0,2,1,0,3,2},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},glass,false);
            Geometry.Mesh("Windscreen",transform,new[]{new Vector3(-.94f,1.03f,.92f),new Vector3(.94f,1.03f,.92f),new Vector3(.74f,1.51f,.1f),new Vector3(-.74f,1.51f,.1f)},new[]{0,1,2,0,2,3},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},glass,false);
            for(int s=-1;s<=1;s+=2)
            {
                var top=new Vector3(s*.75f,1.52f,.1f);var bottom=new Vector3(s*.94f,1.04f,.92f);
                var pillar=Geometry.Box("A pillar",transform,(top+bottom)*.5f,new Vector3(.035f,(top-bottom).magnitude,.035f),paint);pillar.transform.localRotation=Quaternion.FromToRotation(Vector3.up,top-bottom);
                var sideVertices=new[]{new Vector3(s*.96f,1.05f,.7f),new Vector3(s*.96f,1.05f,-1.85f),new Vector3(s*.74f,1.49f,-1.1f),new Vector3(s*.74f,1.49f,.1f)};
                Geometry.Mesh("Side glazing",transform,sideVertices,s>0?new[]{0,1,2,0,2,3}:new[]{0,2,1,0,3,2},new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},glass,false);
                Geometry.Box("Sill",transform,new Vector3(s*1.015f,.49f,0),new Vector3(.08f,.13f,2.9f),dark);
                Geometry.Box("Mirror",transform,new Vector3(s*1.14f,1.2f,.38f),new Vector3(.28f,.12f,.22f),paint);
                Geometry.Box("Door seam",transform,new Vector3(s*1.024f,.83f,-.1f),new Vector3(.012f,.012f,1.4f),dark);
                Geometry.Box("Handle",transform,new Vector3(s*1.04f,.97f,-.53f),new Vector3(.025f,.035f,.18f),alloy);
            }
            Geometry.Box("Grille",transform,new Vector3(0,.63f,2.139f),new Vector3(1.25f,.19f,.045f),dark);
            Geometry.Box("Front splitter",transform,new Vector3(0,.43f,2.1f),new Vector3(1.87f,.055f,.24f),dark);
            Geometry.Box("Rear diffuser",transform,new Vector3(0,.48f,-2.1f),new Vector3(1.68f,.15f,.15f),dark);
            Wheels=new Transform[4]; int index=0;
            for(int s=-1;s<=1;s+=2) for(int axle=-1;axle<=1;axle+=2)
            {
                var pivot=new GameObject("Wheel").transform; pivot.SetParent(transform,false); pivot.localPosition=new Vector3(s*1.01f,.39f,axle*1.36f); Wheels[index++]=pivot;
                CarDetails.Tyre(pivot,rubber,metric.Radius,metric.TyreWidth);
                var rim=Geometry.Primitive(PrimitiveType.Cylinder,"Rim",pivot,new Vector3(s*(metric.TyreWidth*.5f-.01f),0,0),new Vector3(.48f,.012f,.48f),dark);rim.transform.localRotation=Quaternion.Euler(0,0,90);
                int spokes=wheelStyle==1?5:wheelStyle==2?10:7;
                for(int j=0;j<spokes;j++){float angle=j*Mathf.PI*2/spokes;var spoke=Geometry.Box("Spoke",pivot,new Vector3(s*(metric.TyreWidth*.5f-.009f),-Mathf.Sin(angle)*.11f,Mathf.Cos(angle)*.11f),new Vector3(.025f,.045f,.22f),alloy);spoke.transform.localRotation=Quaternion.Euler(angle*Mathf.Rad2Deg,0,0);}
            }
            Headlights=new Light[2]; brakeLights=new Renderer[2];
            var white=Geometry.Material("LED white",new Color(.8f,.93f,1),.1f,.6f); white.EnableKeyword("_EMISSION"); white.SetColor("_EmissionColor",new Color(1.5f,2,2.4f));
            tailOn=Geometry.Material("Night tail red",new Color(.4f,.006f,.012f));tailOn.EnableKeyword("_EMISSION");tailOn.SetColor("_EmissionColor",Color.red*.9f);
            brakeOff=Geometry.Material("Tail red",new Color(.28f,.005f,.01f)); brakeOn=Geometry.Material("Brake red",Color.red);brakeOn.EnableKeyword("_EMISSION");brakeOn.SetColor("_EmissionColor",Color.red*4);
            for(int i=0;i<2;i++)
            {
                float x=i==0?-.65f:.65f;
                Geometry.Box("LED headlamp",transform,new Vector3(x,.8f,2.11f),new Vector3(.48f,.055f,.07f),white);
                brakeLights[i]=Geometry.Box("Rear lamp",transform,new Vector3(x,.81f,-2.16f),new Vector3(.5f,.065f,.05f),brakeOff).GetComponent<Renderer>();
                if(cockpit){var lamp=new GameObject("Headlight");lamp.transform.SetParent(transform,false);lamp.transform.localPosition=new Vector3(x,.8f,2.2f);var l=lamp.AddComponent<Light>();HeadlightTuning.Configure(l);lamp.transform.localRotation=Quaternion.Euler(4,0,0);Headlights[i]=l;}
            }
            if(cockpit)
            {
                Wipers=new Transform[2];
                for(int i=0;i<2;i++)
                {
                    var mount=new GameObject("Rain-sensitive wiper mount").transform;mount.SetParent(transform,false);mount.localPosition=new Vector3(i==0?-.4f:.38f,1.075f,.775f);mount.localRotation=Quaternion.Euler(-52,0,0);
                    var pivot=new GameObject("Wiper sweep").transform;pivot.SetParent(mount,false);Wipers[i]=pivot;
                    Geometry.Box("Wiper arm",pivot,new Vector3(0,.19f,.018f),new Vector3(.014f,.38f,.014f),dark);
                    Geometry.Box("Wiper blade",pivot,new Vector3(0,.39f,.022f),new Vector3(.024f,.38f,.022f),rubber);
                }
                Geometry.Box("Dashboard",transform,new Vector3(0,1.05f,.51f),new Vector3(1.66f,.19f,.44f),dark);
                for(int s=-1;s<=1;s+=2){Geometry.Box("Seat",transform,new Vector3(s*.46f,.72f,-.5f),new Vector3(.6f,.52f,.25f),dark);}
                SteeringWheel=new GameObject("Steering wheel").transform;SteeringWheel.SetParent(transform,false);SteeringWheel.localPosition=new Vector3(.43f,1.09f,.31f);
                // UK right-hand-drive seating position.
                for(int j=0;j<24;j++){float a=j*Mathf.PI*2/24;var grip=Geometry.Box("Wheel grip",SteeringWheel,new Vector3(Mathf.Sin(a)*.19f,Mathf.Cos(a)*.19f,0),new Vector3(.058f,.035f,.035f),dark);grip.transform.localRotation=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);}
                Geometry.Box("Wheel hub",SteeringWheel,Vector3.zero,new Vector3(.32f,.045f,.035f),alloy);
            }
            if(model==2||bodyKit==1)
            {
                Geometry.Box("Spoiler left mount",transform,new Vector3(-.62f,1.08f,-1.86f),new Vector3(.07f,.3f,.09f),dark);
                Geometry.Box("Spoiler right mount",transform,new Vector3(.62f,1.08f,-1.86f),new Vector3(.07f,.3f,.09f),dark);
                Geometry.Box("Rear Spoiler",transform,new Vector3(0,1.24f,-1.86f),new Vector3(1.98f,.055f,.3f),paint);
            }
            if(bodyKit==2)for(int side=-1;side<=1;side+=2)Geometry.Box("Sport sill",transform,new Vector3(side*.99f,.46f,0),new Vector3(.14f,.1f,3.2f),paint);
            VehicleAppearanceMetrics.FitProcedural(transform,model,Body,Wheels,SteeringWheel,Wipers);
            originalVertices=bodyMesh.vertices;if(PresentationHidden)SetPresentationHidden(true);
        }
        public void ApplyPaintJob(Core.OwnedCar owned)
        {
            if(packAppearance){packAppearance.SetPaintJob(owned.paintJob);return;}
            if(!Body||owned.paintJob==0&&!customPaint)return;
            var previous=Body.sharedMaterial;
            var replacement=VehiclePaintJobs.Create(previous,CarColours.Paint[owned.paint],owned.paintJob,owned.finish);
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;bool changed=false;
                for(int index=0;index<materials.Length;index++)if(materials[index]==previous){materials[index]=replacement;changed=true;}
                if(changed)renderer.sharedMaterials=materials;
            }
            if(customPaint)Destroy(customPaint);customPaint=replacement;
        }
        public void SetSurfaceDirt(float dirt){if(packAppearance)packAppearance.SetDirt(dirt);if(customPaint)customPaint.SetFloat("_Dirt",dirt);}
        public int DamageRevision {get;private set;}
        public readonly System.Collections.Generic.List<Core.SavedDent> RecordedDents=new System.Collections.Generic.List<Core.SavedDent>();
        public void Rebuild(Core.OwnedCar owned,bool cockpit=true)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);

            VehiclePopulation.VehiclePopulationHooks.ShapeWillChange(this);
            imported?.ReleaseMaterials();imported=null;singleSeat=null;packAppearance=null;Geometry.ReleaseMeshes(gameObject);
            for(int i=transform.childCount-1;i>=0;i--){var child=transform.GetChild(i).gameObject;foreach(var collider in child.GetComponentsInChildren<Collider>(true))collider.enabled=false;child.transform.SetParent(null);if(Application.isPlaying)Destroy(child);else DestroyImmediate(child);}
            if(customPaint){Destroy(customPaint);customPaint=null;}
            attachments.Clear();RecordedDents.Clear();SteeringWheel=null;Wipers=null;wheelAngle=wheelTarget=wiperPhase=0;
            Build(CarColours.Paint[owned.paint],cockpit,owned.model,owned.wheels,owned.bodyKit,owned.finish);ApplyPaintJob(owned);
        }
        public void RestoreDents(System.Collections.Generic.List<Core.SavedDent> dents)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);

            Repair();if(dents==null)return;
            foreach(var d in dents){Dent(transform.TransformPoint(VehicleGeometry.FromLegacyDentPoint(Model,new Vector3(d.x,d.y,d.z))),d.severity,transform.TransformDirection(new Vector3(d.nx,d.ny,d.nz)),false);RecordedDents.Add(d);}
        }
        void RememberPanels()
        {
            foreach(Transform p in transform.GetComponentsInChildren<Transform>(true))
                if(p.name=="Grille"||p.name=="Front splitter"||p.name=="Rear diffuser"||p.name=="LED headlamp"||p.name=="Rear lamp"||p.name=="Headlight"||p.name=="Mirror"||p.name=="Handle"||p.name=="Sill"||p.name=="Sport sill"||p.name=="Door seam"||p.name.Contains("Spoiler"))attachments[p]=transform.InverseTransformPoint(p.position);
        }
        void Update()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);

            if(singleSeat!=null){singleSeat.Steering(wheelTarget);var controller=GetComponent<VehicleController>();if(controller)singleSeat.Instruments(controller.AbsoluteSpeed);}
            if(imported!=null){var car=GetComponent<VehicleController>();if(car)imported.Instruments(car.AbsoluteSpeed,car.Reverse);}
            if(SteeringWheel){wheelAngle=Mathf.MoveTowards(wheelAngle,wheelTarget,Time.deltaTime*650);if(imported!=null)imported.Steering(wheelAngle);else SteeringWheel.localRotation=Quaternion.Euler(0,0,wheelAngle);}
            if(Wipers==null)return;
            if(WiperRain>.035f)wiperPhase=Mathf.Repeat(wiperPhase+Time.deltaTime*Mathf.Lerp(.5f,2,WiperRain),1);
            else wiperPhase=Mathf.MoveTowards(wiperPhase,wiperPhase>.5f?1:0,Time.deltaTime*1.5f);
            float sweep=(1-Mathf.Cos(wiperPhase*Mathf.PI*2))*.5f;
            foreach(var w in Wipers)w.localRotation=Quaternion.Euler(0,0,Mathf.Lerp(-76,30,sweep));
        }
        public void SetAutomaticLights(bool on){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
nightLights=on;foreach(var l in Headlights)if(l)l.enabled=on&&!PresentationHidden;Brake(braking);}
        public void Brake(bool on){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
braking=on;if(imported!=null){imported.Lights(nightLights,on);return;}if(brakeLights!=null) foreach(var r in brakeLights)r.sharedMaterial=on?brakeOn:nightLights?tailOn:brakeOff;}
        public void SetRaceWheelsVisible(bool visible){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
if(singleSeat!=null)singleSeat.SetWheelsVisible(visible);}
        public void SetRaceCompound(Core.RaceCompound compound){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(transform);
if(singleSeat!=null)singleSeat.SetCompound(compound);}
        public void Dent(Vector3 point,float impulse,Vector3? normal=null,bool remember=true)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);

            using var telemetry=WorkTelemetry.Measure("panel-deformation");
            if(attachments.Count==0)RememberPanels();
            var local=transform.InverseTransformPoint(point);var inward=normal.HasValue?transform.InverseTransformDirection(normal.Value).normalized:(new Vector3(0,.8f,0)-local).normalized;
            if(remember){DamageRevision++;if(RecordedDents.Count>=32)RecordedDents.RemoveAt(0);var stored=VehicleGeometry.ToLegacyDentPoint(Model,local);RecordedDents.Add(new Core.SavedDent{x=stored.x,y=stored.y,z=stored.z,nx=inward.x,ny=inward.y,nz=inward.z,severity=Mathf.Clamp(impulse,0,80)});}
            if(packAppearance){packAppearance.Dent(point,transform.TransformDirection(inward),impulse);return;}if(singleSeat!=null){singleSeat.Dent(point,transform.TransformDirection(inward),impulse);return;}if(imported!=null){imported.Dent(local,inward,impulse);return;}
            using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(bodyMesh);
            var v=bodyMesh.vertices;
            for(int i=0;i<v.Length;i++)v[i]=PanelDeformation.Displace(v[i],originalVertices[i],local,inward,impulse);
            foreach(var panel in attachments)if(panel.Key)panel.Key.position=transform.TransformPoint(PanelDeformation.Displace(transform.InverseTransformPoint(panel.Key.position),panel.Value,local,inward,impulse));
            bodyMesh.vertices=v;bodyMesh.RecalculateNormals();bodyMesh.RecalculateBounds();
        }
        void OnDestroy()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);

            if(customPaint)Destroy(customPaint);imported?.ReleaseMaterials();Geometry.ReleaseMeshes(gameObject);
        }
        public float DeformationExtent
        {
            get{if(packAppearance)return packAppearance.DeformationExtent;if(singleSeat!=null)return singleSeat.DeformationExtent;if(imported!=null)return imported.DeformationExtent;float result=0;var v=bodyMesh.vertices;for(int i=0;i<v.Length;i++)result=Mathf.Max(result,Vector3.Distance(v[i],originalVertices[i]));return result;}
        }
        public void Repair(){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(transform);
DamageRevision++;RecordedDents.Clear();if(packAppearance){packAppearance.Repair();return;}if(singleSeat!=null){singleSeat.Repair();return;}if(imported!=null){imported.Repair();return;}using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(bodyMesh);
            bodyMesh.vertices=(Vector3[])originalVertices.Clone();foreach(var p in attachments)if(p.Key)p.Key.position=transform.TransformPoint(p.Value);bodyMesh.RecalculateNormals();bodyMesh.RecalculateBounds();}
    }
}
