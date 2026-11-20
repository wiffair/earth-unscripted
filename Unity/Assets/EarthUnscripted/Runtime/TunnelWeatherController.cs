using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using NativeWorld=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted
{
    public sealed class TunnelWeatherController:MonoBehaviour
    {
        public GameSession Session;
        public TunnelWeatherIndex Index {get;private set;}
        public int PublishedAtlases {get;private set;}
        public bool Busy=>building!=null||painting;
        const int Size=512;const float Span=2048;
        GeoRegion owner,requestedOwner;int revision=-1,requestedRevision;
        Task<TunnelWeatherIndex> building;bool painting;float retryAt;Color[][] rasterLayers;
        Texture2DArray atlas;Vector2 centre=new Vector2(float.MaxValue,float.MaxValue);
        void Update()
        {
            if(!Session||Session.World?.Route?.Region==null)return;
            var region=Session.World.Route.Region;int version=Session.World.AutomaticWorld?Session.World.AutomaticWorld.QueryRevision:0;
            if(building!=null&&building.IsCompleted)
            {
                if(building.IsFaulted){retryAt=Time.unscaledTime+10;Debug.LogWarning("Tunnel weather index unavailable: "+building.Exception.GetBaseException().Message);}
                else if(requestedOwner==region){Index=building.Result;owner=region;revision=requestedRevision;centre=new Vector2(float.MaxValue,float.MaxValue);}
                building=null;
            }
            if(building==null&&Time.unscaledTime>=retryAt&&(owner!=region||revision!=version))
            {
                requestedOwner=region;requestedRevision=version;
                var maps=Session.World.AutomaticWorld?Session.World.AutomaticWorld.Renderer.ResidentAreas.ToArray():null;var roads=region.roads;
                building=Task.Run(()=>new TunnelWeatherIndex(Sources(maps,roads)));
            }
            if(Index==null||!Session.Car)return;
            var p=Session.Car.Body.position;var point=new Vector2(p.x,p.z);
            if(Index.SegmentCount==0){Shader.SetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled",0);return;}
            if(!painting&&(point-centre).sqrMagnitude>128*128)StartCoroutine(Paint(point,p.y,Index));
        }
        IEnumerator Paint(Vector2 point,float height,TunnelWeatherIndex index)
        {
            painting=true;var minimum=new Vector2(Mathf.Floor(point.x/128)*128-Span*.5f,Mathf.Floor(point.y/128)*128-Span*.5f);
            if(!index.Intersects(minimum,Span)){centre=point;Shader.SetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled",0);painting=false;yield break;}
            var reusable=rasterLayers;var work=Task.Run(()=>index.Raster(minimum,Span,Size,height,reusable));while(!work.IsCompleted)yield return null;
            if(work.IsFaulted){Debug.LogWarning("Tunnel weather atlas unavailable: "+work.Exception.GetBaseException().Message);painting=false;yield break;}
            rasterLayers=work.Result;
            if(index!=Index){painting=false;yield break;}
            var next=new Texture2DArray(Size,Size,4,TextureFormat.RGBAFloat,false,true){name="Local layered tunnel weather",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            for(int i=0;i<4;i++){next.SetPixels(work.Result[i],i);yield return null;}
            if(index!=Index){Destroy(next);painting=false;yield break;}
            using(WorkTelemetry.Measure("weather.tunnel-atlas-publish",null,8))next.Apply(false,true);var previous=atlas;atlas=next;
            Shader.SetGlobalTexture("_EarthUnscriptedTunnelMask",atlas);Shader.SetGlobalVector("_EarthUnscriptedTunnelMaskRect",new Vector4(minimum.x,minimum.y,Span,Span));Shader.SetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled",1);
            centre=point;PublishedAtlases++;if(previous)Destroy(previous);painting=false;
        }
        public static IEnumerable<TunnelWeatherIndex.Source> Sources(NativeWorld.Map[] maps,GeoRoad[] roads)
        {
            if(maps!=null)
            {
                foreach(var map in maps)foreach(var f in map.features)
                {
                    if(f.category!="road"&&f.category!="rail"&&f.category!="path")continue;
                    for(int i=1;i<f.points.Length;i++)
                    {
                        var a=f.points[i-1];var b=f.points[i];
                        if(!Clip(ref a,ref b,map.coreMinX,map.coreMinZ,map.coreMaxX,map.coreMaxZ))continue;
                        yield return new TunnelWeatherIndex.Source{a=a,b=b,width=f.width,clearance=f.tunnelClearance>0?f.tunnelClearance:f.category=="rail"?6:5,tunnel=f.tunnel,kind=f.category,aId=(a-f.points[i-1]).sqrMagnitude<.000001f&&f.sourceNodeIds!=null&&i-1<f.sourceNodeIds.Length?f.sourceNodeIds[i-1]:null,bId=(b-f.points[i]).sqrMagnitude<.000001f&&f.sourceNodeIds!=null&&i<f.sourceNodeIds.Length?f.sourceNodeIds[i]:null};
                    }
                }
            }
            else foreach(var road in roads??Array.Empty<GeoRoad>())for(int i=1;i<road.points.Length;i++)
                yield return new TunnelWeatherIndex.Source{a=road.points[i-1],b=road.points[i],width=road.width,clearance=road.maxVehicleHeight>0?road.maxVehicleHeight+.3f:5,tunnel=road.tunnel,kind="road"};
        }
        static bool Clip(ref Vector3 a,ref Vector3 b,float minX,float minZ,float maxX,float maxZ)
        {
            float lo=0,hi=1;var d=b-a;
            bool Side(float p,float q){if(Mathf.Abs(p)<1e-8f)return q>=0;float r=q/p;if(p<0){if(r>hi)return false;lo=Mathf.Max(lo,r);}else{if(r<lo)return false;hi=Mathf.Min(hi,r);}return true;}
            if(!Side(-d.x,a.x-minX)||!Side(d.x,maxX-a.x)||!Side(-d.z,a.z-minZ)||!Side(d.z,maxZ-a.z)||hi-lo<1e-7f)return false;
            var start=a;a=start+d*lo;b=start+d*hi;return true;
        }
        void OnDestroy(){Shader.SetGlobalFloat("_EarthUnscriptedTunnelMaskEnabled",0);if(atlas)Destroy(atlas);}
    }
}
