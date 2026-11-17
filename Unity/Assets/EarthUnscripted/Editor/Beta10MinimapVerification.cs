using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    // Main-thread texture lifecycle check; the next native drive measures GPU
    // upload/blit timings. This test does not infer hitch elimination.
    public static class Beta10MinimapVerification
    {
        public static void RunCLI(){try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static void Run()
        {
            int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException("Beta10 minimap: "+message);}
            var type=typeof(DrivingUI).Assembly.GetType("EarthUnscripted.DrivingMinimap");var map=Activator.CreateInstance(type,true);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var warm=type.GetMethod("EnsureResources",flags);var publish=type.GetMethod("PublishRaster",flags);
            Texture2D Current()=>(Texture2D)type.GetField("texture",flags).GetValue(map);
            Texture2D Pending()=>(Texture2D)type.GetField("uploadTexture",flags).GetValue(map);
            try
            {
                warm.Invoke(map,null);var first=Current();var second=Pending();
                Check(first&&second&&first!=second,"both texture slots are created before first map publication");
                Check(!(bool)type.GetProperty("Ready").GetValue(map),"blank warm textures do not claim a prepared map");
                var roads=Array.Empty<GeoRoad>();var pixels=new Color32[512*512];
                for(int step=0;step<4;step++)
                {
                    var active=Current();var inactive=Pending();var color=new Color32((byte)(30+step),80,120,255);
                    for(int i=0;i<pixels.Length;i++)pixels[i]=color;
                    var centre=new Vector2(step*240,-step*120);publish.Invoke(map,new object[]{pixels,centre,roads});
                    Check(Current()==inactive&&Pending()==active,"upload publishes the previously inactive texture");
                    Check(Current()==first||Current()==second,"map updates reuse the two original texture objects");
                    Check(((Color32)Current().GetPixel(256,256)).Equals(color),"published raster pixels are unchanged");
                    Check((Vector2)type.GetField("centre",flags).GetValue(map)==centre,"texture and geographic centre publish together");
                }
                Check((bool)type.GetProperty("Ready").GetValue(map),"raster publication enables the map");
            }
            finally{((IDisposable)map).Dispose();}
            Debug.Log("BETA10_MINIMAP_POLICY_PASS "+checks);
        }
    }
}
