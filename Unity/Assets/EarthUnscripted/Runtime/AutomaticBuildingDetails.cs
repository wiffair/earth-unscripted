using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using NativeWorld = EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted
{
    // Decorative openings on mapped footprints. These do not change a wall's
    // collision shape, surveyed road profiles, or any character material.
    public sealed class AutomaticBuildingDetails : MonoBehaviour
    {
        sealed class Facades
        {
            public Vector3 owner;
            public Bounds bounds;
            public readonly List<Matrix4x4> frames=new List<Matrix4x4>();
            public readonly List<Matrix4x4> glass=new List<Matrix4x4>();
        }
        AutomaticWorldHost host;
        GameSession session;
        Material frame,glass;
        Mesh quad;
        List<Facades> facades=new List<Facades>();
        readonly Matrix4x4[] draw=new Matrix4x4[1023];
        readonly Plane[] planes=new Plane[6];
        bool indexed,reindexPending,reindexRunning;
        public int VisibleOpenings {get;private set;}

        public void Initialize(AutomaticWorldHost owner)
        {
            host=owner;session=FindFirstObjectByType<GameSession>();
            var art=Resources.Load<AutomaticSceneryResources>("AutomaticWorld/Scenery");
            frame=Make("Owned window frames",new Color(.64f,.66f,.6f),.22f,art.surfaceShader);
            glass=Make("Owned opaque glazing",new Color(.065f,.105f,.135f),.78f,art.surfaceShader);
            quad=new Mesh{name="Owned facade quad"};
            quad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            quad.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
            quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};quad.triangles=new[]{0,1,2,0,2,3};
            quad.RecalculateBounds();
            host.Renderer.AreasChanged+=Reindex;Reindex();
        }

        static Material Make(string name,Color colour,float smooth,Shader shader)
        {
            var material=GameGraphics.Create(shader, _euMaterial => { _euMaterial.name=name; _euMaterial.enableInstancing=true; });
            material.SetColor("_BaseColor",colour);material.SetFloat("_Smoothness",smooth);
            material.SetFloat("_NormalStrength",0);material.SetTexture("_BaseMap",Texture2D.whiteTexture);
            material.SetVector("_TileSize",new Vector4(2,2,0,0));return material;
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
        IEnumerator BuildIndex()
        {
            var nextFacades=new List<Facades>();
            var slice=System.Diagnostics.Stopwatch.StartNew();
            var maps=new List<NativeWorld.Map>(host.Renderer.ResidentAreas);
            var seen=new HashSet<string>();var groups=new Dictionary<Vector2Int,Facades>();
            foreach(var map in maps)
            {
                if(map.buildings==null)continue;
                foreach(var house in map.buildings.Where(h=>!h.underground && h.minHeight<=0 && h.layer<=0))
                {
                    if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                    var centre=house.centre;
                    if(centre.x<map.coreMinX || centre.x>=map.coreMaxX || centre.z<map.coreMinZ || centre.z>=map.coreMaxZ || !seen.Add(house.id))continue;
                    if(house.points==null || house.points.Length<4 || house.height<2.7f)continue;
                    var points=new List<Vector3>(house.points);
                    if(Vector3.Distance(points[0],points[points.Count-1])<.05f)points.RemoveAt(points.Count-1);
                    if(points.Count<3)continue;
                    float roof=0,area=0,longest=0;int doorEdge=0;
                    for(int i=0;i<points.Count;i++)
                    {
                        var a=points[i];var b=points[(i+1)%points.Count];
                        roof+=host.Renderer.TryNaturalHeight(a.x,a.z,out var ground)?ground:a.y;
                        area+=a.x*b.z-b.x*a.z;
                        float length=Vector3.ProjectOnPlane(b-a,Vector3.up).magnitude;
                        if(length>longest){longest=length;doorEdge=i;}
                    }
                    roof=roof/points.Count+house.height;
                    if(house.heightIncludesRoof && (house.roofShape=="gabled" || house.roofShape=="pitched"))roof-=Mathf.Clamp(house.roofHeight,.5f,5);
                    var key=new Vector2Int(Mathf.FloorToInt(centre.x/64),Mathf.FloorToInt(centre.z/64));
                    var bounds=new Bounds(centre+Vector3.up*house.height*.5f,new Vector3(longest*2,house.height,longest*2));
                    if(!groups.TryGetValue(key,out var group))
                    {group=new Facades{owner=centre,bounds=bounds};groups.Add(key,group);}
                    else group.bounds.Encapsulate(bounds);
                    bool industrial=house.kind=="industrial" || house.kind=="warehouse" || house.kind=="shed";
                    for(int edge=0;edge<points.Count;edge++)
                    {
                        var a=points[edge];var b=points[(edge+1)%points.Count];
                        var delta=Vector3.ProjectOnPlane(b-a,Vector3.up);float length=delta.magnitude;
                        if(length<2.4f)continue;
                        var direction=delta/length;
                        var outward=Vector3.Cross(Vector3.up,direction)*(area>0?1:-1);
                        var rotation=Quaternion.LookRotation(outward);
                        int columns=Mathf.Min(24,Mathf.Max(1,Mathf.FloorToInt(length/2.8f)));
                        for(int column=0;column<columns;column++)
                        {
                            if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
                            float along=(column+.5f)/columns;var site=Vector3.Lerp(a,b,along);
                            float ground=host.Renderer.TryNaturalHeight(site.x,site.z,out var y)?y:site.y;
                            if(edge==doorEdge && column==columns/2)
                            {
                                var door=site+outward*.045f;door.y=ground+1.05f;
                                if(door.y+1.05f<roof-.2f)Opening(group,door,rotation,outward,industrial?2.3f:.95f,2.1f,false);
                            }
                            if(industrial)continue;
                            for(int floor=0;floor<12;floor++)
                            {
                                float height=ground+1.75f+floor*2.9f;
                                if(height+.7f>roof-.3f)break;
                                if(floor==0 && edge==doorEdge && column==columns/2)continue;
                                site.y=height;Opening(group,site+outward*.045f,rotation,outward,1.08f,1.35f,true);
                            }
                        }
                    }
                }
            }
            foreach(var group in groups.Values)if(group.glass.Count>0)nextFacades.Add(group);
            facades=nextFacades;indexed=true;
        }

        void Opening(Facades group,Vector3 centre,Quaternion rotation,Vector3 outward,float width,float height,bool mullions)
        {
            if(!host.Renderer.FacadeOpeningClear(centre,width,height))return;
            group.frames.Add(Matrix4x4.TRS(centre,rotation,new Vector3(width+.12f,height+.12f,1)));
            group.glass.Add(Matrix4x4.TRS(centre+outward*.008f,rotation,new Vector3(width,height,1)));
            if(!mullions)return;
            group.frames.Add(Matrix4x4.TRS(centre+outward*.014f,rotation,new Vector3(.035f,height,1)));
            group.frames.Add(Matrix4x4.TRS(centre+outward*.014f,rotation,new Vector3(width,.035f,1)));
        }

        void LateUpdate()
        {
            if(reindexPending && !reindexRunning)StartCoroutine(ReindexBudgeted());
            VisibleOpenings=0;
            if(!host || !host.View || session?.Profile==null || session.Profile.quality==0)return;
            float range=session.Profile.quality==1?260:450;
            var view=host.View;GeometryUtility.CalculateFrustumPlanes(view,planes);
            foreach(var group in facades)
            {
                if(group.bounds.SqrDistance(view.transform.position)>range*range || !GeometryUtility.TestPlanesAABB(planes,group.bounds) || !host.Renderer.HasGeometryAt(group.owner))continue;
                Draw(group.frames,frame,view);Draw(group.glass,glass,view);VisibleOpenings+=group.glass.Count;
            }
        }
        void Draw(List<Matrix4x4> matrices,Material material,Camera view)
        {
            for(int offset=0;offset<matrices.Count;offset+=1023)
            {
                int count=Mathf.Min(1023,matrices.Count-offset);matrices.CopyTo(offset,draw,0,count);
                Graphics.DrawMeshInstanced(quad,0,material,draw,count,null,ShadowCastingMode.Off,true,gameObject.layer,view);
            }
        }
        void OnDestroy()
        {
            if(host && host.Renderer)host.Renderer.AreasChanged-=Reindex;
            if(quad)Destroy(quad);if(frame)Destroy(frame);if(glass)Destroy(glass);
        }
    }
}
