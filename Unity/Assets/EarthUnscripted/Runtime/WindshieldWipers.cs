using System;
using System.Collections.Generic;
using UnityEngine;
namespace EarthUnscripted
{
    public static class WindshieldWipers
    {
        public readonly struct Surface
        {
            public readonly Vector3 origin,right,up;public readonly float topWidth;
            public Surface(Vector3 bottomLeft,Vector3 across,Vector3 rising,float upperWidth=0){origin=bottomLeft;right=across;up=rising;topWidth=upperWidth>0?upperWidth:across.magnitude;}
            public float Width=>right.magnitude;public float Height=>up.magnitude;
            public Vector3 Normal=>Vector3.Cross(right,up).normalized;
            public Vector3 Point(float u,float v)=>origin+right*.5f+right.normalized*((u-.5f)*Mathf.Lerp(Width,topWidth,v))+up*v;
            public Vector2 Metres(Vector3 point)=>new Vector2(Vector3.Dot(point-origin,right.normalized),Vector3.Dot(point-origin,up.normalized));
        }
        public static bool TrySurface(CarVisual visual,out Surface surface)=>TrySurface(visual,out surface,out _);
        public static bool TrySurface(CarVisual visual,out Surface surface,out Vector3[] triangles)
        {
            surface=default;triangles=Array.Empty<Vector3>();var points=new List<Vector3>();var owner=visual.transform;var eye=visual.CockpitEye;
            var filters=visual.GetComponentsInChildren<MeshFilter>(true);bool hasNamedScreen=false;
            foreach(var item in filters){string label=item.name.ToLowerInvariant();if(item.gameObject.activeInHierarchy&&(label=="windscreen"||label=="bodywindshield"))hasNamedScreen=true;}
            foreach(var filter in filters)
            {
                if(!filter.sharedMesh||!filter.gameObject.activeInHierarchy)continue;
                string name=filter.name.ToLowerInvariant();if(name.Contains("gasket")||name.Contains("wiper")||name.Contains("seal")||name.Contains("frame")||name.Contains("surround")||name.Contains("trim"))continue;bool explicitScreen=name.Contains("windscreen")||name.Contains("windshield");
                if(!explicitScreen&&(!name.Contains("glass")||hasNamedScreen))continue;
                var vertices=filter.sharedMesh.vertices;var indices=filter.sharedMesh.triangles;
                for(int i=0;i+2<indices.Length;i+=3)
                {
                    var a=owner.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i]]));
                    var b=owner.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i+1]]));
                    var c=owner.InverseTransformPoint(filter.transform.TransformPoint(vertices[indices[i+2]]));
                    if((a.z+b.z+c.z)/3<eye.z+.12f)continue;
                    if(!explicitScreen&&(Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.z)<.45f||Mathf.Max(a.y,b.y,c.y)<eye.y-.45f))continue;
                    points.Add(a);points.Add(b);points.Add(c);
                }
            }
            if(points.Count<3)return false;
            float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            foreach(var point in points){minX=Mathf.Min(minX,point.x);maxX=Mathf.Max(maxX,point.x);minY=Mathf.Min(minY,point.y);maxY=Mathf.Max(maxY,point.y);}
            // Interpret the camper's split panes on their shared symmetric plane,
            // preserving the complete span when glass is supplied in pieces.
            if(VehiclePackLibrary.ForModel(visual.Model)?.Settings.Id=="earthunscripted.vw-splittie")
            {
                // Mirror the explicitly modelled passenger pane, preserving its
                // centre seal/aperture gap rather than spanning it with rain.
                int sourceCount=points.Count;for(int i=0;i<sourceCount;i++){var mirrored=points[i];mirrored.x=-mirrored.x;points.Add(mirrored);}
                float half=Mathf.Max(Mathf.Abs(minX),Mathf.Abs(maxX));minX=-half;maxX=half;
            }
            float bottomZ=0,topZ=0,topMinX=float.PositiveInfinity,topMaxX=float.NegativeInfinity;int bottomCount=0,topCount=0;
            foreach(var point in points){if(point.y<=minY+.035f){bottomZ+=point.z;bottomCount++;}if(point.y>=maxY-.035f){topZ+=point.z;topCount++;topMinX=Mathf.Min(topMinX,point.x);topMaxX=Mathf.Max(topMaxX,point.x);}}
            if(bottomCount==0||topCount==0||maxX-minX<.35f||maxY-minY<.15f)return false;
            if(VehiclePackLibrary.ForModel(visual.Model)?.Settings.Id=="earthunscripted.vw-splittie"){float half=Mathf.Max(Mathf.Abs(topMinX),Mathf.Abs(topMaxX));topMinX=-half;topMaxX=half;}
            surface=new Surface(new Vector3(minX,minY,bottomZ/bottomCount),Vector3.right*(maxX-minX),new Vector3(0,maxY-minY,topZ/topCount-bottomZ/bottomCount),Mathf.Min(maxX-minX,topMaxX-topMinX));triangles=points.ToArray();return true;
        }
        public sealed class PaneMask
        {
            const int Resolution=16;
            readonly List<(Vector2 a,Vector2 b,Vector2 c)>[] cells=new List<(Vector2,Vector2,Vector2)>[Resolution*Resolution];
            readonly float width,height;
            public PaneMask(Surface surface,Vector3[] triangles)
            {
                width=surface.Width;height=surface.Height;
                for(int i=0;i+2<triangles.Length;i+=3)
                {
                    var a=surface.Metres(triangles[i]);var b=surface.Metres(triangles[i+1]);var c=surface.Metres(triangles[i+2]);
                    int x0=Bin(Mathf.Min(a.x,b.x,c.x),width),x1=Bin(Mathf.Max(a.x,b.x,c.x),width),y0=Bin(Mathf.Min(a.y,b.y,c.y),height),y1=Bin(Mathf.Max(a.y,b.y,c.y),height);
                    for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){int index=y*Resolution+x;if(cells[index]==null)cells[index]=new List<(Vector2,Vector2,Vector2)>();cells[index].Add((a,b,c));}
                }
            }
            static int Bin(float value,float span)=>Mathf.Clamp(Mathf.FloorToInt(value/Mathf.Max(.0001f,span)*Resolution),0,Resolution-1);
            bool ContainsPoint(Vector2 point)
            {
                if(point.x<0||point.y<0||point.x>width||point.y>height)return false;
                var candidates=cells[Bin(point.y,height)*Resolution+Bin(point.x,width)];if(candidates==null)return false;
                foreach(var t in candidates)if(InTriangle(point,t.a,t.b,t.c))return true;return false;
            }
            public bool Contains(Vector2 point,float radius=0)
                =>ContainsPoint(point)&&ContainsPoint(point+new Vector2(-radius,-radius*1.5f))&&ContainsPoint(point+new Vector2(radius,-radius*1.5f))&&
                  ContainsPoint(point+new Vector2(radius,radius*1.5f))&&ContainsPoint(point+new Vector2(-radius,radius*1.5f));
        }
        public static float SweepAngle(int index,float sweep)=>Mathf.Lerp(index%2==0?-76:76,index%2==0?30:-30,sweep);
        public static float FittedSweepAngle(Transform wiper,int index,float sweep)
        {var travel=wiper?wiper.GetComponent<WindshieldWiperTravel>():null;return travel?Mathf.Lerp(travel.ParkAngle,travel.EndAngle,sweep):SweepAngle(index,sweep);}
        public static void SetSweep(Transform[] wipers,float sweep)
        {if(wipers==null)return;for(int i=0;i<wipers.Length;i++)if(wipers[i])wipers[i].localRotation=Quaternion.Euler(0,0,FittedSweepAngle(wipers[i],i,sweep));}
        public static bool SplitPaneBounds(Surface surface,Vector3[] triangles,out Vector2 left,out Vector2 right)
        {
            left=new Vector2(float.PositiveInfinity,float.NegativeInfinity);right=left;float middle=surface.Width*.5f;
            var mask=new PaneMask(surface,triangles);
            if(mask.Contains(new Vector2(middle,surface.Height*.5f))||!mask.Contains(new Vector2(surface.Width*.25f,surface.Height*.5f))||!mask.Contains(new Vector2(surface.Width*.75f,surface.Height*.5f)))return false;
            for(int i=0;i+2<triangles.Length;i+=3)
            {
                float a=surface.Metres(triangles[i]).x,b=surface.Metres(triangles[i+1]).x,c=surface.Metres(triangles[i+2]).x;
                float min=Mathf.Min(a,b,c),max=Mathf.Max(a,b,c);
                if(max<=middle){left.x=Mathf.Min(left.x,min);left.y=Mathf.Max(left.y,max);}
                else if(min>=middle){right.x=Mathf.Min(right.x,min);right.y=Mathf.Max(right.y,max);}
            }
            return left.y>left.x&&right.y>right.x&&right.x-left.y>.015f;
        }
        static void TaperedStrip(Transform strip)
        {
            if(!strip)return;var filter=strip.GetComponent<MeshFilter>();if(!filter)return;
            var owner=strip.GetComponent<GeneratedMeshOwner>()??strip.gameObject.AddComponent<GeneratedMeshOwner>();owner.Release();
            // Three six-sided sections retain the blade's exact end stations.
            // The narrow nose avoids the old22mm cube becoming a black plank
            // when a shallow windscreen carries its tip close to the driver's eye.
            var cross=new[]{new Vector2(-.5f,0),new Vector2(-.30f,.5f),new Vector2(.30f,.5f),new Vector2(.5f,0),new Vector2(.30f,-.5f),new Vector2(-.30f,-.5f)};
            var vertices=new Vector3[18];var indices=new List<int>(96);
            for(int row=0;row<3;row++)
            {
                float y=row==0?-.5f:row==1?.3f:.5f,width=row==0?1:row==1?.65f:.35f,depth=row==0?1:row==1?.65f:.4f;
                for(int i=0;i<6;i++)vertices[row*6+i]=new Vector3(cross[i].x*width,y,cross[i].y*depth);
            }
            for(int row=0;row<2;row++)for(int i=0;i<6;i++)
            {int a=row*6+i,b=row*6+(i+1)%6,c=b+6,d=a+6;indices.Add(a);indices.Add(b);indices.Add(c);indices.Add(a);indices.Add(c);indices.Add(d);}
            for(int i=1;i<5;i++){indices.Add(0);indices.Add(i+1);indices.Add(i);indices.Add(12);indices.Add(12+i);indices.Add(12+i+1);}
            var mesh=new Mesh{name="Owned tapered wiper strip",vertices=vertices,triangles=indices.ToArray()};mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;owner.Mesh=mesh;
        }
        public static Transform[] Fit(CarVisual visual,Transform[] wipers)
        {
            if(!TrySurface(visual,out var surface,out var glassTriangles))return wipers??Array.Empty<Transform>();
            bool split=SplitPaneBounds(surface,glassTriangles,out var leftPane,out var rightPane);
            using var appearanceWrite=VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(visual.transform);
            if(wipers==null||wipers.Length!=2)
            {
                wipers=new Transform[2];var rubber=Geometry.Material("Rain wiper rubber",new Color(.018f,.021f,.025f));
                for(int i=0;i<2;i++)
                {
                    var mount=new GameObject("Rain-sensitive wiper mount").transform;mount.SetParent(visual.transform,false);
                    var pivot=new GameObject("Wiper sweep").transform;pivot.SetParent(mount,false);wipers[i]=pivot;
                    Geometry.Box("Wiper arm",pivot,Vector3.zero,Vector3.one,rubber);Geometry.Box("Wiper blade",pivot,Vector3.zero,Vector3.one,rubber);
                }
            }
            for(int i=0;i<2;i++)
            {
                float u=split?(i==0?.19f:.81f):(i==0?.28f:.72f);var mount=wipers[i].parent;
                mount.localPosition=surface.Point(u,.025f)+surface.Normal*.014f;mount.localRotation=Quaternion.LookRotation(surface.Normal,surface.up.normalized);mount.localScale=Vector3.one;
                var pane=split?(i==0?leftPane:rightPane):new Vector2(0,surface.Width);
                float left=u*surface.Width-pane.x-.025f,right=pane.y-u*surface.Width-.025f;
                float radius=surface.Height*.945f;
                float park=Mathf.Min(76,Mathf.Asin(Mathf.Clamp01((i==0?right:left)/radius))*Mathf.Rad2Deg);
                float end=Mathf.Min(30,Mathf.Asin(Mathf.Clamp01((i==0?left:right)/radius))*Mathf.Rad2Deg);
                var travel=wipers[i].GetComponent<WindshieldWiperTravel>()??wipers[i].gameObject.AddComponent<WindshieldWiperTravel>();travel.ParkAngle=i==0?-park:park;travel.EndAngle=i==0?end:-end;
                var arm=wipers[i].Find("Wiper arm");var blade=wipers[i].Find("Wiper blade");
                if(arm){TaperedStrip(arm);arm.localPosition=new Vector3(0,radius*.28f,.008f);arm.localScale=new Vector3(.006f,radius*.56f,.005f);}
                if(blade){TaperedStrip(blade);blade.localPosition=new Vector3(0,radius*.77f,.013f);blade.localScale=new Vector3(.010f,radius*.46f,.008f);}
            }
            SetSweep(wipers,0);CamperCockpit.ReplaceBakedWipers(visual,wipers);return wipers;
        }
        public static bool BladeSegment(Transform car,Surface surface,Transform wiper,out Vector2 a,out Vector2 b)
        {
            a=b=default;if(!wiper)return false;var blade=wiper.Find("Wiper blade");if(!blade)return false;
            a=surface.Metres(car.InverseTransformPoint(blade.TransformPoint(Vector3.down*.5f)));
            b=surface.Metres(car.InverseTransformPoint(blade.TransformPoint(Vector3.up*.5f)));return true;
        }
        static float DistanceSquared(Vector2 p,Vector2 a,Vector2 b)
        {var d=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.0000001f,d.sqrMagnitude));return (p-a-d*t).sqrMagnitude;}
        static bool InTriangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
        {
            float Cross(Vector2 u,Vector2 v)=>u.x*v.y-u.y*v.x;
            float ab=Cross(b-a,p-a),bc=Cross(c-b,p-b),ca=Cross(a-c,p-c);
            if(Mathf.Abs(Cross(b-a,c-a))<.0000001f)return false;
            return !(ab<0||bc<0||ca<0)||!(ab>0||bc>0||ca>0);
        }
        public static bool Swept(Vector2 point,Vector2 oldA,Vector2 oldB,Vector2 a,Vector2 b,float radius)
            =>DistanceSquared(point,a,b)<=radius*radius||DistanceSquared(point,oldA,oldB)<=radius*radius||InTriangle(point,oldA,oldB,b)||InTriangle(point,oldA,b,a);
    }
}
