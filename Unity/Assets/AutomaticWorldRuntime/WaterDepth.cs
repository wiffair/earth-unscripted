using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        readonly Dictionary<Vector2Int,float> seaBedHeights=new Dictionary<Vector2Int,float>();
        public bool TryWaterSurface(Vector3 point,out float height,out bool sea)
        {
            sea=IsMappedSeaAt(point);height=sea?MappedSeaLevel:float.NegativeInfinity;
            if(TryMappedWaterLevel(point,out float mapped))height=Mathf.Max(height,mapped);
            var nearby=Nearby(point.x,point.z);
            if(nearby!=null)foreach(var segment in nearby)
                if(segment.feature.category=="water"&&Project(segment,point.x,point.z,out float surface)<segment.feature.width*.5f)
                    height=Mathf.Max(height,surface);
            return !float.IsNegativeInfinity(height);
        }
        /// <summary>Inferred seabed, not bathymetric survey data. A gentle shore
        /// profile deepens offshore. Road earthworks retain their own support;
        /// bridges retain decks while the bed beneath them can deepen.</summary>
        float LowerSeaBed(float x,float z,float ground)
        {
            if(coastalPatch==null)return ground;
            if(!IsMappedSeaAt(new Vector3(x,0,z)))return ConstrainCoastalRoadWedge(x,z,ground);
            var key=new Vector2Int(Mathf.RoundToInt(x/TerrainStep),Mathf.RoundToInt(z/TerrainStep));
            if(!seaBedHeights.TryGetValue(key,out float bed))
            {
                float nearest=CoastalShoreDistance(x,z);
                bed=MappedSeaLevel-Mathf.Clamp(.08f+nearest*.11f,.08f,28);
                if(seaBedHeights.Count<600000)seaBedHeights[key]=bed;
            }
            return Mathf.Min(ground,bed);
        }
        bool coastalVisualSuppressionPending;
        public string CoastalVisualPreviewPhase{get;private set;}="waiting-coast";
        public int CoastalVisualChunksProcessed{get;private set;}
        public int CoastalVisualChunksTotal{get;private set;}
        readonly List<System.Threading.Tasks.Task> pendingCoastalBakes=new List<System.Threading.Tasks.Task>();
        readonly List<Mesh> pendingCoastalPreviews=new List<Mesh>();
        int coastalBedBakeFailures;
        IEnumerator RefreshCoastalBedsAndPublish(int revision)
        {
            int before=coastalBedBakeFailures;yield return RefreshCoastalBeds();
            if(revision==coastalRevision)CoastalBedsReady=before==coastalBedBakeFailures;
        }
        readonly Dictionary<MeshRenderer,bool> coastalHiddenTerrain=new Dictionary<MeshRenderer,bool>();
        void RestoreCoastalTerrain(MeshRenderer renderer)
        {
            if(!renderer||!coastalHiddenTerrain.TryGetValue(renderer,out bool enabled))return;
            renderer.enabled=enabled;coastalHiddenTerrain.Remove(renderer);fineTerrainVisibilityRevision++;sceneryGroundRevision++;
        }
        IEnumerator SuppressUnpreparedCoastalTerrain()
        {
            CoastalVisualPreviewPhase="suppressing-old-sea";
            foreach(var retired in new List<MeshRenderer>(coastalHiddenTerrain.Keys))if(!retired)coastalHiddenTerrain.Remove(retired);
            var roots=new List<GameObject>(chunks.Values);foreach(var warm in warmChunks.Values)if(warm.root)roots.Add(warm.root);
            var slice=System.Diagnostics.Stopwatch.StartNew();
            foreach(var root in roots)
            {
                if(!root)continue;var child=root.transform.Find("Terrain");if(!child)continue;
                var renderer=child.GetComponent<MeshRenderer>();if(!renderer||!renderer.enabled)continue;
                var bounds=renderer.bounds;bool allSea=true;
                for(int z=0;z<3&&allSea;z++)for(int x=0;x<3&&allSea;x++)allSea=IsMappedSeaAt(new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,x*.5f),0,Mathf.Lerp(bounds.min.z,bounds.max.z,z*.5f)));
                if(allSea){coastalHiddenTerrain[renderer]=renderer.enabled;renderer.enabled=false;fineTerrainVisibilityRevision++;sceneryGroundRevision++;}
                if(slice.Elapsed.TotalMilliseconds>1.5){yield return null;slice.Restart();}
            }
            coastalVisualSuppressionPending=false;
        }
        IEnumerator RefreshCoastalBeds()
        {
            CoastalVisualPreviewPhase="fine-visual-previews";
            // Surface/collider publication is atomic per terrain mesh. The old
            // supported collider remains active throughout asynchronous cooking.
            var roots=new List<GameObject>(chunks.Values);
            foreach(var warm in warmChunks.Values)if(warm.root)roots.Add(warm.root);
            CoastalVisualChunksProcessed=0;CoastalVisualChunksTotal=roots.Count;
            roots.Sort((a,b)=>
            {
                float Distance(GameObject value){if(!value)return float.PositiveInfinity;var r=value.GetComponentInChildren<MeshRenderer>();return r?r.bounds.SqrDistance(observer):float.PositiveInfinity;}
                return Distance(a).CompareTo(Distance(b));
            });
            foreach(var root in roots)
            {
                CoastalVisualChunksProcessed++;
                if(!root)continue;
                var child=root.transform.Find("Terrain");if(!child)continue;
                var filter=child.GetComponent<MeshFilter>();var collider=child.GetComponent<MeshCollider>();
                if(!filter||!filter.sharedMesh||!collider)continue;
                var terrainRenderer=child.GetComponent<MeshRenderer>();
                var original=filter.sharedMesh;var vertices=original.vertices;bool changed=false;var slice=System.Diagnostics.Stopwatch.StartNew();
                bool retired=false;var toWorld=child.localToWorldMatrix;var toLocal=child.worldToLocalMatrix;
                for(int index=0;index<vertices.Length;index++)
                {
                    // Geometry cannot retire in the middle of this main-thread
                    // slice. Check ownership after yields, not via native mesh
                    // property lookups for every sampled vertex.
                    var point=toWorld.MultiplyPoint3x4(vertices[index]);
                    float height=IsMappedSeaAt(point)?Ground(point.x,point.z):ConstrainCoastalRoadWedge(point.x,point.z,point.y);
                    if(point.y>height+.01f){point.y=height;vertices[index]=toLocal.MultiplyPoint3x4(point);changed=true;}
                    // Complex shoreline samples can be costly individually.
                    // Waiting16vertices produced a measured21ms generation slice.
                    if(slice.Elapsed.TotalMilliseconds>1.5)
                    {
                        RecordGenerationSlice("Coastal bed samples",slice.Elapsed.TotalMilliseconds);yield return null;
                        if(!root||!child||!filter||!collider||filter.sharedMesh!=original){retired=true;break;}
                        toWorld=child.localToWorldMatrix;toLocal=child.worldToLocalMatrix;slice.Restart();
                    }
                }
                if(retired)continue;
                if(!changed){RestoreCoastalTerrain(terrainRenderer);continue;}
                var replacement=UnityEngine.Object.Instantiate(original);replacement.name=original.name+" seabed";
                replacement.vertices=vertices;replacement.RecalculateNormals();replacement.RecalculateBounds();
                // The corrected visual is already complete. Publish it once,
                // retaining the old collider until its replacement is cooked.
                // Preview meshes stay unowned by the chunk while the worker
                // reads them, so retiring that chunk cannot free a bake input.
                filter.sharedMesh=replacement;pendingCoastalPreviews.Add(replacement);RestoreCoastalTerrain(terrainRenderer);sceneryGroundRevision++;
                var id=replacement.GetEntityId();var options=collider.cookingOptions;
                var bake=System.Threading.Tasks.Task.Run(()=>Physics.BakeMesh(id,false,options));pendingCoastalBakes.Add(bake);
                while(!bake.IsCompleted)yield return null;
                pendingCoastalBakes.Remove(bake);pendingCoastalPreviews.Remove(replacement);
                if(bake.IsFaulted)
                {
                    coastalBedBakeFailures++;
                    if(filter&&filter.sharedMesh==replacement)filter.sharedMesh=original;
                    if(terrainRenderer){coastalHiddenTerrain[terrainRenderer]=true;terrainRenderer.enabled=false;fineTerrainVisibilityRevision++;sceneryGroundRevision++;}
                    Destroy(replacement);continue;
                }
                if(root&&child&&filter&&collider&&filter.sharedMesh==replacement){collider.sharedMesh=replacement;sceneryGroundRevision++;var owner=filter.GetComponent<EarthUnscripted.GeneratedMeshOwner>()??filter.gameObject.AddComponent<EarthUnscripted.GeneratedMeshOwner>();owner.Mesh=replacement;Destroy(original);}
                else Destroy(replacement);
                yield return null;
            }
        }
    }
}
