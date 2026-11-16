using System;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted
{
    // Artistic facade interpretation, not a source of addresses or dwelling boundaries.
    public static class BuildingFacadePlan
    {
        public const int MaximumOpeningsPerBuilding=192;
        public readonly struct Opening
        {
            public readonly string Id;public readonly Vector3 Position;public readonly Quaternion Rotation;
            public readonly float Width,Height;public readonly bool Door;public readonly int Palette;
            public Opening(string id,Vector3 p,Quaternion r,float w,float h,bool door,int palette){Id=id;Position=p;Rotation=r;Width=w;Height=h;Door=door;Palette=palette;}
        }
        public static IEnumerable<Opening> Create(GeoRegion region,GeoBuilding building,float baseY,Func<Vector3,bool> exterior,Func<float,float,float> surface,BuildingEntryAudit diagnostics=null,string sourceId=null,Func<Vector3,GeoRegion.RoadProjection> finalRoadQuery=null)
        {
            var points=building.points;int n=points.Length-1;float signedArea=0,longest=0;
            for(int i=0;i<n;i++){var a=points[i];var b=points[(i+1)%n];signedArea+=a.x*b.z-b.x*a.z;longest=Mathf.Max(longest,new Vector2(b.x-a.x,b.z-a.z).magnitude);}
            float area=Mathf.Abs(signedArea)*.5f;
            if(n<3||n>256||area<24||building.height<2.5f)yield break;
            bool important=building.kind=="important_building";
            int floors=Mathf.Clamp(building.height<4.3f?1:Mathf.FloorToInt((building.height+.7f)/2.5f),1,5),emitted=0;
            float pitch=building.height/floors,windowCentre=Mathf.Min(1.6f,Mathf.Max(1.4f,pitch*.6f));
            float windowHeight=Mathf.Min(important?1.45f:1.25f,2*(pitch-windowCentre-.2f));if(windowHeight<.65f)yield break;
            for(int edge=0;edge<n;edge++)
            {
                var a=points[edge];var b=points[(edge+1)%n];var along=new Vector3(b.x-a.x,0,b.z-a.z);float length=along.magnitude;
                if(length<2.6f||(!important&&length<longest*.3f))continue;along/=length;
                var outward=new Vector3(along.z,0,-along.x)*(signedArea>=0?1:-1);var rotation=Quaternion.LookRotation(outward,Vector3.up);
                var middle=(a+b)*.5f;GeoRoad road;Vector3 roadPoint;float separation;if(finalRoadQuery==null)road=region.Nearest(middle,out roadPoint,out separation);else{var projection=finalRoadQuery(middle);road=projection.Road;roadPoint=projection.Point;separation=projection.Separation;}var toRoad=roadPoint-middle;toRoad.y=0;
                bool front=road!=null&&!road.tunnel&&!road.bridge&&separation<50&&toRoad.sqrMagnitude>.01f&&Vector3.Dot(outward,toRoad.normalized)>.25f;
                int bays=Mathf.Clamp(Mathf.RoundToInt(length/(important?3.8f:5.1f)),1,32);float bay=length/bays;
                for(int unit=0;unit<bays;unit++)
                {
                    bool entrance=front&&bay>=3.4f&&(!important||unit==bays/2);
                    var audit=entrance?diagnostics?.Begin(sourceId??building.id,building.id,building.id+"/e"+edge+"/b"+unit+"/door",baseY,pitch,floors):null;
                    float windowAlong=(unit+(entrance?.29f:.5f))*bay;
                    for(int floor=0;floor<floors;floor++)
                    {
                        float x=floor>0?(unit+.5f)*bay:windowAlong;
                        float width=Mathf.Min(important?1.45f:1.12f,bay*.45f);
                        if(x<width*.5f+.2f||x>length-width*.5f-.2f)continue;
                        var p=a+along*x+outward*.012f;p.y=baseY+floor*pitch+windowCentre;
                        if(p.y+windowHeight*.5f>baseY+building.height-.18f||!Clear(p,width,windowHeight,along,outward,exterior,surface))continue;
                        yield return new Opening(building.id+"/e"+edge+"/b"+unit+"/w"+floor,p,rotation,width,windowHeight,false,0);
                        if(++emitted>=MaximumOpeningsPerBuilding){diagnostics?.Finish(audit,"FeatureLimit");yield break;}
                    }
                    if(!entrance)continue;
                    float doorAlong=(unit+.76f)*bay;const float doorWidth=.92f,doorHeight=2.08f;
                    if(doorAlong>length-doorWidth*.5f-.2f){diagnostics?.Finish(audit,"DoorEdge");continue;}
                    var door=a+along*doorAlong+outward*.012f;
                    var groundProbe=door+outward*.08f;
                    float centreGround=surface(groundProbe.x,groundProbe.z),leftGround=surface(groundProbe.x+along.x*.46f,groundProbe.z+along.z*.46f),rightGround=surface(groundProbe.x-along.x*.46f,groundProbe.z-along.z*.46f);
                    float highest=Mathf.Max(centreGround,Mathf.Max(leftGround,rightGround)),lowest=Mathf.Min(centreGround,Mathf.Min(leftGround,rightGround));
                    if(audit!=null){audit.groundKnown=true;audit.highestGround=highest;audit.lowestGround=lowest;}
                    if(highest-lowest>.22f){diagnostics?.Finish(audit,"CrossSlope");continue;}
                    float entryBase=baseY+.04f;int entryFloor=Mathf.Max(0,Mathf.CeilToInt((highest+.015f-entryBase)/pitch));
                    if(audit!=null){audit.bandKnown=true;audit.entryBaseY=entryBase;audit.ceilNumerator=highest+.015f-entryBase;audit.selectedBand=entryFloor;audit.thresholdY=entryBase+entryFloor*pitch;}
                    if(entryFloor>=floors){diagnostics?.Finish(audit,"FloorReject");continue;}door.y=entryBase+entryFloor*pitch+doorHeight*.5f;
                    if(door.y+doorHeight*.5f>baseY+building.height-.18f){diagnostics?.Finish(audit,"TopReject");continue;}
                    if(!Clear(door,doorWidth,doorHeight,along,outward,exterior,surface,true,audit)){diagnostics?.Finish(audit,audit?.frameFailure??"FrameClearance");continue;}
                    if(audit!=null)audit.planned=true;
                    yield return new Opening(building.id+"/e"+edge+"/b"+unit+"/door",door,rotation,doorWidth,doorHeight,true,(int)(BuildingAppearanceMaterials.Seed(building.id+unit)%4));
                    if(++emitted>=MaximumOpeningsPerBuilding)yield break;
                }
            }
        }
        static bool Clear(Vector3 p,float width,float height,Vector3 along,Vector3 outward,Func<Vector3,bool> exterior,Func<float,float,float> surface,bool door=false,BuildingEntryAudit.Row audit=null)
        {
            foreach(float offset in new[]{-.5f,0,.5f})
            {
                var q=p+along*(width*offset)+outward*.08f;
                if(!exterior(q)){if(audit!=null)audit.frameFailure="ExteriorReject";return false;}
                if(p.y-height*.5f<surface(q.x,q.z)+(door?.01f:.16f)){if(audit!=null)audit.frameFailure="FrameGroundReject";return false;}
            }
            return true;
        }
    }
}
