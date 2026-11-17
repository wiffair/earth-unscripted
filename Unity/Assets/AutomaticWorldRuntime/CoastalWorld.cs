using System;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        [Serializable] public sealed class CoastRing {public Vector3[] points;}
        [Serializable] public sealed class SeaPolygon {public Vector3[] outline,vertices;public CoastRing[] holes;}
        [Serializable] public sealed class BeachPolygon {public string name,surface,kind;public Vector3[] vertices;}
        [Serializable] public sealed class CoastalPatch {public float centreX,centreZ,radius,seaLevel;public SeaPolygon[] sea;public BeachPolygon[] beaches;public CoastRing[] shore;public bool coastlineFound;}
        CoastalPatch coastalPatch;
        int coastalRevision;
        public bool CoastalBedsReady {get;private set;}
        public bool CoastalSurfaceReady=>coastalRoot&&coastalRoot.activeInHierarchy;
        public int CoastalBeachPixels {get;private set;}
        Texture2D coastalBeachMask;Vector4 coastalMaskBounds;
        public bool CoastalGroundBound=>grass&&grass.HasProperty("_CoastEnabled")&&grass.GetFloat("_CoastEnabled")>.5f;
        public Color MappedBeachAt(float x,float z)=>coastalBeachMask?coastalBeachMask.GetPixelBilinear((x-coastalMaskBounds.x)/coastalMaskBounds.z,(z-coastalMaskBounds.y)/coastalMaskBounds.w):Color.clear;
        GameObject coastalRoot;
        readonly List<Material> coastalMaterials=new List<Material>();
        static bool InCoastRing(Vector3 p,Vector3[] ring)
        {
            if(ring==null||ring.Length<3)return false;bool inside=false;
            for(int i=0,j=ring.Length-1;i<ring.Length;j=i++)
            {var a=ring[i];var b=ring[j];if((a.z>p.z)!=(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;}
            return inside;
        }
        sealed class CoastalRingIndex
        {
            const float Bin=128;readonly Vector3[] ring;readonly Dictionary<int,List<int>> edges=new Dictionary<int,List<int>>();
            public CoastalRingIndex(Vector3[] points)
            {
                ring=points;if(points==null)return;
                for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                {
                    var a=points[i];var b=points[j];if(a.z==b.z)continue;
                    for(int z=Mathf.FloorToInt(Mathf.Min(a.z,b.z)/Bin);z<=Mathf.FloorToInt(Mathf.Max(a.z,b.z)/Bin);z++)
                    {if(!edges.TryGetValue(z,out var list))edges[z]=list=new List<int>();list.Add(i);}
                }
            }
            public bool Contains(Vector3 p)
            {
                if(ring==null||ring.Length<3||!edges.TryGetValue(Mathf.FloorToInt(p.z/Bin),out var candidates))return false;
                bool inside=false;foreach(int i in candidates)
                {var a=ring[i];var b=ring[i==0?ring.Length-1:i-1];if((a.z>p.z)!=(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;}
                return inside;
            }
        }
        readonly Dictionary<Vector3[],CoastalRingIndex> coastalRingIndices=new Dictionary<Vector3[],CoastalRingIndex>();
        bool InIndexedCoastRing(Vector3 point,Vector3[] ring)
        {
            if(ring==null||ring.Length<3)return false;
            if(!coastalRingIndices.TryGetValue(ring,out var index))coastalRingIndices[ring]=index=new CoastalRingIndex(ring);
            return index.Contains(point);
        }
        public bool IsMappedSeaAt(Vector3 point)
        {
            if(coastalPatch?.sea==null)return false;
            foreach(var sea in coastalPatch.sea)
            {
                if(!InIndexedCoastRing(point,sea.outline))continue;bool hole=false;
                foreach(var ring in sea.holes??Array.Empty<CoastRing>())if(InIndexedCoastRing(point,ring.points)){hole=true;break;}
                if(!hole)return true;
            }
            return false;
        }
        public float MappedSeaLevel=>coastalPatch!=null?coastalPatch.seaLevel:float.NegativeInfinity;
        IEnumerator CoastalLoop()
        {
            Vector3 lastBeachCentre=Vector3.positiveInfinity;bool changed=false;
            while(embedded&&ready)
            {
                if(coastalPatch==null||Vector2.Distance(new Vector2(observer.x,observer.z),new Vector2(coastalPatch.centreX,coastalPatch.centreZ))>6000)
                {
                    using(var request=Post("/coast",ObserverRequest()))
                    {
                        request.timeout=180;yield return request.SendWebRequest();
                        if(request.result==UnityWebRequest.Result.Success)
                        {
                            string text=request.downloadHandler.text;
                            var parsing=Task.Run(()=>{var parsed=JsonUtility.FromJson<CoastalPatch>(text);return Tuple.Create(parsed,FinishedGeometryStore.Digest(text),BuildCoastalShoreIndex(parsed));});
                            while(!parsing.IsCompleted)yield return null;
                            if(!parsing.IsFaulted){var patch=parsing.Result.Item1;if(patch!=null&&patch.radius>0){coastalPatch=patch;coastalShoreOwner=patch;coastalShoreIndex=parsing.Result.Item3;coastalRingIndices.Clear();coastalRevision++;sceneryGroundRevision++;coastalVisualSuppressionPending=true;finishedCoastIdentity=patch.shore;finishedCoastHash=parsing.Result.Item2;seaBedHeights.Clear();changed=true;}}
                        }
                    }
                }
                // Paint the actual terrain surface; a separate beach mesh can intersect it.
                if(coastalPatch!=null&&(changed||Vector3.Distance(observer,lastBeachCentre)>1024))
                {
                    bool newCoast=changed;changed=false;lastBeachCentre=observer;
                    if(newCoast){CoastalBedsReady=false;yield return SuppressUnpreparedCoastalTerrain();yield return RefreshCoastalBedsAndPublish(coastalRevision);}
                    CoastalVisualPreviewPhase="water-publication";yield return BuildCoastalSurface(coastalPatch);CoastalVisualPreviewPhase="ready";
                }
                yield return new WaitForSecondsRealtime(5);
            }
        }
        IEnumerator PaintCoastalGround(CoastalPatch patch)
        {
            const int size=2048;const float span=12000;
            var bounds=new Vector4(observer.x-span*.5f,observer.z-span*.5f,span,span);
            bool visible=false;
            foreach(var beach in patch.beaches??Array.Empty<BeachPolygon>())
            {var points=beach.vertices??Array.Empty<Vector3>();for(int i=0;i+2<points.Length;i+=3)
             {var a=points[i];var b=points[i+1];var c=points[i+2];
              if(Mathf.Max(a.x,Mathf.Max(b.x,c.x))>=bounds.x&&Mathf.Min(a.x,Mathf.Min(b.x,c.x))<=bounds.x+span&&Mathf.Max(a.z,Mathf.Max(b.z,c.z))>=bounds.y&&Mathf.Min(a.z,Mathf.Min(b.z,c.z))<=bounds.y+span){visible=true;break;}}
             if(visible)break;}
            if(!visible){if(coastalBeachMask)Destroy(coastalBeachMask);coastalBeachMask=null;CoastalBeachPixels=0;grass.SetFloat("_CoastEnabled",0);yield break;}
            var pixels=new Color32[size*size];int painted=0,work=0;
            foreach(var beach in patch.beaches??Array.Empty<BeachPolygon>())
            {
                var points=beach.vertices??Array.Empty<Vector3>();
                var colour=beach.kind=="sand"?new Color32(255,0,0,255):beach.kind=="pebbles"?new Color32(0,255,0,255):new Color32(0,0,255,255);
                for(int t=0;t+2<points.Length;t+=3)
                {
                    Vector2 Pixel(Vector3 p)=>new Vector2((p.x-bounds.x)*size/span,(p.z-bounds.y)*size/span);
                    var a=Pixel(points[t]);var b=Pixel(points[t+1]);var c=Pixel(points[t+2]);
                    int z0=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,Mathf.Min(b.y,c.y))));
                    int z1=Mathf.Min(size-1,Mathf.CeilToInt(Mathf.Max(a.y,Mathf.Max(b.y,c.y))));
                    for(int z=z0;z<=z1;z++)
                    {
                        float left=float.PositiveInfinity,right=float.NegativeInfinity,y=z+.5f;
                        void Edge(Vector2 p,Vector2 q){if((p.y>y)==(q.y>y))return;float x=p.x+(q.x-p.x)*(y-p.y)/(q.y-p.y);left=Mathf.Min(left,x);right=Mathf.Max(right,x);}
                        Edge(a,b);Edge(b,c);Edge(c,a);
                        if(float.IsInfinity(left))continue;
                        int x0=Mathf.Max(0,Mathf.FloorToInt(left)),x1=Mathf.Min(size-1,Mathf.CeilToInt(right));
                        for(int x=x0;x<=x1;x++){int i=z*size+x;if(pixels[i].a==0)painted++;pixels[i]=colour;}
                        if(++work%256==0)yield return null;
                    }
                }
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name="Mapped sand and pebble ground mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            texture.SetPixels32(pixels);texture.Apply(false,false);
            if(coastalBeachMask)Destroy(coastalBeachMask);coastalBeachMask=texture;coastalMaskBounds=bounds;CoastalBeachPixels=painted;
            grass.SetTexture("_CoastMask",texture);grass.SetVector("_CoastRect",bounds);grass.SetFloat("_CoastEnabled",1);
        }
        IEnumerator BuildCoastalSurface(CoastalPatch patch)
        {
            int previousMaterialCount=coastalMaterials.Count;
            var root=new GameObject("Mapped sea and beaches");root.transform.SetParent(transform,false);root.SetActive(false);
            var shader=Resources.Load<Shader>("CoastalSurface");
            if(!shader){Destroy(root);yield break;}
            Material Surface(string name,float mode){var m=EarthUnscripted.GameGraphics.Create(shader, _euMaterial => { _euMaterial.name=name; });m.SetFloat("_Mode",mode);ApplyRoadWaterMask(m);coastalMaterials.Add(m);return m;}
            var seaVertices=new List<Vector3>();foreach(var polygon in patch.sea??Array.Empty<SeaPolygon>())foreach(var p in polygon.vertices??Array.Empty<Vector3>())seaVertices.Add(new Vector3(p.x,patch.seaLevel,p.z));
            void MeshSurface(string name,List<Vector3> vertices,Material material)
            {
                if(vertices.Count==0)return;
                var indices=new int[vertices.Count];for(int i=0;i+2<indices.Length;i+=3){indices[i]=i;indices[i+1]=i+2;indices[i+2]=i+1;}
                var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                var child=new GameObject(name);child.transform.SetParent(root.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;child.AddComponent<EarthUnscripted.GeneratedMeshOwner>().Mesh=mesh;var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
            MeshSurface("Sea at source datum",seaVertices,Surface("Moving coastal water",0));yield return null;
            yield return PaintCoastalGround(patch);yield return PaintWaterSubstrate();
            var foam=new List<Vector3>();
            foreach(var shore in patch.shore??Array.Empty<CoastRing>())for(int i=1;i<(shore.points?.Length??0);i++)
            {
                var a=shore.points[i-1];var c=shore.points[i];var right=new Vector3(c.z-a.z,0,a.x-c.x).normalized;
                var a0=a+right*.3f;var a1=a+right*1.8f;var c0=c+right*.3f;var c1=c+right*1.8f;
                a0.y=a1.y=c0.y=c1.y=patch.seaLevel+.035f;
                foam.AddRange(new[]{a0,c0,c1,a0,c1,a1});
            }
            MeshSurface("Coastal wash",foam,Surface("Breaking shoreline wash",4));
            for(int i=0;i<previousMaterialCount;i++)if(coastalMaterials[i])Destroy(coastalMaterials[i]);
            coastalMaterials.RemoveRange(0,previousMaterialCount);
            root.SetActive(true);if(coastalRoot){ownedRoots.Remove(coastalRoot);DestroyChunk(coastalRoot);}coastalRoot=root;ownedRoots.Add(root);
        }
    }
}
