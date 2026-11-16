using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EarthUnscripted.TerrainDraft
{
    // Supplied FINAL float buffers, not source authoring or a geometry admission.
    // The renderer/collider must consume these same local buffers and origin.
    public sealed class LocalTerrainMesh
    {
        public readonly string Id;
        public readonly double OriginX,OriginY,OriginZ;
        public readonly float[] XYZ;
        public readonly int[] Indices;
        public LocalTerrainMesh(string id,double x,double y,double z,float[] xyz,int[] indices)
        {Id=id;OriginX=x;OriginY=y;OriginZ=z;XYZ=xyz;Indices=indices;}
    }
    public sealed class TerrainSurfaceLimits
    {
        public int MaximumPackets=8100,MaximumVertices=2000000,MaximumTriangles=4000000,MaximumReferences=12000000,MaximumCandidatesPerCell=24000;
        public double IndexCellMetres=32,AgreementMetres=.0002;
        internal void Validate()
        {
            if(MaximumPackets<1||MaximumVertices<3||MaximumTriangles<1||MaximumReferences<1||MaximumCandidatesPerCell<1||!TerrainTriangleSurface.Finite(IndexCellMetres)||IndexCellMetres<1||IndexCellMetres>320||!TerrainTriangleSurface.Finite(AgreementMetres)||AgreementMetres<0||AgreementMetres>.0002)throw new ArgumentException("Explicit finite terrain surface limits required");
        }
    }
    public sealed class TerrainTriangleSurface
    {
        public readonly struct Hit
        {
            public readonly double Height,NormalX,NormalY,NormalZ,MaximumAgreementGap;
            public readonly string PacketId;
            public readonly int Triangle,Matches;
            internal Hit(double y,double nx,double ny,double nz,string id,int triangle,int matches,double gap)
            {Height=y;NormalX=nx;NormalY=ny;NormalZ=nz;PacketId=id;Triangle=triangle;Matches=matches;MaximumAgreementGap=gap;}
        }
        readonly struct Reference
        {internal readonly int Packet,Offset;internal Reference(int p,int o){Packet=p;Offset=o;}}
        readonly LocalTerrainMesh[] packets;
        readonly Dictionary<(int,int),Reference[]> cells;
        readonly double cellMetres,agreement;
        readonly int maximumCandidates;
        public string SourceDigest{get;}
        public string SeamDigest{get;}
        public string GeometryDigest{get;}
        public int PacketCount=>packets.Length;
        public int VertexCount{get;}
        public int TriangleCount{get;}
        public int ReferenceCount{get;}
        public const string Representation="EarthUnscripted/local-f32-terrain-triangles/1";
        internal static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        static void Require(bool b,string why){if(!b)throw new InvalidDataException("Final terrain triangles: "+why);}
        static bool Hash(string s)=>s!=null&&s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        static int Cell(double x,double size)
        {double n=Math.Floor(x/size);Require(Finite(n)&&n>=int.MinValue&&n<=int.MaxValue,"index coordinate");return (int)n;}
        static void Point(LocalTerrainMesh p,int vertex,out double x,out double y,out double z)
        {int i=vertex*3;x=p.OriginX+p.XYZ[i];y=p.OriginY+p.XYZ[i+1];z=p.OriginZ+p.XYZ[i+2];}
        public TerrainTriangleSurface(IReadOnlyList<LocalTerrainMesh> input,string sourceDigest,string seamDigest,TerrainSurfaceLimits limits,CancellationToken cancellation=default)
        {
            if(limits==null)throw new ArgumentNullException(nameof(limits));limits.Validate();
            Require(input!=null&&input.Count>0&&input.Count<=limits.MaximumPackets&&Hash(sourceDigest)&&Hash(seamDigest),"source/seam identity and packet budget");
            SourceDigest=sourceDigest;SeamDigest=seamDigest;cellMetres=limits.IndexCellMetres;agreement=limits.AgreementMetres;maximumCandidates=limits.MaximumCandidatesPerCell;
            cancellation.ThrowIfCancellationRequested();
            // A stable identity order also makes edge-hit selection independent
            // of worker completion/insertion order. All snapshots are owned.
            var ordered=input.OrderBy(p=>p?.Id,StringComparer.Ordinal).ToArray();packets=new LocalTerrainMesh[ordered.Length];
            var ids=new HashSet<string>(StringComparer.Ordinal);var pending=new Dictionary<(int,int),List<Reference>>();int vertices=0,triangles=0,references=0;
            for(int pi=0;pi<ordered.Length;pi++)
            {
                cancellation.ThrowIfCancellationRequested();var source=ordered[pi];
                Require(source!=null&&!string.IsNullOrEmpty(source.Id)&&source.Id.Length<=256&&!source.Id.Any(char.IsControl)&&ids.Add(source.Id),"unique bounded packet identity");
                Require(Finite(source.OriginX)&&Finite(source.OriginY)&&Finite(source.OriginZ)&&Math.Abs(source.OriginX)<=10000000&&Math.Abs(source.OriginZ)<=10000000&&Math.Abs(source.OriginY)<20000,"finite local origin");
                Require(source.XYZ!=null&&source.XYZ.Length>=9&&source.XYZ.Length%3==0&&source.Indices!=null&&source.Indices.Length>0&&source.Indices.Length%3==0,"triangle buffer shape");
                Require(source.XYZ.Length/3<=limits.MaximumVertices-vertices&&source.Indices.Length/3<=limits.MaximumTriangles-triangles,"aggregate buffer budget");vertices+=source.XYZ.Length/3;triangles+=source.Indices.Length/3;
                var p=new LocalTerrainMesh(source.Id,source.OriginX,source.OriginY,source.OriginZ,(float[])source.XYZ.Clone(),(int[])source.Indices.Clone());packets[pi]=p;
                for(int i=0;i<p.XYZ.Length/3;i++)
                {if((i&1023)==0)cancellation.ThrowIfCancellationRequested();Point(p,i,out double x,out double y,out double z);Require(Finite(x)&&Finite(y)&&Finite(z)&&Math.Abs(x)<=10000000&&Math.Abs(z)<=10000000&&Math.Abs(y)<20000,"finite uploaded vertex");}
                for(int i=0;i<p.Indices.Length;i+=3)
                {
                    cancellation.ThrowIfCancellationRequested();int a=p.Indices[i],b=p.Indices[i+1],c=p.Indices[i+2];Require(a>=0&&b>=0&&c>=0&&a<p.XYZ.Length/3&&b<p.XYZ.Length/3&&c<p.XYZ.Length/3,"local triangle index");
                    Point(p,a,out double ax,out _,out double az);Point(p,b,out double bx,out _,out double bz);Point(p,c,out double cx,out _,out double cz);
                    double area=(bz-az)*(cx-ax)-(bx-ax)*(cz-az);Require(Finite(area)&&area>1e-12,"upward nondegenerate height-field triangle");
                    int x0=Cell(Math.Min(ax,Math.Min(bx,cx)),cellMetres),x1=Cell(Math.Max(ax,Math.Max(bx,cx)),cellMetres),z0=Cell(Math.Min(az,Math.Min(bz,cz)),cellMetres),z1=Cell(Math.Max(az,Math.Max(bz,cz)),cellMetres);
                    long count=((long)x1-x0+1)*((long)z1-z0+1);Require(count<=limits.MaximumReferences-references,"spatial reference preflight budget");
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    {if((references&1023)==0)cancellation.ThrowIfCancellationRequested();if(!pending.TryGetValue((x,z),out var list))pending.Add((x,z),list=new List<Reference>());Require(list.Count<maximumCandidates,"complete cell candidate budget; no truncation");list.Add(new Reference(pi,i));references++;}
                }
            }
            cancellation.ThrowIfCancellationRequested();cells=pending.ToDictionary(p=>p.Key,p=>p.Value.ToArray());VertexCount=vertices;TriangleCount=triangles;ReferenceCount=references;GeometryDigest=Digest(cancellation);
        }
        string Digest(CancellationToken cancellation)
        {
            using(var sha=SHA256.Create())using(var stream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write))
            {
                using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
                {
                    void Text(string s){byte[] bytes=Encoding.UTF8.GetBytes(s);w.Write(bytes.Length);w.Write(bytes);}
                    Text(Representation);Text(SourceDigest);Text(SeamDigest);w.Write(packets.Length);
                    foreach(var p in packets){cancellation.ThrowIfCancellationRequested();Text(p.Id);w.Write(p.OriginX);w.Write(p.OriginY);w.Write(p.OriginZ);w.Write(p.XYZ.Length);for(int i=0;i<p.XYZ.Length;i++){if((i&4095)==0)cancellation.ThrowIfCancellationRequested();w.Write(p.XYZ[i]);}w.Write(p.Indices.Length);foreach(int index in p.Indices)w.Write(index);}w.Flush();
                }
                stream.FlushFinalBlock();return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
        }
        public LocalTerrainMesh CopyForRenderer(string id)
        {var p=packets.SingleOrDefault(p=>p.Id==id);if(p==null)throw new ArgumentException("Unknown terrain packet",nameof(id));return new LocalTerrainMesh(p.Id,p.OriginX,p.OriginY,p.OriginZ,(float[])p.XYZ.Clone(),(int[])p.Indices.Clone());}
        public bool TryHeight(double worldX,double worldZ,out Hit hit,CancellationToken cancellation=default)
        {
            hit=default;if(!Finite(worldX)||!Finite(worldZ))throw new ArgumentOutOfRangeException("Finite query required");cancellation.ThrowIfCancellationRequested();
            if(!cells.TryGetValue((Cell(worldX,cellMetres),Cell(worldZ,cellMetres)),out var candidates))return false;
            double firstY=0,nx=0,ny=0,nz=0,gap=0;int matches=0,firstPacket=0,firstTriangle=0;
            for(int i=0;i<candidates.Length;i++)
            {
                if((i&127)==0)cancellation.ThrowIfCancellationRequested();var r=candidates[i];var p=packets[r.Packet];
                Point(p,p.Indices[r.Offset],out double ax,out double ay,out double az);Point(p,p.Indices[r.Offset+1],out double bx,out double by,out double bz);Point(p,p.Indices[r.Offset+2],out double cx,out double cy,out double cz);
                double dx=worldX-ax,dz=worldZ-az,bx0=bx-ax,bz0=bz-az,cx0=cx-ax,cz0=cz-az,den=bx0*cz0-bz0*cx0;
                double u=(dx*cz0-dz*cx0)/den,v=(bx0*dz-bz0*dx)/den;
                const double edgeTolerance=1e-10;if(u< -edgeTolerance||v< -edgeTolerance||u+v>1+edgeTolerance)continue;
                double y=ay+u*(by-ay)+v*(cy-ay);Require(Finite(y),"nonfinite final triangle interpolation");
                if(matches==0){firstY=y;firstPacket=r.Packet;firstTriangle=r.Offset/3;nx=(by-ay)*cz0-bz0*(cy-ay);ny=bz0*cx0-bx0*cz0;nz=bx0*(cy-ay)-(by-ay)*cx0;double length=Math.Sqrt(nx*nx+ny*ny+nz*nz);nx/=length;ny/=length;nz/=length;}
                else{gap=Math.Max(gap,Math.Abs(y-firstY));Require(gap<=agreement,"conflicting terrain heights; no upper-surface fallback");}matches++;
            }
            if(matches==0)return false;hit=new Hit(firstY,nx,ny,nz,packets[firstPacket].Id,firstTriangle,matches,gap);return true;
        }
    }
}
