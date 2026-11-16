using UnityEngine;
namespace RoadRush
{
    public sealed class TyreEffects : MonoBehaviour
    {
        public VehicleController Car;
        readonly ParticleSystem[] systems=new ParticleSystem[4];
        Vector3[] vertices=new Vector3[384*4];int[] triangles=new int[384*6];Vector2[] uv=new Vector2[384*4];float[] born=new float[384];
        readonly Vector3[] previous=new Vector3[4];readonly bool[] hasPrevious=new bool[4];int slot;float nextMark;Mesh marks;GameObject markObject;Material effectMaterial;
        float dirt;GameSession session;SurfaceTrackSystem tracks;Material skidMaterial;
        public int EmittedParticles {get;private set;}
        readonly float[] emissionCarry=new float[4];
        void Start()
        {
            session=FindFirstObjectByType<GameSession>();if(session)tracks=SurfaceTrackSystem.Ensure(session);
            var mat=ParticleAppearance.Create("Soft tyre spray and smoke",new Color(.72f,.73f,.72f,.8f));effectMaterial=mat;
            for(int i=0;i<4;i++)
            {
                var g=new GameObject("Tyre spray and smoke "+i);systems[i]=g.AddComponent<ParticleSystem>();
                var main=systems[i].main;main.startLifetime=.6f;main.startSpeed=2;main.startSize=.15f;main.maxParticles=192;main.gravityModifier=.7f;main.simulationSpace=ParticleSystemSimulationSpace.World;main.playOnAwake=false;
                var shape=systems[i].shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=28;shape.radius=.08f;
                var emission=systems[i].emission;emission.rateOverTime=0;systems[i].GetComponent<ParticleSystemRenderer>().sharedMaterial=mat;
            }
            marks=new Mesh{name="Bounded tyre marks"};markObject=new GameObject("Pooled skid marks");markObject.AddComponent<MeshFilter>().sharedMesh=marks;skidMaterial=GameGraphics.Create(Geometry.Material("Rubber skid mark template",new Color(.035f,.037f,.039f),0,.15f));markObject.AddComponent<MeshRenderer>().sharedMaterial=skidMaterial;
            for(int i=0;i<born.Length;i++){int a=i*4;int t=i*6;int[] q={a,a+2,a+1,a+1,a+2,a+3};for(int j=0;j<6;j++)triangles[t+j]=q[j];uv[a]=Vector2.zero;uv[a+1]=Vector2.right;uv[a+2]=Vector2.up;uv[a+3]=Vector2.one;}
        }
        void LateUpdate()
        {
            if(!Car||marks==null)return;bool changed=false;
            for(int i=0;i<4;i++)
            {
                bool contact=Car.WheelGrounded[i]&&Time.timeScale>0;
                float slip=Car.Slip+Car.WheelSlip[i]*6;
                string surface=Car.ContactSurfaceNames[i]??"";
                bool paved=Car.RoadSurface||surface.Contains("asphalt")||surface.Contains("Footpath");
                bool gravel=surface.Contains("shoulder")||surface.Contains("ballast")||surface.Contains("gravel");
                bool splash=Car.WaterDepth>.02f;
                bool wet=Car.SurfaceWetness>.2f;
                bool snow=Car.SurfaceSnow>.08f;
                if(tracks)tracks.Wheel((GetEntityId(),i),Car.ContactPoints[i],Car.ContactNormals[i],Car.transform.right,Mathf.Clamp(Car.Metrics.Radius*.6f,.17f,.7f),contact&&Car.AbsoluteSpeed>.35f&&Car.WaterDepth<.06f,snow?1:wet?0:-1,paved?new Color(.12f,.13f,.14f,.82f):new Color(.15f,.12f,.075f,.8f),snow?Car.SurfaceSnow:Car.SurfaceWetness);
                float amount=!contact&&!splash?0:splash?Mathf.Clamp01(Car.AbsoluteSpeed/6):snow?Mathf.Clamp01(Car.AbsoluteSpeed/14):wet&&paved?Mathf.Clamp01(Car.AbsoluteSpeed/18)*Car.SurfaceWetness:paved?Mathf.Clamp01((slip-1.3f)/5):Mathf.Clamp01((Car.AbsoluteSpeed+slip*2-3)/12);
                var ps=systems[i];
                ps.transform.position=Car.ContactPoints[i]+Vector3.up*.06f;
                if(splash){var point=Car.transform.TransformPoint(new Vector3(i<2?-Car.Metrics.Track*.5f:Car.Metrics.Track*.5f,0,i%2==0?-Car.Metrics.Wheelbase*.5f:Car.Metrics.Wheelbase*.5f));point.y=Car.Body.position.y-Car.Metrics.RestRootHeight(Car.Body.mass,Car.SpringFactor)+Car.WaterDepth;ps.transform.position=point;}
                ps.transform.rotation=Quaternion.LookRotation(-Car.transform.forward+Vector3.up*.55f);
                var main=ps.main;
                main.startColor=splash?new Color(.64f,.81f,.87f,.65f):snow?new Color(.88f,.92f,.98f,.6f):paved?new Color(.7f,.72f,.75f,.34f):gravel?new Color(.47f,.43f,.36f,.95f):wet?new Color(.23f,.13f,.065f,.9f):new Color(.22f,.33f,.09f,.85f);
                main.startSize=splash?.10f:snow?.12f:paved?.42f:gravel?.055f:wet?.09f:.07f;
                main.startLifetime=splash?.8f:snow?.7f:paved?1.5f:.65f;main.gravityModifier=splash?.8f:snow?.7f:paved?-.10f:1.4f;
                var emission=ps.emission;emission.rateOverTime=0;
                if(!ps.isPlaying)ps.Play();
                emissionCarry[i]+=amount*(splash?115:paved?65:85)*Time.deltaTime;
                int count=Mathf.Min(12,Mathf.FloorToInt(emissionCarry[i]));emissionCarry[i]-=count;
                if(count>0)
                {
                    var particle=new ParticleSystem.EmitParams{velocity=-Car.transform.forward*(1+Car.AbsoluteSpeed*.15f+Car.WheelSlip[i]*3)+Vector3.up*(splash?2+Car.AbsoluteSpeed*.15f:paved?.6f:1.3f+Car.WheelSlip[i]*2)};
                    ps.Emit(particle,count);EmittedParticles+=count;
                    if(!paved&&!splash)dirt=Mathf.Min(1,dirt+Time.deltaTime*amount*(wet?.045f:.012f));
                    if(splash)dirt=Mathf.Max(0,dirt-Time.deltaTime*.02f);
                }
                bool marking=!snow&&contact&&Car.RoadSurface&&Car.SurfaceWetness<.5f&&slip>1.7f&&Car.AbsoluteSpeed>3;
                if(marking&&Time.time>=nextMark)
                {
                    var p=Car.ContactPoints[i]+Car.ContactNormals[i]*.018f;
                    if(hasPrevious[i]&&Vector3.Distance(previous[i],p)<2)
                    {
                        var right=Car.transform.right*.11f;int a=slot*4;vertices[a]=previous[i]-right;vertices[a+1]=previous[i]+right;vertices[a+2]=p-right;vertices[a+3]=p+right;born[slot]=Time.time;slot=(slot+1)%born.Length;changed=true;
                    }
                    previous[i]=p;hasPrevious[i]=true;
                }
                else if(!marking)hasPrevious[i]=false;
            }
            Car.Visual?.SetSurfaceDirt(dirt);
            if(Time.time>=nextMark)nextMark=Time.time+.045f;
            for(int i=0;i<born.Length;i++)if(born[i]>0&&Time.time-born[i]>25){for(int j=0;j<4;j++)vertices[i*4+j]=Vector3.zero;born[i]=0;changed=true;}
            if(changed){marks.vertices=vertices;marks.triangles=triangles;marks.uv=uv;marks.RecalculateNormals();marks.RecalculateBounds();}
        }
        void OnDestroy(){foreach(var ps in systems)if(ps)Destroy(ps.gameObject);if(markObject)Destroy(markObject);if(marks)Destroy(marks);if(effectMaterial)Destroy(effectMaterial);if(skidMaterial)Destroy(skidMaterial);if(tracks)for(int i=0;i<4;i++)tracks.Forget((GetEntityId(),i));}
    }
}
