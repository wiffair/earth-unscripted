using System.Collections.Generic;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GeoWorld
    {
        // Existing chunk lifetime owns both renderer and collider. No persistent
        // parallel world is created, and unloaded chunks retain no authored mesh.
        IEnumerable<int> AuthoredRoadSteps(Transform root,Vector2Int key)
        {
            var plan=Region.CircuitRoadSurfaces;if(plan==null)yield break;
            foreach(var part in plan.Parts)
            {
                var vertices=new List<Vector3>(8192);var triangles=new List<int>();var uv=new List<Vector2>();
                void Flush()
                {
                    if(triangles.Count>0)Geometry.Mesh("Authored circuit pavement",root,
                        vertices.ToArray(),triangles.ToArray(),uv.ToArray(),Asphalt,true);
                    vertices.Clear();triangles.Clear();uv.Clear();
                }
                for(int i=0;i<part.Triangles.Count;i+=3)
                {
                    var polygon=RoadSurfaceChunkClip.Triangle(part.Vertices[part.Triangles[i]],
                        part.Vertices[part.Triangles[i+1]],part.Vertices[part.Triangles[i+2]],key,Size);
                    if(polygon!=null)
                    {
                        if(vertices.Count+polygon.Count>8192)Flush();
                        RoadSurfaceChunkClip.Append(vertices,triangles,uv,polygon);
                    }
                    if(i%(3*32)==0)yield return 0;
                }
                Flush();yield return 0;
            }
        }

        // Only exterior terrain is emitted. At its exact cut boundary use a
        // 4mm contact allowance, rather than exposing the hidden 80mm road bed.
        // This leaves the authored driving mesh untouched.
        float AuthoredTerrainSurfaceHeight(float x,float z)
            =>Region.CircuitRoadSurfaces.TryHeight(x,z,out float top)?top-.004f:Region.TerrainHeight(x,z);

        IEnumerable<int> AuthoredTerrainSteps(Transform root,Vector2Int key,Vector3[] original,int[] source)
        {
            // One immutable chunk build owns these bounded exact caches. Clipped
            // triangle fans repeat positions heavily; retain every original
            // sample/float bit while avoiding duplicate field and normal work.
            const int HeightMemoEntries=131072,NormalMemoEntries=65536;
            var heights=new RoadRush.Core.ExactPointFieldMemo<float>(AuthoredTerrainSurfaceHeight,HeightMemoEntries);
            const float normalStep=.25f;
            var normalField=new RoadRush.Core.ExactPointFieldMemo<Vector3>((x,z)=>
            {
                float dx=(heights.Read(x+normalStep,z)-heights.Read(x-normalStep,z))/(2*normalStep);
                float dz=(heights.Read(x,z+normalStep)-heights.Read(x,z-normalStep))/(2*normalStep);
                return new Vector3(-dx,1,-dz).normalized;
            },NormalMemoEntries);
            var vertices=new List<Vector3>(8192);var triangles=new List<int>();var uv=new List<Vector2>();
            var batchNormals=new List<Vector3>(8192);
            void Flush()
            {
                if(triangles.Count>0)
                {
                    var item=Geometry.Mesh("Terrain outside authored pavement",root,
                        vertices.ToArray(),triangles.ToArray(),uv.ToArray(),ground,true);
                    // The identical common-field normals were evaluated during
                    // cooperative face emission, not in one large blocking loop.
                    item.GetComponent<MeshFilter>().sharedMesh.normals=batchNormals.ToArray();
                }
                vertices.Clear();triangles.Clear();uv.Clear();batchNormals.Clear();
            }
            var topology=new AuthoredTerrainTopology();
            var refinement=new AuthoredTerrainRefinement(heights.Read);
            for(int i=0;i<source.Length;i+=3)
            {
                var face=new[]{original[source[i]],original[source[i+1]],original[source[i+2]]};
                RoadBankExteriorClip.Data exterior=null;
                foreach(var step in RoadBankExteriorClip.ClipSteps(face,0,false,
                    Region.CircuitRoadSurfaces.TerrainPavement,value=>exterior=value))yield return step;
                foreach(var step in refinement.AddMeshSteps(exterior.Vertices,exterior.Triangles,topology))yield return step;
                if(i%(3*32)==0)yield return 0;
            }
            // Include one virtual grid-cell ring. Neighbour chunks can introduce
            // different clipping cuts along the common boundary; collecting both
            // sides before conformity makes their emitted edges agree too.
            int n=Mathf.RoundToInt(Mathf.Sqrt(original.Length))-1;
            for(int z=-1;z<=n;z++)for(int x=-1;x<=n;x++)
            {
                if(x>=0&&x<n&&z>=0&&z<n)continue;
                Vector3 Point(int gx,int gz)=>new Vector3((key.x+(float)gx/n)*Size,0,(key.y+(float)gz/n)*Size);
                var a=Point(x,z);var b=Point(x,z+1);var c=Point(x+1,z);var d=Point(x+1,z+1);
                foreach(var face in new[]{new[]{a,b,c},new[]{c,b,d}})
                {
                    RoadBankExteriorClip.Data exterior=null;
                    foreach(var step in RoadBankExteriorClip.ClipSteps(face,0,false,
                        Region.CircuitRoadSurfaces.TerrainPavement,value=>exterior=value))yield return step;
                    foreach(var step in refinement.AddMeshSteps(exterior.Vertices,exterior.Triangles,topology,false))yield return step;
                }
                yield return 0;
            }
            void Append(Vector3 a,Vector3 b,Vector3 c)
            {
                if(vertices.Count+3>60000)Flush();int offset=vertices.Count;
                foreach(var point in new[]{a,b,c})
                {
                    var p=point;p.y=heights.Read(p.x,p.z);vertices.Add(p);
                    // ConformSteps yields between bounded face groups. Spreading
                    // the same normal work here prevents an entire mesh batch
                    // from sampling thousands of normals without yielding.
                    batchNormals.Add(normalField.Read(p.x,p.z));
                    uv.Add(new Vector2((p.x-Region.minX)/Region.width,(p.z-Region.minZ)/Region.depth));
                }
                triangles.Add(offset);triangles.Add(offset+1);triangles.Add(offset+2);
            }
            foreach(var step in topology.ConformSteps(Append))yield return step;
            Flush();
            BootTelemetry.Current?.Mark("authored-terrain.refinement",key.x+","+key.y+" visits="+refinement.Visits+" leaves="+refinement.Leaves+" unresolved="+refinement.UnresolvedLeaves+" maximum_unresolved_error_m="+refinement.MaximumUnresolvedError);
            BootTelemetry.Current?.Mark("authored-terrain.exact-memo",key.x+","+key.y+" heights="+heights.Count+" evaluations="+heights.Evaluations+" hits="+heights.Hits+" normal_evaluations="+normalField.Evaluations+" normal_hits="+normalField.Hits);
            yield return 0;
        }
    }
}
