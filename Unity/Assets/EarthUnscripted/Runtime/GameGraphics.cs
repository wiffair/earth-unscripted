using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EarthUnscripted
{
    // The game owns one simulation and one set of source assets. Only material
    // construction and lighting depend on the selected build's render pipeline.
    public static class GameGraphics
    {
        public static bool HighDefinition => GraphicsSettings.defaultRenderPipeline is HDRenderPipelineAsset;
        public static string Name => HighDefinition ? "HD" : "Standard";
        static Shader lit;
        public static Material Create(Shader source)
        {
            if(!source)throw new InvalidOperationException("A source surface shader is required");
            var material=new Material(source);
            Adapt(material);return material;
        }
        public static Material Create(Material source)
        {
            if(!source)throw new ArgumentNullException(nameof(source));
            var material=new Material(source);
            Adapt(material);return material;
        }
        public static Material Create(Shader source,Action<Material> configure)
        {var material=new Material(source);configure(material);Adapt(material);return material;}
        public static Material Create(Material source,Action<Material> configure)
        {var material=new Material(source);configure(material);Adapt(material);return material;}
        public static void SetShader(Material material,Shader shader)
        { material.shader=shader;Adapt(material); }
        public static void Adapt(Material material)
        {
            if(!HighDefinition||!material||!material.shader)return;
            string name=material.shader.name;
            if(name.StartsWith("EarthUnscriptedHD/")||name.StartsWith("HDRP/"))return;
            if(name.Contains("Sky")||name.Contains("Driving Map"))return;
            if(name.Contains("SoftWeatherParticle")||name.Contains("CamperSmoke"))
            {
                var particle=Resources.Load<Shader>("Graphics/HDParticle");
                if(!particle)throw new InvalidOperationException("HD particle shader missing");
                material.shader=particle;return;
            }
            // Photographed distant trees are unlit in both renderers. Keep their
            // instanced fade/view shader separate from HDRP's Lit depth passes.
            if(name.Contains("Automatic Tree Views"))
            {
                material.shader=Resources.Load<Shader>("Graphics/HDTreeBillboard");
                material.enableInstancing=true;return;
            }
            bool known=name.StartsWith("EarthUnscripted/")||name.StartsWith("Universal Render Pipeline/")||name=="Standard";
            if(!known)return;
            var colour=material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):
                material.HasProperty("_Color")?material.GetColor("_Color"):Color.white;
            Texture texture=material.HasProperty("_BaseMap")?material.GetTexture("_BaseMap"):
                material.HasProperty("_MainTex")?material.GetTexture("_MainTex"):null;
            string textureKey=material.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
            var scale=texture?material.GetTextureScale(textureKey):Vector2.one;
            var offset=texture?material.GetTextureOffset(textureKey):Vector2.zero;
            bool transparent=material.HasProperty("_Surface")&&material.GetFloat("_Surface")>.5f;
            float sourceCutoff=material.HasProperty("_Cutoff")?material.GetFloat("_Cutoff"):.35f;
            float mode=name.Contains("Surface Tracks")?8:name.Contains("Photographic Surface")?1:name.Contains("GeoTerrain")?2:name.Contains("VehiclePaint")?3:
                name.Contains("Foliage")||name.Contains("Broadleaf")?4:name.Contains("Automatic Tree Views")?5:
                name.Contains("CoastalSurface")?6:name.Contains("SignLegend")||name.Contains("WorldText")?7:0;
            bool text=mode==7;
            bool cutout=mode==4||mode==5||text;
            if(!lit)lit=Resources.Load<Shader>("Graphics/HDSharedLit");
            if(!lit)throw new InvalidOperationException("Shared HD surface shader missing");
            material.shader=lit;
            material.SetColor("_BaseColor",colour);
            if(texture)material.SetTexture("_BaseMap",texture);
            material.SetTextureScale("_BaseMap",scale);material.SetTextureOffset("_BaseMap",offset);
            material.SetFloat("_EarthUnscriptedMode",mode);
            material.SetFloat("_EarthUnscriptedWorldText",name.Contains("WorldText")?1:0);
            material.SetFloat("_SurfaceType",transparent?1:0);
            material.SetFloat("_AlphaCutoffEnable",cutout?1:0);
            material.SetFloat("_AlphaCutoff",0); // Custom alpha uses the source mask/cutoff.
            material.SetFloat("_DoubleSidedEnable",mode==4||mode==5||name.Contains("WorldText")||mode==6?1:0);
            if(name.Contains("Broadleaf"))material.SetFloat("_EarthUnscriptedBroadleaf",1);
            material.enableInstancing=true;
            Validate(material);
            material.SetFloat("_Cutoff",sourceCutoff);
        }
        // Run Unity's public validator against its matching Lit property layout,
        // then retain those pass/keyword/stencil settings on our Lit extension.
        // The extension derives from the same package-pinned Lit.shader source.
        public static void Validate(Material material)
        {
            var custom=material.shader;
            material.shader=Shader.Find("HDRP/Lit");
            HDMaterial.ValidateMaterial(material);
            var keywords=material.shaderKeywords;int queue=material.renderQueue;
            // Shader assignment resets the queue. HDRP's alpha-test GBuffer uses
            // Equal depth and requires its cutout prepass queue to be retained.
            material.shader=custom;material.shaderKeywords=keywords;material.renderQueue=queue;
        }
    }
}
