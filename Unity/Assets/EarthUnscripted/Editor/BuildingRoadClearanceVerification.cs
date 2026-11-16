using System;
using System.IO;
using Clipper2Lib;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    public static class BuildingRoadClearanceVerification
    {
        static PathD Box(double x,double z,double w,double h)=>new PathD{new PointD(x,z),new PointD(x+w,z),new PointD(x+w,z+h),new PointD(x,z+h)};
        static void Check(bool test,string why){if(!test)throw new InvalidDataException("Building clearance: "+why);}
        public static void Run()
        {
            int checks=0;
            void Case(PathsD subject,PathsD roads,double area)
            {
                var result=NewbridgeWorld.TrimBuildingRoads(subject,roads);
                Check(Math.Abs(Clipper.Area(result)-area)<.001,"expected retained building area");
                Check(Math.Abs(Clipper.Area(Clipper.Intersect(result,roads,FillRule.NonZero,3)))<.001,"no road overlap remains");
                if(result.Count>0)Check(Clipper.Triangulate(result,3,out var triangles)==TriangulateResult.success && Math.Abs(Clipper.Area(triangles)-area)<.001,"roof triangles preserve area including holes");
                checks++;
            }
            Case(new PathsD{Box(0,0,10,10)},new PathsD{Box(-1,-1,3,12)},80);
            Case(new PathsD{Box(0,0,10,10)},new PathsD{Box(4,-1,2,12)},80);
            Case(new PathsD{Box(0,0,10,10)},new PathsD{Box(3,3,4,4)},84);
            Case(new PathsD{Box(0,0,10,10)},new PathsD{Box(-1,-1,12,12)},0);
            Case(new PathsD{Box(0,0,10,10)},new PathsD{Box(11,0,2,10)},100);
            Case(new PathsD{new PathD{new PointD(0,0),new PointD(10,0),new PointD(10,4),new PointD(4,4),new PointD(4,10),new PointD(0,10)}},new PathsD{Box(2,-1,2,12)},44);
            Case(new PathsD{Box(0,0,10,10)},new PathsD{Box(4,-1,2,12),Box(-1,4,12,2)},64);
            Check(NewbridgeWorld.BuildingSharesRoadHeight(0,0,0,8),"ordinary building shares road height");
            Check(NewbridgeWorld.BuildingSharesRoadHeight(0,0,2,10),"low hanging floor cannot block vehicles");
            Check(!NewbridgeWorld.BuildingSharesRoadHeight(0,0,5.5f,12),"proper overhead building clearance preserved");
            Check(!NewbridgeWorld.BuildingSharesRoadHeight(12,12,0,8),"high bridge above building preserved");checks+=4;
            var root=Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath));
            Directory.CreateDirectory(Path.Combine(root,"Feedback26"));File.WriteAllText(Path.Combine(root,"Feedback26/clearance-checks.json"),"{\"passed\":true,\"cases\":"+checks+",\"scope\":\"Actual production Clipper subtraction and roof triangulation: partial cuts, split footprints, interior road hole, full removal, untouched, concave, crossing roads\"}");
        }
    }
}
