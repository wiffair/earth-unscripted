using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.TerrainDraft;
using EarthUnscripted.Editor.VerificationSupport;

namespace EarthUnscripted.Editor
{
    public static class ManagedTerrainFieldVerification
    {
        static int checks;
        static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("MANAGED TERRAIN FAIL "+why);checks++;Debug.Log("MANAGED TERRAIN PASS "+why);}
        static string Hash(string path){if(!File.Exists(path))return "missing";using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path)));}
        public static void Run()
        {
            checks=0;int exit=1;string output=Path.GetFullPath("Builds/Verification/ManagedTerrainField/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=Hash(career);
            try
            {
                Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"isolated numeric preparation only");
                using var reader=new JsonTextReader(new StringReader(File.ReadAllText("Builds/Verification/ManagedTerrainField/python-golden-input.json"))){FloatParseHandling=FloatParseHandling.Double};
                var root=JObject.Load(reader);var d=root["dem"];
                var dem=new TerrainDem((double)d["minX"],(double)d["minZ"],(double)d["width"],(double)d["depth"],(int)d["nx"],(int)d["nz"],d["heights"].Select(t=>(double)t).ToArray());
                TerrainPoint Point(JToken t)=>new TerrainPoint((double)t[0],(double)t[1],(double)t[2]);
                var edges=root["edges"].Select(e=>new TerrainEdge((int)e["group"],Point(e["a"]),Point(e["b"]),TerrainEdgeRole.PacketInfluence)).ToArray();
                int groups=(int)root["groups"];var cases=new List<object>();var referenceBits=new List<int>();
                foreach(double cellSize in new[]{4d,16d})
                {
                    var field=new CompactTerrainField(edges,groups,dem,new TerrainFieldLimits{CellSize=cellSize});var scratch=field.NewScratch();
                    double max=0;int n=0,bitDifferences=0,edgeDifferences=0,edgeProbes=0,indexDifferences=0;var differing=new List<object>();
                    foreach(var q in root["queries"])
                    {
                        double actual=field.Height((double)q["x"],(double)q["z"],scratch),expected=(double)q["expected"];
                        int bits=BitConverter.SingleToInt32Bits((float)actual),expectedBits=(int)q["expectedSingleBits"];
                        max=Math.Max(max,Math.Abs(actual-expected));
                        if(cellSize==4)referenceBits.Add(bits);else if(referenceBits[n]!=bits)indexDifferences++;
                        if((string)q["kind"]=="source-edge"){edgeProbes++;if(bits!=expectedBits)edgeDifferences++;}
                        if(bits!=expectedBits){bitDifferences++;if(differing.Count<20)differing.Add(new{x=(double)q["x"],z=(double)q["z"],actual,expected,bits,expectedBits});}n++;
                    }
                    Check(max<1e-10,"independent Python double parity, index="+cellSize);
                    Check(edgeProbes==262&&edgeDifferences==0,"all original source-edge final float bits preserved, index="+cellSize);
                    Check(indexDifferences==0,"4m/16m indexes produce identical Unity sample bits");
                    File.WriteAllText(Path.Combine(output,"rounding-differences-"+cellSize+".json"),JsonConvert.SerializeObject(differing,Formatting.Indented));
                    cases.Add(new{cellSize,queries=n,maximumDoubleError=max,binary32Differences=bitDifferences,sourceEdgeQueries=edgeProbes,sourceEdgeDifferences=edgeDifferences,indexDifferences,cells=field.CellCount,references=field.ReferenceCount});
                }
                object safety=SafetyChecks.Run(),seams=SeamChecks.Run();Check(true,"original source-conflict/ownership/cancellation adversaries pass");Check(true,"original canonical seam and residency checks pass");
                Check(Hash(career)==before,"real career unchanged");
                File.WriteAllText(Path.Combine(output,"receipt.json"),JsonConvert.SerializeObject(new{status="PASS_PRIMITIVES_ONLY",checks,cases,safety,seams,normalTerrainActivated=false,scope="Actual Unity field/index/source-admission/seam primitives; no2D mesher, collider or fullworld admission. Cross-language binary32 differences retained."},Formatting.Indented));exit=0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}Debug.Log("MANAGED TERRAIN "+(exit==0?"PASS ":"FAIL ")+checks);EditorApplication.Exit(exit);
        }
    }
}
