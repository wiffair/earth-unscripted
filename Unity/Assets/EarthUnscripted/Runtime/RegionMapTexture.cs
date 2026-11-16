using UnityEngine;
namespace EarthUnscripted
{
    public static class RegionMapTexture
    {
        public static Texture2D Create(GeoRegion r)
        {
            const int n=256,maximumLineSteps=1000000;int lineSteps=0;bool budgetExceeded=false;
            var pixels=new Color[n*n];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(.06f,.09f,.08f,.7f);
            Vector2Int Point(Vector3 p)=>new Vector2Int(Mathf.RoundToInt((p.x-r.minX)/r.width*(n-1)),Mathf.RoundToInt((p.z-r.minZ)/r.depth*(n-1)));
            void Line(Vector3 start,Vector3 end,Color colour)
            {
                if(budgetExceeded)return;
                var a=Point(start);var b=Point(end);float dx=(float)b.x-a.x,dy=(float)b.y-a.y,lo=0,hi=1;
                // Clip before stepping: legitimate geometry beyond the map rectangle
                // must not create an unbounded offscreen pixel loop on the UI thread.
                bool Clip(float p,float q)
                {
                    if(Mathf.Abs(p)<.000001f)return q>=0;float t=q/p;
                    if(p<0){if(t>hi)return false;lo=Mathf.Max(lo,t);}else{if(t<lo)return false;hi=Mathf.Min(hi,t);}return true;
                }
                if(!Clip(-dx,a.x)||!Clip(dx,n-1-a.x)||!Clip(-dy,a.y)||!Clip(dy,n-1-a.y))return;
                b=new Vector2Int(Mathf.RoundToInt(a.x+dx*hi),Mathf.RoundToInt(a.y+dy*hi));
                a=new Vector2Int(Mathf.RoundToInt(a.x+dx*lo),Mathf.RoundToInt(a.y+dy*lo));
                int steps=Mathf.Max(Mathf.Abs(b.x-a.x),Mathf.Abs(b.y-a.y));
                if((lineSteps+=steps+1)>maximumLineSteps){budgetExceeded=true;return;}
                for(int j=0;j<=steps;j++){float t=steps==0?0:(float)j/steps;int x=Mathf.RoundToInt(Mathf.Lerp(a.x,b.x,t)),y=Mathf.RoundToInt(Mathf.Lerp(a.y,b.y,t));if(x>=0&&x<n&&y>=0&&y<n)pixels[y*n+x]=colour;}
            }
            foreach(var w in r.waters)for(int i=0;i<w.points.Length-1;i++)Line(w.points[i],w.points[i+1],new Color(.16f,.4f,.5f));
            foreach(var road in r.roads)if(!road.tunnel)for(int i=0;i<road.points.Length-1;i++)Line(road.points[i],road.points[i+1],new Color(.61f,.66f,.6f));
            if(budgetExceeded)return null;
            var texture=new Texture2D(n,n,TextureFormat.RGBA32,false){name="Offline road-network minimap"};texture.SetPixels(pixels);texture.Apply();return texture;
        }
    }
}
