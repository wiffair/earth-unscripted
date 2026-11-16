using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    public static class NatureMeshes
    {
        static readonly Dictionary<int,Mesh> crowns=new Dictionary<int,Mesh>();
        static Mesh Crown(int seed,bool simple)
        {
            int key=seed*2+(simple?1:0);if(crowns.TryGetValue(key,out var mesh)&&mesh)return mesh;var random=new System.Random(918+seed);var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();int count=simple?8:25,rows=simple?4:6,columns=simple?6:9;
            for(int cluster=0;cluster<count;cluster++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2,spread=Mathf.Sqrt((float)random.NextDouble())*.29f;var centre=new Vector3(Mathf.Sin(angle)*spread,.51f+(float)random.NextDouble()*.35f,Mathf.Cos(angle)*spread);float radius=.085f+(float)random.NextDouble()*.075f;int start=v.Count;
                for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++){float a=y/(float)rows*Mathf.PI,b=x/(float)columns*Mathf.PI*2;float perturb=.87f+(float)random.NextDouble()*.26f;var point=new Vector3(Mathf.Sin(a)*Mathf.Cos(b),Mathf.Cos(a)*1.3f,Mathf.Sin(a)*Mathf.Sin(b))*radius*perturb;v.Add(centre+point);uv.Add(new Vector2(x/(float)columns,y/(float)rows));if(y<rows&&x<columns){int i=start+y*(columns+1)+x;t.AddRange(new[]{i,i+1,i+columns+1,i+1,i+columns+2,i+columns+1});}}
            }
            mesh=new Mesh{name="Original branching canopy "+key};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();crowns[key]=mesh;return mesh;
        }
        public static void Tree(Transform parent,Vector3 position,float height,int seed,Material bark,Material leaves)
        {
            var root=new GameObject("Branching tree").transform;root.SetParent(parent,false);root.localPosition=position;root.localScale=Vector3.one*height;root.localRotation=Quaternion.Euler(0,seed*43%360,0);
            var trunk=Geometry.Primitive(PrimitiveType.Cylinder,"Trunk",root,new Vector3(0,.3f,0),new Vector3(.035f,.3f,.035f),bark).GetComponent<Renderer>();
            var branches=new List<Renderer>();for(int i=0;i<5;i++){float a=i*2.4f;var start=new Vector3(0,.36f,0);var end=new Vector3(Mathf.Sin(a)*.18f,.66f,Mathf.Cos(a)*.18f);var branch=Geometry.Primitive(PrimitiveType.Cylinder,"Branch",root,(start+end)*.5f,new Vector3(.015f,(end-start).magnitude*.5f,.015f),bark);branch.transform.localRotation=Quaternion.FromToRotation(Vector3.up,end-start);branches.Add(branch.GetComponent<Renderer>());}
            Renderer Create(string name,bool low){var g=new GameObject(name);g.transform.SetParent(root,false);g.AddComponent<MeshFilter>().sharedMesh=Crown(Mathf.Abs(seed)%3,low);var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=leaves;return r;}
            var fine=Create("Leaf clusters",false);branches.Add(fine);branches.Add(trunk);var coarse=Create("Distant leaf clusters",true);var group=root.gameObject.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.045f,branches.ToArray()),new LOD(.008f,new[]{coarse,trunk}),new LOD(.002f,new[]{coarse})});group.RecalculateBounds();
        }
    }
}
