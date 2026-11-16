using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    public static class CarDetails
    {
        public static void Tyre(Transform parent,Material material,float targetRadius=.36f,float width=.225f)
        {
            const int rings=96,section=16;var v=new Vector3[(rings+1)*(section+1)];var uv=new Vector2[v.Length];var t=new List<int>();
            for(int r=0;r<=rings;r++)for(int j=0;j<=section;j++)
            {
                float a=r/(float)rings*Mathf.PI*2,b=j/(float)section*Mathf.PI*2;float tread=Mathf.Cos(b)>.2f&&(r%6==0||j==3||j==section-3)?.007f:0;float radius=(.285f+Mathf.Cos(b)*.08f-tread)*targetRadius/.365f;int index=r*(section+1)+j;v[index]=new Vector3(Mathf.Sin(b)*width*.5f,Mathf.Cos(a)*radius,Mathf.Sin(a)*radius);uv[index]=new Vector2(r/(float)rings,j/(float)section);
                if(r<rings&&j<section){int c=index+section+1;t.AddRange(new[]{index,c,index+1,index+1,c,c+1});}
            }
            Geometry.Mesh("Profiled tyre tread",parent,v,t.ToArray(),uv,material,false);
        }
    }
}
