using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
 public sealed partial class NewbridgeWorld
 {
  sealed class QueryCell
  {
   public readonly List<Segment> segments=new List<Segment>();
   public readonly Dictionary<string,List<int>> sources=new Dictionary<string,List<int>>(StringComparer.Ordinal);
  }
  // Chunk queries belong to their numeric core. Keep one complete chunk of
  // boundary context, including an unloaded neighbour's road shoulders. The
  // full768m source profiles/junctions remain unchanged for fitting/rendering.
  static void QueryIndexBounds(Map source,out int west,out int south,out int east,out int north)
  {
   if(source.coreMaxX>source.coreMinX&&source.coreMaxZ>source.coreMinZ)
   {
    west=Mathf.FloorToInt((source.coreMinX-ChunkSize)/SegmentCellSize);south=Mathf.FloorToInt((source.coreMinZ-ChunkSize)/SegmentCellSize);
    east=Mathf.FloorToInt((source.coreMaxX+ChunkSize)/SegmentCellSize);north=Mathf.FloorToInt((source.coreMaxZ+ChunkSize)/SegmentCellSize);
   }
   else{west=south=int.MinValue;east=north=int.MaxValue;}
  }
  static bool InsideQueryIndex(Map source,Vector2Int cell)
  {QueryIndexBounds(source,out int west,out int south,out int east,out int north);return cell.x>=west&&cell.x<=east&&cell.y>=south&&cell.y<=north;}
  static QueryCell FallbackQueryCell(Map source,SpatialData spatial,Vector2Int cell)
  {
   if(spatial.fallbackCells.TryGetValue(cell,out var cached))return cached;
   // Exceptional halo queries retain exactly the old candidate order and
   // geometry. Cache only four such cells; never eagerly duplicate a region's
   // entire planning halo into thousands of unused query buckets.
   var result=new QueryCell();
   foreach(var feature in source.features)
   {
    if(spatial.queryFeatureBounds.TryGetValue(feature,out var bounds)&&(cell.x<bounds.x||cell.x>bounds.z||cell.y<bounds.y||cell.y>bounds.w))continue;
    float station=0;
    for(int i=1;i<feature.points.Length;i++)
    {
     var segment=new Segment{feature=feature,a=feature.points[i-1],b=feature.points[i],centreA=feature.points[i-1],centreB=feature.points[i],pointIndex=i,station=station};
     station+=Vector2.Distance(new Vector2(segment.a.x,segment.a.z),new Vector2(segment.b.x,segment.b.z));
     if(feature.category=="road"&&feature.roadCentreOffset!=0){segment.a+=Join(feature.points,i-1)*feature.roadCentreOffset;segment.b+=Join(feature.points,i)*feature.roadCentreOffset;}
     float margin=feature.width*.5f+12;
     if(cell.x<Mathf.FloorToInt((Mathf.Min(segment.a.x,segment.b.x)-margin)/SegmentCellSize)||cell.x>Mathf.FloorToInt((Mathf.Max(segment.a.x,segment.b.x)+margin)/SegmentCellSize)||
        cell.y<Mathf.FloorToInt((Mathf.Min(segment.a.z,segment.b.z)-margin)/SegmentCellSize)||cell.y>Mathf.FloorToInt((Mathf.Max(segment.a.z,segment.b.z)+margin)/SegmentCellSize))continue;
     string identity=feature.sourceId??feature.id;
     if(!result.sources.TryGetValue(identity,out var own))result.sources[identity]=own=new List<int>();own.Add(result.segments.Count);result.segments.Add(segment);
    }
   }
   while(spatial.fallbackCells.Count>=4)spatial.fallbackCells.Remove(spatial.fallbackOrder.Dequeue());
   spatial.fallbackCells[cell]=result;spatial.fallbackOrder.Enqueue(cell);spatial.fallbackBuilds++;return result;
  }
  public long QueryIndexSegmentCopies{get{long count=0;foreach(var spatial in spatialByMap.Values)count+=spatial.indexedSegmentCopies;return count;}}
  public long QueryIndexUnboundedSegmentCopies{get{long count=0;foreach(var spatial in spatialByMap.Values)count+=spatial.unboundedSegmentCopies;return count;}}
  public int QueryIndexFallbackBuilds
  {get{int count=0;foreach(var spatial in spatialByMap.Values)count+=spatial.fallbackBuilds;return count;}}
 }
}
