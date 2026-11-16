using System;
using System.Threading;
using UnityEngine;

namespace RoadRush
{
    // Fresh, exclusively owned numeric data. Unity mesh/material/collider creation
    // remains on the main thread. Keep the original terrain sampling and winding.
    public sealed class DefaultTerrainPlan
    {
        public const int Columns=160,Rows=200;
        public const float Spacing=12;
        public readonly RoutePath Owner;
        public readonly Vector3[] Vertices;
        public readonly Vector2[] UV;
        public readonly int[] Triangles;
        DefaultTerrainPlan(RoutePath owner,Vector3[] vertices,Vector2[] uv,int[] triangles)
        {Owner=owner;Vertices=vertices;UV=uv;Triangles=triangles;}
        public static DefaultTerrainPlan Prepare(RoutePath route,CancellationToken token)
        {
            if(route==null||route.Region!=null)throw new ArgumentException("Default terrain requires its owned procedural route");
            token.ThrowIfCancellationRequested();
            var vertices=new Vector3[(Columns+1)*(Rows+1)];
            var uv=new Vector2[vertices.Length];var triangles=new int[Columns*Rows*6];
            for(int z=0;z<=Rows;z++)for(int x=0;x<=Columns;x++)
            {
                if((x&31)==0)token.ThrowIfCancellationRequested();
                int i=z*(Columns+1)+x;float wx=(x-Columns/2)*Spacing,wz=(z-Rows/2)*Spacing;
                vertices[i]=new Vector3(wx,route.TerrainHeight(wx,wz),wz);uv[i]=new Vector2(wx/12,wz/12);
            }
            int k=0;
            for(int z=0;z<Rows;z++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<Columns;x++)
                {
                    int a=z*(Columns+1)+x;
                    triangles[k++]=a;triangles[k++]=a+Columns+1;triangles[k++]=a+1;
                    triangles[k++]=a+1;triangles[k++]=a+Columns+1;triangles[k++]=a+Columns+2;
                }
            }
            token.ThrowIfCancellationRequested();return new DefaultTerrainPlan(route,vertices,uv,triangles);
        }
    }
}
