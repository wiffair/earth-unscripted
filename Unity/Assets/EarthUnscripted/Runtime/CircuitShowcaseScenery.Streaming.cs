using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class CircuitShowcaseScenery
    {
        // Stage1: resident low-detail context and all collision are retained.
        // Only close visual meshes are streamed. The bounded source plan remains
        // in memory until indexed on-disk pack cells have their own contract.
        public const float DetailLoadMetres=800,DetailRetainMetres=1100;
        const double SliceSeconds=.002;
        sealed class DetailCell
        {
            public Vector2Int Key;public Bounds Bounds;
            public readonly List<CircuitSceneryPlan.Part> Parts=new List<CircuitSceneryPlan.Part>();
            public GameObject Root,High;public LODGroup Group;
            public Renderer[] Low=Array.Empty<Renderer>(),Detailed=Array.Empty<Renderer>();
            public readonly List<Mesh> HighMeshes=new List<Mesh>();
            public bool Loaded,Failed;
        }
        readonly Dictionary<Vector2Int,DetailCell> detailCells=new Dictionary<Vector2Int,DetailCell>();
        readonly Dictionary<string,Material> streamMaterials=new Dictionary<string,Material>();
        readonly List<Vector3> detailObservers=new List<Vector3>();
        CircuitSceneryPlan streamingPlan;GeoWorld streamWorld;bool traceStreaming;
        bool initialDetailReady,pumping;float nextDetailReconcile;
        public int DetailCellCount=>detailCells.Count;
        public int ResidentDetailCells=>detailCells.Values.Count(c=>c.Loaded);
        public int DetailCellsBuilt {get;private set;}
        public int DetailCellsRetired {get;private set;}
        public int ResidentDetailVertices=>detailCells.Values.Sum(c=>c.HighMeshes.Sum(m=>m?m.vertexCount:0));
        public bool InitialDetailReady=>initialDetailReady;
        public int InitialResidentDetailCells {get;private set;}
        public int ResidentCollisionCount=>GetComponentsInChildren<MeshCollider>().Length;
        public double MaximumSliceMilliseconds {get;private set;}
        [Serializable] public struct StreamAction
        { public int frame;public string phase,cell;public double startSeconds,milliseconds;public int gcCollections; }
        const int ActionCapacity=4096;
        readonly Queue<StreamAction> actions=new Queue<StreamAction>();
        public StreamAction[] CaptureActions()=>actions.ToArray();
        void RecordAction(string phase,DetailCell cell,double began)
        {
            if(!traceStreaming)return;
            if(actions.Count==ActionCapacity)actions.Dequeue();
            actions.Enqueue(new StreamAction{frame=Time.frameCount,phase=phase,cell=cell.Key.ToString(),startSeconds=began,milliseconds=(Time.realtimeSinceStartupAsDouble-began)*1000,gcCollections=GC.CollectionCount(0)});
        }
        double sliceBegan;
        void BeginSlice(){sliceBegan=Time.realtimeSinceStartupAsDouble;}
        bool SliceExpired()
        {
            double seconds=Time.realtimeSinceStartupAsDouble-sliceBegan;
            MaximumSliceMilliseconds=Math.Max(MaximumSliceMilliseconds,seconds*1000);
            return seconds>=SliceSeconds;
        }
        void InitializeStreaming(CircuitSceneryPlan plan)
        {
            traceStreaming=Array.IndexOf(Environment.GetCommandLineArgs(),"-showcaseFrameTiming")>=0;
            streamingPlan=plan;streamWorld=GetComponentInParent<WorldBuilder>().GeographicWorld;
            var colours=new[]{new Color(.53f,.54f,.52f),new Color(.43f,.47f,.5f),new Color(.65f,.68f,.7f),new Color(.24f,.38f,.46f),new Color(.08f,.24f,.53f),new Color(.65f,.06f,.045f),new Color(.87f,.88f,.86f),new Color(.055f,.065f,.075f),new Color(.12f,.13f,.14f),new Color(.55f,.49f,.39f),new Color(.15f,.31f,.17f),new Color(.9f,.67f,.08f)};
            for(int i=0;i<colours.Length;i++)
            {
                string key=CircuitSceneryPlan.Palette[i];
                var material=CircuitOwnedMaterials.Create("Showcase "+key,colours[i],key=="steel"||key=="roof"?.7f:key=="glass"?.15f:0,key=="glass"?.85f:key=="steel"?.38f:.23f);
                if(key=="asphalt")ApplyAsphalt(material);if(key=="concrete")ApplyConcrete(material);
                owned.Add(material);streamMaterials.Add(key,material);
            }
        }
        static Bounds PartBounds(CircuitSceneryPlan.Part part)
        {
            var bounds=new Bounds(part.Vertices==null?part.Position:part.Vertices[0],Vector3.zero);
            if(part.Vertices!=null){foreach(var p in part.Vertices)bounds.Encapsulate(p);}
            else
            {
                var rotation=Quaternion.Euler(0,part.Yaw,0);
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                    bounds.Encapsulate(part.Position+rotation*Vector3.Scale(part.Size,new Vector3(x,y,z)*.5f));
            }
            return bounds;
        }
        public static float HorizontalDistance(Bounds bounds,Vector3 point)
        {
            float x=Mathf.Max(Mathf.Max(bounds.min.x-point.x,0),point.x-bounds.max.x);
            float z=Mathf.Max(Mathf.Max(bounds.min.z-point.z,0),point.z-bounds.max.z);
            return Mathf.Sqrt(x*x+z*z);
        }
        public static bool ShouldHoldDetail(Bounds bounds,IReadOnlyList<Vector3> observers,bool alreadyResident)
        {
            float radius=alreadyResident?DetailRetainMetres:DetailLoadMetres;
            for(int i=0;i<observers.Count;i++)if(HorizontalDistance(bounds,observers[i])<=radius)return true;
            return false;
        }
        float ObserverDistance(DetailCell cell)
        {
            float distance=float.PositiveInfinity;
            for(int i=0;i<detailObservers.Count;i++)distance=Mathf.Min(distance,HorizontalDistance(cell.Bounds,detailObservers[i]));
            return distance;
        }
        GameObject Child(string name,Transform parent){var child=new GameObject(name);child.transform.SetParent(parent,false);return child;}
        Renderer RendererFor(string name,Transform parent,string material,Batch batch,List<Mesh> tracked=null)
        {
            var child=Child(name,parent);var mesh=MakeMesh(name,batch);tracked?.Add(mesh);
            child.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=streamMaterials[material];return renderer;
        }
        void SetCellLods(DetailCell cell)
        {
            cell.Group.SetLODs(cell.Loaded?new[]{new LOD(.07f,cell.Detailed),new LOD(.001f,cell.Low)}:new[]{new LOD(.001f,cell.Low)});
            cell.Group.RecalculateBounds();
        }
        public IEnumerator PrepareInitialDetail(Vector3 start,Func<bool> cancelled,Action<string> status)
        {
            if(streamingPlan==null||initialDetailReady)yield break;
            status?.Invoke("Indexing circuit scenery…");BeginSlice();
            foreach(var part in streamingPlan.Parts)
            {
                if(cancelled())yield break;
                var bounds=PartBounds(part);var key=new Vector2Int(Mathf.FloorToInt(bounds.center.x/CellMetres),Mathf.FloorToInt(bounds.center.z/CellMetres));
                if(!detailCells.TryGetValue(key,out var cell)){cell=new DetailCell{Key=key,Bounds=bounds};detailCells.Add(key,cell);}else cell.Bounds.Encapsulate(bounds);
                cell.Parts.Add(part);
                if(SliceExpired()){yield return null;BeginSlice();}
            }
            // All static collision exists before actors are admitted. Detail
            // retirement never disables a wall for an opponent or LAN peer.
            status?.Invoke("Preparing circuit collision…");
            foreach(var part in streamingPlan.Parts)
            {
                if(cancelled())yield break;
                if(part.Collision)
                {
                    var batch=new Batch();Append(part,batch);var root=Child("Collision "+part.Name,transform);
                    root.AddComponent<MeshCollider>().sharedMesh=MakeMesh(part.Name+" collision",batch);
                }
                if(SliceExpired()){yield return null;BeginSlice();}
            }
            foreach(var cell in detailCells.Values)
            {
                if(cancelled())yield break;
                status?.Invoke("Preparing distant circuit scenery…");
                cell.Root=Child("Circuit art cell "+cell.Key,transform);cell.Group=cell.Root.AddComponent<LODGroup>();
                var batches=new Dictionary<string,Batch>();
                foreach(var part in cell.Parts)
                {
                    if(part.Lod!=0){if(!batches.TryGetValue(part.Material,out var batch)){batch=new Batch();batches.Add(part.Material,batch);}Append(part,batch);}
                    if(SliceExpired()){yield return null;BeginSlice();}
                }
                var renderers=new List<Renderer>();
                foreach(var batch in batches)
                {
                    renderers.Add(RendererFor("LOD1 "+batch.Key,cell.Root.transform,batch.Key,batch.Value));
                    if(SliceExpired()){yield return null;BeginSlice();}
                }
                cell.Low=renderers.ToArray();SetCellLods(cell);
            }
            detailObservers.Clear();detailObservers.Add(start);
            foreach(var cell in detailCells.Values.OrderBy(ObserverDistance))
            {
                if(cancelled())yield break;
                if(!ShouldHoldDetail(cell.Bounds,detailObservers,false))continue;
                status?.Invoke("Preparing nearby circuit detail…");yield return BuildDetailed(cell,cancelled);
            }
            InitialResidentDetailCells=ResidentDetailCells;initialDetailReady=true;
        }
        IEnumerator BuildDetailed(DetailCell cell,Func<bool> cancelled)
        {
            cell.High=Child("Resident close detail",cell.Root.transform);cell.High.SetActive(false);
            var batches=new Dictionary<string,Batch>();var renderers=new List<Renderer>();bool committed=false;BeginSlice();
            try
            {
                foreach(var part in cell.Parts)
                {
                    if(cancelled())yield break;
                    if(part.Lod!=1){if(!batches.TryGetValue(part.Material,out var batch)){batch=new Batch();batches.Add(part.Material,batch);}Append(part,batch);}
                    if(SliceExpired()){yield return null;BeginSlice();}
                }
                foreach(var batch in batches)
                {
                    if(cancelled())yield break;
                    double uploadBegan=Time.realtimeSinceStartupAsDouble;
                    renderers.Add(RendererFor("LOD0 "+batch.Key,cell.High.transform,batch.Key,batch.Value,cell.HighMeshes));
                    RecordAction("detail-upload",cell,uploadBegan);
                    if(SliceExpired()){yield return null;BeginSlice();}
                }
                double commitBegan=Time.realtimeSinceStartupAsDouble;
                cell.Detailed=renderers.ToArray();cell.Loaded=true;cell.High.SetActive(true);SetCellLods(cell);DetailCellsBuilt++;committed=true;
                RecordAction("detail-commit",cell,commitBegan);
            }
            finally{if(!committed)ReleaseDetail(cell,false);}
        }
        void ReleaseDetail(DetailCell cell,bool count)
        {
            double retireBegan=Time.realtimeSinceStartupAsDouble;
            cell.Loaded=false;cell.Detailed=Array.Empty<Renderer>();if(cell.Group)SetCellLods(cell);
            if(cell.High){cell.High.SetActive(false);ReleaseObject(cell.High);cell.High=null;}
            foreach(var mesh in cell.HighMeshes){owned.Remove(mesh);if(mesh)ReleaseObject(mesh);}cell.HighMeshes.Clear();
            if(count)DetailCellsRetired++;RecordAction("detail-retire",cell,retireBegan);
        }
        void ReadObservers()
        {
            detailObservers.Clear();if(streamWorld)
            {
                if(streamWorld.Observer)detailObservers.Add(streamWorld.Observer.position);
                if(streamWorld.SecondaryObserver)detailObservers.Add(streamWorld.SecondaryObserver.position);
                foreach(var observer in streamWorld.RaceObservers)if(observer)detailObservers.Add(observer.position);
            }
            var camera=Camera.main;if(camera&&camera.isActiveAndEnabled)detailObservers.Add(camera.transform.position);
        }
        void Update()
        {
            if(!initialDetailReady||pumping||Time.unscaledTime<nextDetailReconcile)return;
            nextDetailReconcile=Time.unscaledTime+.25f;ReadObservers();if(detailObservers.Count==0)return;
            // A teleport can invalidate many cells. Retire at most one per
            // reconciliation rather than destroying a whole window in one frame.
            DetailCell retire=null,next=null;float farthest=DetailRetainMetres,nearest=DetailLoadMetres;
            foreach(var cell in detailCells.Values)
            {
                float distance=ObserverDistance(cell);
                if(cell.Loaded&&distance>farthest){retire=cell;farthest=distance;}
                else if(!cell.Loaded&&!cell.Failed&&distance<=nearest){next=cell;nearest=distance;}
            }
            if(retire!=null)ReleaseDetail(retire,true);
            if(next!=null)StartCoroutine(PumpDetail(next));
        }
        IEnumerator PumpDetail(DetailCell cell)
        {
            pumping=true;var job=BuildDetailed(cell,()=>!this||!cell.Root||!ShouldHoldDetail(cell.Bounds,detailObservers,true));
            try
            {
                while(true)
                {
                    bool more=false;object current=null;Exception failure=null;
                    try{more=job.MoveNext();if(more)current=job.Current;}catch(Exception error){failure=error;}
                    if(failure!=null){cell.Failed=true;Debug.LogError("Circuit detail kept its coarse fallback: "+failure);yield break;}
                    if(!more)yield break;yield return current;
                }
            }
            finally{(job as IDisposable)?.Dispose();pumping=false;}
        }
    }
}
