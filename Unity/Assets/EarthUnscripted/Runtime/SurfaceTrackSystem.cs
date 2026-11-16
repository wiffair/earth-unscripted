using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted
{
    // Transient weather marks belong to the drive, never to a prepared-area cache.
    // One bounded mesh serves all wheels; it creates no colliders or per-mark objects.
    public sealed class SurfaceTrackSystem:MonoBehaviour
    {
        public const int Capacity=8192;
        sealed class Stroke {public Vector3 point,normal,right;public float distance,updated;public int kind,segments;public bool valid;}
        readonly List<(EntityId,int)> retired=new List<(EntityId,int)>();
        readonly Dictionary<(EntityId,int),Stroke> strokes=new Dictionary<(EntityId,int),Stroke>();
        readonly Vector3[] vertices=new Vector3[Capacity*4],normals=new Vector3[Capacity*4];
        readonly Vector2[] uv=new Vector2[Capacity*4];readonly Vector4[] life=new Vector4[Capacity*4];
        readonly Color[] colours=new Color[Capacity*4];readonly int[] triangles=new int[Capacity*6];
        readonly float[] born=new float[Capacity],lifetimes=new float[Capacity];readonly bool[] active=new bool[Capacity];
        readonly RaycastHit[] hits=new RaycastHit[12];
        GameSession session;Mesh mesh;Material material;MeshRenderer view;int slot;bool dirty;float nextUpload,nextTraffic,nextScan;
        TrafficCar[] traffic=Array.Empty<TrafficCar>();
        public int ActiveSegments{get;private set;}public int SnowSegments{get;private set;}public int WetSegments{get;private set;}
        public Mesh Mesh=>mesh;
        public MeshRenderer Renderer=>view;
        public int SegmentsFor(EntityId source){int total=0;foreach(var pair in strokes)if(pair.Key.Item1==source)total+=pair.Value.segments;return total;}
        public static SurfaceTrackSystem Ensure(GameSession owner)
        {
            var result=owner.GetComponent<SurfaceTrackSystem>();if(!result){result=owner.gameObject.AddComponent<SurfaceTrackSystem>();result.session=owner;result.Initialize();}return result;
        }
        void Initialize()
        {
            var root=new GameObject("Pooled snow and wet tyre tracks");root.transform.SetParent(transform,false);
            mesh=new Mesh{name="Bounded weather tyre tracks",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;view=root.AddComponent<MeshRenderer>();
            material=GameGraphics.Create(Resources.Load<Shader>("TyreTracks"));view.sharedMaterial=material;
            view.shadowCastingMode=ShadowCastingMode.Off;view.receiveShadows=true;
            for(int i=0;i<Capacity;i++){int a=i*4,t=i*6;triangles[t]=a;triangles[t+1]=a+2;triangles[t+2]=a+1;triangles[t+3]=a+1;triangles[t+4]=a+2;triangles[t+5]=a+3;}
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.SetUVs(1,life);mesh.colors=colours;mesh.triangles=triangles;
        }
        public void Forget((EntityId,int) source){strokes.Remove(source);}
        public void Wheel((EntityId,int) source,Vector3 point,Vector3 normal,Vector3 wheelRight,float width,bool contact,int markKind,Color colour,float strength)
        {
            if(!mesh||!session)return;
            if(!session.Profile.surfaceTracks||Time.timeScale==0||session.Menu){if(strokes.TryGetValue(source,out var paused))paused.valid=false;return;}
            if(!strokes.TryGetValue(source,out var stroke)){stroke=new Stroke();strokes.Add(source,stroke);}
            stroke.updated=Time.time;
            if(!float.IsFinite(point.sqrMagnitude)||!float.IsFinite(normal.sqrMagnitude)||!float.IsFinite(wheelRight.sqrMagnitude)||!contact||markKind<0||strength<.08f||normal.sqrMagnitude<.5f){stroke.valid=false;return;}
            normal.Normalize();var right=Vector3.ProjectOnPlane(wheelRight,normal).normalized*Mathf.Clamp(width,.12f,.8f)*.5f;
            point+=normal*.012f;float distance=Vector3.Distance(stroke.point,point);
            bool continuation=stroke.valid&&stroke.kind==markKind&&distance<=3&&Vector3.Dot(normal,stroke.normal)>.7f;
            if(continuation&&distance<.16f)return;
            if(continuation)
            {
                int a=slot*4;float expiry=markKind==1?600:45;
                colour.a=markKind==1?.5f+.5f*Mathf.Clamp01(strength):.49f*Mathf.Clamp01(strength);
                vertices[a]=stroke.point-stroke.right;vertices[a+1]=stroke.point+stroke.right;vertices[a+2]=point-right;vertices[a+3]=point+right;
                float next=stroke.distance+distance;
                for(int v=0;v<4;v++)
                {normals[a+v]=v<2?stroke.normal:normal;uv[a+v]=new Vector2(v%2,v<2?stroke.distance:next);life[a+v]=new Vector4(Time.time,expiry,markKind,Mathf.Clamp01(strength));colours[a+v]=colour;}
                if(!active[slot])ActiveSegments++;
                active[slot]=true;born[slot]=Time.time;lifetimes[slot]=expiry;
                slot=(slot+1)%Capacity;stroke.distance=next;stroke.segments++;dirty=true;
                if(markKind==1)SnowSegments++;else WetSegments++;
            }
            else stroke.distance=0;
            stroke.point=point;stroke.right=right;stroke.normal=normal;stroke.kind=markKind;stroke.valid=true;
        }
        void LateUpdate()
        {
            using var timing=WorkTelemetry.Measure("frame.surface-tracks",null,8);
            if(!mesh||!session)return;Shader.SetGlobalFloat("_EarthUnscriptedTrackTime",Time.time);
            if(!session.Profile.surfaceTracks){view.enabled=false;strokes.Clear();return;}view.enabled=true;
            if(session.Menu||Time.timeScale==0)return;
            if(Time.time>=nextTraffic){nextTraffic=Time.time+.1f;TrafficMarks();}
            if(Time.time<nextUpload)return;nextUpload=Time.time+.08f;
            for(int i=0;i<Capacity;i++)if(active[i]&&Time.time-born[i]>=lifetimes[i])
            {active[i]=false;ActiveSegments--;for(int v=0;v<4;v++)vertices[i*4+v]=Vector3.zero;dirty=true;}
            if(!dirty)return;dirty=false;
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.SetUVs(1,life);mesh.colors=colours;mesh.RecalculateBounds();
        }
        void TrafficMarks()
        {
            if(Time.time>=nextScan){nextScan=Time.time+1;traffic=FindObjectsByType<TrafficCar>(FindObjectsSortMode.None);retired.Clear();foreach(var pair in strokes)if(Time.time-pair.Value.updated>30)retired.Add(pair.Key);foreach(var key in retired)strokes.Remove(key);}
            var weather=session.Conditions;bool snow=weather&&weather.Snow>.08f,wet=weather&&weather.Wetness>.2f;
            if(!snow&&!wet)return;
            foreach(var car in traffic)
            {
                if(!car||!car.gameObject.activeInHierarchy||car.Session!=session||!car.Visual||car.Speed<.4f||(car.transform.position-session.Car.Body.position).sqrMagnitude>350*350)continue;
                var local=weather.SurfaceAt(car.PhysicsPosition);snow=local.Snow>.08f;wet=local.Wet>.2f;if(!snow&&!wet)continue;
                var wheels=car.Visual.Wheels;float radius=Core.VehicleDimensions.ForModel(car.Visual.Model).Radius;
                for(int w=0;w<Math.Min(4,wheels.Length);w++)
                {
                    var anchor=wheels[w].position;int count=Physics.RaycastNonAlloc(anchor+Vector3.up*.1f,Vector3.down,hits,radius+.45f,~0,QueryTriggerInteraction.Ignore);int best=-1;float distance=float.PositiveInfinity;
                    for(int i=0;i<count;i++)if(!hits[i].collider.attachedRigidbody&&hits[i].distance<distance){best=i;distance=hits[i].distance;}
                    var id=(car.GetEntityId(),w);
                    if(best<0){if(strokes.TryGetValue(id,out var old))old.valid=false;continue;}
                    Wheel(id,hits[best].point,hits[best].normal,car.transform.right,Mathf.Clamp(radius*.6f,.17f,.7f),true,snow?1:0,new Color(.12f,.13f,.14f,.75f),snow?local.Snow:local.Wet);
                }
            }
        }
        void OnDestroy(){if(view)Destroy(view.gameObject);if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
