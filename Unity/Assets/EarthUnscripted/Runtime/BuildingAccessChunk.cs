using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    // One owned static mesh/collider per chunk, rather than an object per step.
    public sealed class BuildingAccessChunk : MonoBehaviour
    {
        const int MaximumVertices=131072,MaximumFlights=512;
        readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> triangles=new List<int>();readonly List<Vector2> uv=new List<Vector2>();
        public int FlightCount {get;private set;}public int OverflowCount {get;private set;}public bool Sealed {get;private set;}
        public bool Append(BuildingAccessGeometry.Data data)
        {
            if(data==null||Sealed)return false;
            if(FlightCount>=MaximumFlights||vertices.Count+data.Verts.Length>MaximumVertices){OverflowCount++;return false;}
            int start=vertices.Count;vertices.AddRange(data.Verts);uv.AddRange(data.UV);foreach(int index in data.Tris)triangles.Add(start+index);FlightCount++;return true;
        }
        public void Commit()
        {
            if(Sealed)return;Sealed=true;if(vertices.Count==0)return;
            var mesh=Geometry.Mesh("Supported garden steps",transform,vertices.ToArray(),triangles.ToArray(),uv.ToArray(),BuildingAppearanceMaterials.Access,true);
            mesh.GetComponent<MeshFilter>().sharedMesh.RecalculateTangents();vertices.Clear();triangles.Clear();uv.Clear();
        }
    }
}
