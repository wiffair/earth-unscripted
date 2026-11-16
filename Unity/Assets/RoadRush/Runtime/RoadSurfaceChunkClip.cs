using System;using System.Collections.Generic;using System.IO;using UnityEngine;
namespace RoadRush
{
    // Pure triangle-plane-preserving batches. Identical data drives render/collider.
    public static class RoadSurfaceChunkClip
    {
        public sealed class Data{public Vector3[] Vertices;public int[] Triangles;public Vector2[] UV;}
        public static List<Vector3> Triangle(Vector3 a,Vector3 b,Vector3 c,Vector2Int key,float size)
        {
            if(Vector3.Cross(b-a,c-a).y<0){var swap=b;b=c;c=swap;}var polygon=new List<Vector3>{a,b,c};float west=key.x*size,east=west+size,south=key.y*size,north=south+size;
            for(int plane=0;plane<4&&polygon.Count>0;plane++)
            {
                float Signed(Vector3 p)=>plane==0?p.x-west:plane==1?east-p.x:plane==2?p.z-south:north-p.z;float boundary=plane==0?west:plane==1?east:plane==2?south:north;
                var output=new List<Vector3>(8);var previous=polygon[polygon.Count-1];float pd=Signed(previous);foreach(var next in polygon){float nd=Signed(next);if((pd>=0)!=(nd>=0)){var p=Vector3.LerpUnclamped(previous,next,pd/(pd-nd));if(plane<2)p.x=boundary;else p.z=boundary;output.Add(p);}if(nd>=0)output.Add(next);previous=next;pd=nd;}polygon=output;
            }
            for(int i=polygon.Count-1;i>=0&&polygon.Count>1;i--)if((polygon[i]-polygon[(i+1)%polygon.Count]).sqrMagnitude<1e-12f)polygon.RemoveAt(i);if(polygon.Count<3)return null;if(polygon.Count>8)throw new InvalidDataException("Surface chunk polygon budget");return polygon;
        }
        public static void Append(List<Vector3> vertices,List<int> triangles,List<Vector2> uv,IReadOnlyList<Vector3> polygon)
        {
            if(polygon==null)return;int start=vertices.Count;foreach(var p in polygon){vertices.Add(p);uv.Add(new Vector2(p.x/8,p.z/8));}for(int i=1;i<polygon.Count-1;i++)if(Vector3.Cross(polygon[i]-polygon[0],polygon[i+1]-polygon[0]).y>1e-8f){triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
        }
        public static IEnumerable<Data> Batches(Vector3[] vertices,int[] triangles,Vector2Int key,float size)
        {
            var v=new List<Vector3>(8192);var t=new List<int>(8192*2);var uv=new List<Vector2>(8192);int work=0;for(int i=0;i<triangles.Length;i+=3){var polygon=Triangle(vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]],key,size);if(polygon!=null){if(v.Count+polygon.Count>8192){yield return new Data{Vertices=v.ToArray(),Triangles=t.ToArray(),UV=uv.ToArray()};v.Clear();t.Clear();uv.Clear();}Append(v,t,uv,polygon);}if(++work%32==0)yield return null;}if(t.Count>0)yield return new Data{Vertices=v.ToArray(),Triangles=t.ToArray(),UV=uv.ToArray()};
        }
    }
}
