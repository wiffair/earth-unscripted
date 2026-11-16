using System;using System.Threading;
namespace EarthUnscripted.TerrainDraft.Core800 {
 public static class CoreTerrainPreparation {
  // Reviewed fixture producer: opens only independently pinned immutable source
  // files; never reads/writes a live country DB, career or runtime world.
  public static VerifiedCoreTerrain BuildReviewedPantside800(CancellationToken cancellation=default)=>BuildReviewedPantside800(CoreSourceReader.PacketPath,CoreSourceReader.RawPath,cancellation);
  public static VerifiedCoreTerrain BuildReviewedPantside800(string packetPath,string rawPath,CancellationToken cancellation=default){var source=CoreSourceReader.Read(packetPath,rawPath,cancellation);var preparation=new CoreCellPreparation(source);preparation.PrepareAllBorders(cancellation);foreach(var cell in preparation.Cells){cancellation.ThrowIfCancellationRequested();if(cell.Failure!=null)throw new ArgumentException("Core preflight failed at"+cell.X+","+cell.Z+":"+cell.Failure);cell.Proof=preparation.BuildCell(cell,cancellation);}return VerifiedCoreTerrain.IssueComplete(source,preparation.Cells,cancellation);}
 }
}
