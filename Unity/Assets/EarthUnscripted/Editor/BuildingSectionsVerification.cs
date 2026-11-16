using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BuildingSectionsVerification
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception("BUILDING SECTIONS FAIL: "+text);checks++;Debug.Log("BUILDING SECTIONS PASS: "+text);}
        static GeoBuilding Building(params float[] xz){var p=new List<Vector3>();for(int i=0;i<xz.Length;i+=2)p.Add(new Vector3(xz[i],0,xz[i+1]));p.Add(p[0]);return new GeoBuilding{id="section-fixture",kind="building",height=6,roof="gabled",points=p.ToArray()};}
        static GeoRegion Region(){var r=new GeoRegion{id="sections-fixture",name="Sections fixture",minX=-500,minZ=-500,width=1000,depth=1000,nx=2,nz=2,heights=new[]{100f,100f,100f,100f},landNx=2,landNz=2,landCover=new int[4],roads=Array.Empty<GeoRoad>(),waters=Array.Empty<GeoWater>(),areas=Array.Empty<GeoArea>(),buildings=Array.Empty<GeoBuilding>(),route=new[]{Vector3.zero,Vector3.right*100}};r.Prepare();return r;}
        static double Area(Vector3[] p){double a=0;for(int i=0;i<p.Length-1;i++)a+=(double)p[i].x*p[i+1].z-(double)p[i+1].x*p[i].z;return Math.Abs(a)*.5;}
        static string Descriptor(BuildingSections.Data data)=>string.Join("|",data.Parts.Select(p=>p.Building.id+":"+p.Site.LevelFloorY.ToString("R")+":"+string.Join(";",p.Building.points.Select(v=>v.ToString("R")))));
        static double Cross(Vector3 a,Vector3 b,Vector3 c)=>((double)b.x-a.x)*(c.z-a.z)-((double)b.z-a.z)*(c.x-a.x);
        static IEnumerable<Vector3[]> Triangles(Vector3[] loop)
        {var p=loop.Take(loop.Length-1).ToArray();var t=GeoWorld.Triangulate(p);for(int i=0;i<t.Length;i+=3){var a=p[t[i]];var b=p[t[i+1]];var c=p[t[i+2]];yield return Cross(a,b,c)>0?new[]{a,b,c}:new[]{c,b,a};}}
        static double Overlap(Vector3[] first,Vector3[] second)
        {
            double total=0;foreach(var ta in Triangles(first))foreach(var tb in Triangles(second))
            {
                var polygon=ta.ToList();for(int edge=0;edge<3;edge++)
                {
                    var a=tb[edge];var b=tb[(edge+1)%3];var clipped=new List<Vector3>();for(int i=0;i<polygon.Count;i++)
                    {var p=polygon[i];var q=polygon[(i+1)%polygon.Count];double da=Cross(a,b,p),db=Cross(a,b,q);if(da>=0)clipped.Add(p);if((da>=0)!=(db>=0)){double t=da/(da-db);clipped.Add(new Vector3((float)(p.x+((double)q.x-p.x)*t),0,(float)(p.z+((double)q.z-p.z)*t)));}}polygon=clipped;
                }
                if(polygon.Count>=3)total+=Area(polygon.Concat(new[]{polygon[0]}).ToArray());
            }return total;
        }
        static float BoundaryDistance(Vector3 p,Vector3[] loop)
        {
            float best=float.PositiveInfinity;for(int i=0;i<loop.Length-1;i++){var a=loop[i];var d=loop[i+1]-a;d.y=0;var delta=p-a;delta.y=0;float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector3.Dot(delta,d)/d.sqrMagnitude):0;var q=a+d*t;q.y=p.y;best=Mathf.Min(best,Vector3.Distance(q,p));}return best;
        }
        static void GeometryChecks(GeoBuilding source,BuildingSections.Data data,string name)
        {
            Check(data.Sectioned&&data.Parts.Length>=2&&data.Parts.Length<=48&&data.StripCount<=24&&data.EdgeWork<=32768,name+" yields bounded derived parts");
            double perimeter=0;for(int i=0;i<source.points.Length-1;i++)perimeter+=Vector3.Distance(source.points[i],source.points[i+1]);double tolerance=Math.Max(.02,perimeter*.002),sum=data.Parts.Sum(p=>Area(p.Building.points));
            Check(Math.Abs(sum-Area(source.points))<=tolerance,name+" preserves total source-footprint area within1mm endpoint quantisation tolerance");
            bool inside=true;foreach(var part in data.Parts)foreach(var p in part.Building.points)inside&=GeoRegion.Contains(source.points,new Vector2(p.x,p.z))||BoundaryDistance(p,source.points)<=.002f;Check(inside,name+" derived outline vertices remain inside or within2mm of the original boundary");
            double overlap=0;for(int a=0;a<data.Parts.Length;a++)for(int b=a+1;b<data.Parts.Length;b++)overlap+=Overlap(data.Parts[a].Building.points,data.Parts[b].Building.points);Check(overlap<=tolerance,name+" sections do not double-cover area beyond the shared-boundary quantisation tolerance");
            Check(data.Parts.All(p=>p.SourceId==source.id&&p.Building.id.StartsWith(source.id+"/site-",StringComparison.Ordinal)&&p.Building.kind==source.kind&&p.Building.height==source.height&&p.Building.roof==source.roof&&p.Site.SampleCount<=64&&p.Site.EntryThresholdY==p.Site.LevelFloorY),name+" source lineage scale roof style and horizontal floor are preserved without inventing addresses");
            Check(data.Parts.Select(p=>p.Building.id).Distinct().Count()==data.Parts.Length&&data.Parts.All(p=>p.StripIndex<24&&p.Building.points.Length<=257),name+" part IDs indices and corner counts stay bounded");
        }
        static void SyntheticChecks()
        {
            var region=Region();Func<float,float,float> surface=(x,z)=>100+x*.12f;var source=Building(0,0,55,0,55,8,0,8);string before=JsonUtility.ToJson(source),world=JsonUtility.ToJson(region);var data=BuildingSections.Create(region,source,surface);GeometryChecks(source,data,"Straight sloping row");Check(!data.HasRoofBurialRisk&&data.Parts.Select(p=>p.Site.LevelFloorY).Distinct().Count()>1,"long sloping row steps level floors with no sampled roof burial");
            var reversed=Building(0,0,55,0,55,8,0,8);reversed.points=reversed.points.Reverse().ToArray();var rotated=Building(55,8,0,8,0,0,55,0);Check(Descriptor(data)==Descriptor(BuildingSections.Create(region,reversed,surface))&&Descriptor(data)==Descriptor(BuildingSections.Create(region,rotated,surface)),"CW CCW and rotated source rings preserve exact ordered part geometry IDs and floor data");
            var reverseOrder=(BuildingSections.Data)typeof(BuildingSections).GetMethod("CreateCore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{region,source,surface,true});Check(Descriptor(data)==Descriptor(reverseOrder),"reversed triangle visitation preserves exact output descriptors");Check(JsonUtility.ToJson(source)==before&&JsonUtility.ToJson(region)==world,"derived sections leave source footprint and canonical region immutable");
            var u=Building(0,0,60,0,60,20,0,20,0,14,50,14,50,6,0,6);var ud=BuildingSections.Create(region,u,surface);GeometryChecks(u,ud,"Concave row with disconnected strip intersections");Check(ud.Parts.GroupBy(p=>p.StripIndex).Any(g=>g.Count()>1),"a strip containing disconnected source legs remains separate instead of creating a fake bridge");
            var tooLong=Building(0,0,250,0,250,8,0,8);var unsupported=BuildingSections.Create(region,tooLong,surface);Check(!unsupported.Sectioned&&unsupported.Parts.Length==0&&!string.IsNullOrEmpty(unsupported.FallbackReason),"over-budget source returns explicit original-footprint fallback with no partial replacement");
            var small=Building(0,0,10,0,10,8,0,8);Check(!BuildingSections.Create(region,small,surface).Sectioned,"ordinary small houses retain their original level footprint");
            var skinny=BuildingRoofGeometry.Create(new[]{new Vector3(0,0,0),new Vector3(4,0,0),new Vector3(4,0,8),new Vector3(0,0,8),new Vector3(0,0,0)},5,"gabled",Vector3.right);float max=skinny.Vertices.Max(p=>p.y);Check(skinny.Pitched&&skinny.Vertices.Where(p=>Mathf.Abs(p.y-max)<.0001f).All(p=>Mathf.Abs(p.z-4)<.0001f),"skinny section preserves the original row ridge axis rather than rotating90degrees");
        }
        static void ActualChecks()
        {
            var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText("Builds/Verification/BuildingFacade/native-world.json"));region.Prepare();string before=WorldSnapshotCodec.CanonicalJson(region);var world=new GameObject("Section surface sampler").AddComponent<GeoWorld>();world.Region=region;world.gameObject.SetActive(false);var risks=new[]{"os-03D6CE2D-EBA1-4BFE-ADBB-7CEE75FFFD3D-0","os-527DE8A1-6CB9-4F3E-8ADD-38BED48FD9D9-0","os-601F7B17-0B0F-4B01-B9D6-6597A56E7579-0","os-6DC15F3D-C258-491C-9F58-A22A74E8EAD4-0","os-AB49463D-C8EE-48E5-8E34-F7A3D8B8DF93-0"};Check(risks.All(id=>region.buildings.Any(b=>b.id==id)),"actual diagnostic input retains all five known Pantside roof-risk source outlines independently of subsequent site-report changes");var report=new JArray();
            foreach(var risk in risks)
            {
                string id=risk;var source=region.buildings.Single(b=>b.id==id);var data=BuildingSections.Create(region,source,world.MeshSurfaceHeight);if(data.Sectioned)GeometryChecks(source,data,id);else Check(!string.IsNullOrEmpty(data.FallbackReason)&&data.Parts.Length==0,id+" unsupported geometry has explicit nonpartial fallback");
                report.Add(new JObject{{"id",id},{"sectioned",data.Sectioned},{"fallbackReason",data.FallbackReason},{"parts",data.Parts.Length},{"unresolvedRoofRisk",data.HasRoofBurialRisk},{"riskParts",new JArray(data.Parts.Where(p=>p.Site.RoofBurialRisk).Select(p=>p.Building.id))},{"partSites",new JArray(data.Parts.Select(p=>new JObject{{"id",p.Building.id},{"levelFloorY",p.Site.LevelFloorY},{"boundaryMinY",p.Site.BoundaryMinY},{"boundaryMaxY",p.Site.BoundaryMaxY},{"highRelief",p.Site.HighRelief},{"roofBurialRisk",p.Site.RoofBurialRisk},{"complexFootprint",p.Site.ComplexFootprint}}))}});
            }
            Check(before==WorldSnapshotCodec.CanonicalJson(region),"actual derived-part planning preserves canonical world snapshot geometry and lineage");Directory.CreateDirectory("Builds/Verification/BuildingSections");File.WriteAllText("Builds/Verification/BuildingSections/pantside-analysis.json",new JObject{{"analysisOnly",true},{"sources",report},{"resolvedSources",report.Count(t=>(bool)t["sectioned"]&&!(bool)t["unresolvedRoofRisk"])},{"unsupportedSources",report.Count(t=>!(bool)t["sectioned"])},{"unresolvedSources",report.Count(t=>(bool)t["unresolvedRoofRisk"])}}.ToString());UnityEngine.Object.DestroyImmediate(world.gameObject);
        }
        public static void Run()
        {
            try{SyntheticChecks();ActualChecks();Directory.CreateDirectory("Builds/Verification/BuildingSections");File.WriteAllText("Builds/Verification/BuildingSections/result.txt",$"PASS {checks} bounded section topology area overlap determinism lineage floor and actual five-source diagnostic checks\nRoof-risk resolution is measured separately in pantside-analysis.json; no native pixel/access/performance acceptance claim.\n");EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
