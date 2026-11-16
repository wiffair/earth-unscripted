using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    // Appearance adapter only. VehicleController continues to own all physics,
    // suspension anchors, tyre forces, controller feedback and saved tuning.
    public sealed class ImportedCarAppearance
    {
        public Transform[] Wheels,Wipers;
        readonly Transform[] calipers=new Transform[4];
        public Transform SteeringWheel;
        public Renderer Body;
        public Light[] Headlights;
        // Inside the source roof/glass with clearance for the .06m camera near plane.
        public Vector3 CockpitEye=new Vector3(0,1.02f,.38f);
        CarDashboard dashboard;public string DashboardReadout=>dashboard?.SpeedText??"";
        public void Instruments(float speed,bool reversing){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(owner);
dashboard?.Set(speed,reversing);}
        public float DeformationExtent {get;private set;}
        readonly Transform owner;
        readonly List<Material> materials=new List<Material>();
        readonly List<Material> rearLamps=new List<Material>(),frontLamps=new List<Material>();
        readonly List<Panel> panels=new List<Panel>();
        Quaternion steeringRest;
        sealed class Panel
        {
            public Mesh mesh;public Transform transform;public Vector3[] original,rootOriginal,current,normals;public Vector4[] tangents,tangentScratch;
            public Matrix4x4 intoRoot,fromRoot;
        }
        ImportedCarAppearance(Transform owner){this.owner=owner;}
        static Transform Named(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        public static bool TryBuild(Transform parent,Color colour,int wheelStyle,int bodyKit,int finish,out ImportedCarAppearance result)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(parent);

            result=null;var asset=Resources.Load<GameObject>("Vehicles/CarConcept");if(!asset)return false;
            foreach(string part in new[]{"WheelRearL","WheelFrontL","WheelRearR","WheelFrontR","InteriorSteeringCylinder"})
                if(!Named(asset.transform,part)){Debug.LogWarning("Imported car is incomplete; using procedural appearance");return false;}
            var instance=UnityEngine.Object.Instantiate(asset,parent,false);instance.name="Licensed concept coupe";
            var adapter=new ImportedCarAppearance(parent);adapter.Configure(instance.transform,colour,wheelStyle,bodyKit,finish);result=adapter;return true;
        }
        void Configure(Transform root,Color colour,int wheelStyle,int bodyKit,int finish)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(owner);

            // Neutralise the source's 30-degree steering pose before making the
            // four physics-driven pivots. Keep the model's wheel-spin rest poses.
            string[] names={"WheelRearL","WheelFrontL","WheelRearR","WheelFrontR"};
            var sourceWheels=names.Select(name=>Named(root,name)).ToArray();
            foreach(var wheel in sourceWheels)
            {
                var axle=owner.InverseTransformDirection(wheel.right);float yaw=Mathf.Atan2(-axle.z,axle.x)*Mathf.Rad2Deg;
                wheel.RotateAround(wheel.position,owner.up,-yaw);
            }
            var metric=Core.VehicleDimensions.ForModel(0);
            Wheels=new Transform[4];
            for(int i=0;i<4;i++)
            {
                var pivot=new GameObject("Wheel").transform;pivot.SetParent(owner,false);pivot.localPosition=new Vector3(i<2?-metric.Track*.5f:metric.Track*.5f,metric.RestWheelLocalY(Core.GarageRules.Catalogue[0].Mass),i%2==0?-metric.Wheelbase*.5f:metric.Wheelbase*.5f);Wheels[i]=pivot;
                var wheel=sourceWheels[i];wheel.SetParent(pivot,true);wheel.localPosition=Vector3.zero;
                var pad=wheel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.EndsWith("BrakePad",StringComparison.Ordinal));
                if(pad){var fixedPart=new GameObject("Fixed brake caliper "+i).transform;fixedPart.SetParent(owner,false);fixedPart.localPosition=pivot.localPosition;pad.SetParent(fixedPart,true);calipers[i]=fixedPart;}
                VehicleAppearanceMetrics.FitImportedWheel(owner,wheel,calipers[i],0);
                if(wheelStyle!=0)
                {
                    foreach(var part in wheel.GetComponentsInChildren<Transform>(true))if(part.name.EndsWith("Rim",StringComparison.Ordinal))part.gameObject.SetActive(false);
                    var alloy=Geometry.Material("Concept alternate alloy",new Color(.6f,.64f,.68f),.85f,.7f);int spokes=wheelStyle==1?5:10;
                    for(int j=0;j<spokes;j++){float a=j*Mathf.PI*2/spokes;var spoke=Geometry.Box("Alternate spoke",pivot,new Vector3((i<2?-1:1)*(metric.TyreWidth*.5f-.009f),-Mathf.Sin(a)*.12f,Mathf.Cos(a)*.12f),new Vector3(.03f,.05f,.25f),alloy);spoke.transform.localRotation=Quaternion.Euler(a*Mathf.Rad2Deg,0,0);}
                }
            }
            // Circular control assembly is rigid and detached before bodyXfit.
            SteeringWheel=Named(root,"InteriorSteeringCylinder");var steeringPoint=owner.InverseTransformPoint(SteeringWheel.position);SteeringWheel.SetParent(owner,true);SteeringWheel.localPosition=new Vector3(steeringPoint.x*1.03f*metric.LegacyBodyXScale,steeringPoint.y,steeringPoint.z*.9715f-.0833f);
            root.localScale=new Vector3(1.03f*metric.LegacyBodyXScale,1,.9715f);root.localPosition=new Vector3(0,0,-.0833f);
            foreach(string name in new[]{"BodyWindshieldWipers","BodyWindshieldWipersBase"}){var part=Named(root,name);if(part)part.gameObject.SetActive(false);}
            var copies=new Dictionary<Material,Material>();
            // Wheel nodes have moved out of root; every imported renderer remains
            // below owner, and is processed once before optional trim is added.
            foreach(var renderer in owner.GetComponentsInChildren<MeshRenderer>(true))
            {
                var assigned=renderer.sharedMaterials;
                for(int i=0;i<assigned.Length;i++)
                {
                    var source=assigned[i];if(!source)continue;
                    if(!copies.TryGetValue(source,out var material))
                    {
                        material=GameGraphics.Create(source);copies[source]=material;materials.Add(material);
                        if(source.name.Contains("Paint ")){material.color=colour;material.SetFloat("_Metallic",finish==1?.05f:.7f);material.SetFloat("_Smoothness",finish==1?.18f:finish==2?.9f:.68f);if(material.HasProperty("_ClearCoatMask"))material.SetFloat("_ClearCoatMask",finish==1?0:finish==2?1:source.GetFloat("_ClearCoatMask"));}
                        if(source.name.Contains("Brakelight"))rearLamps.Add(material);
                        if(source.name.Contains("Headlight"))frontLamps.Add(material);
                    }
                    assigned[i]=material;
                }
                renderer.sharedMaterials=assigned;
            }
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var bodyNode=filter.transform;if(bodyNode.name.StartsWith("Primitive ",StringComparison.Ordinal)&&bodyNode.parent)bodyNode=bodyNode.parent;
                if(!bodyNode.name.StartsWith("Body",StringComparison.Ordinal)||bodyNode.name.Contains("Wiper"))continue;
                var source=filter.sharedMesh;if(!source)continue;var mesh=UnityEngine.Object.Instantiate(source);mesh.name=source.name+" per-car damage";mesh.MarkDynamic();filter.sharedMesh=mesh;
                var ownership=filter.gameObject.AddComponent<GeneratedMeshOwner>();ownership.Mesh=mesh;
                var matrix=owner.worldToLocalMatrix*filter.transform.localToWorldMatrix;var original=mesh.vertices;
                panels.Add(new Panel{mesh=mesh,transform=filter.transform,original=original,normals=mesh.normals,tangents=mesh.tangents,tangentScratch=mesh.tangents,rootOriginal=original.Select(v=>matrix.MultiplyPoint3x4(v)).ToArray(),current=(Vector3[])original.Clone(),intoRoot=matrix,fromRoot=matrix.inverse});
                if(bodyNode.name=="BodyPanelsColor2"||Body==null)Body=filter.GetComponent<Renderer>();
            }
            steeringRest=SteeringWheel.localRotation;
            Headlights=new Light[2];for(int i=0;i<2;i++)
            {
                var lamp=new GameObject("Headlight").transform;lamp.SetParent(owner,false);lamp.localPosition=new Vector3((i==0?-.68f:.68f)*metric.LegacyBodyXScale,.61f,2.24f);lamp.localRotation=Quaternion.Euler(4,0,0);
                var light=lamp.gameObject.AddComponent<Light>();HeadlightTuning.Configure(light);Headlights[i]=light;
            }
            var dark=Geometry.Material("Concept wiper rubber",new Color(.015f,.02f,.025f),0,.12f);Wipers=new Transform[2];
            for(int i=0;i<2;i++)
            {
                var mount=new GameObject("Rain-sensitive wiper mount").transform;mount.SetParent(owner,false);mount.localPosition=new Vector3(i==0?-.4f*metric.LegacyBodyXScale:.055f,.85f,1.32f);mount.localRotation=Quaternion.Euler(-78,0,0);
                var pivot=new GameObject("Wiper sweep").transform;pivot.SetParent(mount,false);Wipers[i]=pivot;
                Geometry.Box("Wiper arm",pivot,new Vector3(0,.24f,.012f),new Vector3(.012f,.48f,.012f),dark);Geometry.Box("Wiper blade",pivot,new Vector3(0,.49f,.017f),new Vector3(.022f,.37f,.018f),dark);
            }
            if(bodyKit==1)
            {
                for(int side=-1;side<=1;side+=2)Geometry.Box("Concept spoiler mount",owner,new Vector3(side*.65f*metric.LegacyBodyXScale,.94f,-1.7f),new Vector3(.06f,.23f,.08f),dark);
                Geometry.Box("Concept rear spoiler",owner,new Vector3(0,1.07f,-1.7f),new Vector3(1.95f*metric.LegacyBodyXScale,.055f,.28f),Body.sharedMaterial);
            }
            else if(bodyKit==2)for(int side=-1;side<=1;side+=2)Geometry.Box("Concept sport sill",owner,new Vector3(side*1.11f*metric.LegacyBodyXScale,.34f,.05f),new Vector3(.1f*metric.LegacyBodyXScale,.08f,2.9f),dark);
            dashboard=new CarDashboard(owner);Lights(false,false);
        }
        public void FollowWheel(int index,float steer,Vector3 position)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(owner);
if(index>=0&&index<calipers.Length&&calipers[index]){calipers[index].localPosition=position;calipers[index].localRotation=Quaternion.Euler(0,steer,0);}}
        public void Steering(float angle){
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(owner);
if(SteeringWheel)SteeringWheel.localRotation=steeringRest*Quaternion.AngleAxis(-angle,Vector3.up);}
        public void Lights(bool night,bool braking)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(owner);

            foreach(var material in rearLamps){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",Color.red*(braking?4:night?.8f:0));}
            foreach(var material in frontLamps){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(.8f,.9f,1)*(night?2:0));}
        }
        public void Dent(Vector3 point,Vector3 inward,float severity)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(owner);

            foreach(var panel in panels)
            {
                using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(panel.mesh);
                for(int i=0;i<panel.current.Length;i++)
                {
                    var current=panel.intoRoot.MultiplyPoint3x4(panel.current[i]);var changed=PanelDeformation.Displace(current,panel.rootOriginal[i],point,inward,severity);
                    panel.current[i]=panel.fromRoot.MultiplyPoint3x4(changed);DeformationExtent=Mathf.Max(DeformationExtent,Vector3.Distance(changed,panel.rootOriginal[i]));
                }
                panel.mesh.vertices=panel.current;panel.mesh.RecalculateNormals();
                var normals=panel.mesh.normals;if(panel.tangents.Length==normals.Length)
                {
                    for(int i=0;i<normals.Length;i++){var n=normals[i];var original=panel.tangents[i];var t=new Vector3(original.x,original.y,original.z);t-=n*Vector3.Dot(n,t);if(t.sqrMagnitude<.000001f)t=Vector3.Cross(n,Mathf.Abs(n.y)>.9f?Vector3.right:Vector3.up);t.Normalize();panel.tangentScratch[i]=new Vector4(t.x,t.y,t.z,original.w);}
                    panel.mesh.tangents=panel.tangentScratch;
                }
                panel.mesh.RecalculateBounds();
            }
        }
        public void Repair()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(owner);

            DeformationExtent=0;foreach(var panel in panels){
                using var appearanceMeshWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(panel.mesh);
                panel.current=(Vector3[])panel.original.Clone();panel.mesh.vertices=panel.current;panel.mesh.normals=panel.normals;panel.mesh.tangents=panel.tangents;panel.mesh.RecalculateBounds();}
        }
        public void ReleaseMaterials()
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(owner);
foreach(var material in materials)if(material){if(Application.isPlaying)UnityEngine.Object.Destroy(material);else UnityEngine.Object.DestroyImmediate(material);}materials.Clear();}
    }
}
