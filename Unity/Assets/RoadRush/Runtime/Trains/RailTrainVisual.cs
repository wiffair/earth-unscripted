using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using RoadRush.Candidates.Articulated;
using RoadRush.Candidates.Compatibility;
using RoadRush.TrainDiagnosticsCandidate;
using Pose=RoadRush.Candidates.Articulated.Pose;

namespace RoadRush
{
    /// <summary>
    /// Owns stock-specific bodies, per-consist paint and original component LODs.
    /// Body proxies, bogies, axles and wheels follow the variable formation.
    /// Cached templates/source fixtures are never modified or destroyed.
    /// </summary>
    public sealed class RailTrainVisual : IDisposable
    {
        public const int CarCount=2;
        readonly GameObject root;
        readonly MeshSpec[] specs;
        readonly Mesh[] meshes;
        readonly Material[] materials;
        readonly Transform[] bodies;
        readonly TrainFormation formation;readonly int count;
        readonly Dictionary<string,Mesh> stockMeshes=new Dictionary<string,Mesh>();
        readonly Rigidbody[] physics;
        readonly Transform[,] bogies;
        readonly Transform[,,] axles;
        readonly Transform[,,,] wheels;
        readonly List<Material> ownedMaterials=new List<Material>();
        readonly AudioSource audio;
        readonly AudioClip engine;
        readonly Transform[] endMarkers=new Transform[4];
        readonly Material whiteMarker,redMarker;
        Pose previous,current;
        float poseTime;bool released;
        public GameObject Root=>root;
        public int MeshCount=>meshes.Length+stockMeshes.Count;
        public TrainFormation Formation=>formation;
        public int MaterialCount=>ownedMaterials.Count;
        public IReadOnlyList<Material> OwnedMaterials=>ownedMaterials;
        public IReadOnlyList<Rigidbody> Bodies=>physics;

        public RailTrainVisual(Transform parent):this(parent,TrainStock.All[0]){}
        public RailTrainVisual(Transform parent,TrainFormation stock)
        {
            formation=stock??throw new ArgumentNullException(nameof(stock));count=formation.Vehicles.Length;
            bodies=new Transform[count];physics=new Rigidbody[count];bogies=new Transform[count,2];axles=new Transform[count,2,3];wheels=new Transform[count,2,3,2];
            root=new GameObject(formation.Name+" · local train");root.SetActive(false);root.transform.SetParent(parent,false);
            specs=MeshFixture.Create();meshes=new Mesh[specs.Length];materials=new Material[MeshFixture.MaterialNames.Length];
            for(int index=0;index<materials.Length;index++)
            {
                var colour=MeshFixture.MaterialColours[index];
                materials[index]=GameGraphics.Create(Geometry.Material("Train template "+MeshFixture.MaterialNames[index],new Color((float)colour.X,(float)colour.Y,(float)colour.Z),.15f,.3f));
                ownedMaterials.Add(materials[index]);
            }
            materials[0].SetColor("_BaseColor",formation.Body);materials[2].SetColor("_BaseColor",formation.Accent);
            for(int index=0;index<meshes.Length;index++)
            {
                var spec=specs[index];if(MeshFixture.ComputeBitCommitment(spec)!=spec.BitCommitment)throw new InvalidOperationException("Train mesh data changed");
                var mesh=new Mesh{name="Train "+spec.Name,indexFormat=IndexFormat.UInt32};
                mesh.vertices=spec.Vertices.Select(ToUnity).ToArray();mesh.subMeshCount=spec.SubmeshIndices.Length;
                for(int slot=0;slot<spec.SubmeshIndices.Length;slot++)mesh.SetTriangles(spec.SubmeshIndices[slot],slot,false);
                mesh.RecalculateNormals();mesh.RecalculateBounds();meshes[index]=mesh;
            }
            for(int car=0;car<count;car++)
            {
                var vehicle=formation.Vehicles[car];
                bodies[car]=Body(vehicle,car);
                var body=bodies[car].gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.mass=vehicle.Mass;
                body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;physics[car]=body;
                var box=bodies[car].gameObject.AddComponent<BoxCollider>();box.center=new Vector3(0,(vehicle.Height+.15f)*.5f,0);box.size=new Vector3(vehicle.Width*.99f,vehicle.Height-.15f,vehicle.Length-.08f);
                for(int bogie=0;bogie<2;bogie++)
                {
                    bogies[car,bogie]=Part("bogie",root.transform,"Bogie");bogies[car,bogie].localScale=new Vector3(formation.Gauge/1.435f,1,1);
                    for(int axle=0;axle<vehicle.Axles;axle++)
                    {
                        axles[car,bogie,axle]=Part("axle",root.transform,"Axle");axles[car,bogie,axle].localScale=new Vector3(formation.Gauge/1.435f,1,1);
                        for(int wheel=0;wheel<2;wheel++){wheels[car,bogie,axle,wheel]=Part("wheel",root.transform,"Wheel");wheels[car,bogie,axle,wheel].localScale=Vector3.one*(vehicle.WheelRadius/.42f);}
                    }
                }
                foreach(int end in new[]{-1,1}){var coupler=Part("coupler",bodies[car],"Coupler");coupler.localPosition=new Vector3(0,.8f,end*(vehicle.Length*.5f+.08f));}
            }
            whiteMarker=GameGraphics.Create(Geometry.Material("Train white end lamp template",new Color(.95f,.93f,.78f)));
            redMarker=GameGraphics.Create(Geometry.Material("Train red end lamp template",new Color(.8f,.035f,.015f)));
            ownedMaterials.Add(whiteMarker);ownedMaterials.Add(redMarker);
            for(int index=0;index<4;index++)
            {
                var lamp=GameObject.CreatePrimitive(PrimitiveType.Sphere);UnityEngine.Object.Destroy(lamp.GetComponent<Collider>());
                lamp.name="Train end lamp";lamp.transform.SetParent(index<2?bodies[0]:bodies[count-1],false);
                lamp.transform.localPosition=new Vector3(index%2==0?-.78f:.78f,1.65f,index<2?formation.Vehicles[0].Length*.5f+.07f:-formation.Vehicles[count-1].Length*.5f-.07f);lamp.transform.localScale=new Vector3(.13f,.10f,.06f);endMarkers[index]=lamp.transform;
            }
            const int sampleRate=22050;var samples=new float[sampleRate*2];
            // Original seamless low diesel/rolling harmonics; no external recording.
            for(int index=0;index<samples.Length;index++)
            {
                float time=index/(float)sampleRate;float pulse=formation.Id.Contains("66")?24:formation.Id.Contains("197")?52:45;
                samples[index]=.08f*Mathf.Sin(2*Mathf.PI*pulse*time)+.025f*Mathf.Sin(2*Mathf.PI*pulse*2*time)+.012f*Mathf.Sin(2*Mathf.PI*173*time)*Mathf.Sin(2*Mathf.PI*7*time);
            }
            engine=AudioClip.Create("Original train diesel loop",samples.Length,1,sampleRate,false);engine.SetData(samples,0);
            var audioObject=new GameObject("Train diesel and rolling sound");audioObject.transform.SetParent(root.transform,false);
            audio=audioObject.AddComponent<AudioSource>();audio.clip=engine;audio.loop=true;audio.spatialBlend=1;audio.minDistance=10;audio.maxDistance=550;audio.rolloffMode=AudioRolloffMode.Logarithmic;audio.dopplerLevel=.3f;
            root.SetActive(false);
        }
        Transform Body(TrainVehicle stock,int index)
        {
            var pivot=new GameObject("Carriage "+index+" · "+formation.Name).transform;pivot.SetParent(root.transform,false);
            int role=index==0?1:index==count-1?-1:0;
            if(stock.Style==TrainBodyStyle.Locomotive||stock.Style==TrainBodyStyle.Container)role=0;
            var group=pivot.gameObject.AddComponent<LODGroup>();var levels=new LOD[3];
            for(int lod=0;lod<3;lod++)
            {
                string key=stock.Id+":"+role+":"+lod;
                if(!stockMeshes.TryGetValue(key,out var mesh)){mesh=TrainStockMeshes.Body(stock,role,lod);stockMeshes.Add(key,mesh);}
                var child=new GameObject("LOD"+lod);child.transform.SetParent(pivot,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                levels[lod]=new LOD(lod==0?.09f:lod==1?.025f:.003f,new Renderer[]{renderer});
            }
            group.SetLODs(levels);group.RecalculateBounds();return pivot;
        }
        Transform Part(string part,Transform parent,string name)
        {
            var pivot=new GameObject(name).transform;pivot.SetParent(parent,false);var group=pivot.gameObject.AddComponent<LODGroup>();var levels=new LOD[3];
            for(int lod=0;lod<3;lod++)
            {
                int index=Array.FindIndex(specs,spec=>spec.Name=="L"+lod+"_"+part);var child=new GameObject("LOD"+lod);child.transform.SetParent(pivot,false);
                child.AddComponent<MeshFilter>().sharedMesh=meshes[index];var renderer=child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials=specs[index].Materials.Select(material=>materials[Array.IndexOf(MeshFixture.MaterialNames,material)]).ToArray();
                levels[lod]=new LOD(lod==0?.08f:lod==1?.025f:.004f,new Renderer[]{renderer});
            }
            group.SetLODs(levels);group.RecalculateBounds();return pivot;
        }
        public static Vector3 ToUnity(Vec3 value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
        static Quaternion Rotation(Frame frame)=>Quaternion.LookRotation(ToUnity(frame.forward),ToUnity(frame.up));
        public void SetPose(Pose pose,bool reset=false)
        {
            if(pose.status!="active"||pose.cars.Count!=count)throw new ArgumentException("Whole supported train pose required");
            previous=reset||current==null?pose:current;current=pose;poseTime=Time.time;
            if(reset)
            {
                // Inactive interpolated rigidbodies do not establish an active
                // PhysX pose through Rigidbody.position alone. Seed scene poses
                // BEFORE activation, then synchronize both actor and transform.
                // Otherwise first MovePosition sweeps a carriage from (0,0,0).
                root.SetActive(false);
                for(int car=0;car<count;car++)bodies[car].SetPositionAndRotation(ToUnity(pose.cars[car].body.origin),Rotation(pose.cars[car].body));
                root.SetActive(true);
                for(int car=0;car<count;car++)
                {
                    var position=ToUnity(pose.cars[car].body.origin);var rotation=Rotation(pose.cars[car].body);
                    physics[car].position=position;physics[car].rotation=rotation;bodies[car].SetPositionAndRotation(position,rotation);
                }
                Physics.SyncTransforms();
            }
            else root.SetActive(true);
        }
        float Fraction=>Mathf.Clamp01((Time.time-poseTime)/.05f);
        static void Place(Transform target,Frame a,Frame b,float fraction)
        {target.SetPositionAndRotation(Vector3.Lerp(ToUnity(a.origin),ToUnity(b.origin),fraction),Quaternion.Slerp(Rotation(a),Rotation(b),fraction));}
        public float MaximumCommandedBodySpeed {get;private set;}
        public bool FixedTick()
        {
            if(current==null||!root.activeSelf)return true;float fraction=Fraction;
            // Validate the whole consist before issuing either body's target.
            // A bad source pose retires the train; it must never become a sweep
            // through traffic. These bounds exceed the service's 16m/s limit.
            for(int car=0;car<count;car++)
            {
                var target=Vector3.Lerp(ToUnity(previous.cars[car].body.origin),ToUnity(current.cars[car].body.origin),fraction);
                var rotation=Quaternion.Slerp(Rotation(previous.cars[car].body),Rotation(current.cars[car].body),fraction);
                float distance=Vector3.Distance(physics[car].position,target);
                MaximumCommandedBodySpeed=Mathf.Max(MaximumCommandedBodySpeed,distance/Time.fixedDeltaTime);
                if(!float.IsFinite(distance)||distance>Mathf.Max(40,formation.MaximumSpeed+8)*Time.fixedDeltaTime+.05f||Quaternion.Angle(physics[car].rotation,rotation)>120*Time.fixedDeltaTime+1)return false;
            }
            for(int car=0;car<count;car++)
            {
                physics[car].MovePosition(Vector3.Lerp(ToUnity(previous.cars[car].body.origin),ToUnity(current.cars[car].body.origin),fraction));
                physics[car].MoveRotation(Quaternion.Slerp(Rotation(previous.cars[car].body),Rotation(current.cars[car].body),fraction));
            }
            return true;
        }
        public void Draw(float speed,float volume,int travelDirection,bool paused)
        {
            if(current==null||!root.activeSelf)return;float fraction=Fraction;
            for(int car=0;car<count;car++)for(int bogie=0;bogie<2;bogie++)
            {
                var before=previous.cars[car].bogies[bogie];var after=current.cars[car].bogies[bogie];Place(bogies[car,bogie],before.frame,after.frame,fraction);
                for(int axle=0;axle<formation.Vehicles[car].Axles;axle++)
                {
                    var a=before.axles[axle];var b=after.axles[axle];Place(axles[car,bogie,axle],a.frame,b.frame,fraction);
                    for(int wheel=0;wheel<2;wheel++)
                    {
                        var oldWheel=a.wheels[wheel];var newWheel=b.wheels[wheel];double phase=oldWheel.phase+(newWheel.phase-oldWheel.phase)*fraction;
                        wheels[car,bogie,axle,wheel].SetPositionAndRotation(Vector3.Lerp(ToUnity(oldWheel.centre),ToUnity(newWheel.centre),fraction),Quaternion.Slerp(Rotation(a.frame),Rotation(b.frame),fraction)*Quaternion.AngleAxis((float)(phase%(Math.PI*2)*180/Math.PI),Vector3.right));
                    }
                }
            }
            for(int index=0;index<4;index++)endMarkers[index].GetComponent<MeshRenderer>().sharedMaterial=(index<2)==(travelDirection>0)?whiteMarker:redMarker;
            audio.transform.position=ToUnity(current.cars[0].body.origin);audio.volume=paused?0:volume*(.25f+Mathf.Clamp01(speed/16)*.3f);audio.pitch=.8f+Mathf.Clamp01(speed/20)*.3f;
            if(!audio.isPlaying&&!paused)audio.Play();
        }
        public void Hide(){root.SetActive(false);audio.Stop();}
        public void Dispose()
        {
            if(released)return;released=true;
            if(audio)audio.Stop();if(root)UnityEngine.Object.Destroy(root);
            foreach(var mesh in meshes)if(mesh)UnityEngine.Object.Destroy(mesh);
            foreach(var mesh in stockMeshes.Values)if(mesh)UnityEngine.Object.Destroy(mesh);
            foreach(var material in ownedMaterials)if(material)UnityEngine.Object.Destroy(material);
            if(engine)UnityEngine.Object.Destroy(engine);
        }
    }
}
