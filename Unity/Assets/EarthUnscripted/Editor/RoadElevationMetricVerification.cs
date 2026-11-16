using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    // Numerical reconstruction only: this does not admit a world or its meshes.
    public static class RoadElevationMetricVerification
    {
        static int checks;
        static void Check(bool ok,string why)
        { if(!ok)throw new InvalidOperationException("ELEVATION METRIC FAIL "+why);checks++;Debug.Log("ELEVATION METRIC PASS "+why); }
        static void Near(double actual,double expected,double tolerance,string why)
        { Check(Math.Abs(actual-expected)<=tolerance,why+" actual="+actual.ToString("R")+" expected="+expected.ToString("R")); }
        static string FileHash(string path)
        { if(!File.Exists(path))return "missing";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        static RoadElevationCurve.Sample Sample(RoadElevationCurve c,double s,double lateral)=>c.Evaluate(s,lateral);
        static void Equal(RoadElevationCurve.Sample a,RoadElevationCurve.Sample b,string why)
        {
            bool Bits(double x,double y)=>BitConverter.DoubleToInt64Bits(x)==BitConverter.DoubleToInt64Bits(y);
            Check(Bits(a.Elevation,b.Elevation)&&Bits(a.LongitudinalGrade,b.LongitudinalGrade)&&Bits(a.LateralGrade,b.LateralGrade)&&Bits(a.CentreElevation,b.CentreElevation)&&Bits(a.CentreGrade,b.CentreGrade)&&Bits(a.Bank,b.Bank)&&Bits(a.Crown,b.Crown),why);
        }
        static void Verify()
        {
            // Raw XYZ has two different slopes. Published height is independently
            // authored as y=20+0.1H; derivatives must be dY/dH, not dY/d(raw S).
            var road=new GeoRoad{id="metric-source",start="a",end="c",name="Metric fixture",kind="residential",width=6,limit=30,sourceNodeIds=new[]{"a","b","c"},points=new[]{new Vector3(0,0,0),new Vector3(30,12,40),new Vector3(70,3,70)}};
            var metric=new RoadElevationMetric(road);
            Near(metric.HorizontalAtPoint(1),50,0,"first horizontal station");
            Near(metric.HorizontalLength,100,0,"complete horizontal length");
            Near(metric.SourceAtPoint(1),Math.Sqrt(2644),0,"first raw 3D source station");
            Near(metric.SourceLength,Math.Sqrt(2644)+Math.Sqrt(2581),0,"complete raw source length");
            var profile=new RoadElevationProfileDto{roadId=road.id,distances=new[]{0d,metric.SourceAtPoint(1),metric.SourceLength},elevations=new[]{20f,25f,30f},gradeDerivatives=new[]{.1f,.1f,.1f},crossfalls=new[]{0f,0f,0f},crownRates=new[]{.025f,.025f,.025f},crownSmooth=.15f};
            RoadElevationProfileDto.Validate(new[]{profile},new[]{road});
            var curve=new RoadElevationCurve(road,profile);
            var clone=WorldFieldJson.Clone(profile);
            var reconstructed=new RoadElevationCurve(road,clone);
            double knot=metric.SourceAtPoint(1);
            double Below(double v)=>BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v)-1);
            double Above(double v)=>BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v)+1);
            var stations=new[]{0d,metric.ToSource(12.5),Below(knot),knot,Above(knot),metric.ToSource(87.5),metric.SourceLength};
            foreach(double s in stations)
            {
                Near(metric.ToSource(metric.ToHorizontal(s)),s,3e-14,"source/horizontal inverse");
                foreach(double lateral in new[]{-3d,0d,3d})Equal(Sample(curve,s,lateral),Sample(reconstructed,s,lateral),"all reconstructed sample bits exact at source="+s.ToString("R")+" lateral="+lateral);
                Near(curve.Evaluate(s,0).CentreElevation,20+.1*metric.ToHorizontal(s),1e-7,"height follows horizontal metres");
                Near(curve.Evaluate(s,0).CentreGrade,.1,2e-8,"physical longitudinal grade");
                Near(curve.Evaluate(s,-3).Elevation,curve.Evaluate(s,3).Elevation,0,"unbanked crown symmetric");
                Check(curve.Evaluate(s,0).Elevation>curve.Evaluate(s,3).Elevation,"centre crown above road edge");
            }
            foreach(double h in new[]{10d,49.99,50d,50.01,90d})
            {
                const double step=.0001;
                double derivative=(curve.Evaluate(metric.ToSource(h+step),2).Elevation-curve.Evaluate(metric.ToSource(h-step),2).Elevation)/(2*step);
                Near(derivative,curve.Evaluate(metric.ToSource(h),2).LongitudinalGrade,1e-9,"finite-difference dY/dH across source boundary");
            }
            var sample=curve.Evaluate(metric.ToSource(25),2);
            double lateralDerivative=(curve.Evaluate(metric.ToSource(25),2.0001).Elevation-curve.Evaluate(metric.ToSource(25),1.9999).Elevation)/.0002;
            Near(lateralDerivative,sample.LateralGrade,1e-9,"crown lateral derivative");
            road.width=1.25f;
            var narrowField=new RoadElevationField(new[]{road},new[]{clone},Array.Empty<RoadJunctionPatchDto>());
            Check(narrowField.RoadCount==1&&road.width==1.25f,"source-only elevation field reconstructs narrow road without widening");
            Equal(curve.Evaluate(knot,0),narrowField.Evaluate(road.id,knot,0),"narrow field centre samples preserve curve controls");
            string digest=RoadSourceIdentity.Compute(new[]{road});
            var immutable=new RoadElevationCurve(road,profile);
            var original=immutable.Evaluate(knot,2);
            profile.elevations[1]+=10;road.points[1].y+=5;
            Equal(original,immutable.Evaluate(knot,2),"curve owns immutable source and profile snapshots");
            Check(digest!=RoadSourceIdentity.Compute(new[]{road}),"source commitment detects original source mutation");
        }
        public static void Run()
        {
            checks=0;int exit=1;
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=FileHash(career);
            string output=Path.GetFullPath("Builds/Verification/RoadElevationMetric/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            try
            {
                Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"no gameplay or career session");
                Verify();Check(before==FileHash(career),"real career unchanged");
                File.WriteAllText(Path.Combine(output,"receipt.json"),"{\"status\":\"PASS\",\"checks\":"+checks+",\"scope\":\"precise curve reconstruction and physical derivative units; no world/mesh admission\"}");exit=0;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}
            Debug.Log("ELEVATION METRIC "+(exit==0?"PASS ":"FAIL ")+checks);EditorApplication.Exit(exit);
        }
    }
}
