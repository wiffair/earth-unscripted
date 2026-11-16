using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadRush.Editor
{
    public static class SharedGraphicsPolicyVerification
    {
        public static void Run()
        {
            int checks=0;
            void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
            foreach(int rate in new[]{24000,44100,48000,96000})
            {
                float coefficient=AudioParameterSmoother.Coefficient(rate,4),gain=0;
                Check(coefficient>0&&coefficient<.02f,"Audio gain cannot jump at a frame boundary");
                for(int sample=0;sample<rate/25;sample++)gain=AudioParameterSmoother.Step(gain,1,coefficient);
                Check(gain>.999f&&gain<=1,"Audio gain settles quickly without overshoot");
                for(int sample=0;sample<rate/25;sample++)gain=AudioParameterSmoother.Step(gain,0,coefficient);
                Check(gain<.001f,"Pause gain becomes silent without a discontinuity");
            }
            Check(NewbridgePrototype.NewbridgeWorld.BuildingSharesRoadHeight(10,10,0,20),"Building wall at tunnel driving height must be removed");
            Check(!NewbridgePrototype.NewbridgeWorld.BuildingSharesRoadHeight(10,10,16,25),"Building safely above tunnel bore remains intact");
            var previous=GraphicsSettings.defaultRenderPipeline;
            try
            {
                GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/RoadRush/Graphics/HDPipeline.asset");
                var source=Shader.Find("Universal Render Pipeline/Lit");var material=GameGraphics.Create(source);
                try
                {
                    Check(material.shader.name=="RoadRushHD/Shared Lit","HD creates a native HDRP Lit extension");
                    var map=Texture2D.whiteTexture;material.mainTexture=map;
                    Check(material.GetTexture("_BaseMap")==map,"Shared main-texture updates reach HD source map");
                    material.color=Color.red;
                    Check(material.GetColor("_BaseColor")==Color.red,"Shared car/scenery colour updates reach HD");
                    Check(material.enableInstancing,"HD retains instanced scene drawing");
                }
                finally{UnityEngine.Object.DestroyImmediate(material);}
            }
            finally{GraphicsSettings.defaultRenderPipeline=previous;}
            Debug.Log("SHARED_GRAPHICS_POLICY_PASS "+checks);
        }
    }
}
