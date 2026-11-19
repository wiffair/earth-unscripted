using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        float StationClearanceFloor(List<Vector3> outline,float floor)
        {
            float minX=outline.Min(p=>p.x),maxX=outline.Max(p=>p.x),minZ=outline.Min(p=>p.z),maxZ=outline.Max(p=>p.z);
            foreach(var f in workingMap.features)
                if(f.category=="rail"&&!f.tunnel)foreach(var p in f.points)
                    if(p.x>=minX-3&&p.x<=maxX+3&&p.z>=minZ-3&&p.z<=maxZ+3)floor=Mathf.Max(floor,p.y+Mathf.Max(5.6f,f.railClearanceHeight)+.1f);
            return floor;
        }
        sealed class ControlPlacementCache
        {
            public readonly int side;public readonly Task<TrafficControlLayout.Placement[]> task;
            public ControlPlacementCache(int side,Task<TrafficControlLayout.Placement[]> task){this.side=side;this.task=task;}
        }
        readonly Dictionary<Map,ControlPlacementCache> controlPlacementCache=new Dictionary<Map,ControlPlacementCache>();
        static void ObserveControlPlacementFault(Task<TrafficControlLayout.Placement[]> task)
        {task.ContinueWith(done=>{var observed=done.Exception;},System.Threading.CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
        void UpdateControlPlacementCounters()
        {
            report.controlLayoutCachedMaps=controlPlacementCache.Count;int placements=0;
            foreach(var entry in controlPlacementCache.Values)if(entry.task.Status==TaskStatus.RanToCompletion)placements+=entry.task.Result.Length;
            report.controlLayoutCachedPlacements=placements;
        }
        void ForgetControlPlacements(Map source)
        {
            if(controlPlacementCache.TryGetValue(source,out var entry)){ObserveControlPlacementFault(entry.task);controlPlacementCache.Remove(source);}
            UpdateControlPlacementCounters();
        }
        void ClearControlPlacements()
        {
            foreach(var entry in controlPlacementCache.Values)ObserveControlPlacementFault(entry.task);
            controlPlacementCache.Clear();UpdateControlPlacementCounters();
        }
        Task<TrafficControlLayout.Placement[]> TrafficPlacements(Map source,int side)
        {
            if(controlPlacementCache.TryGetValue(source,out var cached)&&cached.side==side&&!cached.task.IsFaulted&&!cached.task.IsCanceled)
            {report.controlLayoutCacheHits++;UpdateControlPlacementCounters();return cached.task;}
            // A new immutable source occupying the same tile supersedes its old
            // placement snapshot. Never keep the constructor's spatial index.
            foreach(var old in controlPlacementCache.Keys.Where(map=>ReferenceEquals(map,source)||map.tileX==source.tileX&&map.tileZ==source.tileZ).ToArray())ForgetControlPlacements(old);
            var task=Task.Run(()=>new TrafficControlLayout(source,side).Items.ToArray());
            ObserveControlPlacementFault(task);controlPlacementCache.Add(source,new ControlPlacementCache(side,task));report.controlLayoutBuilds++;UpdateControlPlacementCounters();return task;
        }
        IEnumerator CreateTrafficControls(Transform root,Map map,Vector2Int key)
        {
            var seen=new HashSet<string>();
            string country=(map.frame?.country_code??"").ToUpperInvariant().Split('-')[0];
            if(country=="UK")country="GB";
            if(country.Length==0)country=map.frame?.provider=="welsh"||map.frame?.provider=="britain"?"GB":map.frame?.provider=="switzerland"||map.frame?.provider=="swiss-or-coarse"?"CH":"";
            int side=map.frame?.traffic_side=="left"?-1:1;
            var pending=TrafficPlacements(map,side);while(!pending.IsCompleted)yield return null;
            var placements=pending.GetAwaiter().GetResult();UpdateControlPlacementCounters();int created=0;
            foreach(var placed in placements)
            {
                var f=placed.road;var c=placed.control;
                if(c.x<map.coreMinX||c.x>=map.coreMaxX||c.z<map.coreMinZ||c.z>=map.coreMaxZ || Mathf.FloorToInt(c.x/ChunkSize)!=key.x||Mathf.FloorToInt(c.z/ChunkSize)!=key.y||!seen.Add(c.id))continue;
                TrafficControlVisual.Create(root,c,f.width,country,side,p=>RoadSurfacePoint(f,p).y,p=>Ground(p.x,p.z),f.oneway,placed.position);
                if(++created%12==0)yield return null;
            }
        }
    }
    public static class TrafficControlPhase
    {
        public static int Axis(NewbridgeWorld.Control c)=>Mathf.Abs(c.dx)>=Mathf.Abs(c.dz)?0:1;
        // Fixed local simulation, not live signal telemetry. Opposing approaches
        // share green; conflicting axes get an all-red clearance interval.
        public static int State(NewbridgeWorld.Control c,float seconds,string country)
        {
            float phase=Mathf.Repeat(seconds,60);if(Axis(c)==1)phase=Mathf.Repeat(phase+30,60);
            bool redAmber=country=="GB"||country=="CH"||country=="DE";
            if(phase<23)return 2; // green
            if(phase<26)return 1; // amber
            if(redAmber&&phase>=58)return 3;
            return 0;
        }
    }
    public sealed class TrafficControlVisual:MonoBehaviour
    {
        static readonly Dictionary<Color,Material> materials=new Dictionary<Color,Material>();
        NewbridgeWorld.Control control;string country;Renderer[] lamps;int last=-1;
        static void Remove(UnityEngine.Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        internal static Material Mat(Color color)
        {
            if(materials.TryGetValue(color,out var m)&&m)return m;
            var shader=Resources.Load<Shader>("EarthUnscriptedTransportControl");
            if(!shader)throw new InvalidOperationException("Packaged transport-control shader is missing");
            m=EarthUnscripted.GameGraphics.Create(shader, _euMaterial => { _euMaterial.color=color; });materials[color]=m;return m;
        }
        internal static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 scale,Color color)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(parent,false);o.transform.localPosition=pos;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=Mat(color);Remove(o.GetComponent<Collider>());return o;
        }
        internal static void Polygon(Transform parent,int sides,float radius,float z,Color color,float angle=0,bool back=false)
        {
            var v=new Vector3[sides+1];var indices=new int[sides*3];for(int i=0;i<sides;i++){float a=(i*360f/sides+angle)*Mathf.Deg2Rad;v[i+1]=new Vector3(Mathf.Sin(a)*radius,Mathf.Cos(a)*radius,z);int j=i*3;indices[j]=0;indices[j+1]=back?i+1:(i+1)%sides+1;indices[j+2]=back?(i+1)%sides+1:i+1;}
            v[0]=new Vector3(0,0,z);var mesh=new Mesh{name="Transport sign polygon"};mesh.vertices=v;mesh.triangles=indices;mesh.RecalculateNormals();var o=new GameObject("Sign face",typeof(MeshFilter),typeof(MeshRenderer));o.transform.SetParent(parent,false);o.GetComponent<MeshFilter>().sharedMesh=mesh;o.GetComponent<MeshRenderer>().sharedMaterial=Mat(color);
        }
        public static void Create(Transform parent,NewbridgeWorld.Control c,float width,string country,int side,Func<Vector3,float> roadHeight=null,Func<Vector3,float> groundHeight=null,bool oneWay=false,Vector3? roadsidePosition=null)
        {
            var o=new GameObject("OSM "+c.kind+" "+c.id);var lifetime=o.AddComponent<TrafficControlVisual>();o.transform.SetParent(parent,false);var f=c.Forward.normalized;var right=Vector3.Cross(Vector3.up,f);
            o.transform.position=roadsidePosition??(c.Position+right*(width*.5f+1f)*side);o.transform.rotation=Quaternion.LookRotation(-f,Vector3.up);
            if(groundHeight!=null){var placed=o.transform.position;placed.y=groundHeight(placed);o.transform.position=placed;}
            if(c.kind=="crossing")
            {
                // Draw only mapped markings; unknown/unmarked crossings are not
                // silently promoted to zebra crossings with different priority.
                if(c.direction<0||c.crossing=="unknown"||c.crossing=="unmarked"||c.crossing=="no")return;
                o.transform.position=c.Position+Vector3.up*.025f;o.transform.rotation=Quaternion.LookRotation(f,Vector3.up);
                for(float x=-width*.5f+.4f;x<width*.5f;x+=.85f)
                {
                    var world=o.transform.TransformPoint(new Vector3(x,0,0));float y=roadHeight==null?0:roadHeight(world)-c.y;
                    if(c.crossing=="zebra"||c.crossing=="yes")Box(o.transform,"Crossing stripe",new Vector3(x,y,0),new Vector3(.45f,.018f,2.4f),Color.white);
                    else foreach(float z in new[]{-1.2f,1.2f})Box(o.transform,"Crossing studs",new Vector3(x,y,z),new Vector3(.13f,.018f,.13f),Color.white);
                }
                if(country=="GB"&&c.crossing=="zebra")foreach(float x in new[]{-width*.5f-.45f,width*.5f+.45f}){Box(o.transform,"Belisha post",new Vector3(x,1.25f,0),new Vector3(.08f,2.5f,.08f),Color.black);var beacon=GameObject.CreatePrimitive(PrimitiveType.Sphere);beacon.transform.SetParent(o.transform,false);beacon.transform.localPosition=new Vector3(x,2.6f,0);beacon.transform.localScale=Vector3.one*.3f;beacon.GetComponent<Renderer>().sharedMaterial=Mat(new Color(1,.6f,0));Remove(beacon.GetComponent<Collider>());}
                return;
            }
            if(c.kind=="stop"||c.kind=="traffic_signals"||c.kind=="give_way")
            {
                var line=new GameObject("Mapped stop/give-way line");line.transform.SetParent(o.transform,false);line.transform.position=c.Position-f*.4f+right*(oneWay?0:width*.25f*side)+Vector3.up*.025f;line.transform.rotation=Quaternion.LookRotation(f,Vector3.up);
                if(roadHeight!=null){var point=line.transform.position;point.y=roadHeight(point)+.025f;line.transform.position=point;}
                float span=width*(oneWay?1:.5f)-.3f;
                if(c.kind=="give_way")foreach(float z in country=="GB"?new[]{-.15f,.15f}:new[]{0f})for(float x=-span*.5f;x<span*.5f;x+=.7f)Box(line.transform,"Give-way dash",new Vector3(x,0,z),new Vector3(.38f,.012f,.12f),Color.white);
                else Box(line.transform,"Stop line",Vector3.zero,new Vector3(span,.012f,.25f),Color.white);
            }
            var structure=new GameObject("Sign structure");structure.transform.SetParent(o.transform,false);
            Box(structure.transform,"Post",new Vector3(0,1.2f,-.08f),new Vector3(.065f,2.4f,.065f),Color.gray);
            if(c.kind=="traffic_signals")
            {
                Box(o.transform,"Signal housing",new Vector3(0,2.4f,0),new Vector3(.34f,1.05f,.22f),Color.black);
                var visual=lifetime;visual.control=c;visual.country=country;visual.lamps=new Renderer[3];
                for(int i=0;i<3;i++){var l=GameObject.CreatePrimitive(PrimitiveType.Sphere);l.name="Signal lens";l.transform.SetParent(o.transform,false);l.transform.localPosition=new Vector3(0,2.73f-i*.33f,.14f);l.transform.localScale=new Vector3(.23f,.23f,.07f);Remove(l.GetComponent<Collider>());visual.lamps[i]=l.GetComponent<Renderer>();}
                visual.Update();return;
            }
            // +Z is the sign front. Attach the panel to the rear post with a short clamp.
            Box(structure.transform,"Rear mounting clamp",new Vector3(0,2.2f,-.045f),new Vector3(.09f,.12f,.05f),Color.gray);
            var face=StandardTrafficSigns.Place(structure.transform,c.kind,c.value,country);
            if(c.repeater)face.transform.localScale*=c.diameter/.8f;
            structure.AddComponent<EarthUnscripted.RoadSignImpact>();
            var post=structure.AddComponent<BoxCollider>();post.center=new Vector3(0,1.2f,-.08f);post.size=new Vector3(.09f,2.4f,.09f);
            var panel=structure.AddComponent<BoxCollider>();panel.center=new Vector3(0,2.2f,0);panel.size=new Vector3(c.repeater?c.diameter:.8f,c.repeater?c.diameter:.8f,.06f);
        }
        internal static void Arrow(Transform t,Color color){Box(t,"Arrow shaft",new Vector3(0,0,.03f),new Vector3(.5f,.08f,.025f),color);var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.transform.SetParent(t,false);o.transform.localPosition=new Vector3(.18f,.08f,.03f);o.transform.localRotation=Quaternion.Euler(0,0,-45);o.transform.localScale=new Vector3(.23f,.08f,.025f);o.GetComponent<Renderer>().sharedMaterial=Mat(color);Remove(o.GetComponent<Collider>());var p=Instantiate(o,t);p.transform.localPosition=new Vector3(.18f,-.08f,.03f);p.transform.localRotation=Quaternion.Euler(0,0,45);}
        void Update()
        {
            if(control==null)return;
            int state=TrafficControlPhase.State(control,Time.time,country);if(state==last)return;last=state;
            for(int i=0;i<3;i++)lamps[i].sharedMaterial=Mat((state==i||state==3&&i<2)?(i==0?Color.red:i==1?Color.yellow:Color.green):new Color(.045f,.045f,.045f));
        }
    }
}
