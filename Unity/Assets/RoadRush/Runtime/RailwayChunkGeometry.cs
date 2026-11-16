using System;
using System.Collections.Generic;
using UnityEngine;
namespace RoadRush
{
    // Geometry consumes the prepared profile verbatim. It never solves elevations or adds train physics.
    public static class RailwayChunkGeometry
    {
        const int BatchVertices=8192;
        sealed class Batch
        {
            public readonly string Name;public readonly Material Material;public readonly bool Collision;
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> triangles=new List<int>();readonly List<Vector2> uv=new List<Vector2>();int serial;
            public Transform Root;public RailwayChunkMeshOwner Owner;public int Count=>vertices.Count;
            public Batch(string name,Material material,bool collision){Name=name;Material=material;Collision=collision;}
            public void Flush(Transform root){if(Count==0)return;var generated=Geometry.Mesh(Name+" "+serial++,root,vertices.ToArray(),triangles.ToArray(),uv.ToArray(),Material,Collision);generated.GetComponent<MeshFilter>().sharedMesh.RecalculateTangents();Owner.Register(generated);vertices.Clear();triangles.Clear();uv.Clear();}
            void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
            {
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){var swap=b;b=d;d=swap;}
                int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
                // Planar world-metric projection keeps map phase continuous across chunk/segment cuts.
                // Horizontal surfaces use XZ; vertical course height always uses world Y.
                var normal=Vector3.Cross(b-a,c-a).normalized;Vector2 Project(Vector3 p)=>Mathf.Abs(normal.y)>.7071068f?new Vector2(p.x,p.z):Mathf.Abs(normal.x)>Mathf.Abs(normal.z)?new Vector2(p.z,p.y):new Vector2(p.x,p.y);
                uv.Add(Project(a));uv.Add(Project(b));uv.Add(Project(c));uv.Add(Project(d));
                triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);triangles.Add(i);triangles.Add(i+2);triangles.Add(i+3);
            }
            public void EarthBody(Vector3 a,Vector3 b,Vector3 sa,Vector3 sb,float half,float toeA,float toeB,float bottomA,float bottomB)
            {
                if(Count+24>BatchVertices)Flush(Root);
                var al=a-sa*toeA;var ar=a+sa*toeA;var bl=b-sb*toeB;var br=b+sb*toeB;al.y=ar.y=bottomA;bl.y=br.y=bottomB;
                var alt=a-sa*half-Vector3.up*RailwayMetrics.TrackBuildUp;var art=a+sa*half-Vector3.up*RailwayMetrics.TrackBuildUp;
                var blt=b-sb*half-Vector3.up*RailwayMetrics.TrackBuildUp;var brt=b+sb*half-Vector3.up*RailwayMetrics.TrackBuildUp;
                var dir=b-a;var side=(sa+sb)*.5f;
                Face(alt,art,brt,blt,Vector3.up);Face(al,bl,br,ar,Vector3.down);Face(al,alt,blt,bl,-side);Face(ar,br,brt,art,side);Face(al,ar,art,alt,-dir);Face(bl,blt,brt,br,dir);
            }
            public void Prism(Vector3 a,Vector3 b,Vector3 sideA,Vector3 sideB,float halfWidth,float bottom,float top,float topHalfWidth=-1)
            {
                if(Count+24>BatchVertices)Flush(Root);
                if(topHalfWidth<0)topHalfWidth=halfWidth;
                Vector3 al=a-sideA*halfWidth+Vector3.up*bottom,ar=a+sideA*halfWidth+Vector3.up*bottom,bl=b-sideB*halfWidth+Vector3.up*bottom,br=b+sideB*halfWidth+Vector3.up*bottom;
                Vector3 alt=a-sideA*topHalfWidth+Vector3.up*top,art=a+sideA*topHalfWidth+Vector3.up*top,blt=b-sideB*topHalfWidth+Vector3.up*top,brt=b+sideB*topHalfWidth+Vector3.up*top;
                Vector3 dir=b-a,side=(sideA+sideB)*.5f;
                Face(alt,art,brt,blt,Vector3.up);Face(al,bl,br,ar,Vector3.down);Face(al,alt,blt,bl,-side);Face(ar,br,brt,art,side);Face(al,ar,art,alt,-dir);Face(bl,blt,brt,br,dir);
            }
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static bool Finite(Vector3 p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z);
        static Vector3 Side(Vector3 a,Vector3 b){var d=b-a;d.y=0;return d.sqrMagnitude>.000001f?new Vector3(d.z,0,-d.x).normalized:Vector3.right;}
        static Vector3 Join(Vector3[] points,int i)
        {
            var before=Side(points[Mathf.Max(0,i-1)],points[i]);var after=Side(points[i],points[Mathf.Min(points.Length-1,i+1)]);
            if(i==0)return after;if(i==points.Length-1)return before;
            var sum=before+after;if(sum.sqrMagnitude<.01f)return after;
            var normal=sum.normalized;return normal/Mathf.Max(.5f,Vector3.Dot(normal,after)); // bounded miter, shared across chunks
        }
        static bool Clip(float p,float q,ref float lo,ref float hi){if(Mathf.Abs(p)<.000001f)return q>=0;float r=q/p;if(p<0){if(r>hi)return false;lo=Mathf.Max(lo,r);}else{if(r<lo)return false;hi=Mathf.Min(hi,r);}return true;}
        static bool Range(Vector3 a,Vector3 b,Vector2Int key,float size,out float lo,out float hi)
        {
            lo=0;hi=1;float x=key.x*size,z=key.y*size;
            // Exact half-open ownership avoids duplicate parallel features on chunk boundaries.
            if(Mathf.Abs(a.x-b.x)<.000001f&&(a.x<x||a.x>=x+size))return false;
            if(Mathf.Abs(a.z-b.z)<.000001f&&(a.z<z||a.z>=z+size))return false;
            return Clip(a.x-b.x,a.x-x,ref lo,ref hi)&&Clip(b.x-a.x,x+size-a.x,ref lo,ref hi)&&Clip(a.z-b.z,a.z-z,ref lo,ref hi)&&Clip(b.z-a.z,z+size-a.z,ref lo,ref hi)&&hi-lo>.000001f;
        }
        static bool Owned(Vector3 p,Vector2Int key,float size)=>Mathf.FloorToInt(p.x/size)==key.x&&Mathf.FloorToInt(p.z/size)==key.y;
        static void Sleeper(Batch batch,Vector3 p,Vector3 side,float gauge)
        {var forward=new Vector3(-side.z,0,side.x).normalized;batch.Prism(p-forward*.125f,p+forward*.125f,side.normalized,side.normalized,(gauge+1.05f)*.5f,-.30f,-.16f);}
        static void Support(Batch batch,Vector3 p,Vector3 side,float width,float depth,Func<float,float,float> ground,Func<Vector3,float,bool> allowed)
        {
            if(ground==null||allowed==null)return;var forward=new Vector3(-side.z,0,side.x).normalized;side=side.normalized;
            float radius=Mathf.Sqrt(width*width+depth*depth)*.5f;
            if(!allowed(p,radius))return;
            float low=float.PositiveInfinity,high=float.NegativeInfinity;
            for(int i=0;i<4;i++){var corner=p+side*width*.5f*((i&1)==0?-1:1)+forward*depth*.5f*((i&2)==0?-1:1);float y=ground(corner.x,corner.z);if(!Finite(y))return;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}
            float top=p.y-RailwayMetrics.UnderDeckDrop;
            if(top-high<.35f||top-low>80)return;
            batch.Prism(p-forward*depth*.5f,p+forward*depth*.5f,side,side,width*.5f,low-p.y-.3f,-RailwayMetrics.UnderDeckDrop);
        }
        static bool Bottom(Vector3 p,Vector3 side,float half,Func<float,float,float> ground,out float bottom)
        {
            bottom=float.PositiveInfinity;
            for(int i=-1;i<=1;i++){var q=p+side*(i*half);float h=ground(q.x,q.z);if(!Finite(h))return false;bottom=Mathf.Min(bottom,h-.15f);}
            bottom=Mathf.Min(bottom,p.y-RailwayMetrics.TrackBuildUp-.02f);
            return p.y-bottom<80;
        }
        static float FootprintRadius(Vector3 a,Vector3 b,Vector3 sa,Vector3 sb,float wa,float wb)
        {
            var centre=(a+b)*.5f;float radius=0;
            for(int i=-1;i<=1;i+=2){var da=a+sa*(wa*i)-centre;var db=b+sb*(wb*i)-centre;da.y=db.y=0;radius=Mathf.Max(radius,da.magnitude,db.magnitude);}
            return radius+.01f;
        }
        static void Approach(Batch earth,Batch retaining,RailwayChunkMeshOwner owner,Vector3 a,Vector3 b,Vector3 sa,Vector3 sb,float half,Func<float,float,float> ground,Func<Vector3,float,bool> allowed)
        {
            if(ground==null||allowed==null){owner.ApproachExcluded++;return;}
            float ga=ground(a.x,a.z),gb=ground(b.x,b.z);
            if(!Finite(ga)||!Finite(gb)){owner.ApproachInvalidGround++;return;}
            float ta=a.y-RailwayMetrics.TrackBuildUp,tb=b.y-RailwayMetrics.TrackBuildUp;
            if(Mathf.Max(ta-ga,tb-gb)<=.18f)return;
            if(!Bottom(a,sa,half,ground,out float ba)||!Bottom(b,sb,half,ground,out float bb)){owner.ApproachInvalidGround++;return;}
            float compactA=ba,compactB=bb,toeA=half,toeB=half;bool bounded=true;
            // Bounded terrain following; exact 1.5 horizontal : 1 vertical toes.
            // If terrain falls away too quickly to converge, prefer the compact body.
            for(int n=0;n<4;n++)
            {
                toeA=half+1.5f*Mathf.Max(0,ta-ba);toeB=half+1.5f*Mathf.Max(0,tb-bb);
                if(!Bottom(a,sa,toeA,ground,out float nextA)||!Bottom(b,sb,toeB,ground,out float nextB)){bounded=false;break;}
                if(nextA>=ba-.02f&&nextB>=bb-.02f)break;
                if(n==3){bounded=false;break;}ba=Mathf.Min(ba,nextA);bb=Mathf.Min(bb,nextB);
            }
            var centre=(a+b)*.5f;
            if(bounded&&allowed(centre,FootprintRadius(a,b,sa,sb,toeA,toeB)))
            {earth.EarthBody(a,b,sa,sb,half,toeA,toeB,ba,bb);owner.ApproachEarthBodies++;return;}
            if(allowed(centre,FootprintRadius(a,b,sa,sb,half,half)))
            {retaining.EarthBody(a,b,sa,sb,half,half,half,compactA,compactB);owner.ApproachRetainingBodies++;return;}
            owner.ApproachExcluded++;
        }
        public static IEnumerable<int> Build(Transform root,GeoRegion region,IReadOnlyList<GeoRailway> railways,Vector2Int key,float chunkSize,Func<float,float,float> ground,Func<Vector3,float,bool> supportAllowed)
        {
            if(!root||railways==null||!Finite(chunkSize)||chunkSize<=0)yield break;
            var chunkOwner=root.GetComponent<RailwayChunkMeshOwner>();if(!chunkOwner)chunkOwner=root.gameObject.AddComponent<RailwayChunkMeshOwner>();
            var batches=new List<Batch>();int work=0;
            var steel=new Batch("Railway rails",RailwayMaterials.Rail(false),false);var rust=new Batch("Railway disused rails",RailwayMaterials.Rail(true),false);
            var sleepers=new Batch("Railway sleepers",RailwayMaterials.Sleepers,false);var ballast=new Batch("Railway ballast",RailwayMaterials.Ballast(false),false);var abandoned=new Batch("Railway abandoned formation",RailwayMaterials.Ballast(true),false);
            var deck=new Batch("Railway closed bridge deck and supports",RailwayMaterials.Structure,true);var barriers=new Batch("Railway bridge barriers",RailwayMaterials.Barrier,true);
            var earth=new Batch("Railway approach earth embankment",RailwayMaterials.Earth,true);var retaining=new Batch("Railway approach retaining stone",RailwayMaterials.Structure,true);
            batches.AddRange(new[]{steel,rust,sleepers,ballast,abandoned,deck,barriers,earth,retaining});foreach(var batch in batches){batch.Root=root;batch.Owner=chunkOwner;}
            foreach(var way in railways)
            {
                if(way==null||way.tunnel||!RailwayMetrics.Visible(way.kind)||way.points==null||way.points.Length<2)continue;
                var points=region!=null?region.CopyEffectiveRailwayPoints(way):way.points;bool valid=true;foreach(var point in points)if(!Finite(point)){valid=false;break;}if(!valid)continue;
                int tracks=Mathf.Clamp(way.tracks,1,16);float gauge=Finite(way.gauge)&&way.gauge>=.3f&&way.gauge<=2.5f?way.gauge:RailwayMetrics.DefaultGauge;
                float width=Mathf.Max(RailwayMetrics.DefaultWidth+(tracks-1)*RailwayMetrics.TrackSpacing,Finite(way.width)?Mathf.Clamp(way.width,1,100):RailwayMetrics.DefaultWidth);
                bool retired=way.kind=="abandoned",embedded=way.kind=="tram"&&!way.bridge,mono=way.kind=="monorail";Batch rail=way.kind=="disused"?rust:steel;float distance=0;
                for(int i=0;i<points.Length-1;i++)
                {
                    Vector3 a=points[i],b=points[i+1],flat=b-a;flat.y=0;float length=flat.magnitude;
                    if(length<.001f)continue;
                    if(!Range(a,b,key,chunkSize,out float lo,out float hi)){distance+=length;continue;}
                    Vector3 sa=Join(points,i),sb=Join(points,i+1),ca=Vector3.Lerp(a,b,lo),cb=Vector3.Lerp(a,b,hi),csa=Vector3.Lerp(sa,sb,lo),csb=Vector3.Lerp(sa,sb,hi);
                    if(!embedded&&!mono)(retired?abandoned:ballast).Prism(ca,cb,csa,csb,width*.5f,-.45f,-.29f,width*.5f-.25f);
                    if(!retired)for(int t=0;t<tracks;t++)
                    {
                        float centre=(t-(tracks-1)*.5f)*RailwayMetrics.TrackSpacing;
                        if(mono)rail.Prism(ca+csa*centre,cb+csb*centre,csa,csb,.32f,-.45f,0);
                        else for(int side=-1;side<=1;side+=2)
                        {
                            Vector3 ra=ca+csa*(centre+side*gauge*.5f),rb=cb+csb*(centre+side*gauge*.5f);
                            rail.Prism(ra,rb,csa,csb,.035f,-.055f,0);rail.Prism(ra,rb,csa,csb,.009f,-.13f,-.055f);rail.Prism(ra,rb,csa,csb,.07f,-.16f,-.13f);
                        }
                    }
                    if(way.bridge)
                    {
                        deck.Prism(ca,cb,csa,csb,width*.5f+.4f,-RailwayMetrics.UnderDeckDrop,-RailwayMetrics.TrackBuildUp);
                        for(int side=-1;side<=1;side+=2)
                        {Vector3 ba=ca+csa*(width*.5f+.25f)*side,bb=cb+csb*(width*.5f+.25f)*side;
                            bool clear=true;
                            foreach(var other in railways)
                            {
                                if(other==null||other==way||other.tunnel||!RailwayMetrics.Visible(other.kind))continue;
                                var otherPoints=region!=null?region.CopyEffectiveRailwayPoints(other):other.points;
                                float otherWidth=Mathf.Max(4.2f,other.width+(Mathf.Max(1,other.tracks)-1)*RailwayMetrics.TrackSpacing);
                                if(!RailwayBarrierClearance.Clear(ba,bb,otherPoints,otherWidth,region!=null&&region.countryCode=="GB"?5.6f:6f)){clear=false;break;}
                            }
                            if(clear){barriers.Prism(ba,bb,csa,csb,.10f,-.45f,-.05f);barriers.Prism(ba,bb,csa,csb,.065f,.55f,.67f);}}
                    }
                    float start=distance+lo*length,end=distance+hi*length;
                    if(!way.bridge&&!retired&&!embedded&&!mono)
                    {
                        for(int slice=Mathf.FloorToInt(start/2.5f);slice<=Mathf.FloorToInt(end/2.5f);slice++)
                        {
                            float from=Mathf.Max(start,slice*2.5f),to=Mathf.Min(end,(slice+1)*2.5f);if(to-from<.0001f)continue;
                            float f0=(from-distance)/length,f1=(to-distance)/length;
                            Approach(earth,retaining,chunkOwner,Vector3.Lerp(a,b,f0),Vector3.Lerp(a,b,f1),Vector3.Lerp(sa,sb,f0),Vector3.Lerp(sa,sb,f1),width*.5f,ground,supportAllowed);
                            if(++work%16==0)yield return work;
                        }
                    }
                    if(!retired&&!embedded&&!mono)
                    {
                        int first=Mathf.CeilToInt((start-.00001f)/.65f),last=Mathf.FloorToInt((end+.00001f)/.65f);
                        for(int station=first;station<=last;station++)
                        {
                            float f=(station*.65f-distance)/length;if(f<0||f>=1)continue;Vector3 p=Vector3.Lerp(a,b,f),s=Vector3.Lerp(sa,sb,f);
                            if(!Owned(p,key,chunkSize))continue;
                            for(int t=0;t<tracks;t++)Sleeper(sleepers,p+s*((t-(tracks-1)*.5f)*RailwayMetrics.TrackSpacing),s,gauge);
                            if(++work%32==0){foreach(var batch in batches)if(batch.Count>=BatchVertices)batch.Flush(root);yield return work;}
                        }
                    }
                    if(way.bridge)
                    {
                        for(int station=Mathf.CeilToInt(start/12f);station<=Mathf.FloorToInt(end/12f);station++)
                        {float f=(station*12f-distance)/length;if(f<0||f>=1)continue;Vector3 p=Vector3.Lerp(a,b,f),s=Vector3.Lerp(sa,sb,f);if(!Owned(p,key,chunkSize))continue;
                            Support(deck,p,s,width+.6f,1.1f,ground,supportAllowed);
                        }
                        for(int station=Mathf.CeilToInt(start/3f);station<=Mathf.FloorToInt(end/3f);station++)
                        {float f=(station*3f-distance)/length;if(f<0||f>=1)continue;var p=Vector3.Lerp(a,b,f);if(!Owned(p,key,chunkSize))continue;var s=Vector3.Lerp(sa,sb,f);var dir=new Vector3(-s.z,0,s.x).normalized;
                            for(int side=-1;side<=1;side+=2){var bp=p+s*(width*.5f+.25f)*side;barriers.Prism(bp-dir*.055f,bp+dir*.055f,s.normalized,s.normalized,.065f,-.45f,.67f);}
                            if(++work%32==0){foreach(var batch in batches)if(batch.Count>=BatchVertices)batch.Flush(root);yield return work;}
                        }
                        if(i==0&&lo==0&&Owned(a,key,chunkSize))Support(deck,a,sa,width+.8f,1.6f,ground,supportAllowed);
                        if(i==points.Length-2&&hi==1&&Owned(b,key,chunkSize))Support(deck,b,sb,width+.8f,1.6f,ground,supportAllowed);
                    }
                    distance+=length;
                    foreach(var batch in batches)if(batch.Count>=BatchVertices)batch.Flush(root);
                    if(++work%8==0)yield return work;
                }
            }
            foreach(var batch in batches){if(batch.Count==0)continue;batch.Flush(root);yield return ++work;}
        }
    }
}
