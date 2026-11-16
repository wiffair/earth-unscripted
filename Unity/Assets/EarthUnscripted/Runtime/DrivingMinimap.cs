using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Heading-up local map: the car stays centred while the map pans and
    /// rotates. Immutable road arrays are rasterized off-thread; texture
    /// creation/upload stays on the main thread. All distances are metres.
    /// </summary>
    sealed class DrivingMinimap : IDisposable
    {
        const int Pixels = 512;
        const float TextureSpan = 2048, ViewSpan = 600, RecenterDistance = 240;
        Texture2D texture;
        RenderTexture display;Material material;int renderedFrame=-1;
        Task<Color32[]> preparation;
        Color32[] pixelBuffer;
        Vector2 centre, pendingCentre;
        GeoRoad[] indexedRoads, pendingRoads;
        double nextRefresh;

        public bool Ready => texture && display;
        public void Warm(GameSession session) => Draw(new Rect(0,0,0,0),session);

        public void Draw(Rect bounds, GameSession session)
        {
            var position = session.Car.Body.position;
            var region = session.World.Route.Region;
            if (preparation != null && preparation.IsCompleted)
            {
                if (!preparation.IsFaulted)
                {
                    if (!texture) texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false)
                    { name = "Moving local driving map", wrapMode = TextureWrapMode.Clamp };
                    texture.SetPixels32(preparation.Result); texture.Apply(false);
                    centre = pendingCentre; indexedRoads = pendingRoads;
                }
                preparation = null;
            }
            var location = new Vector2(position.x, position.z);
            if (preparation == null && Time.realtimeSinceStartupAsDouble > nextRefresh &&
                (!texture || indexedRoads != region.roads || (location - centre).sqrMagnitude > RecenterDistance * RecenterDistance))
            {
                nextRefresh = Time.realtimeSinceStartupAsDouble + .5;
                pendingCentre = location; pendingRoads = region.roads;
                var roads = pendingRoads; var waters = region.waters; var mapCentre = location;
                // A single raster job owns this buffer until completion and texture upload.
                var pixels = pixelBuffer ?? (pixelBuffer = new Color32[Pixels * Pixels]);
                preparation = Task.Run(() => Rasterize(roads, waters, mapCentre, pixels));
            }
            if(texture && Event.current.type==EventType.Repaint && renderedFrame!=Time.frameCount)
            {
                if(!material)material=new Material(Resources.Load<Shader>("AutomaticWorld/DrivingMap"));
                if(!display){display=new RenderTexture(256,256,0){name="Heading-up driving map"};display.Create();}
                float heading=session.Car.transform.eulerAngles.y*Mathf.Deg2Rad;
                material.SetVector("_CarUv",new Vector4(.5f+(position.x-centre.x)/TextureSpan,.5f+(position.z-centre.y)/TextureSpan,0,0));
                material.SetVector("_MapTransform",new Vector4(Mathf.Cos(heading),Mathf.Sin(heading),ViewSpan/TextureSpan,0));
                var previous=RenderTexture.active;Graphics.Blit(texture,display,material);RenderTexture.active=previous;
                renderedFrame=Time.frameCount;
            }
            if(bounds.width<=0)return;
            if(display)GUI.DrawTexture(bounds,display);
            var colour=GUI.color;GUI.color=InterfaceTheme.Gold;
            GUI.DrawTexture(new Rect(bounds.center.x-2,bounds.center.y-5,4,10),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bounds.center.x-4,bounds.center.y-2,8,3),Texture2D.whiteTexture);
            GUI.color=colour;
        }

        static Color32[] Rasterize(GeoRoad[] roads, GeoWater[] waters, Vector2 centre, Color32[] pixels)
        {
            if (pixels == null || pixels.Length != Pixels * Pixels) throw new ArgumentException("Invalid minimap buffer");
            for (int index = 0; index < pixels.Length; index++) pixels[index] = new Color32(12, 25, 37, 255);
            float minX = centre.x - TextureSpan * .5f, minZ = centre.y - TextureSpan * .5f;
            void Line(Vector3 first, Vector3 last, Color32 colour)
            {
                float ax = (first.x - minX) / TextureSpan * (Pixels - 1), ay = (first.z - minZ) / TextureSpan * (Pixels - 1);
                float dx = (last.x - first.x) / TextureSpan * (Pixels - 1), dy = (last.z - first.z) / TextureSpan * (Pixels - 1);
                float low = 0, high = 1;
                bool Clip(float direction, float distance)
                {
                    if (Mathf.Abs(direction) < .000001f) return distance >= 0;
                    float fraction = distance / direction;
                    if (direction < 0) { if (fraction > high) return false; low = Mathf.Max(low, fraction); }
                    else { if (fraction < low) return false; high = Mathf.Min(high, fraction); }
                    return true;
                }
                if (!Clip(-dx, ax) || !Clip(dx, Pixels - 1 - ax) || !Clip(-dy, ay) || !Clip(dy, Pixels - 1 - ay)) return;
                int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * (high - low));
                for (int step = 0; step <= steps; step++)
                {
                    float fraction = Mathf.Lerp(low, high, steps == 0 ? 0 : (float)step / steps);
                    int x = Mathf.Clamp(Mathf.RoundToInt(ax + dx * fraction), 0, Pixels - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(ay + dy * fraction), 0, Pixels - 1);
                    pixels[y * Pixels + x] = colour;
                }
            }
            foreach (var water in waters ?? Array.Empty<GeoWater>())
                for (int index = 1; index < water.points.Length; index++)
                    Line(water.points[index - 1], water.points[index], new Color32(70, 150, 190, 255));
            foreach (var road in roads ?? Array.Empty<GeoRoad>())
                for (int index = 1; index < road.points.Length; index++)
                    Line(road.points[index - 1], road.points[index], new Color32(210, 220, 200, 255));
            return pixels;
        }
        public void Dispose()
        {
            if(texture)UnityEngine.Object.Destroy(texture);
            if(material)UnityEngine.Object.Destroy(material);
            if(display){display.Release();UnityEngine.Object.Destroy(display);}
        }
    }
}
