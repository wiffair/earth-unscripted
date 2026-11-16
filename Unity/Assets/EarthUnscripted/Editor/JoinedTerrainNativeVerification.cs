using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using EarthUnscripted.TerrainDraft;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class JoinedTerrainNativeVerification
    {
        public static void Run()
        {
            string output="/private/tmp/rr019-joined-native-"+DateTime.UtcNow.Ticks;
            Directory.CreateDirectory(output);
            PlayerProfile.StorageOverride=Path.Combine(output,"isolated-profile.json");
            try
            {
                if(!Application.isBatchMode||Application.isPlaying||UnityEngine.Object.FindAnyObjectByType<GameSession>())
                    throw new InvalidOperationException("Explicit isolated batch proof only");
                var proof=global::FiveLinkTerrainPreparation.BuildReviewedFiveLinkFixture(default);
                var adapter=WorldMeshAdapterVerification.Verify(proof,"/private/tmp/rr019-world-mesh-adapter-unity-"+DateTime.UtcNow.Ticks);
                if(adapter.status!="PASS")throw new InvalidOperationException("Final-buffer adapter failed");
                var packets=new JArray();double maxRoadGrade=0,maxTerrainGrade=0;int faces=0;
                foreach(var pair in proof.CopyFinalRoadMeshes().Select(p=>(mesh:p,role:"asphalt")).Concat(proof.CopyFinalTerrainMeshes().Select(p=>(mesh:p,role:"terrain"))))
                {
                    var p=pair.mesh;
                    for(int i=0;i<p.Indices.Length;i+=3)
                    {
                        int a=p.Indices[i]*3,b=p.Indices[i+1]*3,c=p.Indices[i+2]*3;
                        double ux=(double)p.XYZ[b]-p.XYZ[a],uy=(double)p.XYZ[b+1]-p.XYZ[a+1],uz=(double)p.XYZ[b+2]-p.XYZ[a+2];
                        double vx=(double)p.XYZ[c]-p.XYZ[a],vy=(double)p.XYZ[c+1]-p.XYZ[a+1],vz=(double)p.XYZ[c+2]-p.XYZ[a+2];
                        double den=ux*vz-uz*vx;if(den==0)throw new InvalidDataException("Degenerate final face");
                        double gx=(uy*vz-vy*uz)/den,gz=(ux*vy-vx*uy)/den,grade=Math.Sqrt(gx*gx+gz*gz);faces++;
                        if(double.IsNaN(grade)||double.IsInfinity(grade)||grade>(pair.role=="asphalt"?.18:1.2))throw new InvalidDataException("Final actual-buffer grade exceeds role policy");
                        if(pair.role=="asphalt")maxRoadGrade=Math.Max(maxRoadGrade,grade);else maxTerrainGrade=Math.Max(maxTerrainGrade,grade);
                    }
                    packets.Add(new JObject{{"id",p.Id},{"role",pair.role},{"origin",new JArray(p.OriginX,p.OriginY,p.OriginZ)},{"xyz",new JArray(p.XYZ)},{"indices",new JArray(p.Indices)}});
                }
                var data=new JObject{{"format","EarthUnscriptedLocalTerrainCell1"},{"version",1},{"sourceDigest",proof.SourceDigest},{"seamDigest",proof.ScopeCoverageDigest},{"packets",packets}};
                string input=Path.Combine(output,"actual-verified-buffers.json");File.WriteAllText(input,data.ToString());string pin=World13DocumentCodec.Hash(File.ReadAllBytes(input));
                File.WriteAllText(Path.Combine(output,"typed-verifier-receipt.json"),new JObject{{"status","PASS_TYPED_GEOMETRY_AND_ADAPTER_PENDING_NATIVE"},{"adapterChecks",adapter.checks},{"sourceDigest",proof.SourceDigest},{"rawDocumentDigest",proof.RawDocumentDigest},{"finalBufferDigest",proof.FinalBufferDigest},{"inputSHA256",pin},{"faces",faces},{"maxRoadGrade",maxRoadGrade},{"maxTerrainGrade",maxTerrainGrade},{"fullWorldAdmitted",false}}.ToString());
                LocalTerrainCellNativeVerification.RunPinnedFixture(input,pin,output+"-native");
            }
            catch(Exception e)
            {
                File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);
            }
        }
    }
}
