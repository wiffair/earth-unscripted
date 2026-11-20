using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        static bool HasCore(Map map)=>map.coreMaxX>map.coreMinX&&map.coreMaxZ>map.coreMinZ;
        static bool InCore(Map map,Vector3 p)=>p.x>=map.coreMinX&&p.x<map.coreMaxX&&p.z>=map.coreMinZ&&p.z<map.coreMaxZ;
        static float RibbonReach(Feature f)
        {
            float half=Mathf.Max(Mathf.Abs(f.width)*.5f,Mathf.Abs(f.travelWidth)*.5f+Mathf.Max(Mathf.Abs(f.shoulderLeft),Mathf.Abs(f.shoulderRight)));
            // Join() bounds miter amplification by 1/.7. Include gravel,
            // tunnel lining and bridge edges as well as the running surface.
            return (half+Mathf.Abs(f.roadCentreOffset)+2)/.7f;
        }
        static bool RectangleRange(Vector3 a,Vector3 b,float west,float south,float east,float north,out float lo,out float hi)
        {
            lo=0;hi=1;
            if(Mathf.Abs(a.x-b.x)<.000001f&&(a.x<west||a.x>=east))return false;
            if(Mathf.Abs(a.z-b.z)<.000001f&&(a.z<south||a.z>=north))return false;
            return Clip(a.x-b.x,a.x-west,ref lo,ref hi)&&Clip(b.x-a.x,east-a.x,ref lo,ref hi)&&Clip(a.z-b.z,a.z-south,ref lo,ref hi)&&Clip(b.z-a.z,north-a.z,ref lo,ref hi)&&hi-lo>.00001f;
        }
        struct RibbonRanges
        {
            public int count,ownedCount;Vector2 first,second,third;
            public Vector2 At(int i)=>i==0?first:i==1?second:third;
            public void Add(float lo,float hi){if(hi-lo<=.00001f)return;var range=new Vector2(lo,hi);if(count==0)first=range;else if(count==1)second=range;else third=range;count++;}
        }
        static RibbonRanges SourceEdgeRanges(Map map,Feature f,Vector3 a,Vector3 b,float x,float z)
        {
            var result=new RibbonRanges();
            if(Range(a,b,x,z,out float lo,out float hi))result.Add(lo,hi);
            result.ownedCount=result.count;
            if(!HasCore(map)||(InCore(map,a)&&InCore(map,b)))return result;
            float reach=RibbonReach(f);
            if(!RectangleRange(a,b,x-reach,z-reach,x+ChunkSize+reach,z+ChunkSize+reach,out float nearLo,out float nearHi))return result;
            // The halo already contains this complete fitted road profile. Its
            // centre line can lie in an unavailable adjacent core while its
            // pavement extends into the owned core. Borrow only the exterior
            // intervals; owned centre-line intervals keep their existing chunks.
            if(RectangleRange(a,b,map.coreMinX,map.coreMinZ,map.coreMaxX,map.coreMaxZ,out float coreLo,out float coreHi))
            {result.Add(nearLo,Mathf.Min(nearHi,coreLo));result.Add(Mathf.Max(nearLo,coreHi),nearHi);}
            else result.Add(nearLo,nearHi);
            return result;
        }
        static void IndexSourceEdgeRibbon(Map map,Feature f,Vector3 a,Vector3 b,Dictionary<Vector2Int,HashSet<Feature>> index)
        {
            if(!HasCore(map)||(InCore(map,a)&&InCore(map,b)))return;
            float reach=RibbonReach(f);
            int firstX=Mathf.Max(Mathf.FloorToInt(map.coreMinX/ChunkSize),Mathf.FloorToInt((Mathf.Min(a.x,b.x)-reach)/ChunkSize));
            int lastX=Mathf.Min(Mathf.CeilToInt(map.coreMaxX/ChunkSize)-1,Mathf.FloorToInt((Mathf.Max(a.x,b.x)+reach)/ChunkSize));
            int firstZ=Mathf.Max(Mathf.FloorToInt(map.coreMinZ/ChunkSize),Mathf.FloorToInt((Mathf.Min(a.z,b.z)-reach)/ChunkSize));
            int lastZ=Mathf.Min(Mathf.CeilToInt(map.coreMaxZ/ChunkSize)-1,Mathf.FloorToInt((Mathf.Max(a.z,b.z)+reach)/ChunkSize));
            for(int z=firstZ;z<=lastZ;z++)for(int x=firstX;x<=lastX;x++)
            {var key=new Vector2Int(x,z);if(!index.TryGetValue(key,out var set))index[key]=set=new HashSet<Feature>();set.Add(f);}
        }
        sealed partial class Batch
        {
            public bool SourceClipEnabled;
            float clipWest,clipSouth,clipEast,clipNorth;
            public void ClipToSource(Map map)
            {SourceClipEnabled=HasCore(map);clipWest=map.coreMinX;clipSouth=map.coreMinZ;clipEast=map.coreMaxX;clipNorth=map.coreMaxZ;}
            public void ClipToSourceChunk(Map map,float x,float z)
            {
                ClipToSource(map);if(!SourceClipEnabled)return;
                clipWest=Mathf.Max(clipWest,x);clipEast=Mathf.Min(clipEast,x+ChunkSize);
                clipSouth=Mathf.Max(clipSouth,z);clipNorth=Mathf.Min(clipNorth,z+ChunkSize);
            }
            bool InsideSource(Vector3 p)=>p.x>=clipWest&&p.x<=clipEast&&p.z>=clipSouth&&p.z<=clipNorth;
            static void ClipVertex(Span<Vector3> output,ref int count,Vector3 point)
            {if(count>0&&output[count-1].Equals(point))return;if(count>=output.Length)throw new InvalidOperationException("Convex ribbon clip overflow");output[count++]=point;}
            static int ClipPlane(Span<Vector3> input,int count,Span<Vector3> output,int axis,float bound,bool greater)
            {
                if(count==0)return 0;int written=0;var previous=input[count-1];float value=axis==0?previous.x:previous.z;bool wasInside=greater?value>=bound:value<=bound;
                for(int i=0;i<count;i++)
                {
                    var next=input[i];float nextValue=axis==0?next.x:next.z;bool inside=greater?nextValue>=bound:nextValue<=bound;
                    if(inside!=wasInside){float t=(bound-value)/(nextValue-value);var crossing=Vector3.LerpUnclamped(previous,next,t);if(axis==0)crossing.x=bound;else crossing.z=bound;ClipVertex(output,ref written,crossing);}
                    if(inside)ClipVertex(output,ref written,next);previous=next;value=nextValue;wasInside=inside;
                }
                if(written>1&&output[0].Equals(output[written-1]))written--;
                return written;
            }
            void SourceTriangle(Vector3 a,Vector3 b,Vector3 c)
            {
                Span<Vector3> first=stackalloc Vector3[8];Span<Vector3> second=stackalloc Vector3[8];first[0]=a;first[1]=b;first[2]=c;
                int count=ClipPlane(first,3,second,0,clipWest,true);count=ClipPlane(second,count,first,0,clipEast,false);
                count=ClipPlane(first,count,second,1,clipSouth,true);count=ClipPlane(second,count,first,1,clipNorth,false);
                if(count<3)return;
                for(int i=1;i<count-1;i++)
                {
                    var x=first[0];var y=first[i];var z=first[i+1];if(Vector3.Cross(y-x,z-x).sqrMagnitude<1e-16f)continue;
                    int start=vertices.Count;vertices.Add(x);vertices.Add(y);vertices.Add(z);triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
                }
            }
        }
    }
}
