using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GeoWorld
    {
        // Local offline previews have explicit user selection. LAN remains refused until
        // shared collision/session identity negotiation is implemented.
        sealed class BuildingReplacement
        {
            public Vector2Int Key;
            public GameObject Chunk, Root;
            public IEnumerator<int> Work;
            public bool Reused;
        }
        sealed class BuildingWindow
        {
            public GeoRegion Region;
            public int Generation;
            public string Identity;
            public bool IsBaseline;
            public BuildingRenderInput.Plan Inputs;
            public readonly Dictionary<Vector2Int,List<GeoBuilding>> Index = new Dictionary<Vector2Int,List<GeoBuilding>>();
            public readonly Dictionary<string,BuildingSitePlan.Data> Sites = new Dictionary<string,BuildingSitePlan.Data>();
            public readonly Dictionary<string,string> SiteOwners=new Dictionary<string,string>();
            public readonly Dictionary<Vector2Int,BuildingReplacement> Ready = new Dictionary<Vector2Int,BuildingReplacement>();
            public BuildingReplacement Preparing;
            public readonly Dictionary<Vector2Int,string> Signatures=new Dictionary<Vector2Int,string>();
        }
        readonly Dictionary<GameObject,string> buildingChildSignatures=new Dictionary<GameObject,string>();
        BuildingWindow pendingBuildings;
        public int LastBuildingWindowReusedChunks {get;private set;}
        public int LastBuildingWindowRebuiltChunks {get;private set;}
        string BuildingFingerprint(Vector2Int key,BuildingRenderInput.Plan inputs,Dictionary<Vector2Int,List<GeoBuilding>> index)
        {
            var owned=index.TryGetValue(key,out var group)?group.Where(b=>Key(Centre(b.points))==key).ToArray():Array.Empty<GeoBuilding>();
            var nearby=new HashSet<GeoBuilding>();
            // Facade clearances can cross owner-cell boundaries and whole-footprint
            // overhangs. Include every source intersecting the geometric probe reach.
            foreach(var building in owned)
            {
                // Current facade/access probes reach at most3.5m from an outline.
                // A roughly4m geometric halo covers them without invalidating an entire
                // 320m neighbour cell when unrelated distant buildings enter it.
                const float maximumAccessHalfWidth=1.5f;
                float neighbourReach=Mathf.Sqrt(BuildingAccessGeometry.MaximumReach*BuildingAccessGeometry.MaximumReach+maximumAccessHalfWidth*maximumAccessHalfWidth)+.2f;
                float minX=building.points.Min(p=>p.x)-neighbourReach,maxX=building.points.Max(p=>p.x)+neighbourReach;
                float minZ=building.points.Min(p=>p.z)-neighbourReach,maxZ=building.points.Max(p=>p.z)+neighbourReach;
                var westSouth=Key(new Vector3(minX,0,minZ));var eastNorth=Key(new Vector3(maxX,0,maxZ));
                for(int z=westSouth.y;z<=eastNorth.y;z++)for(int x=westSouth.x;x<=eastNorth.x;x++)
                    if(index.TryGetValue(new Vector2Int(x,z),out var neighbors))foreach(var neighbor in neighbors)
                        if(neighbor.points.Max(p=>p.x)>=minX&&neighbor.points.Min(p=>p.x)<=maxX&&neighbor.points.Max(p=>p.z)>=minZ&&neighbor.points.Min(p=>p.z)<=maxZ)nearby.Add(neighbor);
            }
            string Entry(GeoBuilding building)
            {if(!inputs.TryGet(building,out var entry))throw new InvalidDataException("Building fingerprint lacks source mapping");return building.id+"="+entry.ContentKey;}
            string rows="RoadRushBuildingChunk1|"+string.Join("|",owned.Select(Entry).OrderBy(v=>v,StringComparer.Ordinal))+"|neighbors|"+string.Join("|",nearby.Select(Entry).OrderBy(v=>v,StringComparer.Ordinal));
            return Core.DataPackageRules.Hash(System.Text.Encoding.UTF8.GetBytes(rows));
        }
        string DesiredBuildingSignature(BuildingWindow window,Vector2Int key)
        {if(!window.Signatures.TryGetValue(key,out var signature))window.Signatures[key]=signature=BuildingFingerprint(key,window.Inputs,window.Index);return signature;}

        public bool BuildingWindowBusy => pendingBuildings != null;
        public int PreparedBuildingWindowChunks=>pendingBuildings?.Ready.Count??0;
        public float MaximumBuildingWindowSliceMilliseconds {get;private set;}
        public void ResetBuildingWindowSliceMetrics()=>MaximumBuildingWindowSliceMilliseconds=0;
        public string PendingBuildingWindowIdentity=>pendingBuildings?.Identity;
        public string BuildingWindowStatus {get;private set;} = "Default scenery";
        public string BuildingWindowIdentity {get;private set;}
        const float BuildingWindowSliceMilliseconds = 2;

        public void QueueBuildingWindowPreview(GeoRegion expectedRegion, GeoBuilding[] sources, string identity)
        {
            var session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
            if(Application.isEditor || Application.companyName != "RoadRush Verification"
                || Application.identifier != "games.blackwood.roadrush.qa.loadingcamera"
                || string.IsNullOrEmpty(PlayerProfile.StorageOverride) || !session || session.Network.Active)
                throw new InvalidOperationException("Building-window preview requires isolated offline native QA");
            QueueBuildingWindow(session,expectedRegion,sources,identity);
        }
        internal void QueueDefaultBuildings(GameSession owner)
        {
            string identity=Core.DataPackageRules.Hash(System.Text.Encoding.UTF8.GetBytes("RoadRushDefaultBuildings1|"+JsonUtility.ToJson(Region)));
            QueueBuildingWindow(owner,Region,Region.buildings,identity,true);
        }
        internal void QueueBuildingWindow(GameSession owner,GeoRegion expectedRegion,GeoBuilding[] sources,string identity,bool isBaseline=false)
        {
            if(!owner || !owner.BootReady || owner.Network.Active || !ReferenceEquals(owner.World.GeographicWorld,this))
                throw new InvalidOperationException("Local pack scenery requires its ready offline world");
            if(!ReferenceEquals(expectedRegion,Region) || IsScopedTown800 || Region.circuit)
                throw new InvalidOperationException("Building window belongs to another world");
            if(identity == null || identity.Length != 64 || identity.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                throw new InvalidDataException("Building window identity required");
            var inputs = BuildingRenderInput.Create(sources);
            if(inputs.Entries.Any(entry => (entry.Diagnostics & BuildingRenderInput.Flags.UnsupportedBudget) != 0))
                throw new InvalidDataException("Building window exceeds renderer budget; current scenery retained");
            var window = new BuildingWindow { Region=Region, Generation=generation, Identity=identity, IsBaseline=isBaseline, Inputs=inputs };
            foreach(var input in inputs.Renderable)Index(window.Index,input.Geometry,input.Geometry.points);
            // Carry only exact unchanged base-site records into the detached cache.
            // A same-ID building with changed geometry/height must be re-evaluated.
            var existing=BuildingInputs.Renderable.ToDictionary(entry=>entry.Geometry.id,StringComparer.Ordinal);
            var unchangedSources=new HashSet<string>(inputs.Renderable.Where(entry=>existing.TryGetValue(entry.Geometry.id,out var prior)&&prior.ContentKey==entry.ContentKey).Select(entry=>entry.Geometry.id),StringComparer.Ordinal);
            foreach(var siteOwner in buildingSiteOwners)
                if(unchangedSources.Contains(siteOwner.Value)&&buildingSites.TryGetValue(siteOwner.Key,out var site))
                {window.Sites[siteOwner.Key]=site;window.SiteOwners[siteOwner.Key]=siteOwner.Value;}
            ResetBuildingWindow();pendingBuildings=window;BuildingWindowStatus="Preparing nearby building scenery…";
        }

        IEnumerable<int> ReplacementBuildingSteps(BuildingReplacement replacement,BuildingWindow window)
        {
            if(window.Index.TryGetValue(replacement.Key,out var group))
                foreach(var building in group)
                    if(Key(Centre(building.points))==replacement.Key)
                        foreach(var step in BuildingSteps(replacement.Root.transform,building))yield return step;
            replacement.Root.GetComponent<BuildingAccessChunk>()?.Commit();
        }
        bool AdvanceBuildingReplacement(BuildingWindow window,BuildingReplacement replacement)
        {
            // A MoveNext is synchronous on the main thread. Swap only the renderer's
            // neighbour/site context during that slice; road-bank exclusions stay fixed.
            var previousIndex=buildings;var previousSites=buildingSites;var previousOwners=buildingSiteOwners;
            try { buildings=window.Index;buildingSites=window.Sites;buildingSiteOwners=window.SiteOwners;return replacement.Work.MoveNext(); }
            finally { buildings=previousIndex;buildingSites=previousSites;buildingSiteOwners=previousOwners; }
        }
        public void CancelBuildingWindowPreview()
        {ResetBuildingWindow();BuildingWindowStatus="Building preparation cancelled; current scenery retained";}
        void ResetBuildingWindow()
        {
            var window=pendingBuildings;pendingBuildings=null;if(window==null)return;
            var cleanup=new List<BuildingReplacement>(window.Ready.Values);
            if(window.Preparing!=null)cleanup.Add(window.Preparing);
            // Cleanup is best-effort for every owned child, even if one iterator or
            // engine object refuses disposal. A failed item cannot skip its siblings.
            foreach(var item in cleanup)
            {
                if(item.Reused)continue;
                try{item.Work?.Dispose();}catch(Exception error){Debug.LogWarning("Building iterator cleanup: "+error.Message);}
                ReleaseBuildingChild(item.Root);
            }
        }
        void ReleaseBuildingChild(GameObject root)
        {
            if(!root)return;buildingChildSignatures.Remove(root);
            try{Release(root);}
            catch(Exception error)
            {
                Debug.LogWarning("Building child cleanup: "+error.Message);
                try{root.SetActive(false);Destroy(root);}catch(Exception fallback){Debug.LogWarning("Building child destruction: "+fallback.Message);}
            }
        }
        void PumpBuildingWindow()
        {
            var window=pendingBuildings;if(window==null)return;
            if(window.Generation!=generation || !ReferenceEquals(window.Region,Region))
            {ResetBuildingWindow();BuildingWindowStatus="World changed; previous scenery retained";return;}
            long began=Timestamp();
            try
            {
                foreach(var key in window.Ready.Keys.ToArray())
                    if(!loaded.TryGetValue(key,out var owner)||owner!=window.Ready[key].Chunk)
                    {if(!window.Ready[key].Reused)ReleaseBuildingChild(window.Ready[key].Root);window.Ready.Remove(key);}
                do
                {
                    var item=window.Preparing;
                    if(item!=null && (!item.Root || !loaded.TryGetValue(item.Key,out var current)||current!=item.Chunk))
                    {try{item.Work.Dispose();}finally{ReleaseBuildingChild(item.Root);window.Preparing=null;}item=null;}
                    if(item==null)
                    {
                        var keys=loaded.Keys.Where(key=>!window.Ready.ContainsKey(key)).OrderBy(key=>key.x).ThenBy(key=>key.y).ToArray();
                        if(keys.Length==0)
                        {
                            // Commit all currently resident children together. New chunks
                            // subsequently use this same input/index; retired roots are owned
                            // by normal chunk disposal. No terrain/road root is replaced.
                            // An unpublished ordinary chunk may already contain old
                            // buildings. Cancel/requeue that preparation at the handover;
                            // all resident road/terrain objects stay untouched.
                            if(stage!=null)
                            {
                                var staleKey=stage.key;
                                try{CancelStage("building-window handover");}
                                finally{pendingBuilds.Enqueue(Cell(staleKey));priorityDirty=true;}
                            }
                            var previousRoots=window.Ready.Values.Where(replacement=>!replacement.Reused).Select(replacement=>replacement.Chunk.transform.Find("Building scenery")).Where(root=>root).ToArray();
                            try
                            {
                                foreach(var previous in previousRoots)previous.gameObject.SetActive(false);
                                foreach(var replacement in window.Ready.Values)if(!replacement.Reused)replacement.Root.SetActive(true);
                                foreach(var replacement in window.Ready.Values)if(!replacement.Reused)replacement.Root.name="Building scenery";
                            }
                            catch
                            {
                                foreach(var replacement in window.Ready.Values)if(!replacement.Reused&&replacement.Root)replacement.Root.SetActive(false);
                                foreach(var previous in previousRoots)if(previous)previous.gameObject.SetActive(true);
                                throw;
                            }
                            buildings=window.Index;buildingSites=window.Sites;buildingSiteOwners=window.SiteOwners;BuildingInputs=window.Inputs;
                            LastBuildingWindowReusedChunks=window.Ready.Values.Count(item=>item.Reused);LastBuildingWindowRebuiltChunks=window.Ready.Count-LastBuildingWindowReusedChunks;
                            BuildingWindowIdentity=window.IsBaseline?null:window.Identity;BuildingWindowStatus=window.IsBaseline?"Default scenery restored":"Building window ready";
                            // Transfer ownership before fallible retirement. Once published,
                            // staged-cancellation cleanup must never destroy these children.
                            pendingBuildings=null;
                            foreach(var previous in previousRoots)ReleaseBuildingChild(previous.gameObject);
                            return;
                        }
                        var key=keys[0];var signature=DesiredBuildingSignature(window,key);
                        var currentRoot=loaded[key].transform.Find("Building scenery");
                        if(currentRoot&&buildingChildSignatures.TryGetValue(currentRoot.gameObject,out var currentSignature)&&currentSignature==signature)
                        {
                            window.Ready.Add(key,new BuildingReplacement{Key=key,Chunk=loaded[key],Root=currentRoot.gameObject,Reused=true});
                            continue;
                        }
                        var root=CreateBuildingRoot(loaded[key].transform,key,signature);root.name="Pending building scenery";root.gameObject.SetActive(false);
                        item=new BuildingReplacement { Key=key,Chunk=loaded[key],Root=root.gameObject };
                        item.Work=ReplacementBuildingSteps(item,window).GetEnumerator();window.Preparing=item;
                    }
                    if(!AdvanceBuildingReplacement(window,item))
                    {item.Work.Dispose();window.Ready.Add(item.Key,item);window.Preparing=null;}
                }while(Elapsed(began)<BuildingWindowSliceMilliseconds);
            }
            catch(Exception error)
            {ResetBuildingWindow();BuildingWindowStatus="Building preparation failed; previous scenery retained: "+error.Message;Debug.LogWarning(BuildingWindowStatus);}
            finally{MaximumBuildingWindowSliceMilliseconds=Mathf.Max(MaximumBuildingWindowSliceMilliseconds,Elapsed(began));}
        }
    }
}
