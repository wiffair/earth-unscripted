using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using EarthUnscripted.TerrainDraft;

namespace EarthUnscripted.Editor.VerificationSupport
{
public static class TerrainTriangleSurfaceChecks
{
    static int checks;
    static readonly List<string> passed=new List<string>();
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;passed.Add(why);}
    static void Reject(Action a,string why){try{a();}catch(Exception e)when(e is InvalidDataException||e is ArgumentException){Check(true,why);return;}throw new Exception("accepted "+why);}
    static string Hash(char c)=>new string(c,64);
    static TerrainSurfaceLimits Limits()=>new TerrainSurfaceLimits{MaximumPackets=100,MaximumVertices=1000,MaximumTriangles=1000,MaximumReferences=10000,MaximumCandidatesPerCell=1000};
    static LocalTerrainMesh Plane(string id,double originX=0,double originY=0,double originZ=0,float extraY=0)
    {
        // Exactly representable plane y=10+x/4-z/8 in LOCAL coordinates.
        return new LocalTerrainMesh(id,originX,originY,originZ,new float[]{0,10+extraY,0,0,6+extraY,32,32,18+extraY,0,32,14+extraY,32},new[]{0,1,2,2,1,3});
    }
    static TerrainTriangleSurface Build(params LocalTerrainMesh[] p)=>new TerrainTriangleSurface(p,Hash('a'),Hash('b'),Limits());
    [Serializable] public sealed class Report {public string status="PASS_NUMERIC_ONLY",geometryDigest,scope="Final localfloat triangle query only; no native collider or wholeworld admission";public int checks;public List<string> passed;}
    public static Report Execute(string output)
    {
        checks=0;passed.Clear();
            var source=Plane("tile",100000,200,-100000);var surface=Build(source);
            Check(surface.PacketCount==1&&surface.VertexCount==4&&surface.TriangleCount==2,"owned final buffer counts");
            bool samples=true,normals=true;
            for(int x=0;x<=32;x++)for(int z=0;z<=32;z++)
            {
                samples&=surface.TryHeight(100000+x,-100000+z,out var h)&&Math.Abs(h.Height-(210+x*.25-z*.125))<1e-12;
                double n=Math.Sqrt(1+.25*.25+.125*.125);
                normals&=Math.Abs(h.NormalX+.25/n)<1e-14&&Math.Abs(h.NormalY-1/n)<1e-14&&Math.Abs(h.NormalZ-.125/n)<1e-14;
            }
            Check(samples,"all1089 final-triangle samples include vertices and shared diagonal");Check(normals,"normals follow final triangle plane");
            Check(surface.TryHeight(100016,-99984,out var centre)&&centre.Matches==2&&centre.MaximumAgreementGap==0,"shared diagonal agrees exactly");
            Check(!surface.TryHeight(99999,-99984,out _)&&!surface.TryHeight(100033,-99984,out _),"missing required terrain returns false without analytic fallback");
            string digest=surface.GeometryDigest;source.XYZ[1]=999;source.Indices[0]=999;
            Check(surface.TryHeight(100000,-100000,out var unchanged)&&unchanged.Height==210&&surface.GeometryDigest==digest,"caller buffer mutation cannot poison frozen query geometry");
            var renderer=surface.CopyForRenderer("tile");Check(renderer.OriginX==100000&&renderer.OriginY==200&&renderer.XYZ[1]==10&&renderer.Indices[0]==0,"renderer receives same local float buffers and origin");
            renderer.XYZ[1]=-999;Check(surface.TryHeight(100000,-100000,out unchanged)&&unchanged.Height==210,"renderer copy cannot mutate query snapshot");
            var left=Plane("a",0,0,0);var right=Plane("b",32,8,0);var forward=Build(left,right);var reverse=Build(right,left);
            Check(forward.GeometryDigest==reverse.GeometryDigest,"worker completion order does not alter finalized geometry digest");
            Check(forward.TryHeight(32,16,out var a)&&reverse.TryHeight(32,16,out var b)&&a.Height==16&&a.Height==b.Height&&a.PacketId==b.PacketId,"independent local origins share exact seam height and canonical hit owner");
            Check(forward.TryHeight(48,16,out var second)&&second.Height==20,"neighbor local-origin reconstruction");
            var holeLeft=Plane("left",-64);var holeRight=Plane("right",32);
            Check(!Build(holeLeft,holeRight).TryHeight(0,16,out _),"gap between packets remains a miss, never filled from DEM");
            var overlap=Build(Plane("lower"),Plane("upper",extraY:1));Reject(()=>overlap.TryHeight(16,16,out _),"contradictory heights reject rather than select upper/lower surface");
            Check(Build(Plane("same-a"),Plane("same-b")).TryHeight(16,16,out var duplicate)&&duplicate.Matches==4,"equal duplicate coverage is reported, not called topology admission");
            Reject(()=>Build(Plane("same"),Plane("same")),"duplicate packet identity");
            var malformed=Plane("bad");malformed.Indices[0]=4;Reject(()=>Build(malformed),"indices must address local packet vertices");
            malformed=Plane("bad");malformed.XYZ[1]=float.NaN;Reject(()=>Build(malformed),"nonfinite supplied mesh vertex");
            malformed=Plane("bad");malformed.Indices[1]=2;malformed.Indices[2]=1;Reject(()=>Build(malformed),"downward final triangle");
            malformed=Plane("bad");malformed.Indices[2]=1;Reject(()=>Build(malformed),"degenerate final triangle");
            var small=Limits();small.MaximumVertices=3;Reject(()=>new TerrainTriangleSurface(new[]{Plane("v")},Hash('a'),Hash('b'),small),"aggregate vertex budget refuses partial preparation");
            small=Limits();small.MaximumTriangles=1;Reject(()=>new TerrainTriangleSurface(new[]{Plane("t")},Hash('a'),Hash('b'),small),"aggregate triangle budget");
            small=Limits();small.MaximumReferences=1;Reject(()=>new TerrainTriangleSurface(new[]{Plane("r")},Hash('a'),Hash('b'),small),"reference preflight budget");
            small=Limits();small.MaximumCandidatesPerCell=1;Reject(()=>new TerrainTriangleSurface(new[]{Plane("d")},Hash('a'),Hash('b'),small),"dense cell is rejected without truncating candidates");
            Reject(()=>new TerrainTriangleSurface(new[]{Plane("identity")},"claimed",Hash('b'),Limits()),"source identity shape required, no trust inferred");
            Reject(()=>surface.TryHeight(double.NaN,0,out _),"nonfinite query");
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();bool stopped=false;try{new TerrainTriangleSurface(new[]{Plane("c")},Hash('a'),Hash('b'),Limits(),cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"pre-cancelled constructor publishes no surface");
                stopped=false;try{surface.TryHeight(100001,-99999,out _,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"query cancellation");
            }
            var changed=Build(Plane("tile",100000,200,-100000,extraY:.001f));Check(changed.GeometryDigest!=digest,"actual final vertex change alters geometry identity");
        var report=new Report{checks=checks,passed=new List<string>(passed),geometryDigest=digest};
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"receipt.json"),WorldFieldJson.Write(report,true));return report;
    }
}
}
