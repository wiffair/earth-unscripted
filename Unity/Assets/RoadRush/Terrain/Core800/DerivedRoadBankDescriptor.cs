using System;using System.Linq;
namespace RoadRush.TerrainDraft.Core800 {
 public readonly struct DerivedBankTriangleReference {public readonly string PacketId;public readonly int TriangleIndex;internal DerivedBankTriangleReference(string packetId,int index){PacketId=packetId;TriangleIndex=index;}}
 public sealed class DerivedRoadBankDescriptor {
  public string Id{get;}public string Recipe{get;}public string Provenance=>"fictional derived cut bank";public string SourceEdgeDigest{get;}public string FinalBufferDigest{get;}public double ProjectedArea{get;}public double MaximumGrade{get;}public bool BlocksPedestrianAnimalPlacement=>true;
  readonly string[] sourcePacketIds;readonly DerivedBankTriangleReference[] references;readonly double[][] loops;
  // Issued only after complete bank component and final-buffer validation.
  internal DerivedRoadBankDescriptor(string id,string recipe,string sourceEdgeDigest,string finalBufferDigest,string[] sources,DerivedBankTriangleReference[] triangles,double[][] footprintLoops,double area,double maximumGrade){Id=id;Recipe=recipe;SourceEdgeDigest=sourceEdgeDigest;FinalBufferDigest=finalBufferDigest;sourcePacketIds=(string[])sources.Clone();references=(DerivedBankTriangleReference[])triangles.Clone();loops=footprintLoops.Select(p=>(double[])p.Clone()).ToArray();ProjectedArea=area;MaximumGrade=maximumGrade;}
  public string[] CopySourcePacketIds()=>(string[])sourcePacketIds.Clone();public DerivedBankTriangleReference[] CopyTriangleReferences()=>(DerivedBankTriangleReference[])references.Clone();
  // Each loop is flat worldX/worldZ pairs; topological orientation retained.
  public double[][] CopyFootprintLoops()=>loops.Select(p=>(double[])p.Clone()).ToArray();
 }
}
