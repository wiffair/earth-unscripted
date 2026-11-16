using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    // Original, bounded, world-space solid access geometry. Terrain/canonical map
    // data are queried only. No GameObjects, materials or colliders are allocated.
    public static class BuildingAccessGeometry
    {
        public const float MinimumDrop=.05f,MaximumRiser=.19f,MinimumTread=.28f,MaximumReach=3.5f,ProbeSpacing=.1f;
        public const float MaximumDrop=MaximumSteps*MaximumRiser,MaximumSupportDepth=MaximumDrop+.15f;
        public const int MaximumSteps=12,MaximumVertices=8192;
        public enum Rejection { None,Input,Rotation,DropSmall,DropLarge,Count,Budget,SurfaceNonfinite,SurfaceAboveTread,SupportDepth,Clearance,FinalLandingDrop,CrossGrade,LongGrade,DegenerateMesh }
        static bool Reject(out Data data,out Rejection reason,Rejection value){data=null;reason=value;return false;}
        public sealed class Data
        {
            public readonly Vector3[] Verts,Normals;public readonly int[] Tris;public readonly Vector2[] UV;public readonly Bounds Bounds;
            public readonly int Count;public readonly float Rise,TreadDepth,Reach;
            internal Data(Vector3[] verts,int[] tris,Vector2[] uv,Vector3[] normals,Bounds bounds,int count,float rise,float tread)
            {Verts=verts;Tris=tris;UV=uv;Normals=normals;Bounds=bounds;Count=count;Rise=rise;TreadDepth=tread;Reach=count*tread;}
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        public readonly struct SearchAttempt
        {
            public readonly int Variant,Iterations,Count;public readonly float TargetDrop;public readonly Rejection Reason;public readonly bool RoadBlocked,Accepted;
            public SearchAttempt(int variant,int iterations,int count,float drop,Rejection reason,bool road=false,bool accepted=false){Variant=variant;Iterations=iterations;Count=count;TargetDrop=drop;Reason=reason;RoadBlocked=road;Accepted=accepted;}
        }
        public static bool TryFindStepFlight(Vector3 threshold,Quaternion outward,float width,float nearGround,Func<Vector3,bool> clear,Func<float,float,float> surface,Func<Vector3[],bool> footprintClear,out Data data,out SearchAttempt result,Action<SearchAttempt> observe=null)
        {
            data=null;result=new SearchAttempt(-1,0,0,0,Rejection.Input);
            float norm=outward.x*outward.x+outward.y*outward.y+outward.z*outward.z+outward.w*outward.w;
            if(clear==null||surface==null||footprintClear==null||!Finite(threshold)||!Finite(nearGround)||!Finite(width)||width<.5f||width>3||!Finite(norm)||Mathf.Abs(norm-1)>.001f||Mathf.Abs(outward.x)>.000001f||Mathf.Abs(outward.z)>.000001f){observe?.Invoke(result);return false;}
            outward=outward.normalized;var forward=outward*Vector3.forward;var right=outward*Vector3.right;int across=Mathf.CeilToInt(width/ProbeSpacing);
            for(int variant=0;variant<3;variant++)
            {
                float target=nearGround;int iterations=0;bool invalid=false;
                for(int attempt=0;attempt<5;attempt++)
                {
                    iterations=attempt+1;int probeCount=Mathf.Clamp(Mathf.CeilToInt((threshold.y-target)/MaximumRiser),1,MaximumSteps);var end=threshold+forward*(probeCount*MinimumTread);float low=float.MaxValue,high=float.MinValue;
                    for(int x=0;x<=across;x++){var p=end+right*(width*(x/(float)across-.5f));float y=surface(p.x,p.z);if(!Finite(y)){invalid=true;break;}low=Mathf.Min(low,y);high=Mathf.Max(high,y);}if(invalid)break;
                    float next=variant==0?low:variant==1?(low+high)*.5f:high;bool converged=Mathf.Abs(target-next)<.001f;target=next;if(converged)break;
                }
                float drop=threshold.y-target;int count=Mathf.CeilToInt(drop/MaximumRiser);
                if(invalid){result=new SearchAttempt(variant,iterations,count,drop,Rejection.SurfaceNonfinite);observe?.Invoke(result);continue;}
                if(count<1||count>MaximumSteps){result=new SearchAttempt(variant,iterations,count,drop,Rejection.Count);observe?.Invoke(result);continue;}
                var extent=right*(width*.5f);var endPoint=threshold+forward*(count*MinimumTread);var polygon=new[]{threshold-extent,threshold+extent,endPoint+extent,endPoint-extent,threshold-extent};
                if(!footprintClear(polygon)){result=new SearchAttempt(variant,iterations,count,drop,Rejection.Clearance,true);observe?.Invoke(result);continue;}
                bool accepted=TryCreateStepFlight(threshold,outward,width,target,clear,surface,out data,out var reason);result=new SearchAttempt(variant,iterations,count,drop,reason,false,accepted);observe?.Invoke(result);if(accepted)return true;
            }
            return false;
        }
        public static bool TryCreateStepFlight(Vector3 threshold,Quaternion outward,float width,float targetGroundY,Func<Vector3,bool> clear,Func<float,float,float> surface,out Data data)
            =>TryCreateStepFlight(threshold,outward,width,targetGroundY,clear,surface,out data,out _);
        public static bool TryCreateStepFlight(Vector3 threshold,Quaternion outward,float width,float targetGroundY,Func<Vector3,bool> clear,Func<float,float,float> surface,out Data data,out Rejection rejection)
        {
            data=null;rejection=Rejection.None;if(clear==null||surface==null||!Finite(threshold)||!Finite(width)||width<.5f||width>3||!Finite(targetGroundY))return Reject(out data,out rejection,Rejection.Input);
            float q=outward.x*outward.x+outward.y*outward.y+outward.z*outward.z+outward.w*outward.w;
            if(!Finite(q)||Mathf.Abs(q-1)>.001f||Mathf.Abs(outward.x)>0.000001f||Mathf.Abs(outward.z)>0.000001f)return Reject(out data,out rejection,Rejection.Rotation);outward=outward.normalized;
            float drop=threshold.y-targetGroundY;if(!Finite(drop))return Reject(out data,out rejection,Rejection.Input);if(drop<MinimumDrop)return Reject(out data,out rejection,Rejection.DropSmall);if(drop>MaximumDrop)return Reject(out data,out rejection,Rejection.DropLarge);
            int count=Mathf.CeilToInt(drop/MaximumRiser);if(count<1||count>MaximumSteps)return Reject(out data,out rejection,Rejection.Count);float rise=drop/count,tread=MinimumTread,reach=count*tread;
            if(rise>MaximumRiser+.000001f||reach>MaximumReach)return Reject(out data,out rejection,Rejection.Count);
            int nx=Mathf.CeilToInt(width/ProbeSpacing),sections=Mathf.CeilToInt(tread/ProbeSpacing),nz=count*sections,quadCount=nx*nz+2*nz+2*nx+count+count-1;
            if(quadCount*4>MaximumVertices)return Reject(out data,out rejection,Rejection.Budget);
            var bottom=new Vector3[nz+1,nx+1];var ground=new float[nz+1,nx+1];
            float Z(int row)=>(row/sections+(row%sections)/(float)sections)*tread;
            Vector3 Point(float x,float y,float z)=>threshold+outward*new Vector3(x,y,z);
            float Top(int step)=>threshold.y-rise*step;
            // First landing is level with the doorway. Target is maximum garden
            // ground across the far end; the last riser descends to that ground.
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
            {
                float xx=width*(x/(float)nx-.5f),zz=Z(z),top=Top(Math.Min(z/sections,count-1));var p=Point(xx,0,zz);float y=surface(p.x,p.z);
                if(!Finite(y))return Reject(out data,out rejection,Rejection.SurfaceNonfinite);if(y>top+.0001f)return Reject(out data,out rejection,Rejection.SurfaceAboveTread);if(top-y>MaximumSupportDepth)return Reject(out data,out rejection,Rejection.SupportDepth);
                ground[z,x]=y;bottom[z,x]=new Vector3(p.x,y-.05f,p.z);
                if(!clear(new Vector3(p.x,top,p.z))||!clear(bottom[z,x]))return Reject(out data,out rejection,Rejection.Clearance);
                // Do not bridge a terrain discontinuity/cliff or introduce a last
                // unplanned high drop off the final tread into its garden landing.
                if(z==nz&&Top(count-1)-y>MaximumRiser+.0001f)return Reject(out data,out rejection,Rejection.FinalLandingDrop);
                if(x>0&&Mathf.Abs(y-ground[z,x-1])>1.25f*width/nx+.001f)return Reject(out data,out rejection,Rejection.CrossGrade);
                if(z>0&&Mathf.Abs(y-ground[z-1,x])>1.25f*(Z(z)-Z(z-1))+.001f)return Reject(out data,out rejection,Rejection.LongGrade);
            }
            var vertices=new List<Vector3>((nz+1)*(nx+1)+4*(2*nz+2*nx+count+count-1));var triangles=new List<int>(quadCount*6);var tex=new List<Vector2>(quadCount*4);
            bool valid=true,hasBounds=false;Bounds trackedBounds=default;
            void Track(Vector3 p){if(!hasBounds){trackedBounds=new Bounds(p,Vector3.zero);hasBounds=true;}trackedBounds.Encapsulate(p);}
            int Vertex(Vector3 p,int plane){int index=vertices.Count;vertices.Add(p);var local=Quaternion.Inverse(outward)*(p-threshold);tex.Add(plane==0?new Vector2(local.x,local.z):plane==1?new Vector2(local.z,local.y):new Vector2(local.x,local.y));return index;}
            var gridIndices=new int[nz+1,nx+1];for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)gridIndices[z,x]=-1;
            int Grid(int z,int x){if(gridIndices[z,x]<0)gridIndices[z,x]=Vertex(bottom[z,x],0);return gridIndices[z,x];}
            void BottomQuad(int z,int x)
            {
                var a=bottom[z,x];var b=bottom[z,x+1];var c=bottom[z+1,x];var d=bottom[z+1,x+1];Track(a);Track(b);Track(c);Track(d);
                int aa=Grid(z,x),bb=Grid(z,x+1),cc=Grid(z+1,x),dd=Grid(z+1,x+1);triangles.AddRange(new[]{aa,bb,cc,bb,dd,cc});valid&=Vector3.Cross(b-a,c-a).sqrMagnitude>.0000000001f;
            }
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int plane,bool reverse=false)
            {
                int start=vertices.Count;foreach(var p in new[]{a,b,c,d}){Track(p);Vertex(p,plane);}
                int[] indices=reverse?new[]{0,1,2,1,3,2}:new[]{0,2,1,1,2,3};foreach(int i in indices)triangles.Add(start+i);
                valid&=Vector3.Cross(vertices[start+indices[1]]-vertices[start],vertices[start+indices[2]]-vertices[start]).sqrMagnitude>.0000000001f;
            }
            Vector3 At(float x,float worldY,float z)=>Point(x,worldY-threshold.y,z);
            for(int i=0;i<count;i++)
            {
                float z0=i*tread,z1=(i+1)*tread,y=Top(i);Quad(At(-width*.5f,y,z0),At(width*.5f,y,z0),At(-width*.5f,y,z1),At(width*.5f,y,z1),0);
                if(i+1<count){float low=Top(i+1);Quad(At(width*.5f,low,z1),At(-width*.5f,low,z1),At(width*.5f,y,z1),At(-width*.5f,y,z1),2);}
            }
            for(int z=0;z<nz;z++)
            {
                float z0=Z(z),z1=Z(z+1),top=Top(z/sections);
                for(int x=0;x<nx;x++)BottomQuad(z,x);
                Quad(bottom[z,0],At(-width*.5f,top,z0),bottom[z+1,0],At(-width*.5f,top,z1),1);
                Quad(At(width*.5f,top,z0),bottom[z,nx],At(width*.5f,top,z1),bottom[z+1,nx],1);
            }
            for(int x=0;x<nx;x++)
            {
                float x0=width*(x/(float)nx-.5f),x1=width*((x+1)/(float)nx-.5f);
                Quad(bottom[0,x],bottom[0,x+1],At(x0,Top(0),0),At(x1,Top(0),0),2);
                Quad(bottom[nz,x+1],bottom[nz,x],At(x1,Top(count-1),reach),At(x0,Top(count-1),reach),2);
            }
            if(!valid||vertices.Count>MaximumVertices)return Reject(out data,out rejection,Rejection.DegenerateMesh);
            var normals=new Vector3[vertices.Count];for(int i=0;i<triangles.Count;i+=3){int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);if(n.sqrMagnitude<.0000000001f)return Reject(out data,out rejection,Rejection.DegenerateMesh);normals[a]+=n;normals[b]+=n;normals[c]+=n;}
            for(int i=0;i<vertices.Count;i++){if(!Finite(vertices[i])||!Finite(normals[i]))return Reject(out data,out rejection,Rejection.DegenerateMesh);normals[i]=normals[i].normalized;}
            data=new Data(vertices.ToArray(),triangles.ToArray(),tex.ToArray(),normals,trackedBounds,count,rise,tread);return true;
        }
    }
}
