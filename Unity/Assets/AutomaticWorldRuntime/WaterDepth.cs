using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        readonly Dictionary<Vector2Int,float> seaBedHeights=new Dictionary<Vector2Int,float>();
        public bool TryWaterSurface(Vector3 point,out float height,out bool sea)
        {
            sea=IsMappedSeaAt(point);height=sea?MappedSeaLevel:float.NegativeInfinity;
            if(TryMappedWaterLevel(point,out float mapped))height=Mathf.Max(height,mapped);
            var nearby=Nearby(point.x,point.z);
            if(nearby!=null)foreach(var segment in nearby)
                if(segment.feature.category=="water"&&Project(segment,point.x,point.z,out float surface)<segment.feature.width*.5f)
                    height=Mathf.Max(height,surface);
            return !float.IsNegativeInfinity(height);
        }
        /// <summary>Inferred seabed, not bathymetric survey data. A gentle shore
        /// profile deepens offshore. Road earthworks retain their own support;
        /// bridges retain decks while the bed beneath them can deepen.</summary>
        float LowerSeaBed(float x,float z,float ground)
        {
            if(coastalPatch==null||!IsMappedSeaAt(new Vector3(x,0,z)))return ground;
            var key=new Vector2Int(Mathf.RoundToInt(x/TerrainStep),Mathf.RoundToInt(z/TerrainStep));
            if(!seaBedHeights.TryGetValue(key,out float bed))
            {
                float nearest=250;
                foreach(var ring in coastalPatch.shore??Array.Empty<CoastRing>())
                    for(int index=1;index<(ring.points?.Length??0);index++)
                    {
                        var a=new Vector2(ring.points[index-1].x,ring.points[index-1].z);
                        var b=new Vector2(ring.points[index].x,ring.points[index].z);
                        var point=new Vector2(x,z);var direction=b-a;
                        float station=Mathf.Clamp01(Vector2.Dot(point-a,direction)/Mathf.Max(.0001f,direction.sqrMagnitude));
                        nearest=Mathf.Min(nearest,Vector2.Distance(point,a+direction*station));
                    }
                bed=MappedSeaLevel-Mathf.Clamp(.08f+nearest*.11f,.08f,28);
                if(seaBedHeights.Count<600000)seaBedHeights[key]=bed;
            }
            return Mathf.Min(ground,bed);
        }
        IEnumerator RefreshCoastalBeds()
        {
            // Surface/collider publication is atomic per terrain mesh. The old
            // supported collider remains active throughout asynchronous cooking.
            var roots=new List<GameObject>(chunks.Values);
            foreach(var warm in warmChunks.Values)if(warm.root)roots.Add(warm.root);
            foreach(var root in roots)
            {
                if(!root)continue;
                var child=root.transform.Find("Terrain");if(!child)continue;
                var filter=child.GetComponent<MeshFilter>();var collider=child.GetComponent<MeshCollider>();
                if(!filter||!filter.sharedMesh||!collider)continue;
                var original=filter.sharedMesh;var vertices=original.vertices;bool changed=false;var slice=System.Diagnostics.Stopwatch.StartNew();
                for(int index=0;index<vertices.Length;index++)
                {
                    var point=child.TransformPoint(vertices[index]);
                    if(IsMappedSeaAt(point))
                    {float height=Ground(point.x,point.z);if(point.y>height+.01f){point.y=height;vertices[index]=child.InverseTransformPoint(point);changed=true;}}
                    if(index%16==15&&slice.Elapsed.TotalMilliseconds>2)
                    {RecordGenerationSlice("Coastal bed samples",slice.Elapsed.TotalMilliseconds);yield return null;slice.Restart();}
                }
                if(!changed)continue;
                var replacement=UnityEngine.Object.Instantiate(original);replacement.name=original.name+" seabed";
                replacement.vertices=vertices;replacement.RecalculateNormals();replacement.RecalculateBounds();
                var id=replacement.GetEntityId();var options=collider.cookingOptions;
                var bake=System.Threading.Tasks.Task.Run(()=>Physics.BakeMesh(id,false,options));
                while(!bake.IsCompleted)yield return null;
                bake.GetAwaiter().GetResult();
                if(filter&&collider){filter.sharedMesh=replacement;collider.sharedMesh=replacement;var owner=filter.GetComponent<RoadRush.GeneratedMeshOwner>()??filter.gameObject.AddComponent<RoadRush.GeneratedMeshOwner>();owner.Mesh=replacement;Destroy(original);}
                else Destroy(replacement);
                yield return null;
            }
        }
    }
}
