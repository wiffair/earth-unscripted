using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class RoadWidthPairedGroundingVerification
    {
        [Serializable] sealed class Roads{public GeoRoad[] roads;}
        static string RoadHash(GeoRegion r)=>EarthUnscripted.Core.DataPackageRules.Hash(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Roads{roads=r.roads})));
        static IEnumerable<(Vector3 leftA,Vector3 rightA,Vector3 leftB,Vector3 rightB)> LegacyRows(GeoRoad road)
        {
            var p=road.points;float half=road.width*.5f;for(int i=0;i<p.Length-1;i++)
            {var a=p[i];var b=p[i+1];var dir=(b-a).normalized;var before=i>0?(a-p[i-1]).normalized:dir;var after=i+2<p.Length?(p[i+2]-b).normalized:dir;var normal=Vector3.Cross(Vector3.up,dir).normalized;var r0=Vector3.Cross(Vector3.up,(before+dir).normalized).normalized;var r1=Vector3.Cross(Vector3.up,(dir+after).normalized).normalized;r0/=Mathf.Max(.6f,Vector3.Dot(r0,normal));r1/=Mathf.Max(.6f,Vector3.Dot(r1,normal));if(Vector3.Angle(before,dir)>60)r0=normal;if(Vector3.Angle(dir,after)>60)r1=normal;int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/2.5f));for(int j=0;j<steps;j++){float u=j/(float)steps,v=(j+1)/(float)steps;var x=Vector3.Lerp(a,b,u);var y=Vector3.Lerp(a,b,v);var rx=Vector3.Lerp(r0,r1,u)*half;var ry=Vector3.Lerp(r0,r1,v)*half;yield return(x-rx,x+rx,y-ry,y+ry);}}
        }
        public static void Run()
        {
            WorldBuilder world=null;string previous=PlayerProfile.StorageOverride;int samples=0,missing=0,covered=0,raised=0,capSamples=0;float worstCover=-100,worstGap=0;var failures=new List<string>();var diagnostics=new JArray();
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-earthUnscriptedWidthWorld");if(at<0||at+1>=args.Length)throw new InvalidDataException("Explicit admitted fixture required");
                string output=Path.GetFullPath("Builds/Verification/WidthPairedGrounding/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(output);PlayerProfile.StorageOverride=Path.Combine(output,"profile.json");
                var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(args[at+1]));var data=WorldDataFinalizer.AttachAuthoritative(region,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                int referenceAt=Array.IndexOf(args,"-earthUnscriptedWidthRowsFrom");bool reference=referenceAt>=0&&referenceAt+1<args.Length;var rowsData=data;
                if(reference){var candidate=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(args[referenceAt+1]));rowsData=WorldDataFinalizer.AttachAuthoritative(candidate,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);if(RoadHash(region)!=RoadHash(candidate))throw new InvalidDataException("Reference and candidate ordinary/source roads differ");if(data.RoadSurfaces.Parts.Count!=0||region.roadWidthProfiles.Any(p=>p.bevelBoundary||p.derivatives.Any(v=>v!=0)||p.widths.Any(w=>w!=region.roads.Single(r=>r.id==p.roadId).width)))throw new InvalidDataException("Reference must have constant original-width schema1 controls");}
                var selected=rowsData.RoadSurfaces.Parts.Keys.ToHashSet(StringComparer.Ordinal);
                world=new GameObject("Admitted road width grounding").AddComponent<WorldBuilder>();world.BuildPrepared(data);var geo=world.GeographicWorld;var cells=new HashSet<Vector2Int>();
                foreach(var part in rowsData.RoadSurfaces.Parts.Values)foreach(var p in part.Surface.Vertices)cells.Add(new Vector2Int(Mathf.FloorToInt(p.x/320),Mathf.FloorToInt(p.z/320)));
                if(reference)foreach(var road in region.roads.Where(r=>selected.Contains(r.id)))foreach(var row in LegacyRows(road))foreach(var p in new[]{row.leftA,row.rightA,row.leftB,row.rightB})cells.Add(new Vector2Int(Mathf.FloorToInt(p.x/320),Mathf.FloorToInt(p.z/320)));
                foreach(var road in region.roads.Where(r=>selected.Contains(r.id)))foreach(int endpoint in new[]{0,1}){var p=endpoint==0?road.points[0]:road.points.Last();string node=endpoint==0?road.start:road.end;float radius=region.JunctionRadius(node,region.EffectiveWidth(road,endpoint==0?0:road.length)*.5f,road.layer);cells.Add(new Vector2Int(Mathf.FloorToInt(p.x/320),Mathf.FloorToInt(p.z/320)));for(int n=0;n<32;n++){float a=n*Mathf.PI/16;var edge=p+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;cells.Add(new Vector2Int(Mathf.FloorToInt(edge.x/320),Mathf.FloorToInt(edge.z/320)));}}
                var build=typeof(GeoWorld).GetMethod("Build",BindingFlags.Instance|BindingFlags.NonPublic);foreach(var cell in cells)build.Invoke(geo,new object[]{cell,"width grounding"});Physics.SyncTransforms();
                void Probe(Vector3 p,string label)
                {
                    float expected=region.RoadSurfaceHeight(p.x,p.z),asphalt=float.MinValue,terrain=float.MinValue;
                    foreach(var hit in Physics.RaycastAll(new Vector3(p.x,expected+80,p.z),Vector3.down,180))
                    {
                        if(hit.collider.GetComponentInParent<GeoWorld>()!=geo)continue;string name=hit.collider.name;
                        if((name=="Mapped road"||name=="Junction apron"||name.StartsWith("Finalized mapped asphalt"))&&Mathf.Abs(hit.point.y-expected)<4)asphalt=Mathf.Max(asphalt,hit.point.y);
                        if(name=="Terrain")terrain=Mathf.Max(terrain,hit.point.y);
                    }
                    samples++;if(asphalt==float.MinValue||terrain==float.MinValue){missing++;if(failures.Count<30)failures.Add("Missing "+label+" "+p);return;}
                    float cover=terrain-asphalt,gap=asphalt-terrain;if((cover>.04f||gap>1.2f)&&diagnostics.Count<64)diagnostics.Add(new JObject{{"road",label},{"x",p.x},{"z",p.z},{"expectedRoad",expected},{"asphalt",asphalt},{"terrain",terrain},{"analyticTerrain",region.TerrainHeight(p.x,p.z)},{"sampledTerrain",geo.MeshSurfaceHeight(p.x,p.z)}});worstCover=Mathf.Max(worstCover,cover);worstGap=Mathf.Max(worstGap,gap);if(cover>.04f){covered++;if(failures.Count<30)failures.Add("Covered "+label+" "+p+" "+cover);}if(gap>1.2f){raised++;if(failures.Count<30)failures.Add("Raised "+label+" "+p+" "+gap);}
                }
                int changedSamples=0;
                var alongSamples=Enumerable.Range(0,13).Select(i=>(i+.5f)/13).ToArray();var lateralSamples=Enumerable.Range(0,13).Select(i=>.02f+.96f*i/12).ToArray();
                if(!reference)foreach(var part in data.RoadSurfaces.Parts.Values)
                {
                    var surface=part.Surface;for(int row=1;row<surface.Distances.Length;row++)
                    {if(surface.Distances[row]-surface.Distances[row-1]<.000001)continue;int a=(row-1)*(surface.Columns+1),b=row*(surface.Columns+1);foreach(float along in alongSamples)foreach(float lane in lateralSamples){var left=Vector3.Lerp(surface.Vertices[a],surface.Vertices[b],along);var right=Vector3.Lerp(surface.Vertices[a+surface.Columns],surface.Vertices[b+surface.Columns],along);Probe(Vector3.Lerp(left,right,lane),part.Road.id);changedSamples++;}}
                }
                else foreach(var road in region.roads.Where(r=>selected.Contains(r.id)))foreach(var row in LegacyRows(road))foreach(float along in alongSamples)foreach(float lane in lateralSamples){Probe(Vector3.Lerp(Vector3.Lerp(row.leftA,row.leftB,along),Vector3.Lerp(row.rightA,row.rightB,along),lane),"reference/"+road.id);changedSamples++;}
                var sampledNodes=new HashSet<string>(StringComparer.Ordinal);foreach(var road in region.roads.Where(r=>selected.Contains(r.id)&&!r.bridge&&!r.tunnel))foreach(int endpoint in new[]{0,1})
                {var p=endpoint==0?road.points[0]:road.points.Last();string node=endpoint==0?road.start:road.end;if(!sampledNodes.Add(road.layer+"|"+node))continue;float radius=region.JunctionRadius(node,region.EffectiveWidth(road,endpoint==0?0:road.length)*.5f,road.layer);foreach(float radial in new[]{.25f,.5f,.75f,.9f})for(int angle=0;angle<32;angle++){float a=angle*Mathf.PI/16;Probe(p+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius*radial,"cap/"+node);capSamples++;}}
                foreach(var road in region.roads)if(!road.bridge&&!road.tunnel)
                {float arc=0;for(int i=1;i<road.points.Length;i++){var a=road.points[i-1];var b=road.points[i];float length=Vector3.Distance(a,b);var side=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;foreach(float along in new[]{.2f,.5f,.8f})foreach(float lane in new[]{-.38f,0,.38f}){var p=Vector3.Lerp(a,b,along)+side*lane*region.EffectiveWidth(road,arc+length*along);if(geo.HasSurface(p))Probe(p,"neighbor/"+road.id);}arc+=length;}}
                var receipt=new JObject{{"status",missing==0&&covered==0&&raised==0?"PASS":"FAIL"},{"mode",reference?"constant-reference-dense":"admitted-dense"},{"compatibility",WorldSnapshotCodec.Compatibility},{"inputSHA256",EarthUnscripted.Core.DataPackageRules.Hash(File.ReadAllBytes(args[at+1]))},{"rowsFromSHA256",reference?EarthUnscripted.Core.DataPackageRules.Hash(File.ReadAllBytes(args[referenceAt+1])):null},{"sourceRoadHash",RoadHash(region)},{"samples",samples},{"changedSurfaceSamples",changedSamples},{"changedRoads",data.RoadSurfaces.Parts.Count},{"rowRoads",selected.Count},{"capSamples",capSamples},{"requestedCells",cells.Count},{"missing",missing},{"covered",covered},{"raised",raised},{"worstCover",worstCover},{"worstGap",worstGap},{"firstFailures",new JArray(failures)},{"diagnostics",diagnostics},{"scope","Actual Unity collider grounding for finalized changed-road rows/edges and resident neighboring lanes; dense13x13 row sampling and selected-source node caps; same0.04m cover/1.2m gap gates; not full-map residency/native visuals/performance or physical driving."}};
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Debug.Log("WIDTH GROUNDING "+receipt.ToString(Newtonsoft.Json.Formatting.None));if(samples<2000||changedSamples<1000||missing>0||covered>0||raised>0)throw new Exception("Admitted width grounding gates failed; see "+output);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;if(world){Geometry.ReleaseMeshes(world.gameObject);UnityEngine.Object.DestroyImmediate(world.gameObject);}}
        }
    }
}
