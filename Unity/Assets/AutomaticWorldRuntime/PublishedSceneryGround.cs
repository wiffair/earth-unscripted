using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
 public sealed partial class NewbridgeWorld
 {
  List<Vector3> publishedGroundVertices;List<int> publishedGroundTriangles;int[] publishedGroundCells;FarPatch publishedGroundGrid;
  int sceneryGroundRevision,fineTerrainVisibilityRevision;
  readonly Dictionary<Vector2Int,(GameObject root,MeshCollider ground,MeshRenderer renderer)> sceneryGroundColliders=new Dictionary<Vector2Int,(GameObject,MeshCollider,MeshRenderer)>();
  bool PublishedPhysicalTerrain(Vector2Int key,out MeshCollider collider)
  {
   collider=null;if(!chunks.TryGetValue(key,out var root)||!root||!root.activeInHierarchy)return false;
   if(!sceneryGroundColliders.TryGetValue(key,out var entry)||entry.root!=root||!entry.ground)
   {var child=root.transform.Find("Terrain");entry=(root,child?child.GetComponent<MeshCollider>():null,child?child.GetComponent<MeshRenderer>():null);sceneryGroundColliders[key]=entry;}
   collider=entry.ground;return collider&&collider.enabled&&collider.sharedMesh;
  }
  bool PublishedFineTerrain(Vector2Int key,out MeshCollider collider)
  {
   if(!PublishedPhysicalTerrain(key,out collider))return false;
   var renderer=sceneryGroundColliders[key].renderer;return renderer&&renderer.enabled&&renderer.gameObject.activeInHierarchy;
  }
  public int SceneryGroundRevision=>sceneryGroundRevision;
  public int SceneryCoastRevision=>coastalRevision;
  int scenerySeaRevision=-1;readonly Dictionary<Vector2Int,bool> scenerySeaCells=new Dictionary<Vector2Int,bool>();
  bool SceneryIsSea(float x,float z)
  {
   if(coastalPatch==null)return false;
   if(scenerySeaRevision!=coastalRevision){scenerySeaCells.Clear();scenerySeaRevision=coastalRevision;}
   var key=new Vector2Int(Mathf.FloorToInt(x/8),Mathf.FloorToInt(z/8));
   if(scenerySeaCells.TryGetValue(key,out bool sea))return sea;
   sea=IsMappedSeaAt(new Vector3(x,0,z));if(scenerySeaCells.Count>=400000)scenerySeaCells.Clear();scenerySeaCells[key]=sea;return sea;
  }
  readonly Dictionary<Vector2Int,(int revision,ulong stamp)> sceneryFineStamps=new Dictionary<Vector2Int,(int,ulong)>();
  readonly Dictionary<int,ulong> sceneryFarStamps=new Dictionary<int,ulong>();
  List<Vector3> sceneryFarStampVertices;int[] sceneryFarStampCells;
  // Identity is local to the actual published support. Unrelated chunk arrivals
  // must not hide an unchanged grove; replacement ground must withhold its old
  // matrices until the existing budgeted reindex has grounded them again.
  public ulong PublishedScenerySupportStamp(float x,float z)
  {
   var key=ChunkKey(new Vector3(x,0,z));
   if(PublishedFineTerrain(key,out var collider))
   {
    if(sceneryFineStamps.TryGetValue(key,out var cached)&&cached.revision==sceneryGroundRevision)return cached.stamp;
    var filter=collider.GetComponent<MeshFilter>();ulong stamp=filter&&filter.sharedMesh==collider.sharedMesh?0x100000000UL|(uint)collider.sharedMesh.GetEntityId().GetHashCode():0;
    if(sceneryFineStamps.Count>4096)sceneryFineStamps.Clear();sceneryFineStamps[key]=(sceneryGroundRevision,stamp);return stamp;
   }
   var grid=publishedGroundGrid;if(!farRoot||grid==null||publishedGroundCells==null)return 0;
   int ix=Mathf.FloorToInt((x-grid.minX)/grid.step),iz=Mathf.FloorToInt((z-grid.minZ)/grid.step);
   if(ix<0||iz<0||ix>=grid.nx-1||iz>=grid.nz-1)return 0;
   if(sceneryFarStampVertices!=publishedGroundVertices||sceneryFarStampCells!=publishedGroundCells)
   {sceneryFarStamps.Clear();sceneryFarStampVertices=publishedGroundVertices;sceneryFarStampCells=publishedGroundCells;}
   int cell=(iz*(grid.nx-1)+ix)*2,start=publishedGroundCells[cell],count=publishedGroundCells[cell+1];if(count==0)return 0;
   if(sceneryFarStamps.TryGetValue(cell,out ulong existing))return existing;
   ulong hash=14695981039346656037UL;
   unchecked{for(int i=start;i<start+count;i++){var point=publishedGroundVertices[publishedGroundTriangles[i]];hash=(hash^(uint)point.x.GetHashCode())*1099511628211UL;hash=(hash^(uint)point.y.GetHashCode())*1099511628211UL;hash=(hash^(uint)point.z.GetHashCode())*1099511628211UL;}}
   hash|=0x8000000000000000UL;sceneryFarStamps[cell]=hash;return hash;
  }
  public bool HasPublishedScenerySupport(float x,float z)
  {
   if(PublishedFineTerrain(ChunkKey(new Vector3(x,0,z)),out _))return true;
   var grid=publishedGroundGrid;if(!farRoot||grid==null||publishedGroundCells==null)return false;
   int ix=Mathf.FloorToInt((x-grid.minX)/grid.step),iz=Mathf.FloorToInt((z-grid.minZ)/grid.step);
   return ix>=0&&iz>=0&&ix<grid.nx-1&&iz<grid.nz-1&&publishedGroundCells[(iz*(grid.nx-1)+ix)*2+1]>0;
  }
  public bool TryPublishedSceneryGround(float x,float z,out float height)
  {height=0;return !SceneryIsSea(x,z)&&TryPublishedTerrainHeight(x,z,out height);}
  public bool TryPublishedTerrainHeight(float x,float z,out float height)
  {
   height=0;var key=ChunkKey(new Vector3(x,0,z));
   if(PublishedFineTerrain(key,out var collider))
   {
    // A corrected visual preview can precede its collider bake. The old high
    // collider is not visible-ground authority; withhold trees until they match.
    var filter=collider.GetComponent<MeshFilter>();if(!filter||filter.sharedMesh!=collider.sharedMesh)return false;
    if(collider.Raycast(new Ray(new Vector3(x,10000,z),Vector3.down),out var hit,24000)){height=hit.point.y;return true;}
   }
   var grid=publishedGroundGrid;if(!farRoot||grid==null||publishedGroundCells==null)return false;
   int ix=Mathf.FloorToInt((x-grid.minX)/grid.step),iz=Mathf.FloorToInt((z-grid.minZ)/grid.step);
   if(ix<0||iz<0||ix>=grid.nx-1||iz>=grid.nz-1)return false;
   int cell=(iz*(grid.nx-1)+ix)*2,start=publishedGroundCells[cell],count=publishedGroundCells[cell+1];
   // Sample the emitted triangle list, including subdivided boundary fans.
   // A bilinear raw-height lookup does not describe the published surface.
   for(int i=start;i<start+count;i+=3)
   {
    var a=publishedGroundVertices[publishedGroundTriangles[i]];var b=publishedGroundVertices[publishedGroundTriangles[i+1]];var c=publishedGroundVertices[publishedGroundTriangles[i+2]];
    float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(denominator)<.000001f)continue;
    float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/denominator;
    float v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/denominator;
    if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
    height=a.y*u+b.y*v+c.y*(1-u-v);return true;
   }
   return false;
  }
  [System.Serializable] public sealed class SeaTerrainEvidence
  {public int samples,incorrectAboveSea;public float maximumY;public List<Vector3> failures=new List<Vector3>();}
  public bool IsInteriorMappedSea(Vector3 point,float margin=64)
  {
   return IsMappedSeaAt(point)&&IsMappedSeaAt(point+Vector3.left*margin)&&IsMappedSeaAt(point+Vector3.right*margin)&&IsMappedSeaAt(point+Vector3.forward*margin)&&IsMappedSeaAt(point+Vector3.back*margin);
  }
  public SeaTerrainEvidence InspectVisibleCoastalTerrain(Vector3 centre,float radius)
  {
   var evidence=new SeaTerrainEvidence();
   foreach(var pair in chunks)
   {
    if(!PublishedFineTerrain(pair.Key,out var collider))continue;
    var filter=collider.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;
    var matrix=filter.transform.localToWorldMatrix;var vertices=filter.sharedMesh.vertices;
    for(int i=0;i<vertices.Length;i+=16)
    {
     var point=matrix.MultiplyPoint3x4(vertices[i]);if(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(centre.x,centre.z))>radius||!IsInteriorMappedSea(point))continue;
     evidence.samples++;evidence.maximumY=Mathf.Max(evidence.maximumY,point.y);
     if(point.y>MappedSeaLevel+.5f&&!HasDrivingSurfaceAt(point+Vector3.up*.2f))
     {evidence.incorrectAboveSea++;if(evidence.failures.Count<40)evidence.failures.Add(point);}
    }
   }
   return evidence;
  }
 }
}
