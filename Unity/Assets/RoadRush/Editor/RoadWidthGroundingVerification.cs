using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class RoadWidthGroundingVerification
    {
        public static void Run()
        {
            WorldBuilder world=null;string previous=PlayerProfile.StorageOverride;int samples=0,missing=0,covered=0,raised=0;float worstCover=-100,worstGap=0;var failures=new List<string>();var diagnostics=new JArray();
            try
            {
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-roadRushWidthWorld");if(at<0||at+1>=args.Length)throw new InvalidDataException("Explicit admitted fixture required");
                string output=Path.GetFullPath("Builds/Verification/WidthGrounding/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));Directory.CreateDirectory(output);PlayerProfile.StorageOverride=Path.Combine(output,"profile.json");
                var region=JsonUtility.FromJson<GeoRegion>(File.ReadAllText(args[at+1]));var data=WorldDataFinalizer.AttachAuthoritative(region,CancellationToken.None,RoadWidthWorldPreparation.PrepareSurfacesOwned);
                world=new GameObject("Admitted road width grounding").AddComponent<WorldBuilder>();world.BuildPrepared(data);var geo=world.GeographicWorld;var cells=new HashSet<Vector2Int>();
                foreach(var part in data.RoadSurfaces.Parts.Values)foreach(var p in part.Surface.Vertices)cells.Add(new Vector2Int(Mathf.FloorToInt(p.x/320),Mathf.FloorToInt(p.z/320)));
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
                foreach(var part in data.RoadSurfaces.Parts.Values)
                {
                    var surface=part.Surface;for(int row=1;row<surface.Distances.Length;row++)
                    {
                        if(surface.Distances[row]-surface.Distances[row-1]<.000001)continue;int a=(row-1)*(surface.Columns+1),b=row*(surface.Columns+1);
                        foreach(float along in new[]{.15f,.5f,.85f})foreach(float lane in new[]{.05f,.12f,.5f,.88f,.95f})
                        {var left=Vector3.Lerp(surface.Vertices[a],surface.Vertices[b],along);var right=Vector3.Lerp(surface.Vertices[a+surface.Columns],surface.Vertices[b+surface.Columns],along);Probe(Vector3.Lerp(left,right,lane),part.Road.id);changedSamples++;}
                    }
                }
                foreach(var road in region.roads)if(!road.bridge&&!road.tunnel)
                {float arc=0;for(int i=1;i<road.points.Length;i++){var a=road.points[i-1];var b=road.points[i];float length=Vector3.Distance(a,b);var side=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;foreach(float along in new[]{.2f,.5f,.8f})foreach(float lane in new[]{-.38f,0,.38f}){var p=Vector3.Lerp(a,b,along)+side*lane*region.EffectiveWidth(road,arc+length*along);if(geo.HasSurface(p))Probe(p,"neighbor/"+road.id);}arc+=length;}}
                var receipt=new JObject{{"status",missing==0&&covered==0&&raised==0?"PASS":"FAIL"},{"samples",samples},{"changedSurfaceSamples",changedSamples},{"changedRoads",data.RoadSurfaces.Parts.Count},{"requestedCells",cells.Count},{"missing",missing},{"covered",covered},{"raised",raised},{"worstCover",worstCover},{"worstGap",worstGap},{"firstFailures",new JArray(failures)},{"diagnostics",diagnostics},{"scope","Actual Unity collider grounding for finalized changed-road rows/edges and resident neighboring lanes; not full-map residency/native visuals/performance or physical driving."}};
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Debug.Log("WIDTH GROUNDING "+receipt.ToString(Newtonsoft.Json.Formatting.None));if(samples<2000||changedSamples<1000||missing>0||covered>0||raised>0)throw new Exception("Admitted width grounding gates failed; see "+output);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;if(world){Geometry.ReleaseMeshes(world.gameObject);UnityEngine.Object.DestroyImmediate(world.gameObject);}}
        }
    }
}
