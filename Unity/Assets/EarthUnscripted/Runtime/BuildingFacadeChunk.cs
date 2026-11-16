using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using EarthUnscripted.FacadePersistentCandidate800;
#if UNITY_EDITOR
using EarthUnscripted.Scenery800.Diagnostics;
#endif

namespace EarthUnscripted
{
    // Decorative instances only. Collision and the building shell belong to GeoWorld.
    [DefaultExecutionOrder(300)]
    public sealed class BuildingFacadeChunk : MonoBehaviour
    {
        public const int Capacity=4096;
        const int BatchSize=128,GroupCount=24;
        public readonly struct Feature
        {
            public readonly string Id;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float Width,Height;
            public readonly bool IsDoor;
            public readonly int Palette;
            public Feature(string id,Vector3 position,Quaternion rotation,float width,float height,bool door,int palette)
            {Id=id;Position=position;Rotation=rotation;Width=width;Height=height;IsDoor=door;Palette=palette;}
        }
        [Serializable] public struct FrameStatistics
        {public int frame,residentFeatures,submittedWindows,submittedDoors,drawSubmissions;public long submittedTriangles;}
        sealed class Group
        {
            public Mesh Mesh;public Material Material;public readonly Matrix4x4[] Matrices=new Matrix4x4[BatchSize];
            public int Count;public Bounds Bounds;public bool Near;
            public readonly int[] FeatureIndices=new int[BatchSize];
        }
        static Mesh windowFrameNear,windowFrameFar,pane,doorFrameNear,doorFrameFar,handle;
        static FrameStatistics statistics;
        readonly List<Feature> features=new List<Feature>();
        readonly Group[] groups=new Group[GroupCount];
        Matrix4x4[] matrices;Vector3[] positions;Bounds[] bounds;Matrix4x4 previousRoot;
        bool matricesReady;int overflowCount;
        FacadePersistentTown800 persistentController;
        internal void AttachPersistentCandidate(FacadePersistentTown800 controller)
        { if(persistentController!=null&&persistentController!=controller)throw new InvalidOperationException("Facade already attached");persistentController=controller; }
        internal void DetachPersistentCandidate(FacadePersistentTown800 controller)
        { if(ReferenceEquals(persistentController,controller))persistentController=null; }
        void OnDisable(){persistentController?.SourceDisabled(this);}
        void OnDestroy(){persistentController?.SourceDisabled(this);}
        public IReadOnlyList<Feature> Features=>features;
        public int FeatureCount=>features.Count;
        public int OverflowCount=>overflowCount;
        public static FrameStatistics Statistics=>statistics.frame==Time.frameCount?statistics:new FrameStatistics{frame=Time.frameCount};
        public bool CheckCapacity(){if(features.Count<Capacity)return true;overflowCount++;return false;}
        public bool AddWindow(string id,Vector3 centre,Quaternion outwardRotation,float width,float height)
            =>Add(id,centre,outwardRotation,width,height,false,0);
        public bool AddDoor(string id,Vector3 centre,Quaternion outwardRotation,float width,float height,int palette)
            =>Add(id,centre,outwardRotation,width,height,true,palette);
        bool Add(string id,Vector3 centre,Quaternion rotation,float width,float height,bool door,int palette)
        {
            if(features.Count>=Capacity){overflowCount++;return false;}
            float rotationLength=rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w;
            if(string.IsNullOrEmpty(id)||!Finite(centre.x)||!Finite(centre.y)||!Finite(centre.z)||
               !Finite(width)||!Finite(height)||width<=0||height<=0||width>20||height>20||palette<0||palette>3||
               !Finite(rotation.x)||!Finite(rotation.y)||!Finite(rotation.z)||!Finite(rotation.w)||
               !Finite(rotationLength)||rotationLength<.000001f)return false;
            features.Add(new Feature(id,centre,rotation.normalized,width,height,door,palette));matricesReady=false;return true;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        void OnEnable()
        {
            EnsureMeshes();
            for(int i=0;i<GroupCount;i++)if(groups[i]==null)groups[i]=new Group();
            Configure(0,windowFrameNear,BuildingAppearanceMaterials.Frame,true);
            Configure(1,pane,BuildingAppearanceMaterials.Glass,true);
            Configure(2,windowFrameFar,BuildingAppearanceMaterials.Frame,false);
            Configure(3,pane,BuildingAppearanceMaterials.Glass,false);
            for(int palette=0;palette<4;palette++)
            {
                int i=4+palette*5;var material=BuildingAppearanceMaterials.Door(palette);
                Configure(i,doorFrameNear,BuildingAppearanceMaterials.Frame,true);
                Configure(i+1,pane,material,true);Configure(i+2,handle,BuildingAppearanceMaterials.Frame,true);
                Configure(i+3,doorFrameFar,BuildingAppearanceMaterials.Frame,false);Configure(i+4,pane,material,false);
            }
        }
        void Configure(int index,Mesh mesh,Material material,bool near)
        {groups[index].Mesh=mesh;groups[index].Material=material;groups[index].Near=near;}
        void RebuildMatrices()
        {
            var root=transform.localToWorldMatrix;if(matricesReady&&(persistentController!=null&&persistentController.CandidateRequested?root.Equals(previousRoot):root==previousRoot))return;
            previousRoot=root;
            if(matrices==null||matrices.Length!=features.Count)
            {matrices=new Matrix4x4[features.Count];positions=new Vector3[features.Count];bounds=new Bounds[features.Count];}
            for(int i=0;i<features.Count;i++)
            {
                var f=features[i];var matrix=root*Matrix4x4.TRS(f.Position,f.Rotation,new Vector3(f.Width,f.Height,1));matrices[i]=matrix;
                positions[i]=root.MultiplyPoint3x4(f.Position);var b=new Bounds(matrix.MultiplyPoint3x4(new Vector3(-.5f,-.5f,0)),Vector3.zero);
                // All near/far geometry fits this shared envelope, including sill and handle.
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=0;z<=1;z++)b.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x*.5f,y*.5f,z*.08f)));
                b.Expand(.002f);bounds[i]=b;
            }
            matricesReady=true;
        }
        void LateUpdate()
        {
            if(!isActiveAndEnabled)return;
            if(statistics.frame!=Time.frameCount)statistics=new FrameStatistics{frame=Time.frameCount};
            statistics.residentFeatures+=features.Count;
            var camera=Camera.main;
            if(persistentController!=null&&persistentController.CandidateRequested)
                persistentController.BeginSource(this,camera,features.Count);
            if(!camera||features.Count==0)return;
            RebuildMatrices();var eye=camera.transform.position;float far=Mathf.Min(900,camera.farClipPlane),farSquared=far*far;
            for(int i=0;i<features.Count;i++)
            {
                float distance=(positions[i]-eye).sqrMagnitude;if(distance>farSquared)continue;
                bool near=distance<180*180;var feature=features[i];
                if(persistentController!=null&&persistentController.CandidateRequested)
                    persistentController.SelectedFeature(this,feature.IsDoor,near);
                if(!feature.IsDoor)
                {statistics.submittedWindows++;int index=near?0:2;Queue(index,i,camera);Queue(index+1,i,camera);}
                else
                {statistics.submittedDoors++;int index=4+feature.Palette*5+(near?0:3);Queue(index,i,camera);Queue(index+1,i,camera);if(near)Queue(index+2,i,camera);}
            }
            for(int i=0;i<GroupCount;i++)Flush(groups[i],camera);
        }
        void Queue(int index,int feature,Camera camera)
        {
            var group=groups[index];if(group.Count==0)group.Bounds=bounds[feature];else group.Bounds.Encapsulate(bounds[feature]);
            #if UNITY_EDITOR
            int slot=group.Count;
#endif
            group.FeatureIndices[group.Count]=feature;
            group.Matrices[group.Count++]=matrices[feature];
#if UNITY_EDITOR
            var observer=FacadeSubmissionHooks800.Current;
            if(observer!=null)FacadeSubmissionHooks800.NotifyQueued(observer,this,index,slot,feature,
                features[feature],previousRoot,group.Matrices[slot],positions[feature],bounds[feature],camera,statistics.frame);
#endif
            if(group.Count==BatchSize)Flush(group,camera);
        }
        void Flush(Group group,Camera camera)
        {
            int count=group.Count;if(count==0)return;group.Count=0;
            if(persistentController!=null&&persistentController.CandidateRequested)
            {
                persistentController.Append(this,Array.IndexOf(groups,group),group.Mesh,group.Material,
                    group.Near,gameObject.layer,group.Matrices,group.FeatureIndices,count);
                statistics.submittedTriangles+=(long)group.Mesh.GetIndexCount(0)/3*count;
                return;
            }
            var shadow=group.Near?ShadowCastingMode.On:ShadowCastingMode.Off;
            if(SystemInfo.supportsInstancing&&group.Material.enableInstancing)
            {
                var render=new RenderParams(group.Material){camera=camera,worldBounds=group.Bounds,shadowCastingMode=shadow,receiveShadows=true,lightProbeUsage=LightProbeUsage.Off,layer=gameObject.layer,renderingLayerMask=1};
                #if UNITY_EDITOR
                var observer=FacadeSubmissionHooks800.Current;
                if(observer!=null)FacadeSubmissionHooks800.NotifyFlushing(observer,this,Array.IndexOf(groups,group),group.Mesh,group.Material,
                    group.Matrices,count,group.Bounds,camera,true,shadow,render,statistics.frame);
#endif
                Graphics.RenderMeshInstanced(render,group.Mesh,0,group.Matrices,count);statistics.drawSubmissions++;
            }
            #if UNITY_EDITOR
            else
            {
                var observer=FacadeSubmissionHooks800.Current;
                if(observer!=null)FacadeSubmissionHooks800.NotifyFlushing(observer,this,Array.IndexOf(groups,group),group.Mesh,group.Material,
                    group.Matrices,count,group.Bounds,camera,false,shadow,null,statistics.frame);
#endif
#if !UNITY_EDITOR
            else
#endif
                for(int i=0;i<count;i++)
            {Graphics.DrawMesh(group.Mesh,group.Matrices[i],group.Material,gameObject.layer,camera,0,null,shadow,true,null,LightProbeUsage.Off);statistics.drawSubmissions++;}
#if UNITY_EDITOR
            }
#endif
            statistics.submittedTriangles+=(long)group.Mesh.GetIndexCount(0)/3*count;
        }
        static void EnsureMeshes()
        {
            if(windowFrameNear&&windowFrameFar&&pane&&doorFrameNear&&doorFrameFar&&handle)return;
            windowFrameNear=Frame("Shared recessed sash window",false,false);windowFrameFar=Frame("Shared flat sash window",false,true);
            doorFrameNear=Frame("Shared recessed door frame",true,false);doorFrameFar=Frame("Shared flat door frame",true,true);
            var p=new Builder();p.Front(-.5f,-.5f,.5f,.5f,.025f);pane=p.Mesh("Shared opaque facade panel");
            var h=new Builder();h.Box(.32f,-.055f,.375f,.055f,.055f,.08f);handle=h.Mesh("Shared door handle");
        }
        static Mesh Frame(string name,bool door,bool flat)
        {
            var b=new Builder();Strip(b,-.5f,-.5f,-.445f,.5f,flat);Strip(b,.445f,-.5f,.5f,.5f,flat);
            Strip(b,-.445f,.455f,.445f,.5f,flat);Strip(b,-.445f,-.5f,.445f,-.455f,flat);
            if(!door)
            {Strip(b,-.018f,-.455f,.018f,.455f,flat);Strip(b,-.445f,-.018f,.445f,.018f,flat);if(flat)b.Front(-.5f,-.5f,.5f,-.46f,.08f);else b.Box(-.5f,-.5f,.5f,-.46f,.015f,.08f);}
            return b.Mesh(name);
        }
        static void Strip(Builder builder,float x0,float y0,float x1,float y1,bool flat)
        {if(flat)builder.Front(x0,y0,x1,y1,.07f);else builder.Box(x0,y0,x1,y1,0,.07f);}
        // Original flat-shaded boxes and planes: shared for the application lifetime.
        // No GeneratedMeshOwner is attached: retiring a chunk must not destroy them.
        sealed class Builder
        {
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> indices=new List<int>();
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.one);uv.Add(Vector2.up);indices.Add(n);indices.Add(n+1);indices.Add(n+2);indices.Add(n);indices.Add(n+2);indices.Add(n+3);}
            public void Front(float x0,float y0,float x1,float y1,float z)=>Quad(new Vector3(x0,y0,z),new Vector3(x1,y0,z),new Vector3(x1,y1,z),new Vector3(x0,y1,z));
            public void Box(float x0,float y0,float x1,float y1,float z0,float z1)
            {
                Front(x0,y0,x1,y1,z1);
                Quad(new Vector3(x1,y0,z0),new Vector3(x0,y0,z0),new Vector3(x0,y1,z0),new Vector3(x1,y1,z0));
                Quad(new Vector3(x0,y0,z0),new Vector3(x0,y0,z1),new Vector3(x0,y1,z1),new Vector3(x0,y1,z0));
                Quad(new Vector3(x1,y0,z1),new Vector3(x1,y0,z0),new Vector3(x1,y1,z0),new Vector3(x1,y1,z1));
                Quad(new Vector3(x0,y1,z1),new Vector3(x1,y1,z1),new Vector3(x1,y1,z0),new Vector3(x0,y1,z0));
                Quad(new Vector3(x0,y0,z0),new Vector3(x1,y0,z0),new Vector3(x1,y0,z1),new Vector3(x0,y0,z1));
            }
            public Mesh Mesh(string name)
            {var mesh=new Mesh{name=name,hideFlags=HideFlags.DontSave};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
        }
    }
}
