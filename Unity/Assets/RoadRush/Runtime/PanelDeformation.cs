using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    public static class PanelDeformation
    {
        public static void Subdivide(Mesh mesh,int passes)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceMesh(mesh);

            for(int pass=0;pass<passes;pass++)
            {
                var v=new List<Vector3>(mesh.vertices);var uv=new List<Vector2>(mesh.uv);var t=new List<int>();var edges=new Dictionary<ulong,int>();
                int Mid(int a,int b)
                {
                    ulong key=((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
                    if(edges.TryGetValue(key,out int i))return i;i=v.Count;v.Add((v[a]+v[b])*.5f);uv.Add((uv[a]+uv[b])*.5f);edges[key]=i;return i;
                }
                var source=mesh.triangles;
                for(int i=0;i<source.Length;i+=3)
                {int a=source[i],b=source[i+1],c=source[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);t.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});}
                mesh.Clear();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);
            }
            mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
        public static Vector3 Displace(Vector3 vertex,Vector3 original,Vector3 impact,Vector3 inward,float severity)
        {
            float radius=Mathf.Lerp(.45f,1.15f,Mathf.Clamp01(severity/24));float distance=Vector3.Distance(original,impact);
            float weight=Mathf.Pow(Mathf.Clamp01(1-distance/radius),2);float depth=Mathf.Clamp(severity*.028f,0,.45f)*weight;
            var result=vertex+inward*depth;
            // Slight deterministic panel buckling around the indentation rim.
            result.y+=Mathf.Sin(original.x*29+original.z*17)*depth*.16f;
            result=original+Vector3.ClampMagnitude(result-original,.7f);result.y=Mathf.Max(Mathf.Min(.3f,original.y),result.y);return result;
        }
    }
}
