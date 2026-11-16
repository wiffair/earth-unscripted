using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading;using RoadRush.TerrainDraft;
namespace RoadRush
{
    // Explicit diagnostic scoped product. This is NOT a prepared-world13 capability.
    // Only the terrain verifier's private-constructed result can enter this factory.
    public sealed class PreparedWorldMeshPlan
    {
        public enum Role {GroundRoad,GroundTerrain}
        public sealed class Packet
        {
            public readonly string Id;public readonly Role SurfaceRole;readonly LocalTerrainMesh mesh;
            internal Packet(Role role,LocalTerrainMesh data){SurfaceRole=role;Id=data.Id;mesh=Clone(data);}
            public LocalTerrainMesh CopyFinalBuffers()=>Clone(mesh);
        }
        readonly Packet[] packets;readonly Dictionary<string,TerrainTriangleSurface> roadQueries;
        readonly TerrainTriangleSurface groundQuery;readonly string[] sourceIds;readonly Dictionary<string,SourceRoadRow[]> sourceFrames;
        public readonly string RawDocumentDigest,SourceDigest,ControlDigest,OwnerLedgerDigest,StructureLedgerDigest,ScopeCoverageDigest,GeometryDigest,DemInputDigest,FinalBufferDigest;
        public readonly double MinX,MinZ,MaxX,MaxZ;
        public bool FullWorldAdmitted=>false;
        public bool SourceSubsetOnly=>true;
        public int PacketCount=>packets.Length;
        public string[] CopySourceIds()=>(string[])sourceIds.Clone();
        public Packet[] CopyPackets()=>packets.Select(p=>new Packet(p.SurfaceRole,p.CopyFinalBuffers())).ToArray();
        static LocalTerrainMesh Clone(LocalTerrainMesh p)=>new LocalTerrainMesh(p.Id,p.OriginX,p.OriginY,p.OriginZ,(float[])p.XYZ.Clone(),(int[])p.Indices.Clone());
        static void Need(bool okay,string why){if(!okay)throw new InvalidDataException("Scoped world mesh adapter: "+why);}
        static bool Hash(string text)=>text!=null&&text.Length==64&&text.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        static void Check(CancellationToken token,Func<bool> current){token.ThrowIfCancellationRequested();if(current!=null&&!current())throw new OperationCanceledException("Scoped mesh request/source pins stale");}
        PreparedWorldMeshPlan(byte[] verifiedRawBytes,VerifiedJoinedTerrain verified,CancellationToken token,Func<bool> current)
        {
            Check(token,current);Need(verifiedRawBytes!=null&&verified!=null,"typed raw and geometry verifier results required");
            byte[] bytes=(byte[])verifiedRawBytes.Clone();RawDocumentDigest=World13DocumentCodec.Hash(bytes);
            var document=WorldFieldJson.Read<World13Document>(new System.Text.UTF8Encoding(false,true).GetString(bytes));
            Need(verified.RawDocumentDigest==RawDocumentDigest,"geometry belongs to another raw document/DEM/feature set");DemInputDigest=verified.DemInputDigest;FinalBufferDigest=verified.FinalBufferDigest;
            SourceDigest=verified.SourceDigest;ControlDigest=verified.ControlDigest;OwnerLedgerDigest=verified.OwnerLedgerDigest;StructureLedgerDigest=verified.StructureLedgerDigest;ScopeCoverageDigest=verified.ScopeCoverageDigest;
            Need(new[]{RawDocumentDigest,DemInputDigest,FinalBufferDigest,SourceDigest,ControlDigest,OwnerLedgerDigest,StructureLedgerDigest,ScopeCoverageDigest}.All(Hash),"bound raw/control/owner/structure/scope identities");
            Need(document.roadAuthority.sourceDigest==SourceDigest,"geometry belongs to another raw source");
            var scope=verified.Scope;Need(scope!=null&&scope.SourceSubsetOnly,"only the explicitly verified diagnostic subset is supported");
            MinX=scope.MinX;MinZ=scope.MinZ;MaxX=scope.MaxX;MaxZ=scope.MaxZ;
            Need(new[]{MinX,MinZ,MaxX,MaxZ}.All(Finite)&&MaxX>MinX&&MaxZ>MinZ&&MaxX-MinX<=320&&MaxZ-MinZ<=320,"bounded diagnostic coverage");
            sourceIds=scope.CopySourceIds();Need(sourceIds!=null&&sourceIds.Length>0&&sourceIds.Length<=128&&sourceIds.Distinct(StringComparer.Ordinal).Count()==sourceIds.Length,"unique bounded scoped sources");
            var roads=document.geography.roads.ToDictionary(r=>r.id,StringComparer.Ordinal);
            foreach(string id in sourceIds)
            {Check(token,current);Need(roads.TryGetValue(id,out var road),"foreign source road in verifier scope");Need(!road.bridge&&!road.tunnel&&road.layer==0,"bridge/tunnel/nonzero-layer requires a separate proven structure query sheet");}
            // The initial verified scope has no post-source additions. Do not run
            // old garage/pit/rail finalization on it or infer synthetic relationships.
            Need(!sourceIds.Any(id=>id=="fictional-service-approach"||id=="fictional-pit-lane"),"augmented service/pit input requires explicit separate authoring admission");
            var roadMeshes=verified.CopyFinalRoadMeshes();var terrainMeshes=verified.CopyFinalTerrainMeshes();
            Need(roadMeshes!=null&&roadMeshes.Length>0&&terrainMeshes!=null&&terrainMeshes.Length>0,"both final road and terrain packets required");
            packets=roadMeshes.Select(m=>new Packet(Role.GroundRoad,m)).Concat(terrainMeshes.Select(m=>new Packet(Role.GroundTerrain,m))).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            Need(packets.Length<=256&&packets.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()==packets.Length,"unique bounded packet set");
            long vertices=0,triangles=0;foreach(var packet in packets)
            {
                Check(token,current);var m=packet.CopyFinalBuffers();Need(m.XYZ!=null&&m.XYZ.Length>=9&&m.XYZ.Length%3==0&&m.Indices!=null&&m.Indices.Length>=3&&m.Indices.Length%3==0,"complete final buffers");vertices+=m.XYZ.Length/3;triangles+=m.Indices.Length/3;
                Need(vertices<=250000&&triangles<=500000,"diagnostic aggregate memory budget");
                Need((double)(float)m.OriginX==m.OriginX&&(double)(float)m.OriginY==m.OriginY&&(double)(float)m.OriginZ==m.OriginZ,"Unity origin must be exactly representable");
                for(int i=0;i<m.XYZ.Length;i+=3){double x=m.OriginX+m.XYZ[i],z=m.OriginZ+m.XYZ[i+2];Need(x>=MinX-.000001&&x<=MaxX+.000001&&z>=MinZ-.000001&&z<=MaxZ+.000001,"packet extends beyond verifier coverage");}
            }
            var limits=new TerrainSurfaceLimits{MaximumPackets=256,MaximumVertices=250000,MaximumTriangles=500000,MaximumReferences=2000000,MaximumCandidatesPerCell=24000,IndexCellMetres=8,AgreementMetres=.0002};
            groundQuery=new TerrainTriangleSurface(packets.Select(p=>p.CopyFinalBuffers()).ToArray(),SourceDigest,ScopeCoverageDigest,limits,token);GeometryDigest=groundQuery.GeometryDigest;
            roadQueries=new Dictionary<string,TerrainTriangleSurface>(StringComparer.Ordinal);
            // Exact cap membership must come from the verifier-owned ledger; never
            // guess nearest roads or treat source-less caps as arbitrary asphalt.
            var memberships=verified.CopyRoadMembers();
            Need(memberships!=null&&memberships.Count==roadMeshes.Length,"complete verified road packet membership ledger");
            foreach(var mesh in roadMeshes)
            {Need(memberships.TryGetValue(mesh.Id,out var members)&&members!=null&&members.Length>0&&members.Length<=32&&members.Distinct(StringComparer.Ordinal).Count()==members.Length&&members.All(id=>sourceIds.Contains(id)),"foreign/missing cap/source membership");}
            long queryVertices=0;foreach(string id in sourceIds)
            {Check(token,current);var selected=roadMeshes.Where(m=>memberships[m.Id].Contains(id)).ToArray();Need(selected.Length>0,"scoped source lacks final physical road packets");queryVertices+=selected.Sum(m=>(long)m.XYZ.Length/3);Need(queryVertices<=1500000,"per-road query snapshot replication budget");roadQueries.Add(id,new TerrainTriangleSurface(selected,SourceDigest,ScopeCoverageDigest,limits,token));}
            sourceFrames=verified.CopyRoadCenterFrames();Need(sourceFrames!=null&&sourceFrames.Count==sourceIds.Length&&sourceIds.All(sourceFrames.ContainsKey),"complete bound source-station/derived-row map");
            int rowCount=0;foreach(var rows in sourceFrames.Values){Need(rows!=null&&rows.Length>=2&&rows.Length<=16384&&(rowCount+=rows.Length)<=100000,"source row map budget");for(int i=0;i<rows.Length;i++)Need(Finite(rows[i].SourceS)&&Finite(rows[i].DerivedH)&&Finite(rows[i].CenterX)&&Finite(rows[i].CenterZ)&&Finite(rows[i].RightX)&&Finite(rows[i].RightZ)&&(i==0||rows[i].SourceS>rows[i-1].SourceS&&rows[i].DerivedH>rows[i-1].DerivedH),"finite monotone source station map");}
            Check(token,current);
        }
        public static PreparedWorldMeshPlan FromVerifiedJoinedScope(CanonicalRaw13Document source,VerifiedJoinedTerrain verified,CancellationToken token=default,Func<bool> stillCurrent=null)
        {if(source==null)throw new ArgumentNullException(nameof(source));return new PreparedWorldMeshPlan(source.Write(token),verified,token,stillCurrent);}
        // Interpolate the ACTUAL emitted C2 rows, not original sourceXZ or a
        // guessed nearest road. Height/tangent then come from the same final face.
        public UnityEngine.Vector3 PointOnSourceRoad(string roadId,double sourceS,double lateral,out UnityEngine.Vector3 forward)
        {
            if(!Finite(sourceS)||!Finite(lateral)||Math.Abs(lateral)>50||roadId==null||!sourceFrames.TryGetValue(roadId,out var rows)||sourceS<rows[0].SourceS||sourceS>rows[rows.Length-1].SourceS)throw new InvalidDataException("Explicit bounded source station/road binding required");
            int lo=0,hi=rows.Length-2;while(lo<hi){int mid=(lo+hi+1)/2;if(rows[mid].SourceS<=sourceS)lo=mid;else hi=mid-1;}var a=rows[lo];var b=rows[lo+1];double t=(sourceS-a.SourceS)/(b.SourceS-a.SourceS),x=a.CenterX+((double)b.CenterX-a.CenterX)*t+(a.RightX+((double)b.RightX-a.RightX)*t)*lateral,z=a.CenterZ+((double)b.CenterZ-a.CenterZ)*t+(a.RightZ+((double)b.RightZ-a.RightZ)*t)*lateral;
            // Exact represented float world point selected for all caller queries.
            float px=(float)x,pz=(float)z;if(!TryRoad(roadId,px,pz,out var hit))throw new InvalidDataException("Derived source row point outside proved final named-road triangles");
            double dx=(double)b.CenterX-a.CenterX,dz=(double)b.CenterZ-a.CenterZ;if(hit.NormalY<=0)throw new InvalidDataException("Final road triangle is not a supported upward sheet");double dy=-(hit.NormalX*dx+hit.NormalZ*dz)/hit.NormalY,length=Math.Sqrt(dx*dx+dy*dy+dz*dz);if(length<=0)throw new InvalidDataException("Degenerate derived road row");forward=new UnityEngine.Vector3((float)(dx/length),(float)(dy/length),(float)(dz/length));return new UnityEngine.Vector3(px,(float)hit.Height,pz);
        }
        public bool Contains(double x,double z)=>Finite(x)&&Finite(z)&&x>=MinX&&x<=MaxX&&z>=MinZ&&z<=MaxZ;
        public bool TryGround(double x,double z,out TerrainTriangleSurface.Hit hit)
        {hit=default;return Contains(x,z)&&groundQuery.TryHeight(x,z,out hit);}
        public bool TryRoad(string roadId,double x,double z,out TerrainTriangleSurface.Hit hit)
        {hit=default;return Contains(x,z)&&roadId!=null&&roadQueries.TryGetValue(roadId,out var surface)&&surface.TryHeight(x,z,out hit);}
        public double RequireGround(double x,double z)
        {if(!TryGround(x,z,out var hit))throw new InvalidDataException("No final ground triangle in proved scope; analytic fallback forbidden");return hit.Height;}
        public double RequireRoad(string roadId,double x,double z)
        {if(!TryRoad(roadId,x,z,out var hit))throw new InvalidDataException("No final road triangle on named proved sheet; nearest/global fallback forbidden");return hit.Height;}
    }
}
