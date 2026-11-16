using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace EarthUnscripted
{
    // Original code-generated bodywork, not a reskin of the generic carriage.
    // Shared bogie/wheel LODs remain the credited original prototype meshes.
    public static class TrainStockMeshes
    {
        sealed class Builder
        {
            readonly List<Vector3> positions=new List<Vector3>();readonly List<int>[] indices=new List<int>[9];
            public Builder(){for(int i=0;i<indices.Length;i++)indices[i]=new List<int>();}
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material)
            {int n=positions.Count;positions.AddRange(new[]{a,b,c,d});indices[material].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,int material)
            {int n=positions.Count;positions.AddRange(new[]{a,b,c});indices[material].AddRange(new[]{n,n+1,n+2});}
            public void Box(Vector3 centre,Vector3 size,int material)
            {
                Vector3 lo=centre-size*.5f,hi=centre+size*.5f;
                Quad(new Vector3(lo.x,lo.y,lo.z),new Vector3(lo.x,lo.y,hi.z),new Vector3(lo.x,hi.y,hi.z),new Vector3(lo.x,hi.y,lo.z),material);
                Quad(new Vector3(hi.x,lo.y,hi.z),new Vector3(hi.x,lo.y,lo.z),new Vector3(hi.x,hi.y,lo.z),new Vector3(hi.x,hi.y,hi.z),material);
                Quad(new Vector3(lo.x,hi.y,lo.z),new Vector3(lo.x,hi.y,hi.z),new Vector3(hi.x,hi.y,hi.z),new Vector3(hi.x,hi.y,lo.z),material);
                Quad(new Vector3(lo.x,lo.y,hi.z),new Vector3(lo.x,lo.y,lo.z),new Vector3(hi.x,lo.y,lo.z),new Vector3(hi.x,lo.y,hi.z),material);
                Quad(new Vector3(hi.x,lo.y,lo.z),new Vector3(lo.x,lo.y,lo.z),new Vector3(lo.x,hi.y,lo.z),new Vector3(hi.x,hi.y,lo.z),material);
                Quad(new Vector3(lo.x,lo.y,hi.z),new Vector3(hi.x,lo.y,hi.z),new Vector3(hi.x,hi.y,hi.z),new Vector3(lo.x,hi.y,hi.z),material);
            }
            public void Hull(float length,float width,float height,float nose,bool front,bool back,float endWidth=.9f)
            {
                float half=length*.5f;float[] z={-half,-half+(back?nose:.03f),half-(front?nose:.03f),half};var rings=new Vector3[4][];
                for(int i=0;i<4;i++)
                {
                    bool end=i==0&&back||i==3&&front;float w=width*.5f*(end?endWidth:1),h=height-(end?nose*.31f:0);
                    rings[i]=new[]{new Vector3(-w,1.02f,z[i]),new Vector3(-w,h-.34f,z[i]),new Vector3(-w+.28f,h,z[i]),new Vector3(w-.28f,h,z[i]),new Vector3(w,h-.34f,z[i]),new Vector3(w,1.02f,z[i])};
                }
                for(int i=0;i<3;i++)for(int n=0;n<6;n++){int next=(n+1)%6;Quad(rings[i][n],rings[i+1][n],rings[i+1][next],rings[i][next],n==2?7:0);}
                for(int n=1;n<5;n++){Triangle(rings[0][0],rings[0][n],rings[0][n+1],0);Triangle(rings[3][0],rings[3][n+1],rings[3][n],0);}
            }
            public Mesh Finish(string name)
            {var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(positions);mesh.subMeshCount=9;for(int i=0;i<9;i++)mesh.SetTriangles(indices[i],i,false);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
        }
        public static Mesh Body(TrainVehicle stock,int role,int lod)
        {
            var b=new Builder();float length=stock.Length,width=stock.Width,height=stock.Height-(stock.Style==TrainBodyStyle.Container?0:stock.Style==TrainBodyStyle.Locomotive?.08f:.30f);bool front=role==1,back=role==-1;
            if(stock.Style==TrainBodyStyle.Container)
            {
                b.Box(new Vector3(0,.94f,0),new Vector3(width,.28f,length),6);
                for(int container=0;container<2;container++)
                {
                    float z=(container==0?-1:1)*length*.245f;
                    b.Box(new Vector3(0,2.5f,z),new Vector3(2.43f,2.58f,length*.465f),container==0?2:0);
                    if(lod==0)for(float rib=-length*.22f;rib<length*.22f;rib+=.7f)foreach(int side in new[]{-1,1})b.Box(new Vector3(side*1.23f,2.5f,z+rib),new Vector3(.055f,2.5f,.035f),3);
                }
            }
            else if(stock.Style==TrainBodyStyle.Locomotive)
            {
                b.Box(new Vector3(0,.95f,0),new Vector3(width,.4f,length),6);b.Hull(length,width,height,1.05f,true,true,.94f);
                foreach(int end in new[]{-1,1})
                {
                    float z=end*(length*.5f+.045f);
                    b.Box(new Vector3(0,1.85f,z),new Vector3(width*.58f,1.55f,.09f),5);
                    b.Box(new Vector3(0,3.02f,z+end*.02f),new Vector3(width*.69f,.72f,.11f),4);
                }
                foreach(int side in new[]{-1,1})
                {
                    b.Box(new Vector3(side*(width*.5f+.015f),2.45f,0),new Vector3(.04f,1.05f,length*.55f),1);
                    if(lod==0)for(float z=-length*.27f;z<length*.27f;z+=.42f)b.Box(new Vector3(side*(width*.5f+.045f),2.45f,z),new Vector3(.035f,1.04f,.045f),3);
                }
                if(lod<2)for(int fan=0;fan<3;fan++)b.Box(new Vector3(0,height+.035f,fan*2-2),new Vector3(1.35f,.07f,1.3f),1);
            }
            else
            {
                float nose=stock.Style==TrainBodyStyle.Intercity?2.8f:stock.Style==TrainBodyStyle.Civity?1.12f:.45f;
                b.Hull(length,width,height,nose,front,back,stock.Style==TrainBodyStyle.Intercity?.56f:.9f);b.Box(new Vector3(0,.77f,0),new Vector3(width*.88f,.52f,length*.84f),1);
                float start=-length*.5f+(back?nose+1:1),finish=length*.5f-(front?nose+1:1);
                foreach(int side in new[]{-1,1})
                {
                    float x=side*(width*.5f+.012f);
                    b.Box(new Vector3(x,1.65f,0),new Vector3(.025f,.20f,length-nose*(front||back?1:0)),2);
                    if(lod==2)b.Box(new Vector3(x,2.73f,(start+finish)*.5f),new Vector3(.027f,.70f,finish-start),4);
                    else for(float z=start+.8f;z<finish-.3f;z+=1.8f)b.Box(new Vector3(x,2.73f,z),new Vector3(.03f,.72f,1.25f),4);
                    foreach(float z in new[]{-length*.31f,length*.31f})
                    {
                        b.Box(new Vector3(x+side*.025f,2.12f,z),new Vector3(.04f,2.08f,1.22f),2);
                        if(lod<2){b.Box(new Vector3(x+side*.055f,2.75f,z),new Vector3(.02f,.62f,.91f),4);b.Box(new Vector3(x+side*.055f,2.11f,z),new Vector3(.02f,1.95f,.026f),3);}
                    }
                }
                if(front||back)
                {
                    int end=front?1:-1;float z=end*(length*.5f-nose*.38f);
                    if(stock.Style==TrainBodyStyle.Intercity)
                    {
                        float near=length*.5f-nose*.20f,far=length*.5f-nose*.75f;
                        float yn=height-nose*.31f+nose*.20f*.31f+.025f,yf=height-nose*.31f+nose*.75f*.31f+.025f;
                        var a=new Vector3(-.54f,yn,end*near);var c=new Vector3(.75f,yf,end*far);var d=new Vector3(.54f,yn,end*near);var e=new Vector3(-.75f,yf,end*far);
                        if(end>0)b.Quad(a,d,c,e,4);else b.Quad(a,e,c,d,4);
                    }
                    else b.Box(new Vector3(0,2.90f,end*(length*.5f+.055f)),new Vector3(width*.65f,.75f,.10f),4);
                    b.Box(new Vector3(0,1.72f,end*(length*.5f+.035f)),new Vector3(width*.53f,1.1f,.08f),5);
                    if(stock.Style!=TrainBodyStyle.Intercity)b.Box(new Vector3(0,2.0f,end*(length*.5f+.035f)),new Vector3(.65f,1.8f,.12f),1);
                }
                if(lod<2)
                {
                    for(float z=-length*.27f;z<length*.3f;z+=length*.24f)b.Box(new Vector3(0,height+.10f,z),new Vector3(1.2f,.20f,1.45f),7);
                    if(stock.Style==TrainBodyStyle.Intercity)b.Box(new Vector3(0,height+.23f,0),new Vector3(1.3f,.12f,2),6); // stowed bi-mode pantograph
                }
            }
            return b.Finish("Owned "+stock.Id+" body "+role+" LOD"+lod);
        }
    }
}
