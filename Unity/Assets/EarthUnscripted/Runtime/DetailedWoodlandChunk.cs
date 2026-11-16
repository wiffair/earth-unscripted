using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted
{
    // One renderer per completed chunk; all tree geometry/materials are shared
    // Resources assets. Only simple physical trunks are individual scene objects.
    [DefaultExecutionOrder(300)]
    public sealed class DetailedWoodlandChunk : MonoBehaviour
    {
        const int BucketCapacity=256,SubmissionLimit=128;
        public readonly struct TreeInstance
        {
            public readonly string Id;public readonly Vector3 Position;public readonly float Height,Radius;public readonly int Variant;public readonly Matrix4x4 LocalMatrix;
            public TreeInstance(GeoTree tree,Vector3 position){Id=tree.id;Position=position;Height=tree.height;Radius=tree.crownRadius;Variant=(tree.seed%3+3)%3;LocalMatrix=Matrix4x4.TRS(position,Quaternion.Euler(0,tree.seed%360,0),new Vector3(Radius,Height,Radius));}
        }
        [Serializable] public struct FrameStatistics
        {public int frame,residentTrees,submittedTrees,drawSubmissions,nearTrees,mediumTrees,farTrees;public long submittedTriangles;public bool instancingSupported;}
        static readonly Mesh[,] barkMeshes=new Mesh[3,3],leafMeshes=new Mesh[3,3];static Material barkMaterial,leafMaterial;
        static FrameStatistics statistics=new FrameStatistics();static readonly int WindProperty=Shader.PropertyToID("_WindStrength"),LodProperty=Shader.PropertyToID("_LodIndex"),DistanceProperty=Shader.PropertyToID("_LodDistances"),CameraProperty=Shader.PropertyToID("_TreeCameraPosition"),WetnessProperty=Shader.PropertyToID("_Wetness");
        readonly List<TreeInstance> trees=new List<TreeInstance>();readonly Matrix4x4[][] buckets=new Matrix4x4[9][];readonly int[] counts=new int[9];
        Matrix4x4[] worldMatrices;Vector3[] worldPositions;Matrix4x4 previousRoot;bool matricesReady;Bounds worldBounds;MaterialPropertyBlock properties;GameSession owner;
        public int TreeCount=>trees.Count;public IReadOnlyList<TreeInstance> Instances=>trees;public Bounds WorldBounds=>worldBounds;
        public static FrameStatistics Statistics=>statistics;
        public static void EnsureAssets()
        {
            if(barkMaterial&&leafMaterial&&barkMeshes[0,0]&&leafMeshes[2,2])return;
            for(int variant=0;variant<3;variant++)for(int lod=0;lod<3;lod++)
            {
                string prefix="Vegetation/Broadleaf-"+variant+"-"+lod+"-";
                barkMeshes[variant,lod]=Resources.Load<Mesh>(prefix+"bark");leafMeshes[variant,lod]=Resources.Load<Mesh>(prefix+"leaves");
                if(!barkMeshes[variant,lod]||!leafMeshes[variant,lod])throw new InvalidOperationException("Detailed woodland mesh is missing: "+prefix);
            }
            barkMaterial=Resources.Load<Material>("Vegetation/BroadleafBark");leafMaterial=Resources.Load<Material>("Vegetation/BroadleafLeaves");
            if(!barkMaterial||!leafMaterial)throw new InvalidOperationException("Detailed woodland materials are missing");
        }
        public void Add(GeoTree tree,Vector3 position)
        {
            if(trees.Count>=BucketCapacity)throw new InvalidOperationException("Canonical tree chunk exceeds renderer capacity");
            if(tree.height<4||tree.height>45||tree.crownRadius<1||tree.crownRadius>6||float.IsNaN(tree.height)||float.IsNaN(tree.crownRadius))throw new InvalidOperationException("Invalid canonical tree dimensions");
            trees.Add(new TreeInstance(tree,position));matricesReady=false;
            var trunk=new GameObject("Mapped tree trunk "+tree.id);trunk.transform.SetParent(transform,false);trunk.transform.localPosition=position;
            var collider=trunk.AddComponent<CapsuleCollider>();collider.direction=1;collider.radius=Mathf.Clamp(tree.crownRadius*.075f,.12f,.5f);collider.height=Mathf.Max(collider.radius*2,tree.height*.5f);collider.center=Vector3.up*(collider.height*.5f);
        }
        void OnEnable(){EnsureAssets();owner=FindAnyObjectByType<GameSession>();properties=new MaterialPropertyBlock();for(int i=0;i<9;i++)buckets[i]=new Matrix4x4[BucketCapacity];}
        void RebuildWorldMatrices()
        {
            var root=transform.localToWorldMatrix;if(matricesReady&&root==previousRoot)return;previousRoot=root;worldMatrices=new Matrix4x4[trees.Count];worldPositions=new Vector3[trees.Count];
            bool first=true;foreach(var tree in trees)
            {
                for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)for(int y=0;y<=1;y++)
                {var p=root.MultiplyPoint3x4(tree.Position+new Vector3(x*tree.Radius,y*tree.Height,z*tree.Radius));if(first){worldBounds=new Bounds(p,Vector3.zero);first=false;}else worldBounds.Encapsulate(p);}
            }
            for(int i=0;i<trees.Count;i++){worldMatrices[i]=root*trees[i].LocalMatrix;worldPositions[i]=root.MultiplyPoint3x4(trees[i].Position+Vector3.up*trees[i].Height*.5f);}matricesReady=true;
        }
        void LateUpdate()
        {
            if(trees.Count==0)return;var camera=Camera.main;if(!camera)return;RebuildWorldMatrices();
            if(statistics.frame!=Time.frameCount)statistics=new FrameStatistics{frame=Time.frameCount,instancingSupported=SystemInfo.supportsInstancing};statistics.residentTrees+=trees.Count;
            Array.Clear(counts,0,counts.Length);Vector3 eye=camera.transform.position;float far=camera.farClipPlane+50;
            int quality=owner?owner.Profile.quality:2;float near=quality==0?80:quality==1?115:160,medium=quality==0?260:quality==1?350:450;
            properties.SetVector(DistanceProperty,new Vector4(near,medium,0,0));properties.SetVector(CameraProperty,new Vector4(eye.x,eye.y,eye.z,1));
            for(int i=0;i<trees.Count;i++)
            {
                float squared=(worldPositions[i]-eye).sqrMagnitude;if(squared>far*far)continue;statistics.submittedTrees++;
                // Adjacent meshes overlap only inside a20% band. The shader uses
                // complementary screen-door masks, so the two LODs do not blink.
                if(squared<near*near*1.21f)AddToBucket(i,0);
                if(squared>near*near*.81f&&squared<medium*medium*1.21f)AddToBucket(i,1);
                if(squared>medium*medium*.81f)AddToBucket(i,2);
            }
            properties.SetFloat(WindProperty,owner&&owner.Conditions?Mathf.Clamp01(owner.Conditions.Wind/70):.25f);properties.SetFloat(WetnessProperty,owner&&owner.Conditions?owner.Conditions.Wetness:0);
            for(int variant=0;variant<3;variant++)for(int lod=0;lod<3;lod++)
            {
                int index=variant*3+lod,count=counts[index];if(count==0)continue;
                Submit(barkMeshes[variant,lod],barkMaterial,lod,buckets[index],count,camera);Submit(leafMeshes[variant,lod],leafMaterial,lod,buckets[index],count,camera);
            }
        }
        void AddToBucket(int tree,int lod)
        {
            int bucket=trees[tree].Variant*3+lod;buckets[bucket][counts[bucket]++]=worldMatrices[tree];
            if(lod==0)statistics.nearTrees++;else if(lod==1)statistics.mediumTrees++;else statistics.farTrees++;
        }
        void Submit(Mesh mesh,Material material,int lod,Matrix4x4[] matrices,int count,Camera camera)
        {
            properties.SetFloat(LodProperty,lod);
            var shadows=lod<2?ShadowCastingMode.On:ShadowCastingMode.Off;
            if(SystemInfo.supportsInstancing)
            {
                var render=new RenderParams(material){camera=camera,worldBounds=worldBounds,shadowCastingMode=shadows,receiveShadows=true,lightProbeUsage=LightProbeUsage.Off,matProps=properties,layer=gameObject.layer,renderingLayerMask=1};
                for(int first=0;first<count;first+=SubmissionLimit){int amount=Math.Min(SubmissionLimit,count-first);Graphics.RenderMeshInstanced(render,mesh,0,matrices,amount,first);statistics.drawSubmissions++;}
            }
            else for(int i=0;i<count;i++){Graphics.DrawMesh(mesh,matrices[i],material,gameObject.layer,camera,0,properties,shadows,true,null,LightProbeUsage.Off);statistics.drawSubmissions++;}
            statistics.submittedTriangles+=(long)mesh.GetIndexCount(0)/3*count;
        }
    }
}
