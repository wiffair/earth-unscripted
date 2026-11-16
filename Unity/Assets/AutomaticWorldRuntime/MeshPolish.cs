using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // Only same-level asphalt excludes pavement. Footbridges and underpasses
        // remain independent. Trim source connectivity lines at the kerb rather
        // than extruding a pavement ribbon to the carriageway centre node.
        bool PathSharesRoadLevel(Feature owner,Feature road)
        {
            return owner!=null&&!owner.bridge&&!owner.tunnel&&!road.bridge&&!road.tunnel&&owner.layer==road.layer&&(owner.level??"0")==(road.level??"0");
        }

        bool InsideRoad(Vector3 point,Feature pathOwner=null)
        {
            var nearby=Nearby(point.x,point.z);
            if(nearby==null)return false;
            foreach(var road in nearby)
                if(road.feature.category=="road" && Project(road,point.x,point.z,out float height)<road.feature.width*.5f+.04f && (PathSharesRoadLevel(pathOwner,road.feature)||Mathf.Abs(height-point.y)<.45f))return true;
            return false;
        }

        Vector3 OutsideRoad(Vector3 point,Feature pathOwner=null)
        {
            var nearby=Nearby(point.x,point.z);
            if(nearby==null)return point;
            foreach(var road in nearby)
            {
                if(road.feature.category!="road")continue;
                float distance=Project(road,point.x,point.z,out float height);
                if(distance>=road.feature.width*.5f+.05f || (!PathSharesRoadLevel(pathOwner,road.feature)&&Mathf.Abs(height-point.y)>=.45f))continue;
                var direction=road.b-road.a;direction.y=0;
                float station=Mathf.Clamp01(Vector3.Dot(point-road.a,direction)/Mathf.Max(1e-8f,direction.sqrMagnitude));
                var centre=Vector3.Lerp(road.a,road.b,station);var normal=point-centre;normal.y=0;
                if(normal.sqrMagnitude>1e-8f)point+=normal.normalized*(road.feature.width*.5f+.05f-distance);
            }
            return point;
        }

        void FootpathStrip(Batch batch,Feature owner,Vector3 a,Vector3 b,Vector3 sideA,Vector3 sideB,float halfWidth)
        {
            bool firstInside=InsideRoad(a,owner),lastInside=InsideRoad(b,owner);
            if(firstInside && lastInside)return;
            if(firstInside!=lastInside)
            {
                float low=0,high=1;
                for(int iteration=0;iteration<9;iteration++)
                {
                    float middle=(low+high)*.5f;
                    if(InsideRoad(Vector3.Lerp(a,b,middle),owner)==firstInside)low=middle;else high=middle;
                }
                float crossing=(low+high)*.5f;
                var boundary=Vector3.Lerp(a,b,crossing);var boundarySide=Vector3.Lerp(sideA,sideB,crossing);
                if(firstInside){a=boundary;sideA=boundarySide;}else{b=boundary;sideB=boundarySide;}
            }
            if(Vector3.Distance(a,b)<.01f)return;
            if (!IsGroundPath(owner))
            {
                batch.Quad(OutsideRoad(a-sideA*halfWidth,owner),OutsideRoad(b-sideB*halfWidth,owner),OutsideRoad(b+sideB*halfWidth,owner),OutsideRoad(a+sideA*halfWidth,owner));
                return;
            }
            int rows = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z))/TerrainStep));
            for(int row=0;row<rows;row++)
            {
                float lo=row/(float)rows,hi=(row+1)/(float)rows;
                var first=Vector3.Lerp(a,b,lo);var last=Vector3.Lerp(a,b,hi);
                var firstSide=Vector3.Lerp(sideA,sideB,lo);var lastSide=Vector3.Lerp(sideA,sideB,hi);
                GroundedPathQuad(batch,owner,OutsideRoad(first-firstSide*halfWidth,owner),OutsideRoad(last-lastSide*halfWidth,owner),OutsideRoad(last+lastSide*halfWidth,owner),OutsideRoad(first+firstSide*halfWidth,owner));
            }
        }

        static float CrossXZ(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        static bool InTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            float first=CrossXZ(a,b,p),second=CrossXZ(b,c,p),third=CrossXZ(c,a,p);
            return (first>=-.0001f && second>=-.0001f && third>=-.0001f)||(first<=.0001f && second<=.0001f && third<=.0001f);
        }

        BuildingWork CaptureBuilding(Building house)
        {
            if(house.underground)return null;
            var points=new List<Vector3>();
            foreach(var point in house.points)
                if(points.Count==0 || Vector2.Distance(new Vector2(points.Last().x,points.Last().z),new Vector2(point.x,point.z))>.02f)points.Add(point);
            if(points.Count>2 && Vector2.Distance(new Vector2(points[0].x,points[0].z),new Vector2(points.Last().x,points.Last().z))<.02f)points.RemoveAt(points.Count-1);
            if(points.Count<3)return null;
            float foundation=house.minHeight>0?house.points.Average(point=>point.y)+house.minHeight:points.Min(point=>Ground(point.x,point.z))-.2f;
            if(house.layer>0 && house.kind=="train_station" && house.minHeight<=0)foundation=StationClearanceFloor(points,foundation);
            float roofHeight=Mathf.Max(foundation+.3f,points.Select(point=>RawHeight(point.x,point.z)).Average()+house.height);
            if(house.layer>0 && house.kind=="train_station")roofHeight=Mathf.Max(roofHeight,foundation+.6f);
            return new BuildingWork{house=house,points=points,foundation=foundation,roofHeight=roofHeight,spatial=spatialByMap[workingMap],audit=RoadRush.SinglePlayerBeta.VerificationOutput!=null};
        }
        void CompileBuilding(BuildingWork work,Batch walls,Batch roofs,BuildingWorkStats stats)
        {
            var house=work.house;var points=work.points;float foundation=work.foundation,roofHeight=work.roofHeight;
            var roadClearFootprints=BuildingRoadSpace(house,points,foundation,roofHeight+Mathf.Max(0,house.roofHeight),work.spatial,stats,work.audit);
            if(roadClearFootprints!=null){ClippedBuilding(house,points,roadClearFootprints,foundation,roofHeight,walls,roofs,stats);return;}
            float area=0;for(int i=0;i<points.Count;i++)area+=points[i].x*points[(i+1)%points.Count].z-points[(i+1)%points.Count].x*points[i].z;
            bool pitched=TryPitchedRoof(points,house,out var roofSide,out float roofMin,out float roofMax);
            float rise=pitched?Mathf.Clamp(house.roofHeight,.5f,5):0;
            if(pitched && house.heightIncludesRoof)roofHeight-=rise;
            for(int i=0;i<points.Count;i++)
            {
                var a=points[i];var b=points[(i+1)%points.Count];a.y=b.y=foundation;
                var topA=a;var topB=b;topA.y=topB.y=roofHeight;
                if(area>0)walls.Quad(b,a,topA,topB);else walls.Quad(a,b,topB,topA);
            }
            if(pitched)
            {
                float mid=(roofMin+roofMax)*.5f,half=(roofMax-roofMin)*.5f;
                foreach(bool below in new[]{true,false})
                {
                    var halfRoof=new List<Vector3>();
                    var previous=points[points.Count-1];float previousDistance=Vector3.Dot(previous,roofSide)-mid;
                    bool previousInside=below?previousDistance<=0:previousDistance>=0;
                    foreach(var point in points)
                    {
                        float distance=Vector3.Dot(point,roofSide)-mid;bool inside=below?distance<=0:distance>=0;
                        if(previousInside!=inside)halfRoof.Add(Vector3.Lerp(previous,point,previousDistance/(previousDistance-distance)));
                        if(inside)halfRoof.Add(point);
                        previous=point;previousDistance=distance;previousInside=inside;
                    }
                    for(int i=0;i<halfRoof.Count;i++)
                    {var point=halfRoof[i];point.y=roofHeight+rise*Mathf.Clamp01(1-Mathf.Abs(Vector3.Dot(point,roofSide)-mid)/half);halfRoof[i]=point;}
                    for(int i=1;i<halfRoof.Count-1;i++)
                    {
                        var a=halfRoof[0];var b=halfRoof[i];var c=halfRoof[i+1];
                        if(Vector3.Cross(b-a,c-a).y<0)roofs.Triangle(a,c,b);else roofs.Triangle(a,b,c);
                    }
                }
                for(int i=0;i<points.Count;i++)
                {
                    var a=points[i];var b=points[(i+1)%points.Count];
                    float da=Vector3.Dot(a,roofSide)-mid,db=Vector3.Dot(b,roofSide)-mid;
                    if(da*db>=0)continue;
                    var peak=Vector3.Lerp(a,b,da/(da-db));a.y=b.y=roofHeight;peak.y=roofHeight+rise;
                    if(area>0)walls.Triangle(b,a,peak);else walls.Triangle(a,b,peak);
                }
                return;
            }
            var remaining=Enumerable.Range(0,points.Count).ToList();
            float orientation=Mathf.Sign(area);
            for(int attempts=0;remaining.Count>2 && attempts<points.Count*points.Count;attempts++)
            {
                bool clipped=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int ai=remaining[(i+remaining.Count-1)%remaining.Count],bi=remaining[i],ci=remaining[(i+1)%remaining.Count];
                    var a=points[ai];var b=points[bi];var c=points[ci];
                    if(CrossXZ(a,b,c)*orientation<=.0001f)continue;
                    if(remaining.Any(index=>index!=ai&&index!=bi&&index!=ci&&InTriangle(points[index],a,b,c)))continue;
                    a.y=b.y=c.y=roofHeight;
                    if(Vector3.Cross(b-a,c-a).y<0)roofs.Triangle(a,c,b);else roofs.Triangle(a,b,c);
                    remaining.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)break;
            }
        }

        static bool TryPitchedRoof(List<Vector3> points,Building house,out Vector3 side,out float min,out float max)
        {
            side=Vector3.right;min=max=0;
            if(house.roofShape!="gabled" && house.roofShape!="pitched")return false;
            float orientation=0,longest=0;Vector3 axis=Vector3.forward;
            for(int i=0;i<points.Count;i++)
            {
                float cross=CrossXZ(points[i],points[(i+1)%points.Count],points[(i+2)%points.Count]);
                if(Mathf.Abs(cross)>.001f){if(orientation!=0 && Mathf.Sign(cross)!=orientation)return false;orientation=Mathf.Sign(cross);}
                var edge=Vector3.ProjectOnPlane(points[(i+1)%points.Count]-points[i],Vector3.up);
                if(edge.sqrMagnitude>longest){longest=edge.sqrMagnitude;axis=edge.normalized;}
            }
            side=Vector3.Cross(Vector3.up,axis);var roofSide=side;min=points.Min(point=>Vector3.Dot(point,roofSide));max=points.Max(point=>Vector3.Dot(point,roofSide));
            return max-min>2 && max-min<40;
        }
    }
}
