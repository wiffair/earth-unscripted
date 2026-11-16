using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    // A lightweight labelled map is independent of terrain/physics preparation.
    // Only visible tiles are requested; revisits reuse a seven-day disk cache.
    sealed class NavigationAtlas : IDisposable
    {
        const double MetresPerDegree=111319.4908;
        readonly HttpClient http=new HttpClient{Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=4*1024*1024};
        sealed class Tile {public Texture2D image;public Task<byte[]> loading;public double retry;public long used;}
        readonly Dictionary<string,Tile> tiles=new Dictionary<string,Tile>();
        readonly List<(string key,int x,int y,int zoom,Rect rect)> visible=new List<(string,int,int,int,Rect)>();
        long age;bool disposed;readonly string cache;
        public NavigationAtlas()
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RoadRushBeta/0.23.0-beta.8 (Blackwood-Games-personal-driving-beta)");
            cache=Path.Combine(RoadRush.SinglePlayerBeta.ProductRoot,"NavigationTiles-v1");
        }
        static double Latitude(GeoRegion r,double z)=>r.latitude+z/MetresPerDegree;
        static double Longitude(GeoRegion r,double x)=>r.longitude+x/(MetresPerDegree*Math.Cos(r.latitude*Math.PI/180));
        static double TileY(double latitude,int count){double a=Math.Max(-85,Math.Min(85,latitude))*Math.PI/180;return (1-Math.Log(Math.Tan(a)+1/Math.Cos(a))/Math.PI)*.5*count;}
        static double TileLatitude(int y,int count)=>Math.Atan(Math.Sinh(Math.PI*(1-2.0*y/count)))*180/Math.PI;
        public void Window(GeoRegion r,Vector2 centre,float span,Rect local)
        {
            visible.Clear();double cosine=Math.Cos(r.latitude*Math.PI/180);
            int zoom=Mathf.Clamp(Mathf.RoundToInt((float)Math.Log(156543.03392*cosine*local.width/span,2)),3,18),count=1<<zoom;
            double halfHeight=span*.5*local.height/local.width;
            int left=(int)Math.Floor((Longitude(r,centre.x-span*.5)+180)/360*count),right=(int)Math.Floor((Longitude(r,centre.x+span*.5)+180)/360*count);
            int top=Math.Max(0,(int)Math.Floor(TileY(Latitude(r,centre.y+halfHeight),count))),bottom=Math.Min(count-1,(int)Math.Floor(TileY(Latitude(r,centre.y-halfHeight),count)));
            for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
            {
                int wrapped=(x%count+count)%count;string key=zoom+"_"+wrapped+"_"+y;
                float west=(float)(((double)x/count*360-180-r.longitude)*MetresPerDegree*cosine),east=(float)(((double)(x+1)/count*360-180-r.longitude)*MetresPerDegree*cosine);
                float north=(float)((TileLatitude(y,count)-r.latitude)*MetresPerDegree),south=(float)((TileLatitude(y+1,count)-r.latitude)*MetresPerDegree);
                var rect=new Rect(local.width*.5f+(west-centre.x)*local.width/span,local.height*.5f-(north-centre.y)*local.width/span,(east-west)*local.width/span,(north-south)*local.width/span);
                visible.Add((key,wrapped,y,zoom,rect));
                if(!tiles.TryGetValue(key,out var tile))tiles.Add(key,tile=new Tile());tile.used=++age;
            }
        }
        async Task<byte[]> Fetch(string key,int zoom,int x,int y)
        {
            string file=Path.Combine(cache,key+".png");
            if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7))return await Task.Run(()=>File.ReadAllBytes(file));
            using(var response=await http.GetAsync($"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png"))
            {
                response.EnsureSuccessStatusCode();var bytes=await response.Content.ReadAsByteArrayAsync();
                if(bytes.Length>1024*1024)throw new IOException("Map tile exceeds byte limit");
                // Honour a longer provider max-age; seven days is the minimum.
                var age=response.Headers.CacheControl?.MaxAge??TimeSpan.FromDays(7);
                await Task.Run(()=>{Directory.CreateDirectory(cache);File.WriteAllBytes(file,bytes);if(age>TimeSpan.FromDays(7))File.SetLastWriteTimeUtc(file,DateTime.UtcNow+age-TimeSpan.FromDays(7));});
                return bytes;
            }
        }
        public void Update()
        {
            if(disposed)return;
            foreach(var t in tiles.Values)
                if(t.loading!=null&&t.loading.IsCompleted)
                {
                    if(!t.loading.IsFaulted&&!t.loading.IsCanceled){var image=new Texture2D(2,2,TextureFormat.RGB24,false);if(image.LoadImage(t.loading.Result)){image.wrapMode=TextureWrapMode.Clamp;t.image=image;}else UnityEngine.Object.Destroy(image);}
                    t.loading=null;t.retry=Time.realtimeSinceStartupAsDouble+30;
                }
            int running=tiles.Values.Count(t=>t.loading!=null);
            foreach(var item in visible)
            {
                var tile=tiles[item.key];if(running>=2)break;
                if(!tile.image&&tile.loading==null&&Time.realtimeSinceStartupAsDouble>=tile.retry){tile.loading=Fetch(item.key,item.zoom,item.x,item.y);running++;}
            }
            if(tiles.Count>64)
                foreach(var key in tiles.Where(p=>p.Value.loading==null&&!visible.Any(v=>v.key==p.Key)).OrderBy(p=>p.Value.used).Select(p=>p.Key).Take(tiles.Count-64).ToArray())
                {if(tiles[key].image)UnityEngine.Object.Destroy(tiles[key].image);tiles.Remove(key);}
        }
        public void Draw(){foreach(var item in visible)if(tiles.TryGetValue(item.key,out var t)&&t.image)GUI.DrawTexture(item.rect,t.image);}
        public async Task<NavigationRoute> Plan(GeoRegion region,Vector3 start,Vector3 end)
        {
            string F(double n)=>n.ToString("F7",CultureInfo.InvariantCulture);
            string pair(Vector3 p)=>F(Longitude(region,p.x))+","+F(Latitude(region,p.z));
            string url="https://router.project-osrm.org/route/v1/driving/"+pair(start)+";"+pair(end)+"?overview=full&geometries=geojson&steps=false";
            var data=JObject.Parse(await http.GetStringAsync(url));
            if((string)data["code"]!="Ok")throw new IOException("No driving route found. Try a nearby road.");
            var coords=data["routes"]?[0]?["geometry"]?["coordinates"] as JArray;
            if(coords==null||coords.Count<2||coords.Count>50000)throw new IOException("Route unavailable or too long. Try a closer destination.");
            double cosine=Math.Cos(region.latitude*Math.PI/180);var points=new Vector3[coords.Count];
            for(int i=0;i<points.Length;i++)points[i]=new Vector3((float)((coords[i][0].Value<double>()-region.longitude)*MetresPerDegree*cosine),start.y,(float)((coords[i][1].Value<double>()-region.latitude)*MetresPerDegree));
            return new NavigationRoute(points);
        }
        public void Dispose(){disposed=true;http.Dispose();foreach(var t in tiles.Values)if(t.image)UnityEngine.Object.Destroy(t.image);tiles.Clear();}
    }
}
