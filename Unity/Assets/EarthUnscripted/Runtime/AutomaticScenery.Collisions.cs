using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class AutomaticScenery
    {
        sealed class Trunk { public GameObject root;public CapsuleCollider collider;public Tree tree;public TreeImpact impact; }
        readonly Dictionary<Vector2Int,Trunk> trunks=new Dictionary<Vector2Int,Trunk>();
        readonly Stack<Trunk> spareTrunks=new Stack<Trunk>();
        readonly List<Vector2Int> retireTrunks=new List<Vector2Int>();
        readonly List<Tree> pendingTrunks=new List<Tree>();
        Transform trunkRoot;Vector3 lastTrunkObserver=new Vector3(float.MaxValue,0,0);
        List<Grove> collisionGroves;int trunkCursor,trunkStride;
        public int CollisionTrees=>trunks.Count;
        public static float TrunkRadius(float height,float crown)=>Mathf.Clamp(Mathf.Max(height*.018f,crown*.035f),.08f,.8f);

        void UpdateTreeCollisions()
        {
            if(!host || !host.Observer)return;
            var eye=host.Observer.position;int quality=session?.Profile?.quality??1;int stride=quality==0?4:quality==1?2:1;
            if(collisionGroves!=groves || stride!=trunkStride || (eye-lastTrunkObserver).sqrMagnitude>12*12)
            {
                trunkStride=stride;lastTrunkObserver=eye;collisionGroves=groves;
                retireTrunks.Clear();
                foreach(var row in trunks)
                {
                    if(knownTrees.TryGetValue(row.Key,out var current)&&current!=row.Value.tree)
                    {row.Value.tree=current;row.Value.root.transform.position=new Vector3(current.source.x,current.groundY,current.source.z);}
                    if(row.Value.tree.source.seed%stride!=0 || new Vector2(row.Value.tree.source.x-eye.x,row.Value.tree.source.z-eye.z).sqrMagnitude>220*220)
                        retireTrunks.Add(row.Key);
                    else if(!row.Value.tree.supported)retireTrunks.Add(row.Key);
                }
                foreach(var key in retireTrunks){var trunk=trunks[key];trunk.root.SetActive(false);spareTrunks.Push(trunk);trunks.Remove(key);}
                pendingTrunks.Clear();trunkCursor=0;
                foreach(var group in groves)
                {
                    if(group.bounds.SqrDistance(eye)>170*170)continue;
                    foreach(var tree in group.trees)
                        if(tree.source.seed%stride==0 && new Vector2(tree.source.x-eye.x,tree.source.z-eye.z).sqrMagnitude<160*160)
                            pendingTrunks.Add(tree);
                }
            }
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(trunkCursor<pendingTrunks.Count && watch.Elapsed.TotalMilliseconds<1.5)
            {
                var tree=pendingTrunks[trunkCursor++];var source=tree.source;
                var key=new Vector2Int(Mathf.FloorToInt(source.x/8),Mathf.FloorToInt(source.z/8));
                if(tree.broken||!tree.supported||trunks.ContainsKey(key))continue;
                if(trunks.Count>=3072)break;
                if(!trunkRoot){var root=new GameObject("Streamed tree trunk collisions");root.transform.SetParent(transform,false);trunkRoot=root.transform;}
                Trunk trunk;
                if(spareTrunks.Count>0)trunk=spareTrunks.Pop();
                else{var obj=new GameObject("Tree trunk");obj.transform.SetParent(trunkRoot,false);trunk=new Trunk{root=obj,collider=obj.AddComponent<CapsuleCollider>(),impact=obj.AddComponent<TreeImpact>()};}
                float radius=TrunkRadius(source.height,source.crownRadius);float height=Mathf.Max(radius*2,Mathf.Min(8,source.height*.65f));
                trunk.tree=tree;trunk.root.transform.position=new Vector3(source.x,tree.groundY,source.z);
                trunk.collider.enabled=true;trunk.impact.Configure(radius,()=>tree.broken=true);
                trunk.collider.radius=radius;trunk.collider.height=height;trunk.collider.center=new Vector3(0,height*.5f,0);trunk.collider.direction=1;
                trunk.root.SetActive(true);trunks.Add(key,trunk);
            }
        }
    }
}
