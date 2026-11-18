using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using NativeWorld = EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted
{
    // One live world owns these visual resources. Source models, actors and
    // profiles are never edited. Density and distances follow the existing
    // Low / Medium / High quality menu, including changes made while driving.
    public sealed partial class AutomaticScenery : MonoBehaviour
    {
        sealed class Grove
        {
            public Bounds bounds;
            public readonly List<Tree> trees = new List<Tree>();
        }
        sealed class TreeDamage { public bool broken;public AutomaticScenery owner; }
        sealed class Tree
        {
            public Tree(TreeDamage shared=null,AutomaticScenery owner=null){damage=shared??new TreeDamage{owner=owner};}
            public readonly TreeDamage damage;
            public NativeWorld.TreeSeed source;public float born,groundY;public bool supported;
            public int groundRevision=-1,coastRevision=-1;public ulong supportStamp;
            public bool broken{get=>damage.broken;set{if(damage.broken==value)return;damage.broken=value;if(damage.owner)damage.owner.treeDamageRevision++;}}
            public Matrix4x4 near,far;
        }
        AutomaticWorldHost host;
        AutomaticSceneryResources art;
        GameSession session;
        Material previousSky, sky;
        Mesh billboard;
        List<Grove> groves = new List<Grove>();
        Dictionary<Vector2Int,Tree> knownTrees = new Dictionary<Vector2Int,Tree>();
        // Keep two generations: the published view is never modified during a yield.
        List<Grove> spareGroves = new List<Grove>();
        Dictionary<Vector2Int,Tree> spareTrees = new Dictionary<Vector2Int,Tree>();
        Dictionary<Vector2Int,Grove> activeGroups = new Dictionary<Vector2Int,Grove>();
        Dictionary<Vector2Int,Grove> spareGroups = new Dictionary<Vector2Int,Grove>();
        readonly Stack<Grove> freeGroves = new Stack<Grove>();
        readonly Material[] views = new Material[3];
        readonly Material[][] treeMaterials = new Material[3][];
        readonly Matrix4x4[][] far = { new Matrix4x4[1023], new Matrix4x4[1023], new Matrix4x4[1023] };
        readonly Matrix4x4[][] near = { new Matrix4x4[1023], new Matrix4x4[1023], new Matrix4x4[1023] };
        readonly int[] farCount = new int[3], nearCount = new int[3];
        readonly Vector4[][] farFade = { new Vector4[1023], new Vector4[1023], new Vector4[1023] };
        readonly Vector4[][] nearFade = { new Vector4[1023], new Vector4[1023], new Vector4[1023] };
        MaterialPropertyBlock properties;
        readonly Plane[] planes = new Plane[6];
        readonly List<Tree> groundingProof=new List<Tree>(128);
        readonly bool captureGrounding=Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta10WorldSmoke")>=0;
        bool indexed,reindexPending,reindexRunning;
        int indexedGroundRevision=-1;
        public bool TreeGroundingReady=>indexed&&!reindexPending&&!reindexRunning&&host&&indexedGroundRevision==host.Renderer.SceneryGroundRevision;
        public int VisibleTrees { get; private set; }
        public int DetailedTrees { get; private set; }
        public int Quality { get; private set; }
        public int AvailableTrees { get; private set; }
        public float DrawDistanceMetres { get; private set; }
        public int AddedWhileDriving { get; private set; }
        public float MinimumArrivalDistanceMetres { get; private set; } = float.PositiveInfinity;

        public void Initialize(AutomaticWorldHost owner)
        {
            host = owner;
            properties = new MaterialPropertyBlock();
            art = Resources.Load<AutomaticSceneryResources>("AutomaticWorld/Scenery");
            if (!art || !art.surfaceShader || !art.billboardShader || !art.treeViews || art.treeModels.Length != 3)
                throw new InvalidOperationException("Automatic scenery resources are incomplete");
            session = FindFirstObjectByType<GameSession>();
            host.Renderer.ApplyPhotographicSurfaces(art);
            previousSky = RenderSettings.skybox;
            sky = GameGraphics.Create(art.skyMaterial, _euMaterial => { _euMaterial.name = "Owned photographic sky"; });
            RenderSettings.skybox = sky;
            billboard = new Mesh { name = "Owned tree view quad" };
            // The bake includes 8% clear space above/below the source model.
            billboard.vertices = new[] { new Vector3(-.27f,-.04f,0), new Vector3(.27f,-.04f,0),
                new Vector3(.27f,1.04f,0), new Vector3(-.27f,1.04f,0) };
            billboard.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            billboard.normals = new[] { Vector3.back,Vector3.back,Vector3.back,Vector3.back };
            billboard.triangles = new[] { 0,2,1,0,3,2 };
            billboard.bounds = new Bounds(new Vector3(0,.5f,0), new Vector3(1.2f,1.2f,1.2f));
            for (int i=0;i<3;i++)
            {
                treeMaterials[i]=new Material[art.treeModels[i].materials.Length];
                for(int part=0;part<treeMaterials[i].Length;part++){treeMaterials[i][part]=GameGraphics.Create(art.treeModels[i].materials[part]);}
                views[i] = GameGraphics.Create(art.billboardShader, _euMaterial => { _euMaterial.name = "Owned tree views " + i; _euMaterial.enableInstancing = true; });
                views[i].SetTexture("_Views",art.treeViews); views[i].SetFloat("_Variant",i);
            }
            host.Renderer.AreasChanged += Reindex;
            Reindex();
        }

        void Reindex()
        {
            if(indexed){reindexPending=true;return;}
            var work=BuildIndex();while(work.MoveNext()){}
        }

        IEnumerator ReindexBudgeted()
        {
            reindexPending=false;reindexRunning=true;
            try{yield return BuildIndex();}finally{reindexRunning=false;}
        }

        // Publish complete new grove/record sets together. Existing trees remain
        // visible while each background-area arrival is prepared in 2 ms slices.
        IEnumerator BuildIndex()
        {
            int groundRevision=host.Renderer.SceneryGroundRevision;
            var nextGroves=spareGroves;nextGroves.Clear();int nextAvailable=0,steps=0;
            foreach(var old in spareGroups.Values){old.trees.Clear();freeGroves.Push(old);}
            spareGroups.Clear();spareTrees.Clear();
            var slice=System.Diagnostics.Stopwatch.StartNew();
            var maps=new List<NativeWorld.Map>(host.Renderer.ResidentAreas);
            var groups = spareGroups;
            var current = spareTrees;
            foreach (var map in maps)
            {
                if (map.trees == null) continue;
                foreach (var tree in map.trees)
                {
                    if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                    var cell = new Vector2Int(Mathf.FloorToInt(tree.x/8),Mathf.FloorToInt(tree.z/8));
                    if(current.ContainsKey(cell))continue;
                    if(!knownTrees.TryGetValue(cell,out var record))
                    {
                        bool driving=indexed && session && session.Started && !session.Menu;
                        record=new Tree(owner:this) {source=tree,born=Time.time-(driving?0:10)};
                        if(driving && host.Observer)
                        {
                            AddedWhileDriving++;
                            float distance=new Vector2(tree.x-host.Observer.position.x,tree.z-host.Observer.position.z).magnitude;
                            MinimumArrivalDistanceMetres=Mathf.Min(MinimumArrivalDistanceMetres,distance);
                        }
                    }
                    current.Add(cell,GroundRecord(record));
                }
            }
            // Retiring an input core must not erase its still-visible trees.
            // Keep only nearby records; this does not grow with drive length.
            var observer=host.Observer?host.Observer.position:transform.position;
            foreach(var previous in knownTrees)
            {
                if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                if(!current.ContainsKey(previous.Key) &&
                    new Vector2(previous.Value.source.x-observer.x,previous.Value.source.z-observer.z).sqrMagnitude<3500*3500)
                    current.Add(previous.Key,GroundRecord(previous.Value));
            }

            foreach(var entry in current)
            {
                    var record=entry.Value;var tree=record.source;
                    if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                    var key = new Vector2Int(Mathf.FloorToInt(tree.x/64),Mathf.FloorToInt(tree.z/64));
                    var position = new Vector3(tree.x,record.groundY+tree.height*.5f,tree.z);
                    var bounds = new Bounds(position,new Vector3(tree.crownRadius*2,tree.height,tree.crownRadius*2));
                    if (!groups.TryGetValue(key,out var group))
                    { group=freeGroves.Count>0?freeGroves.Pop():new Grove();group.bounds=bounds;groups.Add(key,group);nextGroves.Add(group); }
                    else group.bounds.Encapsulate(bounds);
                    group.trees.Add(record);nextAvailable++;
            }
            spareTrees=knownTrees;knownTrees=current;
            spareGroves=groves;groves=nextGroves;
            spareGroups=activeGroups;activeGroups=groups;
            AvailableTrees=nextAvailable;indexed=true;indexedGroundRevision=groundRevision;
        }

        Tree GroundRecord(Tree previous)
        {
            int revision=host.Renderer.SceneryGroundRevision;
            if(previous.groundRevision==revision)return previous;
            var source=previous.source;bool supported=host.Renderer.TryPublishedSceneryGround(source.x,source.z,out float height);
            if(previous.supported==supported&&Mathf.Abs(previous.groundY-height)<.01f)
            {previous.groundRevision=revision;previous.coastRevision=host.Renderer.SceneryCoastRevision;previous.supportStamp=supported?host.Renderer.PublishedScenerySupportStamp(source.x,source.z):0;return previous;}
            var record=new Tree(previous.damage){source=source,born=previous.born,groundY=height,supported=supported,groundRevision=revision,coastRevision=host.Renderer.SceneryCoastRevision,supportStamp=supported?host.Renderer.PublishedScenerySupportStamp(source.x,source.z):0};
            var rotation=Quaternion.Euler(0,source.seed%360,0);var position=new Vector3(source.x,height,source.z);var model=art.treeModels[source.seed%3];
            record.far=Matrix4x4.TRS(position,rotation,Vector3.one*source.height);
            record.near=Matrix4x4.TRS(position,rotation,Vector3.one*(source.height/model.height))*model.local;
            return record;
        }

        void LateUpdate()
        {
            using var frameWork=WorkTelemetry.Measure("frame.tree-draw",null,8);
            if(!reindexRunning&&indexed&&host&&indexedGroundRevision!=host.Renderer.SceneryGroundRevision)reindexPending=true;
            if(reindexPending && !reindexRunning)StartCoroutine(ReindexBudgeted());
            if (!host || !host.Ready || !host.View || !art) return;
            UpdateTreeCollisions();
            var camera=host.View;
            Quality = session?.Profile != null ? session.Profile.quality : 1;
            int stride=Quality==0?4:Quality==1?2:1;
            float range=Quality==0?1000:Quality==1?2200:3200;
            float detail=Quality==0?0:Quality==1?28:65;
            DrawDistanceMetres=range;
            float light=session && session.Conditions ? session.Conditions.Daylight : 1;
            foreach (var material in views)
            {
                material.SetFloat("_Brightness",SceneryLighting.TreeBrightness(light));
                if(GameGraphics.HighDefinition)material.SetFloat("_Radiance",SceneryLighting.TreeRadiance(light));
            }
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            var eye=camera.transform.position;
            // These values are constant for this synchronous frame. Avoid an
            // engine clock call and repeated host lookups for every tree.
            float now=Time.time;
            var renderer=host.Renderer;
            int groundRevision=renderer.SceneryGroundRevision,coastRevision=renderer.SceneryCoastRevision;
            DrawCulledTrees(camera,eye,range,detail,stride,now,renderer,groundRevision,coastRevision);
        }

        void Draw(int variant,bool detailed,Camera camera)
        {
            int count=detailed?nearCount[variant]:farCount[variant];
            if(count==0)return;
            if(detailed)
            {
                var model=art.treeModels[variant];
                properties.SetVectorArray("_TreeFade",nearFade[variant]);
                for(int part=0;part<model.mesh.subMeshCount;part++)
                    Graphics.DrawMeshInstanced(model.mesh,part,treeMaterials[variant][Mathf.Min(part,treeMaterials[variant].Length-1)],near[variant],count,properties,ShadowCastingMode.On,true,gameObject.layer,camera);
                nearCount[variant]=0;
            }
            else
            {
                properties.SetVectorArray("_TreeFade",farFade[variant]);
                Graphics.DrawMeshInstanced(billboard,0,views[variant],far[variant],count,properties,
                    ShadowCastingMode.Off,false,gameObject.layer,camera,LightProbeUsage.Off);
                farCount[variant]=0;
            }
        }

        void OnDestroy()
        {
            DisposeTreeCulling();
            if(host && host.Renderer)host.Renderer.AreasChanged-=Reindex;
            if(RenderSettings.skybox==sky)RenderSettings.skybox=previousSky;
            if(sky)Destroy(sky);
            if(billboard)Destroy(billboard);
            foreach(var material in views)if(material)Destroy(material);
            foreach(var group in treeMaterials)if(group!=null)foreach(var material in group)if(material)Destroy(material);
        }
    }
}
