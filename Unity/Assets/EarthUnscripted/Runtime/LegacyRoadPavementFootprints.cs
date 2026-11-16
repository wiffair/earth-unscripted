using System;using System.Collections.Generic;using UnityEngine;
namespace EarthUnscripted
{
    // Snapshot of release17 RibbonSteps' XZ footprint, for admission comparison.
    // Output triangles (including32-sided caps) rather than convex-assuming quads.
    public static class LegacyRoadPavementFootprints
    {
        public static IEnumerable<RoadBankExteriorClip.Footprint> BuildStrips(GeoRegion region)=>Build(region,false);
        public static IEnumerable<RoadBankExteriorClip.Footprint> Build(GeoRegion region,bool includeCaps=true)
        {
            var caps=new HashSet<string>();foreach(var road in region.roads)
            {
                if(road.tunnel)continue;bool deck=region.IsEffectiveBridgeDeck(road);var p=deck?region.CopyEffectiveBridgePoints(road):road.points;float half=road.width*.5f;
                RoadBankExteriorClip.Footprint F(Vector3 a,Vector3 b,Vector3 c)=>new RoadBankExteriorClip.Footprint{Id=road.id,Layer=road.layer,Bridge=deck,Polygon=new[]{new Vector2(a.x,a.z),new Vector2(b.x,b.z),new Vector2(c.x,c.z)}};
                for(int i=0;i<p.Length-1;i++)
                {
                    var direction=(p[i+1]-p[i]).normalized;var before=i>0?(p[i]-p[i-1]).normalized:direction;var after=i+2<p.Length?(p[i+2]-p[i+1]).normalized:direction;
                    var r0=Vector3.Cross(Vector3.up,(before+direction).normalized).normalized;var r1=Vector3.Cross(Vector3.up,(direction+after).normalized).normalized;var right=Vector3.Cross(Vector3.up,direction).normalized;r0/=Mathf.Max(.6f,Vector3.Dot(r0,right));r1/=Mathf.Max(.6f,Vector3.Dot(r1,right));if(Vector3.Angle(before,direction)>60)r0=right;if(Vector3.Angle(direction,after)>60)r1=right;
                    int steps=Math.Max(1,(int)Math.Ceiling(Vector3.Distance(p[i],p[i+1])/2.5));for(int j=0;j<steps;j++){float u=j/(float)steps,v=(j+1)/(float)steps;var a=Vector3.Lerp(p[i],p[i+1],u);var b=Vector3.Lerp(p[i],p[i+1],v);var ra=Vector3.Lerp(r0,r1,u)*half;var rb=Vector3.Lerp(r0,r1,v)*half;yield return F(a-ra,b-rb,a+ra);yield return F(a+ra,b-rb,b+rb);}
                }
                if(deck||!includeCaps)continue;
                for(int i=0;i<p.Length;i++)
                {
                    bool endpoint=i==0||i==p.Length-1;bool sharp=!endpoint&&Vector3.Angle(p[i]-p[i-1],p[i+1]-p[i])>60;if(!endpoint&&!sharp)continue;string id=i==0?road.start:i==p.Length-1?road.end:road.id+"/interior/"+i;
                    if(!caps.Add(id))continue;float radius=endpoint?region.JunctionRadius(id,half):half;for(int n=0;n<32;n++){double a=n*Math.PI/16,b=(n+1)*Math.PI/16;yield return F(p[i],p[i]+new Vector3((float)Math.Cos(a)*radius,0,(float)Math.Sin(a)*radius),p[i]+new Vector3((float)Math.Cos(b)*radius,0,(float)Math.Sin(b)*radius));}
                }
            }
        }
    }
}
