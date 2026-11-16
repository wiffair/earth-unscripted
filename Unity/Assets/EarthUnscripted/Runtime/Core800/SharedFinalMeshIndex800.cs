using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;using System.Threading;using EarthUnscripted.TerrainDraft;
namespace EarthUnscripted.Core800
{
    // Pure final-buffer storage/query, NOT world/source admission. All named-road
    // filters reference this ONE private table; no per-road/stroke XYZ copies.
    public sealed class SharedFinalMeshIndex800
    {
        public readonly struct Hit
        {
            public readonly string PacketId;public readonly int Triangle,Matches;public readonly double Height,NormalX,NormalY,NormalZ,MaximumAgreementGap;
            internal Hit(string id,int triangle,int matches,double y,double nx,double ny,double nz,double gap){PacketId=id;Triangle=triangle;Matches=matches;Height=y;NormalX=nx;NormalY=ny;NormalZ=nz;MaximumAgreementGap=gap;}
        }
        readonly struct Reference{internal readonly int Packet,Offset;internal Reference(int p,int o){Packet=p;Offset=o;}}
        readonly LocalTerrainMesh[] packets;readonly Dictionary<string,int> packetIds=new Dictionary<string,int>(StringComparer.Ordinal);
        readonly Dictionary<(int,int),Reference[]> cells;readonly Dictionary<string,HashSet<int>> roads=new Dictionary<string,HashSet<int>>(StringComparer.Ordinal);
        public int PacketCount=>packets.Length;public int VertexCount{get;}public int TriangleCount{get;}public int ReferenceCount{get;}public string GeometryDigest{get;}
        const int MaxPackets=2048,MaxVertices=500000,MaxTriangles=1000000,MaxReferences=12000000,MaxCandidates=24000;const double CellMetres=8,Agreement=.0002;
        static void Need(bool okay,string why){if(!okay)throw new InvalidDataException("Shared final800 index: "+why);}
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        static int Cell(double x){double n=Math.Floor(x/CellMetres);Need(Finite(n)&&n>=int.MinValue&&n<=int.MaxValue,"finite index position");return(int)n;}
        static bool Name(string s,int max)=>!string.IsNullOrEmpty(s)&&s.Length<=max&&!s.Any(char.IsControl);
        static LocalTerrainMesh Clone(LocalTerrainMesh m)=>new LocalTerrainMesh(m.Id,m.OriginX,m.OriginY,m.OriginZ,(float[])m.XYZ.Clone(),(int[])m.Indices.Clone());
        static void Point(LocalTerrainMesh p,int i,out double x,out double y,out double z){i*=3;x=p.OriginX+p.XYZ[i];y=p.OriginY+p.XYZ[i+1];z=p.OriginZ+p.XYZ[i+2];}
        public SharedFinalMeshIndex800(IReadOnlyList<LocalTerrainMesh> finalBuffers,IReadOnlyDictionary<string,string[]> exactRoadMembers,CancellationToken token=default)
        {
            Need(finalBuffers!=null&&finalBuffers.Count>0&&finalBuffers.Count<=MaxPackets&&exactRoadMembers!=null,"complete bounded input");token.ThrowIfCancellationRequested();
            var ordered=finalBuffers.OrderBy(p=>p?.Id,StringComparer.Ordinal).ToArray();packets=new LocalTerrainMesh[ordered.Length];var pending=new Dictionary<(int,int),List<Reference>>();int vertices=0,triangles=0,references=0;
            for(int pi=0;pi<ordered.Length;pi++)
            {
                token.ThrowIfCancellationRequested();var p=ordered[pi];Need(p!=null&&Name(p.Id,256)&&!packetIds.ContainsKey(p.Id),"unique packet ID");Need(Finite(p.OriginX)&&Finite(p.OriginY)&&Finite(p.OriginZ)&&Math.Abs(p.OriginX)<=10000000&&Math.Abs(p.OriginZ)<=10000000&&Math.Abs(p.OriginY)<20000,"finite origin");
                Need(p.XYZ!=null&&p.XYZ.Length>=9&&p.XYZ.Length%3==0&&p.XYZ.Length/3<=24000&&p.Indices!=null&&p.Indices.Length>0&&p.Indices.Length%3==0&&p.Indices.Length/3<=48000,"complete per-packet buffer budgets");Need(p.XYZ.Length/3<=MaxVertices-vertices&&p.Indices.Length/3<=MaxTriangles-triangles,"whole aggregate budgets; never thin");vertices+=p.XYZ.Length/3;triangles+=p.Indices.Length/3;packets[pi]=p=Clone(p);packetIds.Add(p.Id,pi);
                for(int i=0;i<p.XYZ.Length/3;i++){if((i&1023)==0)token.ThrowIfCancellationRequested();Point(p,i,out double x,out double y,out double z);Need(Finite(x)&&Finite(y)&&Finite(z)&&Math.Abs(x)<=10000000&&Math.Abs(z)<=10000000&&Math.Abs(y)<20000,"finite final vertices");}
                for(int i=0;i<p.Indices.Length;i+=3)
                {
                    if((i&1023)==0)token.ThrowIfCancellationRequested();int a=p.Indices[i],b=p.Indices[i+1],c=p.Indices[i+2];Need(a>=0&&b>=0&&c>=0&&a<p.XYZ.Length/3&&b<p.XYZ.Length/3&&c<p.XYZ.Length/3,"triangle indices");Point(p,a,out double ax,out _,out double az);Point(p,b,out double bx,out _,out double bz);Point(p,c,out double cx,out _,out double cz);double area=(bz-az)*(cx-ax)-(bx-ax)*(cz-az);Need(Finite(area)&&area>1e-12,"upward nondegenerate triangles");int x0=Cell(Math.Min(ax,Math.Min(bx,cx))),x1=Cell(Math.Max(ax,Math.Max(bx,cx))),z0=Cell(Math.Min(az,Math.Min(bz,cz))),z1=Cell(Math.Max(az,Math.Max(bz,cz)));Need(((long)x1-x0+1)*((long)z1-z0+1)<=MaxReferences-references,"spatial reference budget");
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){if((references&1023)==0)token.ThrowIfCancellationRequested();if(!pending.TryGetValue((x,z),out var list))pending.Add((x,z),list=new List<Reference>());Need(list.Count<MaxCandidates,"cell candidate budget; no truncation");list.Add(new Reference(pi,i));references++;}
                }
            }
            int memberships=0;foreach(var pair in exactRoadMembers)
            {token.ThrowIfCancellationRequested();Need(packetIds.TryGetValue(pair.Key,out int p)&&pair.Value!=null&&pair.Value.Length>0&&pair.Value.Length<=32&&pair.Value.Distinct(StringComparer.Ordinal).Count()==pair.Value.Length,"exact packet-member ledger");Need((memberships+=pair.Value.Length)<=4096,"member-reference budget");foreach(string road in pair.Value){Need(Name(road,128),"bounded source-road identity");if(!roads.TryGetValue(road,out var set)){Need(roads.Count<128,"source road count");roads.Add(road,set=new HashSet<int>());}set.Add(p);}}
            token.ThrowIfCancellationRequested();cells=pending.ToDictionary(p=>p.Key,p=>p.Value.ToArray());VertexCount=vertices;TriangleCount=triangles;ReferenceCount=references;GeometryDigest=Digest(token);
        }
        string Digest(CancellationToken token)
        {
            using(var sha=SHA256.Create())using(var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write)){using(var w=new BinaryWriter(stream,new UTF8Encoding(false,true),true)){w.Write("EarthUnscriptedSharedFinal800Index1");w.Write(packets.Length);foreach(var p in packets){token.ThrowIfCancellationRequested();w.Write(p.Id);w.Write(p.OriginX);w.Write(p.OriginY);w.Write(p.OriginZ);w.Write(p.XYZ.Length);foreach(float x in p.XYZ)w.Write(x);w.Write(p.Indices.Length);foreach(int x in p.Indices)w.Write(x);}w.Write(roads.Count);foreach(var road in roads.OrderBy(p=>p.Key,StringComparer.Ordinal)){w.Write(road.Key);var ids=road.Value.Select(i=>packets[i].Id).OrderBy(s=>s,StringComparer.Ordinal).ToArray();w.Write(ids.Length);foreach(string id in ids)w.Write(id);}w.Flush();}stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();}
        }
        public readonly struct NamedTriangle
        {public readonly string PacketId;public readonly int Triangle;public readonly TerrainPoint A,B,C;internal NamedTriangle(string id,int triangle,TerrainPoint a,TerrainPoint b,TerrainPoint c){PacketId=id;Triangle=triangle;A=a;B=b;C=c;}}
        // Value-only traversal: paint builds its spatial index ONCE, not full
        // vertex snapshots perstroke. Shared cap packet appears once perroad.
        public int VisitNamedTriangles(string roadId,Action<NamedTriangle> visitor,CancellationToken token=default)
        {
            if(visitor==null)throw new ArgumentNullException(nameof(visitor));token.ThrowIfCancellationRequested();if(roadId==null||!roads.TryGetValue(roadId,out var selected))return 0;int count=0;
            foreach(int pi in selected.OrderBy(i=>packets[i].Id,StringComparer.Ordinal)){var p=packets[pi];for(int i=0;i<p.Indices.Length;i+=3){if((i&1023)==0)token.ThrowIfCancellationRequested();Point(p,p.Indices[i],out double ax,out double ay,out double az);Point(p,p.Indices[i+1],out double bx,out double by,out double bz);Point(p,p.Indices[i+2],out double cx,out double cy,out double cz);visitor(new NamedTriangle(p.Id,i/3,new TerrainPoint(ax,ay,az),new TerrainPoint(bx,by,bz),new TerrainPoint(cx,cy,cz)));count++;}}
            return count;
        }
        public string[] CopyPacketIds()=>packets.Select(p=>p.Id).ToArray();
        public string[] CopyNamedPacketIds(string roadId)=>roadId!=null&&roads.TryGetValue(roadId,out var ids)?ids.Select(i=>packets[i].Id).OrderBy(s=>s,StringComparer.Ordinal).ToArray():Array.Empty<string>();
        public LocalTerrainMesh CopyForUpload(string packetId)=>packetId!=null&&packetIds.TryGetValue(packetId,out int i)?Clone(packets[i]):throw new ArgumentException("Unknown final packet");
        public bool TryGround(double x,double z,out Hit hit,CancellationToken token=default)=>TryQuery(x,z,null,out hit,token);
        public bool TryRoad(string roadId,double x,double z,out Hit hit,CancellationToken token=default){hit=default;token.ThrowIfCancellationRequested();return roadId!=null&&roads.TryGetValue(roadId,out var ids)&&TryQuery(x,z,ids,out hit,token);}
        public bool TryPlacementGround(double x,double z,Func<string,int,bool> excluded,out Hit hit,CancellationToken token=default)
        {bool blocked=false;bool found=TryQuery(x,z,null,out hit,token,(id,t)=>{if(excluded!=null&&excluded(id,t))blocked=true;});if(blocked)hit=default;return found&&!blocked;}
        bool TryQuery(double x,double z,HashSet<int> allowed,out Hit hit,CancellationToken token,Action<string,int> matched=null)
        {
            hit=default;Need(Finite(x)&&Finite(z),"finite query");token.ThrowIfCancellationRequested();if(!cells.TryGetValue((Cell(x),Cell(z)),out var candidates))return false;int matches=0,firstPacket=0,firstTriangle=0;double firstY=0,nx=0,ny=0,nz=0,gap=0;
            for(int i=0;i<candidates.Length;i++)
            {
                if((i&127)==0)token.ThrowIfCancellationRequested();var r=candidates[i];if(allowed!=null&&!allowed.Contains(r.Packet))continue;var p=packets[r.Packet];Point(p,p.Indices[r.Offset],out double ax,out double ay,out double az);Point(p,p.Indices[r.Offset+1],out double bx,out double by,out double bz);Point(p,p.Indices[r.Offset+2],out double cx,out double cy,out double cz);double dx=x-ax,dz=z-az,ux=bx-ax,uz=bz-az,vx=cx-ax,vz=cz-az,den=ux*vz-uz*vx,u=(dx*vz-dz*vx)/den,v=(ux*dz-uz*dx)/den;if(u< -1e-10||v< -1e-10||u+v>1+1e-10)continue;double y=ay+u*(by-ay)+v*(cy-ay);Need(Finite(y),"finite interpolation");
                if(matches==0){firstY=y;firstPacket=r.Packet;firstTriangle=r.Offset/3;nx=(by-ay)*vz-uz*(cy-ay);ny=uz*vx-ux*vz;nz=ux*(cy-ay)-(by-ay)*vx;double length=Math.Sqrt(nx*nx+ny*ny+nz*nz);nx/=length;ny/=length;nz/=length;}else{gap=Math.Max(gap,Math.Abs(y-firstY));Need(gap<=Agreement,"disagreeing physical sheets; no highest-surface fallback");}matched?.Invoke(p.Id,r.Offset/3);matches++;
            }
            if(matches==0)return false;hit=new Hit(packets[firstPacket].Id,firstTriangle,matches,firstY,nx,ny,nz,gap);return true;
        }
    }
}
