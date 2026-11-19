using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Original geometry inspired by the split-window camper's stamped
    /// metal cabin. The reviewed external pack and its integrity remain unchanged.
    /// Only buried window caps in that model are omitted during instantiation.</summary>
    public sealed class CamperCockpit : MonoBehaviour
    {
        public const string VehicleId="earthunscripted.vw-splittie";
        const string ReviewedArchive="4aba3feceabec55e190eaca9e371af689a0320bdf04053443d5fc79baf2f23ca";
        public static bool Supports(VehiclePack pack)=>pack!=null&&pack.Settings.Id==VehicleId&&pack.Settings.Version=="2.0.0"&&string.Equals(pack.ArchiveHash,ReviewedArchive,StringComparison.OrdinalIgnoreCase);
        public int RemovedWindowCapTriangles {get;private set;}
        public int HollowedInteriorTriangles {get;private set;}
        public int TrimmedWindowRevealTriangles {get;private set;}
        public Transform[] WindshieldFrames {get;private set;}
        public Transform[] Quarterlights {get;private set;}
        public int OpenedQuarterlightTriangles {get;private set;}
        public int RemovedBakedWiperComponents {get;private set;}
        public Transform Needle {get;private set;}
        VehicleController car;
        public static bool WindowCap(Vector3 a,Vector3 b,Vector3 c)
        {
            bool InPane(Vector3 p,int side)=>Mathf.Abs(p.z-1.75f)<.0002f&&p.x*side>=.069f&&p.x*side<=.721f&&p.y>=1.424f&&p.y<=1.826f;
            return InPane(a,1)&&InPane(b,1)&&InPane(c,1)||InPane(a,-1)&&InPane(b,-1)&&InPane(c,-1);
        }
        public static int[] OpenWindowCaps(VehiclePackPart part,out int removed)
        {
            removed=0;if(part.Attachment!="body")return part.Triangles;
            var kept=new List<int>(part.Triangles.Length);
            for(int i=0;i<part.Triangles.Length;i+=3)
            {
                int a=part.Triangles[i],b=part.Triangles[i+1],c=part.Triangles[i+2];
                if(WindowCap(part.Vertices[a],part.Vertices[b],part.Vertices[c])){removed++;continue;}
                kept.Add(a);kept.Add(b);kept.Add(c);
            }
            return removed==0?part.Triangles:kept.ToArray();
        }
        struct ClipPoint {public Vector3 p,n;public Vector2 uv;public int original;}
        static VehiclePackPart CopyPart(VehiclePackPart source,int[] triangles)=>new VehiclePackPart{Name=source.Name,Attachment=source.Attachment,Material=source.Material,Vertices=source.Vertices,Normals=source.Normals,UV=source.UV,Triangles=triangles};
        static Vector3 RevealMinimum(int side)=>new Vector3(side<0?-.7451f:side>0 ? .0449f : -.0451f,1.3999f,1.749f);
        static Vector3 RevealMaximum(int side,float front=1.995f)=>new Vector3(side<0?-.0449f:side>0 ? .7451f : .0451f,1.8501f,front);
        public static VehiclePackPart OpenCabin(VehiclePackPart source,out int caps,out int hollowed,out int reveals,out int quarters)
        {
            var part=CopyPart(source,OpenWindowCaps(source,out caps));hollowed=reveals=quarters=0;
            if(source.Attachment!="body"||source.Name!="Seafoam body"&&source.Name!="Warm ivory body")return part;
            part=RemoveVolume(part,new Vector3(-.78f,1.20f,.50f),new Vector3(.78f,1.37f,1.98f),out hollowed);
            if(source.Name=="Warm ivory body")for(int side=-1;side<=1;side++)
            {
                // The original window-cutters made 25cm deep white wells. Keep
                // the exterior skin/front strip, and remove only the buried
                // aperture reveal surfaces, identically for both split panes.
                part=RemoveVolume(part,RevealMinimum(side),RevealMaximum(side),out int removed);reveals+=removed;
            }
            if(source.Name=="Warm ivory body")for(int side=-1;side<=1;side+=2)
            {
                // Open the existing front door window's buried inside cap, then
                // add the missing quarterlight. Roof/beltline and the final
                // curved front post (z>=1.965) remain original solid coachwork.
                var low=new Vector3(side<0?-.8500f:.6490f,1.4024f,.8299f);var high=new Vector3(side<0?-.6490f:.8500f,1.8176f,1.5301f);
                part=RemoveVolume(part,low,high,out int sideReveal);reveals+=sideReveal;
                low=new Vector3(side<0?-.881f:.649f,1.420f,1.549f);high=new Vector3(side<0?-.649f:.881f,1.820f,1.965f);
                part=RemoveVolume(part,low,high,out int opened);quarters+=opened;
            }
            return part;
        }
        public static int OpaqueQuarterlightTriangles(VehiclePackPart part)
        {
            int count=0;for(int side=-1;side<=1;side+=2)
            {
                RemoveVolume(part,new Vector3(side<0?-.880f:.650f,1.421f,1.550f),new Vector3(side<0?-.650f:.880f,1.819f,1.964f),out int remaining);count+=remaining;
            }
            return count;
        }
        public static int RetainedFrontPostTriangles(VehiclePackPart part,int side)
        {
            RemoveVolume(part,new Vector3(side<0?-.880f:.740f,1.43f,1.9651f),new Vector3(side<0?-.740f:.880f,1.81f,2.034f),out int count);return count;
        }
        public static int DeepWindowRevealTriangles(VehiclePackPart part)
        {
            int count=0;for(int side=-1;side<=1;side++){RemoveVolume(part,RevealMinimum(side),RevealMaximum(side,1.9945f),out int remaining);count+=remaining;}return count;
        }
        static VehiclePackPart RemoveVolume(VehiclePackPart source,Vector3 minimum,Vector3 maximum,out int removed)
        {
            removed=0;var triangles=source.Triangles;var part=CopyPart(source,triangles);
            var region=new Bounds((minimum+maximum)*.5f,maximum-minimum);
            var vertices=new List<Vector3>(source.Vertices);var normals=source.Normals==null?null:new List<Vector3>(source.Normals);
            var uv=source.UV==null?null:new List<Vector2>(source.UV);var kept=new List<int>(triangles.Length);
            ClipPoint Original(int i)=>new ClipPoint{p=source.Vertices[i],n=source.Normals==null?Vector3.zero:source.Normals[i],uv=source.UV==null?Vector2.zero:source.UV[i],original=i};
            ClipPoint Mix(ClipPoint a,ClipPoint b,float t)=>new ClipPoint{p=Vector3.LerpUnclamped(a.p,b.p,t),n=Vector3.LerpUnclamped(a.n,b.n,t).normalized,uv=Vector2.LerpUnclamped(a.uv,b.uv,t),original=-1};
            float Distance(ClipPoint p,int plane){int axis=plane/2;return plane%2==0?p.p[axis]-minimum[axis]:maximum[axis]-p.p[axis];}
            void Split(List<ClipPoint> polygon,int plane,out List<ClipPoint> inside,out List<ClipPoint> outside)
            {
                inside=new List<ClipPoint>();outside=new List<ClipPoint>();
                var previous=polygon[polygon.Count-1];float before=Distance(previous,plane);
                foreach(var point in polygon)
                {
                    float after=Distance(point,plane);
                    if((before>=0)!=(after>=0)){var crossing=Mix(previous,point,before/(before-after));inside.Add(crossing);outside.Add(crossing);}
                    if(after>=0)inside.Add(point);else outside.Add(point);previous=point;before=after;
                }
            }
            int Index(ClipPoint point)
            {
                if(point.original>=0)return point.original;int index=vertices.Count;vertices.Add(point.p);normals?.Add(point.n);uv?.Add(point.uv);return index;
            }
            void Publish(List<ClipPoint> polygon)
            {
                if(polygon.Count<3)return;int first=Index(polygon[0]);
                for(int i=1;i<polygon.Count-1;i++)
                {
                    if(Vector3.Cross(polygon[i].p-polygon[0].p,polygon[i+1].p-polygon[0].p).sqrMagnitude<1e-14f)continue;
                    kept.Add(first);kept.Add(Index(polygon[i]));kept.Add(Index(polygon[i+1]));
                }
            }
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=Original(triangles[i]);var b=Original(triangles[i+1]);var c=Original(triangles[i+2]);var bounds=new Bounds(a.p,Vector3.zero);bounds.Encapsulate(b.p);bounds.Encapsulate(c.p);
                if(!region.Intersects(bounds)){kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);continue;}
                var inside=new List<ClipPoint>{a,b,c};var pieces=new List<List<ClipPoint>>();
                for(int plane=0;plane<6&&inside.Count>=3;plane++){Split(inside,plane,out var next,out var outside);if(outside.Count>=3)pieces.Add(outside);inside=next;}
                double area=0;if(inside.Count>=3)for(int j=1;j<inside.Count-1;j++)area+=Vector3.Cross(inside[j].p-inside[0].p,inside[j+1].p-inside[0].p).magnitude;
                if(area<1e-9){kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);continue;}
                removed++;foreach(var piece in pieces)Publish(piece);
            }
            if(removed>0){part.Vertices=vertices.ToArray();part.Normals=normals?.ToArray();part.UV=uv?.ToArray();part.Triangles=kept.ToArray();}
            return part;
        }
        sealed class ConnectedPart
        {
            public readonly HashSet<int> vertices=new HashSet<int>();public readonly HashSet<int> offsets=new HashSet<int>();
            public Bounds bounds;public bool hasBounds;
        }
        sealed class BakedWiper {public Mesh mesh;public ConnectedPart component;public string signature;}
        static List<BakedWiper> BakedWipers(Transform owner)
        {
            var found=new List<BakedWiper>();
            foreach(var filter in owner.GetComponentsInChildren<MeshFilter>())
            {
                bool blade=filter.name=="Rubber body",arm=filter.name=="Chrome body";
                if(!blade&&!arm||!filter.sharedMesh)continue;var mesh=filter.sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;var parents=new int[vertices.Length];
                for(int i=0;i<parents.Length;i++)parents[i]=i;
                int Root(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
                void Join(int a,int b){a=Root(a);b=Root(b);if(a!=b)parents[a]=b;}
                for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
                var components=new Dictionary<int,ConnectedPart>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int root=Root(triangles[i]);if(!components.TryGetValue(root,out var component))components.Add(root,component=new ConnectedPart());
                    component.offsets.Add(i);
                    for(int j=0;j<3;j++)if(component.vertices.Add(triangles[i+j]))
                    {var point=vertices[triangles[i+j]];if(component.hasBounds)component.bounds.Encapsulate(point);else{component.bounds=new Bounds(point,Vector3.zero);component.hasBounds=true;}}
                }
                foreach(var component in components.Values)
                {
                    // Each baked tube is its own connected 16-vertex/28-triangle
                    // primitive in the reviewed pack. Window seals have different
                    // bounds behind z2.05 and can never match these signatures.
                    if(component.vertices.Count!=16||component.offsets.Count!=28)continue;
                    for(int side=-1;side<=1;side+=2)
                    {
                        var minimum=blade?new Vector3(.3283f,1.49812f,2.059f):new Vector3(.17668f,1.40663f,2.056f);
                        var maximum=blade?new Vector3(.6117f,1.56188f,2.083f):new Vector3(.47332f,1.53837f,2.074f);
                        if(side<0){float x=minimum.x;minimum.x=-maximum.x;maximum.x=-x;}
                        if((component.bounds.min-minimum).sqrMagnitude<.000001f&&(component.bounds.max-maximum).sqrMagnitude<.000001f)
                            found.Add(new BakedWiper{mesh=mesh,component=component,signature=(blade?"blade":"arm")+side});
                    }
                }
            }
            return found;
        }
        public static int CountBakedWiperComponents(Transform owner)=>BakedWipers(owner).Count;
        public static void ReplaceBakedWipers(CarVisual visual,Transform[] replacements)
        {
            if(replacements==null||replacements.Length!=2||!replacements[0]||!replacements[1])return;
            var cabin=visual.GetComponentInChildren<CamperCockpit>();if(!cabin||cabin.RemovedBakedWiperComponents!=0)return;
            var found=BakedWipers(visual.transform);var signatures=new HashSet<string>();foreach(var part in found)signatures.Add(part.signature);
            // Fail closed on a revised/unrecognised model. Never partly remove
            // a static assembly or leave a car without its original wipers.
            if(found.Count!=4||signatures.Count!=4)return;
            var byMesh=new Dictionary<Mesh,HashSet<int>>();
            foreach(var part in found){if(!byMesh.TryGetValue(part.mesh,out var offsets))byMesh.Add(part.mesh,offsets=new HashSet<int>());offsets.UnionWith(part.component.offsets);}
            foreach(var entry in byMesh)
            {
                var triangles=entry.Key.triangles;var kept=new List<int>(triangles.Length-entry.Value.Count*3);
                for(int i=0;i<triangles.Length;i+=3)if(!entry.Value.Contains(i)){kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);}
                using var edit=VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(entry.Key);
                entry.Key.triangles=kept.ToArray();entry.Key.RecalculateBounds();
            }
            cabin.RemovedBakedWiperComponents=4;
        }
        public static CamperCockpit Build(Transform parent,Transform steering,int removedCaps,int hollowedTriangles,int trimmedReveals,int openedQuarters)
        {
            var root=new GameObject("Split-window camper cabin");root.transform.SetParent(parent,false);
            var cabin=root.AddComponent<CamperCockpit>();cabin.RemovedWindowCapTriangles=removedCaps;cabin.HollowedInteriorTriangles=hollowedTriangles;cabin.TrimmedWindowRevealTriangles=trimmedReveals;cabin.OpenedQuarterlightTriangles=openedQuarters;
            cabin.Create(steering);return cabin;
        }
        sealed class Batch
        {
            public readonly List<Vector3> v=new List<Vector3>();public readonly List<int> t=new List<int>();public readonly Material material;
            public Batch(Material m){material=m;}
            public int Vertex(Vector3 p){v.Add(p);return v.Count-1;}
            public void Tri(int a,int b,int c){t.Add(a);t.Add(b);t.Add(c);}
            public void Quad(int a,int b,int c,int d){Tri(a,b,c);Tri(a,c,d);}
            public void Box(Vector3 p,Vector3 s)
            {
                int n=v.Count;for(int i=0;i<8;i++)Vertex(p+Vector3.Scale(s*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                Quad(n,n+2,n+3,n+1);Quad(n+4,n+5,n+7,n+6);Quad(n,n+1,n+5,n+4);Quad(n+2,n+6,n+7,n+3);Quad(n,n+4,n+6,n+2);Quad(n+1,n+3,n+7,n+5);
            }
            public void Tube(Vector3 a,Vector3 b,float radius,int segments=8)
            {
                var direction=(b-a).normalized;var u=Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up).normalized;var w=Vector3.Cross(direction,u);
                int n=v.Count;for(int end=0;end<2;end++)for(int i=0;i<segments;i++){float angle=i*Mathf.PI*2/segments;Vertex((end==0?a:b)+radius*(u*Mathf.Cos(angle)+w*Mathf.Sin(angle)));}
                for(int i=0;i<segments;i++){int j=(i+1)%segments;Quad(n+i,n+j,n+segments+j,n+segments+i);}
                int first=Vertex(a),last=Vertex(b);for(int i=0;i<segments;i++){int j=(i+1)%segments;Tri(first,n+j,n+i);Tri(last,n+segments+i,n+segments+j);}
            }
            public void Publish(string name,Transform parent)
            {if(v.Count>0)Geometry.Mesh(name,parent,v.ToArray(),t.ToArray(),new Vector2[v.Count],material,false);}
        }
        void Create(Transform steering)
        {
            var ivory=Geometry.Material("Camper cabin warm enamel",new Color(.72f,.69f,.58f),.12f,.28f);
            var black=Geometry.Material("Camper cabin black bakelite",new Color(.018f,.021f,.019f),.02f,.22f);
            var chrome=Geometry.Material("Camper cabin polished metal",new Color(.49f,.51f,.49f),.8f,.62f);
            var ink=Geometry.Material("Camper instrument markings",new Color(.75f,.71f,.54f),0,.18f);
            var enamel=new Batch(ivory);var dark=new Batch(black);var metal=new Batch(chrome);var markings=new Batch(ink);
            // Rounded stamped dashboard, with real depth rather than a white board.
            const int sections=24;int n=enamel.v.Count;
            for(int side=0;side<2;side++)for(int i=0;i<sections;i++)
            {float angle=i*Mathf.PI*2/sections;enamel.Vertex(new Vector3(side==0?-.805f:.805f,1.285f+Mathf.Sin(angle)*.085f,1.85f+Mathf.Cos(angle)*.165f));}
            for(int i=0;i<sections;i++){int j=(i+1)%sections;enamel.Quad(n+i,n+sections+i,n+sections+j,n+j);}
            int left=enamel.Vertex(new Vector3(-.805f,1.285f,1.85f)),right=enamel.Vertex(new Vector3(.805f,1.285f,1.85f));
            for(int i=0;i<sections;i++){int j=(i+1)%sections;enamel.Tri(left,n+i,n+j);enamel.Tri(right,n+sections+j,n+sections+i);}
            // A slim painted centre divider with its black rubber backing sits
            // behind the unchanged exterior skin, rather than a deep white box.
            dark.Box(new Vector3(0,1.625f,1.998f),new Vector3(.060f,.48f,.014f));
            enamel.Box(new Vector3(0,1.625f,1.985f),new Vector3(.035f,.48f,.012f));
            // Vertical ventilation slots and a simple pressed glovebox outline.
            for(int i=0;i<27;i++)dark.Box(new Vector3(-.39f+i*.019f,1.292f,1.681f),new Vector3(.0075f,.061f,.007f));
            dark.Box(new Vector3(-.59f,1.285f,1.681f),new Vector3(.25f,.064f,.003f));
            enamel.Box(new Vector3(-.59f,1.286f,1.676f),new Vector3(.242f,.055f,.006f));
            metal.Tube(new Vector3(-.70f,1.353f,1.702f),new Vector3(-.70f,1.38f,1.671f),.008f);
            metal.Tube(new Vector3(-.70f,1.38f,1.671f),new Vector3(-.47f,1.38f,1.671f),.008f);
            metal.Tube(new Vector3(-.47f,1.38f,1.671f),new Vector3(-.47f,1.353f,1.702f),.008f);
            // Shelf has an open, shadowed recess below the rolled metal lip.
            dark.Box(new Vector3(0,1.155f,1.93f),new Vector3(1.52f,.09f,.018f));
            enamel.Box(new Vector3(0,1.102f,1.80f),new Vector3(1.56f,.025f,.27f));
            metal.Tube(new Vector3(-.77f,1.12f,1.659f),new Vector3(.77f,1.12f,1.659f),.011f);
            // Single round speedometer: recessed dark face, bezel, cream ticks.
            var gauge=new Vector3(.43f,1.305f,1.663f);
            metal.Tube(gauge+Vector3.forward*.016f,gauge,.087f,48);
            dark.Tube(gauge-Vector3.forward*.001f,gauge-Vector3.forward*.004f,.077f,48);
            for(int tick=0;tick<=40;tick++)
            {
                float angle=(130-tick*6.5f)*Mathf.Deg2Rad;var direction=new Vector3(-Mathf.Sin(angle),Mathf.Cos(angle),0);
                markings.Tube(gauge+direction*(tick%5==0?.060f:.066f)-Vector3.forward*.006f,gauge+direction*.072f-Vector3.forward*.006f,tick%5==0?.0015f:.0008f,5);
            }
            // Odometer window and small warning lamps below the needle spindle.
            markings.Box(gauge+new Vector3(0,-.033f,-.006f),new Vector3(.052f,.012f,.002f));
            dark.Box(gauge+new Vector3(0,-.033f,-.008f),new Vector3(.048f,.009f,.002f));
            for(int digit=0;digit<5;digit++)SevenSegment(markings,gauge+new Vector3((digit-2)*.008f,-.033f,-.010f),.0055f,digit==4?1:0);
            metal.Tube(gauge+new Vector3(0,0,-.009f),gauge+new Vector3(0,0,-.016f),.006f,12);
            for(int i=0;i<2;i++)dark.Tube(new Vector3(.60f+i*.085f,1.278f,1.685f),new Vector3(.60f+i*.085f,1.278f,1.651f),.014f,12);
            // The hollow cabin has a proper floor above the front axle and
            // simple pedals/gear lever, rather than the exterior body's solid top.
            // Overlap the inner door skins and front bulkhead. The old
            // narrow floor left open strips beside both footwells.
            dark.Box(new Vector3(0,.69f,.80f),new Vector3(1.65f,.025f,2.40f));
            for(int pedal=0;pedal<3;pedal++)dark.Box(new Vector3(.25f+pedal*.10f,.79f,1.86f),new Vector3(.055f,.08f,.025f));
            metal.Tube(new Vector3(0,.71f,1.15f),new Vector3(0,1.03f,1.38f),.011f);
            dark.Tube(new Vector3(0,1.02f,1.38f),new Vector3(0,1.065f,1.38f),.023f,12);
            // Window hardware and inside door pulls remain below the apertures.
            for(int side=-1;side<=1;side+=2)
            {
                // Continuous inner door skin joins the floor and beltline. The
                // exterior coachwork is a single outward-facing skin.
                enamel.Box(new Vector3(side*.813f,1.035f,1.30f),new Vector3(.028f,.70f,1.36f));
                dark.Box(new Vector3(side*.790f,.99f,1.27f),new Vector3(.012f,.46f,.63f));
                enamel.Box(new Vector3(side*.812f,1.393f,1.30f),new Vector3(.045f,.025f,1.36f));
                metal.Tube(new Vector3(side*.809f,1.22f,1.46f),new Vector3(side*.792f,1.17f,1.39f),.010f);
                metal.Tube(new Vector3(side*.792f,1.17f,1.39f),new Vector3(side*.809f,1.13f,1.33f),.010f);
            }
            // Join the dashboard roll to the windscreen sill; the exterior
            // skin's back face must not leave a view of the road through it.
            enamel.Box(new Vector3(0,1.38f,1.94f),new Vector3(1.60f,.06f,.12f));
            for(int side=-1;side<=1;side+=2)
                enamel.Box(new Vector3(side*.813f,1.922f,1.30f),new Vector3(.03f,.22f,1.36f));
            // Close the footwell and roof header without covering any glass.
            enamel.Box(new Vector3(0,.951f,1.968f),new Vector3(1.64f,.52f,.034f));
            enamel.Box(new Vector3(0,1.900f,1.995f),new Vector3(1.64f,.080f,.030f));
            for(int side=-1;side<=1;side+=2)
                enamel.Box(new Vector3(side*.772f,1.625f,1.992f),new Vector3(.025f,.47f,.022f));
            enamel.Publish("Rounded ivory dashboard and shelf",transform);dark.Publish("Cabin vents and instrument recesses",transform);metal.Publish("Cabin handles and speedometer bezel",transform);markings.Publish("Speedometer scale and odometer",transform);
            // Identical rounded interior surround geometry for both panes.
            // Exterior A-pillars/roof, opaque front skin and original black
            // window seals are retained; this is a shallow interior trim.
            WindshieldFrames=new Transform[2];
            for(int side=0;side<2;side++)
            {
                var frameRoot=new GameObject(side==0?"Matched split frame left":"Matched split frame right").transform;frameRoot.SetParent(transform,false);
                frameRoot.localPosition=new Vector3(side==0?-.395f:.395f,1.625f,1.995f);WindshieldFrames[side]=frameRoot;
                var trim=new Batch(ivory);var path=new List<Vector3>();const float halfWidth=.35f,halfHeight=.225f,radius=.025f;
                for(int corner=0;corner<4;corner++)
                {
                    var centre=new Vector2(corner==0||corner==3?halfWidth-radius:-halfWidth+radius,corner<2?halfHeight-radius:-halfHeight+radius);
                    for(int step=0;step<=6;step++){float angle=(corner*90+step*15)*Mathf.Deg2Rad;path.Add(new Vector3(centre.x+Mathf.Cos(angle)*radius,centre.y+Mathf.Sin(angle)*radius,0));}
                }
                for(int i=0;i<path.Count;i++)trim.Tube(path[i],path[(i+1)%path.Count],.006f,6);
                trim.Publish("Thin matched windshield surround",frameRoot);
            }
            // The stock mesh omitted both front quarterlights, leaving a half-
            // metre opaque side panel that looked like an oversized A-pillar.
            // Add proper exterior-facing glass and slim seals; do not fake this
            // by making the body transparent from the driver's seat.
            Quarterlights=new Transform[2];
            var sideGlass=Geometry.Material("Camper quarterlight exterior glass",new Color(.055f,.15f,.19f),.45f,.84f);
            if(sideGlass.HasProperty("_Cull"))sideGlass.SetFloat("_Cull",2);
            if(sideGlass.HasProperty("_CullMode"))sideGlass.SetFloat("_CullMode",2);
            if(sideGlass.HasProperty("_CullModeForward"))sideGlass.SetFloat("_CullModeForward",2);
            for(int side=-1;side<=1;side+=2)
            {
                var frame=new GameObject(side<0?"Left front quarterlight":"Right front quarterlight").transform;frame.SetParent(transform,false);Quarterlights[side<0?0:1]=frame;
                var points=new[]{new Vector3(side*.875f,1.424f,1.552f),new Vector3(side*.875f,1.816f,1.552f),new Vector3(side*.798f,1.816f,1.958f),new Vector3(side*.798f,1.424f,1.958f)};
                Geometry.Mesh("Quarterlight exterior glass",frame,points,side>0?new[]{0,1,2,0,2,3}:new[]{0,2,1,0,3,2},new Vector2[4],sideGlass,false);
                var seals=new Batch(black);for(int i=0;i<4;i++)seals.Tube(points[i],points[(i+1)%4],.011f,8);
                seals.Publish("Thin quarterlight rubber seals",frame);
            }
            // A thin, large period steering rim and raked column, not a solid disc.
            var steeringCentre=new Vector3(.43f,1.265f,1.53f);
            // Fixed rake belongs above the rotating pivot, so local Z remains
            // the column axis at every steering angle instead of precessing.
            var mount=new GameObject("Fixed raked steering mount").transform;mount.SetParent(steering.parent,false);
            mount.localPosition=steeringCentre;mount.localRotation=Quaternion.Euler(40,0,0);
            steering.SetParent(mount,false);steering.localPosition=Vector3.zero;
            var wheelRoot=new GameObject("Raked period steering rim").transform;wheelRoot.SetParent(steering,false);
            var rim=new Batch(black);var spokes=new Batch(chrome);const int rimSegments=56;
            for(int i=0;i<rimSegments;i++)
            {
                float a=i*Mathf.PI*2/rimSegments,b=(i+1)*Mathf.PI*2/rimSegments;
                rim.Tube(new Vector3(Mathf.Sin(a)*.231f,Mathf.Cos(a)*.231f,0),new Vector3(Mathf.Sin(b)*.231f,Mathf.Cos(b)*.231f,0),.009f,6);
            }
            for(int i=0;i<3;i++){float a=(i*120+90)*Mathf.Deg2Rad;spokes.Tube(new Vector3(0,0,-.005f),new Vector3(Mathf.Sin(a)*.22f,Mathf.Cos(a)*.22f,0),.006f,6);}
            rim.Tube(new Vector3(0,0,-.027f),new Vector3(0,0,.014f),.033f,24);
            rim.Publish("Thin bakelite steering rim and horn",wheelRoot);spokes.Publish("Three steering spokes",wheelRoot);
            var column=new Batch(black);column.Tube(new Vector3(.43f,.77f,1.92f),steeringCentre,.022f,12);column.Publish("Raked steering column",transform);
            var needleObject=new GameObject("Working camper speedometer needle");needleObject.transform.SetParent(transform,false);needleObject.transform.localPosition=gauge-Vector3.forward*.015f;Needle=needleObject.transform;
            var needleBatch=new Batch(ink);needleBatch.Tube(Vector3.zero,new Vector3(0,.060f,0),.0018f,6);needleBatch.Publish("Speedometer needle",Needle);
        }
        static void SevenSegment(Batch batch,Vector3 p,float height,int digit)
        {
            // Tiny geometric digits never depend on a font atlas/texture rebuild.
            int[] masks={0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f};
            Vector2[] a={new Vector2(-.3f,.5f),new Vector2(.3f,.5f),new Vector2(.3f,0),new Vector2(.3f,-.5f),new Vector2(-.3f,-.5f),new Vector2(-.3f,0),new Vector2(-.3f,0)};
            Vector2[] b={new Vector2(.3f,.5f),new Vector2(.3f,0),new Vector2(.3f,-.5f),new Vector2(-.3f,-.5f),new Vector2(-.3f,0),new Vector2(-.3f,.5f),new Vector2(.3f,0)};
            for(int i=0;i<7;i++)if((masks[digit]&(1<<i))!=0)batch.Tube(p+new Vector3(a[i].x,a[i].y,0)*height,p+new Vector3(b[i].x,b[i].y,0)*height,height*.08f,4);
        }
        void LateUpdate()
        {
            if(!car)car=GetComponentInParent<VehicleController>();
            if(Needle&&car)Needle.localRotation=Quaternion.Euler(0,0,130-Mathf.Clamp(car.AbsoluteSpeed*Core.DrivingMath.MphPerMetreSecond,0,80)/80*260);
        }
    }
}
