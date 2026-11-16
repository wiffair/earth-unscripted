using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class EffectiveRoadWidthVerification
    {
        public static void Run()
        {
            int checks=0;string previous=PlayerProfile.StorageOverride;
            void Check(bool pass,string why){if(!pass)throw new Exception("EFFECTIVE WIDTH FAIL "+why);checks++;Debug.Log("EFFECTIVE WIDTH PASS "+why);}
            RoadWidthProfileDto Profile(GeoRoad r,float a,float b)=>new RoadWidthProfileDto{roadId=r.id,distances=new[]{0f,r.length},widths=new[]{a,b},derivatives=new[]{0f,0f}};
            try
            {
                PlayerProfile.StorageOverride=Path.GetFullPath("Builds/Verification/effective-width-profile.json");
                var source=GeoRegion.Load("dee-valley");RailwayProfileSolver.Prepare(source);var rng=new System.Random(1881);
                var points=Enumerable.Range(0,1000).Select(_=>new Vector3(source.minX+(float)rng.NextDouble()*source.width,0,source.minZ+(float)rng.NextDouble()*source.depth)).ToArray();
                var heights=points.Select(p=>source.RoadSurfaceHeight(p.x,p.z)).ToArray();var layered=points.Select(p=>source.RoadSurfaceHeightOnLayer(p.x,p.z,0)).ToArray();
                string roads=JsonUtility.ToJson(new Roads{roads=source.roads});var controls=source.roads.Where(r=>!r.tunnel).Select(r=>Profile(r,r.width,Mathf.Min(80,r.width+1))).ToArray();
                Check(LegacyRoadWidthPreparation.TryInstall(source,1,controls,out _),"complete actual source controls install from cloned canonical data");
                Check(source.PreparedRoadWidths!=null&&source.roadWidthSchema==1,"Prepare reconstructs the immutable evaluator from canonical fields");
                Check(points.Select((p,i)=>source.RoadSurfaceHeight(p.x,p.z)==heights[i]).All(x=>x),"1000 actual vertical road samples remain bit-identical");
                Check(points.Select((p,i)=>source.RoadSurfaceHeightOnLayer(p.x,p.z,0)==layered[i]).All(x=>x),"1000 layer-filtered vertical samples remain bit-identical");
                Check(JsonUtility.ToJson(new Roads{roads=source.roads})==roads,"source road widths and coordinates stay unchanged");
                var held=source.PreparedRoadWidths;string canonicalControls=JsonUtility.ToJson(source);
                Check(!LegacyRoadWidthPreparation.TryInstall(source,1,Array.Empty<RoadWidthProfileDto>(),out _)&&ReferenceEquals(held,source.PreparedRoadWidths)&&JsonUtility.ToJson(source)==canonicalControls,"invalid candidate controls leave both canonical data and existing evaluator unchanged");
                var round=WorldSnapshotCodec.Decode(WorldSnapshotCodec.CanonicalJson(source),WorldSnapshotCodec.NetworkId(source),WorldSnapshotCodec.Epoch(source));
                Check(round.PreparedRoadWidths!=null&&round.roadWidthSchema==1,"peer decode reconstructs authoritative width evaluator");
                Check(points.All(p=>{var a=source.ProjectRoad(p);var b=round.ProjectRoad(p);return a.Road?.id==b.Road?.id&&a.Width==b.Width&&a.SourceDistance==b.SourceDistance;}),"1000 peer projections retain exact road, width and source distance");
                var wide=new GeoRoad{id="wide",start="a",end="j",kind="residential",width=12,limit=30,points=new[]{new Vector3(-40,10,0),new Vector3(0,10,0)}};
                var narrow=new GeoRoad{id="narrow",start="j",end="z",kind="residential",width=8.5f,limit=30,points=new[]{new Vector3(0,10,0),new Vector3(40,10,0)}};
                var fixture=new GeoRegion{roads=new[]{wide,narrow},waters=Array.Empty<GeoWater>(),railways=Array.Empty<GeoRailway>(),buildings=Array.Empty<GeoBuilding>(),nx=2,nz=2,minX=-50,minZ=-50,width=100,depth=100,heights=new[]{20f,20f,20f,20f}};fixture.Prepare();
                var tyre=new Vector3(10,10,4.5f);Check(!fixture.IsOnRoad(tyre),"legacy tyre point lies outside narrow road");
                Check(LegacyRoadWidthPreparation.TryInstall(fixture,1,new[]{Profile(wide,12,12),Profile(narrow,12,8.5f)},out _),"synthetic outward controls install");
                Check(fixture.IsOnRoad(tyre)&&fixture.ProjectRoad(tyre).EdgeDistance<0,"tyre footprint uses effective local width");
                Check(Mathf.Abs(fixture.TerrainHeight(tyre.x,tyre.z)-9.45f)<.00001f,"effective road exclusion keeps validated .55m bed");
                Check(Mathf.Abs(fixture.JunctionRadius("j",0,0)-6)<.00001f,"junction radius uses shared effective endpoint");
                Check(LegacyRoadWidthPreparation.TryInstall(fixture,1,new[]{Profile(wide,12,8.5f),Profile(narrow,8.5f,8.5f)},out _),"synthetic inward controls install");
                Check(!fixture.IsOnRoad(new Vector3(-2,10,5.5f))&&fixture.JunctionRadius("j",0,0)==4.25f,"inward tyre footprint and apron drop the old wide source edge");
                var lane=fixture.DrivingLanePoint(wide,new Vector3(-2,10,0),Vector3.right,out _);Check(Mathf.Abs(Mathf.Abs(lane.z)-Mathf.Min(2.7f,fixture.EffectiveWidth(wide,38)*.24f))<.00001f,"AI lane offset follows the same finalized field");
                string unprofiled="";Check(LegacyRoadWidthPreparation.TryInstall(fixture,0,null,out unprofiled)&&fixture.PreparedRoadWidths==null&&fixture.roadWidthProfiles.Length==0,"explicit legacy reset removes both canonical and evaluated controls");
                var surfaceRoad=new GeoRoad{id="surface",start="a",end="b",width=8.5f,points=new[]{new Vector3(0,10,-40),new Vector3(0,10,40)}};
                var underground=new GeoRoad{id="underground",start="c",end="d",width=20,tunnel=true,layer=-1,points=new[]{new Vector3(2,0,-40),new Vector3(2,0,40)}};
                var tunnelFixture=new GeoRegion{roads=new[]{surfaceRoad,underground},waters=Array.Empty<GeoWater>(),nx=2,nz=2,minX=-50,minZ=-50,width=100,depth=100,heights=new[]{20f,20f,20f,20f}};tunnelFixture.Prepare();
                Check(tunnelFixture.ProjectRoad(Vector3.zero,false).Road==underground&&tunnelFixture.ProjectRoad(Vector3.zero,false,false).Road==surfaceRoad,"surface-only projection excludes a closer underground footprint without changing default road queries");
                Check(Mathf.Abs(tunnelFixture.TerrainHeight(0,0)-9.45f)<.00001f,"underground road cannot suppress the supporting terrain bed beneath visible asphalt");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/effective-road-width.txt","PASS "+checks+" actual Unity projection/vertical/terrain/AI/protocol checks. Surface meshes and native gameplay not activated by this test.\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
            finally{PlayerProfile.StorageOverride=previous;}
        }
        [Serializable]sealed class Roads{public GeoRoad[] roads;}
    }
}
