using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public static class RoadElevationStripVerification
    {
        static int checks;
        static void Check(bool b,string why){if(!b)throw new InvalidOperationException("ELEVATION STRIP FAIL "+why);checks++;Debug.Log("ELEVATION STRIP PASS "+why);}
        static void Reject(Action a,string why){try{a();}catch(InvalidDataException){Check(true,why);return;}throw new InvalidOperationException("Accepted "+why);}
        static string Hash(string path){if(!File.Exists(path))return "missing";using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path)));}
        static GeoRoad Road(string id,params Vector3[] p)=>new GeoRoad{id=id,start=id+"-0",end=id+"-"+(p.Length-1),width=6,name=id,kind="residential",limit=30,points=p,sourceNodeIds=Enumerable.Range(0,p.Length).Select(i=>id+"-"+i).ToArray()};
        static RoadElevationProfileDto Profile(GeoRoad r,float start,float grade)
        {
            var m=new RoadElevationMetric(r);int n=m.PointCount;
            return new RoadElevationProfileDto{roadId=r.id,distances=Enumerable.Range(0,n).Select(m.SourceAtPoint).ToArray(),elevations=Enumerable.Range(0,n).Select(i=>start+grade*(float)m.HorizontalAtPoint(i)).ToArray(),gradeDerivatives=Enumerable.Repeat(grade,n).ToArray(),crossfalls=new float[n],crownRates=Enumerable.Repeat(.025f,n).ToArray()};
        }
        static void Verify()
        {
            var r=Road("slope",new Vector3(0,0,0),new Vector3(30,12,40),new Vector3(70,3,70));
            var m=new RoadElevationMetric(r);var p=Profile(r,20,.1f);var curve=new RoadElevationCurve(r,p);var plan=RoadMarkingPlan.BuildFrames(new[]{r});var frame=plan.Frames[r.id];
            var field=new RoadElevationField(new[]{r},new[]{p},Array.Empty<RoadJunctionPatchDto>());
            string before=RoadSourceIdentity.Compute(new[]{r});
            var data=RoadElevationStripGeometry.Create(r,frame,s=>4+2*s/frame.Length,6,new[]{m.ToSource(12.345),m.ToSource(87.65)},(id,s,l,c,right,dc,dr)=>field.EvaluateAtFrame(id,s,l,c,right,dc,dr));
            Check(data.SourceRoadDigest==before&&before==RoadSourceIdentity.Compute(new[]{r}),"source commitment and raw points unchanged");
            Check(data.SourceS.Contains(m.ToSource(12.345))&&data.SourceS.Contains(m.ToSource(87.65)),"required double source stops retained exactly");
            Check(data.Columns%2==0&&data.RequiredCaps.Length==2&&data.RequiredCaps.All(c=>c.Endpoint),"crown centre sampled and endpoint caps explicitly required");
            bool framesEqual=true,heightsEqual=true,widthsEqual=true,finite=true;
            for(int row=0;row<data.SourceS.Length;row++)
            {
                frame.At(data.LegacyWidthArc[row],out var centre,out var right,data.PreviousSide[row]);
                float width=(float)(4+2*data.LegacyWidthArc[row]/frame.Length);
                widthsEqual&=data.Widths[row]==width;
                for(int col=0;col<=data.Columns;col++)
                {
                    double lateral=(4+2*data.LegacyWidthArc[row]/frame.Length)*(col/(double)data.Columns-.5);
                    var expected=centre+right*(float)lateral;var actual=data.Vertices[row*(data.Columns+1)+col];
                    framesEqual&=expected.x==actual.x&&expected.z==actual.z;
                    heightsEqual&=actual.y==(float)curve.Evaluate(data.SourceS[row],lateral*Math.Sqrt((double)right.x*right.x+(double)right.z*right.z)).Elevation;
                    finite&=!float.IsNaN(actual.y)&&!float.IsInfinity(actual.y);
                }
            }
            Check(framesEqual,"all uploaded XZ retain existing width/miter frame");Check(heightsEqual&&finite,"all uploaded Y use exact source station and physical lateral crown");Check(widthsEqual,"legacy width station semantics preserved");
            var almost=Road("precision",new Vector3(-1000,20,0),new Vector3(.00012345f,21,10));var am=new RoadElevationMetric(almost);var af=RoadMarkingPlan.BuildFrames(new[]{almost}).Frames[almost.id];
            // Mono can retain wide subtraction intermediates, so its two arcs
            // may coincide. Explicitly freeze a binary32 width-station domain to
            // exercise conversion without claiming all backends round alike.
            af.Length=(double)(float)af.Length;af.Arc[1]=af.Length;var adapter=new RoadElevationHorizontalFrame(am,af);
            Check(Math.Abs(am.SourceLength-af.Length)>1e-7,"explicit binary32 width-domain fixture differs from exact source arc");
            Check(adapter.LegacySourceDistance(am.SourceLength)==af.Length&&adapter.LegacySourceDistance(0)==0,"source-to-width map preserves endpoint domains");
            double exactMid=am.SourceLength*.37;Check(Math.Abs(adapter.LegacySourceDistance(exactMid)-af.Length*.37)<1e-12,"source-to-width mapping uses segment fraction");
            var corner=Road("corner",Vector3.zero,new Vector3(25,0,0),new Vector3(25,0,25));var cm=new RoadElevationMetric(corner);var cp=Profile(corner,10,0);var cc=new RoadElevationCurve(corner,cp);var cf=RoadMarkingPlan.BuildFrames(new[]{corner}).Frames[corner.id];
            var cd=RoadElevationStripGeometry.Create(corner,cf,s=>6,6,Array.Empty<double>(),(id,s,l,c,right,dc,dr)=>cc.EvaluateAtFrame(s,l,right,dr));
            Check(cd.RequiredCaps.Length==3&&cd.RequiredCaps.Count(c=>!c.Endpoint)==1&&cd.RequiredCaps.Single(c=>!c.Endpoint).NodeId==corner.sourceNodeIds[1],"sharp corner carries exact source-node cap requirement");
            Check(cd.SourceS.Count(s=>s==cm.SourceAtPoint(1))==2,"incoming/outgoing corner rows retained without a bridging strip face");
            var low=Road("low",Vector3.zero,new Vector3(0,0,40));var high=Road("high",new Vector3(0,30,0),new Vector3(0,30,40));high.bridge=true;high.layer=1;
            var layered=RoadMarkingPlan.BuildFrames(new[]{low,high});var lp=Profile(low,10,0);var hp=Profile(high,40,0);var layerField=new RoadElevationField(new[]{low,high},new[]{lp,hp},Array.Empty<RoadJunctionPatchDto>());
            RoadElevationStripGeometry.Data Make(GeoRoad road)=>RoadElevationStripGeometry.Create(road,layered.Frames[road.id],s=>6,6,Array.Empty<double>(),(id,s,l,c,right,dc,dr)=>layerField.EvaluateAtFrame(id,s,l,c,right,dc,dr));
            var ld=Make(low);var hd=Make(high);
            Check(ld.Vertices.Length==hd.Vertices.Length&&ld.Vertices.Zip(hd.Vertices,(a,b)=>a.x==b.x&&a.z==b.z&&Math.Abs((b.y-a.y)-30)<.00001).All(b=>b),"coincident XZ roads retain separate road-local heights");
            Reject(()=>RoadElevationStripGeometry.Create(r,frame,s=>double.NaN,6,Array.Empty<double>(),(id,s,l,c,right,dc,dr)=>curve.Evaluate(s,l)),"nonfinite width rejected");
            Reject(()=>RoadElevationStripGeometry.Create(r,frame,s=>7,6,Array.Empty<double>(),(id,s,l,c,right,dc,dr)=>curve.Evaluate(s,l)),"width outside declared maximum rejected");
            Reject(()=>RoadElevationStripGeometry.Create(r,frame,s=>6,6,new[]{m.SourceLength+1},(id,s,l,c,right,dc,dr)=>curve.Evaluate(s,l)),"foreign source stop rejected");
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool rejected=false;try{RoadElevationStripGeometry.Create(r,frame,s=>6,6,Array.Empty<double>(),(id,s,l,c,right,dc,dr)=>curve.Evaluate(s,l),false,cancel.Token);}catch(OperationCanceledException){rejected=true;}Check(rejected,"cancelled preparation publishes no strip");}
        }
        public static void Run()
        {
            checks=0;int exit=1;string output=Path.GetFullPath("Builds/Verification/RoadElevationStrip/"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(output);
            string career=Path.Combine(Application.persistentDataPath,"earth-unscripted-save.json"),before=Hash(career);
            try{Check(!Application.isPlaying&&!UnityEngine.Object.FindAnyObjectByType<GameSession>(),"isolated numeric preparation, no game session");Verify();Check(Hash(career)==before,"real career unchanged");File.WriteAllText(Path.Combine(output,"receipt.json"),"{\"status\":\"PASS\",\"checks\":"+checks+",\"scope\":\"road-local strip generation only; required caps remain separate; no full-world or native admission\"}");exit=0;}
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);}Debug.Log("ELEVATION STRIP "+(exit==0?"PASS ":"FAIL ")+checks);EditorApplication.Exit(exit);
        }
    }
}
