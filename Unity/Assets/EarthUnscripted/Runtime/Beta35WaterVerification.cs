using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    public static class Beta35WaterVerification
    {
        public static IEnumerator Exercise(GameSession session,Action<bool,string> check)
        {
            var world=session.World.AutomaticWorld.Renderer;double timeout=Time.realtimeSinceStartupAsDouble+60;
            while(world.MappedWaterSurfaces==0&&Time.realtimeSinceStartupAsDouble<timeout)yield return null;
            var map=world.ResidentAreas.FirstOrDefault(m=>(m.waterAreas??Array.Empty<NewbridgePrototype.NewbridgeWorld.WaterArea>()).Any(w=>w.id.StartsWith("1493543074-",StringComparison.Ordinal)));
            check(map!=null,"Actual Cardiff Bay source footprint is present in the streamed area");
            var bay=map.waterAreas.First(w=>w.id.StartsWith("1493543074-",StringComparison.Ordinal));
            check(world.MappedWaterSurfaces>0&&bay.vertices.Length>=300,"Cardiff Bay has a published rendered water surface");
            Vector3 interior=Vector3.zero;float largest=0;
            for(int i=0;i+2<bay.vertices.Length;i+=3)
            {var a=bay.vertices[i];var b=bay.vertices[i+1];var c=bay.vertices[i+2];float size=Vector3.Cross(b-a,c-a).sqrMagnitude;if(size>largest){largest=size;interior=(a+b+c)/3;}}
            int gx=Mathf.Clamp(Mathf.RoundToInt((interior.x-map.minX)/map.gridStep),0,map.nx-1),gz=Mathf.Clamp(Mathf.RoundToInt((interior.z-map.minZ)/map.gridStep),0,map.nz-1);
            float bed=map.heights[gz*map.nx+gx];check(bed<bay.level-.05f,"The real Bay's terrain bed is below its water plane");
            check(world.TryWaterSurface(interior,out float level,out _)&&Mathf.Abs(level-bay.level)<.08f,"Vehicle water-depth queries include the mapped Bay");
            session.SetMenu(true);session.View.enabled=false;var camera=session.View.GetComponent<Camera>();
            foreach(var ui in UnityEngine.Object.FindObjectsByType<DrivingUI>())ui.enabled=false;
            foreach(var indicator in UnityEngine.Object.FindObjectsByType<BetaIndicator>())indicator.enabled=false;
            var bounds=new Bounds(bay.vertices[0],Vector3.zero);foreach(var p in bay.vertices)bounds.Encapsulate(p);
            var centre=bounds.center;camera.transform.position=centre+new Vector3(0,1400,-800);camera.transform.LookAt(centre);camera.fieldOfView=65;camera.farClipPlane=12000;
            session.Weather=0;session.Profile.fixedHour=12;session.Profile.clockMode=0;
            yield return new WaitForSecondsRealtime(2);yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();int waterPixels=0;
            try
            {
                foreach(var p in image.GetPixels32())if(p.b>p.r*1.15f&&p.g>p.r*1.1f&&p.g>12)waterPixels++;
                File.WriteAllBytes(Path.Combine(SinglePlayerBeta.VerificationOutput,"cardiff-bay-water.png"),image.EncodeToPNG());
                check(waterPixels>image.width*image.height*.015,"Actual Bay water is visibly rendered above its bed");
            }
            finally{UnityEngine.Object.Destroy(image);}
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"cardiff-bay-water.json"),new JObject{["sourceId"]=bay.id,["surfaceY"]=bay.level,["levelSource"]=bay.levelSource,["bedY"]=bed,["triangles"]=bay.vertices.Length/3,["visibleWaterPixels"]=waterPixels,["mappedWaterRoots"]=world.MappedWaterSurfaces,["width"]=bounds.size.x,["length"]=bounds.size.z}.ToString());
        }
    }
}
