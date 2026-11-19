using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthUnscripted.Editor
{
    public static class ShaderFallbackVerification
    {
        static int checks;
        static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        static void Colour(Color actual,Color expected,string message)=>Check(Vector4.Distance(actual,expected)<.0001f,message);
        static void Surface(Material material,Shader original,int mode,bool hd,Texture texture,Color colour,Vector2 scale,Vector2 offset,string label)
        {
            Check(material.shader.name==(hd?"EarthUnscriptedHD/Shared Lit":original.name),label+" final shader preserves pipeline choice");
            if(hd)Check(material.GetFloat("_EarthUnscriptedMode")==mode,label+" retains adaptation mode");
            Check(material.GetTexture("_BaseMap")==texture,label+" retains configured texture");
            Check(material.GetTextureScale("_BaseMap")==scale&&material.GetTextureOffset("_BaseMap")==offset,label+" retains texture transform");
            if(material.HasProperty("_BaseColor"))Colour(material.GetColor("_BaseColor"),colour,label+" retains colour");
        }
        public static void Run()
        {
            checks=0;
            var previous=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;
            var owned=new List<UnityEngine.Object>();
            try
            {
                var standard=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/EarthUnscripted/Settings/EarthUnscriptedPipeline.asset");
                var hd=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/EarthUnscripted/Graphics/HDPipeline.asset");
                Check(standard&&hd&&Shader.Find("HDRP/Lit"),"Both pipeline assets and fallback shader exist");
                var texture=new Texture2D(2,2);owned.Add(texture);
                foreach(var entry in new[]{("GeoTerrain.shader","EarthUnscripted/GeoTerrain",2), ("AutomaticSurface.shader","EarthUnscripted/Automatic Photographic Surface",1)})
                {
                    string file="Assets/EarthUnscripted/Shaders/"+entry.Item1;
                    string source=File.ReadAllText(file);int fallback=source.LastIndexOf("Fallback \"HDRP/Lit\"",StringComparison.Ordinal);
                    Check(fallback>source.LastIndexOf("UsePass",StringComparison.Ordinal)&&source.Substring(fallback).Trim()=="Fallback \"HDRP/Lit\"\n}",entry.Item1+" fallback belongs to Shader boundary after original subshader");
                    Check(source.Contains("\"RenderPipeline\"=\"UniversalPipeline\""),entry.Item1+" retains original UniversalPipeline subshader");
                    var shader=AssetDatabase.LoadAssetAtPath<Shader>(file);Check(shader&&shader.name==entry.Item2,entry.Item1+" retains original source name");
                    string[] properties=Enumerable.Range(0,shader.GetPropertyCount()).Select(shader.GetPropertyName).ToArray();
                    Check(properties.Contains("_BaseMap")&&!properties.Contains("_EarthUnscriptedMode"),entry.Item1+" retains original property layout rather than replacing shader");
                    if(entry.Item3==2)Check(properties.SequenceEqual(new[]{"_CircuitTurf","_BaseMap","_Grass","_Rock","_GrassNormal","_RockNormal"}),"Terrain source properties unchanged");
                    else Check(properties.Contains("_BaseColor")&&properties.Contains("_NormalStrength")&&properties.Contains("_CoastEnabled")&&properties.Length==12,"Photographic source properties unchanged");
                    foreach(bool high in new[]{false,true})
                    {
                        GraphicsSettings.defaultRenderPipeline=high?hd:standard;QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
                        var raw=new Material(shader);owned.Add(raw);var defaults=GameGraphics.Create(shader);owned.Add(defaults);
                        Check(defaults.shader.name==(high?"EarthUnscriptedHD/Shared Lit":shader.name),"Default material retains pipeline shader");
                        if(high){Check(defaults.GetFloat("_EarthUnscriptedMode")==entry.Item3,"Default material retains mode");Colour(defaults.GetColor("_BaseColor"),Color.white,"Original source default colour adapts to white");}
                        Check(defaults.GetTexture("_BaseMap")==raw.GetTexture("_BaseMap"),"Original source default texture preserved");
                        bool configuredOriginal=false;var colour=new Color(.25f,.5f,.75f,1);var scale=new Vector2(2,3);var offset=new Vector2(.2f,.4f);
                        var configured=GameGraphics.Create(shader,m=>{configuredOriginal=m.shader==shader&&m.HasProperty("_BaseMap");m.SetTexture("_BaseMap",texture);m.SetTextureScale("_BaseMap",scale);m.SetTextureOffset("_BaseMap",offset);if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",colour);});owned.Add(configured);
                        Check(configuredOriginal,"Configure callback sees original shader and properties before adaptation");
                        Surface(configured,shader,entry.Item3,high,texture,entry.Item3==2?Color.white:colour,scale,offset,"Configured material");
                        var assigned=new Material(shader);owned.Add(assigned);assigned.SetTexture("_BaseMap",texture);assigned.SetTextureScale("_BaseMap",scale);assigned.SetTextureOffset("_BaseMap",offset);if(assigned.HasProperty("_BaseColor"))assigned.SetColor("_BaseColor",colour);
                        GameGraphics.SetShader(assigned,shader);Surface(assigned,shader,entry.Item3,high,texture,entry.Item3==2?Color.white:colour,scale,offset,"Assigned material");
                    }
                }
                Debug.Log("SHADER FALLBACK PASS "+checks+" original properties/configure order/Standard and HD mode preservation; native support is checked in real players");
            }
            finally{GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=previousQuality;foreach(var item in owned)if(item)UnityEngine.Object.DestroyImmediate(item);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
