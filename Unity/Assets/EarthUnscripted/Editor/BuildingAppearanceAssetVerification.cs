using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    // GPU readback verifies the real platform import without making source textures readable.
    public static class BuildingAppearanceAssetVerification
    {
        const string AssetRoot="Assets/EarthUnscripted/Resources/BuildingAppearance/";
        static int checks;static readonly List<string> results=new List<string>();
        static void Check(bool ok,string message){if(!ok)throw new Exception("BUILDING APPEARANCE FAIL: "+message);checks++;results.Add("PASS "+message);Debug.Log("BUILDING APPEARANCE PASS: "+message);}
        static string Hash(string path)=>EarthUnscripted.Core.DataPackageRules.HashFile(path);
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static Color32[] Source(string path)
        {
            var t=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try{if(!ImageConversion.LoadImage(t,File.ReadAllBytes(path),false))throw new Exception("Cannot decode source "+path);Check(t.width==1024&&t.height==1024,"source pixel dimensions "+Path.GetFileName(path));return t.GetPixels32();}
            finally{UnityEngine.Object.DestroyImmediate(t);}
        }
        static Color32[] Readback(Texture2D source,Material shader,bool normal)
        {
            var previous=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;
            var rt=RenderTexture.GetTemporary(1024,1024,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);rt.filterMode=FilterMode.Point;
            var read=new Texture2D(1024,1024,TextureFormat.RGBA32,false,true);
            try{shader.SetFloat("_DecodeNormal",normal?1:0);GL.sRGBWrite=false;Graphics.Blit(source,rt,shader);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,1024,1024),0,0,false);read.Apply(false,false);return read.GetPixels32();}
            finally{GL.sRGBWrite=oldSrgb;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(read);}
        }
        static Vector3 Normal(Color32 c)=>new Vector3(c.r/127.5f-1,c.g/127.5f-1,c.b/127.5f-1).normalized;
        static void Surface(Material material,string set)
        {
            Check(material&&material.shader&&material.shader.name=="Universal Render Pipeline/Lit","URP Lit material "+material.name);
            Check(material.mainTexture==Resources.Load<Texture2D>("BuildingAppearance/"+set+"Colour")&&material.GetTexture("_BumpMap")==Resources.Load<Texture2D>("BuildingAppearance/"+set+"Normal")&&material.GetTexture("_MetallicGlossMap")==Resources.Load<Texture2D>("BuildingAppearance/"+set+"MetalSmoothness"),"material binds shared "+set+" PBR maps");
            Check(material.IsKeywordEnabled("_NORMALMAP")&&material.IsKeywordEnabled("_METALLICSPECGLOSSMAP")&&!material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"),"normal and metallic-alpha smoothness shader variants "+set);
            Check(Mathf.Abs(material.GetFloat("_BumpScale")-.65f)<.001f&&Mathf.Abs(material.GetFloat("_Smoothness")-.75f)<.001f&&material.mainTextureScale==Vector2.one,"restrained PBR parameters and unscaled metric UVs "+set);
            Color tint=material.color;Check(Finite(tint.r)&&Finite(tint.g)&&Finite(tint.b)&&Mathf.Min(tint.r,Mathf.Min(tint.g,tint.b))>=0&&tint.maxColorComponent<=1,"finite bounded surface tint "+set);
        }
        public static void Run()
        {
            string output="Builds/Verification/building-appearance-assets.txt";Directory.CreateDirectory(Path.GetDirectoryName(output));Material readback=null;
            var preserved=Directory.GetFiles(AssetRoot).ToDictionary(p=>p,Hash);
            try
            {
                checks=0;results.Clear();var manifest=JObject.Parse(File.ReadAllText("Data/BuildingAppearance/manifest.json"));var review=JObject.Parse(File.ReadAllText("Data/BuildingAppearance/source-review.json"));
                Check((long)manifest["downloadedBytes"]<=12*1024*1024&&manifest["licence"].Value<string>()=="CC0-1.0","CC0 source bundle remains below12MiB");
                Check(Hash(Path.Combine("Data/BuildingAppearance",(string)manifest["archive"]["path"]))==(string)manifest["archive"]["sha256"],"reviewed local source archive SHA256");
                string notes=File.ReadAllText("Data/BuildingAppearance/README.md");Check(notes.Contains("no doors/windows")&&notes.Contains("texture review")&&notes.Contains("https://polyhaven.com/license"),"human primary-source and no-baked-opening review retained; not inferred from pixel test");
                var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/EarthUnscripted/Editor/BuildingAppearanceVerification.shader");Check(shader&&shader.isSupported,"GPU import-readback shader supported");readback=new Material(shader);
                var repeats=new Dictionary<string,float>{{"Brick",1.4f},{"Render",1},{"Stone",2.57f},{"Slate",3}};
                foreach(JObject material in manifest["materials"])
                {
                    string set=(string)material["name"];Check(repeats.ContainsKey(set)&&material["tileMetres"].All(x=>Mathf.Abs(x.Value<float>()-repeats[set])<.001f),"provider physical repeat retained "+set);
                    var source=(JObject)review["materials"].Single(x=>(string)x["name"]==set);
                    foreach(JObject map in source["sourceMaps"])Check(Hash(Path.Combine("Data/BuildingAppearance",(string)map["path"]))==(string)map["sha256"],"pinned provider source "+set+" "+(string)map["kind"]);
                    foreach(string kind in new[]{"Colour","Normal","MetalSmoothness"})
                    {
                        var record=material["maps"][kind];string path=AssetRoot+(string)record["file"];var texture=Resources.Load<Texture2D>("BuildingAppearance/"+set+kind);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                        Check(texture&&AssetDatabase.GetAssetPath(texture)==path&&Hash(path)==(string)record["sha256"]&&AssetDatabase.AssetPathToGUID(path)==(string)record["guid"],"exact Resources/hash/GUID binding "+set+kind);
                        Check(importer&&importer.textureType==(kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default)&&importer.sRGBTexture==(kind=="Colour")&&!importer.convertToNormalmap&&!importer.flipGreenChannel,"actual importer colourspace/type/+Y orientation "+set+kind);
                        Check(kind!="MetalSmoothness"||importer.alphaSource==TextureImporterAlphaSource.FromInput,"packed smoothness alpha retained by importer "+set+kind);
                        Check(!importer.isReadable&&importer.mipmapEnabled&&importer.streamingMipmaps&&importer.wrapModeU==TextureWrapMode.Repeat&&importer.wrapModeV==TextureWrapMode.Repeat&&importer.filterMode==FilterMode.Trilinear&&importer.anisoLevel>=4&&importer.maxTextureSize==1024,"bounded shared mip/repeat import "+set+kind);
                        Check(texture.width==1024&&texture.height==1024&&texture.mipmapCount>1&&texture.wrapModeU==TextureWrapMode.Repeat&&texture.wrapModeV==TextureWrapMode.Repeat,"actual imported dimensions/mips/repeat "+set+kind);
                        Check(new[]{TextureFormat.DXT1,TextureFormat.DXT5,TextureFormat.BC4,TextureFormat.BC5,TextureFormat.BC6H,TextureFormat.BC7}.Contains(texture.format),"actual imported GPU block compression "+set+kind+" "+texture.format);
                        results.Add("FORMAT "+set+kind+" "+texture.format+" "+texture.graphicsFormat);
                    }
                    string roughPath=Path.Combine("Data/BuildingAppearance",(string)source["sourceMaps"].Single(x=>(string)x["kind"]=="Roughness")["path"]);
                    var rough=Source(roughPath);var packed=Readback(Resources.Load<Texture2D>("BuildingAppearance/"+set+"MetalSmoothness"),readback,false);double alphaError=0;float maxError=0,maxMetal=0;int samples=0;
                    for(int y=5;y<1024;y+=11)for(int x=5;x<1024;x+=11){int i=y*1024+x;float error=Mathf.Abs(packed[i].a/255f-(1-rough[i].r/255f));alphaError+=error;maxError=Mathf.Max(maxError,error);maxMetal=Mathf.Max(maxMetal,Mathf.Max(packed[i].r,Mathf.Max(packed[i].g,packed[i].b))/255f);samples++;}
                    Check(maxMetal<.025f&&alphaError/samples<.025&&maxError<.16f,"actual compressed dielectric RGB and alpha1-roughness "+set+" mean="+(alphaError/samples).ToString("F5")+" max="+maxError.ToString("F5"));
                    string normalPath=Path.Combine("Data/BuildingAppearance",(string)source["sourceMaps"].Single(x=>(string)x["kind"]=="Normal")["path"]);
                    var expected=Source(normalPath);var decoded=Readback(Resources.Load<Texture2D>("BuildingAppearance/"+set+"Normal"),readback,true);double dots=0,yError=0,flippedError=0;float minZ=1;int count=0;
                    for(int y=5;y<1024;y+=11)for(int x=5;x<1024;x+=11){int i=y*1024+x;var a=Normal(expected[i]);var b=Normal(decoded[i]);if(!Finite(b.x)||!Finite(b.y)||!Finite(b.z))throw new Exception("Nonfinite normal");dots+=Vector3.Dot(a,b);yError+=Math.Abs(a.y-b.y);flippedError+=Math.Abs(a.y+b.y);minZ=Mathf.Min(minZ,b.z);count++;}
                    Check(dots/count>.96&&yError/count<.08&&yError<flippedError*.8&&minZ>-.025f,"actual GPU normals retain GL+Y and finite upper hemisphere "+set+" dot="+(dots/count).ToString("F5")+" y="+(yError/count).ToString("F5")+" invertedY="+(flippedError/count).ToString("F5"));
                }
                var walls=new Material[6];for(int i=0;i<200&&walls.Any(m=>!m);i++){var b=new GeoBuilding{id="asset-check-"+i,kind="building"};int index=BuildingAppearanceMaterials.WallIndex(b);walls[index]=BuildingAppearanceMaterials.Wall(b);Check(Mathf.Abs(BuildingAppearanceMaterials.WallRepeat(b)-(index<2?1.4f:index==5?2.57f:1))<.001,"runtime wall repeat matches provider scale "+index);Check(ReferenceEquals(walls[index],BuildingAppearanceMaterials.Wall(b)),"wall palette material shared "+index);}
                Check(walls.All(m=>m)&&walls.Distinct().Count()==6,"all six stable fictional wall palette choices reachable");for(int i=0;i<6;i++)Surface(walls[i],i<2?"Brick":i==5?"Stone":"Render");Surface(BuildingAppearanceMaterials.Roof,"Slate");Check(ReferenceEquals(BuildingAppearanceMaterials.Roof,BuildingAppearanceMaterials.Roof),"slate roof material shared");
                var important=new GeoBuilding{id="important",kind="important_building"};Check(BuildingAppearanceMaterials.WallIndex(important)==3&&ReferenceEquals(BuildingAppearanceMaterials.Wall(important),walls[3]),"important-building style uses existing shared neutral palette");
                var frame=BuildingAppearanceMaterials.Frame;var glass=BuildingAppearanceMaterials.Glass;Check(frame&&glass&&frame!=glass&&frame.color.grayscale>glass.color.grayscale&&glass.GetFloat("_Smoothness")>.75f&&frame.GetFloat("_Smoothness")<glass.GetFloat("_Smoothness"),"separate available frame/glass colour and glossy opaque window response");
                for(int i=0;i<4;i++){var door=BuildingAppearanceMaterials.Door(i);Check(door&&ReferenceEquals(door,BuildingAppearanceMaterials.Door(i))&&door.color.grayscale<frame.color.grayscale,"shared original entrance palette "+i);}
                Check(preserved.All(kv=>Hash(kv.Key)==kv.Value),"all12 base assets and metadata unchanged; no readability reimports");
                File.WriteAllLines(output,new[]{"PASS "+checks+" building appearance asset/material/GPU checks"}.Concat(results));EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllLines(output,new[]{"FAIL "+e}.Concat(results));EditorApplication.Exit(1);}
            finally{if(readback)UnityEngine.Object.DestroyImmediate(readback);}
        }
    }
}
