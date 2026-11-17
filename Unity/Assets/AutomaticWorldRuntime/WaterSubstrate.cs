using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EarthUnscripted.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        Texture2D waterSubstrate;
        IEnumerator PaintWaterSubstrate()
        {
            const int pixels=1024;const float span=8192;
            var bounds=new Vector4(observer.x-span*.5f,observer.z-span*.5f,span,span);

            var triangles=new List<Vector3>();
            foreach(var polygon in coastalPatch?.sea??Array.Empty<SeaPolygon>())
                foreach(var point in polygon.vertices??Array.Empty<Vector3>())triangles.Add(new Vector3(point.x,MappedSeaLevel,point.z));
            foreach(var area in areas.Values)foreach(var feature in area.features??Array.Empty<Feature>())
            {
                if(feature.category!="water"||feature.tunnel)continue;
                for(int index=1;index<feature.points.Length;index++)
                {
                    var a=feature.points[index-1];var b=feature.points[index];var side=Side(a,b)*feature.width*.5f;
                    triangles.AddRange(new[]{a-side,b-side,b+side,a-side,b+side,a+side});
                }
            }
            var raster=System.Threading.Tasks.Task.Run(()=>{
                var colours=new float[pixels*pixels*2];
            for(int index=0;index+2<triangles.Count;index+=3)
            {
                Vector2 Pixel(Vector3 p)=>new Vector2((p.x-bounds.x)*pixels/span,(p.z-bounds.y)*pixels/span);
                var a=Pixel(triangles[index]);var b=Pixel(triangles[index+1]);var c=Pixel(triangles[index+2]);
                int start=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y,c.y))),end=Mathf.Min(pixels-1,Mathf.CeilToInt(Mathf.Max(a.y,b.y,c.y)));
                float height=(triangles[index].y+triangles[index+1].y+triangles[index+2].y)/3;
                for(int y=start;y<=end;y++)
                {
                    float left=float.PositiveInfinity,right=float.NegativeInfinity;
                    void Edge(Vector2 p,Vector2 q){if((p.y>y+.5f)==(q.y>y+.5f))return;float x=p.x+(q.x-p.x)*(y+.5f-p.y)/(q.y-p.y);left=Mathf.Min(left,x);right=Mathf.Max(right,x);}
                    Edge(a,b);Edge(b,c);Edge(c,a);
                    if(!float.IsInfinity(left))for(int x=Mathf.Max(0,Mathf.FloorToInt(left));x<=Mathf.Min(pixels-1,Mathf.CeilToInt(right));x++){int at=(y*pixels+x)*2;colours[at]=height;colours[at+1]=1;}
                }
            }
                return colours;
            });
            while(!raster.IsCompleted)yield return null;
            var texture=new Texture2D(pixels,pixels,TextureFormat.RGFloat,false,true){name="Water height and channel substrate mask",wrapMode=TextureWrapMode.Clamp};
            texture.SetPixelData(raster.GetAwaiter().GetResult(),0);texture.Apply(false,true);
            if(waterSubstrate)Destroy(waterSubstrate);waterSubstrate=texture;
            grass.SetTexture("_WaterMask",texture);grass.SetVector("_WaterRect",bounds);grass.SetFloat("_WaterMaskEnabled",1);
        }
    }
}
