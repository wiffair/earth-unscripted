using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace EarthUnscripted.Editor
{
    public static class BroadleafRenderingSetup
    {
        public static void Configure()
        {
            var shader=Shader.Find("EarthUnscripted/Broadleaf");if(!shader)throw new InvalidOperationException("Woodland shader unavailable");
            foreach(bool leaf in new[]{false,true})
            {
                string path="Assets/EarthUnscripted/Resources/Vegetation/Broadleaf"+(leaf?"Leaves":"Bark")+".mat";
                var texture=Resources.Load<Texture2D>("Vegetation/"+(leaf?"LeafAtlas":"BarkAtlas"));if(!texture)throw new InvalidOperationException("Woodland atlas unavailable");
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
                material.shader=shader;material.name=leaf?"Scanned oak leaves":"Original broadleaf bark";material.enableInstancing=true;material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Cutoff",leaf?.4f:0);material.SetFloat("_IsLeaf",leaf?1:0);material.SetFloat("_WindStrength",.25f);material.SetFloat("_Wetness",0);material.SetFloat("_Cull",leaf?0:2);material.SetFloat("_NormalStrength",.45f);
                if(leaf){var normal=Resources.Load<Texture2D>("Vegetation/LeafNormal");var roughness=Resources.Load<Texture2D>("Vegetation/LeafRoughness");if(!normal||!roughness)throw new InvalidOperationException("Leaf surface maps unavailable");material.SetTexture("_NormalMap",normal);material.SetTexture("_RoughnessMap",roughness);}
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }
        public static void Run()
        {
            try
            {
                Configure();DetailedWoodlandChunk.EnsureAssets();var shader=Shader.Find("EarthUnscripted/Broadleaf");
                if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Woodland shader has compilation errors");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/broadleaf-rendering-setup.txt","PASS shared instanced woodland materials, atlases and shader error check\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
