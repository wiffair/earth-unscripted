using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        // OSM centre lines remain the running-carriageway reference. Explicit
        // shoulders extend the surface asymmetrically and retain a solid line
        // separating them from the traffic lanes.
        void RoadSurface(Batch road,Batch verge,Batch markings,Feature feature,Vector3 a,Vector3 b,Vector3 rightA,Vector3 rightB,float station)
        {foreach(var row in RoadSurfaceRows(road,verge,markings,feature,a,b,rightA,rightB,station)){} }

        IEnumerable<bool> RoadSurfaceRows(Batch road,Batch verge,Batch markings,Feature feature,Vector3 a,Vector3 b,Vector3 rightA,Vector3 rightB,float station)
        {
            float travel=feature.travelWidth>0?feature.travelWidth:feature.width;
            float half=travel*.5f;
            var surfaceA=a+rightA*feature.roadCentreOffset;
            var surfaceB=b+rightB*feature.roadCentreOffset;
            // Gravel belongs outside the asphalt. A full support ribbon under
            // the carriageway could protrude through a lower merging lane.
            foreach(int sign in new[]{-1,1})
            {
                const float vergeWidthMetres=1.4f;
                float offset=(feature.width*.5f+vergeWidthMetres*.5f)*sign;
                RoadVerge(verge,feature,surfaceA+rightA*offset,surfaceB+rightB*offset,rightA,rightB,vergeWidthMetres*.5f);
            }
            foreach(var row in RoadRibbonRows(road,feature,a,b,rightA,rightB,half))yield return row;
            if(feature.shoulderLeft>0)
                foreach(var row in RoadRibbonRows(road,feature,a-rightA*(half+feature.shoulderLeft*.5f),b-rightB*(half+feature.shoulderLeft*.5f),rightA,rightB,feature.shoulderLeft*.5f))yield return row;
            if(feature.shoulderRight>0)
                foreach(var row in RoadRibbonRows(road,feature,a+rightA*(half+feature.shoulderRight*.5f),b+rightB*(half+feature.shoulderRight*.5f),rightA,rightB,feature.shoulderRight*.5f))yield return row;
            int lanes=feature.laneCount>0?feature.laneCount:Mathf.Max(1,Mathf.RoundToInt(travel/3.3f));
            if(!feature.roundabout && Mathf.FloorToInt(station/6)%2==0)
                for(int lane=1;lane<lanes;lane++)
                {
                    float offset=-half+travel*lane/lanes;
                    RoadPaint(markings,feature,a+rightA*offset,b+rightB*offset,rightA,rightB,.055f);
                }
            if(travel>5.5f)
                foreach(int sign in new[]{-1,1})
                    RoadPaint(markings,feature,a+rightA*(half-.12f)*sign,b+rightB*(half-.12f)*sign,rightA,rightB,.055f);
        }

        // Segments are source-resampled at about 2 m. Omit a cosmetic gravel
        // quad if it enters another lane's vehicle corridor; terrain still
        // supplies the ground there. This includes joined bridge/tunnel ways
        // and a sharp bend overlapping a distant segment of its own source way.
        void RoadVerge(Batch verge,Feature owner,Vector3 a,Vector3 b,Vector3 rightA,Vector3 rightB,float halfWidth)
        {
            var samples=new[]{a-rightA*halfWidth,a+rightA*halfWidth,b-rightB*halfWidth,b+rightB*halfWidth,(a+b)*.5f};
            foreach(var point in samples)
            {
                var nearby=Nearby(point.x,point.z);
                if(nearby==null)continue;
                foreach(var segment in nearby)
                {
                    var other=segment.feature;
                    if(other.category!="road")continue;
                    float distance=Project(segment,point.x,point.z,out float height);
                    if(distance>other.width*.5f+.05f)continue;
                    float separation=Mathf.Abs(height-point.y);
                    if(other.sourceId==owner.sourceId)
                    {
                        // Ordinary support beside this same local segment is
                        // valid; a different-height hairpin segment is not.
                        if(separation>.12f && separation<2f)return;
                    }
                    else if(separation<2f)return;
                }
            }
            verge.Strip(a,b,rightA,rightB,halfWidth,-.085f);
        }
        const float LaneGantrySpacingMetres=250;
        const float LaneGantryHeightMetres=6.5f;

        // Static OSM describes lane-control infrastructure, not current gantry
        // telemetry. Variable lanes start closed in this game approximation.
        void LaneGantry(Batch structure,Batch closed,Batch open,Feature feature,Vector3 a,Vector3 b,Vector3 rightA,Vector3 rightB,float begin,float end)
        {
            float station=Mathf.Ceil(begin/LaneGantrySpacingMetres)*LaneGantrySpacingMetres;
            if(station>=end || end-begin<.001f)return;
            float fraction=(station-begin)/(end-begin);
            var centre=Vector3.Lerp(a,b,fraction);var right=Vector3.Lerp(rightA,rightB,fraction).normalized;
            var forward=Vector3.ProjectOnPlane(b-a,Vector3.up).normalized;
            var nearby=Nearby(centre.x,centre.z);
            if(nearby!=null)
                foreach(var segment in nearby)
                {
                    if(segment.feature.sourceId==feature.sourceId)continue;
                    float distance=Project(segment,centre.x,centre.z,out float height);
                    if(distance<segment.feature.width*.5f+1 && height>centre.y+.75f && height<centre.y+8)return;
                }
            var surface=centre+right*feature.roadCentreOffset;
            foreach(int sign in new[]{-1,1})
            {
                var point=surface+right*(feature.width*.5f+1)*sign;
                if(InsideRoad(point))return;
            }
            foreach(int sign in new[]{-1,1})
            {
                var point=surface+right*(feature.width*.5f+1)*sign;
                structure.Box(point-forward*.18f,point+forward*.18f,right,right,.18f,0,LaneGantryHeightMetres);
            }
            structure.Box(surface-forward*.18f,surface+forward*.18f,right,right,feature.width*.5f+1,LaneGantryHeightMetres,LaneGantryHeightMetres+.25f);
            float travel=feature.travelWidth>0?feature.travelWidth:feature.width;
            int lanes=Mathf.Max(1,feature.laneCount);var access=(feature.laneAccess??"..").Split('|');
            for(int lane=0;lane<lanes;lane++)
            {
                var signal=centre+right*(-travel*.5f+travel*(lane+.5f)/lanes)+Vector3.up*(LaneGantryHeightMetres-.65f)-forward*.23f;
                bool isClosed=lane<access.Length&&(access[lane]=="variable"||access[lane]=="no");
                if(isClosed)
                {
                    SignalStroke(closed,signal-right*.28f-Vector3.up*.28f,signal+right*.28f+Vector3.up*.28f,forward,.065f);
                    SignalStroke(closed,signal-right*.28f+Vector3.up*.28f,signal+right*.28f-Vector3.up*.28f,forward,.065f);
                }
                else
                {
                    SignalStroke(open,signal-Vector3.up*.3f,signal+Vector3.up*.3f,forward,.065f);
                    SignalStroke(open,signal-Vector3.up*.3f,signal+right*.25f,forward,.065f);
                    SignalStroke(open,signal-Vector3.up*.3f,signal-right*.25f,forward,.065f);
                }
            }
            report.laneControlGantries++;
        }

        static void SignalStroke(Batch batch,Vector3 a,Vector3 b,Vector3 normal,float halfWidth)
        {
            var side=Vector3.Cross(normal,b-a).normalized*halfWidth;
            batch.Quad(a-side,b-side,b+side,a+side);
        }
    }
}
