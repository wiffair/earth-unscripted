using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading;using EarthUnscripted.TerrainDraft;using Proof=EarthUnscripted.TerrainDraft.Core800.VerifiedCoreTerrain;using Row=EarthUnscripted.TerrainDraft.Core800.SourceRoadRow;
namespace EarthUnscripted.Core800
{
    // Explicit800m SOURCE SUBSET. Existing32m factories/types/gates untouched.
    public sealed class PreparedWorldMeshPlan800
    {
        public enum Role {GroundRoad,GroundTerrain}
        public readonly struct RootKey:IEquatable<RootKey>
        {public readonly int X,Z;public RootKey(int x,int z){X=x;Z=z;}public bool Equals(RootKey r)=>X==r.X&&Z==r.Z;public override bool Equals(object o)=>o is RootKey r&&Equals(r);public override int GetHashCode()=>unchecked(X*397^Z);public override string ToString()=>X+","+Z;}
        readonly SharedFinalMeshIndex800 index;readonly SideAwareRoadRows800 frames;
        readonly EarthUnscripted.TerrainDraft.Core800.DerivedRoadBankDescriptor[] banks;readonly Dictionary<string,HashSet<int>> bankTriangles=new Dictionary<string,HashSet<int>>(StringComparer.Ordinal);
        readonly Dictionary<RootKey,string[]> roots;readonly Dictionary<string,Role> roles;
        public readonly string RawDocumentDigest,SourceDigest,DemInputDigest,ControlDigest,OwnerLedgerDigest,StructureLedgerDigest,ScopeCoverageDigest,FinalBufferDigest;
        public string GeometryDigest=>index.GeometryDigest;public bool FullWorldAdmitted=>false;public bool SourceSubsetOnly=>true;
        public int PacketCount=>index.PacketCount;public int VertexCount=>index.VertexCount;public int TriangleCount=>index.TriangleCount;public int ReferenceCount=>index.ReferenceCount;
        public const double MinX=-400,MinZ=-400,MaxX=400,MaxZ=400;
        static void Need(bool okay,string why){if(!okay)throw new InvalidDataException("Prepared800 scope: "+why);}
        static bool Hash(string s)=>s!=null&&s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static void Check(CancellationToken token,Func<bool> current){token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("Prepared800 request/source pins stale");}
        PreparedWorldMeshPlan800(byte[] ownedRawBytes,Proof proof,CancellationToken token,Func<bool> current)
        {
            Check(token,current);Need(ownedRawBytes!=null&&proof!=null,"actual owned source and complete producer result required");var document=WorldFieldJson.Read<World13Document>(new System.Text.UTF8Encoding(false,true).GetString(ownedRawBytes));
            Need(proof.SourceSubsetOnly&&proof.MinX==MinX&&proof.MinZ==MinZ&&proof.MaxX==MaxX&&proof.MaxZ==MaxZ,"exact declared diagnostic subset only");RawDocumentDigest=World13DocumentCodec.Hash(ownedRawBytes);Need(proof.RawDocumentDigest==RawDocumentDigest,"producer raw context mismatch");SourceDigest=proof.SourceDigest;DemInputDigest=proof.DemInputDigest;ControlDigest=proof.ControlDigest;OwnerLedgerDigest=proof.OwnerLedgerDigest;StructureLedgerDigest=proof.StructureLedgerDigest;ScopeCoverageDigest=proof.ScopeCoverageDigest;FinalBufferDigest=proof.FinalBufferDigest;
            Need(new[]{RawDocumentDigest,SourceDigest,DemInputDigest,ControlDigest,OwnerLedgerDigest,StructureLedgerDigest,ScopeCoverageDigest,FinalBufferDigest}.All(Hash)&&document.roadAuthority.sourceDigest==SourceDigest,"matching bound source/control/owner/structure/final identities");
            var cells=proof.CopyCells();Need(cells!=null&&cells.Length==676,"all676cells required; no partial commit");var keys=new HashSet<(int,int)>();
            foreach(var cell in cells)
            {
                Check(token,current);Need(cell.CellX>=-13&&cell.CellX<=12&&cell.CellZ>=-13&&cell.CellZ<=12&&keys.Add((cell.CellX,cell.CellZ)),"exact unique global32cell set");Need(cell.MinX==Math.Max(MinX,cell.CellX*32.0)&&cell.MinZ==Math.Max(MinZ,cell.CellZ*32.0)&&cell.MaxX==Math.Min(MaxX,(cell.CellX+1)*32.0)&&cell.MaxZ==Math.Min(MaxZ,(cell.CellZ+1)*32.0),"exact partial edge-cell extent");Need(cell.RootX==(int)Math.Floor(cell.CellX/10.0)&&cell.RootZ==(int)Math.Floor(cell.CellZ/10.0)&&Hash(cell.CoverageDigest)&&Hash(cell.FinalBufferDigest),"complete cell/root and final coverage binding");
            }
            var roadMeshes=proof.CopyFinalRoadMeshes();var terrainMeshes=proof.CopyFinalTerrainMeshes();Need(roadMeshes!=null&&terrainMeshes!=null&&roadMeshes.Length>0&&terrainMeshes.Length>0&&roadMeshes.Length+terrainMeshes.Length<=2048,"bounded complete physical roles");var membership=proof.CopyRoadMembers();Need(membership!=null&&membership.Count==roadMeshes.Length,"exact road-member packet coverage");var sources=document.geography.roads.ToDictionary(r=>r.id,StringComparer.Ordinal);var rawIDs=membership.Values.SelectMany(v=>v??Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();Need(rawIDs.Length>0&&rawIDs.Length<=128,"scoped source count");
            foreach(string id in rawIDs)Need(sources.TryGetValue(id,out var raw)&&!raw.bridge&&!raw.tunnel&&raw.layer==0,"foreign/structural sheet cannot alias ground");
            var all=roadMeshes.Concat(terrainMeshes).ToArray();roles=new Dictionary<string,Role>(StringComparer.Ordinal);var pending=new Dictionary<RootKey,List<string>>();
            void Add(LocalTerrainMesh p,Role role)
            {
                Check(token,current);Need(p!=null&&p.XYZ!=null&&p.XYZ.Length>=9&&p.XYZ.Length%3==0&&!roles.ContainsKey(p.Id),"complete unique packet");Need((double)(float)p.OriginX==p.OriginX&&(double)(float)p.OriginY==p.OriginY&&(double)(float)p.OriginZ==p.OriginZ,"exact Unity packet origin");double x0=double.PositiveInfinity,z0=x0,x1=double.NegativeInfinity,z1=x1;
                for(int i=0;i<p.XYZ.Length;i+=3){double x=p.OriginX+p.XYZ[i],z=p.OriginZ+p.XYZ[i+2];Need(!double.IsNaN(x)&&!double.IsInfinity(x)&&!double.IsNaN(z)&&!double.IsInfinity(z)&&x>=MinX&&x<=MaxX&&z>=MinZ&&z<=MaxZ,"packet beyond verified scope");x0=Math.Min(x0,x);z0=Math.Min(z0,z);x1=Math.Max(x1,x);z1=Math.Max(z1,z);}
                var root=new RootKey((int)Math.Floor(x0/320),(int)Math.Floor(z0/320));Need(root.X>=-2&&root.X<=1&&root.Z>=-2&&root.Z<=1&&x1<=(root.X+1)*320&&z1<=(root.Z+1)*320,"packet must have one exact320root owner; no repacking");if(!pending.TryGetValue(root,out var bucket))pending.Add(root,bucket=new List<string>());Need(bucket.Count<320,"per-root packet budget320; measured258 max");bucket.Add(p.Id);roles.Add(p.Id,role);
            }
            foreach(var p in roadMeshes){Need(membership.ContainsKey(p.Id),"road packet missing membership");Add(p,Role.GroundRoad);}foreach(var p in terrainMeshes){Need(!membership.ContainsKey(p.Id),"terrain cannot masquerade as named road");Add(p,Role.GroundTerrain);}Need(pending.Count==16,"complete16root set");
            banks=proof.CopyDerivedRoadBanks();Need(banks!=null&&banks.Length<=64,"bounded bank ledger");var packetMap=all.ToDictionary(p=>p.Id,StringComparer.Ordinal);
            foreach(var bank in banks){Check(token,current);Need(bank!=null&&Hash(bank.SourceEdgeDigest)&&bank.FinalBufferDigest==FinalBufferDigest&&bank.BlocksPedestrianAnimalPlacement&&bank.ProjectedArea>0&&bank.MaximumGrade>1.2&&bank.MaximumGrade<=2.0,"bound derived-bank metadata");var refs=bank.CopyTriangleReferences();Need(refs!=null&&refs.Length>0&&refs.Length<=100000,"bounded bank faces");foreach(var r in refs){Need(roles.TryGetValue(r.PacketId,out var role)&&role==Role.GroundTerrain&&r.TriangleIndex>=0&&r.TriangleIndex<packetMap[r.PacketId].Indices.Length/3,"bank face must reference final terrain triangle");if(!bankTriangles.TryGetValue(r.PacketId,out var set))bankTriangles.Add(r.PacketId,set=new HashSet<int>());Need(set.Add(r.TriangleIndex),"unique bank triangle ownership");}}
            index=new SharedFinalMeshIndex800(all,membership,token);roots=pending.ToDictionary(p=>p.Key,p=>p.Value.OrderBy(s=>s,StringComparer.Ordinal).ToArray());var sourceRows=proof.CopyRoadCenterFrames();Need(sourceRows!=null&&rawIDs.All(sourceRows.ContainsKey)&&sourceRows.Keys.All(id=>rawIDs.Contains(id)),"source rows match physical named-road membership");frames=new SideAwareRoadRows800(sourceRows);Check(token,current);
        }
        public static PreparedWorldMeshPlan800 FromVerifiedCore(CanonicalRaw13Document source,Proof complete,CancellationToken token=default,Func<bool> stillCurrent=null)
        {if(source==null)throw new ArgumentNullException(nameof(source));return new PreparedWorldMeshPlan800(source.Write(token),complete,token,stillCurrent);}
        public EarthUnscripted.TerrainDraft.Core800.DerivedRoadBankDescriptor[] CopyDerivedRoadBanks()=>(EarthUnscripted.TerrainDraft.Core800.DerivedRoadBankDescriptor[])banks.Clone();
        public bool IsDerivedRoadBank(string packetId,int triangle)=>packetId!=null&&bankTriangles.TryGetValue(packetId,out var set)&&set.Contains(triangle);
        // Placement excludes classified banks; physical support uses the same final surface.
        public bool TryPlacementGround(double x,double z,out SharedFinalMeshIndex800.Hit hit){hit=default;return Contains(x,z)&&index.TryPlacementGround(x,z,IsDerivedRoadBank,out hit);}
        public RootKey[] CopyRootKeys()=>roots.Keys.OrderBy(k=>k.X).ThenBy(k=>k.Z).ToArray();
        public string[] CopyRootPacketIds(RootKey key)=>roots.TryGetValue(key,out var ids)?(string[])ids.Clone():Array.Empty<string>();
        public LocalTerrainMesh CopyForUpload(string id)=>index.CopyForUpload(id);
        public Role PacketRole(string id)=>roles.TryGetValue(id,out var role)?role:throw new ArgumentException("Unknown800packet");
        public string[] CopyNamedRoadPacketIds(string roadId)=>index.CopyNamedPacketIds(roadId);
        public LocalTerrainMesh[] CopyNamedRoadPackets(string roadId)=>index.CopyNamedPacketIds(roadId).Select(index.CopyForUpload).ToArray(); // preparation only, never perstroke
        public int VisitNamedTriangles(string roadId,Action<SharedFinalMeshIndex800.NamedTriangle> visitor,CancellationToken token=default)=>index.VisitNamedTriangles(roadId,visitor,token);
        public Row[] CopySourceRows(string roadId)=>frames.CopyRows(roadId);
        public bool TrySourceFrame(string roadId,double sourceS,RoadRowSide side,out SideAwareRoadRows800.Frame frame)=>frames.TryFrame(roadId,sourceS,side,out frame);
        public bool Contains(double x,double z)=>!double.IsNaN(x)&&!double.IsInfinity(x)&&!double.IsNaN(z)&&!double.IsInfinity(z)&&x>=MinX&&x<=MaxX&&z>=MinZ&&z<=MaxZ;
        public bool TryGround(double x,double z,out SharedFinalMeshIndex800.Hit hit){hit=default;return Contains(x,z)&&index.TryGround(x,z,out hit);}
        public bool TryRoad(string roadId,double x,double z,out SharedFinalMeshIndex800.Hit hit){hit=default;return Contains(x,z)&&index.TryRoad(roadId,x,z,out hit);}
    }
}
