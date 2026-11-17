using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    [DefaultExecutionOrder(1200)]
    public sealed class WindshieldRain : MonoBehaviour
    {
        const int MaximumDrops=180;
        struct Drop{public Vector2 uv;public float radius,age;}
        readonly Drop[] drops=new Drop[MaximumDrops];int count;float emission;uint random=739391;
        readonly Vector2[] oldA=new Vector2[2],oldB=new Vector2[2],bladeA=new Vector2[2],bladeB=new Vector2[2];bool previousBlades;readonly bool[] validBlade=new bool[2];
        readonly List<Vector3> vertices=new List<Vector3>(MaximumDrops*4);
        readonly List<Vector2> uvs=new List<Vector2>(MaximumDrops*4);
        readonly List<Color> colours=new List<Color>(MaximumDrops*4);
        readonly List<int> indices=new List<int>(MaximumDrops*6);
        DriveCamera driver;Camera view;GameSession session;CarVisual boundVisual;Transform[] boundWipers;
        WindshieldWipers.Surface surface;WindshieldWipers.PaneMask paneMask;Mesh mesh;Material material;bool hasGlass;
        public int DropCount=>count;public int ClearedDrops{get;private set;}public int EmittedDrops{get;private set;}
        public bool HasGlass=>hasGlass;public bool RenderingGlass{get;private set;}
        public WindshieldWipers.Surface GlassSurface=>surface;
        float RandomValue(){random^=random<<13;random^=random>>17;random^=random<<5;return (random&0xffffff)/16777216f;}
        void Awake(){driver=GetComponent<DriveCamera>();view=GetComponent<Camera>();session=FindFirstObjectByType<GameSession>();}
        public void BindNow()
        {
            var visual=driver&&driver.Car?driver.Car.Visual:null;if(visual==boundVisual&&visual&&boundWipers==visual.Wipers)return;
            boundVisual=visual;boundWipers=visual?visual.Wipers:null;hasGlass=visual&&WindshieldWipers.TrySurface(visual,out surface);paneMask=null;if(hasGlass&&WindshieldWipers.TrySurface(visual,out surface,out var paneTriangles))paneMask=new WindshieldWipers.PaneMask(surface,paneTriangles);count=0;emission=0;previousBlades=false;
        }
        public void Advance(float dt,float rain,float speed)
        {
            if(dt<=0||!hasGlass)return;
            // Use the actual world-space rubber blade endpoints. Clearing follows
            // each swept quadrilateral, rather than an unrelated screen animation.
            var a=bladeA;var b=bladeB;
            for(int i=0;i<2;i++)
            {
                validBlade[i]=boundWipers!=null&&i<boundWipers.Length&&WindshieldWipers.BladeSegment(boundVisual.transform,surface,boundWipers[i],out a[i],out b[i]);if(!validBlade[i])continue;
                if(previousBlades&&(a[i]-oldA[i]).sqrMagnitude+(b[i]-oldB[i]).sqrMagnitude>.0000001f)
                    for(int j=count-1;j>=0;j--)if(WindshieldWipers.Swept(surface.Metres(surface.Point(drops[j].uv.x,drops[j].uv.y)),oldA[i],oldB[i],a[i],b[i],.014f+drops[j].radius))
                    {drops[j]=drops[--count];ClearedDrops++;}
                oldA[i]=a[i];oldB[i]=b[i];
            }
            previousBlades=true;
            for(int i=count-1;i>=0;i--)
            {
                var d=drops[i];d.age+=dt;d.uv.y-=dt*(.003f+d.radius*.9f);if(d.uv.y<0||(paneMask!=null&&!paneMask.Contains(surface.Metres(surface.Point(d.uv.x,d.uv.y)),d.radius))||d.age>Mathf.Lerp(18,6,Mathf.Clamp01(speed/45)))drops[i]=drops[--count];else drops[i]=d;
            }
            emission+=Mathf.Clamp01(rain)*dt*(70+Mathf.Min(speed,45)*1.2f);
            while(emission>=1)
            {
                emission--;if(count==MaximumDrops)continue;var drop=new Drop{uv=new Vector2(RandomValue(),RandomValue()),radius=Mathf.Lerp(.0014f,.0042f,RandomValue())};
                var point=surface.Metres(surface.Point(drop.uv.x,drop.uv.y));
                if(paneMask!=null&&!paneMask.Contains(point,drop.radius))continue;
                bool underRubber=false;for(int i=0;i<2;i++)if(validBlade[i]&&WindshieldWipers.Swept(point,bladeA[i],bladeB[i],bladeA[i],bladeB[i],.014f+drop.radius)){underRubber=true;break;}
                if(underRubber)continue;drops[count++]=drop;EmittedDrops++;
            }
        }
        public void SeedVerificationDrop(Vector2 uv)
        {
            if(!Application.isEditor&&string.IsNullOrEmpty(SinglePlayerBeta.VerificationOutput))throw new System.InvalidOperationException("Isolated windshield proof required");
            if(count<MaximumDrops)drops[count++]=new Drop{uv=uv,radius=.004f,age=.5f};
        }
        void LateUpdate()
        {
            BindNow();RenderingGlass=false;if(!hasGlass||!driver.Car)return;
            float rain=session&&session.Conditions?session.Conditions.RainAt(boundVisual.transform.TransformPoint(surface.Point(.5f,.5f))):boundVisual.WiperRain;
            Advance(Time.deltaTime,rain,driver.Car.AbsoluteSpeed);
            if(driver.Mode!=2||driver.Preview||driver.Input&&driver.Input.LookBack||session&&session.Menu||Vector3.Dot(transform.forward,driver.Car.transform.forward)<.2f)return;
            RenderingGlass=count>0;if(!RenderingGlass)return;
            if(!mesh){mesh=new Mesh{name="Cockpit glass raindrops"};mesh.MarkDynamic();}
            if(!material){var shader=Resources.Load<Shader>("WindshieldRain");if(!shader)return;material=new Material(shader){name="Owned cockpit raindrop lenses",hideFlags=HideFlags.DontSave};}
            // Beads bulge above the glass: small view-facing lens quads avoid
            // turning the shallow concept-car screen into flat white ellipses.
            // Their centres retain real glass depth and the actual pane mask.
            var across=boundVisual.transform.InverseTransformDirection(view.transform.right);var rising=boundVisual.transform.InverseTransformDirection(view.transform.up);
            vertices.Clear();uvs.Clear();colours.Clear();indices.Clear();
            for(int i=0;i<count;i++)
            {
                var d=drops[i];var point=surface.Point(d.uv.x,d.uv.y);if(paneMask!=null&&!paneMask.Contains(surface.Metres(point),d.radius))continue;
                // On the outer glass, just behind the blade rubber from the
                // driver's eye. Ordinary depth rejects cabin hardware/pillars;
                // pane triangles independently preserve split-window gaps.
                var centre=point+surface.Normal*.045f;float r=d.radius;int offset=vertices.Count;
                vertices.Add(centre-across*r-rising*r*1.5f);vertices.Add(centre+across*r-rising*r*1.5f);vertices.Add(centre+across*r+rising*r*1.5f);vertices.Add(centre-across*r+rising*r*1.5f);
                uvs.Add(Vector2.zero);uvs.Add(Vector2.right);uvs.Add(Vector2.one);uvs.Add(Vector2.up);
                var tint=new Color(.62f,.76f,.84f,Mathf.Clamp01(d.age*7)*.72f);for(int j=0;j<4;j++)colours.Add(tint);
                indices.Add(offset);indices.Add(offset+1);indices.Add(offset+2);indices.Add(offset);indices.Add(offset+2);indices.Add(offset+3);
            }
            mesh.Clear(false);mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetColors(colours);mesh.SetTriangles(indices,0,false);mesh.bounds=new Bounds(surface.Point(.5f,.5f)+surface.Normal*.045f,new Vector3(surface.Width+.1f,surface.Height+.1f,Mathf.Abs(surface.up.z)+.15f));
            Graphics.DrawMesh(mesh,boundVisual.transform.localToWorldMatrix,material,gameObject.layer,view,0,null,UnityEngine.Rendering.ShadowCastingMode.Off,false,null,UnityEngine.Rendering.LightProbeUsage.Off);
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
