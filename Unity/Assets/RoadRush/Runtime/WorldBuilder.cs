using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadRush
{
    public sealed partial class WorldBuilder : MonoBehaviour
    {
        public RoutePath Route { get; private set; }
        public GeoWorld GeographicWorld { get; private set; }
        public bool IsCircuit=>Route?.Region?.circuit??false;
        public Transform Garage { get; private set; }
        public Material Asphalt { get; private set; }
        public Light Sun { get; private set; }
        public Light Moon { get; private set; }
        public WorldAtmosphere Atmosphere { get; private set; }
        public CircuitShowcaseScenery CircuitArt { get; private set; }
        public Vector3[] Checkpoints { get; private set; }
        float garageDistance=35;Vector3 garageSite;
        // Shared preparation for rendering and preview validation. No scene meshes,
        // lighting or materials are created until Build continues below.
        PreparedWorldData preparedData;
        public void PrepareRegionGeometry(GeoRegion region)
        {
            // Synchronous Editor/legacy wrapper shares the same data finalizer.
            // Automatic width admission is connected through asynchronous boot later.
            var data=WorldDataFinalizer.PrepareOwned(region,null,System.Threading.CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
            AttachPreparedData(data);
        }
        void AttachPreparedData(PreparedWorldData data)
        {
            if(data==null||data.Route==null)throw new System.ArgumentException("Missing prepared world");
            if(data.Region==null&&(data.DefaultTerrain==null||!ReferenceEquals(data.DefaultTerrain.Owner,data.Route)))throw new System.IO.InvalidDataException("Prepared default terrain owner mismatch");
            if(data.Region!=null&&data.Region.roadWidthSchema==1&&(data.RoadSurfaces==null||data.RoadSurfaces.Owner!=data.Region||data.RoadSurfaces.WidthField!=data.Region.PreparedRoadWidths))throw new System.IO.InvalidDataException("Prepared world surface identity mismatch");
            preparedData=data;Route=data.Route;garageDistance=data.GarageDistance;garageSite=data.GarageSite;
        }
        public void BuildPrepared(PreparedWorldData data,bool deferInitialCoverage=false)
        {AttachPreparedData(data);BuildResources(data.Region,deferInitialCoverage);}
        public void Build(string regionId="")
        {
            var region=string.IsNullOrEmpty(regionId)?null:GeoRegion.Load(regionId);
            PrepareRegionGeometry(region);BuildResources(region);
        }
        void BuildEnvironment()
        {
using(BootTelemetry.Measure("main.world.environment")){
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.51f,.64f,.78f);RenderSettings.ambientEquatorColor=new Color(.41f,.44f,.4f);RenderSettings.ambientGroundColor=new Color(.18f,.2f,.17f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.0007f;RenderSettings.fogColor=new Color(.65f,.75f,.82f);
            var sunObject=new GameObject("Approximate cycle sun");sunObject.transform.SetParent(transform,false);Sun=sunObject.AddComponent<Light>();Sun.type=LightType.Directional;Sun.intensity=2.1f;Sun.color=new Color(1,.89f,.72f);Sun.shadows=LightShadows.Soft;sunObject.transform.rotation=Quaternion.Euler(32,-35,0);
            var moonObject=new GameObject("Approximate cycle moon");moonObject.transform.SetParent(transform,false);Moon=moonObject.AddComponent<Light>();Moon.type=LightType.Directional;Moon.intensity=0;Moon.color=new Color(.48f,.62f,.95f);Moon.shadows=LightShadows.Soft;moonObject.transform.rotation=Quaternion.Euler(212,-35,0);
            RenderSettings.sun=Sun;RenderSettings.reflectionIntensity=1;
            var skyShader=SurfaceLibrary.Instance?SurfaceLibrary.Instance.Sky:Shader.Find("Skybox/Procedural");if(skyShader){var sky=new Material(skyShader);sky.SetFloat("_AtmosphereThickness",1f);sky.SetColor("_SkyTint",new Color(.5f,.5f,.5f));sky.SetColor("_GroundColor",new Color(.33f,.35f,.32f));sky.SetFloat("_Exposure",1.05f);RenderSettings.skybox=sky;}
            Atmosphere=WorldAtmosphere.Install(transform);

}
        }
        void BuildResources(GeoRegion region,bool deferInitialCoverage=false)
        {
            BuildEnvironment();
            bool circuitReplacementInstalled=IsCircuit&&CircuitReplacementArt.Enabled&&CircuitReplacementArt.TryInstall(transform,region);
            if(region==null){using(BootTelemetry.Measure("main.default.terrain"))BuildTerrain();using(BootTelemetry.Measure("main.default.roads"))BuildRoad();using(BootTelemetry.Measure("main.default.village"))BuildVillage();using(BootTelemetry.Measure("main.default.trees"))BuildTrees();}
            else{Asphalt=(region.CircuitScenery!=null||region.CircuitRoadSurfaces!=null)?CircuitShowcaseScenery.CreateAsphalt(transform):Geometry.Surface("Road asphalt",new Color(.13f,.145f,.16f),.22f,1);GeographicWorld=new GameObject("Offline Dee valley").AddComponent<GeoWorld>();GeographicWorld.transform.SetParent(transform,false);if(preparedData.RoadSurfaces!=null)GeographicWorld.InitializePrepared(region,Asphalt,preparedData.RoadSurfaces,deferInitialCoverage);else GeographicWorld.Initialize(region,Asphalt,deferInitialCoverage);}
            if(region?.CircuitScenery!=null)
            {
                CircuitArt=CircuitShowcaseScenery.Install(transform,region.CircuitScenery,deferInitialCoverage);
                // HUD/service consumers retain a real anchor, without spawning
                // the fictional roadside garage architecture inside a circuit.
                Garage=new GameObject("Circuit service anchor").transform;
                Garage.SetParent(transform,false);Garage.position=region.CircuitScenery.Bay;
            }
            else using(BootTelemetry.Measure("main.world.garage"))BuildGarage();using(BootTelemetry.Measure("main.world.checkpoints"))BuildCheckpoints();if(IsCircuit)using(BootTelemetry.Measure("main.world.circuitstart"))BuildCircuitStart();
            if(IsCircuit&&!circuitReplacementInstalled&&CircuitCandidateScenery.Enabled)CircuitCandidateScenery.TryInstall(transform,region);
            if(region==null)using(BootTelemetry.Measure("main.default.staticbatch"))StaticBatchingUtility.Combine(gameObject);using(BootTelemetry.Measure("main.world.dynamicgi"))DynamicGI.UpdateEnvironment();
        }
        Texture2D SolidLand(){var texture=new Texture2D(1,1);texture.SetPixel(0,0,new Color(.3f,.35f,.19f));texture.Apply();return texture;}
        void BuildTerrain()
        {
            var terrain=preparedData.DefaultTerrain;
            Geometry.Mesh("Valley terrain",transform,terrain.Vertices,terrain.Triangles,terrain.UV,ArtSurfaces.Terrain(SolidLand()),true);
            var water=Geometry.Material("Lake water",new Color(.14f,.3f,.36f),.5f,.94f);
            Geometry.Box("Lake",transform,new Vector3(100,-1.8f,100),new Vector3(380,.1f,530),water);
            // A physical outer boundary keeps recovery predictable.
            var rock=Geometry.Material("Boundary stone",new Color(.29f,.32f,.29f));
            Geometry.Box("North boundary",transform,new Vector3(0,15,1195),new Vector3(1920,70,12),rock,true);
            Geometry.Box("South boundary",transform,new Vector3(0,15,-1195),new Vector3(1920,70,12),rock,true);
            Geometry.Box("East boundary",transform,new Vector3(955,15,0),new Vector3(12,70,2400),rock,true);
            Geometry.Box("West boundary",transform,new Vector3(-955,15,0),new Vector3(12,70,2400),rock,true);
            foreach(Transform child in transform)if(child.name.EndsWith("boundary"))child.GetComponent<Renderer>().enabled=false;
        }
        void BuildRoad()
        {
            Asphalt=Geometry.Surface("Road asphalt",new Color(.13f,.145f,.16f),.22f,1);
            var shoulder=Geometry.Surface("Gravel verge",new Color(.33f,.32f,.25f),.35f,1);
            Strip("Road shoulder",-1.5f,shoulder,-.07f,true);Strip("Continuous road",0,Asphalt,0,true);
            var white=Geometry.Material("Road paint",new Color(.87f,.87f,.78f));
            var guard=Geometry.Material("Galvanised steel",new Color(.53f,.58f,.62f),.7f,.38f);
            var bollard=Geometry.Material("Bollard",new Color(.75f,.76f,.7f));
            for(float d=0;d<Route.Length;d+=12)
            {
                float width=Route.Width(d);var rotation=Route.Rotation(d);
                if(d%24<12)Marking("Centre dash",d,5,0,.14f,white);
                foreach(int side in new[]{-1,1})
                {
                    Marking("Edge paint",d,12,side,.12f,white);
                    if(width>16&&Route.Width(d+5)>16)Marking("Lane dash",d,5,side*2,.13f,white);
                    if(d%48<12){PlacedBox("Reflector",Route.At(d,side*(width*.5f+1.2f))+Vector3.up*.45f,new Vector3(.16f,.9f,.16f),rotation,bollard);}
                    if(width>15 && d>100){PlacedBox("Guard rail",Route.At(d,side*(width*.5f+1.7f))+Vector3.up*.72f,new Vector3(.16f,.36f,11.9f),rotation,guard,true);}
                }
            }
            for(float d=90;d<Route.Length;d+=300)
            {
                var q=Route.Rotation(d);var p=Route.At(d,-Route.Width(d)*.5f-2.3f);
                PlacedBox("Sign pole",p+Vector3.up*1.4f,new Vector3(.09f,2.8f,.09f),q,guard);
                var sign=new GameObject("Speed sign").transform;sign.SetParent(transform,false);sign.SetPositionAndRotation(p+Vector3.up*2.9f,q*Quaternion.Euler(0,180,0));
                Geometry.Box("Sign board",sign,Vector3.zero,new Vector3(1.5f,1.3f,.08f),white);
                Geometry.BoardText(Route.Limit(d)+"\nMPH",sign,Vector3.zero,1.24f,1.05f,.08f,Color.black);
            }
        }
        void Marking(string name,float start,float length,int lane,float width,Material material)
        {
            int n=Mathf.CeilToInt(length);var v=new Vector3[(n+1)*2];var uv=new Vector2[v.Length];var t=new int[n*6];
            for(int i=0;i<=n;i++)
            {
                float d=start+length*i/n;float offset=lane==0?0:Mathf.Abs(lane)==1?Mathf.Sign(lane)*(Route.Width(d)*.5f-.35f):Mathf.Sign(lane)*Route.Width(d)*.25f;
                var p=Route.At(d,offset)+Vector3.up*.025f;var right=Vector3.Cross(Vector3.up,Route.Forward(d)).normalized;v[i*2]=p-right*width*.5f;v[i*2+1]=p+right*width*.5f;
                if(i<n){int a=i*2;int[] q={a,a+2,a+1,a+1,a+2,a+3};for(int j=0;j<6;j++)t[i*6+j]=q[j];}
            }
            Geometry.Mesh(name,transform,v,t,uv,material,false);
        }
        void Strip(string name,float extra,Material material,float y,bool collision)
        {
            int count=Route.Points.Length;var v=new Vector3[count*2];var uv=new Vector2[v.Length];var t=new int[(count-1)*6];
            for(int i=0;i<count;i++)
            {
                float d=Route.DistanceAtPoint(i);
                var p=Route.Points[i];var direction=(Route.Points[(i+1)%(count-1)]-Route.Points[(i-1+(count-1))%(count-1)]).normalized;
                var right=Vector3.Cross(Vector3.up,direction).normalized;
                float width=Route.Width(d)-extra*2;
                v[i*2]=p-right*width*.5f+Vector3.up*y;v[i*2+1]=p+right*width*.5f+Vector3.up*y;
                uv[i*2]=new Vector2(0,i*1.4f);uv[i*2+1]=new Vector2(width*.25f,i*1.4f);
                if(i<count-1){int a=i*2;int[] segment={a,a+2,a+1,a+1,a+2,a+3};for(int j=0;j<6;j++)t[i*6+j]=segment[j];}
            }
            Geometry.Mesh(name,transform,v,t,uv,material,collision);
        }
        void BuildVillage()
        {
            var random=new System.Random(90);var roof=Geometry.Material("Slate",new Color(.14f,.17f,.19f));
            var facade=Facade();var pavement=Geometry.Material("Pavement",new Color(.43f,.44f,.4f));
            for(float d=Route.Length*.32f;d<Route.Length*.52f;d+=27)
            {
                foreach(int side in new[]{-1,1})
                {
                    var p=Route.At(d,side*18);float h=8+(float)random.NextDouble()*8;var q=Route.Rotation(d);
                    PlacedBox("Village building",p+Vector3.up*h*.5f,new Vector3(12,h,14),q,facade,true);
                    PlacedBox("Slate roof",p+Vector3.up*(h+.2f),new Vector3(12.8f,.6f,14.8f),q,roof);
                    PlacedBox("Footpath",Route.At(d,side*6.5f)-Vector3.up*.08f,new Vector3(3,.25f,27),q,pavement,true);
                    if(side==-1){var post=PlacedBox("Street lamp",Route.At(d,-8)+Vector3.up*3,new Vector3(.12f,6,.12f),q,roof);Geometry.Box("Lamp head",post.transform,new Vector3(0,.48f,-.2f),new Vector3(3,.035f,1),Geometry.Material("Lamp glow",new Color(.9f,.78f,.45f)));}
                }
            }
        }
        Material Facade()
        {
            var m=Geometry.Material("Brick and windows",new Color(.7f,.63f,.52f),0,.3f);var tex=new Texture2D(128,128,TextureFormat.RGB24,true){wrapMode=TextureWrapMode.Repeat};var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                bool window=x%32>8&&x%32<24&&y%40>10&&y%40<30;
                bool mortar=y%8==0||(x+(y/8%2)*8)%16==0;
                pixels[y*128+x]=window?(x%32==16||y%40==20?new Color(.55f,.56f,.53f):new Color(.13f,.23f,.28f)):mortar?new Color(.42f,.4f,.36f):new Color(.72f,.54f,.4f);
            }
            tex.SetPixels(pixels);tex.Apply();m.mainTexture=tex;return m;
        }
        void BuildTrees()
        {
            var random=new System.Random(418);var bark=Geometry.Material("Bark",new Color(.22f,.17f,.11f));var leaf=Geometry.Material("Leaves",new Color(.2f,.32f,.12f));
            // Fixed population: exploration never accumulates objects.
            for(int i=0;i<520;i++)
            {
                float d=(float)random.NextDouble()*Route.Length;float side=random.Next(2)==0?-1:1;float lateral=side*(Route.Width(d)*.5f+15+(float)random.NextDouble()*145);
                var p=Route.At(d,lateral);if(d>Route.Length*.3f&&d<Route.Length*.55f&&Mathf.Abs(lateral)<45)continue;
                p.y=Route.TerrainHeight(p.x,p.z);float h=6+(float)random.NextDouble()*7;
                NatureMeshes.Tree(transform,p,h,i,bark,leaf);
            }
        }
        void BuildGarage()
        {
            Garage=new GameObject("HARBOR AUTO").transform;Garage.SetParent(transform,false);Garage.SetPositionAndRotation(Route.Region!=null?garageSite:Route.At(35,-23),Route.Rotation(garageDistance));
            var concrete=Geometry.Surface("Garage concrete",new Color(.36f,.37f,.35f),.2f,2);
            Geometry.Box("Service forecourt",Garage,new Vector3(0,-.06f,0),new Vector3(34,.12f,40),concrete,true);
            var red=Geometry.Material("Garage red",new Color(.48f,.09f,.06f));
            Geometry.Box("Garage wall",Garage,new Vector3(-10,2,-4),new Vector3(7,4,13),concrete,true);
            Geometry.Box("Garage canopy",Garage,new Vector3(-4,4,0),new Vector3(18,.3f,16),red);
            Geometry.Box("Garage sign board",Garage,new Vector3(-4,3.3f,8.06f),new Vector3(14,1.3f,.1f),red);
            Geometry.BoardText("HARBOR AUTO",Garage,new Vector3(-4,3.3f,8.06f),12.6f,1.05f,.1f);
            Geometry.FittedLabel("STOP HERE TO REPAIR",Garage,new Vector3(1,.04f,1),Quaternion.Euler(90,0,0),18,2.2f);
        }
        void BuildCircuitStart()
        {
            var root=new GameObject("Circuit start and grid").transform;root.SetParent(transform,false);root.SetPositionAndRotation(Route.At(0),Route.Rotation(0));float width=Route.Width(0);
            var steel=Geometry.Material("Circuit gantry steel",new Color(.2f,.23f,.25f),.5f,.35f);var white=Geometry.Material("Circuit line white",new Color(.86f,.86f,.8f));var black=Geometry.Material("Circuit line black",new Color(.06f,.07f,.075f));
            foreach(int side in new[]{-1,1})Geometry.Box("Start gantry post",root,new Vector3(side*(width*.5f+1.3f),3.5f,0),new Vector3(.25f,7,.25f),steel,true);
            Geometry.Box("Start gantry",root,new Vector3(0,6.7f,0),new Vector3(width+3,.65f,.3f),steel);
            Geometry.BoardText(Route.Region.name,root,new Vector3(0,6.7f,0),width,.5f,.3f);
            int tiles=Mathf.CeilToInt(width/.7f);for(int row=0;row<2;row++)for(int x=0;x<tiles;x++)Geometry.Box("Start checker",root,new Vector3(-width*.5f+(x+.5f)*width/tiles,.025f,row*.65f),new Vector3(width/tiles,.012f,.65f),(row+x)%2==0?white:black);
            for(int i=0;i<8;i++)
            {float d=-8-i/2*7;float lane=i%2==0?-2.7f:2.7f;var origin=Route.At(d,lane)+Vector3.up*.02f;var rotation=Route.Rotation(d);foreach(int side in new[]{-1,1})PlacedBox("Grid side",origin+rotation*new Vector3(side*1.15f,0,0),new Vector3(.08f,.01f,4.6f),rotation,white);PlacedBox("Grid front",origin+rotation*new Vector3(0,0,2.3f),new Vector3(2.3f,.01f,.08f),rotation,white);}
        }
        void BuildCheckpoints()
        {
            Checkpoints=new Vector3[8];
            for(int i=0;i<8;i++)Checkpoints[i]=Route.At((i+1)*Route.Length/8);
        }
        GameObject PlacedBox(string name,Vector3 position,Vector3 size,Quaternion rotation,Material material,bool collision=false)
        {var g=Geometry.Box(name,transform,position,size,material,collision);g.transform.rotation=rotation;return g;}
    }
}
